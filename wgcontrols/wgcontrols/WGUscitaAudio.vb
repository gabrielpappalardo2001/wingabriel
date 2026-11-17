' ============================================================================================
' WGUscitaAudio - dove si ascolta l'audio: altoparlanti, cuffie Bluetooth, cuffie con il filo
' ============================================================================================
'
' Passa l'audio di Windows (e quindi anche la voce di WinGabriel, che segue il predefinito) da un
' dispositivo all'altro senza passare dalle impostazioni, e annuncia ogni cambio: quando si
' accendono le cuffie Bluetooth "Audio su Cuffie ...", quando si spengono o escono di portata
' "Cuffie ... scollegate, audio su Altoparlanti". Se la voce usava proprio il dispositivo staccato,
' sintesivocale la riporta sul predefinito (GestisciDispositivoAudioNonDisponibile).
' I dispositivi "vivavoce" (Hands-Free) delle cuffie Bluetooth hanno una qualità da telefonata:
' nel passaggio veloce si saltano, nell'elenco completo ci sono.
' Le cuffie Bluetooth associate ma non collegate compaiono nell'elenco e si collegano al volo
' (WGCuffieBluetooth); se ci sono solo gli altoparlanti, il passaggio veloce prova a collegare le cuffie.
'
'   WGUscitaAudio.Avvia()                  ' una volta, dal thread dell'interfaccia
'   WGUscitaAudio.Scegli()                 ' elenco dei dispositivi, Invio per usarne uno
'   WGUscitaAudio.PassaAlSuccessivo()      ' altoparlanti <-> cuffie al volo
'   WGUscitaAudio.NomeAttuale()
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports NAudio.CoreAudioApi
Imports NAudio.CoreAudioApi.Interfaces

Public NotInheritable Class WGUscitaAudio

    Private Shared ReadOnly log As New Logger()
    Private Shared _enumeratore As MMDeviceEnumerator
    Private Shared _notifiche As Notifiche
    Private Shared _contesto As SynchronizationContext
    Private Shared WithEvents _attesaAnnuncio As System.Windows.Forms.Timer
    Private Shared _ultimoId As String = ""
    Private Shared _ultimoNome As String = ""
    Private Shared _staccato As String = ""
    Private Shared ReadOnly _lock As New Object()

    Private Sub New()
    End Sub

    Private Class Notifiche
        Implements IMMNotificationClient

        Public Sub OnDeviceStateChanged(deviceId As String, newState As DeviceState) Implements IMMNotificationClient.OnDeviceStateChanged
            If newState <> DeviceState.Active Then Staccato(deviceId)
        End Sub

        Public Sub OnDeviceAdded(pwstrDeviceId As String) Implements IMMNotificationClient.OnDeviceAdded
        End Sub

        Public Sub OnDeviceRemoved(deviceId As String) Implements IMMNotificationClient.OnDeviceRemoved
            Staccato(deviceId)
        End Sub

        Public Sub OnDefaultDeviceChanged(flow As DataFlow, role As Role, defaultDeviceId As String) Implements IMMNotificationClient.OnDefaultDeviceChanged
            If flow <> DataFlow.Render OrElse role <> Role.Multimedia Then Return
            PredefinitoCambiato()
        End Sub

        Public Sub OnPropertyValueChanged(pwstrDeviceId As String, key As PropertyKey) Implements IMMNotificationClient.OnPropertyValueChanged
        End Sub
    End Class

    ''' <summary>Comincia ad annunciare i cambi di uscita audio.</summary>
    Public Shared Sub Avvia()
        Try
            If _enumeratore IsNot Nothing Then Return
            _contesto = SynchronizationContext.Current
            _attesaAnnuncio = New System.Windows.Forms.Timer() With {.Interval = 1200}
            _enumeratore = EnumeratoreAudio.Crea()
            _ultimoId = WGAudio.AltoparlantePredefinitoId()
            _ultimoNome = NomeDi(_ultimoId)
            _notifiche = New Notifiche()
            _enumeratore.RegisterEndpointNotificationCallback(_notifiche)
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.Avvia: " & ex.Message)
        End Try
    End Sub

    ' Il dispositivo che si stava usando si è staccato: lo si ricorda per l'annuncio.
    Private Shared Sub Staccato(id As String)
        SyncLock _lock
            If String.Equals(id, _ultimoId, StringComparison.OrdinalIgnoreCase) AndAlso _ultimoNome <> "" Then _staccato = _ultimoNome
        End SyncLock
        PredefinitoCambiato()
    End Sub

    ' Le notifiche arrivano da un thread di Windows e a raffica: si annuncia una volta sola, poco dopo.
    Private Shared Sub PredefinitoCambiato()
        Try
            Dim riavvia As SendOrPostCallback = Sub(s)
                                                    _attesaAnnuncio.Stop()
                                                    _attesaAnnuncio.Start()
                                                End Sub
            If _contesto IsNot Nothing Then _contesto.Post(riavvia, Nothing)
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.PredefinitoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub _attesaAnnuncio_Tick(sender As Object, e As EventArgs) Handles _attesaAnnuncio.Tick
        _attesaAnnuncio.Stop()
        Try
            Dim id As String = WGAudio.AltoparlantePredefinitoId()
            Dim staccato As String
            SyncLock _lock
                staccato = _staccato
                _staccato = ""
                If id = "" OrElse String.Equals(id, _ultimoId, StringComparison.OrdinalIgnoreCase) Then
                    If staccato <> "" Then sintesivocale.GetInstance().LeggiTesto(staccato & " scollegato", False, "Annuncio")
                    Return
                End If
                _ultimoId = id
                _ultimoNome = NomeDi(id)
            End SyncLock
            sintesivocale.GetInstance().LeggiTesto(If(staccato <> "", staccato & " scollegato, audio su ", "Audio su ") & _ultimoNome, False, "Annuncio")
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.Annuncio: " & ex.Message)
        End Try
    End Sub

    Private Shared Function NomeDi(id As String) As String
        Try
            If String.IsNullOrEmpty(id) Then Return ""
            Dim trovato As WGDispositivoAudio = WGAudio.ElencaAltoparlanti().FirstOrDefault(Function(d) String.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
            If Not String.IsNullOrEmpty(trovato.Nome) Then Return trovato.Nome
            If _enumeratore IsNot Nothing Then
                Using dispositivo As MMDevice = _enumeratore.GetDevice(id)
                    Return dispositivo.FriendlyName
                End Using
            End If
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.NomeDi: " & ex.Message)
        End Try
        Return "dispositivo sconosciuto"
    End Function

    ''' <summary>
    ''' Nome del dispositivo su cui si ascolta adesso. Dopo Avvia viene dalla memoria, aggiornata a ogni
    ''' cambio: si può chiamare a ogni disegno della barra di stato.
    ''' </summary>
    Public Shared Function NomeAttuale() As String
        SyncLock _lock
            If _enumeratore IsNot Nothing AndAlso _ultimoNome <> "" Then Return _ultimoNome
        End SyncLock
        Return NomeDi(WGAudio.AltoparlantePredefinitoId())
    End Function

    Private Shared Function Vivavoce(nome As String) As Boolean
        Dim minuscolo As String = If(nome, "").ToLowerInvariant()
        Return minuscolo.Contains("hands-free") OrElse minuscolo.Contains("handsfree") OrElse minuscolo.Contains("vivavoce") OrElse minuscolo.Contains("auricolare")
    End Function

    ''' <summary>Usa questo dispositivo; vero se Windows l'ha preso davvero.</summary>
    Public Shared Function Usa(dispositivo As WGDispositivoAudio) As Boolean
        Try
            WGAudio.ImpostaAltoparlantePredefinito(dispositivo.Id)
            Dim riuscito As Boolean = String.Equals(WGAudio.AltoparlantePredefinitoId(), dispositivo.Id, StringComparison.OrdinalIgnoreCase)
            If riuscito Then
                SyncLock _lock
                    _ultimoId = dispositivo.Id
                    _ultimoNome = dispositivo.Nome
                    _staccato = ""
                End SyncLock
                sintesivocale.GetInstance().LeggiTesto("Audio su " & dispositivo.Nome, False, "Annuncio")
            Else
                WGDomanda.Chiedi("Windows non ha passato l'audio su " & dispositivo.Nome & ". Vuoi riprovare?", Sub() Usa(dispositivo))
            End If
            Return riuscito
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.Usa: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Passa al dispositivo successivo (senza i vivavoce): con altoparlanti e cuffie, dall'uno all'altro.
    ''' </summary>
    Public Shared Sub PassaAlSuccessivo()
        Try
            Dim tutti As List(Of WGDispositivoAudio) = WGAudio.ElencaAltoparlanti()
            Dim scelti As List(Of WGDispositivoAudio) = tutti.Where(Function(d) Not Vivavoce(d.Nome)).ToList()
            If scelti.Count = 0 Then scelti = tutti
            Dim attuale As String = WGAudio.AltoparlantePredefinitoId()
            If scelti.Count < 2 Then
                ' Solo gli altoparlanti: si provano a collegare le cuffie Bluetooth associate.
                Dim cuffie As UscitaBluetooth = WGCuffieBluetooth.Elenca().FirstOrDefault(Function(u) Not u.Collegata AndAlso Not u.Vivavoce)
                If cuffie IsNot Nothing Then
                    CollegaEUsa(cuffie)
                    Return
                End If
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio(If(scelti.Count = 0, "Non c'è nessun dispositivo per ascoltare l'audio.", "C'è solo " & scelti(0).Nome & ": per passare alle cuffie, collegale con il filo oppure associale dal Bluetooth."))
                messaggio.ShowDialog()
                Return
            End If
            Dim posizione As Integer = scelti.FindIndex(Function(d) String.Equals(d.Id, attuale, StringComparison.OrdinalIgnoreCase))
            Usa(scelti((posizione + 1) Mod scelti.Count))
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.PassaAlSuccessivo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Collega le cuffie Bluetooth (con l'attesa) e ci passa l'audio; se non si collegano chiede se riprovare.
    ''' </summary>
    Public Shared Async Sub CollegaEUsa(uscita As UscitaBluetooth)
        Try
            If uscita Is Nothing Then Return
            Dim collegata As Boolean = Await WGProcessoInCorso.EseguiAsync("Collegamento di " & uscita.Nome & ": le cuffie devono essere accese", Function() WGCuffieBluetooth.CollegaAsync(uscita))
            If Not collegata Then
                WGDomanda.Chiedi(uscita.Nome & " non si è collegato: controlla che sia acceso e vicino. Vuoi riprovare?", Sub() CollegaEUsa(uscita))
                Return
            End If
            Dim dispositivo As WGDispositivoAudio = WGAudio.ElencaAltoparlanti().FirstOrDefault(Function(d) String.Equals(d.Id, uscita.Id, StringComparison.OrdinalIgnoreCase))
            If String.IsNullOrEmpty(dispositivo.Id) Then dispositivo = New WGDispositivoAudio With {.Id = uscita.Id, .Nome = uscita.Nome}
            Usa(dispositivo)
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.CollegaEUsa: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Stacca le cuffie Bluetooth: l'audio torna sugli altoparlanti.</summary>
    Public Shared Sub Scollega(uscita As UscitaBluetooth)
        Try
            If Not WGCuffieBluetooth.Scollega(uscita) Then
                WGDomanda.Chiedi("Windows non ha scollegato " & uscita.Nome & ". Vuoi riprovare?", Sub() Scollega(uscita))
            End If
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.Scollega: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Elenco dei dispositivi audio, con quello in uso già selezionato.</summary>
    Public Shared Sub Scegli()
        Try
            Dim dispositivi As List(Of WGDispositivoAudio) = WGAudio.ElencaAltoparlanti()
            If dispositivi.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non c'è nessun dispositivo per ascoltare l'audio: controlla che altoparlanti o cuffie siano collegati.")
                messaggio.ShowDialog()
                Return
            End If
            Dim attuale As String = WGAudio.AltoparlantePredefinitoId()
            Dim menu As New WGMenu()
            Dim indiceAttuale As Integer = 0
            For i As Integer = 0 To dispositivi.Count - 1
                Dim dispositivo As WGDispositivoAudio = dispositivi(i)
                Dim inUso As Boolean = String.Equals(dispositivo.Id, attuale, StringComparison.OrdinalIgnoreCase)
                If inUso Then indiceAttuale = i
                menu.aggiungi(dispositivo.Nome & If(inUso, ", in uso", "") & If(Vivavoce(dispositivo.Nome), ", qualità da telefonata", ""), Sub() Usa(dispositivo))
            Next
            ' Cuffie Bluetooth associate ma non collegate: Invio le collega e ci passa l'audio.
            Dim bluetooth As List(Of UscitaBluetooth) = WGCuffieBluetooth.Elenca()
            For Each uscita As UscitaBluetooth In bluetooth.Where(Function(u) Not u.Collegata AndAlso Not u.Vivavoce)
                Dim daCollegare As UscitaBluetooth = uscita
                menu.aggiungi(uscita.Nome & ", Bluetooth non collegato, Invio per collegarlo", Sub() CollegaEUsa(daCollegare))
            Next
            For Each uscita As UscitaBluetooth In bluetooth.Where(Function(u) u.Collegata AndAlso Not u.Vivavoce)
                Dim daScollegare As UscitaBluetooth = uscita
                menu.aggiungi("Scollega " & uscita.Nome, Sub() Scollega(daScollegare))
            Next
            menu.posiziona(indiceAttuale)
            menu.avvia("Uscita audio", True)
        Catch ex As Exception
            log.ScriviLog("WGUscitaAudio.Scegli: " & ex.Message)
        End Try
    End Sub

End Class
