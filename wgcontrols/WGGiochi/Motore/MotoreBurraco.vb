Imports Newtonsoft.Json.Linq

''' <summary>
''' Burraco italiano per 2 giocatori o 4 (a coppie: 0 e 2 contro 1 e 3), a punti. Stesse regole di burraco.php:
''' 108 carte, jolly e 2 come pinella, pozzo congelato, pozzetti, minimo di apertura con calate provvisorie, chiusura con burraco.
''' </summary>
Friend Class MotoreBurraco
    Implements IMotoreGioco

    Private Shared ReadOnly SEMI As String() = {"cuori", "quadri", "fiori", "picche"}

    ' ------------------------------------------------------------------ carte
    Public Shared Function Seme(id As Integer) As Integer
        Try
            Dim k As Integer = id Mod 54
            Return If(k >= 52, -1, k \ 13)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Seme", ex)
            Throw
        End Try
    End Function

    Public Shared Function Rango(id As Integer) As Integer
        Try
            Dim k As Integer = id Mod 54
            Return If(k >= 52, 0, (k Mod 13) + 1)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Rango", ex)
            Throw
        End Try
    End Function

    Public Shared Function EJolly(id As Integer) As Boolean
        Try
            Return id Mod 54 >= 52
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.EJolly", ex)
            Throw
        End Try
    End Function

    Public Shared Function PuoEssereJolly(id As Integer) As Boolean
        Try
            Return EJolly(id) OrElse Rango(id) = 2
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.PuoEssereJolly", ex)
            Throw
        End Try
    End Function

    Public Shared Function NomeRango(r As Integer) As String
        Try
            Select Case r
                Case 1, 14 : Return "Asso"
                Case 11 : Return "Fante"
                Case 12 : Return "Donna"
                Case 13 : Return "Re"
                Case 2 To 10 : Return r.ToString()
                Case Else : Return "?"
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.NomeRango", ex)
            Throw
        End Try
    End Function

    Public Shared Function Nome(id As Integer) As String
        Try
            If EJolly(id) Then Return "Jolly"
            Return NomeRango(Rango(id)) & " di " & SEMI(Seme(id))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Nome", ex)
            Throw
        End Try
    End Function

    Public Shared Function Valore(id As Integer) As Integer
        Try
            If EJolly(id) Then Return 30
            Dim r As Integer = Rango(id)
            If r = 1 Then Return 15
            If r = 2 Then Return 20
            Return If(r >= 8, 10, 5)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Valore", ex)
            Throw
        End Try
    End Function

    Public Shared Function CartaJson(id As Integer) As JObject
        Try
            Return Attrezzi.J("id", id, "nome", Nome(id), "seme", Seme(id), "rango", Rango(id), "jolly", EJolly(id))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.CartaJson", ex)
            Throw
        End Try
    End Function

    Private Shared Function ChiaveOrdine(id As Integer) As Integer
        Try
            If EJolly(id) Then Return 1000 + id
            Return Seme(id) * 100 + Rango(id) * 2 + id \ 54
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ChiaveOrdine", ex)
            Throw
        End Try
    End Function

    Public Shared Function Ordina(carte As IEnumerable(Of Integer)) As List(Of Integer)
        Try
            Return carte.OrderBy(Function(c) ChiaveOrdine(c)).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Ordina", ex)
            Throw
        End Try
    End Function

    Public Shared Function ElencaCarte(ids As IEnumerable(Of Integer)) As String
        Try
            Return String.Join(", ", Ordina(ids).Select(Function(i) Nome(i)))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ElencaCarte", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ combinazioni
    Friend Class Slot
        Public Id As Integer
        Public RangoSlot As Integer
        Public Jolly As Boolean
    End Class

    Friend Class Comb
        Public Tipo As String            ' "tris" o "scala"
        Public RangoTris As Integer
        Public JollyTris As Integer? = Nothing
        Public SemeScala As Integer
        Public Slots As List(Of Slot)
        Public Carte As List(Of Integer)
    End Class

    Private Class SquadraStato
        Public Combinazioni As New List(Of Comb)()
        Public Pozzetto As Boolean = False
        Public Aperta As Boolean = False
    End Class

    Public Shared Function Combinazione(carte As List(Of Integer)) As Comb
        Try
            Dim n As Integer = carte.Count
            If n < 3 OrElse n > 14 Then Return Nothing
            Dim t As Comb = ComeTris(carte)
            If t IsNot Nothing Then Return t
            Return ComeScala(carte)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Combinazione", ex)
            Throw
        End Try
    End Function

    Private Shared Function ComeTris(carte As List(Of Integer)) As Comb
        Try
            Dim n As Integer = carte.Count
            For r As Integer = 1 To 13
                Dim nat As New List(Of Integer)()
                Dim jol As New List(Of Integer)()
                Dim ok As Boolean = True
                For Each id As Integer In carte
                    If Not EJolly(id) AndAlso Rango(id) = r Then
                        nat.Add(id)
                    ElseIf PuoEssereJolly(id) Then
                        jol.Add(id)
                    Else
                        ok = False
                        Exit For
                    End If
                Next
                If ok AndAlso nat.Count >= 2 AndAlso jol.Count <= 1 AndAlso nat.Count + jol.Count = n Then
                    Return New Comb With {.Tipo = "tris", .RangoTris = r, .Carte = Ordina(carte), .JollyTris = If(jol.Count > 0, CType(jol(0), Integer?), Nothing)}
                End If
            Next
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ComeTris", ex)
            Throw
        End Try
    End Function

    Private Shared Function ComeScala(carte As List(Of Integer)) As Comb
        Try
            Dim n As Integer = carte.Count
            For s As Integer = 0 To 3
                For inizio As Integer = 1 To 14 - n + 1
                    Dim usate As New HashSet(Of Integer)()
                    Dim slot As New List(Of Slot)()
                    Dim mancanti As New List(Of Integer)()
                    For p As Integer = inizio To inizio + n - 1
                        Dim rk As Integer = If(p > 13, 1, p)
                        Dim trovata As Integer = -1
                        For i As Integer = 0 To carte.Count - 1
                            Dim id As Integer = carte(i)
                            If Not usate.Contains(i) AndAlso Not EJolly(id) AndAlso Seme(id) = s AndAlso Rango(id) = rk Then
                                trovata = i
                                Exit For
                            End If
                        Next
                        If trovata >= 0 Then
                            usate.Add(trovata)
                            slot.Add(New Slot With {.Id = carte(trovata), .RangoSlot = p, .Jolly = False})
                        Else
                            slot.Add(Nothing)
                            mancanti.Add(slot.Count - 1)
                        End If
                    Next
                    Dim avanzi As New List(Of Integer)()
                    For i As Integer = 0 To carte.Count - 1
                        If Not usate.Contains(i) Then avanzi.Add(carte(i))
                    Next
                    If mancanti.Count > 1 OrElse avanzi.Count <> mancanti.Count Then Continue For
                    If mancanti.Count = 1 Then
                        If Not PuoEssereJolly(avanzi(0)) Then Continue For
                        slot(mancanti(0)) = New Slot With {.Id = avanzi(0), .RangoSlot = inizio + mancanti(0), .Jolly = True}
                    End If
                    Return New Comb With {.Tipo = "scala", .SemeScala = s, .Slots = slot, .Carte = slot.Select(Function(x) x.Id).ToList()}
                Next
            Next
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ComeScala", ex)
            Throw
        End Try
    End Function

    Public Shared Function EBurraco(c As Comb) As Boolean
        Try
            Return c.Carte.Count >= 7
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.EBurraco", ex)
            Throw
        End Try
    End Function

    Public Shared Function EBurracoPulito(c As Comb) As Boolean
        Try
            If Not EBurraco(c) Then Return False
            If c.Tipo = "tris" Then Return Not c.JollyTris.HasValue
            Return Not c.Slots.Any(Function(x) x.Jolly)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.EBurracoPulito", ex)
            Throw
        End Try
    End Function

    Private Shared Function ENaturale(c As Comb) As Boolean
        Try
            If c.Tipo = "tris" Then Return Not c.JollyTris.HasValue
            Return Not c.Slots.Any(Function(x) x.Jolly)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ENaturale", ex)
            Throw
        End Try
    End Function

    Public Shared Function Descrivi(c As Comb) As String
        Try
            Dim n As Integer = c.Carte.Count
            Dim extra As String = ""
            If EBurraco(c) Then extra = If(EBurracoPulito(c), ", burraco pulito", ", burraco sporco")
            If c.Tipo = "tris" Then
                Dim nomeR As String
                Select Case c.RangoTris
                    Case 1 : nomeR = "assi"
                    Case 11 : nomeR = "fanti"
                    Case 12 : nomeR = "donne"
                    Case 13 : nomeR = "re"
                    Case Else : nomeR = c.RangoTris.ToString()
                End Select
                Return "tris di " & nomeR & " (" & n.ToString() & " carte" & extra & ")"
            End If
            Dim primo As Integer = c.Slots(0).RangoSlot
            Dim ultimo As Integer = c.Slots(n - 1).RangoSlot
            Dim t As String = "scala di " & SEMI(c.SemeScala) & " dal " & NomeRango(primo) & " al " & NomeRango(ultimo)
            For Each x As Slot In c.Slots
                If x.Jolly Then
                    t &= ", con " & Nome(x.Id) & " al posto del " & NomeRango(x.RangoSlot)
                    Exit For
                End If
            Next
            Return t & " (" & n.ToString() & " carte" & extra & ")"
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Descrivi", ex)
            Throw
        End Try
    End Function

    Public Shared Function PuntiCombinazione(c As Comb) As Integer
        Try
            Dim p As Integer = c.Carte.Sum(Function(id) Valore(id))
            If EBurraco(c) Then p += If(EBurracoPulito(c), 200, 100)
            Return p
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.PuntiCombinazione", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le combinazioni nuove che si possono calare con queste carte, senza quelle contenute in un'altra.</summary>
    Public Shared Function NuoveCombinazioni(manoIn As List(Of Integer)) As List(Of List(Of Integer))
        Try
            Dim risultato As New List(Of List(Of Integer))()
            Dim mano As List(Of Integer) = New List(Of Integer)(manoIn)
            Dim jollyInMano As List(Of Integer) = mano.Where(Function(id) PuoEssereJolly(id)).ToList()

            For r As Integer = 1 To 13
                Dim rr As Integer = r
                Dim nat As List(Of Integer) = Ordina(mano.Where(Function(id) Not EJolly(id) AndAlso Rango(id) = rr))
                If nat.Count >= 3 Then
                    risultato.Add(nat)
                ElseIf nat.Count = 2 Then
                    For Each j As Integer In jollyInMano
                        If Rango(j) = r Then Continue For
                        Dim x As New List(Of Integer)(nat)
                        x.Add(j)
                        risultato.Add(x)
                        Exit For
                    Next
                End If
            Next

            ' scale: per ogni seme le carte vere, poi tutte le finestre di rango con al massimo un buco da riempire con un jolly
            Dim vere As New SortedDictionary(Of Integer, SortedDictionary(Of Integer, List(Of Integer)))()
            For Each id As Integer In mano
                If EJolly(id) Then Continue For
                Dim s As Integer = Seme(id)
                If Not vere.ContainsKey(s) Then vere(s) = New SortedDictionary(Of Integer, List(Of Integer))()
                If Not vere(s).ContainsKey(Rango(id)) Then vere(s)(Rango(id)) = New List(Of Integer)()
                vere(s)(Rango(id)).Add(id)
            Next
            For Each kv In vere
                Dim perRango As SortedDictionary(Of Integer, List(Of Integer)) = kv.Value
                If perRango.Count < 2 Then Continue For
                For n As Integer = 14 To 3 Step -1
                    For inizio As Integer = 1 To 14 - n + 1
                        Dim carte As New List(Of Integer)()
                        Dim uso As New Dictionary(Of Integer, Integer)()
                        Dim mancanti As Integer = 0
                        For p As Integer = inizio To inizio + n - 1
                            Dim rk As Integer = If(p > 13, 1, p)
                            Dim k As Integer = 0
                            uso.TryGetValue(rk, k)
                            If perRango.ContainsKey(rk) AndAlso k < perRango(rk).Count Then
                                carte.Add(perRango(rk)(k))
                                uso(rk) = k + 1
                            Else
                                mancanti += 1
                                If mancanti > 1 Then Exit For
                            End If
                        Next
                        If mancanti > 1 OrElse carte.Count < 2 Then Continue For
                        If mancanti = 1 Then
                            Dim jolly As Integer? = Nothing
                            For Each j As Integer In jollyInMano
                                If Not carte.Contains(j) Then
                                    jolly = j
                                    Exit For
                                End If
                            Next
                            If Not jolly.HasValue Then Continue For
                            carte.Add(jolly.Value)
                        End If
                        risultato.Add(Ordina(carte))
                    Next
                Next
            Next

            ' niente doppioni e niente combinazioni contenute in altre
            Dim visti As New HashSet(Of String)()
            Dim elenco As New List(Of List(Of Integer))()
            For Each ids As List(Of Integer) In risultato
                Dim chiave As String = String.Join(",", ids)
                If visti.Add(chiave) Then elenco.Add(ids)
            Next
            Dim tenute As New List(Of List(Of Integer))()
            For i As Integer = 0 To elenco.Count - 1
                Dim a As List(Of Integer) = elenco(i)
                Dim contenuta As Boolean = False
                For j As Integer = 0 To elenco.Count - 1
                    Dim b As List(Of Integer) = elenco(j)
                    If i <> j AndAlso b.Count > a.Count AndAlso a.All(Function(x) b.Contains(x)) Then
                        contenuta = True
                        Exit For
                    End If
                Next
                If Not contenuta Then tenute.Add(a)
            Next
            Return tenute
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.NuoveCombinazioni", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ partita
    Private _n As Integer
    Private _nomi As List(Of String)
    Private _obiettivo As Integer
    Private _totali As Integer() = {0, 0}
    Private _manoNumero As Integer = 1
    Private _finita As Boolean = False
    Private _vincitori As New List(Of Integer)()
    Private _punteggi As Integer()() = Nothing   ' per squadra: tavolo, chiusura, pozzetto, in_mano, totale
    Private _motivo As String = ""
    Private _mani As List(Of List(Of Integer))
    Private _pozzetti As List(Of List(Of Integer))
    Private _scarti As List(Of Integer)
    Private _mazzo As List(Of Integer)
    Private _squadre As SquadraStato()
    Private _turno As Integer
    Private _fase As String
    Private _chiusaDa As Integer? = Nothing
    Private _provvPosto As Integer = -1
    Private _provvMano As List(Of Integer) = Nothing
    Private _rinuncia As Boolean = False

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

    Public Sub New(nomi As List(Of String), obiettivo As Integer)
        Try
            Dim n As Integer = nomi.Count
            If n <> 2 AndAlso n <> 4 Then Throw New ArgumentException("Il burraco si gioca in 2 o in 4.")
            _n = n
            _nomi = New List(Of String)(nomi)
            _obiettivo = Math.Max(500, Math.Min(5005, obiettivo))
            DaiCarte(0)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.New", ex)
            Throw
        End Try
    End Sub

    Private Function SquadraDi(posto As Integer) As Integer
        Try
            Return If(_n = 4, posto Mod 2, posto)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.SquadraDi", ex)
            Throw
        End Try
    End Function

    Private Shared Function Preleva(l As List(Of Integer), n As Integer) As List(Of Integer)
        Try
            Dim r As List(Of Integer) = l.GetRange(0, n)
            l.RemoveRange(0, n)
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Preleva", ex)
            Throw
        End Try
    End Function

    Private Sub DaiCarte(primo As Integer)
        Try
            Dim mazzo As New List(Of Integer)(Enumerable.Range(0, 108))
            Attrezzi.Mescola(mazzo)
            _mani = New List(Of List(Of Integer))()
            For i As Integer = 0 To _n - 1
                _mani.Add(Ordina(Preleva(mazzo, 11)))
            Next
            _pozzetti = New List(Of List(Of Integer)) From {Preleva(mazzo, 11), Preleva(mazzo, 11)}
            _scarti = Preleva(mazzo, 1)
            _mazzo = mazzo
            _squadre = {New SquadraStato(), New SquadraStato()}
            _turno = primo
            _fase = "pesca"
            _chiusaDa = Nothing
            _provvPosto = -1
            _provvMano = Nothing
            _rinuncia = False
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.DaiCarte", ex)
            Throw
        End Try
    End Sub

    Public ReadOnly Property Attore As Integer Implements IMotoreGioco.Attore
        Get
            Return _turno
        End Get
    End Property

    Public Function Opzioni(posto As Integer) As JArray Implements IMotoreGioco.Opzioni
        Try
            Return New JArray()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Opzioni", ex)
            Throw
        End Try
    End Function

    Public Sub ImpostaComputer(computer As Boolean()) Implements IMotoreGioco.ImpostaComputer
    End Sub

    Public Function Inizio() As List(Of EventoMotore) Implements IMotoreGioco.Inizio
        Try
            Return New List(Of EventoMotore) From {New EventoMotore(Nothing, "Si gioca a burraco fino a " & _obiettivo.ToString() & " punti. Ognuno ha 11 carte. Negli scarti c'è " & Nome(_scarti(0)) & ". Inizia " & _nomi(_turno) & ".")}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Inizio", ex)
            Throw
        End Try
    End Function

    Private Function MinimoApertura(sq As Integer) As Integer
        Try
            Dim t As Integer = _totali(sq)
            Return If(t < 0, 15, If(t < 1500, 50, 90))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.MinimoApertura", ex)
            Throw
        End Try
    End Function

    Private Shared Function PuntiCarte(comb As IEnumerable(Of Comb)) As Integer
        Try
            Dim p As Integer = 0
            For Each c As Comb In comb
                For Each id As Integer In c.Carte
                    p += Valore(id)
                Next
            Next
            Return p
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.PuntiCarte", ex)
            Throw
        End Try
    End Function

    Private Function Congelato() As Boolean
        Try
            Return _scarti.Any(Function(id) PuoEssereJolly(id))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Congelato", ex)
            Throw
        End Try
    End Function

    Private Function HaBurraco(squadra As Integer) As Boolean
        Try
            Return _squadre(squadra).Combinazioni.Any(Function(c) EBurraco(c))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.HaBurraco", ex)
            Throw
        End Try
    End Function

    Private Function StatoAccettabile(posto As Integer, rimaste As Integer, dopo As List(Of Comb)) As Boolean
        Try
            Dim sq As Integer = SquadraDi(posto)
            Dim apertaDopo As Boolean = _squadre(sq).Aperta OrElse PuntiCarte(dopo) >= MinimoApertura(sq)
            If Not apertaDopo Then Return rimaste >= 1
            If rimaste >= 2 Then Return True
            Dim pozzettoPreso As Boolean = _squadre(sq).Pozzetto
            Dim haBurr As Boolean = dopo.Any(Function(c) EBurraco(c))
            Return Not pozzettoPreso OrElse haBurr
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.StatoAccettabile", ex)
            Throw
        End Try
    End Function

    Private Shared Function Distinte(mano As List(Of Integer)) As List(Of Integer)
        Try
            Dim viste As New HashSet(Of String)()
            Dim r As New List(Of Integer)()
            For Each id As Integer In Ordina(mano)
                Dim chiave As String = If(EJolly(id), "J", Seme(id).ToString() & "-" & Rango(id).ToString())
                If viste.Add(chiave) Then r.Add(id)
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Distinte", ex)
            Throw
        End Try
    End Function

    Private Shared Function ConSostituita(l As List(Of Comb), i As Integer, nuova As Comb) As List(Of Comb)
        Try
            Dim r As New List(Of Comb)(l)
            r(i) = nuova
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ConSostituita", ex)
            Throw
        End Try
    End Function

    Private Shared Function ConAggiunta(l As List(Of Comb), nuova As Comb) As List(Of Comb)
        Try
            Dim r As New List(Of Comb)(l)
            r.Add(nuova)
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ConAggiunta", ex)
            Throw
        End Try
    End Function

    Private Shared Function ConCarta(carte As List(Of Integer), id As Integer) As List(Of Integer)
        Try
            Dim r As New List(Of Integer)(carte)
            r.Add(id)
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ConCarta", ex)
            Throw
        End Try
    End Function

    Public Function Mosse(posto As Integer) As List(Of MossaMotore) Implements IMotoreGioco.Mosse
        Try
            Dim risultato As New List(Of MossaMotore)()
            If _finita OrElse posto <> _turno Then Return risultato
            Dim sq As Integer = SquadraDi(posto)
            Dim mano As List(Of Integer) = _mani(posto)
            Dim squadraStato As SquadraStato = _squadre(sq)
            Dim aperta As Boolean = squadraStato.Aperta
            Dim minimo As Integer = MinimoApertura(sq)

            If _fase = "pesca" Then
                If _mazzo.Count > 0 Then risultato.Add(New MossaMotore("pesca", "Pesca dal mazzo (" & _mazzo.Count.ToString() & " carte rimaste)"))
                If _scarti.Count > 0 Then
                    Dim cima As Integer = _scarti(_scarti.Count - 1)
                    Dim n As Integer = _scarti.Count
                    If Not PuoEssereJolly(cima) Then ' con un jolly o un 2 in cima gli scarti non si prendono
                        Dim congelato As Boolean = Me.Congelato()
                        If Not congelato AndAlso aperta Then
                            For i As Integer = 0 To squadraStato.Combinazioni.Count - 1
                                Dim c As Comb = squadraStato.Combinazioni(i)
                                Dim nuova As Comb = Combinazione(ConCarta(c.Carte, cima))
                                If nuova IsNot Nothing Then
                                    If StatoAccettabile(posto, mano.Count + n - 1, ConSostituita(squadraStato.Combinazioni, i, nuova)) Then
                                        risultato.Add(New MossaMotore("scarti|att|" & i.ToString(), "Prendi gli scarti (" & n.ToString() & " carte) e attacca " & Nome(cima) & " alla " & Descrivi(c)))
                                    End If
                                End If
                            Next
                        End If
                        Dim pool As List(Of Integer) = ConCarta(mano, cima)
                        If congelato Then pool = pool.Where(Function(id) Not PuoEssereJolly(id)).ToList()
                        For Each ids As List(Of Integer) In NuoveCombinazioni(pool)
                            If Not ids.Contains(cima) Then Continue For
                            Dim comb As Comb = Combinazione(ids)
                            If comb Is Nothing OrElse (congelato AndAlso Not ENaturale(comb)) Then Continue For
                            If Not aperta AndAlso PuntiCarte({comb}) < minimo Then Continue For
                            If StatoAccettabile(posto, mano.Count + n - ids.Count, ConAggiunta(squadraStato.Combinazioni, comb)) Then
                                risultato.Add(New MossaMotore("scarti|cala|" & String.Join(",", ids), "Prendi gli scarti (" & n.ToString() & " carte) e cala " & Descrivi(comb) & ": " & ElencaCarte(ids)))
                            End If
                        Next
                    End If
                End If
                Return risultato
            End If

            ' fase di gioco: calare, attaccare, scartare
            Dim provvisoria As Boolean = Not aperta AndAlso squadraStato.Combinazioni.Count > 0
            For Each ids As List(Of Integer) In NuoveCombinazioni(mano)
                Dim cb As Comb = Combinazione(ids)
                If StatoAccettabile(posto, mano.Count - ids.Count, ConAggiunta(squadraStato.Combinazioni, cb)) Then
                    risultato.Add(New MossaMotore("cala|" & String.Join(",", ids), "Cala " & Descrivi(cb) & ": " & ElencaCarte(ids)))
                End If
            Next
            Dim attacchi As New HashSet(Of Integer)()
            For Each id As Integer In Distinte(mano)
                For i As Integer = 0 To squadraStato.Combinazioni.Count - 1
                    Dim c As Comb = squadraStato.Combinazioni(i)
                    Dim nuova As Comb = Combinazione(ConCarta(c.Carte, id))
                    If nuova IsNot Nothing Then
                        If StatoAccettabile(posto, mano.Count - 1, ConSostituita(squadraStato.Combinazioni, i, nuova)) Then
                            risultato.Add(New MossaMotore("att|" & id.ToString() & "|" & i.ToString(), "Attacca " & Nome(id) & ", " & Valore(id).ToString() & " punti, alla " & Descrivi(c)))
                            attacchi.Add(id)
                        End If
                    End If
                Next
            Next
            If provvisoria Then
                risultato.Add(New MossaMotore("annulla", "Annulla le combinazioni provvisorie (" & PuntiCarte(squadraStato.Combinazioni).ToString() & " punti su " & minimo.ToString() & ") e riprendi le carte"))
                Return risultato
            End If
            For Each id As Integer In Distinte(mano)
                If mano.Count = 1 AndAlso squadraStato.Pozzetto Then
                    If Not HaBurraco(sq) Then Continue For ' non si può chiudere senza burraco
                    If PuoEssereJolly(id) AndAlso attacchi.Contains(id) Then Continue For
                End If
                risultato.Add(New MossaMotore("scarta|" & id.ToString(), "Scarta " & Nome(id) & ", " & Valore(id).ToString() & " punti"))
            Next
            Return risultato
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Mosse", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Togli(mano As List(Of Integer), ids As IEnumerable(Of Integer))
        Try
            For Each id As Integer In ids
                Dim i As Integer = mano.IndexOf(id)
                If i < 0 Then Throw New InvalidOperationException("Carta non in mano.")
                mano.RemoveAt(i)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Togli", ex)
            Throw
        End Try
    End Sub

    Private Sub PrendiPozzetto(posto As Integer, eventi As List(Of EventoMotore))
        Try
            Dim sq As Integer = SquadraDi(posto)
            Dim carte As List(Of Integer) = _pozzetti(0)
            _pozzetti.RemoveAt(0)
            _squadre(sq).Pozzetto = True
            Dim nuova As New List(Of Integer)(_mani(posto))
            nuova.AddRange(carte)
            _mani(posto) = Ordina(nuova)
            eventi.Add(New EventoMotore(Nothing, _nomi(posto) & " prende il pozzetto."))
            eventi.Add(New EventoMotore(posto, "Nel pozzetto hai trovato: " & ElencaCarte(carte) & "."))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.PrendiPozzetto", ex)
            Throw
        End Try
    End Sub

    Private Sub ControllaApertura(posto As Integer, eventi As List(Of EventoMotore))
        Try
            Dim sq As Integer = SquadraDi(posto)
            If _squadre(sq).Aperta Then Return
            Dim punti As Integer = PuntiCarte(_squadre(sq).Combinazioni)
            If punti >= MinimoApertura(sq) Then
                _squadre(sq).Aperta = True
                _provvPosto = -1
                _provvMano = Nothing
                eventi.Add(New EventoMotore(Nothing, _nomi(posto) & " apre con " & punti.ToString() & " punti."))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ControllaApertura", ex)
            Throw
        End Try
    End Sub

    Private Sub RicordaProvvisorio(posto As Integer)
        Try
            Dim sq As Integer = SquadraDi(posto)
            If Not _squadre(sq).Aperta AndAlso _provvMano Is Nothing Then
                _provvPosto = posto
                _provvMano = New List(Of Integer)(_mani(posto))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.RicordaProvvisorio", ex)
            Throw
        End Try
    End Sub

    Private Shared Function ListaInt(testo As String) As List(Of Integer)
        Try
            Return testo.Split(","c).Select(Function(x) Integer.Parse(x)).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ListaInt", ex)
            Throw
        End Try
    End Function

    Public Function Applica(posto As Integer, idMossa As String, verifica As Boolean) As List(Of EventoMotore) Implements IMotoreGioco.Applica
        Try
            If verifica AndAlso Not Mosse(posto).Any(Function(m) m.Id = idMossa) Then Throw New MossaNonConsentita()
            Dim eventi As New List(Of EventoMotore)()
            Dim nome As String = _nomi(posto)
            Dim sq As Integer = SquadraDi(posto)
            Dim parti As String() = idMossa.Split("|"c)

            If parti(0) = "pesca" Then
                Dim carta As Integer = _mazzo(0)
                _mazzo.RemoveAt(0)
                _mani(posto) = Ordina(ConCarta(_mani(posto), carta))
                _fase = "azione"
                eventi.Add(New EventoMotore(Nothing, nome & " pesca dal mazzo."))
                eventi.Add(New EventoMotore(posto, "Hai pescato: " & MotoreBurraco.Nome(carta) & "."))
                Return eventi
            End If

            If parti(0) = "scarti" Then
                Dim cima As Integer = _scarti(_scarti.Count - 1)
                _scarti.RemoveAt(_scarti.Count - 1)
                Dim pila As New List(Of Integer)(_scarti)
                _scarti = New List(Of Integer)()
                Dim nPila As Integer = pila.Count + 1
                Dim unita As New List(Of Integer)(_mani(posto))
                unita.AddRange(pila)
                _mani(posto) = Ordina(unita)
                eventi.Add(New EventoMotore(Nothing, nome & " prende gli scarti: " & nPila.ToString() & " carte."))
                If pila.Count > 0 Then eventi.Add(New EventoMotore(posto, "Con gli scarti hai preso: " & ElencaCarte(pila) & "."))
                If parti(1) = "att" Then
                    Dim i As Integer = Integer.Parse(parti(2))
                    Dim nuova As Comb = Combinazione(ConCarta(_squadre(sq).Combinazioni(i).Carte, cima))
                    _squadre(sq).Combinazioni(i) = nuova
                    eventi.Add(New EventoMotore(Nothing, nome & " attacca " & MotoreBurraco.Nome(cima) & " alla " & Descrivi(nuova) & "."))
                Else
                    Dim ids As List(Of Integer) = ListaInt(parti(2))
                    Dim daMano As List(Of Integer) = ids.Where(Function(x) x <> cima).ToList()
                    Togli(_mani(posto), daMano)
                    Dim comb As Comb = Combinazione(ids)
                    _squadre(sq).Combinazioni.Add(comb)
                    eventi.Add(New EventoMotore(Nothing, nome & " cala " & Descrivi(comb) & ": " & ElencaCarte(ids) & "."))
                End If
                _fase = "azione"
                ControllaApertura(posto, eventi)
                DopoCalata(posto, eventi)
                Return eventi
            End If

            If parti(0) = "cala" Then
                RicordaProvvisorio(posto)
                Dim ids As List(Of Integer) = ListaInt(parti(1))
                Togli(_mani(posto), ids)
                Dim comb As Comb = Combinazione(ids)
                _squadre(sq).Combinazioni.Add(comb)
                eventi.Add(New EventoMotore(Nothing, nome & " cala " & Descrivi(comb) & ": " & ElencaCarte(ids) & "."))
                ControllaApertura(posto, eventi)
                DopoCalata(posto, eventi)
                Return eventi
            End If

            If parti(0) = "att" Then
                RicordaProvvisorio(posto)
                Dim id As Integer = Integer.Parse(parti(1))
                Dim i As Integer = Integer.Parse(parti(2))
                Togli(_mani(posto), {id})
                Dim nuova As Comb = Combinazione(ConCarta(_squadre(sq).Combinazioni(i).Carte, id))
                _squadre(sq).Combinazioni(i) = nuova
                eventi.Add(New EventoMotore(Nothing, nome & " attacca " & MotoreBurraco.Nome(id) & " alla " & Descrivi(nuova) & "."))
                ControllaApertura(posto, eventi)
                DopoCalata(posto, eventi)
                Return eventi
            End If

            If parti(0) = "annulla" Then
                _mani(posto) = Ordina(_provvMano)
                _squadre(sq).Combinazioni = New List(Of Comb)()
                _provvPosto = -1
                _provvMano = Nothing
                _rinuncia = True
                eventi.Add(New EventoMotore(Nothing, nome & " annulla le combinazioni provvisorie."))
                Return eventi
            End If

            If parti(0) = "scarta" Then
                Dim id As Integer = Integer.Parse(parti(1))
                Togli(_mani(posto), {id})
                _scarti.Add(id)
                eventi.Add(New EventoMotore(Nothing, nome & " scarta " & MotoreBurraco.Nome(id) & "."))
                _provvPosto = -1
                _provvMano = Nothing
                _rinuncia = False
                If _mani(posto).Count = 0 Then
                    If Not _squadre(sq).Pozzetto Then
                        PrendiPozzetto(posto, eventi)
                    Else
                        _chiusaDa = posto
                        eventi.Add(New EventoMotore(Nothing, nome & " chiude la mano!"))
                        Conta(eventi)
                        Return eventi
                    End If
                End If
                _turno = (posto + 1) Mod _n
                _fase = "pesca"
                ControllaMazzo(eventi)
                Return eventi
            End If
            Throw New MossaNonConsentita()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Applica", ex)
            Throw
        End Try
    End Function

    Private Sub DopoCalata(posto As Integer, eventi As List(Of EventoMotore))
        Try
            If _mani(posto).Count > 0 Then Return
            Dim sq As Integer = SquadraDi(posto)
            If Not _squadre(sq).Pozzetto Then
                PrendiPozzetto(posto, eventi)
                Return
            End If
            _chiusaDa = posto
            eventi.Add(New EventoMotore(Nothing, _nomi(posto) & " chiude la mano senza scartare!"))
            Conta(eventi)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.DopoCalata", ex)
            Throw
        End Try
    End Sub

    Private Sub ControllaMazzo(eventi As List(Of EventoMotore))
        Try
            If _finita OrElse _fase <> "pesca" Then Return
            If Mosse(_turno).Count = 0 Then
                eventi.Add(New EventoMotore(Nothing, "Il mazzo è finito: la mano termina."))
                Conta(eventi)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.ControllaMazzo", ex)
            Throw
        End Try
    End Sub

    Private Function NomiSquadre() As String()
        Try
            Dim r(1) As String
            For sq As Integer = 0 To 1
                Dim membri As New List(Of String)()
                For p As Integer = 0 To _n - 1
                    If SquadraDi(p) = sq Then membri.Add(_nomi(p))
                Next
                r(sq) = String.Join(" e ", membri)
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.NomiSquadre", ex)
            Throw
        End Try
    End Function

    Private Sub Conta(eventi As List(Of EventoMotore))
        Try
            Dim punti As Integer() = {0, 0}
            Dim dettaglio(1)() As Integer
            For sq As Integer = 0 To 1
                Dim tavolo As Integer = _squadre(sq).Combinazioni.Sum(Function(c) PuntiCombinazione(c))
                Dim mano As Integer = 0
                For p As Integer = 0 To _n - 1
                    If SquadraDi(p) = sq Then mano += _mani(p).Sum(Function(id) Valore(id))
                Next
                Dim chiusura As Integer = If(_chiusaDa.HasValue AndAlso SquadraDi(_chiusaDa.Value) = sq, 100, 0)
                Dim pozzetto As Integer = If(_squadre(sq).Pozzetto, 0, -100)
                punti(sq) = tavolo + chiusura + pozzetto - mano
                dettaglio(sq) = {tavolo, chiusura, pozzetto, -mano, punti(sq)}
            Next
            _punteggi = dettaglio
            Dim nomiSq As String() = NomiSquadre()
            For sq As Integer = 0 To 1
                _totali(sq) += punti(sq)
            Next
            eventi.Add(New EventoMotore(Nothing, "Fine della mano " & _manoNumero.ToString() & ". Punti della mano: " & nomiSq(0) & " " & punti(0).ToString() & ", " & nomiSq(1) & " " & punti(1).ToString() &
                                        ". Totali: " & nomiSq(0) & " " & _totali(0).ToString() & ", " & nomiSq(1) & " " & _totali(1).ToString() & "."))
            Dim fine As Boolean = False
            If Math.Max(_totali(0), _totali(1)) >= _obiettivo AndAlso _totali(0) <> _totali(1) Then
                fine = True
                _motivo = "Raggiunti " & _obiettivo.ToString() & " punti."
            ElseIf _manoNumero >= 15 Then
                fine = True
                _motivo = "Sono state giocate " & _manoNumero.ToString() & " mani."
            End If
            If fine Then
                _finita = True
                _vincitori = If(_totali(0) = _totali(1), New List(Of Integer)(), If(_totali(0) > _totali(1), New List(Of Integer) From {0}, New List(Of Integer) From {1}))
                eventi.Add(New EventoMotore(Nothing, If(_totali(0) = _totali(1), "La partita finisce in parità.", "Vince la partita " & nomiSq(If(_totali(0) > _totali(1), 0, 1)) & ".")))
                Return
            End If
            _manoNumero += 1
            Dim primo As Integer = (_manoNumero - 1) Mod _n
            DaiCarte(primo)
            eventi.Add(New EventoMotore(Nothing, "Comincia la mano " & _manoNumero.ToString() & ". Negli scarti c'è " & Nome(_scarti(0)) & ". Inizia " & _nomi(primo) & "."))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Conta", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ vista per un giocatore
    Public Function Vista(posto As Integer) As JObject Implements IMotoreGioco.Vista
        Try
            Dim mioSq As Integer = If(posto >= 0, SquadraDi(posto), -1)
            Dim cima As JToken = If(_scarti.Count > 0, CType(CartaJson(_scarti(_scarti.Count - 1)), JToken), JValue.CreateNull())
            Dim scarti As JObject = Attrezzi.J("n", _scarti.Count, "cima", cima, "carte", Attrezzi.Arr(_scarti.Select(Function(id) CartaJson(id))), "congelato", Congelato())
            Dim mano As IEnumerable(Of Integer) = If(posto >= 0 AndAlso posto < _mani.Count, _mani(posto), New List(Of Integer)())
            Dim punteggi As JToken = JValue.CreateNull()
            If _punteggi IsNot Nothing Then
                punteggi = Attrezzi.Arr(_punteggi.Select(Function(d) Attrezzi.J("tavolo", d(0), "chiusura", d(1), "pozzetto", d(2), "in_mano", d(3), "totale", d(4))))
            End If
            Dim squadre As New JArray()
            For sq As Integer = 0 To 1
                Dim comb As New JArray()
                For i As Integer = 0 To _squadre(sq).Combinazioni.Count - 1
                    Dim c As Comb = _squadre(sq).Combinazioni(i)
                    comb.Add(Attrezzi.J("indice", i, "descrizione", Descrivi(c), "tipo", c.Tipo,
                                        "burraco", If(EBurraco(c), CObj(If(EBurracoPulito(c), "pulito", "sporco")), Nothing),
                                        "carte", Attrezzi.Arr(c.Carte.Select(Function(id) CartaJson(id)))))
                Next
                Dim membri As New List(Of Integer)()
                For p As Integer = 0 To _n - 1
                    If SquadraDi(p) = sq Then membri.Add(p)
                Next
                squadre.Add(Attrezzi.J("membri", Attrezzi.Arr(membri), "pozzetto_preso", _squadre(sq).Pozzetto, "aperta", _squadre(sq).Aperta,
                                       "minimo", MinimoApertura(sq), "punti_calati", PuntiCarte(_squadre(sq).Combinazioni), "combinazioni", comb))
            Next
            Return Attrezzi.J("tipo", "burraco", "n", _n, "nomi", Attrezzi.Arr(_nomi), "turno", _turno, "fase", _fase, "mazzo", _mazzo.Count,
                              "pozzetti_rimasti", _pozzetti.Count, "scarti", scarti, "mano", Attrezzi.Arr(mano.Select(Function(id) CartaJson(id))),
                              "carte_in_mano", Attrezzi.Arr(_mani.Select(Function(m) m.Count)), "squadre", squadre, "finita", _finita, "punteggi", punteggi,
                              "partita", Attrezzi.J("obiettivo", _obiettivo, "totali", Attrezzi.Arr(_totali), "mano", _manoNumero),
                              "mia_squadra", mioSq, "miei_compagni", New JArray())
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Vista", ex)
            Throw
        End Try
    End Function

    Public Function Situazione(posto As Integer) As String Implements IMotoreGioco.Situazione
        Try
            Dim t As String = "Mano " & _manoNumero.ToString() & ", si gioca fino a " & _obiettivo.ToString() & " punti. "
            Dim nomiSq As String() = NomiSquadre()
            t &= "Totali: " & nomiSq(0) & " " & _totali(0).ToString() & ", " & nomiSq(1) & " " & _totali(1).ToString() & ". "
            t &= "Hai in mano " & _mani(posto).Count.ToString() & " carte: " & ElencaCarte(_mani(posto)) & ". "
            t &= "Mazzo: " & _mazzo.Count.ToString() & " carte. "
            If _scarti.Count > 0 Then
                t &= "In cima agli scarti c'è " & Nome(_scarti(_scarti.Count - 1)) & ", in tutto " & _scarti.Count.ToString() & " carte" & If(Congelato(), ", pozzo congelato", "") & ". "
            Else
                t &= "Gli scarti sono vuoti. "
            End If
            For sq As Integer = 0 To 1
                Dim mia As Boolean = sq = SquadraDi(posto)
                Dim c As List(Of Comb) = _squadre(sq).Combinazioni
                t &= If(mia, "La tua squadra", "La squadra avversaria") & If(_squadre(sq).Pozzetto, " ha preso il pozzetto", " non ha ancora preso il pozzetto") &
                    If(_squadre(sq).Aperta, "", ", non ha ancora aperto: servono " & MinimoApertura(sq).ToString() & " punti") & ": "
                If c.Count = 0 Then
                    t &= "nessuna combinazione. "
                Else
                    Dim parti As New List(Of String)()
                    For i As Integer = 0 To c.Count - 1
                        parti.Add((i + 1).ToString() & ", " & Descrivi(c(i)) & ": " & ElencaCarte(c(i).Carte))
                    Next
                    t &= String.Join("; ", parti) & ". "
                End If
            Next
            Return t.Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Situazione", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ computer
    Private Class Piano
        Public Melds As New List(Of List(Of Integer))()
        Public Punti As Integer
    End Class

    Private Shared Function PianoApertura(mano As List(Of Integer)) As Piano
        Try
            Dim melds As List(Of List(Of Integer)) = NuoveCombinazioni(mano).OrderByDescending(Function(ids) ids.Sum(Function(i) Valore(i))).ToList()
            Dim usate As New List(Of Integer)()
            Dim p As New Piano()
            For Each ids As List(Of Integer) In melds
                If Not ids.Any(Function(x) usate.Contains(x)) Then
                    p.Melds.Add(ids)
                    usate.AddRange(ids)
                    p.Punti += ids.Sum(Function(i) Valore(i))
                End If
            Next
            Return p
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.PianoApertura", ex)
            Throw
        End Try
    End Function

    Private Shared Function Chiave(m As MossaMotore) As String
        Try
            Dim parti As String() = m.Id.Split("|"c)
            Return parti(0) & If(m.Id.StartsWith("scarti|", StringComparison.Ordinal), "|" & parti(1), "")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Chiave", ex)
            Throw
        End Try
    End Function

    Private Function Utilita(mano As List(Of Integer), id As Integer) As Integer
        Try
            If PuoEssereJolly(id) Then Return 500
            Dim punti As Integer = 0
            Dim r As Integer = Rango(id)
            Dim s As Integer = Seme(id)
            For Each altro As Integer In mano
                If altro = id OrElse EJolly(altro) Then Continue For
                If Rango(altro) = r Then punti += 30
                If Seme(altro) = s AndAlso Math.Abs(Rango(altro) - r) <= 2 Then
                    punti += If(Math.Abs(Rango(altro) - r) = 1, 25, 12)
                End If
            Next
            Return punti + Valore(id) \ 5
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Utilita", ex)
            Throw
        End Try
    End Function

    Public Function Bot(posto As Integer) As String Implements IMotoreGioco.Bot
        Try
            Dim mosse As List(Of MossaMotore) = Me.Mosse(posto)
            If mosse.Count = 0 Then Return ""
            Dim sq As Integer = SquadraDi(posto)
            Dim per As New Dictionary(Of String, List(Of MossaMotore))()
            For Each m As MossaMotore In mosse
                Dim k As String = Chiave(m)
                If Not per.ContainsKey(k) Then per(k) = New List(Of MossaMotore)()
                per(k).Add(m)
            Next

            If _fase = "pesca" Then
                If per.ContainsKey("scarti|att") Then Return per("scarti|att")(0).Id
                If per.ContainsKey("scarti|cala") AndAlso _scarti.Count <= 6 Then
                    For Each m As MossaMotore In per("scarti|cala")
                        Dim ids As List(Of Integer) = ListaInt(m.Id.Split("|"c)(2))
                        If Not ids.Any(Function(id) PuoEssereJolly(id)) Then Return m.Id
                    Next
                End If
                Return If(per.ContainsKey("pesca"), "pesca", mosse(0).Id)
            End If

            Dim mano As List(Of Integer) = _mani(posto)
            Dim squadraStato As SquadraStato = _squadre(sq)
            If Not squadraStato.Aperta Then
                Dim provvisoria As Boolean = squadraStato.Combinazioni.Count > 0
                If Not _rinuncia Then
                    Dim minimo As Integer = MinimoApertura(sq)
                    Dim gia As Integer = PuntiCarte(squadraStato.Combinazioni)
                    Dim piano As Piano = PianoApertura(mano)
                    If gia + piano.Punti >= minimo Then
                        For Each ids As List(Of Integer) In piano.Melds
                            Dim id As String = "cala|" & String.Join(",", ids)
                            If per.ContainsKey("cala") Then
                                For Each m As MossaMotore In per("cala")
                                    If m.Id = id Then Return id
                                Next
                            End If
                        Next
                    End If
                End If
                If provvisoria Then Return "annulla"
                Dim migliore0 As String = Nothing
                Dim punteggio0 As Integer = Integer.MaxValue
                If per.ContainsKey("scarta") Then
                    For Each m As MossaMotore In per("scarta")
                        Dim id As Integer = Integer.Parse(m.Id.Split("|"c)(1))
                        Dim p As Integer = Utilita(mano, id)
                        If migliore0 Is Nothing OrElse p < punteggio0 Then
                            punteggio0 = p
                            migliore0 = m.Id
                        End If
                    Next
                End If
                Return If(migliore0, mosse(0).Id)
            End If

            If per.ContainsKey("att") Then
                For Each m As MossaMotore In per("att")
                    Dim id As Integer = Integer.Parse(m.Id.Split("|"c)(1))
                    If Not PuoEssereJolly(id) Then Return m.Id
                Next
            End If
            If per.ContainsKey("cala") Then
                Dim ordinate As List(Of MossaMotore) = per("cala").OrderByDescending(Function(m) m.Id.Split("|"c)(1).Split(","c).Length).ToList()
                For Each m As MossaMotore In ordinate
                    Dim ids As List(Of Integer) = ListaInt(m.Id.Split("|"c)(1))
                    If mano.Count - ids.Count >= 1 Then Return m.Id
                Next
            End If
            Dim migliore As String = Nothing
            Dim migliorePunteggio As Integer = Integer.MaxValue
            If per.ContainsKey("scarta") Then
                For Each m As MossaMotore In per("scarta")
                    Dim id As Integer = Integer.Parse(m.Id.Split("|"c)(1))
                    Dim p As Integer = Utilita(mano, id)
                    If p < migliorePunteggio Then
                        migliorePunteggio = p
                        migliore = m.Id
                    End If
                Next
            End If
            If migliore IsNot Nothing Then Return migliore
            Return mosse(0).Id
        Catch ex As Exception
            RegistroErrori.Registra("MotoreBurraco.Bot", ex)
            Throw
        End Try
    End Function

End Class
