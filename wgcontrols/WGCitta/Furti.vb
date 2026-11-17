Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

' ============================================================================================
' I furti di soldi e la rivendita di Porto Franco (frasi scritte da Gemini, in testi.json, sezione furti). Niente armi e niente violenza:
'  - il borseggio: a piedi, si prova a infilare la mano nella tasca di un passante; va meglio di notte e quando si è stimati poco dalla
'    polizia, va peggio con una volante vicina;
'  - la cassa: in un bar, un negozio, un ristorante, un distributore si svuota la cassa; poi per qualche giorno lì non c'è più niente;
'  - il colpo in banca: rende molto, riesce poco, e se va storto la polizia arriva subito; una banca si colpisce una volta alla settimana;
'  - la rivendita: il ricettatore compra i veicoli rubati; ogni giorno tre committenti ordinano un'auto precisa pagandola di più (sono sempre
'    gli stessi per tutto il giorno); al concessionario si vendono anche le macchine proprie, a poco più della metà del listino.
' ============================================================================================

''' <summary>Un'auto ordinata da un committente, per oggi.</summary>
Public NotInheritable Class OrdineAuto
    Public Tipo As String = ""
    Public Prezzo As Integer
    Public Consegnato As Boolean
End Class

Public NotInheritable Class FurtiCitta
    Private ReadOnly _m As Mondo
    Private _prossimoBorseggio As Double

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Una frase a caso di testi.json, sezione furti, gruppo e chiave; "" se manca.</summary>
    Private Function Frase(gruppo As String, chiave As String) As String
        Try
            Dim f As JObject = TryCast(_m.Dati.Testi("furti"), JObject)
            If f Is Nothing Then Return ""
            Dim g As JObject = TryCast(f(gruppo), JObject)
            If g Is Nothing Then Return ""
            Dim a As JArray = TryCast(g(chiave), JArray)
            If a Is Nothing OrElse a.Count = 0 Then Return ""
            Return a(_m.Caso().Next(a.Count)).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.Frase", ex)
            Throw
        End Try
    End Function

    Private Function PoliziaVicina() As Boolean
        Try
            Return _m.Veicoli.Any(Function(x) x.Ruolo = RuoloVeicolo.Polizia AndAlso _m.DistanzaDa(x.X, x.Y) < 130)
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.PoliziaVicina", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ il borseggio
    ''' <summary>Dal menu Alt, a piedi: prova a prendere il portafoglio a un passante.</summary>
    Public Sub Borseggia()
        Try
            If Not _m.APiedi Then
                _m.Dici("Per borseggiare devi essere a piedi.", True)
                Return
            End If
            If _m.Stelle > 0 Then
                _m.Dici("Con la polizia alle calcagna non è il momento.", True)
                Return
            End If
            If _m.Tempo < _prossimoBorseggio Then
                _m.Dici("Aspetta un po', se no ti notano.", True)
                Return
            End If
            Dim caso As Random = _m.Caso()
            If caso.NextDouble() < If(_m.Notte, 0.45, 0.15) Then
                _prossimoBorseggio = _m.Tempo + 5
                _m.Dici(Frase("borseggio", "nessuno"), True)
                Return
            End If
            _prossimoBorseggio = _m.Tempo + 20
            Dim bravura As Double = Math.Min(0.15, _m.Partita.Contatore("borseggi") / 100.0)
            Dim probabilita As Double = 0.5 + bravura + If(_m.Notte, 0.1, 0) - If(PoliziaVicina(), 0.25, 0)
            Dim quartiere As String = _m.Dati.QuartiereDi(_m.X, _m.Y).Id
            If caso.NextDouble() < probabilita Then
                Dim euro As Integer = 15 + caso.Next(0, 90)
                _m.Partita.Guadagna(euro)
                _m.Partita.Conta("borseggi")
                _m.Partita.Conta("soldi_rubati", euro)
                If caso.NextDouble() < 0.3 Then _m.Partita.CambiaRispetto(quartiere, -1)
                _m.Suona(MotoreAudio.Soldi(), 0.5F, 0)
                _m.Dici(Frase("borseggio", "successo") & " Hai in più " & euro.ToString() & " euro.", True)
            Else
                _m.Partita.CambiaRispetto(quartiere, -1)
                _m.Dici(Frase("borseggio", "scoperto"), True)
                If caso.NextDouble() < 0.5 Then _m.Ricerca(1, "Qualcuno ha chiamato la polizia!")
            End If
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.Borseggia", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ la cassa
    Public Shared Function HaUnaCassa(tipo As String) As Boolean
        Select Case tipo
            Case "bar", "ristorante", "pizzeria", "negozio", "mercato", "distributore", "albergo", "club"
                Return True
            Case Else
                Return False
        End Select
    End Function

    Private Function ChiusoFinoA(l As Luogo, prefisso As String) As Double
        Return _m.Partita.Contatore(prefisso & l.Id)
    End Function

    ''' <summary>Svuota la cassa del luogo in cui si è entrati. Restituisce cosa dire.</summary>
    Public Function SvuotaCassa(l As Luogo) As String
        Try
            If _m.Partita.Proprieta.Contains(l.Id) Then Return "È roba tua: la cassa te la tieni com'è."
            If _m.Partita.Giorno < ChiusoFinoA(l, "cassa_") Then Return Frase("cassa", "chiuso")
            Dim caso As Random = _m.Caso()
            Dim probabilita As Double = 0.6 - If(_m.Stelle > 0, 0.25, 0) - If(PoliziaVicina(), 0.15, 0) + If(_m.Notte, 0.1, 0) + Math.Min(0.1, _m.Partita.Contatore("casse") / 60.0)
            Dim quartiere As String = l.Quartiere.Id
            If caso.NextDouble() < probabilita Then
                Dim euro As Integer = If(l.Tipo = "distributore", 100 + caso.Next(0, 250), 80 + caso.Next(0, 320))
                _m.Partita.Guadagna(euro)
                _m.Partita.Conta("casse")
                _m.Partita.Conta("soldi_rubati", euro)
                _m.Partita.Contatori("cassa_" & l.Id) = _m.Partita.Giorno + 3
                _m.Partita.CambiaRispetto(quartiere, -1)
                _m.Suona(MotoreAudio.Soldi(), 0.6F, 0)
                If caso.NextDouble() < 0.25 Then _m.Ricerca(1, "Il gestore si accorge del buco in cassa e chiama la polizia!")
                Return Frase("cassa", "successo") & " Prendi " & euro.ToString() & " euro."
            End If
            _m.Partita.Contatori("cassa_" & l.Id) = _m.Partita.Giorno + 1
            _m.Partita.CambiaRispetto(quartiere, -2)
            _m.Ricerca(If(caso.NextDouble() < 0.5, 1, 2), "Scatta l'allarme!")
            Return Frase("cassa", "allarme")
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.SvuotaCassa", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ la banca
    ''' <summary>Un colpo furbo alla banca in cui si è entrati. Restituisce cosa dire.</summary>
    Public Function ColpoInBanca(l As Luogo) As String
        Try
            If _m.Partita.Giorno < ChiusoFinA(l) Then Return Frase("banca", "chiusa")
            Dim caso As Random = _m.Caso()
            Dim probabilita As Double = 0.3 - If(_m.Stelle > 0, 0.15, 0) + If(_m.Notte, 0.08, 0) + If(_m.Partita.QuantiOggetti("attrezzi") > 0, 0.12, 0) + Math.Min(0.1, _m.Partita.Contatore("colpi_banca") / 20.0)
            Dim quartiere As String = l.Quartiere.Id
            _m.Partita.Contatori("banca_" & l.Id) = _m.Partita.Giorno + 7
            If caso.NextDouble() < probabilita Then
                Dim euro As Integer = 1500 + caso.Next(0, 4500)
                _m.Partita.Guadagna(euro)
                _m.Partita.Conta("colpi_banca")
                _m.Partita.Conta("soldi_rubati", euro)
                _m.Partita.CambiaRispetto(quartiere, -3)
                _m.Suona(MotoreAudio.Soldi(), 0.8F, 0)
                If caso.NextDouble() < 0.4 Then _m.Ricerca(2, "Appena fuori scatta la caccia all'uomo!")
                Return Frase("banca", "successo") & " Hai portato via " & euro.ToString() & " euro."
            End If
            _m.Partita.CambiaRispetto(quartiere, -3)
            _m.Ricerca(3, "Suona l'allarme della banca!")
            Return Frase("banca", "fallito")
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.ColpoInBanca", ex)
            Throw
        End Try
    End Function

    Private Function ChiusoFinA(l As Luogo) As Double
        Return _m.Partita.Contatore("banca_" & l.Id)
    End Function

    ' ------------------------------------------------------------------ gli ordini e la rivendita
    ''' <summary>Le tre auto ordinate oggi (sono le stesse per tutto il giorno e per ogni partita).</summary>
    Public Function Ordini() As List(Of OrdineAuto)
        Try
            Dim giorno As Integer = _m.Partita.Giorno
            Dim caso As New Random(giorno * 7919 + _m.Partita.Posto * 31 + 17)
            Dim tipi As List(Of String) = _m.Dati.PrezziVeicoli.Keys.Where(Function(t) _m.Dati.Veicoli.ContainsKey(t)).OrderBy(Function(t) caso.Next()).Take(3).ToList()
            Dim elenco As New List(Of OrdineAuto)()
            For Each t As String In tipi
                Dim o As New OrdineAuto()
                o.Tipo = t
                o.Prezzo = CInt(Math.Round(_m.Dati.PrezziVeicoli(t) * (0.3 + caso.NextDouble() * 0.25) / 10)) * 10
                o.Consegnato = _m.Partita.Contatore("ordine_" & giorno.ToString() & "_" & t) > 0
                elenco.Add(o)
            Next
            Return elenco
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.Ordini", ex)
            Throw
        End Try
    End Function

    ''' <summary>Descrive un ordine con la frase di Gemini.</summary>
    Public Function DescriviOrdine(o As OrdineAuto) As String
        Try
            Dim nome As String = _m.Dati.Veicoli(o.Tipo).Nome
            Dim testo As String = Frase("rivendita", "ordine")
            If testo = "" Then testo = "Cerco " & nome & ", la pago " & o.Prezzo.ToString() & " euro."
            Return testo.Replace("{auto}", nome).Replace("{prezzo}", o.Prezzo.ToString()) & If(o.Consegnato, " (già consegnata)", "")
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.DescriviOrdine", ex)
            Throw
        End Try
    End Function

    ''' <summary>Consegna al committente il veicolo su cui si è (rubato, non tuo) se è quello ordinato. Restituisce cosa dire.</summary>
    Public Function ConsegnaOrdine(o As OrdineAuto) As String
        Try
            Dim v As Veicolo = _m.Veicolo
            If v Is Nothing OrElse _m.APiedi Then Return "Per consegnare un'auto devi esserci sopra."
            If o.Consegnato Then Return "Questa l'hanno già avuta."
            If v.Tuo Then Return "Questa è tua: vendila al concessionario, se vuoi."
            If v.Tipo.Id <> o.Tipo Then Return "Non è quella che serve: cercano " & _m.Dati.Veicoli(o.Tipo).Nome & "."
            Dim stato As Double = Math.Max(0, v.Salute) / v.Tipo.Robustezza
            If stato < 0.4 Then Return "È ridotta troppo male: così non la vuole nessuno."
            Dim euro As Integer = CInt(Math.Round(o.Prezzo * (0.6 + 0.4 * stato) / 10)) * 10
            If EconomiaCitta.Caricate(v) > 0 Then Return "Prima scarica la merce."
            _m.Scendi()
            _m.TogliVeicolo(v)
            _m.Partita.Guadagna(euro)
            _m.Partita.Conta("ordini")
            _m.Partita.Contatori("ordine_" & _m.Partita.Giorno.ToString() & "_" & o.Tipo) = 1
            _m.Partita.CambiaRispetto(_m.Dati.QuartiereDi(_m.X, _m.Y).Id, 2)
            _m.Suona(MotoreAudio.Soldi(), 0.8F, 0)
            Dim testo As String = Frase("rivendita", "consegnata")
            Return testo & " Ti pagano " & euro.ToString() & " euro."
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.ConsegnaOrdine", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le macchine tue che si possono vendere (non quella su cui si è, se ha merce a bordo).</summary>
    Public Function MacchineVendibili() As List(Of Veicolo)
        Try
            Return _m.Veicoli.Where(Function(v) v.Tuo AndAlso v.Ruolo <> RuoloVeicolo.Relitto AndAlso EconomiaCitta.Caricate(v) = 0).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.MacchineVendibili", ex)
            Throw
        End Try
    End Function

    ''' <summary>Quanto dà il concessionario per una macchina tua.</summary>
    Public Function ValoreTua(v As Veicolo) As Integer
        Try
            Dim listino As Integer
            If Not _m.Dati.PrezziVeicoli.TryGetValue(v.Tipo.Id, listino) Then listino = 3000
            Dim stato As Double = Math.Max(0.2, Math.Max(0, v.Salute) / _m.Robustezza(v))
            Dim migliorie As Integer = 0
            Dim s As HashSet(Of String) = Nothing
            If _m.Partita.Migliorie.TryGetValue(v.Tipo.Id, s) Then migliorie = s.Count
            Return CInt(Math.Round(listino * (0.5 + 0.03 * migliorie) * stato / 10)) * 10
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.ValoreTua", ex)
            Throw
        End Try
    End Function

    ''' <summary>Vende al concessionario una macchina tua. Restituisce cosa dire.</summary>
    Public Function VendiTua(v As Veicolo) As String
        Try
            Dim euro As Integer = ValoreTua(v)
            If v Is _m.Veicolo Then _m.Scendi()
            _m.TogliVeicolo(v)
            _m.Partita.Posseduti.Remove(v.Tipo.Id)
            _m.Partita.Guadagna(euro)
            _m.Partita.Conta("vendute")
            _m.Suona(MotoreAudio.Soldi(), 0.8F, 0)
            Dim testo As String = Frase("rivendita", "offerta").Replace("{auto}", v.Tipo.Nome).Replace("{prezzo}", euro.ToString())
            If testo = "" Then testo = "Per " & If(v.Tipo.Maschile, "il ", "la ") & v.Tipo.Nome & " ti danno " & euro.ToString() & " euro."
            Return testo
        Catch ex As Exception
            RegistroErrori.Registra("FurtiCitta.VendiTua", ex)
            Throw
        End Try
    End Function
End Class
