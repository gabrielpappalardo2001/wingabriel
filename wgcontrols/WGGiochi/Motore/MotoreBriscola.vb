Imports Newtonsoft.Json.Linq

''' <summary>
''' Briscola per 2, 3, 4 o 5 giocatori, con le varianti: classica, al meglio delle tre, a chiamata (5), marianna con accusi, scoperta,
''' briscolino (3). Stesse regole di briscola.php.
''' </summary>
Friend Class MotoreBriscola
    Implements IMotoreGioco

    Public Shared ReadOnly VARIANTI As String() = {"classica", "al_meglio", "chiamata", "marianna", "scoperta", "briscolino"}
    Private Shared ReadOnly TAB_PUNTI As Integer() = {11, 0, 10, 0, 0, 0, 0, 2, 3, 4}
    Private Shared ReadOnly TAB_FORZA As Integer() = {10, 1, 9, 2, 3, 4, 5, 6, 7, 8}

    Private Class Giocata
        Public Posto As Integer
        Public Carta As Integer
    End Class

    Private Class UltimaPresaInfo
        Public Vincitore As Integer
        Public Carte As List(Of Integer)
        Public Punti As Integer
    End Class

    Private Class PunteggioSquadra
        Public Punti As Integer
        Public Carte As Integer
    End Class

    Private _variante As String
    Private _n As Integer
    Private _nomi As List(Of String)
    Private _smazzata As Integer = 1
    Private _totaliSmazzate As Integer()
    Private _totaliPunti As Integer()
    Private _obiettivoMarianna As Integer
    Private _finita As Boolean = False
    Private _vincitori As New List(Of Integer)()
    Private _punteggi As List(Of PunteggioSquadra) = Nothing
    Private _motivo As String = ""
    Private _turno As Integer
    Private _tavolo As New List(Of Giocata)()
    Private _ultimaPresa As UltimaPresaInfo = Nothing
    Private _prese As List(Of Integer)()
    Private _accusi As Integer()
    Private _accusiDichiarati As New List(Of Integer)()
    Private _chiHaPresoUltimo As Integer? = Nothing
    Private _mazzo As List(Of Integer)
    Private _mani As List(Of List(Of Integer))
    Private _briscola As Integer? = Nothing
    Private _faseChiamata As Boolean = False
    Private _chiamante As Integer = 0
    Private _cartaChiamata As Integer? = Nothing
    Private _socio As Integer? = Nothing
    Private _socioScoperto As Boolean = False

    Public ReadOnly Property Turno As Integer Implements IMotoreGioco.Turno
        Get
            Return _turno
        End Get
    End Property

    Public ReadOnly Property Finita As Boolean Implements IMotoreGioco.Finita
        Get
            Return _finita
        End Get
    End Property

    Public Shared Function Punti(id As Integer) As Integer
        Try
            Return TAB_PUNTI(id Mod 10)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Punti", ex)
            Throw
        End Try
    End Function

    Private Shared Function Forza(id As Integer) As Integer
        Try
            Return TAB_FORZA(id Mod 10)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Forza", ex)
            Throw
        End Try
    End Function

    Public Sub New(nomi As List(Of String), variante As String, puntiObiettivo As Integer)
        Try
            Dim n As Integer = nomi.Count
            If VARIANTI.Contains(variante) Then
                _variante = variante
            Else
                _variante = If(n = 5, "chiamata", If(n = 3, "briscolino", "classica"))
            End If
            If _variante = "chiamata" AndAlso n <> 5 Then
                n = 5
            ElseIf _variante = "briscolino" AndAlso n <> 3 Then
                n = 3
            ElseIf (_variante = "classica" OrElse _variante = "al_meglio" OrElse _variante = "marianna" OrElse _variante = "scoperta") AndAlso n <> 2 AndAlso n <> 4 Then
                n = If(n >= 4, 4, 2)
            End If
            _n = n
            _nomi = New List(Of String)(nomi)
            Dim numSquadre As Integer = If(_variante = "briscolino", 3, 2)
            _totaliSmazzate = New Integer(numSquadre - 1) {}
            _totaliPunti = New Integer(numSquadre - 1) {}
            _obiettivoMarianna = If(puntiObiettivo > 0, puntiObiettivo, 250)
            PreparaMano(0)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.New", ex)
            Throw
        End Try
    End Sub

    Private Function NumSquadre() As Integer
        Try
            Return If(_variante = "briscolino", 3, 2)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.NumSquadre", ex)
            Throw
        End Try
    End Function

    Private Function SquadraDi(posto As Integer) As Integer
        Try
            If _variante = "chiamata" Then
                Return If(posto = _chiamante OrElse (_socio.HasValue AndAlso posto = _socio.Value), 0, 1)
            End If
            If _n = 4 Then Return posto Mod 2
            Return posto
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.SquadraDi", ex)
            Throw
        End Try
    End Function

    Private Sub PreparaMano(primo As Integer)
        Try
            _turno = primo
            _tavolo = New List(Of Giocata)()
            _ultimaPresa = Nothing
            Dim ns As Integer = NumSquadre()
            _prese = New List(Of Integer)(ns - 1) {}
            For i As Integer = 0 To ns - 1
                _prese(i) = New List(Of Integer)()
            Next
            _accusi = New Integer(ns - 1) {}
            _accusiDichiarati = New List(Of Integer)()
            _chiHaPresoUltimo = Nothing
            _mani = New List(Of List(Of Integer))()
            For i As Integer = 0 To _n - 1
                _mani.Add(New List(Of Integer)())
            Next
            Dim mazzo As List(Of Integer) = MazzoItaliano.Mescolato()
            If _variante = "chiamata" Then
                For giro As Integer = 0 To 7
                    For i As Integer = 0 To _n - 1
                        _mani(i).Add(mazzo(0))
                        mazzo.RemoveAt(0)
                    Next
                Next
                For i As Integer = 0 To _n - 1
                    _mani(i) = MazzoItaliano.Ordina(_mani(i))
                Next
                _mazzo = New List(Of Integer)()
                _chiamante = primo
                _faseChiamata = True
                _cartaChiamata = Nothing
                _socio = Nothing
                _socioScoperto = False
                _briscola = Nothing
            Else
                If _variante = "briscolino" Then mazzo.Remove(1) ' si toglie il due di coppe: 39 carte, 13 a testa
                For giro As Integer = 0 To 2
                    For i As Integer = 0 To _n - 1
                        _mani(i).Add(mazzo(0))
                        mazzo.RemoveAt(0)
                    Next
                Next
                For i As Integer = 0 To _n - 1
                    _mani(i) = MazzoItaliano.Ordina(_mani(i))
                Next
                _mazzo = mazzo
                _briscola = mazzo(mazzo.Count - 1)
                _faseChiamata = False
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.PreparaMano", ex)
            Throw
        End Try
    End Sub

    Private Function Mani3() As IEnumerable(Of EventoMotore)
        Try
            Dim r As New List(Of EventoMotore)()
            For i As Integer = 0 To _mani.Count - 1
                r.Add(New EventoMotore(i, "Le tue carte: " & MazzoItaliano.Elenca(_mani(i)) & "."))
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Mani3", ex)
            Throw
        End Try
    End Function

    Public ReadOnly Property Attore As Integer Implements IMotoreGioco.Attore
        Get
            Return _turno
        End Get
    End Property

    Public Function Opzioni(posto As Integer) As JArray Implements IMotoreGioco.Opzioni
        Try
            Return New JArray()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Opzioni", ex)
            Throw
        End Try
    End Function

    Public Sub ImpostaComputer(computer As Boolean()) Implements IMotoreGioco.ImpostaComputer
    End Sub

    Public Function Inizio() As List(Of EventoMotore) Implements IMotoreGioco.Inizio
        Try
            Dim r As New List(Of EventoMotore)()
            If _variante = "chiamata" Then
                r.Add(New EventoMotore(Nothing, "Si gioca a Briscola a chiamata (in 5 giocatori). 8 carte a testa, tutto il mazzo distribuito. " & _nomi(_turno) & " deve chiamare la carta!"))
                r.AddRange(Mani3())
                Return r
            End If
            Dim b As Integer = _briscola.Value
            Dim testo As String = "Si gioca a briscola"
            Select Case _variante
                Case "al_meglio" : testo &= " al meglio delle 3 smazzate"
                Case "marianna" : testo &= " (Marianna con accuso fino a " & _obiettivoMarianna.ToString() & " punti)"
                Case "scoperta" : testo &= " scoperta (carte visibili)"
                Case "briscolino" : testo &= " a tre giocatori (Briscolino)"
            End Select
            testo &= ". La briscola è " & MazzoItaliano.SEMI(MazzoItaliano.Seme(b)) & ": la carta in fondo al mazzo è " & MazzoItaliano.Nome(b) & ". Inizia " & _nomi(_turno) & "."
            r.Add(New EventoMotore(Nothing, testo))
            r.AddRange(Mani3())
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Inizio", ex)
            Throw
        End Try
    End Function

    Public Function Mosse(posto As Integer) As List(Of MossaMotore) Implements IMotoreGioco.Mosse
        Try
            Dim r As New List(Of MossaMotore)()
            If _finita OrElse posto <> _turno Then Return r

            If _faseChiamata Then
                Dim mano As List(Of Integer) = _mani(posto)
                For seme As Integer = 0 To 3
                    For Each pos As Integer In {0, 2, 9} ' asso, tre, re
                        Dim cid As Integer = seme * 10 + pos
                        If Not mano.Contains(cid) Then r.Add(New MossaMotore("chiama|" & cid.ToString(), "Chiama: " & MazzoItaliano.Nome(cid)))
                    Next
                Next
                Return r
            End If

            If _variante = "marianna" AndAlso _tavolo.Count = 0 AndAlso (Not _chiHaPresoUltimo.HasValue OrElse _chiHaPresoUltimo.Value = posto) Then
                Dim mano As List(Of Integer) = _mani(posto)
                Dim semeBriscola As Integer = MazzoItaliano.Seme(_briscola.Value)
                For s As Integer = 0 To 3
                    If _accusiDichiarati.Contains(s) Then Continue For
                    If mano.Contains(s * 10 + 9) AndAlso mano.Contains(s * 10 + 8) Then
                        Dim bonus As Integer = If(s = semeBriscola, 40, 20)
                        r.Add(New MossaMotore("accuso|" & s.ToString(), "Accusa Marianna di " & MazzoItaliano.SEMI(s) & " (+" & bonus.ToString() & " punti)"))
                    End If
                Next
            End If

            Dim semeB As Integer = If(_briscola.HasValue, MazzoItaliano.Seme(_briscola.Value), -1)
            For Each id As Integer In _mani(posto)
                Dim p As Integer = Punti(id)
                Dim testo As String = MazzoItaliano.Nome(id) & ", " & p.ToString() & If(p = 1, " punto", " punti")
                If semeB >= 0 AndAlso MazzoItaliano.Seme(id) = semeB Then testo &= ", briscola"
                r.Add(New MossaMotore("gioca|" & id.ToString(), testo))
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Mosse", ex)
            Throw
        End Try
    End Function

    ''' <summary>Chi sta vincendo la mano fra le carte sul tavolo: la posizione nell'elenco del tavolo.</summary>
    Private Function VincenteTavolo() As Integer
        Try
            Dim semeBriscola As Integer = MazzoItaliano.Seme(_briscola.Value)
            Dim miglior As Integer = 0
            For i As Integer = 1 To _tavolo.Count - 1
                If Batte(_tavolo(i).Carta, _tavolo(miglior).Carta, semeBriscola) Then miglior = i
            Next
            Return miglior
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.VincenteTavolo", ex)
            Throw
        End Try
    End Function

    ''' <summary>La carta a batte la carta b?</summary>
    Private Shared Function Batte(a As Integer, b As Integer, semeBriscola As Integer) As Boolean
        Try
            Dim sa As Integer = MazzoItaliano.Seme(a)
            Dim sb As Integer = MazzoItaliano.Seme(b)
            If sa = sb Then Return Forza(a) > Forza(b)
            If sa = semeBriscola Then Return True
            Return False ' semi diversi senza briscola: vale la carta che c'era prima
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Batte", ex)
            Throw
        End Try
    End Function

    Public Function Applica(posto As Integer, idMossa As String, verifica As Boolean) As List(Of EventoMotore) Implements IMotoreGioco.Applica
        Try
            If verifica AndAlso Not Mosse(posto).Any(Function(m) m.Id = idMossa) Then Throw New MossaNonConsentita()
            Dim parti As String() = idMossa.Split("|"c)

            If parti(0) = "chiama" AndAlso parti.Length > 1 Then
                Dim idCarta As Integer = Integer.Parse(parti(1))
                _cartaChiamata = idCarta
                _briscola = idCarta
                _faseChiamata = False
                _socio = Nothing
                For p As Integer = 0 To _mani.Count - 1
                    If _mani(p).Contains(idCarta) Then
                        _socio = p
                        Exit For
                    End If
                Next
                _socioScoperto = False
                _turno = posto ' il chiamante gioca la prima carta
                Return New List(Of EventoMotore) From {New EventoMotore(Nothing, _nomi(posto) & " chiama " & MazzoItaliano.Nome(idCarta) & "! Il seme di briscola è " & MazzoItaliano.SEMI(MazzoItaliano.Seme(idCarta)) & ". Chi ha la carta è il compagno segreto.")}
            End If

            If parti(0) = "accuso" AndAlso parti.Length > 1 Then
                Dim seme As Integer = Integer.Parse(parti(1))
                _accusiDichiarati.Add(seme)
                Dim bonus As Integer = If(seme = MazzoItaliano.Seme(_briscola.Value), 40, 20)
                _accusi(SquadraDi(posto)) += bonus
                Return New List(Of EventoMotore) From {New EventoMotore(Nothing, _nomi(posto) & " accusa Marianna di " & MazzoItaliano.SEMI(seme) & ": +" & bonus.ToString() & " punti per la squadra!")}
            End If

            If parti(0) <> "gioca" OrElse parti.Length < 2 Then Throw New MossaNonConsentita()
            Dim id As Integer = Integer.Parse(parti(1))
            Dim k As Integer = _mani(posto).IndexOf(id)
            If k < 0 Then Throw New MossaNonConsentita()
            _mani(posto).RemoveAt(k)
            _tavolo.Add(New Giocata With {.Posto = posto, .Carta = id})
            Dim nome As String = _nomi(posto)
            Dim eventi As New List(Of EventoMotore)()
            eventi.Add(New EventoMotore(Nothing, nome & " gioca " & MazzoItaliano.Nome(id) & "."))

            If _variante = "chiamata" AndAlso _cartaChiamata.HasValue AndAlso id = _cartaChiamata.Value AndAlso Not _socioScoperto Then
                _socioScoperto = True
                eventi.Add(New EventoMotore(Nothing, nome & " ha giocato la carta chiamata ed è il compagno di " & _nomi(_chiamante) & "!"))
            End If

            If _tavolo.Count < _n Then
                _turno = (posto + 1) Mod _n
                Return eventi
            End If

            ' la mano è completa: prende chi vince
            Dim v As Integer = VincenteTavolo()
            Dim vincitore As Integer = _tavolo(v).Posto
            Dim punti As Integer = 0
            Dim carte As New List(Of Integer)()
            For Each g As Giocata In _tavolo
                punti += MotoreBriscola.Punti(g.Carta)
                carte.Add(g.Carta)
            Next
            _prese(SquadraDi(vincitore)).AddRange(carte)
            _ultimaPresa = New UltimaPresaInfo With {.Vincitore = vincitore, .Carte = carte, .Punti = punti}
            _chiHaPresoUltimo = vincitore
            _tavolo = New List(Of Giocata)()
            eventi.Add(New EventoMotore(Nothing, _nomi(vincitore) & " prende con " & MazzoItaliano.Nome(carte(v)) & ": " & punti.ToString() & If(punti = 1, " punto", " punti") & "."))

            If _mazzo.Count > 0 Then
                Dim i As Integer = 0
                While i < _n AndAlso _mazzo.Count > 0
                    Dim chi As Integer = (vincitore + i) Mod _n
                    Dim c As Integer = _mazzo(0)
                    _mazzo.RemoveAt(0)
                    _mani(chi).Add(c)
                    _mani(chi) = MazzoItaliano.Ordina(_mani(chi))
                    eventi.Add(New EventoMotore(chi, "Hai pescato: " & MazzoItaliano.Nome(c) & "."))
                    i += 1
                End While
                If _mazzo.Count = 0 Then eventi.Add(New EventoMotore(Nothing, "Il mazzo è finito: non si pesca più."))
            End If
            _turno = vincitore

            If _mani.Sum(Function(m) m.Count) = 0 Then Concludi(eventi)
            Return eventi
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Applica", ex)
            Throw
        End Try
    End Function

    Private Function PuntiSquadra(sq As Integer) As Integer
        Try
            Dim p As Integer = If(sq < _accusi.Length, _accusi(sq), 0)
            If sq < _prese.Length Then
                For Each c As Integer In _prese(sq)
                    p += Punti(c)
                Next
            End If
            Return p
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.PuntiSquadra", ex)
            Throw
        End Try
    End Function

    Private Function NomiSquadra(sq As Integer) As String
        Try
            If _variante = "chiamata" Then
                If sq = 0 Then
                    Dim c As String = _nomi(_chiamante)
                    Dim s As String = If(_socio.HasValue, _nomi(_socio.Value), "compagno")
                    Return If(_socioScoperto OrElse _finita, c & " e " & s, c & " e compagno")
                End If
                Dim difensori As New List(Of String)()
                For i As Integer = 0 To _n - 1
                    If i <> _chiamante AndAlso Not (_socio.HasValue AndAlso i = _socio.Value) Then difensori.Add(_nomi(i))
                Next
                Return String.Join(", ", difensori)
            End If
            If _variante = "briscolino" Then Return _nomi(sq)
            If _n = 2 Then Return _nomi(sq)
            Return _nomi(sq) & " e " & _nomi(sq + 2)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.NomiSquadra", ex)
            Throw
        End Try
    End Function

    Private Sub ImpostaPunteggi(a As Integer, b As Integer)
        Try
            _punteggi = New List(Of PunteggioSquadra) From {
                New PunteggioSquadra With {.Punti = a, .Carte = _prese(0).Count},
                New PunteggioSquadra With {.Punti = b, .Carte = _prese(1).Count}}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.ImpostaPunteggi", ex)
            Throw
        End Try
    End Sub

    Private Sub NuovaMano(eventi As List(Of EventoMotore), parola As String)
        Try
            _smazzata += 1
            Dim primo As Integer = (_smazzata - 1) Mod _n
            PreparaMano(primo)
            eventi.Add(New EventoMotore(Nothing, "Comincia la " & parola & " " & _smazzata.ToString() & ". Inizia " & _nomi(primo) & "."))
            eventi.AddRange(Mani3())
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.NuovaMano", ex)
            Throw
        End Try
    End Sub

    Private Sub Concludi(eventi As List(Of EventoMotore))
        Try
            Dim v As String = _variante
            If v = "briscolino" Then
                Dim pp As Integer() = {PuntiSquadra(0), PuntiSquadra(1), PuntiSquadra(2)}
                _finita = True
                _punteggi = New List(Of PunteggioSquadra)()
                For i As Integer = 0 To 2
                    _punteggi.Add(New PunteggioSquadra With {.Punti = pp(i), .Carte = _prese(i).Count})
                Next
                Dim maxP As Integer = pp.Max()
                Dim vincenti As New List(Of Integer)()
                For i As Integer = 0 To 2
                    If pp(i) = maxP Then vincenti.Add(i)
                Next
                If vincenti.Count > 1 Then
                    _vincitori = New List(Of Integer)()
                    _motivo = "Pareggio a " & maxP.ToString() & " punti."
                Else
                    _vincitori = vincenti
                    _motivo = "Vince " & _nomi(vincenti(0)) & " con " & maxP.ToString() & " punti."
                End If
                eventi.Add(New EventoMotore(Nothing, "La partita è finita. " & _motivo))
                Return
            End If

            Dim p0 As Integer = PuntiSquadra(0)
            Dim p1 As Integer = PuntiSquadra(1)

            If v = "chiamata" Then
                _finita = True
                _socioScoperto = True
                ImpostaPunteggi(p0, p1)
                Dim socio As Integer = If(_socio.HasValue, _socio.Value, _chiamante)
                If p0 >= 61 Then
                    _vincitori = New List(Of Integer) From {_chiamante}
                    If socio <> _chiamante Then _vincitori.Add(socio)
                    _motivo = "Vincono " & _nomi(_chiamante) & " e " & _nomi(socio) & " con " & p0.ToString() & " punti contro " & p1.ToString() & "."
                Else
                    _vincitori = New List(Of Integer)()
                    For i As Integer = 0 To 4
                        If i <> _chiamante AndAlso i <> socio Then _vincitori.Add(i)
                    Next
                    _motivo = "Vincono i difensori (" & NomiSquadra(1) & ") con " & p1.ToString() & " punti contro " & p0.ToString() & "."
                End If
                eventi.Add(New EventoMotore(Nothing, "La partita è finita. " & _motivo))
                Return
            End If

            If v = "al_meglio" Then
                Dim vm As Integer = If(p0 > p1, 0, If(p1 > p0, 1, -1))
                If vm >= 0 Then _totaliSmazzate(vm) += 1
                Dim s0 As Integer = _totaliSmazzate(0)
                Dim s1 As Integer = _totaliSmazzate(1)
                eventi.Add(New EventoMotore(Nothing, "Smazzata " & _smazzata.ToString() & " conclusa: " & NomiSquadra(0) & " " & p0.ToString() & " punti, " & NomiSquadra(1) & " " & p1.ToString() & " punti. Smazzate vinte: " & s0.ToString() & " a " & s1.ToString() & "."))
                If s0 >= 2 OrElse s1 >= 2 Then
                    _finita = True
                    Dim vinc As Integer = If(s0 >= 2, 0, 1)
                    _vincitori = If(_n = 2, New List(Of Integer) From {vinc}, New List(Of Integer) From {vinc, vinc + 2})
                    _motivo = "Vince la partita " & NomiSquadra(vinc) & " per " & _totaliSmazzate(vinc).ToString() & " smazzate a " & _totaliSmazzate(1 - vinc).ToString() & "."
                    ImpostaPunteggi(s0, s1)
                    eventi.Add(New EventoMotore(Nothing, "La partita è finita. " & _motivo))
                    Return
                End If
                NuovaMano(eventi, "smazzata")
                Return
            End If

            If v = "marianna" Then
                _totaliPunti(0) += p0
                _totaliPunti(1) += p1
                Dim t0 As Integer = _totaliPunti(0)
                Dim t1 As Integer = _totaliPunti(1)
                eventi.Add(New EventoMotore(Nothing, "Mano " & _smazzata.ToString() & " finita: " & NomiSquadra(0) & " +" & p0.ToString() & " (totale " & t0.ToString() & "), " & NomiSquadra(1) & " +" & p1.ToString() & " (totale " & t1.ToString() & "). Obiettivo: " & _obiettivoMarianna.ToString() & " punti."))
                If (t0 >= _obiettivoMarianna OrElse t1 >= _obiettivoMarianna) AndAlso t0 <> t1 Then
                    _finita = True
                    Dim vinc As Integer = If(t0 > t1, 0, 1)
                    _vincitori = If(_n = 2, New List(Of Integer) From {vinc}, New List(Of Integer) From {vinc, vinc + 2})
                    _motivo = "Vince " & NomiSquadra(vinc) & " con " & _totaliPunti(vinc).ToString() & " punti contro " & _totaliPunti(1 - vinc).ToString() & "."
                    ImpostaPunteggi(t0, t1)
                    eventi.Add(New EventoMotore(Nothing, "La partita è finita. " & _motivo))
                    Return
                End If
                NuovaMano(eventi, "mano")
                Return
            End If

            ' classica o scoperta
            _finita = True
            ImpostaPunteggi(p0, p1)
            If p0 = p1 Then
                _vincitori = New List(Of Integer)()
                _motivo = "Pareggio: " & p0.ToString() & " punti a testa."
            Else
                Dim vinc As Integer = If(p0 > p1, 0, 1)
                Dim pv As Integer() = {p0, p1}
                _vincitori = If(_n = 2, New List(Of Integer) From {vinc}, New List(Of Integer) From {vinc, vinc + 2})
                _motivo = "Vince " & NomiSquadra(vinc) & " con " & pv(vinc).ToString() & " punti contro " & pv(1 - vinc).ToString() & "."
            End If
            eventi.Add(New EventoMotore(Nothing, "La partita è finita. " & _motivo))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Concludi", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ vista
    Public Function Vista(posto As Integer) As JObject Implements IMotoreGioco.Vista
        Try
            Dim v As String = _variante
            Dim mioSq As Integer = If(posto >= 0, SquadraDi(posto), -1)
            Dim tavolo As New JArray()
            For Each g As Giocata In _tavolo
                Dim x As JObject = MazzoItaliano.Carta(g.Carta)
                x("da") = _nomi(g.Posto)
                tavolo.Add(x)
            Next
            Dim briscola As JToken = If(_briscola.HasValue, CType(MazzoItaliano.Carta(_briscola.Value), JToken), JValue.CreateNull())
            Dim semeB As String = If(_briscola.HasValue, MazzoItaliano.SEMI(MazzoItaliano.Seme(_briscola.Value)), "")
            Dim squadre As New JArray()
            For sq As Integer = 0 To NumSquadre() - 1
                Dim membri As New List(Of Integer)()
                For p As Integer = 0 To _n - 1
                    If SquadraDi(p) = sq Then membri.Add(p)
                Next
                Dim vedi As Boolean = _finita OrElse If(v = "chiamata", _socioScoperto, sq = mioSq)
                squadre.Add(Attrezzi.J("nome", NomiSquadra(sq), "membri", Attrezzi.Arr(membri), "carte_prese", _prese(sq).Count,
                                       "punti", If(vedi, CObj(PuntiSquadra(sq)), Nothing), "accusi", If(sq < _accusi.Length, _accusi(sq), 0)))
            Next
            Dim riepilogo As New List(Of String)()
            If semeB <> "" Then
                riepilogo.Add("Briscola: " & semeB & If(_mazzo.Count > 0, ", carta in fondo al mazzo: " & MazzoItaliano.Nome(_briscola.Value), If(v = "chiamata", " (chiamata)", ", mazzo finito")))
            Else
                riepilogo.Add("In attesa della chiamata della carta di briscola")
            End If
            If v <> "chiamata" Then riepilogo.Add("Mazzo: " & _mazzo.Count.ToString() & " carte")
            If mioSq >= 0 AndAlso mioSq < _prese.Length Then riepilogo.Add("Prese squadra: " & _prese(mioSq).Count.ToString() & " carte per " & PuntiSquadra(mioSq).ToString() & " punti")

            Dim maniScoperte As JToken = JValue.CreateNull()
            If v = "scoperta" OrElse _finita Then
                Dim a As New JArray()
                For Each m As List(Of Integer) In _mani
                    a.Add(Attrezzi.Arr(m.Select(Function(i) MazzoItaliano.Carta(i))))
                Next
                maniScoperte = a
            End If

            Dim ultimaPresa As JToken = JValue.CreateNull()
            If _ultimaPresa IsNot Nothing Then
                ultimaPresa = Attrezzi.J("vincitore", _ultimaPresa.Vincitore, "punti", _ultimaPresa.Punti, "carte", Attrezzi.Arr(_ultimaPresa.Carte.Select(Function(c) MazzoItaliano.Carta(c))))
            End If
            Dim punteggi As JToken = JValue.CreateNull()
            If _punteggi IsNot Nothing Then punteggi = Attrezzi.Arr(_punteggi.Select(Function(p) Attrezzi.J("punti", p.Punti, "carte", p.Carte)))
            Dim mano As IEnumerable(Of Integer) = If(posto >= 0 AndAlso posto < _mani.Count, _mani(posto), New List(Of Integer)())

            Return Attrezzi.J("tipo", "briscola", "variante", v, "n", _n, "nomi", Attrezzi.Arr(_nomi), "turno", _turno,
                              "fase_chiamata", _faseChiamata,
                              "carta_chiamata", If(_cartaChiamata.HasValue, CType(MazzoItaliano.Carta(_cartaChiamata.Value), JToken), JValue.CreateNull()),
                              "socio_scoperto", _socioScoperto, "mazzo", _mazzo.Count, "briscola", briscola, "seme_briscola", semeB,
                              "tavolo_carte", tavolo, "ultima_presa", ultimaPresa,
                              "mano", Attrezzi.Arr(mano.Select(Function(i) MazzoItaliano.Carta(i))), "mani_scoperte", maniScoperte,
                              "carte_in_mano", Attrezzi.Arr(_mani.Select(Function(m) m.Count)), "squadre", squadre, "mia_squadra", mioSq,
                              "smazzata", _smazzata, "totali_smazzate", Attrezzi.Arr(_totaliSmazzate), "totali_punti", Attrezzi.Arr(_totaliPunti),
                              "finita", _finita, "punteggi", punteggi, "vincitori", Attrezzi.Arr(_vincitori), "motivo", _motivo,
                              "riepilogo", Attrezzi.Arr(riepilogo), "testo_tavolo", TestoTavolo(), "testo_punti", TestoPunti(mioSq))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Vista", ex)
            Throw
        End Try
    End Function

    Private Function TestoTavolo() As String
        Try
            Dim t As String = ""
            If _briscola.HasValue Then
                t &= "Briscola: " & MazzoItaliano.Nome(_briscola.Value) & If(_mazzo.Count > 0, ", carta in fondo al mazzo. ", ". ")
            ElseIf _faseChiamata Then
                t &= "In attesa della chiamata. "
            End If
            If _variante <> "chiamata" Then t &= "Mazzo: " & _mazzo.Count.ToString() & " carte. "
            If _tavolo.Count = 0 Then
                t &= "Sul tavolo non ci sono carte."
            Else
                t &= "Sul tavolo: " & String.Join("; ", _tavolo.Select(Function(g) _nomi(g.Posto) & " ha giocato " & MazzoItaliano.Nome(g.Carta))) & "."
            End If
            Dim mani As New List(Of String)()
            For i As Integer = 0 To _mani.Count - 1
                Dim n As Integer = _mani(i).Count
                mani.Add(_nomi(i) & " " & n.ToString() & If(n = 1, " carta", " carte"))
            Next
            t &= " Carte in mano: " & String.Join(", ", mani) & "."
            If _ultimaPresa IsNot Nothing Then
                t &= " Ultima presa: " & _nomi(_ultimaPresa.Vincitore) & " con " & _ultimaPresa.Punti.ToString() & If(_ultimaPresa.Punti = 1, " punto", " punti") & "."
            End If
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.TestoTavolo", ex)
            Throw
        End Try
    End Function

    Private Function TestoPunti(mioSq As Integer) As String
        Try
            If _finita Then Return _motivo
            Dim v As String = _variante
            If v = "al_meglio" Then
                Return "Al meglio delle 3 smazzate (smazzata " & _smazzata.ToString() & "). Smazzate vinte: " & NomiSquadra(0) & " " & _totaliSmazzate(0).ToString() & ", " & NomiSquadra(1) & " " & _totaliSmazzate(1).ToString() & ". Punti mano: " & PuntiSquadra(mioSq).ToString() & "."
            End If
            If v = "marianna" Then
                Return "Marianna fino a " & _obiettivoMarianna.ToString() & " punti (mano " & _smazzata.ToString() & "). Totali: " & NomiSquadra(0) & " " & _totaliPunti(0).ToString() & ", " & NomiSquadra(1) & " " & _totaliPunti(1).ToString() & ". Punti in questa mano: " & PuntiSquadra(mioSq).ToString() & "."
            End If
            If v = "chiamata" Then
                Return "Briscola a chiamata: " & _nomi(_chiamante) & " e compagno contro i difensori. Punti squadra: " & PuntiSquadra(mioSq).ToString() & "."
            End If
            If mioSq < 0 Then Return "I punti si contano alla fine."
            Return "Hai preso " & _prese(mioSq).Count.ToString() & " carte per " & PuntiSquadra(mioSq).ToString() & " punti. Gli altri punti si vedono alla fine."
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.TestoPunti", ex)
            Throw
        End Try
    End Function

    Public Function Situazione(posto As Integer) As String Implements IMotoreGioco.Situazione
        Try
            Dim t As String = TestoTavolo() & " "
            t &= "Hai in mano " & _mani(posto).Count.ToString() & " carte: " & MazzoItaliano.Elenca(_mani(posto)) & ". "
            t &= TestoPunti(SquadraDi(posto))
            Return t.Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Situazione", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ computer
    ''' <summary>Chiave per ordinare le carte da giocare: prima quelle che non sono briscole, poi meno punti, poi meno forza (la più bassa è la prima).</summary>
    Private Shared Function ChiaveScarto(c As Integer, semeBriscola As Integer) As Integer
        Try
            Return If(MazzoItaliano.Seme(c) = semeBriscola, 1, 0) * 10000 + Punti(c) * 100 + Forza(c)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.ChiaveScarto", ex)
            Throw
        End Try
    End Function

    ''' <summary>La mossa semplice di chi gioca nelle simulazioni (le stesse idee del computer di sempre). cartaAvversaria: la carta sul tavolo, se c'è.</summary>
    Private Shared Function MossaSemplice(mano As List(Of Integer), cartaAvversaria As Integer?, semeBriscola As Integer) As Integer
        Try
            If Not cartaAvversaria.HasValue Then Return mano.OrderBy(Function(c) ChiaveScarto(c, semeBriscola)).First()
            Dim avv As Integer = cartaAvversaria.Value
            Dim vincenti As List(Of Integer) = mano.Where(Function(c) Batte(c, avv, semeBriscola)).OrderBy(Function(c) ChiaveScarto(c, semeBriscola)).ToList()
            If vincenti.Count > 0 Then
                Dim meglio As Integer = vincenti(0)
                Dim conBriscola As Boolean = MazzoItaliano.Seme(meglio) = semeBriscola AndAlso MazzoItaliano.Seme(avv) <> semeBriscola
                Dim puntiTavolo As Integer = Punti(avv)
                If Not conBriscola OrElse puntiTavolo + Punti(meglio) >= 10 OrElse puntiTavolo >= 4 Then Return meglio
            End If
            Return mano.OrderBy(Function(c) ChiaveScarto(c, semeBriscola)).First()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.MossaSemplice", ex)
            Throw
        End Try
    End Function

    ''' <summary>Gioca fino alla fine con la mossa semplice e dice quanti punti in più ha fatto io. Il mazzo si pesca come nel gioco (prima chi ha preso).</summary>
    Private Shared Function GiocaFinoAllaFine(mani As List(Of Integer)(), mazzo As List(Of Integer), tavolo As List(Of Integer()), turno As Integer, io As Integer, semeBriscola As Integer) As Integer
        Try
            Dim valore As Integer = 0
            Do
                While tavolo.Count < 2
                    Dim c As Integer = MossaSemplice(mani(turno), If(tavolo.Count = 1, CType(tavolo(0)(1), Integer?), Nothing), semeBriscola)
                    mani(turno).Remove(c)
                    tavolo.Add({turno, c})
                    turno = 1 - turno
                End While
                Dim v As Integer = If(Batte(tavolo(1)(1), tavolo(0)(1), semeBriscola), 1, 0)
                Dim vincitore As Integer = tavolo(v)(0)
                Dim puntiPresa As Integer = Punti(tavolo(0)(1)) + Punti(tavolo(1)(1))
                valore += If(vincitore = io, puntiPresa, -puntiPresa)
                tavolo.Clear()
                If mazzo.Count > 0 Then
                    mani(vincitore).Add(mazzo(0))
                    mazzo.RemoveAt(0)
                    If mazzo.Count > 0 Then
                        mani(1 - vincitore).Add(mazzo(0))
                        mazzo.RemoveAt(0)
                    End If
                End If
                turno = vincitore
                If mani(0).Count = 0 AndAlso mani(1).Count = 0 Then Return valore
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.GiocaFinoAllaFine", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Quanti punti in più (o in meno) fa chi gioca (io) da qui alla fine, se entrambi giocano al meglio: a mazzo finito le carte dell'avversario
    ''' si ricavano da quelle già viste, quindi si prova ogni sequenza di mosse. Solo per due giocatori.
    ''' </summary>
    Private Shared Function EsitoFinale(mani As List(Of Integer)(), tavolo As List(Of Integer()), turno As Integer, io As Integer, semeBriscola As Integer) As Integer
        Try
            If tavolo.Count = 2 Then
                Dim v As Integer = If(Batte(tavolo(1)(1), tavolo(0)(1), semeBriscola), 1, 0)
                Dim vincitore As Integer = tavolo(v)(0)
                Dim puntiPresa As Integer = Punti(tavolo(0)(1)) + Punti(tavolo(1)(1))
                Dim valore As Integer = If(vincitore = io, puntiPresa, -puntiPresa)
                If mani(0).Count = 0 AndAlso mani(1).Count = 0 Then Return valore
                Return valore + EsitoFinale(mani, New List(Of Integer()), vincitore, io, semeBriscola)
            End If
            Dim migliore As Integer = If(turno = io, -1000, 1000)
            For Each c As Integer In mani(turno).ToList()
                Dim dopo As List(Of Integer)() = {New List(Of Integer)(mani(0)), New List(Of Integer)(mani(1))}
                dopo(turno).Remove(c)
                Dim t2 As New List(Of Integer())(tavolo)
                t2.Add({turno, c})
                Dim r As Integer = EsitoFinale(dopo, t2, 1 - turno, io, semeBriscola)
                migliore = If(turno = io, Math.Max(migliore, r), Math.Min(migliore, r))
            Next
            Return migliore
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.EsitoFinale", ex)
            Throw
        End Try
    End Function

    Private Function BotFinale(posto As Integer, viste As HashSet(Of Integer)) As String
        Try
            Dim altro As Integer = 1 - posto
            Dim mia As List(Of Integer) = _mani(posto)
            Dim sua As List(Of Integer) = Enumerable.Range(0, 40).Where(Function(c) Not viste.Contains(c)).ToList()
            If sua.Count <> _mani(altro).Count OrElse mia.Count = 0 Then Return Nothing
            Dim semeBriscola As Integer = MazzoItaliano.Seme(_briscola.Value)
            Dim migliore As Integer? = Nothing
            Dim valoreMigliore As Integer = -1000
            For Each c As Integer In mia
                Dim mani As List(Of Integer)() = {New List(Of Integer)(), New List(Of Integer)()}
                mani(posto) = mia.Where(Function(x) x <> c).ToList()
                mani(altro) = New List(Of Integer)(sua)
                Dim tavolo As New List(Of Integer())()
                For Each g As Giocata In _tavolo
                    tavolo.Add({g.Posto, g.Carta})
                Next
                tavolo.Add({posto, c})
                Dim v As Integer = EsitoFinale(mani, tavolo, altro, posto, semeBriscola)
                ' a parità di punti si tiene la carta più forte per dopo (si gioca la più debole)
                If v > valoreMigliore OrElse (v = valoreMigliore AndAlso migliore.HasValue AndAlso Forza(c) < Forza(migliore.Value)) Then
                    valoreMigliore = v
                    migliore = c
                End If
            Next
            Return If(migliore.HasValue, "gioca|" & migliore.Value.ToString(), Nothing)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.BotFinale", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Due giocatori: del mazzo e dell'avversario si sa quello che è uscito (prese, tavolo, la propria mano, la briscola scoperta). Il resto si mescola
    ''' più volte a caso (ogni volta un'ipotesi su cosa ha in mano l'avversario e in che ordine escono le carte), si gioca ogni mossa fino alla fine
    ''' con la mossa semplice, e si sceglie la carta che in media fa più punti. A mazzo finito l'avversario non ha segreti e si prova ogni sequenza.
    ''' </summary>
    Private Function BotSimulato(posto As Integer) As String
        Try
            Dim altro As Integer = 1 - posto
            Dim mia As List(Of Integer) = _mani(posto)
            If mia.Count = 0 Then Return Nothing
            Dim semeBriscola As Integer = MazzoItaliano.Seme(_briscola.Value)
            Dim nelMazzo As Integer = _mazzo.Count
            Dim viste As New HashSet(Of Integer)(mia)
            For Each g As Giocata In _tavolo
                viste.Add(g.Carta)
            Next
            For Each sq As List(Of Integer) In _prese
                For Each c As Integer In sq
                    viste.Add(c)
                Next
            Next
            If nelMazzo > 0 Then viste.Add(_briscola.Value)   ' la briscola scoperta sta in fondo al mazzo
            Dim ignote As List(Of Integer) = Enumerable.Range(0, 40).Where(Function(c) Not viste.Contains(c)).ToList()
            Dim h As Integer = _mani(altro).Count
            If ignote.Count <> h + Math.Max(0, nelMazzo - 1) Then Return Nothing
            If nelMazzo = 0 Then Return BotFinale(posto, viste)
            Dim mondi As New List(Of Tuple(Of List(Of Integer), List(Of Integer)))()
            For r As Integer = 0 To 79
                Dim pool As List(Of Integer) = New List(Of Integer)(ignote)
                For i As Integer = pool.Count - 1 To 1 Step -1
                    Dim j As Integer = Attrezzi.Casuale(0, i + 1)
                    Dim x As Integer = pool(i)
                    pool(i) = pool(j)
                    pool(j) = x
                Next
                Dim manoAvv As List(Of Integer) = pool.Take(h).ToList()
                Dim mazzo As List(Of Integer) = pool.Skip(h).ToList()
                mazzo.Add(_briscola.Value)
                mondi.Add(Tuple.Create(manoAvv, mazzo))
            Next
            Dim migliore As Integer? = Nothing
            Dim mediaMigliore As Double = Double.NegativeInfinity
            For Each c As Integer In mia.Distinct()
                Dim somma As Long = 0
                For Each mondo As Tuple(Of List(Of Integer), List(Of Integer)) In mondi
                    Dim mani As List(Of Integer)() = {New List(Of Integer)(), New List(Of Integer)()}
                    mani(posto) = mia.Where(Function(x) x <> c).ToList()
                    mani(altro) = New List(Of Integer)(mondo.Item1)
                    Dim tavolo As New List(Of Integer())()
                    For Each g As Giocata In _tavolo
                        tavolo.Add({g.Posto, g.Carta})
                    Next
                    tavolo.Add({posto, c})
                    somma += GiocaFinoAllaFine(mani, New List(Of Integer)(mondo.Item2), tavolo, altro, posto, semeBriscola)
                Next
                Dim media As Double = somma / mondi.Count
                ' a parità conviene giocare la carta meno preziosa
                If media > mediaMigliore + 0.000000001 OrElse (Math.Abs(media - mediaMigliore) <= 0.000000001 AndAlso migliore.HasValue AndAlso ChiaveScarto(c, semeBriscola) < ChiaveScarto(migliore.Value, semeBriscola)) Then
                    mediaMigliore = media
                    migliore = c
                End If
            Next
            Return If(migliore.HasValue, "gioca|" & migliore.Value.ToString(), Nothing)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.BotSimulato", ex)
            Throw
        End Try
    End Function

    Public Function Bot(posto As Integer) As String Implements IMotoreGioco.Bot
        Try
            If _faseChiamata AndAlso posto = _turno Then
                Return Mosse(posto)(0).Id
            End If
            If _variante = "marianna" Then
                For Each m As MossaMotore In Mosse(posto)
                    If m.Id.StartsWith("accuso|", StringComparison.Ordinal) Then Return m.Id
                Next
            End If

            ' in due: si prova a indovinare le carte dell'avversario (a mazzo finito si ricavano con certezza) e si sceglie la carta che in media rende di più
            If _n = 2 AndAlso _variante <> "chiamata" AndAlso _briscola.HasValue Then
                Dim scelta As String = BotSimulato(posto)
                If scelta IsNot Nothing Then Return scelta
            End If

            Dim mano As List(Of Integer) = _mani(posto)
            Dim semeBriscola As Integer = If(_briscola.HasValue, MazzoItaliano.Seme(_briscola.Value), 0)
            Dim PiuBassa As Func(Of IEnumerable(Of Integer), Integer) =
                Function(carte) carte.OrderBy(Function(c) If(MazzoItaliano.Seme(c) = semeBriscola, 1, 0)).ThenBy(Function(c) Punti(c)).ThenBy(Function(c) Forza(c)).First()

            If _tavolo.Count = 0 Then Return "gioca|" & PiuBassa(mano).ToString()

            Dim v As Integer = VincenteTavolo()
            Dim cartaVincente As Integer = _tavolo(v).Carta
            Dim postoVincente As Integer = _tavolo(v).Posto
            Dim puntiTavolo As Integer = _tavolo.Sum(Function(g) Punti(g.Carta))
            Dim ultimo As Boolean = _tavolo.Count = _n - 1
            Dim compagnoVince As Boolean = (_n = 4 OrElse _variante = "chiamata") AndAlso SquadraDi(postoVincente) = SquadraDi(posto)

            If compagnoVince Then
                If ultimo Then
                    Dim carichi As List(Of Integer) = mano.Where(Function(c) MazzoItaliano.Seme(c) <> semeBriscola).OrderByDescending(Function(c) Punti(c)).ToList()
                    If carichi.Count > 0 Then Return "gioca|" & carichi(0).ToString()
                End If
                Return "gioca|" & PiuBassa(mano).ToString()
            End If

            Dim vincenti As List(Of Integer) = mano.Where(Function(c) Batte(c, cartaVincente, semeBriscola)).ToList()
            If vincenti.Count > 0 Then
                Dim meglio As Integer = vincenti.OrderBy(Function(c) If(MazzoItaliano.Seme(c) = semeBriscola, 1, 0)).ThenBy(Function(c) Punti(c)).ThenBy(Function(c) Forza(c)).First()
                Dim conBriscola As Boolean = MazzoItaliano.Seme(meglio) = semeBriscola AndAlso MazzoItaliano.Seme(cartaVincente) <> semeBriscola
                If Not conBriscola OrElse puntiTavolo + Punti(meglio) >= 10 OrElse (puntiTavolo >= 4 AndAlso ultimo) Then Return "gioca|" & meglio.ToString()
            End If
            Return "gioca|" & PiuBassa(mano).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBriscola.Bot", ex)
            Throw
        End Try
    End Function

End Class
