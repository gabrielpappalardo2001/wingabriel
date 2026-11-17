' ============================================================================================
' WGStrumenti - gli strumenti musicali di WinGabriel
' ============================================================================================
'
' Dal menu iniziale: "Strumenti musicali" -> categoria -> strumento (oppure "I miei progetti"). Si apre l'ambiente dello
' strumento: una riga con la traccia corrente e le opzioni con il tasto Alt. Le note si suonano con i tasti.
'
'   Le due file di lettere sono due tastiere di pianoforte, una sopra l'altra:
'     fila bassa:  Z X C V B N M , . -     note bianche (Do Re Mi Fa Sol La Si Do Re Mi)
'                  S D   G H J   L ò       note nere
'     fila alta:   Q W E R T Y U I O P è + note bianche, un'ottava sopra
'                  2 3   5 6 7   9 0   ì   note nere
'   Finché un tasto è premuto la nota segue il proprio decadimento; sollevato, la nota si stacca subito. Non c'è il pedale.
'   Pagina su e Pagina giù: ottava su e giù. Maiusc: nota più forte.
'
' Ogni registrazione (Invio avvia e ferma) è una traccia di un progetto: le note suonate e lo strumento. Mentre si registra una
' traccia nuova si sentono le precedenti. Inserisci aggiunge una traccia scegliendo lo strumento. Freccia su e freccia giù
' passano da una traccia all'altra (l'ultima riga è la traccia nuova da registrare), con Maiusc si selezionano più tracce, Canc
' le elimina; la barra spaziatrice ascolta tutte le tracce. Uscendo, il progetto si salva con il nome scelto dall'utente e si
' riapre da "I miei progetti"; da Alt, Esporta, si scrive un file in una cartella a scelta (MusicXML, MIDI, mp3 o wav).
'
' Le note non vengono lette, se non lo si chiede dalle opzioni. Le note non passano dalla coda degli eventi di WinGabriel:
' il gancio della tastiera le manda direttamente al motore audio (vedi WGTastiera.ImpostaGestoreTastiMusicali).
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms


''' <summary>Le impostazioni degli strumenti musicali e del registratore, nel file di configurazione di WinGabriel.</summary>
Public NotInheritable Class ImpostazioniMusica

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Public Shared Function Leggi(id As String, predefinito As String) As String
        Try
            Dim cfg As New WGConfig()
            If Not cfg.EsisteId(id) Then Return predefinito
            Dim valore As String = cfg.LeggiValore(id)
            If String.IsNullOrWhiteSpace(valore) Then Return predefinito
            Return valore
        Catch ex As Exception
            log.ScriviLog("ImpostazioniMusica.Leggi (" & id & "): " & ex.Message)
            Return predefinito
        End Try
    End Function

    Public Shared Sub Scrivi(id As String, valore As String)
        Try
            Dim cfg As New WGConfig()
            cfg.SalvaValore(id, valore)
        Catch ex As Exception
            log.ScriviLog("ImpostazioniMusica.Scrivi (" & id & "): " & ex.Message)
        End Try
    End Sub

    Public Shared Function LeggiIntero(id As String, predefinito As Integer) As Integer
        Dim valore As Integer
        If Integer.TryParse(Leggi(id, predefinito.ToString()), valore) Then Return valore
        Return predefinito
    End Function

    Public Shared Function LeggiBooleano(id As String, predefinito As Boolean) As Boolean
        Return String.Equals(Leggi(id, If(predefinito, "true", "false")), "true", StringComparison.OrdinalIgnoreCase)
    End Function

    Public Shared Sub ScriviBooleano(id As String, valore As Boolean)
        Scrivi(id, If(valore, "true", "false"))
    End Sub

End Class

''' <summary>Quali tasti sono note. I tasti si riconoscono dalla posizione sulla tastiera, così funziona con ogni lingua.</summary>
Public NotInheritable Class MappaTastiera

    Private Shared ReadOnly _semitoni As New Dictionary(Of Integer, Integer)()

    Shared Sub New()
        ' fila bassa (codici di scansione Windows)
        Dim z As Integer() = {&H2C, &H2D, &H2E, &H2F, &H30, &H31, &H32, &H33, &H34, &H35}
        Dim zNote As Integer() = {0, 2, 4, 5, 7, 9, 11, 12, 14, 16}
        For i As Integer = 0 To z.Length - 1
            _semitoni(z(i)) = zNote(i)
        Next
        Dim s As Integer() = {&H1F, &H20, &H22, &H23, &H24, &H26, &H27}
        Dim sNote As Integer() = {1, 3, 6, 8, 10, 13, 15}
        For i As Integer = 0 To s.Length - 1
            _semitoni(s(i)) = sNote(i)
        Next
        ' fila alta
        Dim q As Integer() = {&H10, &H11, &H12, &H13, &H14, &H15, &H16, &H17, &H18, &H19, &H1A, &H1B}
        Dim qNote As Integer() = {12, 14, 16, 17, 19, 21, 23, 24, 26, 28, 29, 31}
        For i As Integer = 0 To q.Length - 1
            _semitoni(q(i)) = qNote(i)
        Next
        Dim n As Integer() = {&H3, &H4, &H6, &H7, &H8, &HA, &HB, &HD}
        Dim nNote As Integer() = {13, 15, 18, 20, 22, 25, 27, 30}
        For i As Integer = 0 To n.Length - 1
            _semitoni(n(i)) = nNote(i)
        Next
    End Sub

    Private Sub New()
    End Sub

    ''' <summary>Se il tasto (virtuale e di scansione) è un tasto di nota: quanti semitoni sopra il Do dell'ottava scelta; altrimenti -1.</summary>
    Public Shared Function Semitono(vkCode As Integer, scanCode As Integer) As Integer
        Dim valido As Boolean = (vkCode >= &H30 AndAlso vkCode <= &H39) OrElse (vkCode >= &H41 AndAlso vkCode <= &H5A) OrElse
                                (vkCode >= &HBA AndAlso vkCode <= &HC0) OrElse (vkCode >= &HDB AndAlso vkCode <= &HDF)
        If Not valido Then Return -1
        Dim valore As Integer
        If _semitoni.TryGetValue(scanCode, valore) Then Return valore
        Return -1
    End Function

    Private Shared ReadOnly NomiNote() As String = {"Do", "Do diesis", "Re", "Re diesis", "Mi", "Fa", "Fa diesis", "Sol", "Sol diesis", "La", "La diesis", "Si"}

    Public Shared Function NomeNota(notaMidi As Integer) As String
        Dim ottava As Integer = notaMidi \ 12 - 1
        Return NomiNote(((notaMidi Mod 12) + 12) Mod 12) & " " & ottava.ToString()
    End Function

End Class


''' <summary>Funzioni comuni agli ambienti musicali: parlare, mostrare messaggi, passare al thread dell'interfaccia, salvare la registrazione.</summary>
Public NotInheritable Class AiutoMusica

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>
    ''' Il segnale di inizio registrazione: un beep breve, senza parole, per non distrarre chi suona. Si sente prima che la registrazione
    ''' parta (il microfono non lo registra).
    ''' </summary>
    Public Shared Sub SegnaleInizio()
        Try
            Console.Beep(1000, 120)
        Catch
            Try
                System.Media.SystemSounds.Beep.Play()
                Thread.Sleep(150)
            Catch
            End Try
        End Try
    End Sub

    Public Shared Sub Annuncia(testo As String)
        Try
            Dim voce As sintesivocale = sintesivocale.GetInstance()
            If voce IsNot Nothing Then voce.LeggiTesto(testo, False, "Alta", False)
        Catch ex As Exception
            log.ScriviLog("AiutoMusica.Annuncia: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub AnnunciaInSottofondo(testo As String)
        Task.Run(Sub() Annuncia(testo))
    End Sub

    Public Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("AiutoMusica.Messaggio: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub SuInterfaccia(azione As Action)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.InvokeRequired Then
                c.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As Exception
            log.ScriviLog("AiutoMusica.SuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Public Shared Function NomeFileSicuro(testo As String) As String
        Dim sb As New StringBuilder()
        For Each ch As Char In testo
            If Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 Then
                sb.Append(" "c)
            Else
                sb.Append(ch)
            End If
        Next
        Return sb.ToString().Trim()
    End Function

    ''' <summary>Percorso libero per una registrazione: nome, data e ora, estensione.</summary>
    Public Shared Function PercorsoNuovo(cartella As String, nome As String, estensione As String) As String
        Dim baseNome As String = NomeFileSicuro(nome) & " " & DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss")
        Dim percorso As String = Path.Combine(cartella, baseNome & estensione)
        Dim n As Integer = 2
        While File.Exists(percorso)
            percorso = Path.Combine(cartella, baseNome & " (" & n.ToString() & ")" & estensione)
            n += 1
        End While
        Return percorso
    End Function

    ''' <summary>
    ''' Prepara il file temporaneo (preparazione restituisce "" se è andata bene, altrimenti il motivo), lo converte in mp3 nella
    ''' cartella scelta e avvisa. Durante il lavoro mostra l'attesa. Restituisce subito: il lavoro prosegue in sottofondo.
    ''' </summary>
    Public Shared Sub ElaboraESalva(preparazione As Func(Of String), percorsoTemporaneo As String, percorsoMp3 As String)
        Dim attesa As New WGProcessoInCorso()
        attesa.Avvia("Sto salvando la registrazione, attendere", False)
        Task.Run(Sub()
                     Dim esito As String = ""
                     Try
                         Dim problema As String = preparazione()
                         If problema <> "" OrElse Not File.Exists(percorsoTemporaneo) Then
                             esito = If(problema <> "", problema, "Non è stato possibile scrivere la registrazione.")
                         Else
                             Dim messaggioErrore As String = ""
                             If WGEsportazioneAudio.ConvertiInMp3(percorsoTemporaneo, percorsoMp3, messaggioErrore) Then
                                 esito = "Registrazione salvata in " & percorsoMp3 & "."
                             Else
                                 Dim alternativo As String = Path.ChangeExtension(percorsoMp3, ".wav")
                                 If WGEsportazioneAudio.ConvertiInWav16(percorsoTemporaneo, alternativo) Then
                                     esito = messaggioErrore & " La registrazione è stata salvata come file wav in " & alternativo & "."
                                 Else
                                     esito = messaggioErrore & " Non è stato possibile salvare la registrazione."
                                 End If
                             End If
                         End If
                     Catch ex As Exception
                         log.ScriviLog("AiutoMusica.ElaboraESalva: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         esito = "Non è stato possibile salvare la registrazione."
                     Finally
                         Try
                             If File.Exists(percorsoTemporaneo) Then File.Delete(percorsoTemporaneo)
                         Catch
                         End Try
                     End Try
                     SuInterfaccia(Sub()
                                       Try
                                           attesa.Fine()
                                       Catch
                                       End Try
                                       Messaggio(esito)
                                   End Sub)
                 End Sub)
    End Sub

    ''' <summary>Chiude il file del motore musicale e lo salva come mp3.</summary>
    Public Shared Sub SalvaRegistrazione(registratore As RegistratoreFlusso, percorsoTemporaneo As String, percorsoMp3 As String)
        ElaboraESalva(Function() As String
                          Thread.Sleep(150)
                          Dim scrittoBene As Boolean = registratore.Chiudi()
                          If Not scrittoBene Then Return "Non è stato possibile scrivere la registrazione."
                          If registratore.Fotogrammi < 4800 Then Return "La registrazione era troppo breve."
                          Return ""
                      End Function, percorsoTemporaneo, percorsoMp3)
    End Sub

End Class

''' <summary>L'ambiente di uno strumento: la riga della traccia, le opzioni con Alt e i tasti che suonano. Le registrazioni sono tracce di un progetto.</summary>
Public NotInheritable Class SessioneStrumento
    Implements IGestoreTastiMusicali, IDisposable

    Private Const VkPaginaSu As Integer = &H21
    Private Const VkPaginaGiu As Integer = &H22
    Private Const VkSpazio As Integer = &H20
    Private Const VkSu As Integer = &H26
    Private Const VkGiu As Integer = &H28
    Private Const VkInserisci As Integer = &H2D
    Private Const VkCanc As Integer = &H2E
    ''' <summary>I tasti suonati dal vivo e le note delle tracce già registrate stanno nello stesso motore: non devono avere le stesse chiavi.</summary>
    Private Const OffsetChiavi As Integer = 1000000000
    Private Const IdControllo As String = "controllo"

    Private Shared ReadOnly Latenze() As Integer = {0, 60, 100, 150}

    ''' <summary>Un tasto che sta suonando mentre si registra: la nota si completa quando il tasto si lascia.</summary>
    Private NotInheritable Class NotaAperta
        Public Inizio As Double
        Public Nota As Integer
        Public Velocita As Double
    End Class

    Private ReadOnly _log As New Logger()
    Private _categoria As CategoriaStrumenti
    Private _strumento As DefinizioneStrumento
    Private ReadOnly _progetto As ProgettoMusicale
    Private ReadOnly _percorsoProgettoAperto As String
    Private ReadOnly _selezione As New HashSet(Of TracciaProgetto)()
    Private ReadOnly _bloccoTracce As New Object()
    ' la riga corrente: una traccia registrata oppure (l'ultima) la traccia nuova da registrare
    Private _indiceRiga As Integer
    Private _modificato As Boolean
    Private _ascolto As Boolean
    Private _spazioAscolto As Boolean
    Private _cancPremuto As Boolean
    Private _inserisciPremuto As Boolean
    Private _riproduttore As RiproduttoreSpartito
    ' il volume dello strumento della traccia nuova, in percento (100 è quello normale); AltGr con freccia su e giù lo cambia
    Private _volumeNuova As Integer = 100
    Private _metronomoLibero As Boolean
    Private _metronomoInRegistrazione As Boolean
    Private _tempoMetronomo As Integer = 100
    Private _battitiMetronomo As Integer = 4
    Private _salvaAllUscita As Boolean = True

    Private _registrando As Boolean
    Private _cronometro As Stopwatch
    Private _nuovaTraccia As TracciaProgetto
    Private ReadOnly _noteAperte As New Dictionary(Of Integer, NotaAperta)()
    Private ReadOnly _bloccoRegistrazione As New Object()

    Private _menuStrumenti As MenuStrumentiMusicali
    Private _motore As MotoreSintesi
    Private _lista As WGMidiRiproduzione
    Private _ottava As Integer
    Private ReadOnly _tastiPremuti As New HashSet(Of Integer)()
    Private ReadOnly _bloccoTasti As New Object()
    Private _nomeSottofondo As String = ""
    Private _timerFuoco As System.Threading.Timer
    Private _chiusa As Boolean
    Private _leggiNote As Boolean
    Private _indiceLatenza As Integer
    Private _volumeStrumento As Integer
    Private _volumeSottofondo As Integer
    Private _scalaRiverbero As Integer
    Private _ripetiSottofondo As Boolean
    Private _dialogoSottofondo As WGFileDialog
    Private _selettoreCartella As SelettoreCartellaMusica

    ''' <summary>Un progetto nuovo, che comincia con questo strumento.</summary>
    Public Sub New(categoria As CategoriaStrumenti, strumento As DefinizioneStrumento)
        _categoria = categoria
        _strumento = strumento
        _ottava = strumento.OttavaIniziale
        _progetto = New ProgettoMusicale()
        _percorsoProgettoAperto = ""
    End Sub

    ''' <summary>Un progetto già salvato che si riapre: lo strumento è quello della traccia nuova.</summary>
    Public Sub New(strumento As DefinizioneStrumento, progetto As ProgettoMusicale, percorsoProgetto As String)
        _strumento = strumento
        _ottava = strumento.OttavaIniziale
        _progetto = progetto
        _percorsoProgettoAperto = percorsoProgetto
        _indiceRiga = progetto.Tracce.Count
    End Sub

    Public Sub Avvia()
        Try
            _leggiNote = ImpostazioniMusica.LeggiBooleano("musica_leggi_note", False)
            _indiceLatenza = Math.Max(0, Math.Min(Latenze.Length - 1, ImpostazioniMusica.LeggiIntero("musica_latenza_v4", 0)))
            _volumeStrumento = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("musica_volume_strumento", 80)))
            _volumeSottofondo = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("musica_volume_sottofondo", 70)))
            _scalaRiverbero = Math.Max(0, Math.Min(150, ImpostazioniMusica.LeggiIntero("musica_riverbero", 100)))
            _ripetiSottofondo = ImpostazioniMusica.LeggiBooleano("musica_ripeti_sottofondo", False)
            _tempoMetronomo = Math.Max(30, Math.Min(300, ImpostazioniMusica.LeggiIntero("musica_metronomo_bpm", 100)))
            _battitiMetronomo = Math.Max(1, Math.Min(12, ImpostazioniMusica.LeggiIntero("musica_metronomo_battiti", 4)))
            _metronomoInRegistrazione = ImpostazioniMusica.LeggiBooleano("musica_metronomo_registrazione", False)
            If _progetto.Tracce.Count > 0 Then
                ' un progetto già avviato: le tracce nuove seguono il tempo delle altre
                _tempoMetronomo = Math.Max(30, Math.Min(300, _progetto.Tempo))
                _battitiMetronomo = Math.Max(1, Math.Min(12, _progetto.Battiti))
            End If

            _motore = New MotoreSintesi()
            _motore.VolumeStrumento = _volumeStrumento / 100.0F
            _motore.VolumeSottofondo = _volumeSottofondo / 100.0F
            _motore.ScalaRiverbero = _scalaRiverbero / 100.0F
            _motore.SottofondoRipetuto = _ripetiSottofondo
            ApplicaMetronomo()
            AddHandler _motore.SottofondoFinito, AddressOf OnSottofondoFinito
            _motore.ImpostaStrumento(_strumento)
            Try
                _motore.Avvia(Latenze(_indiceLatenza))
            Catch exAudio As Exception
                _log.ScriviLog("SessioneStrumento.Avvia (audio): " & exAudio.Message & Environment.NewLine & exAudio.StackTrace)
                _motore.Dispose()
                _motore = Nothing
                AiutoMusica.Messaggio("Non è stato possibile aprire la scheda audio per suonare.")
                Return
            End Try

            RiapriSottofondoDelProgetto()
            AggiornaRiproduttore()

            _lista = New WGMidiRiproduzione()
            _lista.multiselezioneConsentita = False
            _lista.impostaTitolo("Strumenti musicali")
            RiempiLista()
            AddHandler _lista.escPremuto, AddressOf OnEsc
            AddHandler _lista.ElementoSelezionato, AddressOf OnVoceSelezionata
            AddHandler _lista.AltGrPremuto, AddressOf OnAltGr
            _lista.AggiornaOpzioni = AddressOf DefinisciOpzioni
            _lista.avvia()

            WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Me)
            _timerFuoco = New System.Threading.Timer(AddressOf ControllaFuoco, Nothing, 300, 300)
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Chiudi()
        End Try
    End Sub

    ''' <summary>Se il progetto aperto aveva un sottofondo e il file c'è ancora, lo si ricarica.</summary>
    Private Sub RiapriSottofondoDelProgetto()
        Try
            Dim percorso As String = _progetto.Sottofondo
            If percorso = "" OrElse Not File.Exists(percorso) OrElse _motore Is Nothing Then Return
            Dim caricato As New SottofondoAudio(percorso)
            caricato.Ripeti = _ripetiSottofondo
            _motore.CaricaSottofondo(caricato)
            _nomeSottofondo = Path.GetFileNameWithoutExtension(percorso)
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.RiapriSottofondoDelProgetto: " & ex.Message)
        End Try
    End Sub

    ' ---- la riga -----------------------------------------------------------------------------------------
    ' Il controllo mostra la riga corrente: una traccia già registrata (con il suo strumento e la durata) oppure la traccia nuova.
    ' Freccia su e freccia giù passano da una riga all'altra; la riga si annuncia a voce.

    Private Function NumeroTracce() As Integer
        SyncLock _bloccoTracce
            Return _progetto.Tracce.Count
        End SyncLock
    End Function

    Private Shared Function TestoVolume(volume As Integer) As String
        Return If(volume = 100, "", ", volume " & volume.ToString() & " per cento")
    End Function

    Private Function TestoControllo() As String
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaProgetto) = _progetto.Tracce
            If _indiceRiga >= tracce.Count Then
                Dim t As String = "Nuova traccia: " & _strumento.Nome & TestoVolume(_volumeNuova)
                If tracce.Count > 0 Then t &= ". " & If(tracce.Count = 1, "Una traccia registrata", tracce.Count.ToString() & " tracce registrate")
                Return t
            End If
            Dim traccia As TracciaProgetto = tracce(_indiceRiga)
            Dim secondi As Integer = CInt(Math.Round(traccia.Fine))
            Dim durata As String = If(secondi >= 60, (secondi \ 60).ToString() & " minuti e " & (secondi Mod 60).ToString() & " secondi", secondi.ToString() & If(secondi = 1, " secondo", " secondi"))
            Return "Traccia " & (_indiceRiga + 1).ToString() & " di " & tracce.Count.ToString() & ", " & traccia.NomeStrumento & ", " & durata & TestoVolume(traccia.Volume) & If(_selezione.Contains(traccia), ", selezionata", "")
        End SyncLock
    End Function

    Private Sub RiempiLista()
        If _lista Is Nothing Then Return
        _lista.pulisci()
        _lista.aggiungiElemento(TestoControllo(), IdControllo)
        _lista.posiziona(IdControllo)
    End Sub

    Private Sub AggiornaRiga()
        Dim lista As WGMidiRiproduzione = _lista
        If lista IsNot Nothing Then lista.aggiungiElemento(TestoControllo(), IdControllo)
    End Sub

    ' Le opzioni di Alt: solo quelle che servono adesso. Volumi, riverbero, latenza, ripetizione del sottofondo e lettura delle note
    ' sono nella Configurazione del menu Musica.
    Private Sub DefinisciOpzioni()
        _lista.pulisciOpzioni()
        Dim registrando As Boolean = _registrando
        Dim tracce As Integer = NumeroTracce()
        _lista.aggiungiOpzione(If(registrando, "Ferma la registrazione", "Registra"), AddressOf OpzioneRegistrazione)
        _lista.aggiungiOpzione("Stato", AddressOf OpzioneStato)
        If Not registrando Then
            _lista.aggiungiOpzione("Aggiungi una traccia, scegliendo lo strumento", AddressOf OpzioneAggiungiTraccia)
            If tracce > 0 Then
                _lista.aggiungiOpzione(If(_ascolto, "Ferma l'ascolto delle tracce", "Ascolta le tracce"), AddressOf OpzioneAscolto)
                _lista.aggiungiOpzione("Elimina le tracce selezionate o la traccia corrente", AddressOf OpzioneElimina)
                _lista.aggiungiOpzione("Esporta", AddressOf OpzioneEsporta)
            End If
        End If
        If _nomeSottofondo = "" Then
            _lista.aggiungiOpzione("Scegli il sottofondo", AddressOf OpzioneScegliSottofondo)
        Else
            _lista.aggiungiOpzione(If(_motore IsNot Nothing AndAlso _motore.SottofondoInRiproduzione, "Ferma il sottofondo", "Avvia il sottofondo"), AddressOf OpzioneAvviaFermaSottofondo)
            If Not registrando Then
                _lista.aggiungiOpzione("Cambia sottofondo", AddressOf OpzioneCambiaSottofondo)
            End If
        End If
        If Not registrando Then
            _lista.aggiungiOpzione(If(_metronomoLibero, "Ferma il metronomo", "Avvia il metronomo"), AddressOf OpzioneMetronomo)
            _lista.aggiungiOpzione("Tempo del metronomo: " & _tempoMetronomo.ToString() & " al minuto", AddressOf OpzioneTempoMetronomo)
            _lista.aggiungiOpzione("Battiti per battuta: " & _battitiMetronomo.ToString(), AddressOf OpzioneBattiti)
            _lista.aggiungiOpzione(If(_metronomoInRegistrazione, "Registra senza metronomo", "Registra con il metronomo"), AddressOf OpzioneMetronomoRegistrazione)
        End If
        _lista.aggiungiOpzione("Aiuto", AddressOf OpzioneAiuto)
    End Sub

    Private Function StatoCompleto() As String
        Dim t As New StringBuilder()
        t.Append(If(_progetto.Nome <> "", "Progetto " & _progetto.Nome & ". ", "Progetto nuovo. "))
        t.Append(_strumento.Nome).Append(". ")
        If Not _strumento.Percussivo Then t.Append("Ottava ").Append(_ottava.ToString()).Append(". ")
        t.Append(If(_registrando, "Registrazione in corso. ", "Registrazione ferma. "))
        Dim tracce As Integer = NumeroTracce()
        t.Append(If(tracce = 0, "Nessuna traccia registrata. ", If(tracce = 1, "Una traccia registrata. ", tracce.ToString() & " tracce registrate. ")))
        t.Append(TestoControllo()).Append(". ")
        If _nomeSottofondo = "" Then
            t.Append("Nessun sottofondo. ")
        Else
            t.Append("Sottofondo ").Append(_nomeSottofondo).Append(If(_motore IsNot Nothing AndAlso _motore.SottofondoInRiproduzione, ", in riproduzione", ", fermo")).Append(If(_ripetiSottofondo, ", ripetuto", "")).Append(". ")
        End If
        t.Append("Volume della traccia nuova ").Append(_volumeNuova.ToString()).Append(" per cento. ")
        t.Append("Metronomo ").Append(If(_metronomoLibero, "acceso", "spento")).Append(", ").Append(_tempoMetronomo.ToString()).Append(" al minuto, ").Append(_battitiMetronomo.ToString()).Append(" battiti per battuta, ").Append(If(_metronomoInRegistrazione, "si sente mentre registri. ", "non si sente mentre registri. "))
        t.Append("Volume generale dello strumento ").Append(_volumeStrumento.ToString()).Append(" per cento, del sottofondo ").Append(_volumeSottofondo.ToString()).Append(" per cento, riverbero ").Append(_scalaRiverbero.ToString()).Append(" per cento.")
        Return t.ToString()
    End Function

    Private Sub OnVoceSelezionata(id As String)
        Try
            AvviaFermaRegistrazione()
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.OnVoceSelezionata: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OpzioneStato(sender As Object, e As EventArgs)
        AiutoMusica.Annuncia(StatoCompleto())
    End Sub

    ''' <summary>Esc: se il progetto ha modifiche da salvare, prima si chiede che cosa fare.</summary>
    Private Sub OnEsc(sender As Object, e As EventArgs)
        Try
            If _registrando Then FermaRegistrazione()
            If Not _modificato OrElse NumeroTracce() = 0 Then
                Chiudi(False)
                Return
            End If
            WGDomanda.Chiedi("Vuoi salvare il progetto?", Sub() Chiudi(True), Sub() Chiudi(False))
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.OnEsc: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Chiudi(True)
        End Try
    End Sub

    ' ---- le opzioni ----------------------------------------------------------------------------------

    Private Sub OpzioneRegistrazione(sender As Object, e As EventArgs)
        AvviaFermaRegistrazione()
    End Sub

    Private Sub OpzioneAggiungiTraccia(sender As Object, e As EventArgs)
        AggiungiTraccia()
    End Sub

    Private Sub OpzioneAscolto(sender As Object, e As EventArgs)
        AvviaFermaAscolto()
    End Sub

    Private Sub OpzioneElimina(sender As Object, e As EventArgs)
        EliminaTracce()
    End Sub

    Private Sub OpzioneEsporta(sender As Object, e As EventArgs)
        Esporta()
    End Sub

    Private Sub OpzioneCambiaSottofondo(sender As Object, e As EventArgs)
        AiutoSottofondo.MostraCambia(_nomeSottofondo <> "", Sub() ScegliSottofondo(), Sub() OpzioneRimuoviSottofondo(Nothing, Nothing))
    End Sub

    Private Sub OpzioneScegliSottofondo(sender As Object, e As EventArgs)
        ScegliSottofondo()
    End Sub

    Private Sub OpzioneAvviaFermaSottofondo(sender As Object, e As EventArgs)
        If _nomeSottofondo = "" Then
            AiutoMusica.Messaggio("Non c'è nessuna traccia di sottofondo. Sceglila prima.")
        Else
            AvviaFermaSottofondo()
        End If
    End Sub

    Private Sub OpzioneRimuoviSottofondo(sender As Object, e As EventArgs)
        Try
            If _nomeSottofondo = "" Then
                AiutoMusica.Messaggio("Non c'è nessuna traccia di sottofondo.")
                Return
            End If
            If _registrando Then
                AiutoMusica.Messaggio("Non si può togliere il sottofondo durante la registrazione.")
                Return
            End If
            _motore.FermaSottofondo()
            _motore.CaricaSottofondo(Nothing)
            _progetto.Sottofondo = ""
            _modificato = True
            _nomeSottofondo = ""
            AiutoMusica.Annuncia("Sottofondo rimosso")
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.OpzioneRimuoviSottofondo: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAiuto(sender As Object, e As EventArgs)
        MostraAiuto()
    End Sub

    ' ---- metronomo ------------------------------------------------------------------------------------

    Private Sub ApplicaMetronomo()
        Dim motore As MotoreSintesi = _motore
        If motore Is Nothing Then Return
        motore.Metronomo.Bpm = _tempoMetronomo
        motore.Metronomo.Battiti = _battitiMetronomo
    End Sub

    Private Sub OpzioneMetronomo(sender As Object, e As EventArgs)
        If _motore Is Nothing Then Return
        _metronomoLibero = Not _metronomoLibero
        ApplicaMetronomo()
        _motore.Metronomo.Attivo = _metronomoLibero
        If _metronomoLibero Then _motore.Metronomo.Riavvia()
        AiutoMusica.Annuncia(If(_metronomoLibero, "Metronomo acceso, " & _tempoMetronomo.ToString() & " al minuto", "Metronomo spento"))
    End Sub

    Private Sub OpzioneTempoMetronomo(sender As Object, e As EventArgs)
        AiutoRevisione.ChiediTesto("Tempo del metronomo, battiti al minuto, da 30 a 300", _tempoMetronomo.ToString(),
                                   Sub(testo As String)
                                       Dim valore As Integer
                                       If Not Integer.TryParse(If(testo, "").Trim(), valore) OrElse valore < 30 OrElse valore > 300 Then
                                           AiutoMusica.Messaggio("Il tempo va da 30 a 300 battiti al minuto.")
                                           Return
                                       End If
                                       _tempoMetronomo = valore
                                       ImpostazioniMusica.Scrivi("musica_metronomo_bpm", valore.ToString())
                                       ApplicaMetronomo()
                                       AiutoMusica.Annuncia("Metronomo a " & valore.ToString() & " al minuto")
                                   End Sub)
    End Sub

    Private Sub OpzioneBattiti(sender As Object, e As EventArgs)
        AiutoRevisione.ChiediTesto("Battiti per battuta, da 1 a 12", _battitiMetronomo.ToString(),
                                   Sub(testo As String)
                                       Dim valore As Integer
                                       If Not Integer.TryParse(If(testo, "").Trim(), valore) OrElse valore < 1 OrElse valore > 12 Then
                                           AiutoMusica.Messaggio("I battiti per battuta vanno da 1 a 12.")
                                           Return
                                       End If
                                       _battitiMetronomo = valore
                                       ImpostazioniMusica.Scrivi("musica_metronomo_battiti", valore.ToString())
                                       ApplicaMetronomo()
                                       AiutoMusica.Annuncia(valore.ToString() & If(valore = 1, " battito per battuta", " battiti per battuta"))
                                   End Sub)
    End Sub

    Private Sub OpzioneMetronomoRegistrazione(sender As Object, e As EventArgs)
        _metronomoInRegistrazione = Not _metronomoInRegistrazione
        ImpostazioniMusica.ScriviBooleano("musica_metronomo_registrazione", _metronomoInRegistrazione)
        AiutoMusica.Annuncia(If(_metronomoInRegistrazione, "Il metronomo si sentirà mentre registri", "Il metronomo non si sentirà mentre registri"))
    End Sub

    ' ---- il menu delle modifiche (AltGr, come in Word) -----------------------------------------------------
    ' AltGr da solo apre un menu per modificare le tracce: eliminarle, spostarle, cambiarne il volume. Sopra la traccia nuova
    ' cambia solo il volume con cui si suona lo strumento.

    Private Sub OnAltGr(sender As Object, e As EventArgs)
        Try
            If _registrando OrElse _chiusa Then Return
            Dim indice As Integer
            Dim totale As Integer
            Dim selezionate As Integer
            Dim volume As Integer
            SyncLock _bloccoTracce
                indice = _indiceRiga
                totale = _progetto.Tracce.Count
                selezionate = _selezione.Count
                volume = If(indice < totale, _progetto.Tracce(indice).Volume, _volumeNuova)
            End SyncLock
            Dim suTraccia As Boolean = indice < totale
            Dim menu As New WGMenu()
            If suTraccia OrElse selezionate > 0 Then
                menu.aggiungi(If(selezionate > 1, "Elimina le tracce selezionate", "Elimina traccia"), Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() EliminaTracce()))
            End If
            If suTraccia AndAlso indice > 0 Then menu.aggiungi("Sposta traccia più in alto", Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() SpostaTraccia(-1)))
            If suTraccia AndAlso indice < totale - 1 Then menu.aggiungi("Sposta traccia più in basso", Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() SpostaTraccia(1)))
            menu.aggiungi("Modifica volume: " & volume.ToString() & " per cento", Sub(s As Object, ev As EventArgs) ChiudiEEsegui(menu, Sub() ChiediVolume(volume)))
            menu.avvia(If(suTraccia, "Modifica la traccia " & (indice + 1).ToString(), "Modifica la traccia nuova"))
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.OnAltGr: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub ChiudiEEsegui(menu As WGMenu, azione As Action)
        Try
            menu.chiudi()
        Catch
        End Try
        azione()
    End Sub

    Private Sub ChiediVolume(attuale As Integer)
        AiutoRevisione.ChiediTesto("Volume in percento, da 0 a 400", attuale.ToString(),
                                   Sub(testo As String)
                                       Dim valore As Integer
                                       If Not Integer.TryParse(If(testo, "").Trim(), valore) OrElse valore < 0 OrElse valore > 400 Then
                                           AiutoMusica.Messaggio("Il volume va da 0 a 400 per cento.")
                                           Return
                                       End If
                                       ImpostaVolume(valore)
                                   End Sub)
    End Sub

    Private Sub ImpostaVolume(volume As Integer)
        Dim indice As Integer = -1
        SyncLock _bloccoTracce
            If _indiceRiga < _progetto.Tracce.Count Then
                _progetto.Tracce(_indiceRiga).Volume = volume
                indice = _indiceRiga
            Else
                _volumeNuova = volume
            End If
        End SyncLock
        If indice >= 0 Then
            _modificato = True
            Dim riproduttore As RiproduttoreSpartito = _riproduttore
            If riproduttore IsNot Nothing Then riproduttore.ImpostaGuadagno(indice, volume / 100.0)
        End If
        AggiornaRiga()
        AiutoMusica.Annuncia("Volume " & volume.ToString() & " per cento")
    End Sub

    ''' <summary>Scambia la traccia corrente con quella sopra (delta -1) o sotto (1); la riga la segue.</summary>
    Private Sub SpostaTraccia(delta As Integer)
        Dim spostata As Boolean = False
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaProgetto) = _progetto.Tracce
            Dim nuovo As Integer = _indiceRiga + delta
            If _indiceRiga < tracce.Count AndAlso nuovo >= 0 AndAlso nuovo < tracce.Count Then
                Dim t As TracciaProgetto = tracce(_indiceRiga)
                tracce(_indiceRiga) = tracce(nuovo)
                tracce(nuovo) = t
                _indiceRiga = nuovo
                _selezione.Clear()
                spostata = True
            End If
        End SyncLock
        If Not spostata Then Return
        _modificato = True
        AggiornaRiproduttore()
        AggiornaRiga()
        AiutoMusica.Annuncia("Traccia spostata. " & TestoControllo())
    End Sub

    Private Sub MostraAiuto()
        Dim testo As New StringBuilder()
        If _strumento.Percussivo Then
            testo.Append("Ogni tasto è un pezzo diverso. ")
        Else
            testo.Append("Le due file di lettere sono due tastiere di pianoforte. ")
            testo.Append("Nella fila bassa, Z X C V B N M virgola punto e il tasto del trattino sono le note bianche, da Do a Mi; S D G H J L e il tasto della o accentata sono le note nere. ")
            testo.Append("Nella fila alta, Q W E R T Y U I O P e i due tasti seguenti sono le note bianche un'ottava più su; i numeri 2 3 5 6 7 9 0 e il tasto della i accentata sono le note nere. ")
        End If
        testo.Append("Finché tieni premuto un tasto la nota svanisce piano piano; se lo sollevi, la nota si stacca subito. Pagina su e pagina giù cambiano ottava. Con Maiusc la nota è più forte. ")
        testo.Append("Invio avvia e ferma la registrazione: ogni registrazione è una traccia del progetto, e mentre registri senti quelle già fatte. ")
        testo.Append("Inserisci aggiunge una traccia, scegliendo lo strumento. Freccia su e freccia giù passano da una traccia all'altra; l'ultima riga è la traccia nuova. Con Maiusc e le frecce selezioni più tracce, con Canc le elimini. ")
        testo.Append("La barra spaziatrice ascolta tutte le tracce insieme. AltGr da solo apre il menu delle modifiche: elimina traccia, sposta traccia e modifica volume, della traccia corrente o, sulla traccia nuova, dello strumento che stai per registrare. Quando avvii la registrazione senti solo un beep. ")
        testo.Append("Dal menu Alt trovi anche il metronomo: puoi accenderlo, scegliere il tempo e i battiti per battuta, e farlo sentire mentre registri; il ritmo dell'esportazione segue quel tempo. ")
        testo.Append("Alt apre le opzioni: registrazione, aggiungere una traccia, ascolto, esportazione in MusicXML, MIDI, mp3 o wav, e sottofondo. Quando esci, il progetto si salva con il nome che scegli, e lo ritrovi nel menu degli strumenti musicali, in I miei progetti. Volumi, riverbero e latenza si cambiano da Configurazione, nel menu Musica.")
        If _strumento.NomiPezzi IsNot Nothing Then
            testo.Append(" I pezzi, a partire dal tasto Z: ")
            Dim primi As Integer = Math.Min(17, _strumento.NomiPezzi.Length)
            For i As Integer = 0 To primi - 1
                testo.Append(_strumento.NomiPezzi(i)).Append(If(i < primi - 1, ", ", "."))
            Next
        End If
        AiutoMusica.Messaggio(testo.ToString())
    End Sub

    ' ---- registrazione --------------------------------------------------------------------------------

    Private Sub AvviaFermaRegistrazione()
        Try
            If _registrando Then
                FermaRegistrazione()
            Else
                IniziaRegistrazione()
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.AvviaFermaRegistrazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile avviare la registrazione.")
        End Try
    End Sub

    ''' <summary>Comincia una traccia nuova: le tracce già registrate e il sottofondo partono dall'inizio, insieme al cronometro.</summary>
    Private Sub IniziaRegistrazione()
        Dim motore As MotoreSintesi = _motore
        If motore Is Nothing OrElse _registrando Then Return
        SyncLock _bloccoTracce
            _selezione.Clear()
            _indiceRiga = _progetto.Tracce.Count
        End SyncLock
        _ascolto = False
        AiutoMusica.SegnaleInizio()
        _nuovaTraccia = TracciaProgetto.Da(_strumento)
        _nuovaTraccia.Volume = _volumeNuova
        SyncLock _bloccoRegistrazione
            _noteAperte.Clear()
            _cronometro = Stopwatch.StartNew()
            _registrando = True
        End SyncLock
        If _nomeSottofondo <> "" Then motore.AvviaSottofondo(True)
        ApplicaMetronomo()
        motore.Metronomo.Attivo = _metronomoInRegistrazione OrElse _metronomoLibero
        If motore.Metronomo.Attivo Then motore.Metronomo.Riavvia()
        Dim riproduttore As RiproduttoreSpartito = _riproduttore
        If riproduttore IsNot Nothing Then riproduttore.Avvia(0.0)
        AggiornaRiga()
    End Sub

    ''' <summary>Chiude la traccia e la aggiunge al progetto. Non salva niente: il progetto si salva quando si esce, e si esporta quando si vuole.</summary>
    Private Sub FermaRegistrazione()
        If Not _registrando Then Return
        Dim nuova As TracciaProgetto = _nuovaTraccia
        SyncLock _bloccoRegistrazione
            _registrando = False
            Dim durata As Double = _cronometro.Elapsed.TotalSeconds
            For Each kv As KeyValuePair(Of Integer, NotaAperta) In _noteAperte
                nuova.Note.Add(New NotaSpartito() With {.Inizio = kv.Value.Inizio, .Durata = Math.Max(0.03, durata - kv.Value.Inizio), .Nota = kv.Value.Nota, .Velocita = kv.Value.Velocita})
            Next
            _noteAperte.Clear()
        End SyncLock
        _nuovaTraccia = Nothing
        Dim riproduttore As RiproduttoreSpartito = _riproduttore
        If riproduttore IsNot Nothing Then riproduttore.Ferma()
        If _motore IsNot Nothing Then
            _motore.FermaSottofondo()
            _motore.Metronomo.Attivo = _metronomoLibero
        End If
        If nuova.Note.Count = 0 Then
            AggiornaRiga()
            AiutoMusica.Annuncia("Non hai suonato nessuna nota, la traccia non è stata aggiunta")
            Return
        End If
        nuova.Note.Sort(Function(a As NotaSpartito, b As NotaSpartito) a.Inizio.CompareTo(b.Inizio))
        Dim numero As Integer
        SyncLock _bloccoTracce
            If _metronomoInRegistrazione OrElse _metronomoLibero Then
                ' il ritmo dell'esportazione segue il metronomo con cui si è registrato
                _progetto.Tempo = _tempoMetronomo
                _progetto.Battiti = _battitiMetronomo
            End If
            _progetto.Tracce.Add(nuova)
            numero = _progetto.Tracce.Count
            _selezione.Clear()
            _indiceRiga = _progetto.Tracce.Count
        End SyncLock
        _modificato = True
        AggiornaRiproduttore()
        AggiornaRiga()
        AiutoMusica.Annuncia("Traccia " & numero.ToString() & " registrata. " & TestoControllo())
    End Sub

    ''' <summary>Dà al riproduttore le tracce come sono adesso.</summary>
    Private Sub AggiornaRiproduttore()
        Dim vecchio As RiproduttoreSpartito = _riproduttore
        _riproduttore = Nothing
        _ascolto = False
        If vecchio IsNot Nothing Then
            RemoveHandler vecchio.Terminato, AddressOf OnTracceFinite
            vecchio.Ferma()
        End If
        Dim motore As MotoreSintesi = _motore
        If motore Is Nothing Then Return
        Dim spartito As Spartito
        Dim strumenti As List(Of DefinizioneStrumento)
        Dim guadagni As List(Of Double)
        SyncLock _bloccoTracce
            If _progetto.Tracce.Count = 0 Then Return
            spartito = _progetto.InSpartito()
            strumenti = _progetto.Strumenti()
            guadagni = _progetto.Guadagni()
        End SyncLock
        Dim nuovo As New RiproduttoreSpartito(spartito, motore, strumenti, guadagni)
        AddHandler nuovo.Terminato, AddressOf OnTracceFinite
        _riproduttore = nuovo
    End Sub

    Private Sub OnTracceFinite(sender As Object, e As EventArgs)
        If _chiusa OrElse _registrando Then Return
        _ascolto = False
        Dim motore As MotoreSintesi = _motore
        If motore IsNot Nothing Then motore.FermaSottofondo()
        AiutoMusica.AnnunciaInSottofondo("Le tracce sono finite")
    End Sub

    ''' <summary>Chiude la nota di un tasto che si lascia (o che il sistema ha fatto lasciare) mentre si registra.</summary>
    Private Sub ChiudiNotaRegistrata(scanCode As Integer)
        SyncLock _bloccoRegistrazione
            If Not _registrando Then Return
            Dim aperta As NotaAperta = Nothing
            If _noteAperte.TryGetValue(scanCode, aperta) Then
                _noteAperte.Remove(scanCode)
                Dim fine As Double = _cronometro.Elapsed.TotalSeconds
                _nuovaTraccia.Note.Add(New NotaSpartito() With {.Inizio = aperta.Inizio, .Durata = Math.Max(0.03, fine - aperta.Inizio), .Nota = aperta.Nota, .Velocita = aperta.Velocita})
            End If
        End SyncLock
    End Sub

    ' ---- le tracce: aggiungere, ascoltare, spostarsi, eliminare ----------------------------------------

    ''' <summary>Apre i menu degli strumenti: lo strumento scelto è quello della traccia nuova, le tracce già registrate restano.</summary>
    Private Sub AggiungiTraccia()
        Try
            If _registrando Then
                AiutoMusica.AnnunciaInSottofondo("Non si può cambiare strumento durante la registrazione")
                Return
            End If
            Dim menu As New MenuStrumentiMusicali()
            menu.AlloScelto = Sub(categoria As CategoriaStrumenti, strumento As DefinizioneStrumento)
                                  AiutoMusica.SuInterfaccia(Sub() ImpostaStrumentoTraccia(categoria, strumento))
                              End Sub
            _menuStrumenti = menu
            menu.Avvia()
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.AggiungiTraccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaStrumentoTraccia(categoria As CategoriaStrumenti, strumento As DefinizioneStrumento)
        Try
            Dim motore As MotoreSintesi = _motore
            If _chiusa OrElse motore Is Nothing OrElse _registrando Then Return
            _categoria = categoria
            _strumento = strumento
            _ottava = strumento.OttavaIniziale
            SyncLock _bloccoTasti
                _tastiPremuti.Clear()
            End SyncLock
            motore.ImpostaStrumento(strumento)
            SyncLock _bloccoTracce
                _selezione.Clear()
                _indiceRiga = _progetto.Tracce.Count
            End SyncLock
            AggiornaRiga()
            AiutoMusica.Annuncia("Nuova traccia: " & strumento.Nome)
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.ImpostaStrumentoTraccia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaFermaAscolto()
        Dim motore As MotoreSintesi = _motore
        If motore Is Nothing OrElse _registrando Then Return
        Dim riproduttore As RiproduttoreSpartito = _riproduttore
        If riproduttore Is Nothing Then
            AiutoMusica.AnnunciaInSottofondo("Non ci sono tracce da ascoltare")
            Return
        End If
        If _ascolto Then
            _ascolto = False
            riproduttore.Ferma()
            motore.FermaSottofondo()
            AiutoMusica.AnnunciaInSottofondo("Ascolto fermo")
        Else
            _ascolto = True
            If _nomeSottofondo <> "" Then motore.AvviaSottofondo(True)
            riproduttore.Avvia(0.0)
            AiutoMusica.AnnunciaInSottofondo("Ascolto delle tracce")
        End If
    End Sub

    ''' <summary>Freccia su e giù: la riga precedente o la successiva; con Maiusc la traccia lasciata e quella raggiunta si selezionano.</summary>
    Private Sub MuoviRiga(delta As Integer, estendi As Boolean)
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaProgetto) = _progetto.Tracce
            Dim righe As Integer = tracce.Count + 1
            Dim nuovo As Integer = _indiceRiga + delta
            If nuovo >= 0 AndAlso nuovo < righe Then
                If estendi Then
                    If _indiceRiga < tracce.Count Then _selezione.Add(tracce(_indiceRiga))
                    If nuovo < tracce.Count Then _selezione.Add(tracce(nuovo))
                Else
                    _selezione.Clear()
                End If
                _indiceRiga = nuovo
            End If
        End SyncLock
        Dim testo As String = TestoControllo()
        AggiornaRiga()
        AiutoMusica.AnnunciaInSottofondo(testo)
    End Sub

    Private Sub EliminaTracce()
        If _registrando Then
            AiutoMusica.AnnunciaInSottofondo("Non si può eliminare durante la registrazione")
            Return
        End If
        Dim eliminate As Integer
        SyncLock _bloccoTracce
            Dim tracce As List(Of TracciaProgetto) = _progetto.Tracce
            Dim daTogliere As List(Of TracciaProgetto)
            If _selezione.Count > 0 Then
                daTogliere = tracce.Where(Function(t) _selezione.Contains(t)).ToList()
            ElseIf _indiceRiga < tracce.Count Then
                daTogliere = New List(Of TracciaProgetto) From {tracce(_indiceRiga)}
            Else
                daTogliere = New List(Of TracciaProgetto)()
            End If
            For Each t As TracciaProgetto In daTogliere
                tracce.Remove(t)
            Next
            _selezione.Clear()
            _indiceRiga = Math.Min(_indiceRiga, tracce.Count)
            eliminate = daTogliere.Count
        End SyncLock
        If eliminate = 0 Then
            AiutoMusica.AnnunciaInSottofondo("Qui non c'è nessuna traccia da eliminare. " & TestoControllo())
            Return
        End If
        _modificato = True
        AggiornaRiproduttore()
        AggiornaRiga()
        AiutoMusica.AnnunciaInSottofondo(If(eliminate = 1, "Traccia eliminata. ", eliminate.ToString() & " tracce eliminate. ") & TestoControllo())
    End Sub

    ' ---- esportazione -----------------------------------------------------------------------------------
    ' Si sceglie la cartella (partendo dalla scelta delle risorse, senza nessuna cartella predefinita), poi il nome e il formato.

    Private Sub Esporta()
        Try
            If _registrando Then
                AiutoMusica.AnnunciaInSottofondo("Ferma prima la registrazione")
                Return
            End If
            If NumeroTracce() = 0 Then
                AiutoMusica.Messaggio("Non ci sono tracce da esportare.")
                Return
            End If
            _selettoreCartella = New SelettoreCartellaMusica()
            _selettoreCartella.Scegli(Sub(cartella As String) AiutoMusica.SuInterfaccia(Sub() ChiediNomeEsportazione(cartella)))
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.Esporta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediNomeEsportazione(cartella As String)
        Dim predefinito As String = If(_progetto.Nome <> "", _progetto.Nome, "Registrazione")
        AiutoRevisione.ChiediTesto("Nome del file", predefinito, Sub(testo As String)
                                                                     Dim nome As String = AiutoMusica.NomeFileSicuro(If(testo, "")).Trim()
                                                                     If nome = "" Then nome = AiutoMusica.NomeFileSicuro(predefinito)
                                                                     ScegliFormatoEsportazione(cartella, nome)
                                                                 End Sub)
    End Sub

    Private Sub ScegliFormatoEsportazione(cartella As String, nome As String)
        Try
            Dim menu As New WGMenu()
            Dim formati As String() = {"MusicXML", "MIDI", "mp3", "wav"}
            For Each formato As String In formati
                Dim scelto As String = formato
                menu.aggiungi(scelto, Sub(s As Object, ev As EventArgs)
                                          Try
                                              menu.chiudi()
                                          Catch
                                          End Try
                                          EseguiEsportazione(cartella, nome, scelto)
                                      End Sub)
            Next
            menu.avvia("Formato di " & nome)
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.ScegliFormatoEsportazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function PercorsoLibero(cartella As String, nome As String, estensione As String) As String
        Dim percorso As String = Path.Combine(cartella, nome & estensione)
        Dim n As Integer = 2
        While File.Exists(percorso)
            percorso = Path.Combine(cartella, nome & " (" & n.ToString() & ")" & estensione)
            n += 1
        End While
        Return percorso
    End Function

    ''' <summary>Esporta una copia del progetto di adesso, in sottofondo; alla fine dice dove ha scritto il file.</summary>
    Private Sub EseguiEsportazione(cartella As String, nome As String, formato As String)
        Try
            Dim copia As New ProgettoMusicale()
            SyncLock _bloccoTracce
                copia.Nome = nome
                copia.Sottofondo = If(_nomeSottofondo <> "", _progetto.Sottofondo, "")
                copia.SottofondoRipetuto = _ripetiSottofondo
                copia.Tracce.AddRange(_progetto.Tracce)
            End SyncLock
            Dim estensione As String = If(formato = "MusicXML", ".musicxml", If(formato = "MIDI", ".mid", If(formato = "mp3", ".mp3", ".wav")))
            Dim destinazione As String = PercorsoLibero(cartella, nome, estensione)
            Dim vStrumento As Single = _volumeStrumento / 100.0F
            Dim vSottofondo As Single = _volumeSottofondo / 100.0F
            Dim riverbero As Single = _scalaRiverbero / 100.0F
            Dim attesa As New WGProcessoInCorso()
            attesa.Avvia("Sto esportando, attendere", False)
            Task.Run(Sub()
                         Dim esito As String
                         Try
                             Select Case formato
                                 Case "MusicXML"
                                     ProgettoEsportazione.ScriviMusicXml(copia, destinazione)
                                     esito = "Esportato in " & destinazione & ". Il ritmo è arrotondato alla semicroma, a 120 al minuto."
                                 Case "MIDI"
                                     ProgettoEsportazione.ScriviMidi(copia, destinazione)
                                     esito = "Esportato in " & destinazione & "."
                                 Case Else
                                     esito = EsportaAudio(copia, destinazione, formato = "mp3", vStrumento, vSottofondo, riverbero)
                             End Select
                         Catch ex As Exception
                             _log.ScriviLog("SessioneStrumento.EseguiEsportazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                             esito = "Non è stato possibile esportare: " & ex.Message
                         End Try
                         AiutoMusica.SuInterfaccia(Sub()
                                                       Try
                                                           attesa.Fine()
                                                       Catch
                                                       End Try
                                                       AiutoMusica.Messaggio(esito)
                                                   End Sub)
                     End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.EseguiEsportazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function EsportaAudio(progetto As ProgettoMusicale, destinazione As String, mp3 As Boolean, vStrumento As Single, vSottofondo As Single, riverbero As Single) As String
        Dim temporaneo As String = Path.Combine(Path.GetTempPath(), "wg-progetto-" & Guid.NewGuid().ToString("N") & ".wav")
        Try
            Dim problema As String = ProgettoEsportazione.RendiInWav(progetto, temporaneo, vStrumento, vSottofondo * vSottofondo, riverbero)
            If problema <> "" Then Return problema
            If mp3 Then
                Dim messaggio As String = ""
                If WGEsportazioneAudio.ConvertiInMp3(temporaneo, destinazione, messaggio) Then Return "Esportato in " & destinazione & "."
                Dim alternativo As String = Path.ChangeExtension(destinazione, ".wav")
                If WGEsportazioneAudio.ConvertiInWav16(temporaneo, alternativo) Then Return messaggio & " L'ho salvato come file wav in " & alternativo & "."
                Return messaggio & " Non è stato possibile salvare."
            End If
            If WGEsportazioneAudio.ConvertiInWav16(temporaneo, destinazione) Then Return "Esportato in " & destinazione & "."
            Return "Non è stato possibile scrivere il file wav."
        Finally
            Try
                If File.Exists(temporaneo) Then File.Delete(temporaneo)
            Catch
            End Try
        End Try
    End Function

    ' ---- sottofondo -----------------------------------------------------------------------------------

    Private Sub ScegliSottofondo()
        Try
            If _registrando Then
                AiutoMusica.Messaggio("Non si può cambiare il sottofondo durante la registrazione.")
                Return
            End If
            _dialogoSottofondo = New WGFileDialog()
            _dialogoSottofondo.MultiselezioneConsentita = False
            For Each estensione As String In New String() {".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".aif", ".aiff", ".mp2"}
                _dialogoSottofondo.AggiungiEstensione(estensione)
            Next
            AddHandler _dialogoSottofondo.selezioneterminata, AddressOf OnSottofondoScelto
            _dialogoSottofondo.Avvia()
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.ScegliSottofondo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnSottofondoScelto(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogoSottofondo.selezioneterminata, AddressOf OnSottofondoScelto
            Catch
            End Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorso As String = elementi(0)
            If Not File.Exists(percorso) Then Return
            Dim attesa As New WGProcessoInCorso()
            attesa.Avvia("Sto caricando il sottofondo, attendere", False)
            Task.Run(Sub()
                         Dim caricato As SottofondoAudio = Nothing
                         Dim errore As String = ""
                         Try
                             caricato = New SottofondoAudio(percorso)
                         Catch ex As Exception
                             errore = "Non è stato possibile aprire il file audio: " & ex.Message
                         End Try
                         AiutoMusica.SuInterfaccia(Sub()
                                                       Try
                                                           attesa.Fine()
                                                       Catch
                                                       End Try
                                                       If caricato Is Nothing OrElse _chiusa OrElse _motore Is Nothing Then
                                                           If caricato IsNot Nothing Then caricato.Dispose()
                                                           If errore <> "" Then AiutoMusica.Messaggio(errore)
                                                           Return
                                                       End If
                                                       caricato.Ripeti = _ripetiSottofondo
                                                       _motore.CaricaSottofondo(caricato)
                                                       _progetto.Sottofondo = percorso
                                                       _modificato = True
                                                       _nomeSottofondo = Path.GetFileNameWithoutExtension(percorso)
                                                       AiutoMusica.Annuncia("Sottofondo scelto: " & _nomeSottofondo)
                                                   End Sub)
                     End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.OnSottofondoScelto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaFermaSottofondo()
        If _motore.SottofondoInRiproduzione Then
            _motore.FermaSottofondo()
            AiutoMusica.Annuncia("Sottofondo fermo")
        Else
            _motore.AvviaSottofondo(True)
            AiutoMusica.Annuncia("Sottofondo avviato")
        End If
    End Sub

    Private Sub OnSottofondoFinito(sender As Object, e As EventArgs)
        If _chiusa Then Return
        AiutoMusica.Annuncia("Il sottofondo è finito")
    End Sub

    ' ---- chiusura e salvataggio del progetto -------------------------------------------------------------

    Public Sub Chiudi(Optional salva As Boolean = True)
        If _chiusa Then Return
        _salvaAllUscita = salva
        Try
            ' la registrazione in corso non si perde: diventa l'ultima traccia
            If _registrando Then FermaRegistrazione()
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.Chiudi (registrazione): " & ex.Message)
        End Try
        _chiusa = True
        Try
            Try
                WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Nothing)
            Catch
            End Try
            If _timerFuoco IsNot Nothing Then _timerFuoco.Dispose()
            _timerFuoco = Nothing
            Dim riproduttore As RiproduttoreSpartito = _riproduttore
            _riproduttore = Nothing
            If riproduttore IsNot Nothing Then
                RemoveHandler riproduttore.Terminato, AddressOf OnTracceFinite
                riproduttore.Ferma()
            End If
            If _motore IsNot Nothing Then
                _motore.SilenziaTutto()
                Dim motore As MotoreSintesi = _motore
                _motore = Nothing
                Task.Run(Sub()
                             Thread.Sleep(250)
                             motore.Dispose()
                         End Sub)
            End If
            If _lista IsNot Nothing Then
                Try
                    RemoveHandler _lista.escPremuto, AddressOf OnEsc
                    RemoveHandler _lista.ElementoSelezionato, AddressOf OnVoceSelezionata
                    RemoveHandler _lista.AltGrPremuto, AddressOf OnAltGr
                    _lista.abbandona()
                Catch ex As Exception
                    _log.ScriviLog("SessioneStrumento.Chiudi (lista): " & ex.Message)
                End Try
                _lista = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.Chiudi: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        SalvaProgettoAllUscita()
    End Sub

    ''' <summary>
    ''' All'uscita, se ci sono tracce e qualcosa è cambiato, il progetto si salva subito con un nome provvisorio (così non si perde
    ''' niente) e poi si chiede il nome all'utente: il progetto prende quel nome.
    ''' </summary>
    Private Sub SalvaProgettoAllUscita()
        Try
            If Not _salvaAllUscita OrElse Not _modificato OrElse _progetto.Tracce.Count = 0 Then Return
            _progetto.SottofondoRipetuto = _ripetiSottofondo
            Dim nomeAttuale As String = _progetto.Nome
            Dim provvisorio As String = If(_percorsoProgettoAperto <> "", _percorsoProgettoAperto,
                                           ProgettoMusicale.PercorsoDi("Progetto senza nome " & DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss")))
            If nomeAttuale = "" Then _progetto.Nome = Path.GetFileNameWithoutExtension(provvisorio)
            _progetto.Salva(provvisorio)
            Dim progetto As ProgettoMusicale = _progetto
            Dim aperto As String = _percorsoProgettoAperto
            AiutoMusica.SuInterfaccia(Sub()
                                          AiutoRevisione.ChiediTesto("Nome del progetto", If(nomeAttuale <> "", nomeAttuale, ""),
                                                                     Sub(testo As String)
                                                                         Try
                                                                             Dim nome As String = AiutoMusica.NomeFileSicuro(If(testo, "")).Trim()
                                                                             If nome = "" Then Return
                                                                             Dim destinazione As String = ProgettoMusicale.PercorsoDi(nome)
                                                                             If File.Exists(destinazione) AndAlso Not String.Equals(destinazione, aperto, StringComparison.OrdinalIgnoreCase) AndAlso Not String.Equals(destinazione, provvisorio, StringComparison.OrdinalIgnoreCase) Then
                                                                                 Dim n As Integer = 2
                                                                                 Dim base As String = nome
                                                                                 While File.Exists(ProgettoMusicale.PercorsoDi(base & " (" & n.ToString() & ")"))
                                                                                     n += 1
                                                                                 End While
                                                                                 nome = base & " (" & n.ToString() & ")"
                                                                                 destinazione = ProgettoMusicale.PercorsoDi(nome)
                                                                             End If
                                                                             progetto.Nome = nome
                                                                             progetto.Salva(destinazione)
                                                                             If Not String.Equals(destinazione, provvisorio, StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(provvisorio) Then File.Delete(provvisorio)
                                                                             AiutoMusica.Annuncia("Progetto salvato con il nome " & nome)
                                                                         Catch ex As Exception
                                                                             _log.ScriviLog("SessioneStrumento.SalvaProgettoAllUscita (nome): " & ex.Message)
                                                                             AiutoMusica.Messaggio("Non è stato possibile dare il nome al progetto: " & ex.Message)
                                                                         End Try
                                                                     End Sub)
                                      End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.SalvaProgettoAllUscita: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile salvare il progetto: " & ex.Message)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Chiudi()
    End Sub

    ' ---- i tasti --------------------------------------------------------------------------------------

    Private Function Attiva() As Boolean
        Dim lista As WGMidiRiproduzione = _lista
        If _chiusa OrElse lista Is Nothing Then Return False
        Return lista.PuoRicevereInput() AndAlso Not lista.OpzioniAperte
    End Function

    ''' <summary>Se WinGabriel perde il primo piano o si apre qualcosa sopra, le note tenute si lasciano.</summary>
    Private Sub ControllaFuoco(stato As Object)
        Try
            If _chiusa Then Return
            Dim lista As WGMidiRiproduzione = _lista
            If lista Is Nothing Then Return
            If lista.Chiusa Then
                AiutoMusica.SuInterfaccia(Sub() Chiudi())
                Return
            End If
            If lista.PuoRicevereInput() Then Return
            Dim daLasciare As New List(Of Integer)()
            SyncLock _bloccoTasti
                daLasciare.AddRange(_tastiPremuti)
                _tastiPremuti.Clear()
            End SyncLock
            Dim motore As MotoreSintesi = _motore
            If motore Is Nothing Then Return
            For Each chiave As Integer In daLasciare
                motore.NotaOff(OffsetChiavi + chiave, True)
                ChiudiNotaRegistrata(chiave)
            Next
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.ControllaFuoco: " & ex.Message)
        End Try
    End Sub

    Public Function GestisciTasto(vkCode As Integer, scanCode As Integer, premuto As Boolean, shift As Boolean, modificatori As Boolean) As Boolean Implements IGestoreTastiMusicali.GestisciTasto
        Try
            Dim motore As MotoreSintesi = _motore
            If _chiusa OrElse motore Is Nothing Then Return False

            If Not premuto Then
                If vkCode = VkSpazio Then _spazioAscolto = False
                If vkCode = VkCanc Then _cancPremuto = False
                If vkCode = VkInserisci Then _inserisciPremuto = False
                If (vkCode = VkSu OrElse vkCode = VkGiu OrElse vkCode = VkCanc OrElse vkCode = VkInserisci) AndAlso Not modificatori AndAlso Attiva() Then Return True
                Dim eraPremuto As Boolean
                SyncLock _bloccoTasti
                    eraPremuto = _tastiPremuti.Remove(scanCode)
                End SyncLock
                If eraPremuto Then
                    ' il tasto sollevato stacca la nota subito; finché è premuto il suono segue il proprio decadimento
                    motore.NotaOff(OffsetChiavi + scanCode, True)
                    ChiudiNotaRegistrata(scanCode)
                    Return True
                End If
                Return False
            End If

            If modificatori Then Return False
            If Not Attiva() Then Return False

            Select Case vkCode
                Case VkPaginaSu
                    CambiaOttava(1)
                    Return True
                Case VkPaginaGiu
                    CambiaOttava(-1)
                    Return True
                Case VkSu
                    MuoviRiga(-1, shift)
                    Return True
                Case VkGiu
                    MuoviRiga(1, shift)
                    Return True
                Case VkCanc
                    ' il gancio della tastiera deve restituire subito: l'eliminazione ricostruisce il riproduttore
                    If Not _cancPremuto Then
                        _cancPremuto = True
                        Task.Run(Sub() EliminaTracce())
                    End If
                    Return True
                Case VkInserisci
                    If Not _inserisciPremuto Then
                        _inserisciPremuto = True
                        AiutoMusica.SuInterfaccia(Sub() AggiungiTraccia())
                    End If
                    Return True
                Case VkSpazio
                    ' la barra spaziatrice ascolta le tracce (mentre si registra le tracce suonano già)
                    If Not _registrando AndAlso Not _spazioAscolto Then
                        _spazioAscolto = True
                        AvviaFermaAscolto()
                    End If
                    Return True
            End Select

            Dim semitono As Integer = MappaTastiera.Semitono(vkCode, scanCode)
            If semitono < 0 Then Return False

            SyncLock _bloccoTasti
                If _tastiPremuti.Contains(scanCode) Then Return True
                _tastiPremuti.Add(scanCode)
            End SyncLock

            Dim nota As Integer = If(_strumento.Percussivo, semitono, 12 * (_ottava + 1) + semitono)
            If nota > 127 Then nota = 127
            Dim velocita As Double = If(shift, 1.0, 0.78)
            motore.NotaOn(OffsetChiavi + scanCode, nota, velocita, _volumeNuova / 100.0)
            SyncLock _bloccoRegistrazione
                If _registrando Then _noteAperte(scanCode) = New NotaAperta() With {.Inizio = _cronometro.Elapsed.TotalSeconds, .Nota = nota, .Velocita = velocita}
            End SyncLock
            If _leggiNote Then
                Dim nome As String = If(_strumento.Percussivo AndAlso _strumento.NomiPezzi IsNot Nothing,
                                        _strumento.NomiPezzi(((nota Mod _strumento.NomiPezzi.Length) + _strumento.NomiPezzi.Length) Mod _strumento.NomiPezzi.Length),
                                        MappaTastiera.NomeNota(nota))
                AiutoMusica.AnnunciaInSottofondo(nome)
            End If
            Return True
        Catch ex As Exception
            _log.ScriviLog("SessioneStrumento.GestisciTasto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub CambiaOttava(delta As Integer)
        If _strumento.Percussivo Then Return
        Dim nuova As Integer = Math.Max(0, Math.Min(7, _ottava + delta))
        If nuova = _ottava Then
            AiutoMusica.AnnunciaInSottofondo("Ottava " & _ottava.ToString() & ", il limite")
            Return
        End If
        _ottava = nuova
        AiutoMusica.AnnunciaInSottofondo("Ottava " & _ottava.ToString())
    End Sub

End Class

''' <summary>
''' Il menu "Strumenti musicali": I miei progetti, Scegli uno strumento da suonare, Scarica o scegli un set di strumenti. Quando i suoni
''' sono tutti scaricati la terza voce non c'è; le voci si aggiungono e si tolgono nel menu già aperto (RimuoviVoce, AggiungiDopo)
''' quando si torna ad esso.
''' </summary>
Public Class MenuStrumentiMusicali

    Private Const VoceProgetti As String = "I miei progetti"
    Private Const VoceStrumento As String = "Scegli uno strumento da suonare"
    Private Const VoceInsiemi As String = "Scarica o scegli un set di strumenti"

    Private ReadOnly _log As New Logger()
    Private _radice As WGMenu
    Private _insiemi As WGMenu
    Private _banco As BancoSoundFont
    Private ReadOnly _menuAperti As New List(Of WGMenu)()

    ''' <summary>
    ''' Se c'è, scegliere uno strumento non apre un ambiente nuovo: si chiudono i menu e si dà lo strumento a chi l'ha chiesto
    ''' (serve ad aggiungere una traccia dentro l'ambiente già aperto).
    ''' </summary>
    Public Property AlloScelto As Action(Of CategoriaStrumenti, DefinizioneStrumento)

    Private Sub ChiudiMenu()
        For i As Integer = _menuAperti.Count - 1 To 0 Step -1
            Try
                _menuAperti(i).chiudi()
            Catch
            End Try
        Next
        _menuAperti.Clear()
        _radice = Nothing
        _insiemi = Nothing
    End Sub

    Private Shared Function TuttoScaricato() As Boolean
        Return GestoreSuoni.Disponibile() AndAlso OrchestraSonatina.Installata()
    End Function

    Public Sub Avvia()
        Try
            If GestoreSuoni.Disponibile() Then
                GestoreSuoni.Carica(Sub(banco As BancoSoundFont)
                                        _banco = banco
                                        ApriPrimoMenu()
                                    End Sub)
            Else
                _banco = Nothing
                ApriPrimoMenu()
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire gli strumenti musicali.")
        End Try
    End Sub

    ''' <summary>Aggiungendo una traccia si va dritti agli strumenti; altrimenti c'è il menu con le tre voci.</summary>
    Private Sub ApriPrimoMenu()
        If AlloScelto IsNot Nothing Then
            ApriScelta(Nothing, EventArgs.Empty)
        Else
            ApriRadice()
        End If
    End Sub

    Private Sub ApriRadice()
        Try
            Dim menu As New WGMenu()
            _radice = menu
            _menuAperti.Add(menu)
            menu.aggiungi(VoceProgetti, AddressOf ApriProgetti)
            menu.aggiungi(VoceStrumento, AddressOf ApriScelta)
            If Not TuttoScaricato() Then menu.aggiungi(VoceInsiemi, AddressOf ApriInsiemi)
            AddHandler menu.MenuRiattivato, AddressOf AggiornaVociRadice
            menu.avvia("Strumenti musicali")
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriRadice: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire gli strumenti musicali.")
        End Try
    End Sub

    ''' <summary>Quando si torna al menu, la voce dei set di strumenti c'è solo se manca qualcosa da scaricare.</summary>
    Private Sub AggiornaVociRadice(sender As Object, e As EventArgs)
        Try
            Dim radice As WGMenu = _radice
            If radice Is Nothing Then Return
            If TuttoScaricato() Then
                radice.RimuoviVoce(VoceInsiemi)
            Else
                radice.AggiungiDopo(VoceStrumento, VoceInsiemi, AddressOf ApriInsiemi)
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.AggiornaVociRadice: " & ex.Message)
        End Try
    End Sub

    ' ---- scegliere uno strumento ------------------------------------------------------------------------

    Private Sub ApriScelta(sender As Object, e As EventArgs)
        Try
            Dim menu As New WGMenu()
            _menuAperti.Add(menu)
            If _banco IsNot Nothing Then
                For Each categoria As CategoriaStrumenti In CatalogoSoundFont.Categorie(_banco)
                    Dim scelta As CategoriaStrumenti = categoria
                    menu.aggiungi(scelta.Nome, Sub(s As Object, ev As EventArgs) ApriCategoria(scelta))
                Next
            End If
            If OrchestraSonatina.Installata() Then menu.aggiungi("Orchestra", AddressOf ApriOrchestra)
            menu.aggiungi("Strumenti sintetizzati", AddressOf ApriSintetizzati)
            menu.avvia("Scegli uno strumento")
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriScelta: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire gli strumenti musicali.")
        End Try
    End Sub

    Private Sub ApriOrchestra(sender As Object, e As EventArgs)
        Try
            Dim menu As New WGMenu()
            _menuAperti.Add(menu)
            For Each categoria As CategoriaStrumenti In OrchestraSonatina.Categorie()
                Dim scelta As CategoriaStrumenti = categoria
                menu.aggiungi(scelta.Nome, Sub(s As Object, ev As EventArgs) ApriCategoria(scelta))
            Next
            menu.avvia("Orchestra")
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriOrchestra: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriSintetizzati(sender As Object, e As EventArgs)
        Try
            Dim menu As New WGMenu()
            _menuAperti.Add(menu)
            For Each categoria As CategoriaStrumenti In CatalogoStrumenti.Categorie()
                Dim scelta As CategoriaStrumenti = categoria
                menu.aggiungi(scelta.Nome, Sub(s As Object, ev As EventArgs) ApriCategoria(scelta))
            Next
            menu.avvia("Strumenti sintetizzati")
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriSintetizzati: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriCategoria(categoria As CategoriaStrumenti)
        Try
            Dim menu As New WGMenu()
            _menuAperti.Add(menu)
            For Each strumento As DefinizioneStrumento In categoria.Strumenti
                Dim scelto As DefinizioneStrumento = strumento
                menu.aggiungi(scelto.Nome, Sub(sender As Object, e As EventArgs) ApriStrumento(categoria, scelto))
            Next
            menu.avvia(categoria.Nome)
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriCategoria: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriStrumento(categoria As CategoriaStrumenti, strumento As DefinizioneStrumento)
        Try
            If AlloScelto IsNot Nothing Then
                Dim azione As Action(Of CategoriaStrumenti, DefinizioneStrumento) = AlloScelto
                ChiudiMenu()
                azione(categoria, strumento)
                Return
            End If
            Dim sessione As New SessioneStrumento(categoria, strumento)
            sessione.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriStrumento: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire lo strumento.")
        End Try
    End Sub

    ' ---- scaricare o scegliere un set di strumenti -------------------------------------------------------

    Private Sub ApriInsiemi(sender As Object, e As EventArgs)
        Try
            Dim menu As New WGMenu()
            _insiemi = menu
            _menuAperti.Add(menu)
            If GestoreSuoni.Disponibile() Then
                menu.aggiungi("Scarica di nuovo i suoni ad alta qualità", AddressOf ScaricaSuoni)
            Else
                menu.aggiungi("Scarica i suoni ad alta qualità", AddressOf ScaricaSuoni)
            End If
            If Not OrchestraSonatina.Installata() Then
                menu.aggiungi("Scarica l'orchestra, strumenti campionati da un'orchestra vera, circa cinquecento megabyte", AddressOf ScaricaOrchestra)
            End If
            menu.aggiungi("Scegli un file di suoni SoundFont", AddressOf ScegliFileSuoni)
            menu.avvia("Set di strumenti")
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriInsiemi: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>Dopo un download o una scelta: si ricarica il banco dei suoni e si torna al menu, che si aggiorna da solo.</summary>
    Private Sub DopoScaricamento(riuscito As Boolean)
        Try
            If Not riuscito Then Return
            If GestoreSuoni.Disponibile() Then
                GestoreSuoni.Carica(Sub(banco As BancoSoundFont)
                                        _banco = banco
                                        TornaAlMenu()
                                    End Sub)
            Else
                TornaAlMenu()
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.DopoScaricamento: " & ex.Message)
        End Try
    End Sub

    Private Sub TornaAlMenu()
        AiutoMusica.SuInterfaccia(Sub()
                                      Try
                                          If _insiemi IsNot Nothing Then
                                              _menuAperti.Remove(_insiemi)
                                              _insiemi.chiudi()
                                              _insiemi = Nothing
                                          End If
                                      Catch
                                      End Try
                                      AggiornaVociRadice(Nothing, EventArgs.Empty)
                                  End Sub)
    End Sub

    Private Sub ScaricaOrchestra(sender As Object, e As EventArgs)
        OrchestraSonatina.Scarica(Sub(riuscito As Boolean) DopoScaricamento(riuscito))
    End Sub

    Private Sub ScaricaSuoni(sender As Object, e As EventArgs)
        Try
            GestoreSuoni.Scarica(Sub(riuscito As Boolean) DopoScaricamento(riuscito))
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ScaricaSuoni: " & ex.Message)
            AiutoMusica.Messaggio("Non è stato possibile scaricare i suoni.")
        End Try
    End Sub

    Private Sub ScegliFileSuoni(sender As Object, e As EventArgs)
        GestoreSuoni.ScegliFile(Sub(riuscito As Boolean) DopoScaricamento(riuscito))
    End Sub

    ' ---- i progetti -------------------------------------------------------------------------------------

    ''' <summary>I progetti salvati: si sceglie un nome e si riapre l'ambiente con le sue tracce.</summary>
    Private Sub ApriProgetti(sender As Object, e As EventArgs)
        Try
            Dim elenco As List(Of String) = ProgettoMusicale.Elenco()
            If elenco.Count = 0 Then
                AiutoMusica.Messaggio("Non ci sono ancora progetti. Si salvano quando esci da uno strumento dopo aver registrato.")
                Return
            End If
            Dim menu As New WGMenu()
            _menuAperti.Add(menu)
            For Each file As String In elenco
                Dim percorso As String = file
                menu.aggiungi(Path.GetFileNameWithoutExtension(percorso), Sub(s As Object, ev As EventArgs) ApriProgetto(percorso))
            Next
            menu.avvia("I miei progetti")
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriProgetti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApriProgetto(percorso As String)
        Try
            Dim banco As BancoSoundFont = _banco
            Dim progetto As ProgettoMusicale = ProgettoMusicale.Carica(percorso, Function(origine As String, nome As String) ProgettoMusicale.TrovaStrumento(origine, nome, banco))
            Dim mancanti As Integer = progetto.Tracce.Where(Function(t As TracciaProgetto) t.Strumento Is Nothing).Count()
            Dim strumento As DefinizioneStrumento = Nothing
            For i As Integer = progetto.Tracce.Count - 1 To 0 Step -1
                If progetto.Tracce(i).Strumento IsNot Nothing Then
                    strumento = progetto.Tracce(i).Strumento
                    Exit For
                End If
            Next
            If strumento Is Nothing Then strumento = CatalogoStrumenti.Categorie()(0).Strumenti(0)
            ChiudiMenu()
            If mancanti > 0 Then AiutoMusica.Messaggio(If(mancanti = 1, "Uno strumento del progetto non è disponibile (forse i suoni ad alta qualità non sono scaricati): quella traccia non suona, ma resta nel progetto.", mancanti.ToString() & " strumenti del progetto non sono disponibili (forse i suoni ad alta qualità non sono scaricati): quelle tracce non suonano, ma restano nel progetto."))
            Dim sessione As New SessioneStrumento(strumento, progetto, percorso)
            sessione.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuStrumentiMusicali.ApriProgetto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire il progetto: " & ex.Message)
        End Try
    End Sub

End Class
