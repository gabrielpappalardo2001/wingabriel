' ============================================================================================
' WGSessione - riaprire all'avvio le applicazioni lasciate aperte (modalità smart)
' ============================================================================================
'
' Ogni volta che un'applicazione si apre o si chiude, l'elenco di come riaprirle viene salvato in
' dati\gui\gui.sqlite, così vale anche dopo un blocco del programma:
'   - "menu:voce > voce" ripete le scelte nei menu, fino al menu più interno raggiunto;
'   - "scrivania:id" riapre l'elemento della scrivania;
'   - "percorso:C:\..." riapre il file o la cartella.
' All'uscita l'elenco viene congelato prima che le applicazioni si chiudano, e scongelato se l'uscita
' viene annullata. All'avvio, se l'impostazione è attiva, le applicazioni si riaprono una alla volta
' in sottofondo, senza venire davanti e senza parlare: davanti resta la scrivania (o la home), che si
' usa intanto normalmente, e alle applicazioni riaperte si va quando servono (F10, Control freccia).
'
'   WGSessione.Attiva = True
'   WGSessione.SalvaPresto()      ' dal contenitore, quando cambiano le applicazioni
'   WGSessione.Congela() / Scongela()
'   WGSessione.RiapriAsync()      ' all'avvio
'   WGSessione.RiapriDopo(nome)   ' all'avvio, se prima si è aperta un'applicazione (le notizie):
'                                 ' si riapre in silenzio quando quella si chiude
' ============================================================================================
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports WGDb

Public NotInheritable Class WGSessione

    Private Const IdConfigurazione As String = "riapri_sessione"
    Private Shared ReadOnly log As New Logger()
    Private Shared _congelata As Boolean
    Private Shared _riaperturaInCorso As Boolean
    ' Riapertura rimandata: finché non avviene l'elenco salvato non si tocca.
    Private Shared _rimandata As Boolean
    Private Shared WithEvents _timerRimando As Timer
    Private Shared _applicazioneDaAspettare As String = ""
    Private Shared _vistaAperta As Boolean
    Private Shared WithEvents _timerSalvataggio As Timer

    Private Sub New()
    End Sub

    ''' <summary>Riaprire all'avvio le applicazioni lasciate aperte (predefinito: no).</summary>
    Public Shared Property Attiva As Boolean
        Get
            Try
                Return WGModalita.Smart AndAlso New WGConfig().LeggiValore(IdConfigurazione) = "1"
            Catch ex As Exception
                log.ScriviLog("WGSessione.Attiva: " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                Dim config As New WGConfig()
                config.AggiungiValore(IdConfigurazione, If(value, "1", "0"))
            Catch ex As Exception
                log.ScriviLog("WGSessione.Attiva Set: " & ex.Message)
            End Try
        End Set
    End Property

    ' La sessione sta nel database dell'interfaccia (tabella Sessione, una riga per applicazione); il vecchio
    ' sessione.txt si importa la prima volta.
    Private Const TabellaSessione As String = "Sessione"

    Private Shared Sub PreparaArchivio()
        WGGui.PreparaElenco(TabellaSessione, "Voce", "sistema", "sessione.txt")
    End Sub

    ''' <summary>Salva poco dopo (le aperture e chiusure ravvicinate si salvano una volta sola).</summary>
    Friend Shared Sub SalvaPresto()
        Try
            If _congelata OrElse _riaperturaInCorso OrElse _rimandata OrElse Not WGModalita.Smart Then Return
            If _timerSalvataggio Is Nothing Then _timerSalvataggio = New Timer() With {.Interval = 1500}
            _timerSalvataggio.Stop()
            _timerSalvataggio.Start()
        Catch ex As Exception
            log.ScriviLog("WGSessione.SalvaPresto: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub _timerSalvataggio_Tick(sender As Object, e As EventArgs) Handles _timerSalvataggio.Tick
        _timerSalvataggio.Stop()
        Salva()
    End Sub

    Private Shared Sub Salva()
        Try
            If _congelata OrElse _riaperturaInCorso OrElse _rimandata Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return
            PreparaArchivio()
            WGGui.ScriviElenco(TabellaSessione, "Voce", contenitore.DescriviSessione())
        Catch ex As Exception
            log.ScriviLog("WGSessione.Salva: " & ex.Message)
        End Try
    End Sub

    ''' <summary>All'inizio dell'uscita: l'elenco resta quello di adesso mentre le applicazioni si chiudono.</summary>
    Public Shared Sub Congela()
        If _congelata Then Return
        Salva()
        _congelata = True
    End Sub

    ''' <summary>Uscita annullata: si torna a salvare.</summary>
    Public Shared Sub Scongela()
        _congelata = False
        SalvaPresto()
    End Sub

    ''' <summary>
    ''' All'avvio si è aperta subito un'applicazione (per esempio le notizie): le applicazioni dell'ultima
    ''' volta si riaprono quando la si chiude, e intanto l'elenco salvato non cambia.
    ''' </summary>
    Public Shared Sub RiapriDopo(nomeApplicazione As String)
        Try
            If Not Attiva Then Return
            _rimandata = True
            _applicazioneDaAspettare = nomeApplicazione
            _vistaAperta = False
            If _timerRimando Is Nothing Then _timerRimando = New Timer() With {.Interval = 1500}
            _timerRimando.Start()
        Catch ex As Exception
            log.ScriviLog("WGSessione.RiapriDopo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub _timerRimando_Tick(sender As Object, e As EventArgs) Handles _timerRimando.Tick
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return
            Dim aperta As Boolean = contenitore.ApplicazioneAperta(_applicazioneDaAspettare)
            If aperta Then
                _vistaAperta = True
                Return
            End If
            ' Se non si è mai aperta si aspetta al massimo qualche giro, poi si riapre lo stesso.
            If Not _vistaAperta Then
                _vistaAperta = True
                Return
            End If
            _timerRimando.Stop()
            _rimandata = False
            RiapriAsync()
        Catch ex As Exception
            log.ScriviLog("WGSessione.Rimando: " & ex.Message)
        End Try
    End Sub

    ''' <summary>All'avvio: riapre le applicazioni dell'ultima volta, poi torna alla scrivania.</summary>
    Public Shared Async Sub RiapriAsync()
        Try
            If Not Attiva Then Return
            PreparaArchivio()
            Dim modi As List(Of String) = WGGui.LeggiElenco(TabellaSessione, "Voce").Where(Function(r) Not String.IsNullOrWhiteSpace(r)).ToList()
            If modi.Count = 0 Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing Then Return
            _riaperturaInCorso = True
            Dim riaperte As Integer = 0
            Try
                ' Si lascia partire la scrivania prima di riaprire il resto.
                Await Task.Delay(1500)
                ' In sottofondo: nessuna attesa da aspettare, niente davanti cambia, niente parla.
                contenitore.IniziaAperturaInSottofondo()
                Try
                    For Each modo As String In modi
                        Try
                            If Await RiapriUnaAsync(contenitore, modo) Then riaperte += 1
                            Await Task.Delay(700)
                        Catch exUna As Exception
                            log.ScriviLog("WGSessione.RiapriAsync " & modo & ": " & exUna.Message)
                        End Try
                    Next
                Finally
                    contenitore.FineAperturaInSottofondo()
                End Try
            Finally
                _riaperturaInCorso = False
            End Try
            SalvaPresto()
        Catch ex As Exception
            log.ScriviLog("WGSessione.RiapriAsync: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Function RiapriUnaAsync(contenitore As WGContenitore, modo As String) As Task(Of Boolean)
        If modo.StartsWith("scrivania:") Then Return Await WGScrivania.ApriPerIdAsync(modo.Substring("scrivania:".Length))
        If modo.StartsWith("percorso:") Then
            Dim percorso As String = modo.Substring("percorso:".Length)
            If Not File.Exists(percorso) AndAlso Not Directory.Exists(percorso) Then Return False
            WGScrivania.ApriPercorso(percorso)
            Return True
        End If
        If modo.StartsWith("menu:") Then
            Dim voci As List(Of String) = modo.Substring("menu:".Length).Split(New String() {WGScrivania.SeparatoreMenu}, StringSplitOptions.None).ToList()
            If voci.Count = 0 OrElse voci(0) = "" Then Return False
            contenitore.ApriComeNuovaApplicazione(voci(0), modo)
            Return Await contenitore.EseguiPercorsoMenuAsync(voci)
        End If
        Return False
    End Function

End Class
