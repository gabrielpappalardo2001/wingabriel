Imports wgcontrols

' ============================================================================================
' RicercaViaggi - trova i viaggi da una stazione a un'altra con gli orari programmati
' ============================================================================================
'
' Come funziona
'   Per il giorno cercato (e quelli attorno, perché i treni notturni passano la mezzanotte) si
'   mettono in fila tutti i tratti tra due fermate consecutive, in ordine di partenza. Si
'   percorre la fila a strati: lo strato k dice a che ora si può essere in ogni stazione
'   usando al massimo k treni. Per cambiare treno servono almeno cinque minuti.
'   Per avere più soluzioni si ripete la ricerca partendo un minuto dopo la prima partenza
'   trovata; poi si scartano le soluzioni peggiori di un'altra in tutto.
' ============================================================================================

Friend Enum TipoTreni
    Tutti = 0
    AltaVelocita = 1
    Veloci = 2
    Regionali = 3
End Enum

''' <summary>Un pezzo di viaggio su un solo treno.</summary>
Friend Class TrattoViaggio
    Public Corsa As CorsaOrario
    Public Salita As Integer
    Public Discesa As Integer
    ''' <summary>Secondi, dalla mezzanotte del giorno cercato, a cui è partita la corsa (può essere negativo o oltre le 24 ore).</summary>
    Public Base As Integer

    Public ReadOnly Property Partenza As Integer
        Get
            Return Base + Corsa.Partenze(Salita)
        End Get
    End Property

    Public ReadOnly Property Arrivo As Integer
        Get
            Return Base + Corsa.Arrivi(Discesa)
        End Get
    End Property
End Class

''' <summary>Un viaggio completo: uno o più tratti con i cambi.</summary>
Friend Class SoluzioneViaggio
    Public ReadOnly Tratti As New List(Of TrattoViaggio)()

    Public ReadOnly Property Partenza As Integer
        Get
            Return Tratti(0).Partenza
        End Get
    End Property

    Public ReadOnly Property Arrivo As Integer
        Get
            Return Tratti(Tratti.Count - 1).Arrivo
        End Get
    End Property

    Public ReadOnly Property Cambi As Integer
        Get
            Return Tratti.Count - 1
        End Get
    End Property

    Public Function Uguale(altra As SoluzioneViaggio) As Boolean
        If altra.Tratti.Count <> Tratti.Count Then Return False
        For i As Integer = 0 To Tratti.Count - 1
            Dim a As TrattoViaggio = Tratti(i)
            Dim b As TrattoViaggio = altra.Tratti(i)
            If a.Corsa IsNot b.Corsa OrElse a.Base <> b.Base OrElse a.Salita <> b.Salita OrElse a.Discesa <> b.Discesa Then Return False
        Next
        Return True
    End Function
End Class

Friend NotInheritable Class RicercaViaggi

    Private Shared ReadOnly _log As New Logger()

    Private Const Infinito As Integer = Integer.MaxValue
    Private Const SecondiGiorno As Integer = 86400
    Private Const Trasbordo As Integer = 5 * 60

    Private ReadOnly _orari As OrariTreni

    Private _giornoPronto As Integer = Integer.MinValue
    Private _tipoPronto As TipoTreni = TipoTreni.Tutti
    Private _n As Integer
    Private _dep As Integer()
    Private _arr As Integer()
    Private _tDep As Integer()
    Private _tArr As Integer()
    Private _viaggio As Integer()
    Private _fermataDa As Integer()
    Private _viaggioCorsa As Integer()
    Private _viaggioBase As Integer()
    Private _nViaggi As Integer

    Public Sub New(orari As OrariTreni)
        _orari = orari
    End Sub

    Private Shared Function Ammessa(c As CorsaOrario, tipo As TipoTreni) As Boolean
        Select Case tipo
            Case TipoTreni.AltaVelocita : Return c.AltaVelocita
            Case TipoTreni.Veloci : Return c.AltaVelocita OrElse c.Veloce
            Case TipoTreni.Regionali : Return Not c.AltaVelocita AndAlso Not c.Veloce
        End Select
        Return True
    End Function

    ''' <summary>Prepara la fila dei tratti per il giorno dato, se non è già pronta.</summary>
    Private Sub Prepara(giorno As Integer, tipo As TipoTreni)
        Try
            If giorno = _giornoPronto AndAlso tipo = _tipoPronto AndAlso _dep IsNot Nothing Then Return
            Dim corse As List(Of CorsaOrario) = _orari.Corse

            ' Primo passaggio: si contano i tratti.
            Dim conta As Integer = 0
            Dim viaggi As Integer = 0
            For Each c As CorsaOrario In corse
                If Not Ammessa(c, tipo) Then Continue For
                Dim servizio As ServizioOrario = _orari.Servizi(c.Servizio)
                For o As Integer = -1 To 1
                    Dim g As Integer = giorno + o
                    If servizio.Attivo(g, (g + 3) Mod 7) Then
                        viaggi += 1
                        conta += c.Stazioni.Length - 1
                    End If
                Next
            Next

            Dim dep(conta - 1) As Integer
            Dim arr(conta - 1) As Integer
            Dim tDep(conta - 1) As Integer
            Dim tArr(conta - 1) As Integer
            Dim viaggio(conta - 1) As Integer
            Dim fermataDa(conta - 1) As Integer
            Dim viaggioCorsa(viaggi - 1) As Integer
            Dim viaggioBase(viaggi - 1) As Integer

            Dim k As Integer = 0
            Dim v As Integer = 0
            For ci As Integer = 0 To corse.Count - 1
                Dim c As CorsaOrario = corse(ci)
                If Not Ammessa(c, tipo) Then Continue For
                Dim servizio As ServizioOrario = _orari.Servizi(c.Servizio)
                For o As Integer = -1 To 1
                    Dim g As Integer = giorno + o
                    If Not servizio.Attivo(g, (g + 3) Mod 7) Then Continue For
                    Dim baseSec As Integer = o * SecondiGiorno
                    viaggioCorsa(v) = ci
                    viaggioBase(v) = baseSec
                    For i As Integer = 0 To c.Stazioni.Length - 2
                        Dim a As Integer = c.Stazioni(i)
                        Dim b As Integer = c.Stazioni(i + 1)
                        If a < 0 OrElse b < 0 OrElse a = b Then Continue For
                        Dim d As Integer = baseSec + c.Partenze(i)
                        Dim r As Integer = baseSec + c.Arrivi(i + 1)
                        If r < d Then Continue For
                        dep(k) = a
                        arr(k) = b
                        tDep(k) = d
                        tArr(k) = r
                        viaggio(k) = v
                        fermataDa(k) = i
                        k += 1
                    Next
                    v += 1
                Next
            Next

            ' Si mettono in ordine di partenza.
            Dim ordine(k - 1) As Integer
            For i As Integer = 0 To k - 1
                ordine(i) = i
            Next
            Dim chiavi(k - 1) As Integer
            Array.Copy(tDep, chiavi, k)
            Array.Sort(chiavi, ordine)

            _dep = New Integer(k - 1) {}
            _arr = New Integer(k - 1) {}
            _tDep = New Integer(k - 1) {}
            _tArr = New Integer(k - 1) {}
            _viaggio = New Integer(k - 1) {}
            _fermataDa = New Integer(k - 1) {}
            For i As Integer = 0 To k - 1
                Dim s As Integer = ordine(i)
                _dep(i) = dep(s)
                _arr(i) = arr(s)
                _tDep(i) = tDep(s)
                _tArr(i) = tArr(s)
                _viaggio(i) = viaggio(s)
                _fermataDa(i) = fermataDa(s)
            Next
            _viaggioCorsa = viaggioCorsa
            _viaggioBase = viaggioBase
            _nViaggi = v
            _n = k
            _giornoPronto = giorno
            _tipoPronto = tipo
        Catch ex As Exception
            _giornoPronto = Integer.MinValue
            _dep = Nothing
            _log.ScriviLog("RicercaViaggi.Prepara: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Sub

    ''' <summary>Primo tratto che parte alle ore date o dopo.</summary>
    Private Function PrimoDa(secondi As Integer) As Integer
        Dim basso As Integer = 0
        Dim alto As Integer = _n
        While basso < alto
            Dim medio As Integer = (basso + alto) \ 2
            If _tDep(medio) < secondi Then
                basso = medio + 1
            Else
                alto = medio
            End If
        End While
        Return basso
    End Function

    ''' <summary>Una ricerca a strati da un orario di partenza: le soluzioni migliori per ogni numero di treni.</summary>
    Private Function Passata(origine As Integer, destinazione As Integer, t0 As Integer, maxTreni As Integer) As List(Of SoluzioneViaggio)
        Dim risultato As New List(Of SoluzioneViaggio)()
        Dim nS As Integer = _orari.Stazioni.Count
        Dim arrivi(maxTreni)() As Integer
        Dim prevSalita(maxTreni)() As Integer
        Dim prevDiscesa(maxTreni)() As Integer
        arrivi(0) = New Integer(nS - 1) {}
        For i As Integer = 0 To nS - 1
            arrivi(0)(i) = Infinito
        Next
        arrivi(0)(origine) = t0
        Dim migliore As Integer = Infinito
        Dim inizio As Integer = PrimoDa(t0)
        Dim corse As List(Of CorsaOrario) = _orari.Corse

        For k As Integer = 1 To maxTreni
            arrivi(k) = CType(arrivi(k - 1).Clone(), Integer())
            prevSalita(k) = New Integer(nS - 1) {}
            prevDiscesa(k) = New Integer(nS - 1) {}
            For i As Integer = 0 To nS - 1
                prevSalita(k)(i) = -1
                prevDiscesa(k)(i) = -1
            Next
            Dim stato(_nViaggi - 1) As Integer
            For i As Integer = 0 To _nViaggi - 1
                stato(i) = -1
            Next
            Dim precedente As Integer() = arrivi(k - 1)
            Dim attuale As Integer() = arrivi(k)
            For idx As Integer = inizio To _n - 1
                Dim d As Integer = _tDep(idx)
                If migliore <> Infinito AndAlso d > migliore Then Exit For
                Dim v As Integer = _viaggio(idx)
                Dim corsa As CorsaOrario = corse(_viaggioCorsa(v))
                If stato(v) < 0 Then
                    Dim s As Integer = _dep(idx)
                    Dim a As Integer = precedente(s)
                    If a <> Infinito Then
                        Dim richiesto As Integer = If(k = 1, a, a + Trasbordo)
                        If d >= richiesto AndAlso corsa.Sale(_fermataDa(idx)) Then stato(v) = idx
                    End If
                End If
                If stato(v) >= 0 Then
                    Dim ia As Integer = _arr(idx)
                    Dim ta As Integer = _tArr(idx)
                    If ta < attuale(ia) AndAlso corsa.Scende(_fermataDa(idx) + 1) Then
                        attuale(ia) = ta
                        prevSalita(k)(ia) = stato(v)
                        prevDiscesa(k)(ia) = idx
                        If ia = destinazione AndAlso ta < migliore Then migliore = ta
                    End If
                End If
            Next
        Next

        ' Le soluzioni: ogni strato che migliora l'arrivo rispetto a quello con meno treni.
        Dim ultimo As Integer = Infinito
        For k As Integer = 1 To maxTreni
            Dim a As Integer = arrivi(k)(destinazione)
            If a = Infinito OrElse a >= ultimo Then Continue For
            ultimo = a
            Dim s As SoluzioneViaggio = Ricostruisci(arrivi, prevSalita, prevDiscesa, k, destinazione)
            If s IsNot Nothing AndAlso s.Tratti.Count > 0 Then risultato.Add(s)
        Next
        Return risultato
    End Function

    Private Function Ricostruisci(arrivi As Integer()(), prevSalita As Integer()(), prevDiscesa As Integer()(), strato As Integer, destinazione As Integer) As SoluzioneViaggio
        Dim sol As New SoluzioneViaggio()
        Dim s As Integer = destinazione
        Dim k As Integer = strato
        While k > 0
            While k > 0 AndAlso arrivi(k)(s) = arrivi(k - 1)(s)
                k -= 1
            End While
            If k = 0 Then Exit While
            Dim b As Integer = prevSalita(k)(s)
            Dim e As Integer = prevDiscesa(k)(s)
            If b < 0 OrElse e < 0 Then Return Nothing
            Dim v As Integer = _viaggio(b)
            Dim t As New TrattoViaggio()
            t.Corsa = _orari.Corse(_viaggioCorsa(v))
            t.Base = _viaggioBase(v)
            t.Salita = _fermataDa(b)
            t.Discesa = _fermataDa(e) + 1
            sol.Tratti.Insert(0, t)
            s = _dep(b)
            k -= 1
        End While
        Return sol
    End Function

    ''' <summary>
    ''' I viaggi da origine a destinazione che partono dall'istante dato o dopo, per tipo di treno, fino a un numero di cambi.
    ''' Sono in ordine di partenza.
    ''' </summary>
    Public Function Cerca(origine As Integer, destinazione As Integer, partenza As DateTime, tipo As TipoTreni, maxCambi As Integer, quanti As Integer) As List(Of SoluzioneViaggio)
        Try
            Dim giorno As Integer = OrariTreni.GiornoDa(partenza)
            Prepara(giorno, tipo)
            Dim t0 As Integer = CInt(partenza.TimeOfDay.TotalSeconds)
            Dim tutte As New List(Of SoluzioneViaggio)()
            Dim limite As Integer = 2 * SecondiGiorno
            For giro As Integer = 1 To 16
                Dim trovate As List(Of SoluzioneViaggio) = Passata(origine, destinazione, t0, maxCambi + 1)
                If trovate.Count = 0 Then Exit For
                Dim primaPartenza As Integer = Integer.MaxValue
                For Each s As SoluzioneViaggio In trovate
                    primaPartenza = Math.Min(primaPartenza, s.Partenza)
                    Dim nuova As Boolean = True
                    For Each g As SoluzioneViaggio In tutte
                        If g.Uguale(s) Then
                            nuova = False
                            Exit For
                        End If
                    Next
                    If nuova Then tutte.Add(s)
                Next
                t0 = primaPartenza + 60
                If t0 >= limite Then Exit For
                If Migliori(tutte).Count >= quanti Then Exit For
            Next
            Dim finali As List(Of SoluzioneViaggio) = Migliori(tutte)
            If finali.Count > quanti Then finali.RemoveRange(quanti, finali.Count - quanti)
            Return finali
        Catch ex As Exception
            _log.ScriviLog("RicercaViaggi.Cerca: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

    ''' <summary>Toglie le soluzioni che un'altra batte in tutto (parte dopo, arriva prima, meno cambi) e le mette in ordine di partenza.</summary>
    Private Shared Function Migliori(tutte As List(Of SoluzioneViaggio)) As List(Of SoluzioneViaggio)
        Dim risultato As New List(Of SoluzioneViaggio)()
        For Each a As SoluzioneViaggio In tutte
            Dim battuta As Boolean = False
            For Each b As SoluzioneViaggio In tutte
                If b Is a Then Continue For
                If b.Partenza >= a.Partenza AndAlso b.Arrivo <= a.Arrivo AndAlso b.Cambi <= a.Cambi Then
                    If b.Partenza > a.Partenza OrElse b.Arrivo < a.Arrivo OrElse b.Cambi < a.Cambi Then
                        battuta = True
                        Exit For
                    End If
                End If
            Next
            If Not battuta Then risultato.Add(a)
        Next
        risultato.Sort(Function(x, y)
                           Dim c As Integer = x.Partenza.CompareTo(y.Partenza)
                           If c <> 0 Then Return c
                           Return x.Arrivo.CompareTo(y.Arrivo)
                       End Function)
        Return risultato
    End Function

End Class
