' ============================================================================================
' WGAccoglienza - la prima volta con la nuova versione: scelta della modalità e prova dei tasti
' ============================================================================================
'
' Al primo avvio della versione con scrivania e applicazioni separate WinGabriel chiede se usare la
' modalità smart o la classica. Con la smart propone una prova guidata: ogni passo dice cosa premere
' e aspetta che lo si faccia davvero, controllando lo stato di WinGabriel (non i tasti), così vale
' qualunque strada si usi, anche il touch. Se un passo non riesce entro un minuto lo si ripete una
' volta e poi si va avanti. La prova si ripete da Impostazioni, Sistema.
'
'   Dim prova As Boolean = Await WGAccoglienza.ChiediSeServeAsync()   ' all'avvio, prima di tutto il resto
'   If prova Then WGAccoglienza.AvviaProva()                            ' quando la scrivania è pronta
'   WGAccoglienza.AvviaProva()
' ============================================================================================
Imports System.Collections.Generic
Imports System.Windows.Forms

Public NotInheritable Class WGAccoglienza

    Private Const IdConfigurazione As String = "accoglienza_nuova_versione"
    Private Shared ReadOnly log As New Logger()

    Private Class Passo
        Public Istruzione As String
        Public Fatto As Func(Of WGContenitore, Boolean)
    End Class

    Private Shared _passi As List(Of Passo)
    Private Shared _indice As Integer
    Private Shared _inizioPasso As DateTime
    Private Shared _ripetuto As Boolean
    Private Shared WithEvents _timer As Timer
    ' Nome dell'applicazione corrente all'inizio del passo, per i passi "passa a un'altra applicazione".
    Private Shared _applicazioneAllInizio As String = ""

    Private Sub New()
    End Sub

    ''' <summary>
    ''' La prima volta, all'avvio e prima di tutto il resto: il benvenuto con la scelta della modalità e
    ''' la domanda sulla prova dei tasti. Finisce quando si è risposto; vero se si vuole la prova, da
    ''' avviare quando la scrivania è pronta. Le volte dopo finisce subito con falso.
    ''' </summary>
    Public Shared Function ChiediSeServeAsync() As System.Threading.Tasks.Task(Of Boolean)
        Dim esito As New System.Threading.Tasks.TaskCompletionSource(Of Boolean)()
        Try
            Dim config As New WGConfig()
            If config.LeggiValore(IdConfigurazione) = "1" Then
                esito.TrySetResult(False)
            Else
                config.AggiungiValore(IdConfigurazione, "1")
                ChiediModalita(esito)
            End If
        Catch ex As Exception
            log.ScriviLog("WGAccoglienza.ChiediSeServeAsync: " & ex.Message)
            esito.TrySetResult(False)
        End Try
        Return esito.Task
    End Function

    ''' <summary>Come ChiediSeServeAsync, avviando da sé la prova se la si vuole.</summary>
    Public Shared Async Sub AvviaSeServe()
        Try
            If Await ChiediSeServeAsync() Then AvviaProva()
        Catch ex As Exception
            log.ScriviLog("WGAccoglienza.AvviaSeServe: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ChiediModalita(esito As System.Threading.Tasks.TaskCompletionSource(Of Boolean))
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Modalità smart, consigliata", Sub(s, e)
                                                             WGModalita.ScegliAlProssimoAvvio(True)
                                                             WGDomanda.Chiedi("Vuoi una prova guidata dei tasti nuovi? Dura un paio di minuti",
                                                                              Sub() esito.TrySetResult(True),
                                                                              Sub()
                                                                                  sintesivocale.GetInstance().LeggiTesto("La prova si fa quando vuoi da Impostazioni, Sistema. La guida è nel menu applicazioni, con Alt.")
                                                                                  esito.TrySetResult(False)
                                                                              End Sub)
                                                         End Sub)
            menu.aggiungi("Modalità classica, il WinGabriel di sempre", Sub(s, e)
                                                   WGModalita.ScegliAlProssimoAvvio(False)
                                                   Dim m As New WGMessaggi()
                                                   m.Messaggio(WGModalita.DescrizioneClassica & " Vale dal prossimo avvio di WinGabriel. Si cambia da Impostazioni, Modalità.")
                                                   m.ShowDialog()
                                                   esito.TrySetResult(False)
                                               End Sub)
            AddHandler menu.abbandonato, Sub(s As Object, e As EventArgs) esito.TrySetResult(False)
            menu.avvia("Benvenuto, scegli la modalità", True)
        Catch ex As Exception
            log.ScriviLog("WGAccoglienza.ChiediModalita: " & ex.Message)
            esito.TrySetResult(False)
        End Try
    End Sub

    ''' <summary>Prova guidata dei tasti della modalità smart.</summary>
    Public Shared Sub AvviaProva()
        Try
            If Not WGModalita.Smart OrElse Not WGContenitore.ApplicazioniSeparate Then
                sintesivocale.GetInstance().LeggiTesto("La prova riguarda la modalità smart con le applicazioni separate")
                Return
            End If
            _passi = New List(Of Passo) From {
                New Passo() With {.Istruzione = "Premi il tasto Windows per aprire il menu applicazioni.",
                                  .Fatto = Function(c) c.ESulMenuApplicazioni()},
                New Passo() With {.Istruzione = "Ora scegli Editor con le frecce e premi Invio: si apre come applicazione a sé.",
                                  .Fatto = Function(c) c.ApplicazioneAperta("Editor")}
            }
            ' Senza scrivania (disattivata o in modalità tablet) i passi della scrivania non si possono fare.
            If WGScrivania.Attiva AndAlso Not WGModalitaTablet.Attiva() Then
                _passi.Add(New Passo() With {.Istruzione = "Premi Control Alt D per tornare alla scrivania. L'editor resta aperto.",
                                             .Fatto = Function(c) String.Equals(c.NomeApplicazioneCorrente, WGScrivania.NomeApplicazione, StringComparison.OrdinalIgnoreCase)})
                _passi.Add(New Passo() With {.Istruzione = "Premi Control F10 per tornare subito all'ultima applicazione usata, l'editor.",
                                             .Fatto = Function(c) String.Equals(c.NomeApplicazioneCorrente, "Editor", StringComparison.OrdinalIgnoreCase)})
            End If
            _passi.AddRange({
                New Passo() With {.Istruzione = "Premi Control freccia sinistra o destra per passare a un'altra applicazione aperta.",
                                  .Fatto = Function(c) Not String.Equals(c.NomeApplicazioneCorrente, _applicazioneAllInizio, StringComparison.OrdinalIgnoreCase)},
                New Passo() With {.Istruzione = "Premi F10 per l'elenco delle applicazioni aperte, scegli Editor e premi Invio.",
                                  .Fatto = Function(c) String.Equals(c.NomeApplicazioneCorrente, "Editor", StringComparison.OrdinalIgnoreCase)},
                New Passo() With {.Istruzione = "Premi Control F4 per chiudere l'editor. Se hai scritto qualcosa, ti chiederà se salvarlo.",
                                  .Fatto = Function(c) Not c.ApplicazioneAperta("Editor")}
            })
            _indice = -1
            If _timer Is Nothing Then _timer = New Timer() With {.Interval = 400}
            ProssimoPasso()
            _timer.Start()
        Catch ex As Exception
            log.ScriviLog("WGAccoglienza.AvviaProva: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ProssimoPasso()
        _indice += 1
        _ripetuto = False
        _inizioPasso = DateTime.Now
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        _applicazioneAllInizio = If(contenitore Is Nothing, "", contenitore.NomeApplicazioneCorrente)
        If _indice >= _passi.Count Then
            _timer.Stop()
            sintesivocale.GetInstance().LeggiTesto("Prova finita, complimenti. Ricorda anche Windows S per cercare in tutto WinGabriel e Tab sulla scrivania per l'ora, il volume, dove si ascolta l'audio, la batteria e la connessione. La guida completa è nel menu applicazioni, con Alt, Guida delle applicazioni.")
            Return
        End If
        sintesivocale.GetInstance().LeggiTesto("Passo " & (_indice + 1) & " di " & _passi.Count & ". " & _passi(_indice).Istruzione)
    End Sub

    Private Shared Sub _timer_Tick(sender As Object, e As EventArgs) Handles _timer.Tick
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse _passi Is Nothing OrElse _indice < 0 OrElse _indice >= _passi.Count Then
                _timer.Stop()
                Return
            End If
            If _passi(_indice).Fatto(contenitore) Then
                _timer.Stop()
                ' Si lascia prima leggere quello che WinGabriel annuncia dopo il tasto.
                Dim pausa As New Timer() With {.Interval = 1800}
                AddHandler pausa.Tick, Sub(s, ev)
                                           pausa.Stop()
                                           pausa.Dispose()
                                           ProssimoPasso()
                                           If _indice < _passi.Count Then _timer.Start()
                                       End Sub
                pausa.Start()
                Return
            End If
            If (DateTime.Now - _inizioPasso).TotalSeconds > 60 Then
                If _ripetuto Then
                    sintesivocale.GetInstance().LeggiTesto("Andiamo avanti.")
                    ProssimoPasso()
                Else
                    _ripetuto = True
                    _inizioPasso = DateTime.Now
                    sintesivocale.GetInstance().LeggiTesto("Riproviamo. " & _passi(_indice).Istruzione)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGAccoglienza.Tick: " & ex.Message)
        End Try
    End Sub

End Class
