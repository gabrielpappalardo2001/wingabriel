Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

' ============================================================================================
' Le missioni: si sceglie una missione dal telefono (o andando dal personaggio), si va da chi la dà (il navigatore porta lì) e la missione
' comincia da sola. Poi i passi uno alla volta (vedi i tipi in scripts\citta\genera-missioni.py): parlare, andare, rubare un veicolo,
' portarlo, seguire o inseguire qualcuno, scortare un'auto amica, seminare la polizia, aspettare, scendere, arrivare di nascosto,
' raccogliere oggetti sparsi, inseguire in barca, scegliere a metà strada.
' Tre tipi di missioni: quelle della trama (capitoli), le storie di quartiere (capitolo 0, quando si vuole) e i finali di capitolo
' (aprono il capitolo dopo). Se qualcosa va storto la missione fallisce: si può riprendere dal telefono, dall'ultimo punto di controllo.
' Fare le missioni di un personaggio fa crescere la sua stima e il rispetto della banda del suo quartiere (e scontenta la banda rivale).
' ============================================================================================

Public NotInheritable Class EsecutoreMissione
    Private ReadOnly _m As Mondo
    ''' <summary>La missione scelta dal telefono, che comincia arrivando dal personaggio.</summary>
    Public Scelta As Missione
    ''' <summary>La missione in corso.</summary>
    Public Attiva As Missione
    Private _indice As Integer
    Private _inizioPasso As Double
    Private _attesaFine As Double
    Private _veicoloMissione As Veicolo
    Private _bersaglio As Veicolo
    Private _colpi As Integer
    Private _troppoVicino As Double
    Private _troppoLontano As Double
    Private _ultimoAvviso As Double
    Private _ultimoTic As Double
    Private _partito As Boolean
    Private _avvisiTempo As New HashSet(Of Integer)()
    Private _introduzione As Boolean
    ''' <summary>Il tempo per la consegna in corso: quello della missione, ma mai meno di quanto serve guidando con calma.</summary>
    Private _tempoPorta As Integer
    ' punto di controllo e ricompensa
    Private _controllo As Integer
    Private _tipoRubato As String = ""
    Private _bonus As Double
    ' scorta
    Private ReadOnly _nemici As New Dictionary(Of Veicolo, Integer)()
    Private _prossimoNemico As Double
    Private _ultimaSaluteAlleato As Double
    ' furtivo
    Public ReadOnly Guardie As New List(Of Posizione)()
    Private ReadOnly _passiGuardie As New List(Of Double)()
    ' raccogli
    Public ReadOnly Oggetti As New List(Of Luogo)()
    Private _metaRaccolta As Luogo
    Private _prossimoSegnale As Double
    ' scelta
    Private _attesaScelta As Boolean
    ' ripresa di una partita salvata
    Private _riuso As Boolean
    Private _riprendi As Boolean

    ''' <summary>Una scelta a metà missione: il menu la propone e poi chiama RispondiInMissione.</summary>
    Public Event SceltaInMissione(p As PassoMissione)
    Public Event DaSalvare()
    ''' <summary>Una scelta della trama da fare adesso (quando si apre il capitolo dopo il suo).</summary>
    Public Event SceltaRichiesta(s As Scelta)

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.New", ex)
            Throw
        End Try
    End Sub

    Private ReadOnly Property PassoAttuale As PassoMissione
        Get
            If Attiva Is Nothing OrElse _indice >= Attiva.Passi.Count Then Return Nothing
            Return Attiva.Passi(_indice)
        End Get
    End Property

    ' ------------------------------------------------------------------ quali missioni si possono fare
    ''' <summary>Le missioni che si possono fare adesso.</summary>
    Public Function Disponibili() As List(Of Missione)
        Try
            Return _m.Dati.Missioni.Where(Function(x) Not _m.Partita.Completate.Contains(x.Id) AndAlso (x.Finale OrElse Not _m.Partita.Bloccati.Contains(x.Personaggio.Id)) AndAlso
                                               CapitoloAperto(x.Capitolo) AndAlso (Not x.Finale OrElse FinaleAperto(x.Capitolo)) AndAlso
                                               x.Richiede.All(Function(r) _m.Partita.Completate.Contains(r) OrElse MissioneSaltata(r))).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Disponibili", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una missione richiesta che non si farà mai (il suo personaggio è stato bloccato): non ferma le altre.</summary>
    Private Function MissioneSaltata(id As String) As Boolean
        Try
            Dim m As Missione = _m.Dati.Missioni.FirstOrDefault(Function(x) x.Id = id)
            Return m IsNot Nothing AndAlso Not m.Finale AndAlso _m.Partita.Bloccati.Contains(m.Personaggio.Id)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.MissioneSaltata", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le missioni normali di un capitolo (senza il finale), dei personaggi non bloccati.</summary>
    Private Function MissioniDelCapitolo(numero As Integer) As List(Of Missione)
        Try
            Return _m.Dati.Missioni.Where(Function(x) x.Capitolo = numero AndAlso Not x.Finale AndAlso Not _m.Partita.Bloccati.Contains(x.Personaggio.Id)).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.MissioniDelCapitolo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il finale di un capitolo si apre quando del capitolo se ne sono fatte almeno tre (o tutte, se sono meno).</summary>
    Private Function FinaleAperto(numero As Integer) As Boolean
        Try
            Dim normali As List(Of Missione) = MissioniDelCapitolo(numero)
            Return normali.Where(Function(x) _m.Partita.Completate.Contains(x.Id)).Count() >= Math.Min(3, normali.Count)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.FinaleAperto", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Un capitolo è aperto se è il primo (o sono le storie di quartiere), o se il capitolo prima è aperto e il suo finale è fatto
    ''' (senza finale: se se ne sono fatte almeno tre).
    ''' </summary>
    Public Function CapitoloAperto(numero As Integer) As Boolean
        Try
            If numero <= 1 Then Return True
            If Not CapitoloAperto(numero - 1) Then Return False
            Dim finale As Missione = _m.Dati.Missioni.FirstOrDefault(Function(x) x.Capitolo = numero - 1 AndAlso x.Finale)
            If finale IsNot Nothing Then Return _m.Partita.Completate.Contains(finale.Id)
            Return FinaleAperto(numero - 1)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.CapitoloAperto", ex)
            Throw
        End Try
    End Function

    Private Function UltimoCapitolo() As Integer
        Try
            Return If(_m.Dati.Capitoli.Count > 0, _m.Dati.Capitoli.Max(Function(x) x.Numero), 1)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.UltimoCapitolo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il capitolo più avanti aperto.</summary>
    Public Function CapitoloAttuale() As Integer
        Try
            Dim n As Integer = 1
            Dim ultimo As Integer = UltimoCapitolo()
            While n < ultimo AndAlso CapitoloAperto(n + 1)
                n += 1
            End While
            Return n
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.CapitoloAttuale", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il valore dell'ultima scelta della trama, che decide il finale ("a" se non c'è).</summary>
    Private Function ValoreFinale() As String
        Try
            Dim ultima As Scelta = _m.Dati.Scelte.OrderByDescending(Function(s) s.Capitolo).FirstOrDefault()
            Dim v As String = ""
            If ultima IsNot Nothing AndAlso _m.Partita.Scelte.TryGetValue(ultima.Id, v) Then Return v
            Return "a"
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.ValoreFinale", ex)
            Throw
        End Try
    End Function

    ''' <summary>La risposta a una scelta della trama: si ricorda, si blocca chi va bloccato, si sente la conseguenza.</summary>
    Public Sub Rispondi(s As Scelta, o As OpzioneScelta)
        Try
            _m.Partita.Scelte(s.Id) = o.Valore
            Dim testo As String = o.Conseguenza
            If o.Blocca IsNot Nothing Then
                _m.Partita.Bloccati.Add(o.Blocca.Id)
                _m.Partita.CambiaStima(o.Blocca.Id, -60)
                testo &= " " & o.Blocca.Nome & " non ti darà più lavoro."
            End If
            If s.Personaggio IsNot Nothing Then _m.Partita.CambiaStima(s.Personaggio.Id, 10)
            _m.Dici(testo, True)
            RaiseEvent DaSalvare()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Rispondi", ex)
            Throw
        End Try
    End Sub

    ''' <summary>La storia fin qui: i capitoli aperti, con il loro racconto.</summary>
    Public Function Storia() As String
        Try
            Dim n As Integer = CapitoloAttuale()
            Dim pezzi As New List(Of String)()
            For Each c As Capitolo In _m.Dati.Capitoli.Where(Function(x) x.Numero <= n)
                pezzi.Add("Capitolo " & c.Numero.ToString() & ", " & c.Titolo & ". " & c.Racconto)
            Next
            If _m.Partita.Finale Then
                Dim f As String = ""
                If _m.Dati.Finali.TryGetValue(ValoreFinale(), f) Then pezzi.Add("Il finale. " & f)
            End If
            Return String.Join(" ", pezzi)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Storia", ex)
            Throw
        End Try
    End Function

    ''' <summary>Dopo una missione: un capitolo nuovo, una scelta da fare, il finale.</summary>
    Private Sub DopoLaMissione(capitoloPrima As Integer, x As Missione)
        Try
            Dim ora As Integer = CapitoloAttuale()
            If ora > capitoloPrima Then
                Dim c As Capitolo = _m.Dati.Capitoli.FirstOrDefault(Function(k) k.Numero = ora)
                If c IsNot Nothing Then _m.Dici("Capitolo " & ora.ToString() & ": " & c.Titolo & ". " & c.Racconto)
                _m.Notifica("capitolo", If(c IsNot Nothing, c.Titolo, ""))
            End If
            For Each s As Scelta In _m.Dati.Scelte
                If s.Capitolo < ora AndAlso Not _m.Partita.Scelte.ContainsKey(s.Id) Then
                    RaiseEvent SceltaRichiesta(s)
                    Exit For
                End If
            Next
            ' il finale della storia: dopo il finale dell'ultimo capitolo
            If Not _m.Partita.Finale AndAlso x.Finale AndAlso x.Capitolo = UltimoCapitolo() Then
                _m.Partita.Finale = True
                Dim f As String = ""
                If _m.Dati.Finali.TryGetValue(ValoreFinale(), f) Then
                    _m.Suona(MotoreAudio.Fanfara(), 0.9F, 0)
                    _m.Dici("Il finale. " & f & " La città resta aperta: puoi continuare a girare, a fare le storie di quartiere, i lavori e le sfide.", True)
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.DopoLaMissione", ex)
            Throw
        End Try
    End Sub

    Public Sub Scegli(x As Missione)
        Try
            If Attiva IsNot Nothing Then
                _m.Dici("Hai già una missione in corso: " & Attiva.Titolo & ".", True)
                Return
            End If
            If _m.Sfide.InCorso Then _m.Sfide.Annulla()
            Scelta = x
            If _m.Partita.Ripresa.ContainsKey(x.Id) Then
                Inizia(x)
                Return
            End If
            _m.Dici(x.Titolo & ". Ti aspetta " & x.Personaggio.Nome & ", a " & x.Luogo.Nome & "." & If(_m.Partita.Ripresa.ContainsKey(x.Id), " Riprenderai dall'ultimo punto di controllo.", ""), True)
            _m.ImpostaMeta(x.Luogo, True)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Scegli", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Rinuncia alla missione in corso (dal menu di pausa).</summary>
    Public Sub Abbandona()
        Try
            If Attiva Is Nothing Then
                Scelta = Nothing
                Return
            End If
            Sospendi("Hai messo da parte la missione.")
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Abbandona", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ il ciclo
    Public Sub Passo(dt As Double)
        Try
            If Attiva Is Nothing Then
                If Scelta IsNot Nothing AndAlso _m.Barca Is Nothing AndAlso _m.DistanzaDa(Scelta.Luogo.X, Scelta.Luogo.Y) < Mondo.DISTANZA_LUOGO AndAlso Math.Abs(_m.Velocita) < 3 Then Inizia(Scelta)
                Return
            End If
            If _introduzione Then
                If _m.Tempo >= _attesaFine Then
                    _introduzione = False
                    IniziaPasso()
                End If
                Return
            End If
            Dim p As PassoMissione = PassoAttuale
            If p Is Nothing Then
                Riesci()
                Return
            End If
            Dim trascorso As Double = _m.Tempo - _inizioPasso
            Select Case p.Tipo
                Case "parla"
                    If _m.Tempo >= _attesaFine Then Avanti()
                Case "vai"
                    If _m.DistanzaDa(p.Luogo.X, p.Luogo.Y) < Mondo.DISTANZA_LUOGO Then Avanti()
                Case "ruba"
                    If Not _m.APiedi AndAlso _m.Veicolo Is _veicoloMissione Then Avanti()
                Case "porta"
                    PassoPorta(p, trascorso)
                Case "segui"
                    PassoSegui(p)
                Case "insegui"
                    PassoInsegui(p)
                Case "scorta"
                    PassoScorta(p, dt)
                Case "fuggi"
                    If _m.Stelle = 0 Then Avanti()
                Case "aspetta"
                    Dim manca As Integer = CInt(Math.Ceiling(p.Secondi - trascorso))
                    If manca = 10 AndAlso _avvisiTempo.Add(10) AndAlso p.Secondi > 15 Then _m.Dici("Ancora dieci secondi.")
                    If manca <= 0 Then Avanti()
                Case "scendi"
                    If _m.APiedi Then Avanti()
                Case "furtivo"
                    PassoFurtivo(p, dt)
                Case "raccogli"
                    PassoRaccogli(p)
                Case "barca"
                    PassoBarca(p)
                Case "scelta"
                    ' si aspetta la risposta dal menu
                Case Else
                    Avanti()
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Passo", ex)
            Throw
        End Try
    End Sub

    Private Shared Function DurataParlato(testo As String) As Double
        Try
            ' circa tredici caratteri al secondo con la sintesi, più una pausa
            Return 0.8 + If(testo, "").Length / 13.0
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.DurataParlato", ex)
            Throw
        End Try
    End Function

    Private Sub Inizia(x As Missione)
        Try
            Attiva = x
            Scelta = Nothing
            _indice = 0
            _veicoloMissione = Nothing
            _bersaglio = Nothing
            _controllo = 0
            _tipoRubato = ""
            _bonus = 0
            _m.ImpostaMeta(Nothing, True)
            Dim testo As String = x.Titolo & ". " & x.Personaggio.Nome & ": " & x.Introduzione
            ' si riprende dall'ultimo punto di controllo
            Dim ripresa As String = ""
            If _m.Partita.Ripresa.TryGetValue(x.Id, ripresa) Then
                Dim pezzi As String() = ripresa.Split("|"c)
                Dim n As Integer
                If Integer.TryParse(pezzi(0), n) AndAlso n > 0 AndAlso n < x.Passi.Count Then
                    _indice = n
                    _controllo = n
                    _tipoRubato = If(pezzi.Length > 1, pezzi(1), "")
                    testo = x.Titolo & ". Riprendi esattamente da dove eri, passo " & (n + 1).ToString() & " di " & x.Passi.Count.ToString() & "."
                    If _tipoRubato <> "" AndAlso ServeIlVeicolo(n) Then
                        Dim trId As String = If(pezzi.Length > 3, pezzi(2), "")
                        Dim trVm As Tratto = If(trId = "", Nothing, _m.Dati.Tratti.FirstOrDefault(Function(tt) tt.Id = trId))
                        If trVm IsNot Nothing Then
                            Dim dVm As Double
                            Double.TryParse(pezzi(3), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, dVm)
                            _veicoloMissione = _m.CreaVeicoloSu(_tipoRubato, trVm, dVm, RuoloVeicolo.Parcheggiato)
                            testo &= " Il veicolo della missione è dov'era, a " & Mappa.Metri(_m.DistanzaDa(_veicoloMissione.X, _veicoloMissione.Y)) & ", " & _m.LatoA(_veicoloMissione.X, _veicoloMissione.Y) & ": " & _veicoloMissione.Tipo.ConArticolo() & "."
                        Else
                            _veicoloMissione = _m.CreaVeicolo(_tipoRubato, x.Luogo, RuoloVeicolo.Parcheggiato)
                            testo &= " Il veicolo della missione è a " & x.Luogo.Nome & ": " & _veicoloMissione.Tipo.ConArticolo() & "."
                        End If
                    End If
                End If
            End If
            _m.Suona(MotoreAudio.Bip(True), 0.6F, 0)
            _m.Dici(testo, True)
            _introduzione = True
            _attesaFine = _m.Tempo + DurataParlato(testo)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Inizia", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Vero se dal passo n in poi serve il veicolo rubato prima (prima di scendere o rubarne un altro).</summary>
    Private Function ServeIlVeicolo(n As Integer) As Boolean
        Try
            For k As Integer = n To Attiva.Passi.Count - 1
                Select Case Attiva.Passi(k).Tipo
                    Case "porta", "segui", "insegui", "scorta" : Return True
                    Case "ruba", "scendi", "barca", "furtivo" : Return False
                End Select
            Next
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.ServeIlVeicolo", ex)
            Throw
        End Try
    End Function

    Private Sub Avanti()
        Try
            Dim fatto As PassoMissione = PassoAttuale
            If fatto IsNot Nothing Then
                If fatto.Tipo = "ruba" Then _tipoRubato = fatto.Veicolo
                ' sceso dal veicolo della missione, quello che viene dopo si fa con il veicolo che si vuole
                If fatto.Tipo = "scendi" OrElse fatto.Tipo = "barca" OrElse fatto.Tipo = "furtivo" Then
                    _veicoloMissione = Nothing
                    _tipoRubato = ""
                End If
                ' i passi d'azione sono punti di controllo: se poi va male si riparte da qui
                If {"vai", "ruba", "porta", "segui", "insegui", "scorta", "fuggi", "furtivo", "raccogli", "barca"}.Contains(fatto.Tipo) Then _controllo = _indice + 1
            End If
            _indice += 1
            If PassoAttuale Is Nothing Then
                Riesci()
            Else
                IniziaPasso()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Avanti", ex)
            Throw
        End Try
    End Sub

    Private Sub IniziaPasso()
        Try
            Dim p As PassoMissione = PassoAttuale
            _inizioPasso = _m.Tempo
            _avvisiTempo.Clear()
            _troppoVicino = 0
            _troppoLontano = 0
            _colpi = 0
            _partito = False
            _m.Suona(MotoreAudio.Tic(), 0.5F, 0)
            Select Case p.Tipo
                Case "parla"
                    _m.Dici(p.Testo)
                    _attesaFine = _m.Tempo + DurataParlato(p.Testo)
                Case "vai"
                    _m.ImpostaMeta(p.Luogo, True)
                    _m.Dici("Navigatore: " & p.Luogo.Nome & ", " & p.Luogo.Quartiere.Nome & ". " & p.Testo)
                Case "ruba"
                    If Not (_riuso AndAlso _veicoloMissione IsNot Nothing) Then _veicoloMissione = _m.CreaVeicolo(p.Veicolo, p.Luogo, RuoloVeicolo.Parcheggiato)
                    _m.ImpostaMeta(p.Luogo, True)
                    _m.Dici("Il navigatore ti porta a " & p.Luogo.Nome & ", " & p.Luogo.Quartiere.Nome & ": " & Char.ToUpper(_veicoloMissione.Tipo.ConArticolo()(0)) & _veicoloMissione.Tipo.ConArticolo().Substring(1) & " è lì. " & p.Testo)
                Case "porta"
                    _m.ImpostaMeta(p.Luogo, True)
                    _tempoPorta = 0
                    If p.Tempo > 0 Then
                        ' circa 30 all'ora di media, più mezzo minuto: si arriva senza correre come pazzi
                        Dim strada As Double = Mappa.Lunghezza(_m.Dati, _m.Pos, p.Luogo.Tratto, p.Luogo.Distanza)
                        _tempoPorta = Math.Max(p.Tempo, CInt(Math.Ceiling((strada / 8.5 + 30) / 10)) * 10)
                    End If
                    Dim t As String = "Navigatore: " & p.Luogo.Nome & ", " & p.Luogo.Quartiere.Nome & ". " & p.Testo
                    If _tempoPorta > 0 Then t &= " Hai " & TestoSecondi(_tempoPorta) & "."
                    If p.Integro Then t &= " Non rovinare la macchina."
                    _m.Dici(t)
                Case "segui"
                    _bersaglio = _m.CreaVeicolo("berlina", p.Da, RuoloVeicolo.Bersaglio)
                    _bersaglio.Meta = Nothing
                    _m.ImpostaMeta(p.Da, True)
                    _m.Dici(p.Testo & " L'auto da seguire è a " & p.Da.Nome & ": avvicinati e partirà.")
                Case "insegui"
                    _bersaglio = _m.CreaVeicolo("sportiva", p.Da, RuoloVeicolo.Bersaglio)
                    _bersaglio.Meta = Nothing
                    _m.ImpostaMeta(p.Da, True)
                    _m.Dici(p.Testo & " L'auto è a " & p.Da.Nome & ". Raggiungila e speronala tre volte.")
                Case "scorta"
                    _bersaglio = _m.CreaVeicolo("berlina", p.Da, RuoloVeicolo.Bersaglio)
                    _bersaglio.Meta = Nothing
                    _ultimaSaluteAlleato = _bersaglio.Salute
                    _nemici.Clear()
                    _m.ImpostaMeta(p.Da, True)
                    _m.Dici(p.Testo & " L'auto da proteggere è a " & p.Da.Nome & ": quando arrivi parte per " & p.A.Nome & ". Chi la attacca va speronato due volte.")
                Case "fuggi"
                    _m.ImpostaMeta(Nothing, True)
                    _m.Dici(p.Testo)
                    _m.RicercaAlmeno(Math.Max(1, p.Stelle), "La polizia ti sta cercando!")
                Case "aspetta"
                    _m.Dici(p.Testo & " Aspetta " & TestoSecondi(p.Secondi) & ".")
                Case "scendi"
                    _m.Dici(p.Testo)
                Case "furtivo"
                    PreparaGuardie(p.Luogo)
                    _m.ImpostaMeta(p.Luogo, True)
                    _m.Dici(p.Testo & " Meta: " & p.Luogo.Nome & ". Ci sono guardie che fanno la ronda: lascia la macchina lontano, avvicinati a piedi e non correre. Se ne senti una vicina, fermati.")
                Case "raccogli"
                    PreparaOggetti(p)
                    _m.Dici(p.Testo & " Sono " & Oggetti.Count.ToString() & ", sparsi " & If(p.Quartiere IsNot Nothing, "nel quartiere " & p.Quartiere.Nome, "qui intorno") & ": segui il loro tintinnio, il navigatore ti porta al più vicino.")
                Case "barca"
                    _m.ImpostaMeta(p.Luogo, True)
                    _m.Dici(p.Testo & " Il motoscafo è a " & p.Luogo.Nome & ".")
                Case "scelta"
                    _attesaScelta = True
                    _m.Dici(p.Domanda, True)
                    RaiseEvent SceltaInMissione(p)
                Case Else
                    _m.Dici(p.Testo)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.IniziaPasso", ex)
            Throw
        End Try
    End Sub

    Private Shared Function TestoSecondi(s As Integer) As String
        Try
            If s >= 60 AndAlso s Mod 60 = 0 Then Return If(s = 60, "un minuto", (s \ 60).ToString() & " minuti")
            If s > 60 Then Return (s \ 60).ToString() & " minuti e " & (s Mod 60).ToString() & " secondi"
            Return s.ToString() & " secondi"
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.TestoSecondi", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ portare, seguire, inseguire
    Private Sub PassoPorta(p As PassoMissione, trascorso As Double)
        Try
            If _tempoPorta > 0 Then
                Dim manca As Integer = CInt(Math.Ceiling(_tempoPorta - trascorso))
                For Each soglia As Integer In {60, 30, 10}
                    If manca = soglia AndAlso _tempoPorta > soglia AndAlso _avvisiTempo.Add(soglia) Then _m.Dici("Mancano " & TestoSecondi(soglia) & ".")
                Next
                If manca <= 0 Then
                    _tempoPorta = 0
                    _bonus = Math.Min(_bonus, 0) - 0.3
                    _m.Dici("Tempo scaduto: la consegna vale ancora, ma ti pagheranno meno.", True)
                    Return
                End If
            End If
            If _m.DistanzaDa(p.Luogo.X, p.Luogo.Y) >= Mondo.DISTANZA_LUOGO Then Return
            If _m.APiedi Then
                If _m.Tempo - _ultimoAvviso > 8 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Devi arrivarci con la macchina.")
                End If
                Return
            End If
            If _veicoloMissione IsNot Nothing AndAlso _m.Veicolo IsNot _veicoloMissione Then
                If _m.Tempo - _ultimoAvviso > 8 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Non è questa la macchina giusta.")
                End If
                Return
            End If
            If Math.Abs(_m.Velocita) > 4 Then
                If _m.Tempo - _ultimoAvviso > 5 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Ci sei: fermati qui.")
                End If
                Return
            End If
            If p.Integro AndAlso _m.Veicolo.Salute < _m.Veicolo.Tipo.Robustezza * 0.5 Then
                _bonus = Math.Min(_bonus, 0) - 0.3
                _m.Dici("La macchina è rovinata: il carico vale meno e ti pagheranno meno.", True)
            End If
            Avanti()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PassoPorta", ex)
            Throw
        End Try
    End Sub

    Private Sub TicVerso(v As Veicolo)
        Try
            If _m.Tempo - _ultimoTic < 1.1 Then Return
            _ultimoTic = _m.Tempo
            _m.Suona(MotoreAudio.Tic(), 0.45F, _m.AzimutVerso(v.X, v.Y))
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.TicVerso", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoSegui(p As PassoMissione)
        Try
            Dim b As Veicolo = _bersaglio
            If b Is Nothing Then
                Avanti()
                Return
            End If
            Dim d As Double = _m.DistanzaDa(b.X, b.Y)
            If Not _partito Then
                If d < 120 Then
                    _partito = True
                    b.Meta = p.A
                    _m.ImpostaMeta(Nothing, True)
                    _m.Dici("Parte! Stalle dietro, ma senza esagerare.", True)
                End If
                Return
            End If
            TicVerso(b)
            If d > 230 Then
                _troppoLontano += 0.033
                If _troppoLontano > 8 Then
                    _troppoLontano = 0
                    b.FermoFino = _m.Tempo + 25
                    _m.Dici("L'auto si è fermata ad aspettarti: raggiungila.", True)
                    Return
                End If
                If _m.Tempo - _ultimoAvviso > 6 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("La stai perdendo!", True)
                End If
            Else
                _troppoLontano = 0
            End If
            If d < 14 Then
                _troppoVicino += 0.033
                If _troppoVicino > 5 Then
                    _troppoVicino = 0
                    _bonus = Math.Min(_bonus, 0) - 0.1
                    _m.Dici("Ti ha notato ma non ti ha seminato: tieni più distanza.", True)
                    Return
                End If
                If _m.Tempo - _ultimoAvviso > 4 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Troppo vicino, rallenta!", True)
                End If
            Else
                ' tornando a distanza il sospetto passa piano piano
                _troppoVicino = Math.Max(0, _troppoVicino - 0.02)
            End If
            If b.Pos.Tratto Is p.A.Tratto AndAlso Math.Abs(b.Pos.Distanza - p.A.Distanza) < 12 Then
                b.Ruolo = RuoloVeicolo.Parcheggiato
                b.Velocita = 0
                _m.Dici("Si è fermata a " & p.A.Nome & ".")
                _bersaglio = Nothing
                Avanti()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PassoSegui", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoInsegui(p As PassoMissione)
        Try
            Dim b As Veicolo = _bersaglio
            If b Is Nothing Then
                Avanti()
                Return
            End If
            Dim d As Double = _m.DistanzaDa(b.X, b.Y)
            If Not _partito Then
                If d < 100 Then
                    _partito = True
                    b.InFuga = True
                    _m.ImpostaMeta(Nothing, True)
                    _m.Suona(MotoreAudio.Sgommata(), 0.7F, _m.AzimutVerso(b.X, b.Y))
                    _m.Dici("Ti ha visto, muoviti!", True)
                End If
                Return
            End If
            TicVerso(b)
            If d > 450 Then
                _troppoLontano += 0.033
                If _troppoLontano > 15 Then
                    _troppoLontano = 0
                    b.FermoFino = _m.Tempo + 20
                    _m.Dici("Si è fermata con il motore in panne: raggiungila!", True)
                End If
            Else
                _troppoLontano = 0
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PassoInsegui", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ la scorta
    ''' <summary>L'auto amica da proteggere durante una scorta (Nothing se non c'è).</summary>
    Public ReadOnly Property Alleato As Veicolo
        Get
            Dim p As PassoMissione = PassoAttuale
            If p IsNot Nothing AndAlso p.Tipo = "scorta" Then Return _bersaglio
            Return Nothing
        End Get
    End Property

    Private Sub PassoScorta(p As PassoMissione, dt As Double)
        Try
            Dim a As Veicolo = _bersaglio
            If a Is Nothing Then
                Avanti()
                Return
            End If
            Dim d As Double = _m.DistanzaDa(a.X, a.Y)
            If Not _partito Then
                If d < 60 AndAlso Not _m.APiedi Then
                    _partito = True
                    a.Meta = p.A
                    a.VelocitaVoluta = 0
                    _prossimoNemico = _m.Tempo + 10
                    _m.ImpostaMeta(Nothing, True)
                    _m.Dici("Parte! Stalle vicino e tieni lontano chi le si avvicina.", True)
                ElseIf d < 60 AndAlso _m.Tempo - _ultimoAvviso > 8 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Ti serve un veicolo per fare da scorta.")
                End If
                Return
            End If
            TicVerso(a)
            ' arrivano gli assalitori, al massimo due alla volta
            For Each n As Veicolo In _nemici.Keys.ToList()
                If n.Ruolo <> RuoloVeicolo.Banda Then _nemici.Remove(n)
            Next
            If _m.Tempo >= _prossimoNemico AndAlso _nemici.Count < 2 Then
                _prossimoNemico = _m.Tempo + 16 + _m.Caso().NextDouble() * 12
                Dim t As Tratto = _m.TrattoACaso(a.Pos, 110, 230)
                Dim n As Veicolo = _m.CreaVeicoloSu(If(_m.Caso().Next(2) = 0, "fuoristrada", "berlina"), t, t.Lunghezza / 2, RuoloVeicolo.Banda)
                n.Preda = a
                _nemici(n) = 0
                _m.Suona(MotoreAudio.Sgommata(), 0.6F, _m.AzimutVerso(n.X, n.Y))
                _m.Dici("Arriva qualcuno " & _m.LatoA(n.X, n.Y) & ": vuole l'auto che scorti!", True)
            End If
            If d > 300 Then
                _troppoLontano += dt
                If _troppoLontano > 10 Then
                    _troppoLontano = 0
                    a.FermoFino = _m.Tempo + 20
                    _m.Dici("La scorta si è fermata ad aspettarti: raggiungila.", True)
                    Return
                End If
                If _m.Tempo - _ultimoAvviso > 5 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Sei troppo lontano dalla scorta!", True)
                End If
            Else
                _troppoLontano = 0
            End If
            If a.Pos.Tratto Is p.A.Tratto AndAlso Math.Abs(a.Pos.Distanza - p.A.Distanza) < 12 Then
                a.Ruolo = RuoloVeicolo.Parcheggiato
                a.Velocita = 0
                For Each n As Veicolo In _nemici.Keys.ToList()
                    _m.TogliVeicolo(n)
                Next
                _nemici.Clear()
                _m.Partita.Conta("scorte")
                _m.Dici("L'auto è arrivata a " & p.A.Nome & " sana e salva.", True)
                _bersaglio = Nothing
                Avanti()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PassoScorta", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un'auto di banda ha speronato quella che scorti.</summary>
    Public Sub AlleatoColpito(v As Veicolo)
        Try
            If v Is Nothing OrElse v IsNot Alleato Then Return
            If v.Salute <= 0 Then
                v.Salute = 40
                _bonus = Math.Min(_bonus, 0) - 0.2
                _m.Dici("L'auto è ridotta male ma regge ancora: portala fino in fondo.", True)
                Return
            End If
            If _ultimaSaluteAlleato - v.Salute >= 25 OrElse _m.Tempo - _ultimoAvviso > 4 Then
                _ultimaSaluteAlleato = v.Salute
                _ultimoAvviso = _m.Tempo
                _m.Dici("La stanno speronando! " & If(v.Salute > 60, "Regge ancora.", If(v.Salute > 30, "È ammaccata.", "Sta per cedere.")), True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.AlleatoColpito", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ di nascosto
    ''' <summary>Due o tre guardie che fanno la ronda sul tratto del luogo e su quelli vicini.</summary>
    Private Sub PreparaGuardie(l As Luogo)
        Try
            Guardie.Clear()
            _passiGuardie.Clear()
            Dim tratti As New List(Of Tratto) From {l.Tratto}
            tratti.AddRange(l.Tratto.A.Tratti.Where(Function(t) t IsNot l.Tratto).Take(1))
            tratti.AddRange(l.Tratto.B.Tratti.Where(Function(t) t IsNot l.Tratto).Take(1))
            For k As Integer = 0 To Math.Min(2, tratti.Count - 1)
                Dim t As Tratto = tratti(k)
                Guardie.Add(New Posizione(t, _m.Caso().NextDouble() * t.Lunghezza, If(_m.Caso().Next(2) = 0, 1, -1)))
                _passiGuardie.Add(0)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PreparaGuardie", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoFurtivo(p As PassoMissione, dt As Double)
        Try
            Dim l As Luogo = p.Luogo
            ' in macchina vicino al posto ti sentono arrivare
            If Not _m.APiedi AndAlso _m.DistanzaDa(l.X, l.Y) < 70 Then
                Scoperto("Ti sentono arrivare col motore!")
                Return
            End If
            Dim raggio As Double = If(_m.StaCorrendo, 20, If(_m.FermoAPiedi, 3, 8))
            For i As Integer = 0 To Guardie.Count - 1
                Dim g As Posizione = Guardie(i)
                ' la ronda: avanti e indietro, senza allontanarsi troppo
                Dim resto As Double = g.Avanza(1.1 * dt)
                If resto > 0 OrElse g.AllIncrocio < 0.05 Then
                    Dim uscite As List(Of Tratto) = g.Prossimo.Tratti.Where(Function(t) t IsNot g.Tratto AndAlso Math.Sqrt(((t.A.X + t.B.X) / 2 - l.X) ^ 2 + ((t.A.Y + t.B.Y) / 2 - l.Y) ^ 2) < 160).ToList()
                    If uscite.Count > 0 AndAlso _m.Caso().NextDouble() < 0.6 Then
                        g.Imbocca(uscite(_m.Caso().Next(uscite.Count)), g.Prossimo)
                    Else
                        g.Inverti()
                    End If
                End If
                If Not _m.APiedi Then Continue For
                Dim d As Double = _m.DistanzaDa(g.X, g.Y)
                If d < 45 AndAlso _m.Tempo >= _passiGuardie(i) Then
                    _passiGuardie(i) = _m.Tempo + 0.62
                    _m.Suona(MotoreAudio.Passo(), CSng(Math.Min(0.75, 0.15 + 6 / Math.Max(3, d))), _m.AzimutVerso(g.X, g.Y))
                End If
                If d < raggio Then
                    Scoperto("Una guardia ti vede!")
                    Return
                End If
                If d < raggio * 2.2 AndAlso _m.Tempo - _ultimoAvviso > 4 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Una guardia a " & Mappa.Metri(d) & ", " & _m.LatoA(g.X, g.Y) & ". " & If(_m.StaCorrendo, "Non correre!", "Fermati e lasciala passare."), True)
                End If
            Next
            If _m.APiedi AndAlso _m.DistanzaDa(l.X, l.Y) < Mondo.DISTANZA_LUOGO Then
                Guardie.Clear()
                _m.Partita.Conta("furtivo")
                _m.Dici("Ci sei, e nessuno ti ha visto.", True)
                Avanti()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PassoFurtivo", ex)
            Throw
        End Try
    End Sub

    Private Sub Scoperto(motivo As String)
        Try
            Guardie.Clear()
            _m.Suona(MotoreAudio.Fischio(), 0.7F, 0)
            _bonus = Math.Min(_bonus, 0) - 0.1
            Dim passoF As PassoMissione = PassoAttuale
            If passoF IsNot Nothing AndAlso passoF.Luogo IsNot Nothing Then PreparaGuardie(passoF.Luogo)
            _m.Dici(motivo & " Le guardie ricominciano la ronda: riprova.", True)
            _m.Ricerca(1, "Le guardie chiamano la polizia.")
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Scoperto", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ raccogliere
    Private Sub PreparaOggetti(p As PassoMissione)
        Try
            Oggetti.Clear()
            Dim q As Quartiere = If(p.Quartiere, _m.Dati.QuartiereDi(_m.X, _m.Y))
            Dim tratti As List(Of Tratto) = _m.Dati.Tratti.Where(Function(t) t.A.Quartiere Is q AndAlso t.B.Quartiere Is q AndAlso Not t.Galleria).ToList()
            If tratti.Count = 0 Then tratti = _m.Dati.Tratti.ToList()
            Dim nome As String = If(p.Oggetto = "", "un oggetto", p.Oggetto)
            For k As Integer = 1 To Math.Max(3, Math.Min(6, If(p.Quanti > 0, p.Quanti, 4)))
                Dim t As Tratto = tratti(_m.Caso().Next(tratti.Count))
                Dim l As New Luogo With {.Id = "raccolta" & k.ToString(), .Tipo = "raccolta", .Nome = nome, .Tratto = t, .Distanza = t.Lunghezza * (0.2 + 0.6 * _m.Caso().NextDouble()), .Quartiere = q, .Lato = "destra"}
                t.Punto(l.Distanza, l.X, l.Y)
                Oggetti.Add(l)
            Next
            AggiornaMetaRaccolta()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PreparaOggetti", ex)
            Throw
        End Try
    End Sub

    Private Sub AggiornaMetaRaccolta()
        Try
            _metaRaccolta = Oggetti.OrderBy(Function(o) _m.DistanzaDa(o.X, o.Y)).FirstOrDefault()
            _m.ImpostaMeta(_metaRaccolta, True)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.AggiornaMetaRaccolta", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoRaccogli(p As PassoMissione)
        Try
            If Oggetti.Count = 0 Then
                Avanti()
                Return
            End If
            Dim o As Luogo = Oggetti.OrderBy(Function(x) _m.DistanzaDa(x.X, x.Y)).First()
            If o IsNot _metaRaccolta Then AggiornaMetaRaccolta()
            Dim d As Double = _m.DistanzaDa(o.X, o.Y)
            If d < 120 AndAlso _m.Tempo >= _prossimoSegnale Then
                _prossimoSegnale = _m.Tempo + Math.Max(0.3, d / 45)
                _m.Suona(MotoreAudio.Monete(), CSng(Math.Min(0.7, 0.15 + 12 / Math.Max(8, d))), _m.AzimutVerso(o.X, o.Y))
            End If
            If d < If(_m.APiedi, 5, 8) AndAlso (_m.APiedi OrElse Math.Abs(_m.Velocita) < 4) Then
                Oggetti.Remove(o)
                _m.Suona(MotoreAudio.Soldi(), 0.6F)
                If Oggetti.Count = 0 Then
                    _m.Dici("Preso anche l'ultimo!", True)
                    _m.ImpostaMeta(Nothing, True)
                    Avanti()
                Else
                    _m.Dici("Preso! Ne mancano " & Oggetti.Count.ToString() & ".", True)
                    AggiornaMetaRaccolta()
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PassoRaccogli", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ in barca
    Private Sub PassoBarca(p As PassoMissione)
        Try
            If _m.Barca IsNot Nothing Then
                If Not _m.Barca.Esito.HasValue Then Return
                Dim preso As Boolean = _m.Barca.Esito.Value
                _m.Barca = Nothing
                ' si torna a terra, al molo
                _m.Dici("Torni a terra, al molo.")
                If preso Then
                    _m.Partita.Conta("barca")
                    _m.Notifica("barca")
                    Avanti()
                Else
                    _m.Dici("Ti è sfuggito: riprova.", True)
                    IniziaPasso()
                End If
                Return
            End If
            If _m.DistanzaDa(p.Luogo.X, p.Luogo.Y) >= Mondo.DISTANZA_LUOGO Then Return
            If Not _m.APiedi Then
                If Math.Abs(_m.Velocita) > 2 Then Return
                _m.Scendi()
            End If
            _m.ImpostaMeta(Nothing, True)
            _m.Barca = New InseguimentoBarca(_m)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PassoBarca", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ la scelta a metà missione
    ''' <summary>La risposta alla scelta a metà missione: soldi (più paga, meno stima), rispetto (più stima), polizia (due stelle, paga più alta).</summary>
    Public Sub RispondiInMissione(k As Integer)
        Try
            Dim p As PassoMissione = PassoAttuale
            If p Is Nothing OrElse p.Tipo <> "scelta" OrElse Not _attesaScelta Then Return
            _attesaScelta = False
            If k >= 0 AndAlso k < p.Opzioni.Count Then
                Dim o As OpzioneMissione = p.Opzioni(k)
                Dim chi As String = Attiva.Personaggio.Id
                _m.Dici(o.Risposta, True)
                Select Case o.Effetto
                    Case "soldi"
                        _bonus += 0.4
                        _m.Partita.CambiaStima(chi, -10)
                    Case "rispetto"
                        _m.Partita.CambiaStima(chi, 15)
                    Case "polizia"
                        _bonus += 0.6
                        _m.RicercaAlmeno(2, "Hai fatto rumore: arriva la polizia!")
                End Select
            End If
            Avanti()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.RispondiInMissione", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ fine della missione
    ''' <summary>La banda rivale di un quartiere: chi lavora per l'una scontenta l'altra.</summary>
    Public Shared Function Rivale(quartiere As String) As String
        Try
            Select Case quartiere
                Case "porto" : Return "marina"
                Case "marina" : Return "porto"
                Case "lungomare" : Return "centro"
                Case "centro" : Return "lungomare"
                Case "industriale" : Return "castello"
                Case "castello" : Return "industriale"
                Case "giardini" : Return "collina"
                Case "collina" : Return "giardini"
                Case "stazione" : Return "universita"
                Case "universita" : Return "stazione"
                Case "belvedere" : Return "cava"
                Case "cava" : Return "belvedere"
                Case "pineta" : Return "policlinico"
                Case "policlinico" : Return "pineta"
                Case "aeroporto" : Return "altopiano"
                Case "altopiano" : Return "aeroporto"
                Case Else : Return ""
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Rivale", ex)
            Throw
        End Try
    End Function

    Private Sub Riesci()
        Try
            Dim x As Missione = Attiva
            Attiva = Nothing
            PulisciPasso()
            _m.ImpostaMeta(Nothing, True)
            Dim capitoloPrima As Integer = CapitoloAttuale()
            ' la paga: più alta con le scelte rischiose e con la stima del personaggio
            Dim stima As Integer = _m.Partita.StimaDi(x.Personaggio.Id)
            Dim soldi As Integer = CInt(Math.Round(x.Soldi * (1 + _bonus) * (1 + Math.Max(0, stima) / 500.0) / 10.0)) * 10
            _m.Partita.Guadagna(soldi)
            _m.Partita.Completate.Add(x.Id)
            _m.Partita.Ripresa.Remove(x.Id)
            _m.Partita.CambiaStima(x.Personaggio.Id, If(x.Finale, 15, 8))
            Dim q As String = x.Personaggio.Quartiere.Id
            _m.Partita.CambiaRispetto(q, If(x.Finale, 12, If(x.Storia, 10, 8)))
            If Rivale(q) <> "" Then _m.Partita.CambiaRispetto(Rivale(q), -3)
            _m.Suona(MotoreAudio.Successo(), 0.8F, 0)
            _m.Suona(MotoreAudio.Soldi(), 0.6F, 0)
            Dim nuove As Integer = Disponibili().Count
            _m.Dici("Missione compiuta. " & x.Personaggio.Nome & ": " & x.Conclusione & " Guadagni " & soldi.ToString() & " euro." &
                    If(nuove = 0, "", " Lavori disponibili sul telefono: " & nuove.ToString() & "."), True)
            _m.Notifica("missione", x.Titolo)
            DopoLaMissione(capitoloPrima, x)
            RaiseEvent DaSalvare()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Riesci", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Toglie quello che il passo in corso aveva messo in giro (bersagli, nemici, guardie, oggetti).</summary>
    Private Sub PulisciPasso()
        Try
            For Each n As Veicolo In _nemici.Keys.ToList()
                _m.TogliVeicolo(n)
            Next
            _nemici.Clear()
            Guardie.Clear()
            Oggetti.Clear()
            _metaRaccolta = Nothing
            _attesaScelta = False
            _bersaglio = Nothing
            _veicoloMissione = Nothing
            _introduzione = False
            If _m.Barca IsNot Nothing Then
                _m.Barca.Spegni()
                _m.Barca = Nothing
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PulisciPasso", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Mette da parte la missione (non c'è penalità): dal telefono la si riprende esattamente dal passo in cui era, con il veicolo dov'era.
    ''' </summary>
    Private Sub Sospendi(motivo As String)
        Try
            Dim x As Missione = Attiva
            If x Is Nothing Then Return
            Dim vm As Veicolo = _veicoloMissione
            Dim ripresa As String = _indice.ToString() & "|" & _tipoRubato
            If vm IsNot Nothing AndAlso _tipoRubato <> "" AndAlso ServeIlVeicolo(_indice) AndAlso _m.Veicolo IsNot vm Then
                ripresa &= "|" & vm.Pos.Tratto.Id & "|" & Math.Round(vm.Pos.Distanza, 1).ToString(Globalization.CultureInfo.InvariantCulture)
                _m.TogliVeicolo(vm)
            End If
            Attiva = Nothing
            Scelta = Nothing
            If _bersaglio IsNot Nothing Then _m.TogliVeicolo(_bersaglio)
            PulisciPasso()
            _m.ImpostaMeta(Nothing, True)
            If _indice > 0 Then _m.Partita.Ripresa(x.Id) = ripresa
            _m.Dici((motivo & " La riprendi dal telefono, " & If(_indice > 0, "esattamente da qui.", "dall'inizio.")).Trim(), True)
            RaiseEvent DaSalvare()
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Sospendi", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ salvataggio dello stato
    ''' <summary>Il veicolo della missione in corso (Nothing se non c'è).</summary>
    Public ReadOnly Property VeicoloDellaMissione As Veicolo
        Get
            Return _veicoloMissione
        End Get
    End Property

    ''' <summary>Il tipo del passo in corso ("" se non c'è o se si sta ancora ascoltando l'introduzione).</summary>
    Public ReadOnly Property TipoPasso As String
        Get
            Dim p As PassoMissione = PassoAttuale
            Return If(p Is Nothing OrElse _introduzione, "", p.Tipo)
        End Get
    End Property

    ''' <summary>La missione scelta o in corso, al passo in cui si è, per il salvataggio.</summary>
    Public Function Fotografa() As JObject
        Try
            Dim j As New JObject()
            If Scelta IsNot Nothing Then j("scelta") = Scelta.Id
            If Attiva IsNot Nothing Then
                j("attiva") = Attiva.Id
                j("indice") = _indice
                j("controllo") = _controllo
                j("tipoRubato") = _tipoRubato
                j("bonus") = _bonus
            End If
            Return j
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Fotografa", ex)
            Throw
        End Try
    End Function

    ''' <summary>Rimette la missione com'era (senza parlare: poi Riprendi). vm è il veicolo della missione, se c'è.</summary>
    Public Sub Ripristina(j As JObject, vm As Veicolo)
        Try
            If j Is Nothing Then Return
            Dim idScelta As String = StatoPartita.Testo(j, "scelta")
            If idScelta <> "" Then
                Scelta = _m.Dati.Missioni.FirstOrDefault(Function(y) y.Id = idScelta)
                If Scelta IsNot Nothing Then _m.ImpostaMeta(Scelta.Luogo, True)
            End If
            Dim id As String = StatoPartita.Testo(j, "attiva")
            If id = "" Then Return
            Dim mis As Missione = _m.Dati.Missioni.FirstOrDefault(Function(y) y.Id = id)
            If mis Is Nothing Then Return
            Dim n As Integer = CInt(StatoPartita.Numero(j, "indice"))
            If n < 0 OrElse n >= mis.Passi.Count Then Return
            Attiva = mis
            Scelta = Nothing
            _indice = n
            _controllo = CInt(StatoPartita.Numero(j, "controllo"))
            _tipoRubato = StatoPartita.Testo(j, "tipoRubato")
            _bonus = StatoPartita.Numero(j, "bonus")
            _veicoloMissione = vm
            _introduzione = False
            _riprendi = True
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Ripristina", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Dopo il ripristino: ricomincia il passo in cui si era arrivati.</summary>
    Public Sub Riprendi()
        Try
            If Not _riprendi OrElse Attiva Is Nothing Then Return
            _riprendi = False
            _riuso = True
            Try
                _m.Dici("Riprendi la missione: " & Attiva.Titolo & ", passo " & (_indice + 1).ToString() & " di " & Attiva.Passi.Count.ToString() & ".", True)
                IniziaPasso()
            Finally
                _riuso = False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Riprendi", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ cosa succede nel mondo
    ''' <summary>Il veicolo da rubare per la missione in corso (Nothing se non c'è): salendo si preferisce questo.</summary>
    Public ReadOnly Property VeicoloDaPrendere As Veicolo
        Get
            Dim p As PassoMissione = PassoAttuale
            If p IsNot Nothing AndAlso p.Tipo = "ruba" Then Return _veicoloMissione
            ' ripresa da un punto di controllo: il veicolo della missione è lì
            If p IsNot Nothing AndAlso _veicoloMissione IsNot Nothing AndAlso _m.Veicolo IsNot _veicoloMissione Then Return _veicoloMissione
            Return Nothing
        End Get
    End Property

    Public Function PuoiPrendere(v As Veicolo) As Boolean
        Try
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.PuoiPrendere", ex)
            Throw
        End Try
    End Function

    ''' <summary>L'auto da rubare per la missione: si ruba lo stesso, ma la polizia arriva più spesso.</summary>
    Public Function FurtoTollerato(v As Veicolo) As Boolean
        Try
            If v Is _veicoloMissione Then
                If _m.Caso().NextDouble() < 0.25 Then _m.Ricerca(1, "Il proprietario ti ha visto e chiama la volante!")
                Return True
            End If
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.FurtoTollerato", ex)
            Throw
        End Try
    End Function

    Public Sub Salito(v As Veicolo)
    End Sub

    Public Sub Sceso(v As Veicolo)
        Try
            Dim p As PassoMissione = PassoAttuale
            If p IsNot Nothing AndAlso (p.Tipo = "porta" OrElse p.Tipo = "segui" OrElse p.Tipo = "insegui" OrElse p.Tipo = "scorta") AndAlso v.Ruolo <> RuoloVeicolo.Relitto Then
                _m.Dici("Sei sceso, rimettiti al volante per continuare.")
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Sceso", ex)
            Throw
        End Try
    End Sub

    Public Sub Distrutto(v As Veicolo)
        Try
            If Attiva Is Nothing Then Return
            If v Is _veicoloMissione Then
                Dim t As Tratto = v.Pos.Tratto
                _veicoloMissione = _m.CreaVeicoloSu(If(_tipoRubato <> "", _tipoRubato, v.Tipo.Id), t, Math.Min(t.Lunghezza, v.Pos.Distanza + 10), RuoloVeicolo.Parcheggiato)
                _bonus = Math.Min(_bonus, 0) - 0.2
                _m.Dici("L'auto della missione è distrutta, ma qui vicino ne trovi un'altra uguale: la missione continua.", True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Distrutto", ex)
            Throw
        End Try
    End Sub

    Public Sub Urto(v As Veicolo)
        Try
            Dim p As PassoMissione = PassoAttuale
            If p Is Nothing Then Return
            ' scorta: due colpi a chi la attacca; ma attento a non speronare l'auto amica
            If p.Tipo = "scorta" Then
                If v Is _bersaglio Then
                    v.Salute -= 6
                    _m.Dici("Attento, così la rovini tu!", True)
                    AlleatoColpito(v)
                    Return
                End If
                Dim colpi As Integer
                If _nemici.TryGetValue(v, colpi) Then
                    colpi += 1
                    If colpi >= 2 Then
                        _nemici.Remove(v)
                        v.Ruolo = RuoloVeicolo.Relitto
                        v.Velocita = 0
                        _m.Dici("Uno in meno: va a sbattere contro il marciapiede.", True)
                    Else
                        _nemici(v) = colpi
                        _m.Dici("Preso! Ancora un colpo.", True)
                    End If
                End If
                Return
            End If
            If p.Tipo <> "insegui" OrElse v IsNot _bersaglio Then Return
            _colpi += 1
            If _colpi >= 3 Then
                v.InFuga = False
                v.Ruolo = RuoloVeicolo.Parcheggiato
                v.Velocita = 0
                _m.Dici("Terzo colpo: l'auto si ferma, con il motore che fuma. Ce l'hai fatta!", True)
                _bersaglio = Nothing
                Avanti()
            Else
                _m.Dici("Presa! Ancora " & If(3 - _colpi = 1, "un colpo.", (3 - _colpi).ToString() & " colpi."), True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Urto", ex)
            Throw
        End Try
    End Sub

    Public Sub Seminato()
    End Sub

    Public Sub Arrestato()
        Try
            If Attiva Is Nothing Then Return
            Dim vm As Veicolo = _veicoloMissione
            If _bersaglio IsNot Nothing Then _m.TogliVeicolo(_bersaglio)
            PulisciPasso()
            _veicoloMissione = vm
            _m.Dici("La missione resta in piedi: riparti da dove eri.", True)
            _riuso = True
            Try
                IniziaPasso()
            Finally
                _riuso = False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Arrestato", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Vero se il luogo è la meta della missione (il navigatore non la spegne arrivando).</summary>
    Public Function MetaDellaMissione(l As Luogo) As Boolean
        Try
            If Scelta IsNot Nothing AndAlso l Is Scelta.Luogo Then Return True
            If l IsNot Nothing AndAlso l Is _metaRaccolta Then Return True
            Dim p As PassoMissione = PassoAttuale
            Return p IsNot Nothing AndAlso (l Is p.Luogo OrElse l Is p.Da)
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.MetaDellaMissione", ex)
            Throw
        End Try
    End Function

    ''' <summary>M: cosa c'è da fare adesso.</summary>
    Public Function Obiettivo() As String
        Try
            If Attiva Is Nothing Then
                If Scelta IsNot Nothing Then Return "Vai da " & Scelta.Personaggio.Nome & ", a " & Scelta.Luogo.Nome & ", per la missione " & Scelta.Titolo & "."
                If _m.Sfide.InCorso Then Return _m.Sfide.Descrizione()
                Return "Nessuna missione in corso. Controlla sul telefono con il tasto T."
            End If
            Dim p As PassoMissione = PassoAttuale
            If p Is Nothing OrElse _introduzione Then Return Attiva.Titolo & "."
            If _m.Barca IsNot Nothing Then Return _m.Barca.Descrizione()
            Dim t As String = Attiva.Titolo & ", passo " & (_indice + 1).ToString() & " di " & Attiva.Passi.Count.ToString() & ". " & p.Testo
            Select Case p.Tipo
                Case "vai", "ruba", "porta", "furtivo", "barca"
                    t &= " Meta: " & p.Luogo.Nome & ", a " & Mappa.Metri(_m.DistanzaDa(p.Luogo.X, p.Luogo.Y)) & "."
                    If p.Tipo = "porta" AndAlso _tempoPorta > 0 Then t &= " Restano " & TestoSecondi(Math.Max(0, CInt(_tempoPorta - (_m.Tempo - _inizioPasso)))) & "."
                    If p.Tipo = "furtivo" AndAlso Guardie.Count > 0 Then
                        Dim g As Posizione = Guardie.OrderBy(Function(x) _m.DistanzaDa(x.X, x.Y)).First()
                        t &= " La guardia più vicina è a " & Mappa.Metri(_m.DistanzaDa(g.X, g.Y)) & ", " & _m.LatoA(g.X, g.Y) & "."
                    End If
                Case "segui", "insegui", "scorta"
                    If _bersaglio IsNot Nothing Then t &= " L'auto è a " & Mappa.Metri(_m.DistanzaDa(_bersaglio.X, _bersaglio.Y)) & "."
                    If p.Tipo = "scorta" AndAlso _bersaglio IsNot Nothing Then t &= " " & If(_bersaglio.Salute > 60, "È in buono stato.", If(_bersaglio.Salute > 30, "È ammaccata.", "Sta per cedere.")) & If(_nemici.Count > 0, " Assalitori: " & _nemici.Count.ToString() & ".", "")
                Case "raccogli"
                    If Oggetti.Count > 0 Then
                        Dim o As Luogo = Oggetti.OrderBy(Function(x) _m.DistanzaDa(x.X, x.Y)).First()
                        t &= " Ne mancano " & Oggetti.Count.ToString() & "; il più vicino è a " & Mappa.Metri(_m.DistanzaDa(o.X, o.Y)) & ", " & _m.LatoA(o.X, o.Y) & "."
                    End If
                Case "fuggi"
                    t &= " Ricercato: " & _m.TestoStelle() & "."
                Case "aspetta"
                    t &= " Restano " & TestoSecondi(Math.Max(0, CInt(p.Secondi - (_m.Tempo - _inizioPasso)))) & "."
            End Select
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("EsecutoreMissione.Obiettivo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il veicolo da seguire, inseguire o scortare, per disegnarlo sulla mappa.</summary>
    Public ReadOnly Property Bersaglio As Veicolo
        Get
            Return _bersaglio
        End Get
    End Property

    ''' <summary>Il luogo da raggiungere adesso, per disegnarlo sulla mappa.</summary>
    Public ReadOnly Property LuogoDaRaggiungere As Luogo
        Get
            If Attiva Is Nothing Then Return If(Scelta IsNot Nothing, Scelta.Luogo, Nothing)
            Dim p As PassoMissione = PassoAttuale
            If p Is Nothing Then Return Nothing
            If p.Tipo = "raccogli" Then Return _metaRaccolta
            If p.Luogo IsNot Nothing Then Return p.Luogo
            Return p.Da
        End Get
    End Property
End Class
