Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json.Linq

' ============================================================================================
' Le cose che capitano per strada, ogni tanto, quando non si è in missione né al lavoro e la polizia non ti cerca:
'   autostop    qualcuno chiede un passaggio: fermati vicino e portalo dove deve andare;
'   portafoglio senti delle monete per terra, poco più avanti: passaci sopra a piedi (o fermati lì in macchina);
'   scippo      un ladro scappa in macchina con la borsa di qualcuno: speronalo per fermarlo;
'   blocco      un posto di blocco sulla strada: se guidi un'auto rubata possono riconoscerla.
' Le battute le ha scritte Gemini (testi.json, sezione eventi).
' ============================================================================================

Public NotInheritable Class EventiCitta
    Private ReadOnly _m As Mondo
    Private _prossimo As Double = 90
    ''' <summary>L'evento in corso: "", autostop, portafoglio, scippo, blocco.</summary>
    Public Tipo As String = ""
    Private _fine As Double
    Private _punto As Luogo
    Private _meta As Luogo
    Private _testi As JObject
    Private _ladro As Veicolo
    Private _blocco As Veicolo
    Private _bloccoParlato As Boolean
    Private _prossimoSuono As Double
    Private _aBordo As Boolean
    Private _accettato As Boolean
    ' la domanda in attesa di risposta (il menu la propone, anche dal menu Alt se lo si chiude)
    Private _inAttesa As Boolean
    Private _titolo As String = ""
    Private _opzioni As New List(Of String)()
    Private _risposta As Action(Of Integer)

    ''' <summary>Un evento che chiede cosa fare (un passante che vuole un passaggio, un posto di blocco): il menu apre la domanda e poi chiama Rispondi.</summary>
    Public Event DomandaPosta(titolo As String, opzioni As List(Of String))

    Public Sub New(m As Mondo)
        Try
            _m = m
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un punto della strada davanti a sé (un luogo che non sta nei dati), tra minimo e massimo metri.</summary>
    Private Function PuntoDavanti(minimo As Double, massimo As Double) As Luogo
        Try
            Dim p As Posizione = _m.Pos
            Dim t As Tratto = p.Tratto
            Dim d As Double = p.Distanza + p.Verso * (minimo + _m.Caso().NextDouble() * (massimo - minimo))
            If d < 2 OrElse d > t.Lunghezza - 2 Then
                ' non ci sta su questa strada: sulla prossima
                Dim uscite As List(Of Tratto) = p.Prossimo.Tratti.Where(Function(x) x IsNot t).ToList()
                If uscite.Count = 0 Then Return Nothing
                t = uscite(_m.Caso().Next(uscite.Count))
                d = If(t.A Is p.Prossimo, Math.Min(t.Lunghezza - 3, 25), Math.Max(3, t.Lunghezza - 25))
            End If
            Dim l As New Luogo With {.Id = "evento", .Tipo = "evento", .Tratto = t, .Distanza = d, .Quartiere = _m.Dati.QuartiereDi(t.A.X, t.A.Y), .Lato = "destra"}
            t.Punto(d, l.X, l.Y)
            Return l
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.PuntoDavanti", ex)
            Throw
        End Try
    End Function

    Private Function Testo(chiave As String) As String
        Try
            If _testi Is Nothing OrElse _testi(chiave) Is Nothing Then Return ""
            Return _testi(chiave).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.Testo", ex)
            Throw
        End Try
    End Function

    Public Sub Passo(dt As Double)
        Try
            If Tipo = "" Then
                If _m.Tempo < _prossimo Then Return
                _prossimo = _m.Tempo + 70 + _m.Caso().NextDouble() * 110
                If _m.Missioni.Attiva IsNot Nothing OrElse _m.Lavori.Attivo OrElse _m.Stelle > 0 OrElse _m.InGalleria Then Return
                Comincia()
                Return
            End If
            Select Case Tipo
                Case "autostop" : PassoAutostop()
                Case "portafoglio" : PassoPortafoglio()
                Case "scippo" : PassoScippo()
                Case "blocco" : PassoBlocco()
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.Passo", ex)
            Throw
        End Try
    End Sub

    Private Sub Comincia()
        Try
            Dim r As Double = _m.Caso().NextDouble()
            If _m.APiedi Then
                If r < 0.6 Then ComincioPortafoglio()
                Return
            End If
            If r < 0.4 Then
                ComincioAutostop()
            ElseIf r < 0.6 Then
                ComincioPortafoglio()
            ElseIf r < 0.85 Then
                ComincioScippo()
            Else
                ComincioBlocco()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.Comincia", ex)
            Throw
        End Try
    End Sub

    Private Sub Fine()
        Try
            If _ladro IsNot Nothing Then _m.TogliVeicolo(_ladro)
            If _blocco IsNot Nothing Then _m.TogliVeicolo(_blocco)
            _ladro = Nothing
            _blocco = Nothing
            If _meta IsNot Nothing AndAlso _m.Meta Is _meta Then _m.ImpostaMeta(Nothing, True)
            If _punto IsNot Nothing AndAlso _m.Meta Is _punto Then _m.ImpostaMeta(Nothing, True)
            _meta = Nothing
            _punto = Nothing
            _aBordo = False
            _accettato = False
            _inAttesa = False
            _risposta = Nothing
            Tipo = ""
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.Fine", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ autostop
    Private Sub ComincioAutostop()
        Try
            _punto = PuntoDavanti(60, 110)
            If _punto Is Nothing Then Return
            _testi = _m.Dati.OggettoACaso("eventi", "autostop", _m.Caso())
            Tipo = "autostop"
            _fine = _m.Tempo + 75
            _punto.Nome = "il passante"
            If _m.Meta Is Nothing Then _m.ImpostaMeta(_punto, True)
            _m.Dici("Più avanti, " & _m.LatoA(_punto.X, _punto.Y) & ", qualcuno fa segno di fermarti. " & Testo("richiesta"))
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.ComincioAutostop", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoAutostop()
        Try
            If Not _aBordo Then
                If _m.Tempo > _fine OrElse _m.APiedi Then
                    Fine()
                    Return
                End If
                If Not _inAttesa AndAlso Not _accettato AndAlso _m.DistanzaDa(_punto.X, _punto.Y) < 30 AndAlso Math.Abs(_m.Velocita) < 5 Then
                    Chiedi("Un passante chiede un passaggio. " & Testo("richiesta"), New List(Of String) From {"Fallo salire", "Riparti senza di lui"}, AddressOf RispostaAutostop)
                End If
                If _accettato Then
                    _accettato = False
                    _aBordo = True
                    _meta = _m.Dati.Luoghi.Values.Where(Function(l) l.Tipo <> "casa" AndAlso l.Tipo <> "rifugio" AndAlso _m.DistanzaDa(l.X, l.Y) > 400 AndAlso _m.DistanzaDa(l.X, l.Y) < 1300).OrderBy(Function(l) _m.Caso().Next()).FirstOrDefault()
                    If _meta Is Nothing Then
                        Fine()
                        Return
                    End If
                    _m.Suona(MotoreAudio.Portiera(), 0.7F)
                    _fine = _m.Tempo + 300
                    _m.ImpostaMeta(_meta, True)
                    _m.Dici("Sale a bordo: deve andare a " & _meta.Nome & ", " & _meta.Quartiere.Nome & ". Il navigatore ti porta lì.", True)
                End If
                Return
            End If
            If _m.APiedi OrElse _m.Tempo > _fine Then
                _m.Dici("Il passeggero scende e se ne va a piedi.")
                Fine()
                Return
            End If
            If _m.DistanzaDa(_meta.X, _meta.Y) < Mondo.DISTANZA_LUOGO AndAlso Math.Abs(_m.Velocita) < 3 Then
                Dim soldi As Integer = 30 + _m.Caso().Next(60)
                _m.Partita.Guadagna(soldi)
                _m.Partita.Conta("passaggi")
                _m.Suona(MotoreAudio.Soldi(), 0.6F)
                _m.Dici(Testo("grazie") & " Ti lascia " & soldi.ToString() & " euro.", True)
                Fine()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.PassoAutostop", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ portafoglio
    Private Sub ComincioPortafoglio()
        Try
            _punto = PuntoDavanti(25, 60)
            If _punto Is Nothing Then Return
            Tipo = "portafoglio"
            _fine = _m.Tempo + 60
            _prossimoSuono = 0
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.ComincioPortafoglio", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoPortafoglio()
        Try
            If _m.Tempo > _fine Then
                Fine()
                Return
            End If
            If _m.Tempo >= _prossimoSuono Then
                _prossimoSuono = _m.Tempo + 1.8
                _m.Suona(MotoreAudio.Monete(), CSng(Math.Min(0.6, 12 / Math.Max(10, _m.DistanzaDa(_punto.X, _punto.Y)))), _m.AzimutVerso(_punto.X, _punto.Y))
            End If
            Dim vicino As Double = If(_m.APiedi, 4, 8)
            If _m.DistanzaDa(_punto.X, _punto.Y) < vicino AndAlso (_m.APiedi OrElse Math.Abs(_m.Velocita) < 3) Then
                Dim soldi As Integer = 20 + _m.Caso().Next(130)
                _m.Partita.Soldi += soldi
                _m.Suona(MotoreAudio.Soldi(), 0.6F)
                _m.Dici(_m.Dati.TestoACaso("eventi", "portafoglio", _m.Caso()) & " Sono " & soldi.ToString() & " euro.", True)
                Fine()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.PassoPortafoglio", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ scippo
    Private Sub ComincioScippo()
        Try
            _punto = PuntoDavanti(50, 90)
            If _punto Is Nothing Then Return
            _testi = _m.Dati.OggettoACaso("eventi", "scippo", _m.Caso())
            Tipo = "scippo"
            _fine = _m.Tempo + 120
            _ladro = _m.CreaVeicolo("utilitaria", _punto, RuoloVeicolo.Bersaglio)
            _ladro.InFuga = True
            _m.Suona(MotoreAudio.Sgommata(), 0.7F, _m.AzimutVerso(_punto.X, _punto.Y))
            _m.Dici(Testo("grido") & " Il ladro grida: " & Testo("ladro") & " Scappa in macchina: speronalo!", True)
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.ComincioScippo", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoScippo()
        Try
            If _ladro Is Nothing Then
                Fine()
                Return
            End If
            If _m.Tempo > _fine OrElse _m.DistanzaDa(_ladro.X, _ladro.Y) > 450 Then
                _m.Dici("Il ladro è sparito tra i vicoli.")
                Fine()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.PassoScippo", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ posto di blocco
    Private Sub ComincioBlocco()
        Try
            _punto = PuntoDavanti(120, 200)
            If _punto Is Nothing OrElse _punto.Tratto.Galleria Then Return
            Tipo = "blocco"
            _fine = _m.Tempo + 80
            _bloccoParlato = False
            _blocco = _m.CreaVeicolo("polizia", _punto, RuoloVeicolo.Parcheggiato)
            _blocco.Blocco = True
            _m.Dici("Più avanti c'è un posto di blocco.")
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.ComincioBlocco", ex)
            Throw
        End Try
    End Sub

    Private Sub PassoBlocco()
        Try
            If _blocco Is Nothing OrElse _m.Tempo > _fine Then
                Fine()
                Return
            End If
            If Not _bloccoParlato AndAlso _m.DistanzaDa(_blocco.X, _blocco.Y) < 30 Then
                _bloccoParlato = True
                _m.Suona(MotoreAudio.Fischio(), 0.6F, _m.AzimutVerso(_blocco.X, _blocco.Y))
                Chiedi("Posto di blocco: un agente ti fa segno di fermarti.", New List(Of String) From {"Fermati al controllo", "Tira dritto"}, AddressOf RispostaBlocco)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.PassoBlocco", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ le domande
    Private Sub Chiedi(titolo As String, opzioni As List(Of String), risposta As Action(Of Integer))
        Try
            _inAttesa = True
            _titolo = titolo
            _opzioni = opzioni
            _risposta = risposta
            RaiseEvent DomandaPosta(titolo, opzioni)
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.Chiedi", ex)
            Throw
        End Try
    End Sub

    ''' <summary>C'è una domanda a cui non si è ancora risposto.</summary>
    Public ReadOnly Property DomandaAperta As Boolean
        Get
            Return _inAttesa AndAlso _risposta IsNot Nothing
        End Get
    End Property

    ''' <summary>Il nome della voce del menu Alt che riapre la domanda.</summary>
    Public ReadOnly Property EtichettaDomanda As String
        Get
            Return If(Tipo = "blocco", "Posto di blocco", "Passante")
        End Get
    End Property

    ''' <summary>Riapre la domanda (dal menu Alt, se si era chiuso il menu senza rispondere).</summary>
    Public Sub RipetiDomanda()
        Try
            If DomandaAperta Then RaiseEvent DomandaPosta(_titolo, _opzioni)
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.RipetiDomanda", ex)
            Throw
        End Try
    End Sub

    Public Sub Rispondi(k As Integer)
        Try
            If Not DomandaAperta Then Return
            Dim a As Action(Of Integer) = _risposta
            _inAttesa = False
            _risposta = Nothing
            a.Invoke(k)
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.Rispondi", ex)
            Throw
        End Try
    End Sub

    Private Sub RispostaAutostop(k As Integer)
        Try
            If Tipo <> "autostop" Then Return
            If k <> 0 OrElse _m.APiedi Then
                _m.Dici("Riparti senza di lui.", True)
                Fine()
                Return
            End If
            _accettato = True
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.RispostaAutostop", ex)
            Throw
        End Try
    End Sub

    Private Sub RispostaBlocco(k As Integer)
        Try
            If Tipo <> "blocco" Then Return
            If _m.APiedi Then
                Fine()
                Return
            End If
            If k = 0 Then
                _m.Veicolo.Velocita = 0
                If Not _m.Veicolo.Tuo AndAlso _m.Caso().NextDouble() < 0.35 Then
                    _m.Ricerca(1, "Al posto di blocco riconoscono la macchina rubata!")
                    Fine()
                Else
                    _m.Dici("L'agente: " & _m.Dati.TestoACaso("eventi", "blocco", _m.Caso()) & " Puoi ripartire.", True)
                End If
            Else
                _m.Ricerca(1, "Tiri dritto al posto di blocco!")
            End If
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.RispostaBlocco", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Vero se il luogo è la meta di un evento (il navigatore non la spegne arrivando).</summary>
    Public Function MetaDelloEvento(l As Luogo) As Boolean
        Try
            Return Tipo = "autostop" AndAlso l IsNot Nothing AndAlso (l Is _punto OrElse l Is _meta)
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.MetaDelloEvento", ex)
            Throw
        End Try
    End Function

    Public Sub Urto(v As Veicolo)
        Try
            If Tipo <> "scippo" OrElse v IsNot _ladro Then Return
            _ladro.InFuga = False
            _ladro.Ruolo = RuoloVeicolo.Parcheggiato
            _ladro.Velocita = 0
            _ladro = Nothing
            Dim premio As Integer = 100 + _m.Caso().Next(150)
            _m.Partita.Guadagna(premio)
            _m.Partita.Conta("scippatori")
            _m.Suona(MotoreAudio.Soldi(), 0.6F)
            _m.Dici("Il ladro si ferma e scappa a piedi lasciando la borsa. " & Testo("grazie") & " Ti danno " & premio.ToString() & " euro di ricompensa.", True)
            Fine()
        Catch ex As Exception
            RegistroErrori.Registra("EventiCitta.Urto", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Per la mappa: il ladro da fermare.</summary>
    Public ReadOnly Property Ladro As Veicolo
        Get
            Return _ladro
        End Get
    End Property
End Class
