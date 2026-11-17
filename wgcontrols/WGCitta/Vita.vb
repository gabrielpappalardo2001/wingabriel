Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks

' ============================================================================================
' La vita della città: quello che si sente intorno anche quando non succede niente.
'  - I rumori di fondo veri di ogni posto, che cambiano con l'ora: le onde e i gabbiani al porto, le barche alla marina, il mercato la
'    mattina, i bambini e la fontana ai giardini, il cantiere all'industriale, la movida la sera sul lungomare, il sagrato davanti alle chiese.
'  - La gente per strada: passanti che chiacchierano, venditori ambulanti, il vigile che fischia se lasci la macchina in mezzo alla strada.
'    Le loro battute le ha scritte Gemini e le dicono le voci di Windows, da un punto intorno a te.
'  - Le campane a mezzogiorno e la sera, le navi che suonano dal mare, cani e piccioni.
'  - Le telefonate degli amici e dei personaggi (chi ti stima di più chiama più spesso).
'  - Le bande dei quartieri: chi non ti rispetta ti tende un agguato in macchina; chi ti rispetta ti lascia passare e ti copre con la polizia.
' Le battute dette dalle voci di Windows vanno anche nell'elenco degli annunci da rileggere (evento Sentito del mondo).
' ============================================================================================

Public NotInheritable Class VitaCitta
    Private ReadOnly _m As Mondo
    Private ReadOnly _strati As New Dictionary(Of String, VoceRegistrata)()
    Private _prossimaVoce As Double = 8
    Private _voceInPreparazione As Boolean
    Private _prossimaTelefonata As Double = 150
    Private _prossimaNave As Double = 40
    Private _prossimoAnimale As Double = 30
    Private _ultimaOra As Integer = -1
    Private _fermoDa As Double
    Private _vigile As Integer
    Private _ultimoQuartiere As Quartiere
    Private ReadOnly _salutati As New Dictionary(Of String, Integer)()
    Private _scena As VoceRegistrata
    ''' <summary>La macchina della banda che ti tende un agguato (Nothing se nessuna).</summary>
    Public Agguato As Veicolo
    Private _fineAgguato As Double
    Private _colpiAgguato As Integer
    Private _bandaAgguato As String = ""

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.New", ex)
            Throw
        End Try
    End Sub

    Public Sub Passo(dt As Double)
        Try
            AggiornaFondo()
            If _m.Scuola OrElse _m.Barca IsNot Nothing Then Return
            Voci()
            Telefonate()
            Campane()
            NaviEAnimali()
            Vigile(dt)
            Bande()
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Passo", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ i rumori di fondo
    Private Sub Strato(nome As String, volume As Double, Optional azimut As Single = 0, Optional spaziale As Boolean = False)
        Try
            If _m.Audio Is Nothing Then Return
            Dim v As VoceRegistrata = Nothing
            If Not _strati.TryGetValue(nome, v) Then
                If volume < 0.005 Then Return
                v = New VoceRegistrata(nome, True) With {.Volume = 0, .Spaziale = spaziale}
                _strati(nome) = v
                If v.Presente Then _m.Audio.Aggiungi(v)
            End If
            v.Volume = CSng(Math.Max(0, Math.Min(1, volume)))
            v.Azimut = azimut
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Strato", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Vicinanza (da 0 a 1) al luogo più vicino di un tipo, entro raggio metri.</summary>
    Private Function Vicino(tipo As String, raggio As Double) As Double
        Try
            Dim d As Double = Double.MaxValue
            For Each l As Luogo In _m.Dati.Luoghi.Values
                If l.Tipo <> tipo Then Continue For
                Dim x As Double = _m.DistanzaDa(l.X, l.Y)
                If x < d Then d = x
            Next
            Return Math.Max(0, 1 - d / raggio)
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Vicino", ex)
            Throw
        End Try
    End Function

    ''' <summary>Vero se l'ora è tra da (compresa) e a (esclusa), anche a cavallo della mezzanotte.</summary>
    Private Function Tra(da As Double, a As Double) As Boolean
        Try
            Dim h As Double = _m.Partita.Ora
            Return If(da <= a, h >= da AndAlso h < a, h >= da OrElse h < a)
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Tra", ex)
            Throw
        End Try
    End Function

    Private Sub AggiornaFondo()
        Try
            If _m.Audio Is Nothing Then Return
            Dim q As String = _m.Dati.QuartiereDi(_m.X, _m.Y).Id
            Dim y As Double = _m.Y
            ' in macchina si sente meno, in galleria quasi niente, dentro un luogo poco
            Dim f As Double = If(_m.InGalleria, 0.1, If(_m.APiedi, 1.0, 0.5))
            If _scena IsNot Nothing Then f *= 0.25
            If _m.Barca IsNot Nothing Then f = 0
            Dim giorno As Boolean = Tra(7, 20)
            Dim mare As Double = Math.Max(0, 1 - y / 260)
            Dim arteria As Boolean = _m.Pos.Tratto.Strada.Arteria
            Dim vicolo As Boolean = Not arteria AndAlso (q = "centro" OrElse q = "castello" OrElse q = "giardini")
            Strato("fondo_onde", 0.32 * mare * f, _m.AzimutVerso(_m.X, -300), True)
            Strato("fondo_gabbiani", If(giorno AndAlso (q = "porto" OrElse q = "marina" OrElse q = "lungomare"), 0.22 * mare * f, 0), _m.AzimutVerso(_m.X, -150), True)
            Strato("fondo_porticciolo", If(q = "porto", 0.18 * Math.Max(0, 1 - y / 300) * f, 0))
            Strato("fondo_pontile", If(q = "marina", 0.18 * Math.Max(0, 1 - y / 300) * f, 0))
            Strato("fondo_alberi_barche", If(q = "marina" AndAlso Not giorno, 0.15 * mare * f, 0))
            Strato("fondo_strada", If(arteria AndAlso Tra(7, 23), 0.14 * f, 0))
            Strato("fondo_vicolo", If(vicolo AndAlso giorno, 0.14 * f, 0))
            Strato("fondo_piazza", If(giorno, 0.2 * Vicino("piazza", 160) * f, 0))
            Strato("fondo_mercato", If(Tra(7, 13.5), 0.28 * Vicino("mercato", 220) * f, 0))
            Strato("fondo_mercato2", If(Tra(7, 13.5) AndAlso q = "giardini", 0.12 * f, 0))
            Strato("fondo_notte", If(Tra(21, 2) AndAlso (q = "lungomare" OrElse q = "centro"), 0.2 * f, 0))
            Strato("fondo_cantiere", If(Tra(7, 18) AndAlso q = "industriale", 0.16 * f, 0))
            Strato("fondo_fontana", 0.18 * Vicino("parco", 120) * f)
            Strato("fondo_bambini", If(Tra(9, 19), 0.18 * Vicino("parco", 160) * f, 0))
            Strato("fondo_sagrato", If(giorno, 0.14 * Vicino("chiesa", 90) * f, 0))
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.AggiornaFondo", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Dentro un luogo: il suo rumore (bar, ristorante, negozio, folla) al posto della strada; Nothing per uscire.</summary>
    Public Sub Scena(tipo As String)
        Try
            If _m.Audio Is Nothing Then Return
            If _scena IsNot Nothing Then _m.Audio.Togli(_scena)
            _scena = Nothing
            If tipo Is Nothing Then Return
            Dim nome As String
            Select Case tipo
                Case "bar" : nome = "scena_bar"
                Case "ristorante", "pizzeria", "albergo" : nome = "scena_ristorante"
                Case "negozio", "distributore", "banca", "stazione", "ospedale" : nome = "scena_negozio"
                Case "mercato" : nome = "fondo_mercato"
                Case "club", "piazza" : nome = "scena_folla"
                Case "chiesa" : nome = "fondo_sagrato"
                Case Else : nome = "scena_caffe"
            End Select
            _scena = New VoceRegistrata(nome, True) With {.Volume = 0.5F, .Spaziale = False}
            If _scena.Presente Then _m.Audio.Aggiungi(_scena)
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Scena", ex)
            Throw
        End Try
    End Sub

    Public Sub Spegni()
        Try
            If _m.Audio Is Nothing Then Return
            For Each v As VoceRegistrata In _strati.Values
                _m.Audio.Togli(v)
            Next
            _strati.Clear()
            If _scena IsNot Nothing Then _m.Audio.Togli(_scena)
            _scena = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Spegni", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ le voci della gente
    ''' <summary>Una battuta detta da una voce di Windows, da un punto intorno (azimut), poi messa tra gli annunci da rileggere.</summary>
    Public Sub Parla(chi As String, testo As String, donna As Boolean, azimut As Single, volume As Single, Optional telefono As Boolean = False)
        Try
            If String.IsNullOrWhiteSpace(testo) OrElse _voceInPreparazione Then Return
            _voceInPreparazione = True
            Dim variante As Integer = _m.Caso().Next(4)
            Dim velocita As Integer = _m.Caso().Next(-1, 3)
            Task.Run(Function() VociCitta.Sintetizza(testo, donna, velocita, variante, telefono)).ContinueWith(
                Sub(t)
                    Try
                        _voceInPreparazione = False
                        If t.IsFaulted OrElse t.Result Is Nothing OrElse _m.Audio Is Nothing Then Return
                        Dim v As New VoceCampioni(t.Result) With {.Spaziale = Not telefono, .Azimut = azimut, .Volume = volume}
                        _m.Audio.Aggiungi(v)
                        _m.Senti(If(chi = "", testo, chi & ": " & testo))
                    Catch ex As Exception
                        RegistroErrori.Registra("VitaCitta.Parla (voce pronta)", ex)
                    End Try
                End Sub)
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Parla", ex)
            Throw
        End Try
    End Sub

    Private Function VitaQui() As VitaQuartiere
        Try
            Dim v As VitaQuartiere = Nothing
            _m.Dati.Vita.TryGetValue(_m.Dati.QuartiereDi(_m.X, _m.Y).Id, v)
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.VitaQui", ex)
            Throw
        End Try
    End Function

    Private Function ACaso(elenco As List(Of String)) As String
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return ""
            Return elenco(_m.Caso().Next(elenco.Count))
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.ACaso", ex)
            Throw
        End Try
    End Function

    Private Sub Voci()
        Try
            If _m.Tempo < _prossimaVoce OrElse _m.InGalleria Then Return
            Dim notte As Boolean = Tra(22, 6)
            _prossimaVoce = _m.Tempo + If(notte, 35 + _m.Caso().NextDouble() * 40, 14 + _m.Caso().NextDouble() * 18)
            ' in macchina veloce non si sente nessuno
            If Not _m.APiedi AndAlso Math.Abs(_m.Velocita) > 9 Then Return
            Dim v As VitaQuartiere = VitaQui()
            If v Is Nothing Then Return
            Dim testo As String
            If Not notte AndAlso Tra(7, 20) AndAlso _m.Caso().NextDouble() < 0.3 AndAlso v.Ambulanti.Count > 0 Then
                testo = ACaso(v.Ambulanti)
            Else
                testo = ACaso(v.Passanti)
            End If
            Dim lato As Single = CSng((0.4 + _m.Caso().NextDouble() * 2.4) * If(_m.Caso().Next(2) = 0, 1, -1))
            Parla("", testo, _m.Caso().Next(2) = 0, lato, If(_m.APiedi, 0.55F, 0.3F))
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Voci", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ le telefonate
    Private Sub Telefonate()
        Try
            If _m.Tempo < _prossimaTelefonata Then Return
            _prossimaTelefonata = _m.Tempo + 160 + _m.Caso().NextDouble() * 200
            If _m.Missioni.Attiva IsNot Nothing OrElse _m.Stelle > 0 OrElse _m.Dati.Telefonate.Count = 0 Then Return
            ' chi chiama: un personaggio che ti stima (o con cui hai lavorato), se no un amico
            Dim conosciuti As List(Of Personaggio) = _m.Dati.Personaggi.Values.Where(Function(p) Not _m.Partita.Bloccati.Contains(p.Id) AndAlso
                                                                                    (_m.Partita.StimaDi(p.Id) > 0 OrElse _m.Dati.Missioni.Any(Function(x) x.Personaggio Is p AndAlso _m.Partita.Completate.Contains(x.Id)))).ToList()
            Dim chi As Personaggio = If(conosciuti.Count > 0, conosciuti.OrderByDescending(Function(p) _m.Partita.StimaDi(p.Id) + _m.Caso().Next(40)).First(), Nothing)
            Dim testo As String
            Dim offerta As Missione = If(chi Is Nothing, Nothing, _m.Missioni.Disponibili().FirstOrDefault(Function(x) x.Personaggio Is chi))
            If offerta IsNot Nothing AndAlso _m.Caso().NextDouble() < 0.4 Then
                testo = "Ho un lavoro per te: " & offerta.Titolo & ". Passa da me, a " & offerta.Luogo.Nome & "."
            Else
                Dim quartieri As List(Of Quartiere) = _m.Dati.Quartieri
                testo = ACaso(_m.Dati.Telefonate).Replace("{quartiere}", quartieri(_m.Caso().Next(quartieri.Count)).Nome)
            End If
            _m.Suona(MotoreAudio.Squillo(), 0.6F)
            Dim nome As String = If(chi Is Nothing, "un amico", chi.Nome)
            _m.Dici("Telefonata da " & nome & ".")
            ' la voce: donna se il nome del personaggio finisce per a (Carmela, Silvana...), altrimenti uomo
            Dim donna As Boolean = chi IsNot Nothing AndAlso chi.Nome.Split(" "c)(0).EndsWith("a")
            Parla(nome, testo, donna, 0, 0.75F, True)
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Telefonate", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ campane, navi, animali
    Private Sub Campane()
        Try
            Dim ora As Integer = CInt(Math.Floor(_m.Partita.Ora))
            If ora = _ultimaOra Then Return
            Dim prima As Integer = _ultimaOra
            _ultimaOra = ora
            If prima < 0 Then Return
            Dim chiesa As Luogo = _m.Dati.Luoghi.Values.Where(Function(l) l.Tipo = "chiesa").OrderBy(Function(l) _m.DistanzaDa(l.X, l.Y)).FirstOrDefault()
            If chiesa Is Nothing Then Return
            Dim d As Double = _m.DistanzaDa(chiesa.X, chiesa.Y)
            If d > 700 Then Return
            Dim volume As Single = CSng(0.7 / (1 + d / 140))
            If ora = 12 OrElse ora = 19 Then
                _m.Suona(MotoreAudio.Registrato("campane"), volume, _m.AzimutVerso(chiesa.X, chiesa.Y))
            ElseIf ora >= 8 AndAlso ora <= 21 Then
                _m.Suona(MotoreAudio.Registrato(If(_m.Caso().Next(2) = 0, "campana_ora", "campana")), volume * 0.8F, _m.AzimutVerso(chiesa.X, chiesa.Y))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Campane", ex)
            Throw
        End Try
    End Sub

    Private Sub NaviEAnimali()
        Try
            If _m.Tempo >= _prossimaNave Then
                _prossimaNave = _m.Tempo + 70 + _m.Caso().NextDouble() * 90
                If _m.Y < 400 AndAlso Not _m.InGalleria Then _m.Suona(MotoreAudio.Registrato(If(_m.Caso().Next(2) = 0, "nave", "nave2")), CSng(0.45 * Math.Max(0.2, 1 - _m.Y / 400)), _m.AzimutVerso(_m.X + _m.Caso().Next(-300, 300), -500))
            End If
            If _m.Tempo >= _prossimoAnimale Then
                _prossimoAnimale = _m.Tempo + 35 + _m.Caso().NextDouble() * 60
                Dim q As String = _m.Dati.QuartiereDi(_m.X, _m.Y).Id
                If Tra(7, 21) AndAlso (q = "giardini" OrElse q = "castello" OrElse q = "collina" OrElse q = "centro") AndAlso Not _m.InGalleria Then
                    Dim lato As Single = CSng((_m.Caso().NextDouble() * 2 - 1) * Math.PI)
                    _m.Suona(MotoreAudio.Registrato(If(_m.Caso().Next(3) = 0, "piccioni", "cane")), If(_m.APiedi, 0.35F, 0.18F), lato)
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.NaviEAnimali", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ il vigile
    Private Sub Vigile(dt As Double)
        Try
            Dim q As String = _m.Dati.QuartiereDi(_m.X, _m.Y).Id
            Dim inSosta As Boolean = Not _m.APiedi AndAlso Math.Abs(_m.Velocita) < 0.3 AndAlso _m.LuogoVicino() Is Nothing AndAlso _m.Stelle = 0 AndAlso
                                     Tra(8, 20) AndAlso (q = "centro" OrElse q = "giardini" OrElse q = "porto" OrElse q = "lungomare") AndAlso _m.Missioni.Attiva Is Nothing
            If Not inSosta Then
                _fermoDa = 0
                _vigile = 0
                Return
            End If
            _fermoDa += dt
            Dim v As VitaQuartiere = VitaQui()
            If _vigile = 0 AndAlso _fermoDa > 25 Then
                _vigile = 1
                Dim lato As Single = If(_m.Caso().Next(2) = 0, 1.2F, -1.2F)
                _m.Suona(MotoreAudio.Fischio(), 0.6F, lato)
                If v IsNot Nothing Then Parla("Il vigile", ACaso(v.Vigile), _m.Caso().Next(2) = 0, lato, 0.6F)
            ElseIf _vigile = 1 AndAlso _fermoDa > 55 Then
                _vigile = 2
                Dim multa As Integer = Math.Min(60, _m.Partita.Soldi)
                _m.Partita.Soldi -= multa
                _m.Dici("Il vigile ti lascia la multa sul parabrezza: " & multa.ToString() & " euro.", True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Vigile", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ le bande
    Private Sub Bande()
        Try
            Dim q As Quartiere = _m.Dati.QuartiereDi(_m.X, _m.Y)
            If q IsNot _ultimoQuartiere Then
                _ultimoQuartiere = q
                EntraNelQuartiere(q)
            End If
            If Agguato Is Nothing Then Return
            If Agguato.Ruolo <> RuoloVeicolo.Banda OrElse _m.Tempo > _fineAgguato OrElse _m.DistanzaDa(Agguato.X, Agguato.Y) > 420 OrElse _m.APiedi Then
                FineAgguato(False)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Bande", ex)
            Throw
        End Try
    End Sub

    Private Sub EntraNelQuartiere(q As Quartiere)
        Try
            Dim v As VitaQuartiere = Nothing
            If Not _m.Dati.Vita.TryGetValue(q.Id, v) OrElse v.NomeBanda = "" Then Return
            Dim r As Integer = _m.Partita.RispettoDi(q.Id)
            If r >= 40 Then
                ' una volta al giorno ti salutano
                Dim giorno As Integer = 0
                If _salutati.TryGetValue(q.Id, giorno) AndAlso giorno = _m.Partita.Giorno Then Return
                _salutati(q.Id) = _m.Partita.Giorno
                Parla(v.NomeBanda, v.Passaggio, False, CSng(If(_m.Caso().Next(2) = 0, 1.3, -1.3)), 0.55F)
            ElseIf r <= -20 AndAlso Agguato Is Nothing AndAlso Not _m.APiedi AndAlso _m.Missioni.Attiva Is Nothing AndAlso _m.Caso().NextDouble() < 0.5 Then
                Dim t As Tratto = _m.TrattoACaso(_m.Pos, 140, 300)
                Agguato = _m.CreaVeicoloSu("fuoristrada", t, t.Lunghezza / 2, RuoloVeicolo.Banda)
                Agguato.Preda = Nothing
                _fineAgguato = _m.Tempo + 75
                _colpiAgguato = 0
                _bandaAgguato = q.Id
                _m.Suona(MotoreAudio.Sgommata(), 0.7F, _m.AzimutVerso(Agguato.X, Agguato.Y))
                _m.Notifica("agguato")
                _m.Dici("Agguato! " & v.NomeBanda & " ti viene addosso con un fuoristrada: speronalo due volte o scappa dal quartiere.", True)
                Parla(v.Capo, v.Minaccia, False, _m.AzimutVerso(Agguato.X, Agguato.Y), 0.7F)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.EntraNelQuartiere", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Hai urtato un veicolo: se è quello dell'agguato, conta il colpo.</summary>
    Public Sub Urto(v As Veicolo)
        Try
            If v Is Nothing OrElse v IsNot Agguato Then Return
            _colpiAgguato += 1
            If _colpiAgguato >= 2 Then
                FineAgguato(True)
            Else
                _m.Dici("Colpito! Ancora una volta.", True)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.Urto", ex)
            Throw
        End Try
    End Sub

    Private Sub FineAgguato(vinto As Boolean)
        Try
            Dim v As Veicolo = Agguato
            Agguato = Nothing
            If v Is Nothing Then Return
            If vinto Then
                v.Ruolo = RuoloVeicolo.Relitto
                v.Velocita = 0
                _m.Partita.CambiaRispetto(_bandaAgguato, 15)
                _m.Partita.Conta("agguati")
                _m.Suona(MotoreAudio.Successo(), 0.6F)
                _m.Dici("Il fuoristrada della banda si pianta contro il marciapiede: ti sei fatto rispettare.", True)
            Else
                _m.TogliVeicolo(v)
                _m.Dici("La banda ti ha perso.")
            End If
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.FineAgguato", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Il rispetto della banda del quartiere in cui sei, a voce.</summary>
    Public Function DescriviBanda() As String
        Try
            Dim q As Quartiere = _m.Dati.QuartiereDi(_m.X, _m.Y)
            Dim v As VitaQuartiere = Nothing
            If Not _m.Dati.Vita.TryGetValue(q.Id, v) OrElse v.NomeBanda = "" Then Return ""
            Dim r As Integer = _m.Partita.RispettoDi(q.Id)
            Dim come As String = If(r >= 40, "ti rispettano e ti coprono con la polizia", If(r <= -20, "ce l'hanno con te: attento agli agguati", "ancora non ti conoscono"))
            Return q.Nome & " è di " & v.NomeBanda & ", il capo è " & v.Capo & ". " & v.DescrizioneBanda & " Qui " & come & "."
        Catch ex As Exception
            RegistroErrori.Registra("VitaCitta.DescriviBanda", ex)
            Throw
        End Try
    End Function
End Class
