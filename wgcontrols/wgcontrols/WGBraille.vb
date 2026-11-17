' ============================================================================================
' WGBraille - il braille di WinGabriel
' ============================================================================================
'
' A cosa serve
'   Gestisce la barra braille collegata (Braille Sense 6 e 6 mini, altre HIMS, barre HID standard): la trova da
'   sola all'avvio e quando viene collegata, mostra sulle celle tutto quello che il programma dice (il testo
'   arriva da sintesivocale.LeggiTesto, l'unico punto da cui passa il testo), mostra la riga del campo in cui si
'   scrive con il cursore, traduce i punti battuti sulla tastiera braille in lettere e fa muovere nei menu.
'   Le tabelle di traduzione sono quelle di liblouis (WGBrailleLouis).
'
' Come si usa (il resto del programma non deve fare quasi niente)
'   WGBraille.Avvia()                          ' lo fa sintesivocale al primo uso; individua la barra da solo
'   WGBraille.Mostra("testo", "Normale")       ' lo fa sintesivocale.LeggiTesto
'   WGBraille.Collega(editor)                  ' un editor di testo (che implementa IWGBrailleCampo) si registra qui
'   WGBraille.ImpostaContestoPlayer(True)      ' il lettore multimediale e' in primo piano
'   WGBraille.Attivo = False                   ' impostazioni: spegne il braille
'
' Tasti (la barra HIMS manda accordi: si battono insieme, valgono quando si rilasciano)
'   Fuori da un campo di testo:
'     tasti di movimento accanto allo spazio ...... su, giu', sinistra (indietro), destra (entra): come
'                                                   le frecce, in menu e nei campi
'     spazio o punto 8 ........................... conferma, entra nel menu (invio)
'     punto 7 .................................... indietro (esc)
'   In un campo di testo modificabile:
'     punti, anche in accordo ..................... scrive la lettera
'     spazio ..................................... scrive lo spazio
'     punto 8 da solo ............................ invio
'     punto 7 da solo ............................ cancella indietro
'   Accordi con lo spazio, sempre: spazio+punto 1 su, +4 giu', +3 sinistra, +6 destra, +1+3 inizio, +4+6 fine,
'     +1+2+6 pagina su, +3+4+5 pagina giu', +1+5 esc, +1+4+5 canc.
'   Tasti di panoramica: scorrono la riga sulle celle. Instradamento: nei campi sposta il cursore, nel lettore
'     multimediale si muove nel brano.
'
' Errori
'   Nessun metodo solleva eccezioni: tutto finisce nel log (dati\logs\log.txt).
' ============================================================================================
Imports System.Threading
Imports System.Windows.Forms

''' <summary>Un editor di testo che sa anche dire quali caratteri della riga sono in grassetto, corsivo o sottolineato
''' (bit 1, 2 e 4), per mostrarli sulla barra con i punti 7 e 8. Facoltativa: non tutti i campi la implementano.</summary>
Public Interface IWGBrailleCampoAttributi
    ''' <summary>Un valore per ogni carattere della riga dell'ultimo BrailleStato, o Nothing se non disponibile.</summary>
    Function BrailleAttributi() As Byte()
End Interface

''' <summary>Un editor di testo che sa farsi leggere e scrivere dalla barra braille.</summary>
Public Interface IWGBrailleCampo
    ''' <summary>Vero se si puo' scrivere (altrimenti e' solo lettura).</summary>
    ReadOnly Property BrailleModificabile As Boolean
    ''' <summary>La riga in cui si trova il cursore e la posizione del cursore dentro la riga (0 = prima della prima lettera).</summary>
    Function BrailleStato(ByRef riga As String, ByRef cursore As Integer) As Boolean
    ''' <summary>Scrive del testo nella posizione del cursore (con la lettura che farebbe la tastiera).</summary>
    Sub BrailleInserisci(testo As String)
    ''' <summary>Sposta il cursore nella riga corrente (instradamento).</summary>
    Sub BrailleSposta(posizioneNellaRiga As Integer)
End Interface

''' <summary>Un campo che sta in un elenco di campi (un modulo): i tasti di movimento della barra passano al campo precedente e successivo.</summary>
Public Interface IWGBrailleElencoCampi
    ReadOnly Property BrailleInElencoCampi As Boolean
    ''' <summary>-1 il campo precedente, +1 il successivo.</summary>
    Sub BrailleCampoAdiacente(direzione As Integer)
End Interface

Public NotInheritable Class WGBraille

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _lockAvvio As New Object()
    Private Shared ReadOnly _lockRilevamento As New Object()

    Private Const ChiaveAttivo As String = "braille_attivo"
    Private Const ChiaveTabella As String = "braille_tabella"
    Private Const ChiaveTabellaInput As String = "braille_tabella_input"
    Private Const ChiaveAttributi As String = "braille_attributi"
    Private Const ChiavePunti As String = "braille_punti"
    Private Const ChiavePorta As String = "braille_porta"
    Private Const ChiaveCursore As String = "braille_cursore"
    Private Const ChiaveTempoMessaggio As String = "braille_tempo_messaggio"
    Private Const ChiaveTempoLettura As String = "braille_tempo_lettura_continua"
    Private Const TabellaPredefinita6 As String = "it-it-comp6.utb"
    Private Const TabellaPredefinita8 As String = "it-it-comp8.utb"

    Private Shared _avviato As Boolean
    Private Shared _fermato As Boolean
    Private Shared _display As IWGBrailleDisplay
    Private Shared _thread As Thread
    Private Shared ReadOnly _sveglia As New AutoResetEvent(False)
    Private Shared _timer As System.Threading.Timer
    Private Shared ReadOnly _tentativiPorta As New Dictionary(Of String, Long)()
    Private Shared ReadOnly _portaOccupata As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Public Const ModoManualeBluetooth As String = "ManualeBluetooth"
    Public Const ModoManualeUsb As String = "ManualeUsb"

    ' Cio' che e' sulla barra.
    Private Shared _testo As String = ""
    Private Shared _cursore As Integer = -1
    Private Shared _celle As Integer() = New Integer() {}
    Private Shared _posInput As Integer() = New Integer() {}
    Private Shared _offset As Integer = 0
    Private Shared _tickMessaggio As Long = 0
    Private Shared _daMessaggio As Boolean = False

    Private Shared _campo As IWGBrailleCampo
    Private Shared _attributiRiga As Byte() = Nothing
    Private Shared _ultimaRiga As String = Nothing
    Private Shared _ultimoCursore As Integer = -2
    Private Shared _playerAttivo As Boolean = False
    Private Shared _ultimoTestoPlayer As String = ""

    Private Sub New()
    End Sub

#Region "Impostazioni"

    Private Shared Function Leggi(chiave As String, predefinito As String) As String
        Try
            Dim v As String = New WGConfig().LeggiValore(chiave)
            If String.IsNullOrWhiteSpace(v) Then Return predefinito
            Return v.Trim()
        Catch ex As Exception
            log.ScriviLog("WGBraille.Leggi " & chiave & ": " & ex.Message)
            Return predefinito
        End Try
    End Function

    Private Shared Sub Scrivi(chiave As String, valore As String)
        Try
            Dim cfg As New WGConfig()
            cfg.SalvaValore(chiave, valore)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Scrivi " & chiave & ": " & ex.Message)
        End Try
    End Sub

    ''' <summary>Vero se il braille e' acceso (predefinito: si).</summary>
    Public Shared Property Attivo As Boolean
        Get
            Return Leggi(ChiaveAttivo, "1") <> "0"
        End Get
        Set(value As Boolean)
            Scrivi(ChiaveAttivo, If(value, "1", "0"))
            Try
                _sveglia.Set()
            Catch ex As Exception
                log.ScriviLog("WGBraille.Attivo: " & ex.Message)
            End Try
        End Set
    End Property

    ''' <summary>6 o 8 punti per cella (per la tabella e per la tastiera).</summary>
    Public Shared Property Punti As Integer
        Get
            Return If(Leggi(ChiavePunti, "6") = "8", 8, 6)
        End Get
        Set(value As Integer)
            Dim v As Integer = If(value = 8, 8, 6)
            Scrivi(ChiavePunti, v.ToString())
            Dim t As TabellaBraille = TrovaTabella(TabellaFile)
            If t Is Nothing OrElse (t.File <> "" AndAlso t.Punti <> v) Then
                Scrivi(ChiaveTabella, If(v = 8, TabellaPredefinita8, TabellaPredefinita6))
            End If
            Dim tIn As TabellaBraille = TrovaTabella(TabellaFileInput)
            If tIn Is Nothing OrElse (tIn.File <> "" AndAlso tIn.Punti <> v) Then
                Scrivi(ChiaveTabellaInput, If(v = 8, TabellaPredefinita8, TabellaPredefinita6))
            End If
            RicalcolaAttuale()
        End Set
    End Property

    ''' <summary>Il file della tabella di liblouis usata per mostrare il testo sulla barra (vuoto = tabella interna).</summary>
    Public Shared Property TabellaFile As String
        Get
            Return Leggi(ChiaveTabella, TabellaPredefinita6)
        End Get
        Set(value As String)
            Scrivi(ChiaveTabella, If(value, ""))
            RicalcolaAttuale()
        End Set
    End Property

    ''' <summary>Il file della tabella di liblouis usata per tradurre i punti battuti sulla tastiera braille in lettere
    ''' (vuoto = tabella interna). Puo' essere diversa da quella di lettura.</summary>
    Public Shared Property TabellaFileInput As String
        Get
            Return Leggi(ChiaveTabellaInput, TabellaPredefinita6)
        End Get
        Set(value As String)
            Scrivi(ChiaveTabellaInput, If(value, ""))
        End Set
    End Property

    ''' <summary>Vero se sulla barra si vedono anche grassetto, corsivo e sottolineato (punti 7 e 8), quando l'editor lo sa dire.</summary>
    Public Shared Property MostraAttributi As Boolean
        Get
            Return Leggi(ChiaveAttributi, "0") <> "0"
        End Get
        Set(value As Boolean)
            Scrivi(ChiaveAttributi, If(value, "1", "0"))
            RicalcolaAttuale()
        End Set
    End Property

    Public Shared ReadOnly Property DescrizioneTabella As String
        Get
            Dim t As TabellaBraille = TrovaTabella(TabellaFile)
            If t Is Nothing Then Return TabellaFile
            Return t.Descrizione
        End Get
    End Property

    Public Shared ReadOnly Property DescrizioneTabellaInput As String
        Get
            Dim t As TabellaBraille = TrovaTabella(TabellaFileInput)
            If t Is Nothing Then Return TabellaFileInput
            Return t.Descrizione
        End Get
    End Property

    ''' <summary>"Automatica" oppure il nome di una porta (COM5).</summary>
    Public Shared Property Porta As String
        Get
            Return Leggi(ChiavePorta, "Automatica")
        End Get
        Set(value As String)
            Scrivi(ChiavePorta, If(String.IsNullOrWhiteSpace(value), "Automatica", value.Trim()))
            SyncLock _tentativiPorta
                _tentativiPorta.Clear()
            End SyncLock
            Try
                _sveglia.Set()
            Catch ex As Exception
                log.ScriviLog("WGBraille.Porta: " & ex.Message)
            End Try
        End Set
    End Property

    ''' <summary>Vero se il cursore si mostra con i punti 7 e 8.</summary>
    Public Shared Property MostraCursore As Boolean
        Get
            Return Leggi(ChiaveCursore, "1") <> "0"
        End Get
        Set(value As Boolean)
            Scrivi(ChiaveCursore, If(value, "1", "0"))
            RicalcolaAttuale()
        End Set
    End Property

    ''' <summary>Quanto dura un messaggio (o un annuncio) sulla barra prima che si passi al successivo, in secondi. Predefinito 10.</summary>
    Public Shared Property TempoMessaggio As Integer
        Get
            Dim v As Integer
            If Integer.TryParse(Leggi(ChiaveTempoMessaggio, "10"), v) AndAlso v >= 3 AndAlso v <= 600 Then Return v
            Return 10
        End Get
        Set(value As Integer)
            Scrivi(ChiaveTempoMessaggio, Math.Max(3, Math.Min(600, value)).ToString())
        End Set
    End Property

    ''' <summary>Quanto resta sulla barra un testo in lettura continua (F8) prima del successivo, in secondi. Predefinito 10.</summary>
    Public Shared Property TempoLetturaContinua As Integer
        Get
            Dim v As Integer
            If Integer.TryParse(Leggi(ChiaveTempoLettura, "10"), v) AndAlso v >= 3 AndAlso v <= 600 Then Return v
            Return 10
        End Get
        Set(value As Integer)
            Scrivi(ChiaveTempoLettura, Math.Max(3, Math.Min(600, value)).ToString())
        End Set
    End Property

    ''' <summary>Le tabelle che si possono scegliere con i punti per cella attuali: file e descrizione.</summary>
    Public Shared Function ElencoTabelle() As List(Of KeyValuePair(Of String, String))
        Dim r As New List(Of KeyValuePair(Of String, String))()
        Try
            Dim p As Integer = Punti
            For Each t As TabellaBraille In WGBrailleLouis.TabelleProposte()
                If t.Punti = p OrElse t.File = "" Then r.Add(New KeyValuePair(Of String, String)(t.File, t.Descrizione))
            Next
        Catch ex As Exception
            log.ScriviLog("WGBraille.ElencoTabelle: " & ex.Message)
        End Try
        Return r
    End Function

    Private Shared Function TrovaTabella(file As String) As TabellaBraille
        Try
            For Each t As TabellaBraille In WGBrailleLouis.TabelleProposte()
                If String.Equals(t.File, file, StringComparison.OrdinalIgnoreCase) Then Return t
            Next
        Catch ex As Exception
            log.ScriviLog("WGBraille.TrovaTabella: " & ex.Message)
        End Try
        Return Nothing
    End Function

#End Region

#Region "Stato"

    Public Shared ReadOnly Property Collegata As Boolean
        Get
            Dim d As IWGBrailleDisplay = _display
            Return d IsNot Nothing AndAlso d.Collegato
        End Get
    End Property

    Public Shared ReadOnly Property NomeBarra As String
        Get
            Dim d As IWGBrailleDisplay = _display
            If d Is Nothing Then Return ""
            Return d.Nome
        End Get
    End Property

    Public Shared ReadOnly Property CelleBarra As Integer
        Get
            Dim d As IWGBrailleDisplay = _display
            If d Is Nothing Then Return 0
            Return d.NumeroCelle
        End Get
    End Property

    ''' <summary>Vero se la barra collegata ha otto punti per cella (e quindi si puo' scegliere l'ottopunti).</summary>
    Public Shared ReadOnly Property BarraOttoPunti As Boolean
        Get
            Dim d As IWGBrailleDisplay = _display
            Return d Is Nothing OrElse d.OttoPunti
        End Get
    End Property

    Public Shared ReadOnly Property DescrizioneStato As String
        Get
            Try
                If Not Attivo Then Return "Il braille è spento."
                Dim d As IWGBrailleDisplay = _display
                If d Is Nothing OrElse Not d.Collegato Then Return "Nessuna barra braille collegata."
                Dim come As String = If(d.Collegamento = "Bluetooth", "via Bluetooth", If(d.Collegamento = "Hid", "senza driver", "via cavo"))
                Return "Collegata: " & d.Nome & ", " & d.NumeroCelle.ToString() & " celle, " & come & "."
            Catch ex As Exception
                log.ScriviLog("WGBraille.DescrizioneStato: " & ex.Message)
                Return ""
            End Try
        End Get
    End Property

    ''' <summary>Le porte seriali presenti, per sceglierle a mano nelle impostazioni.</summary>
    Public Shared Function PorteDisponibili() As List(Of String)
        Dim r As New List(Of String)()
        Try
            For Each c As CandidatoBraille In WGBrailleRilevamento.CandidatiSeriali()
                r.Add(c.Percorso & ", " & c.Descrizione)
            Next
        Catch ex As Exception
            log.ScriviLog("WGBraille.PorteDisponibili: " & ex.Message)
        End Try
        Return r
    End Function

#End Region

#Region "Avvio e rilevamento"

    ''' <summary>Fa partire l'individuazione automatica della barra (una volta sola).</summary>
    Public Shared Sub Avvia()
        Try
            SyncLock _lockAvvio
                If _avviato Then Return
                _avviato = True
            End SyncLock
            _fermato = False
            ' Tutto il lavoro (impostazioni, ricerca, collegamento, annuncio) avviene su un thread a parte: l'avvio di
            ' WinGabriel non aspetta mai il braille.
            _thread = New Thread(AddressOf CicloRilevamento)
            _thread.IsBackground = True
            _thread.Priority = ThreadPriority.BelowNormal
            _thread.Name = "WGBraille"
            _thread.Start()
        Catch ex As Exception
            log.ScriviLog("WGBraille.Avvia: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Chiude tutto (chiusura del programma).</summary>
    Public Shared Sub Ferma()
        Try
            _fermato = True
            _sveglia.Set()
            If _timer IsNot Nothing Then _timer.Dispose()
            Scollega(False)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Ferma: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Cerca subito la barra (dalle impostazioni). Restituisce vero se ne ha trovata una.</summary>
    Public Shared Function RilevaOra() As Boolean
        Try
            If Not Attivo Then Return False
            If Collegata Then Return True
            SyncLock _tentativiPorta
                _tentativiPorta.Clear()
            End SyncLock
            TentaCollegamento(False)
            Return Collegata
        Catch ex As Exception
            log.ScriviLog("WGBraille.RilevaOra: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Sub CicloRilevamento()
        Try
            _timer = New System.Threading.Timer(AddressOf OnTimer, Nothing, 1000, 200)
            While Not _fermato
                Try
                    If Attivo Then
                        Dim d As IWGBrailleDisplay = _display
                        If d Is Nothing Then
                            TentaCollegamento(True)
                        Else
                            Dim hims As WGBrailleHims = TryCast(d, WGBrailleHims)
                            If hims IsNot Nothing Then hims.Verifica()
                        End If
                    ElseIf _display IsNot Nothing Then
                        Scollega(False)
                    End If
                Catch ex As Exception
                    log.ScriviLog("WGBraille.CicloRilevamento (giro): " & ex.Message)
                End Try
                _sveglia.WaitOne(If(_display Is Nothing, 7000, 6000))
            End While
        Catch ex As Exception
            log.ScriviLog("WGBraille.CicloRilevamento: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub TentaCollegamento(daAnnunciare As Boolean)
        Try
            ' Un blocco a parte: aprire una porta Bluetooth puo' durare secondi e non deve fermare chi mostra il testo.
            SyncLock _lockRilevamento
                If _display IsNot Nothing Then Return
                Dim scelta As String = WGBraille.Porta
                Dim manualeBluetooth As Boolean = String.Equals(scelta, ModoManualeBluetooth, StringComparison.OrdinalIgnoreCase)
                Dim manualeUsb As Boolean = String.Equals(scelta, ModoManualeUsb, StringComparison.OrdinalIgnoreCase)
                Dim automatico As Boolean = String.Equals(scelta, "Automatica", StringComparison.OrdinalIgnoreCase)
                Dim nomePortaScelta As String = ""
                If Not (automatico OrElse manualeBluetooth OrElse manualeUsb) Then nomePortaScelta = scelta.Split(","c)(0).Trim()
                Dim candidati As List(Of CandidatoBraille) = WGBrailleRilevamento.Candidati()

                ' 1. Barre HID standard: nessun driver. 2. HIMS via WinUSB.
                For Each c As CandidatoBraille In candidati
                    If c.Tipo <> "Hid" AndAlso c.Tipo <> "WinUsb" Then Continue For
                    If nomePortaScelta <> "" Then Continue For
                    If manualeBluetooth AndAlso c.Collegamento <> "Bluetooth" Then Continue For
                    If manualeUsb AndAlso c.Collegamento = "Bluetooth" Then Continue For
                    Dim d As IWGBrailleDisplay
                    If c.Tipo = "Hid" Then d = WGBrailleHid.Prova(c) Else d = WGBrailleHims.Prova(c)
                    If d IsNot Nothing Then
                        Collega(d, daAnnunciare)
                        Return
                    End If
                Next

                ' 3. HIMS su porta seriale (Bluetooth o cavo). In automatico si provano le porte che sembrano barre
                ' braille; in manuale tutte quelle del tipo scelto.
                For Each c As CandidatoBraille In candidati
                    If c.Tipo <> "Seriale" Then Continue For
                    If nomePortaScelta <> "" Then
                        If Not String.Equals(c.Percorso, nomePortaScelta, StringComparison.OrdinalIgnoreCase) Then Continue For
                    ElseIf manualeBluetooth Then
                        If c.Collegamento <> "Bluetooth" Then Continue For
                    ElseIf manualeUsb Then
                        If c.Collegamento = "Bluetooth" Then Continue For
                    ElseIf Not c.Probabile Then
                        Continue For
                    End If
                    ' Una porta che non risponde fa aspettare diversi secondi, una occupata da un altro programma si libera
                    ' di solito presto: non si riprova a ogni giro.
                    Dim adesso As Long = Environment.TickCount
                    SyncLock _tentativiPorta
                        Dim ultimo As Long
                        Dim pausa As Long = If(_portaOccupata.Contains(c.Percorso), 8000, 25000)
                        If _tentativiPorta.TryGetValue(c.Percorso, ultimo) AndAlso adesso - ultimo < pausa Then Continue For
                        _tentativiPorta(c.Percorso) = adesso
                    End SyncLock
                    WGBrailleHims.PortaOccupata = False
                    Dim d As WGBrailleHims = WGBrailleHims.Prova(c)
                    If d IsNot Nothing Then
                        SyncLock _tentativiPorta
                            _portaOccupata.Remove(c.Percorso)
                        End SyncLock
                        Collega(d, daAnnunciare)
                        Return
                    End If
                    If WGBrailleHims.PortaOccupata Then
                        Dim primaVolta As Boolean
                        SyncLock _tentativiPorta
                            primaVolta = _portaOccupata.Add(c.Percorso)
                        End SyncLock
                        If primaVolta Then Annuncia("La porta " & c.Percorso & " della barra braille è usata da un altro programma, per esempio un lettore di schermo.")
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGBraille.TentaCollegamento: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Collega(d As IWGBrailleDisplay, daAnnunciare As Boolean)
        Try
            AddHandler d.TastiPremuti, AddressOf OnTasti
            AddHandler d.Instradamento, AddressOf OnInstradamento
            AddHandler d.Disconnessa, AddressOf OnDisconnessa
            _display = d
            SyncLock _lock
                _offset = 0
                _ultimaRiga = Nothing
                _ultimoCursore = -2
            End SyncLock
            Annuncia("Barra braille collegata: " & d.Nome & ", " & d.NumeroCelle.ToString() & " celle.")
            Ridisegna(True)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Collega: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub OnDisconnessa()
        Try
            Scollega(True)
        Catch ex As Exception
            log.ScriviLog("WGBraille.OnDisconnessa: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Scollega(daAnnunciare As Boolean)
        Try
            Dim d As IWGBrailleDisplay
            SyncLock _lock
                d = _display
                _display = Nothing
            End SyncLock
            If d Is Nothing Then Return
            RemoveHandler d.TastiPremuti, AddressOf OnTasti
            RemoveHandler d.Instradamento, AddressOf OnInstradamento
            RemoveHandler d.Disconnessa, AddressOf OnDisconnessa
            Try
                d.Dispose()
            Catch ex As Exception
                log.ScriviLog("WGBraille.Scollega (dispose): " & ex.Message)
            End Try
            If daAnnunciare Then
                Annuncia("Barra braille scollegata.")
                _sveglia.Set()
            End If
        Catch ex As Exception
            log.ScriviLog("WGBraille.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Annuncia(testo As String)
        Try
            Dim v As sintesivocale = sintesivocale.GetInstance()
            If v IsNot Nothing Then v.LeggiTesto(testo, False, "Annuncio")
        Catch ex As Exception
            log.ScriviLog("WGBraille.Annuncia: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Testo sulla barra"

    ''' <summary>
    ''' Mostra sulla barra un testo detto dalla voce. Se si sta scrivendo in un campo, i testi di una sola lettera (l'eco
    ''' di quello che si batte) non coprono la riga del campo.
    ''' </summary>
    Public Shared Sub Mostra(testo As String, Optional priorita As String = "Normale")
        Try
            If _display Is Nothing Then Return
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim pulito As String = testo.Replace(vbCrLf, " ").Replace(vbCr, " ").Replace(vbLf, " ").Replace(vbTab, " ").Trim()
            If pulito = "" Then Return
            SyncLock _lock
                If _campo IsNot Nothing AndAlso pulito.Length <= 1 Then Return
                _testo = pulito
                _cursore = -1
                _offset = 0
                _daMessaggio = True
                _tickMessaggio = Environment.TickCount
            End SyncLock
            Ridisegna(False)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Mostra: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RicalcolaAttuale()
        Try
            Ridisegna(False)
        Catch ex As Exception
            log.ScriviLog("WGBraille.RicalcolaAttuale: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Gli inizi delle "pagine" (finestre) in cui si divide la riga per lo scorrimento: ognuna ha al massimo n celle, e
    ''' si tronca a un confine di parola (una cella vuota, cioe' uno spazio) invece che a meta' parola, quando ce n'e' una
    ''' nella finestra; se una parola e' piu' lunga della barra non c'e' spazio dove tagliare e si tronca comunque a n.
    ''' </summary>
    Private Shared Function CalcolaLimitiAccapo(celle As Integer(), n As Integer) As List(Of Integer)
        Dim pagine As New List(Of Integer)()
        Try
            If n <= 0 Then
                pagine.Add(0)
                Return pagine
            End If
            Dim totale As Integer = If(celle Is Nothing, 0, celle.Length)
            If totale = 0 Then
                pagine.Add(0)
                Return pagine
            End If
            Dim pos As Integer = 0
            While pos < totale
                pagine.Add(pos)
                Dim fine As Integer = Math.Min(pos + n, totale)
                If fine >= totale Then Exit While
                Dim taglioA As Integer = -1
                For i As Integer = fine - 1 To pos + 1 Step -1
                    If celle(i) = 0 Then
                        taglioA = i
                        Exit For
                    End If
                Next
                If taglioA = -1 Then
                    pos = fine
                Else
                    pos = taglioA
                    While pos < totale AndAlso celle(pos) = 0
                        pos += 1
                    End While
                    If pos >= totale Then Exit While
                End If
            End While
        Catch ex As Exception
            log.ScriviLog("WGBraille.CalcolaLimitiAccapo: " & ex.Message)
            pagine.Clear()
            pagine.Add(0)
        End Try
        Return pagine
    End Function

    ''' <summary>L'indice, nella lista degli inizi di pagina, della pagina che contiene la cella offset (l'ultimo inizio non oltre offset).</summary>
    Private Shared Function IndicePagina(pagine As List(Of Integer), offset As Integer) As Integer
        Dim r As Integer = 0
        For i As Integer = 0 To pagine.Count - 1
            If pagine(i) <= offset Then r = i Else Exit For
        Next
        Return r
    End Function

    ' Traduce il testo corrente e scrive la finestra di celle visibile. ricentra: porta in vista il cursore.
    Private Shared Sub Ridisegna(ricentra As Boolean)
        Try
            Dim d As IWGBrailleDisplay = _display
            If d Is Nothing OrElse Not d.Collegato Then Return
            Dim n As Integer = d.NumeroCelle
            If n <= 0 Then Return
            Dim finestra(n - 1) As Byte
            SyncLock _lock
                Dim celle As Integer() = Nothing
                Dim pos As Integer() = Nothing
                WGBrailleLouis.Traduci(_testo, TabellaFile, celle, pos)
                _celle = celle
                _posInput = pos
                Dim celleCursore As Integer = -1
                If _cursore >= 0 AndAlso MostraCursore Then
                    celleCursore = celle.Length
                    For i As Integer = 0 To pos.Length - 1
                        If pos(i) >= _cursore Then
                            celleCursore = i
                            Exit For
                        End If
                    Next
                End If
                Dim totale As Integer = Math.Max(celle.Length, If(celleCursore >= celle.Length, celleCursore + 1, 0))
                Dim celleEstese(Math.Max(0, totale - 1)) As Integer
                For i As Integer = 0 To totale - 1
                    celleEstese(i) = If(i < celle.Length, celle(i), 0)
                Next
                Dim pagine As List(Of Integer) = CalcolaLimitiAccapo(celleEstese, n)
                If ricentra AndAlso celleCursore >= 0 Then
                    _offset = pagine(IndicePagina(pagine, celleCursore))
                End If
                Dim ultimoOffset As Integer = pagine(pagine.Count - 1)
                If _offset > ultimoOffset Then _offset = ultimoOffset
                If _offset < 0 Then _offset = 0
                _offset = pagine(IndicePagina(pagine, _offset))
                Dim attributi As Byte() = _attributiRiga
                For i As Integer = 0 To n - 1
                    Dim idx As Integer = _offset + i
                    Dim v As Integer = If(idx < celle.Length, celle(idx), 0)
                    If attributi IsNot Nothing AndAlso idx < pos.Length Then
                        Dim carattere As Integer = pos(idx)
                        If carattere >= 0 AndAlso carattere < attributi.Length Then
                            Dim a As Byte = attributi(carattere)
                            If (a And 4) <> 0 Then
                                v = v Or &HC0 ' sottolineato: punti 7 e 8
                            Else
                                If (a And 1) <> 0 Then v = v Or &H40 ' grassetto: punto 7
                                If (a And 2) <> 0 Then v = v Or &H80 ' corsivo: punto 8
                            End If
                        End If
                    End If
                    If idx = celleCursore Then v = v Or &HC0
                    finestra(i) = CByte(v And &HFF)
                Next
            End SyncLock
            d.Mostra(finestra)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Ridisegna: " & ex.Message)
        End Try
    End Sub

    ' Nel lettore multimediale i tasti di scorrimento cambiano brano. Con il lettore in primo piano sempre; altrimenti (per
    ' esempio navigando nei menu mentre suona qualcosa) solo quando la riga sulla barra non ha altro da scorrere.
    Private Shared Function CambiaBrano(avanti As Boolean) As Boolean
        Try
            If _campo IsNot Nothing Then Return False
            Dim mpv As WGMpv = WGMpv.IstanzaEsistente
            If mpv Is Nothing OrElse mpv.Durata.TotalSeconds <= 0 Then Return False
            If Not _playerAttivo Then
                Dim d As IWGBrailleDisplay = _display
                Dim n As Integer = If(d Is Nothing, 0, d.NumeroCelle)
                SyncLock _lock
                    If n > 0 Then
                        Dim pagine As List(Of Integer) = CalcolaLimitiAccapo(_celle, n)
                        Dim indiceAttuale As Integer = IndicePagina(pagine, _offset)
                        If avanti AndAlso indiceAttuale + 1 < pagine.Count Then Return False
                        If Not avanti AndAlso _offset > 0 Then Return False
                    End If
                End SyncLock
            End If
            Dim tastiera As WGTastiera = WGTastiera.GetInstance()
            If tastiera Is Nothing Then Return False
            tastiera.SimulaCambioBrano(avanti)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGBraille.CambiaBrano: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Sub Scorri(avanti As Boolean)
        Try
            Dim d As IWGBrailleDisplay = _display
            If d Is Nothing Then Return
            Dim n As Integer = d.NumeroCelle
            If n <= 0 Then Return
            Dim cambiaRiga As Boolean = False
            SyncLock _lock
                Dim pagine As List(Of Integer) = CalcolaLimitiAccapo(_celle, n)
                Dim indiceAttuale As Integer = IndicePagina(pagine, _offset)
                If avanti Then
                    If indiceAttuale + 1 < pagine.Count Then
                        _offset = pagine(indiceAttuale + 1)
                    Else
                        cambiaRiga = True
                    End If
                Else
                    If indiceAttuale > 0 Then
                        _offset = pagine(indiceAttuale - 1)
                    Else
                        cambiaRiga = True
                    End If
                End If
            End SyncLock
            AzzeraAttesaFine()
            If cambiaRiga Then
                ' In lettura continua il testo seguente arriva da solo (o da F8): il cursore non si tocca.
                If LetturaContinuaAttiva Then Return
                ' La riga braille e' finita: in un campo si passa alla riga di testo dopo (o prima). In avanti il cursore va
                ' all'inizio della riga seguente, indietro alla fine della precedente, e la barra la mostra dal punto giusto.
                If _campo IsNot Nothing Then
                    Dim tastiera As WGTastiera = WGTastiera.GetInstance()
                    If tastiera IsNot Nothing Then
                        If avanti Then
                            tastiera.SimulaTasto(Keys.End)
                            tastiera.SimulaTasto(Keys.Right)
                        Else
                            tastiera.SimulaTasto(Keys.Home)
                            tastiera.SimulaTasto(Keys.Left)
                        End If
                    End If
                End If
                Return
            End If
            Ridisegna(False)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Scorri: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Campi di testo e lettore"

    ''' <summary>Un editor di testo si collega al braille: da qui in poi, quando ha il fuoco, la barra mostra la sua riga.</summary>
    Public Shared Sub Collega(editor As Control)
        Try
            Dim campo As IWGBrailleCampo = TryCast(editor, IWGBrailleCampo)
            If campo Is Nothing OrElse editor Is Nothing Then Return
            AddHandler editor.GotFocus, Sub(s As Object, e As EventArgs) RegistraCampoAttivo(campo)
            AddHandler editor.LostFocus, Sub(s As Object, e As EventArgs) DeregistraCampoAttivo(campo)
            AddHandler editor.Disposed, Sub(s As Object, e As EventArgs) DeregistraCampoAttivo(campo)
            If editor.IsHandleCreated AndAlso editor.Focused Then RegistraCampoAttivo(campo)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Collega (editor): " & ex.Message)
        End Try
    End Sub

    Public Shared Sub RegistraCampoAttivo(campo As IWGBrailleCampo)
        Try
            SyncLock _lock
                _campo = campo
                _ultimaRiga = Nothing
                _ultimoCursore = -2
                _daMessaggio = False
            End SyncLock
            AggiornaDaCampo()
        Catch ex As Exception
            log.ScriviLog("WGBraille.RegistraCampo: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub DeregistraCampoAttivo(campo As IWGBrailleCampo)
        Try
            SyncLock _lock
                If Object.ReferenceEquals(_campo, campo) Then
                    _campo = Nothing
                    _ultimaRiga = Nothing
                    _ultimoCursore = -2
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGBraille.DeregistraCampo: " & ex.Message)
        End Try
    End Sub

    ' --- Lettura continua e fine lettura simulata ---------------------------------------------------------
    ' Con la voce spenta e la barra collegata non arriva nessun "fine lettura" dal motore vocale: lo simula la barra
    ' stessa, dopo il tempo scelto dall'utente. I tasti di scorrimento azzerano l'attesa.

    Private Shared ReadOnly _lettureContinue As New HashSet(Of Object)()
    Private Shared ReadOnly _lockFine As New Object()
    Private Shared _timerFine As System.Threading.Timer
    Private Shared _callbackFine As Action

    ''' <summary>Un editor avvisa che la sua lettura continua (F8) e' partita o finita.</summary>
    Public Shared Sub ImpostaLetturaContinua(chi As Object, attiva As Boolean)
        Try
            SyncLock _lettureContinue
                If attiva Then _lettureContinue.Add(chi) Else _lettureContinue.Remove(chi)
            End SyncLock
            If Not attiva AndAlso Not LetturaContinuaAttiva Then AnnullaFine()
        Catch ex As Exception
            log.ScriviLog("WGBraille.ImpostaLetturaContinua: " & ex.Message)
        End Try
    End Sub

    Public Shared ReadOnly Property LetturaContinuaAttiva As Boolean
        Get
            SyncLock _lettureContinue
                Return _lettureContinue.Count > 0
            End SyncLock
        End Get
    End Property

    ''' <summary>Vero se la voce e' spenta e c'e' una barra braille collegata: il braille e' l'unico canale.</summary>
    Public Shared ReadOnly Property SoloBraille As Boolean
        Get
            Try
                If Not Collegata Then Return False
                Dim v As sintesivocale = sintesivocale.GetInstance()
                Return v IsNot Nothing AndAlso v.VoceSpenta
            Catch ex As Exception
                log.ScriviLog("WGBraille.SoloBraille: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Il tempo dopo cui un editor in lettura continua considera bloccata la lettura e passa oltre: con la sola barra braille
    ''' deve essere piu' lungo di qualunque tempo scelto, altrimenti scatta prima della fine simulata.
    ''' </summary>
    Public Shared Function IntervalloWatchdog(predefinitoMs As Integer) As Integer
        Try
            If SoloBraille Then Return 620000
        Catch ex As Exception
            log.ScriviLog("WGBraille.IntervalloWatchdog: " & ex.Message)
        End Try
        Return predefinitoMs
    End Function

    ''' <summary>La voce e' spenta: chiama callback (che solleva "fine lettura") dopo il tempo di permanenza sulla barra.</summary>
    Friend Shared Sub PianificaFine(callback As Action)
        Try
            SyncLock _lockFine
                _callbackFine = callback
                RiavviaFineInterno()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGBraille.PianificaFine: " & ex.Message)
        End Try
    End Sub

    Friend Shared Sub AnnullaFine()
        Try
            SyncLock _lockFine
                _callbackFine = Nothing
                If _timerFine IsNot Nothing Then
                    _timerFine.Dispose()
                    _timerFine = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGBraille.AnnullaFine: " & ex.Message)
        End Try
    End Sub

    ' Rimette a zero l'attesa (premendo un tasto di scorrimento si ha di nuovo tutto il tempo per leggere).
    Private Shared Sub AzzeraAttesaFine()
        Try
            SyncLock _lockFine
                If _callbackFine IsNot Nothing Then RiavviaFineInterno()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGBraille.AzzeraAttesaFine: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RiavviaFineInterno()
        If _timerFine IsNot Nothing Then _timerFine.Dispose()
        Dim secondi As Integer = If(LetturaContinuaAttiva, TempoLetturaContinua, TempoMessaggio)
        _timerFine = New System.Threading.Timer(AddressOf OnFineScaduta, Nothing, secondi * 1000, Timeout.Infinite)
    End Sub

    Private Shared Sub OnFineScaduta(stato As Object)
        Try
            Dim cb As Action
            SyncLock _lockFine
                cb = _callbackFine
                _callbackFine = Nothing
                If _timerFine IsNot Nothing Then
                    _timerFine.Dispose()
                    _timerFine = Nothing
                End If
            End SyncLock
            If cb IsNot Nothing Then cb()
        Catch ex As Exception
            log.ScriviLog("WGBraille.OnFineScaduta: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Il lettore multimediale e' in primo piano: la barra mostra la posizione nel brano.</summary>
    Public Shared Sub ImpostaContestoPlayer(attivo As Boolean)
        Try
            _playerAttivo = attivo
            _ultimoTestoPlayer = ""
        Catch ex As Exception
            log.ScriviLog("WGBraille.ImpostaContestoPlayer: " & ex.Message)
        End Try
    End Sub

    ' Ricava dall'editor la riga e il cursore (sul thread dell'editor) e, se sono cambiati, aggiorna la barra.
    Private Shared Sub AggiornaDaCampo()
        Try
            If _display Is Nothing Then Return
            Dim campo As IWGBrailleCampo = _campo
            If campo Is Nothing Then Return
            Dim riga As String = ""
            Dim cursore As Integer = 0
            Dim ok As Boolean = False
            Dim ctl As Control = TryCast(campo, Control)
            If ctl IsNot Nothing Then
                If ctl.IsDisposed OrElse Not ctl.IsHandleCreated Then Return
                If ctl.InvokeRequired Then
                    Dim r As String = ""
                    Dim c As Integer = 0
                    Dim fatto As Boolean = False
                    Try
                        ctl.Invoke(New MethodInvoker(Sub()
                                                         fatto = campo.BrailleStato(r, c)
                                                     End Sub))
                    Catch exInv As Exception
                        log.ScriviLog("WGBraille.AggiornaDaCampo (invoke): " & exInv.Message)
                        Return
                    End Try
                    riga = r
                    cursore = c
                    ok = fatto
                Else
                    ok = campo.BrailleStato(riga, cursore)
                End If
            Else
                ok = campo.BrailleStato(riga, cursore)
            End If
            If Not ok Then Return
            Dim cambiato As Boolean
            SyncLock _lock
                cambiato = Not String.Equals(riga, _ultimaRiga, StringComparison.Ordinal) OrElse cursore <> _ultimoCursore
                If cambiato Then
                    _ultimaRiga = riga
                    _ultimoCursore = cursore
                    _testo = riga
                    _cursore = cursore
                    _daMessaggio = False
                    _attributiRiga = LeggiAttributiCampo(campo, ctl)
                End If
            End SyncLock
            If cambiato Then Ridisegna(True)
        Catch ex As Exception
            log.ScriviLog("WGBraille.AggiornaDaCampo: " & ex.Message)
        End Try
    End Sub

    ' Gli attributi (grassetto, corsivo, sottolineato) della riga appena letta, se l'editor li sa dire e l'impostazione e' attiva.
    Private Shared Function LeggiAttributiCampo(campo As IWGBrailleCampo, ctl As Control) As Byte()
        Try
            If Not MostraAttributi Then Return Nothing
            Dim campoAttr As IWGBrailleCampoAttributi = TryCast(campo, IWGBrailleCampoAttributi)
            If campoAttr Is Nothing Then Return Nothing
            If ctl IsNot Nothing AndAlso ctl.InvokeRequired Then
                If ctl.IsDisposed OrElse Not ctl.IsHandleCreated Then Return Nothing
                Dim a As Byte() = Nothing
                ctl.Invoke(New MethodInvoker(Sub() a = campoAttr.BrailleAttributi()))
                Return a
            End If
            Return campoAttr.BrailleAttributi()
        Catch ex As Exception
            log.ScriviLog("WGBraille.LeggiAttributiCampo: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared _timerInCorso As Integer = 0

    Private Shared Sub OnTimer(stato As Object)
        ' Il timer scatta cinque volte al secondo: se il giro precedente non e' finito si salta questo.
        If Interlocked.Exchange(_timerInCorso, 1) = 1 Then Return
        Try
            If _display Is Nothing Then Return
            If _campo IsNot Nothing Then
                AggiornaDaCampo()
            ElseIf _playerAttivo Then
                AggiornaPlayer()
            End If
        Catch ex As Exception
            log.ScriviLog("WGBraille.OnTimer: " & ex.Message)
        Finally
            Interlocked.Exchange(_timerInCorso, 0)
        End Try
    End Sub

    Private Shared Function Tempo(t As TimeSpan) As String
        If t.TotalHours >= 1 Then Return CInt(Math.Floor(t.TotalHours)).ToString() & ":" & t.Minutes.ToString("00") & ":" & t.Seconds.ToString("00")
        Return t.Minutes.ToString("00") & ":" & t.Seconds.ToString("00")
    End Function

    Private Shared Sub AggiornaPlayer()
        Try
            Dim mpv As WGMpv = WGMpv.IstanzaEsistente
            If mpv Is Nothing Then Return
            If mpv.Durata.TotalSeconds <= 0 Then Return
            ' Un messaggio appena detto dalla voce resta sulla barra qualche secondo.
            If _daMessaggio AndAlso Environment.TickCount - _tickMessaggio < 5000 Then Return
            Dim testo As String = "Posizione " & Tempo(mpv.PosizioneTemporale) & " di " & Tempo(mpv.Durata)
            If testo = _ultimoTestoPlayer Then Return
            _ultimoTestoPlayer = testo
            SyncLock _lock
                _testo = testo
                _cursore = -1
                _offset = 0
                _daMessaggio = False
            End SyncLock
            Ridisegna(False)
        Catch ex As Exception
            log.ScriviLog("WGBraille.AggiornaPlayer: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Tasti della barra"

    Private Enum Azione
        Nessuna
        Su
        Giu
        Sinistra
        Destra
        Invio
        Esc
        Indietro
        Canc
        Home
        Fine
        PagSu
        PagGiu
    End Enum

    Private Shared Sub OnInstradamento(indice As Integer)
        Try
            Dim d As IWGBrailleDisplay = _display
            If d Is Nothing Then Return
            Dim campo As IWGBrailleCampo = _campo
            If campo IsNot Nothing Then
                ' Se sulla barra c'e' un messaggio e non la riga del campo, l'instradamento non ha un punto del testo
                ' a cui riferirsi: riporta sulla barra la riga del campo.
                If _daMessaggio Then
                    SyncLock _lock
                        _ultimaRiga = Nothing
                        _ultimoCursore = -2
                    End SyncLock
                    AggiornaDaCampo()
                    Return
                End If
                Dim posizione As Integer = -1
                SyncLock _lock
                    Dim cella As Integer = _offset + indice
                    If cella >= 0 AndAlso cella < _posInput.Length Then
                        posizione = _posInput(cella)
                    ElseIf cella >= _posInput.Length Then
                        posizione = _testo.Length
                    End If
                End SyncLock
                If posizione >= 0 Then Marshalla(campo, Sub() campo.BrailleSposta(posizione))
                Return
            End If
            Dim mpv As WGMpv = WGMpv.IstanzaEsistente
            If mpv IsNot Nothing AndAlso mpv.Durata.TotalSeconds > 0 Then
                Dim n As Integer = Math.Max(2, d.NumeroCelle)
                Dim frazione As Double = Math.Min(1.0, Math.Max(0.0, indice / CDbl(n - 1)))
                mpv.ScorriAssoluto(frazione * mpv.Durata.TotalSeconds)
            End If
        Catch ex As Exception
            log.ScriviLog("WGBraille.OnInstradamento: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Marshalla(campo As IWGBrailleCampo, azione As MethodInvoker)
        Try
            Dim ctl As Control = TryCast(campo, Control)
            If ctl IsNot Nothing AndAlso ctl.IsHandleCreated AndAlso Not ctl.IsDisposed Then
                ctl.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As Exception
            log.ScriviLog("WGBraille.Marshalla: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub OnTasti(tasti As TastoBraille)
        Try
            If Not Attivo Then Return
            If (tasti And TastoBraille.PanSinistra) <> 0 Then
                If Not CambiaBrano(False) Then Scorri(False)
                Return
            End If
            If (tasti And TastoBraille.PanDestra) <> 0 Then
                If Not CambiaBrano(True) Then Scorri(True)
                Return
            End If

            ' F1 e' l'escape, F2 e' il tasto Alt (in qualunque contesto).
            If (tasti And TastoBraille.F1) <> 0 Then
                Esegui(Azione.Esc)
                Return
            End If
            If (tasti And TastoBraille.F2) <> 0 Then
                Dim tAlt As WGTastiera = WGTastiera.GetInstance()
                If tAlt IsNot Nothing Then tAlt.SimulaAlt()
                Return
            End If

            Dim campo As IWGBrailleCampo = _campo
            Dim inScrittura As Boolean = campo IsNot Nothing AndAlso campo.BrailleModificabile
            Dim dots As Integer = CInt(tasti) And &HFF
            Dim spazio As Boolean = (tasti And TastoBraille.Spazio) <> 0

            ' In un elenco di campi (un modulo) spazio + F (punti 1 2 4) passa al campo successivo. I tasti di movimento
            ' invece scorrono le righe del campo, e solo a fine testo passano al campo vicino (come con la tastiera).
            Dim elenco As IWGBrailleElencoCampi = TryCast(campo, IWGBrailleElencoCampi)
            ' Spazio + I (punti 2 4) torna al campo precedente.
            If elenco IsNot Nothing AndAlso elenco.BrailleInElencoCampi AndAlso (tasti And TastoBraille.Spazio) <> 0 AndAlso (dots = 11 OrElse dots = 10) Then
                Dim direzioneCampo As Integer = If(dots = 11, 1, -1)
                Marshalla(campo, Sub() elenco.BrailleCampoAdiacente(direzioneCampo))
                Return
            End If

            Dim a As Azione = Azione.Nessuna
            ' I tasti di movimento accanto allo spazio (quelli che il sistema usa per i menu) valgono sempre come frecce.
            If (tasti And TastoBraille.FrecciaSu) <> 0 Then
                a = Azione.Su
            ElseIf (tasti And TastoBraille.FrecciaGiu) <> 0 Then
                a = Azione.Giu
            ElseIf (tasti And TastoBraille.FrecciaSinistra) <> 0 Then
                a = Azione.Sinistra
            ElseIf (tasti And TastoBraille.FrecciaDestra) <> 0 Then
                a = Azione.Destra
            ElseIf (tasti And (TastoBraille.SpazioSinistro Or TastoBraille.BasculaSu)) <> 0 Then
                a = If(inScrittura, Azione.Sinistra, Azione.Su)
            ElseIf (tasti And (TastoBraille.SpazioDestro Or TastoBraille.BasculaGiu)) <> 0 Then
                a = If(inScrittura, Azione.Destra, Azione.Giu)
            ElseIf spazio AndAlso dots <> 0 Then
                a = AccordoConSpazio(dots)
            ElseIf spazio Then
                ' In lettura continua lo spazio la interrompe (come sulla tastiera): non scrive niente.
                If LetturaContinuaAttiva Then
                    Dim t As WGTastiera = WGTastiera.GetInstance()
                    If t IsNot Nothing Then t.SimulaTasto(Keys.Space)
                    Return
                End If
                If inScrittura Then
                    Scrivi(" ", campo)
                    Return
                End If
                a = Azione.Invio
            ElseIf inScrittura Then
                If dots = 64 Then
                    a = Azione.Indietro
                ElseIf dots = 128 Then
                    a = Azione.Invio
                Else
                    Dim usabili As Integer = If(WGBraille.Punti = 8, dots, dots And &H3F)
                    If usabili <> 0 Then
                        Dim c As String = WGBrailleLouis.DaPunti(usabili, TabellaFileInput)
                        If c <> "" Then Scrivi(c, campo)
                    End If
                    Return
                End If
            Else
                ' Fuori dai campi i dots non scorrono i menu (lo fanno i tasti di movimento): servono solo conferma e indietro.
                Select Case dots
                    Case 64 : a = Azione.Esc
                    Case 128 : a = Azione.Invio
                End Select
            End If
            Esegui(a)
        Catch ex As Exception
            log.ScriviLog("WGBraille.OnTasti: " & ex.Message)
        End Try
    End Sub

    Private Shared Function AccordoConSpazio(punti As Integer) As Azione
        Select Case punti
            Case 1 : Return Azione.Su
            Case 8 : Return Azione.Giu
            Case 4 : Return Azione.Sinistra
            Case 32 : Return Azione.Destra
            Case 5 : Return Azione.Home
            Case 40 : Return Azione.Fine
            Case 35 : Return Azione.PagSu
            Case 28 : Return Azione.PagGiu
            Case 17 : Return Azione.Esc
            Case 53 : Return Azione.Esc ' spazio + Z (punti 1 3 5 6)
            Case 25 : Return Azione.Canc
            Case 64 : Return Azione.Indietro
            Case 128 : Return Azione.Invio
            Case Else : Return Azione.Nessuna
        End Select
    End Function

    Private Shared Sub Scrivi(testo As String, campo As IWGBrailleCampo)
        Try
            If campo Is Nothing OrElse String.IsNullOrEmpty(testo) Then Return
            Marshalla(campo, Sub()
                              campo.BrailleInserisci(testo)
                              ' La barra segue subito quello che si scrive: a fine celle passa alla pagina dopo.
                              AggiornaDaCampo()
                          End Sub)
        Catch ex As Exception
            log.ScriviLog("WGBraille.Scrivi (testo): " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Esegui(a As Azione)
        Try
            Dim t As Keys = Keys.None
            Select Case a
                Case Azione.Su : t = Keys.Up
                Case Azione.Giu : t = Keys.Down
                Case Azione.Sinistra : t = Keys.Left
                Case Azione.Destra : t = Keys.Right
                Case Azione.Invio : t = Keys.Enter
                Case Azione.Esc : t = Keys.Escape
                Case Azione.Indietro : t = Keys.Back
                Case Azione.Canc : t = Keys.Delete
                Case Azione.Home : t = Keys.Home
                Case Azione.Fine : t = Keys.End
                Case Azione.PagSu : t = Keys.PageUp
                Case Azione.PagGiu : t = Keys.PageDown
            End Select
            If t = Keys.None Then Return
            Dim tastiera As WGTastiera = WGTastiera.GetInstance()
            If tastiera IsNot Nothing Then tastiera.SimulaTasto(t)
            ' Il tasto passa dalla coda della tastiera: poco dopo si rilegge la riga del campo (cancella, frecce, invio).
            If _campo IsNot Nothing Then
                ThreadPool.QueueUserWorkItem(Sub(o As Object)
                                                 Thread.Sleep(90)
                                                 AggiornaDaCampo()
                                             End Sub)
            End If
        Catch ex As Exception
            log.ScriviLog("WGBraille.Esegui: " & ex.Message)
        End Try
    End Sub

#End Region

    ''' <summary>Dalla riga di un testo e dalla posizione assoluta del cursore, la riga corrente e il cursore dentro la riga.</summary>
    Public Shared Sub RigaDaTesto(testo As String, posizione As Integer, ByRef riga As String, ByRef cursore As Integer)
        riga = ""
        cursore = 0
        Try
            If testo Is Nothing Then testo = ""
            If posizione < 0 Then posizione = 0
            If posizione > testo.Length Then posizione = testo.Length
            Dim inizio As Integer = posizione
            While inizio > 0 AndAlso testo(inizio - 1) <> ControlChars.Lf AndAlso testo(inizio - 1) <> ControlChars.Cr
                inizio -= 1
            End While
            Dim fine As Integer = posizione
            While fine < testo.Length AndAlso testo(fine) <> ControlChars.Lf AndAlso testo(fine) <> ControlChars.Cr
                fine += 1
            End While
            riga = testo.Substring(inizio, fine - inizio)
            cursore = posizione - inizio
        Catch ex As Exception
            log.ScriviLog("WGBraille.RigaDaTesto: " & ex.Message)
        End Try
    End Sub

End Class
