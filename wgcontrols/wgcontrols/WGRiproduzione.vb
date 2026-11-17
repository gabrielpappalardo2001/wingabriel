' ============================================================================================
' WGRiproduzione - coordina i lettori: musica più bassa mentre parla la sintesi, un solo file alla volta
' ============================================================================================
'
' Abbassamento (ducking)
'   Quando la sintesi vocale comincia a parlare il volume dei lettori scende alla percentuale scelta
'   nelle impostazioni (predefinita 35%), e torna com'era poco dopo la fine della lettura: la pausa
'   evita che la musica salga e scenda a ogni parola. Il volume scelto dall'utente non cambia.
'   Il lettore mpv è gestito qui direttamente; gli altri (il lettore di YouTube nel browser) si
'   registrano con RegistraLettore.
'
' Un solo file alla volta (solo in modalità smart, come l'abbassamento)
'   Prima di far partire qualcosa, un lettore chiama ChiediSeInterrompere: se c'è già una
'   riproduzione in corso compare il menu "Riproduci dopo" / "Interrompi la riproduzione in corso".
'   Con "Riproduci dopo" l'avvio va in coda e parte quando il lettore attuale chiama SegnalaFine
'   (fine della riproduzione o chiusura del lettore).
'
'   WGRiproduzione.Avvia()                               ' una volta, all'avvio di WinGabriel
'   WGRiproduzione.ChiediSeInterrompere(Sub() ..., "Brano.mp3")
'   WGRiproduzione.SegnalaFine()
' ============================================================================================
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms

Public NotInheritable Class WGRiproduzione

    ''' <summary>Un lettore diverso da mpv: dice se sta suonando e sa abbassare il proprio volume.</summary>
    Public Class Lettore
        Public StaSuonando As Func(Of Boolean)
        Public Attenua As Action(Of Double)
        ''' <summary>Ferma il lettore quando l'ascolto va interrotto (per esempio dal timer di interruzione dell'ascolto).</summary>
        Public Interrompi As Action
    End Class

    Public Const IdConfigurazioneVolumeDuranteSintesi As String = "volume_musica_durante_sintesi"
    Private Const PercentualePredefinita As Integer = 35
    Private Const AttesaRipristinoMs As Integer = 700

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _lettori As New List(Of Lettore)()
    Private Shared ReadOnly _interruttori As New List(Of Action)()
    Private Shared ReadOnly _coda As New Queue(Of Action)()
    Private Shared _avviato As Boolean
    Private Shared _attenuato As Boolean
    Private Shared WithEvents _timerRipristino As Timer
    Private Shared _percentuale As Integer = -1

    Private Sub New()
    End Sub

#Region "Abbassamento durante la sintesi"

    ''' <summary>Da chiamare una volta sul thread dell'interfaccia.</summary>
    Public Shared Sub Avvia()
        Try
            If _avviato Then Return
            _avviato = True
            _timerRipristino = New Timer() With {.Interval = AttesaRipristinoMs}
            AddHandler sintesivocale.GetInstance().OnStatoParlatoCambiato, AddressOf StatoParlatoCambiato
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.Avvia: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Volume della musica mentre parla la sintesi, in percentuale; 100 vuol dire non abbassare.</summary>
    Public Shared Property PercentualeDuranteSintesi As Integer
        Get
            If _percentuale >= 0 Then Return _percentuale
            Try
                Dim config As New WGConfig()
                Dim valore As Integer
                If config.EsisteId(IdConfigurazioneVolumeDuranteSintesi) AndAlso Integer.TryParse(config.LeggiValore(IdConfigurazioneVolumeDuranteSintesi), valore) Then
                    _percentuale = Math.Max(0, Math.Min(100, valore))
                Else
                    _percentuale = PercentualePredefinita
                End If
            Catch ex As Exception
                log.ScriviLog("WGRiproduzione.PercentualeDuranteSintesi: " & ex.Message)
                _percentuale = PercentualePredefinita
            End Try
            Return _percentuale
        End Get
        Set(value As Integer)
            Try
                _percentuale = Math.Max(0, Math.Min(100, value))
                Dim config As New WGConfig()
                config.AggiungiValore(IdConfigurazioneVolumeDuranteSintesi, _percentuale.ToString())
            Catch ex As Exception
                log.ScriviLog("WGRiproduzione.PercentualeDuranteSintesi Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Shared Sub RegistraLettore(lettore As Lettore)
        If lettore Is Nothing Then Return
        SyncLock _lettori
            If Not _lettori.Contains(lettore) Then _lettori.Add(lettore)
        End SyncLock
        If _attenuato AndAlso lettore.Attenua IsNot Nothing Then lettore.Attenua(PercentualeDuranteSintesi / 100.0)
    End Sub

    Public Shared Sub RimuoviLettore(lettore As Lettore)
        If lettore Is Nothing Then Return
        SyncLock _lettori
            _lettori.Remove(lettore)
        End SyncLock
    End Sub

    Private Shared Function Lettori() As Lettore()
        SyncLock _lettori
            Return _lettori.ToArray()
        End SyncLock
    End Function

    Private Shared Sub StatoParlatoCambiato(staParlando As Boolean)
        Try
            ' Novità della modalità smart: nella classica il volume non cambia.
            If Not WGModalita.Smart Then Return
            If staParlando Then
                _timerRipristino.Stop()
                If PercentualeDuranteSintesi >= 100 Then Return
                If Not _attenuato Then Applica(PercentualeDuranteSintesi / 100.0)
            Else
                ' Si aspetta un attimo prima di rialzare: tra una lettura e l'altra la musica non deve saltare su.
                _timerRipristino.Stop()
                _timerRipristino.Start()
            End If
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.StatoParlatoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub _timerRipristino_Tick(sender As Object, e As EventArgs) Handles _timerRipristino.Tick
        _timerRipristino.Stop()
        If _attenuato Then Applica(1.0)
    End Sub

    Private Shared Sub Applica(fattore As Double)
        _attenuato = fattore < 1
        Try
            Dim mpv As WGMpv = WGMpv.IstanzaEsistente
            If mpv IsNot Nothing Then mpv.Attenuazione = fattore
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.Applica mpv: " & ex.Message)
        End Try
        For Each l As Lettore In Lettori()
            Try
                If l.Attenua IsNot Nothing Then l.Attenua(fattore)
            Catch ex As Exception
                log.ScriviLog("WGRiproduzione.Applica lettore: " & ex.Message)
            End Try
        Next
    End Sub

#End Region

#Region "Interruzione dell'ascolto"

    ''' <summary>
    ''' Registra qualcosa da fare quando l'ascolto va interrotto: il lettore in primo piano si chiude da solo, senza chiedere
    ''' se continuare in sottofondo.
    ''' </summary>
    Public Shared Sub RegistraInterruttore(azione As Action)
        If azione Is Nothing Then Return
        SyncLock _interruttori
            If Not _interruttori.Contains(azione) Then _interruttori.Add(azione)
        End SyncLock
    End Sub

    Private Shared _timerInterruzione As System.Threading.Timer
    Private Shared _scadenzaInterruzione As DateTime

    ''' <summary>Vero se è programmata l'interruzione dell'ascolto fra un certo tempo.</summary>
    Public Shared ReadOnly Property InterruzioneProgrammata As Boolean
        Get
            Return _timerInterruzione IsNot Nothing
        End Get
    End Property

    ''' <summary>Quanto manca all'interruzione programmata dell'ascolto; zero se non c'è.</summary>
    Public Shared Function InterruzioneRimanente() As TimeSpan
        If _timerInterruzione Is Nothing Then Return TimeSpan.Zero
        Dim r As TimeSpan = _scadenzaInterruzione - DateTime.Now
        Return If(r < TimeSpan.Zero, TimeSpan.Zero, r)
    End Function

    ''' <summary>Programma l'interruzione dell'ascolto fra la durata data (per addormentarsi con la musica). Sostituisce la precedente.</summary>
    Public Shared Sub ProgrammaInterruzione(durata As TimeSpan)
        Try
            AnnullaInterruzione()
            _scadenzaInterruzione = DateTime.Now + durata
            _timerInterruzione = New System.Threading.Timer(
                Sub(o)
                    Try
                        AnnullaInterruzione()
                        Dim c As WGContenitore = WGContenitore.Istanza
                        If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.IsHandleCreated Then
                            c.BeginInvoke(New Action(AddressOf InterrompiAscolto))
                        Else
                            InterrompiAscolto()
                        End If
                    Catch ex As Exception
                        log.ScriviLog("WGRiproduzione.InterruzioneScaduta: " & ex.Message)
                    End Try
                End Sub, Nothing, durata, System.Threading.Timeout.InfiniteTimeSpan)
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.ProgrammaInterruzione: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub AnnullaInterruzione()
        Try
            Dim t As System.Threading.Timer = _timerInterruzione
            _timerInterruzione = Nothing
            If t IsNot Nothing Then t.Dispose()
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.AnnullaInterruzione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Ferma tutto quello che si sta ascoltando: il lettore in primo piano, l'ascolto in sottofondo, i video di YouTube e il motore
    ''' di riproduzione. Va chiamata sul thread dell'interfaccia.
    ''' </summary>
    Public Shared Sub InterrompiAscolto()
        Try
            Dim azioni As Action()
            SyncLock _interruttori
                azioni = _interruttori.ToArray()
            End SyncLock
            For Each a As Action In azioni
                Try
                    a()
                Catch ex As Exception
                    log.ScriviLog("WGRiproduzione.InterrompiAscolto interruttore: " & ex.Message)
                End Try
            Next
            For Each l As Lettore In Lettori()
                Try
                    If l.Interrompi IsNot Nothing Then l.Interrompi()
                Catch ex As Exception
                    log.ScriviLog("WGRiproduzione.InterrompiAscolto lettore: " & ex.Message)
                End Try
            Next
            Try
                Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
                If centro IsNot Nothing Then centro.TerminaAscoltoAudio()
            Catch ex As Exception
                log.ScriviLog("WGRiproduzione.InterrompiAscolto sottofondo: " & ex.Message)
            End Try
            Try
                Dim mpv As WGMpv = WGMpv.IstanzaEsistente
                If mpv IsNot Nothing Then mpv.StopPlayback()
            Catch ex As Exception
                log.ScriviLog("WGRiproduzione.InterrompiAscolto mpv: " & ex.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.InterrompiAscolto: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Un solo file alla volta"

    ''' <summary>Vero se un lettore sta suonando (non in pausa).</summary>
    Public Shared Function RiproduzioneInCorso() As Boolean
        Try
            ' Nella modalità classica un nuovo file sostituisce quello in corso, come sempre.
            If Not WGModalita.Smart Then Return False
            Dim mpv As WGMpv = WGMpv.IstanzaEsistente
            If mpv IsNot Nothing AndAlso mpv.MotoreAttivo AndAlso Not mpv.InPausa AndAlso mpv.PosizioneTemporale.TotalSeconds > 0 Then Return True
            For Each l As Lettore In Lettori()
                If l.StaSuonando IsNot Nothing AndAlso l.StaSuonando() Then Return True
            Next
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.RiproduzioneInCorso: " & ex.Message)
        End Try
        Return False
    End Function

    ''' <summary>
    ''' Fa partire subito avvio se non suona niente; altrimenti chiede se riprodurre dopo o interrompere.
    ''' Con Esc non succede niente.
    ''' </summary>
    Public Shared Sub ChiediSeInterrompere(avvio As Action, Optional nome As String = "")
        Try
            If avvio Is Nothing Then Return
            If Not RiproduzioneInCorso() Then
                avvio()
                Return
            End If
            Dim menu As New WGMenu()
            menu.aggiungi("Riproduci dopo", Sub(s, e)
                                                 SyncLock _coda
                                                     _coda.Enqueue(avvio)
                                                 End SyncLock
                                                 sintesivocale.GetInstance().LeggiTesto(If(String.IsNullOrWhiteSpace(nome), "Partirà", nome & " partirà") & " quando finisce la riproduzione in corso")
                                             End Sub)
            menu.aggiungi("Interrompi la riproduzione in corso", Sub(s, e)
                                                                     SyncLock _coda
                                                                         _coda.Clear()
                                                                     End SyncLock
                                                                     avvio()
                                                                 End Sub)
            menu.avvia("C'è già una riproduzione in corso" & If(String.IsNullOrWhiteSpace(nome), "", ": " & nome), True)
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.ChiediSeInterrompere: " & ex.Message)
            avvio()
        End Try
    End Sub

    ''' <summary>Il lettore ha finito (o è stato chiuso): parte il prossimo in coda, se c'è.</summary>
    Public Shared Sub SegnalaFine()
        Try
            Dim prossimo As Action = Nothing
            SyncLock _coda
                If _coda.Count > 0 Then prossimo = _coda.Dequeue()
            End SyncLock
            If prossimo Is Nothing Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            ' Si lascia finire la chiusura del lettore precedente prima di partire.
            If contenitore IsNot Nothing AndAlso contenitore.IsHandleCreated Then
                contenitore.BeginInvoke(prossimo)
            Else
                prossimo()
            End If
        Catch ex As Exception
            log.ScriviLog("WGRiproduzione.SegnalaFine: " & ex.Message)
        End Try
    End Sub

    Public Shared ReadOnly Property ElementiInCoda As Integer
        Get
            SyncLock _coda
                Return _coda.Count
            End SyncLock
        End Get
    End Property

#End Region

End Class
