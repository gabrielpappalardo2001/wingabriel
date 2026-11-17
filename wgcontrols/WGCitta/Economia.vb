Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

' ============================================================================================
' L'economia di Porto Franco (nomi e battute scritti da Gemini, in testi.json, sezione economia):
'  - il commercio: ai mercati, ai magazzini e al porto si compra la merce e la si rivende in un altro quartiere, dove costa di più. Ogni
'    merce ha un prezzo diverso in ogni quartiere, che cambia un po' ogni giorno. La merce sta sul veicolo: più è grande, più ne porta.
'    Qualche merce è di contrabbando: rende di più, ma se ti arrestano te la sequestrano e la multa è più alta;
'  - la benzina: i veicoli consumano; si fa il pieno ai distributori (dove si compra anche una tanica di scorta). A secco ci si ferma;
'  - la banca: i soldi versati non te li toglie nessuno (le multe si pagano con quelli che hai in tasca) e ogni giorno maturano interessi;
'  - il ricettatore: nei garage si vendono i veicoli rubati;
'  - due lavori: il corriere (con furgone o camion, partendo da un magazzino: cinque pacchi da consegnare a tempo) e il soccorso stradale
'    (con il fuoristrada, partendo da un'officina: si va a prendere una macchina in panne e la si traina in officina).
' ============================================================================================

Public NotInheritable Class MerceCommercio
    Public Id As String = ""
    ''' <summary>Al plurale, senza articolo: casse di alici.</summary>
    Public Nome As String = ""
    ''' <summary>Al singolare con l'articolo: una cassa di alici.</summary>
    Public Unita As String = ""
    Public Prezzo As Integer
    Public Contrabbando As Boolean
    Public Descrizione As String = ""
End Class

Public NotInheritable Class EconomiaCitta
    Private ReadOnly _m As Mondo
    Public ReadOnly Merci As New List(Of MerceCommercio)()
    ''' <summary>Il lavoro in corso: "", corriere, carro.</summary>
    Public Lavoro As String = ""
    Private _fase As String = ""
    Private _meta As Luogo
    Private ReadOnly _tappe As New List(Of Luogo)()
    Private _scadenza As Double
    Private _guadagno As Integer
    Private _testi As JObject
    Private _inPanne As Veicolo
    Private _officina As Luogo
    Private _avvisoBenzina As Integer
    Private ReadOnly _avvisi As New HashSet(Of Integer)()

    ''' <summary>Quanti metri si fanno con un pieno.</summary>
    Private Const AUTONOMIA As Double = 11000
    Public Const PREZZO_BENZINA As Double = 1.6

    Public Sub New(m As Mondo)
        Try
            _m = m
            Dim e As JObject = TryCast(m.Dati.Testi("economia"), JObject)
            If e Is Nothing Then Return
            For Each j As JToken In If(TryCast(e("merci"), JArray), New JArray())
                Dim prezzo As Integer
                Integer.TryParse(If(j("prezzo"), "0").ToString().Split("."c)(0), prezzo)
                Dim c As JToken = j("contrabbando")
                Merci.Add(New MerceCommercio With {.Id = If(j("id"), "").ToString(), .Nome = If(j("nome"), "").ToString(), .Unita = If(j("unita"), "").ToString(),
                                                   .Prezzo = Math.Max(10, prezzo), .Contrabbando = c IsNot Nothing AndAlso c.Type = JTokenType.Boolean AndAlso CBool(c),
                                                   .Descrizione = If(j("descrizione"), "").ToString()})
            Next
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.New", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ il commercio
    ''' <summary>Un numero tra 0 e 1 che dipende solo dal testo (sempre uguale, anche tra una partita e l'altra).</summary>
    Private Shared Function Sorte(testo As String) As Double
        Try
            Dim h As ULong = 2166136261UL
            For Each ch As Char In testo
                h = ((h Xor CULng(AscW(ch) And &HFFFF)) * 16777619UL) And &HFFFFFFFFUL
            Next
            Return (h Mod 10007UL) / 10007.0
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Sorte", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il prezzo di una unità di merce in un quartiere, oggi.</summary>
    Public Function Prezzo(merce As MerceCommercio, q As Quartiere) As Integer
        Try
            Dim fattore As Double = 0.55 + 1.05 * Sorte(merce.Id & "|" & q.Id)
            Dim giorno As Double = 0.9 + 0.2 * Sorte(merce.Id & "|" & q.Id & "|" & _m.Partita.Giorno.ToString())
            Return Math.Max(1, CInt(Math.Round(merce.Prezzo * fattore * giorno)))
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Prezzo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Vero se in questo luogo si commercia.</summary>
    Public Shared Function SiCommercia(l As Luogo) As Boolean
        Try
            Return l IsNot Nothing AndAlso (l.Tipo = "mercato" OrElse l.Tipo = "magazzino" OrElse l.Tipo = "porto")
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.SiCommercia", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le merci che questo luogo vende (sei, sempre le stesse per quel luogo; il contrabbando solo nei magazzini e al porto).</summary>
    Public Function InVendita(l As Luogo) As List(Of MerceCommercio)
        Try
            Return Merci.Where(Function(x) l.Tipo <> "mercato" OrElse Not x.Contrabbando).OrderBy(Function(x) Sorte(x.Id & "@" & l.Id)).Take(6).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.InVendita", ex)
            Throw
        End Try
    End Function

    ''' <summary>Quanto ti danno per una unità (un po' meno del prezzo del quartiere; quello che il luogo vende lo paga poco).</summary>
    Public Function PrezzoVendita(merce As MerceCommercio, l As Luogo) As Integer
        Try
            Dim p As Double = Prezzo(merce, l.Quartiere) * 0.93
            If InVendita(l).Contains(merce) Then p *= 0.85
            Return Math.Max(1, CInt(Math.Floor(p)))
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.PrezzoVendita", ex)
            Throw
        End Try
    End Function

    ''' <summary>Quante unità porta un veicolo.</summary>
    Public Shared Function Capienza(t As TipoVeicolo) As Integer
        Try
            Select Case t.Id
                Case "moto", "scooter" : Return 1
                Case "sportiva" : Return 2
                Case "utilitaria" : Return 3
                Case "berlina", "taxi" : Return 4
                Case "fuoristrada" : Return 7
                Case "furgone", "ambulanza" : Return 14
                Case "camion" : Return 32
                Case Else : Return 3
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Capienza", ex)
            Throw
        End Try
    End Function

    Public Shared Function Caricate(v As Veicolo) As Integer
        Try
            Return If(v Is Nothing, 0, v.Carico.Values.Sum())
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Caricate", ex)
            Throw
        End Try
    End Function

    ''' <summary>Compra quante unità di una merce (per il veicolo su cui si è). Restituisce cosa dire.</summary>
    Public Function Compra(merce As MerceCommercio, l As Luogo, quante As Integer) As String
        Try
            Dim v As Veicolo = _m.Veicolo
            If v Is Nothing Then Return "La merce la carichi sul veicolo: vieni qui in macchina."
            Dim costo As Integer = Prezzo(merce, l.Quartiere)
            quante = Math.Min(quante, Capienza(v.Tipo) - Caricate(v))
            quante = Math.Min(quante, _m.Partita.Soldi \ costo)
            If quante <= 0 Then Return If(Capienza(v.Tipo) - Caricate(v) <= 0, "Sul veicolo non c'è più posto.", "Non hai abbastanza soldi.")
            _m.Partita.Soldi -= quante * costo
            Dim n As Integer
            v.Carico.TryGetValue(merce.Id, n)
            v.Carico(merce.Id) = n + quante
            ' quanto l'hai pagata in media, per sapere poi il guadagno
            Dim spesa As Double = _m.Partita.Contatore("spesa_" & merce.Id)
            _m.Partita.Contatori("spesa_" & merce.Id) = spesa + quante * costo
            _m.Suona(MotoreAudio.Soldi(), 0.6F)
            Return _m.Dati.TestoACaso("economia", "mercante_compra", _m.Caso()) & " Carichi " & quante.ToString() & " " & merce.Nome & " per " & (quante * costo).ToString() & " euro. Posto rimasto: " & (Capienza(v.Tipo) - Caricate(v)).ToString() & "."
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Compra", ex)
            Throw
        End Try
    End Function

    ''' <summary>Vende tutta la merce di un tipo che c'è sul veicolo. Restituisce cosa dire.</summary>
    Public Function Vendi(merce As MerceCommercio, l As Luogo) As String
        Try
            Dim v As Veicolo = _m.Veicolo
            Dim n As Integer
            If v Is Nothing OrElse Not v.Carico.TryGetValue(merce.Id, n) OrElse n <= 0 Then Return "Non ne hai."
            Dim incasso As Integer = n * PrezzoVendita(merce, l)
            Dim spesa As Double = _m.Partita.Contatore("spesa_" & merce.Id)
            v.Carico.Remove(merce.Id)
            _m.Partita.Contatori.Remove("spesa_" & merce.Id)
            _m.Partita.Guadagna(incasso)
            Dim guadagno As Integer = CInt(incasso - spesa)
            If guadagno > 0 Then _m.Partita.Conta("commercio", guadagno)
            If merce.Contrabbando Then _m.Partita.Conta("contrabbando", n)
            _m.Partita.CambiaRispetto(l.Quartiere.Id, 1)
            _m.Suona(MotoreAudio.Soldi(), 0.7F)
            If incasso >= 2000 Then _m.Notifica("commercio")
            Return _m.Dati.TestoACaso("economia", "mercante_vende", _m.Caso()) & " Vendi " & n.ToString() & " " & merce.Nome & " per " & incasso.ToString() & " euro" &
                   If(spesa > 0, If(guadagno >= 0, ": ci guadagni " & guadagno.ToString() & ".", ": ci rimetti " & (-guadagno).ToString() & "."), ".")
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Vendi", ex)
            Throw
        End Try
    End Function

    ''' <summary>Dove una merce vale di più oggi (per chiedere in giro prima di partire).</summary>
    Public Function DoveConviene(merce As MerceCommercio) As String
        Try
            Dim ordinati As List(Of Quartiere) = _m.Dati.Quartieri.OrderByDescending(Function(q) Prezzo(merce, q)).ToList()
            Dim caro As Quartiere = ordinati.First()
            Dim economico As Quartiere = ordinati.Last()
            Return Char.ToUpper(merce.Nome(0)) & merce.Nome.Substring(1) & ": si pagano di più a " & caro.Nome & ", " & Prezzo(merce, caro).ToString() & " euro; di meno a " & economico.Nome & ", " & Prezzo(merce, economico).ToString() & "."
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.DoveConviene", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il carico del veicolo, a voce.</summary>
    Public Function DescriviCarico(v As Veicolo) As String
        Try
            If v Is Nothing Then Return "Sei a piedi."
            If Caricate(v) = 0 Then Return "Il veicolo è vuoto: ci stanno " & Capienza(v.Tipo).ToString() & " unità di merce."
            Dim pezzi As New List(Of String)()
            For Each kv As KeyValuePair(Of String, Integer) In v.Carico
                Dim merce As MerceCommercio = Merci.FirstOrDefault(Function(x) x.Id = kv.Key)
                If merce IsNot Nothing Then pezzi.Add(kv.Value.ToString() & " " & merce.Nome & If(merce.Contrabbando, " di contrabbando", ""))
            Next
            Return "A bordo hai " & String.Join(", ", pezzi) & ". Posto rimasto: " & (Capienza(v.Tipo) - Caricate(v)).ToString() & "."
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.DescriviCarico", ex)
            Throw
        End Try
    End Function

    ''' <summary>Dopo un arresto: la merce di contrabbando sul veicolo è sequestrata. Restituisce cosa aggiungere all'annuncio.</summary>
    Public Function Sequestro(v As Veicolo) As String
        Try
            If v Is Nothing Then Return ""
            Dim tolte As Integer = 0
            For Each id As String In v.Carico.Keys.ToList()
                Dim merce As MerceCommercio = Merci.FirstOrDefault(Function(x) x.Id = id)
                If merce IsNot Nothing AndAlso merce.Contrabbando Then
                    tolte += v.Carico(id)
                    v.Carico.Remove(id)
                    _m.Partita.Contatori.Remove("spesa_" & id)
                End If
            Next
            If tolte = 0 Then Return ""
            Dim multa As Integer = Math.Min(_m.Partita.Soldi, tolte * 40)
            _m.Partita.Soldi -= multa
            Return " Ti sequestrano la merce di contrabbando e paghi altri " & multa.ToString() & " euro."
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Sequestro", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ la benzina
    ''' <summary>Il consumo, a ogni passo di guida: restituisce falso se il serbatoio è vuoto (il motore non tira).</summary>
    Public Function Consuma(v As Veicolo, metri As Double) As Boolean
        Try
            If _m.Scuola Then Return True
            If v.Benzina <= 0 Then Return False
            Dim consumo As Double = If(v.Tipo.Id = "sportiva" OrElse v.Tipo.Id = "camion", 1.4, If(v.Tipo.DueRuote, 0.6, 1.0))
            v.Benzina = Math.Max(0, v.Benzina - Math.Abs(metri) / AUTONOMIA * 100 * consumo)
            Dim soglia As Integer = If(v.Benzina <= 0, 0, If(v.Benzina < 8, 8, If(v.Benzina < 20, 20, 100)))
            If soglia < _avvisoBenzina Then
                Select Case soglia
                    Case 20 : _m.Dici("Si accende la spia della riserva: ti resta poca benzina.")
                    Case 8 : _m.Dici("La benzina sta finendo: cerca un distributore.", True)
                    Case 0
                        _m.Suona(MotoreAudio.Fallimento(), 0.5F)
                        _m.Dici("Sei rimasto a secco: il motore si spegne. Ti serve una tanica, o un altro veicolo.", True)
                        _m.Partita.Conta("a_secco")
                        _m.Notifica("benzina")
                End Select
            End If
            _avvisoBenzina = soglia
            Return v.Benzina > 0
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Consuma", ex)
            Throw
        End Try
    End Function

    ''' <summary>Salendo su un veicolo: l'avviso della benzina riparte dal suo livello.</summary>
    Public Sub Salito(v As Veicolo)
        Try
            _avvisoBenzina = If(v.Benzina <= 0, 0, If(v.Benzina < 8, 8, If(v.Benzina < 20, 20, 100)))
            If v.Benzina < 20 Then _m.Dici(If(v.Benzina <= 0, "Il serbatoio è vuoto.", "È in riserva."))
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Salito", ex)
            Throw
        End Try
    End Sub

    Public Function CostoPieno(v As Veicolo) As Integer
        Try
            Return CInt(Math.Ceiling((100 - v.Benzina) * PREZZO_BENZINA))
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.CostoPieno", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il pieno (o quello che ci si può permettere). Restituisce cosa dire.</summary>
    Public Function Pieno(v As Veicolo) As String
        Try
            Dim costo As Integer = CostoPieno(v)
            If costo <= 0 Then Return "Il serbatoio è già pieno."
            If _m.Partita.Soldi <= 0 Then Return "Non hai un euro in tasca."
            Dim pago As Integer = Math.Min(costo, _m.Partita.Soldi)
            _m.Partita.Soldi -= pago
            v.Benzina = Math.Min(100, v.Benzina + pago / PREZZO_BENZINA)
            _avvisoBenzina = 100
            _m.Suona(MotoreAudio.Soldi(), 0.5F)
            Return _m.Dati.TestoACaso("economia", "benzinaio", _m.Caso()) & If(pago < costo, " Con " & pago.ToString() & " euro il serbatoio arriva a " & CInt(v.Benzina).ToString() & " su cento.", " Pieno fatto, " & pago.ToString() & " euro.")
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Pieno", ex)
            Throw
        End Try
    End Function

    ''' <summary>Versa la tanica nel veicolo su cui si è. Restituisce cosa dire.</summary>
    Public Function UsaTanica() As String
        Try
            If _m.Veicolo Is Nothing Then Return "La tanica la versi nel veicolo su cui sei."
            If _m.Partita.QuantiOggetti("tanica") <= 0 Then Return "Non hai taniche."
            _m.Partita.CambiaOggetti("tanica", -1)
            _m.Veicolo.Benzina = Math.Min(100, _m.Veicolo.Benzina + 30)
            _avvisoBenzina = 100
            Return "Versi la tanica nel serbatoio: adesso è a " & CInt(_m.Veicolo.Benzina).ToString() & " su cento."
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.UsaTanica", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ il ricettatore
    ''' <summary>Quanto dà il ricettatore per il veicolo (0: non lo vuole).</summary>
    Public Function ValoreRubato(v As Veicolo) As Integer
        Try
            If v Is Nothing OrElse v.Tuo OrElse v.Tipo.Id = "polizia" OrElse v.Tipo.Id = "ambulanza" Then Return 0
            Dim listino As Integer
            If Not _m.Dati.PrezziVeicoli.TryGetValue(v.Tipo.Id, listino) Then listino = 5000
            Dim stato As Double = Math.Max(0.15, Math.Max(0, v.Salute) / v.Tipo.Robustezza)
            Return CInt(Math.Round(listino * 0.22 * stato / 10)) * 10
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.ValoreRubato", ex)
            Throw
        End Try
    End Function

    Public Function VendiRubato() As String
        Try
            Dim v As Veicolo = _m.Veicolo
            Dim valore As Integer = ValoreRubato(v)
            If valore <= 0 Then Return "Questo non lo vuole nessuno."
            If Caricate(v) > 0 Then Return "Prima scarica la merce: qui comprano solo il veicolo."
            _m.Scendi()
            _m.Veicoli.Remove(v)
            _m.Partita.Guadagna(valore)
            _m.Partita.Conta("ricettati")
            _m.Partita.CambiaRispetto(_m.Dati.QuartiereDi(_m.X, _m.Y).Id, 2)
            _m.Suona(MotoreAudio.Soldi(), 0.7F)
            Return _m.Dati.TestoACaso("economia", "ricettatore", _m.Caso()) & " Ti dà " & valore.ToString() & " euro e " & If(v.Tipo.Maschile, "il ", "la ") & v.Tipo.Nome & " sparisce nel retro."
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.VendiRubato", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ i lavori: corriere e soccorso stradale
    Public ReadOnly Property LavoroAttivo As Boolean
        Get
            Return Lavoro <> ""
        End Get
    End Property

    ''' <summary>Se qui e con questo veicolo si può fare il corriere o il soccorso stradale, lo comincia e restituisce vero.</summary>
    Public Function Comincia(qui As Luogo) As Boolean
        Try
            If qui Is Nothing OrElse _m.Veicolo Is Nothing Then Return False
            Dim tipo As String = _m.Veicolo.Tipo.Id
            If qui.Tipo = "magazzino" AndAlso (tipo = "furgone" OrElse tipo = "camion") Then
                Lavoro = "corriere"
                _guadagno = 0
                _tappe.Clear()
                Dim candidati As List(Of Luogo) = _m.Dati.Luoghi.Values.Where(Function(l) l IsNot qui AndAlso l.Tipo <> "casa" AndAlso l.Tipo <> "magazzino" AndAlso _m.DistanzaDa(l.X, l.Y) > 250).
                    OrderBy(Function(l) _m.Caso().Next()).Take(5).OrderBy(Function(l) _m.DistanzaDa(l.X, l.Y)).ToList()
                _tappe.AddRange(candidati)
                _m.Dici("Carichi cinque pacchi sul " & _m.Veicolo.Tipo.Nome & ": vanno consegnati uno dopo l'altro, prima che scada il tempo.", True)
                ProssimoPacco()
                Return True
            End If
            If qui.Tipo = "officina" AndAlso tipo = "fuoristrada" Then
                Lavoro = "carro"
                _guadagno = 0
                _officina = qui
                NuovoSoccorso()
                Return True
            End If
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Comincia", ex)
            Throw
        End Try
    End Function

    Private Sub ProssimoPacco()
        Try
            If _tappe.Count = 0 Then
                _m.Partita.Guadagna(100)
                _guadagno += 100
                Finisci("Giro finito: per tutti e cinque i pacchi c'è un premio di 100 euro.")
                Return
            End If
            _meta = _tappe(0)
            _tappe.RemoveAt(0)
            _testi = _m.Dati.OggettoACaso("economia", "corriere", _m.Caso())
            Dim strada As Double = Mappa.Lunghezza(_m.Dati, _m.Pos, _meta.Tratto, _meta.Distanza)
            Dim tempo As Integer = CInt(Math.Ceiling((strada / 7.0 + 35) / 10)) * 10
            _scadenza = _m.Tempo + tempo
            _avvisi.Clear()
            _m.ImpostaMeta(_meta, True)
            _m.Dici("Pacco per " & _meta.Nome & ", " & _meta.Quartiere.Nome & ". Hai " & SfideCitta.TestoTempo(tempo) & ".", True)
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.ProssimoPacco", ex)
            Throw
        End Try
    End Sub

    Private Sub NuovoSoccorso()
        Try
            Dim t As Tratto = _m.TrattoACaso(_m.Pos, 350, 1100)
            Dim tipi As String() = {"utilitaria", "berlina", "sportiva", "taxi"}
            _inPanne = _m.CreaVeicoloSu(tipi(_m.Caso().Next(tipi.Length)), t, t.Lunghezza * (0.25 + 0.5 * _m.Caso().NextDouble()), RuoloVeicolo.Parcheggiato)
            _inPanne.Benzina = 0
            _testi = _m.Dati.OggettoACaso("economia", "carro", _m.Caso())
            _fase = "andata"
            _meta = New Luogo With {.Id = "panne", .Tipo = "panne", .Nome = "la macchina in panne", .Tratto = t, .Distanza = _inPanne.Pos.Distanza, .Quartiere = _m.Dati.QuartiereDi(_inPanne.X, _inPanne.Y), .Lato = "destra", .X = _inPanne.X, .Y = _inPanne.Y}
            _m.ImpostaMeta(_meta, True)
            _m.Dici("Chiamata per il soccorso stradale: " & _inPanne.Tipo.ConUn() & " in panne in " & t.Strada.Nome & ", " & _meta.Quartiere.Nome & ". Il navigatore ti porta lì.", True)
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.NuovoSoccorso", ex)
            Throw
        End Try
    End Sub

    Private Function Battuta(chiave As String) As String
        Try
            If _testi Is Nothing OrElse _testi(chiave) Is Nothing Then Return ""
            Return _testi(chiave).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Battuta", ex)
            Throw
        End Try
    End Function

    ''' <summary>Smette di lavorare mettendo da parte il giro di consegne (il soccorso stradale no).</summary>
    Public Sub Sospendi(testo As String)
        Try
            Dim j As JObject = Fotografa()
            Dim salvato As Boolean = j("lavoro") IsNot Nothing
            If salvato Then _m.Partita.LavoroSospeso = New JObject From {{"economia", j}}
            Finisci(testo & If(salvato, " Lo riprendi da dove l'hai lasciato.", ""))
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Sospendi", ex)
            Throw
        End Try
    End Sub

    Public Sub Finisci(testo As String)
        Try
            If Lavoro = "" Then Return
            If _inPanne IsNot Nothing AndAlso _fase = "andata" Then _m.TogliVeicolo(_inPanne)
            _inPanne = Nothing
            Lavoro = ""
            _fase = ""
            _meta = Nothing
            _tappe.Clear()
            _m.ImpostaMeta(Nothing, True)
            _m.Dici(testo & If(_guadagno > 0, " In tutto hai guadagnato " & _guadagno.ToString() & " euro.", ""), True)
            _guadagno = 0
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Finisci", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Con una macchina a rimorchio si va più piano.</summary>
    Public ReadOnly Property FattoreVelocita As Double
        Get
            Return If(Lavoro = "carro" AndAlso _fase = "ritorno", 0.6, 1.0)
        End Get
    End Property

    ''' <summary>Il giro di consegne in corso, per il salvataggio (il soccorso stradale ricomincia da capo).</summary>
    Public Function Fotografa() As JObject
        Try
            Dim j As New JObject()
            If Lavoro <> "corriere" OrElse _meta Is Nothing OrElse Not _m.Dati.Luoghi.ContainsKey(_meta.Id) Then Return j
            j("lavoro") = Lavoro
            j("meta") = _meta.Id
            j("scadenza") = Math.Max(30, Math.Round(_scadenza - _m.Tempo))
            j("guadagno") = _guadagno
            j("tappe") = New JArray(_tappe.Where(Function(l) _m.Dati.Luoghi.ContainsKey(l.Id)).Select(Function(l) l.Id).ToArray())
            Return j
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Fotografa", ex)
            Throw
        End Try
    End Function

    Public Sub Ripristina(j As JObject)
        Try
            If j Is Nothing OrElse StatoPartita.Testo(j, "lavoro") <> "corriere" Then Return
            Dim dest As Luogo = Nothing
            If Not _m.Dati.Luoghi.TryGetValue(StatoPartita.Testo(j, "meta"), dest) Then Return
            Lavoro = "corriere"
            _fase = ""
            _meta = dest
            _scadenza = _m.Tempo + StatoPartita.Numero(j, "scadenza")
            _guadagno = CInt(StatoPartita.Numero(j, "guadagno"))
            _avvisi.Clear()
            _tappe.Clear()
            For Each t As JToken In If(TryCast(j("tappe"), JArray), New JArray())
                Dim l As Luogo = Nothing
                If _m.Dati.Luoghi.TryGetValue(t.ToString(), l) Then _tappe.Add(l)
            Next
            _testi = _m.Dati.OggettoACaso("economia", "corriere", _m.Caso())
            _m.ImpostaMeta(_meta, True)
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Ripristina", ex)
            Throw
        End Try
    End Sub

    Public Sub Riprendi()
        Try
            If Lavoro <> "" Then _m.Dici("Riprendi il lavoro. " & Descrizione(), True)
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Riprendi", ex)
            Throw
        End Try
    End Sub

    Public Function MetaDelLavoro(l As Luogo) As Boolean
        Try
            Return Lavoro <> "" AndAlso l IsNot Nothing AndAlso l Is _meta
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.MetaDelLavoro", ex)
            Throw
        End Try
    End Function

    Public ReadOnly Property MetaAttuale As Luogo
        Get
            Return If(Lavoro = "", Nothing, _meta)
        End Get
    End Property

    Public Sub Passo(dt As Double)
        Try
            If Lavoro = "" Then Return
            If _m.APiedi Then Return
            If _meta Is Nothing Then Return
            Dim arrivato As Boolean = _m.DistanzaDa(_meta.X, _meta.Y) < Mondo.DISTANZA_LUOGO AndAlso Math.Abs(_m.Velocita) < 3
            If Lavoro = "corriere" Then
                Dim manca As Integer = CInt(Math.Ceiling(_scadenza - _m.Tempo))
                If manca = 20 AndAlso _avvisi.Add(20) Then _m.Dici("Venti secondi per la consegna.")
                If Not arrivato Then Return
                Dim inTempo As Boolean = manca > 0
                Dim paga As Integer = If(inTempo, 70 + Math.Min(60, manca), 25)
                _m.Partita.Guadagna(paga)
                _m.Partita.Conta("corriere")
                _m.Partita.LavoriFatti += 1
                _guadagno += paga
                _m.Suona(MotoreAudio.Soldi(), 0.6F)
                _m.Dici(Battuta(If(inTempo, "consegna", "ritardo")) & " " & paga.ToString() & " euro.", True)
                ProssimoPacco()
            ElseIf Lavoro = "carro" Then
                If Not arrivato Then Return
                If _fase = "andata" Then
                    _fase = "ritorno"
                    _m.TogliVeicolo(_inPanne)
                    _m.Suona(MotoreAudio.Registrato("cancello"), 0.5F)
                    _meta = _officina
                    _m.ImpostaMeta(_meta, True)
                    _m.Dici(Battuta("richiesta") & " Agganci la macchina: portala a " & _officina.Nome & ". Con il rimorchio vai più piano, e attento alle curve.", True)
                Else
                    Dim paga As Integer = 180 + _m.Caso().Next(120)
                    _m.Partita.Guadagna(paga)
                    _m.Partita.Conta("carro")
                    _m.Partita.LavoriFatti += 1
                    _guadagno += paga
                    _inPanne = Nothing
                    _m.Suona(MotoreAudio.Soldi(), 0.6F)
                    _m.Dici(Battuta("grazie") & " " & paga.ToString() & " euro.", True)
                    NuovoSoccorso()
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Passo", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Per lo schermo e il tasto della missione ("" se non si lavora).</summary>
    Public Function Descrizione() As String
        Try
            If Lavoro = "" OrElse _meta Is Nothing Then Return ""
            If Lavoro = "corriere" Then Return "Corriere: pacco per " & _meta.Nome & ", a " & Mappa.Metri(_m.DistanzaDa(_meta.X, _meta.Y)) & ", restano " & SfideCitta.TestoTempo(Math.Max(0, _scadenza - _m.Tempo)) & ". Pacchi dopo questo: " & _tappe.Count.ToString() & "."
            Return "Soccorso stradale: " & If(_fase = "andata", "vai dalla macchina in panne, a ", "porta la macchina in officina, a ") & Mappa.Metri(_m.DistanzaDa(_meta.X, _meta.Y)) & "."
        Catch ex As Exception
            RegistroErrori.Registra("EconomiaCitta.Descrizione", ex)
            Throw
        End Try
    End Function
End Class
