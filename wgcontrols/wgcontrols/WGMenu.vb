Imports System.ComponentModel
Imports System.Threading
Imports System.Windows.Forms
Imports System.Drawing
Imports wgcontrols
Imports System.Linq
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Diagnostics
Imports System.Net.Http
Imports System.Threading.Tasks
Imports System.Collections.Generic

Public Class WGMenu

    Private WithEvents _inner As WGMenuInt

    Public Event abbandonato As EventHandler
    Public Event MenuRiattivato As EventHandler

    Public Sub New()
        _inner = New WGMenuInt()
    End Sub

    ''' <summary>
    ''' Punto d'innesto per i plugin: chiamato per ogni menu un attimo prima che si apra, con il menu e il suo titolo. Vale per tutti i menu
    ''' di WinGabriel, anche per quelli che verranno scritti in futuro: non serve registrare niente.
    ''' </summary>
    Public Shared Property AllApertura As Action(Of WGMenu, String)

    Public Sub avvia(MenuTitolo As String, Optional sincronomenu As Boolean = False)
        Try
            Dim gestore As Action(Of WGMenu, String) = AllApertura
            If gestore IsNot Nothing Then gestore.Invoke(Me, MenuTitolo)
        Catch ex As Exception
            Try
                Dim registro As New Logger()
                registro.ScriviLog("WGMenu.avvia (AllApertura): " & ex.Message)
            Catch
            End Try
        End Try
        _inner.avvia(MenuTitolo, sincronomenu)
    End Sub

    Public Sub aggiungi(testoVoce As String, evento As EventHandler)
        _inner.aggiungi(testoVoce, evento)
    End Sub

    Public Sub AggiungiDopo(voceRiferimento As String, nuovaVoce As String, evento As EventHandler)
        _inner.AggiungiDopo(voceRiferimento, nuovaVoce, evento)
    End Sub

    Public Sub aggiungiOpzione(testoVoce As String, evento As EventHandler)
        _inner.aggiungiOpzione(testoVoce, evento)
    End Sub

    Public Sub RimuoviVoce(nomeVoce As String)
        _inner.RimuoviVoce(nomeVoce)
    End Sub

    Public Sub RimuoviOpzione(testoOpzione As String)
        _inner.RimuoviOpzione(testoOpzione)
    End Sub

    Public Sub RinominaVoce(vecchioNome As String, nuovoNome As String)
        _inner.RinominaVoce(vecchioNome, nuovoNome)
    End Sub

    Public Sub chiudi()
        _inner.abbandona()
    End Sub

    ''' <summary>Falso per non dire "menù" prima del titolo (per esempio le azioni di una notifica).</summary>
    Public Property TitoloComeMenu As Boolean
        Get
            Return _inner.TitoloComeMenu
        End Get
        Set(value As Boolean)
            _inner.TitoloComeMenu = value
        End Set
    End Property

    Public Sub abbandona()
        _inner.abbandona()
    End Sub

    Public Sub posiziona(indice As Integer)
        _inner.posiziona(indice)
    End Sub

    ''' <summary>
    ''' Cosa fare con Esc (o il gesto di uscita) al posto di chiudere il menu: dal menu applicazioni si torna
    ''' alla scrivania. Se l'azione restituisce falso il menu si chiude come sempre.
    ''' </summary>
    Public Sub ImpostaAzioneEsc(azione As Func(Of Boolean))
        _inner.AzioneEsc = azione
    End Sub

    ''' <summary>Da chiamare sul menu iniziale prima di avviarlo: le voci scelte da lì aprono nuove applicazioni.</summary>
    Public Sub SegnaComeMenuIniziale()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.ImpostaMenuIniziale(_inner)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGMenu.SegnaComeMenuIniziale: " & ex.Message)
        End Try
    End Sub

    Private Sub OnInnerAbbandonato(sender As Object, e As EventArgs) Handles _inner.abbandonato
        RaiseEvent abbandonato(sender, e)
    End Sub

    Private Sub OnInnerMenuRiattivato(sender As Object, e As EventArgs) Handles _inner.MenuRiattivato
        RaiseEvent MenuRiattivato(sender, e)
    End Sub
End Class

Friend Class WGMenuInt
    Inherits System.Windows.Forms.UserControl
    Implements IWGChiudibile

    Public Sub RichiediChiusura() Implements IWGChiudibile.RichiediChiusura
        abbandona()
    End Sub

    Private Class Voce
        Public Property Nome As String
        Public Property Evento As EventHandler
    End Class

    Private Delegate Sub DelegatoAvvia(titolo As String, sincronomenu As Boolean)
    Private Delegate Sub DelegatoStringa(testo As String)
    Private Delegate Sub DelegatoStringaEvento(testo As String, evento As EventHandler)
    Private Delegate Sub DelegatoAggiungiDopo(rif As String, nuova As String, evento As EventHandler)
    Private Delegate Sub DelegatoDueStringhe(primo As String, secondo As String)
    Private Delegate Sub DelegatoIntero(indice As Integer)

    Private utiliti As WGFormUtils
    Private Log As Logger = New Logger()
    Private lettura As sintesivocale
    Private titolo As String
    Private sincrono As Boolean

    Private _primaApertura As Boolean = True
    Private _eventoAbbandonatoEmesso As Boolean = False
    Private _dialogoOpzioniAperto As Boolean = False
    Private _azioneInCorso As Boolean = False
    Private _disposed As Boolean = False
    Private errore As String

    Friend WithEvents Menuvoci As WGListaView

    ' Esc al posto della chiusura (menu applicazioni: torna alla scrivania); falso = chiusura normale.
    Friend AzioneEsc As Func(Of Boolean)
    ' Voci da scegliere dal menu applicazioni per arrivare a questo menu; Nothing se non ci si arriva da un menu.
    Friend PercorsoMenu As List(Of String)
    ' Vero mentre gira un'azione del menu opzioni: le finestre aperte da lì non sono voci del menu.
    Friend EsegueOpzione As Boolean

    Friend Property TitoloComeMenu As Boolean = True

    Friend Function ContieneVoce(nome As String) As Boolean
        Return _eventi.ContainsKey(nome)
    End Function

    ''' <summary>Le voci che fanno qualcosa, nell'ordine del menu (per la ricerca globale).</summary>
    Friend Function VociMenu() As List(Of String)
        Try
            Return _voci.Where(Function(v) Not String.IsNullOrEmpty(v) AndAlso _eventi.ContainsKey(v)).ToList()
        Catch ex As Exception
            ScriviLogErrore("VociMenu", ex)
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>Esegue la voce come con Invio (per i collegamenti della scrivania); falso se la voce non c'è.</summary>
    Friend Function EseguiVoce(nome As String) As Boolean
        Try
            If _disposed OrElse Me.IsDisposed OrElse Me.Disposing Then Return False
            Dim azione As EventHandler = Nothing
            If Not _eventi.TryGetValue(nome, azione) OrElse azione Is Nothing Then Return False
            Dim indice As Integer = Menuvoci.Items.IndexOf(nome)
            If indice >= 0 Then Menuvoci.SelectedIndex = indice
            If _azioneInCorso Then Return False
            _azioneInCorso = True
            Try
                azione.Invoke(Nothing, EventArgs.Empty)
            Finally
                _azioneInCorso = False
            End Try
            RaiseEvent eventoattivato(Me, Nothing)
            If sincrono Then abbandona()
            Return True
        Catch ex As ObjectDisposedException
            Return True
        Catch ex As Exception
            ScriviLogErrore("EseguiVoce", ex)
            Return False
        End Try
    End Function

    ' Percorso della voce selezionata, per metterla sulla scrivania; Nothing se il menu non si raggiunge dal menu applicazioni.
    Private Function PercorsoVoceSelezionata() As List(Of String)
        Dim voce As String = VoceSelezionata
        If voce = "" OrElse Not _eventi.ContainsKey(voce) Then Return Nothing
        Dim c As WGContenitore = WGContenitore.Istanza
        If c IsNot Nothing AndAlso c.EMenuIniziale(Me) Then Return New List(Of String) From {voce}
        If PercorsoMenu Is Nothing Then Return Nothing
        Dim risultato As New List(Of String)(PercorsoMenu)
        risultato.Add(voce)
        Return risultato
    End Function

    Private Const VoceAggiungiAllaScrivania As String = "Aggiungi alla scrivania"
    Private Const VoceAggiungiAllaHome As String = "Aggiungi alla home"

    Friend ReadOnly Property TitoloMenu As String
        Get
            Return If(titolo, "")
        End Get
    End Property

    Friend ReadOnly Property VoceSelezionata As String
        Get
            Try
                If Menuvoci Is Nothing OrElse Menuvoci.IsDisposed Then Return ""
                Dim indice As Integer = Menuvoci.SelectedIndex
                If indice < 0 OrElse indice >= Menuvoci.Items.Count Then Return ""
                Return Menuvoci.Items(indice).ToString()
            Catch ex As Exception
                Log.ScriviLog("WGMenuInt.VoceSelezionata: " & ex.Message)
                Return ""
            End Try
        End Get
    End Property

    Private _voci As New List(Of String)()
    Private _eventi As New Dictionary(Of String, EventHandler)()
    Private _opzioniDefinite As New List(Of Voce)()
    Private _ultimoElemento As Integer = 0

    Private WithEvents _controlloOpzioni As WGControlloOpzioni

    Private _tastiera As WGTastiera
    Private _touk As WGTouk

    Public Event abbandonato As EventHandler
    Public Event MenuRiattivato As EventHandler
    Public Event eventoattivato As EventHandler

    Private Sub ScriviLogErrore(methodName As String, ex As Exception)
        Try
            Dim tipoErrore As String = ex.GetType().Name
            Dim logMessage As String = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in {methodName} ({tipoErrore}).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}"
            Log.ScriviLog(logMessage)
        Catch exLog As Exception
        End Try
    End Sub

    Public Sub New()
        Try
            _tastiera = WGTastiera.GetInstance()
            If _tastiera IsNot Nothing Then
                AddHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                AddHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
            End If

            _touk = WGTouk.GetInstance()
            If _touk IsNot Nothing Then
                AddHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                AddHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                AddHandler _touk.FlickSu, AddressOf _touk_FlickSu
            End If

            Try
                utiliti = WGFormUtils.GetInstance()
            Catch exUtiliti As Exception
                ScriviLogErrore("New (WGFormUtils)", exUtiliti)
            End Try

            Menuvoci = New WGListaView()
            Menuvoci.TitoloComeMenu = TitoloComeMenu
            Menuvoci.Dock = DockStyle.Fill
            Menuvoci.SelezioneConsentita = False
            AddHandler Menuvoci.SelectedIndexChanged, AddressOf Menuvoci_SelectedIndexChanged
            Me.Controls.Add(Menuvoci)

            Me.Dock = DockStyle.Fill
            Me.AccessibleRole = AccessibleRole.Client
            Me.AccessibleName = " "

            If utiliti Is Nothing Then
                utiliti = WGFormUtils.GetInstance()
            End If
            If lettura Is Nothing Then
                lettura = sintesivocale.GetInstance()
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("New", ex)
        End Try
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If _disposed OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return False
            If Not Me.Visible Then Return False
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then Return False
            Return c.PuoRicevereInput(Me)
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub EseguiSuUI(azione As MethodInvoker)
        Try
            If azione Is Nothing Then Return
            If _disposed OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(azione)
            Else
                azione.Invoke()
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            Return
        Catch ex As Exception
            ScriviLogErrore("EseguiSuUI", ex)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys)
        Try
            If Not PuoRicevere() Then Return
            If tasto = Keys.None Then Return

            Dim codice As Keys = tasto And Keys.KeyCode

            Select Case codice
                Case Keys.F1
                    EseguiSuUI(AddressOf WGGuida.MostraGuidaWGMenu)
                    tasto = Keys.None
                Case Keys.Enter, Keys.Return, Keys.Right
                    EseguiSuUI(AddressOf HandleEnterKey)
                    tasto = Keys.None
                Case Keys.Escape, Keys.Left, Keys.Back
                    EseguiSuUI(AddressOf abbandonamenu)
                    tasto = Keys.None
            End Select
        Catch ex As Exception
            Log.ScriviLog($"Errore in _tastiera_KeyDown: {ex.Message}")
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto(sender As Object, e As EventArgs)
        Try
            If Not PuoRicevere() Then Return
            EseguiSuUI(AddressOf apriOpzioni)
        Catch ex As Exception
            Log.ScriviLog($"Errore in _tastiera_AltPremuto: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_TapDoppio()
        Try
            If Not PuoRicevere() Then Return
            EseguiSuUI(AddressOf HandleEnterKey)
        Catch ex As Exception
            Log.ScriviLog($"Errore in _touk_TapDoppio: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_EscapeGesto()
        Try
            If Not PuoRicevere() Then Return
            EseguiSuUI(AddressOf abbandonamenu)
        Catch ex As Exception
            Log.ScriviLog($"Errore in _touk_EscapeGesto: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_FlickSu()
        Try
            If Not PuoRicevere() Then Return
            EseguiSuUI(AddressOf AggiornaAzioniTouch)
            Return
        Catch ex As Exception
            Log.ScriviLog($"Errore in _touk_FlickSu: {ex.Message}")
        End Try
    End Sub

    ''' <summary>
    ''' Travasa i nomi delle opzioni nella lista, che li fa scorrere con il flick verticale
    ''' annunciandoli, senza aprire il controllo opzioni. L'elenco viene aggiornato a ogni gesto
    ''' perche' le opzioni possono cambiare da una voce all'altra.
    ''' </summary>
    Private Sub AggiornaAzioniTouch()
        Try
            If _disposed OrElse Me.IsDisposed OrElse Me.Disposing Then Return
            If Menuvoci Is Nothing OrElse Menuvoci.IsDisposed Then Return

            Dim nomi As New List(Of String)()
            If _opzioniDefinite IsNot Nothing Then
                For Each v As Voce In _opzioniDefinite
                    If v IsNot Nothing AndAlso Not String.IsNullOrEmpty(v.Nome) Then
                        If Not nomi.Contains(v.Nome) Then nomi.Add(v.Nome)
                    End If
                Next
            End If

            Menuvoci.AzioniDisponibili = nomi
        Catch ex As Exception
            Log.ScriviLog($"Errore in AggiornaAzioniTouch: {ex.Message}")
        End Try
    End Sub

    Private Sub Menuvoci_AzioneAttivata(nomeAzione As String) Handles Menuvoci.AzioneAttivata
        Try
            If String.IsNullOrEmpty(nomeAzione) Then Return
            If _opzioniDefinite Is Nothing Then Return

            Dim azione As EventHandler = Nothing
            For Each v As Voce In _opzioniDefinite
                If v IsNot Nothing AndAlso v.Nome = nomeAzione Then
                    azione = v.Evento
                    Exit For
                End If
            Next

            If azione Is Nothing Then Return
            azione.Invoke(Me, EventArgs.Empty)
        Catch ex As Exception
            Log.ScriviLog($"Errore in Menuvoci_AzioneAttivata: {ex.Message}")
        End Try
    End Sub

    Private Sub apriOpzioni()
        Try
            If _disposed OrElse Me.IsDisposed OrElse Me.Disposing Then Return
            If _controlloOpzioni IsNot Nothing AndAlso Not _controlloOpzioni.IsDisposed Then Return

            Dim percorsoScrivania As List(Of String) = PercorsoVoceSelezionata()
            If (_opzioniDefinite Is Nothing OrElse _opzioniDefinite.Count = 0) AndAlso percorsoScrivania Is Nothing Then
                ' ShowDialog apre un ciclo di messaggi annidato durante il quale il menu resta in
                ' cima e continua a ricevere Alt: senza guardia si impilerebbero decine di dialoghi.
                If _dialogoOpzioniAperto Then Return
                _dialogoOpzioniAperto = True
                Try
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Non ci sono opzioni disponibili")
                    messaggio.ShowDialog()
                Finally
                    _dialogoOpzioniAperto = False
                End Try
                Return
            End If

            _ultimoElemento = Menuvoci.SelectedIndex

            Dim vociOpzioni As New List(Of String)()
            For Each v As Voce In _opzioniDefinite
                If v IsNot Nothing AndAlso Not String.IsNullOrEmpty(v.Nome) Then
                    If Not vociOpzioni.Contains(v.Nome) Then
                        vociOpzioni.Add(v.Nome)
                    End If
                End If
            Next
            If percorsoScrivania IsNot Nothing AndAlso Not vociOpzioni.Contains(VoceAggiungiAllaScrivania) Then vociOpzioni.Add(VoceAggiungiAllaScrivania)
            If percorsoScrivania IsNot Nothing AndAlso WGModalita.Smart AndAlso SchermoTouchPresente() AndAlso Not vociOpzioni.Contains(VoceAggiungiAllaHome) Then vociOpzioni.Add(VoceAggiungiAllaHome)
            _percorsoPerScrivania = percorsoScrivania

            Dim titoloOpzioni As String = "Opzioni " & titolo

            _controlloOpzioni = New WGControlloOpzioni(vociOpzioni, titoloOpzioni)

            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then Return
            c.MostraControllo(_controlloOpzioni)
        Catch ex As Exception
            ScriviLogErrore("apriOpzioni", ex)
        End Try
    End Sub

    Private _percorsoPerScrivania As List(Of String)

    Private Function SchermoTouchPresente() As Boolean
        Try
            Dim touk As WGTouk = WGTouk.GetInstance()
            If touk Is Nothing Then Return False
            Return touk.TouchDisponibile
        Catch ex As Exception
            ScriviLogErrore("SchermoTouchPresente", ex)
            Return False
        End Try
    End Function

    Private Sub _controlloOpzioni_OpzioneConfermata(testoVoce As String) Handles _controlloOpzioni.OpzioneConfermata
        Try
            Dim azione As EventHandler = Nothing
            Dim percorso As List(Of String) = _percorsoPerScrivania
            _percorsoPerScrivania = Nothing
            If testoVoce = VoceAggiungiAllaScrivania AndAlso percorso IsNot Nothing AndAlso Not _opzioniDefinite.Any(Function(v) v IsNot Nothing AndAlso v.Nome = testoVoce) Then
                chiudiControlloOpzioni()
                WGScrivania.AggiungiVoceDiMenu(percorso)
                Return
            End If
            If testoVoce = VoceAggiungiAllaHome AndAlso percorso IsNot Nothing AndAlso Not _opzioniDefinite.Any(Function(v) v IsNot Nothing AndAlso v.Nome = testoVoce) Then
                chiudiControlloOpzioni()
                WGHome.AggiungiVoceDiMenu(percorso)
                Return
            End If
            If Not String.IsNullOrEmpty(testoVoce) Then
                For Each v As Voce In _opzioniDefinite
                    If v IsNot Nothing AndAlso v.Nome = testoVoce Then
                        azione = v.Evento
                        Exit For
                    End If
                Next
            End If

            chiudiControlloOpzioni()

            If azione IsNot Nothing Then
                EsegueOpzione = True
                Try
                    azione.Invoke(Me, EventArgs.Empty)
                Finally
                    EsegueOpzione = False
                End Try
            End If
        Catch ex As Exception
            ScriviLogErrore("_controlloOpzioni_OpzioneConfermata", ex)
        End Try
    End Sub

    Private Sub _controlloOpzioni_OpzioniAnnullate() Handles _controlloOpzioni.OpzioniAnnullate
        Try
            chiudiControlloOpzioni()
        Catch ex As Exception
            ScriviLogErrore("_controlloOpzioni_OpzioniAnnullate", ex)
        End Try
    End Sub

    Private Sub chiudiControlloOpzioni()
        Try
            If _controlloOpzioni Is Nothing Then Return
            Dim daChiudere As WGControlloOpzioni = _controlloOpzioni
            _controlloOpzioni = Nothing

            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso Not daChiudere.IsDisposed Then
                c.RimuoviControllo(daChiudere)
            End If
            If Not daChiudere.IsDisposed Then
                daChiudere.Dispose()
            End If
        Catch ex As Exception
            ScriviLogErrore("chiudiControlloOpzioni", ex)
        End Try
    End Sub

    Private Sub HandleEnterKey()
        Try
            If _disposed OrElse Me.IsDisposed OrElse Me.Disposing Then Return
            If Menuvoci Is Nothing Then Return

            If Menuvoci.SelectedItem IsNot Nothing Then
                Dim testoVoce As String = Menuvoci.SelectedItem.ToString()
                MenuStateManager.SalvaStato(Me.titolo, Menuvoci.SelectedIndex)

                ' Dal menu iniziale, una voce la cui applicazione è già aperta riporta a quella.
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso contenitore.EMenuIniziale(Me) AndAlso contenitore.TornaAllApplicazioneAperta(testoVoce) Then Return

                If _eventi.ContainsKey(testoVoce) Then
                    Dim azioneDaEseguire As EventHandler = _eventi(testoVoce)
                    If azioneDaEseguire IsNot Nothing Then
                        ' Se l'azione apre un dialogo o pompa i messaggi, gli eventi tastiera gia'
                        ' accodati rientrerebbero qui ed eseguirebbero la stessa voce due volte.
                        If _azioneInCorso Then Return
                        _azioneInCorso = True
                        Try
                            azioneDaEseguire.Invoke(Nothing, EventArgs.Empty)
                        Finally
                            _azioneInCorso = False
                        End Try

                        RaiseEvent eventoattivato(Me, Nothing)

                        If sincrono = True Then
                            abbandona()
                        End If
                    End If
                End If
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("HandleEnterKey", ex)
            errore = ($"Errore in HandleEnterKey: {ex.Message}")
        End Try
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e)
        Try
            If Not Me.IsDisposed AndAlso Not Me.Disposing Then
                If Me.Visible Then
                    If _primaApertura Then
                        _primaApertura = False
                    Else
                        RaiseEvent MenuRiattivato(Me, EventArgs.Empty)
                    End If
                End If
            End If
        Catch ex As Exception
            ScriviLogErrore("OnVisibleChanged", ex)
        End Try
    End Sub

    Public Sub avvia(MenuTitolo As String, Optional sincronomenu As Boolean = False)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then
                Return
            End If

            If c.InvokeRequired Then
                c.Invoke(New DelegatoAvvia(AddressOf avvia), MenuTitolo, sincronomenu)
                Return
            End If

            If Me.IsDisposed = True OrElse Me.Disposing = True Then Return

            titolo = MenuTitolo
            sincrono = sincronomenu

            SetTitolo(MenuTitolo)

            c.MostraControllo(Me)

            caricaStatoIniziale()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("avvia", ex)
        End Try
    End Sub

    Private Sub caricaStatoIniziale()
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Menuvoci Is Nothing Then Return

            Menuvoci.Titolo = titolo

            If Menuvoci.Items.Count > 0 Then
                If _indicePosizionamentoIniziale >= 0 AndAlso _indicePosizionamentoIniziale < Menuvoci.Items.Count Then
                    Menuvoci.SelectedIndex = _indicePosizionamentoIniziale
                Else
                    Dim lastIndex As Integer = MenuStateManager.OttieniUltimoIndice(titolo)
                    If lastIndex >= 0 AndAlso lastIndex < Menuvoci.Items.Count Then
                        Menuvoci.SelectedIndex = lastIndex
                    Else
                        Menuvoci.SelectedIndex = 0
                    End If
                End If
            End If

            If lettura IsNot Nothing Then
                If lettura.NvdaInEsecuzione() = True Then
                    Menuvoci.AccessibleName = titolo
                Else
                    Menuvoci.AccessibleRole = AccessibleRole.None
                End If
            End If

            Menuvoci.Focus()

            Try
                Dim touk As WGTouk = WGTouk.GetInstance()
                If touk IsNot Nothing AndAlso Menuvoci.IsHandleCreated Then
                    touk.RiassegnaControllo(Menuvoci.Handle)
                End If
            Catch exTouk As Exception
                Log.ScriviLog($"caricaStatoIniziale (RiassegnaControllo): {exTouk.Message}")
            End Try
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("caricaStatoIniziale", ex)
        End Try
    End Sub

    Private _indicePosizionamentoIniziale As Integer = -1

    Public Sub posiziona(indice As Integer)
        Try
            If indice < 0 Then Return
            _indicePosizionamentoIniziale = indice
            If Me.IsHandleCreated AndAlso Menuvoci IsNot Nothing AndAlso Not Menuvoci.IsDisposed Then
                If Me.InvokeRequired Then
                    Me.BeginInvoke(New Action(Of Integer)(AddressOf ApplicaPosizionamento), indice)
                Else
                    ApplicaPosizionamento(indice)
                End If
            End If
        Catch ex As Exception
            ScriviLogErrore("posiziona", ex)
        End Try
    End Sub

    Private Sub ApplicaPosizionamento(indice As Integer)
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return
            If Menuvoci Is Nothing OrElse Menuvoci.IsDisposed Then Return
            If indice >= 0 AndAlso indice < Menuvoci.Items.Count Then
                Menuvoci.SelectedIndex = indice
            End If
        Catch ex As Exception
            ScriviLogErrore("ApplicaPosizionamento", ex)
        End Try
    End Sub

    Public Sub SetTitolo(ByVal newTitolo As String)
        Try
            Me.titolo = newTitolo
            If Menuvoci IsNot Nothing Then
                Menuvoci.Titolo = newTitolo
            End If
        Catch ex As Exception
            ScriviLogErrore("SetTitolo", ex)
        End Try
    End Sub

    Public Sub aggiungi(testoVoce As String, evento As EventHandler)
        Try
            ' Senza contenitore non si puo' proseguire: l'handle della lista verrebbe creato sul
            ' thread chiamante e da li' in poi il menu non risponderebbe piu'.
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then
                Return
            End If
            If c.InvokeRequired Then
                c.Invoke(New DelegatoStringaEvento(AddressOf aggiungi), testoVoce, evento)
                Return
            End If

            If Me.IsDisposed = True OrElse Me.Disposing = True Then Return
            If String.IsNullOrEmpty(testoVoce) Then Return

            If Not _voci.Contains(testoVoce) Then
                _voci.Add(testoVoce)
                _eventi(testoVoce) = evento
                If Menuvoci IsNot Nothing AndAlso Not Menuvoci.Items.Contains(testoVoce) Then
                    Menuvoci.Items.Add(testoVoce)
                End If
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("aggiungi", ex)
        End Try
    End Sub

    Public Sub AggiungiDopo(voceRiferimento As String, nuovaVoce As String, evento As EventHandler)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then
                Return
            End If
            If c.InvokeRequired Then
                c.Invoke(New DelegatoAggiungiDopo(AddressOf AggiungiDopo), voceRiferimento, nuovaVoce, evento)
                Return
            End If

            If Me.IsDisposed = True OrElse Me.Disposing = True Then Return
            If String.IsNullOrEmpty(nuovaVoce) Then Return
            If _voci.Contains(nuovaVoce) Then Return

            Dim index As Integer = _voci.IndexOf(voceRiferimento)
            If index >= 0 Then
                _voci.Insert(index + 1, nuovaVoce)
            Else
                _voci.Add(nuovaVoce)
            End If
            _eventi(nuovaVoce) = evento

            If Menuvoci IsNot Nothing AndAlso Not Menuvoci.Items.Contains(nuovaVoce) Then
                Dim indexLista As Integer = Menuvoci.Items.IndexOf(voceRiferimento)
                If indexLista >= 0 Then
                    Menuvoci.Items.Insert(indexLista + 1, nuovaVoce)
                Else
                    Menuvoci.Items.Add(nuovaVoce)
                End If
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("AggiungiDopo", ex)
        End Try
    End Sub

    Public Sub aggiungiOpzione(testoVoce As String, evento As EventHandler)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then
                Return
            End If
            If c.InvokeRequired Then
                c.Invoke(New DelegatoStringaEvento(AddressOf aggiungiOpzione), testoVoce, evento)
                Return
            End If
            If String.IsNullOrEmpty(testoVoce) Then Return
            If _opzioniDefinite Is Nothing Then Return

            Dim esiste As Boolean = False
            For Each v As Voce In _opzioniDefinite
                If v IsNot Nothing AndAlso v.Nome = testoVoce Then
                    esiste = True
                    Exit For
                End If
            Next
            If Not esiste Then
                _opzioniDefinite.Add(New Voce With {.Nome = testoVoce, .Evento = evento})
                AggiornaAzioniTouch()
            End If
        Catch ex As Exception
            ScriviLogErrore("aggiungiOpzione", ex)
        End Try
    End Sub

    Public Sub RimuoviOpzione(testoOpzione As String)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then Return
            If c.InvokeRequired Then
                c.Invoke(New DelegatoStringa(AddressOf RimuoviOpzione), testoOpzione)
                Return
            End If
            If String.IsNullOrEmpty(testoOpzione) OrElse _opzioniDefinite Is Nothing Then Return
            If _opzioniDefinite.RemoveAll(Function(v) v IsNot Nothing AndAlso v.Nome = testoOpzione) > 0 Then AggiornaAzioniTouch()
        Catch ex As Exception
            ScriviLogErrore("RimuoviOpzione", ex)
        End Try
    End Sub

    Public Sub RimuoviVoce(nomeVoce As String)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then
                Return
            End If
            If c.InvokeRequired Then
                c.Invoke(New DelegatoStringa(AddressOf RimuoviVoce), nomeVoce)
                Return
            End If

            If Me.IsDisposed = True OrElse Me.Disposing = True Then Return
            If String.IsNullOrEmpty(nomeVoce) Then Return

            If _voci.Contains(nomeVoce) Then
                _voci.Remove(nomeVoce)
                If _eventi.ContainsKey(nomeVoce) Then
                    _eventi.Remove(nomeVoce)
                End If

                If Menuvoci IsNot Nothing AndAlso Menuvoci.Items.Contains(nomeVoce) Then
                    Menuvoci.Items.Remove(nomeVoce)
                    If Menuvoci.Items.Count > 0 Then
                        If Menuvoci.SelectedIndex < 0 OrElse Menuvoci.SelectedIndex >= Menuvoci.Items.Count Then
                            Menuvoci.SelectedIndex = 0
                            _ultimoElemento = 0
                        End If
                    Else
                        _ultimoElemento = -1
                    End If
                End If
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("RimuoviVoce", ex)
        End Try
    End Sub

    Public Sub RinominaVoce(vecchioNome As String, nuovoNome As String)
        Try
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then
                Return
            End If
            If c.InvokeRequired Then
                c.Invoke(New DelegatoDueStringhe(AddressOf RinominaVoce), vecchioNome, nuovoNome)
                Return
            End If

            If Me.IsDisposed = True OrElse Me.Disposing = True Then Return
            If String.IsNullOrEmpty(vecchioNome) OrElse String.IsNullOrEmpty(nuovoNome) Then Return
            If String.Equals(vecchioNome, nuovoNome, StringComparison.Ordinal) Then Return
            If Not _voci.Contains(vecchioNome) OrElse _voci.Contains(nuovoNome) Then Return

            Dim indiceVoce As Integer = _voci.IndexOf(vecchioNome)
            _voci(indiceVoce) = nuovoNome
            If _eventi.ContainsKey(vecchioNome) Then
                Dim evento As EventHandler = _eventi(vecchioNome)
                _eventi.Remove(vecchioNome)
                _eventi(nuovoNome) = evento
            End If

            If Menuvoci IsNot Nothing AndAlso Not Menuvoci.IsDisposed Then
                Dim indiceLista As Integer = Menuvoci.Items.IndexOf(vecchioNome)
                If indiceLista >= 0 Then
                    Dim selezionato As Integer = Menuvoci.SelectedIndex
                    Menuvoci.Items.Remove(vecchioNome)
                    Menuvoci.Items.Insert(indiceLista, nuovoNome)
                    If selezionato >= 0 AndAlso selezionato < Menuvoci.Items.Count Then
                        Menuvoci.SelectedIndex = selezionato
                    End If
                End If
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("RinominaVoce", ex)
        End Try
    End Sub

    Private Sub Svuota()
        Try
            _voci.Clear()
            _eventi.Clear()
            _ultimoElemento = 0
            If Menuvoci IsNot Nothing AndAlso Not Menuvoci.IsDisposed Then
                Menuvoci.Items.Clear()
            End If
        Catch ex As Exception
            ScriviLogErrore("Svuota", ex)
        End Try
    End Sub

    Private Sub Menuvoci_SelectedIndexChanged(sender As Object, e As EventArgs)
        Try
            If Menuvoci Is Nothing Then Return
            If Menuvoci.SelectedItem IsNot Nothing Then
                _ultimoElemento = Menuvoci.SelectedIndex
                MenuStateManager.SalvaStato(Me.titolo, _ultimoElemento)
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("Menuvoci_SelectedIndexChanged", ex)
        End Try
    End Sub

    Private Sub abbandonamenu()
        Try
            If _disposed OrElse Me.IsDisposed = True OrElse Me.Disposing = True Then Return
            If AzioneEsc IsNot Nothing AndAlso AzioneEsc.Invoke() Then Return

            If utiliti Is Nothing Then
                utiliti = WGFormUtils.GetInstance()
            End If

            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso c.ContaControlli() <= 1 Then
                If utiliti IsNot Nothing Then
                    utiliti.Chiudi()
                End If
            End If

            abbandona()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("abbandonamenu", ex)
        End Try
    End Sub

    Public Sub abbandona()
        Try
            chiudiControlloOpzioni()

            Dim c As WGContenitore = WGContenitore.Istanza
            If c IsNot Nothing AndAlso c.GestisceControllo(Me) Then
                c.RimuoviControllo(Me)
            End If

            If _eventoAbbandonatoEmesso = False Then
                _eventoAbbandonatoEmesso = True
                RaiseEvent abbandonato(Me, Nothing)
            End If

            Me.Dispose()
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("abbandona", ex)
        End Try
    End Sub

    Public Sub chiudi()
        abbandona()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso Not _disposed Then
                _disposed = True

                If _eventoAbbandonatoEmesso = False Then
                    _eventoAbbandonatoEmesso = True
                    RaiseEvent abbandonato(Me, Nothing)
                End If

                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                    RemoveHandler _tastiera.AltPremuto, AddressOf _tastiera_AltPremuto
                    _tastiera = Nothing
                End If

                If _touk IsNot Nothing Then
                    RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    RemoveHandler _touk.FlickSu, AddressOf _touk_FlickSu
                    _touk = Nothing
                End If

                If _controlloOpzioni IsNot Nothing AndAlso Not _controlloOpzioni.IsDisposed Then
                    _controlloOpzioni.Dispose()
                End If
                _controlloOpzioni = Nothing

                If Menuvoci IsNot Nothing Then
                    RemoveHandler Menuvoci.SelectedIndexChanged, AddressOf Menuvoci_SelectedIndexChanged
                    Menuvoci.Dispose()
                    Menuvoci = Nothing
                End If
            End If
        Catch ex As Exception
            ScriviLogErrore("Dispose", ex)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub
End Class

Friend Class WGControlloOpzioni
    Inherits System.Windows.Forms.UserControl

    Private Delegate Sub DelegatoVuoto()

    Private Log As Logger = New Logger()
    Private _disposed As Boolean = False

    Friend WithEvents listaOpzioni As New WGListaView()

    Private _tastiera As WGTastiera
    Private _touk As WGTouk

    Public Event OpzioneConfermata(testoVoce As String)
    Public Event OpzioniAnnullate()

    Private Sub ScriviLogErrore(methodName As String, ex As Exception)
        Try
            Log.ScriviLog($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}{Environment.NewLine}Errore in {methodName} ({ex.GetType().Name}).{Environment.NewLine}Dettaglio: {ex.Message} {ex.StackTrace}")
        Catch exLog As Exception
        End Try
    End Sub

    Public Sub New(voci As List(Of String), titoloOpzioni As String)
        Try
            _tastiera = WGTastiera.GetInstance()
            If _tastiera IsNot Nothing Then
                AddHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
            End If

            _touk = WGTouk.GetInstance()
            If _touk IsNot Nothing Then
                AddHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                AddHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
            End If

            listaOpzioni.Dock = DockStyle.Fill
            listaOpzioni.SelezioneConsentita = False
            Me.Controls.Add(listaOpzioni)

            Me.Dock = DockStyle.Fill
            Me.AccessibleRole = AccessibleRole.Client
            Me.AccessibleName = " "

            listaOpzioni.Titolo = titoloOpzioni
            If voci IsNot Nothing Then
                For Each v As String In voci
                    If Not String.IsNullOrEmpty(v) Then
                        listaOpzioni.Items.Add(v)
                    End If
                Next
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As Exception
            ScriviLogErrore("New", ex)
        End Try
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        Try
            If listaOpzioni IsNot Nothing AndAlso listaOpzioni.Items.Count > 0 Then
                listaOpzioni.SelectedIndex = 0
            End If
            If listaOpzioni IsNot Nothing Then
                listaOpzioni.Focus()
                Try
                    Dim touk As WGTouk = WGTouk.GetInstance()
                    If touk IsNot Nothing AndAlso listaOpzioni.IsHandleCreated Then
                        touk.RiassegnaControllo(listaOpzioni.Handle)
                    End If
                Catch exTouk As Exception
                    Log.ScriviLog($"OnHandleCreated (RiassegnaControllo): {exTouk.Message}")
                End Try
            End If
        Catch ex As Exception
            ScriviLogErrore("OnHandleCreated", ex)
        End Try
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If _disposed OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return False
            If Not Me.Visible Then Return False
            Dim c As WGContenitore = WGContenitore.Istanza
            If c Is Nothing Then Return False
            Return c.PuoRicevereInput(Me)
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub EseguiSuUI(azione As MethodInvoker)
        Try
            If azione Is Nothing Then Return
            If _disposed OrElse Me.IsDisposed OrElse Not Me.IsHandleCreated Then Return
            If Me.InvokeRequired Then
                Me.BeginInvoke(azione)
            Else
                azione.Invoke()
            End If
        Catch ex As ObjectDisposedException
            Return
        Catch ex As InvalidOperationException
            Return
        Catch ex As Exception
            ScriviLogErrore("EseguiSuUI", ex)
        End Try
    End Sub

    Private Sub _tastiera_KeyDown(ByRef tasto As Keys)
        Try
            If Not PuoRicevere() Then Return
            If tasto = Keys.None Then Return

            Dim codice As Keys = tasto And Keys.KeyCode

            Select Case codice
                Case Keys.Enter, Keys.Return, Keys.Right
                    EseguiSuUI(AddressOf ConfermaSelezione)
                    tasto = Keys.None
                Case Keys.Escape, Keys.Left, Keys.Back
                    EseguiSuUI(AddressOf AnnullaSelezione)
                    tasto = Keys.None
            End Select
        Catch ex As Exception
            Log.ScriviLog($"Errore in WGControlloOpzioni._tastiera_KeyDown: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_TapDoppio()
        Try
            If Not PuoRicevere() Then Return
            EseguiSuUI(AddressOf ConfermaSelezione)
        Catch ex As Exception
            Log.ScriviLog($"Errore in WGControlloOpzioni._touk_TapDoppio: {ex.Message}")
        End Try
    End Sub

    Private Sub _touk_EscapeGesto()
        Try
            If Not PuoRicevere() Then Return
            EseguiSuUI(AddressOf AnnullaSelezione)
        Catch ex As Exception
            Log.ScriviLog($"Errore in WGControlloOpzioni._touk_EscapeGesto: {ex.Message}")
        End Try
    End Sub

    Private Sub ConfermaSelezione()
        Try
            If _disposed OrElse Me.IsDisposed OrElse Me.Disposing Then Return
            Dim testoVoce As String = Nothing
            If listaOpzioni IsNot Nothing AndAlso listaOpzioni.SelectedItem IsNot Nothing Then
                testoVoce = listaOpzioni.SelectedItem.ToString()
            End If
            RaiseEvent OpzioneConfermata(testoVoce)
        Catch ex As Exception
            ScriviLogErrore("ConfermaSelezione", ex)
        End Try
    End Sub

    Private Sub AnnullaSelezione()
        Try
            If _disposed OrElse Me.IsDisposed OrElse Me.Disposing Then Return
            RaiseEvent OpzioniAnnullate()
        Catch ex As Exception
            ScriviLogErrore("AnnullaSelezione", ex)
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso Not _disposed Then
                _disposed = True

                If _tastiera IsNot Nothing Then
                    RemoveHandler _tastiera.KeyDown, AddressOf _tastiera_KeyDown
                    _tastiera = Nothing
                End If

                If _touk IsNot Nothing Then
                    RemoveHandler _touk.TapDoppio, AddressOf _touk_TapDoppio
                    RemoveHandler _touk.EscapeGesto, AddressOf _touk_EscapeGesto
                    _touk = Nothing
                End If

                If listaOpzioni IsNot Nothing Then
                    listaOpzioni.Dispose()
                    listaOpzioni = Nothing
                End If
            End If
        Catch ex As Exception
            ScriviLogErrore("Dispose", ex)
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub
End Class