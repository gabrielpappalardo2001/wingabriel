Imports Newtonsoft.Json.Linq

' ============================================================================================
' I giochi senza internet: il motore di gioco in Visual Basic.
'
' Sono le stesse regole del server (scripts\server\giochi, in PHP) riscritte qui, così si può giocare contro il computer anche senza
' collegamento. I motori (briscola, scopa, burraco, monopoli) producono le stesse mosse e le stesse viste del server, in JSON: lo schermo
' di gioco e il menu non sanno se stanno parlando con il server o con questo motore (vedi MotoreLocale).
' Se si cambiano le regole di un gioco nel server vanno cambiate anche qui, e viceversa.
' ============================================================================================

''' <summary>Un fatto della partita, da raccontare ai giocatori. Per è il posto a cui è riservato (Nothing: lo vedono tutti).</summary>
Friend Class EventoMotore
    Public ReadOnly Per As Integer?
    Public ReadOnly Testo As String
    ''' <summary>Il suono dell'evento, se il gioco lo sa dire da sé (la battaglia navale); altrimenti lo sceglie MotoreLocale dal testo.</summary>
    Public ReadOnly Suono As String

    Public Sub New(per As Integer?, testo As String, Optional suono As String = Nothing)
        Try
            Me.Per = per
            Me.Testo = testo
            Me.Suono = suono
        Catch ex As Exception
            RegistroErrori.Registra("EventoMotore.New", ex)
            Throw
        End Try
    End Sub
End Class

''' <summary>Una mossa possibile: l'id che si rimanda al motore e il testo che si legge.</summary>
Friend Class MossaMotore
    Public ReadOnly Id As String
    Public ReadOnly Testo As String
    Public Parametro As JObject = Nothing

    Public Sub New(id As String, testo As String)
        Try
            Me.Id = id
            Me.Testo = testo
        Catch ex As Exception
            RegistroErrori.Registra("MossaMotore.New", ex)
            Throw
        End Try
    End Sub
End Class

''' <summary>Quello che ogni gioco sa fare. Uno stato per partita; tutto in memoria.</summary>
Friend Interface IMotoreGioco
    ReadOnly Property Turno As Integer
    ReadOnly Property Finita As Boolean
    Function Inizio() As List(Of EventoMotore)
    Function Mosse(posto As Integer) As List(Of MossaMotore)
    ''' <summary>Fa la mossa. Con verifica la controlla prima; se non è ammessa solleva MossaNonConsentita.</summary>
    Function Applica(posto As Integer, idMossa As String, verifica As Boolean) As List(Of EventoMotore)
    Function Vista(posto As Integer) As JObject
    Function Situazione(posto As Integer) As String
    Function Bot(posto As Integer) As String
    ''' <summary>Chi deve agire adesso (di solito chi ha il turno; nel monopoli anche chi risponde a uno scambio o fa un'offerta all'asta).</summary>
    ReadOnly Property Attore As Integer
    ''' <summary>Le opzioni fuori dalle mosse del turno (solo monopoli: ipoteche, case, scambi); per gli altri giochi vuoto.</summary>
    Function Opzioni(posto As Integer) As JArray
    ''' <summary>Dice al motore quali posti sono del computer (serve al monopoli, per gli scambi).</summary>
    Sub ImpostaComputer(computer As Boolean())
End Interface

Friend Class MossaNonConsentita
    Inherits Exception
    Public Sub New()
        MyBase.New("Mossa non consentita.")
    End Sub
End Class

''' <summary>Piccoli attrezzi comuni ai motori.</summary>
Friend NotInheritable Class Attrezzi
    Private Sub New()
    End Sub

    Public Shared ReadOnly Caso As New Random()
    Private Shared ReadOnly _bloccoCaso As New Object()

    Public Shared Function Casuale(minimo As Integer, massimoEscluso As Integer) As Integer
        Try
            SyncLock _bloccoCaso
                Return Caso.Next(minimo, massimoEscluso)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("Attrezzi.Casuale", ex)
            Throw
        End Try
    End Function

    Public Shared Function Frazione() As Double
        Try
            SyncLock _bloccoCaso
                Return Caso.NextDouble()
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("Attrezzi.Frazione", ex)
            Throw
        End Try
    End Function

    Public Shared Sub Mescola(Of T)(l As List(Of T))
        Try
            SyncLock _bloccoCaso
                For i As Integer = l.Count - 1 To 1 Step -1
                    Dim j As Integer = Caso.Next(0, i + 1)
                    Dim tmp As T = l(i)
                    l(i) = l(j)
                    l(j) = tmp
                Next
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("Attrezzi.Mescola", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un oggetto JSON da coppie nome, valore.</summary>
    Public Shared Function J(ParamArray coppie As Object()) As JObject
        Try
            Dim o As New JObject()
            For i As Integer = 0 To coppie.Length - 2 Step 2
                Dim v As Object = coppie(i + 1)
                o(Convert.ToString(coppie(i))) = If(v Is Nothing, JValue.CreateNull(), If(TypeOf v Is JToken, CType(v, JToken), JToken.FromObject(v)))
            Next
            Return o
        Catch ex As Exception
            RegistroErrori.Registra("Attrezzi.J", ex)
            Throw
        End Try
    End Function

    Public Shared Function Arr(Of T)(elementi As IEnumerable(Of T)) As JArray
        Try
            Dim a As New JArray()
            For Each e As T In elementi
                a.Add(If(e Is Nothing, JValue.CreateNull(), If(TypeOf e Is JToken, CType(CObj(e), JToken), JToken.FromObject(e))))
            Next
            Return a
        Catch ex As Exception
            RegistroErrori.Registra("Attrezzi.Arr", ex)
            Throw
        End Try
    End Function

    Public Shared Function Unisci(separatore As String, elementi As IEnumerable(Of String)) As String
        Try
            Return String.Join(separatore, elementi)
        Catch ex As Exception
            RegistroErrori.Registra("Attrezzi.Unisci", ex)
            Throw
        End Try
    End Function

    Public Shared Function Plurale(n As Integer, singolare As String, plur As String) As String
        Try
            Return If(n = 1, singolare, plur)
        Catch ex As Exception
            RegistroErrori.Registra("Attrezzi.Plurale", ex)
            Throw
        End Try
    End Function
End Class

''' <summary>Il mazzo italiano da 40 carte, in comune tra briscola e scopa. Carta = numero da 0 a 39: seme = id \ 10, posizione = id Mod 10.</summary>
Friend NotInheritable Class MazzoItaliano
    Private Sub New()
    End Sub

    Public Shared ReadOnly SEMI As String() = {"coppe", "denari", "bastoni", "spade"}
    Private Shared ReadOnly NOMI As String() = {"Asso", "Due", "Tre", "Quattro", "Cinque", "Sei", "Sette", "Fante", "Cavallo", "Re"}
    Private Shared ReadOnly SIGLE As String() = {"A", "2", "3", "4", "5", "6", "7", "F", "C", "R"}
    Private Shared ReadOnly RANGO_IMMAGINE As Integer() = {1, 2, 3, 4, 5, 6, 7, 11, 12, 13}

    Public Shared Function Seme(id As Integer) As Integer
        Try
            Return id \ 10
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Seme", ex)
            Throw
        End Try
    End Function

    Public Shared Function Posizione(id As Integer) As Integer
        Try
            Return id Mod 10
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Posizione", ex)
            Throw
        End Try
    End Function

    ''' <summary>Valore della carta nella scopa: 1 l'asso ... 7 il sette, 8 il fante, 9 il cavallo, 10 il re.</summary>
    Public Shared Function Numero(id As Integer) As Integer
        Try
            Return (id Mod 10) + 1
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Numero", ex)
            Throw
        End Try
    End Function

    Public Shared Function Nome(id As Integer) As String
        Try
            Return NOMI(id Mod 10) & " di " & SEMI(id \ 10)
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Nome", ex)
            Throw
        End Try
    End Function

    Public Shared Function Breve(id As Integer) As String
        Try
            Return SIGLE(id Mod 10) & " " & SEMI(id \ 10).Substring(0, 3)
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Breve", ex)
            Throw
        End Try
    End Function

    Public Shared Function Sigla(id As Integer) As String
        Try
            Return SIGLE(id Mod 10)
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Sigla", ex)
            Throw
        End Try
    End Function

    Public Shared Function SemeNome(id As Integer) As String
        Try
            Return SEMI(id \ 10)
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.SemeNome", ex)
            Throw
        End Try
    End Function

    Public Shared Function Immagine(id As Integer, Optional speciale As String = Nothing) As String
        Try
            If Not String.IsNullOrEmpty(speciale) Then Return Sigla(id) & "_" & SemeNome(id) & "_" & speciale
            Return Sigla(id) & "_" & SemeNome(id)
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Immagine", ex)
            Throw
        End Try
    End Function

    Public Shared Function Carta(id As Integer, Optional speciale As String = Nothing) As JObject
        Try
            Return Attrezzi.J("id", id, "nome", Nome(id), "seme", Seme(id), "seme_nome", SemeNome(id), "rango", RANGO_IMMAGINE(id Mod 10),
                              "sigla", Sigla(id), "immagine", Immagine(id, speciale), "jolly", False, "breve", Breve(id))
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Carta", ex)
            Throw
        End Try
    End Function

    Public Shared Function Ordina(carte As IEnumerable(Of Integer)) As List(Of Integer)
        Try
            Dim l As New List(Of Integer)(carte)
            l.Sort()
            Return l
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Ordina", ex)
            Throw
        End Try
    End Function

    Public Shared Function Elenca(ids As IEnumerable(Of Integer)) As String
        Try
            Return String.Join(", ", Ordina(ids).Select(Function(i) Nome(i)))
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Elenca", ex)
            Throw
        End Try
    End Function

    Public Shared Function Mescolato() As List(Of Integer)
        Try
            Dim m As New List(Of Integer)(Enumerable.Range(0, 40))
            Attrezzi.Mescola(m)
            Return m
        Catch ex As Exception
            RegistroErrori.Registra("MazzoItaliano.Mescolato", ex)
            Throw
        End Try
    End Function
End Class
