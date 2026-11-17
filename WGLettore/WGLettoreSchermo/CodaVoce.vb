Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Threading

' Coda e gestione della sintesi vocale con priorità, storico delle frasi, pause e formattazione del testo letto.
Friend Enum PrioritaAnnuncio
    Bassa = 0
    Normale = 1
    Interruzione = 2
    Allarme = 3
End Enum

Friend NotInheritable Class CodaVoce

    Private ReadOnly log As New Logger()
    Private ReadOnly _blocco As New Object()
    Private ReadOnly _attesa As New List(Of RichiestaVoce)()
    Private _thread As Thread
    Private _attivo As Integer
    Private _pausa As Integer
    Private _ultimoTesto As String = ""
    Private _pausaUtente As Integer
    Private _testoSospeso As String = ""
    Private _ultimoTick As Integer
    Private _finestraFrequenza As New Queue(Of Integer)()
    Private _ultimaLingua As String = ""
    Private _impostazioni As ImpostazioniLettore
    Private _filtroSilenzio As Func(Of Boolean)
    Private ReadOnly _fineVoce As New ManualResetEventSlim(True)
    Private ReadOnly _nuovaVoce As New AutoResetEvent(False)
    Private _inEmissione As Integer
    Private _tickEmissione As Integer
    Private _lunghezzaEmissione As Integer
    Private _eventiVoceCollegati As Boolean
    Private ReadOnly _storico As New List(Of String)()
    Private _storicoIndice As Integer
    Private _senzaStorico As Integer

    Private NotInheritable Class RichiestaVoce
        Public Testo As String
        Public Priorita As PrioritaAnnuncio
        Public DaUtente As Boolean
    End Class

    Public Sub New(impostazioni As ImpostazioniLettore)
        _impostazioni = impostazioni
    End Sub

    Public Property FiltroSilenzio As Func(Of Boolean)
        Get
            Return _filtroSilenzio
        End Get
        Set(value As Func(Of Boolean))
            _filtroSilenzio = value
        End Set
    End Property

    Private Function Silenziato() As Boolean
        Try
            Dim filtro As Func(Of Boolean) = _filtroSilenzio
            Return filtro IsNot Nothing AndAlso filtro.Invoke()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.Silenziato", exAtteso)
            Return False
        End Try
    End Function

    Public Sub Avvia()
        Try
            If Interlocked.CompareExchange(_attivo, 1, 0) <> 0 Then Return
            _thread = New Thread(AddressOf Ciclo)
            _thread.IsBackground = True
            _thread.Name = "WGLettoreSchermoVoce"
            _thread.Start()
            CollegaEventiVoce()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.Avvia: " & ex.Message)
        End Try
    End Sub

    Public Sub Ferma()
        Try
            Interlocked.Exchange(_attivo, 0)
            _nuovaVoce.Set()
            If _thread IsNot Nothing Then
                _thread.Join(800)
                _thread = Nothing
            End If
            SyncLock _blocco
                _attesa.Clear()
            End SyncLock
            ScollegaEventiVoce()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.Ferma: " & ex.Message)
        End Try
    End Sub

    Private Sub CollegaEventiVoce()
        Try
            If Volatile.Read(_attivo) = 0 Then Return
            If _eventiVoceCollegati Then Return
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura Is Nothing Then Return
            AddHandler lettura.OnFineLettura, AddressOf AlFineLetturaVoce
            AddHandler lettura.OnStatoParlatoCambiato, AddressOf AlCambioStatoVoce
            _eventiVoceCollegati = True
        Catch ex As Exception
            log.ScriviLog("CodaVoce.CollegaEventiVoce: " & ex.Message)
        End Try
    End Sub

    Private Sub ScollegaEventiVoce()
        Try
            If Not _eventiVoceCollegati Then Return
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura IsNot Nothing Then
                RemoveHandler lettura.OnFineLettura, AddressOf AlFineLetturaVoce
                RemoveHandler lettura.OnStatoParlatoCambiato, AddressOf AlCambioStatoVoce
            End If
            _eventiVoceCollegati = False
            SegnaFineVoce()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.ScollegaEventiVoce: " & ex.Message)
        End Try
    End Sub

    Private Sub AlFineLetturaVoce()
        SegnaFineVoce()
    End Sub

    Private Sub AlCambioStatoVoce(staParlando As Boolean)
        If Not staParlando Then SegnaFineVoce()
    End Sub

    Private Sub SegnaFineVoce()
        Volatile.Write(_inEmissione, 0)
        _fineVoce.[Set]()
    End Sub

    Private Function AttendiFineVoce() As Boolean
        Try
            If Volatile.Read(_inEmissione) = 0 AndAlso Not StaParlando Then Return True
            Dim scadenza As Integer = Environment.TickCount + Math.Max(2000, _lunghezzaEmissione * 220)
            While Environment.TickCount < scadenza
                If _fineVoce.Wait(25) Then _fineVoce.Reset()
                If Volatile.Read(_inEmissione) = 0 Then Return True
                If Not StaParlando Then
                    If Environment.TickCount - _tickEmissione > 250 Then SegnaFineVoce()
                    Return True
                End If
            End While
            Return Not StaParlando
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.AttendiFineVoce", exAtteso)
            Return True
        End Try
    End Function

    Public Property InPausa As Boolean
        Get
            Return Volatile.Read(_pausa) = 1
        End Get
        Set(value As Boolean)
            Interlocked.Exchange(_pausa, If(value, 1, 0))
            _nuovaVoce.Set()
            If value Then FermaSintesi()
        End Set
    End Property

    Public Function AlternaPausaUtente() As Boolean
        Try
            If Interlocked.Exchange(_pausaUtente, 0) = 1 Then
                Interlocked.Exchange(_pausa, 0)
                _nuovaVoce.Set()
                Dim testo As String
                SyncLock _blocco
                    testo = _testoSospeso
                    _testoSospeso = ""
                End SyncLock
                If testo <> "" Then Emetti(testo, PrioritaAnnuncio.Normale)
                Return False
            End If
            If Volatile.Read(_inEmissione) = 0 AndAlso Not StaParlando Then Return False
            SyncLock _blocco
                _testoSospeso = _ultimoTesto
            End SyncLock
            Interlocked.Exchange(_pausaUtente, 1)
            InPausa = True
            Return True
        Catch ex As Exception
            log.ScriviLog("CodaVoce.AlternaPausaUtente: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub SaltaFrase()
        Try
            FermaSintesi()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.SaltaFrase: " & ex.Message)
        End Try
    End Sub

    Public Sub TogliPausaUtente()
        Try
            If Interlocked.Exchange(_pausaUtente, 0) = 0 Then Return
            Interlocked.Exchange(_pausa, 0)
            _nuovaVoce.Set()
            SyncLock _blocco
                _testoSospeso = ""
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CodaVoce.TogliPausaUtente: " & ex.Message)
        End Try
    End Sub

    Public Sub SvuotaAttesa()
        Try
            SyncLock _blocco
                _attesa.Clear()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("CodaVoce.SvuotaAttesa: " & ex.Message)
        End Try
    End Sub

    Public Sub FermaSeNonSuperata(tickRichiesta As Integer)
        Try
            If Volatile.Read(_inEmissione) = 1 AndAlso _tickEmissione - tickRichiesta > 0 Then Return
            FermaSintesi()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.FermaSeNonSuperata: " & ex.Message)
        End Try
    End Sub

    Public Sub Annulla()
        Try
            SyncLock _blocco
                _attesa.Clear()
            End SyncLock
            FermaSintesi()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.Annulla: " & ex.Message)
        End Try
    End Sub

    Public Property SenzaStorico As Boolean
        Get
            Return Volatile.Read(_senzaStorico) = 1
        End Get
        Set(value As Boolean)
            Volatile.Write(_senzaStorico, If(value, 1, 0))
        End Set
    End Property

    Private Sub RegistraInStorico(testo As String)
        Try
            If SenzaStorico Then Return
            Dim pulito As String = testo.Trim()
            If pulito.Length < 2 Then Return
            SyncLock _storico
                If _storico.Count > 0 AndAlso String.Equals(_storico(_storico.Count - 1), pulito, StringComparison.CurrentCulture) Then Return
                _storico.Add(pulito)
                If _storico.Count > 80 Then _storico.RemoveAt(0)
                _storicoIndice = 0
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("CodaVoce.RegistraInStorico", ex)
        End Try
    End Sub

    ' Senza "indietro": l'ultima frase detta; con "indietro": quella prima, e a ogni richiesta successiva una ancora più indietro.
    Public Function FraseDelloStorico(indietro As Boolean) As String
        Try
            SyncLock _storico
                If _storico.Count = 0 Then Return ""
                If indietro Then
                    If _storicoIndice + 1 >= _storico.Count Then Return ""
                    _storicoIndice += 1
                Else
                    _storicoIndice = 0
                End If
                Return _storico(_storico.Count - 1 - _storicoIndice)
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Scrivi("CodaVoce.FraseDelloStorico", ex)
            Return ""
        End Try
    End Function

    ' dalProgramma: la frase la dice il programma che ospita il lettore (il suo menu), mentre il lettore tace: il filtro del silenzio non vale.
    Public Sub Accoda(testo As String, priorita As PrioritaAnnuncio, interrompi As Boolean, daUtente As Boolean, Optional dalProgramma As Boolean = False)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If Not dalProgramma AndAlso Silenziato() Then Return
            RegistraInStorico(testo)
            If Volatile.Read(_pausa) = 1 AndAlso Not daUtente AndAlso priorita < PrioritaAnnuncio.Allarme Then Return
            If Not daUtente AndAlso Not AccettaDuplicato(testo) Then Return
            If Not daUtente AndAlso priorita < PrioritaAnnuncio.Interruzione AndAlso Not AccettaFrequenza() Then Return
            If interrompi OrElse priorita >= PrioritaAnnuncio.Interruzione Then
                SvuotaSotto(priorita)
                Emetti(testo, priorita, dalProgramma)
                Return
            End If
            Dim richiesta As New RichiestaVoce()
            richiesta.Testo = testo
            richiesta.Priorita = priorita
            richiesta.DaUtente = daUtente
            SyncLock _blocco
                For indice As Integer = _attesa.Count - 1 To 0 Step -1
                    If String.Equals(_attesa(indice).Testo, testo, StringComparison.CurrentCulture) Then Return
                Next
                _attesa.Add(richiesta)
                If _attesa.Count > 12 Then
                    Dim tolti As Integer = 0
                    For indice As Integer = _attesa.Count - 1 To 0 Step -1
                        If _attesa(indice).Priorita = PrioritaAnnuncio.Bassa Then
                            _attesa.RemoveAt(indice)
                            tolti += 1
                            If tolti >= 4 Then Exit For
                        End If
                    Next
                End If
            End SyncLock
            _nuovaVoce.Set()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.Accoda: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Vero se ci sono frasi in attesa di essere dette.</summary>
    Public ReadOnly Property HaFrasiInAttesa As Boolean
        Get
            SyncLock _blocco
                Return _attesa.Count > 0
            End SyncLock
        End Get
    End Property

    Public ReadOnly Property StaParlando As Boolean
        Get
            Try
                Dim lettura As IVoceLettore = VoceLettore.Corrente()
                If lettura Is Nothing Then Return False
                Return lettura.StaParlando
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("CodaVoce.StaParlando", exAtteso)
                Return False
            End Try
        End Get
    End Property

    Private Sub Ciclo()
        While Volatile.Read(_attivo) = 1
            Try
                If Volatile.Read(_pausa) = 1 Then
                    _nuovaVoce.WaitOne(250)
                    Continue While
                End If
                Dim richiesta As RichiestaVoce = Estrai()
                If richiesta Is Nothing Then
                    _nuovaVoce.WaitOne(250)
                    Continue While
                End If
                If Silenziato() Then Continue While
                CollegaEventiVoce()
                If Not AttendiFineVoce() Then
                    SyncLock _blocco
                        _attesa.Insert(0, richiesta)
                    End SyncLock
                    Thread.Sleep(20)
                    Continue While
                End If
                Emetti(richiesta.Testo, richiesta.Priorita)
            Catch ex As Exception
                log.ScriviLog("CodaVoce.Ciclo: " & ex.Message)
                Thread.Sleep(40)
            End Try
        End While
    End Sub

    Private Function Estrai() As RichiestaVoce
        SyncLock _blocco
            If _attesa.Count = 0 Then Return Nothing
            Dim migliore As Integer = 0
            For indice As Integer = 1 To _attesa.Count - 1
                If _attesa(indice).Priorita > _attesa(migliore).Priorita Then migliore = indice
            Next
            Dim scelta As RichiestaVoce = _attesa(migliore)
            _attesa.RemoveAt(migliore)
            Return scelta
        End SyncLock
    End Function

    Private Sub SvuotaSotto(priorita As PrioritaAnnuncio)
        SyncLock _blocco
            For indice As Integer = _attesa.Count - 1 To 0 Step -1
                If _attesa(indice).Priorita < priorita Then _attesa.RemoveAt(indice)
            Next
        End SyncLock
    End Sub

    Private Function AccettaDuplicato(testo As String) As Boolean
        Dim ora As Integer = Environment.TickCount
        SyncLock _blocco
            If String.Equals(_ultimoTesto, testo, StringComparison.CurrentCulture) AndAlso ora - _ultimoTick < 400 Then Return False
            Return True
        End SyncLock
    End Function

    Private Function AccettaFrequenza() As Boolean
        Dim ora As Integer = Environment.TickCount
        Dim limite As Integer = 8
        If _impostazioni IsNot Nothing AndAlso _impostazioni.Prolissita <= 0 Then limite = 4
        SyncLock _blocco
            While _finestraFrequenza.Count > 0 AndAlso ora - _finestraFrequenza.Peek() > 1000
                _finestraFrequenza.Dequeue()
            End While
            If _finestraFrequenza.Count >= limite Then Return False
            _finestraFrequenza.Enqueue(ora)
            Return True
        End SyncLock
    End Function

    Private Sub Emetti(testo As String, priorita As PrioritaAnnuncio, Optional dalProgramma As Boolean = False)
        Try
            If Not dalProgramma AndAlso Silenziato() Then Return
            Dim preparato As String = Prepara(testo)
            If preparato = "" Then Return
            SyncLock _blocco
                _ultimoTesto = testo
                _ultimoTick = Environment.TickCount
            End SyncLock
            If priorita >= PrioritaAnnuncio.Interruzione Then FermaSintesi()
            SuonaPriorita(priorita)
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura Is Nothing Then Return
            CollegaEventiVoce()
            Dim escludi As Boolean = _impostazioni IsNot Nothing AndAlso _impostazioni.LivelloPunteggiatura >= 0
            Dim nomePriorita As String = "Normale"
            If priorita >= PrioritaAnnuncio.Interruzione Then nomePriorita = "Alta"
            _fineVoce.Reset()
            Volatile.Write(_inEmissione, 1)
            _tickEmissione = Environment.TickCount
            _lunghezzaEmissione = preparato.Length
            lettura.LeggiTesto(preparato, priorita:=nomePriorita, escludiPunteggiatura:=escludi)
        Catch ex As Exception
            log.ScriviLog("CodaVoce.Emetti: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaSintesi()
        Try
            Dim lettura As IVoceLettore = VoceLettore.Corrente()
            If lettura IsNot Nothing AndAlso lettura.StaParlando Then lettura.Ferma()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.FermaSintesi: " & ex.Message)
        End Try
    End Sub

    Private Sub SuonaPriorita(priorita As PrioritaAnnuncio)
        Try
            If Silenziato() Then Return
            If _impostazioni Is Nothing OrElse Not _impostazioni.Suoni Then Return
            If priorita < PrioritaAnnuncio.Allarme Then Return
            SuoniLettore.Avviso()
        Catch ex As Exception
            log.ScriviLog("CodaVoce.SuonaPriorita: " & ex.Message)
        End Try
    End Sub

    Public Sub SuonaLimite()
        Try
            If Silenziato() Then Return
            If _impostazioni Is Nothing OrElse Not _impostazioni.Suoni Then Return
            SuoniLettore.LimiteRaggiunto()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.SuonaLimite", exAtteso)
        End Try
    End Sub

    Public Sub SuonaCampo(entrata As Boolean)
        Try
            If Silenziato() Then Return
            If _impostazioni Is Nothing OrElse Not _impostazioni.Suoni Then Return
            If entrata Then
                SuoniLettore.ModalitaFocus()
            Else
                SuoniLettore.ModalitaNavigazione()
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.SuonaCampo", exAtteso)
        End Try
    End Sub

    Public Function Prepara(testo As String) As String
        Try
            If testo Is Nothing Then Return ""
            Dim esito As String = testo.Trim()
            If esito = "" Then Return ""
            esito = LetturaEmoji.Converti(esito)
            esito = SeparaParoleComposte(esito)
            esito = ApplicaMaiuscole(esito)
            esito = ApplicaNumeri(esito)
            esito = ApplicaPunteggiatura(esito)
            esito = ApplicaLingua(esito)
            Return esito
        Catch ex As Exception
            log.ScriviLog("CodaVoce.Prepara: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

    ' Come nella sintesi di WinGabriel: "WGLettore" si legge "WG Lettore", "NomeFile2" "Nome File 2"; le parole intere e le sigle restano com'sono.
    Private Function SeparaParoleComposte(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.Length < 3 Then Return If(testo, "")
            Dim risultato As New StringBuilder(testo.Length + 16)
            risultato.Append(testo(0))
            For indice As Integer = 1 To testo.Length - 1
                If ConfineParolaComposta(testo, indice) Then risultato.Append(" "c)
                risultato.Append(testo(indice))
            Next
            Return risultato.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.SeparaParoleComposte", exAtteso)
            Return If(testo, "")
        End Try
    End Function

    Private Shared Function ConfineParolaComposta(testo As String, indice As Integer) As Boolean
        Dim precedente As Char = testo(indice - 1)
        Dim corrente As Char = testo(indice)
        If Char.IsLower(precedente) AndAlso Char.IsUpper(corrente) Then Return True
        If Char.IsUpper(precedente) AndAlso Char.IsUpper(corrente) Then
            If indice + 2 < testo.Length AndAlso Char.IsLower(testo(indice + 1)) AndAlso Char.IsLower(testo(indice + 2)) Then Return True
            Return False
        End If
        If Char.IsLetter(precedente) AndAlso Char.IsDigit(corrente) Then Return ParolaMista(testo, indice)
        If Char.IsDigit(precedente) AndAlso Char.IsUpper(corrente) Then Return ParolaMista(testo, indice)
        Return False
    End Function

    Private Shared Function ParolaMista(testo As String, indice As Integer) As Boolean
        Dim inizio As Integer = indice
        While inizio > 0 AndAlso Char.IsLetterOrDigit(testo(inizio - 1))
            inizio -= 1
        End While
        Dim fine As Integer = indice
        While fine < testo.Length AndAlso Char.IsLetterOrDigit(testo(fine))
            fine += 1
        End While
        Dim lettere As Integer = 0
        For posizione As Integer = inizio To fine - 1
            If Char.IsLetter(testo(posizione)) Then lettere += 1
        Next
        Return lettere >= 2
    End Function

    Private Function ApplicaMaiuscole(testo As String) As String
        Try
            Dim modo As String = If(_impostazioni Is Nothing, "parla", _impostazioni.Maiuscole)
            If modo = "nessuna" OrElse testo = "" Then Return testo
            If testo.Length = 1 AndAlso Char.IsLetter(testo(0)) AndAlso Char.IsUpper(testo(0)) Then
                If modo = "beep" Then SuonaMaiuscola()
                If modo = "beep" Then Return testo
                ' Come in WinGabriel: la lettera com'è, poi "maiuscola" ("A maiuscola").
                Return testo & " maiuscola"
            End If
            If modo <> "tutto" Then Return testo
            Dim sb As New StringBuilder(testo.Length + 16)
            Dim indice As Integer = 0
            While indice < testo.Length
                If Char.IsLetter(testo(indice)) AndAlso Char.IsUpper(testo(indice)) Then
                    Dim fine As Integer = indice + 1
                    While fine < testo.Length AndAlso Char.IsLetter(testo(fine))
                        fine += 1
                    End While
                    Dim parola As String = testo.Substring(indice, fine - indice)
                    Dim inizioParola As Boolean = indice = 0 OrElse Not Char.IsLetter(testo(indice - 1))
                    If parola.Length = 1 AndAlso inizioParola Then
                        sb.Append(parola).Append(" maiuscola")
                    Else
                        If inizioParola Then sb.Append("maiuscola ")
                        sb.Append(parola)
                    End If
                    indice = fine
                Else
                    sb.Append(testo(indice))
                    indice += 1
                End If
            End While
            Return sb.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.ApplicaMaiuscole", exAtteso)
            Return testo
        End Try
    End Function

    Private Sub SuonaMaiuscola()
        Try
            If _impostazioni Is Nothing OrElse Not _impostazioni.Suoni Then Return
            SuoniLettore.TastoEsplorato()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.SuonaMaiuscola", exAtteso)
        End Try
    End Sub

    Private Function ApplicaNumeri(testo As String) As String
        Try
            Dim modo As String = If(_impostazioni Is Nothing, "eredita", _impostazioni.Numeri)
            If modo = "eredita" OrElse modo = "intero" OrElse testo = "" Then Return testo
            Dim sb As New StringBuilder(testo.Length + 8)
            Dim indice As Integer = 0
            While indice < testo.Length
                If Char.IsDigit(testo(indice)) Then
                    Dim inizio As Integer = indice
                    While indice < testo.Length AndAlso Char.IsDigit(testo(indice))
                        indice += 1
                    End While
                    Dim numero As String = testo.Substring(inizio, indice - inizio)
                    If numero.Length = 1 Then
                        sb.Append(numero)
                    ElseIf modo = "cifra" Then
                        For Each cifra As Char In numero
                            sb.Append(cifra).Append(" "c)
                        Next
                    Else
                        Dim passo As Integer = If(modo = "triplette", 3, 2)
                        Dim resto As Integer = numero.Length Mod passo
                        Dim cursore As Integer = 0
                        If resto > 0 Then
                            sb.Append(numero, 0, resto).Append(" "c)
                            cursore = resto
                        End If
                        While cursore < numero.Length
                            sb.Append(numero, cursore, passo).Append(" "c)
                            cursore += passo
                        End While
                    End If
                Else
                    sb.Append(testo(indice))
                    indice += 1
                End If
            End While
            Return sb.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.ApplicaNumeri", exAtteso)
            Return testo
        End Try
    End Function

    Private Function ApplicaPunteggiatura(testo As String) As String
        Try
            Dim livello As Integer = If(_impostazioni Is Nothing, -1, _impostazioni.LivelloPunteggiatura)
            If livello < 0 OrElse testo = "" Then Return testo
            If livello = 0 Then Return testo
            Dim sb As New StringBuilder(testo.Length)
            For Each carattere As Char In testo
                Dim detto As String = NomeSimbolo(carattere)
                Dim peso As Integer = PesoSimbolo(carattere)
                If detto <> "" AndAlso peso <= livello Then
                    sb.Append(" ").Append(detto).Append(" ")
                Else
                    sb.Append(carattere)
                End If
            Next
            Return sb.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.ApplicaPunteggiatura", exAtteso)
            Return testo
        End Try
    End Function

    Private Function PesoSimbolo(carattere As Char) As Integer
        Select Case carattere
            Case "."c, ","c, "?"c, "!"c
                Return 1
            Case ";"c, ":"c, "("c, ")"c, "'"c, """"c, "-"c, "–"c, "—"c
                Return 2
            Case Else
                Return 3
        End Select
    End Function

    Private Function NomeSimbolo(carattere As Char) As String
        Select Case carattere
            Case "."c : Return "punto"
            Case ","c : Return "virgola"
            Case ";"c : Return "punto e virgola"
            Case ":"c : Return "due punti"
            Case "!"c : Return "punto esclamativo"
            Case "?"c : Return "punto interrogativo"
            Case "("c : Return "aperta parentesi"
            Case ")"c : Return "chiusa parentesi"
            Case "["c : Return "aperta quadra"
            Case "]"c : Return "chiusa quadra"
            Case "{"c : Return "aperta graffa"
            Case "}"c : Return "chiusa graffa"
            Case "'"c : Return "apostrofo"
            Case """"c : Return "virgolette"
            Case "-"c, "–"c, "—"c : Return "trattino"
            Case "/"c : Return "barra"
            Case "\"c : Return "barra rovesciata"
            Case "@"c : Return "chiocciola"
            Case "#"c : Return "cancelletto"
            Case "%"c : Return "percento"
            Case "&"c : Return "e commerciale"
            Case "*"c : Return "asterisco"
            Case "+"c : Return "più"
            Case "="c : Return "uguale"
            Case "<"c : Return "minore"
            Case ">"c : Return "maggiore"
            Case "_"c : Return "sottolineato"
            Case "€"c : Return "euro"
            Case "$"c : Return "dollaro"
            Case Else : Return ""
        End Select
    End Function

    Private Function ApplicaLingua(testo As String) As String
        Try
            Dim lingua As String = LinguaDelTesto(testo)
            If lingua = "" OrElse lingua = _ultimaLingua Then Return testo
            _ultimaLingua = lingua
            If lingua = "italiano" Then Return testo
            Return "in " & lingua & ", " & testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("CodaVoce.ApplicaLingua", exAtteso)
            Return testo
        End Try
    End Function

    Public Shared Function LinguaDelTesto(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        Dim cirillico As Integer = 0
        Dim arabo As Integer = 0
        Dim greco As Integer = 0
        Dim ebraico As Integer = 0
        Dim cjk As Integer = 0
        Dim kana As Integer = 0
        Dim latino As Integer = 0
        For Each carattere As Char In testo
            Dim codice As Integer = AscW(carattere)
            If codice >= &H400 AndAlso codice <= &H4FF Then cirillico += 1
            If codice >= &H600 AndAlso codice <= &H6FF Then arabo += 1
            If codice >= &H370 AndAlso codice <= &H3FF Then greco += 1
            If codice >= &H590 AndAlso codice <= &H5FF Then ebraico += 1
            If codice >= &H3040 AndAlso codice <= &H30FF Then kana += 1
            If codice >= &H4E00 AndAlso codice <= &H9FFF Then cjk += 1
            If (codice >= &H41 AndAlso codice <= &H24F) OrElse carattere = "à"c OrElse carattere = "è"c OrElse carattere = "é"c OrElse carattere = "ì"c OrElse carattere = "ò"c OrElse carattere = "ù"c Then latino += 1
        Next
        Dim massimo As Integer = Math.Max(latino, Math.Max(cirillico, Math.Max(arabo, Math.Max(greco, Math.Max(ebraico, Math.Max(cjk, kana))))))
        If massimo < 2 Then Return ""
        If massimo = kana Then Return "giapponese"
        If massimo = cjk Then Return "cinese"
        If massimo = cirillico Then Return "russo"
        If massimo = arabo Then Return "arabo"
        If massimo = greco Then Return "greco"
        If massimo = ebraico Then Return "ebraico"
        Return "italiano"
    End Function

End Class
