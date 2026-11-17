Imports System
Imports System.Collections.Generic
Imports System.Linq

' ============================================================================================
' Il mondo di Porto Franco: il giocatore (a piedi o su un veicolo), il traffico, la polizia, il navigatore, l'ora del giorno e il tempo,
' i semafori, i pedoni, e i suoni che li fanno sentire. Il ciclo del gioco chiama Passo una trentina di volte al secondo con i tasti
' tenuti premuti; i comandi singoli (svoltare, cambiare corsia, salire, chiedere dove si è) sono metodi a parte. Quello che va detto a voce
' esce con l'evento Annuncio; i fatti che commenta la radio con l'evento Notizia; i suoni vanno al MotoreAudio.
' Tutto si muove lungo le strade: a un incrocio si sceglie dove svoltare (o lo sceglie il navigatore, con la guida assistita).
' Ogni strada ha due corsie, una per senso: si può passare nell'altra per sorpassare, rischiando chi arriva di fronte.
' ============================================================================================

Public Enum RuoloVeicolo
    Parcheggiato
    Traffico
    Polizia
    Giocatore
    Bersaglio
    Relitto
    ''' <summary>Una macchina di una banda: insegue la sua preda (o il giocatore) e la sperona.</summary>
    Banda
End Enum

Public NotInheritable Class Veicolo
    Public Tipo As TipoVeicolo
    Public Pos As Posizione
    ''' <summary>Metri al secondo (negativa in retromarcia).</summary>
    Public Velocita As Double
    Public Salute As Double = 100
    Public Ruolo As RuoloVeicolo = RuoloVeicolo.Parcheggiato
    ''' <summary>È del giocatore: prenderlo non è un furto e ha le migliorie comprate.</summary>
    Public Tuo As Boolean
    ''' <summary>Per chi guida da solo: gli incroci da attraversare.</summary>
    Public Percorso As New List(Of Incrocio)()
    Public ProssimoCalcolo As Double
    Public VelocitaVoluta As Double
    Public FermoFino As Double
    ''' <summary>Una volante ferma a un posto di blocco.</summary>
    Public Blocco As Boolean
    Public Sirena As VoceSirena
    ''' <summary>Per le missioni e i lavori: dove deve andare (segui, corse) o da chi scappa (insegui).</summary>
    Public Meta As Luogo
    Public InFuga As Boolean
    Public UltimoUrto As Double = -10
    Public UltimaDistanza As Double = -1
    ''' <summary>Per le macchine delle bande: chi inseguono (Nothing: il giocatore).</summary>
    Public Preda As Veicolo
    ''' <summary>Quanta benzina c'è, da 0 a 100.</summary>
    Public Benzina As Double = 100
    ''' <summary>La merce a bordo: id della merce, quante unità.</summary>
    Public ReadOnly Carico As New Dictionary(Of String, Integer)()

    Public ReadOnly Property X As Double
        Get
            Return Pos.X
        End Get
    End Property

    Public ReadOnly Property Y As Double
        Get
            Return Pos.Y
        End Get
    End Property
End Class

''' <summary>I tasti tenuti premuti in questo momento.</summary>
Public Structure Comandi
    Public Su As Boolean
    Public Giu As Boolean
    Public Corsa As Boolean
End Structure

''' <summary>Un pedone che attraversa davanti a un incrocio.</summary>
Friend NotInheritable Class Attraversamento
    Public Incrocio As Incrocio
    Public Tratto As Tratto
    Public Fine As Double
End Class

Public NotInheritable Class Mondo
    Public ReadOnly Dati As DatiCitta
    Public ReadOnly Audio As MotoreAudio
    Public ReadOnly Partita As Salvataggio
    Public ReadOnly Imp As ImpostazioniCitta
    Public ReadOnly Veicoli As New List(Of Veicolo)()
    Public Missioni As EsecutoreMissione
    Public Lavori As LavoriCitta
    Public Eventi As EventiCitta
    ''' <summary>Vero durante la scuola guida: niente traffico, polizia, eventi.</summary>
    Public Scuola As Boolean
    Public Guida As ScuolaGuida
    Public Radio As RadioCitta
    Public Vita As VitaCitta
    Public Progressi As ProgressiCitta
    Public Sfide As SfideCitta
    Public Economia As EconomiaCitta
    ''' <summary>I furti di soldi, la rivendita e gli ordini di auto.</summary>
    Public Furti As FurtiCitta
    ''' <summary>L'inseguimento in barca in corso (Nothing se si è a terra).</summary>
    Public Barca As InseguimentoBarca
    ''' <summary>A piedi: vero mentre si corre (per le missioni furtive).</summary>
    Public StaCorrendo As Boolean
    ''' <summary>A piedi: vero se si è fermi.</summary>
    Public FermoAPiedi As Boolean = True

    ''' <summary>Secondi di gioco passati da quando si è aperta la partita.</summary>
    Public Tempo As Double
    Public APiedi As Boolean = True
    ''' <summary>Dove si trova il giocatore a piedi (in macchina conta la posizione del veicolo).</summary>
    Public PosPiedi As Posizione
    Public Veicolo As Veicolo
    ''' <summary>Livello di ricercato, da 0 a 5 stelle.</summary>
    Public Stelle As Integer
    ''' <summary>La svolta scelta per il prossimo incrocio: -1 sinistra, 0 dritto, 1 destra, 2 indietro; Nothing = nessuna scelta.</summary>
    Public Svolta As Integer? = Nothing
    ''' <summary>In macchina: 0 nella propria corsia, 1 nella corsia opposta (sorpasso).</summary>
    Public Corsia As Integer
    ''' <summary>La meta del navigatore (Nothing: nessuna).</summary>
    Public Meta As Luogo
    Public Pioggia As Boolean
    Private _percorso As New List(Of Incrocio)()
    Private _ricalcolo As Double
    Private _prossimoBip As Double
    ''' <summary>L'incrocio di cui il navigatore sta guidando la svolta, quali annunci sono già stati fatti (1 lontano, 2 vicino) e il prossimo tic del conto alla rovescia.</summary>
    Private _navIncrocio As Incrocio
    Private _navFasi As Integer
    Private _prossimoTicSvolta As Double
    Private _annunciatoTratto As Tratto
    Private _annunciatoVerso As Integer
    ''' <summary>Il gas che sta dando il giocatore (0 o 1), per il suono del motore.</summary>
    Private _gasGiocatore As Single
    Private _ultimaStrada As Strada
    Private _ultimoQuartiere As Quartiere
    Private _nonVisto As Double
    Private _fermoVicinoPolizia As Double
    Private _prossimoPasso As Double
    Private _avvisoIncrocioPiedi As Boolean
    Private _prossimoMeteo As Double
    Private _ultimaOra As Integer = -1
    Private _ultimoGiorno As Integer = -1
    Private _prossimoSensore As Double
    Private _prossimoFreno As Double
    Private _prossimaProssimita As Double
    Private ReadOnly _distProssimita As New Dictionary(Of Veicolo, Double)()
    Private _ultimoIncrocio As Incrocio
    Private _trattoArrivoIncrocio As Tratto
    Private _angoloArrivoIncrocio As Double
    Private _tempoIncrocio As Double
    Private _metriIncrocio As Double
    Private _arrivoLuogo As Luogo
    Private _arrivoSnap As Boolean
    Private _prossimoTicSemaforo As Double
    Private _rossoAnnunciato As Incrocio
    Private _pedoneDeciso As Tratto
    Private ReadOnly _attraversamenti As New List(Of Attraversamento)()
    Private _elicottero As VoceElicottero
    Private _bloccoMesso As Boolean
    Private ReadOnly _caso As New Random()
    Private _curvaAnnunciata As Tratto
    Private _distanzaPrima As Double = -1
    Private _trattoPrima As Tratto
    Private ReadOnly _radar As New Queue(Of (Quando As Double, Voce As VoceAudio, Azimut As Single, Volume As Single))()
    Private _prossimoLuccichio As Double
    Private _inseguitoDa4 As Boolean
    Private _abitacolo As VoceRegistrata

    ' i suoni che durano
    Private ReadOnly _motore As New VoceMotore()
    Private ReadOnly _ambiente As New VoceAmbiente()
    Private ReadOnly _pioggia As New VocePioggia()
    Private ReadOnly _motoriTraffico As New List(Of VoceMotore)()

    Public Event Annuncio(testo As String, urgente As Boolean)
    ''' <summary>Una battuta detta da una voce del gioco (passanti, telefonate): non si legge, ma va tra gli annunci da rileggere.</summary>
    Public Event Sentito(testo As String)
    ''' <summary>Un fatto che la radio può commentare: inseguimento, missione, arresto, incidente, corsa, pioggia, notte, mattina.</summary>
    Public Event Notizia(tipo As String, titolo As String, quartiere As String)

    Public Const DISTANZA_LUOGO As Double = 18

    ''' <summary>Quante macchine girano (dalle impostazioni: poco, normale, tanto traffico).</summary>
    Private ReadOnly Property TRAFFICO As Integer
        Get
            Return If(Imp.Traffico = "poco", 26, If(Imp.Traffico = "tanto", 66, 46))
        End Get
    End Property

    ''' <summary>Quanto è dura la polizia (dalle impostazioni): 0,6 tranquilla, 1 normale, 1,4 severa.</summary>
    Private ReadOnly Property Severita As Double
        Get
            Return If(Imp.Polizia = "tranquilla", 0.6, If(Imp.Polizia = "severa", 1.4, 1.0))
        End Get
    End Property

    Public Sub New(dati As DatiCitta, audio As MotoreAudio, partita As Salvataggio, imp As ImpostazioniCitta, Optional scuolaGuida As Boolean = False)
        Try
            Me.Dati = dati
            Me.Audio = audio
            Me.Partita = partita
            Me.Imp = If(imp, New ImpostazioniCitta())
            Scuola = scuolaGuida
            Missioni = New EsecutoreMissione(Me)
            Lavori = New LavoriCitta(Me)
            Eventi = New EventiCitta(Me)
            Radio = New RadioCitta(Me)
            Vita = New VitaCitta(Me)
            Progressi = New ProgressiCitta(Me)
            Sfide = New SfideCitta(Me)
            Economia = New EconomiaCitta(Me)
            Furti = New FurtiCitta(Me)
            If scuolaGuida Then Guida = New ScuolaGuida(Me)
            ' si comincia a piedi davanti a casa, con le proprie auto parcheggiate lì
            Dim casa As Luogo = dati.Luoghi.Values.FirstOrDefault(Function(l) l.Tipo = If(scuolaGuida, "autoscuola", "casa"))
            If casa Is Nothing Then casa = dati.Luoghi.Values.First()
            PosPiedi = New Posizione(casa.Tratto, casa.Distanza, 1)
            If Not scuolaGuida Then
                Dim d As Double = Math.Min(casa.Tratto.Lunghezza - 3, casa.Distanza + 5)
                For Each id As String In partita.Posseduti
                    Dim tipo As TipoVeicolo = Nothing
                    If Not dati.Veicoli.TryGetValue(id, tipo) Then Continue For
                    Dim v As New Veicolo With {.Tipo = tipo, .Pos = New Posizione(casa.Tratto, d, 1), .Tuo = True}
                    v.Salute = Robustezza(v)
                    v.Benzina = partita.BenzinaDi(id)
                    ' la merce salvata torna sul primo dei tuoi veicoli
                    If Not Veicoli.Any(Function(x) x.Tuo) Then
                        For Each kv As KeyValuePair(Of String, Integer) In partita.Carico
                            v.Carico(kv.Key) = kv.Value
                        Next
                    End If
                    Veicoli.Add(v)
                    d = Math.Max(3, d - 7)
                Next
                For Each p As Parcheggio In dati.Parcheggi
                    Veicoli.Add(New Veicolo With {.Tipo = p.Veicolo, .Pos = New Posizione(p.Tratto, p.Distanza, 1), .Salute = p.Veicolo.Robustezza, .Benzina = 25 + _caso.Next(75)})
                Next
                ' un taxi alle stazioni e un'ambulanza agli ospedali, per i lavori
                For Each l As Luogo In dati.Luoghi.Values
                    Dim tipo As String = If(l.Tipo = "stazione", "taxi", If(l.Tipo = "ospedale", "ambulanza", ""))
                    If tipo <> "" AndAlso dati.Veicoli.ContainsKey(tipo) Then Veicoli.Add(New Veicolo With {.Tipo = dati.Veicoli(tipo), .Pos = New Posizione(l.Tratto, l.Distanza, 1), .Salute = dati.Veicoli(tipo).Robustezza})
                Next
                For i As Integer = 1 To TRAFFICO
                    Veicoli.Add(NuovoTraffico(Nothing))
                Next
            End If
            If audio IsNot Nothing Then
                ' il fondo fatto al momento serve solo se mancano le registrazioni dei quartieri
                _ambiente.Volume = If(Registrazioni.Esiste("fondo_strada"), 0, 0.25F)
                audio.Aggiungi(_ambiente)
                _abitacolo = New VoceRegistrata("abitacolo", True) With {.Volume = 0, .Spaziale = False}
                If _abitacolo.Presente Then audio.Aggiungi(_abitacolo)
                _pioggia.Volume = 0
                audio.Aggiungi(_pioggia)
                _motore.Volume = 0
                audio.Aggiungi(_motore)
                For i As Integer = 1 To 6
                    Dim v As New VoceMotore() With {.Volume = 0}
                    _motoriTraffico.Add(v)
                    audio.Aggiungi(v)
                Next
            End If
            _ultimaStrada = PosPiedi.Tratto.Strada
            _ultimoQuartiere = PosPiedi.Tratto.Strada.Quartiere
            _ultimaOra = CInt(Math.Floor(partita.Ora))
            _ultimoGiorno = partita.Giorno
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.New", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ dove si è
    Public ReadOnly Property Pos As Posizione
        Get
            Return If(APiedi OrElse Veicolo Is Nothing, PosPiedi, Veicolo.Pos)
        End Get
    End Property

    Public ReadOnly Property X As Double
        Get
            Return Pos.X
        End Get
    End Property

    Public ReadOnly Property Y As Double
        Get
            Return Pos.Y
        End Get
    End Property

    Public ReadOnly Property Velocita As Double
        Get
            Return If(APiedi OrElse Veicolo Is Nothing, 0, Veicolo.Velocita)
        End Get
    End Property

    Public ReadOnly Property Notte As Boolean
        Get
            Dim h As Double = Partita.Ora
            Return h >= 21 OrElse h < 6
        End Get
    End Property

    Public ReadOnly Property InGalleria As Boolean
        Get
            Return Pos.Tratto.Galleria
        End Get
    End Property

    Public Function DistanzaDa(x As Double, y As Double) As Double
        Try
            Return Math.Sqrt((Me.X - x) ^ 2 + (Me.Y - y) ^ 2)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.DistanzaDa", ex)
            Throw
        End Try
    End Function

    ''' <summary>Da dove si sente un punto: 0 davanti, positivo a sinistra, negativo a destra (radianti).</summary>
    Public Function AzimutVerso(x As Double, y As Double) As Single
        Try
            Return CSng(Mappa.Differenza(Math.Atan2(y - Me.Y, x - Me.X), Pos.Angolo))
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.AzimutVerso", ex)
            Throw
        End Try
    End Function

    ''' <summary>Da -1 (tutto a sinistra) a 1 (tutto a destra).</summary>
    Public Function PanVerso(x As Double, y As Double) As Single
        Try
            Return CSng(-Math.Sin(AzimutVerso(x, y)))
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PanVerso", ex)
            Throw
        End Try
    End Function

    ''' <summary>Vero se il punto è davanti (entro 90 gradi).</summary>
    Public Function Davanti(x As Double, y As Double) As Boolean
        Try
            Return Math.Abs(AzimutVerso(x, y)) < Math.PI / 2
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Davanti", ex)
            Throw
        End Try
    End Function

    ''' <summary>A voce: a sinistra, a destra, davanti, dietro.</summary>
    Public Function LatoA(x As Double, y As Double) As String
        Try
            Dim az As Double = AzimutVerso(x, y)
            If Math.Abs(az) < Math.PI / 5 Then Return "davanti"
            If Math.Abs(az) > 4 * Math.PI / 5 Then Return "dietro"
            Return If(az > 0, "a sinistra", "a destra")
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.LatoA", ex)
            Throw
        End Try
    End Function

    Public Sub Dici(testo As String, Optional urgente As Boolean = False)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            RaiseEvent Annuncio(testo, urgente)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Dici", ex)
            Throw
        End Try
    End Sub

    Public Sub Senti(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            RaiseEvent Sentito(testo)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Senti", ex)
            Throw
        End Try
    End Sub

    Public Sub Notifica(tipo As String, Optional titolo As String = "", Optional quartiere As String = "")
        Try
            RaiseEvent Notizia(tipo, titolo, If(quartiere = "", Dati.QuartiereDi(X, Y).Nome, quartiere))
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Notifica", ex)
            Throw
        End Try
    End Sub

    Public Sub Suona(v As VoceAudio, Optional volume As Single = 1, Optional azimut As Single = 0)
        Try
            If Audio IsNot Nothing Then Audio.Suona(v, volume, azimut)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Suona", ex)
            Throw
        End Try
    End Sub

    Public Function Caso() As Random
        Try
            Return _caso
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Caso", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ i veicoli del giocatore e le migliorie
    Public Function Robustezza(v As Veicolo) As Double
        Try
            Return v.Tipo.Robustezza * If(v.Tuo AndAlso Partita.HaMiglioria(v.Tipo.Id, "blindatura"), 1.4, 1.0)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Robustezza", ex)
            Throw
        End Try
    End Function

    Private Function VelocitaMassima(v As Veicolo) As Double
        Try
            Dim base As Double = v.Tipo.Velocita * If(v.Tuo AndAlso Partita.HaMiglioria(v.Tipo.Id, "motore"), 1.15, 1.0)
            Return base * (0.55 + 0.45 * Math.Max(0, v.Salute) / Robustezza(v))
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.VelocitaMassima", ex)
            Throw
        End Try
    End Function

    Private Function Frenata(v As Veicolo) As Double
        Try
            Dim f As Double = If(v.Tuo AndAlso Partita.HaMiglioria(v.Tipo.Id, "freni"), 19, 14)
            Return If(Pioggia, f * 0.7, f)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Frenata", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ il ciclo
    Public Sub Passo(dt As Double, c As Comandi)
        Try
            dt = Math.Min(dt, 0.1)
            Tempo += dt
            If Not Scuola Then Orologio(dt)
            PassoRadar()
            If Barca IsNot Nothing Then
                ' in barca il mondo di terra si ferma: conta solo l'inseguimento
                Barca.Passo(dt, c)
                Vita.Passo(dt)
                Missioni.Passo(dt)
                Return
            End If
            If APiedi Then
                PassoPiedi(dt, c)
            Else
                PassoGuida(dt, c)
            End If
            For Each v As Veicolo In Veicoli.ToList()
                Select Case v.Ruolo
                    Case RuoloVeicolo.Traffico : PassoTraffico(v, dt)
                    Case RuoloVeicolo.Polizia : PassoPolizia(v, dt)
                    Case RuoloVeicolo.Bersaglio : PassoBersaglio(v, dt)
                    Case RuoloVeicolo.Banda : PassoBanda(v, dt)
                End Select
            Next
            If Not APiedi Then ControllaUrti()
            ' dopo un urto si può essere scesi (macchina distrutta)
            If Not APiedi AndAlso Veicolo IsNot Nothing Then
                Semafori(dt)
                If Not Scuola Then Pedoni(dt)
            End If
            If Not Scuola Then
                Ricercato(dt)
                Missioni.Passo(dt)
                Lavori.Passo(dt)
                Eventi.Passo(dt)
                Economia.Passo(dt)
                Sfide.Passo(dt)
                Progressi.Passo(dt)
                PassoNascosti()
            ElseIf Guida IsNot Nothing Then
                Guida.Passo(dt)
            End If
            If Radio IsNot Nothing Then Radio.Passo(dt)
            Vita.Passo(dt)
            ArrivoAutomatico()
            Navigatore(dt)
            CambioZona()
            AggiornaSuoni(dt)
            If CInt(Tempo * 2) Mod 6 = 0 Then RicicloTraffico()
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Passo", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ ore, giorni e tempo
    Private Sub Orologio(dt As Double)
        Try
            ' un secondo vero è un minuto del gioco (un giorno dura 24 minuti); dalle impostazioni l'orologio può andare più piano
            Partita.Minuti += dt * If(Imp.Orologio = "lento", 0.5, If(Imp.Orologio = "lentissimo", 0.25, 1.0))
            Partita.Conta("secondi", dt)
            Dim ora As Integer = CInt(Math.Floor(Partita.Ora))
            If ora <> _ultimaOra Then
                _ultimaOra = ora
                If ora = 21 Then
                    Notifica("notte")
                    Dici("Fa sera, c'è meno giro ma più pattuglie.")
                ElseIf ora = 7 Then
                    Notifica("mattina")
                End If
            End If
            If Partita.Giorno <> _ultimoGiorno Then
                _ultimoGiorno = Partita.Giorno
                NuovoGiorno()
            End If
            If Tempo >= _prossimoMeteo Then
                _prossimoMeteo = Tempo + 30 + _caso.NextDouble() * 30
                If Pioggia AndAlso _caso.NextDouble() < 0.25 Then
                    Pioggia = False
                    Dici("Ha smesso di piovere.")
                ElseIf Not Pioggia AndAlso _caso.NextDouble() < 0.07 Then
                    Pioggia = True
                    Dici("Comincia a piovere, l'asfalto è una saponetta, frena prima.")
                    Notifica("pioggia")
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Orologio", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Ogni nuovo giorno: le attività comprate rendono, la polizia dimentica un po'.</summary>
    Private Sub NuovoGiorno()
        Try
            Dim rendita As Integer = 0
            For Each id As String In Partita.Proprieta
                Dim l As Luogo = Nothing
                If Dati.Luoghi.TryGetValue(id, l) Then rendita += l.Rendita
            Next
            For Each q As String In Partita.Notorieta.Keys.ToList()
                Partita.Notorieta(q) *= 0.7
            Next
            ' gli interessi della banca: mezzo punto al giorno, al massimo 600 euro
            If Partita.Banca > 0 Then
                Dim interessi As Integer = Math.Min(600, CInt(Math.Floor(Partita.Banca * 0.005)))
                If interessi > 0 Then
                    Partita.Banca += interessi
                    Dici("La banca ti accredita " & interessi.ToString() & " euro di interessi.")
                End If
            End If
            If rendita > 0 Then
                Partita.Soldi += rendita
                Suona(MotoreAudio.Soldi(), 0.5F)
                Dici("Comincia il giorno " & Partita.Giorno.ToString() & ": le tue attività hanno reso " & rendita.ToString() & " euro.")
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.NuovoGiorno", ex)
            Throw
        End Try
    End Sub

    ''' <summary>O: l'ora e il tempo.</summary>
    Public Function OraETempo() As String
        Try
            Dim h As Integer = CInt(Math.Floor(Partita.Ora))
            Dim m As Integer = CInt(Math.Floor((Partita.Ora - h) * 60))
            Return "Giorno " & Partita.Giorno.ToString() & ", sono le " & h.ToString() & " e " & m.ToString("00") & ". " & If(Pioggia, "Piove.", "Non piove.") & If(Notte, " È notte.", "")
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.OraETempo", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ a piedi
    Private Sub PassoPiedi(dt As Double, c As Comandi)
        Try
            StaCorrendo = c.Su AndAlso c.Corsa
            FermoAPiedi = Not c.Su
            If Not c.Su Then Return
            Dim v As Double = If(c.Corsa, 3.6, 1.5)
            If PosPiedi.AllIncrocio < 0.05 Then
                If Not AttraversaIncrocio(PosPiedi, False) Then Return
            End If
            Dim resto As Double = PosPiedi.Avanza(v * dt)
            Partita.Metri += v * dt
            If Tempo >= _prossimoPasso Then
                _prossimoPasso = Tempo + If(c.Corsa, 0.3, 0.55)
                Suona(If(c.Corsa, MotoreAudio.Corsa(), MotoreAudio.Passo()), 0.35F, 0)
            End If
            If resto > 0 OrElse PosPiedi.AllIncrocio < 0.05 Then
                If Not _avvisoIncrocioPiedi Then
                    _avvisoIncrocioPiedi = True
                    Dici(DescriviIncrocio(PosPiedi, True))
                End If
            Else
                _avvisoIncrocioPiedi = False
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoPiedi", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ alla guida
    Private Sub PassoGuida(dt As Double, c As Comandi)
        Try
            Dim v As Veicolo = Veicolo
            Dim massima As Double = VelocitaMassima(v) * Economia.FattoreVelocita
            ' in salita si accelera meno, in discesa di più
            Dim pendenza As Double = v.Pos.Tratto.Pendenza * v.Pos.Verso
            ' senza benzina il motore non tira
            Dim tira As Boolean = Economia.Consuma(v, v.Velocita * dt)
            ' il freno vince sempre sul gas: se si tengono premute tutte e due le frecce (o la ripetizione automatica dell'altra si ferma) si frena
            Dim frena As Boolean = c.Giu AndAlso v.Velocita > 0.3
            _gasGiocatore = If(c.Su AndAlso tira AndAlso Not frena, 1.0F, 0.0F)
            If frena Then
                Dim prima As Double = v.Velocita
                v.Velocita = Math.Max(0, v.Velocita - Frenata(v) * dt)
                If prima > 9 AndAlso Tempo >= _prossimoFreno Then
                    _prossimoFreno = Tempo + 0.9
                    Suona(MotoreAudio.Registrato("freno_aria"), CSng(Math.Min(0.5, 0.15 + prima / 80)), 0)
                End If
            ElseIf c.Su AndAlso tira Then
                If v.Velocita < 0 Then
                    v.Velocita = Math.Min(0, v.Velocita + 9 * dt)
                Else
                    v.Velocita += (v.Tipo.Accelerazione * If(v.Tuo AndAlso Partita.HaMiglioria(v.Tipo.Id, "turbo"), 1.25, 1.0) * (1 - 0.7 * v.Velocita / massima) - 9.8 * pendenza * 0.6) * dt
                End If
            ElseIf c.Giu Then
                v.Velocita = Math.Max(-5, v.Velocita - 2.5 * dt)
            Else
                v.Velocita -= Math.Sign(v.Velocita) * Math.Min(Math.Abs(v.Velocita), (0.6 + 0.012 * v.Velocita * v.Velocita + 9.8 * pendenza * 0.3) * dt)
            End If
            v.Velocita = Math.Min(v.Velocita, massima)
            Dim trattoPrima As Tratto = v.Pos.Tratto
            Dim distanzaPrima As Double = v.Pos.Distanza
            Dim resto As Double = v.Pos.Avanza(v.Velocita * dt)
            Partita.Metri += Math.Abs(v.Velocita) * dt
            If Notte Then Partita.Conta("metri_notte", Math.Abs(v.Velocita) * dt)
            If v.Tipo.DueRuote Then Partita.Conta("metri_moto", Math.Abs(v.Velocita) * dt)
            Curve(v, trattoPrima, distanzaPrima)
            If v.Velocita < 0 AndAlso v.Pos.AllIncrocio >= v.Pos.Tratto.Lunghezza - 0.01 Then
                v.Velocita = 0
            ElseIf v.Velocita > 0 AndAlso (resto > 0 OrElse v.Pos.AllIncrocio < 0.01) Then
                Dim incrocio As Incrocio = v.Pos.Prossimo
                Dim trattoDa As Tratto = v.Pos.Tratto
                Dim angoloDa As Double = v.Pos.Angolo
                If AttraversaIncrocio(v.Pos, True) Then
                    _ultimoIncrocio = incrocio
                    _trattoArrivoIncrocio = trattoDa
                    _angoloArrivoIncrocio = angoloDa
                    _tempoIncrocio = Tempo
                    _metriIncrocio = Partita.Metri
                    PassatoIncrocio(incrocio)
                    If resto > 0 Then v.Pos.Avanza(resto)
                Else
                    If v.Velocita > 6 Then Danneggia(v.Velocita * 2.5, "Sbatti contro il marciapiede in fondo alla strada.")
                    v.Velocita = 0
                End If
            End If
            Dim soglia As Double = Math.Max(30, v.Velocita * 3)
            If v.Velocita > 0.5 AndAlso v.Pos.AllIncrocio < soglia AndAlso Not (_annunciatoTratto Is v.Pos.Tratto AndAlso _annunciatoVerso = v.Pos.Verso) Then
                _annunciatoTratto = v.Pos.Tratto
                _annunciatoVerso = v.Pos.Verso
                If Meta IsNot Nothing Then
                    ' con il navigatore acceso a voce si dice solo semaforo e rotonda: la svolta la guida GuidaSvolta
                    Dim breve As String = IncrocioBreve(v.Pos)
                    If breve <> "" Then Dici(breve)
                Else
                    Dici(DescriviIncrocio(v.Pos, False))
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoGuida", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Le curve a metà di alcune strade: si annunciano prima (con un tic dalla parte in cui gira); prese troppo forte si sbanda,
    ''' con la pioggia prima.
    ''' </summary>
    Private Sub Curve(v As Veicolo, trattoPrima As Tratto, distanzaPrima As Double)
        Try
            Dim t As Tratto = v.Pos.Tratto
            If t.Curva = 0 OrElse v.Velocita <= 0.5 Then Return
            Dim meta As Double = t.Lunghezza / 2
            ' quanto manca a metà strada, nel senso di marcia
            Dim manca As Double = (meta - v.Pos.Distanza) * v.Pos.Verso
            Dim verso As Integer = t.Curva * v.Pos.Verso
            If manca > 0 AndAlso manca < Math.Max(35, v.Velocita * 3) AndAlso _curvaAnnunciata IsNot t Then
                _curvaAnnunciata = t
                Suona(MotoreAudio.TicCurva(), 0.55F, If(verso < 0, 1.2F, -1.2F))
                Dici("Curva " & If(verso < 0, "a sinistra", "a destra") & " tra " & Mappa.Metri(manca) & ".")
            End If
            ' si passa la metà adesso
            If trattoPrima Is t AndAlso (meta - distanzaPrima) * v.Pos.Verso > 0 AndAlso manca <= 0 Then
                _curvaAnnunciata = Nothing
                Dim limite As Double = If(Pioggia, 10, 12.5) * If(v.Tipo.DueRuote, 1.1, 1.0) * If(v.Tuo AndAlso Partita.HaMiglioria(v.Tipo.Id, "gomme"), 1.2, 1.0)
                If v.Velocita > limite + 4 Then
                    Suona(MotoreAudio.Sgommata(), 0.8F, If(verso < 0, -1.0F, 1.0F))
                    Danneggia((v.Velocita - limite) * 2.5, "Prendi la curva troppo forte e finisci fuori strada!")
                    v.Velocita = Math.Min(v.Velocita, 5)
                ElseIf v.Velocita > limite Then
                    Suona(MotoreAudio.Sgommata(), 0.55F, If(verso < 0, -1.0F, 1.0F))
                    v.Velocita = limite - 2
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Curve", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Freno a mano: veloce si gira la macchina di colpo (inversione a U con sgommata), piano si inchioda.
    ''' </summary>
    Public Sub FrenoAMano()
        Try
            If APiedi OrElse Veicolo Is Nothing Then
                Dici("Sei a piedi.", True)
                Return
            End If
            Dim v As Veicolo = Veicolo
            Dim velocita As Double = Math.Abs(v.Velocita)
            ' l'inversione si fa a qualunque velocità: da fermi o piano si gira con calma, più veloci si fa la sgommata, troppo veloci si fa il testacoda
            v.Pos.Inverti()
            Svolta = Nothing
            _annunciatoTratto = Nothing
            _annunciatoVerso = 0
            _ricalcolo = 0
            _percorso.Clear()
            _navIncrocio = Nothing
            If velocita > 20 OrElse (Pioggia AndAlso velocita > 15) Then
                Suona(MotoreAudio.Sgommata(), 0.9F, 0)
                v.Velocita = 2
                Danneggia(8, "Testacoda! La macchina si gira e sbatte di fianco.")
            ElseIf velocita > 6 Then
                Suona(MotoreAudio.Sgommata(), 0.8F, 0)
                v.Velocita = velocita * 0.3
                Dici("Inversione: adesso vai verso " & Mappa.Cardinale(v.Pos.Angolo) & ".", True)
            Else
                Suona(MotoreAudio.Registrato("freno_aria"), 0.4F, 0)
                v.Velocita = 0
                Dici("Giri la macchina: adesso sei rivolto verso " & Mappa.Cardinale(v.Pos.Angolo) & ".", True)
            End If
            If Corsia = 1 Then Corsia = 0
            Partita.Conta("inversioni")
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.FrenoAMano", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Manda a casa il veicolo su cui sei (rubato): diventa tuo e lo ritrovi parcheggiato davanti a casa, mentre tu resti a piedi dove sei.
    ''' </summary>
    Public Sub MandaACasa()
        Try
            If APiedi OrElse Veicolo Is Nothing Then
                Dici("Per mandare un veicolo a casa devi esserci sopra.", True)
                Return
            End If
            Dim v As Veicolo = Veicolo
            If v.Tuo Then
                Dici("Questo è già tuo.", True)
                Return
            End If
            If Math.Abs(Velocita) > 1.5 Then
                Dici("Prima fermati.", True)
                Return
            End If
            If Stelle > 0 Then
                Dici("Con la polizia alle calcagna non puoi mandarlo a casa.", True)
                Return
            End If
            If v.Tipo.Id = "polizia" OrElse v.Tipo.Id = "ambulanza" Then
                Dici("Questo non te lo puoi tenere: se ne accorgerebbero subito.", True)
                Return
            End If
            If Missioni.VeicoloDellaMissione Is v Then
                Dici("Serve per la missione: finiscila prima.", True)
                Return
            End If
            Dim casa As Luogo = Dati.Luoghi.Values.FirstOrDefault(Function(l) l.Tipo = "casa")
            If casa Is Nothing Then
                Dici("Non hai una casa dove mandarlo.", True)
                Return
            End If
            Dim gia As Integer = Veicoli.Where(Function(x) x.Tuo AndAlso x.Pos.Tratto Is casa.Tratto).Count()
            Scendi()
            v.Tuo = True
            v.Velocita = 0
            v.Pos = New Posizione(casa.Tratto, Math.Max(3, Math.Min(casa.Tratto.Lunghezza - 3, casa.Distanza + 5 - 7 * gia)), 1)
            If Not Partita.Posseduti.Contains(v.Tipo.Id) Then Partita.Posseduti.Add(v.Tipo.Id)
            Partita.Conta("garage")
            Suona(MotoreAudio.Registrato("cancello"), 0.6F, 0)
            Dici("Mandi " & v.Tipo.ConArticolo() & " a casa: adesso è " & If(v.Tipo.Maschile, "tuo", "tua") & " e lo ritrovi davanti a casa.", True)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.MandaACasa", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' P: fermi davanti a casa o a un tuo rifugio, il veicolo va in garage e diventa tuo (anche se l'hai rubato); così lo ritrovi a casa.
    ''' </summary>
    Public Sub MettiInGarage()
        Try
            If APiedi OrElse Veicolo Is Nothing Then
                Dici("Per mettere un veicolo in garage devi esserci sopra.", True)
                Return
            End If
            Dim l As Luogo = LuogoVicino()
            If l Is Nothing OrElse Not (l.Tipo = "casa" OrElse (l.Tipo = "rifugio" AndAlso Partita.Proprieta.Contains(l.Id))) Then
                Dici("Il garage ce l'hai a casa e nei rifugi che hai comprato.", True)
                Return
            End If
            If Math.Abs(Velocita) > 1 Then
                Dici("Prima fermati.", True)
                Return
            End If
            If Stelle > 0 Then
                Dici("Con la polizia alle calcagna non puoi fermarti a parcheggiare.", True)
                Return
            End If
            Dim v As Veicolo = Veicolo
            If v.Tipo.Id = "polizia" OrElse v.Tipo.Id = "ambulanza" Then
                Dici("Questo non te lo puoi tenere: se ne accorgerebbero subito.", True)
                Return
            End If
            Suona(MotoreAudio.Registrato("cancello"), 0.6F, 0)
            If Not v.Tuo Then
                v.Tuo = True
                Partita.Conta("garage")
            End If
            If Not Partita.Posseduti.Contains(v.Tipo.Id) Then Partita.Posseduti.Add(v.Tipo.Id)
            Dici("Metti " & v.Tipo.ConArticolo() & " in garage: adesso è " & If(v.Tipo.Maschile, "tuo", "tua") & " e lo ritrovi qui ogni volta.", True)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.MettiInGarage", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Dopo un incrocio in macchina: col rosso e la polizia vicina sono guai; si torna nella propria corsia.</summary>
    Private Sub PassatoIncrocio(incrocio As Incrocio)
        Try
            If incrocio.Semaforo AndAlso Not Verde(incrocio, Veicolo.Pos.Angolo) AndAlso Not Scuola Then
                If Veicoli.Any(Function(x) x.Ruolo = RuoloVeicolo.Polizia AndAlso DistanzaDa(x.X, x.Y) < 170) Then
                    Ricerca(1, "Sei passato col rosso davanti alle forze dell'ordine!")
                Else
                    Suona(MotoreAudio.Clacson(), 0.5F, 1.2F)
                End If
            End If
            If Corsia = 1 Then
                Corsia = 0
                Dici("Torni nella tua corsia.")
            End If
            _rossoAnnunciato = Nothing
            _pedoneDeciso = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassatoIncrocio", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Si arriva a un incrocio: si imbocca la svolta scelta, o quella del navigatore (con la guida assistita), o dritto. In macchina, una
    ''' curva presa troppo veloce fa sbandare (con la pioggia di più). Falso se non si sa dove andare (ci si ferma).
    ''' </summary>
    Private Function AttraversaIncrocio(p As Posizione, inMacchina As Boolean) As Boolean
        Try
            Dim incrocio As Incrocio = p.Prossimo
            Dim svolte As List(Of Svolta) = Mappa.Svolte(incrocio, p.Tratto, p.Angolo)
            Dim scelta As Svolta = Nothing
            If Svolta.HasValue Then scelta = svolte.FirstOrDefault(Function(s) s.Verso = Svolta.Value)
            If scelta Is Nothing AndAlso Meta IsNot Nothing AndAlso Not Svolta.HasValue AndAlso (Imp.GuidaAssistita OrElse Not inMacchina) Then scelta = SvoltaNavigatore(incrocio, svolte)
            If scelta Is Nothing AndAlso Not Svolta.HasValue Then scelta = svolte.FirstOrDefault(Function(s) s.Verso = 0)
            If scelta Is Nothing Then
                Dim avanti As List(Of Svolta) = svolte.Where(Function(s) s.Verso <> 2).ToList()
                If avanti.Count = 1 AndAlso Not Svolta.HasValue Then scelta = avanti(0)
            End If
            If scelta Is Nothing Then
                If Svolta.HasValue Then
                    Dici("Da qui " & NomeSvolta(Svolta.Value) & " non si può passare. " & DescriviIncrocio(p, True), True)
                    Svolta = Nothing
                End If
                Return False
            End If
            If inMacchina AndAlso (scelta.Verso <> 0 OrElse incrocio.Rotonda) Then
                Dim v As Veicolo = Veicolo
                ' alla rotonda si gira sempre, anche andando dritto: bisogna entrarci piano
                Dim limiteSbandata As Double = If(incrocio.Rotonda, If(Pioggia, 12, 15), If(Pioggia, 17, 22))
                Dim limiteCurva As Double = If(incrocio.Rotonda, If(Pioggia, 8, 10), If(Pioggia, 11, 14))
                If v.Velocita > limiteSbandata Then
                    Suona(MotoreAudio.Sgommata(), 0.8F, 0)
                    Danneggia((v.Velocita - limiteSbandata + 6) * 3, If(Pioggia, "Sull'asfalto bagnato perdi l'auto in curva e finisci contro un palo!", "Prendi la curva a tutta birra e finisci contro un palo!"))
                    v.Velocita = 4
                ElseIf v.Velocita > limiteCurva Then
                    Suona(MotoreAudio.Sgommata(), 0.6F, If(scelta.Verso < 0, -1.2F, 1.2F))
                    v.Velocita = limiteCurva - 4
                End If
            End If
            ' con il navigatore, nel punto della svolta si sente un doppio tono dalla parte in cui si gira
            If inMacchina AndAlso Meta IsNot Nothing AndAlso (scelta.Verso = -1 OrElse scelta.Verso = 1) Then Suona(MotoreAudio.SvoltaNavigatore(), 0.8F, If(scelta.Verso < 0, 1.2F, -1.2F))
            p.Imbocca(scelta.Tratto, incrocio)
            Svolta = Nothing
            If scelta.Tratto.Strada IsNot _ultimaStrada Then
                _ultimaStrada = scelta.Tratto.Strada
                Dim testo As String = scelta.Tratto.Strada.Nome
                If scelta.Tratto.Galleria Then testo &= ", in galleria"
                If scelta.Tratto.Ponte Then testo &= ", sul ponte del canale"
                If Math.Abs(scelta.Tratto.Pendenza) > 0.01 Then testo &= If(scelta.Tratto.Pendenza * p.Verso > 0, ", in salita", ", in discesa")
                If Not Imp.AnnunciEssenziali OrElse scelta.Tratto.Galleria OrElse scelta.Tratto.Ponte Then Dici(testo & ", verso " & Mappa.Cardinale(p.Angolo) & ".")
            End If
            If Meta IsNot Nothing Then _ricalcolo = 0
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.AttraversaIncrocio", ex)
            Throw
        End Try
    End Function

    Private Shared Function NomeSvolta(v As Integer) As String
        Try
            Select Case v
                Case -1 : Return "a sinistra"
                Case 1 : Return "a destra"
                Case 2 : Return "indietro"
                Case Else : Return "dritto"
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.NomeSvolta", ex)
            Throw
        End Try
    End Function

    ''' <summary>Cosa c'è al prossimo incrocio: le strade, il semaforo e, con il navigatore, dove girare.</summary>
    Public Function DescriviIncrocio(p As Posizione, arrivato As Boolean) As String
        Try
            Dim incrocio As Incrocio = p.Prossimo
            Dim svolte As List(Of Svolta) = Mappa.Svolte(incrocio, p.Tratto, p.Angolo).Where(Function(s) s.Verso <> 2).ToList()
            Dim pezzi As New List(Of String)()
            For Each s As Svolta In svolte
                pezzi.Add(s.Nome & " " & s.Tratto.Strada.Nome)
            Next
            Dim nome As String = If(incrocio.Rotonda, "Rotonda", "Incrocio")
            Dim testo As String = If(arrivato, nome & ". ", nome & " tra " & Mappa.Metri(p.AllIncrocio) & ". ")
            If incrocio.Rotonda AndAlso Not APiedi Then testo &= "Rallenta. "
            If incrocio.Semaforo AndAlso Not APiedi Then testo &= If(Verde(incrocio, p.Angolo), "Semaforo verde. ", "Semaforo rosso. ")
            testo &= If(pezzi.Count = 0, "Strada senza uscita.", String.Join(", ", pezzi) & ".")
            If Meta IsNot Nothing Then
                Dim s As Svolta = SvoltaNavigatore(incrocio, Mappa.Svolte(incrocio, p.Tratto, p.Angolo))
                If s IsNot Nothing Then testo &= " Navigatore: " & If(s.Verso = 0, "vai dritto.", "gira " & s.Nome & ".")
            End If
            If Svolta.HasValue Then testo &= " Hai preso " & NomeSvolta(Svolta.Value) & "."
            Return testo
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.DescriviIncrocio", ex)
            Throw
        End Try
    End Function

    ''' <summary>Freccia sinistra o destra (o giù a piedi): la svolta al prossimo incrocio. A piedi, se si è all'incrocio, si svolta subito.</summary>
    Public Sub ScegliSvolta(verso As Integer)
        Try
            Dim p As Posizione = Pos
            If APiedi AndAlso p.AllIncrocio < 0.5 Then
                Dim svolte As List(Of Svolta) = Mappa.Svolte(p.Prossimo, p.Tratto, p.Angolo)
                Dim s As Svolta = svolte.FirstOrDefault(Function(x) x.Verso = verso)
                If s Is Nothing Then
                    Dici("Da qui " & NomeSvolta(verso) & " non si va.", True)
                    Return
                End If
                Svolta = verso
                AttraversaIncrocio(p, False)
                Return
            End If
            If APiedi AndAlso verso = 2 Then
                p.Inverti()
                Svolta = Nothing
                _avvisoIncrocioPiedi = False
                Dici("Torni indietro, verso " & Mappa.Cardinale(p.Angolo) & ".", True)
                Return
            End If
            ' sterzata data in ritardo: se l'incrocio è appena passato (due secondi e mezzo, quaranta metri) si prende la strada voluta lo stesso
            If Not APiedi AndAlso _ultimoIncrocio IsNot Nothing AndAlso Tempo - _tempoIncrocio < 2.5 AndAlso Partita.Metri - _metriIncrocio < 40 Then
                Dim passate As List(Of Svolta) = Mappa.Svolte(_ultimoIncrocio, _trattoArrivoIncrocio, _angoloArrivoIncrocio)
                Dim giusta As Svolta = passate.FirstOrDefault(Function(x) x.Verso = verso)
                If giusta IsNot Nothing AndAlso giusta.Tratto IsNot p.Tratto Then
                    Dim fatti As Double = Partita.Metri - _metriIncrocio
                    p.Imbocca(giusta.Tratto, _ultimoIncrocio)
                    p.Avanza(fatti)
                    _ultimoIncrocio = Nothing
                    Svolta = Nothing
                    _navIncrocio = Nothing
                    _annunciatoTratto = Nothing
                    _annunciatoVerso = 0
                    _ricalcolo = 0
                    Suona(MotoreAudio.TicSvolta(), 0.6F, If(verso < 0, 1.2F, -1.2F))
                    Dici("Giri " & NomeSvolta(verso) & ", " & giusta.Tratto.Strada.Nome & ".", True)
                    Return
                End If
            End If
            Dim tutte As List(Of Svolta) = Mappa.Svolte(p.Prossimo, p.Tratto, p.Angolo)
            Dim scelta As Svolta = tutte.FirstOrDefault(Function(x) x.Verso = verso)
            If scelta Is Nothing Then
                Dici("Al prossimo incrocio " & NomeSvolta(verso) & " non si può.", True)
                Return
            End If
            Svolta = verso
            Dici("Al prossimo incrocio " & NomeSvolta(verso) & ", " & scelta.Tratto.Strada.Nome & ".", True)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.ScegliSvolta", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Ctrl più freccia: si passa nella corsia opposta per sorpassare (1) o si torna nella propria (0).</summary>
    Public Sub CambiaCorsia(nuova As Integer)
        Try
            If APiedi Then
                Dici("Sei a piedi.", True)
                Return
            End If
            If Corsia = nuova Then
                Dici(If(nuova = 1, "Sei già nella corsia opposta.", "Sei già nella tua corsia."), True)
                Return
            End If
            Corsia = nuova
            Suona(MotoreAudio.Tic(), 0.4F, If(nuova = 1, 1.0F, -1.0F))
            Dici(If(nuova = 1, "Invadi l'altra corsia, occhio a chi arriva di muso.", "Rientri nella tua corsia."), True)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.CambiaCorsia", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ semafori e pedoni
    ''' <summary>Il semaforo è verde per chi arriva con questa direzione? Le due direzioni si alternano ogni 25 secondi.</summary>
    Public Function Verde(incrocio As Incrocio, angolo As Double) As Boolean
        Try
            If Not incrocio.Semaforo Then Return True
            Dim sfasamento As Double = (incrocio.X * 7 + incrocio.Y * 13) Mod 50
            Dim fase As Boolean = ((Tempo + sfasamento) Mod 50) < 25
            Dim estOvest As Boolean = Math.Abs(Math.Cos(angolo)) >= Math.Abs(Math.Sin(angolo))
            Return fase = estOvest
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Verde", ex)
            Throw
        End Try
    End Function

    Private Sub Semafori(dt As Double)
        Try
            Dim p As Posizione = Veicolo.Pos
            Dim incrocio As Incrocio = p.Prossimo
            If Not incrocio.Semaforo OrElse p.AllIncrocio > 45 OrElse Veicolo.Velocita < 0 Then Return
            If Verde(incrocio, p.Angolo) Then
                If _rossoAnnunciato Is incrocio Then
                    _rossoAnnunciato = Nothing
                    Dici("Verde.", True)
                End If
                Return
            End If
            If _rossoAnnunciato IsNot incrocio Then
                _rossoAnnunciato = incrocio
                Dici("Semaforo rosso!", True)
            End If
            If Tempo >= _prossimoTicSemaforo Then
                _prossimoTicSemaforo = Tempo + 0.8
                Suona(MotoreAudio.TicSemaforo(), 0.45F, AzimutVerso(incrocio.X, incrocio.Y))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Semafori", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' A volte, avvicinandosi a un incrocio, un pedone attraversa: si sentono i passi e il fischio; bisogna fermarsi prima.
    ''' Di giorno capita più spesso. Chi non si ferma lo sfiora: danni, insulti e, se c'è la polizia, una stella.
    ''' </summary>
    Private Sub Pedoni(dt As Double)
        Try
            Dim p As Posizione = Veicolo.Pos
            If p.AllIncrocio < 60 AndAlso p.AllIncrocio > 30 AndAlso _pedoneDeciso IsNot p.Tratto AndAlso Not p.Tratto.Galleria Then
                _pedoneDeciso = p.Tratto
                If _caso.NextDouble() < If(Notte, 0.04, 0.1) Then
                    _attraversamenti.Add(New Attraversamento With {.Incrocio = p.Prossimo, .Tratto = p.Tratto, .Fine = Tempo + 7})
                    Suona(MotoreAudio.Fischio(), 0.55F, AzimutVerso(p.Prossimo.X, p.Prossimo.Y))
                    Dici("Un passante attraversa all'improvviso!", True)
                End If
            End If
            For Each a As Attraversamento In _attraversamenti.ToList()
                If Tempo > a.Fine Then
                    _attraversamenti.Remove(a)
                    Continue For
                End If
                If Tempo >= _prossimoPasso Then
                    _prossimoPasso = Tempo + 0.4
                    Suona(MotoreAudio.Passo(), 0.4F, AzimutVerso(a.Incrocio.X, a.Incrocio.Y))
                End If
                If p.Tratto Is a.Tratto AndAlso p.Prossimo Is a.Incrocio AndAlso p.AllIncrocio < 7 AndAlso Veicolo.Velocita > 3 Then
                    _attraversamenti.Remove(a)
                    Dim grido As String = Dati.TestoACaso("eventi", "pedoni", _caso)
                    Danneggia(3, "Sfiori il pedone! " & grido)
                    If Veicoli.Any(Function(x) x.Ruolo = RuoloVeicolo.Polizia AndAlso DistanzaDa(x.X, x.Y) < 250) OrElse _caso.NextDouble() < 0.3 Then Ricerca(1, "Qualcuno ha chiamato la polizia.")
                ElseIf p.Tratto Is a.Tratto AndAlso p.AllIncrocio < 18 AndAlso Math.Abs(Veicolo.Velocita) < 1 Then
                    _attraversamenti.Remove(a)
                    If p.AllIncrocio < 9 Then Dici(Dati.TestoACaso("eventi", "pedoni", _caso))
                End If
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Pedoni", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ salire, scendere, danni
    ''' <summary>Invio: sale sul veicolo più vicino o scende da quello su cui si è.</summary>
    Public Sub SaliScendi()
        Try
            If Not APiedi Then
                If Math.Abs(Veicolo.Velocita) > 1.5 Then
                    Dici("Prima fermati.", True)
                    Return
                End If
                Scendi()
                Return
            End If
            ' se c'è l'auto da rubare per la missione, a portata di mano, si sale su quella; altrimenti sulla più vicina
            Dim daPrendere As Veicolo = Missioni.VeicoloDaPrendere
            Dim vicino As Veicolo = If(daPrendere IsNot Nothing AndAlso DistanzaDa(daPrendere.X, daPrendere.Y) < 12, daPrendere,
                Veicoli.Where(Function(v) v.Ruolo <> RuoloVeicolo.Polizia AndAlso v.Ruolo <> RuoloVeicolo.Relitto AndAlso DistanzaDa(v.X, v.Y) < 9).OrderBy(Function(v) DistanzaDa(v.X, v.Y)).FirstOrDefault())
            If vicino Is Nothing Then
                Dici("Qui vicino non c'è nessun veicolo.", True)
                Return
            End If
            If vicino.Ruolo = RuoloVeicolo.Traffico AndAlso vicino.Velocita > 1.5 Then
                Dici("Quella macchina non si ferma.", True)
                Return
            End If
            If vicino.Ruolo = RuoloVeicolo.Bersaglio Then
                Dici("Quella macchina non la puoi prendere adesso.", True)
                Return
            End If
            Sali(vicino)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.SaliScendi", ex)
            Throw
        End Try
    End Sub

    ''' <summary>I veicoli a portata di furto: fermi (parcheggiati o in coda), entro la distanza, dal più vicino.</summary>
    Public Function VeicoliVicini(massimo As Double) As List(Of Veicolo)
        Try
            Return Veicoli.Where(Function(v) v IsNot Veicolo AndAlso Not v.Blocco AndAlso
                                     (v.Ruolo = RuoloVeicolo.Parcheggiato OrElse (v.Ruolo = RuoloVeicolo.Traffico AndAlso Math.Abs(v.Velocita) <= 1.5)) AndAlso
                                     DistanzaDa(v.X, v.Y) < massimo).OrderBy(Function(v) DistanzaDa(v.X, v.Y)).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.VeicoliVicini", ex)
            Throw
        End Try
    End Function

    ''' <summary>Va dritto al veicolo scelto (da fermi) e ci sale: il furto semplice, senza dover girare a cercarlo.</summary>
    Public Sub Ruba(v As Veicolo)
        Try
            If v Is Nothing OrElse Not Veicoli.Contains(v) Then Return
            If Not APiedi Then
                If Math.Abs(Veicolo.Velocita) > 1.5 Then
                    Dici("Prima fermati.", True)
                    Return
                End If
                Scendi()
            End If
            If v.Ruolo = RuoloVeicolo.Traffico AndAlso Math.Abs(v.Velocita) > 1.5 Then
                Dici("Quella macchina non si ferma.", True)
                Return
            End If
            If v.Ruolo <> RuoloVeicolo.Parcheggiato AndAlso v.Ruolo <> RuoloVeicolo.Traffico Then
                Dici("Quella non la puoi prendere adesso.", True)
                Return
            End If
            PosPiedi = New Posizione(v.Pos.Tratto, v.Pos.Distanza, v.Pos.Verso)
            Sali(v)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Ruba", ex)
            Throw
        End Try
    End Sub

    Public Sub Sali(v As Veicolo)
        Try
            Dim eraTraffico As Boolean = v.Ruolo = RuoloVeicolo.Traffico
            Dim furto As Boolean = Not v.Tuo AndAlso Not Scuola
            v.Ruolo = RuoloVeicolo.Giocatore
            v.Velocita = 0
            v.Pos = New Posizione(PosPiedi.Tratto, PosPiedi.Distanza, PosPiedi.Verso)
            Veicolo = v
            APiedi = False
            Svolta = Nothing
            Corsia = 0
            If v.Tipo.DueRuote Then
                Suona(MotoreAudio.Registrato("avvio_moto"), 0.6F, 0)
            Else
                Suona(MotoreAudio.Portiera(), 0.8F, 0)
                Suona(MotoreAudio.Registrato("avvio_auto"), 0.45F, 0)
            End If
            Dim testo As String = If(v.Tipo.DueRuote, "Monti in sella " & v.Tipo.ConSu().Replace("sulla ", "alla ").Replace("sul ", "al "), "Sali " & v.Tipo.ConSu()) & If(v.Tuo, If(v.Tipo.Maschile, ", il tuo.", ", la tua."), ".")
            If furto Then Partita.CambiaRispetto(Dati.QuartiereDi(X, Y).Id, -1)
            If eraTraffico Then
                testo = If(v.Tipo.DueRuote, "Butti giù il motociclista e prendi ", "Tiri giù l'autista e prendi ") & v.Tipo.ConArticolo() & "."
                Ricerca(1, "Ti hanno visto rubare la macchina!")
            ElseIf furto AndAlso Not Missioni.FurtoTollerato(v) AndAlso v.Tipo.Id <> "taxi" AndAlso v.Tipo.Id <> "ambulanza" Then
                Dim poliziaVicina As Boolean = Veicoli.Any(Function(x) x.Ruolo = RuoloVeicolo.Polizia AndAlso DistanzaDa(x.X, x.Y) < 150)
                If poliziaVicina OrElse _caso.NextDouble() < 0.3 Then Ricerca(1, "Scatta l'allarme: qualcuno ha chiamato la polizia!")
            End If
            Dici(testo & " " & SaluteVeicolo(), True)
            Economia.Salito(v)
            Missioni.Salito(v)
            Lavori.Salito(v)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Sali", ex)
            Throw
        End Try
    End Sub

    Public Sub Scendi()
        Try
            If APiedi Then Return
            Dim v As Veicolo = Veicolo
            PosPiedi = New Posizione(v.Pos.Tratto, v.Pos.Distanza, v.Pos.Verso)
            If v.Ruolo <> RuoloVeicolo.Relitto Then v.Ruolo = RuoloVeicolo.Parcheggiato
            v.Velocita = 0
            Veicolo = Nothing
            APiedi = True
            Svolta = Nothing
            Corsia = 0
            _avvisoIncrocioPiedi = False
            Suona(MotoreAudio.Portiera(), 0.8F, 0)
            Dici("Scendi.", True)
            Missioni.Sceso(v)
            Lavori.Sceso(v)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Scendi", ex)
            Throw
        End Try
    End Sub

    Public Function SaluteVeicolo() As String
        Try
            If Veicolo Is Nothing Then Return ""
            Dim p As Integer = CInt(Math.Max(0, Veicolo.Salute) / Robustezza(Veicolo) * 100)
            If p >= 90 Then Return "È in ottimo stato."
            If p >= 60 Then Return "Ha qualche ammaccatura."
            If p >= 30 Then Return "È parecchio malconcia."
            Return "Sta per andare a pezzi."
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.SaluteVeicolo", ex)
            Throw
        End Try
    End Function

    Public Sub Danneggia(quanto As Double, testo As String)
        Try
            If Veicolo Is Nothing OrElse quanto <= 0 Then Return
            Veicolo.Salute -= quanto * 100 / Veicolo.Tipo.Robustezza
            Suona(MotoreAudio.Urto(quanto > 20), CSng(Math.Min(1, 0.4 + quanto / 40)), 0)
            If quanto > 25 Then Suona(MotoreAudio.Registrato("vetri"), 0.5F, 0)
            ' in moto un colpo forte ti butta giù
            If Veicolo.Tipo.DueRuote AndAlso quanto > 14 AndAlso Veicolo.Salute > 0 Then
                Dim moto As Veicolo = Veicolo
                Dici(testo & " Cadi " & If(moto.Tipo.Maschile, "dallo ", "dalla ") & moto.Tipo.Nome & " e rotoli sull'asfalto. Ti rialzi tutto ammaccato.", True)
                Scendi()
                Partita.Conta("cadute")
                Return
            End If
            If Veicolo.Salute <= 0 Then
                Veicolo.Salute = 0
                Dim v As Veicolo = Veicolo
                v.Ruolo = RuoloVeicolo.Relitto
                Dici(testo & " La macchina è distrutta: il motore si spegne e scendi di corsa.", True)
                Notifica("incidente")
                Scendi()
                v.Ruolo = RuoloVeicolo.Relitto
                v.Carico.Clear()
                Missioni.Distrutto(v)
                Lavori.Distrutto(v)
            ElseIf Not String.IsNullOrEmpty(testo) Then
                Dici(testo & " " & SaluteVeicolo(), True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Danneggia", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Gli urti tra il veicolo del giocatore e gli altri.</summary>
    Private Sub ControllaUrti()
        Try
            Dim mio As Veicolo = Veicolo
            For Each v As Veicolo In Veicoli
                ' le auto parcheggiate stanno al bordo e i relitti sono stati spinti via: non si urtano
                If v Is mio OrElse v.Ruolo = RuoloVeicolo.Relitto OrElse (v.Ruolo = RuoloVeicolo.Parcheggiato AndAlso Not v.Blocco) Then Continue For
                If Tempo - mio.UltimoUrto < 1.2 Then Continue For
                Dim relativa As Double
                Dim frontale As Boolean = False
                If v.Pos.Tratto Is mio.Pos.Tratto Then
                    If Math.Abs(v.Pos.Distanza - mio.Pos.Distanza) > 4 Then Continue For
                    ' nella propria corsia si urta chi va nello stesso senso; nella corsia opposta chi arriva di fronte
                    Dim stessaCorsia As Boolean = If(Corsia = 0, v.Pos.Verso = mio.Pos.Verso, v.Pos.Verso <> mio.Pos.Verso)
                    If Not stessaCorsia AndAlso Not v.Blocco Then Continue For
                    frontale = v.Pos.Verso <> mio.Pos.Verso
                    relativa = If(frontale, Math.Abs(mio.Velocita) + Math.Abs(v.Velocita), Math.Abs(mio.Velocita - v.Velocita))
                Else
                    Dim d As Double = Math.Sqrt((v.X - mio.X) ^ 2 + (v.Y - mio.Y) ^ 2)
                    If d > 3 OrElse mio.Pos.AllIncrocio > 4 OrElse v.Pos.AllIncrocio > 4 Then Continue For
                    relativa = Math.Abs(mio.Velocita) + Math.Abs(v.Velocita) * 0.5
                End If
                If relativa < 1.5 Then Continue For
                mio.UltimoUrto = Tempo
                v.UltimoUrto = Tempo
                v.Salute -= relativa * 2
                v.FermoFino = Tempo + 4
                mio.Velocita *= If(frontale, 0.1, 0.35)
                Dim chi As String
                Select Case v.Ruolo
                    Case RuoloVeicolo.Polizia : chi = "una volante della polizia"
                    Case RuoloVeicolo.Bersaglio : chi = If(v Is Missioni.Alleato, "l'auto che stai scortando", "l'auto che stai inseguendo")
                    Case RuoloVeicolo.Banda : chi = "la macchina della banda"
                    Case Else : chi = v.Tipo.ConUn()
                End Select
                Danneggia(relativa * If(frontale, 2.4, 1.6), If(frontale, "Frontale con " & chi & "!", "Urti " & chi & "!"))
                If v.Ruolo = RuoloVeicolo.Traffico Then
                    Suona(MotoreAudio.Clacson(), 0.6F, AzimutVerso(v.X, v.Y))
                    Dim grido As String = Dati.TestoACaso("eventi", "clacson", _caso)
                    If grido <> "" Then Dici(grido)
                End If
                If v.Ruolo = RuoloVeicolo.Polizia Then Ricerca(1, "Hai speronato la polizia!")
                Missioni.Urto(v)
                Lavori.Urto(v)
                Eventi.Urto(v)
                Vita.Urto(v)
                Exit For
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.ControllaUrti", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ traffico
    Private Function NuovoTraffico(vicinoA As Posizione) As Veicolo
        Try
            Dim tipi As String() = {"utilitaria", "utilitaria", "berlina", "berlina", "taxi", "furgone", "sportiva", "fuoristrada", "moto", "scooter", "camion", "suv", "pickup", "monovolume", "coupe", "cabrio", "motocarro", "autobus", "muscle"}
            Dim tipo As TipoVeicolo = Dati.Veicoli(tipi(_caso.Next(tipi.Length)))
            Dim t As Tratto = TrattoACaso(vicinoA, 120, 450)
            Dim v As New Veicolo With {.Tipo = tipo, .Pos = New Posizione(t, _caso.NextDouble() * t.Lunghezza, If(_caso.Next(2) = 0, 1, -1)), .Ruolo = RuoloVeicolo.Traffico, .Salute = tipo.Robustezza}
            v.VelocitaVoluta = t.Limite / 3.6 * (0.55 + _caso.NextDouble() * 0.3)
            v.Velocita = v.VelocitaVoluta
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.NuovoTraffico", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un tratto a caso; se si dà una posizione, a una distanza da lì tra minimo e massimo metri.</summary>
    Public Function TrattoACaso(vicinoA As Posizione, minimo As Double, massimo As Double) As Tratto
        Try
            If vicinoA Is Nothing Then Return Dati.Tratti(_caso.Next(Dati.Tratti.Count))
            Dim px As Double = vicinoA.X
            Dim py As Double = vicinoA.Y
            Dim adatti As List(Of Tratto) = Dati.Tratti.Where(Function(t)
                                                                  Dim d As Double = Math.Sqrt(((t.A.X + t.B.X) / 2 - px) ^ 2 + ((t.A.Y + t.B.Y) / 2 - py) ^ 2)
                                                                  Return d >= minimo AndAlso d <= massimo
                                                              End Function).ToList()
            If adatti.Count = 0 Then Return Dati.Tratti(_caso.Next(Dati.Tratti.Count))
            Return adatti(_caso.Next(adatti.Count))
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.TrattoACaso", ex)
            Throw
        End Try
    End Function

    Private Sub RicicloTraffico()
        Try
            ' di notte la città si svuota: metà del traffico resta lontano
            Dim attivi As Integer = If(Notte, TRAFFICO \ 2, TRAFFICO)
            Dim k As Integer = 0
            For Each v As Veicolo In Veicoli.Where(Function(x) x.Ruolo = RuoloVeicolo.Traffico).ToList()
                k += 1
                Dim d As Double = DistanzaDa(v.X, v.Y)
                If d > 650 AndAlso k <= attivi Then
                    Dim nuovo As Veicolo = NuovoTraffico(Pos)
                    v.Pos = nuovo.Pos
                    v.Tipo = nuovo.Tipo
                    v.VelocitaVoluta = nuovo.VelocitaVoluta
                    v.Velocita = nuovo.Velocita
                    v.Salute = nuovo.Salute
                    v.UltimaDistanza = -1
                ElseIf k > attivi AndAlso d > 300 AndAlso d < 650 Then
                    ' allontanato: torna lontano
                    Dim t As Tratto = TrattoACaso(Pos, 900, 3000)
                    v.Pos = New Posizione(t, t.Lunghezza / 2, 1)
                    v.UltimaDistanza = -1
                End If
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.RicicloTraffico", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Quanto spazio c'è davanti a un veicolo prima del prossimo nella stessa corsia (e chi è).</summary>
    Private Function SpazioDavanti(v As Veicolo, ByRef velocitaDiChiCeDavanti As Double) As Double
        Try
            Dim minimo As Double = Double.MaxValue
            velocitaDiChiCeDavanti = 0
            Dim corsiaV As Integer = If(v Is Veicolo, Corsia, 0)
            For Each o As Veicolo In Veicoli
                If o Is v OrElse o.Pos.Tratto IsNot v.Pos.Tratto OrElse (o.Ruolo = RuoloVeicolo.Parcheggiato AndAlso Not o.Blocco) Then Continue For
                Dim corsiaO As Integer = If(o Is Veicolo, Corsia, 0)
                ' stessa corsia: stesso senso e stessa corsia, oppure sensi opposti e uno dei due nella corsia dell'altro
                Dim stessa As Boolean = If(o.Pos.Verso = v.Pos.Verso, corsiaO = corsiaV, corsiaO <> corsiaV)
                If Not stessa AndAlso Not o.Blocco Then Continue For
                Dim d As Double = (o.Pos.Distanza - v.Pos.Distanza) * v.Pos.Verso
                If d > 0 AndAlso d < minimo Then
                    minimo = d
                    velocitaDiChiCeDavanti = If(o.Pos.Verso = v.Pos.Verso, Math.Max(0, o.Velocita), -Math.Abs(o.Velocita))
                End If
            Next
            If APiedi AndAlso PosPiedi.Tratto Is v.Pos.Tratto AndAlso v IsNot Veicolo Then
                Dim d As Double = (PosPiedi.Distanza - v.Pos.Distanza) * v.Pos.Verso
                If d > 0 AndAlso d < minimo Then
                    minimo = d
                    velocitaDiChiCeDavanti = 0
                End If
            End If
            Return minimo
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.SpazioDavanti", ex)
            Throw
        End Try
    End Function

    ''' <summary>Muove un veicolo che guida da solo verso la velocità voluta, rispettando chi ha davanti e i semafori.</summary>
    Private Sub GuidaAutomatica(v As Veicolo, dt As Double, voluta As Double, scegli As Func(Of Veicolo, Incrocio, Tratto), Optional rispettaSemafori As Boolean = True)
        Try
            If Tempo < v.FermoFino OrElse v.Blocco Then
                v.Velocita = Math.Max(0, v.Velocita - 8 * dt)
                Return
            End If
            Dim davanti As Double = 0
            Dim spazio As Double = SpazioDavanti(v, davanti)
            If spazio < 7 Then
                voluta = 0
            ElseIf spazio < 18 Then
                voluta = Math.Min(voluta, Math.Max(0, davanti))
                ' chi arriva di fronte nella sua corsia suona e frena
                If davanti < 0 AndAlso v.Ruolo = RuoloVeicolo.Traffico AndAlso Tempo - v.UltimoUrto > 3 Then
                    v.UltimoUrto = Tempo - 1
                    Suona(MotoreAudio.Clacson(), 0.7F, AzimutVerso(v.X, v.Y))
                End If
            End If
            If rispettaSemafori AndAlso v.Pos.Prossimo.Semaforo AndAlso v.Pos.AllIncrocio < 14 AndAlso v.Pos.AllIncrocio > 2 AndAlso Not Verde(v.Pos.Prossimo, v.Pos.Angolo) Then voluta = 0
            If v.Velocita < voluta Then
                v.Velocita = Math.Min(voluta, v.Velocita + v.Tipo.Accelerazione * dt)
            Else
                v.Velocita = Math.Max(voluta, v.Velocita - 7 * dt)
            End If
            Dim resto As Double = v.Pos.Avanza(v.Velocita * dt)
            If resto > 0 OrElse v.Pos.AllIncrocio < 0.01 Then
                Dim incrocio As Incrocio = v.Pos.Prossimo
                Dim t As Tratto = scegli(v, incrocio)
                If t Is Nothing Then
                    v.Velocita = 0
                    Return
                End If
                v.Pos.Imbocca(t, incrocio)
                v.Pos.Avanza(resto)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.GuidaAutomatica", ex)
            Throw
        End Try
    End Sub

    Public Function SvoltaACaso(v As Veicolo, incrocio As Incrocio) As Tratto
        Try
            Dim uscite As List(Of Tratto) = incrocio.Tratti.Where(Function(t) t IsNot v.Pos.Tratto).ToList()
            If uscite.Count = 0 Then Return v.Pos.Tratto
            Dim dritto As Tratto = uscite.FirstOrDefault(Function(t) Math.Abs(Mappa.Differenza(Mappa.AngoloUscita(t, incrocio), v.Pos.Angolo)) < Math.PI / 4)
            If dritto IsNot Nothing AndAlso _caso.NextDouble() < 0.55 Then Return dritto
            Return uscite(_caso.Next(uscite.Count))
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.SvoltaACaso", ex)
            Throw
        End Try
    End Function

    Private Sub PassoTraffico(v As Veicolo, dt As Double)
        Try
            GuidaAutomatica(v, dt, v.VelocitaVoluta, AddressOf SvoltaACaso)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoTraffico", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Segue un percorso di incroci; se è finito, svolta a caso.</summary>
    Public Function SvoltaSulPercorso(v As Veicolo, incrocio As Incrocio) As Tratto
        Try
            Dim i As Integer = v.Percorso.IndexOf(incrocio)
            If i >= 0 AndAlso i + 1 < v.Percorso.Count Then
                Dim t As Tratto = Mappa.TrattoTra(incrocio, v.Percorso(i + 1))
                If t IsNot Nothing Then Return t
            End If
            Return SvoltaACaso(v, incrocio)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.SvoltaSulPercorso", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ polizia
    ''' <summary>Sale il livello di ricercato (e la polizia del quartiere si ricorda di te).</summary>
    Public Sub Ricerca(quante As Integer, motivo As String)
        Try
            If Scuola Then Return
            Dim prima As Integer = Stelle
            Stelle = Math.Min(5, Math.Max(Stelle, 0) + quante)
            _nonVisto = 0
            If Stelle > prima Then
                If Stelle > Partita.MassimoStelle Then Partita.MassimoStelle = Stelle
                If Stelle >= 4 Then _inseguitoDa4 = True
                Dim q As String = Dati.QuartiereDi(X, Y).Id
                Partita.Notorieta(q) = Math.Min(100, Partita.NotorietaDi(q) + 6 * (Stelle - prima))
                Suona(MotoreAudio.Stella(), 0.7F, 0)
                Dici(motivo & " Ricercato: " & TestoStelle() & ".", True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Ricerca", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Porta il ricercato almeno a un certo livello (per le missioni di fuga).</summary>
    Public Sub RicercaAlmeno(livello As Integer, motivo As String)
        Try
            If Stelle >= livello Then Return
            Ricerca(livello - Stelle, motivo)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.RicercaAlmeno", ex)
            Throw
        End Try
    End Sub

    Public Function TestoStelle() As String
        Try
            Select Case Stelle
                Case 0 : Return "nessuna stella"
                Case 1 : Return "una stella"
                Case Else : Return Stelle.ToString() & " stelle"
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.TestoStelle", ex)
            Throw
        End Try
    End Function

    ''' <summary>Toglie le stelle (officina, arresto) e fa sparire posti di blocco ed elicottero.</summary>
    Public Sub Ripulisci()
        Try
            Stelle = 0
            _nonVisto = 0
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Ripulisci", ex)
            Throw
        End Try
    End Sub

    Private Sub Ricercato(dt As Double)
        Try
            Dim volanti As List(Of Veicolo) = Veicoli.Where(Function(v) v.Ruolo = RuoloVeicolo.Polizia).ToList()
            If Stelle = 0 Then
                For Each v As Veicolo In volanti
                    If v.Blocco Then
                        TogliVeicolo(v)
                        Continue For
                    End If
                    v.Ruolo = RuoloVeicolo.Traffico
                    v.VelocitaVoluta = v.Pos.Tratto.Limite / 3.6 * 0.7
                    If v.Sirena IsNot Nothing AndAlso Audio IsNot Nothing Then Audio.Togli(v.Sirena)
                    v.Sirena = Nothing
                Next
                _bloccoMesso = False
                If _elicottero IsNot Nothing Then
                    If Audio IsNot Nothing Then Audio.Togli(_elicottero)
                    _elicottero = Nothing
                End If
                Return
            End If
            ' quante volanti: dipende dalle stelle, dalla notte e da quanto ti conoscono nel quartiere
            Dim notorieta As Double = Partita.NotorietaDi(Dati.QuartiereDi(X, Y).Id)
            Dim volute As Integer = Math.Min(6, CInt(Math.Round((Stelle + If(Stelle >= 2, 1, 0) + If(Notte, 1, 0) + If(notorieta > 50, 1, 0)) * Severita)))
            Dim inseguitori As Integer = volanti.Where(Function(v) Not v.Blocco).Count()
            ' il santino: per un giorno la polizia ti trova più difficilmente
            Dim fortuna As Double = If(Partita.SantinoFino > Partita.Minuti, 0.5, 1.0)
            If inseguitori < volute AndAlso _caso.NextDouble() < dt * 0.5 * (1 + notorieta / 50) * Severita * fortuna Then
                Dim t As Tratto = TrattoACaso(Pos, 280, 520)
                Dim v As New Veicolo With {.Tipo = Dati.Veicoli("polizia"), .Pos = New Posizione(t, t.Lunghezza / 2, 1), .Ruolo = RuoloVeicolo.Polizia, .Salute = 130}
                v.Sirena = New VoceSirena() With {.Volume = 0}
                If Audio IsNot Nothing Then Audio.Aggiungi(v.Sirena)
                Veicoli.Add(v)
            End If
            ' posto di blocco dalle tre stelle: due volanti ferme su una strada davanti a te
            If Stelle >= 3 AndAlso Not _bloccoMesso AndAlso Not APiedi Then MettiBlocco()
            ' elicottero dalle quattro stelle
            If Stelle >= 4 AndAlso _elicottero Is Nothing Then
                _elicottero = New VoceElicottero() With {.Volume = 0.35F}
                If Audio IsNot Nothing Then Audio.Aggiungi(_elicottero)
                Dici("Senti un elicottero sopra di te: ti stanno seguendo dall'alto. In galleria non ti vede.", True)
            End If
            If _elicottero IsNot Nothing Then _elicottero.Azimut = CSng(Math.Sin(Tempo / 9) * Math.PI)
            ' visto o no: se nessuna volante è vicina per un po', si perde una stella; con l'elicottero ci vuole molto di più (in galleria no)
            Dim vicina As Boolean = volanti.Any(Function(v) Not v.Blocco AndAlso DistanzaDa(v.X, v.Y) < 90)
            If vicina Then
                _nonVisto = 0
            Else
                ' ci si nasconde meglio: a piedi nei vicoli, nei quartieri dove la banda ti rispetta
                Dim aiuto As Double = 1.0
                If APiedi AndAlso Not Pos.Tratto.Strada.Arteria Then aiuto *= 1.7
                If Partita.RispettoDi(Dati.QuartiereDi(X, Y).Id) >= 40 Then aiuto *= 1.5
                _nonVisto += dt * aiuto / Severita * If(_elicottero IsNot Nothing AndAlso Not InGalleria, 0.3, 1.0)
                If _nonVisto > 16 + 4 * Stelle Then
                    _nonVisto = 0
                    Stelle -= 1
                    If Stelle = 0 Then
                        Suona(MotoreAudio.Successo(), 0.6F, 0)
                        Dici("Hai seminato la polizia: non ti cerca più nessuno.", True)
                        Notifica("inseguimento")
                        Partita.Conta("seminato")
                        If _inseguitoDa4 Then Partita.Conta("seminato4")
                        If _elicottero IsNot Nothing AndAlso InGalleria Then Partita.Conta("galleria")
                        _inseguitoDa4 = False
                        Missioni.Seminato()
                    Else
                        Dici("Ti cercano di meno: " & TestoStelle() & ".", True)
                    End If
                End If
            End If
            Dim attaccata As Boolean = volanti.Any(Function(v) DistanzaDa(v.X, v.Y) < 7)
            If attaccata AndAlso (APiedi OrElse Math.Abs(Velocita) < 2.5) Then
                _fermoVicinoPolizia += dt
                If _fermoVicinoPolizia > 2.5 Then
                    _fermoVicinoPolizia = 0
                    Arresto()
                End If
            Else
                _fermoVicinoPolizia = 0
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Ricercato", ex)
            Throw
        End Try
    End Sub

    Private Sub MettiBlocco()
        Try
            ' il tratto dopo il prossimo incrocio, nella direzione in cui si va
            Dim p As Posizione = Pos
            Dim uscite As List(Of Tratto) = p.Prossimo.Tratti.Where(Function(t) t IsNot p.Tratto AndAlso Not t.Galleria).ToList()
            If uscite.Count = 0 Then Return
            Dim dritto As Tratto = uscite.OrderBy(Function(t) Math.Abs(Mappa.Differenza(Mappa.AngoloUscita(t, p.Prossimo), p.Angolo))).First()
            For k As Integer = 0 To 1
                Dim v As New Veicolo With {.Tipo = Dati.Veicoli("polizia"), .Ruolo = RuoloVeicolo.Polizia, .Salute = 130, .Blocco = True}
                v.Pos = New Posizione(dritto, dritto.Lunghezza * (0.4 + 0.15 * k), If(dritto.A Is p.Prossimo, -1, 1))
                v.Sirena = New VoceSirena() With {.Volume = 0}
                If Audio IsNot Nothing Then Audio.Aggiungi(v.Sirena)
                Veicoli.Add(v)
            Next
            _bloccoMesso = True
            Dici("Posto di blocco su " & dritto.Strada.Nome & ", dopo il prossimo incrocio!", True)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.MettiBlocco", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoPolizia(v As Veicolo, dt As Double)
        Try
            If v.Blocco Then Return
            If Tempo >= v.ProssimoCalcolo Then
                v.ProssimoCalcolo = Tempo + 1.5
                Dim p As Posizione = Pos
                v.Percorso = Mappa.Percorso(Dati, v.Pos, p.Tratto, p.Distanza)
            End If
            Dim d As Double = DistanzaDa(v.X, v.Y)
            Dim voluta As Double = v.Tipo.Velocita * (0.42 + 0.08 * Stelle) * (0.85 + 0.15 * Severita)
            If d < 25 Then voluta = Math.Max(Math.Abs(Velocita) + 2, 6)
            GuidaAutomatica(v, dt, voluta, AddressOf SvoltaSulPercorso, False)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoPolizia", ex)
            Throw
        End Try
    End Sub

    Private Sub Arresto()
        Try
            Dim multa As Integer = Math.Max(100, Partita.Soldi \ 10)
            multa = Math.Min(multa, Partita.Soldi)
            Partita.Soldi -= multa
            Partita.Arresti += 1
            Dim q As String = Dati.QuartiereDi(X, Y).Nome
            Ripulisci()
            For Each v As Veicolo In Veicoli.Where(Function(x) x.Ruolo = RuoloVeicolo.Polizia).ToList()
                TogliVeicolo(v)
            Next
            Dim sequestro As String = ""
            If Not APiedi Then
                Dim sequestrata As Veicolo = Veicolo
                sequestro = Economia.Sequestro(sequestrata)
                Scendi()
                If Not sequestrata.Tuo Then Veicoli.Remove(sequestrata)
            End If
            Dim commissariato As Luogo = Dati.Luoghi.Values.Where(Function(l) l.Tipo = "commissariato").OrderBy(Function(l) DistanzaDa(l.X, l.Y)).FirstOrDefault()
            If commissariato IsNot Nothing Then PosPiedi = New Posizione(commissariato.Tratto, commissariato.Distanza, 1)
            _inseguitoDa4 = False
            Suona(MotoreAudio.Registrato("manette"), 0.7F, 0)
            Suona(MotoreAudio.Fallimento(), 0.8F, 0)
            Dici("Arrestato! Ti portano al commissariato" & If(commissariato IsNot Nothing, ", " & commissariato.Nome, "") & ". Paghi " & multa.ToString() & " euro di multa e ti rilasciano." & sequestro, True)
            Notifica("arresto", "", q)
            Sfide.Annulla("Ti hanno arrestato.")
            Missioni.Arrestato()
            Lavori.Arrestato()
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Arresto", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ i veicoli delle missioni e dei lavori
    Public Function CreaVeicolo(tipo As String, luogo As Luogo, ruolo As RuoloVeicolo) As Veicolo
        Try
            Dim t As TipoVeicolo = Nothing
            If Not Dati.Veicoli.TryGetValue(tipo, t) Then t = Dati.Veicoli("berlina")
            Dim v As New Veicolo With {.Tipo = t, .Pos = New Posizione(luogo.Tratto, luogo.Distanza, 1), .Ruolo = ruolo, .Salute = t.Robustezza}
            Veicoli.Add(v)
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.CreaVeicolo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un veicolo su un tratto preciso (per le bande e i nemici delle scorte).</summary>
    Public Function CreaVeicoloSu(tipo As String, t As Tratto, distanza As Double, ruolo As RuoloVeicolo) As Veicolo
        Try
            Dim tv As TipoVeicolo = Nothing
            If Not Dati.Veicoli.TryGetValue(tipo, tv) Then tv = Dati.Veicoli("berlina")
            Dim v As New Veicolo With {.Tipo = tv, .Pos = New Posizione(t, Math.Max(0, Math.Min(t.Lunghezza, distanza)), If(_caso.Next(2) = 0, 1, -1)), .Ruolo = ruolo, .Salute = tv.Robustezza}
            Veicoli.Add(v)
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.CreaVeicoloSu", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una macchina di banda: insegue la preda (o il giocatore) e la sperona.</summary>
    Private Sub PassoBanda(v As Veicolo, dt As Double)
        Try
            Dim preda As Veicolo = v.Preda
            Dim px As Double, py As Double
            Dim pt As Tratto, pd As Double
            If preda IsNot Nothing Then
                If preda.Ruolo = RuoloVeicolo.Relitto OrElse Not Veicoli.Contains(preda) Then
                    v.Ruolo = RuoloVeicolo.Traffico
                    v.VelocitaVoluta = v.Pos.Tratto.Limite / 3.6 * 0.7
                    Return
                End If
                px = preda.X : py = preda.Y : pt = preda.Pos.Tratto : pd = preda.Pos.Distanza
            Else
                px = X : py = Y : pt = Pos.Tratto : pd = Pos.Distanza
            End If
            If Tempo >= v.ProssimoCalcolo Then
                v.ProssimoCalcolo = Tempo + 1.5
                v.Percorso = Mappa.Percorso(Dati, v.Pos, pt, pd)
            End If
            Dim d As Double = Math.Sqrt((v.X - px) ^ 2 + (v.Y - py) ^ 2)
            Dim vp As Double = If(preda IsNot Nothing, Math.Abs(preda.Velocita), Math.Abs(Velocita))
            Dim voluta As Double = If(d < 25, vp + 3, v.Tipo.Velocita * 0.72)
            GuidaAutomatica(v, dt, voluta, AddressOf SvoltaSulPercorso, False)
            ' contro un'altra macchina (la scorta): la sperona da sola
            If preda IsNot Nothing AndAlso d < 6 AndAlso Tempo - v.UltimoUrto > 1.6 Then
                v.UltimoUrto = Tempo
                preda.Salute -= 9
                Suona(MotoreAudio.Urto(False), CSng(Math.Min(0.8, 0.3 + 20 / Math.Max(10, DistanzaDa(v.X, v.Y)))), AzimutVerso(v.X, v.Y))
                Missioni.AlleatoColpito(preda)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoBanda", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ il radar sonoro
    ''' <summary>
    ''' A: il radar. Un giro di bip intorno a te, uno per veicolo entro 150 metri, ognuno dalla sua direzione, a partire da davanti e in
    ''' senso orario: acuto doppio la polizia, acuto le bande e le auto da seguire, medio il traffico, basso le auto ferme. Più è vicino,
    ''' più è forte.
    ''' </summary>
    Public Sub Radar()
        Try
            _radar.Clear()
            Dim vicini As List(Of Veicolo) = Veicoli.Where(Function(v) v IsNot Veicolo AndAlso v.Ruolo <> RuoloVeicolo.Relitto AndAlso DistanzaDa(v.X, v.Y) < 150).
                OrderBy(Function(v)
                            ' in senso orario partendo da davanti: l'azimut è positivo a sinistra
                            Dim a As Double = -AzimutVerso(v.X, v.Y)
                            If a < 0 Then a += 2 * Math.PI
                            Return a
                        End Function).Take(14).ToList()
            If vicini.Count = 0 Then
                Dici("Radar: intorno non c'è nessun veicolo.", True)
                Return
            End If
            Dim polizia As Integer = vicini.Where(Function(v) v.Ruolo = RuoloVeicolo.Polizia).Count()
            Dici("Radar: " & vicini.Count.ToString() & If(vicini.Count = 1, " veicolo", " veicoli") & If(polizia > 0, ", di cui " & polizia.ToString() & " della polizia", "") & ".", True)
            Dim t As Double = Tempo + 1.6
            For Each v As Veicolo In vicini
                Dim f As Double
                Select Case v.Ruolo
                    Case RuoloVeicolo.Polizia : f = 1760
                    Case RuoloVeicolo.Banda, RuoloVeicolo.Bersaglio : f = 1320
                    Case RuoloVeicolo.Parcheggiato : f = 440
                    Case Else : f = 880
                End Select
                Dim vol As Single = CSng(Math.Min(0.8, 0.2 + 20 / Math.Max(15, DistanzaDa(v.X, v.Y))))
                _radar.Enqueue((t, MotoreAudio.BipRadar(f), AzimutVerso(v.X, v.Y), vol))
                If v.Ruolo = RuoloVeicolo.Polizia Then _radar.Enqueue((t + 0.09, MotoreAudio.BipRadar(f), AzimutVerso(v.X, v.Y), vol))
                t += 0.32
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Radar", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoRadar()
        Try
            While _radar.Count > 0 AndAlso _radar.Peek().Quando <= Tempo
                Dim b = _radar.Dequeue()
                Suona(b.Voce, b.Volume, b.Azimut)
            End While
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoRadar", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ gli oggetti nascosti
    ''' <summary>L'oggetto nascosto non ancora trovato più vicino (Nothing se li hai trovati tutti).</summary>
    Public Function NascostoPiuVicino() As Nascosto
        Try
            Return Dati.Nascosti.Where(Function(n) Not Partita.Nascosti.Contains(n.Id)).OrderBy(Function(n) DistanzaDa(n.X, n.Y)).FirstOrDefault()
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.NascostoPiuVicino", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Un oggetto nascosto entro 90 metri si fa sentire con un luccichio dalla sua direzione, sempre più spesso man mano che ci si avvicina;
    ''' si prende passandoci sopra (a piedi, o in macchina andando piano).
    ''' </summary>
    Private Sub PassoNascosti()
        Try
            Dim n As Nascosto = NascostoPiuVicino()
            If n Is Nothing Then Return
            Dim d As Double = DistanzaDa(n.X, n.Y)
            If d > 90 Then Return
            If Tempo >= _prossimoLuccichio Then
                _prossimoLuccichio = Tempo + Math.Max(0.35, d / 40)
                Suona(MotoreAudio.Luccichio(), CSng(Math.Min(0.7, 0.15 + 12 / Math.Max(8, d))), AzimutVerso(n.X, n.Y))
            End If
            If d < If(APiedi, 5, 8) AndAlso (APiedi OrElse Math.Abs(Velocita) < 4) Then
                Partita.Nascosti.Add(n.Id)
                Partita.Guadagna(300)
                Suona(MotoreAudio.Successo(), 0.7F)
                Dim restano As Integer = Dati.Nascosti.Count - Partita.Nascosti.Count
                Dici("Hai trovato " & n.Nome & "! " & n.Storia & " Ti frutta 300 euro. " & If(restano = 0, "Li hai trovati tutti!", "Ne restano " & restano.ToString() & "."), True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoNascosti", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Prima di salvare: la benzina dei tuoi veicoli e la merce che hai a bordo (o sul primo dei tuoi veicoli che ne ha).</summary>
    Public Sub PreparaSalvataggio()
        Try
            For Each v As Veicolo In Veicoli.Where(Function(x) x.Tuo)
                Partita.Benzina(v.Tipo.Id) = Math.Round(v.Benzina)
            Next
            Partita.Carico.Clear()
            Dim conMerce As Veicolo = If(Veicolo IsNot Nothing AndAlso Veicolo.Carico.Count > 0, Veicolo, Veicoli.FirstOrDefault(Function(x) x.Tuo AndAlso x.Carico.Count > 0))
            If conMerce IsNot Nothing Then
                For Each kv As KeyValuePair(Of String, Integer) In conMerce.Carico
                    Partita.Carico(kv.Key) = kv.Value
                Next
            End If
            Partita.Stato = StatoPartita.Fotografa(Me)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PreparaSalvataggio", ex)
            Throw
        End Try
    End Sub

    ''' <summary>La registrazione del motore di un tipo di veicolo.</summary>
    Public Shared Function NomeMotore(tipo As TipoVeicolo, Optional salute As Double = 1) As String
        Try
            If salute < 0.3 AndAlso Not tipo.DueRuote Then Return "motore_guasto"
            Select Case tipo.Id
                Case "utilitaria" : Return "motore_utilitaria"
                Case "sportiva" : Return "motore_sportiva"
                Case "furgone", "ambulanza", "pickup", "monovolume" : Return "motore_furgone"
                Case "suv" : Return "motore_fuoristrada"
                Case "autobus" : Return "motore_camion"
                Case "muscle" : Return "motore_muscle"
                Case "motocarro" : Return "motore_motocarro"
                Case "fuoristrada" : Return "motore_fuoristrada"
                Case "camion" : Return "motore_camion"
                Case "moto" : Return "motore_moto"
                Case "scooter" : Return "motore_scooter"
                Case Else : Return "motore_berlina"
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.NomeMotore", ex)
            Throw
        End Try
    End Function

    Public Sub TogliVeicolo(v As Veicolo)
        Try
            If v Is Nothing Then Return
            If v.Sirena IsNot Nothing AndAlso Audio IsNot Nothing Then Audio.Togli(v.Sirena)
            v.Sirena = Nothing
            If v Is Veicolo Then Return
            Veicoli.Remove(v)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.TogliVeicolo", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoBersaglio(v As Veicolo, dt As Double)
        Try
            If v.InFuga Then
                GuidaAutomatica(v, dt, v.Pos.Tratto.Limite / 3.6 * 1.05, AddressOf SvoltaInFuga, False)
                Return
            End If
            If v.Meta Is Nothing Then
                v.Velocita = 0
                Return
            End If
            If Tempo >= v.ProssimoCalcolo Then
                v.ProssimoCalcolo = Tempo + 3
                v.Percorso = Mappa.Percorso(Dati, v.Pos, v.Meta.Tratto, v.Meta.Distanza)
            End If
            Dim voluta As Double = If(v.VelocitaVoluta > 0, v.VelocitaVoluta, v.Pos.Tratto.Limite / 3.6 * 0.62)
            If v.Pos.Tratto Is v.Meta.Tratto AndAlso Math.Abs(v.Pos.Distanza - v.Meta.Distanza) < 8 Then voluta = 0
            GuidaAutomatica(v, dt, voluta, AddressOf SvoltaSulPercorso, v.VelocitaVoluta = 0)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.PassoBersaglio", ex)
            Throw
        End Try
    End Sub

    Private Function SvoltaInFuga(v As Veicolo, incrocio As Incrocio) As Tratto
        Try
            Dim uscite As List(Of Tratto) = incrocio.Tratti.Where(Function(t) t IsNot v.Pos.Tratto).ToList()
            If uscite.Count = 0 Then Return v.Pos.Tratto
            If _caso.NextDouble() < 0.25 Then Return uscite(_caso.Next(uscite.Count))
            Return uscite.OrderByDescending(Function(t)
                                                Dim a As Incrocio = t.Altro(incrocio)
                                                Return (a.X - X) ^ 2 + (a.Y - Y) ^ 2
                                            End Function).First()
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.SvoltaInFuga", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ navigatore
    ''' <summary>La meta che adesso conta: quella scelta col navigatore o quella di un lavoro, di una consegna o di una missione.</summary>
    Public Function MetaAttiva() As Luogo
        Try
            Return If(Meta, If(Lavori.MetaAttuale, If(Economia.MetaAttuale, Missioni.LuogoDaRaggiungere)))
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.MetaAttiva", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Sono arrivato: se la meta è qui vicino (entro sessanta metri) ci si ferma proprio davanti e il lavoro, la consegna o la missione
    ''' vanno avanti come se ci si fosse fermati a mano; così non serve centrare il punto giusto con la macchina.
    ''' </summary>
    Public Sub ConfermaArrivo()
        Try
            Dim l As Luogo = MetaAttiva()
            If l Is Nothing Then
                Dici("Adesso non devi arrivare da nessuna parte.", True)
                Return
            End If
            Dim d As Double = DistanzaDa(l.X, l.Y)
            If d > 60 Then
                Dici(l.Nome & " è ancora a " & Mappa.Metri(d) & ", " & LatoA(l.X, l.Y) & ".", True)
                Return
            End If
            If APiedi Then
                PosPiedi = New Posizione(l.Tratto, l.Distanza, PosPiedi.Verso)
            Else
                Veicolo.Pos = New Posizione(l.Tratto, l.Distanza, Veicolo.Pos.Verso)
                Veicolo.Velocita = 0
            End If
            Suona(MotoreAudio.Successo(), 0.5F, 0)
            Dici("Sei arrivato: " & l.Nome & ", sulla " & l.Lato & ".", True)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.ConfermaArrivo", ex)
            Throw
        End Try
    End Sub

    Public Sub ImpostaMeta(l As Luogo, Optional silenzioso As Boolean = False)
        Try
            Meta = l
            _ricalcolo = 0
            _percorso.Clear()
            If l IsNot Nothing AndAlso Not silenzioso Then Dici("Navigatore: " & l.Nome & ", " & l.Quartiere.Nome & ". " & Indicazione(), True)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.ImpostaMeta", ex)
            Throw
        End Try
    End Sub

    ''' <summary>La svolta che il navigatore consiglia a questo incrocio.</summary>
    Private Function SvoltaNavigatore(incrocio As Incrocio, svolte As List(Of Svolta)) As Svolta
        Try
            If Meta Is Nothing Then Return Nothing
            Dim i As Integer = _percorso.IndexOf(incrocio)
            If i < 0 AndAlso Pos.Prossimo Is incrocio Then
                _percorso = Mappa.Percorso(Dati, Pos, Meta.Tratto, Meta.Distanza)
                _ricalcolo = 2
                i = _percorso.IndexOf(incrocio)
            End If
            If i >= 0 AndAlso i + 1 < _percorso.Count Then
                Dim t As Tratto = Mappa.TrattoTra(incrocio, _percorso(i + 1))
                Return svolte.FirstOrDefault(Function(s) s.Tratto Is t)
            End If
            If i >= 0 AndAlso i = _percorso.Count - 1 Then Return svolte.FirstOrDefault(Function(s) s.Tratto Is Meta.Tratto)
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.SvoltaNavigatore", ex)
            Throw
        End Try
    End Function

    ''' <summary>Cosa dice il navigatore adesso.</summary>
    Public Function Indicazione() As String
        Try
            If Meta Is Nothing Then Return "Nessuna meta: il navigatore è spento."
            Dim d As Double = DistanzaDa(Meta.X, Meta.Y)
            If d < DISTANZA_LUOGO Then Return "Sei arrivato: " & Meta.Nome & ", sulla " & Meta.Lato & "."
            Dim p As Posizione = Pos
            If _percorso.Count = 0 Then _percorso = Mappa.Percorso(Dati, p, Meta.Tratto, Meta.Distanza)
            If _percorso.Count = 0 Then Return Meta.Nome & " è più avanti su questa strada, tra " & Mappa.Metri(Math.Abs(Meta.Distanza - p.Distanza)) & ", sulla " & Meta.Lato & "."
            If p.Tratto Is Meta.Tratto AndAlso (Meta.Distanza - p.Distanza) * p.Verso < 0 Then Return "La meta è dietro di te: torna indietro. Mancano " & Mappa.Metri(d) & " in linea d'aria."
            Dim s As Svolta = SvoltaNavigatore(p.Prossimo, Mappa.Svolte(p.Prossimo, p.Tratto, p.Angolo))
            Dim testo As String = "Tra " & Mappa.Metri(p.AllIncrocio) & ", "
            If s Is Nothing Then
                testo &= "all'incrocio torna indietro"
            ElseIf s.Verso = 0 Then
                testo &= "vai dritto su " & s.Tratto.Strada.Nome
            Else
                testo &= "gira " & s.Nome & " in " & s.Tratto.Strada.Nome
            End If
            Return testo & ". Mancano " & Mappa.Metri(d) & " in linea d'aria."
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Indicazione", ex)
            Throw
        End Try
    End Function

    ''' <summary>Solo semaforo e rotonda del prossimo incrocio (la svolta la dice GuidaSvolta); "" se non c'è niente da dire.</summary>
    Private Function IncrocioBreve(p As Posizione) As String
        Try
            Dim incrocio As Incrocio = p.Prossimo
            Dim t As String = ""
            If incrocio.Rotonda Then t &= "Rotonda, rallenta. "
            If incrocio.Semaforo Then t &= If(Verde(incrocio, p.Angolo), "Semaforo verde.", "Semaforo rosso.")
            Return t.Trim()
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.IncrocioBreve", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' La guida alla svolta del navigatore, in macchina: una voce da lontano ("Tra centocinquanta metri gira a sinistra"), una da vicino
    ''' ("Tra sessanta metri, a sinistra") e poi tic sempre più fitti, dalla parte della svolta, fino all'incrocio; lì si sente un
    ''' doppio tono (AttraversaIncrocio). Vero se si è nel conto alla rovescia (allora il bip della direzione tace, per non confondere).
    ''' </summary>
    Private Function GuidaSvolta() As Boolean
        Try
            If APiedi OrElse Veicolo Is Nothing OrElse Meta Is Nothing Then
                _navIncrocio = Nothing
                Return False
            End If
            Dim p As Posizione = Veicolo.Pos
            Dim incrocio As Incrocio = p.Prossimo
            If incrocio IsNot _navIncrocio Then
                _navIncrocio = incrocio
                _navFasi = 0
            End If
            If Veicolo.Velocita < 0.5 Then Return False
            Dim prossimaSvolta As Svolta = SvoltaNavigatore(incrocio, Mappa.Svolte(incrocio, p.Tratto, p.Angolo))
            If prossimaSvolta Is Nothing OrElse prossimaSvolta.Verso = 0 OrElse prossimaSvolta.Verso = 2 Then Return False
            Dim d As Double = p.AllIncrocio
            Dim vel As Double = Math.Max(5, Veicolo.Velocita)
            Dim lontano As Double = Math.Max(170, vel * 9)
            Dim vicino As Double = Math.Max(65, vel * 4)
            Dim lato As Single = If(prossimaSvolta.Verso < 0, 1.2F, -1.2F)
            Dim arrotondata As Double = Math.Max(10, Math.Round(d / 10) * 10)
            If d < lontano AndAlso d >= vicino AndAlso (_navFasi And 1) = 0 Then
                _navFasi = _navFasi Or 1
                Dici("Tra " & Mappa.Metri(arrotondata) & " gira " & prossimaSvolta.Nome & ".")
            End If
            If d < vicino Then
                If (_navFasi And 2) = 0 Then
                    _navFasi = _navFasi Or 3
                    Dici("Tra " & Mappa.Metri(arrotondata) & ", " & prossimaSvolta.Nome & ".", True)
                End If
                If Tempo >= _prossimoTicSvolta Then
                    _prossimoTicSvolta = Tempo + Math.Max(0.14, Math.Min(0.9, d / (vel * 2.2)))
                    Suona(MotoreAudio.TicSvolta(), 0.6F, lato)
                End If
                Return True
            End If
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.GuidaSvolta", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Entrando a meno di quarantacinque metri dalla meta (del navigatore, di una missione, di un lavoro) si sente il suono dell'arrivo e si
    ''' sente "Sei arrivato"; se poi ci si ferma lì vicino il veicolo si mette proprio sul punto, così non serve centrarlo.
    ''' </summary>
    Private Sub ArrivoAutomatico()
        Try
            If Scuola OrElse Barca IsNot Nothing Then Return
            Dim l As Luogo = MetaAttiva()
            If l Is Nothing OrElse Lavori.Tipo = "corsa" Then
                _arrivoLuogo = Nothing
                Return
            End If
            Dim d As Double = DistanzaDa(l.X, l.Y)
            If _arrivoLuogo IsNot l Then
                If d >= 45 Then Return
                _arrivoLuogo = l
                _arrivoSnap = False
                Suona(MotoreAudio.Successo(), 0.5F, 0)
                Dici("Sei arrivato: " & l.Nome & ", sulla " & l.Lato & "." & If(Not APiedi AndAlso Math.Abs(Velocita) > 3, " Fermati.", ""), True)
            ElseIf d > 150 Then
                _arrivoLuogo = Nothing
                Return
            End If
            If _arrivoSnap OrElse APiedi OrElse Veicolo Is Nothing OrElse d >= 45 OrElse d < DISTANZA_LUOGO - 2 OrElse Math.Abs(Veicolo.Velocita) >= 3 Then Return
            Select Case Missioni.TipoPasso
                Case "segui", "insegui", "scorta", "raccogli", "furtivo", "barca", "fuggi"
                    Return
            End Select
            If Sfide.InCorso Then Return
            _arrivoSnap = True
            Veicolo.Pos = New Posizione(l.Tratto, l.Distanza, Veicolo.Pos.Verso)
            Veicolo.Velocita = 0
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.ArrivoAutomatico", ex)
            Throw
        End Try
    End Sub

    Private Sub Navigatore(dt As Double)
        Try
            If Meta Is Nothing Then Return
            _ricalcolo -= dt
            If _ricalcolo <= 0 Then
                _ricalcolo = 2
                _percorso = Mappa.Percorso(Dati, Pos, Meta.Tratto, Meta.Distanza)
            End If
            Dim d As Double = DistanzaDa(Meta.X, Meta.Y)
            Dim vicinoAllaSvolta As Boolean = GuidaSvolta()
            If Tempo >= _prossimoBip AndAlso Not vicinoAllaSvolta Then
                _prossimoBip = Tempo + Math.Max(0.8, Math.Min(2.4, d / 120))
                Dim tx As Double = Meta.X
                Dim ty As Double = Meta.Y
                If _percorso.Count > 0 Then
                    tx = _percorso(0).X
                    ty = _percorso(0).Y
                    If DistanzaDa(tx, ty) < 6 AndAlso _percorso.Count > 1 Then
                        tx = _percorso(1).X
                        ty = _percorso(1).Y
                    End If
                End If
                Suona(MotoreAudio.Bip(Davanti(tx, ty)), 0.3F, AzimutVerso(tx, ty))
            End If
            If d < DISTANZA_LUOGO Then
                Dim l As Luogo = Meta
                If Not Missioni.MetaDellaMissione(l) AndAlso Not Lavori.MetaDelLavoro(l) AndAlso Not Sfide.MetaDellaSfida(l) AndAlso Not Economia.MetaDelLavoro(l) AndAlso Not Eventi.MetaDelloEvento(l) Then
                    Meta = Nothing
                    Dim detto As Boolean = _arrivoLuogo Is l
                    If Not detto Then Suona(MotoreAudio.Successo(), 0.5F, 0)
                    Dici(If(detto, "", "Sei arrivato: " & l.Nome & ", sulla " & l.Lato & ". ") & l.Descrizione)
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Navigatore", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ cambi di quartiere
    Private Sub CambioZona()
        Try
            Dim q As Quartiere = Dati.QuartiereDi(X, Y)
            If q IsNot _ultimoQuartiere Then
                _ultimoQuartiere = q
                Dici("Entri nel quartiere " & q.Nome & "." & If(Imp.AnnunciEssenziali, "", " " & q.Descrizione & " " & q.Suoni))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.CambioZona", ex)
            Throw
        End Try
    End Sub

    ''' <summary>D: dove sei.</summary>
    Public Function DoveSei() As String
        Try
            Dim p As Posizione = Pos
            Dim testo As String = p.Tratto.Strada.Nome & If(p.Tratto.Galleria, ", in galleria", "") & If(p.Tratto.Ponte, ", sul ponte", "") & ", quartiere " & Dati.QuartiereDi(X, Y).Nome & ", verso " & Mappa.Cardinale(p.Angolo) & ". "
            If APiedi Then
                testo &= "A piedi."
            Else
                Dim su As String = Veicolo.Tipo.ConSu()
                testo &= Char.ToUpper(su(0)) & su.Substring(1) & ", a " & CInt(Math.Abs(Veicolo.Velocita) * 3.6).ToString() & " all'ora" & If(Corsia = 1, ", nella corsia opposta", "") & "."
            End If
            Dim l As Luogo = LuogoVicino()
            If l IsNot Nothing Then testo &= " Qui c'è " & l.Nome & ", sulla " & l.Lato & "."
            Return testo
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.DoveSei", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il luogo più vicino a portata di mano (Nothing se non ce n'è).</summary>
    Public Function LuogoVicino() As Luogo
        Try
            Return Dati.Luoghi.Values.Where(Function(l) DistanzaDa(l.X, l.Y) < DISTANZA_LUOGO).OrderBy(Function(l) DistanzaDa(l.X, l.Y)).FirstOrDefault()
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.LuogoVicino", ex)
            Throw
        End Try
    End Function

    ''' <summary>L: i luoghi più vicini, con distanza e direzione.</summary>
    Public Function LuoghiVicini() As String
        Try
            Dim vicini As List(Of Luogo) = Dati.Luoghi.Values.OrderBy(Function(l) DistanzaDa(l.X, l.Y)).Take(5).ToList()
            Return String.Join(". ", vicini.Select(Function(l) l.Nome & ", " & Mappa.Metri(DistanzaDa(l.X, l.Y)) & " " & LatoA(l.X, l.Y))) & "."
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.LuoghiVicini", ex)
            Throw
        End Try
    End Function

    ''' <summary>Q: chi c'è intorno (veicoli in movimento, polizia, auto da prendere).</summary>
    Public Function Intorno() As String
        Try
            Dim vicini As List(Of Veicolo) = Veicoli.Where(Function(v) v IsNot Veicolo AndAlso v.Ruolo <> RuoloVeicolo.Relitto AndAlso DistanzaDa(v.X, v.Y) < 80).OrderBy(Function(v) DistanzaDa(v.X, v.Y)).Take(5).ToList()
            If vicini.Count = 0 Then Return "Intorno non c'è nessuno."
            Dim pezzi As New List(Of String)()
            For Each v As Veicolo In vicini
                Dim cosa As String
                Select Case v.Ruolo
                    Case RuoloVeicolo.Polizia : cosa = If(v.Blocco, "posto di blocco", "volante della polizia")
                    Case RuoloVeicolo.Parcheggiato : cosa = v.Tipo.Nome & If(v.Tipo.Maschile, " parcheggiato", " parcheggiata") & If(v.Tuo, If(v.Tipo.Maschile, ", il tuo", ", la tua"), "") & If(v Is Missioni.VeicoloDaPrendere, ", quella della missione", "")
                    Case RuoloVeicolo.Bersaglio : cosa = "l'auto da tenere d'occhio, " & v.Tipo.Nome
                    Case Else : cosa = v.Tipo.Nome
                End Select
                pezzi.Add(cosa & ", " & Mappa.Metri(DistanzaDa(v.X, v.Y)) & " " & LatoA(v.X, v.Y))
            Next
            Return String.Join(". ", pezzi) & "."
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Intorno", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ il sensore e i suoni che seguono il gioco
    ''' <summary>Metri al prossimo veicolo davanti nella tua corsia, se ti stai avvicinando (MaxValue se niente); frontale: arriva di fronte.</summary>
    Public Function OstacoloDavanti(Optional ByRef frontale As Boolean = False) As Double
        Try
            frontale = False
            If APiedi OrElse Veicolo Is Nothing Then Return Double.MaxValue
            Dim davanti As Double = 0
            Dim spazio As Double = SpazioDavanti(Veicolo, davanti)
            If spazio > If(Corsia = 1, 80, 45) OrElse Veicolo.Velocita - davanti < 1 Then Return Double.MaxValue
            frontale = davanti < 0
            Return spazio
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.OstacoloDavanti", ex)
            Throw
        End Try
    End Function

    Private Sub Sensore()
        Try
            Dim frontale As Boolean
            Dim spazio As Double = OstacoloDavanti(frontale)
            If spazio = Double.MaxValue OrElse Tempo < _prossimoSensore Then Return
            _prossimoSensore = Tempo + Math.Max(0.12, spazio / 60)
            Suona(If(frontale, MotoreAudio.Allarme(), MotoreAudio.Tic()), CSng(Math.Min(0.8, 0.25 + 8 / Math.Max(4, spazio))), 0)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Sensore", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Il bip di prossimità: un veicolo che si avvicina di lato o da dietro entro sedici metri fa un bip squadrato dalla sua parte (cassa
    ''' destra o sinistra), sempre più fitto man mano che si avvicina. Davanti nella propria corsia ci pensa il sensore.
    ''' </summary>
    Private Sub Prossimita()
        Try
            If APiedi OrElse Veicolo Is Nothing Then Return
            If _distProssimita.Count > 300 Then _distProssimita.Clear()
            Dim migliore As Veicolo = Nothing
            Dim dMigliore As Double = Double.MaxValue
            Dim azMigliore As Single = 0
            For Each o As Veicolo In Veicoli
                If o Is Veicolo OrElse o.Ruolo = RuoloVeicolo.Relitto OrElse (o.Ruolo = RuoloVeicolo.Parcheggiato AndAlso Not o.Blocco) Then Continue For
                Dim d As Double = DistanzaDa(o.X, o.Y)
                If d > 16 Then
                    _distProssimita.Remove(o)
                    Continue For
                End If
                Dim prima As Double
                Dim siAvvicina As Boolean = Not _distProssimita.TryGetValue(o, prima) OrElse d < prima - 0.02
                _distProssimita(o) = d
                If Not siAvvicina Then Continue For
                Dim a As Single = AzimutVerso(o.X, o.Y)
                If Math.Abs(a) < 0.3 Then Continue For
                If d < dMigliore Then
                    dMigliore = d
                    migliore = o
                    azMigliore = a
                End If
            Next
            If migliore Is Nothing OrElse Tempo < _prossimaProssimita Then Return
            _prossimaProssimita = Tempo + Math.Max(0.09, Math.Min(0.5, dMigliore / 30))
            Dim lato As Single = Math.Sign(azMigliore) * Math.Min(1.5F, Math.Max(1.0F, Math.Abs(azMigliore)))
            Suona(MotoreAudio.Prossimita(), CSng(Math.Min(0.85, 0.35 + 6 / Math.Max(3, dMigliore))), lato)
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Prossimita", ex)
            Throw
        End Try
    End Sub

    Private Sub AggiornaSuoni(dt As Double)
        Try
            If Not APiedi Then
                Sensore()
                Prossimita()
            End If
            If Audio Is Nothing Then Return
            ' eco: forte in galleria, media nei vicoli, poca all'aperto e sul mare
            Dim t As Tratto = Pos.Tratto
            Dim q As String = Dati.QuartiereDi(X, Y).Id
            Audio.Eco = If(t.Galleria, 0.6F, If(t.Ponte OrElse Y < 60, 0.03F, If(Not t.Strada.Arteria AndAlso (q = "centro" OrElse q = "castello" OrElse q = "giardini"), 0.25F, 0.1F)))
            If APiedi OrElse Veicolo Is Nothing Then
                _motore.Volume = 0
                If _abitacolo IsNot Nothing Then _abitacolo.Volume = 0
            Else
                _motore.Volume = 0.38F
                _motore.Azimut = 0
                _motore.Spaziale = False
                _motore.Registrazione = NomeMotore(Veicolo.Tipo, Math.Max(0, Veicolo.Salute) / Robustezza(Veicolo))
                _motore.Timbro = CSng(Veicolo.Tipo.Suono)
                Dim giri As Double = Math.Min(1, Math.Abs(Veicolo.Velocita) / Veicolo.Tipo.Velocita)
                _motore.Giri = CSng(giri)
                _motore.Gas = _gasGiocatore
                _motore.Sforzo = CSng(Math.Max(0, Math.Min(1, Veicolo.Pos.Tratto.Pendenza * Veicolo.Pos.Verso * 12)))
                ' il rumore della strada e del vento, che cresce con la velocità (in moto si sente di più)
                If _abitacolo IsNot Nothing Then
                    _abitacolo.Volume = CSng(giri * If(Veicolo.Tipo.DueRuote, 0.2, 0.1))
                    _abitacolo.Velocita = CSng(0.75 + 0.5 * giri)
                End If
            End If
            _ambiente.Mare = CSng(Math.Max(0, 1 - Y / 220))
            _ambiente.Volume = If(InGalleria, 0.04F, If(Notte, 0.07F, 0.12F))
            _pioggia.Volume = If(Pioggia AndAlso Not InGalleria, 0.2F, 0)
            _pioggia.InMacchina = Not APiedi
            Dim vicini As List(Of Veicolo) = Veicoli.Where(Function(v) (v.Ruolo = RuoloVeicolo.Traffico OrElse v.Ruolo = RuoloVeicolo.Bersaglio OrElse v.Ruolo = RuoloVeicolo.Polizia OrElse v.Ruolo = RuoloVeicolo.Banda) AndAlso v IsNot Veicolo).
                OrderBy(Function(v) DistanzaDa(v.X, v.Y)).Take(_motoriTraffico.Count).ToList()
            For i As Integer = 0 To _motoriTraffico.Count - 1
                Dim voce As VoceMotore = _motoriTraffico(i)
                If i >= vicini.Count Then
                    voce.Volume = 0
                    Continue For
                End If
                Dim v As Veicolo = vicini(i)
                Dim d As Double = DistanzaDa(v.X, v.Y)
                voce.Volume = If(d > 100, 0, CSng(0.22 / (1 + d / 10)))
                voce.Azimut = AzimutVerso(v.X, v.Y)
                voce.Registrazione = NomeMotore(v.Tipo)
                voce.Timbro = CSng(v.Tipo.Suono)
                voce.Giri = CSng(Math.Min(1, Math.Abs(v.Velocita) / v.Tipo.Velocita))
                voce.Doppler = Doppler(v, d, dt)
            Next
            For Each v As Veicolo In Veicoli
                If v.Sirena Is Nothing Then Continue For
                Dim d As Double = DistanzaDa(v.X, v.Y)
                v.Sirena.Volume = CSng(0.45 / (1 + d / 30))
                v.Sirena.Azimut = AzimutVerso(v.X, v.Y)
                v.Sirena.Doppler = Doppler(v, d, dt)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.AggiornaSuoni", ex)
            Throw
        End Try
    End Sub

    ''' <summary>L'effetto Doppler: chi si avvicina suona più acuto, chi si allontana più grave.</summary>
    Private Function Doppler(v As Veicolo, d As Double, dt As Double) As Single
        Try
            Dim r As Single = 1
            If v.UltimaDistanza >= 0 AndAlso dt > 0 Then
                Dim avvicinamento As Double = Math.Max(-60, Math.Min(60, (v.UltimaDistanza - d) / dt))
                r = CSng(343 / (343 - avvicinamento))
            End If
            v.UltimaDistanza = d
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Doppler", ex)
            Throw
        End Try
    End Function

    ''' <summary>Spegne tutti i suoni del mondo (uscendo dal gioco).</summary>
    Public Sub Silenzio()
        Try
            If Audio Is Nothing Then Return
            Audio.Togli(_motore)
            Audio.Togli(_ambiente)
            Audio.Togli(_pioggia)
            If _abitacolo IsNot Nothing Then Audio.Togli(_abitacolo)
            If Vita IsNot Nothing Then Vita.Spegni()
            If Barca IsNot Nothing Then Barca.Spegni()
            If _elicottero IsNot Nothing Then Audio.Togli(_elicottero)
            For Each v As VoceMotore In _motoriTraffico
                Audio.Togli(v)
            Next
            For Each v As Veicolo In Veicoli
                If v.Sirena IsNot Nothing Then Audio.Togli(v.Sirena)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Mondo.Silenzio", ex)
            Throw
        End Try
    End Sub
End Class
