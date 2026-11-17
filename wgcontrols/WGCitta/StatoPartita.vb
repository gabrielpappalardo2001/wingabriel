Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

' ============================================================================================
' Lo stato completo della partita: oltre ai numeri di Salvataggio (soldi, missioni fatte, stima, contatori...) si salva dove sei, se sei a
' piedi o su quale veicolo, i tuoi veicoli parcheggiati (con salute, benzina e merce), il tempo, le stelle, la meta del navigatore, la
' missione a metà (al passo in cui eri), il lavoro in corso. Si riprende esattamente da lì.
' ============================================================================================

Public NotInheritable Class StatoPartita

    Public Shared Function Testo(o As JToken, chiave As String) As String
        Try
            If o Is Nothing OrElse o(chiave) Is Nothing Then Return ""
            Return o(chiave).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.Testo", ex)
            Throw
        End Try
    End Function

    Public Shared Function Numero(o As JToken, chiave As String) As Double
        Try
            If o Is Nothing OrElse o(chiave) Is Nothing Then Return 0
            Dim t As JToken = o(chiave)
            If t.Type = JTokenType.Integer OrElse t.Type = JTokenType.Float Then Return t.Value(Of Double)()
            Dim v As Double
            Double.TryParse(t.ToString(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, v)
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.Numero", ex)
            Throw
        End Try
    End Function

    Public Shared Function Vero(o As JToken, chiave As String) As Boolean
        Try
            Return o IsNot Nothing AndAlso o(chiave) IsNot Nothing AndAlso String.Equals(o(chiave).ToString(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.Vero", ex)
            Throw
        End Try
    End Function

    Private Shared Function DaPosizione(p As Posizione) As JObject
        Try
            Return New JObject From {{"t", p.Tratto.Id}, {"d", Math.Round(p.Distanza, 1)}, {"v", p.Verso}}
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.DaPosizione", ex)
            Throw
        End Try
    End Function

    Private Shared Function APosizione(m As Mondo, o As JToken) As Posizione
        Try
            If o Is Nothing OrElse o.Type <> JTokenType.Object Then Return Nothing
            Dim id As String = Testo(o, "t")
            Dim tr As Tratto = m.Dati.Tratti.FirstOrDefault(Function(x) x.Id = id)
            If tr Is Nothing Then Return Nothing
            Return New Posizione(tr, Math.Max(0, Math.Min(tr.Lunghezza, Numero(o, "d"))), If(Numero(o, "v") < 0, -1, 1))
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.APosizione", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una fotografia del mondo adesso (si mette in Salvataggio.Stato).</summary>
    Public Shared Function Fotografa(m As Mondo) As JObject
        Try
            Dim j As New JObject()
            If m.Scuola Then Return j
            j("apiedi") = m.APiedi
            j("piedi") = DaPosizione(m.PosPiedi)
            j("pioggia") = m.Pioggia
            j("stelle") = m.Stelle
            j("corsia") = m.Corsia
            If m.Meta IsNot Nothing AndAlso m.Dati.Luoghi.ContainsKey(m.Meta.Id) Then j("meta") = m.Meta.Id
            Dim daMissione As Veicolo = m.Missioni.VeicoloDellaMissione
            Dim vs As New JArray()
            For Each v As Veicolo In m.Veicoli
                If Not (v.Tuo OrElse v Is m.Veicolo OrElse v Is daMissione) Then Continue For
                If v.Ruolo <> RuoloVeicolo.Parcheggiato AndAlso v.Ruolo <> RuoloVeicolo.Giocatore Then Continue For
                Dim carico As New JObject()
                For Each kv As KeyValuePair(Of String, Integer) In v.Carico
                    carico(kv.Key) = kv.Value
                Next
                vs.Add(New JObject From {{"tipo", v.Tipo.Id}, {"pos", DaPosizione(v.Pos)}, {"salute", Math.Round(v.Salute, 1)}, {"benzina", Math.Round(v.Benzina, 1)},
                                         {"tuo", v.Tuo}, {"corrente", v Is m.Veicolo}, {"missione", v Is daMissione}, {"carico", carico}})
            Next
            j("veicoli") = vs
            j("missioni") = m.Missioni.Fotografa()
            j("lavori") = m.Lavori.Fotografa()
            j("economia") = m.Economia.Fotografa()
            Return j
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.Fotografa", ex)
            Throw
        End Try
    End Function

    ''' <summary>Rimette il mondo com'era nella fotografia (senza parlare: poi si chiama Riprendi). Falso se non c'è niente da ripristinare.</summary>
    Public Shared Function Ripristina(m As Mondo, j As JObject) As Boolean
        Try
            If j Is Nothing OrElse m.Scuola OrElse j("piedi") Is Nothing Then Return False
            Dim corrente As Veicolo = Nothing
            Dim daMissione As Veicolo = Nothing
            Dim vs As JArray = TryCast(j("veicoli"), JArray)
            If vs IsNot Nothing Then
                m.Veicoli.RemoveAll(Function(x) x.Tuo)
                For Each o As JToken In vs
                    Dim tipo As TipoVeicolo = Nothing
                    If Not m.Dati.Veicoli.TryGetValue(Testo(o, "tipo"), tipo) Then Continue For
                    Dim p As Posizione = APosizione(m, o("pos"))
                    If p Is Nothing Then Continue For
                    Dim nuovo As New Veicolo With {.Tipo = tipo, .Pos = p, .Tuo = Vero(o, "tuo"), .Ruolo = RuoloVeicolo.Parcheggiato, .Salute = Numero(o, "salute"), .Benzina = Numero(o, "benzina")}
                    Dim carico As JObject = TryCast(o("carico"), JObject)
                    If carico IsNot Nothing Then
                        For Each pr As JProperty In carico.Properties()
                            nuovo.Carico(pr.Name) = CInt(pr.Value)
                        Next
                    End If
                    m.Veicoli.Add(nuovo)
                    If Vero(o, "corrente") Then corrente = nuovo
                    If Vero(o, "missione") Then daMissione = nuovo
                Next
            End If
            Dim pp As Posizione = APosizione(m, j("piedi"))
            If pp IsNot Nothing Then m.PosPiedi = pp
            If corrente IsNot Nothing AndAlso Not Vero(j, "apiedi") Then
                corrente.Ruolo = RuoloVeicolo.Giocatore
                m.Veicolo = corrente
                m.APiedi = False
            Else
                m.Veicolo = Nothing
                m.APiedi = True
            End If
            m.Pioggia = Vero(j, "pioggia")
            m.Stelle = CInt(Numero(j, "stelle"))
            m.Corsia = CInt(Numero(j, "corsia"))
            Dim idMeta As String = Testo(j, "meta")
            Dim dest As Luogo = Nothing
            If idMeta <> "" AndAlso m.Dati.Luoghi.TryGetValue(idMeta, dest) Then m.ImpostaMeta(dest, True)
            m.Missioni.Ripristina(TryCast(j("missioni"), JObject), daMissione)
            m.Lavori.Ripristina(TryCast(j("lavori"), JObject))
            m.Economia.Ripristina(TryCast(j("economia"), JObject))
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.Ripristina", ex)
            Throw
        End Try
    End Function

    ''' <summary>Dopo il ripristino: dice cosa si stava facendo (missione, lavoro).</summary>
    Public Shared Sub Riprendi(m As Mondo)
        Try
            m.Missioni.Riprendi()
            m.Lavori.Riprendi()
            m.Economia.Riprendi()
        Catch ex As Exception
            RegistroErrori.Registra("StatoPartita.Riprendi", ex)
            Throw
        End Try
    End Sub
End Class
