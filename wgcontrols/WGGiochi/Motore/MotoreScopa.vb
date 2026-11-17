Imports Newtonsoft.Json.Linq

''' <summary>
''' Scopa per 2 giocatori o 4 (a coppie: 0 e 2 contro 1 e 3), a mani successive, con le varianti: a 21 punti, scopone scientifico,
''' asso pigliatutto, napola, re bello, scopa a 15, completa. Stesse regole di scopa.php.
''' </summary>
Friend Class MotoreScopa
    Implements IMotoreGioco

    Public Shared ReadOnly VARIANTI As String() = {"classica", "punti_21", "scopone", "asso_piglia_tutto", "napola", "rebello", "scopa_quindici", "completa"}
    Private Const SETTEBELLO As Integer = 16 ' sette di denari
    Private Const REBELLO As Integer = 19    ' re di denari

    Private Class PuntiSquadra
        Public CartePrese As Integer
        Public DenariPresi As Integer
        Public Settebello As Boolean
        Public Rebello As Boolean
        Public Napola As Integer
        Public Primiera As Integer? = Nothing
        Public Scope As Integer
        Public Carte As Integer
        Public Denari As Integer
        Public SetteBelloPunto As Integer
        Public ReBelloPunto As Integer
        Public PrimieraPunto As Integer
        Public NapolaPunti As Integer
        Public Totale As Integer
    End Class

    Private _variante As String
    Private _n As Integer
    Private _nomi As List(Of String)
    Private _obiettivo As Integer
    Private _totali As Integer() = {0, 0}
    Private _manoNumero As Integer = 1
    Private _finita As Boolean = False
    Private _vincitori As New List(Of Integer)()
    Private _punteggiMano As PuntiSquadra() = Nothing
    Private _motivo As String = ""
    Private _ultima As String = Nothing
    Private _tavolo As List(Of Integer)
    Private _mazzo As List(Of Integer)
    Private _mani As List(Of List(Of Integer))
    Private _prese As List(Of Integer)() = {New List(Of Integer)(), New List(Of Integer)()}
    Private _scope As Integer() = {0, 0}
    Private _ultimaPresa As Integer? = Nothing
    Private _turno As Integer = 0

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

    Public Sub New(nomi As List(Of String), variante As String, puntiObiettivo As Integer)
        Try
            Dim n As Integer = nomi.Count
            _variante = If(VARIANTI.Contains(variante), variante, "classica")
            If _variante = "scopone" AndAlso n <> 4 Then
                n = 4
            ElseIf n <> 2 AndAlso n <> 4 Then
                n = If(n >= 4, 4, 2)
            End If
            _obiettivo = 11
            If _variante = "punti_21" Then
                _obiettivo = 21
            ElseIf puntiObiettivo > 0 Then
                _obiettivo = puntiObiettivo
            End If
            _n = n
            _nomi = New List(Of String)(nomi)
            DaiCarte(0)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.New", ex)
            Throw
        End Try
    End Sub

    Private Function SquadraDi(posto As Integer) As Integer
        Try
            Return If(_n = 4, posto Mod 2, posto)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.SquadraDi", ex)
            Throw
        End Try
    End Function

    Private Shared Function ValorePrimiera(id As Integer) As Integer
        Try
            Return {16, 12, 13, 14, 15, 18, 21, 10, 10, 10}(id Mod 10)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.ValorePrimiera", ex)
            Throw
        End Try
    End Function

    Private Sub DaiCarte(primo As Integer)
        Try
            Dim tavolo As List(Of Integer)
            Dim mazzo As List(Of Integer)
            Do
                mazzo = MazzoItaliano.Mescolato()
                tavolo = mazzo.GetRange(0, 4)
                mazzo.RemoveRange(0, 4)
            Loop While tavolo.Where(Function(c) c Mod 10 = 9).Count() >= 3
            _tavolo = MazzoItaliano.Ordina(tavolo)
            _mazzo = mazzo
            _mani = New List(Of List(Of Integer))()
            For i As Integer = 0 To _n - 1
                _mani.Add(New List(Of Integer)())
            Next
            If _variante = "scopone" Then
                For giro As Integer = 0 To 8
                    For i As Integer = 0 To _n - 1
                        _mani(i).Add(Pesca())
                    Next
                Next
                For i As Integer = 0 To _n - 1
                    _mani(i) = MazzoItaliano.Ordina(_mani(i))
                Next
            Else
                DaiTre()
            End If
            _prese = {New List(Of Integer)(), New List(Of Integer)()}
            _scope = {0, 0}
            _ultimaPresa = Nothing
            _turno = primo
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.DaiCarte", ex)
            Throw
        End Try
    End Sub

    Private Function Pesca() As Integer
        Try
            Dim c As Integer = _mazzo(0)
            _mazzo.RemoveAt(0)
            Return c
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Pesca", ex)
            Throw
        End Try
    End Function

    Private Sub DaiTre()
        Try
            For giro As Integer = 0 To 2
                For i As Integer = 0 To _n - 1
                    _mani(i).Add(Pesca())
                Next
            Next
            For i As Integer = 0 To _n - 1
                _mani(i) = MazzoItaliano.Ordina(_mani(i))
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.DaiTre", ex)
            Throw
        End Try
    End Sub

    Private Function NomiMani() As IEnumerable(Of EventoMotore)
        Try
            Dim r As New List(Of EventoMotore)()
            For i As Integer = 0 To _mani.Count - 1
                r.Add(New EventoMotore(i, "Le tue carte: " & MazzoItaliano.Elenca(_mani(i)) & "."))
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.NomiMani", ex)
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
            RegistroErrori.Registra("MotoreScopa.Opzioni", ex)
            Throw
        End Try
    End Function

    Public Sub ImpostaComputer(computer As Boolean()) Implements IMotoreGioco.ImpostaComputer
    End Sub

    Public Function Inizio() As List(Of EventoMotore) Implements IMotoreGioco.Inizio
        Try
            Dim nomeVariante As String = "a scopa"
            Select Case _variante
                Case "scopone" : nomeVariante = "a Scopone scientifico (9 carte a testa)"
                Case "asso_piglia_tutto" : nomeVariante = "a Scopa d'Assi (Asso pigliatutto)"
                Case "napola" : nomeVariante = "a Scopa con Napola (sequenze di denari)"
                Case "rebello" : nomeVariante = "a Scopa con Re Bello"
                Case "scopa_quindici" : nomeVariante = "a Scopa a 15 (prese con somma a 15)"
                Case "completa" : nomeVariante = "a Scopa completa (Napola, Asso pigliatutto e Re Bello)"
            End Select
            Dim r As New List(Of EventoMotore)()
            r.Add(New EventoMotore(Nothing, "Si gioca " & nomeVariante & " fino a " & _obiettivo.ToString() & " punti. Sul tavolo ci sono: " & MazzoItaliano.Elenca(_tavolo) & ". Inizia " & _nomi(_turno) & "."))
            r.AddRange(NomiMani())
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Inizio", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ mosse
    ''' <summary>Le prese possibili con una carta: elenco di elenchi di carte del tavolo (un elenco vuoto: la carta resta sul tavolo).</summary>
    Private Shared Function Prese(tavolo As List(Of Integer), carta As Integer, variante As String) As List(Of List(Of Integer))
        Try
            Dim v As Integer = MazzoItaliano.Numero(carta)
            Dim vuota As New List(Of List(Of Integer)) From {New List(Of Integer)()}

            If variante = "scopa_quindici" Then
                Dim manca As Integer = 15 - v
                If manca <= 0 Then Return vuota
                Dim trovate As List(Of List(Of Integer)) = SommeSulTavolo(tavolo, manca)
                Return If(trovate.Count > 0, trovate, vuota)
            End If

            If (variante = "asso_piglia_tutto" OrElse variante = "completa") AndAlso v = 1 Then
                Dim assi As New List(Of List(Of Integer))()
                For Each c As Integer In tavolo
                    If MazzoItaliano.Numero(c) = 1 Then assi.Add(New List(Of Integer) From {c})
                Next
                If assi.Count > 0 Then Return assi
                If tavolo.Count > 0 Then Return New List(Of List(Of Integer)) From {New List(Of Integer)(tavolo)}
                Return vuota
            End If

            Dim uguali As New List(Of List(Of Integer))()
            For Each c As Integer In tavolo
                If MazzoItaliano.Numero(c) = v Then uguali.Add(New List(Of Integer) From {c})
            Next
            If uguali.Count > 0 Then Return uguali
            If tavolo.Count < 2 Then Return vuota
            Dim somme As List(Of List(Of Integer)) = SommeSulTavolo(tavolo, v)
            Return If(somme.Count > 0, somme, vuota)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Prese", ex)
            Throw
        End Try
    End Function

    ''' <summary>Tutti i gruppi di carte del tavolo che sommano esattamente all'obiettivo (almeno una carta).</summary>
    Private Shared Function SommeSulTavolo(tavolo As List(Of Integer), obiettivo As Integer) As List(Of List(Of Integer))
        Try
            Dim r As New List(Of List(Of Integer))()
            If tavolo.Count = 0 Then Return r
            Dim corrente As New List(Of Integer)()
            Cerca(tavolo, 0, obiettivo, corrente, r)
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.SommeSulTavolo", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Cerca(tavolo As List(Of Integer), da As Integer, manca As Integer, corrente As List(Of Integer), r As List(Of List(Of Integer)))
        Try
            If manca = 0 AndAlso corrente.Count > 0 Then
                r.Add(New List(Of Integer)(corrente))
                Return
            End If
            For i As Integer = da To tavolo.Count - 1
                Dim n As Integer = MazzoItaliano.Numero(tavolo(i))
                If n > manca Then Continue For
                corrente.Add(tavolo(i))
                Cerca(tavolo, i + 1, manca - n, corrente, r)
                corrente.RemoveAt(corrente.Count - 1)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Cerca", ex)
            Throw
        End Try
    End Sub

    Private Function EUltimaGiocata() As Boolean
        Try
            If _mazzo.Count > 0 Then Return False
            Return _mani.Sum(Function(m) m.Count) = 1
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.EUltimaGiocata", ex)
            Throw
        End Try
    End Function

    Private Shared Function IdPresa(carta As Integer, presa As List(Of Integer)) As String
        Try
            Return "gioca|" & carta.ToString() & "|" & If(presa.Count > 0, String.Join(",", presa), "-")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.IdPresa", ex)
            Throw
        End Try
    End Function

    Public Function Mosse(posto As Integer) As List(Of MossaMotore) Implements IMotoreGioco.Mosse
        Try
            Dim r As New List(Of MossaMotore)()
            If _finita OrElse posto <> _turno Then Return r
            For Each carta As Integer In _mani(posto)
                For Each presa As List(Of Integer) In Prese(_tavolo, carta, _variante)
                    Dim testo As String = MazzoItaliano.Nome(carta) & ", vale " & MazzoItaliano.Numero(carta).ToString()
                    Dim vale As Integer = MazzoItaliano.Numero(carta)
                    If carta = SETTEBELLO Then testo &= ", sette bello"
                    If carta = REBELLO AndAlso (_variante = "rebello" OrElse _variante = "completa") Then testo &= ", re bello"
                    If presa.Count = 0 Then
                        testo &= ", la lasci sul tavolo"
                    Else
                        testo &= ", prendi " & String.Join(" e ", presa.Select(Function(c) MazzoItaliano.Nome(c)))
                        Dim eraPigliatutto As Boolean = (_variante = "asso_piglia_tutto" OrElse _variante = "completa") AndAlso vale = 1 AndAlso presa.Count > 1 AndAlso presa.Count = _tavolo.Count
                        If eraPigliatutto Then
                            testo &= " (asso pigliatutto)"
                        ElseIf presa.Count = _tavolo.Count AndAlso Not EUltimaGiocata() Then
                            testo &= " (scopa)"
                        End If
                    End If
                    r.Add(New MossaMotore(IdPresa(carta, presa), testo))
                Next
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Mosse", ex)
            Throw
        End Try
    End Function

    Public Function Applica(posto As Integer, idMossa As String, verifica As Boolean) As List(Of EventoMotore) Implements IMotoreGioco.Applica
        Try
            If verifica AndAlso Not Mosse(posto).Any(Function(m) m.Id = idMossa) Then Throw New MossaNonConsentita()
            Dim parti As String() = idMossa.Split("|"c)
            If parti(0) <> "gioca" OrElse parti.Length <> 3 Then Throw New MossaNonConsentita()
            Dim carta As Integer = Integer.Parse(parti(1))
            Dim presa As List(Of Integer) = If(parti(2) = "-", New List(Of Integer)(), parti(2).Split(","c).Select(Function(x) Integer.Parse(x)).ToList())
            Dim posMano As Integer = _mani(posto).IndexOf(carta)
            If posMano < 0 Then Throw New MossaNonConsentita()
            _mani(posto).RemoveAt(posMano)
            Dim nome As String = _nomi(posto)
            Dim sq As Integer = SquadraDi(posto)
            Dim eventi As New List(Of EventoMotore)()
            Dim v As String = _variante
            Dim nessunaAltra As Boolean = _mazzo.Count = 0 AndAlso _mani.Sum(Function(m) m.Count) = 0

            If presa.Count > 0 Then
                For Each c As Integer In presa
                    Dim k As Integer = _tavolo.IndexOf(c)
                    If k < 0 Then Throw New MossaNonConsentita()
                    _tavolo.RemoveAt(k)
                    _prese(sq).Add(c)
                Next
                _prese(sq).Add(carta)
                _ultimaPresa = sq
                Dim testo As String = nome & " gioca " & MazzoItaliano.Nome(carta) & " e prende " & MazzoItaliano.Elenca(presa) & "."
                Dim eraPigliatutto As Boolean = (v = "asso_piglia_tutto" OrElse v = "completa") AndAlso (carta Mod 10 = 0) AndAlso presa.Count > 1
                If _tavolo.Count = 0 AndAlso Not nessunaAltra Then
                    If eraPigliatutto Then
                        testo &= " Asso pigliatutto! Tavolo ripulito."
                    Else
                        _scope(sq) += 1
                        testo &= " Scopa!"
                    End If
                ElseIf eraPigliatutto Then
                    testo &= " Asso pigliatutto! Tavolo ripulito."
                End If
                eventi.Add(New EventoMotore(Nothing, testo))
                _ultima = testo
            Else
                _tavolo.Add(carta)
                _tavolo = MazzoItaliano.Ordina(_tavolo)
                Dim testo As String = nome & " gioca " & MazzoItaliano.Nome(carta) & " e la lascia sul tavolo."
                eventi.Add(New EventoMotore(Nothing, testo))
                _ultima = testo
            End If
            _turno = (posto + 1) Mod _n

            If _mani.Sum(Function(m) m.Count) = 0 Then
                If _mazzo.Count > 0 Then
                    DaiTre()
                    eventi.Add(New EventoMotore(Nothing, "Si danno altre tre carte a testa. Mazzo: " & _mazzo.Count.ToString() & " carte."))
                    eventi.AddRange(NomiMani())
                Else
                    FinisciMano(eventi)
                End If
            End If
            Return eventi
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Applica", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ punti
    Private Function NomiSquadra(sq As Integer) As String
        Try
            If _n = 2 Then Return _nomi(sq)
            Return _nomi(sq) & " e " & _nomi(sq + 2)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.NomiSquadra", ex)
            Throw
        End Try
    End Function

    Private Shared Function PrimieraDi(carte As List(Of Integer)) As Integer?
        Try
            Dim migliore As New Dictionary(Of Integer, Integer)()
            For Each c As Integer In carte
                Dim s As Integer = MazzoItaliano.Seme(c)
                Dim x As Integer = ValorePrimiera(c)
                If Not migliore.ContainsKey(s) OrElse migliore(s) < x Then migliore(s) = x
            Next
            If migliore.Count = 4 Then Return migliore.Values.Sum()
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.PrimieraDi", ex)
            Throw
        End Try
    End Function

    Private Shared Function CalcolaNapola(carte As List(Of Integer)) As Integer
        Try
            Dim seq As Integer = 0
            For pos As Integer = 0 To 9
                If carte.Contains(10 + pos) Then
                    seq += 1
                Else
                    Exit For
                End If
            Next
            Return If(seq >= 3, seq, 0)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.CalcolaNapola", ex)
            Throw
        End Try
    End Function

    Private Function CalcolaPunti() As PuntiSquadra()
        Try
            Dim usaNapola As Boolean = _variante = "napola" OrElse _variante = "completa"
            Dim usaRebello As Boolean = _variante = "rebello" OrElse _variante = "completa"
            Dim r(1) As PuntiSquadra
            For sq As Integer = 0 To 1
                Dim c As List(Of Integer) = _prese(sq)
                Dim p As New PuntiSquadra()
                p.CartePrese = c.Count
                p.DenariPresi = c.Where(Function(x) MazzoItaliano.Seme(x) = 1).Count()
                p.Settebello = c.Contains(SETTEBELLO)
                p.Rebello = usaRebello AndAlso c.Contains(REBELLO)
                p.Napola = If(usaNapola, CalcolaNapola(c), 0)
                p.Primiera = PrimieraDi(c)
                p.Scope = _scope(sq)
                p.NapolaPunti = p.Napola
                r(sq) = p
            Next
            For sq As Integer = 0 To 1
                Dim a As PuntiSquadra = r(sq)
                Dim b As PuntiSquadra = r(1 - sq)
                a.Carte = If(a.CartePrese > b.CartePrese, 1, 0)
                a.Denari = If(a.DenariPresi > b.DenariPresi, 1, 0)
                a.SetteBelloPunto = If(a.Settebello, 1, 0)
                a.ReBelloPunto = If(a.Rebello, 1, 0)
                Dim pa As Integer? = a.Primiera
                Dim pb As Integer? = b.Primiera
                a.PrimieraPunto = If(pa.HasValue AndAlso (Not pb.HasValue OrElse pa.Value > pb.Value), 1, 0)
                a.Totale = a.Carte + a.Denari + a.SetteBelloPunto + a.ReBelloPunto + a.PrimieraPunto + a.NapolaPunti + a.Scope
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.CalcolaPunti", ex)
            Throw
        End Try
    End Function

    Private Sub FinisciMano(eventi As List(Of EventoMotore))
        Try
            If _tavolo.Count > 0 Then
                Dim sq As Integer = If(_ultimaPresa.HasValue, _ultimaPresa.Value, 0)
                _prese(sq).AddRange(_tavolo)
                eventi.Add(New EventoMotore(Nothing, "La mano è finita: le carte rimaste sul tavolo vanno a " & NomiSquadra(sq) & "."))
                _tavolo = New List(Of Integer)()
            Else
                eventi.Add(New EventoMotore(Nothing, "La mano è finita."))
            End If
            Dim p As PuntiSquadra() = CalcolaPunti()
            _punteggiMano = p
            For sq As Integer = 0 To 1
                _totali(sq) += p(sq).Totale
                Dim parti As New List(Of String)()
                If p(sq).Carte > 0 Then parti.Add("carte")
                If p(sq).Denari > 0 Then parti.Add("denari")
                If p(sq).SetteBelloPunto > 0 Then parti.Add("sette bello")
                If p(sq).ReBelloPunto > 0 Then parti.Add("re bello")
                If p(sq).PrimieraPunto > 0 Then parti.Add("primiera")
                If p(sq).NapolaPunti > 0 Then parti.Add("napola (" & p(sq).NapolaPunti.ToString() & " punti)")
                If p(sq).Scope > 0 Then parti.Add(p(sq).Scope.ToString() & If(p(sq).Scope = 1, " scopa", " scope"))
                eventi.Add(New EventoMotore(Nothing, NomiSquadra(sq) & ": " & If(parti.Count > 0, String.Join(", ", parti), "nessun punto") & ". Punti della mano: " & p(sq).Totale.ToString() & ". Totale: " & _totali(sq).ToString() & "."))
            Next
            If Math.Max(_totali(0), _totali(1)) >= _obiettivo AndAlso _totali(0) <> _totali(1) Then
                Dim v As Integer = If(_totali(0) > _totali(1), 0, 1)
                _finita = True
                _vincitori = If(_n = 2, New List(Of Integer) From {v}, New List(Of Integer) From {v, v + 2})
                _motivo = "Vince " & NomiSquadra(v) & " per " & _totali(v).ToString() & " a " & _totali(1 - v).ToString() & "."
                eventi.Add(New EventoMotore(Nothing, "La partita è finita. " & _motivo))
                Return
            End If
            _manoNumero += 1
            Dim primo As Integer = (_manoNumero - 1) Mod _n
            DaiCarte(primo)
            eventi.Add(New EventoMotore(Nothing, "Comincia la mano " & _manoNumero.ToString() & ". Sul tavolo ci sono: " & MazzoItaliano.Elenca(_tavolo) & ". Inizia " & _nomi(primo) & "."))
            eventi.AddRange(NomiMani())
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.FinisciMano", ex)
            Throw
        End Try
    End Sub

    Private Function CartaDettaglio(id As Integer) As JObject
        Try
            Dim speciale As String = Nothing
            If id = SETTEBELLO Then
                speciale = "settebello"
            ElseIf id = REBELLO AndAlso (_variante = "rebello" OrElse _variante = "completa") Then
                speciale = "rebello"
            ElseIf (id Mod 10 = 0) AndAlso (_variante = "asso_piglia_tutto" OrElse _variante = "completa") Then
                speciale = "pigliatutto"
            End If
            Return MazzoItaliano.Carta(id, speciale)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.CartaDettaglio", ex)
            Throw
        End Try
    End Function

    Private Shared Function PuntiJson(p As PuntiSquadra) As JObject
        Try
            Return Attrezzi.J("carte_prese", p.CartePrese, "denari_presi", p.DenariPresi, "settebello", p.Settebello, "rebello", p.Rebello,
                              "napola", p.Napola, "primiera", If(p.Primiera.HasValue, CObj(p.Primiera.Value), Nothing), "scope", p.Scope,
                              "carte", p.Carte, "denari", p.Denari, "sette_bello", p.SetteBelloPunto, "re_bello", p.ReBelloPunto,
                              "primiera_punto", p.PrimieraPunto, "napola_punti", p.NapolaPunti, "totale", p.Totale)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.PuntiJson", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ vista
    Public Function Vista(posto As Integer) As JObject Implements IMotoreGioco.Vista
        Try
            Dim v As String = _variante
            Dim mioSq As Integer = If(posto >= 0, SquadraDi(posto), -1)
            Dim squadre As New JArray()
            Dim napole(1) As Integer
            Dim rebelli(1) As Boolean
            Dim settebelli(1) As Boolean
            For sq As Integer = 0 To 1
                Dim membri As New List(Of Integer)()
                For p As Integer = 0 To _n - 1
                    If SquadraDi(p) = sq Then membri.Add(p)
                Next
                Dim c As List(Of Integer) = _prese(sq)
                napole(sq) = CalcolaNapola(c)
                rebelli(sq) = c.Contains(REBELLO)
                settebelli(sq) = c.Contains(SETTEBELLO)
                squadre.Add(Attrezzi.J("membri", Attrezzi.Arr(membri), "carte_prese", c.Count, "denari_presi", c.Where(Function(x) MazzoItaliano.Seme(x) = 1).Count(),
                                       "settebello", settebelli(sq), "rebello", rebelli(sq), "napola", napole(sq), "scope", _scope(sq)))
            Next
            Dim riepilogo As New List(Of String)()
            riepilogo.Add("Totali: " & NomiSquadra(0) & " " & _totali(0).ToString() & ", " & NomiSquadra(1) & " " & _totali(1).ToString())
            riepilogo.Add("Mazzo: " & _mazzo.Count.ToString() & " carte")
            For sq As Integer = 0 To 1
                Dim info As String = If(sq = mioSq, "La tua squadra", NomiSquadra(sq)) & ": " & _prese(sq).Count.ToString() & " carte prese, " & _scope(sq).ToString() & " scope" & If(settebelli(sq), ", ha il sette bello", "")
                If rebelli(sq) AndAlso (v = "rebello" OrElse v = "completa") Then info &= ", ha il re bello"
                If napole(sq) > 0 AndAlso (v = "napola" OrElse v = "completa") Then info &= ", napola da " & napole(sq).ToString() & " punti"
                riepilogo.Add(info)
            Next
            Dim mano As IEnumerable(Of Integer) = If(posto >= 0 AndAlso posto < _mani.Count, _mani(posto), New List(Of Integer)())
            Dim punteggi As JToken = JValue.CreateNull()
            If _punteggiMano IsNot Nothing Then punteggi = Attrezzi.Arr(_punteggiMano.Select(Function(p) PuntiJson(p)))
            Return Attrezzi.J("tipo", "scopa", "variante", v, "n", _n, "nomi", Attrezzi.Arr(_nomi), "turno", _turno, "mazzo", _mazzo.Count,
                              "tavolo_carte", Attrezzi.Arr(_tavolo.Select(Function(i) CartaDettaglio(i))), "ultima", _ultima,
                              "mano", Attrezzi.Arr(mano.Select(Function(i) CartaDettaglio(i))), "carte_in_mano", Attrezzi.Arr(_mani.Select(Function(m) m.Count)),
                              "squadre", squadre, "mia_squadra", mioSq, "finita", _finita,
                              "partita", Attrezzi.J("obiettivo", _obiettivo, "totali", Attrezzi.Arr(_totali), "mano", _manoNumero),
                              "punteggi", punteggi, "vincitori", Attrezzi.Arr(_vincitori), "motivo", _motivo, "riepilogo", Attrezzi.Arr(riepilogo),
                              "testo_tavolo", TestoTavolo(), "testo_punti", TestoPunti(mioSq))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Vista", ex)
            Throw
        End Try
    End Function

    Private Function TestoTavolo() As String
        Try
            Dim t As String = If(_tavolo.Count > 0,
                "Sul tavolo ci sono " & _tavolo.Count.ToString() & " carte: " & MazzoItaliano.Elenca(_tavolo) & ". Mazzo: " & _mazzo.Count.ToString() & " carte.",
                "Il tavolo è vuoto. Mazzo: " & _mazzo.Count.ToString() & " carte.")
            Dim mani As New List(Of String)()
            For i As Integer = 0 To _mani.Count - 1
                Dim n As Integer = _mani(i).Count
                mani.Add(_nomi(i) & " " & n.ToString() & If(n = 1, " carta", " carte"))
            Next
            t &= " Carte in mano: " & String.Join(", ", mani) & "."
            If Not String.IsNullOrEmpty(_ultima) Then t &= " Ultima giocata: " & _ultima
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.TestoTavolo", ex)
            Throw
        End Try
    End Function

    Private Function TestoPunti(mioSq As Integer) As String
        Try
            Dim v As String = _variante
            Dim t As String = "Mano " & _manoNumero.ToString() & ", si gioca fino a " & _obiettivo.ToString() & " punti. Totali: " & NomiSquadra(0) & " " & _totali(0).ToString() & ", " & NomiSquadra(1) & " " & _totali(1).ToString() & ". "
            For sq As Integer = 0 To 1
                Dim denari As Integer = _prese(sq).Where(Function(x) MazzoItaliano.Seme(x) = 1).Count()
                t &= If(sq = mioSq, "La tua squadra", NomiSquadra(sq)) & " in questa mano: " & _prese(sq).Count.ToString() & " carte, " & denari.ToString() & " denari, " & _scope(sq).ToString() & " scope" &
                    If(_prese(sq).Contains(SETTEBELLO), ", il sette bello", "")
                If (v = "rebello" OrElse v = "completa") AndAlso _prese(sq).Contains(REBELLO) Then t &= ", il re bello"
                Dim nap As Integer = If(v = "napola" OrElse v = "completa", CalcolaNapola(_prese(sq)), 0)
                If nap > 0 Then t &= ", napola da " & nap.ToString() & " punti"
                t &= ". "
            Next
            Return t.Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.TestoPunti", ex)
            Throw
        End Try
    End Function

    Public Function Situazione(posto As Integer) As String Implements IMotoreGioco.Situazione
        Try
            Return (TestoTavolo() & " Hai in mano " & _mani(posto).Count.ToString() & " carte: " & MazzoItaliano.Elenca(_mani(posto)) & ". " & TestoPunti(SquadraDi(posto))).Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Situazione", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ computer
    ''' <summary>
    ''' Probabilità che un avversario faccia scopa se sul tavolo restano queste carte: ci riesce solo con una carta del valore uguale alla somma
    ''' (se la somma è fino a 10). Si contano le carte già viste (prese, tavolo, la propria mano) e si stima quante ne restano nelle mani degli altri.
    ''' </summary>
    Private Function RischioScopa(posto As Integer, resta As List(Of Integer)) As Double
        Try
            If resta.Count = 0 Then Return 0.0
            Dim somma As Integer = resta.Sum(Function(c) MazzoItaliano.Numero(c))
            If somma > 10 Then Return 0.0
            Dim viste As New HashSet(Of Integer)(_tavolo)
            For Each c As Integer In _mani(posto)
                viste.Add(c)
            Next
            For Each sq As List(Of Integer) In _prese
                For Each c As Integer In sq
                    viste.Add(c)
                Next
            Next
            Dim ignote As Integer = 40 - viste.Count
            Dim uguali As Integer = viste.Where(Function(c) MazzoItaliano.Numero(c) = somma).Count()
            Dim k As Integer = 4 - uguali   ' carte di quel valore che possono ancora essere in mano agli altri
            Dim h As Integer = 0            ' carte in mano agli avversari (nel gioco a coppie il compagno non conta)
            Dim coppie As Boolean = _mani.Count = 4
            For i As Integer = 0 To _mani.Count - 1
                If i <> posto AndAlso Not (coppie AndAlso i Mod 2 = posto Mod 2) Then h += _mani(i).Count
            Next
            If k <= 0 OrElse ignote <= 0 OrElse h <= 0 Then Return 0.0
            h = Math.Min(h, ignote)
            Dim senza As Double = 1.0
            For j As Integer = 0 To h - 1
                senza *= Math.Max(0, ignote - k - j) / CDbl(ignote - j)
            Next
            Return 1.0 - senza
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.RischioScopa", ex)
            Throw
        End Try
    End Function

    Public Function Bot(posto As Integer) As String Implements IMotoreGioco.Bot
        Try
            Dim migliore As String = Nothing
            Dim punteggioMigliore As Double = Double.NegativeInfinity
            Dim v As String = _variante
            For Each m As MossaMotore In Mosse(posto)
                Dim parti As String() = m.Id.Split("|"c)
                Dim carta As Integer = Integer.Parse(parti(1))
                Dim presa As List(Of Integer) = If(parti(2) = "-", New List(Of Integer)(), parti(2).Split(","c).Select(Function(x) Integer.Parse(x)).ToList())
                Dim p As Double = 0.0
                If presa.Count > 0 Then
                    Dim tutte As New List(Of Integer)(presa)
                    tutte.Add(carta)
                    p += tutte.Count * 2
                    For Each c As Integer In tutte
                        If c = SETTEBELLO Then p += 40
                        If c = REBELLO AndAlso (v = "rebello" OrElse v = "completa") Then p += 30
                        If (v = "napola" OrElse v = "completa") AndAlso MazzoItaliano.Seme(c) = 1 AndAlso (c Mod 10 <= 2) Then p += 15
                        If MazzoItaliano.Seme(c) = 1 Then p += 4
                        p += ValorePrimiera(c) / 5
                    Next
                    Dim resta As List(Of Integer) = _tavolo.Where(Function(x) Not presa.Contains(x)).ToList()
                    If resta.Count = 0 AndAlso Not EUltimaGiocata() Then
                        p += 100
                    ElseIf resta.Count > 0 Then
                        p -= 60 * RischioScopa(posto, resta)   ' non si lascia al tavolo una scopa facile
                    End If
                Else
                    p -= ValorePrimiera(carta) / 4
                    If carta = SETTEBELLO Then p -= 60
                    If carta = REBELLO AndAlso (v = "rebello" OrElse v = "completa") Then p -= 45
                    If MazzoItaliano.Seme(carta) = 1 Then p -= 6
                    Dim dopo As New List(Of Integer)(_tavolo)
                    dopo.Add(carta)
                    p -= 60 * RischioScopa(posto, dopo)   ' lascia una scopa: dipende da quante carte giuste restano
                End If
                p += Attrezzi.Casuale(0, 100) / 1000.0
                If p > punteggioMigliore Then
                    punteggioMigliore = p
                    migliore = m.Id
                End If
            Next
            Return If(migliore, "")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreScopa.Bot", ex)
            Throw
        End Try
    End Function

End Class
