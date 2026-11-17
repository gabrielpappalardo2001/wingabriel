' ============================================================================================
' WGSpartitiRiproduzione - suonare uno spartito: una parte alla volta o tutte insieme
' ============================================================================================
'
' RiproduttoreSpartito manda al motore musicale (WGSintesi.Motore) le note dello spartito al momento giusto, ciascuna con
' lo strumento della sua parte (suoni campionati General MIDI). SessioneSpartito è l'ambiente: un controllo a riga sola,
' Invio avvia e ferma, Alt apre le opzioni. MenuSpartiti sceglie il file e, se è un PDF o una foto, lo fa riconoscere.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public NotInheritable Class RiproduttoreSpartito
    Implements IDisposable

    <DllImport("winmm.dll")>
    Private Shared Function timeBeginPeriod(periodo As UInteger) As UInteger
    End Function

    <DllImport("winmm.dll")>
    Private Shared Function timeEndPeriod(periodo As UInteger) As UInteger
    End Function

    Private NotInheritable Class EventoSpartito
        Public Tempo As Double
        Public Acceso As Boolean
        Public Chiave As Integer
        Public Nota As Integer
        Public Velocita As Double
        Public Strumento As DefinizioneStrumento
        Public Parte As Integer
        Public Guadagno As Double = 1.0
    End Class

    Private ReadOnly _spartito As Spartito
    Private ReadOnly _motore As MotoreSintesi
    Private ReadOnly _strumenti As List(Of DefinizioneStrumento)
    Private ReadOnly _guadagni As List(Of Double)
    Private _eventi As List(Of EventoSpartito)
    Private _parte As Integer = -1
    Private _velocita As Double = 1.0
    Private _versione As Integer
    Private _thread As Thread
    Private _inRiproduzione As Boolean
    Private _partenza As Double
    Private _cronometro As Stopwatch
    Private _velocitaAvvio As Double = 1.0
    Private _fermoA As Double

    Public Event Terminato As EventHandler
    Public Event IntervalloFinito As EventHandler

    Public Sub New(spartito As Spartito, motore As MotoreSintesi, strumenti As List(Of DefinizioneStrumento), Optional guadagni As List(Of Double) = Nothing)
        _spartito = spartito
        _motore = motore
        _strumenti = strumenti
        _guadagni = If(guadagni, New List(Of Double)())
        While _guadagni.Count < spartito.Parti.Count
            _guadagni.Add(1.0)
        End While
        CostruisciEventi()
    End Sub

    ''' <summary>Cambia il volume di una parte (1 è quello normale), anche mentre suona.</summary>
    Public Sub ImpostaGuadagno(parte As Integer, guadagno As Double)
        If parte < 0 OrElse parte >= _guadagni.Count Then Return
        _guadagni(parte) = guadagno
        Dim eventi As List(Of EventoSpartito) = _eventi
        If eventi Is Nothing Then Return
        For Each ev As EventoSpartito In eventi
            If ev.Parte = parte Then ev.Guadagno = guadagno
        Next
    End Sub

    Public ReadOnly Property InRiproduzione As Boolean
        Get
            Return _inRiproduzione
        End Get
    End Property

    ''' <summary>La parte che suona da sola; -1 per tutte insieme.</summary>
    Public Property Parte As Integer
        Get
            Return _parte
        End Get
        Set(valore As Integer)
            _parte = valore
            CostruisciEventi()
        End Set
    End Property

    Public Property Velocita As Double
        Get
            Return _velocita
        End Get
        Set(valore As Double)
            _velocita = Math.Max(0.25, Math.Min(3.0, valore))
        End Set
    End Property

    ''' <summary>L'istante dello spartito (in secondi, alla velocità normale) a cui si è arrivati.</summary>
    Public ReadOnly Property Posizione As Double
        Get
            If _inRiproduzione AndAlso _cronometro IsNot Nothing Then
                Return Math.Min(_spartito.Durata, _partenza + _cronometro.Elapsed.TotalSeconds * _velocitaAvvio)
            End If
            Return _fermoA
        End Get
    End Property

    Private Sub CostruisciEventi()
        Dim eventi As New List(Of EventoSpartito)()
        Dim chiave As Integer = 1
        For i As Integer = 0 To _spartito.Parti.Count - 1
            If _parte >= 0 AndAlso _parte <> i Then Continue For
            Dim strumento As DefinizioneStrumento = _strumenti(i)
            If strumento Is Nothing Then Continue For
            For Each n As NotaSpartito In _spartito.Parti(i).Note
                Dim on1 As New EventoSpartito()
                on1.Tempo = n.Inizio
                on1.Acceso = True
                on1.Chiave = chiave
                on1.Nota = n.Nota
                on1.Velocita = n.Velocita
                on1.Strumento = strumento
                on1.Parte = i
                on1.Guadagno = _guadagni(i)
                eventi.Add(on1)
                Dim off1 As New EventoSpartito()
                off1.Tempo = n.Inizio + If(_spartito.Parti(i).Batteria, Math.Min(n.Durata, 0.4), n.Durata * 0.98)
                off1.Acceso = False
                off1.Chiave = chiave
                eventi.Add(off1)
                chiave += 1
                If chiave > 2000000000 Then chiave = 1
            Next
        Next
        eventi.Sort(Function(a As EventoSpartito, b As EventoSpartito)
                        Dim c As Integer = a.Tempo.CompareTo(b.Tempo)
                        If c <> 0 Then Return c
                        Return a.Acceso.CompareTo(b.Acceso)
                    End Function)
        _eventi = eventi
    End Sub

    ''' <summary>Comincia a suonare dall'istante indicato.</summary>
    Public Sub Avvia(daSecondi As Double)
        Avvia(daSecondi, Double.MaxValue)
    End Sub

    ''' <summary>Suona solo fino all'istante indicato, poi si ferma da sola (per ascoltare una battuta).</summary>
    Public Sub Avvia(daSecondi As Double, fino As Double)
        Ferma()
        If daSecondi >= _spartito.Durata Then daSecondi = 0.0
        _partenza = Math.Max(0.0, daSecondi)
        _velocitaAvvio = _velocita
        _cronometro = Stopwatch.StartNew()
        _inRiproduzione = True
        _versione += 1
        Dim versione As Integer = _versione
        Dim partenza As Double = _partenza
        Dim velocita As Double = _velocitaAvvio
        Dim eventi As List(Of EventoSpartito) = _eventi
        _thread = New Thread(Sub() Ciclo(versione, partenza, velocita, eventi, fino))
        _thread.IsBackground = True
        _thread.Name = "WGSpartito"
        _thread.Priority = ThreadPriority.AboveNormal
        _thread.Start()
    End Sub

    ''' <summary>Ferma e ricorda dove si era arrivati.</summary>
    Public Sub Ferma()
        If _inRiproduzione Then
            _fermoA = Posizione
            _inRiproduzione = False
        End If
        _versione += 1
        Dim t As Thread = _thread
        _thread = Nothing
        If t IsNot Nothing AndAlso t IsNot Thread.CurrentThread Then
            Try
                t.Join(300)
            Catch
            End Try
        End If
        Try
            _motore.SilenziaTutto()
        Catch
        End Try
    End Sub

    Public Sub Vai(secondi As Double)
        Dim sta As Boolean = _inRiproduzione
        If sta Then
            Avvia(secondi)
        Else
            _fermoA = Math.Max(0.0, Math.Min(_spartito.Durata, secondi))
        End If
    End Sub

    Private Sub Ciclo(versione As Integer, partenza As Double, velocita As Double, eventi As List(Of EventoSpartito), limite As Double)
        Dim periodoImpostato As Boolean = False
        Try
            Try
                periodoImpostato = timeBeginPeriod(1UI) = 0UI
            Catch
            End Try
            Dim sw As Stopwatch = _cronometro
            Dim i As Integer = 0
            While i < eventi.Count AndAlso eventi(i).Tempo < partenza - 0.0001
                i += 1
            End While
            While versione = _versione AndAlso i < eventi.Count
                Dim ev As EventoSpartito = eventi(i)
                If ev.Tempo >= limite AndAlso ev.Acceso Then
                    i += 1
                    Continue While
                End If
                Dim bersaglio As Double = (Math.Min(ev.Tempo, limite) - partenza) / velocita * 1000.0
                While versione = _versione
                    Dim resto As Double = bersaglio - sw.Elapsed.TotalMilliseconds
                    If resto <= 0.0 Then Exit While
                    If resto > 3.0 Then Thread.Sleep(1) Else Thread.SpinWait(150)
                End While
                If versione <> _versione Then Return
                If ev.Acceso Then
                    _motore.NotaOnStrumento(ev.Chiave, ev.Nota, ev.Velocita, ev.Strumento, ev.Guadagno)
                Else
                    _motore.NotaOff(ev.Chiave)
                End If
                i += 1
            End While
            ' la coda: le ultime note finiscono di risuonare
            Dim fine As Double = (Math.Min(_spartito.Durata, limite) - partenza) / velocita * 1000.0 + 1200.0
            While versione = _versione AndAlso sw.Elapsed.TotalMilliseconds < fine
                Thread.Sleep(20)
            End While
            If versione = _versione Then
                _inRiproduzione = False
                If limite < _spartito.Durata Then
                    _fermoA = partenza
                    RaiseEvent IntervalloFinito(Me, EventArgs.Empty)
                Else
                    _fermoA = 0.0
                    RaiseEvent Terminato(Me, EventArgs.Empty)
                End If
            End If
        Catch ex As Exception
        Finally
            If periodoImpostato Then
                Try
                    timeEndPeriod(1UI)
                Catch
                End Try
            End If
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Ferma()
    End Sub

End Class

''' <summary>L'ambiente per ascoltare uno spartito.</summary>
Public NotInheritable Class SessioneSpartito
    Implements IGestoreTastiMusicali, IDisposable

    Private Const VkSinistra As Integer = &H25
    Private Const VkSu As Integer = &H26
    Private Const VkDestra As Integer = &H27
    Private Const VkGiu As Integer = &H28
    Private Const VkPaginaSu As Integer = &H21
    Private Const VkPaginaGiu As Integer = &H22
    Private Const VkInizio As Integer = &H24
    Private Const VkSpazio As Integer = &H20
    Private Const IdControllo As String = "controllo"

    Private Shared ReadOnly Velocita() As Integer = {50, 60, 70, 80, 90, 100, 110, 125, 150, 175, 200}

    Private ReadOnly _log As New Logger()
    Private _spartito As Spartito
    Private ReadOnly _banco As BancoSoundFont
    Private ReadOnly _revisione As RevisioneMusicXml
    Private ReadOnly _percorsoCorretto As String
    Private ReadOnly _versioneCorretta As Boolean
    Private _strumenti As List(Of DefinizioneStrumento)
    Private _parteDopoAscolto As Integer = -2
    Public Avviso As String = ""
    Private _motore As MotoreSintesi
    Private _riproduttore As RiproduttoreSpartito
    Private _lista As WGMidiRiproduzione
    Private _registratore As RegistratoreFlusso
    Private _percorsoTemporaneo As String = ""
    Private _timerFuoco As System.Threading.Timer
    Private _chiusa As Boolean
    Private _indiceVelocita As Integer = 5
    Private _volume As Integer = 80
    Private _scalaRiverbero As Integer = 100
    Private _selettoreCartella As SelettoreCartellaMusica

    Public Sub New(spartito As Spartito, banco As BancoSoundFont)
        _spartito = spartito
        _banco = banco
    End Sub

    ''' <summary>Per gli spartiti MusicXML: con la revisione si possono controllare e correggere le battute.</summary>
    Public Sub New(spartito As Spartito, banco As BancoSoundFont, revisione As RevisioneMusicXml, percorsoCorretto As String, versioneCorretta As Boolean)
        _spartito = spartito
        _banco = banco
        _revisione = revisione
        _percorsoCorretto = percorsoCorretto
        _versioneCorretta = versioneCorretta
    End Sub

    Private Function CostruisciStrumenti(spartito As Spartito) As List(Of DefinizioneStrumento)
        Dim strumenti As New List(Of DefinizioneStrumento)()
        For Each parte As ParteSpartito In spartito.Parti
            Dim orchestrale As DefinizioneStrumento = OrchestraSonatina.PerParte(parte.Nome, parte.Programma, parte.Batteria)
            strumenti.Add(If(orchestrale, CatalogoSoundFont.StrumentoGM(_banco, parte.Programma, parte.Batteria)))
        Next
        Return strumenti
    End Function

    Public Sub Avvia()
        Try
            _volume = Math.Max(0, Math.Min(100, ImpostazioniMusica.LeggiIntero("spartiti_volume", 80)))
            _scalaRiverbero = Math.Max(0, Math.Min(150, ImpostazioniMusica.LeggiIntero("spartiti_riverbero", 100)))

            Dim strumenti As List(Of DefinizioneStrumento) = CostruisciStrumenti(_spartito)
            _strumenti = strumenti
            _motore = New MotoreSintesi()
            _motore.VolumeStrumento = _volume / 100.0F
            _motore.ScalaRiverbero = _scalaRiverbero / 100.0F
            Dim base As DefinizioneStrumento = strumenti.FirstOrDefault(Function(s) s IsNot Nothing AndAlso Not s.Percussivo)
            If base Is Nothing Then base = strumenti.FirstOrDefault(Function(s) s IsNot Nothing)
            If base IsNot Nothing Then _motore.ImpostaStrumento(base)
            Try
                _motore.Avvia(100)
            Catch exAudio As Exception
                _log.ScriviLog("SessioneSpartito.Avvia (audio): " & exAudio.Message)
                _motore.Dispose()
                _motore = Nothing
                AiutoMusica.Messaggio("Non è stato possibile aprire la scheda audio per suonare.")
                Return
            End Try
            _riproduttore = New RiproduttoreSpartito(_spartito, _motore, strumenti)
            _riproduttore.Velocita = Velocita(_indiceVelocita) / 100.0
            AddHandler _riproduttore.Terminato, AddressOf OnTerminato
            AddHandler _riproduttore.IntervalloFinito, AddressOf OnIntervalloFinito

            _lista = New WGMidiRiproduzione()
            _lista.multiselezioneConsentita = False
            _lista.impostaTitolo("Spartito")
            RiempiLista()
            AddHandler _lista.escPremuto, AddressOf OnEsc
            AddHandler _lista.ElementoSelezionato, AddressOf OnVoceSelezionata
            _lista.AggiornaOpzioni = AddressOf DefinisciOpzioni
            _lista.avvia()

            WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Me)
            _timerFuoco = New System.Threading.Timer(AddressOf ControllaFuoco, Nothing, 300, 300)
            AiutoMusica.Messaggio(DescrizioneParti())
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Chiudi()
        End Try
    End Sub

    ' ---- il controllo ----------------------------------------------------------------------------------

    Private Function TestoControllo() As String
        Return _spartito.Titolo
    End Function

    ' Le opzioni di Alt: solo quelle che servono adesso. Volume e riverbero sono nella Configurazione del menu Musica.
    Private Sub DefinisciOpzioni()
        _lista.pulisciOpzioni()
        Dim suona As Boolean = _riproduttore IsNot Nothing AndAlso _riproduttore.InRiproduzione
        _lista.aggiungiOpzione(If(suona, "Ferma", "Avvia"), AddressOf OpzioneAvviaFerma)
        _lista.aggiungiOpzione("Stato", AddressOf OpzioneStato)
        If _riproduttore IsNot Nothing Then
            If _spartito.Parti.Count > 1 Then
                _lista.aggiungiOpzione("Ascolta uno strumento", AddressOf OpzioneScegliParte)
                If _riproduttore.Parte >= 0 Then _lista.aggiungiOpzione("Tutti gli strumenti", AddressOf OpzioneTutte)
                _lista.aggiungiOpzione("Strumento successivo", AddressOf OpzioneParteSuccessiva)
                _lista.aggiungiOpzione("Strumento precedente", AddressOf OpzioneParteprecedente)
            End If
            If _riproduttore.Posizione > 0.0 Then _lista.aggiungiOpzione("Dall'inizio", AddressOf OpzioneInizio)
            If _indiceVelocita < Velocita.Length - 1 Then _lista.aggiungiOpzione("Più veloce", AddressOf OpzionePiuVeloce)
            If _indiceVelocita > 0 Then _lista.aggiungiOpzione("Più lento", AddressOf OpzionePiuLento)
        End If
        If _revisione IsNot Nothing Then
            _lista.aggiungiOpzione("Controlla", AddressOf OpzioneControlla)
            _lista.aggiungiOpzione("Confronta con un'altra lettura", AddressOf OpzioneAltraLettura)
            _lista.aggiungiOpzione("Correggi una battuta", AddressOf OpzioneCorreggi)
            If _prima = "" Then
                _lista.aggiungiOpzione("Correggi gli errori sicuri", AddressOf OpzioneAutomatica)
            Else
                _lista.aggiungiOpzione("Annulla la correzione automatica", AddressOf OpzioneAnnullaAutomatica)
            End If
            _lista.aggiungiOpzione("Assegna uno strumento", AddressOf OpzioneStrumentoParte)
            _lista.aggiungiOpzione("Assegna un organico", AddressOf OpzioneOrganico)
            _lista.aggiungiOpzione("Trasponi uno strumento", AddressOf OpzioneTrasposizione)
            _lista.aggiungiOpzione("Tempo del brano", AddressOf OpzioneTempo)
            _lista.aggiungiOpzione("Esporta in MusicXML", AddressOf OpzioneEsporta)
            _lista.aggiungiOpzione("Esporta compresso", AddressOf OpzioneEsportaCompresso)
        End If
        _lista.aggiungiOpzione("Salva in mp3", AddressOf OpzioneSalva)
        _lista.aggiungiOpzione("Altro spartito", AddressOf OpzioneAltro)
        _lista.aggiungiOpzione("Aiuto", AddressOf OpzioneAiuto)
    End Sub

    Private Function NomeParteCorrente() As String
        If _riproduttore Is Nothing OrElse _riproduttore.Parte < 0 Then Return "tutti gli strumenti insieme"
        Dim i As Integer = _riproduttore.Parte
        Return "strumento " & (i + 1).ToString() & " di " & _spartito.Parti.Count.ToString() & ", " & _spartito.Parti(i).Nome & ", da solo"
    End Function

    Private Function DescrizioneParti() As String
        Dim t As New StringBuilder()
        t.Append(_spartito.Titolo).Append(". ")
        If _spartito.Origine = "MIDI" Then
            If _spartito.TracceMidi > 1 Then
                t.Append("File MIDI multitraccia, con " & _spartito.TracceMidi.ToString() & " tracce")
            Else
                t.Append("File MIDI a una sola traccia")
            End If
            t.Append(If(_spartito.Parti.Count = 1, ", uno strumento. ", ", " & _spartito.Parti.Count.ToString() & " strumenti. "))
            If _spartito.Assegnazioni.Count > 0 Then
                t.Append("Il file non dice tutti gli strumenti, li ho scelti io: " & String.Join("; ", _spartito.Assegnazioni) & ". ")
            Else
                t.Append("Gli strumenti sono quelli del file. ")
            End If
        Else
            t.Append(If(_spartito.Parti.Count = 1, "Uno strumento. ", _spartito.Parti.Count.ToString() & " strumenti. "))
        End If
        If _versioneCorretta Then t.Append("Si apre la versione che avevi corretto. ")
        If Avviso <> "" Then t.Append(Avviso).Append(". ")
        Return t.ToString().TrimEnd(" "c, "."c)
    End Function

    Private Function StatoCompleto() As String
        Dim t As New StringBuilder()
        t.Append(_spartito.Titolo).Append(". ")
        t.Append(If(_riproduttore IsNot Nothing AndAlso _riproduttore.InRiproduzione, "In riproduzione. ", "Fermo. "))
        t.Append("Battuta ").Append(_spartito.NumeroBattuta(If(_riproduttore Is Nothing, 0.0, _riproduttore.Posizione)).ToString()).Append(" di ").Append(_spartito.Battute.Count.ToString()).Append(". ")
        t.Append("Si ascolta: ").Append(NomeParteCorrente()).Append(". ")
        t.Append("Velocità ").Append(Velocita(_indiceVelocita).ToString()).Append(" per cento. ")
        t.Append("Volume ").Append(_volume.ToString()).Append(" per cento, riverbero ").Append(_scalaRiverbero.ToString()).Append(" per cento. ")
        t.Append(If(_registratore IsNot Nothing, "Registrazione in corso. ", ""))
        Return t.ToString().TrimEnd()
    End Function

    Private Sub RiempiLista()
        If _lista Is Nothing Then Return
        _lista.pulisci()
        _lista.aggiungiElemento(TestoControllo(), IdControllo)
        _lista.posiziona(IdControllo)
    End Sub

    Private Sub OnVoceSelezionata(id As String)
        AvviaFerma()
    End Sub

    Private Sub OnEsc(sender As Object, e As EventArgs)
        Chiudi()
    End Sub

    Private Sub OpzioneStato(sender As Object, e As EventArgs)
        AiutoMusica.Annuncia(StatoCompleto())
    End Sub

    ' ---- le opzioni ------------------------------------------------------------------------------------

    Private Sub OpzioneAvviaFerma(sender As Object, e As EventArgs)
        AvviaFerma()
    End Sub

    Private Sub AvviaFerma()
        Try
            If _riproduttore Is Nothing Then Return
            If _riproduttore.InRiproduzione Then
                _riproduttore.Ferma()
                AiutoMusica.AnnunciaInSottofondo("Fermo alla battuta " & _spartito.NumeroBattuta(_riproduttore.Posizione).ToString())
            Else
                _riproduttore.Avvia(_riproduttore.Posizione)
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.AvviaFerma: " & ex.Message)
        End Try
    End Sub

    Private Sub CambiaParte(parte As Integer)
        If _riproduttore Is Nothing Then Return
        Dim sta As Boolean = _riproduttore.InRiproduzione
        Dim posizione As Double = _riproduttore.Posizione
        If sta Then _riproduttore.Ferma()
        _riproduttore.Parte = parte
        If sta Then _riproduttore.Avvia(posizione)
        AiutoMusica.Annuncia("Si ascolta " & NomeParteCorrente())
    End Sub

    Private Sub OpzioneTutte(sender As Object, e As EventArgs)
        CambiaParte(-1)
    End Sub

    ''' <summary>Un elenco delle parti: scelta una, suona da sola (e parte subito se lo spartito era fermo).</summary>
    Private Sub OpzioneScegliParte(sender As Object, e As EventArgs)
        Try
            If _riproduttore Is Nothing Then Return
            Dim menu As New WGMenu()
            menu.aggiungi("Tutti gli strumenti", Sub(s As Object, ev As EventArgs)
                                                        Try
                                                            menu.chiudi()
                                                        Catch
                                                        End Try
                                                        CambiaParte(-1)
                                                        If Not _riproduttore.InRiproduzione Then _riproduttore.Avvia(_riproduttore.Posizione)
                                                    End Sub)
            For i As Integer = 0 To _spartito.Parti.Count - 1
                Dim indice As Integer = i
                Dim parte As ParteSpartito = _spartito.Parti(i)
                Dim testo As String = (i + 1).ToString() & ". " & parte.Nome
                If Not parte.Batteria Then testo &= ", " & CatalogoSoundFont.NomeProgramma(parte.Programma)
                menu.aggiungi(testo, Sub(s As Object, ev As EventArgs)
                                         Try
                                             menu.chiudi()
                                         Catch
                                         End Try
                                         CambiaParte(indice)
                                         If Not _riproduttore.InRiproduzione Then _riproduttore.Avvia(_riproduttore.Posizione)
                                     End Sub)
            Next
            menu.avvia("Strumenti")
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.OpzioneScegliParte: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneParteSuccessiva(sender As Object, e As EventArgs)
        Dim n As Integer = _spartito.Parti.Count
        Dim corrente As Integer = _riproduttore.Parte
        CambiaParte(If(corrente < 0, 0, If(corrente + 1 >= n, -1, corrente + 1)))
    End Sub

    Private Sub OpzioneParteprecedente(sender As Object, e As EventArgs)
        Dim n As Integer = _spartito.Parti.Count
        Dim corrente As Integer = _riproduttore.Parte
        CambiaParte(If(corrente < 0, n - 1, corrente - 1))
    End Sub

    Private Sub OpzioneElencoParti(sender As Object, e As EventArgs)
        Dim t As New StringBuilder()
        For i As Integer = 0 To _spartito.Parti.Count - 1
            t.Append(i + 1).Append(": ").Append(_spartito.Parti(i).Nome).Append(If(i < _spartito.Parti.Count - 1, ". ", "."))
        Next
        AiutoMusica.Messaggio("Gli strumenti dello spartito. " & t.ToString())
    End Sub

    Private Sub OpzioneInizio(sender As Object, e As EventArgs)
        _riproduttore.Vai(0.0)
        AiutoMusica.Annuncia("Dall'inizio")
    End Sub

    Private Sub CambiaVelocita(delta As Integer)
        Dim nuovo As Integer = Math.Max(0, Math.Min(Velocita.Length - 1, _indiceVelocita + delta))
        If nuovo = _indiceVelocita Then
            AiutoMusica.AnnunciaInSottofondo("Velocità " & Velocita(_indiceVelocita).ToString() & " per cento, il limite")
            Return
        End If
        _indiceVelocita = nuovo
        Dim sta As Boolean = _riproduttore.InRiproduzione
        Dim posizione As Double = _riproduttore.Posizione
        If sta Then _riproduttore.Ferma()
        _riproduttore.Velocita = Velocita(_indiceVelocita) / 100.0
        If sta Then _riproduttore.Avvia(posizione)
        AiutoMusica.AnnunciaInSottofondo("Velocità " & Velocita(_indiceVelocita).ToString() & " per cento")
    End Sub

    Private Sub OpzionePiuVeloce(sender As Object, e As EventArgs)
        CambiaVelocita(1)
    End Sub

    Private Sub OpzionePiuLento(sender As Object, e As EventArgs)
        CambiaVelocita(-1)
    End Sub

    Private Sub CambiaVolume(delta As Integer)
        _volume = Math.Max(0, Math.Min(100, _volume + delta))
        _motore.VolumeStrumento = _volume / 100.0F
        ImpostazioniMusica.Scrivi("spartiti_volume", _volume.ToString())
        AiutoMusica.Annuncia("Volume " & _volume.ToString() & " per cento")
    End Sub

    Private Sub CambiaRiverbero(delta As Integer)
        _scalaRiverbero = Math.Max(0, Math.Min(150, _scalaRiverbero + delta))
        _motore.ScalaRiverbero = _scalaRiverbero / 100.0F
        ImpostazioniMusica.Scrivi("spartiti_riverbero", _scalaRiverbero.ToString())
        AiutoMusica.Annuncia("Riverbero " & _scalaRiverbero.ToString() & " per cento")
    End Sub

    Private Sub OpzioneVolumeSu(sender As Object, e As EventArgs)
        CambiaVolume(10)
    End Sub

    Private Sub OpzioneVolumeGiu(sender As Object, e As EventArgs)
        CambiaVolume(-10)
    End Sub

    Private Sub OpzioneRiverberoSu(sender As Object, e As EventArgs)
        CambiaRiverbero(25)
    End Sub

    Private Sub OpzioneRiverberoGiu(sender As Object, e As EventArgs)
        CambiaRiverbero(-25)
    End Sub

    Private Sub OpzioneAiuto(sender As Object, e As EventArgs)
        AiutoMusica.Messaggio("Invio o barra spaziatrice avviano e fermano lo spartito. Freccia sinistra e freccia destra vanno alla battuta precedente e alla successiva. " &
                              "Freccia giù e freccia su, oppure pagina giù e pagina su, passano da uno strumento all'altro, da solo; dopo l'ultimo si tornano a sentire tutti insieme. " &
                              "Inizio ricomincia da capo. Alt apre le opzioni: strumenti, velocità, e per gli spartiti riconosciuti il controllo, le correzioni e il confronto con un'altra lettura; poi il salvataggio in mp3: parte con un beep e riascolta lo spartito dall'inizio, e quando lo spartito finisce il file mp3 viene salvato da solo; per salvare prima, scegli di nuovo l'opzione. Volume e riverbero si cambiano da Configurazione, nel menu Musica.")
    End Sub

    Private Sub OpzioneAltro(sender As Object, e As EventArgs)
        Try
            Chiudi()
            AiutoMusica.SuInterfaccia(Sub()
                                          Dim menu As New MenuSpartiti()
                                          menu.Avvia()
                                      End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.OpzioneAltro: " & ex.Message)
        End Try
    End Sub

    ' ---- controllo e correzione -------------------------------------------------------------------------

    Private Sub OpzioneControlla(sender As Object, e As EventArgs)
        Dim elenco As New ElencoProblemi(_revisione, AddressOf ApriBattuta)
        elenco.Avvia()
    End Sub

    Private Sub ScegliParte(titolo As String, alScelta As Action(Of Integer))
        If _revisione.Parti.Count = 1 Then
            alScelta(0)
            Return
        End If
        Dim menu As New WGMenu()
        For i As Integer = 0 To _revisione.Parti.Count - 1
            Dim indice As Integer = i
            menu.aggiungi((i + 1).ToString() & ". " & _revisione.Parti(i).Nome, Sub(s As Object, ev As EventArgs)
                                                                                     Try
                                                                                         menu.chiudi()
                                                                                     Catch
                                                                                     End Try
                                                                                     alScelta(indice)
                                                                                 End Sub)
        Next
        menu.avvia(titolo)
    End Sub

    Private _prima As String = ""

    Private Sub ApplicaStrumento(parte As Integer, nome As String, programma As Integer, trasposizione As Integer)
        _revisione.ImpostaStrumentoParte(parte, nome, programma)
        _revisione.ImpostaTrasposizione(parte, trasposizione)
        RicaricaDaRevisione()
    End Sub

    Private Sub OpzioneStrumentoParte(sender As Object, e As EventArgs)
        ScegliParte("Strumento", Sub(parte As Integer)
                                                                       Dim famiglie As String() = CatalogoSoundFont.NomiFamiglie()
                                                                       Dim menu As New WGMenu()
                                                                       For f As Integer = 0 To famiglie.Length - 1
                                                                           Dim famiglia As Integer = f
                                                                           menu.aggiungi(famiglie(f), Sub(s As Object, ev As EventArgs)
                                                                                                          Dim menuStrumenti As New WGMenu()
                                                                                                          For k As Integer = 0 To 7
                                                                                                              Dim programma As Integer = famiglia * 8 + k
                                                                                                              Dim nome As String = CatalogoSoundFont.NomeProgramma(programma)
                                                                                                              menuStrumenti.aggiungi(nome, Sub(s2 As Object, e2 As EventArgs)
                                                                                                                                              Try
                                                                                                                                                  menuStrumenti.chiudi()
                                                                                                                                                  menu.chiudi()
                                                                                                                                              Catch
                                                                                                                                              End Try
                                                                                                                                              ApplicaStrumento(parte, nome, programma, If(programma = 43, -12, 0))
                                                                                                                                              AiutoMusica.Annuncia(_revisione.Parti(parte).Nome & ": ora suona come " & nome)
                                                                                                                                          End Sub)
                                                                                                          Next
                                                                                                          menuStrumenti.avvia(famiglie(famiglia))
                                                                                                      End Sub)
                                                                       Next
                                                                       menu.avvia("Strumento di " & _revisione.Parti(parte).Nome)
                                                                   End Sub)
    End Sub

    Private Sub OpzioneOrganico(sender As Object, e As EventArgs)
        Dim menu As New WGMenu()
        For Each organico As String() In RevisioneMusicXml.Organici
            Dim scelto As String() = organico
            menu.aggiungi(scelto(0), Sub(s As Object, ev As EventArgs)
                                         Try
                                             menu.chiudi()
                                         Catch
                                         End Try
                                         If scelto.Length - 1 <> _revisione.Parti.Count Then
                                             AiutoMusica.Messaggio("Questo organico ha " & (scelto.Length - 1).ToString() & " strumenti, ma lo spartito ne ha " & _revisione.Parti.Count.ToString() & ". Assegna lo strumento a ognuno con l'altra opzione.")
                                             Return
                                         End If
                                         For i As Integer = 1 To scelto.Length - 1
                                             Dim campi As String() = scelto(i).Split("|"c)
                                             _revisione.ImpostaStrumentoParte(i - 1, campi(0), Integer.Parse(campi(1)))
                                             _revisione.ImpostaTrasposizione(i - 1, Integer.Parse(campi(2)))
                                         Next
                                         RicaricaDaRevisione()
                                         AiutoMusica.Messaggio("Strumenti assegnati, con i clarinetti in si bemolle, i corni in mi bemolle e i contrabbassi un'ottava sotto, come nelle sinfonie di Beethoven. Se nel tuo spartito sono in un'altra tonalità, cambia la trasposizione di quegli strumenti.")
                                     End Sub)
        Next
        menu.avvia("Organico")
    End Sub

    Private Shared ReadOnly NomiTempi() As String = {"Largo, 50 battiti al minuto", "Adagio, 70", "Andante, 90", "Moderato, 110", "Allegro, 130", "Allegro con brio, 160", "Vivace, 160", "Presto, 190", "Un altro numero di battiti al minuto"}
    Private Shared ReadOnly ValoriTempi() As Integer = {50, 70, 90, 110, 130, 160, 160, 190, 0}

    Private Sub ApplicaTempo(battiti As Integer)
        _revisione.ImpostaTempo(battiti)
        RicaricaDaRevisione()
        AiutoMusica.Annuncia("Tempo " & battiti.ToString() & " semiminime al minuto")
    End Sub

    Private Sub OpzioneTempo(sender As Object, e As EventArgs)
        Dim menu As New WGMenu()
        For i As Integer = 0 To NomiTempi.Length - 1
            Dim indice As Integer = i
            menu.aggiungi(NomiTempi(i), Sub(s As Object, ev As EventArgs)
                                            Try
                                                menu.chiudi()
                                            Catch
                                            End Try
                                            If ValoriTempi(indice) > 0 Then
                                                ApplicaTempo(ValoriTempi(indice))
                                            Else
                                                AiutoRevisione.ChiediTesto("Tempo", "120",
                                                    Sub(testo As String)
                                                        Dim n As Integer
                                                        If Not Integer.TryParse(testo.Trim(), n) OrElse n < 30 OrElse n > 300 Then
                                                            AiutoMusica.Messaggio("Scrivi un numero da trenta a trecento.")
                                                            Return
                                                        End If
                                                        ApplicaTempo(n)
                                                    End Sub)
                                            End If
                                        End Sub)
        Next
        menu.avvia("Tempo")
    End Sub

    ' ---- un'altra lettura dello stesso brano -----------------------------------------------------------------
    ' Un'altra edizione dello stesso brano, la partitura e le parti staccate, o il risultato di un altro programma: si legge anche
    ' quello e si confronta battuta per battuta con lo spartito aperto. Dove le due letture coincidono la battuta è sicura; dove
    ' una sola torna nel conto delle durate, si prende quella; il resto diventa un dubbio nell'elenco del controllo.

    Private _dialogoAltraLettura As WGFileDialog

    Private Sub OpzioneAltraLettura(sender As Object, e As EventArgs)
        Try
            _dialogoAltraLettura = New WGFileDialog()
            _dialogoAltraLettura.MultiselezioneConsentita = False
            For Each estensione As String In New String() {".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".mxl", ".musicxml", ".xml"}
                _dialogoAltraLettura.AggiungiEstensione(estensione)
            Next
            AddHandler _dialogoAltraLettura.selezioneterminata, AddressOf OnAltraLetturaScelta
            _dialogoAltraLettura.Avvia()
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.OpzioneAltraLettura: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnAltraLetturaScelta(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogoAltraLettura.selezioneterminata, AddressOf OnAltraLetturaScelta
            Catch
            End Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorso As String = elementi(0)
            If Not File.Exists(percorso) Then Return
            If RiconoscimentoSpartiti.EImmagineOPdf(percorso) Then
                Dim esistenti As List(Of String) = RiconoscimentoSpartiti.GiaRiconosciuto(percorso)
                If esistenti.Count > 0 Then
                    ScegliBranoDaConfrontare(esistenti)
                Else
                    ' il riconoscimento va in sottofondo; alla fine il confronto parte da solo
                    RiconoscimentoSpartiti.Riconosci(percorso, AddressOf ScegliBranoDaConfrontare, "Quando ha finito lo confronto da solo con lo spartito aperto.")
                End If
            Else
                ConfrontaCon(percorso)
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.OnAltraLetturaScelta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliBranoDaConfrontare(risultati As List(Of String))
        Try
            If _chiusa OrElse risultati Is Nothing OrElse risultati.Count = 0 Then Return
            If risultati.Count = 1 Then
                ConfrontaCon(risultati(0))
                Return
            End If
            Dim menu As New WGMenu()
            For i As Integer = 0 To risultati.Count - 1
                Dim file As String = risultati(i)
                menu.aggiungi("Brano " & (i + 1).ToString() & ": " & RiconoscimentoSpartiti.TitoloBrano(file), Sub(s As Object, ev As EventArgs) ConfrontaCon(file))
            Next
            menu.avvia("Brano")
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.ScegliBranoDaConfrontare: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfrontaCon(percorso As String)
        Try
            If _riproduttore IsNot Nothing AndAlso _riproduttore.InRiproduzione Then _riproduttore.Ferma()
            Dim istantanea As String = _revisione.Istantanea()
            Dim attesa As New WGProcessoInCorso()
            attesa.Avvia("Sto confrontando le due letture, attendere", False)
            Task.Run(Sub()
                         Dim esito As EsitoConfronto = Nothing
                         Dim errore As String = ""
                         Try
                             Dim altra As RevisioneMusicXml = RevisioneMusicXml.Apri(percorso)
                             esito = ConfrontoLetture.Applica(_revisione, altra, Nothing, RiconoscimentoSpartiti.LeggiVerifica(percorso).Penalita)
                         Catch ex As Exception
                             _log.ScriviLog("SessioneSpartito.ConfrontaCon: " & ex.Message & Environment.NewLine & ex.StackTrace)
                             errore = "Non è stato possibile leggere l'altro spartito: " & ex.Message
                         End Try
                         AiutoMusica.SuInterfaccia(Sub()
                                                       Try
                                                           attesa.Fine()
                                                       Catch
                                                       End Try
                                                       If errore <> "" Then
                                                           _revisione.Ripristina(istantanea)
                                                           AiutoMusica.Messaggio(errore)
                                                           Return
                                                       End If
                                                       If Not esito.Confrontabile Then
                                                           AiutoMusica.Messaggio(esito.Riassunto())
                                                           Return
                                                       End If
                                                       If esito.Prese > 0 Then
                                                           _prima = istantanea
                                                           If Not _chiusa Then RicaricaDaRevisione() Else _revisione.Salva(_percorsoCorretto, False)
                                                       End If
                                                       _revisione.DubbiEsterni.AddRange(esito.Dubbi)
                                                       RiconoscimentoSpartiti.AggiungiDubbiExtra(_percorsoCorretto, esito)
                                                       AiutoMusica.Messaggio(esito.Riassunto() & If(esito.Dubbie > 0, ". Apri Controlla lo spartito per sentirli uno per uno.", "."))
                                                   End Sub)
                     End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.ConfrontaCon: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAutomatica(sender As Object, e As EventArgs)
        Try
            If _riproduttore.InRiproduzione Then _riproduttore.Ferma()
            Dim istantanea As String = _revisione.Istantanea()
            Dim attesa As New WGProcessoInCorso()
            attesa.Avvia("Sto correggendo gli errori sicuri, attendere", False)
            Task.Run(Sub()
                         Dim fatte As List(Of ProblemaSpartito) = Nothing
                         Dim errore As String = ""
                         Try
                             fatte = _revisione.CorreggiAutomaticamente()
                         Catch ex As Exception
                             _log.ScriviLog("SessioneSpartito.OpzioneAutomatica: " & ex.Message & Environment.NewLine & ex.StackTrace)
                             errore = "Non è stato possibile correggere automaticamente: " & ex.Message
                         End Try
                         AiutoMusica.SuInterfaccia(Sub()
                                                       Try
                                                           attesa.Fine()
                                                       Catch
                                                       End Try
                                                       If errore <> "" Then
                                                           _revisione.Ripristina(istantanea)
                                                           AiutoMusica.Messaggio(errore)
                                                           Return
                                                       End If
                                                       If fatte.Count = 0 Then
                                                           AiutoMusica.Messaggio("Non c'erano correzioni sicure da fare. Con Controlla lo spartito puoi vedere i dubbi che restano e correggerli a mano.")
                                                           Return
                                                       End If
                                                       _prima = istantanea
                                                       RicaricaDaRevisione()
                                                       MostraCorrezioni(fatte)
                                                   End Sub)
                     End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.OpzioneAutomatica: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCorrezioni(fatte As List(Of ProblemaSpartito))
        Dim menu As New WGMenu()
        For Each c As ProblemaSpartito In fatte
            Dim scelta As ProblemaSpartito = c
            menu.aggiungi(scelta.Testo, Sub(s As Object, ev As EventArgs) ApriBattuta(scelta.Parte, scelta.Misura))
        Next
        menu.avvia(fatte.Count.ToString() & If(fatte.Count = 1, " correzione", " correzioni"))
    End Sub

    Private Sub OpzioneAnnullaAutomatica(sender As Object, e As EventArgs)
        Try
            If _prima = "" Then
                AiutoMusica.Messaggio("Non c'è nessuna correzione automatica da annullare.")
                Return
            End If
            If _riproduttore.InRiproduzione Then _riproduttore.Ferma()
            _revisione.Ripristina(_prima)
            _prima = ""
            RicaricaDaRevisione()
            AiutoMusica.Messaggio("Correzione automatica annullata.")
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.OpzioneAnnullaAutomatica: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneCorreggi(sender As Object, e As EventArgs)
        ScegliParte("Strumento", Sub(parte As Integer)
                                                       AiutoRevisione.ChiediTesto("Numero della battuta", _spartito.NumeroBattuta(_riproduttore.Posizione).ToString(),
                                                           Sub(testo As String)
                                                               Dim indice As Integer = _revisione.IndiceMisuraDaNumero(parte, testo.Trim())
                                                               If indice < 0 Then
                                                                   AiutoMusica.Messaggio("Non c'è nessuna battuta con questo numero.")
                                                                   Return
                                                               End If
                                                               ApriBattuta(parte, indice)
                                                           End Sub)
                                                   End Sub)
    End Sub

    Private Sub ApriBattuta(parte As Integer, misura As Integer)
        Try
            If _riproduttore.InRiproduzione Then _riproduttore.Ferma()
            Dim editor As New EditorBattuta(_revisione, parte, misura, AddressOf RicaricaDaRevisione, AddressOf SuonaBattuta)
            editor.Avvia()
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.ApriBattuta: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>Dopo ogni correzione lo spartito suonato si rifà dal file corretto, che si conserva per le volte successive.</summary>
    Private Sub RicaricaDaRevisione()
        Try
            _revisione.Salva(_percorsoCorretto, False)
            Dim nuovo As Spartito = Spartito.Carica(_percorsoCorretto)
            Dim parte As Integer = _riproduttore.Parte
            Dim velocita As Double = _riproduttore.Velocita
            _riproduttore.Ferma()
            RemoveHandler _riproduttore.Terminato, AddressOf OnTerminato
            RemoveHandler _riproduttore.IntervalloFinito, AddressOf OnIntervalloFinito
            _spartito = nuovo
            _strumenti = CostruisciStrumenti(nuovo)
            _riproduttore = New RiproduttoreSpartito(nuovo, _motore, _strumenti)
            _riproduttore.Velocita = velocita
            If parte < nuovo.Parti.Count Then _riproduttore.Parte = parte
            AddHandler _riproduttore.Terminato, AddressOf OnTerminato
            AddHandler _riproduttore.IntervalloFinito, AddressOf OnIntervalloFinito
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.RicaricaDaRevisione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aggiornare lo spartito dopo la correzione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Fa sentire una battuta sola, di una sola parte.</summary>
    Private Sub SuonaBattuta(parteXml As Integer, misura As Integer)
        Try
            Dim id As String = _revisione.Parti(parteXml).Id
            Dim parte As Integer = _spartito.Parti.FindIndex(Function(p) p.Id = id)
            If parte < 0 Then
                AiutoMusica.Messaggio("Questo strumento non ha note da suonare.")
                Return
            End If
            Dim posto As Integer = _spartito.OrdineMisure.IndexOf(misura)
            If posto < 0 OrElse posto >= _spartito.Battute.Count Then
                AiutoMusica.Messaggio("Non riesco a trovare questa battuta nello spartito.")
                Return
            End If
            Dim inizio As Double = _spartito.Battute(posto)
            Dim fine As Double = If(posto + 1 < _spartito.Battute.Count, _spartito.Battute(posto + 1), _spartito.Durata)
            If _parteDopoAscolto = -2 Then _parteDopoAscolto = _riproduttore.Parte
            _riproduttore.Parte = parte
            _riproduttore.Avvia(inizio, fine)
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.SuonaBattuta: " & ex.Message)
        End Try
    End Sub

    Private Sub OnIntervalloFinito(sender As Object, e As EventArgs)
        AiutoMusica.SuInterfaccia(Sub()
                                      If _chiusa OrElse _riproduttore Is Nothing Then Return
                                      If _parteDopoAscolto <> -2 Then
                                          _riproduttore.Parte = _parteDopoAscolto
                                          _parteDopoAscolto = -2
                                      End If
                                  End Sub)
    End Sub

    Private Shared ReadOnly NomiTrasposizioni() As String = {"Nessuna: suona come è scritto", "Clarinetto, tromba o sassofono soprano in si bemolle", "Clarinetto in la", "Corno in fa o corno inglese", "Sassofono contralto", "Sassofono tenore", "Sassofono baritono", "Ottavino, un'ottava più in alto", "Contrabbasso o chitarra, un'ottava più in basso", "Un altro numero di semitoni"}
    Private Shared ReadOnly SemitoniTrasposizioni() As Integer = {0, -2, -3, -7, -9, -14, -21, 12, -12, 0}

    Private Sub OpzioneTrasposizione(sender As Object, e As EventArgs)
        ScegliParte("Strumento", Sub(parte As Integer)
                                                             Dim menu As New WGMenu()
                                                             For i As Integer = 0 To NomiTrasposizioni.Length - 1
                                                                 Dim indice As Integer = i
                                                                 menu.aggiungi(NomiTrasposizioni(i), Sub(s As Object, ev As EventArgs)
                                                                                                         Try
                                                                                                             menu.chiudi()
                                                                                                         Catch
                                                                                                         End Try
                                                                                                         If indice = NomiTrasposizioni.Length - 1 Then
                                                                                                             AiutoRevisione.ChiediTesto("Semitoni", _revisione.TrasposizioneAttuale(parte).ToString(),
                                                                                                                 Sub(testo As String)
                                                                                                                     Dim n As Integer
                                                                                                                     If Not Integer.TryParse(testo.Trim(), n) OrElse Math.Abs(n) > 36 Then
                                                                                                                         AiutoMusica.Messaggio("Scrivi un numero di semitoni, da meno trentasei a trentasei.")
                                                                                                                         Return
                                                                                                                     End If
                                                                                                                     ApplicaTrasposizione(parte, n)
                                                                                                                 End Sub)
                                                                                                         Else
                                                                                                             ApplicaTrasposizione(parte, SemitoniTrasposizioni(indice))
                                                                                                         End If
                                                                                                     End Sub)
                                                             Next
                                                             menu.avvia("Trasposizione di " & _revisione.Parti(parte).Nome)
                                                         End Sub)
    End Sub

    Private Sub ApplicaTrasposizione(parte As Integer, semitoni As Integer)
        _revisione.ImpostaTrasposizione(parte, semitoni)
        RicaricaDaRevisione()
        AiutoMusica.Annuncia(If(semitoni = 0, "Lo strumento suona come è scritto", "Lo strumento suona di " & Math.Abs(semitoni).ToString() & If(Math.Abs(semitoni) = 1, " semitono ", " semitoni ") & If(semitoni > 0, "più in alto", "più in basso") & " di come è scritta"))
    End Sub

    Private Sub OpzioneEsporta(sender As Object, e As EventArgs)
        Esporta(False)
    End Sub

    Private Sub OpzioneEsportaCompresso(sender As Object, e As EventArgs)
        Esporta(True)
    End Sub

    Private Sub Esporta(compresso As Boolean)
        Try
            ' la cartella si sceglie ogni volta, partendo dalla scelta delle risorse
            _selettoreCartella = New SelettoreCartellaMusica()
            _selettoreCartella.Scegli(Sub(cartella As String)
                                          AiutoMusica.SuInterfaccia(Sub()
                                                                        Try
                                                                            Dim destinazione As String = AiutoMusica.PercorsoNuovo(cartella, _spartito.Titolo, If(compresso, ".mxl", ".musicxml"))
                                                                            _revisione.Salva(destinazione, compresso)
                                                                            AiutoMusica.Messaggio("Spartito esportato in MusicXML: " & destinazione)
                                                                        Catch exSalva As Exception
                                                                            _log.ScriviLog("SessioneSpartito.Esporta: " & exSalva.Message & Environment.NewLine & exSalva.StackTrace)
                                                                            AiutoMusica.Messaggio("Non è stato possibile esportare lo spartito: " & exSalva.Message)
                                                                        End Try
                                                                    End Sub)
                                      End Sub)
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.Esporta: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile esportare lo spartito: " & ex.Message)
        End Try
    End Sub

    ' ---- navigazione per battute ------------------------------------------------------------------------

    Private Sub VaiBattuta(delta As Integer)
        If _riproduttore Is Nothing Then Return
        Dim posizione As Double = _riproduttore.Posizione
        Dim attuale As Integer = _spartito.NumeroBattuta(posizione)
        Dim nuova As Integer = attuale + delta
        If delta < 0 AndAlso posizione - _spartito.InizioBattuta(attuale) > 0.6 Then nuova = attuale
        nuova = Math.Max(1, Math.Min(_spartito.Battute.Count, nuova))
        _riproduttore.Vai(_spartito.InizioBattuta(nuova))
        AiutoMusica.AnnunciaInSottofondo("Battuta " & nuova.ToString())
    End Sub

    ' ---- registrazione in mp3 ---------------------------------------------------------------------------

    Private Sub OpzioneSalva(sender As Object, e As EventArgs)
        Try
            If _registratore IsNot Nothing Then
                FermaERegistra()
                Return
            End If
            ScegliCartella(True)
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.OpzioneSalva: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile avviare la registrazione.")
        End Try
    End Sub

    Private Sub IniziaRegistrazione()
        ' un beep, senza parole: chi ascolta non deve essere distratto
        AiutoMusica.SegnaleInizio()
        _percorsoTemporaneo = Path.Combine(Path.GetTempPath(), "wg-spartito-" & Guid.NewGuid().ToString("N") & ".wav")
        _registratore = New RegistratoreFlusso(_percorsoTemporaneo)
        _riproduttore.Ferma()
        _motore.AvviaRegistrazione(_registratore)
        _riproduttore.Avvia(0.0)
    End Sub

    Private Sub FermaERegistra()
        If _registratore Is Nothing Then Return
        Dim registratore As RegistratoreFlusso = _registratore
        Dim temporaneo As String = _percorsoTemporaneo
        _registratore = Nothing
        _percorsoTemporaneo = ""
        _riproduttore.Ferma()
        _motore.FermaRegistrazione()
        Dim cartella As String = _cartellaRegistrazione
        If cartella = "" Then cartella = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        Dim nome As String = _spartito.Titolo
        If _riproduttore.Parte >= 0 Then nome &= " " & _spartito.Parti(_riproduttore.Parte).Nome
        AiutoMusica.SalvaRegistrazione(registratore, temporaneo, AiutoMusica.PercorsoNuovo(cartella, nome, ".mp3"))
    End Sub

    Private Sub OnTerminato(sender As Object, e As EventArgs)
        AiutoMusica.SuInterfaccia(Sub()
                                      If _chiusa Then Return
                                      If _registratore IsNot Nothing Then
                                          FermaERegistra()
                                      Else
                                          AiutoMusica.Annuncia("Lo spartito è finito")
                                      End If
                                  End Sub)
    End Sub

    Private Sub OpzioneCartella(sender As Object, e As EventArgs)
        ScegliCartella(False)
    End Sub

    Private _registraDopoCartella As Boolean
    ''' <summary>La cartella scelta per salvare la registrazione in mp3 di questa volta (non si ricorda per la prossima).</summary>
    Private _cartellaRegistrazione As String = ""

    Private Sub ScegliCartella(poiRegistra As Boolean)
        _registraDopoCartella = poiRegistra
        _selettoreCartella = New SelettoreCartellaMusica()
        _selettoreCartella.Scegli(Sub(cartella)
                                      AiutoMusica.SuInterfaccia(Sub()
                                                                    _cartellaRegistrazione = cartella
                                                                    If _registraDopoCartella AndAlso Not _chiusa AndAlso _registratore Is Nothing Then
                                                                        _registraDopoCartella = False
                                                                        IniziaRegistrazione()
                                                                    End If
                                                                End Sub)
                                  End Sub)
    End Sub

    ' ---- chiusura e tasti --------------------------------------------------------------------------------

    Public Sub Chiudi()
        If _chiusa Then Return
        _chiusa = True
        Try
            Try
                WGTastiera.GetInstance().ImpostaGestoreTastiMusicali(Nothing)
            Catch
            End Try
            If _timerFuoco IsNot Nothing Then _timerFuoco.Dispose()
            _timerFuoco = Nothing
            If _riproduttore IsNot Nothing Then
                RemoveHandler _riproduttore.Terminato, AddressOf OnTerminato
                RemoveHandler _riproduttore.IntervalloFinito, AddressOf OnIntervalloFinito
                _riproduttore.Ferma()
            End If
            If _registratore IsNot Nothing Then
                Dim registratore As RegistratoreFlusso = _registratore
                Dim temporaneo As String = _percorsoTemporaneo
                _registratore = Nothing
                Dim cartella As String = _cartellaRegistrazione
                If cartella = "" Then cartella = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                If _motore IsNot Nothing Then _motore.FermaRegistrazione()
                AiutoMusica.SalvaRegistrazione(registratore, temporaneo, AiutoMusica.PercorsoNuovo(cartella, _spartito.Titolo, ".mp3"))
            End If
            If _motore IsNot Nothing Then
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
                    _lista.abbandona()
                Catch ex As Exception
                    _log.ScriviLog("SessioneSpartito.Chiudi (lista): " & ex.Message)
                End Try
                _lista = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.Chiudi: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Chiudi()
    End Sub

    Private Sub ControllaFuoco(stato As Object)
        Try
            If _chiusa Then Return
            Dim lista As WGMidiRiproduzione = _lista
            If lista Is Nothing Then Return
            If lista.Chiusa Then AiutoMusica.SuInterfaccia(Sub() Chiudi())
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.ControllaFuoco: " & ex.Message)
        End Try
    End Sub

    Private Function Attiva() As Boolean
        Dim lista As WGMidiRiproduzione = _lista
        If _chiusa OrElse lista Is Nothing Then Return False
        Return lista.PuoRicevereInput() AndAlso Not lista.OpzioniAperte
    End Function

    Public Function GestisciTasto(vkCode As Integer, scanCode As Integer, premuto As Boolean, shift As Boolean, modificatori As Boolean) As Boolean Implements IGestoreTastiMusicali.GestisciTasto
        Try
            If _chiusa OrElse _riproduttore Is Nothing Then Return False
            If Not premuto OrElse modificatori Then Return False
            If Not Attiva() Then Return False
            Select Case vkCode
                Case VkSpazio
                    AvviaFerma()
                Case VkSinistra
                    VaiBattuta(-1)
                Case VkDestra
                    VaiBattuta(1)
                Case VkSu
                    If _spartito.Parti.Count > 1 Then OpzioneParteprecedente(Nothing, EventArgs.Empty)
                Case VkGiu
                    If _spartito.Parti.Count > 1 Then OpzioneParteSuccessiva(Nothing, EventArgs.Empty)
                Case VkPaginaGiu
                    OpzioneParteSuccessiva(Nothing, EventArgs.Empty)
                Case VkPaginaSu
                    OpzioneParteprecedente(Nothing, EventArgs.Empty)
                Case VkInizio
                    OpzioneInizio(Nothing, EventArgs.Empty)
                Case Else
                    Return False
            End Select
            Return True
        Catch ex As Exception
            _log.ScriviLog("SessioneSpartito.GestisciTasto: " & ex.Message)
            Return False
        End Try
    End Function

End Class

''' <summary>Il menu "Spartiti musicali": sceglie il file, lo fa riconoscere se è un PDF o una foto, poi lo suona.</summary>
Public Class MenuSpartiti

    Private ReadOnly _log As New Logger()
    Private _dialogo As WGFileDialog
    Private _banco As BancoSoundFont

    Public Sub Avvia()
        Try
            If GestoreSuoni.Disponibile() Then
                GestoreSuoni.Carica(Sub(banco As BancoSoundFont)
                                        If banco Is Nothing Then Return
                                        _banco = banco
                                        ApriRadice()
                                    End Sub)
            Else
                Dim menu As New WGMenu()
                menu.aggiungi("Scarica i suoni ad alta qualità, necessari per suonare gli spartiti", Sub(s As Object, e As EventArgs) GestoreSuoni.Scarica(Sub(riuscito As Boolean)
                                                                                                                                                              If riuscito Then Avvia()
                                                                                                                                                          End Sub))
                menu.aggiungi("Scegli un file di suoni SoundFont", Sub(s As Object, e As EventArgs) GestoreSuoni.ScegliFile(Sub(riuscito As Boolean)
                                                                                                                                  If riuscito Then Avvia()
                                                                                                                              End Sub))
                menu.avvia("Spartiti musicali")
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile aprire gli spartiti musicali.")
        End Try
    End Sub

    Private Sub ApriRadice()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Apri uno spartito", AddressOf ScegliSpartito)
            menu.aggiungi("Cerca negli archivi", AddressOf CercaArchivi)
            menu.aggiungi("Spartiti scaricati", AddressOf ElencaScaricati)
            menu.aggiungi("Spartiti riconosciuti", AddressOf ElencaRiconosciuti)
            If Not OrchestraSonatina.Installata() Then
                menu.aggiungi("Scarica l'orchestra, strumenti campionati da un'orchestra vera, circa cinquecento megabyte", AddressOf ScaricaOrchestra)
            End If
            menu.avvia("Spartiti musicali")
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.ApriRadice: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ElencaScaricati(sender As Object, e As EventArgs)
        Try
            Dim file As List(Of String) = RiconoscimentoSpartiti.ElencoScaricati()
            If file.Count = 0 Then
                AiutoMusica.Messaggio("Non hai ancora scaricato nessuno spartito.")
                Return
            End If
            Dim menu As New WGMenu()
            For Each f As String In file
                Dim percorso As String = f
                Dim estensione As String = Path.GetExtension(f).TrimStart("."c).ToUpperInvariant()
                menu.aggiungi(RiconoscimentoSpartiti.NomeLeggibile(f) & ", " & estensione, Sub(s As Object, ev As EventArgs) ApriPercorso(percorso))
            Next
            menu.avvia("Scaricati")
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.ElencaScaricati: " & ex.Message)
        End Try
    End Sub

    Private Sub ElencaRiconosciuti(sender As Object, e As EventArgs)
        Try
            Dim riconosciuti As List(Of KeyValuePair(Of String, List(Of String))) = RiconoscimentoSpartiti.ElencoRiconosciuti()
            If riconosciuti.Count = 0 Then
                AiutoMusica.Messaggio("Non hai ancora riconosciuto nessuno spartito.")
                Return
            End If
            Dim menu As New WGMenu()
            For Each r As KeyValuePair(Of String, List(Of String)) In riconosciuti
                Dim file As List(Of String) = r.Value
                menu.aggiungi(r.Key, Sub(s As Object, ev As EventArgs) ScegliMovimento(file))
            Next
            menu.avvia("Riconosciuti")
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.ElencaRiconosciuti: " & ex.Message)
        End Try
    End Sub

    Private Sub ScaricaOrchestra(sender As Object, e As EventArgs)
        OrchestraSonatina.Scarica(Sub(riuscito As Boolean)
                                      If riuscito Then AiutoMusica.Messaggio("Orchestra scaricata: gli spartiti ora suonano con gli strumenti dell'orchestra.")
                                  End Sub)
    End Sub

    Private Sub ScegliSpartito(sender As Object, e As EventArgs)
        Try
            _dialogo = New WGFileDialog()
            _dialogo.MultiselezioneConsentita = False
            For Each estensione As String In New String() {".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".mxl", ".musicxml", ".xml", ".mid", ".midi"}
                _dialogo.AggiungiEstensione(estensione)
            Next
            AddHandler _dialogo.selezioneterminata, AddressOf OnSpartitoScelto
            _dialogo.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.ScegliSpartito: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnSpartitoScelto(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogo.selezioneterminata, AddressOf OnSpartitoScelto
            Catch
            End Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim percorso As String = elementi(0)
            If Not File.Exists(percorso) Then Return
            ApriPercorso(percorso)
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.OnSpartitoScelto: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>Un PDF o una foto si legge, un MusicXML o un MIDI si suona.</summary>
    Private Sub ApriPercorso(percorso As String)
        If RiconoscimentoSpartiti.EImmagineOPdf(percorso) Then
            Dim esistenti As List(Of String) = RiconoscimentoSpartiti.GiaRiconosciuto(percorso)
            If esistenti.Count > 0 Then
                ScegliMovimento(esistenti)
            Else
                ' il riconoscimento può durare anche dieci minuti: va in sottofondo e alla fine si chiede se ascoltare
                RiconoscimentoSpartiti.Riconosci(percorso, Sub(risultati As List(Of String))
                                                               WGDomanda.Chiedi("Lo spartito è stato riconosciuto. Vuoi ascoltarlo?", Sub() ScegliMovimento(risultati))
                                                           End Sub)
            End If
        Else
            ApriFile(percorso)
        End If
    End Sub

    ' ---- la ricerca negli archivi ----------------------------------------------------------------------------

    Private Sub CercaArchivi(sender As Object, e As EventArgs)
        AiutoRevisione.ChiediTesto("Cerca", "", Sub(testo As String)
                                                    If String.IsNullOrWhiteSpace(testo) Then Return
                                                    EseguiRicerca(testo.Trim())
                                                End Sub)
    End Sub

    Private _ultimaRicerca As String = ""

    Private Sub EseguiRicerca(testo As String)
        _ultimaRicerca = testo
        Dim attesa As New WGProcessoInCorso()
        Dim annullamento As New CancellationTokenSource()
        AddHandler attesa.cancella, Sub(s As Object, ev As EventArgs) annullamento.Cancel()
        attesa.Avvia("Sto cercando negli archivi, attendere", True)
        Task.Run(Sub()
                     Dim risultati As New List(Of RisultatoArchivio)()
                     Dim errore As String = ""
                     Try
                         risultati = ArchiviSpartiti.Cerca(testo, annullamento.Token)
                     Catch ex As OperationCanceledException
                         errore = ""
                     Catch ex As Exception
                         _log.ScriviLog("MenuSpartiti.EseguiRicerca: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         errore = "Non è stato possibile cercare negli archivi: " & ex.Message
                     End Try
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If annullamento.IsCancellationRequested Then Return
                                                   If errore <> "" Then
                                                       AiutoMusica.Messaggio(errore)
                                                   ElseIf risultati.Count = 0 Then
                                                       AiutoMusica.Messaggio("Non ho trovato niente per " & testo & ".")
                                                   Else
                                                       MostraRisultati(risultati)
                                                   End If
                                               End Sub)
                 End Sub)
    End Sub

    ''' <summary>
    ''' I PDF sono meno affidabili (vanno riconosciuti): se ci sono risultati MusicXML o MIDI si mostrano solo quelli,
    ''' e i PDF si aggiungono dalle opzioni, con Alt. Se ci sono solo PDF si mostrano subito.
    ''' </summary>
    Private Sub MostraRisultati(risultati As List(Of RisultatoArchivio), Optional conPdf As Boolean = False)
        Try
            Dim senzaPdf As List(Of RisultatoArchivio) = risultati.Where(Function(x) x.Formato <> "PDF").ToList()
            Dim pdfNascosti As Boolean = Not conPdf AndAlso senzaPdf.Count > 0 AndAlso senzaPdf.Count < risultati.Count
            Dim daMostrare As List(Of RisultatoArchivio) = If(pdfNascosti, senzaPdf, risultati)
            Dim menu As New WGMenu()
            For Each r As RisultatoArchivio In daMostrare
                Dim scelto As RisultatoArchivio = r
                menu.aggiungi(scelto.Etichetta(), Sub(s As Object, ev As EventArgs) ScegliRisultato(scelto))
            Next
            If pdfNascosti Then
                menu.aggiungiOpzione("Mostra formati PDF", Sub(s As Object, ev As EventArgs)
                                                               menu.chiudi()
                                                               MostraRisultati(risultati, True)
                                                           End Sub)
            End If
            menu.avvia("Risultati")
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.MostraRisultati: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliRisultato(r As RisultatoArchivio)
        If r.Elemento <> "" Then
            ElencaFileDelElemento(r)
        Else
            ScaricaEApri(r)
        End If
    End Sub

    Private Sub ElencaFileDelElemento(elemento As RisultatoArchivio)
        Dim attesa As New WGProcessoInCorso()
        Dim annullamento As New CancellationTokenSource()
        AddHandler attesa.cancella, Sub(s As Object, ev As EventArgs) annullamento.Cancel()
        attesa.Avvia("Sto cercando i file, attendere", True)
        Task.Run(Sub()
                     Dim file As New List(Of RisultatoArchivio)()
                     Dim errore As String = ""
                     Try
                         file = ArchiviSpartiti.FileDelElemento(elemento, annullamento.Token, _ultimaRicerca)
                     Catch ex As OperationCanceledException
                         errore = ""
                     Catch ex As Exception
                         _log.ScriviLog("MenuSpartiti.ElencaFileDelElemento: " & ex.Message)
                         errore = "Non è stato possibile leggere l'elenco dei file: " & ex.Message
                     End Try
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If annullamento.IsCancellationRequested Then Return
                                                   If errore <> "" Then
                                                       AiutoMusica.Messaggio(errore)
                                                   ElseIf file.Count = 0 Then
                                                       AiutoMusica.Messaggio("Non ci sono file MIDI in questo elemento.")
                                                   ElseIf file.Count = 1 Then
                                                       ScaricaEApri(file(0))
                                                   Else
                                                       Dim menu As New WGMenu()
                                                       For Each f As RisultatoArchivio In file
                                                           Dim scelto As RisultatoArchivio = f
                                                           menu.aggiungi(scelto.Titolo, Sub(s As Object, ev As EventArgs) ScaricaEApri(scelto))
                                                       Next
                                                       menu.avvia("File")
                                                   End If
                                               End Sub)
                 End Sub)
    End Sub

    Private Sub ScaricaEApri(r As RisultatoArchivio)
        Dim attesa As New WGProcessoInCorso()
        Dim annullamento As New CancellationTokenSource()
        AddHandler attesa.cancella, Sub(s As Object, ev As EventArgs) annullamento.Cancel()
        attesa.Avvia("Sto scaricando il file, attendere", True)
        Task.Run(Sub()
                     Dim percorso As String = ""
                     Dim errore As String = ""
                     Try
                         percorso = ArchiviSpartiti.Scarica(r, annullamento.Token)
                     Catch ex As OperationCanceledException
                         errore = ""
                     Catch ex As Exception
                         _log.ScriviLog("MenuSpartiti.ScaricaEApri: " & ex.Message)
                         errore = "Non è stato possibile scaricare il file: " & ex.Message
                     End Try
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If annullamento.IsCancellationRequested Then Return
                                                   If errore <> "" Then
                                                       AiutoMusica.Messaggio(errore)
                                                   ElseIf percorso <> "" Then
                                                       ApriPercorso(percorso)
                                                   End If
                                               End Sub)
                 End Sub)
    End Sub

    ''' <summary>Un PDF può contenere più brani: se il riconoscimento ne ha trovati più d'uno si sceglie quale suonare.</summary>
    Private Sub ScegliMovimento(risultati As List(Of String))
        Try
            If risultati Is Nothing OrElse risultati.Count = 0 Then Return
            If risultati.Count = 1 Then
                ApriFile(risultati(0))
                Return
            End If
            Dim menu As New WGMenu()
            For i As Integer = 0 To risultati.Count - 1
                Dim file As String = risultati(i)
                menu.aggiungi("Spartito " & (i + 1).ToString() & ": " & RiconoscimentoSpartiti.TitoloBrano(file), Sub(s As Object, ev As EventArgs) AzioniSpartito(file))
            Next
            menu.aggiungiOpzione("Salva tutti gli spartiti in file MusicXML separati", Sub(s As Object, ev As EventArgs) SalvaMusicXml(risultati))
            menu.avvia("Spartiti riconosciuti")
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.ScegliMovimento: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Cosa fare con uno degli spartiti riconosciuti: ascoltarlo o salvarlo in un file MusicXML.</summary>
    Private Sub AzioniSpartito(file As String)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Apri e ascolta lo spartito", Sub(s As Object, ev As EventArgs) ApriFile(file))
            menu.aggiungi("Salva in un file MusicXML", Sub(s As Object, ev As EventArgs) SalvaMusicXml(New List(Of String) From {file}))
            menu.avvia(RiconoscimentoSpartiti.TitoloBrano(file))
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.AzioniSpartito: " & ex.Message)
        End Try
    End Sub

    Private _selettoreSalvataggio As SelettoreCartellaMusica

    ''' <summary>Salva gli spartiti indicati ognuno nel suo file MusicXML, nella cartella che si sceglie ogni volta.</summary>
    Private Sub SalvaMusicXml(file As List(Of String))
        Try
            _selettoreSalvataggio = New SelettoreCartellaMusica()
            _selettoreSalvataggio.Scegli(Sub(scelta As String) AiutoMusica.SuInterfaccia(Sub() EseguiSalvataggioMusicXml(file, scelta)))
        Catch ex As Exception
            _log.ScriviLog("MenuSpartiti.SalvaMusicXml: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EseguiSalvataggioMusicXml(file As List(Of String), cartella As String)
        Dim attesa As New WGProcessoInCorso()
        attesa.Avvia("Sto salvando, attendere", False)
        Task.Run(Sub()
                     Dim salvati As Integer = 0
                     Dim errore As String = ""
                     Dim ultimo As String = ""
                     For Each f As String In file
                         Dim destinazione As String = AiutoMusica.PercorsoNuovo(cartella, RiconoscimentoSpartiti.TitoloBrano(f), ".mxl")
                         Dim esito As String = RiconoscimentoSpartiti.SalvaMusicXml(f, destinazione)
                         If esito = "" Then
                             salvati += 1
                             ultimo = destinazione
                         Else
                             errore = esito
                         End If
                     Next
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If salvati = 0 Then
                                                       AiutoMusica.Messaggio(If(errore <> "", errore, "Non è stato salvato niente."))
                                                   ElseIf salvati = 1 Then
                                                       AiutoMusica.Messaggio("Spartito salvato in " & ultimo & ".")
                                                   Else
                                                       AiutoMusica.Messaggio("Ho salvato " & salvati.ToString() & " spartiti in file MusicXML separati, nella cartella " & cartella & "." & If(errore <> "", " Qualcuno non è stato salvato.", ""))
                                                   End If
                                               End Sub)
                 End Sub)
    End Sub

    Private Sub ApriFile(percorso As String)
        Dim attesa As New WGProcessoInCorso()
        attesa.Avvia("Sto leggendo lo spartito, attendere", False)
        Task.Run(Sub()
                     Dim spartito As Spartito = Nothing
                     Dim revisione As RevisioneMusicXml = Nothing
                     Dim corretto As String = RiconoscimentoSpartiti.PercorsoCorretto(percorso)
                     Dim versioneCorretta As Boolean = False
                     Dim avviso As String = ""
                     Dim confronto As String = ""
                     Dim riparazioni As String = ""
                     Dim lettura As String = ""
                     Dim errore As String = ""
                     Try
                         Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
                         If estensione = ".mxl" OrElse estensione = ".musicxml" OrElse estensione = ".xml" Then
                             Dim sorgente As String = percorso
                             If File.Exists(corretto) Then
                                 sorgente = corretto
                                 versioneCorretta = True
                             End If
                             revisione = RevisioneMusicXml.Apri(sorgente)
                             ' le correzioni certe che vengono dalla lettura dei caratteri del PDF (codine, altezze, battute vuote)
                             Dim glifi As RisultatoGlifi = RiconoscimentoSpartiti.LeggiGlifi(percorso)
                             If Not versioneCorretta AndAlso glifi.Correzioni.Count > 0 Then
                                 Dim dalPdf As List(Of ProblemaSpartito) = revisione.ApplicaCorrezioniGlifi(glifi.Correzioni)
                                 If dalPdf.Count > 0 Then
                                     revisione.Salva(corretto, False)
                                     sorgente = corretto
                                     lettura = "Leggendo i caratteri del PDF ho fatto da solo " & dalPdf.Count.ToString() & If(dalPdf.Count = 1, " correzione sicura", " correzioni sicure") & ", tra note ricostruite, codine e altezze"
                                 End If
                             End If
                             revisione.DubbiEsterni.AddRange(glifi.Dubbi)
                             revisione.MisureVerificate.UnionWith(glifi.Verificate)
                             ' poi le riparazioni certe, che si ricavano dai valori scritti nello spartito
                             If Not versioneCorretta Then
                                 Dim riparate As List(Of ProblemaSpartito) = revisione.RiparaDaiTipi()
                                 If riparate.Count > 0 Then
                                     revisione.Salva(corretto, False)
                                     sorgente = corretto
                                     riparazioni = "Ho fatto da solo " & riparate.Count.ToString() & If(riparate.Count = 1, " riparazione sicura", " riparazioni sicure") & " del ritmo, leggendo i valori scritti delle note"
                                 End If
                             End If
                             ' una scansione è stata letta due volte: si confrontano le due letture battuta per battuta
                             If Not versioneCorretta Then
                                 Dim secondaFile As String = RiconoscimentoSpartiti.SecondaLettura(percorso)
                                 If secondaFile <> "" Then
                                     Try
                                         Dim altra As RevisioneMusicXml = RevisioneMusicXml.Apri(secondaFile)
                                         altra.ApplicaCorrezioniGlifi(RiconoscimentoSpartiti.LeggiGlifi(secondaFile).Correzioni)
                                         altra.RiparaDaiTipi()
                                         Dim esito As EsitoConfronto = ConfrontoLetture.Applica(revisione, altra, RiconoscimentoSpartiti.LeggiVerifica(percorso).Penalita, RiconoscimentoSpartiti.LeggiVerifica(secondaFile).Penalita)
                                         confronto = esito.Riassunto()
                                         RiconoscimentoSpartiti.ScriviConfronto(percorso, esito)
                                         If esito.Prese > 0 Then
                                             revisione.Salva(corretto, False)
                                             sorgente = corretto
                                         End If
                                     Catch ex As Exception
                                         _log.ScriviLog("MenuSpartiti.ApriFile (confronto): " & ex.Message & Environment.NewLine & ex.StackTrace)
                                     End Try
                                 End If
                             End If
                             ' i dubbi: dal confronto con l'immagine della pagina e dal confronto fra le due letture
                             Dim verificaImmagine As RisultatoVerifica = RiconoscimentoSpartiti.LeggiVerifica(percorso)
                             Dim verificaConfronto As RisultatoVerifica = RiconoscimentoSpartiti.LeggiConfronto(percorso)
                             Dim verificaExtra As RisultatoVerifica = RiconoscimentoSpartiti.LeggiDubbiExtra(corretto)
                             For Each d As DubbioMisura In verificaImmagine.Dubbi
                                 Dim k As Long = RisultatoVerifica.Chiave(d.Parte, d.Misura)
                                 If Not verificaConfronto.Sostituite.Contains(k) AndAlso Not verificaExtra.Sostituite.Contains(k) Then revisione.DubbiEsterni.Add(d)
                             Next
                             revisione.DubbiEsterni.AddRange(verificaConfronto.Dubbi)
                             revisione.DubbiEsterni.AddRange(verificaExtra.Dubbi)
                             If Not versioneCorretta AndAlso revisione.ApplicaTrasposizioniDaiNomi() Then
                                 revisione.Salva(corretto, False)
                                 sorgente = corretto
                             End If
                             If Not versioneCorretta AndAlso revisione.TutteGeneriche() Then
                                 Dim fileNomi As String = Path.Combine(Path.GetDirectoryName(sorgente), "nomi-parti.txt")
                                 If File.Exists(fileNomi) Then
                                     Dim nomi As String() = File.ReadAllLines(fileNomi, Encoding.UTF8).Where(Function(x) x.Trim() <> "").ToArray()
                                     If nomi.Length = revisione.Parti.Count Then
                                         For i As Integer = 0 To nomi.Length - 1
                                             revisione.ImpostaStrumentoParte(i, nomi(i).Trim(), Math.Max(0, Spartito.ProgrammaDalNomeNoto(nomi(i))))
                                             revisione.ImpostaTrasposizione(i, Spartito.TrasposizioneDalNome(nomi(i)))
                                         Next
                                         revisione.Salva(corretto, False)
                                         sorgente = corretto
                                         avviso = "Gli strumenti sono stati letti dal testo del PDF"
                                     End If
                                 End If
                             End If
                             ' il titolo scritto in grande nel PDF, se l'acquisizione non ne ha trovato uno
                             If Not versioneCorretta AndAlso revisione.ImpostaTitoloSeMancante(If(File.Exists(Path.ChangeExtension(percorso, ".titolo.txt")), RiconoscimentoSpartiti.TitoloBrano(percorso), "")) Then
                                 revisione.Salva(corretto, False)
                                 sorgente = corretto
                             End If
                             Dim diteggiature As Integer = revisione.NumeroDiteggiature()
                             If diteggiature > 0 Then lettura &= If(lettura <> "", ". ", "") & "Nello spartito ci sono " & diteggiature.ToString() & If(diteggiature = 1, " indicazione di diteggiatura", " indicazioni di diteggiatura")
                             ' l'indicazione di tempo, se lo spartito non dice a che velocità va: dal PDF o dalle parole scritte sopra le prime battute
                             If Not versioneCorretta AndAlso Not revisione.TempoDichiarato() Then
                                 Dim battiti As Integer = RiconoscimentoSpartiti.TempoLetto(percorso)
                                 If battiti <= 0 Then battiti = RiconoscimentoSpartiti.TempoDaTesto(revisione.TestoIndicazioniTempo())
                                 If battiti > 0 Then
                                     revisione.ImpostaTempo(battiti)
                                     revisione.Salva(corretto, False)
                                     sorgente = corretto
                                     lettura &= If(lettura <> "", ". ", "") & "Ho letto l'indicazione di tempo: " & battiti.ToString() & " battiti al minuto"
                                 End If
                             End If
                             If Not versioneCorretta AndAlso avviso = "" AndAlso revisione.TutteGeneriche() Then
                                 Dim descrizione As String = ""
                                 If revisione.AssegnaAutomaticamente(descrizione) Then
                                     revisione.Salva(corretto, False)
                                     sorgente = corretto
                                     avviso = descrizione
                                 End If
                             End If
                             spartito = Spartito.Carica(sorgente)
                         Else
                             spartito = Spartito.Carica(percorso)
                         End If
                     Catch ex As Exception
                         _log.ScriviLog("MenuSpartiti.ApriFile: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         errore = "Non è stato possibile leggere lo spartito: " & ex.Message
                     End Try
                     AiutoMusica.SuInterfaccia(Sub()
                                                   Try
                                                       attesa.Fine()
                                                   Catch
                                                   End Try
                                                   If errore <> "" Then
                                                       AiutoMusica.Messaggio(errore)
                                                       Return
                                                   End If
                                                   Dim sessione As SessioneSpartito = If(revisione IsNot Nothing, New SessioneSpartito(spartito, _banco, revisione, corretto, versioneCorretta), New SessioneSpartito(spartito, _banco))
                                                   Dim frasi As New List(Of String)()
                                                   For Each f As String In New String() {avviso, lettura, riparazioni, confronto}
                                                       If f <> "" Then frasi.Add(f)
                                                   Next
                                                   sessione.Avviso = String.Join(". ", frasi)
                                                   sessione.Avvia()
                                               End Sub)
                 End Sub)
    End Sub

End Class
