Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks

' ============================================================================================
' La radio in macchina (tasto R: accende, cambia stazione, spegne). Cinque stazioni di Porto Franco, ognuna con il suo genere e il suo
' conduttore. Trasmettono canzoni vere (brani di Kevin MacLeod, licenza Creative Commons Attribuzione: vedi musica\LICENZA.txt): prima di
' ogni pezzo il conduttore ne dice titolo e autore, tra un pezzo e l'altro chiacchiera, fa pubblicità finte e dà le notizie su quello
' che succede in città (un inseguimento, un arresto, un lavoro misterioso, la pioggia, la notte), anche sopra la canzone abbassata.
' Tutti i testi li ha scritti Gemini. La voce del conduttore è una voce di Windows (uomo o donna, in italiano se c'è), registrata in
' memoria e suonata nel mixer del gioco. Se per un genere non ci sono canzoni si usa la musica fatta al momento (VoceMusica).
' ============================================================================================

Public NotInheritable Class RadioCitta
    Private ReadOnly _m As Mondo
    ''' <summary>La stazione accesa (-1: spenta).</summary>
    Public Stazione As Integer = -1
    Private _musica As VoceMusica
    Private _canzone As VoceRegistrata
    Private _brano As Brano
    Private _pronta As Short()
    Private _branoPronto As Brano
    Private _caricando As Boolean
    Private _voce As VoceCampioni
    Private _inPreparazione As Boolean
    ''' <summary>Cosa viene dopo: parla, annuncio, canzone.</summary>
    Private _prossima As String = "annuncio"
    Private _attesa As Double
    Private _inizioCanzone As Double
    Private _prossimoParlato As Double
    Private ReadOnly _notizie As New Queue(Of String)()
    Private ReadOnly _ultimi As New List(Of String)()
    Private Const VOLUME_CANZONE As Single = 0.7F

    Public Sub New(m As Mondo)
        Try
            _m = m
            AddHandler _m.Notizia, AddressOf Notizia
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.New", ex)
            Throw
        End Try
    End Sub

    Private ReadOnly Property Info As RadioInfo
        Get
            If Stazione < 0 OrElse Stazione >= _m.Dati.Radio.Count Then Return Nothing
            Return _m.Dati.Radio(Stazione)
        End Get
    End Property

    ''' <summary>La canzone che sta suonando (Nothing se nessuna).</summary>
    Public ReadOnly Property BranoInOnda As Brano
        Get
            Return If(_canzone IsNot Nothing, _brano, Nothing)
        End Get
    End Property

    ''' <summary>R: la prossima stazione, e dopo l'ultima si spegne.</summary>
    Public Sub Cambia()
        Try
            If _m.APiedi Then
                _m.Dici("La radio c'è solo in macchina.", True)
                Return
            End If
            Dim nuova As Integer = Stazione + 1
            Ferma()
            If nuova >= _m.Dati.Radio.Count Then
                Stazione = -1
                _m.Dici("Radio spenta.", True)
                Return
            End If
            Stazione = nuova
            Dim r As RadioInfo = Info
            If Not Canzoni.Elenco().Any(Function(b) b.Stile = r.Musica) Then
                ' niente canzoni per questo genere: musica fatta al momento
                _musica = New VoceMusica(r.Musica) With {.Volume = 0.55F}
                If _m.Audio IsNot Nothing Then _m.Audio.Aggiungi(_musica)
                _prossimoParlato = _m.Tempo + 8
            End If
            _prossima = "annuncio"
            _attesa = _m.Tempo + 2.5
            _m.Dici(r.Nome & ", con " & r.Conduttore & ".", True)
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Cambia", ex)
            Throw
        End Try
    End Sub

    Private Sub Ferma()
        Try
            If _m.Audio IsNot Nothing Then
                If _musica IsNot Nothing Then _m.Audio.Togli(_musica)
                If _canzone IsNot Nothing Then _m.Audio.Togli(_canzone)
                If _voce IsNot Nothing Then _m.Audio.Togli(_voce)
            End If
            _musica = Nothing
            _canzone = Nothing
            _voce = Nothing
            _pronta = Nothing
            _branoPronto = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Ferma", ex)
            Throw
        End Try
    End Sub

    Public Sub Spegni()
        Try
            Stazione = -1
            Ferma()
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Spegni", ex)
            Throw
        End Try
    End Sub

    Private Sub Notizia(tipo As String, titolo As String, quartiere As String)
        Try
            Dim r As RadioInfo = If(Info, If(_m.Dati.Radio.Count > 0, _m.Dati.Radio(0), Nothing))
            If r Is Nothing Then Return
            Dim elenco As List(Of String) = Nothing
            If Not r.Notizie.TryGetValue(tipo, elenco) OrElse elenco.Count = 0 Then Return
            _notizie.Enqueue(Riempi(elenco(_m.Caso().Next(elenco.Count)), titolo, quartiere))
            While _notizie.Count > 3
                _notizie.Dequeue()
            End While
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Notizia", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Sostituisce i segnaposto: {quartiere}, {luogo}, {ora}, {titolo}.</summary>
    Private Function Riempi(testo As String, titolo As String, quartiere As String) As String
        Try
            Dim luoghi As List(Of Luogo) = _m.Dati.Luoghi.Values.ToList()
            Dim ore As String() = {"mezzanotte", "l'una", "le due", "le tre", "le quattro", "le cinque", "le sei", "le sette", "le otto", "le nove", "le dieci", "le undici", "mezzogiorno",
                                   "l'una", "le due", "le tre", "le quattro", "le cinque", "le sei", "le sette", "le otto", "le nove", "le dieci", "le undici"}
            Dim ora As String = ore(CInt(Math.Floor(_m.Partita.Ora)) Mod 24)
            Return testo.Replace("{quartiere}", If(quartiere = "", _m.Dati.QuartiereDi(_m.X, _m.Y).Nome, quartiere)).
                         Replace("{luogo}", luoghi(_m.Caso().Next(luoghi.Count)).Nome).
                         Replace("l'{ora}", ora).Replace("le {ora}", ora).Replace("{ora}", ora).
                         Replace("{titolo}", If(titolo = "", "un lavoretto", titolo))
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Riempi", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una chiacchiera del conduttore: una notizia se ce n'è, se no una battuta o una pubblicità.</summary>
    Private Function Chiacchiera(r As RadioInfo) As String
        Try
            If _notizie.Count > 0 Then Return _notizie.Dequeue()
            Dim elenco As List(Of String) = If(_m.Caso().NextDouble() < 0.7, r.Battute, r.Pubblicita)
            If elenco.Count = 0 Then elenco = r.Battute
            If elenco.Count = 0 Then Return ""
            Return Riempi(elenco(_m.Caso().Next(elenco.Count)), "", "")
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Chiacchiera", ex)
            Throw
        End Try
    End Function

    Public Sub Passo(dt As Double)
        Try
            If Stazione < 0 Then Return
            If _m.APiedi Then
                ' scendendo dalla macchina la radio si spegne
                Spegni()
                Return
            End If
            Dim r As RadioInfo = Info
            If _voce IsNot Nothing AndAlso _voce.FinitaOra Then
                _voce = Nothing
                If _musica IsNot Nothing Then _musica.Abbassa = False
                If _canzone IsNot Nothing Then _canzone.Volume = VOLUME_CANZONE
            End If
            ' senza canzoni: la musica fatta al momento, con il conduttore che ci parla sopra ogni tanto
            If _musica IsNot Nothing Then
                If _m.Tempo >= _prossimoParlato AndAlso _voce Is Nothing AndAlso Not _inPreparazione Then
                    _prossimoParlato = _m.Tempo + 45 + _m.Caso().NextDouble() * 45
                    If _m.Caso().NextDouble() < 0.3 Then _musica.NuovoBrano()
                    Parla(Chiacchiera(r), r.VoceDonna)
                End If
                Return
            End If
            If _canzone IsNot Nothing Then
                If _canzone.FinitaOra Then
                    _canzone = Nothing
                    _prossima = "parla"
                    _attesa = _m.Tempo + 0.5
                ElseIf _notizie.Count > 0 AndAlso _voce Is Nothing AndAlso Not _inPreparazione AndAlso _m.Tempo - _inizioCanzone > 12 AndAlso _canzone.Durata - (_m.Tempo - _inizioCanzone) > 20 Then
                    ' una notizia fresca: il conduttore la dà sopra la canzone
                    Parla(_notizie.Dequeue(), r.VoceDonna)
                End If
                Return
            End If
            If _voce IsNot Nothing OrElse _inPreparazione OrElse _m.Tempo < _attesa Then Return
            Select Case _prossima
                Case "parla"
                    _prossima = "annuncio"
                    Parla(Chiacchiera(r), r.VoceDonna)
                Case "annuncio"
                    Dim adatti As List(Of Brano) = Canzoni.Elenco().Where(Function(b) b.Stile = r.Musica AndAlso Not _ultimi.Contains(b.File)).ToList()
                    If adatti.Count = 0 Then
                        _ultimi.Clear()
                        adatti = Canzoni.Elenco().Where(Function(b) b.Stile = r.Musica).ToList()
                    End If
                    If adatti.Count = 0 Then Return
                    Dim b2 As Brano = adatti(_m.Caso().Next(adatti.Count))
                    _ultimi.Add(b2.File)
                    If _ultimi.Count > 8 Then _ultimi.RemoveAt(0)
                    Dim frase As String = If(r.Annunci.Count > 0, r.Annunci(_m.Caso().Next(r.Annunci.Count)), "{canzone}, di {autore}.")
                    _prossima = "canzone"
                    _pronta = Nothing
                    _branoPronto = b2
                    _caricando = True
                    Dim stazione As Integer = Me.Stazione
                    Task.Run(Function() Canzoni.Carica(b2)).ContinueWith(Sub(t)
                                                                             _caricando = False
                                                                             If Not t.IsFaulted AndAlso Me.Stazione = stazione AndAlso _branoPronto Is b2 Then _pronta = t.Result
                                                                         End Sub)
                    Parla(frase.Replace("{canzone}", b2.Titolo).Replace("{autore}", b2.Autore), r.VoceDonna)
                Case "canzone"
                    If _caricando Then Return
                    If _pronta Is Nothing OrElse _m.Audio Is Nothing Then
                        _prossima = "parla"
                        _attesa = _m.Tempo + 4
                        Return
                    End If
                    _brano = _branoPronto
                    _canzone = New VoceRegistrata(_pronta) With {.Spaziale = False, .Gruppo = GruppoAudio.Radio, .Volume = VOLUME_CANZONE}
                    _pronta = Nothing
                    _inizioCanzone = _m.Tempo
                    _m.Audio.Aggiungi(_canzone)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Passo", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Il conduttore dice un testo: si sintetizza in sottofondo e poi si suona (sopra la musica abbassata, se c'è).</summary>
    Private Sub Parla(testo As String, donna As Boolean)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            _inPreparazione = True
            Dim stazione As Integer = Me.Stazione
            Task.Run(Function() VociCitta.Sintetizza(testo, donna, 1)).ContinueWith(Sub(t)
                                                                                         Try
                                                                                             _inPreparazione = False
                                                                                             If t.IsFaulted OrElse t.Result Is Nothing OrElse Me.Stazione <> stazione OrElse _m.Audio Is Nothing Then Return
                                                                                             _voce = New VoceCampioni(t.Result) With {.Spaziale = False, .Gruppo = GruppoAudio.Radio, .Volume = 1}
                                                                                             If _musica IsNot Nothing Then _musica.Abbassa = True
                                                                                             If _canzone IsNot Nothing Then _canzone.Volume = 0.15F
                                                                                             _m.Audio.Aggiungi(_voce)
                                                                                             _m.Senti(If(Info IsNot Nothing, Info.Conduttore & ", alla radio: ", "") & testo)
                                                                                         Catch ex As Exception
                                                                                             RegistroErrori.Registra("RadioCitta.Parla (voce pronta)", ex)
                                                                                         End Try
                                                                                     End Sub)
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Parla", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Per lo schermo: cosa si ascolta.</summary>
    Public Function Descrizione() As String
        Try
            Dim r As RadioInfo = Info
            If r Is Nothing Then Return ""
            Dim b As Brano = BranoInOnda
            Return r.Nome & If(b Is Nothing, "", ": " & b.Titolo & ", " & b.Autore)
        Catch ex As Exception
            RegistroErrori.Registra("RadioCitta.Descrizione", ex)
            Throw
        End Try
    End Function
End Class
