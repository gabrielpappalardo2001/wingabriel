Imports Newtonsoft.Json.Linq

''' <summary>
''' Monopoli per 2-4 giocatori (anche contro il computer), con le varianti: classica, parcheggio ricco, doppio al Via, completo, veloce.
''' Stesse regole di monopoli.php: aste, ipoteche, costruzioni (32 case e 12 alberghi della banca), scambi, prigione, 60 giri massimi.
''' </summary>
Friend Class MotoreMonopoli
    Implements IMotoreGioco

    Public Const SOLDI_INIZIALI As Integer = 1500
    Public Const GIRI_MASSIMI As Integer = 60
    Public Const MAX_CASE_BANCA As Integer = 32
    Public Const MAX_ALBERGHI_BANCA As Integer = 12

    Public Shared ReadOnly VARIANTI As String() = {"classica", "parcheggio_ricco", "doppio_via", "completo", "veloce"}
    Public Shared ReadOnly NOMI_VARIANTI As New Dictionary(Of String, String) From {
        {"classica", "Classica"}, {"parcheggio_ricco", "Parcheggio Ricco"}, {"doppio_via", "Doppio al Via"}, {"completo", "Regole complete"}, {"veloce", "Partita Veloce"}}

    Private Class Casella
        Public Nome As String
        Public Tipo As String
        Public Prezzo As Integer
        Public Colore As String
        Public Affitti As Integer()
        Public Costo As Integer
    End Class

    Private Shared Function C(nome As String, tipo As String, Optional prezzo As Integer = 0, Optional colore As String = Nothing, Optional affitti As Integer() = Nothing, Optional costo As Integer = 0) As Casella
        Try
            Return New Casella With {.Nome = nome, .Tipo = tipo, .Prezzo = prezzo, .Colore = colore, .Affitti = affitti, .Costo = costo}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.C", ex)
            Throw
        End Try
    End Function

    Private Shared ReadOnly CASELLE As Casella() = {
        C("Via", "via"),
        C("Vicolo Corto", "terreno", 60, "marrone", {2, 10, 30, 90, 160, 250}, 50),
        C("Probabilità", "probabilita"),
        C("Vicolo Stretto", "terreno", 60, "marrone", {4, 20, 60, 180, 320, 450}, 50),
        C("Tassa patrimoniale", "tassa", 200),
        C("Stazione Sud", "stazione", 200),
        C("Bastioni Gran Sasso", "terreno", 100, "azzurro", {6, 30, 90, 270, 400, 550}, 50),
        C("Imprevisti", "imprevisti"),
        C("Viale Monterosa", "terreno", 100, "azzurro", {6, 30, 90, 270, 400, 550}, 50),
        C("Viale Vesuvio", "terreno", 120, "azzurro", {8, 40, 100, 300, 450, 600}, 50),
        C("Prigione, solo visita", "prigione"),
        C("Via Accademia", "terreno", 140, "rosa", {10, 50, 150, 450, 625, 750}, 100),
        C("Società Elettrica", "societa", 150),
        C("Corso Ateneo", "terreno", 140, "rosa", {10, 50, 150, 450, 625, 750}, 100),
        C("Piazza Università", "terreno", 160, "rosa", {12, 60, 180, 500, 700, 900}, 100),
        C("Stazione Ovest", "stazione", 200),
        C("Via Verdi", "terreno", 180, "arancione", {14, 70, 200, 550, 750, 950}, 100),
        C("Probabilità", "probabilita"),
        C("Corso Raffaello", "terreno", 180, "arancione", {14, 70, 200, 550, 750, 950}, 100),
        C("Piazza Dante", "terreno", 200, "arancione", {16, 80, 220, 600, 800, 1000}, 100),
        C("Parcheggio gratuito", "parcheggio"),
        C("Via Marco Polo", "terreno", 220, "rosso", {18, 90, 250, 700, 875, 1050}, 150),
        C("Imprevisti", "imprevisti"),
        C("Corso Magellano", "terreno", 220, "rosso", {18, 90, 250, 700, 875, 1050}, 150),
        C("Largo Colombo", "terreno", 240, "rosso", {20, 100, 300, 750, 925, 1100}, 150),
        C("Stazione Nord", "stazione", 200),
        C("Viale Costantino", "terreno", 260, "giallo", {22, 110, 330, 800, 975, 1150}, 150),
        C("Viale Traiano", "terreno", 260, "giallo", {22, 110, 330, 800, 975, 1150}, 150),
        C("Società Acqua Potabile", "societa", 150),
        C("Piazza Giulio Cesare", "terreno", 280, "giallo", {24, 120, 360, 850, 1025, 1200}, 150),
        C("Vai in prigione", "vai_prigione"),
        C("Via Roma", "terreno", 300, "verde", {26, 130, 390, 900, 1100, 1275}, 200),
        C("Corso Impero", "terreno", 300, "verde", {26, 130, 390, 900, 1100, 1275}, 200),
        C("Probabilità", "probabilita"),
        C("Largo Augusto", "terreno", 320, "verde", {28, 150, 450, 1000, 1200, 1400}, 200),
        C("Stazione Est", "stazione", 200),
        C("Imprevisti", "imprevisti"),
        C("Viale dei Giardini", "terreno", 350, "blu", {35, 175, 500, 1100, 1300, 1500}, 200),
        C("Tassa di lusso", "tassa", 100),
        C("Parco della Vittoria", "terreno", 400, "blu", {50, 200, 600, 1400, 1700, 2000}, 200)}

    Private Class Carta
        Public Testo As String
        Public Tipo As String
        Public P1 As Integer
        Public P2 As Integer
    End Class

    Private Shared Function K(testo As String, tipo As String, Optional p1 As Integer = 0, Optional p2 As Integer = 0) As Carta
        Try
            Return New Carta With {.Testo = testo, .Tipo = tipo, .P1 = p1, .P2 = p2}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.K", ex)
            Throw
        End Try
    End Function

    Private Shared ReadOnly CARTE_IMPREVISTI As Carta() = {
        K("Avanza fino al Via e ritira 200 euro.", "vai", 0),
        K("Vai in prigione direttamente, senza passare dal Via.", "prigione"),
        K("Avanza fino a Largo Colombo. Se passi dal Via ritira 200 euro.", "vai", 24),
        K("Avanza fino a Via Roma. Se passi dal Via ritira 200 euro.", "vai", 31),
        K("Torna indietro di tre caselle.", "indietro"),
        K("La banca ti paga un dividendo di 50 euro.", "ricevi", 50),
        K("Esci gratis di prigione. Puoi tenere questa carta.", "esci"),
        K("Paga la multa per eccesso di velocità: 15 euro.", "paga", 15),
        K("Fai le riparazioni ai tuoi edifici: paga 25 euro per ogni casa e 100 euro per ogni albergo.", "riparazioni", 25, 100),
        K("Sei stato eletto presidente del consiglio: paga 50 euro a ogni giocatore.", "paga_tutti", 50),
        K("Il tuo prestito edilizio è maturato: ritira 150 euro.", "ricevi", 150),
        K("Avanza fino alla Stazione Nord. Se passi dal Via ritira 200 euro.", "vai", 25),
        K("Hai vinto un concorso di cruciverba: ritira 100 euro.", "ricevi", 100),
        K("Paga la tassa scolastica: 150 euro.", "paga", 150),
        K("Avanza fino a Bastioni Gran Sasso. Se passi dal Via ritira 200 euro.", "vai", 6),
        K("Avanza fino al Parco della Vittoria.", "vai", 39)}

    Private Shared ReadOnly CARTE_PROBABILITA As Carta() = {
        K("Errore della banca a tuo favore: ritira 200 euro.", "ricevi", 200),
        K("Paga la visita dal dottore: 50 euro.", "paga", 50),
        K("Dalla vendita di un'azione ricavi 50 euro.", "ricevi", 50),
        K("Esci gratis di prigione. Puoi tenere questa carta.", "esci"),
        K("Vai in prigione direttamente, senza passare dal Via.", "prigione"),
        K("È il tuo compleanno: ogni giocatore ti dà 10 euro.", "compleanno", 10),
        K("Rimborso delle tasse: ritira 20 euro.", "ricevi", 20),
        K("Ricevi il pagamento dell'assicurazione sulla vita: ritira 100 euro.", "ricevi", 100),
        K("Paga le spese d'ospedale: 100 euro.", "paga", 100),
        K("Paga le rette scolastiche: 50 euro.", "paga", 50),
        K("Ricevi un compenso di consulenza: 25 euro.", "ricevi", 25),
        K("Riparazioni stradali: paga 40 euro per ogni casa e 115 euro per ogni albergo.", "riparazioni", 40, 115),
        K("Hai vinto il secondo premio in un concorso di bellezza: ritira 10 euro.", "ricevi", 10),
        K("Hai ricevuto un'eredità da un parente lontano: ritira 100 euro.", "ricevi", 100),
        K("Avanza fino al Via e ritira 200 euro.", "vai", 0),
        K("Il fondo di previdenza ti rende 100 euro: ritirali.", "ricevi", 100)}

    Private Class Prop
        Public Di As Integer? = Nothing
        Public Case_ As Integer = 0
        Public Ipoteca As Boolean = False
    End Class

    Private Class Scambio
        Public Da As Integer
        Public A As Integer
        Public DaiProp As List(Of Integer)
        Public DaiSoldi As Integer
        Public ChiediProp As List(Of Integer)
        Public ChiediSoldi As Integer
    End Class

    Private Class Asta
        Public Casella As Integer
        Public Partecipanti As List(Of Integer)
        Public Indice As Integer
        Public Offerta As Integer
        Public Leader As Integer? = Nothing
        Public Da As Integer
    End Class

    Private _variante As String
    Private _n As Integer
    Private _nomi As List(Of String)
    Private _soldi As Integer()
    Private _pos As Integer()
    Private _prigione As Integer()
    Private _esci As Integer()
    Private _fuori As Boolean()
    Private _prop As Prop()          ' 40 elementi, Nothing se la casella non è una proprietà
    Private _piatto As Integer = 0
    Private _imprevisti As List(Of Integer)
    Private _probabilita As List(Of Integer)
    Private _turno As Integer = 0
    Private _fase As String = "tiro"
    Private _doppi As Integer = 0
    Private _dadi As Integer() = {0, 0}
    Private _giri As Integer = 0
    Private _scambio As Scambio = Nothing
    Private _asta As Asta = Nothing
    Private _finita As Boolean = False
    Private _vincitore As Integer? = Nothing
    Private _motivo As String = ""
    Private _punteggi As Integer() = Nothing
    Private _computer As Boolean() = Nothing
    Private _ultimoScambioBot As Integer() = Nothing   ' per ogni posto, il giro dell'ultima proposta di scambio del computer (vedi ScambioBot)

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
            RegistroErrori.Registra("MotoreMonopoli.ImpostaComputer", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ utilità
    Private Shared Function NomeCasella(i As Integer) As String
        Try
            Return CASELLE(i).Nome
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.NomeCasella", ex)
            Throw
        End Try
    End Function

    Private Shared Function EProprieta(i As Integer) As Boolean
        Try
            Dim t As String = CASELLE(i).Tipo
            Return t = "terreno" OrElse t = "stazione" OrElse t = "societa"
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.EProprieta", ex)
            Throw
        End Try
    End Function

    Private Shared Function Prezzo(i As Integer) As Integer
        Try
            Return CASELLE(i).Prezzo
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Prezzo", ex)
            Throw
        End Try
    End Function

    Private Shared Function Gruppo(i As Integer) As List(Of Integer)
        Try
            Dim g As String = CASELLE(i).Colore
            Dim r As New List(Of Integer)()
            For k As Integer = 0 To CASELLE.Length - 1
                If CASELLE(k).Tipo = "terreno" AndAlso CASELLE(k).Colore = g Then r.Add(k)
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Gruppo", ex)
            Throw
        End Try
    End Function

    Private Shared Function Euro(n As Integer) As String
        Try
            Return n.ToString() & " euro"
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Euro", ex)
            Throw
        End Try
    End Function

    Private Shared Function Dado() As Integer
        Try
            Return Attrezzi.Casuale(1, 7)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Dado", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ partita
    Public Sub New(nomi As List(Of String), variante As String)
        Try
            Dim n As Integer = nomi.Count
            If n < 2 OrElse n > 4 Then Throw New ArgumentException("Il monopoli si gioca da 2 a 4.")
            _variante = If(VARIANTI.Contains(variante), variante, "classica")
            _n = n
            _nomi = New List(Of String)(nomi)
            _imprevisti = New List(Of Integer)(Enumerable.Range(0, CARTE_IMPREVISTI.Length))
            _probabilita = New List(Of Integer)(Enumerable.Range(0, CARTE_PROBABILITA.Length))
            Attrezzi.Mescola(_imprevisti)
            Attrezzi.Mescola(_probabilita)
            _prop = New Prop(CASELLE.Length - 1) {}
            Dim indiciProp As New List(Of Integer)()
            For i As Integer = 0 To CASELLE.Length - 1
                If EProprieta(i) Then
                    _prop(i) = New Prop()
                    indiciProp.Add(i)
                End If
            Next
            If _variante = "veloce" Then
                Attrezzi.Mescola(indiciProp)
                For p As Integer = 0 To n - 1
                    For k As Integer = 0 To 1
                        If indiciProp.Count > 0 Then
                            _prop(indiciProp(0)).Di = p
                            indiciProp.RemoveAt(0)
                        End If
                    Next
                Next
            End If
            _soldi = Enumerable.Repeat(SOLDI_INIZIALI, n).ToArray()
            _pos = New Integer(n - 1) {}
            _prigione = Enumerable.Repeat(-1, n).ToArray()
            _esci = New Integer(n - 1) {}
            _fuori = New Boolean(n - 1) {}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.New", ex)
            Throw
        End Try
    End Sub

    Public Function Inizio() As List(Of EventoMotore) Implements IMotoreGioco.Inizio
        Try
            Dim v As String = _variante
            Dim desc As String = "Si gioca a monopoli"
            Select Case v
                Case "parcheggio_ricco" : desc &= " con Parcheggio Ricco (multe e tasse nel piatto)"
                Case "doppio_via" : desc &= " con Doppio sul Via (400 euro fermandosi sul Via)"
                Case "completo" : desc &= " completo (Parcheggio Ricco e Doppio sul Via)"
                Case "veloce" : desc &= " veloce (2 proprietà iniziali a testa, 3 case per albergo)"
            End Select
            Dim r As New List(Of EventoMotore)()
            r.Add(New EventoMotore(Nothing, desc & ". Ognuno ha " & Euro(SOLDI_INIZIALI) & ". Inizia " & _nomi(0) & "."))
            If v = "veloce" Then
                For p As Integer = 0 To _n - 1
                    Dim mie As New List(Of String)()
                    For i As Integer = 0 To _prop.Length - 1
                        If _prop(i) IsNot Nothing AndAlso _prop(i).Di.HasValue AndAlso _prop(i).Di.Value = p Then mie.Add(NomeCasella(i))
                    Next
                    If mie.Count > 0 Then r.Add(New EventoMotore(p, "Proprietà assegnate all'inizio: " & String.Join(" e ", mie) & "."))
                Next
            End If
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Inizio", ex)
            Throw
        End Try
    End Function

    Private Function Attivi() As List(Of Integer)
        Try
            Dim r As New List(Of Integer)()
            For i As Integer = 0 To _n - 1
                If Not _fuori(i) Then r.Add(i)
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Attivi", ex)
            Throw
        End Try
    End Function

    Private Function Possiede(i As Integer, p As Integer) As Boolean
        Try
            Return _prop(i) IsNot Nothing AndAlso _prop(i).Di.HasValue AndAlso _prop(i).Di.Value = p
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Possiede", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ patrimonio
    Public Function Patrimonio(p As Integer) As Integer
        Try
            Dim t As Integer = _soldi(p)
            For i As Integer = 0 To _prop.Length - 1
                If Possiede(i, p) Then
                    t += If(_prop(i).Ipoteca, Prezzo(i) \ 2, Prezzo(i))
                    If CASELLE(i).Tipo = "terreno" Then t += _prop(i).Case_ * (CASELLE(i).Costo \ 2)
                End If
            Next
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Patrimonio", ex)
            Throw
        End Try
    End Function

    Private Function PossiedeGruppo(p As Integer, i As Integer) As Boolean
        Try
            For Each k As Integer In Gruppo(i)
                If Not Possiede(k, p) Then Return False
            Next
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.PossiedeGruppo", ex)
            Throw
        End Try
    End Function

    Private Function GruppoSenzaIpoteche(i As Integer) As Boolean
        Try
            For Each k As Integer In Gruppo(i)
                If _prop(k).Ipoteca Then Return False
            Next
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.GruppoSenzaIpoteche", ex)
            Throw
        End Try
    End Function

    Private Function CaseNelGruppo(i As Integer) As Integer
        Try
            Dim t As Integer = 0
            For Each k As Integer In Gruppo(i)
                t += _prop(k).Case_
            Next
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.CaseNelGruppo", ex)
            Throw
        End Try
    End Function

    Private Function Affitto(i As Integer, dadi As Integer) As Integer
        Try
            Dim x As Prop = _prop(i)
            If x.Ipoteca OrElse Not x.Di.HasValue Then Return 0
            Dim tipo As String = CASELLE(i).Tipo
            If tipo = "stazione" Then
                Dim q As Integer = 0
                For k As Integer = 0 To _prop.Length - 1
                    If CASELLE(k).Tipo = "stazione" AndAlso _prop(k).Di = x.Di Then q += 1
                Next
                Return 25 * CInt(2 ^ (q - 1))
            End If
            If tipo = "societa" Then
                Dim q As Integer = 0
                For k As Integer = 0 To _prop.Length - 1
                    If CASELLE(k).Tipo = "societa" AndAlso _prop(k).Di = x.Di Then q += 1
                Next
                Return dadi * If(q >= 2, 10, 4)
            End If
            Dim baseAff As Integer = CASELLE(i).Affitti(x.Case_)
            If x.Case_ = 0 AndAlso PossiedeGruppo(x.Di.Value, i) Then Return baseAff * 2
            Return baseAff
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Affitto", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ pagamenti
    Private Function Liquidabile(p As Integer) As Integer
        Try
            Dim t As Integer = _soldi(p)
            For i As Integer = 0 To _prop.Length - 1
                If Possiede(i, p) Then
                    If CASELLE(i).Tipo = "terreno" Then t += _prop(i).Case_ * (CASELLE(i).Costo \ 2)
                    If Not _prop(i).Ipoteca Then t += Prezzo(i) \ 2
                End If
            Next
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Liquidabile", ex)
            Throw
        End Try
    End Function

    Private Sub Reperisci(p As Integer, somma As Integer, eventi As List(Of EventoMotore))
        Try
            Dim nome As String = _nomi(p)
            While _soldi(p) < somma
                Dim scelta As Integer = -1
                Dim max As Integer = 0
                For i As Integer = 0 To _prop.Length - 1
                    If Possiede(i, p) AndAlso _prop(i).Case_ > max AndAlso CASELLE(i).Tipo = "terreno" Then
                        Dim maxGruppo As Integer = 0
                        For Each k As Integer In Gruppo(i)
                            maxGruppo = Math.Max(maxGruppo, _prop(k).Case_)
                        Next
                        If _prop(i).Case_ = maxGruppo Then
                            max = _prop(i).Case_
                            scelta = i
                        End If
                    End If
                Next
                If scelta < 0 Then Exit While
                _prop(scelta).Case_ -= 1
                _soldi(p) += CASELLE(scelta).Costo \ 2
                eventi.Add(New EventoMotore(Nothing, nome & " vende un edificio a " & NomeCasella(scelta) & " per " & Euro(CASELLE(scelta).Costo \ 2) & "."))
            End While
            For i As Integer = 0 To _prop.Length - 1
                If _soldi(p) >= somma Then Exit For
                If Possiede(i, p) AndAlso Not _prop(i).Ipoteca AndAlso _prop(i).Case_ = 0 Then
                    _prop(i).Ipoteca = True
                    _soldi(p) += Prezzo(i) \ 2
                    eventi.Add(New EventoMotore(Nothing, nome & " ipoteca " & NomeCasella(i) & " e incassa " & Euro(Prezzo(i) \ 2) & "."))
                End If
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Reperisci", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Paga somma da "da" ad "a" (Nothing = banca). False se "da" è fallito.</summary>
    Private Function Paga(da As Integer, a As Integer?, somma As Integer, eventi As List(Of EventoMotore)) As Boolean
        Try
            If somma <= 0 Then Return True
            If _soldi(da) < somma AndAlso Liquidabile(da) >= somma Then Reperisci(da, somma, eventi)
            If _soldi(da) >= somma Then
                _soldi(da) -= somma
                If a.HasValue Then
                    _soldi(a.Value) += somma
                ElseIf _variante = "parcheggio_ricco" OrElse _variante = "completo" Then
                    _piatto += somma
                End If
                Return True
            End If
            Fallimento(da, a, eventi)
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Paga", ex)
            Throw
        End Try
    End Function

    Private Sub Fallimento(p As Integer, creditore As Integer?, eventi As List(Of EventoMotore))
        Try
            Dim nome As String = _nomi(p)
            eventi.Add(New EventoMotore(Nothing, nome & " non riesce a pagare ed è eliminato" & If(creditore.HasValue, ": tutto quello che possiede passa a " & _nomi(creditore.Value), ": le sue proprietà tornano alla banca") & "."))
            If creditore.HasValue Then
                _soldi(creditore.Value) += Math.Max(0, _soldi(p))
                _esci(creditore.Value) += _esci(p)
            End If
            _soldi(p) = 0
            _esci(p) = 0
            For i As Integer = 0 To _prop.Length - 1
                If Possiede(i, p) Then
                    If creditore.HasValue Then
                        _prop(i).Di = creditore
                        _prop(i).Case_ = 0
                    Else
                        _prop(i) = New Prop()
                    End If
                End If
            Next
            _fuori(p) = True
            _prigione(p) = -1
            _scambio = Nothing
            _asta = Nothing

            If _variante = "veloce" Then
                Dim attivi As List(Of Integer) = Me.Attivi()
                If attivi.Count > 0 Then
                    Dim vincitore As Integer = attivi.OrderByDescending(Function(x) Patrimonio(x)).First()
                    TerminaConVincitore(vincitore, eventi, "Partita veloce conclusa con il fallimento di " & nome & ". Vince " & _nomi(vincitore) & " con un patrimonio di " & Euro(Patrimonio(vincitore)) & ".")
                    Return
                End If
            End If
            Dim vivi As List(Of Integer) = Attivi()
            If vivi.Count = 1 Then TerminaConVincitore(vivi(0), eventi, nome & " era l'ultimo avversario.")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Fallimento", ex)
            Throw
        End Try
    End Sub

    Private Sub TerminaConVincitore(v As Integer, eventi As List(Of EventoMotore), motivo As String)
        Try
            _finita = True
            _vincitore = v
            _motivo = motivo
            Dim punti(_n - 1) As Integer
            For i As Integer = 0 To _n - 1
                punti(i) = If(_fuori(i), 0, Patrimonio(i))
            Next
            _punteggi = punti
            eventi.Add(New EventoMotore(Nothing, "La partita è finita: vince " & _nomi(v) & " con un patrimonio di " & Euro(punti(v)) & ". " & motivo))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.TerminaConVincitore", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Chi deve agire adesso: chi risponde a uno scambio, chi deve fare un'offerta all'asta, altrimenti chi ha il turno.</summary>
    Public ReadOnly Property Attore As Integer Implements IMotoreGioco.Attore
        Get
            If _scambio IsNot Nothing Then Return _scambio.A
            If _asta IsNot Nothing Then Return _asta.Partecipanti(_asta.Indice)
            Return _turno
        End Get
    End Property

    ' ------------------------------------------------------------------ mosse
    Public Function Mosse(p As Integer) As List(Of MossaMotore) Implements IMotoreGioco.Mosse
        Try
            Dim m As New List(Of MossaMotore)()
            If _finita OrElse _fuori(p) Then Return m
            If _scambio IsNot Nothing Then
                If _scambio.A = p Then
                    m.Add(New MossaMotore("accetta_scambio", "Accetta lo scambio: " & DescriviScambio(_scambio)))
                    m.Add(New MossaMotore("rifiuta_scambio", "Rifiuta lo scambio proposto da " & _nomi(_scambio.Da)))
                End If
                Return m
            End If
            If _asta IsNot Nothing Then
                If Attore <> p Then Return m
                Dim a As Asta = _asta
                Dim min As Integer = If(a.Offerta > 0, a.Offerta + 10, 10)
                If _soldi(p) >= min Then
                    Dim mo As New MossaMotore("offri", "Offri un prezzo più alto" & If(a.Offerta > 0, " di " & Euro(a.Offerta), "") & ", almeno " & Euro(min))
                    mo.Parametro = Attrezzi.J("tipo", "numero", "min", min, "max", _soldi(p), "domanda", "Quanto offri per " & NomeCasella(a.Casella) & "? Da " & min.ToString() & " a " & _soldi(p).ToString() & " euro")
                    m.Add(mo)
                End If
                m.Add(New MossaMotore("ritirati_asta", "Ritirati dall'asta"))
                Return m
            End If
            If p <> _turno Then Return m
            If _fase = "acquisto" Then
                Dim i As Integer = _pos(p)
                If _soldi(p) >= Prezzo(i) Then m.Add(New MossaMotore("compra", "Compra " & NomeCasella(i) & " per " & Euro(Prezzo(i))))
                m.Add(New MossaMotore("passa", "Non comprare " & NomeCasella(i) & " e fai l'asta"))
                Return m
            End If
            If _fase = "tiro" Then
                If _prigione(p) >= 0 Then
                    If _soldi(p) >= 50 Then m.Add(New MossaMotore("paga_prigione", "Paga 50 euro per uscire di prigione"))
                    If _esci(p) > 0 Then m.Add(New MossaMotore("usa_carta", "Usa la carta ""Esci gratis di prigione"""))
                    m.Add(New MossaMotore("tira", "Tira i dadi per fare un doppio (tentativo " & (_prigione(p) + 1).ToString() & " di 3)"))
                Else
                    m.Add(New MossaMotore("tira", If(_doppi > 0, "Tira ancora i dadi", "Tira i dadi")))
                End If
            End If
            Return m
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Mosse", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le opzioni del turno, in gruppi (ipoteca, riscatta, costruisci, vendi) più, se si può, lo scambio.</summary>
    Public Function Opzioni(p As Integer) As JArray Implements IMotoreGioco.Opzioni
        Try
            Dim r As New JArray()
            If _finita OrElse _fuori(p) OrElse _scambio IsNot Nothing OrElse _asta IsNot Nothing OrElse p <> _turno OrElse Not (_fase = "tiro" OrElse _fase = "fine") Then Return r
            Dim ids As String() = {"ipoteca", "riscatta", "costruisci", "vendi"}
            Dim testi As String() = {"Metti in ipoteca una proprietà", "Riscatta un'ipoteca", "Costruisci case o alberghi", "Vendi case o alberghi"}
            Dim gestione As List(Of MossaMotore) = MosseGestione(p)
            For g As Integer = 0 To 3
                Dim voci As New JArray()
                For Each x As MossaMotore In gestione
                    If x.Id.Split("|"c)(0) = ids(g) Then voci.Add(Attrezzi.J("id", x.Id, "testo", x.Testo))
                Next
                If voci.Count > 0 Then r.Add(Attrezzi.J("id", ids(g), "testo", testi(g), "voci", voci))
            Next
            Dim scambio As JObject = DatiScambio(p)
            If scambio IsNot Nothing Then r.Add(Attrezzi.J("id", "scambio", "testo", "Proponi uno scambio a un altro giocatore", "voci", New JArray(), "scambio", scambio))
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Opzioni", ex)
            Throw
        End Try
    End Function

    Private Function OpzioniPresenti(p As Integer) As Boolean
        Try
            Return Opzioni(p).Count > 0
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.OpzioniPresenti", ex)
            Throw
        End Try
    End Function

    Private Function Scambiabili(q As Integer) As List(Of Integer)
        Try
            Dim r As New List(Of Integer)()
            For i As Integer = 0 To _prop.Length - 1
                If Not Possiede(i, q) OrElse _prop(i).Ipoteca Then Continue For
                If CASELLE(i).Tipo = "terreno" AndAlso CaseNelGruppo(i) > 0 Then Continue For
                r.Add(i)
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Scambiabili", ex)
            Throw
        End Try
    End Function

    Private Function ScambiabiliJson(q As Integer) As JArray
        Try
            Return Attrezzi.Arr(Scambiabili(q).Select(Function(i) Attrezzi.J("id", i, "nome", NomeCasella(i), "prezzo", Prezzo(i))))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ScambiabiliJson", ex)
            Throw
        End Try
    End Function

    Private Function DatiScambio(p As Integer) As JObject
        Try
            Dim altri As New JArray()
            For Each q As Integer In Attivi()
                If q <> p Then altri.Add(Attrezzi.J("indice", q, "nome", _nomi(q), "soldi", _soldi(q), "proprieta", ScambiabiliJson(q)))
            Next
            If altri.Count = 0 Then Return Nothing
            Return Attrezzi.J("giocatori", altri, "mie", ScambiabiliJson(p), "miei_soldi", _soldi(p))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.DatiScambio", ex)
            Throw
        End Try
    End Function

    Private Function MossePossibili(p As Integer) As List(Of MossaMotore)
        Try
            Dim r As List(Of MossaMotore) = Mosse(p)
            For Each g As JObject In Opzioni(p)
                For Each v As JObject In CType(g("voci"), JArray)
                    r.Add(New MossaMotore(Convert.ToString(v("id")), Convert.ToString(v("testo"))))
                Next
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.MossePossibili", ex)
            Throw
        End Try
    End Function

    Private Shared Function ElencoProprieta(ids As List(Of Integer)) As String
        Try
            Return If(ids.Count > 0, String.Join(", ", ids.Select(Function(i) NomeCasella(i) & If(CASELLE(i).Tipo = "terreno", " (" & CASELLE(i).Colore & ")", ""))), "nessuna proprietà")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ElencoProprieta", ex)
            Throw
        End Try
    End Function

    Private Function DescriviScambio(t As Scambio) As String
        Try
            Dim dai As New List(Of String)()
            If t.DaiProp.Count > 0 Then dai.Add(ElencoProprieta(t.DaiProp))
            If t.DaiSoldi > 0 Then dai.Add(Euro(t.DaiSoldi))
            Dim chiedi As New List(Of String)()
            If t.ChiediProp.Count > 0 Then chiedi.Add(ElencoProprieta(t.ChiediProp))
            If t.ChiediSoldi > 0 Then chiedi.Add(Euro(t.ChiediSoldi))
            Return _nomi(t.Da) & " ti dà " & If(dai.Count > 0, String.Join(" e ", dai), "niente") & " e chiede " & If(chiedi.Count > 0, String.Join(" e ", chiedi), "niente")
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.DescriviScambio", ex)
            Throw
        End Try
    End Function

    ''' <summary>Legge e controlla "scambio|verso|prop,prop|soldi|prop,prop|soldi". Nothing se non va bene.</summary>
    Private Function LeggiScambio(p As Integer, id As String) As Scambio
        Try
            Dim parti As String() = id.Split("|"c)
            If parti.Length <> 6 OrElse parti(0) <> "scambio" Then Return Nothing
            Dim a As Integer
            If Not Integer.TryParse(parti(1), a) Then Return Nothing
            If a = p OrElse a < 0 OrElse a >= _n OrElse _fuori(a) Then Return Nothing
            Dim lista As Func(Of String, List(Of Integer)) =
                Function(t As String) As List(Of Integer)
                    If t = "-" OrElse t = "" Then Return New List(Of Integer)()
                    Return t.Split(","c).Select(Function(x) Integer.Parse(x)).Distinct().ToList()
                End Function
            Dim dai As List(Of Integer)
            Dim chiedi As List(Of Integer)
            Dim daiSoldi As Integer
            Dim chiediSoldi As Integer
            Try
                dai = lista(parti(2))
                chiedi = lista(parti(4))
                daiSoldi = Integer.Parse(parti(3))
                chiediSoldi = Integer.Parse(parti(5))
            Catch exLog As Exception
                RegistroErrori.Registra("MotoreMonopoli.LeggiScambio", exLog)
                Return Nothing
            End Try
            If daiSoldi < 0 OrElse chiediSoldi < 0 OrElse daiSoldi > _soldi(p) OrElse chiediSoldi > _soldi(a) Then Return Nothing
            If dai.Count = 0 AndAlso chiedi.Count = 0 AndAlso daiSoldi = 0 AndAlso chiediSoldi = 0 Then Return Nothing
            Dim mie As List(Of Integer) = Scambiabili(p)
            Dim sue As List(Of Integer) = Scambiabili(a)
            For Each i As Integer In dai
                If Not mie.Contains(i) Then Return Nothing
            Next
            For Each i As Integer In chiedi
                If Not sue.Contains(i) Then Return Nothing
            Next
            Return New Scambio With {.Da = p, .A = a, .DaiProp = dai, .DaiSoldi = daiSoldi, .ChiediProp = chiedi, .ChiediSoldi = chiediSoldi}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.LeggiScambio", ex)
            Throw
        End Try
    End Function

    Private Function ValoreScambio(q As Integer, prendi As List(Of Integer), dai As List(Of Integer)) As Integer
        Try
            Dim v As Integer = 0
            For Each i As Integer In prendi
                v += Prezzo(i)
                If CASELLE(i).Tipo = "terreno" Then
                    Dim mancanti As Integer = 0
                    For Each k As Integer In Gruppo(i)
                        If Not Possiede(k, q) AndAlso Not prendi.Contains(k) Then mancanti += 1
                    Next
                    If mancanti = 0 Then v += 200
                End If
            Next
            For Each i As Integer In dai
                v -= Prezzo(i)
                If CASELLE(i).Tipo = "terreno" AndAlso PossiedeGruppo(q, i) Then v -= 200
            Next
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ValoreScambio", ex)
            Throw
        End Try
    End Function

    Private Function ValutaScambioComputer(t As Scambio) As Boolean
        Try
            Dim b As Integer = t.A
            Dim guadagno As Integer = ValoreScambio(b, t.DaiProp, t.ChiediProp) + t.DaiSoldi - t.ChiediSoldi
            Dim ceduto As Integer = t.ChiediSoldi
            For Each i As Integer In t.ChiediProp
                ceduto += Prezzo(i)
            Next
            Dim minimo As Integer = Math.Max(20, CInt(Math.Truncate(ceduto * 0.1)))
            Dim completa As Boolean = False
            For Each i As Integer In t.ChiediProp
                If CASELLE(i).Tipo <> "terreno" Then Continue For
                Dim tutte As Boolean = True
                For Each k As Integer In Gruppo(i)
                    Dim suo As Boolean = (Possiede(k, t.Da) AndAlso Not t.DaiProp.Contains(k)) OrElse t.ChiediProp.Contains(k)
                    If Not suo Then
                        tutte = False
                        Exit For
                    End If
                Next
                If tutte Then completa = True
            Next
            If completa AndAlso guadagno < 300 Then Return False
            Return guadagno >= minimo
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ValutaScambioComputer", ex)
            Throw
        End Try
    End Function

    Private Sub ConcludiScambio(t As Scambio, eventi As List(Of EventoMotore))
        Try
            For Each i As Integer In t.DaiProp
                _prop(i).Di = t.A
            Next
            For Each i As Integer In t.ChiediProp
                _prop(i).Di = t.Da
            Next
            _soldi(t.Da) += t.ChiediSoldi - t.DaiSoldi
            _soldi(t.A) += t.DaiSoldi - t.ChiediSoldi
            eventi.Add(New EventoMotore(Nothing, _nomi(t.A) & " accetta lo scambio con " & _nomi(t.Da) & ": le proprietà e i soldi cambiano di mano."))
            _scambio = Nothing
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ConcludiScambio", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Quante case e quanti alberghi ci sono in gioco (in questa variante l'albergo è il quarto edificio nella partita veloce).</summary>
    Private Sub EdificiInGioco(ByRef case_ As Integer, ByRef alberghi As Integer)
        Try
            case_ = 0
            alberghi = 0
            For Each pr As Prop In _prop
                If pr Is Nothing Then Continue For
                If pr.Case_ = 5 OrElse (_variante = "veloce" AndAlso pr.Case_ = 4) Then
                    alberghi += 1
                ElseIf pr.Case_ > 0 Then
                    case_ += pr.Case_
                End If
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.EdificiInGioco", ex)
            Throw
        End Try
    End Sub

    Private Function MosseGestione(p As Integer) As List(Of MossaMotore)
        Try
            Dim m As New List(Of MossaMotore)()
            Dim v As String = _variante
            Dim sogliaAlbergo As Integer = If(v = "veloce", 3, 4)
            Dim maxEdifici As Integer = If(v = "veloce", 4, 5)
            Dim caseTotali As Integer
            Dim alberghiTotali As Integer
            EdificiInGioco(caseTotali, alberghiTotali)

            For i As Integer = 0 To _prop.Length - 1
                If Not Possiede(i, p) Then Continue For
                Dim x As Prop = _prop(i)
                Dim nome As String = NomeCasella(i)
                Dim costo As Integer = CASELLE(i).Costo
                If CASELLE(i).Tipo = "terreno" Then
                    Dim gr As List(Of Integer) = Gruppo(i)
                    Dim minCase As Integer = gr.Min(Function(k) _prop(k).Case_)
                    Dim maxCase As Integer = gr.Max(Function(k) _prop(k).Case_)
                    If PossiedeGruppo(p, i) AndAlso GruppoSenzaIpoteche(i) AndAlso x.Case_ < maxEdifici AndAlso x.Case_ = minCase AndAlso _soldi(p) >= costo Then
                        Dim puoCostruire As Boolean = If(x.Case_ = sogliaAlbergo, alberghiTotali < MAX_ALBERGHI_BANCA, caseTotali < MAX_CASE_BANCA)
                        If puoCostruire Then
                            m.Add(New MossaMotore("costruisci|" & i.ToString(), If(x.Case_ = sogliaAlbergo, "Costruisci un albergo a ", "Costruisci una casa a ") & nome & " per " & Euro(costo)))
                        End If
                    End If
                    If x.Case_ > 0 AndAlso x.Case_ = maxCase Then
                        m.Add(New MossaMotore("vendi|" & i.ToString(), "Vendi " & If(x.Case_ = maxEdifici, "l'albergo di ", "una casa di ") & nome & " per " & Euro(costo \ 2)))
                    End If
                End If
                If Not x.Ipoteca AndAlso x.Case_ = 0 AndAlso (CASELLE(i).Tipo <> "terreno" OrElse CaseNelGruppo(i) = 0) Then
                    m.Add(New MossaMotore("ipoteca|" & i.ToString(), "Ipoteca " & nome & " e incassa " & Euro(Prezzo(i) \ 2)))
                End If
                If x.Ipoteca Then
                    Dim c As Integer = CostoRiscatto(i)
                    If _soldi(p) >= c Then m.Add(New MossaMotore("riscatta|" & i.ToString(), "Riscatta l'ipoteca di " & nome & " per " & Euro(c)))
                End If
            Next
            Return m
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.MosseGestione", ex)
            Throw
        End Try
    End Function

    Private Shared Function CostoRiscatto(i As Integer) As Integer
        Try
            Return CInt(Math.Ceiling(Prezzo(i) / 2.0 * 1.1))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.CostoRiscatto", ex)
            Throw
        End Try
    End Function

    Public ReadOnly Property CaseDisponibili As Integer
        Get
            Dim c As Integer, a As Integer
            EdificiInGioco(c, a)
            Return Math.Max(0, MAX_CASE_BANCA - c)
        End Get
    End Property

    Public ReadOnly Property AlberghiDisponibili As Integer
        Get
            Dim c As Integer, a As Integer
            EdificiInGioco(c, a)
            Return Math.Max(0, MAX_ALBERGHI_BANCA - a)
        End Get
    End Property

    ' ------------------------------------------------------------------ applicazione
    Private Shared ReadOnly MultiplaRe As New System.Text.RegularExpressions.Regex("^(ipoteca|riscatta|costruisci|vendi)\|\d+(,\d+)+$")

    Public Function Applica(p As Integer, idMossa As String, verifica As Boolean) As List(Of EventoMotore) Implements IMotoreGioco.Applica
        Try
            If verifica Then
                Dim ok As Boolean = MossePossibili(p).Any(Function(x) x.Id = idMossa)
                If Not ok AndAlso idMossa.StartsWith("scambio|", StringComparison.Ordinal) AndAlso OpzioniScambioPossibile(p) AndAlso LeggiScambio(p, idMossa) IsNot Nothing Then ok = True
                If Not ok AndAlso idMossa.StartsWith("offri|", StringComparison.Ordinal) AndAlso LeggiOfferta(p, idMossa).HasValue Then ok = True
                If Not ok AndAlso MultiplaRe.IsMatch(idMossa) AndAlso OpzioniPresenti(p) Then ok = True
                If Not ok Then Throw New MossaNonConsentita()
            End If
            Dim eventi As New List(Of EventoMotore)()
            Dim nome As String = _nomi(p)
            Dim parti As String() = idMossa.Split("|"c)

            If MultiplaRe.IsMatch(idMossa) Then
                ' più proprietà insieme: si applicano in ordine, e solo se vanno bene tutte
                Dim copia As MotoreMonopoli = Me.Copia()
                For Each s As String In parti(1).Split(","c)
                    Dim singola As String = parti(0) & "|" & Integer.Parse(s).ToString()
                    If Not copia.MosseGestione(p).Any(Function(x) x.Id = singola) Then Throw New MossaNonConsentita()
                    eventi.AddRange(copia.Applica(p, singola, False))
                Next
                CopiaDa(copia)
                Return eventi
            End If

            Select Case parti(0)
                Case "tira"
                    Tira(p, eventi)
                Case "paga_prigione"
                    Paga(p, Nothing, 50, eventi)
                    _prigione(p) = -1
                    eventi.Add(New EventoMotore(Nothing, nome & " paga 50 euro ed esce di prigione."))
                Case "usa_carta"
                    _esci(p) -= 1
                    _prigione(p) = -1
                    eventi.Add(New EventoMotore(Nothing, nome & " usa la carta ""Esci gratis di prigione"" ed esce di prigione."))
                Case "compra"
                    Dim i As Integer = _pos(p)
                    _soldi(p) -= Prezzo(i)
                    _prop(i).Di = p
                    eventi.Add(New EventoMotore(Nothing, nome & " compra " & NomeCasella(i) & " per " & Euro(Prezzo(i)) & "."))
                    DopoAzione(p, eventi)
                Case "passa"
                    eventi.Add(New EventoMotore(Nothing, nome & " non compra " & NomeCasella(_pos(p)) & "."))
                    AvviaAsta(p, _pos(p), eventi)
                Case "offri"
                    Dim importo As Integer? = LeggiOfferta(p, idMossa)
                    If Not importo.HasValue Then Throw New MossaNonConsentita()
                    _asta.Offerta = importo.Value
                    _asta.Leader = p
                    eventi.Add(New EventoMotore(Nothing, nome & " offre " & Euro(importo.Value) & " per " & NomeCasella(_asta.Casella) & "."))
                    _asta.Indice = (_asta.Indice + 1) Mod _asta.Partecipanti.Count
                    ControllaAsta(eventi)
                Case "ritirati_asta"
                    eventi.Add(New EventoMotore(Nothing, nome & " si ritira dall'asta."))
                    _asta.Partecipanti.RemoveAt(_asta.Indice)
                    If _asta.Partecipanti.Count > 0 Then _asta.Indice = _asta.Indice Mod _asta.Partecipanti.Count
                    ControllaAsta(eventi)
                Case "costruisci"
                    Dim i As Integer = Integer.Parse(parti(1))
                    Dim costo As Integer = CASELLE(i).Costo
                    _soldi(p) -= costo
                    _prop(i).Case_ += 1
                    eventi.Add(New EventoMotore(Nothing, nome & " costruisce " & If(_prop(i).Case_ = 5, "un albergo", "una casa") & " a " & NomeCasella(i) & " per " & Euro(costo) & "."))
                Case "vendi"
                    Dim i As Integer = Integer.Parse(parti(1))
                    _prop(i).Case_ -= 1
                    _soldi(p) += CASELLE(i).Costo \ 2
                    eventi.Add(New EventoMotore(Nothing, nome & " vende un edificio a " & NomeCasella(i) & "."))
                Case "ipoteca"
                    Dim i As Integer = Integer.Parse(parti(1))
                    _prop(i).Ipoteca = True
                    _soldi(p) += Prezzo(i) \ 2
                    eventi.Add(New EventoMotore(Nothing, nome & " ipoteca " & NomeCasella(i) & "."))
                Case "riscatta"
                    Dim i As Integer = Integer.Parse(parti(1))
                    _prop(i).Ipoteca = False
                    _soldi(p) -= CostoRiscatto(i)
                    eventi.Add(New EventoMotore(Nothing, nome & " riscatta l'ipoteca di " & NomeCasella(i) & "."))
                Case "scambio"
                    Dim t As Scambio = LeggiScambio(p, idMossa)
                    If t Is Nothing Then Throw New MossaNonConsentita()
                    _scambio = t
                    If _ultimoScambioBot Is Nothing Then
                        _ultimoScambioBot = Enumerable.Repeat(-99, 4).ToArray()
                    End If
                    _ultimoScambioBot(p) = _giri   ' il computer non ripropone subito (vedi ScambioBot)
                    eventi.Add(New EventoMotore(Nothing, nome & " propone a " & _nomi(t.A) & " uno scambio: dà " & ElencoProprieta(t.DaiProp) & " e " & Euro(t.DaiSoldi) & ", chiede " & ElencoProprieta(t.ChiediProp) & " e " & Euro(t.ChiediSoldi) & "."))
                    If EComputer(t.A) Then
                        If ValutaScambioComputer(t) Then
                            ConcludiScambio(t, eventi)
                        Else
                            eventi.Add(New EventoMotore(Nothing, _nomi(t.A) & " rifiuta lo scambio."))
                            _scambio = Nothing
                        End If
                    End If
                Case "accetta_scambio"
                    ConcludiScambio(_scambio, eventi)
                Case "rifiuta_scambio"
                    eventi.Add(New EventoMotore(Nothing, nome & " rifiuta lo scambio proposto da " & _nomi(_scambio.Da) & "."))
                    _scambio = Nothing
                Case Else
                    Throw New MossaNonConsentita()
            End Select
            ChiudiTurnoSeFinito(eventi)
            Return eventi
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Applica", ex)
            Throw
        End Try
    End Function

    Private Function OpzioniScambioPossibile(p As Integer) As Boolean
        Try
            For Each g As JObject In Opzioni(p)
                If Convert.ToString(g("id")) = "scambio" Then Return True
            Next
            Return False
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.OpzioniScambioPossibile", ex)
            Throw
        End Try
    End Function

    Private Function EComputer(q As Integer) As Boolean
        Try
            Return _computer IsNot Nothing AndAlso q < _computer.Length AndAlso _computer(q)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.EComputer", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una copia dello stato (per provare più mosse insieme senza rovinare la partita se una non va bene).</summary>
    Private Function Copia() As MotoreMonopoli
        Try
            Dim c As MotoreMonopoli = DirectCast(Me.MemberwiseClone(), MotoreMonopoli)
            c._soldi = CType(_soldi.Clone(), Integer())
            c._pos = CType(_pos.Clone(), Integer())
            c._prigione = CType(_prigione.Clone(), Integer())
            c._esci = CType(_esci.Clone(), Integer())
            c._fuori = CType(_fuori.Clone(), Boolean())
            c._prop = _prop.Select(Function(x) If(x Is Nothing, Nothing, New Prop With {.Di = x.Di, .Case_ = x.Case_, .Ipoteca = x.Ipoteca})).ToArray()
            c._imprevisti = New List(Of Integer)(_imprevisti)
            c._probabilita = New List(Of Integer)(_probabilita)
            c._dadi = CType(_dadi.Clone(), Integer())
            If _ultimoScambioBot IsNot Nothing Then c._ultimoScambioBot = CType(_ultimoScambioBot.Clone(), Integer())
            Return c
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Copia", ex)
            Throw
        End Try
    End Function

    Private Sub CopiaDa(c As MotoreMonopoli)
        Try
            _soldi = c._soldi
            _pos = c._pos
            _prigione = c._prigione
            _esci = c._esci
            _fuori = c._fuori
            _prop = c._prop
            _piatto = c._piatto
            _imprevisti = c._imprevisti
            _probabilita = c._probabilita
            _turno = c._turno
            _fase = c._fase
            _doppi = c._doppi
            _dadi = c._dadi
            _giri = c._giri
            _scambio = c._scambio
            _asta = c._asta
            _finita = c._finita
            _vincitore = c._vincitore
            _motivo = c._motivo
            _punteggi = c._punteggi
            _ultimoScambioBot = c._ultimoScambioBot
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.CopiaDa", ex)
            Throw
        End Try
    End Sub

    Public Sub ChiudiTurnoSeFinito(eventi As List(Of EventoMotore))
        Try
            If Not _finita AndAlso _fase = "fine" AndAlso _asta Is Nothing AndAlso _scambio Is Nothing Then Prossimo(eventi)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ChiudiTurnoSeFinito", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ asta
    Private Function LeggiOfferta(p As Integer, id As String) As Integer?
        Try
            Dim parti As String() = id.Split("|"c)
            If parti.Length <> 2 OrElse _asta Is Nothing OrElse Attore <> p Then Return Nothing
            Dim n As Integer
            If Not parti(1).All(Function(ch) Char.IsDigit(ch)) OrElse parti(1).Length = 0 OrElse Not Integer.TryParse(parti(1), n) Then Return Nothing
            Dim min As Integer = If(_asta.Offerta > 0, _asta.Offerta + 10, 10)
            Return If(n >= min AndAlso n <= _soldi(p), CType(n, Integer?), Nothing)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.LeggiOfferta", ex)
            Throw
        End Try
    End Function

    Private Sub AvviaAsta(p As Integer, casella As Integer, eventi As List(Of EventoMotore))
        Try
            Dim ordine As New List(Of Integer)()
            For k As Integer = 1 To _n
                Dim q As Integer = (p + k) Mod _n
                If Not _fuori(q) AndAlso _soldi(q) >= 10 Then ordine.Add(q)
            Next
            If ordine.Count = 0 Then
                eventi.Add(New EventoMotore(Nothing, "Nessuno può partecipare all'asta: " & NomeCasella(casella) & " resta libera."))
                DopoAzione(p, eventi)
                Return
            End If
            _asta = New Asta With {.Casella = casella, .Partecipanti = ordine, .Indice = 0, .Offerta = 0, .Leader = Nothing, .Da = p}
            _fase = "asta"
            eventi.Add(New EventoMotore(Nothing, "Asta per " & NomeCasella(casella) & ", prezzo di listino " & Euro(Prezzo(casella)) & ". Si parte da " & _nomi(ordine(0)) & "."))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.AvviaAsta", ex)
            Throw
        End Try
    End Sub

    Private Sub ControllaAsta(eventi As List(Of EventoMotore))
        Try
            Dim a As Asta = _asta
            Dim n As Integer = a.Partecipanti.Count
            Dim fine As Boolean = False
            If n = 0 Then
                eventi.Add(New EventoMotore(Nothing, "Nessuno offre: " & NomeCasella(a.Casella) & " resta libera."))
                fine = True
            ElseIf a.Leader.HasValue AndAlso n = 1 Then
                Dim vincitore As Integer = a.Leader.Value
                _soldi(vincitore) -= a.Offerta
                _prop(a.Casella).Di = vincitore
                eventi.Add(New EventoMotore(Nothing, _nomi(vincitore) & " vince l'asta e compra " & NomeCasella(a.Casella) & " per " & Euro(a.Offerta) & "."))
                fine = True
            End If
            If fine Then
                Dim chi As Integer = a.Da
                _asta = Nothing
                DopoAzione(chi, eventi)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ControllaAsta", ex)
            Throw
        End Try
    End Sub

    Private Function ValoreAsta(q As Integer, i As Integer) As Integer
        Try
            Dim v As Integer = Prezzo(i)
            If CASELLE(i).Tipo = "terreno" Then
                Dim miei As Integer = 0
                Dim altri As New Dictionary(Of Integer, Integer)()
                For Each k As Integer In Gruppo(i)
                    If k = i Then Continue For
                    Dim di As Integer? = _prop(k).Di
                    If di.HasValue AndAlso di.Value = q Then
                        miei += 1
                    ElseIf di.HasValue Then
                        altri(di.Value) = If(altri.ContainsKey(di.Value), altri(di.Value), 0) + 1
                    End If
                Next
                Dim totale As Integer = Gruppo(i).Count - 1
                If miei = totale Then
                    v += 300
                ElseIf miei > 0 Then
                    v += 80
                End If
                For Each n As Integer In altri.Values
                    If n = totale Then v += 150
                Next
            ElseIf CASELLE(i).Tipo = "stazione" Then
                Dim mie As Integer = 0
                For k As Integer = 0 To _prop.Length - 1
                    If CASELLE(k).Tipo = "stazione" AndAlso Possiede(k, q) Then mie += 1
                Next
                v += 30 + 40 * mie
            End If
            Return v
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ValoreAsta", ex)
            Throw
        End Try
    End Function

    Private Sub Prossimo(eventi As List(Of EventoMotore))
        Try
            If _finita Then Return
            Dim p As Integer = _turno
            For k As Integer = 1 To _n
                Dim q As Integer = (p + k) Mod _n
                If Not _fuori(q) Then
                    If q <= p Then
                        _giri += 1
                        If _giri >= GIRI_MASSIMI Then
                            Dim migliore As Integer = q
                            Dim max As Integer = -1
                            For Each a As Integer In Attivi()
                                Dim pat As Integer = Patrimonio(a)
                                If pat > max Then
                                    max = pat
                                    migliore = a
                                End If
                            Next
                            TerminaConVincitore(migliore, eventi, "Sono passati " & GIRI_MASSIMI.ToString() & " giri: vince chi ha più patrimonio.")
                            Return
                        End If
                    End If
                    _turno = q
                    _fase = "tiro"
                    _doppi = 0
                    eventi.Add(New EventoMotore(Nothing, "Tocca a " & _nomi(q) & "."))
                    Return
                End If
            Next
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Prossimo", ex)
            Throw
        End Try
    End Sub

    Private Sub DopoAzione(p As Integer, eventi As List(Of EventoMotore))
        Try
            If _finita Then Return
            If _fuori(p) Then
                Prossimo(eventi)
                Return
            End If
            If _doppi > 0 AndAlso _prigione(p) < 0 Then
                _fase = "tiro"
                eventi.Add(New EventoMotore(Nothing, _nomi(p) & " ha fatto un doppio e tira ancora."))
            Else
                _fase = "fine"
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.DopoAzione", ex)
            Throw
        End Try
    End Sub

    Private Sub Tira(p As Integer, eventi As List(Of EventoMotore))
        Try
            Dim nome As String = _nomi(p)
            Dim d1 As Integer = Dado()
            Dim d2 As Integer = Dado()
            _dadi = {d1, d2}
            Dim doppio As Boolean = d1 = d2
            eventi.Add(New EventoMotore(Nothing, nome & " tira i dadi: " & d1.ToString() & " e " & d2.ToString() & ", totale " & (d1 + d2).ToString() & If(doppio, ", doppio", "") & "."))

            If _prigione(p) >= 0 Then
                If doppio Then
                    _prigione(p) = -1
                    eventi.Add(New EventoMotore(Nothing, nome & " esce di prigione con un doppio."))
                    _doppi = 0
                    Muovi(p, d1 + d2, True, eventi)
                    If Not _finita AndAlso _fase = "tiro" Then _fase = "fine"
                    Return
                End If
                _prigione(p) += 1
                If _prigione(p) >= 3 Then
                    eventi.Add(New EventoMotore(Nothing, nome & " non ha fatto doppi per tre volte e paga 50 euro per uscire."))
                    If Not Paga(p, Nothing, 50, eventi) Then
                        Prossimo(eventi)
                        Return
                    End If
                    _prigione(p) = -1
                    _doppi = 0
                    Muovi(p, d1 + d2, True, eventi)
                    If Not _finita AndAlso _fase = "tiro" Then _fase = "fine"
                    Return
                End If
                eventi.Add(New EventoMotore(Nothing, nome & " resta in prigione."))
                _fase = "fine"
                Return
            End If

            If doppio Then
                _doppi += 1
                If _doppi >= 3 Then
                    eventi.Add(New EventoMotore(Nothing, nome & " ha fatto tre doppi di fila e va in prigione."))
                    VaiInPrigione(p, eventi)
                    _doppi = 0
                    _fase = "fine"
                    Return
                End If
            Else
                _doppi = 0
            End If
            Muovi(p, d1 + d2, True, eventi)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Tira", ex)
            Throw
        End Try
    End Sub

    Private Sub VaiInPrigione(p As Integer, eventi As List(Of EventoMotore))
        Try
            _pos(p) = 10
            _prigione(p) = 0
            eventi.Add(New EventoMotore(Nothing, _nomi(p) & " va in prigione."))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.VaiInPrigione", ex)
            Throw
        End Try
    End Sub

    Private Sub Muovi(p As Integer, passi As Integer, relativo As Boolean, eventi As List(Of EventoMotore))
        Try
            Dim nome As String = _nomi(p)
            Dim da As Integer = _pos(p)
            Dim a As Integer = If(relativo, (da + passi) Mod 40, passi)
            If (relativo AndAlso da + passi >= 40) OrElse (Not relativo AndAlso a < da) Then
                _soldi(p) += 200
                eventi.Add(New EventoMotore(Nothing, nome & " passa dal Via e ritira 200 euro."))
            End If
            _pos(p) = a
            eventi.Add(New EventoMotore(Nothing, nome & " arriva a " & CASELLE(a).Nome & "."))
            If a = 0 AndAlso (_variante = "doppio_via" OrElse _variante = "completo") Then
                _soldi(p) += 200
                eventi.Add(New EventoMotore(Nothing, nome & " si ferma sul Via e ritira altri 200 euro (totale 400 euro)."))
            End If
            Risolvi(p, eventi)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Muovi", ex)
            Throw
        End Try
    End Sub

    Private Sub Risolvi(p As Integer, eventi As List(Of EventoMotore))
        Try
            Dim nome As String = _nomi(p)
            Dim i As Integer = _pos(p)
            Dim cas As Casella = CASELLE(i)
            Select Case cas.Tipo
                Case "parcheggio"
                    If (_variante = "parcheggio_ricco" OrElse _variante = "completo") AndAlso _piatto > 0 Then
                        Dim piatto As Integer = _piatto
                        _soldi(p) += piatto
                        _piatto = 0
                        eventi.Add(New EventoMotore(Nothing, nome & " incassa il piatto del Parcheggio Gratuito pari a " & Euro(piatto) & "!"))
                    End If
                Case "terreno", "stazione", "societa"
                    Dim x As Prop = _prop(i)
                    If Not x.Di.HasValue Then
                        eventi.Add(New EventoMotore(Nothing, cas.Nome & " è libera e costa " & Euro(Prezzo(i)) & "."))
                        If _soldi(p) >= Prezzo(i) Then
                            _fase = "acquisto"
                            Return
                        End If
                        eventi.Add(New EventoMotore(Nothing, nome & " non ha abbastanza soldi per comprarla."))
                    ElseIf x.Di.Value <> p Then
                        Dim dovuto As Integer = Affitto(i, _dadi(0) + _dadi(1))
                        If x.Ipoteca Then
                            eventi.Add(New EventoMotore(Nothing, cas.Nome & " è ipotecata: non si paga l'affitto."))
                        Else
                            eventi.Add(New EventoMotore(Nothing, nome & " paga " & Euro(dovuto) & " di affitto a " & _nomi(x.Di.Value) & "."))
                            Paga(p, x.Di.Value, dovuto, eventi)
                        End If
                    End If
                Case "tassa"
                    eventi.Add(New EventoMotore(Nothing, nome & " paga " & Euro(cas.Prezzo) & " di tassa."))
                    Paga(p, Nothing, cas.Prezzo, eventi)
                Case "vai_prigione"
                    VaiInPrigione(p, eventi)
                    _doppi = 0
                    _fase = "fine"
                    Return
                Case "probabilita", "imprevisti"
                    Pesca(p, cas.Tipo = "imprevisti", eventi)
                    If _fase = "acquisto" Then Return
            End Select
            If _finita Then Return
            If _fuori(p) Then
                Prossimo(eventi)
                Return
            End If
            If _prigione(p) >= 0 Then
                _doppi = 0
                _fase = "fine"
                Return
            End If
            DopoAzione(p, eventi)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Risolvi", ex)
            Throw
        End Try
    End Sub

    Private Sub Pesca(p As Integer, imprevisti As Boolean, eventi As List(Of EventoMotore))
        Try
            Dim nome As String = _nomi(p)
            Dim mazzoCarte As Carta() = If(imprevisti, CARTE_IMPREVISTI, CARTE_PROBABILITA)
            Dim lista As List(Of Integer) = If(imprevisti, _imprevisti, _probabilita)
            Dim indice As Integer = lista(0)
            lista.RemoveAt(0)
            Dim carta As Carta = mazzoCarte(indice)
            eventi.Add(New EventoMotore(Nothing, nome & " pesca " & If(imprevisti, "un imprevisto", "una probabilità") & ": " & carta.Testo))
            Dim tieni As Boolean = False
            Select Case carta.Tipo
                Case "ricevi"
                    _soldi(p) += carta.P1
                Case "paga"
                    Paga(p, Nothing, carta.P1, eventi)
                Case "esci"
                    _esci(p) += 1
                    tieni = True
                Case "prigione"
                    VaiInPrigione(p, eventi)
                Case "vai"
                    Dim dest As Integer = carta.P1
                    Dim da As Integer = _pos(p)
                    If dest < da OrElse dest = 0 Then
                        _soldi(p) += 200
                        eventi.Add(New EventoMotore(Nothing, nome & " passa dal Via e ritira 200 euro."))
                    End If
                    _pos(p) = dest
                    eventi.Add(New EventoMotore(Nothing, nome & " arriva a " & NomeCasella(dest) & "."))
                    If dest = 0 AndAlso (_variante = "doppio_via" OrElse _variante = "completo") Then
                        _soldi(p) += 200
                        eventi.Add(New EventoMotore(Nothing, nome & " si ferma sul Via e ritira altri 200 euro (totale 400 euro)."))
                    End If
                    If dest <> 0 Then Risolvi(p, eventi)
                Case "indietro"
                    _pos(p) = (_pos(p) + 37) Mod 40
                    eventi.Add(New EventoMotore(Nothing, nome & " arriva a " & NomeCasella(_pos(p)) & "."))
                    Risolvi(p, eventi)
                Case "compleanno"
                    For Each q As Integer In Attivi()
                        If q <> p Then Paga(q, p, carta.P1, eventi)
                    Next
                Case "paga_tutti"
                    For Each q As Integer In Attivi()
                        If q <> p AndAlso Not _fuori(p) Then Paga(p, q, carta.P1, eventi)
                    Next
                Case "riparazioni"
                    Dim dovuto As Integer = 0
                    For i As Integer = 0 To _prop.Length - 1
                        If Possiede(i, p) Then dovuto += If(_prop(i).Case_ = 5, carta.P2, _prop(i).Case_ * carta.P1)
                    Next
                    If dovuto > 0 Then
                        Paga(p, Nothing, dovuto, eventi)
                    Else
                        eventi.Add(New EventoMotore(Nothing, nome & " non ha edifici e non paga nulla."))
                    End If
            End Select
            If Not tieni Then lista.Add(indice)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Pesca", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ vista
    Public Function Vista(posto As Integer) As JObject Implements IMotoreGioco.Vista
        Try
            Dim elencoCaselle As New JArray()
            For i As Integer = 0 To CASELLE.Length - 1
                Dim c As Casella = CASELLE(i)
                Dim r As JObject = Attrezzi.J("indice", i, "nome", c.Nome, "tipo", c.Tipo)
                If EProprieta(i) Then
                    r("prezzo") = c.Prezzo
                    r("colore") = If(c.Tipo = "terreno", CType(c.Colore, JToken), JValue.CreateNull())
                    r("valore_ipoteca") = c.Prezzo \ 2
                    If c.Tipo = "terreno" Then
                        r("affitti") = Attrezzi.Arr(c.Affitti)
                        r("costo_edificio") = c.Costo
                        r("gruppo") = Attrezzi.Arr(Gruppo(i).Select(Function(k) CASELLE(k).Nome))
                    ElseIf c.Tipo = "stazione" Then
                        r("affitti") = Attrezzi.Arr({25, 50, 100, 200})
                    Else
                        r("affitti") = Attrezzi.Arr({4, 10})
                    End If
                    Dim x As Prop = _prop(i)
                    r("proprietario") = If(x.Di.HasValue, CType(x.Di.Value, JToken), JValue.CreateNull())
                    r("case") = x.Case_
                    r("ipoteca") = x.Ipoteca
                ElseIf c.Tipo = "tassa" Then
                    r("prezzo") = c.Prezzo
                End If
                elencoCaselle.Add(r)
            Next
            Dim giocatori As New JArray()
            For i As Integer = 0 To _n - 1
                giocatori.Add(Attrezzi.J("nome", _nomi(i), "soldi", _soldi(i), "posizione", _pos(i), "prigione", _prigione(i) >= 0, "fuori", _fuori(i),
                                         "carte_esci", _esci(i), "patrimonio", If(_fuori(i), 0, Patrimonio(i))))
            Next
            Dim scambio As JToken = JValue.CreateNull()
            If _scambio IsNot Nothing Then scambio = Attrezzi.J("da", _scambio.Da, "a", _scambio.A, "testo", DescriviScambio(_scambio))
            Dim asta As JToken = JValue.CreateNull()
            If _asta IsNot Nothing Then
                asta = Attrezzi.J("casella", _asta.Casella, "nome", NomeCasella(_asta.Casella), "offerta", _asta.Offerta,
                                  "leader", If(_asta.Leader.HasValue, CType(_asta.Leader.Value, JToken), JValue.CreateNull()), "attore", Attore)
            End If
            Return Attrezzi.J("tipo", "monopoli", "turno", _turno, "fase", _fase, "dadi", Attrezzi.Arr(_dadi), "caselle", elencoCaselle, "giocatori", giocatori,
                              "finita", _finita, "vincitore", If(_vincitore.HasValue, CType(_vincitore.Value, JToken), JValue.CreateNull()), "giri", _giri,
                              "giri_massimi", GIRI_MASSIMI, "punteggi", If(_punteggi IsNot Nothing, CType(Attrezzi.Arr(_punteggi), JToken), JValue.CreateNull()),
                              "variante", _variante, "variante_nome", NOMI_VARIANTI(_variante), "parcheggio_piatto", _piatto,
                              "case_banca", CaseDisponibili, "alberghi_banca", AlberghiDisponibili, "scambio", scambio, "asta", asta, "attore", Attore)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Vista", ex)
            Throw
        End Try
    End Function

    Public Function Situazione(posto As Integer) As String Implements IMotoreGioco.Situazione
        Try
            Dim t As String = ""
            If _variante <> "classica" Then t &= "[Variante: " & NOMI_VARIANTI(_variante) & "] "
            If (_variante = "parcheggio_ricco" OrElse _variante = "completo") AndAlso _piatto > 0 Then t &= "[Piatto Parcheggio: " & Euro(_piatto) & "] "
            For i As Integer = 0 To _n - 1
                t &= _nomi(i) & If(i = posto, " (tu)", "") & ": "
                If _fuori(i) Then
                    t &= "eliminato. "
                    Continue For
                End If
                t &= Euro(_soldi(i)) & ", sulla casella " & NomeCasella(_pos(i)) & If(_prigione(i) >= 0, " (in prigione)", "")
                Dim mie As New List(Of String)()
                For k As Integer = 0 To _prop.Length - 1
                    If Possiede(k, i) Then
                        Dim x As Prop = _prop(k)
                        mie.Add(NomeCasella(k) & If(x.Ipoteca, " (ipotecata)", If(x.Case_ = 5, " (albergo)", If(x.Case_ > 0, " (" & x.Case_.ToString() & " case)", ""))))
                    End If
                Next
                t &= If(mie.Count > 0, ". Proprietà: " & String.Join(", ", mie) & ". ", ". Nessuna proprietà. ")
            Next
            Return t.Trim()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Situazione", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ computer
    ''' <summary>Il peggior affitto che oggi gli avversari possono chiedere a p (con un tiro medio): serve a decidere quanti soldi tenere da parte.</summary>
    Private Function Esposizione(p As Integer) As Integer
        Try
            Dim massimo As Integer = 0
            For k As Integer = 0 To _prop.Length - 1
                Dim x As Prop = _prop(k)
                If x IsNot Nothing AndAlso x.Di.HasValue AndAlso x.Di.Value <> p AndAlso Not x.Ipoteca AndAlso Not _fuori(x.Di.Value) Then massimo = Math.Max(massimo, Affitto(k, 7))
            Next
            Return massimo
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Esposizione", ex)
            Throw
        End Try
    End Function

    ''' <summary>Quanti soldi il computer tiene da parte: pochi all'inizio, quando conviene comprare tutto, di più quando gli avversari hanno costruito.</summary>
    Private Function RiservaBot(p As Integer, minima As Integer) As Integer
        Try
            Dim r As Integer = Math.Max(minima, Math.Min(350, CInt(Math.Round(Esposizione(p) * 0.6, MidpointRounding.AwayFromZero))))
            If _giri < 12 Then r = Math.Min(r, Math.Max(minima, 100))
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.RiservaBot", ex)
            Throw
        End Try
    End Function

    Private Function ProprietaLibere() As Integer
        Try
            Dim n As Integer = 0
            For k As Integer = 0 To _prop.Length - 1
                If _prop(k) IsNot Nothing AndAlso Not _prop(k).Di.HasValue Then n += 1
            Next
            Return n
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ProprietaLibere", ex)
            Throw
        End Try
    End Function

    Private Function UltimoScambioBot(p As Integer) As Integer
        Try
            If _ultimoScambioBot Is Nothing OrElse p >= _ultimoScambioBot.Length Then Return -99
            Return _ultimoScambioBot(p)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.UltimoScambioBot", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' Se a un colore manca una sola proprietà e ce l'ha un altro giocatore, il computer prova a comprargliela offrendo il prezzo più 320 euro
    ''' (un computer non aiuta nessuno a completare un colore per meno): senza colori completi non si costruisce e la partita non si decide.
    ''' Al massimo una proposta ogni 4 giri. Restituisce la mossa "scambio|..." oppure Nothing.
    ''' </summary>
    Private Function ScambioBot(p As Integer) As String
        Try
            If UltimoScambioBot(p) > _giri - 4 Then Return Nothing
            Dim soldi As Integer = _soldi(p)
            Dim migliore As String = Nothing
            Dim valoreMigliore As Integer = 0
            For i As Integer = 0 To CASELLE.Length - 1
                If CASELLE(i).Tipo <> "terreno" Then Continue For
                Dim x As Prop = _prop(i)
                If Not x.Di.HasValue OrElse x.Di.Value = p OrElse x.Ipoteca OrElse _fuori(x.Di.Value) OrElse CaseNelGruppo(i) > 0 Then Continue For
                Dim q As Integer = x.Di.Value
                Dim gr As List(Of Integer) = Gruppo(i)
                Dim miei As Integer = 0
                For Each k As Integer In gr
                    If Possiede(k, p) AndAlso Not _prop(k).Ipoteca Then miei += 1
                Next
                If miei <> gr.Count - 1 Then Continue For
                Dim offerta As Integer = CInt(Math.Ceiling((Prezzo(i) + 320) / 10.0)) * 10
                If soldi - offerta < 400 Then Continue For
                Dim valore As Integer = CASELLE(i).Costo * gr.Count   ' i colori più cari rendono di più
                If valore > valoreMigliore Then
                    valoreMigliore = valore
                    migliore = "scambio|" & q.ToString() & "|-|" & offerta.ToString() & "|" & i.ToString() & "|0"
                End If
            Next
            Return migliore
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.ScambioBot", ex)
            Throw
        End Try
    End Function

    Public Function Bot(p As Integer) As String Implements IMotoreGioco.Bot
        Try
            Dim mosse As List(Of MossaMotore) = Me.Mosse(p)
            Dim ids As List(Of String) = mosse.Select(Function(x) x.Id).ToList()
            Dim soldi As Integer = _soldi(p)

            ' risposta a una proposta di scambio (di solito il motore risponde per il computer subito: vedi ValutaScambioComputer)
            If _scambio IsNot Nothing AndAlso _scambio.A = p Then Return If(ValutaScambioComputer(_scambio), "accetta_scambio", "rifiuta_scambio")

            ' asta: si offre fino a quello che la proprietà vale davvero per questo giocatore (di più se completa un colore o lo toglie a un avversario)
            If _asta IsNot Nothing Then
                Dim a As Asta = _asta
                Dim massimo As Integer = Math.Min(CInt(Math.Round(ValoreAsta(p, a.Casella) * 1.2, MidpointRounding.AwayFromZero)), soldi - 30)
                Dim prossima As Integer = If(a.Offerta > 0, a.Offerta + 10, Math.Max(10, CInt(Math.Round(Prezzo(a.Casella) * 0.3 / 10, MidpointRounding.AwayFromZero)) * 10))
                If ids.Contains("offri") AndAlso prossima <= massimo AndAlso prossima <= soldi Then Return "offri|" & prossima.ToString()
                Return "ritirati_asta"
            End If

            If _fase = "acquisto" Then
                Dim i As Integer = _pos(p)
                Dim costo As Integer = Prezzo(i)
                Dim tipo As String = CASELLE(i).Tipo
                Dim completa As Boolean = False
                Dim blocca As Boolean = False
                Dim miei As Integer = 0
                If tipo = "terreno" Then
                    Dim altri As Integer = Gruppo(i).Count - 1
                    Dim degli As New Dictionary(Of Integer, Integer)()
                    For Each k As Integer In Gruppo(i)
                        If k = i Then Continue For
                        Dim di As Integer? = _prop(k).Di
                        If di.HasValue AndAlso di.Value = p Then
                            miei += 1
                        ElseIf di.HasValue Then
                            degli(di.Value) = If(degli.ContainsKey(di.Value), degli(di.Value), 0) + 1
                        End If
                    Next
                    completa = miei = altri
                    For Each n As Integer In degli.Values
                        If n = altri Then blocca = True   ' un avversario ha tutto il resto del colore: comprarla glielo impedisce
                    Next
                End If
                Dim riserva As Integer = RiservaBot(p, 60)
                Dim resto As Integer = soldi - costo
                Dim compra As Boolean = ids.Contains("compra") AndAlso (
                    resto >= riserva OrElse completa OrElse (blocca AndAlso resto >= 30) OrElse (miei > 0 AndAlso resto >= 40) OrElse
                    (tipo = "stazione" AndAlso resto >= 50) OrElse (tipo = "societa" AndAlso resto >= 80))
                Return If(compra, "compra", "passa")
            End If

            If _fase = "tiro" AndAlso _prigione(p) >= 0 Then
                If ids.Contains("usa_carta") Then Return "usa_carta"
                ' all'inizio, con proprietà ancora da comprare, conviene uscire; più avanti, con gli avversari che hanno costruito, in prigione si è al sicuro
                If ids.Contains("paga_prigione") AndAlso ProprietaLibere() >= 6 AndAlso soldi >= 250 Then Return "paga_prigione"
                If ids.Contains("paga_prigione") AndAlso soldi > 900 AndAlso Esposizione(p) < 250 Then Return "paga_prigione"
                Return "tira"
            End If

            Dim opzioni As JArray = Me.Opzioni(p)
            If _fase = "tiro" AndAlso _prigione(p) < 0 AndAlso opzioni.OfType(Of JObject)().Any(Function(g) Convert.ToString(g("id")) = "scambio") Then
                Dim proposta As String = ScambioBot(p)
                If proposta IsNot Nothing Then Return proposta
            End If
            Dim gestione As New List(Of MossaMotore)()
            For Each g As JObject In opzioni
                For Each v As JObject In CType(g("voci"), JArray)
                    gestione.Add(New MossaMotore(Convert.ToString(v("id")), Convert.ToString(v("testo"))))
                Next
            Next
            Dim esposizioneP As Integer = Esposizione(p)
            Dim riservaCostruzione As Integer = Math.Max(100, Math.Min(300, CInt(Math.Round(esposizioneP * 0.5, MidpointRounding.AwayFromZero))))

            ' costruire: prima dove ogni euro speso fa crescere di più l'affitto
            Dim migliore As String = Nothing
            Dim punteggioMigliore As Double = -1.0
            Dim serveSoldi As Integer? = Nothing   ' una costruzione buona ma senza abbastanza soldi: si può ipotecare qualcosa per farla
            For Each x As MossaMotore In gestione
                If Not x.Id.StartsWith("costruisci|", StringComparison.Ordinal) Then Continue For
                Dim i As Integer = Integer.Parse(x.Id.Split("|"c)(1))
                Dim caseN As Integer = _prop(i).Case_
                Dim costo As Integer = CASELLE(i).Costo
                Dim aff As Integer() = CASELLE(i).Affitti
                Dim punteggio As Double = (aff(Math.Min(5, caseN + 1)) - aff(caseN)) / CDbl(costo)
                If soldi - costo >= riservaCostruzione Then
                    If punteggio > punteggioMigliore Then
                        punteggioMigliore = punteggio
                        migliore = x.Id
                    End If
                ElseIf Not serveSoldi.HasValue OrElse costo < serveSoldi.Value Then
                    serveSoldi = costo
                End If
            Next
            If migliore IsNot Nothing Then Return migliore
            If serveSoldi.HasValue Then
                ' si ipoteca una proprietà che non fa parte di un colore in costruzione né di un colore a cui ne manca una sola
                For Each x As MossaMotore In gestione
                    If Not x.Id.StartsWith("ipoteca|", StringComparison.Ordinal) Then Continue For
                    Dim k As Integer = Integer.Parse(x.Id.Split("|"c)(1))
                    If CASELLE(k).Tipo = "terreno" Then
                        Dim nelColore As Integer = 0
                        For Each g As Integer In Gruppo(k)
                            If Possiede(g, p) Then nelColore += 1
                        Next
                        If nelColore > 1 Then Continue For
                    End If
                    If soldi + Prezzo(k) \ 2 - serveSoldi.Value >= riservaCostruzione Then Return x.Id
                Next
            End If

            ' riscattare le ipoteche quando i soldi sono in abbondanza
            Dim riservaRiscatto As Integer = Math.Max(250, RiservaBot(p, 60) + 150)
            For Each x As MossaMotore In gestione
                If x.Id.StartsWith("riscatta|", StringComparison.Ordinal) Then
                    Dim k As Integer = Integer.Parse(x.Id.Split("|"c)(1))
                    If soldi - CInt(Math.Ceiling(Prezzo(k) / 2.0 * 1.1)) >= riservaRiscatto Then Return x.Id
                End If
            Next
            Return "tira"
        Catch ex As Exception
            RegistroErrori.Registra("MotoreMonopoli.Bot", ex)
            Throw
        End Try
    End Function

End Class
