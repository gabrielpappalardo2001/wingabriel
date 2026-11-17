Imports System
Imports System.Collections.Generic
Imports System.Linq

' ============================================================================================
' L'inseguimento in barca (passo "barca" delle missioni): dal molo si parte su un motoscafo dietro a chi scappa, prima lungo il canale e
' poi dentro il porto, fino al mare aperto. Il percorso è una striscia d'acqua con tre corsie (sinistra, centro, destra): con le frecce
' sinistra e destra si cambia corsia, con su si accelera, con giù si rallenta.
' Davanti ci sono barche ormeggiate, boe e chiatte: ognuna fa un bip dalla sua corsia, sempre più fitto man mano che si avvicina, e va
' schivata. Il motoscafo inseguito si sente dalla sua parte, con il suo motore; per prenderlo bisogna affiancarlo nella sua stessa corsia.
' Se arriva al mare aperto o il tuo motoscafo va a pezzi, è finita.
' ============================================================================================

Public NotInheritable Class InseguimentoBarca
    Private ReadOnly _m As Mondo
    Private Class Ostacolo
        Public Pos As Double
        Public Corsia As Integer
        Public Nome As String = ""
        Public ProssimoBip As Double
    End Class

    Private ReadOnly _ostacoli As New List(Of Ostacolo)()
    Private _pos As Double
    Private _vel As Double
    Private _corsia As Integer
    Private _salute As Double = 100
    Private _preda As Double
    Private _velPreda As Double
    Private _corsiaPreda As Integer
    Private _prossimoCambioPreda As Double
    Private _affiancato As Double
    Private _annunciatoPorto As Boolean
    Private _ultimoAvviso As Double
    Private ReadOnly _motore As New VoceMotore() With {.Volume = 0.45F, .Spaziale = False}
    Private ReadOnly _motorePreda As New VoceMotore() With {.Volume = 0.4F}
    Private ReadOnly _onde As New VoceRegistrata("fondo_onde", True) With {.Volume = 0.35F, .Spaziale = False}
    ''' <summary>Vero: preso; falso: scappato o barca distrutta. Nothing finché si insegue.</summary>
    Public Esito As Boolean?

    Private Const LUNGHEZZA As Double = 1400
    Private Const CANALE As Double = 650
    Private Const VELOCITA_MAX As Double = 19

    Public Sub New(m As Mondo)
        Try
            _m = m
            Dim caso As Random = m.Caso()
            Dim p As Double = 90
            Dim nomi As String() = {"una barca ormeggiata", "una boa", "una chiatta", "un gozzo di pescatori", "una fila di boe"}
            While p < LUNGHEZZA - 60
                _ostacoli.Add(New Ostacolo With {.Pos = p, .Corsia = caso.Next(-1, 2), .Nome = nomi(caso.Next(nomi.Length))})
                ' a volte due ostacoli affiancati: resta una corsia sola
                If caso.NextDouble() < 0.3 Then
                    Dim altra As Integer = caso.Next(-1, 2)
                    If altra <> _ostacoli.Last().Corsia Then _ostacoli.Add(New Ostacolo With {.Pos = p, .Corsia = altra, .Nome = nomi(caso.Next(nomi.Length))})
                End If
                p += 55 + caso.NextDouble() * 55
            End While
            _preda = 70
            _velPreda = 13
            _corsiaPreda = caso.Next(-1, 2)
            _motore.Registrazione = "motore_barca"
            _motorePreda.Registrazione = "motore_barca"
            _motorePreda.Timbro = 1.3F
            If m.Audio IsNot Nothing Then
                m.Audio.Aggiungi(_motore)
                m.Audio.Aggiungi(_motorePreda)
                If _onde.Presente Then m.Audio.Aggiungi(_onde)
            End If
            m.Dici("Salti sul motoscafo e parti! Lo senti davanti, nel canale. Frecce a destra e a sinistra per cambiare corsia, su per dare gas, giù per rallentare. Schiva quello che senti davanti e affiancalo nella sua corsia.", True)
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.New", ex)
            Throw
        End Try
    End Sub

    Private Shared Function NomeCorsia(c As Integer) As String
        Try
            Return If(c < 0, "a sinistra", If(c > 0, "a destra", "al centro"))
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.NomeCorsia", ex)
            Throw
        End Try
    End Function

    ''' <summary>Da dove si sente qualcosa che sta davanti (o dietro) di "avanti" metri e di "lato" corsie (8 metri l'una).</summary>
    Private Shared Function Azimut(avanti As Double, lato As Integer) As Single
        Try
            Return CSng(Math.Atan2(-lato * 8, avanti))
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.Azimut", ex)
            Throw
        End Try
    End Function

    Public Sub CambiaCorsia(verso As Integer)
        Try
            Dim nuova As Integer = Math.Max(-1, Math.Min(1, _corsia + verso))
            If nuova = _corsia Then
                _m.Suona(MotoreAudio.Urto(False), 0.3F, If(verso < 0, 1.4F, -1.4F))
                _m.Dici("Più in là c'è la banchina.", True)
                Return
            End If
            _corsia = nuova
            _m.Suona(MotoreAudio.Tic(), 0.4F, If(verso < 0, 1.0F, -1.0F))
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.CambiaCorsia", ex)
            Throw
        End Try
    End Sub

    Public Sub Passo(dt As Double, c As Comandi)
        Try
            If Esito.HasValue Then Return
            Dim caso As Random = _m.Caso()
            ' il tuo motoscafo
            If c.Su Then
                _vel = Math.Min(VELOCITA_MAX * (0.6 + 0.4 * _salute / 100), _vel + 4.5 * dt)
            ElseIf c.Giu Then
                _vel = Math.Max(3, _vel - 8 * dt)
            Else
                _vel = Math.Max(4, _vel - 1.2 * dt)
            End If
            _pos += _vel * dt
            ' chi scappa: accelera quando ti avvicini, cambia corsia ogni tanto e schiva gli ostacoli
            Dim distacco As Double = _preda - _pos
            _velPreda = If(distacco < 25, 16.5, If(distacco > 120, 11.5, 14))
            _preda += _velPreda * dt
            If _m.Tempo >= _prossimoCambioPreda Then
                _prossimoCambioPreda = _m.Tempo + 4 + caso.NextDouble() * 5
                Dim nuova As Integer = caso.Next(-1, 2)
                If nuova <> _corsiaPreda Then
                    _corsiaPreda = nuova
                    If distacco < 80 Then _m.Dici("Si sposta " & NomeCorsia(_corsiaPreda) & "!", True)
                End If
            End If
            For Each o As Ostacolo In _ostacoli
                If o.Corsia = _corsiaPreda AndAlso o.Pos - _preda > 0 AndAlso o.Pos - _preda < 15 Then
                    Dim libere As List(Of Integer) = New Integer() {-1, 0, 1}.Where(Function(k) Not _ostacoli.Any(Function(x) x.Corsia = k AndAlso Math.Abs(x.Pos - o.Pos) < 20)).ToList()
                    If libere.Count > 0 Then _corsiaPreda = libere(caso.Next(libere.Count))
                End If
            Next
            If Not _annunciatoPorto AndAlso _pos > CANALE Then
                _annunciatoPorto = True
                _m.Dici("Esci dal canale: sei nel porto, tra le navi. Il mare aperto è vicino!", True)
            End If
            ' gli ostacoli: bip dalla loro corsia, urto se ci passi sopra
            For Each o As Ostacolo In _ostacoli.ToList()
                Dim avanti As Double = o.Pos - _pos
                If avanti < -5 Then
                    _ostacoli.Remove(o)
                    Continue For
                End If
                If avanti < 70 AndAlso _m.Tempo >= o.ProssimoBip Then
                    o.ProssimoBip = _m.Tempo + Math.Max(0.15, avanti / 60)
                    Dim f As Double = If(o.Corsia = _corsia, 1250, 700)
                    _m.Suona(MotoreAudio.BipRadar(f), CSng(Math.Min(0.75, 0.25 + 10 / Math.Max(5, avanti))), Azimut(Math.Max(1, avanti), o.Corsia - _corsia))
                End If
                If o.Corsia = _corsia AndAlso Math.Abs(avanti) < 3 Then
                    _ostacoli.Remove(o)
                    Dim danno As Double = 8 + _vel * 1.6
                    _salute -= danno
                    _vel *= 0.35
                    _m.Suona(MotoreAudio.Urto(True), 0.8F, 0)
                    If _salute <= 0 Then
                        Fine(False, "Prendi in pieno " & o.Nome & ": il motoscafo imbarca acqua e si ferma. Ti è scappato.")
                        Return
                    End If
                    _m.Dici("Sbatti contro " & o.Nome & "! " & If(_salute > 60, "Il motoscafo regge.", If(_salute > 30, "Il motoscafo è ammaccato.", "Il motoscafo sta per affondare.")), True)
                End If
            Next
            ' la preda: si sente dalla sua parte
            distacco = _preda - _pos
            _motore.Giri = CSng(_vel / VELOCITA_MAX)
            _motorePreda.Giri = CSng(_velPreda / VELOCITA_MAX)
            _motorePreda.Azimut = Azimut(distacco, _corsiaPreda - _corsia)
            _motorePreda.Volume = CSng(Math.Min(0.6, 0.12 + 20 / Math.Max(10, Math.Abs(distacco))))
            If distacco < 8 AndAlso distacco > -6 AndAlso _corsiaPreda = _corsia Then
                _affiancato += dt
                If _affiancato > 1.2 Then
                    Fine(True, "Lo affianchi, salti sulla sua barca e spegni il motore. Preso!")
                    Return
                End If
            Else
                _affiancato = 0
                If distacco < 10 AndAlso distacco > -6 AndAlso _m.Tempo - _ultimoAvviso > 2.5 Then
                    _ultimoAvviso = _m.Tempo
                    _m.Dici("Ci sei quasi: è " & NomeCorsia(_corsiaPreda) & ".", True)
                End If
            End If
            If distacco < -25 Then _preda = _pos + 15
            If _preda >= LUNGHEZZA Then Fine(False, "Arriva al mare aperto e sparisce all'orizzonte. Ti è scappato.")
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.Passo", ex)
            Throw
        End Try
    End Sub

    Private Sub Fine(preso As Boolean, testo As String)
        Try
            Esito = preso
            Spegni()
            _m.Suona(If(preso, MotoreAudio.Successo(), MotoreAudio.Fallimento()), 0.8F)
            _m.Dici(testo, True)
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.Fine", ex)
            Throw
        End Try
    End Sub

    Public Sub Spegni()
        Try
            If _m.Audio Is Nothing Then Return
            _m.Audio.Togli(_motore)
            _m.Audio.Togli(_motorePreda)
            _m.Audio.Togli(_onde)
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.Spegni", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Per lo schermo e per il tasto della missione: dove sei e dov'è chi scappa.</summary>
    Public Function Descrizione() As String
        Try
            Dim distacco As Double = _preda - _pos
            Return "In barca, " & If(_pos < CANALE, "nel canale", "nel porto") & ", corsia " & NomeCorsia(_corsia).Replace("a ", "").Replace("al ", "") & ", " & CInt(_vel * 3.6).ToString() & " all'ora. " &
                   "Il motoscafo da prendere è " & If(distacco >= 0, Mappa.Metri(distacco) & " davanti", Mappa.Metri(-distacco) & " dietro") & ", " & NomeCorsia(_corsiaPreda) & ". " &
                   "Al mare aperto mancano " & Mappa.Metri(Math.Max(0, LUNGHEZZA - _preda)) & "."
        Catch ex As Exception
            RegistroErrori.Registra("InseguimentoBarca.Descrizione", ex)
            Throw
        End Try
    End Function
End Class
