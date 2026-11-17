Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

' ============================================================================================
' I lavori che si ripetono, tra una missione e l'altra (tasto J per cominciare e per smettere):
'   taxi      sul taxi (lo trovi alle stazioni): vai a prendere il passeggero, portalo dove vuole, in tempo;
'   ambulanza sull'ambulanza (agli ospedali): vai a prendere chi sta male e portalo all'ospedale più vicino, con la sirena;
'   pizze     in macchina davanti a una pizzeria: tre consegne di fila, prima che le pizze si freddino;
'   corse     in macchina davanti a un locale: una corsa clandestina contro uno sfidante, a tappe, per le strade della città.
' Le battute dei passeggeri, dei clienti e degli sfidanti le ha scritte Gemini (testi.json, sezione lavori).
' ============================================================================================

Public NotInheritable Class LavoriCitta
    Private ReadOnly _m As Mondo
    ''' <summary>Il lavoro in corso: "", taxi, ambulanza, pizze, corsa.</summary>
    Public Tipo As String = ""
    Private _fase As String = ""
    Private _meta As Luogo
    Private _scadenza As Double
    Private _inizio As Double
    Private _personaggio As JObject
    Private _consegne As New List(Of Luogo)()
    Private _tappe As New List(Of Luogo)()
    Private _tappa As Integer
    Private _tappaSfidante As Integer
    Private _sfidante As Veicolo
    Private _sirena As VoceSirena
    Private _avvisi As New HashSet(Of Integer)()
    Private _guadagno As Integer
    Private _ritardo As Boolean

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.New", ex)
            Throw
        End Try
    End Sub

    Public ReadOnly Property Attivo As Boolean
        Get
            Return Tipo <> ""
        End Get
    End Property

    ''' <summary>J: comincia il lavoro che si può fare qui, o smette quello in corso.</summary>
    Public Sub CominciaOFinisci()
        Try
            If Attivo Then
                Sospendi("Smetti di lavorare.")
                Return
            End If
            If _m.Economia.LavoroAttivo Then
                _m.Economia.Sospendi("Smetti di lavorare.")
                Return
            End If
            If _m.Missioni.Attiva IsNot Nothing Then
                _m.Dici("Prima finisci la missione.", True)
                Return
            End If
            If _m.APiedi Then
                _m.Dici("Per lavorare ti serve un veicolo: il taxi alle stazioni, l'ambulanza agli ospedali, una macchina qualsiasi davanti a una pizzeria o a un locale per le corse, un furgone a un magazzino per fare il corriere, un fuoristrada a un'officina per il soccorso stradale.", True)
                Return
            End If
            Dim qui As Luogo = _m.LuogoVicino()
            ' un lavoro messo da parte si riprende da dove era, a meno che da qui non se ne cominci uno nuovo
            If _m.Partita.LavoroSospeso IsNot Nothing Then
                Dim nuovoQui As Boolean = _m.Veicolo.Tipo.Id = "taxi" OrElse _m.Veicolo.Tipo.Id = "ambulanza" OrElse (qui IsNot Nothing AndAlso (qui.Tipo = "pizzeria" OrElse qui.Tipo = "club" OrElse qui.Tipo = "magazzino" OrElse qui.Tipo = "officina"))
                Dim sosp As JObject = _m.Partita.LavoroSospeso
                _m.Partita.LavoroSospeso = Nothing
                If Not nuovoQui Then
                    Ripristina(TryCast(sosp("lavori"), JObject))
                    _m.Economia.Ripristina(TryCast(sosp("economia"), JObject))
                    Riprendi()
                    _m.Economia.Riprendi()
                    If Attivo OrElse _m.Economia.LavoroAttivo Then Return
                End If
            End If
            Select Case _m.Veicolo.Tipo.Id
                Case "taxi"
                    Tipo = "taxi"
                    _m.Dici("Accendi l'insegna del taxi: si lavora.", True)
                    NuovaCorsaTaxi()
                Case "ambulanza"
                    Tipo = "ambulanza"
                    _sirena = New VoceSirena() With {.Volume = 0.25F, .Spaziale = False}
                    If _m.Audio IsNot Nothing Then _m.Audio.Aggiungi(_sirena)
                    _m.Dici("Sei di turno sull'ambulanza.", True)
                    NuovaChiamata()
                Case Else
                    If qui IsNot Nothing AndAlso qui.Tipo = "pizzeria" Then
                        Tipo = "pizze"
                        ComincioPizze(qui)
                    ElseIf qui IsNot Nothing AndAlso qui.Tipo = "club" Then
                        Tipo = "corsa"
                        ComincioCorsa(qui)
                    ElseIf _m.Economia.Comincia(qui) Then
                        ' corriere o soccorso stradale
                    Else
                        _m.Dici("Qui non c'è lavoro: il taxi lo trovi alle stazioni, l'ambulanza agli ospedali; con una macchina qualsiasi puoi consegnare pizze da una pizzeria o correre partendo da un locale; con un furgone o un camion fai il corriere da un magazzino; con un fuoristrada fai il soccorso stradale da un'officina.", True)
                    End If
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.CominciaOFinisci", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Smette di lavorare mettendo da parte il lavoro: con il tasto del lavoro lo si riprende da dove era.</summary>
    Private Sub Sospendi(testo As String)
        Try
            Dim j As JObject = Fotografa()
            Dim salvato As Boolean = j("tipo") IsNot Nothing
            If salvato Then _m.Partita.LavoroSospeso = New JObject From {{"lavori", j}}
            Finisci(testo & If(salvato, " Lo riprendi da dove l'hai lasciato.", ""))
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Sospendi", ex)
            Throw
        End Try
    End Sub

    Private Sub Finisci(testo As String)
        Try
            If _sfidante IsNot Nothing Then _m.TogliVeicolo(_sfidante)
            _sfidante = Nothing
            If _sirena IsNot Nothing AndAlso _m.Audio IsNot Nothing Then _m.Audio.Togli(_sirena)
            _sirena = Nothing
            Tipo = ""
            _fase = ""
            _meta = Nothing
            _m.ImpostaMeta(Nothing, True)
            _m.Dici(testo & If(_guadagno > 0, " In tutto hai guadagnato " & _guadagno.ToString() & " euro.", ""), True)
            _guadagno = 0
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Finisci", ex)
            Throw
        End Try
    End Sub

    Private Sub Paga(soldi As Integer)
        Try
            soldi = Math.Max(5, soldi)
            _m.Partita.Guadagna(soldi)
            _m.Partita.LavoriFatti += 1
            _m.Partita.Conta(Tipo)
            _guadagno += soldi
            _m.Suona(MotoreAudio.Soldi(), 0.6F)
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Paga", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un luogo a caso tra minimo e massimo metri da un punto (niente casa e rifugi).</summary>
    Private Function LuogoTra(x As Double, y As Double, minimo As Double, massimo As Double, Optional tipo As String = "") As Luogo
        Try
            Dim adatti As List(Of Luogo) = _m.Dati.Luoghi.Values.Where(Function(l)
                                                                         Dim d As Double = Math.Sqrt((l.X - x) ^ 2 + (l.Y - y) ^ 2)
                                                                         Return d >= minimo AndAlso d <= massimo AndAlso l.Tipo <> "casa" AndAlso l.Tipo <> "rifugio" AndAlso (tipo = "" OrElse l.Tipo = tipo)
                                                                     End Function).ToList()
            If adatti.Count = 0 Then Return _m.Dati.Luoghi.Values.First(Function(l) l.Tipo <> "casa")
            Return adatti(_m.Caso().Next(adatti.Count))
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.LuogoTra", ex)
            Throw
        End Try
    End Function

    Private Function TempoPer(l As Luogo, metriAlSecondo As Double, margine As Double) As Double
        Try
            Dim strada As Double = Mappa.Lunghezza(_m.Dati, _m.Pos, l.Tratto, l.Distanza)
            Return Math.Ceiling((strada / metriAlSecondo + margine) / 10) * 10
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.TempoPer", ex)
            Throw
        End Try
    End Function

    Private Sub VaiA(l As Luogo, testo As String, Optional tempoMax As Double = 0)
        Try
            _meta = l
            _m.ImpostaMeta(l, True)
            _scadenza = If(tempoMax > 0, _m.Tempo + tempoMax, 0)
            _ritardo = False
            _inizio = _m.Tempo
            _avvisi.Clear()
            _m.Dici(testo & " " & l.Nome & ", " & l.Quartiere.Nome & "." & If(tempoMax > 0, " Hai " & Secondi(CInt(tempoMax)) & ".", ""))
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.VaiA", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Secondi(s As Integer) As String
        Try
            If s >= 60 Then Return (s \ 60).ToString() & If(s \ 60 = 1, " minuto", " minuti") & If(s Mod 60 > 0, " e " & (s Mod 60).ToString() & " secondi", "")
            Return s.ToString() & " secondi"
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Secondi", ex)
            Throw
        End Try
    End Function

    Private Function Arrivato(l As Luogo) As Boolean
        Try
            Return l IsNot Nothing AndAlso _m.DistanzaDa(l.X, l.Y) < Mondo.DISTANZA_LUOGO AndAlso Math.Abs(_m.Velocita) < 3 AndAlso Not _m.APiedi
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Arrivato", ex)
            Throw
        End Try
    End Function

    Private Function Testo(o As JObject, chiave As String) As String
        Try
            If o Is Nothing OrElse o(chiave) Is Nothing Then Return ""
            Return o(chiave).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Testo", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ taxi
    Private Sub NuovaCorsaTaxi()
        Try
            _fase = "prendi"
            _personaggio = _m.Dati.OggettoACaso("lavori", "taxi", _m.Caso())
            VaiA(LuogoTra(_m.X, _m.Y, 150, 700), "Una chiamata: " & Testo(_personaggio, "chi") & " ti aspetta a")
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.NuovaCorsaTaxi", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoTaxi()
        Try
            If _fase = "prendi" AndAlso Arrivato(_meta) Then
                Dim dest As Luogo = LuogoTra(_meta.X, _meta.Y, 400, 1500)
                _m.Dici(Testo(_personaggio, "salita"))
                _fase = "porta"
                VaiA(dest, "Portalo a", TempoPer(dest, 9, 30))
            ElseIf _fase = "porta" AndAlso Arrivato(_meta) Then
                Dim inTempo As Boolean = Not _ritardo AndAlso (_scadenza = 0 OrElse _m.Tempo <= _scadenza)
                Dim metri As Double = Math.Sqrt((_meta.X - _m.X) ^ 2 + (_meta.Y - _m.Y) ^ 2)
                Dim tariffa As Integer = CInt(40 + (_m.Tempo - _inizio) * 0.6)
                _m.Dici(If(inTempo, Testo(_personaggio, "arrivo"), Testo(_personaggio, "ritardo")))
                Paga(If(inTempo, tariffa + 20, tariffa \ 2))
                NuovaCorsaTaxi()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.PassoTaxi", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ ambulanza
    Private Sub NuovaChiamata()
        Try
            _fase = "prendi"
            _personaggio = _m.Dati.OggettoACaso("lavori", "ambulanza", _m.Caso())
            VaiA(LuogoTra(_m.X, _m.Y, 200, 900), "Chiamata urgente: c'è qualcuno che sta male a", 0)
            _scadenza = _m.Tempo + TempoPer(_meta, 10, 40)
            _m.Dici("Hai " & Secondi(CInt(_scadenza - _m.Tempo)) & " per arrivare.")
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.NuovaChiamata", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoAmbulanza()
        Try
            If _m.Tempo > _scadenza AndAlso _scadenza > 0 Then
                _scadenza = 0
                _m.Suona(MotoreAudio.Fallimento(), 0.6F)
                _m.Dici("Sei in ritardo, ma il paziente ha ancora bisogno di te: vai lo stesso.", True)
            End If
            If _fase = "prendi" AndAlso Arrivato(_meta) Then
                _m.Dici(Testo(_personaggio, "salita"))
                Dim ospedale As Luogo = _m.Dati.Luoghi.Values.Where(Function(l) l.Tipo = "ospedale").OrderBy(Function(l) _m.DistanzaDa(l.X, l.Y)).FirstOrDefault()
                If ospedale Is Nothing Then
                    Finisci("In città non c'è un ospedale.")
                    Return
                End If
                _fase = "porta"
                VaiA(ospedale, "All'ospedale:", TempoPer(ospedale, 10, 40))
            ElseIf _fase = "porta" AndAlso Arrivato(_meta) Then
                _m.Dici(Testo(_personaggio, "arrivo"))
                Paga(CInt(120 + Math.Max(0, _scadenza - _m.Tempo) * 2))
                NuovaChiamata()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.PassoAmbulanza", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ pizze
    Private Sub ComincioPizze(pizzeria As Luogo)
        Try
            _consegne = Enumerable.Range(0, 3).Select(Function(i) LuogoTra(pizzeria.X, pizzeria.Y, 150, 800)).ToList()
            Dim totale As Double = 0
            Dim px As Double = pizzeria.X, py As Double = pizzeria.Y
            For Each c As Luogo In _consegne
                totale += Math.Sqrt((c.X - px) ^ 2 + (c.Y - py) ^ 2) * 1.4
                px = c.X
                py = c.Y
            Next
            _m.Dici("Tre pizze da consegnare, prima che si freddino.", True)
            _fase = "consegna"
            VaiA(_consegne(0), "Prima consegna:", Math.Ceiling((totale / 9 + 60) / 10) * 10)
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.ComincioPizze", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoPizze()
        Try
            If _scadenza > 0 Then
                Dim manca As Integer = CInt(Math.Ceiling(_scadenza - _m.Tempo))
                For Each soglia As Integer In {60, 30, 10}
                    If manca = soglia AndAlso _avvisi.Add(soglia) Then _m.Dici("Le pizze si freddano: mancano " & Secondi(soglia) & ".")
                Next
            End If
            If Not Arrivato(_meta) Then Return
            Dim inTempo As Boolean = _m.Tempo <= _scadenza
            Dim cliente As JObject = _m.Dati.OggettoACaso("lavori", "pizze", _m.Caso())
            _m.Dici(If(inTempo, Testo(cliente, "consegna"), Testo(cliente, "ritardo")))
            Paga(If(inTempo, 35, 10))
            _consegne.RemoveAt(0)
            If _consegne.Count = 0 Then
                Finisci("Consegne finite.")
                Return
            End If
            _meta = _consegne(0)
            _m.ImpostaMeta(_meta, True)
            _m.Dici("Prossima consegna: " & _meta.Nome & ", " & _meta.Quartiere.Nome & ".")
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.PassoPizze", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ corse
    Private Sub ComincioCorsa(partenza As Luogo)
        Try
            _personaggio = _m.Dati.OggettoACaso("lavori", "corse", _m.Caso())
            ' quattro tappe che si allontanano e poi tornano al locale
            _tappe.Clear()
            Dim x As Double = partenza.X, y As Double = partenza.Y
            For k As Integer = 1 To 3
                Dim t As Luogo = LuogoTra(x, y, 300, 600)
                _tappe.Add(t)
                x = t.X
                y = t.Y
            Next
            _tappe.Add(partenza)
            _tappa = 0
            _tappaSfidante = 0
            _sfidante = _m.CreaVeicolo("sportiva", partenza, RuoloVeicolo.Bersaglio)
            _sfidante.Meta = _tappe(0)
            _sfidante.VelocitaVoluta = 17
            _sfidante.FermoFino = _m.Tempo + 4
            _m.Dici(Testo(_personaggio, "nome") & ": " & Testo(_personaggio, "sfida"), True)
            _m.Dici("Si parte tra tre secondi. Prima tappa: " & _tappe(0).Nome & ".")
            _m.ImpostaMeta(_tappe(0), True)
            _meta = _tappe(0)
            _fase = "corsa"
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.ComincioCorsa", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoCorsa()
        Try
            If _sfidante Is Nothing Then Return
            ' lo sfidante passa le tappe da solo
            Dim ts As Luogo = _tappe(_tappaSfidante)
            If _sfidante.Pos.Tratto Is ts.Tratto AndAlso Math.Abs(_sfidante.Pos.Distanza - ts.Distanza) < 12 Then
                _tappaSfidante += 1
                If _tappaSfidante >= _tappe.Count Then
                    _m.Suona(MotoreAudio.Fallimento(), 0.7F)
                    _m.Dici("Lo sfidante è arrivato prima di te. " & Testo(_personaggio, "nome") & ": " & Testo(_personaggio, "vittoria"), True)
                    Finisci("Corsa persa.")
                    Return
                End If
                _sfidante.Meta = _tappe(_tappaSfidante)
                _sfidante.ProssimoCalcolo = 0
            End If
            If _m.DistanzaDa(_meta.X, _meta.Y) < Mondo.DISTANZA_LUOGO + 4 AndAlso Not _m.APiedi Then
                _tappa += 1
                _m.Suona(MotoreAudio.Bip(True), 0.7F)
                If _tappa >= _tappe.Count Then
                    _m.Partita.CorseVinte += 1
                    _m.Notifica("corsa")
                    _m.Dici("Hai vinto! " & Testo(_personaggio, "nome") & ": " & Testo(_personaggio, "sconfitta"), True)
                    Paga(400 + 100 * Math.Min(5, _m.Partita.CorseVinte))
                    Finisci("Corsa vinta.")
                    Return
                End If
                _meta = _tappe(_tappa)
                _m.ImpostaMeta(_meta, True)
                Dim davanti As Boolean = _tappa > _tappaSfidante
                _m.Dici("Tappa " & _tappa.ToString() & " su " & _tappe.Count.ToString() & ". " & If(davanti, "Sei davanti!", "Lo sfidante è avanti.") & " Prossima: " & _meta.Nome & ".")
            End If
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.PassoCorsa", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ il ciclo e i fatti del mondo
    Public Sub Passo(dt As Double)
        Try
            If Not Attivo Then Return
            If Tipo = "taxi" OrElse Tipo = "pizze" Then
                If _scadenza > 0 AndAlso _m.Tempo > _scadenza + 60 AndAlso _fase <> "prendi" Then
                    _scadenza = 0
                    _ritardo = True
                    _m.Dici("Sei molto in ritardo, ma il cliente aspetta ancora: ti pagheranno meno.", True)
                End If
            End If
            Select Case Tipo
                Case "taxi" : PassoTaxi()
                Case "ambulanza" : PassoAmbulanza()
                Case "pizze" : PassoPizze()
                Case "corsa" : PassoCorsa()
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Passo", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Il lavoro in corso, per il salvataggio (taxi, ambulanza, pizze; gli altri ricominciano da capo).</summary>
    Public Function Fotografa() As JObject
        Try
            Dim j As New JObject()
            If Not Attivo OrElse _meta Is Nothing OrElse Not (Tipo = "taxi" OrElse Tipo = "ambulanza" OrElse Tipo = "pizze") Then Return j
            If Not _m.Dati.Luoghi.ContainsKey(_meta.Id) Then Return j
            j("tipo") = Tipo
            j("fase") = _fase
            j("meta") = _meta.Id
            j("scadenza") = If(_scadenza > 0, Math.Max(30, Math.Round(_scadenza - _m.Tempo)), 0)
            j("guadagno") = _guadagno
            If _personaggio IsNot Nothing Then j("personaggio") = _personaggio.DeepClone()
            j("consegne") = New JArray(_consegne.Where(Function(l) _m.Dati.Luoghi.ContainsKey(l.Id)).Select(Function(l) l.Id).ToArray())
            Return j
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Fotografa", ex)
            Throw
        End Try
    End Function

    Public Sub Ripristina(j As JObject)
        Try
            If j Is Nothing OrElse j("tipo") Is Nothing Then Return
            Dim dest As Luogo = Nothing
            If Not _m.Dati.Luoghi.TryGetValue(StatoPartita.Testo(j, "meta"), dest) Then Return
            Tipo = StatoPartita.Testo(j, "tipo")
            _fase = StatoPartita.Testo(j, "fase")
            _meta = dest
            Dim resto As Double = StatoPartita.Numero(j, "scadenza")
            _scadenza = If(resto > 0, _m.Tempo + resto, 0)
            _inizio = _m.Tempo
            _avvisi.Clear()
            _guadagno = CInt(StatoPartita.Numero(j, "guadagno"))
            _personaggio = TryCast(j("personaggio"), JObject)
            _consegne = New List(Of Luogo)()
            For Each t As JToken In If(TryCast(j("consegne"), JArray), New JArray())
                Dim l As Luogo = Nothing
                If _m.Dati.Luoghi.TryGetValue(t.ToString(), l) Then _consegne.Add(l)
            Next
            If Tipo = "ambulanza" Then
                _sirena = New VoceSirena() With {.Volume = 0.25F, .Spaziale = False}
                If _m.Audio IsNot Nothing Then _m.Audio.Aggiungi(_sirena)
            End If
            _m.ImpostaMeta(_meta, True)
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Ripristina", ex)
            Throw
        End Try
    End Sub

    Public Sub Riprendi()
        Try
            If Attivo Then _m.Dici("Riprendi il lavoro. " & Descrizione(), True)
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Riprendi", ex)
            Throw
        End Try
    End Sub

    Public Function MetaDelLavoro(l As Luogo) As Boolean
        Try
            Return Attivo AndAlso l Is _meta
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.MetaDelLavoro", ex)
            Throw
        End Try
    End Function

    ''' <summary>Cosa c'è da fare nel lavoro in corso ("" se nessuno).</summary>
    Public Function Descrizione() As String
        Try
            If Not Attivo OrElse _meta Is Nothing Then Return ""
            Dim nomi As New Dictionary(Of String, String) From {{"taxi", "Taxi"}, {"ambulanza", "Ambulanza"}, {"pizze", "Pizze"}, {"corsa", "Corsa"}}
            Dim t As String = nomi(Tipo) & ": " & _meta.Nome & ", a " & Mappa.Metri(_m.DistanzaDa(_meta.X, _meta.Y)) & "."
            If _scadenza > 0 Then t &= " Restano " & Secondi(Math.Max(0, CInt(_scadenza - _m.Tempo))) & "."
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Descrizione", ex)
            Throw
        End Try
    End Function

    Public Sub Salito(v As Veicolo)
    End Sub

    Public Sub Sceso(v As Veicolo)
        Try
            If Attivo Then _m.Dici("Sei sceso: il lavoro resta com'è, risali su un veicolo per continuare.")
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Sceso", ex)
            Throw
        End Try
    End Sub

    Public Sub Distrutto(v As Veicolo)
        Try
            If Attivo Then _m.Dici("Il veicolo è distrutto, ma il lavoro non si ferma: prendine un altro.", True)
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Distrutto", ex)
            Throw
        End Try
    End Sub

    Public Sub Urto(v As Veicolo)
    End Sub

    Public Sub Arrestato()
        Try
            If Attivo Then _m.Dici("Dopo l'arresto il lavoro ti aspetta ancora.", True)
        Catch ex As Exception
            RegistroErrori.Registra("LavoriCitta.Arrestato", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Per la mappa: lo sfidante della corsa.</summary>
    Public ReadOnly Property Sfidante As Veicolo
        Get
            Return _sfidante
        End Get
    End Property

    Public ReadOnly Property MetaAttuale As Luogo
        Get
            Return _meta
        End Get
    End Property
End Class
