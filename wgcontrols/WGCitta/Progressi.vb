Imports System
Imports System.Collections.Generic
Imports System.Linq

' ============================================================================================
' I progressi: i trofei (nomi e frasi scritti da Gemini) e le statistiche della partita; le sfide a tempo tra due luoghi della città,
' con il miglior tempo di ognuna.
' ============================================================================================

Public NotInheritable Class ProgressiCitta
    Private ReadOnly _m As Mondo
    Private _prossimoControllo As Double = 3

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Vero se il traguardo del trofeo è raggiunto.</summary>
    Private Function Raggiunto(id As String) As Boolean
        Try
            Dim p As Salvataggio = _m.Partita
            Select Case id
                Case "prima_missione" : Return p.Completate.Count >= 1
                Case "capitolo3" : Return _m.Missioni.CapitoloAttuale() >= 3
                Case "capitolo6" : Return _m.Missioni.CapitoloAttuale() >= 6
                Case "finale" : Return p.Finale
                Case "ricco" : Return p.Soldi + p.Banca >= 50000
                Case "milionario" : Return p.Soldi + p.Banca >= 200000
                Case "capitolo10" : Return _m.Missioni.CapitoloAttuale() >= 10
                Case "mercante" : Return p.Contatore("commercio") >= 20000
                Case "risparmiatore" : Return p.Banca >= 100000
                Case "corriere" : Return p.Contatore("corriere") >= 40
                Case "ricettatore" : Return p.Contatore("ricettati") >= 10
                Case "a_secco" : Return p.Contatore("a_secco") >= 1
                Case "storie" : Return _m.Dati.Missioni.Where(Function(x) x.Storia AndAlso p.Completate.Contains(x.Id)).Count() >= 30
                Case "marinaio" : Return p.Contatore("barca") >= 5
                Case "fantasma" : Return p.Contatore("furtivo") >= 8
                Case "centauro" : Return p.Contatore("metri_moto") >= 50000
                Case "soccorso_stradale" : Return p.Contatore("carro") >= 15
                Case "contrabbandiere" : Return p.Contatore("contrabbando") >= 100
                Case "guardia_del_corpo" : Return p.Contatore("scorte") >= 10
                Case "tutte" : Return _m.Dati.Missioni.All(Function(x) p.Completate.Contains(x.Id) OrElse (Not x.Finale AndAlso p.Bloccati.Contains(x.Personaggio.Id)))
                Case "tassista" : Return p.Contatore("taxi") >= 20
                Case "pizzaiolo" : Return p.Contatore("pizze") >= 30
                Case "soccorritore" : Return p.Contatore("ambulanza") >= 15
                Case "pilota" : Return p.CorseVinte >= 10
                Case "cinque_stelle" : Return p.MassimoStelle >= 5
                Case "seminato" : Return p.Contatore("seminato4") >= 1
                Case "galleria" : Return p.Contatore("galleria") >= 1
                Case "arrestato" : Return p.Arresti >= 10
                Case "esploratore" : Return p.Metri >= 100000
                Case "collezionista" : Return p.Nascosti.Count >= 10
                Case "tesoro" : Return _m.Dati.Nascosti.Count > 0 AndAlso p.Nascosti.Count >= _m.Dati.Nascosti.Count
                Case "proprietario" : Return p.Proprieta.Where(Function(x) _m.Dati.Luoghi.ContainsKey(x) AndAlso _m.Dati.Luoghi(x).Tipo <> "rifugio").Count() >= 5
                Case "immobiliare" : Return p.Proprieta.Where(Function(x) _m.Dati.Luoghi.ContainsKey(x) AndAlso _m.Dati.Luoghi(x).Tipo = "rifugio").Count() >= 3
                Case "garage" : Return p.Posseduti.Count >= 5
                Case "rispettato" : Return _m.Dati.Quartieri.All(Function(q) p.RispettoDi(q.Id) >= 40)
                Case "buon_samaritano" : Return p.Contatore("scippatori") >= 5
                Case "autostoppista" : Return p.Contatore("passaggi") >= 10
                Case "sfidante" : Return _m.Dati.Sfide.Count > 0 AndAlso _m.Dati.Sfide.All(Function(s) p.Record.ContainsKey(s.Id) AndAlso p.Record(s.Id) <= s.Tempo)
                Case "notturno" : Return p.Contatore("metri_notte") >= 20000
                Case Else : Return False
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.Raggiunto", ex)
            Throw
        End Try
    End Function

    Public Sub Passo(dt As Double)
        Try
            If _m.Tempo < _prossimoControllo Then Return
            _prossimoControllo = _m.Tempo + 2
            For Each t As Trofeo In _m.Dati.Trofei
                If _m.Partita.Trofei.Contains(t.Id) OrElse Not Raggiunto(t.Id) Then Continue For
                _m.Partita.Trofei.Add(t.Id)
                _m.Suona(MotoreAudio.Fanfara(), 0.7F)
                _m.Dici("Trofeo: " & t.Nome & ". " & t.Descrizione)
                Exit For
            Next
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.Passo", ex)
            Throw
        End Try
    End Sub

    ''' <summary>I trofei: presi e da prendere, per il menu.</summary>
    Public Function ElencoTrofei() As List(Of String)
        Try
            Return _m.Dati.Trofei.Select(Function(t) If(_m.Partita.Trofei.Contains(t.Id), "Preso: " & t.Nome & ". " & t.Descrizione, "Da prendere: " & t.Nome & ", " & Traguardo(t.Id))).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.ElencoTrofei", ex)
            Throw
        End Try
    End Function

    ''' <summary>Cosa serve per un trofeo (a voce).</summary>
    Private Shared Function Traguardo(id As String) As String
        Try
            Select Case id
                Case "prima_missione" : Return "fai la prima missione"
                Case "capitolo3" : Return "arriva al capitolo 3"
                Case "capitolo6" : Return "arriva al capitolo 6"
                Case "finale" : Return "arriva alla fine della storia"
                Case "capitolo10" : Return "arriva al capitolo 10"
                Case "mercante" : Return "guadagna 20.000 euro con il commercio"
                Case "risparmiatore" : Return "metti 100.000 euro in banca"
                Case "corriere" : Return "consegna 40 pacchi"
                Case "ricettatore" : Return "vendi 10 veicoli rubati"
                Case "a_secco" : Return "resta senza benzina"
                Case "storie" : Return "finisci 30 storie di quartiere"
                Case "marinaio" : Return "vinci 5 inseguimenti in barca"
                Case "fantasma" : Return "riesci in 8 missioni di nascosto"
                Case "centauro" : Return "fai 50 chilometri in moto"
                Case "soccorso_stradale" : Return "traina 15 macchine"
                Case "contrabbandiere" : Return "vendi 100 unità di merce di contrabbando"
                Case "guardia_del_corpo" : Return "porta a termine 10 scorte"
                Case "tutte" : Return "finisci tutte le missioni"
                Case "ricco" : Return "metti da parte 50.000 euro"
                Case "milionario" : Return "metti da parte 200.000 euro"
                Case "tassista" : Return "fai 20 corse in taxi"
                Case "pizzaiolo" : Return "consegna 30 pizze"
                Case "soccorritore" : Return "porta 15 persone in ospedale"
                Case "pilota" : Return "vinci 10 corse"
                Case "cinque_stelle" : Return "fatti cercare con cinque stelle"
                Case "seminato" : Return "semina la polizia dopo un inseguimento da quattro stelle"
                Case "galleria" : Return "semina l'elicottero restando in galleria"
                Case "arrestato" : Return "fatti arrestare 10 volte"
                Case "esploratore" : Return "percorri 100 chilometri"
                Case "collezionista" : Return "trova 10 oggetti nascosti"
                Case "tesoro" : Return "trova tutti gli oggetti nascosti"
                Case "proprietario" : Return "compra 5 attività"
                Case "immobiliare" : Return "compra 3 rifugi"
                Case "garage" : Return "metti in garage 5 veicoli diversi"
                Case "rispettato" : Return "fatti rispettare da tutte le bande"
                Case "buon_samaritano" : Return "ferma 5 scippatori"
                Case "autostoppista" : Return "dai 10 passaggi"
                Case "sfidante" : Return "batti il tempo di tutte le sfide"
                Case "notturno" : Return "guida 20 chilometri di notte"
                Case Else : Return ""
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.Traguardo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le statistiche della partita, una riga per voce.</summary>
    Public Function Statistiche() As List(Of String)
        Try
            Dim p As Salvataggio = _m.Partita
            Dim minuti As Integer = CInt(p.Contatore("secondi") / 60)
            Dim r As New List(Of String) From {
                "Tempo di gioco: " & (minuti \ 60).ToString() & " ore e " & (minuti Mod 60).ToString() & " minuti, giorno " & p.Giorno.ToString() & " in città",
                "Capitolo " & _m.Missioni.CapitoloAttuale().ToString() & ", missioni fatte: " & p.Completate.Count.ToString() & " su " & _m.Dati.Missioni.Count.ToString() & ", fallite: " & p.Fallite.ToString(),
                "Soldi: " & p.Soldi.ToString() & " euro in tasca e " & p.Banca.ToString() & " in banca, guadagnati in tutto: " & CLng(p.SoldiGuadagnati).ToString() & " euro",
                "Strada fatta: " & (p.Metri / 1000).ToString("0.0") & " chilometri, di notte " & (p.Contatore("metri_notte") / 1000).ToString("0.0"),
                "Corse in taxi: " & CInt(p.Contatore("taxi")).ToString() & ", pizze consegnate: " & CInt(p.Contatore("pizze")).ToString() & ", soccorsi in ambulanza: " & CInt(p.Contatore("ambulanza")).ToString() & ", corse vinte: " & p.CorseVinte.ToString(),
                "Pacchi consegnati: " & CInt(p.Contatore("corriere")).ToString() & ", macchine trainate: " & CInt(p.Contatore("carro")).ToString() & ", guadagni del commercio: " & CInt(p.Contatore("commercio")).ToString() & " euro, veicoli venduti al ricettatore: " & CInt(p.Contatore("ricettati")).ToString(),
                "Passaggi dati: " & CInt(p.Contatore("passaggi")).ToString() & ", scippatori fermati: " & CInt(p.Contatore("scippatori")).ToString() & ", agguati respinti: " & CInt(p.Contatore("agguati")).ToString(),
                "Polizia: arrestato " & p.Arresti.ToString() & " volte, seminata " & CInt(p.Contatore("seminato")).ToString() & " volte, al massimo " & p.MassimoStelle.ToString() & " stelle",
                "Oggetti nascosti trovati: " & p.Nascosti.Count.ToString() & " su " & _m.Dati.Nascosti.Count.ToString(),
                "Sfide vinte: " & _m.Dati.Sfide.Where(Function(s) p.Record.ContainsKey(s.Id) AndAlso p.Record(s.Id) <= s.Tempo).Count().ToString() & " su " & _m.Dati.Sfide.Count.ToString(),
                "Trofei: " & p.Trofei.Count.ToString() & " su " & _m.Dati.Trofei.Count.ToString(),
                "Veicoli tuoi: " & p.Posseduti.Count.ToString() & ", proprietà: " & p.Proprieta.Count.ToString()}
            ' chi ti stima e chi no
            Dim stime As List(Of String) = _m.Dati.Personaggi.Values.Where(Function(x) p.StimaDi(x.Id) <> 0).OrderByDescending(Function(x) p.StimaDi(x.Id)).
                Select(Function(x) x.Nome & " " & DescriviStima(p.StimaDi(x.Id))).ToList()
            If stime.Count > 0 Then r.Add("Stima: " & String.Join(", ", stime))
            Dim bande As List(Of String) = _m.Dati.Quartieri.Where(Function(q) _m.Dati.Vita.ContainsKey(q.Id) AndAlso p.RispettoDi(q.Id) <> 0).
                Select(Function(q) _m.Dati.Vita(q.Id).NomeBanda & ", " & q.Nome & ": " & DescriviRispetto(p.RispettoDi(q.Id))).ToList()
            If bande.Count > 0 Then r.Add("Bande: " & String.Join("; ", bande))
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.Statistiche", ex)
            Throw
        End Try
    End Function

    Public Shared Function DescriviStima(v As Integer) As String
        Try
            If v >= 60 Then Return "ti considera di famiglia"
            If v >= 25 Then Return "si fida di te"
            If v > 0 Then Return "ti stima un po'"
            If v <= -40 Then Return "non ti sopporta"
            Return "è deluso da te"
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.DescriviStima", ex)
            Throw
        End Try
    End Function

    Public Shared Function DescriviRispetto(v As Integer) As String
        Try
            If v >= 40 Then Return "ti rispettano"
            If v > 0 Then Return "ti conoscono"
            If v <= -20 Then Return "ce l'hanno con te"
            Return "ti guardano storto"
        Catch ex As Exception
            RegistroErrori.Registra("ProgressiCitta.DescriviRispetto", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>
''' Le sfide a tempo: si sceglie dal telefono, il navigatore porta alla partenza; arrivati e fermi su un veicolo parte il conto alla
''' rovescia e poi il cronometro fino all'arrivo. Battere il tempo la prima volta paga; il miglior tempo resta salvato.
''' </summary>
Public NotInheritable Class SfideCitta
    Private ReadOnly _m As Mondo
    ''' <summary>La sfida scelta (Nothing se nessuna).</summary>
    Public Scelta As Sfida
    Private _fase As String = ""
    Private _via As Double
    Private _conto As Integer
    Private _avvisi As New HashSet(Of Integer)()

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.New", ex)
            Throw
        End Try
    End Sub

    Public ReadOnly Property InCorso As Boolean
        Get
            Return Scelta IsNot Nothing
        End Get
    End Property

    Public Function Elenco() As List(Of String)
        Try
            Return _m.Dati.Sfide.Select(Function(s)
                                            Dim r As Double = 0
                                            Dim rec As String = If(_m.Partita.Record.TryGetValue(s.Id, r), ", il tuo record " & TestoTempo(r) & If(r <= s.Tempo, ", battuta", ""), "")
                                            Return s.Nome & ": da " & s.Da.Nome & " a " & s.A.Nome & ", tempo da battere " & TestoTempo(s.Tempo) & rec
                                        End Function).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.Elenco", ex)
            Throw
        End Try
    End Function

    Public Shared Function TestoTempo(secondi As Double) As String
        Try
            Dim s As Integer = CInt(Math.Round(secondi))
            If s < 60 Then Return s.ToString() & " secondi"
            Return (s \ 60).ToString() & If(s \ 60 = 1, " minuto", " minuti") & If(s Mod 60 > 0, " e " & (s Mod 60).ToString() & " secondi", "")
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.TestoTempo", ex)
            Throw
        End Try
    End Function

    Public Sub Scegli(s As Sfida)
        Try
            Scelta = s
            _fase = "partenza"
            _m.ImpostaMeta(s.Da, True)
            _m.Dici(s.Nome & ". Vai alla partenza, " & s.Da.Nome & ", con un veicolo: lì parte il cronometro.", True)
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.Scegli", ex)
            Throw
        End Try
    End Sub

    Public Sub Annulla(Optional motivo As String = "")
        Try
            If Scelta Is Nothing Then Return
            Scelta = Nothing
            _fase = ""
            _m.ImpostaMeta(Nothing, True)
            If motivo <> "" Then _m.Dici(motivo & " La sfida è annullata.", True)
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.Annulla", ex)
            Throw
        End Try
    End Sub

    Public Function MetaDellaSfida(l As Luogo) As Boolean
        Try
            Return Scelta IsNot Nothing AndAlso (l Is Scelta.Da OrElse l Is Scelta.A)
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.MetaDellaSfida", ex)
            Throw
        End Try
    End Function

    Public Sub Passo(dt As Double)
        Try
            Dim s As Sfida = Scelta
            If s Is Nothing Then Return
            Select Case _fase
                Case "partenza"
                    If _m.DistanzaDa(s.Da.X, s.Da.Y) < Mondo.DISTANZA_LUOGO AndAlso Not _m.APiedi AndAlso Math.Abs(_m.Velocita) < 1 Then
                        _fase = "conto"
                        _conto = 3
                        _via = _m.Tempo + 2.5
                        _m.Dici(s.Presentazione & " Si va a " & s.A.Nome & ". Tempo da battere: " & TestoTempo(s.Tempo) & ".", True)
                    End If
                Case "conto"
                    If _m.Tempo >= _via Then
                        If _conto > 0 Then
                            _m.Suona(MotoreAudio.Via(False), 0.7F)
                            _m.Dici(If(_conto = 3, "Tre", If(_conto = 2, "Due", "Uno")), True)
                            _conto -= 1
                            _via = _m.Tempo + 1
                        Else
                            _m.Suona(MotoreAudio.Via(True), 0.8F)
                            _m.Dici("Via!", True)
                            _fase = "corsa"
                            _via = _m.Tempo
                            _avvisi.Clear()
                            _m.ImpostaMeta(s.A, True)
                        End If
                    End If
                Case "corsa"
                    If _m.APiedi Then
                        Annulla("Sei sceso dal veicolo.")
                        Return
                    End If
                    Dim t As Double = _m.Tempo - _via
                    Dim manca As Integer = CInt(Math.Ceiling(s.Tempo - t))
                    For Each soglia As Integer In {30, 10}
                        If manca = soglia AndAlso _avvisi.Add(soglia) Then _m.Dici("Per il record mancano " & soglia.ToString() & " secondi.")
                    Next
                    If _m.DistanzaDa(s.A.X, s.A.Y) < Mondo.DISTANZA_LUOGO Then Arrivo(s, t)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.Passo", ex)
            Throw
        End Try
    End Sub

    Private Sub Arrivo(s As Sfida, t As Double)
        Try
            Scelta = Nothing
            _fase = ""
            _m.ImpostaMeta(Nothing, True)
            Dim prima As Double = 0
            Dim avevaRecord As Boolean = _m.Partita.Record.TryGetValue(s.Id, prima)
            Dim battutaPrima As Boolean = avevaRecord AndAlso prima <= s.Tempo
            Dim testo As String = "Arrivato in " & TestoTempo(t) & "."
            If Not avevaRecord OrElse t < prima Then
                _m.Partita.Record(s.Id) = t
                If avevaRecord Then testo &= " Nuovo record! Prima era " & TestoTempo(prima) & "."
            Else
                testo &= " Il tuo record resta " & TestoTempo(prima) & "."
            End If
            If t <= s.Tempo Then
                If Not battutaPrima Then
                    Dim premio As Integer = 800 + 40 * s.Tempo \ 10
                    _m.Partita.Guadagna(premio)
                    _m.Notifica("sfida")
                    testo &= " Hai battuto il tempo: vinci " & premio.ToString() & " euro."
                Else
                    testo &= " Tempo battuto di nuovo."
                End If
                _m.Suona(MotoreAudio.Successo(), 0.8F)
            Else
                testo &= " Non basta: il tempo da battere è " & TestoTempo(s.Tempo) & "."
                _m.Suona(MotoreAudio.Fallimento(), 0.6F)
            End If
            _m.Dici(testo, True)
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.Arrivo", ex)
            Throw
        End Try
    End Sub

    Public Function Descrizione() As String
        Try
            Dim s As Sfida = Scelta
            If s Is Nothing Then Return ""
            Select Case _fase
                Case "partenza" : Return "Sfida " & s.Nome & ": vai alla partenza, " & s.Da.Nome & ", a " & Mappa.Metri(_m.DistanzaDa(s.Da.X, s.Da.Y)) & "."
                Case "corsa" : Return "Sfida " & s.Nome & ": " & TestoTempo(_m.Tempo - _via) & " su " & TestoTempo(s.Tempo) & ", arrivo a " & s.A.Nome & ", a " & Mappa.Metri(_m.DistanzaDa(s.A.X, s.A.Y)) & "."
                Case Else : Return "Sfida " & s.Nome & ": si parte."
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("SfideCitta.Descrizione", ex)
            Throw
        End Try
    End Function
End Class
