Imports System.IO
Imports System.Media
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports Microsoft.Win32.SafeHandles
Imports wgcontrols

' ============================================================================================
' Sveglie - sveglie del menu Altre funzioni
' ============================================================================================
'
' Ogni sveglia ha un orario, un nome facoltativo e i giorni in cui suona (una volta sola, ogni giorno,
' giorni lavorativi, fine settimana o giorni a scelta). Le sveglie restano salvate nella
' configurazione e riprendono da sole all'avvio di WinGabriel.
'
' Un thread di sfondo controlla le sveglie ogni quindici secondi e si sveglia anche da un timer di
' Windows che può risvegliare il computer dalla sospensione, se le impostazioni di risparmio
' energetico lo permettono. Se il computer si risveglia entro mezz'ora dall'orario, la sveglia suona
' lo stesso; più tardi, si salta.
'
' Quando suona, un suono a toni ripetuti gira finché non si sceglie "Spegni" o "Rimanda di dieci
' minuti" (il suono si ferma comunque dopo cinque minuti).
' ============================================================================================

Friend Class DatiSveglia
    Public Id As String = ""
    Public Ora As TimeSpan
    ''' <summary>Giorni in cui suona: bit 0 lunedì ... bit 6 domenica. Zero vuol dire una volta sola.</summary>
    Public Giorni As Integer
    Public Attiva As Boolean = True
    Public Nome As String = ""
    Public Prossima As DateTime = DateTime.MaxValue

    ''' <summary>La prossima volta che suona dopo l'istante dato.</summary>
    Public Function CalcolaProssima(da As DateTime) As DateTime
        If Giorni = 0 Then
            Dim oggi As DateTime = da.Date + Ora
            Return If(oggi > da, oggi, oggi.AddDays(1))
        End If
        For d As Integer = 0 To 7
            Dim giorno As DateTime = da.Date.AddDays(d)
            Dim indice As Integer = (CInt(giorno.DayOfWeek) + 6) Mod 7
            If (Giorni And (1 << indice)) <> 0 AndAlso giorno + Ora > da Then Return giorno + Ora
        Next
        Return DateTime.MaxValue
    End Function

    Public Function DescriviGiorni() As String
        Select Case Giorni
            Case 0 : Return "una volta sola"
            Case 127 : Return "ogni giorno"
            Case 31 : Return "dal lunedì al venerdì"
            Case 96 : Return "sabato e domenica"
        End Select
        Dim nomi As String() = {"lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato", "domenica"}
        Dim elenco As New List(Of String)()
        For i As Integer = 0 To 6
            If (Giorni And (1 << i)) <> 0 Then elenco.Add(nomi(i))
        Next
        If elenco.Count = 1 Then Return "ogni " & elenco(0)
        Return "il " & String.Join(", ", elenco.GetRange(0, elenco.Count - 1)) & " e il " & elenco(elenco.Count - 1)
    End Function

    Public Function Descrizione() As String
        Dim sb As New StringBuilder(Ora.ToString("hh\:mm"))
        If Nome <> "" Then sb.Append(", " & Nome)
        sb.Append(", " & DescriviGiorni())
        sb.Append(If(Attiva, ", attiva", ", disattivata"))
        Return sb.ToString()
    End Function
End Class

Friend NotInheritable Class WGSveglie

    Private Const IdConfig As String = "Sveglie"
    Private Const MinutiTolleranza As Double = 30
    Private Const MinutiRinvio As Integer = 10

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared ReadOnly _sveglie As New List(Of DatiSveglia)()
    Private Shared ReadOnly _rinvii As New List(Of KeyValuePair(Of DateTime, DatiSveglia))()
    Private Shared ReadOnly _inAttesa As New Queue(Of DatiSveglia)()
    Private Shared _avviato As Boolean
    Private Shared _fermo As Boolean
    Private Shared _risveglio As ManualResetEvent
    Private Shared _timerWindows As IntPtr = IntPtr.Zero
    Private Shared _inCorso As Boolean
    Private Shared _suono As SoundPlayer
    Private Shared _fermaSuono As System.Threading.Timer

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CreateWaitableTimer(attributi As IntPtr, ripristinoManuale As Boolean, nome As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetWaitableTimer(timer As IntPtr, ByRef scadenza As Long, periodo As Integer, completamento As IntPtr, argomento As IntPtr, risveglia As Boolean) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CancelWaitableTimer(timer As IntPtr) As Boolean
    End Function

    Private Sub New()
    End Sub

#Region "Elenco e salvataggio"

    ''' <summary>Da chiamare una volta all'avvio di WinGabriel: carica le sveglie e avvia il controllo.</summary>
    Public Shared Sub Avvia()
        Try
            If _avviato Then Return
            _avviato = True
            Carica()
            Try
                _timerWindows = CreateWaitableTimer(IntPtr.Zero, True, Nothing)
                If _timerWindows <> IntPtr.Zero Then
                    _risveglio = New ManualResetEvent(False)
                    _risveglio.SafeWaitHandle = New SafeWaitHandle(_timerWindows, False)
                End If
            Catch ex As Exception
                _log.ScriviLog("WGSveglie.Avvia timer di Windows: " & ex.Message)
                _timerWindows = IntPtr.Zero
                _risveglio = Nothing
            End Try
            Dim t As New Thread(AddressOf Ciclo)
            t.IsBackground = True
            t.Name = "Sveglie"
            t.Start()
            Riarma()
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function Elenco() As List(Of DatiSveglia)
        SyncLock _blocco
            Return New List(Of DatiSveglia)(_sveglie)
        End SyncLock
    End Function

    Public Shared Sub Aggiungi(s As DatiSveglia)
        Try
            SyncLock _blocco
                s.Prossima = If(s.Attiva, s.CalcolaProssima(DateTime.Now), DateTime.MaxValue)
                _sveglie.Add(s)
            End SyncLock
            Salva()
            Riarma()
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Aggiungi: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub Elimina(id As String)
        Try
            SyncLock _blocco
                _sveglie.RemoveAll(Function(s) s.Id = id)
            End SyncLock
            Salva()
            Riarma()
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Elimina: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ImpostaAttiva(id As String, attiva As Boolean)
        Try
            SyncLock _blocco
                For Each s As DatiSveglia In _sveglie
                    If s.Id = id Then
                        s.Attiva = attiva
                        s.Prossima = If(attiva, s.CalcolaProssima(DateTime.Now), DateTime.MaxValue)
                    End If
                Next
            End SyncLock
            Salva()
            Riarma()
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.ImpostaAttiva: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Pulisci(testo As String) As String
        Return If(testo, "").Replace("|", " ").Replace("~", " ").Replace(vbCr, " ").Replace(vbLf, " ").Trim()
    End Function

    Private Shared Sub Salva()
        Try
            Dim righe As New List(Of String)()
            SyncLock _blocco
                For Each s As DatiSveglia In _sveglie
                    righe.Add(s.Id & "|" & s.Ora.ToString("hh\:mm") & "|" & s.Giorni & "|" & If(s.Attiva, "1", "0") & "|" & Pulisci(s.Nome))
                Next
            End SyncLock
            Dim config As New WGConfig()
            config.AggiungiValore(IdConfig, String.Join("~", righe))
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Salva: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Carica()
        Try
            Dim config As New WGConfig()
            Dim testo As String = config.LeggiValore(IdConfig)
            SyncLock _blocco
                _sveglie.Clear()
                If String.IsNullOrWhiteSpace(testo) Then Return
                For Each riga As String In testo.Split("~"c)
                    Dim p As String() = riga.Split("|"c)
                    If p.Length < 5 Then Continue For
                    Dim ora As TimeSpan
                    Dim giorni As Integer
                    If Not TimeSpan.TryParse(p(1), ora) OrElse Not Integer.TryParse(p(2), giorni) Then Continue For
                    Dim s As New DatiSveglia()
                    s.Id = p(0)
                    s.Ora = ora
                    s.Giorni = giorni
                    s.Attiva = p(3) = "1"
                    s.Nome = p(4)
                    s.Prossima = If(s.Attiva, s.CalcolaProssima(DateTime.Now), DateTime.MaxValue)
                    _sveglie.Add(s)
                Next
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Carica: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Controllo"

    ''' <summary>Mette il timer di Windows sulla prossima sveglia, in modo che risvegli il computer; senza sveglie lo ferma.</summary>
    Private Shared Sub Riarma()
        Try
            If _timerWindows = IntPtr.Zero Then Return
            Dim prossima As DateTime = DateTime.MaxValue
            SyncLock _blocco
                For Each s As DatiSveglia In _sveglie
                    If s.Attiva AndAlso s.Prossima < prossima Then prossima = s.Prossima
                Next
                For Each r As KeyValuePair(Of DateTime, DatiSveglia) In _rinvii
                    If r.Key < prossima Then prossima = r.Key
                Next
            End SyncLock
            If prossima = DateTime.MaxValue Then
                CancelWaitableTimer(_timerWindows)
            Else
                Dim scadenza As Long = Math.Max(prossima.ToFileTimeUtc(), DateTime.UtcNow.AddSeconds(1).ToFileTimeUtc())
                SetWaitableTimer(_timerWindows, scadenza, 0, IntPtr.Zero, IntPtr.Zero, True)
            End If
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Riarma: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Ciclo()
        While Not _fermo
            Try
                Dim scattato As Boolean = False
                If _risveglio IsNot Nothing Then
                    scattato = _risveglio.WaitOne(15000)
                Else
                    Thread.Sleep(15000)
                End If
                If _fermo Then Exit While
                Controlla()
                Riarma()
            Catch ex As Exception
                _log.ScriviLog("WGSveglie.Ciclo: " & ex.Message)
                Thread.Sleep(15000)
            End Try
        End While
    End Sub

    Private Shared Sub Controlla()
        Try
            Dim adesso As DateTime = DateTime.Now
            Dim daSuonare As New List(Of DatiSveglia)()
            Dim cambiato As Boolean = False
            SyncLock _blocco
                For Each s As DatiSveglia In _sveglie
                    If Not s.Attiva OrElse s.Prossima > adesso Then Continue For
                    If (adesso - s.Prossima).TotalMinutes <= MinutiTolleranza Then daSuonare.Add(s)
                    If s.Giorni = 0 Then
                        s.Attiva = False
                        s.Prossima = DateTime.MaxValue
                        cambiato = True
                    Else
                        s.Prossima = s.CalcolaProssima(adesso)
                    End If
                Next
                For i As Integer = _rinvii.Count - 1 To 0 Step -1
                    If _rinvii(i).Key <= adesso Then
                        daSuonare.Add(_rinvii(i).Value)
                        _rinvii.RemoveAt(i)
                    End If
                Next
            End SyncLock
            If cambiato Then Salva()
            For Each s As DatiSveglia In daSuonare
                Dim copia As DatiSveglia = s
                SuInterfaccia(Sub() Suona(copia))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Controlla: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Suono e menu"

    Private Shared Sub SuInterfaccia(azione As Action)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.IsHandleCreated Then
                c.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.SuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CreaSuono() As Byte()
        Const frequenzaCampionamento As Integer = 22050
        Dim campioni As New List(Of Short)()
        Dim Tono As Action(Of Double, Double) =
            Sub(frequenza As Double, durata As Double)
                Dim n As Integer = CInt(frequenzaCampionamento * durata)
                Dim rampa As Double = frequenzaCampionamento * 0.01
                For i As Integer = 0 To n - 1
                    Dim inviluppo As Double = Math.Min(1.0, Math.Min(i, n - 1 - i) / rampa)
                    campioni.Add(CShort(Math.Sin(2 * Math.PI * frequenza * i / frequenzaCampionamento) * 20000 * inviluppo))
                Next
            End Sub
        Dim Silenzio As Action(Of Double) =
            Sub(durata As Double)
                For i As Integer = 0 To CInt(frequenzaCampionamento * durata) - 1
                    campioni.Add(0)
                Next
            End Sub
        For b As Integer = 1 To 3
            Tono(1320, 0.12)
            Silenzio(0.08)
        Next
        Silenzio(0.9)
        Using ms As New MemoryStream()
            Using w As New BinaryWriter(ms)
                Dim byteDati As Integer = campioni.Count * 2
                w.Write(Encoding.ASCII.GetBytes("RIFF"))
                w.Write(36 + byteDati)
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "))
                w.Write(16)
                w.Write(CShort(1))
                w.Write(CShort(1))
                w.Write(frequenzaCampionamento)
                w.Write(frequenzaCampionamento * 2)
                w.Write(CShort(2))
                w.Write(CShort(16))
                w.Write(Encoding.ASCII.GetBytes("data"))
                w.Write(byteDati)
                For Each c As Short In campioni
                    w.Write(c)
                Next
                w.Flush()
                Return ms.ToArray()
            End Using
        End Using
    End Function

    Private Shared Sub AvviaSuono()
        Try
            FermaSuono()
            _suono = New SoundPlayer(New MemoryStream(CreaSuono()))
            _suono.PlayLooping()
            _fermaSuono = New System.Threading.Timer(Sub(o) SuInterfaccia(Sub() FermaSuono()), Nothing, 5 * 60 * 1000, Timeout.Infinite)
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.AvviaSuono: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub FermaSuono()
        Try
            If _fermaSuono IsNot Nothing Then
                _fermaSuono.Dispose()
                _fermaSuono = Nothing
            End If
            If _suono IsNot Nothing Then
                _suono.Stop()
                _suono.Dispose()
                _suono = Nothing
            End If
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.FermaSuono: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Fa suonare la sveglia e mostra il menu per spegnerla o rimandarla. Sul thread dell'interfaccia.</summary>
    Private Shared Sub Suona(s As DatiSveglia)
        Try
            If _inCorso Then
                SyncLock _blocco
                    _inAttesa.Enqueue(s)
                End SyncLock
                Return
            End If
            _inCorso = True
            AvviaSuono()
            Dim menu As New WGMenu()
            menu.aggiungi("Spegni", Sub(a, b) menu.abbandona())
            menu.aggiungi("Rimanda di dieci minuti",
                Sub(a, b)
                    Try
                        SyncLock _blocco
                            _rinvii.Add(New KeyValuePair(Of DateTime, DatiSveglia)(DateTime.Now.AddMinutes(MinutiRinvio), s))
                        End SyncLock
                        Riarma()
                    Catch ex As Exception
                        _log.ScriviLog("WGSveglie.Rimanda: " & ex.Message)
                    End Try
                    menu.abbandona()
                End Sub)
            AddHandler menu.abbandonato, Sub(a, b) FineSveglia()
            menu.avvia("Sveglia delle " & s.Ora.ToString("hh\:mm") & If(s.Nome <> "", ", " & s.Nome, ""), True)
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.Suona: " & ex.Message & Environment.NewLine & ex.StackTrace)
            FermaSuono()
            _inCorso = False
        End Try
    End Sub

    Private Shared Sub FineSveglia()
        Try
            FermaSuono()
            _inCorso = False
            Dim prossima As DatiSveglia = Nothing
            SyncLock _blocco
                If _inAttesa.Count > 0 Then prossima = _inAttesa.Dequeue()
            End SyncLock
            If prossima IsNot Nothing Then SuInterfaccia(Sub() Suona(prossima))
        Catch ex As Exception
            _log.ScriviLog("WGSveglie.FineSveglia: " & ex.Message)
            _inCorso = False
        End Try
    End Sub

#End Region

End Class

''' <summary>Il menu Sveglia di Altre funzioni.</summary>
Friend Class MenuSveglia

    Private Shared ReadOnly _log As New Logger()

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.Messaggio: " & ex.Message)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nuova sveglia", AddressOf Nuova)
            m.aggiungi("Sveglie impostate", AddressOf Impostate)
            m.avvia("Sveglia")
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.Avvia: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Nuova()
        Try
            Dim campi As New Campi()
            campi.AggiungiOrario("Orario", "o", True)
            campi.Aggiungi("Nome", "n", False)
            AddHandler campi.Fine, Sub() FineNuova(campi)
            campi.Avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.Nuova: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FineNuova(campi As Campi)
        Try
            If Not campi.TuttiObbligatoriRiempiti() Then Return
            Dim testoOra As String = campi.RecuperaValore("o")
            Dim nome As String = campi.RecuperaValore("n").Trim()
            Dim ora As TimeSpan
            Dim analisi As New WGUtilsDataOra()
            Dim risultato As DateTime? = Nothing
            If analisi.AnalizzaOrario(testoOra, risultato) AndAlso risultato.HasValue Then
                ora = risultato.Value.TimeOfDay
            ElseIf Not TimeSpan.TryParse(testoOra, ora) Then
                Messaggio("Non ho capito l'orario.")
                Return
            End If
            ora = New TimeSpan(ora.Hours, ora.Minutes, 0)
            ScegliRipetizione(ora, nome)
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.FineNuova: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliRipetizione(ora As TimeSpan, nome As String)
        Try
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Ripetizione")
            lista.aggiungiElemento("Una volta sola", "0")
            lista.aggiungiElemento("Ogni giorno", "127")
            lista.aggiungiElemento("Dal lunedì al venerdì", "31")
            lista.aggiungiElemento("Sabato e domenica", "96")
            lista.aggiungiElemento("Giorni a scelta", "scelta")
            AddHandler lista.escPremuto, Sub() lista.abbandona()
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        lista.abbandona()
                        If id = "scelta" Then
                            ScegliGiorni(ora, nome)
                        Else
                            Crea(ora, nome, CInt(id))
                        End If
                    Catch ex As Exception
                        _log.ScriviLog("MenuSveglia.ScegliRipetizione selezione: " & ex.Message)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.ScegliRipetizione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliGiorni(ora As TimeSpan, nome As String)
        Try
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = True
            lista.impostaTitolo("Giorni")
            Dim nomi As String() = {"Lunedì", "Martedì", "Mercoledì", "Giovedì", "Venerdì", "Sabato", "Domenica"}
            For i As Integer = 0 To 6
                lista.aggiungiElemento(nomi(i), i.ToString())
            Next
            AddHandler lista.escPremuto, Sub() lista.abbandona()
            AddHandler lista.ElementiSelezionati,
                Sub(ids As List(Of String))
                    Try
                        Dim mask As Integer = 0
                        For Each id As String In ids
                            mask = mask Or (1 << CInt(id))
                        Next
                        If mask = 0 Then
                            Messaggio("Scegli almeno un giorno.")
                            Return
                        End If
                        lista.abbandona()
                        Crea(ora, nome, mask)
                    Catch ex As Exception
                        _log.ScriviLog("MenuSveglia.ScegliGiorni selezione: " & ex.Message)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.ScegliGiorni: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Crea(ora As TimeSpan, nome As String, giorni As Integer)
        Try
            Dim s As New DatiSveglia()
            s.Id = Guid.NewGuid().ToString("N").Substring(0, 8)
            s.Ora = ora
            s.Nome = nome
            s.Giorni = giorni
            s.Attiva = True
            WGSveglie.Aggiungi(s)
            Messaggio("Sveglia impostata: " & s.Descrizione())
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.Crea: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub Impostate()
        Try
            Dim elenco As List(Of DatiSveglia) = WGSveglie.Elenco()
            If elenco.Count = 0 Then
                Messaggio("Non ci sono sveglie impostate.")
                Return
            End If
            elenco.Sort(Function(a, b) a.Ora.CompareTo(b.Ora))
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Sveglie impostate")
            For Each s As DatiSveglia In elenco
                lista.aggiungiElemento(s.Descrizione(), s.Id)
            Next
            lista.aggiungiOpzione("Elimina",
                Sub(a, b)
                    Try
                        Dim id As String = lista.ottieniElemento()
                        If String.IsNullOrEmpty(id) Then Return
                        WGSveglie.Elimina(id)
                        lista.RimuoviElemento(id)
                        elenco.RemoveAll(Function(x) x.Id = id)
                        If elenco.Count = 0 Then lista.abbandona()
                    Catch ex As Exception
                        _log.ScriviLog("MenuSveglia.Elimina: " & ex.Message)
                    End Try
                End Sub)
            AddHandler lista.escPremuto, Sub() lista.abbandona()
            AddHandler lista.ElementoSelezionato,
                Sub(id As String)
                    Try
                        For Each s As DatiSveglia In elenco
                            If s.Id = id Then
                                WGSveglie.ImpostaAttiva(id, Not s.Attiva)
                                s.Attiva = Not s.Attiva
                                lista.AggiornaTestoElemento(id, s.Descrizione())
                                Exit For
                            End If
                        Next
                    Catch ex As Exception
                        _log.ScriviLog("MenuSveglia.Commuta: " & ex.Message)
                    End Try
                End Sub
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuSveglia.Impostate: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

End Class
