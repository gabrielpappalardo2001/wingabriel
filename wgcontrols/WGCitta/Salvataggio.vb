Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Newtonsoft.Json.Linq

''' <summary>
''' Una partita salvata di Porto Franco. Ci sono tre posti (partita1.json, partita2.json, partita3.json in dati\citta, la cartella dei dati di
''' WinGabriel, che viaggia con la sincronizzazione Filen come il resto). Si salva da casa o da un rifugio, a fine missione, dal menu di pausa
''' e uscendo dal gioco.
''' </summary>
Public NotInheritable Class Salvataggio
    Public Const POSTI As Integer = 3
    ''' <summary>In quale posto si salva (1-3).</summary>
    Public Posto As Integer = 1
    Public Soldi As Integer = 500
    Public ReadOnly Completate As New HashSet(Of String)()
    Public ReadOnly Posseduti As New List(Of String) From {"utilitaria"}
    ''' <summary>Le migliorie comprate per ogni tipo di veicolo: motore, freni, blindatura.</summary>
    Public ReadOnly Migliorie As New Dictionary(Of String, HashSet(Of String))()
    ''' <summary>I luoghi comprati (attività e rifugi).</summary>
    Public ReadOnly Proprieta As New HashSet(Of String)()
    ''' <summary>Le scelte della trama: id della scelta, valore (a o b).</summary>
    Public ReadOnly Scelte As New Dictionary(Of String, String)()
    ''' <summary>I personaggi che non danno più lavori.</summary>
    Public ReadOnly Bloccati As New HashSet(Of String)()
    ''' <summary>Quanto la polizia ti conosce in ogni quartiere (0-100).</summary>
    Public ReadOnly Notorieta As New Dictionary(Of String, Double)()
    ''' <summary>Minuti dall'inizio della partita (si comincia alle nove del mattino del primo giorno).</summary>
    Public Minuti As Double = 9 * 60
    Public Metri As Double
    Public Arresti As Integer
    Public Fallite As Integer
    Public LavoriFatti As Integer
    Public CorseVinte As Integer
    Public Finale As Boolean
    ''' <summary>Quanto ti stima ogni personaggio (da -100 a 100): sale con le missioni fatte bene, scende con quelle fallite.</summary>
    Public ReadOnly Stima As New Dictionary(Of String, Integer)()
    ''' <summary>Quanto ti rispetta la banda di ogni quartiere (da -100 a 100).</summary>
    Public ReadOnly Rispetto As New Dictionary(Of String, Integer)()
    ''' <summary>I trofei ottenuti (id).</summary>
    Public ReadOnly Trofei As New HashSet(Of String)()
    ''' <summary>Gli oggetti nascosti trovati (id).</summary>
    Public ReadOnly Nascosti As New HashSet(Of String)()
    ''' <summary>Il miglior tempo di ogni sfida, in secondi.</summary>
    Public ReadOnly Record As New Dictionary(Of String, Double)()
    ''' <summary>Le missioni fallite da riprendere a metà: id della missione, "passo|veicolo".</summary>
    Public ReadOnly Ripresa As New Dictionary(Of String, String)()
    ''' <summary>Le cose comprate e non ancora usate: id dell'effetto, quante.</summary>
    Public ReadOnly Oggetti As New Dictionary(Of String, Integer)()
    ''' <summary>I contatori delle statistiche e dei trofei (taxi, ambulanza, pizze, corsa, passaggi, scippatori, seminato4, galleria, metri_notte...).</summary>
    Public ReadOnly Contatori As New Dictionary(Of String, Double)()
    ''' <summary>Fino a quando (minuti della partita) vale il santino: la polizia ti trova più difficilmente.</summary>
    Public SantinoFino As Double
    ''' <summary>I soldi versati in banca: al sicuro dalle multe, con gli interessi ogni giorno.</summary>
    Public Banca As Integer
    ''' <summary>La benzina dei tuoi veicoli (per tipo), da 0 a 100.</summary>
    Public ReadOnly Benzina As New Dictionary(Of String, Double)()
    ''' <summary>La merce rimasta sul veicolo quando si è salvato: id della merce, quante unità.</summary>
    Public ReadOnly Carico As New Dictionary(Of String, Integer)()
    Public SoldiGuadagnati As Double
    Public MassimoStelle As Integer
    ''' <summary>Lo stato del mondo al momento del salvataggio (dove sei, il veicolo, la missione a metà...): vedi StatoPartita.</summary>
    Public Stato As JObject
    ''' <summary>Il lavoro messo da parte (taxi, ambulanza, pizze, corriere), da riprendere esattamente da dove era.</summary>
    Public LavoroSospeso As JObject

    Private Shared Function Percorso(posto As Integer) As String
        Try
            Return Path.Combine(WGDb.WGDatabase.CartellaDati("citta"), "partita" & posto.ToString() & ".json")
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Percorso", ex)
            Throw
        End Try
    End Function

    Public Shared Function Esiste(posto As Integer) As Boolean
        Try
            Return File.Exists(Percorso(posto))
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Esiste", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una riga che descrive la partita salvata in un posto ("" se il posto è vuoto).</summary>
    Public Shared Function Riassunto(posto As Integer, totaleMissioni As Integer) As String
        Try
            If Not Esiste(posto) Then Return ""
            Dim s As Salvataggio = Carica(posto)
            Return "giorno " & s.Giorno.ToString() & ", " & (s.Soldi + s.Banca).ToString() & " euro, missioni " & s.Completate.Count.ToString() & " su " & totaleMissioni.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Riassunto", ex)
            Throw
        End Try
    End Function

    Public ReadOnly Property Giorno As Integer
        Get
            Return CInt(Math.Floor(Minuti / 1440)) + 1
        End Get
    End Property

    ''' <summary>L'ora del giorno, da 0 a 24.</summary>
    Public ReadOnly Property Ora As Double
        Get
            Return (Minuti Mod 1440) / 60
        End Get
    End Property

    Public Function HaMiglioria(tipo As String, miglioria As String) As Boolean
        Try
            Dim m As HashSet(Of String) = Nothing
            Return Migliorie.TryGetValue(tipo, m) AndAlso m.Contains(miglioria)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.HaMiglioria", ex)
            Throw
        End Try
    End Function

    Public Sub AggiungiMiglioria(tipo As String, miglioria As String)
        Try
            If Not Migliorie.ContainsKey(tipo) Then Migliorie(tipo) = New HashSet(Of String)()
            Migliorie(tipo).Add(miglioria)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.AggiungiMiglioria", ex)
            Throw
        End Try
    End Sub

    Public Sub Conta(nome As String, Optional quanto As Double = 1)
        Try
            If String.IsNullOrEmpty(nome) Then Return
            Dim v As Double
            Contatori.TryGetValue(nome, v)
            Contatori(nome) = v + quanto
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Conta", ex)
            Throw
        End Try
    End Sub

    Public Function Contatore(nome As String) As Double
        Try
            Dim v As Double
            Return If(Contatori.TryGetValue(nome, v), v, 0)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Contatore", ex)
            Throw
        End Try
    End Function

    ''' <summary>Soldi guadagnati (si contano anche per le statistiche).</summary>
    Public Sub Guadagna(quanti As Integer)
        Try
            Soldi += quanti
            If quanti > 0 Then SoldiGuadagnati += quanti
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Guadagna", ex)
            Throw
        End Try
    End Sub

    Public Function BenzinaDi(tipo As String) As Double
        Try
            Dim v As Double
            Return If(Benzina.TryGetValue(tipo, v), v, 100)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.BenzinaDi", ex)
            Throw
        End Try
    End Function

    Public Function StimaDi(personaggio As String) As Integer
        Try
            Dim v As Integer
            Return If(Stima.TryGetValue(personaggio, v), v, 0)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.StimaDi", ex)
            Throw
        End Try
    End Function

    Public Sub CambiaStima(personaggio As String, quanto As Integer)
        Try
            Stima(personaggio) = Math.Max(-100, Math.Min(100, StimaDi(personaggio) + quanto))
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.CambiaStima", ex)
            Throw
        End Try
    End Sub

    Public Function RispettoDi(quartiere As String) As Integer
        Try
            Dim v As Integer
            Return If(Rispetto.TryGetValue(quartiere, v), v, 0)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.RispettoDi", ex)
            Throw
        End Try
    End Function

    Public Sub CambiaRispetto(quartiere As String, quanto As Integer)
        Try
            Rispetto(quartiere) = Math.Max(-100, Math.Min(100, RispettoDi(quartiere) + quanto))
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.CambiaRispetto", ex)
            Throw
        End Try
    End Sub

    Public Function QuantiOggetti(id As String) As Integer
        Try
            Dim v As Integer
            Return If(Oggetti.TryGetValue(id, v), v, 0)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.QuantiOggetti", ex)
            Throw
        End Try
    End Function

    Public Sub CambiaOggetti(id As String, quanti As Integer)
        Try
            Dim n As Integer = QuantiOggetti(id) + quanti
            If n <= 0 Then
                Oggetti.Remove(id)
            Else
                Oggetti(id) = n
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.CambiaOggetti", ex)
            Throw
        End Try
    End Sub

    Public Function NotorietaDi(quartiere As String) As Double
        Try
            Dim v As Double
            Return If(Notorieta.TryGetValue(quartiere, v), v, 0)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.NotorietaDi", ex)
            Throw
        End Try
    End Function

    Public Shared Function Carica(posto As Integer) As Salvataggio
        Try
            Dim s As New Salvataggio With {.Posto = posto}
            If Not Esiste(posto) Then Return s
            Dim j As JObject = JObject.Parse(File.ReadAllText(Percorso(posto), Encoding.UTF8))
            s.Soldi = CInt(j("soldi"))
            For Each m As JToken In If(TryCast(j("completate"), JArray), New JArray())
                s.Completate.Add(m.ToString())
            Next
            Dim posseduti As JArray = TryCast(j("posseduti"), JArray)
            If posseduti IsNot Nothing AndAlso posseduti.Count > 0 Then
                s.Posseduti.Clear()
                For Each v As JToken In posseduti
                    s.Posseduti.Add(v.ToString())
                Next
            End If
            Dim mig As JObject = TryCast(j("migliorie"), JObject)
            If mig IsNot Nothing Then
                For Each p As JProperty In mig.Properties()
                    For Each x As JToken In p.Value
                        s.AggiungiMiglioria(p.Name, x.ToString())
                    Next
                Next
            End If
            For Each x As JToken In If(TryCast(j("proprieta"), JArray), New JArray())
                s.Proprieta.Add(x.ToString())
            Next
            Dim scelte As JObject = TryCast(j("scelte"), JObject)
            If scelte IsNot Nothing Then
                For Each p As JProperty In scelte.Properties()
                    s.Scelte(p.Name) = p.Value.ToString()
                Next
            End If
            For Each x As JToken In If(TryCast(j("bloccati"), JArray), New JArray())
                s.Bloccati.Add(x.ToString())
            Next
            Dim noto As JObject = TryCast(j("notorieta"), JObject)
            If noto IsNot Nothing Then
                For Each p As JProperty In noto.Properties()
                    s.Notorieta(p.Name) = CDbl(p.Value)
                Next
            End If
            If j("minuti") IsNot Nothing Then s.Minuti = CDbl(j("minuti"))
            If j("metri") IsNot Nothing Then s.Metri = CDbl(j("metri"))
            If j("arresti") IsNot Nothing Then s.Arresti = CInt(j("arresti"))
            If j("fallite") IsNot Nothing Then s.Fallite = CInt(j("fallite"))
            If j("lavori") IsNot Nothing Then s.LavoriFatti = CInt(j("lavori"))
            If j("corse") IsNot Nothing Then s.CorseVinte = CInt(j("corse"))
            If j("finale") IsNot Nothing Then s.Finale = CBool(j("finale"))
            LeggiInteri(TryCast(j("stima"), JObject), s.Stima)
            LeggiInteri(TryCast(j("rispetto"), JObject), s.Rispetto)
            LeggiInteri(TryCast(j("oggetti"), JObject), s.Oggetti)
            LeggiInteri(TryCast(j("carico"), JObject), s.Carico)
            If j("banca") IsNot Nothing Then s.Banca = CInt(j("banca"))
            Dim benz As JObject = TryCast(j("benzina"), JObject)
            If benz IsNot Nothing Then
                For Each p As JProperty In benz.Properties()
                    s.Benzina(p.Name) = CDbl(p.Value)
                Next
            End If
            For Each x As JToken In If(TryCast(j("trofei"), JArray), New JArray())
                s.Trofei.Add(x.ToString())
            Next
            For Each x As JToken In If(TryCast(j("nascosti"), JArray), New JArray())
                s.Nascosti.Add(x.ToString())
            Next
            Dim rec As JObject = TryCast(j("record"), JObject)
            If rec IsNot Nothing Then
                For Each p As JProperty In rec.Properties()
                    s.Record(p.Name) = CDbl(p.Value)
                Next
            End If
            Dim rip As JObject = TryCast(j("ripresa"), JObject)
            If rip IsNot Nothing Then
                For Each p As JProperty In rip.Properties()
                    s.Ripresa(p.Name) = p.Value.ToString()
                Next
            End If
            Dim cont As JObject = TryCast(j("contatori"), JObject)
            If cont IsNot Nothing Then
                For Each p As JProperty In cont.Properties()
                    s.Contatori(p.Name) = CDbl(p.Value)
                Next
            End If
            If j("santino") IsNot Nothing Then s.SantinoFino = CDbl(j("santino"))
            If j("guadagnati") IsNot Nothing Then s.SoldiGuadagnati = CDbl(j("guadagnati"))
            If j("massimo_stelle") IsNot Nothing Then s.MassimoStelle = CInt(j("massimo_stelle"))
            s.Stato = TryCast(j("stato"), JObject)
            s.LavoroSospeso = TryCast(j("lavoro_sospeso"), JObject)
            Return s
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Carica", ex)
            Throw
        End Try
    End Function

    Private Shared Sub LeggiInteri(o As JObject, d As Dictionary(Of String, Integer))
        Try
            If o Is Nothing Then Return
            For Each p As JProperty In o.Properties()
                d(p.Name) = CInt(p.Value)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.LeggiInteri", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Oggetto(Of TV)(d As Dictionary(Of String, TV)) As JObject
        Try
            Dim o As New JObject()
            For Each kv As KeyValuePair(Of String, TV) In d
                o(kv.Key) = JToken.FromObject(kv.Value)
            Next
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Oggetto", ex)
            Throw
        End Try
    End Function

    Public Sub Salva()
        Try
            Dim mig As New JObject()
            For Each kv As KeyValuePair(Of String, HashSet(Of String)) In Migliorie
                mig(kv.Key) = New JArray(kv.Value.ToArray())
            Next
            Dim jScelte As New JObject()
            For Each kv As KeyValuePair(Of String, String) In Me.Scelte
                jScelte(kv.Key) = kv.Value
            Next
            Dim noto As New JObject()
            For Each kv As KeyValuePair(Of String, Double) In Notorieta
                noto(kv.Key) = Math.Round(kv.Value, 1)
            Next
            Dim j As New JObject From {
                {"versione", 2},
                {"soldi", Soldi},
                {"completate", New JArray(Completate.OrderBy(Function(x) x).ToArray())},
                {"posseduti", New JArray(Posseduti.ToArray())},
                {"migliorie", mig},
                {"proprieta", New JArray(Proprieta.OrderBy(Function(x) x).ToArray())},
                {"scelte", jScelte},
                {"bloccati", New JArray(Bloccati.ToArray())},
                {"notorieta", noto},
                {"minuti", Math.Round(Minuti)},
                {"metri", Math.Round(Metri)},
                {"arresti", Arresti},
                {"fallite", Fallite},
                {"lavori", LavoriFatti},
                {"corse", CorseVinte},
                {"finale", Finale},
                {"stima", Oggetto(Stima)},
                {"rispetto", Oggetto(Rispetto)},
                {"oggetti", Oggetto(Oggetti)},
                {"carico", Oggetto(Carico)},
                {"banca", Banca},
                {"benzina", Oggetto(Benzina)},
                {"trofei", New JArray(Trofei.OrderBy(Function(x) x).ToArray())},
                {"nascosti", New JArray(Nascosti.OrderBy(Function(x) x).ToArray())},
                {"record", Oggetto(Record)},
                {"ripresa", Oggetto(Ripresa)},
                {"contatori", Oggetto(Contatori)},
                {"santino", Math.Round(SantinoFino)},
                {"guadagnati", Math.Round(SoldiGuadagnati)},
                {"massimo_stelle", MassimoStelle}
            }
            If Stato IsNot Nothing Then j("stato") = Stato
            If LavoroSospeso IsNot Nothing Then j("lavoro_sospeso") = LavoroSospeso
            Dim p As String = Percorso(Posto)
            Dim temp As String = p & ".tmp"
            File.WriteAllText(temp, j.ToString(), New UTF8Encoding(False))
            If File.Exists(p) Then File.Delete(p)
            File.Move(temp, p)
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Salva", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Cancella la partita di un posto (per ricominciare da capo).</summary>
    Public Shared Sub Cancella(posto As Integer)
        Try
            If Esiste(posto) Then File.Delete(Percorso(posto))
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.Cancella", ex)
            Throw
        End Try
    End Sub

    ''' <summary>La partita della prima versione del gioco (partita.json), se c'è, diventa la partita del primo posto.</summary>
    Public Shared Sub ConvertiVecchia()
        Try
            Dim vecchio As String = Path.Combine(WGDb.WGDatabase.CartellaDati("citta"), "partita.json")
            If File.Exists(vecchio) AndAlso Not Esiste(1) Then File.Move(vecchio, Percorso(1))
        Catch ex As Exception
            RegistroErrori.Registra("Salvataggio.ConvertiVecchia", ex)
            Throw
        End Try
    End Sub
End Class
