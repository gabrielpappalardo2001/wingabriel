' ============================================================================================
' MenuBraille - le impostazioni del braille
' ============================================================================================
'
' La voce "Braille" del menu Impostazioni, subito dopo "Voce e audio": acceso o spento, stato, ricerca della
' barra (parte comunque da sola quando WinGabriel si carica), punti per cella, tabella di traduzione,
' collegamento (automatico, manuale Bluetooth, manuale USB o una porta precisa) e cursore.
' Le voci del menu portano solo il nome dell'impostazione; il valore si sceglie in un elenco.
' ============================================================================================
Imports System.Threading.Tasks
Imports wgcontrols

Friend NotInheritable Class MenuBraille

    Private Shared ReadOnly logger As New wgcontrols.Logger()

    Private Sub New()
    End Sub

    Private Shared Sub Avviso(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("MenuBraille.Avviso: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub Mostra()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Braille", AddressOf ScegliAcceso)
            m.aggiungi("Stato della barra braille", Sub() Avviso(WGBraille.DescrizioneStato))
            m.aggiungi("Cerca la barra braille", AddressOf CercaBarra)
            m.aggiungi("Punti per cella", AddressOf ScegliPunti)
            m.aggiungi("Tabella di traduzione per la lettura", AddressOf ScegliTabella)
            m.aggiungi("Tabella di traduzione per la scrittura", AddressOf ScegliTabellaInput)
            m.aggiungi("Collegamento", AddressOf ScegliCollegamento)
            m.aggiungi("Cursore sulla barra", AddressOf ScegliCursore)
            m.aggiungi("Attributi del testo sulla barra", AddressOf ScegliAttributi)
            m.aggiungi("Tempo dei messaggi", AddressOf ScegliTempoMessaggio)
            m.aggiungi("Tempo della lettura continua", AddressOf ScegliTempoLettura)
            m.avvia("Braille")
        Catch ex As Exception
            logger.ScriviLog("MenuBraille.Mostra eccezione: " & ex.Message)
        End Try
    End Sub

    ' Un elenco di voci tra cui scegliere; la voce in vigore dice ", attiva". applica riceve l'id scelto.
    Private Shared Sub Scegli(titolo As String, voci As List(Of KeyValuePair(Of String, String)), idCorrente As String, applica As Action(Of String))
        Try
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo(titolo)
            For Each v As KeyValuePair(Of String, String) In voci
                lista.aggiungiElemento(v.Value & If(String.Equals(v.Key, idCorrente, StringComparison.OrdinalIgnoreCase), ", attiva", ""), v.Key)
            Next
            AddHandler lista.escPremuto, Sub() lista.abbandona()
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      lista.abbandona()
                                                      applica(id)
                                                  End Sub
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuBraille.Scegli eccezione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function AccesoSpento() As List(Of KeyValuePair(Of String, String))
        Return New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("1", "Acceso"),
            New KeyValuePair(Of String, String)("0", "Spento")}
    End Function

    Private Shared Sub ScegliAcceso()
        Scegli("Braille", AccesoSpento(), If(WGBraille.Attivo, "1", "0"),
               Sub(id As String)
                   WGBraille.Attivo = (id = "1")
                   Avviso(If(WGBraille.Attivo, "Braille acceso.", "Braille spento."))
               End Sub)
    End Sub

    Private Shared Sub ScegliCursore()
        Scegli("Cursore sulla barra", AccesoSpento(), If(WGBraille.MostraCursore, "1", "0"),
               Sub(id As String)
                   WGBraille.MostraCursore = (id = "1")
                   Avviso(If(WGBraille.MostraCursore, "Cursore acceso.", "Cursore spento."))
               End Sub)
    End Sub

    Private Shared Function Durate(secondi As Integer()) As List(Of KeyValuePair(Of String, String))
        Dim voci As New List(Of KeyValuePair(Of String, String))()
        For Each s As Integer In secondi
            voci.Add(New KeyValuePair(Of String, String)(s.ToString(), If(s = 60, "1 minuto", If(s >= 120, (s \ 60).ToString() & " minuti", s.ToString() & " secondi"))))
        Next
        Return voci
    End Function

    ' Quanto resta un messaggio sulla barra prima del successivo (e quanto dura la "lettura" di un annuncio senza voce).
    Private Shared Sub ScegliTempoMessaggio()
        Scegli("Tempo dei messaggi", Durate(New Integer() {5, 10, 20, 30, 60, 120}), WGBraille.TempoMessaggio.ToString(),
               Sub(id As String)
                   WGBraille.TempoMessaggio = Integer.Parse(id)
                   Avviso("Ogni messaggio resta sulla barra " & Durate(New Integer() {Integer.Parse(id)})(0).Value & ".")
               End Sub)
    End Sub

    ' Quanto resta sulla barra un testo in lettura continua (F8) prima di passare al successivo.
    Private Shared Sub ScegliTempoLettura()
        Scegli("Tempo della lettura continua", Durate(New Integer() {10, 20, 30, 60, 120}), WGBraille.TempoLetturaContinua.ToString(),
               Sub(id As String)
                   WGBraille.TempoLetturaContinua = Integer.Parse(id)
                   Avviso("In lettura continua ogni testo resta sulla barra " & Durate(New Integer() {Integer.Parse(id)})(0).Value & ".")
               End Sub)
    End Sub

    Private Shared Sub ScegliPunti()
        Dim voci As New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("6", "6 punti"),
            New KeyValuePair(Of String, String)("8", "8 punti")}
        Scegli("Punti per cella", voci, WGBraille.Punti.ToString(),
               Sub(id As String)
                   If id = "8" AndAlso Not WGBraille.BarraOttoPunti Then
                       Avviso("La barra braille collegata ha sei punti per cella.")
                       Return
                   End If
                   WGBraille.Punti = Integer.Parse(id)
                   Avviso("Braille a " & id & " punti. Tabella per la lettura: " & WGBraille.DescrizioneTabella & ". Tabella per la scrittura: " & WGBraille.DescrizioneTabellaInput & ".")
               End Sub)
    End Sub

    Private Shared Sub ScegliTabella()
        Dim voci As New List(Of KeyValuePair(Of String, String))()
        For Each t As KeyValuePair(Of String, String) In WGBraille.ElencoTabelle()
            voci.Add(New KeyValuePair(Of String, String)(If(t.Key = "", "interna", t.Key), t.Value))
        Next
        Dim corrente As String = If(WGBraille.TabellaFile = "", "interna", WGBraille.TabellaFile)
        Scegli("Tabella di traduzione per la lettura", voci, corrente,
               Sub(id As String)
                   WGBraille.TabellaFile = If(id = "interna", "", id)
                   Avviso("Tabella per la lettura: " & WGBraille.DescrizioneTabella & ".")
               End Sub)
    End Sub

    Private Shared Sub ScegliTabellaInput()
        Dim voci As New List(Of KeyValuePair(Of String, String))()
        For Each t As KeyValuePair(Of String, String) In WGBraille.ElencoTabelle()
            voci.Add(New KeyValuePair(Of String, String)(If(t.Key = "", "interna", t.Key), t.Value))
        Next
        Dim corrente As String = If(WGBraille.TabellaFileInput = "", "interna", WGBraille.TabellaFileInput)
        Scegli("Tabella di traduzione per la scrittura", voci, corrente,
               Sub(id As String)
                   WGBraille.TabellaFileInput = If(id = "interna", "", id)
                   Avviso("Tabella per la scrittura: " & WGBraille.DescrizioneTabellaInput & ".")
               End Sub)
    End Sub

    Private Shared Sub ScegliAttributi()
        Scegli("Attributi del testo sulla barra", AccesoSpento(), If(WGBraille.MostraAttributi, "1", "0"),
               Sub(id As String)
                   WGBraille.MostraAttributi = (id = "1")
                   Avviso(If(WGBraille.MostraAttributi, "Grassetto, corsivo e sottolineato si vedono sulla barra, dove l'editor li conosce.", "Attributi del testo spenti sulla barra."))
               End Sub)
    End Sub

    Private Shared Sub ScegliCollegamento()
        Dim voci As New List(Of KeyValuePair(Of String, String)) From {
            New KeyValuePair(Of String, String)("Automatica", "Automatico"),
            New KeyValuePair(Of String, String)(WGBraille.ModoManualeBluetooth, "Manuale, Bluetooth"),
            New KeyValuePair(Of String, String)(WGBraille.ModoManualeUsb, "Manuale, USB")}
        For Each p As String In WGBraille.PorteDisponibili()
            voci.Add(New KeyValuePair(Of String, String)(p.Split(","c)(0).Trim(), "Porta " & p))
        Next
        Dim corrente As String = WGBraille.Porta.Split(","c)(0).Trim()
        Scegli("Collegamento", voci, corrente,
               Sub(id As String)
                   WGBraille.Porta = id
                   Select Case id
                       Case "Automatica" : Avviso("Collegamento automatico.")
                       Case WGBraille.ModoManualeBluetooth : Avviso("Collegamento manuale, Bluetooth: si provano tutte le porte Bluetooth.")
                       Case WGBraille.ModoManualeUsb : Avviso("Collegamento manuale, USB: si provano tutte le porte e i dispositivi collegati con il cavo.")
                       Case Else : Avviso("Porta " & id & ".")
                   End Select
               End Sub)
    End Sub

    Private Shared Async Sub CercaBarra()
        Try
            Dim trovata As Boolean = Await WGProcessoInCorso.EseguiAsync(Of Boolean)("Cerco la barra braille", Function() Task.Run(Function() WGBraille.RilevaOra()))
            If trovata Then
                Avviso(WGBraille.DescrizioneStato)
            ElseIf Not WGBraille.Attivo Then
                Avviso("Il braille è spento: accendilo per cercare la barra.")
            Else
                Avviso("Nessuna barra braille trovata. Controlla che sia accesa e collegata, via cavo o via Bluetooth già associata a Windows, e che nessun altro programma la stia usando.")
            End If
        Catch ex As Exception
            logger.ScriviLog("MenuBraille.CercaBarra eccezione: " & ex.Message)
        End Try
    End Sub

End Class
