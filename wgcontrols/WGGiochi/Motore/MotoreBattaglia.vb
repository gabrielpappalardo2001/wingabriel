Imports System.Linq
Imports Newtonsoft.Json.Linq

''' <summary>
''' Battaglia navale per due giocatori (due persone, oppure una persona contro il computer). Stesse regole di battaglia.php.
''' Casellario di 10 colonne (A-J) per 10 righe. Ognuno posiziona la sua flotta senza che l'altro la veda, poi si spara a turno:
''' acqua, colpito, colpito e affondato. Vince chi affonda per primo tutte le navi dell'altro.
''' Varianti: classica (10 navi che non si toccano), cinque_navi (5, 4, 3, 3, 2, anche a contatto), a_raffica (come la classica, chi colpisce spara ancora).
''' Mosse: nave|riga|colonna|o (o = "o" orizzontale, "v" verticale; righe e colonne da 0), casuale, annulla, pronto, spara|riga|colonna.
''' </summary>
Friend Class MotoreBattaglia
    Implements IMotoreGioco

    Private Shared ReadOnly INSULTI_ACQUA As String() = {"Solo solletico alle alghe.", "I pesci rossi ringraziano per l'annaffiata.", "Nemmeno i pesci se ne sono accorti.", "La mira è andata in ferie.", "Splash, ma senza gloria.", "I pesci protestano per il disturbo.", "Un tiro da marinaio ubriaco.", "Il bersaglio era dall'altra parte, genio.", "Il cannone spara solo bolle d'aria.", "Colpita una medusa innocente.", "Il fondo del mare ringrazia.", "Munizioni sprecate, che peccato.", "Un gabbiano distratto è salvo per un soffio.", "Il capitano nemico ride sotto i baffi.", "Che mira da campione di nuoto."}
    Private Shared ReadOnly COMPLIMENTI_COLPITO As String() = {"Bel colpo, la nave trema tutta.", "Centro perfetto, complimenti.", "Mira chirurgica!", "Un colpo da maestro.", "Questo ha fatto male sul serio.", "Gran bella cannonata!", "Boom, che botta!", "Il nemico cerca già i giubbotti.", "Colpo secco e preciso, bravissimo.", "Hanno sentito il colpo fino a terra.", "Questa sì che è mira.", "Il capitano nemico non dorme più tranquillo."}
    Private Shared ReadOnly COMPLIMENTI_AFFONDATA As String() = {"Giù nel blu, senza scampo.", "Addio nave, è stato un piacere.", "Pesci, fate spazio a un relitto nuovo.", "È rimasta solo la schiuma a galla.", "Un affondamento da manuale.", "Non galleggia più nemmeno un pezzo.", "Spettacolare, è andata a fondo dritta.", "Questa sì che è una fine gloriosa."}
    Private Shared ReadOnly _caso As New Random()
    Private Shared Function Scelta(l As String()) As String
        Try
            SyncLock _caso
                Return l(_caso.Next(l.Length))
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Scelta", ex)
            Throw
        End Try
    End Function

    Public Const RIGHE As Integer = 10
    Public Const COLONNE As Integer = 10
    Public Shared ReadOnly VARIANTI As String() = {"classica", "cinque_navi", "a_raffica"}

    Private Class Nave
        Public L As Integer
        Public Celle As List(Of Integer())
        Public Colpite As Integer
    End Class

    Private ReadOnly _variante As String
    Private ReadOnly _nomi As List(Of String)
    Private _fase As String = "posizionamento"
    Private ReadOnly _navi As List(Of Nave)() = {New List(Of Nave)(), New List(Of Nave)()}
    Private ReadOnly _da As List(Of Integer)()
    Private ReadOnly _colpi As Dictionary(Of String, String)() = {New Dictionary(Of String, String)(), New Dictionary(Of String, String)()}
    Private ReadOnly _pronto As Boolean() = {False, False}
    Private _turno As Integer = 0
    Private _ultimoColpo As JObject = Nothing
    Private _finita As Boolean = False
    Private _vincitore As Integer? = Nothing
    Private _computer As Boolean() = Nothing

    Public Sub New(nomi As List(Of String), variante As String)
        Try
            If nomi.Count <> 2 Then Throw New ArgumentException("La battaglia navale si gioca in due.")
            _nomi = New List(Of String)(nomi)
            _variante = If(VARIANTI.Contains(variante), variante, "classica")
            _da = {Flotta(_variante), Flotta(_variante)}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.New", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Flotta(variante As String) As List(Of Integer)
        Try
            Return If(variante = "cinque_navi", New List(Of Integer) From {5, 4, 3, 3, 2}, New List(Of Integer) From {4, 3, 3, 2, 2, 2, 1, 1, 1, 1})
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Flotta", ex)
            Throw
        End Try
    End Function

    Private Shared Function Distanza(variante As String) As Boolean
        Try
            Return variante <> "cinque_navi"
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Distanza", ex)
            Throw
        End Try
    End Function

    Private Shared Function Raffica(variante As String) As Boolean
        Try
            Return variante = "a_raffica"
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Raffica", ex)
            Throw
        End Try
    End Function

    Private Shared Function Nome(variante As String) As String
        Try
            Select Case variante
                Case "cinque_navi" : Return "cinque navi"
                Case "a_raffica" : Return "a raffica"
                Case Else : Return "classica"
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Nome", ex)
            Throw
        End Try
    End Function

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

    Public Sub ImpostaComputer(computer As Boolean()) Implements IMotoreGioco.ImpostaComputer
        Try
            _computer = computer
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.ImpostaComputer", ex)
            Throw
        End Try
    End Sub

    Private Function EComputer(p As Integer) As Boolean
        Try
            Return _computer IsNot Nothing AndAlso p < _computer.Length AndAlso _computer(p)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.EComputer", ex)
            Throw
        End Try
    End Function

    Public Function Inizio() As List(Of EventoMotore) Implements IMotoreGioco.Inizio
        Try
            Dim n As Integer = Flotta(_variante).Count
            Return New List(Of EventoMotore) From {New EventoMotore(Nothing, "Si gioca a battaglia navale, " & Nome(_variante) & ": " & n.ToString() & " navi" & If(Distanza(_variante), " che non si possono toccare", "") & ". Ognuno posiziona la sua flotta senza farla vedere, poi si spara a turno.")}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Inizio", ex)
            Throw
        End Try
    End Function

    ''' <summary>Chi deve agire adesso: nel posizionamento chi non è ancora pronto (prima le persone, poi il computer), poi chi ha il turno.</summary>
    Public ReadOnly Property Attore As Integer Implements IMotoreGioco.Attore
        Get
            If _fase = "posizionamento" Then
                For i As Integer = 0 To 1
                    If Not _pronto(i) AndAlso Not EComputer(i) Then Return i
                Next
                For i As Integer = 0 To 1
                    If Not _pronto(i) Then Return i
                Next
            End If
            Return _turno
        End Get
    End Property

    ' ------------------------------------------------------------------ il casellario
    Public Shared Function Coordinate(r As Integer, c As Integer) As String
        Try
            Return ChrW(65 + c) & " " & (r + 1).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Coordinate", ex)
            Throw
        End Try
    End Function

    Private Shared Function Dentro(r As Integer, c As Integer) As Boolean
        Try
            Return r >= 0 AndAlso r < RIGHE AndAlso c >= 0 AndAlso c < COLONNE
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Dentro", ex)
            Throw
        End Try
    End Function

    Private Shared Function Chiave(r As Integer, c As Integer) As String
        Try
            Return r.ToString() & "," & c.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Chiave", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le caselle occupate da una flotta: "r,c" verso l'indice della nave.</summary>
    Private Shared Function Occupate(navi As List(Of Nave)) As Dictionary(Of String, Integer)
        Try
            Dim o As New Dictionary(Of String, Integer)()
            For k As Integer = 0 To navi.Count - 1
                For Each x As Integer() In navi(k).Celle
                    o(Chiave(x(0), x(1))) = k
                Next
            Next
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Occupate", ex)
            Throw
        End Try
    End Function

    Private Shared Function CellePerNave(lunghezza As Integer, r As Integer, c As Integer, orizzontale As Boolean) As List(Of Integer())
        Try
            Dim x As New List(Of Integer())()
            For i As Integer = 0 To lunghezza - 1
                x.Add({r + If(orizzontale, 0, i), c + If(orizzontale, i, 0)})
            Next
            Return x
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.CellePerNave", ex)
            Throw
        End Try
    End Function

    Private Shared Function PuoPosizionare(navi As List(Of Nave), distanza As Boolean, lunghezza As Integer, r As Integer, c As Integer, orizzontale As Boolean) As Boolean
        Try
            Dim occ As Dictionary(Of String, Integer) = Occupate(navi)
            For Each x As Integer() In CellePerNave(lunghezza, r, c, orizzontale)
                If Not Dentro(x(0), x(1)) OrElse occ.ContainsKey(Chiave(x(0), x(1))) Then Return False
                If distanza Then
                    For dr As Integer = -1 To 1
                        For dc As Integer = -1 To 1
                            If occ.ContainsKey(Chiave(x(0) + dr, x(1) + dc)) Then Return False
                        Next
                    Next
                End If
            Next
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.PuoPosizionare", ex)
            Throw
        End Try
    End Function

    ''' <summary>Mette a caso le navi che mancano (se non ci riesce toglie quelle messe e ricomincia).</summary>
    Private Sub PosizionaACaso(p As Integer)
        Try
            Dim dist As Boolean = Distanza(_variante)
            For giro As Integer = 0 To 59
                Dim navi As New List(Of Nave)(_navi(p))
                Dim da As New List(Of Integer)(_da(p))
                If giro >= 30 Then
                    navi = New List(Of Nave)()
                    da = Flotta(_variante)
                End If
                Dim ok As Boolean = True
                For Each l As Integer In da
                    Dim messa As Boolean = False
                    Dim t As Integer = 0
                    While t < 300 AndAlso Not messa
                        t += 1
                        Dim o As Boolean = Attrezzi.Casuale(0, 2) = 1
                        Dim r As Integer = Attrezzi.Casuale(0, RIGHE)
                        Dim c As Integer = Attrezzi.Casuale(0, COLONNE)
                        If PuoPosizionare(navi, dist, l, r, c, o) Then
                            navi.Add(New Nave With {.L = l, .Celle = CellePerNave(l, r, c, o), .Colpite = 0})
                            messa = True
                        End If
                    End While
                    If Not messa Then
                        ok = False
                        Exit For
                    End If
                Next
                If ok Then
                    _navi(p).Clear()
                    _navi(p).AddRange(navi)
                    _da(p).Clear()
                    Return
                End If
            Next
            Throw New InvalidOperationException("Non riesco a posizionare le navi.")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.PosizionaACaso", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Affondata(n As Nave) As Boolean
        Try
            Return n.Colpite >= n.L
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Affondata", ex)
            Throw
        End Try
    End Function

    Private Function NaviRimaste(p As Integer) As Integer
        Try
            Return _navi(p).Where(Function(n) Not Affondata(n)).Count()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.NaviRimaste", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ mosse
    Public Function Mosse(posto As Integer) As List(Of MossaMotore) Implements IMotoreGioco.Mosse
        Try
            Dim m As New List(Of MossaMotore)()
            If _finita OrElse posto < 0 OrElse posto > 1 Then Return m
            If _fase = "posizionamento" Then
                If _pronto(posto) Then Return m
                If _da(posto).Count > 0 Then
                    Dim l As Integer = _da(posto)(0)
                    Dim nave As New MossaMotore("nave", "Posiziona la nave da " & l.ToString() & " caselle")
                    nave.Parametro = New JObject From {{"tipo", "casella"}}
                    m.Add(nave)
                    m.Add(New MossaMotore("casuale", "Metti a caso le navi che mancano"))
                End If
                If _navi(posto).Count > 0 Then m.Add(New MossaMotore("annulla", "Togli l'ultima nave messa"))
                If _da(posto).Count = 0 Then m.Add(New MossaMotore("pronto", "La flotta è pronta, si comincia"))
                Return m
            End If
            If _turno = posto Then
                Dim spara As New MossaMotore("spara", "Spara su una casella")
                spara.Parametro = New JObject From {{"tipo", "casella"}}
                m.Add(spara)
            End If
            Return m
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Mosse", ex)
            Throw
        End Try
    End Function

    Public Function Opzioni(posto As Integer) As JArray Implements IMotoreGioco.Opzioni
        Try
            Return New JArray()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Opzioni", ex)
            Throw
        End Try
    End Function

    ''' <summary>Controlla che la mossa sia ammessa. Solleva MossaNonConsentita altrimenti.</summary>
    Private Sub Controlla(p As Integer, id As String)
        Try
            If _finita Then Throw New MossaNonConsentita()
            Dim parti As String() = id.Split("|"c)
            If _fase = "posizionamento" Then
                If _pronto(p) Then Throw New MossaNonConsentita()
                Select Case parti(0)
                    Case "nave"
                        Dim r As Integer, c As Integer
                        If parti.Length <> 4 OrElse _da(p).Count = 0 OrElse (parti(3) <> "o" AndAlso parti(3) <> "v") OrElse Not Integer.TryParse(parti(1), r) OrElse Not Integer.TryParse(parti(2), c) Then Throw New MossaNonConsentita()
                        If Not PuoPosizionare(_navi(p), Distanza(_variante), _da(p)(0), r, c, parti(3) = "o") Then Throw New MossaNonConsentita()
                    Case "casuale"
                        If _da(p).Count = 0 Then Throw New MossaNonConsentita()
                    Case "annulla"
                        If _navi(p).Count = 0 Then Throw New MossaNonConsentita()
                    Case "pronto"
                        If _da(p).Count > 0 Then Throw New MossaNonConsentita()
                    Case Else
                        Throw New MossaNonConsentita()
                End Select
                Return
            End If
            Dim rr As Integer, cc As Integer
            If parti(0) <> "spara" OrElse parti.Length <> 3 OrElse _turno <> p OrElse Not Integer.TryParse(parti(1), rr) OrElse Not Integer.TryParse(parti(2), cc) Then Throw New MossaNonConsentita()
            If Not Dentro(rr, cc) OrElse _colpi(p).ContainsKey(Chiave(rr, cc)) Then Throw New MossaNonConsentita()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Controlla", ex)
            Throw
        End Try
    End Sub

    ''' <summary>La flotta di questo posto è pronta; se lo sono tutti e due comincia la battaglia.</summary>
    Private Sub SegnaPronto(posto As Integer, ev As List(Of EventoMotore))
        Try
            _pronto(posto) = True
            ev.Add(New EventoMotore(Nothing, _nomi(posto) & " è pronto."))
            If _pronto(0) AndAlso _pronto(1) Then
                _fase = "battaglia"
                _turno = Attrezzi.Casuale(0, 2)
                ev.Add(New EventoMotore(Nothing, "La battaglia comincia. Tocca a " & _nomi(_turno) & "."))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.SegnaPronto", ex)
            Throw
        End Try
    End Sub

    Public Function Applica(posto As Integer, idMossa As String, verifica As Boolean) As List(Of EventoMotore) Implements IMotoreGioco.Applica
        Try
            If verifica Then Controlla(posto, idMossa)
            Dim parti As String() = idMossa.Split("|"c)
            Dim nome As String = _nomi(posto)
            Dim ev As New List(Of EventoMotore)()
            If _fase = "posizionamento" Then
                Select Case parti(0)
                    Case "nave"
                        Dim l As Integer = _da(posto)(0)
                        Dim o As Boolean = parti(3) = "o"
                        Dim celle As List(Of Integer()) = CellePerNave(l, Integer.Parse(parti(1)), Integer.Parse(parti(2)), o)
                        _navi(posto).Add(New Nave With {.L = l, .Celle = celle, .Colpite = 0})
                        _da(posto).RemoveAt(0)
                        Dim fine As Integer() = celle(l - 1)
                        ev.Add(New EventoMotore(posto, "Nave da " & l.ToString() & " posizionata, " & If(o, "orizzontale", "verticale") & ", da " & Coordinate(celle(0)(0), celle(0)(1)) & " a " & Coordinate(fine(0), fine(1)) & ".", "posiziona"))
                    Case "casuale"
                        PosizionaACaso(posto)
                        ev.Add(New EventoMotore(posto, "Le navi sono state messe a caso: la flotta è completa.", "posiziona"))
                    Case "annulla"
                        Dim nave As Nave = _navi(posto)(_navi(posto).Count - 1)
                        _navi(posto).RemoveAt(_navi(posto).Count - 1)
                        _da(posto).Insert(0, nave.L)
                        ev.Add(New EventoMotore(posto, "Tolta la nave da " & nave.L.ToString() & "."))
                    Case "pronto"
                        SegnaPronto(posto, ev)
                    Case Else
                        Throw New MossaNonConsentita()
                End Select
                ' messa l'ultima nave la flotta è pronta da sola: non c'è altro da fare e non serve premere niente
                If (parti(0) = "nave" OrElse parti(0) = "casuale") AndAlso _da(posto).Count = 0 AndAlso Not _pronto(posto) Then SegnaPronto(posto, ev)
                Return ev
            End If

            If parti(0) <> "spara" Then Throw New MossaNonConsentita()
            Dim r2 As Integer = Integer.Parse(parti(1))
            Dim c2 As Integer = Integer.Parse(parti(2))
            Dim altro As Integer = 1 - posto
            Dim ch As String = Chiave(r2, c2)
            ev.Add(New EventoMotore(Nothing, nome & " spara su " & Coordinate(r2, c2) & ".", "cannone"))
            Dim occ As Dictionary(Of String, Integer) = Occupate(_navi(altro))
            _ultimoColpo = Attrezzi.J("posto", posto, "riga", r2, "colonna", c2, "esito", "acqua")
            If Not occ.ContainsKey(ch) Then
                _colpi(posto)(ch) = "o"
                ev.Add(New EventoMotore(Nothing, "Acqua! " & Scelta(INSULTI_ACQUA), "acqua"))
                _turno = altro
                Return ev
            End If
            Dim k As Integer = occ(ch)
            _colpi(posto)(ch) = "X"
            Dim nv As Nave = _navi(altro)(k)
            nv.Colpite += 1
            If Not Affondata(nv) Then
                _ultimoColpo("esito") = "colpita"
                ev.Add(New EventoMotore(Nothing, "Colpito! " & Scelta(COMPLIMENTI_COLPITO), "colpito"))
            Else
                _ultimoColpo("esito") = "affondata"
                ev.Add(New EventoMotore(Nothing, "Colpito e affondato! " & Scelta(COMPLIMENTI_AFFONDATA) & " " & _nomi(altro) & " perde la nave da " & nv.L.ToString() & ".", "affonda"))
                If NaviRimaste(altro) = 0 Then
                    _finita = True
                    _vincitore = posto
                    ev.Add(New EventoMotore(Nothing, "La partita è finita: tutte le navi di " & _nomi(altro) & " sono affondate. Vince " & nome & ".", "fine"))
                    Return ev
                End If
            End If
            If Not Raffica(_variante) Then _turno = altro
            Return ev
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Applica", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ viste
    Private Function MareMio(p As Integer) As JArray
        Try
            Dim g(RIGHE - 1)() As Char
            For r As Integer = 0 To RIGHE - 1
                g(r) = New String("."c, COLONNE).ToCharArray()
            Next
            For Each n As Nave In _navi(p)
                For Each x As Integer() In n.Celle
                    g(x(0))(x(1)) = "N"c
                Next
            Next
            Dim occ As Dictionary(Of String, Integer) = Occupate(_navi(p))
            For Each kv As KeyValuePair(Of String, String) In _colpi(1 - p)
                Dim q As String() = kv.Key.Split(","c)
                Dim r As Integer = Integer.Parse(q(0))
                Dim c As Integer = Integer.Parse(q(1))
                g(r)(c) = If(kv.Value = "o", "o"c, If(Affondata(_navi(p)(occ(kv.Key))), "#"c, "X"c))
            Next
            Return New JArray(g.Select(Function(riga) CType(New String(riga), Object)).ToArray())
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.MareMio", ex)
            Throw
        End Try
    End Function

    Private Function MareNemico(p As Integer, svelato As Boolean) As JArray
        Try
            Dim g(RIGHE - 1)() As Char
            For r As Integer = 0 To RIGHE - 1
                g(r) = New String("."c, COLONNE).ToCharArray()
            Next
            Dim altro As Integer = 1 - p
            If svelato Then
                For Each n As Nave In _navi(altro)
                    For Each x As Integer() In n.Celle
                        g(x(0))(x(1)) = "N"c
                    Next
                Next
            End If
            Dim occ As Dictionary(Of String, Integer) = Occupate(_navi(altro))
            For Each kv As KeyValuePair(Of String, String) In _colpi(p)
                Dim q As String() = kv.Key.Split(","c)
                Dim r As Integer = Integer.Parse(q(0))
                Dim c As Integer = Integer.Parse(q(1))
                g(r)(c) = If(kv.Value = "o", "o"c, If(Affondata(_navi(altro)(occ(kv.Key))), "#"c, "X"c))
            Next
            Return New JArray(g.Select(Function(riga) CType(New String(riga), Object)).ToArray())
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.MareNemico", ex)
            Throw
        End Try
    End Function

    Public Function Vista(posto As Integer) As JObject Implements IMotoreGioco.Vista
        Try
            Dim v As JObject = Attrezzi.J("tipo", "battaglia", "variante", _variante, "fase", If(_finita, "finita", _fase),
                                          "righe", RIGHE, "colonne", COLONNE, "distanza", Distanza(_variante), "raffica", Raffica(_variante),
                                          "turno", _turno, "pronto", New JArray(_pronto.Select(Function(x) CType(x, Object)).ToArray()), "finita", _finita,
                                          "vincitore", If(_vincitore.HasValue, CType(_vincitore.Value, Object), Nothing), "ultimo_colpo", _ultimoColpo)
            If posto < 0 OrElse posto > 1 Then Return v
            Dim altro As Integer = 1 - posto
            v("mia") = MareMio(posto)
            v("nemica") = MareNemico(posto, _finita)
            v("da_posizionare") = New JArray(_da(posto).Select(Function(x) CType(x, Object)).ToArray())
            v("prossima") = If(_da(posto).Count > 0, _da(posto)(0), 0)
            Dim mie As New JArray()
            For Each n As Nave In _navi(posto)
                mie.Add(Attrezzi.J("l", n.L, "colpite", n.Colpite, "affondata", Affondata(n)))
            Next
            v("mie_navi") = mie
            Dim perse As New List(Of Integer)()
            Dim vive As New List(Of Integer)()
            For Each n As Nave In _navi(altro)
                If Affondata(n) Then perse.Add(n.L) Else vive.Add(n.L)
            Next
            v("navi_nemiche_perse") = New JArray(perse.Select(Function(x) CType(x, Object)).ToArray())
            v("navi_nemiche_numero") = If(_navi(altro).Count > 0, _navi(altro).Count, Flotta(_variante).Count)
            v("navi_nemiche_rimaste") = If(_finita, CType(New JArray(vive.Select(Function(x) CType(x, Object)).ToArray()), JToken), JValue.CreateNull())
            v("mie_navi_rimaste") = NaviRimaste(posto)
            v("nemiche_rimaste") = Flotta(_variante).Count - perse.Count
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Vista", ex)
            Throw
        End Try
    End Function

    Private Shared Function ElencoNavi(lunghezze As IEnumerable(Of Integer)) As String
        Try
            Dim t As New List(Of String)()
            For Each g As IGrouping(Of Integer, Integer) In lunghezze.GroupBy(Function(x) x).OrderByDescending(Function(x) x.Key)
                t.Add(g.Count().ToString() & " da " & g.Key.ToString())
            Next
            Return If(t.Count > 0, String.Join(", ", t), "nessuna")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.ElencoNavi", ex)
            Throw
        End Try
    End Function

    Public Function Situazione(posto As Integer) As String Implements IMotoreGioco.Situazione
        Try
            If posto < 0 OrElse posto > 1 Then Return ""
            Dim altro As Integer = 1 - posto
            If _fase = "posizionamento" Then
                Dim t As String = "Posizionamento. Hai messo " & _navi(posto).Count.ToString() & " navi su " & Flotta(_variante).Count.ToString() & "."
                If _da(posto).Count > 0 Then
                    t &= " Mancano: " & ElencoNavi(_da(posto)) & ". Ora la nave da " & _da(posto)(0).ToString() & "."
                ElseIf Not _pronto(posto) Then
                    t &= " La flotta è completa: di' che sei pronto."
                Else
                    t &= " Sei pronto: aspetti " & _nomi(altro) & "."
                End If
                Return t
            End If
            Dim perse As List(Of Integer) = _navi(altro).Where(Function(n) Affondata(n)).Select(Function(n) n.L).ToList()
            Dim sparati As Integer = _colpi(posto).Count
            Dim colpite As Integer = _colpi(posto).Values.Where(Function(x) x = "X").Count()
            Dim s As String = "Hai ancora " & NaviRimaste(posto).ToString() & " navi, " & _nomi(altro) & " ne ha " & (Flotta(_variante).Count - perse.Count).ToString() & ". Hai sparato " & sparati.ToString() & " colpi: " & colpite.ToString() & " sulle navi e " & (sparati - colpite).ToString() & " in acqua."
            If perse.Count > 0 Then s &= " Hai affondato: " & ElencoNavi(perse) & "."
            s &= If(_finita, " La partita è finita.", " Tocca a " & _nomi(_turno) & ".")
            Return s
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Situazione", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ computer
    ''' <summary>
    ''' Il computer spara dove è più probabile che stia una nave: per ogni nave che manca si contano tutti i modi di metterla nel mare nemico senza
    ''' andare contro quello che si sa (acqua, navi affondate e, se le navi non si toccano, le caselle intorno alle navi affondate); le caselle
    ''' che stanno in più modi valgono di più, e molto di più quelle che prolungano una nave già colpita e non ancora affondata.
    ''' </summary>
    Private Function SceltaColpo(p As Integer) As String
        Try
            Dim altro As Integer = 1 - p
            Dim acqua As New HashSet(Of String)()
            Dim colpite As New HashSet(Of String)()
            Dim occ As Dictionary(Of String, Integer) = Occupate(_navi(altro))
            For Each kv As KeyValuePair(Of String, String) In _colpi(p)
                If kv.Value = "o" Then
                    acqua.Add(kv.Key)
                ElseIf Affondata(_navi(altro)(occ(kv.Key))) Then
                    acqua.Add(kv.Key)   ' una nave affondata non si ricolpisce e non serve ad altre navi
                Else
                    colpite.Add(kv.Key)
                End If
            Next
            Dim dist As Boolean = Distanza(_variante)
            Dim mancano As List(Of Integer) = Flotta(_variante)
            For Each n As Nave In _navi(altro)
                If Not Affondata(n) Then Continue For
                mancano.Remove(n.L)
                If dist Then
                    For Each x As Integer() In n.Celle
                        For dr As Integer = -1 To 1
                            For dc As Integer = -1 To 1
                                Dim q As String = Chiave(x(0) + dr, x(1) + dc)
                                If Not _colpi(p).ContainsKey(q) OrElse _colpi(p)(q) = "o" Then acqua.Add(q)
                            Next
                        Next
                    Next
                End If
            Next
            Dim peso As New Dictionary(Of String, Integer)()
            For Each l As Integer In mancano
                For Each o As Boolean In {True, False}
                    For r As Integer = 0 To RIGHE - 1
                        For c As Integer = 0 To COLONNE - 1
                            Dim celle As List(Of Integer()) = CellePerNave(l, r, c, o)
                            Dim ok As Boolean = True
                            Dim toccate As Integer = 0
                            For Each x As Integer() In celle
                                Dim q As String = Chiave(x(0), x(1))
                                If Not Dentro(x(0), x(1)) OrElse acqua.Contains(q) Then
                                    ok = False
                                    Exit For
                                End If
                                If colpite.Contains(q) Then toccate += 1
                            Next
                            If Not ok Then Continue For
                            Dim valore As Integer = If(toccate > 0, 40 * toccate, 1)
                            For Each x As Integer() In celle
                                Dim q As String = Chiave(x(0), x(1))
                                If Not _colpi(p).ContainsKey(q) Then peso(q) = If(peso.ContainsKey(q), peso(q), 0) + valore
                            Next
                        Next
                    Next
                Next
            Next
            Dim migliori As New List(Of String)()
            Dim massimo As Integer = 0
            For Each kv As KeyValuePair(Of String, Integer) In peso
                If kv.Value > massimo Then
                    massimo = kv.Value
                    migliori.Clear()
                    migliori.Add(kv.Key)
                ElseIf kv.Value = massimo Then
                    migliori.Add(kv.Key)
                End If
            Next
            If migliori.Count = 0 Then
                For r As Integer = 0 To RIGHE - 1
                    For c As Integer = 0 To COLONNE - 1
                        If Not _colpi(p).ContainsKey(Chiave(r, c)) Then migliori.Add(Chiave(r, c))
                    Next
                Next
            End If
            Dim scelta As String = migliori(Attrezzi.Casuale(0, migliori.Count))
            Return "spara|" & scelta.Replace(",", "|")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.SceltaColpo", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una mossa a caso per il computer ai livelli facili: in posizionamento mette le navi a caso, in battaglia spara su una casella non ancora colpita.</summary>
    Public Function MossaCasuale(posto As Integer) As String
        Try
            If _fase = "posizionamento" Then Return If(_da(posto).Count > 0, "casuale", Nothing)
            Dim libere As New List(Of String)()
            For r As Integer = 0 To RIGHE - 1
                For c As Integer = 0 To COLONNE - 1
                    If Not _colpi(posto).ContainsKey(Chiave(r, c)) Then libere.Add("spara|" & r.ToString() & "|" & c.ToString())
                Next
            Next
            If libere.Count = 0 Then Return Nothing
            Return libere(Attrezzi.Casuale(0, libere.Count))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.MossaCasuale", ex)
            Throw
        End Try
    End Function

    Public Function Bot(posto As Integer) As String Implements IMotoreGioco.Bot
        Try
            If _fase = "posizionamento" Then Return If(_da(posto).Count > 0, "casuale", "pronto")
            Return SceltaColpo(posto)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBattaglia.Bot", ex)
            Throw
        End Try
    End Function

End Class
