Imports System
Imports System.Globalization
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports wgcontrols

''' <summary>
''' Avviso quando viene pubblicato un nuovo articolo di Nel Quotidiano. Si attiva dalla Configurazione del menu degli articoli.
''' Con l'avviso attivo, a ogni avvio di WinGabriel e poi ogni mezz'ora si scarica l'elenco e, se ci sono articoli più nuovi
''' dell'ultimo già visto, si pubblica una notifica nel centro notifiche (che rispetta il non disturbare); con Invio sulla
''' notifica si apre l'elenco degli articoli.
''' La prima volta che si attiva l'avviso si prende l'ultimo articolo come punto di partenza, senza avvisare di quelli già usciti.
''' </summary>
Public NotInheritable Class NelQuotidianoAvvisi

    Private Const CHIAVE_ATTIVO As String = "NelQuotidianoAvvisoNuoviArticoli"
    Private Const CHIAVE_ULTIMO_ID As String = "NelQuotidianoUltimoArticoloVisto"
    Private Const CHIAVE_AZIONE As String = "nelquotidiano"
    Private Const NOME_APPLICAZIONE As String = "Nel Quotidiano"
    Private Const SECONDI_PRIMO_CONTROLLO As Integer = 90
    Private Const MINUTI_TRA_DUE_CONTROLLI As Integer = 30

    Private Shared ReadOnly _logger As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared _timer As System.Threading.Timer
    Private Shared _inControllo As Integer = 0

    Private Sub New()
    End Sub

    Private Shared Function Leggi(chiave As String) As String
        Try
            Dim config As New WGConfig()
            If Not config.EsisteId(chiave) Then Return ""
            Return If(config.LeggiValore(chiave), "").Trim()
        Catch ex As Exception
            _logger.ScriviLog("NelQuotidianoAvvisi.Leggi (" & chiave & "): " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub Scrivi(chiave As String, valore As String)
        Try
            Dim config As New WGConfig()
            config.SalvaValore(chiave, valore)
        Catch ex As Exception
            _logger.ScriviLog("NelQuotidianoAvvisi.Scrivi (" & chiave & "): " & ex.Message)
        End Try
    End Sub

    ''' <summary>Vero se si vuole essere avvisati quando viene pubblicato un nuovo articolo.</summary>
    Public Shared ReadOnly Property Attivo As Boolean
        Get
            Return Leggi(CHIAVE_ATTIVO) = "1"
        End Get
    End Property

    ''' <summary>Attiva o disattiva l'avviso. Attivandolo, l'ultimo articolo uscito diventa il punto di partenza.</summary>
    Public Shared Sub Imposta(attivo As Boolean)
        Try
            Scrivi(CHIAVE_ATTIVO, If(attivo, "1", "0"))
            If attivo Then
                Scrivi(CHIAVE_ULTIMO_ID, "")
                Avvia()
                Task.Run(Sub() Controlla(Nothing))
            End If
        Catch ex As Exception
            _logger.ScriviLog("NelQuotidianoAvvisi.Imposta: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Da chiamare una volta all'avvio di WinGabriel (e senza danno più volte): fa partire il controllo periodico.</summary>
    Public Shared Sub Avvia()
        Try
            SyncLock _lock
                ' con la notifica già nel centro, dopo un riavvio Invio apre comunque l'elenco
                WGNotifiche.WGCentroNotifiche.RegistraGestore(CHIAVE_AZIONE, AddressOf ApriElenco)
                If _timer IsNot Nothing Then Return
                _timer = New System.Threading.Timer(New TimerCallback(AddressOf Controlla), Nothing, SECONDI_PRIMO_CONTROLLO * 1000, MINUTI_TRA_DUE_CONTROLLI * 60 * 1000)
            End SyncLock
        Catch ex As Exception
            _logger.ScriviLog("NelQuotidianoAvvisi.Avvia: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Controlla(stato As Object)
        Try
            If Not Attivo Then Return
            If Interlocked.Exchange(_inControllo, 1) = 1 Then Return
            Try
                Dim elenco As System.Collections.Generic.List(Of NelQuotidianoApi.VoceArticolo) = NelQuotidianoApi.ElencaAsync().GetAwaiter().GetResult()
                If elenco Is Nothing OrElse elenco.Count = 0 Then Return

                Dim massimo As Long = elenco.Max(Function(a) a.Id)
                Dim ultimo As Long = -1
                If Not Long.TryParse(Leggi(CHIAVE_ULTIMO_ID), NumberStyles.Integer, CultureInfo.InvariantCulture, ultimo) Then ultimo = -1

                ' primo controllo dopo l'attivazione: punto di partenza, nessun avviso per gli articoli già usciti
                If ultimo < 0 Then
                    Scrivi(CHIAVE_ULTIMO_ID, massimo.ToString(CultureInfo.InvariantCulture))
                    Return
                End If

                Dim nuovi As System.Collections.Generic.List(Of NelQuotidianoApi.VoceArticolo) = elenco.Where(Function(a) a.Id > ultimo).OrderBy(Function(a) a.Id).ToList()
                If nuovi.Count = 0 Then Return

                Scrivi(CHIAVE_ULTIMO_ID, massimo.ToString(CultureInfo.InvariantCulture))
                Pubblica(nuovi)
            Finally
                Interlocked.Exchange(_inControllo, 0)
            End Try
        Catch ex As Exception
            ' senza connessione o con il sito che non risponde si riprova al prossimo controllo
            _logger.ScriviLog("NelQuotidianoAvvisi.Controlla: " & ex.GetType().Name & ": " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Pubblica(nuovi As System.Collections.Generic.List(Of NelQuotidianoApi.VoceArticolo))
        Dim ultimo As NelQuotidianoApi.VoceArticolo = nuovi(nuovi.Count - 1)
        Dim titolo As String = If(String.IsNullOrWhiteSpace(ultimo.Titolo), "senza titolo", ultimo.Titolo.Trim())
        Dim testo As String
        If nuovi.Count = 1 Then
            testo = "Nuovo articolo, " & titolo
        Else
            testo = nuovi.Count.ToString(CultureInfo.InvariantCulture) & " nuovi articoli, l'ultimo è " & titolo
        End If
        WGNotifiche.WGCentroNotifiche.Pubblica(NOME_APPLICAZIONE, testo, AddressOf ApriElenco, Nothing, True, Nothing, CHIAVE_AZIONE)
    End Sub

    Private Shared Sub ApriElenco()
        Try
            Dim menu As New MenuNelQuotidiano()
            menu.ApriElencoArticoli()
        Catch ex As Exception
            _logger.ScriviLog("NelQuotidianoAvvisi.ApriElenco: " & ex.Message)
        End Try
    End Sub

End Class
