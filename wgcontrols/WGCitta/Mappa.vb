Imports System
Imports System.Collections.Generic
Imports System.Linq

' ============================================================================================
' Muoversi sulla pianta: dove si è su un tratto, le svolte a un incrocio, la strada più breve, le direzioni a parole.
' ============================================================================================

''' <summary>Dove si trova qualcosa che va per strada: su un tratto, a una distanza dal capo A, nel verso di B (+1) o di A (-1).</summary>
Public NotInheritable Class Posizione
    Public Tratto As Tratto
    Public Distanza As Double
    Public Verso As Integer = 1

    Public Sub New(t As Tratto, distanza As Double, verso As Integer)
        Try
            Tratto = t
            Me.Distanza = distanza
            Me.Verso = If(verso >= 0, 1, -1)
        Catch ex As Exception
            RegistroErrori.Registra("Posizione.New", ex)
            Throw
        End Try
    End Sub

    Public Function Copia() As Posizione
        Try
            Return New Posizione(Tratto, Distanza, Verso)
        Catch ex As Exception
            RegistroErrori.Registra("Posizione.Copia", ex)
            Throw
        End Try
    End Function

    Public ReadOnly Property X As Double
        Get
            Dim px, py As Double
            Tratto.Punto(Distanza, px, py)
            Return px
        End Get
    End Property

    Public ReadOnly Property Y As Double
        Get
            Dim px, py As Double
            Tratto.Punto(Distanza, px, py)
            Return py
        End Get
    End Property

    ''' <summary>La direzione in cui si va, in radianti (0 = est, pi/2 = nord).</summary>
    Public ReadOnly Property Angolo As Double
        Get
            Dim dx As Double = (Tratto.B.X - Tratto.A.X) * Verso
            Dim dy As Double = (Tratto.B.Y - Tratto.A.Y) * Verso
            Return Math.Atan2(dy, dx)
        End Get
    End Property

    ''' <summary>L'incrocio verso cui si va.</summary>
    Public ReadOnly Property Prossimo As Incrocio
        Get
            Return If(Verso > 0, Tratto.B, Tratto.A)
        End Get
    End Property

    ''' <summary>L'incrocio da cui si viene.</summary>
    Public ReadOnly Property Precedente As Incrocio
        Get
            Return If(Verso > 0, Tratto.A, Tratto.B)
        End Get
    End Property

    ''' <summary>Quanti metri mancano al prossimo incrocio.</summary>
    Public ReadOnly Property AllIncrocio As Double
        Get
            Return If(Verso > 0, Tratto.Lunghezza - Distanza, Distanza)
        End Get
    End Property

    ''' <summary>Va avanti (o indietro, con metri negativi) lungo il tratto, senza superare gli incroci. Restituisce i metri che avanzano oltre l'incrocio.</summary>
    Public Function Avanza(metri As Double) As Double
        Try
            Dim nuova As Double = Distanza + metri * Verso
            If nuova > Tratto.Lunghezza Then
                Dim resto As Double = nuova - Tratto.Lunghezza
                Distanza = Tratto.Lunghezza
                Return resto
            End If
            If nuova < 0 Then
                Dim resto As Double = -nuova
                Distanza = 0
                Return resto
            End If
            Distanza = nuova
            Return 0
        Catch ex As Exception
            RegistroErrori.Registra("Posizione.Avanza", ex)
            Throw
        End Try
    End Function

    ''' <summary>Imbocca un altro tratto dall'incrocio dove si è arrivati.</summary>
    Public Sub Imbocca(t As Tratto, daIncrocio As Incrocio)
        Try
            Tratto = t
            If t.A Is daIncrocio Then
                Distanza = 0
                Verso = 1
            Else
                Distanza = t.Lunghezza
                Verso = -1
            End If
        Catch ex As Exception
            RegistroErrori.Registra("Posizione.Imbocca", ex)
            Throw
        End Try
    End Sub

    Public Sub Inverti()
        Try
            Verso = -Verso
        Catch ex As Exception
            RegistroErrori.Registra("Posizione.Inverti", ex)
            Throw
        End Try
    End Sub
End Class

''' <summary>Una svolta possibile a un incrocio: il tratto e come si chiama rispetto a chi arriva.</summary>
Public NotInheritable Class Svolta
    Public Tratto As Tratto
    ''' <summary>-1 sinistra, 0 dritto, 1 destra, 2 indietro.</summary>
    Public Verso As Integer
    Public ReadOnly Property Nome As String
        Get
            Select Case Verso
                Case -1 : Return "a sinistra"
                Case 1 : Return "a destra"
                Case 2 : Return "indietro"
                Case Else : Return "dritto"
            End Select
        End Get
    End Property
End Class

Public NotInheritable Class Mappa

    ''' <summary>L'angolo di un tratto uscendo da un incrocio.</summary>
    Public Shared Function AngoloUscita(t As Tratto, da As Incrocio) As Double
        Try
            Dim altro As Incrocio = t.Altro(da)
            Return Math.Atan2(altro.Y - da.Y, altro.X - da.X)
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.AngoloUscita", ex)
            Throw
        End Try
    End Function

    ''' <summary>La differenza tra due angoli, tra -pi e pi (positiva verso sinistra).</summary>
    Public Shared Function Differenza(a As Double, b As Double) As Double
        Try
            Dim d As Double = a - b
            While d > Math.PI
                d -= 2 * Math.PI
            End While
            While d < -Math.PI
                d += 2 * Math.PI
            End While
            Return d
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.Differenza", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le svolte a un incrocio per chi ci arriva andando nella direzione "angolo" lungo il tratto "arrivo".</summary>
    Public Shared Function Svolte(incrocio As Incrocio, arrivo As Tratto, angolo As Double) As List(Of Svolta)
        Try
            Dim r As New List(Of Svolta)()
            For Each t As Tratto In incrocio.Tratti
                Dim d As Double = Differenza(AngoloUscita(t, incrocio), angolo)
                Dim v As Integer
                If t Is arrivo Then
                    v = 2
                ElseIf Math.Abs(d) < Math.PI / 4 Then
                    v = 0
                ElseIf d > 0 Then
                    v = If(d > 3 * Math.PI / 4, 2, -1)
                Else
                    v = If(d < -3 * Math.PI / 4, 2, 1)
                End If
                r.Add(New Svolta With {.Tratto = t, .Verso = v})
            Next
            Return r.OrderBy(Function(s) If(s.Verso = 2, 9, s.Verso)).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.Svolte", ex)
            Throw
        End Try
    End Function

    ''' <summary>La direzione a parole (nord, nord est...).</summary>
    Public Shared Function Cardinale(angolo As Double) As String
        Try
            Dim nomi As String() = {"est", "nord est", "nord", "nord ovest", "ovest", "sud ovest", "sud", "sud est"}
            Dim k As Integer = CInt(Math.Round(angolo / (Math.PI / 4)))
            k = ((k Mod 8) + 8) Mod 8
            Return nomi(k)
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.Cardinale", ex)
            Throw
        End Try
    End Function

    ''' <summary>Distanze in metri dette a voce, arrotondate.</summary>
    Public Shared Function Metri(m As Double) As String
        Try
            If m >= 1000 Then Return (Math.Round(m / 100) / 10).ToString("0.#", Globalization.CultureInfo.GetCultureInfo("it-IT")) & " chilometri"
            If m >= 100 Then Return (Math.Round(m / 10) * 10).ToString() & " metri"
            Return Math.Max(5, Math.Round(m / 5) * 5).ToString() & " metri"
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.Metri", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' La strada più breve (Dijkstra) da una posizione a un punto di un tratto. Restituisce gli incroci da attraversare, in ordine, a partire
    ''' dal prossimo incrocio di chi parte; vuota se la meta è sul tratto dove si è già, davanti.
    ''' </summary>
    Public Shared Function Percorso(dati As DatiCitta, da As Posizione, verso As Tratto, distanzaMeta As Double) As List(Of Incrocio)
        Try
            ' già sul tratto giusto e la meta è davanti: niente incroci
            If da.Tratto Is verso AndAlso (distanzaMeta - da.Distanza) * da.Verso >= 0 Then Return New List(Of Incrocio)()
            Dim dist As New Dictionary(Of Incrocio, Double)()
            Dim prima As New Dictionary(Of Incrocio, Incrocio)()
            Dim partenza As Incrocio = da.Prossimo
            dist(partenza) = da.AllIncrocio
            Dim aperti As New HashSet(Of Incrocio) From {partenza}
            Dim chiusi As New HashSet(Of Incrocio)()
            While aperti.Count > 0
                Dim n As Incrocio = aperti.OrderBy(Function(x) dist(x)).First()
                aperti.Remove(n)
                chiusi.Add(n)
                For Each t As Tratto In n.Tratti
                    Dim m As Incrocio = t.Altro(n)
                    If chiusi.Contains(m) Then Continue For
                    ' le arterie sono più veloci: costano un po' meno
                    Dim costo As Double = dist(n) + t.Lunghezza * If(t.Strada.Arteria, 0.8, 1.0)
                    Dim vecchio As Double
                    If Not dist.TryGetValue(m, vecchio) OrElse costo < vecchio Then
                        dist(m) = costo
                        prima(m) = n
                        aperti.Add(m)
                    End If
                Next
            End While
            ' si arriva alla meta dal capo A o dal capo B del suo tratto: quello che costa meno
            Dim viaA As Double = If(dist.ContainsKey(verso.A), dist(verso.A) + distanzaMeta, Double.MaxValue)
            Dim viaB As Double = If(dist.ContainsKey(verso.B), dist(verso.B) + verso.Lunghezza - distanzaMeta, Double.MaxValue)
            Dim arrivo As Incrocio = If(viaA <= viaB, verso.A, verso.B)
            Dim r As New List(Of Incrocio)()
            Dim c As Incrocio = arrivo
            r.Add(c)
            While c IsNot partenza
                Dim p As Incrocio = Nothing
                If Not prima.TryGetValue(c, p) Then Exit While
                c = p
                r.Insert(0, c)
            End While
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.Percorso", ex)
            Throw
        End Try
    End Function

    ''' <summary>Quanti metri di strada ci sono da una posizione a un punto di un tratto, seguendo il percorso più breve.</summary>
    Public Shared Function Lunghezza(dati As DatiCitta, da As Posizione, verso As Tratto, distanzaMeta As Double) As Double
        Try
            Dim incroci As List(Of Incrocio) = Percorso(dati, da, verso, distanzaMeta)
            If incroci.Count = 0 Then Return Math.Abs(distanzaMeta - da.Distanza)
            Dim totale As Double = da.AllIncrocio
            For i As Integer = 0 To incroci.Count - 2
                Dim t As Tratto = TrattoTra(incroci(i), incroci(i + 1))
                If t IsNot Nothing Then totale += t.Lunghezza
            Next
            Dim ultimo As Incrocio = incroci(incroci.Count - 1)
            totale += If(ultimo Is verso.A, distanzaMeta, verso.Lunghezza - distanzaMeta)
            Return totale
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.Lunghezza", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il tratto che unisce due incroci vicini (Nothing se non c'è).</summary>
    Public Shared Function TrattoTra(a As Incrocio, b As Incrocio) As Tratto
        Try
            For Each t As Tratto In a.Tratti
                If t.Altro(a) Is b Then Return t
            Next
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Registra("Mappa.TrattoTra", ex)
            Throw
        End Try
    End Function
End Class
