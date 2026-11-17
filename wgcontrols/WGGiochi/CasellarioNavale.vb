Imports System
Imports System.Collections.Generic
Imports System.Text

''' <summary>Lo stato di una casella del casellario della battaglia navale.</summary>
Public Enum StatoCasella
    ''' <summary>Niente (o, nel mare nemico, una casella dove non si è ancora sparato).</summary>
    Vuota = 0
    ''' <summary>Una nave (si vede solo nella propria flotta).</summary>
    Nave = 1
    ''' <summary>Un colpo andato a vuoto: un buco nell'acqua.</summary>
    Acqua = 2
    ''' <summary>Una nave colpita ma non ancora affondata.</summary>
    Colpita = 3
    ''' <summary>Una nave affondata.</summary>
    Affondata = 4
End Enum

''' <summary>
''' Il casellario della battaglia navale: una griglia con le colonne A, B, C... e le righe 1, 2, 3... Ci si muove con le frecce e ogni casella
''' si legge come in un foglio di calcolo con NVDA o JAWS: prima quello che c'è, poi le coordinate ("acqua, B 3").
''' Serve per due cose: mettere le navi all'inizio (Invio posiziona la nave scelta, un tasto la gira) e poi sparare (Invio sul mare nemico).
''' Questa classe sa solo della griglia, del cursore e di cosa dire: il disegno e i tasti sono di SchermoGioco, le regole del gioco del server.
''' </summary>
Public NotInheritable Class CasellarioNavale

    Private ReadOnly _celle As StatoCasella(,)

    Public ReadOnly Property Righe As Integer
    Public ReadOnly Property Colonne As Integer
    ''' <summary>Il nome del mare: "Mare nemico", "La tua flotta".</summary>
    Public Property Titolo As String = ""
    Public Property Riga As Integer = 0
    Public Property Colonna As Integer = 0

    ''' <summary>Lunghezza della nave che si sta posizionando (0: non si sta posizionando).</summary>
    Public Property LunghezzaNave As Integer = 0
    ''' <summary>La nave che si posiziona sta in orizzontale (da sinistra a destra) o in verticale (dall'alto in basso).</summary>
    Public Property Orizzontale As Boolean = True
    ''' <summary>Vero: le navi non possono toccarsi nemmeno di lato o in diagonale.</summary>
    Public Property NaviDistanziate As Boolean = False

    Public Sub New(Optional righe As Integer = 10, Optional colonne As Integer = 10, Optional titolo As String = "")
        Try
            If righe < 1 OrElse righe > 99 OrElse colonne < 1 OrElse colonne > 26 Then Throw New ArgumentOutOfRangeException()
            Me.Righe = righe
            Me.Colonne = colonne
            Me.Titolo = titolo
            _celle = New StatoCasella(righe - 1, colonne - 1) {}
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.New", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ le caselle
    Public Function Stato(riga As Integer, colonna As Integer) As StatoCasella
        Try
            Return _celle(riga, colonna)
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Stato", ex)
            Throw
        End Try
    End Function

    Public Sub Imposta(riga As Integer, colonna As Integer, stato As StatoCasella)
        Try
            If riga < 0 OrElse riga >= Righe OrElse colonna < 0 OrElse colonna >= Colonne Then Return
            _celle(riga, colonna) = stato
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Imposta", ex)
            Throw
        End Try
    End Sub

    Public Sub Svuota()
        Try
            For r As Integer = 0 To Righe - 1
                For c As Integer = 0 To Colonne - 1
                    _celle(r, c) = StatoCasella.Vuota
                Next
            Next
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Svuota", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ il cursore
    ''' <summary>Sposta il cursore di dr righe e dc colonne. Falso se si è al bordo: il cursore resta dov'è.</summary>
    Public Function Muovi(dr As Integer, dc As Integer) As Boolean
        Try
            Dim r As Integer = Riga + dr
            Dim c As Integer = Colonna + dc
            If r < 0 OrElse r >= Righe OrElse c < 0 OrElse c >= Colonne Then Return False
            Riga = r
            Colonna = c
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Muovi", ex)
            Throw
        End Try
    End Function

    Public Sub VaiA(riga As Integer, colonna As Integer)
        Try
            Me.Riga = Math.Max(0, Math.Min(Righe - 1, riga))
            Me.Colonna = Math.Max(0, Math.Min(Colonne - 1, colonna))
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.VaiA", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ cosa dire
    ''' <summary>La lettera della colonna: 0 è A, 1 è B...</summary>
    Public Shared Function Lettera(colonna As Integer) As String
        Try
            Return Convert.ToChar(Asc("A"c) + colonna).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Lettera", ex)
            Throw
        End Try
    End Function

    ''' <summary>Le coordinate come si dicono a voce: "B 3" (colonna, riga; si contano da 1).</summary>
    Public Shared Function Coordinate(riga As Integer, colonna As Integer) As String
        Try
            Return Lettera(colonna) & " " & (riga + 1).ToString()
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Coordinate", ex)
            Throw
        End Try
    End Function

    Public Shared Function NomeStato(stato As StatoCasella) As String
        Try
            Select Case stato
                Case StatoCasella.Nave : Return "nave"
                Case StatoCasella.Acqua : Return "acqua"
                Case StatoCasella.Colpita : Return "colpita"
                Case StatoCasella.Affondata : Return "affondata"
                Case Else : Return "vuota"
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.NomeStato", ex)
            Throw
        End Try
    End Function

    ''' <summary>Come un foglio di calcolo con il lettore di schermo: prima il contenuto della casella, poi le coordinate. Per esempio "acqua, B 3".</summary>
    Public Function Frase() As String
        Try
            Return Frase(Riga, Colonna)
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Frase", ex)
            Throw
        End Try
    End Function

    Public Function Frase(riga As Integer, colonna As Integer) As String
        Try
            Dim testo As String = NomeStato(_celle(riga, colonna)) & ", " & Coordinate(riga, colonna)
            If LunghezzaNave > 0 Then
                ' mentre si posiziona dice anche se la nave ci sta
                testo &= If(PuoPosizionare(riga, colonna), ". Qui ci sta la nave da " & LunghezzaNave.ToString(), ". Qui la nave da " & LunghezzaNave.ToString() & " non ci sta")
            End If
            Return testo
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Frase", ex)
            Throw
        End Try
    End Function

    ' ------------------------------------------------------------------ posizionare le navi
    ''' <summary>Le caselle (riga, colonna) che occuperebbe la nave se partisse da qui, nel verso scelto. Anche quelle fuori dal casellario, se non ci sta.</summary>
    Public Function CaselleNave(riga As Integer, colonna As Integer) As List(Of Integer())
        Try
            Dim r As New List(Of Integer())()
            For i As Integer = 0 To LunghezzaNave - 1
                r.Add({riga + If(Orizzontale, 0, i), colonna + If(Orizzontale, i, 0)})
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.CaselleNave", ex)
            Throw
        End Try
    End Function

    ''' <summary>La nave ci sta (tutta dentro il casellario, senza toccare altre navi) partendo da qui?</summary>
    Public Function PuoPosizionare(riga As Integer, colonna As Integer) As Boolean
        Try
            If LunghezzaNave <= 0 Then Return False
            For Each x As Integer() In CaselleNave(riga, colonna)
                If x(0) < 0 OrElse x(0) >= Righe OrElse x(1) < 0 OrElse x(1) >= Colonne Then Return False
                If _celle(x(0), x(1)) <> StatoCasella.Vuota Then Return False
                If NaviDistanziate Then
                    For dr As Integer = -1 To 1
                        For dc As Integer = -1 To 1
                            Dim rr As Integer = x(0) + dr
                            Dim cc As Integer = x(1) + dc
                            If rr >= 0 AndAlso rr < Righe AndAlso cc >= 0 AndAlso cc < Colonne AndAlso _celle(rr, cc) = StatoCasella.Nave Then Return False
                        Next
                    Next
                End If
            Next
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.PuoPosizionare", ex)
            Throw
        End Try
    End Function

    ''' <summary>Mette la nave scelta partendo da qui. Falso se non ci sta (non cambia niente).</summary>
    Public Function Posiziona(riga As Integer, colonna As Integer) As Boolean
        Try
            If Not PuoPosizionare(riga, colonna) Then Return False
            For Each x As Integer() In CaselleNave(riga, colonna)
                _celle(x(0), x(1)) = StatoCasella.Nave
            Next
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Posiziona", ex)
            Throw
        End Try
    End Function

    ''' <summary>Gira la nave: da orizzontale a verticale e viceversa.</summary>
    Public Sub Ruota()
        Try
            Orizzontale = Not Orizzontale
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.Ruota", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Cosa si dice dopo aver posizionato una nave: "Nave da 4 posizionata, orizzontale, da B 3 a E 3".</summary>
    Public Function FrasePosizionata(riga As Integer, colonna As Integer) As String
        Try
            Dim fine As Integer() = CaselleNave(riga, colonna)(LunghezzaNave - 1)
            Return "Nave da " & LunghezzaNave.ToString() & " posizionata, " & If(Orizzontale, "orizzontale", "verticale") & ", da " & Coordinate(riga, colonna) & " a " & Coordinate(fine(0), fine(1)) & "."
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.FrasePosizionata", ex)
            Throw
        End Try
    End Function

    ''' <summary>Cosa si dice scegliendo la nave da posizionare o girandola.</summary>
    Public Function FraseNaveScelta() As String
        Try
            Return "Nave da " & LunghezzaNave.ToString() & ", " & If(Orizzontale, "orizzontale", "verticale") & "."
        Catch ex As Exception
            RegistroErrori.Registra("CasellarioNavale.FraseNaveScelta", ex)
            Throw
        End Try
    End Function
End Class
