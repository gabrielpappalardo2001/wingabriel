Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports Newtonsoft.Json.Linq

' ============================================================================================
' I dati di Porto Franco: la città (quartieri, incroci, strade, luoghi, personaggi, veicoli, radio), le missioni con la trama e i testi
' (lavori, eventi per strada, radio, scuola guida). Stanno in dati\citta.json, dati\missioni.json e dati\testi.json, incorporati nella
' libreria. Li costruiscono gli script in scripts\citta: la pianta la fa genera-citta.py, tutti i testi li scrive Gemini.
' Le coordinate sono in metri: x verso est, y verso nord. Il mare è a sud.
' ============================================================================================

Public NotInheritable Class Quartiere
    Public Id As String = ""
    Public Nome As String = ""
    Public Descrizione As String = ""
    ''' <summary>Cosa si sente camminando nel quartiere.</summary>
    Public Suoni As String = ""
    Public X0, Y0, X1, Y1 As Double
End Class

Public NotInheritable Class Incrocio
    Public Id As String = ""
    Public X As Double
    Public Y As Double
    Public Quartiere As Quartiere
    Public Semaforo As Boolean
    ''' <summary>Una rotonda: si entra piano, da qualunque parte si arrivi.</summary>
    Public Rotonda As Boolean
    Public ReadOnly Tratti As New List(Of Tratto)()
End Class

Public NotInheritable Class Strada
    Public Id As String = ""
    Public Nome As String = ""
    Public Quartiere As Quartiere
    Public Arteria As Boolean
End Class

''' <summary>Un tratto di strada tra due incroci, percorribile nei due sensi.</summary>
Public NotInheritable Class Tratto
    Public Id As String = ""
    Public A As Incrocio
    Public B As Incrocio
    Public Strada As Strada
    ''' <summary>Limite di velocità in km/h.</summary>
    Public Limite As Integer = 50
    Public Lunghezza As Double
    Public Galleria As Boolean
    Public Ponte As Boolean
    ''' <summary>Quanto sale andando da A a B (metri ogni metro; negativa se scende).</summary>
    Public Pendenza As Double
    ''' <summary>A metà del tratto la strada curva: -1 a sinistra, 1 a destra andando da A a B (0: dritta).</summary>
    Public Curva As Integer

    ''' <summary>L'altro capo del tratto.</summary>
    Public Function Altro(i As Incrocio) As Incrocio
        Try
            Return If(i Is A, B, A)
        Catch ex As Exception
            RegistroErrori.Registra("Tratto.Altro", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il punto a una certa distanza da A (in metri).</summary>
    Public Sub Punto(distanzaDaA As Double, ByRef x As Double, ByRef y As Double)
        Try
            Dim f As Double = If(Lunghezza > 0, Math.Max(0, Math.Min(1, distanzaDaA / Lunghezza)), 0)
            x = A.X + (B.X - A.X) * f
            y = A.Y + (B.Y - A.Y) * f
        Catch ex As Exception
            RegistroErrori.Registra("Tratto.Punto", ex)
            Throw
        End Try
    End Sub
End Class

Public NotInheritable Class Luogo
    Public Id As String = ""
    Public Nome As String = ""
    Public Tipo As String = ""
    Public Quartiere As Quartiere
    Public Descrizione As String = ""
    Public Tratto As Tratto
    ''' <summary>Distanza dal capo A del tratto, in metri.</summary>
    Public Distanza As Double
    Public Lato As String = "destra"
    Public X As Double
    Public Y As Double
    ''' <summary>Se si può comprare: il prezzo (0 se non è in vendita).</summary>
    Public Prezzo As Integer
    ''' <summary>Quanto rende ogni giorno, se è un'attività.</summary>
    Public Rendita As Integer
End Class

Public NotInheritable Class Personaggio
    Public Id As String = ""
    Public Nome As String = ""
    Public Quartiere As Quartiere
    Public Descrizione As String = ""
    Public Saluto As String = ""
    Public Luogo As Luogo
End Class

Public NotInheritable Class TipoVeicolo
    Public Id As String = ""
    Public Nome As String = ""
    ''' <summary>Velocità massima in metri al secondo.</summary>
    Public Velocita As Double
    ''' <summary>Accelerazione in metri al secondo quadrato.</summary>
    Public Accelerazione As Double
    Public Robustezza As Double
    ''' <summary>Quanto è acuto il motore (1 = normale).</summary>
    Public Suono As Double = 1
    ''' <summary>Il furgone, il taxi e il fuoristrada sono maschili; le altre femminili.</summary>
    Public Maschile As Boolean
    ''' <summary>Moto e scooter: si cade dopo un urto forte.</summary>
    Public DueRuote As Boolean

    ''' <summary>"la berlina Gabbiano", "il furgone Mulo".</summary>
    Public Function ConArticolo() As String
        Try
            Return If(Maschile, "il ", "la ") & Nome
        Catch ex As Exception
            RegistroErrori.Registra("TipoVeicolo.ConArticolo", ex)
            Throw
        End Try
    End Function

    ''' <summary>"una berlina Gabbiano", "un furgone Mulo".</summary>
    Public Function ConUn() As String
        Try
            Return If(Maschile, "un ", "una ") & Nome
        Catch ex As Exception
            RegistroErrori.Registra("TipoVeicolo.ConUn", ex)
            Throw
        End Try
    End Function

    ''' <summary>"sulla berlina Gabbiano", "sul furgone Mulo".</summary>
    Public Function ConSu() As String
        Try
            Return If(Maschile, "sul ", "sulla ") & Nome
        Catch ex As Exception
            RegistroErrori.Registra("TipoVeicolo.ConSu", ex)
            Throw
        End Try
    End Function
End Class

Public NotInheritable Class Parcheggio
    Public Tratto As Tratto
    Public Distanza As Double
    Public Veicolo As TipoVeicolo
End Class

Public NotInheritable Class PassoMissione
    Public Tipo As String = ""
    Public Testo As String = ""
    Public Luogo As Luogo
    Public Da As Luogo
    Public A As Luogo
    Public Veicolo As String = ""
    Public Tempo As Integer
    Public Integro As Boolean
    Public Stelle As Integer
    Public Secondi As Integer
    ''' <summary>Per raccogli: il quartiere, quanti oggetti e come si chiamano.</summary>
    Public Quartiere As Quartiere
    Public Quanti As Integer
    Public Oggetto As String = ""
    ''' <summary>Per scelta: la domanda e le due risposte.</summary>
    Public Domanda As String = ""
    Public ReadOnly Opzioni As New List(Of OpzioneMissione)()
End Class

''' <summary>Una risposta a una scelta a metà missione. Effetto: soldi, rispetto o polizia.</summary>
Public NotInheritable Class OpzioneMissione
    Public Testo As String = ""
    Public Risposta As String = ""
    Public Effetto As String = ""
End Class

''' <summary>Un oggetto nascosto per strada: si trova seguendo il suo suono.</summary>
Public NotInheritable Class Nascosto
    Public Id As String = ""
    Public Nome As String = ""
    Public Storia As String = ""
    Public Tratto As Tratto
    Public Distanza As Double
    Public X As Double
    Public Y As Double
End Class

''' <summary>Una sfida a tempo: da un luogo a un altro.</summary>
Public NotInheritable Class Sfida
    Public Id As String = ""
    Public Nome As String = ""
    Public Presentazione As String = ""
    Public Da As Luogo
    Public A As Luogo
    ''' <summary>Il tempo da battere, in secondi.</summary>
    Public Tempo As Integer
End Class

''' <summary>La banda di un quartiere e la gente che si sente per strada.</summary>
Public NotInheritable Class VitaQuartiere
    Public NomeBanda As String = ""
    Public Capo As String = ""
    Public DescrizioneBanda As String = ""
    Public Minaccia As String = ""
    Public Passaggio As String = ""
    Public ReadOnly Passanti As New List(Of String)()
    Public ReadOnly Ambulanti As New List(Of String)()
    Public ReadOnly Vigile As New List(Of String)()
End Class

''' <summary>Una cosa che si compra in un negozio o in un bar.</summary>
Public NotInheritable Class Merce
    ''' <summary>caffe, panino, giornale, gratta, attrezzi, santino, mappa, cappello.</summary>
    Public Effetto As String = ""
    Public Nome As String = ""
    Public Prezzo As Integer
    Public Descrizione As String = ""
End Class

Public NotInheritable Class Trofeo
    Public Id As String = ""
    Public Nome As String = ""
    Public Descrizione As String = ""
End Class

Public NotInheritable Class Missione
    Public Id As String = ""
    Public Personaggio As Personaggio
    Public Luogo As Luogo
    Public ReadOnly Richiede As New List(Of String)()
    Public Capitolo As Integer = 1
    Public Ordine As Integer
    Public Titolo As String = ""
    Public Introduzione As String = ""
    Public Conclusione As String = ""
    Public Soldi As Integer
    ''' <summary>La missione che chiude il capitolo: apre il capitolo dopo e non si blocca mai.</summary>
    Public Finale As Boolean
    Public ReadOnly Passi As New List(Of PassoMissione)()

    ''' <summary>Una storia di quartiere (capitolo 0): fuori dalla trama, si fa quando si vuole.</summary>
    Public ReadOnly Property Storia As Boolean
        Get
            Return Capitolo = 0
        End Get
    End Property
End Class

Public NotInheritable Class Capitolo
    Public Numero As Integer
    Public Titolo As String = ""
    Public Racconto As String = ""
End Class

Public NotInheritable Class OpzioneScelta
    Public Valore As String = ""
    Public Testo As String = ""
    Public Conseguenza As String = ""
    ''' <summary>Il personaggio che dopo questa scelta non dà più lavori (Nothing se nessuno).</summary>
    Public Blocca As Personaggio
End Class

Public NotInheritable Class Scelta
    Public Id As String = ""
    Public Capitolo As Integer
    Public Personaggio As Personaggio
    Public Domanda As String = ""
    Public ReadOnly Opzioni As New List(Of OpzioneScelta)()
End Class

Public NotInheritable Class RadioInfo
    Public Nome As String = ""
    Public Descrizione As String = ""
    ''' <summary>melodica, rock o elettronica.</summary>
    Public Musica As String = ""
    Public Conduttore As String = ""
    Public VoceDonna As Boolean
    Public ReadOnly Battute As New List(Of String)()
    Public ReadOnly Presentazioni As New List(Of String)()
    Public ReadOnly Pubblicita As New List(Of String)()
    ''' <summary>Le frasi che annunciano una canzone vera: contengono {canzone} e {autore}.</summary>
    Public ReadOnly Annunci As New List(Of String)()
    ''' <summary>Notizie per tipo di fatto: inseguimento, missione, arresto, incidente, corsa, pioggia, notte, mattina.</summary>
    Public ReadOnly Notizie As New Dictionary(Of String, List(Of String))()
End Class

''' <summary>Tutti i dati della città, letti una volta dalle risorse della libreria.</summary>
Public NotInheritable Class DatiCitta
    Public Nome As String = ""
    Public Motto As String = ""
    Public Descrizione As String = ""
    Public Larghezza As Double
    Public Altezza As Double
    ''' <summary>La x del canale (0 se non c'è).</summary>
    Public Canale As Double
    ''' <summary>Le x degli altri canali (si passano solo sui ponti).</summary>
    Public ReadOnly Canali As New List(Of Double)()
    Public ReadOnly Quartieri As New List(Of Quartiere)()
    Public ReadOnly Incroci As New Dictionary(Of String, Incrocio)()
    Public ReadOnly Strade As New Dictionary(Of String, Strada)()
    Public ReadOnly Tratti As New List(Of Tratto)()
    Public ReadOnly Luoghi As New Dictionary(Of String, Luogo)()
    Public ReadOnly Personaggi As New Dictionary(Of String, Personaggio)()
    Public ReadOnly Veicoli As New Dictionary(Of String, TipoVeicolo)()
    Public ReadOnly PrezziVeicoli As New Dictionary(Of String, Integer)()
    Public ReadOnly Parcheggi As New List(Of Parcheggio)()
    Public ReadOnly Missioni As New List(Of Missione)()
    Public ReadOnly Capitoli As New List(Of Capitolo)()
    Public ReadOnly Scelte As New List(Of Scelta)()
    ''' <summary>Il finale per ogni valore della seconda scelta.</summary>
    Public ReadOnly Finali As New Dictionary(Of String, String)()
    Public ReadOnly Radio As New List(Of RadioInfo)()
    Public ReadOnly Nascosti As New List(Of Nascosto)()
    Public ReadOnly Sfide As New List(Of Sfida)()
    ''' <summary>La banda e la vita di strada di ogni quartiere (per id del quartiere).</summary>
    Public ReadOnly Vita As New Dictionary(Of String, VitaQuartiere)()
    ''' <summary>Le telefonate degli amici (possono contenere {quartiere}).</summary>
    Public ReadOnly Telefonate As New List(Of String)()
    ''' <summary>Cosa si sente entrando in un luogo, per tipo di luogo.</summary>
    Public ReadOnly Scene As New Dictionary(Of String, List(Of String))()
    Public ReadOnly Merci As New List(Of Merce)()
    Public ReadOnly Trofei As New List(Of Trofeo)()
    ''' <summary>I testi dei lavori, degli eventi e della scuola guida (testi.json così com'è).</summary>
    Public Testi As JObject

    Private Shared _dati As DatiCitta

    ''' <summary>I dati della città (si leggono la prima volta che servono).</summary>
    Public Shared Function Carica() As DatiCitta
        Try
            If _dati Is Nothing Then _dati = Leggi()
            Return _dati
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.Carica", ex)
            Throw
        End Try
    End Function

    Private Shared Function Risorsa(nome As String) As JObject
        Try
            Using s As Stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("dati." & nome & ".json")
                If s Is Nothing Then Throw New FileNotFoundException("Manca la risorsa dati." & nome & ".json")
                Using r As New StreamReader(s, Encoding.UTF8)
                    Return JObject.Parse(r.ReadToEnd())
                End Using
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.Risorsa", ex)
            Throw
        End Try
    End Function

    Private Shared Function T(o As JToken, nome As String) As String
        Try
            Dim v As JToken = o(nome)
            Return If(v Is Nothing OrElse v.Type = JTokenType.Null, "", v.ToString())
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.T", ex)
            Throw
        End Try
    End Function

    Private Shared Function N(o As JToken, nome As String) As Double
        Try
            Dim v As JToken = o(nome)
            If v Is Nothing OrElse v.Type = JTokenType.Null Then Return 0
            If v.Type = JTokenType.String Then
                Dim x As Double
                Return If(Double.TryParse(v.ToString(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, x), x, 0)
            End If
            Return CDbl(v)
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.N", ex)
            Throw
        End Try
    End Function

    Private Shared Function B(o As JToken, nome As String) As Boolean
        Try
            Dim v As JToken = o(nome)
            Return v IsNot Nothing AndAlso v.Type = JTokenType.Boolean AndAlso CBool(v)
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.B", ex)
            Throw
        End Try
    End Function

    Private Shared Function Stringhe(a As JToken) As List(Of String)
        Try
            Dim r As New List(Of String)()
            If a Is Nothing Then Return r
            For Each x As JToken In a
                r.Add(x.ToString())
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.Stringhe", ex)
            Throw
        End Try
    End Function

    Private Shared Function Leggi() As DatiCitta
        Try
            Dim d As New DatiCitta()
            Dim c As JObject = Risorsa("citta")
            d.Nome = T(c, "nome")
            d.Motto = T(c, "motto")
            d.Descrizione = T(c, "descrizione")
            d.Larghezza = N(c, "larghezza")
            d.Altezza = N(c, "altezza")
            d.Canale = N(c, "canale")
            Dim altriCanali As JArray = TryCast(c("canali"), JArray)
            If altriCanali IsNot Nothing Then
                For Each xcan As JToken In altriCanali
                    d.Canali.Add(CDbl(xcan))
                Next
            End If
            Dim quartieri As New Dictionary(Of String, Quartiere)()
            For Each q As JToken In c("quartieri")
                Dim x As New Quartiere With {.Id = T(q, "id"), .Nome = T(q, "nome"), .Descrizione = T(q, "descrizione"), .Suoni = T(q, "suoni"), .X0 = N(q, "x0"), .Y0 = N(q, "y0"), .X1 = N(q, "x1"), .Y1 = N(q, "y1")}
                d.Quartieri.Add(x)
                quartieri(x.Id) = x
            Next
            For Each i As JToken In c("incroci")
                Dim x As New Incrocio With {.Id = T(i, "id"), .X = N(i, "x"), .Y = N(i, "y"), .Quartiere = quartieri(T(i, "quartiere")), .Semaforo = B(i, "semaforo"), .Rotonda = B(i, "rotonda")}
                d.Incroci(x.Id) = x
            Next
            For Each s As JToken In c("strade")
                Dim x As New Strada With {.Id = T(s, "id"), .Nome = T(s, "nome"), .Quartiere = quartieri(T(s, "quartiere")), .Arteria = B(s, "arteria")}
                d.Strade(x.Id) = x
            Next
            Dim tratti As New Dictionary(Of String, Tratto)()
            For Each jt As JToken In c("tratti")
                Dim x As New Tratto With {.Id = T(jt, "id"), .A = d.Incroci(T(jt, "a")), .B = d.Incroci(T(jt, "b")), .Strada = d.Strade(T(jt, "strada")), .Limite = CInt(N(jt, "limite")),
                                          .Galleria = B(jt, "galleria"), .Ponte = B(jt, "ponte"), .Pendenza = N(jt, "pendenza"), .Curva = CInt(N(jt, "curva"))}
                x.Lunghezza = Math.Sqrt((x.B.X - x.A.X) ^ 2 + (x.B.Y - x.A.Y) ^ 2)
                x.A.Tratti.Add(x)
                x.B.Tratti.Add(x)
                d.Tratti.Add(x)
                tratti(x.Id) = x
            Next
            For Each l As JToken In c("luoghi")
                Dim tr As Tratto = tratti(T(l, "tratto"))
                Dim x As New Luogo With {.Id = T(l, "id"), .Nome = T(l, "nome"), .Tipo = T(l, "tipo"), .Quartiere = quartieri(T(l, "quartiere")), .Descrizione = T(l, "descrizione"),
                                         .Tratto = tr, .Distanza = N(l, "posizione") * tr.Lunghezza, .Lato = T(l, "lato"), .Prezzo = CInt(N(l, "prezzo")), .Rendita = CInt(N(l, "rendita"))}
                tr.Punto(x.Distanza, x.X, x.Y)
                d.Luoghi(x.Id) = x
            Next
            For Each p As JToken In c("personaggi")
                Dim x As New Personaggio With {.Id = T(p, "id"), .Nome = T(p, "nome"), .Quartiere = quartieri(T(p, "quartiere")), .Descrizione = T(p, "descrizione"), .Saluto = T(p, "saluto"), .Luogo = d.Luoghi(T(p, "luogo"))}
                d.Personaggi(x.Id) = x
            Next
            For Each v As JToken In c("veicoli")
                ' nei dati la velocità è un numero di comodo: in metri al secondo vale circa sei decimi (la utilitaria arriva a 72 km/h, la sportiva a 130)
                Dim x As New TipoVeicolo With {.Id = T(v, "id"), .Nome = T(v, "nome"), .Velocita = N(v, "velocita") * 0.62, .Accelerazione = N(v, "accelerazione"),
                                               .Robustezza = N(v, "robustezza"), .Suono = N(v, "suono"), .Maschile = B(v, "maschile"), .DueRuote = B(v, "due_ruote")}
                d.Veicoli(x.Id) = x
            Next
            Dim prezzi As JObject = TryCast(c("prezzi_veicoli"), JObject)
            If prezzi IsNot Nothing Then
                For Each p As JProperty In prezzi.Properties()
                    d.PrezziVeicoli(p.Name) = CInt(p.Value)
                Next
            End If
            For Each p As JToken In c("parcheggi")
                Dim tr As Tratto = tratti(T(p, "tratto"))
                d.Parcheggi.Add(New Parcheggio With {.Tratto = tr, .Distanza = N(p, "posizione") * tr.Lunghezza, .Veicolo = d.Veicoli(T(p, "veicolo"))})
            Next
            ' ---- le missioni e la trama
            Dim m As JObject = Risorsa("missioni")
            For Each j As JToken In m("missioni")
                Dim x As New Missione With {.Id = T(j, "id"), .Personaggio = d.Personaggi(T(j, "personaggio")), .Luogo = d.Luoghi(T(j, "luogo")), .Titolo = T(j, "titolo"),
                                            .Introduzione = T(j, "introduzione"), .Conclusione = T(j, "conclusione"), .Soldi = CInt(N(j, "soldi")),
                                            .Capitolo = Math.Max(0, CInt(N(j, "capitolo"))), .Ordine = CInt(N(j, "ordine")), .Finale = B(j, "finale")}
                x.Richiede.AddRange(Stringhe(j("richiede")))
                For Each p As JToken In j("passi")
                    Dim passo As New PassoMissione With {.Tipo = T(p, "tipo"), .Testo = T(p, "testo"), .Veicolo = T(p, "veicolo"), .Tempo = CInt(N(p, "tempo")),
                                                         .Stelle = CInt(N(p, "stelle")), .Secondi = CInt(N(p, "secondi")), .Integro = B(p, "integro"),
                                                         .Quanti = CInt(N(p, "quanti")), .Oggetto = T(p, "oggetto"), .Domanda = T(p, "domanda")}
                    If T(p, "luogo") <> "" Then passo.Luogo = d.Luoghi(T(p, "luogo"))
                    If T(p, "da") <> "" Then passo.Da = d.Luoghi(T(p, "da"))
                    If T(p, "a") <> "" Then passo.A = d.Luoghi(T(p, "a"))
                    If T(p, "quartiere") <> "" Then passo.Quartiere = quartieri(T(p, "quartiere"))
                    For Each oj As JToken In If(TryCast(p("opzioni"), JArray), New JArray())
                        passo.Opzioni.Add(New OpzioneMissione With {.Testo = T(oj, "testo"), .Risposta = T(oj, "risposta"), .Effetto = T(oj, "effetto")})
                    Next
                    ' la scelta a metà missione: il testo da dire è la domanda
                    If passo.Tipo = "scelta" AndAlso passo.Testo = "" Then passo.Testo = passo.Domanda
                    x.Passi.Add(passo)
                Next
                d.Missioni.Add(x)
            Next
            Dim trama As JToken = m("trama")
            If trama IsNot Nothing Then
                For Each cj As JToken In trama("capitoli")
                    d.Capitoli.Add(New Capitolo With {.Numero = CInt(N(cj, "numero")), .Titolo = T(cj, "titolo"), .Racconto = T(cj, "racconto")})
                Next
                For Each sj As JToken In trama("scelte")
                    Dim s As New Scelta With {.Id = T(sj, "id"), .Capitolo = CInt(N(sj, "capitolo")), .Domanda = T(sj, "domanda")}
                    Dim chi As Personaggio = Nothing
                    If d.Personaggi.TryGetValue(T(sj, "personaggio"), chi) Then s.Personaggio = chi
                    For Each oj As JToken In sj("opzioni")
                        Dim o As New OpzioneScelta With {.Valore = T(oj, "valore"), .Testo = T(oj, "testo"), .Conseguenza = T(oj, "conseguenza")}
                        Dim bloccato As Personaggio = Nothing
                        If T(oj, "blocca") <> "" AndAlso d.Personaggi.TryGetValue(T(oj, "blocca"), bloccato) Then o.Blocca = bloccato
                        s.Opzioni.Add(o)
                    Next
                    d.Scelte.Add(s)
                Next
                For Each fj As JToken In trama("finali")
                    d.Finali(T(fj, "valore")) = T(fj, "racconto")
                Next
            End If
            ' ---- i testi
            d.Testi = Risorsa("testi")
            Dim radioTesti As JArray = TryCast(d.Testi("radio"), JArray)
            Dim k As Integer = 0
            For Each rj As JToken In If(TryCast(c("radio"), JArray), New JArray())
                Dim r As New RadioInfo With {.Nome = T(rj, "nome"), .Descrizione = T(rj, "descrizione"), .Musica = T(rj, "musica"), .Conduttore = T(rj, "conduttore"), .VoceDonna = T(rj, "voce") = "donna"}
                If radioTesti IsNot Nothing AndAlso k < radioTesti.Count Then
                    Dim tj As JToken = radioTesti(k)
                    r.Battute.AddRange(Stringhe(tj("battute")))
                    r.Presentazioni.AddRange(Stringhe(tj("presentazioni")))
                    r.Pubblicita.AddRange(Stringhe(tj("pubblicita")))
                    r.Annunci.AddRange(Stringhe(tj("annunci")))
                    Dim notizie As JObject = TryCast(tj("notizie"), JObject)
                    If notizie IsNot Nothing Then
                        For Each p As JProperty In notizie.Properties()
                            r.Notizie(p.Name) = Stringhe(p.Value)
                        Next
                    End If
                End If
                d.Radio.Add(r)
                k += 1
            Next
            ' ---- oggetti nascosti e sfide
            For Each nj As JToken In If(TryCast(c("nascosti"), JArray), New JArray())
                Dim tr As Tratto = tratti(T(nj, "tratto"))
                Dim x As New Nascosto With {.Id = T(nj, "id"), .Nome = T(nj, "nome"), .Storia = T(nj, "storia"), .Tratto = tr, .Distanza = N(nj, "posizione") * tr.Lunghezza}
                tr.Punto(x.Distanza, x.X, x.Y)
                d.Nascosti.Add(x)
            Next
            For Each sj As JToken In If(TryCast(c("sfide"), JArray), New JArray())
                d.Sfide.Add(New Sfida With {.Id = T(sj, "id"), .Nome = T(sj, "nome"), .Presentazione = T(sj, "presentazione"), .Da = d.Luoghi(T(sj, "da")), .A = d.Luoghi(T(sj, "a")), .Tempo = CInt(N(sj, "tempo"))})
            Next
            ' ---- la vita di strada, le scene dei luoghi, le merci e i trofei
            Dim vita As JObject = TryCast(d.Testi("vita"), JObject)
            If vita IsNot Nothing Then
                For Each qj As JToken In If(TryCast(vita("quartieri"), JArray), New JArray())
                    Dim bj As JToken = If(qj("banda"), New JObject())
                    Dim x As New VitaQuartiere With {.NomeBanda = T(bj, "nome"), .Capo = T(bj, "capo"), .DescrizioneBanda = T(bj, "descrizione"), .Minaccia = T(bj, "minaccia"), .Passaggio = T(bj, "passaggio")}
                    x.Passanti.AddRange(Stringhe(qj("passanti")))
                    x.Ambulanti.AddRange(Stringhe(qj("ambulanti")))
                    x.Vigile.AddRange(Stringhe(qj("vigile")))
                    d.Vita(T(qj, "id")) = x
                Next
                d.Telefonate.AddRange(Stringhe(vita("telefonate")))
            End If
            Dim luoghiTesti As JObject = TryCast(d.Testi("luoghi"), JObject)
            If luoghiTesti IsNot Nothing Then
                Dim scene As JObject = TryCast(luoghiTesti("scene"), JObject)
                If scene IsNot Nothing Then
                    For Each p As JProperty In scene.Properties()
                        d.Scene(p.Name) = Stringhe(p.Value)
                    Next
                End If
                For Each oj As JToken In If(TryCast(luoghiTesti("oggetti"), JArray), New JArray())
                    d.Merci.Add(New Merce With {.Effetto = T(oj, "effetto"), .Nome = T(oj, "nome"), .Prezzo = Math.Max(1, CInt(N(oj, "prezzo"))), .Descrizione = T(oj, "descrizione")})
                Next
            End If
            For Each tj As JToken In If(TryCast(d.Testi("trofei"), JArray), New JArray())
                d.Trofei.Add(New Trofeo With {.Id = T(tj, "id"), .Nome = T(tj, "nome"), .Descrizione = T(tj, "descrizione")})
            Next
            Return d
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.Leggi", ex)
            Throw
        End Try
    End Function

    Private Function Elenco(sezione As String, chiave As String) As JArray
        Try
            Dim s As JObject = TryCast(Testi(sezione), JObject)
            If s Is Nothing Then Return Nothing
            Return TryCast(s(chiave), JArray)
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.Elenco", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un testo a caso da un elenco di testi.json (per esempio "eventi", "pedoni"); "" se manca.</summary>
    Public Function TestoACaso(sezione As String, chiave As String, caso As Random) As String
        Try
            Dim a As JArray = Elenco(sezione, chiave)
            If a Is Nothing OrElse a.Count = 0 Then Return ""
            Return a(caso.Next(a.Count)).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.TestoACaso", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un oggetto a caso da un elenco di testi.json (per esempio "lavori", "taxi"); Nothing se manca.</summary>
    Public Function OggettoACaso(sezione As String, chiave As String, caso As Random) As JObject
        Try
            Dim a As JArray = Elenco(sezione, chiave)
            If a Is Nothing OrElse a.Count = 0 Then Return Nothing
            Return TryCast(a(caso.Next(a.Count)), JObject)
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.OggettoACaso", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una frase della scuola guida ("" se manca).</summary>
    Public Function Scuola(chiave As String) As String
        Try
            Dim s As JObject = TryCast(Testi("scuola"), JObject)
            If s Is Nothing OrElse s(chiave) Is Nothing Then Return ""
            Return s(chiave).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.Scuola", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il quartiere in cui cade un punto (il più vicino, se è sul confine).</summary>
    Public Function QuartiereDi(x As Double, y As Double) As Quartiere
        Try
            Dim migliore As Quartiere = Quartieri(0)
            Dim dm As Double = Double.MaxValue
            For Each q As Quartiere In Quartieri
                Dim cx As Double = Math.Max(q.X0, Math.Min(q.X1, x))
                Dim cy As Double = Math.Max(q.Y0, Math.Min(q.Y1, y))
                Dim dd As Double = (cx - x) ^ 2 + (cy - y) ^ 2
                If dd < dm Then
                    dm = dd
                    migliore = q
                End If
            Next
            Return migliore
        Catch ex As Exception
            RegistroErrori.Registra("DatiCitta.QuartiereDi", ex)
            Throw
        End Try
    End Function
End Class
