Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports wgcontrols

' ============================================================================================
' SchermoGioco - lo schermo di una partita, disegnato a mano con le API di Windows (come la scrivania)
' ============================================================================================
'
' Cosa mostra
'   - in alto il titolo della partita;
'   - a sinistra il tavolo, a sezioni (giocatori, carte, proprietà...) con righe di testo, quadratini di colore e carte disegnate;
'   - a destra gli ultimi messaggi, l'ultimo in basso;
'   - in basso l'istruzione ("Ora è il tuo turno, premi Invio per lanciare il dado") e, solo quando c'è
'     davvero da scegliere (una carta, una combinazione), una piccola lista di scelte;
'   - sopra a tutto, quando serve, un pannello di opzioni (la lista delle opzioni, le proprietà da ipotecare...)
'     con scelta singola o multipla.
'
' Tasti
'   Le lettere, Invio, F1 e gli altri tasti non usati qui sono comandi rapidi: arrivano a chi usa lo
'   schermo con l'evento Tasto e la mossa si esegue subito, senza portare sulla voce. Su e giù, Home e Fine
'   scorrono la lista delle scelte (se c'è), Invio conferma. Alt chiede le opzioni, Esc solleva EscPremuto,
'   Pagina su e Pagina giù scorrono il tavolo. Nel pannello: frecce, Home e Fine, una lettera porta alla
'   prima voce che comincia così, Spazio sceglie o toglie la voce (scelta multipla), Invio conferma, Esc chiude.
' ============================================================================================

Public Class CartaGioco
    ' Immagine della carta (nome della risorsa): mazzo francese "7H", "KS", "JK"; mazzo italiano "7_denari", "F_bastoni" e le carte speciali
    ' "7_denari_settebello", "R_denari_rebello", "A_coppe_pigliatutto"; "dorso" per il retro. Vuoto per disegnarla con il solo testo.
    Public Immagine As String = ""
    ' Testo da scrivere sulla carta, per esempio "10♥".
    Public Testo As String = ""
    Public Rosso As Boolean
    Public Jolly As Boolean
    Public Dorso As Boolean
    Public Evidenziata As Boolean
End Class

Public Class RigaGioco
    Public Testo As String = ""
    Public Evidenza As Boolean
    ' Quadratino di colore davanti al testo (COLORREF), -1 per nessuno.
    Public Colore As Integer = -1
    Public Carte As New List(Of CartaGioco)()
    Public Piccola As Boolean
End Class

Public Class SezioneGioco
    Public Titolo As String = ""
    Public Righe As New List(Of RigaGioco)()

    Public Function Aggiungi(testo As String, Optional evidenza As Boolean = False, Optional colore As Integer = -1) As RigaGioco
        Try
            Dim r As New RigaGioco With {.Testo = testo, .Evidenza = evidenza, .Colore = colore}
            Righe.Add(r)
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("SezioneGioco.Aggiungi", ex)
            Throw
        End Try
    End Function
End Class

Public Class CasellaTabellone
    Public Nome As String = ""
    ' Colore del gruppo (COLORREF), -1 se la casella non ne ha.
    Public Colore As Integer = -1
    ' Posto del giocatore proprietario, -1 se è libera.
    Public Proprietario As Integer = -1
    ' Case costruite: da 1 a 4 case, 5 albergo.
    Public Edifici As Integer
    Public Ipotecata As Boolean
    ' Icona della casella (il nome dell'immagine senza "icona-": "stazione", "imprevisti"...), vuoto se non ne ha.
    Public Icona As String = ""
End Class

Public Class PedinaTabellone
    Public Posizione As Integer
    Public Colore As Integer
    Public Fuori As Boolean
    ' Il giocatore di turno.
    Public Attiva As Boolean
End Class

''' <summary>Quello che serve per disegnare il tabellone del Monopoli: 40 caselle, le pedine e i due dadi (0 = non ancora tirati).</summary>
Public Class TabelloneGioco
    Public ReadOnly Caselle As New List(Of CasellaTabellone)()
    Public ReadOnly Pedine As New List(Of PedinaTabellone)()
    Public Dado1 As Integer
    Public Dado2 As Integer
End Class

Public Class VocePannelloGioco
    Public Testo As String = ""
    Public Selezionata As Boolean
    Public Tag As Object
End Class

Public Class TastoGiocoEventArgs
    Inherits EventArgs
    Public ReadOnly Codice As Keys
    Public ReadOnly Ctrl As Boolean
    Public ReadOnly Maiuscole As Boolean
    ' Da mettere a vero se il tasto è stato usato.
    Public Gestito As Boolean

    Public Sub New(codice As Keys, ctrl As Boolean, maiuscole As Boolean)
        Try
            Me.Codice = codice
            Me.Ctrl = ctrl
            Me.Maiuscole = maiuscole
        Catch ex As Exception
            RegistroErrori.Registra("TastoGiocoEventArgs.New", ex)
            Throw
        End Try
    End Sub
End Class

Public Class SchermoGioco
    Inherits UserControl
    Implements IWGAnnuncioIngresso

    Public Event Tasto(sender As Object, e As TastoGiocoEventArgs)
    Public Event SceltaFatta(indice As Integer)
    Public Event OpzioniRichieste()
    ''' <summary>Shift: salta il messaggio che si sta leggendo e passa al successivo.</summary>
    Public Event SaltaRichiesto()
    Public Event EscPremuto()
    ''' <summary>Invio (o Spazio) su una casella del casellario della battaglia navale: si posiziona la nave o si spara.</summary>
    Public Event CasellaPremuta(riga As Integer, colonna As Integer)
    ''' <summary>R sul casellario: gira la nave che si sta posizionando.</summary>
    Public Event RuotaRichiesta()
    ''' <summary>Ctrl+sinistra (orizzontale = Vero) o Ctrl+destra (verticale) durante il posizionamento.</summary>
    Public Event OrientaRichiesta(orizzontale As Boolean)
    ''' <summary>M sul casellario: si passa dal mare nemico alla propria flotta e viceversa.</summary>
    Public Event CambiaMareRichiesto()

    Private Shared ReadOnly log As New Logger()
    Private WithEvents _tastiera As WGTastiera
    Private _suoni As WGSuoni

    Private _titolo As String = ""
    Private _istruzione As String = ""
    Private _aiuto As String = ""
    Private _scelte As New List(Of String)()
    Private _indiceScelta As Integer
    Private ReadOnly _sezioni As New List(Of SezioneGioco)()
    Private ReadOnly _messaggi As New List(Of String)()
    Private _scorri As Integer
    Private _altezzaSezioni As Integer
    Private _testoAnnuncio As Func(Of String)

    Private Class PannelloGioco
        Public Titolo As String = ""
        Public Voci As New List(Of VocePannelloGioco)()
        Public Indice As Integer
        Public Multipla As Boolean
        Public Conferma As Action(Of List(Of Integer))
        Public Annulla As Action
        Public ConsentiVuoto As Boolean
    End Class

    Private ReadOnly _pannelli As New List(Of PannelloGioco)()

    ' Casellario della battaglia navale (solo battaglia navale).
    Private _casellario As CasellarioNavale

    ''' <summary>Il casellario da disegnare e percorrere con le frecce (battaglia navale); Nothing per toglierlo.</summary>
    Public Sub ImpostaCasellario(c As CasellarioNavale)
        Try
            _casellario = c
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.ImpostaCasellario", ex)
            Throw
        End Try
    End Sub

    Public ReadOnly Property Casellario As CasellarioNavale
        Get
            Return _casellario
        End Get
    End Property

    ''' <summary>
    ''' Tasti del casellario: frecce per muoversi (a una a una, come in un foglio di calcolo), Home e Fine all'inizio e alla fine della riga,
    ''' Ctrl+Home e Ctrl+Fine alla prima e all'ultima casella, Invio o Spazio per posizionare o sparare, R per girare la nave, M per cambiare mare.
    ''' Dopo ogni spostamento si legge la casella: prima quello che c'è, poi le coordinate. Vero se il tasto è stato usato.
    ''' </summary>
    Private Function GestisciTastoCasellario(codice As Keys, ctrl As Boolean) As Boolean
        Try
            Dim c As CasellarioNavale = _casellario
            If c Is Nothing Then Return False
            Select Case codice
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right
                    If ctrl Then
                        ' mentre si posiziona una nave: Ctrl+destra la mette in verticale, Ctrl+sinistra in orizzontale
                        If c.LunghezzaNave > 0 AndAlso (codice = Keys.Left OrElse codice = Keys.Right) Then
                            RaiseEvent OrientaRichiesta(codice = Keys.Left)
                            Me.Invalidate()
                            Return True
                        End If
                        Return False
                    End If
                    Dim dr As Integer = If(codice = Keys.Up, -1, If(codice = Keys.Down, 1, 0))
                    Dim dc As Integer = If(codice = Keys.Left, -1, If(codice = Keys.Right, 1, 0))
                    If Not c.Muovi(dr, dc) Then
                        Bordo()   ' al bordo il cursore resta fermo e non si dice niente
                        Return True
                    End If
                Case Keys.Home
                    If ctrl Then c.VaiA(0, 0) Else c.VaiA(c.Riga, 0)
                Case Keys.End
                    If ctrl Then c.VaiA(c.Righe - 1, c.Colonne - 1) Else c.VaiA(c.Riga, c.Colonne - 1)
                Case Keys.Enter, Keys.Space
                    If ctrl Then Return False
                    RaiseEvent CasellaPremuta(c.Riga, c.Colonna)
                    Return True
                Case Keys.R
                    If ctrl Then Return False
                    RaiseEvent RuotaRichiesta()
                    Me.Invalidate()
                    Return True
                Case Keys.M
                    If ctrl Then Return False
                    RaiseEvent CambiaMareRichiesto()
                    Return True
                Case Else
                    Return False
            End Select
            Me.Invalidate()
            Leggi(c.Frase())
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.GestisciTastoCasellario", ex)
            Throw
        End Try
    End Function

    ' Tabellone del Monopoli: pedine e dadi si muovono con una piccola animazione.
    Private _tabellone As TabelloneGioco
    Private _posVisibili As New List(Of Integer)()
    Private ReadOnly _dadiUltimi(1) As Integer
    Private ReadOnly _dadiMostrati(1) As Integer
    Private _dadiFotogrammi As Integer
    Private _timerTabellone As System.Windows.Forms.Timer
    Private ReadOnly _casuale As New Random()

    ' ------------------------------------------------------------------ uso
    Public Sub New(titolo As String)
        Try
            _titolo = If(titolo, "")
            Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.Opaque, True)
            Me.SetStyle(ControlStyles.OptimizedDoubleBuffer, False)
            Me.DoubleBuffered = False
            CaricaAspetto()
            Me.AccessibleName = _titolo
            Me.AccessibleRole = AccessibleRole.None
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.New", ex)
            Throw
        End Try
    End Sub

    Public Property Titolo As String
        Get
            Return _titolo
        End Get
        Set(value As String)
            _titolo = If(value, "")
            Me.AccessibleName = _titolo
            Me.Invalidate()
        End Set
    End Property

    ''' <summary>Cosa fare adesso, scritto in basso.</summary>
    Public Property Istruzione As String
        Get
            Return _istruzione
        End Get
        Set(value As String)
            _istruzione = If(value, "")
            Me.Invalidate()
        End Set
    End Property

    ''' <summary>Riga di aiuto in fondo (i tasti principali).</summary>
    Public Property Aiuto As String
        Get
            Return _aiuto
        End Get
        Set(value As String)
            _aiuto = If(value, "")
            Me.Invalidate()
        End Set
    End Property

    ''' <summary>Il testo letto quando si torna sullo schermo da un'altra finestra (di solito l'istruzione).</summary>
    Public Property TestoAnnuncio As Func(Of String)
        Get
            Return _testoAnnuncio
        End Get
        Set(value As Func(Of String))
            _testoAnnuncio = value
        End Set
    End Property

    Public ReadOnly Property Sezioni As List(Of SezioneGioco)
        Get
            Return _sezioni
        End Get
    End Property

    Public ReadOnly Property Messaggi As List(Of String)
        Get
            Return _messaggi
        End Get
    End Property

    ''' <summary>I messaggi occupano tutta la larghezza (per esempio una conversazione): niente tavolo a sinistra.</summary>
    Public Property MessaggiAPienaLarghezza As Boolean

    ''' <summary>Titolo della zona dei messaggi.</summary>
    Public Property TitoloMessaggi As String = "Messaggi"

    Public ReadOnly Property IndiceScelta As Integer
        Get
            Return _indiceScelta
        End Get
    End Property

    Public ReadOnly Property ScelteCount As Integer
        Get
            Return _scelte.Count
        End Get
    End Property

    Public ReadOnly Property PannelloAperto As Boolean
        Get
            Return _pannelli.Count > 0
        End Get
    End Property

    Public Sub Aggiorna()
        Try
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Aggiorna", ex)
            Throw
        End Try
    End Sub

    ''' <summary>La piccola lista di scelte in basso; vuota per toglierla.</summary>
    Public Sub ImpostaScelte(voci As IEnumerable(Of String), Optional indice As Integer = 0)
        Try
            _scelte = If(voci Is Nothing, New List(Of String)(), New List(Of String)(voci))
            _indiceScelta = Math.Max(0, Math.Min(indice, Math.Max(0, _scelte.Count - 1)))
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.ImpostaScelte", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Il tabellone da disegnare sopra le sezioni (solo Monopoli); Nothing per toglierlo. Se i dadi o le pedine sono cambiati parte l'animazione.</summary>
    Public Sub ImpostaTabellone(t As TabelloneGioco)
        Try
            Dim prima As TabelloneGioco = _tabellone
            _tabellone = t
            If t Is Nothing Then
                _posVisibili.Clear()
                _dadiFotogrammi = 0
                Me.Invalidate()
                Return
            End If
            If prima Is Nothing OrElse _posVisibili.Count <> t.Pedine.Count Then
                _posVisibili = New List(Of Integer)()
                For Each p As PedinaTabellone In t.Pedine
                    _posVisibili.Add(p.Posizione)
                Next
                _dadiFotogrammi = 0
            ElseIf t.Dado1 <> _dadiUltimi(0) OrElse t.Dado2 <> _dadiUltimi(1) Then
                If t.Dado1 > 0 AndAlso t.Dado2 > 0 Then _dadiFotogrammi = 9
            End If
            _dadiUltimi(0) = t.Dado1
            _dadiUltimi(1) = t.Dado2
            AvviaAnimazioneTabellone()
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.ImpostaTabellone", ex)
            Throw
        End Try
    End Sub

    Private Sub AvviaAnimazioneTabellone()
        Try
            If _timerTabellone Is Nothing Then
                _timerTabellone = New System.Windows.Forms.Timer() With {.Interval = 80}
                AddHandler _timerTabellone.Tick, AddressOf PassoTabellone
            End If
            If Not _timerTabellone.Enabled Then _timerTabellone.Start()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.AvviaAnimazioneTabellone", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un passo dell'animazione: prima i dadi rotolano, poi ogni pedina avanza di una casella alla volta.</summary>
    Private Sub PassoTabellone(sender As Object, e As EventArgs)
        Try
            If _tabellone Is Nothing OrElse Me.IsDisposed Then
                _timerTabellone.Stop()
                Return
            End If
            If _dadiFotogrammi > 0 Then
                _dadiFotogrammi -= 1
                _dadiMostrati(0) = _casuale.Next(1, 7)
                _dadiMostrati(1) = _casuale.Next(1, 7)
                Me.Invalidate()
                Return
            End If
            Dim mosso As Boolean = False
            Dim n As Integer = Math.Min(_posVisibili.Count, _tabellone.Pedine.Count)
            For i As Integer = 0 To n - 1
                Dim meta As Integer = _tabellone.Pedine(i).Posizione
                Dim ora As Integer = _posVisibili(i)
                If ora = meta Then Continue For
                Dim distanza As Integer = ((meta - ora) Mod 40 + 40) Mod 40
                ' Un salto lungo o all'indietro (per esempio in prigione) non si percorre casella per casella.
                _posVisibili(i) = If(distanza > 12, meta, (ora + 1) Mod 40)
                mosso = True
            Next
            If Not mosso Then _timerTabellone.Stop()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.PassoTabellone: " & ex.Message)
        End Try
    End Sub

    Public Sub Avvia()
        Try
            _tastiera = WGTastiera.GetInstance()
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.MostraControllo(Me)
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.Avvia: " & ex.Message)
        End Try
    End Sub

    Public Sub Chiudi()
        Try
            _tastiera = Nothing
            _pannelli.Clear()
            If _timerTabellone IsNot Nothing Then _timerTabellone.Stop()
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing Then contenitore.RimuoviControllo(Me)
            Me.Dispose()
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.Chiudi: " & ex.Message)
        End Try
    End Sub

    Private Function PuoRicevere() As Boolean
        Try
            If Me.IsDisposed OrElse Not Me.IsHandleCreated OrElse Not Me.Visible Then Return False
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Return contenitore IsNot Nothing AndAlso contenitore.PuoRicevereInput(Me)
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.PuoRicevere: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub Leggi(testo As String)
        Try
            If Not String.IsNullOrEmpty(testo) Then
                MenuGiochi.SegnaLettura()
                sintesivocale.GetInstance().LeggiTesto(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.Leggi: " & ex.Message)
        End Try
    End Sub

    Private Sub Bordo()
        Try
            If _suoni Is Nothing Then _suoni = New WGSuoni()
            _suoni.LimiteRaggiunto()
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.Bordo: " & ex.Message)
        End Try
    End Sub

    Public Sub AnnunciaIngresso() Implements IWGAnnuncioIngresso.AnnunciaIngresso
        Try
            If _pannelli.Count > 0 Then
                Dim p As PannelloGioco = _pannelli(_pannelli.Count - 1)
                Leggi(p.Titolo & ". " & TestoVocePannello(p))
            ElseIf _testoAnnuncio IsNot Nothing Then
                Leggi(_testoAnnuncio())
            Else
                Leggi(_istruzione)
            End If
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.AnnunciaIngresso: " & ex.Message)
        End Try
    End Sub

    ' ------------------------------------------------------------------ pannelli (opzioni, elenchi)
    ''' <summary>Apre un pannello con una lista di voci. Con multipla Spazio sceglie e Invio conferma tutte; altrimenti Invio conferma la voce.</summary>
    Public Sub ApriPannello(titolo As String, voci As IEnumerable(Of VocePannelloGioco), multipla As Boolean, conferma As Action(Of List(Of Integer)), Optional annulla As Action = Nothing, Optional indice As Integer = 0, Optional consentiVuoto As Boolean = False)
        Try
            Dim p As New PannelloGioco With {.Titolo = If(titolo, ""), .Multipla = multipla, .Conferma = conferma, .Annulla = annulla, .ConsentiVuoto = consentiVuoto}
            p.Voci.AddRange(voci)
            If p.Voci.Count = 0 Then Return
            p.Indice = Math.Max(0, Math.Min(indice, p.Voci.Count - 1))
            _pannelli.Add(p)
            Me.Invalidate()
            Leggi(p.Titolo & ". " & TestoVocePannello(p))
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.ApriPannello", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Chiude l'ultimo pannello aperto (senza chiamare Annulla).</summary>
    Public Sub ChiudiPannello()
        Try
            If _pannelli.Count > 0 Then _pannelli.RemoveAt(_pannelli.Count - 1)
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.ChiudiPannello", ex)
            Throw
        End Try
    End Sub

    Public Sub ChiudiPannelli()
        Try
            _pannelli.Clear()
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.ChiudiPannelli", ex)
            Throw
        End Try
    End Sub

    Private Shared Function TestoVocePannello(p As PannelloGioco) As String
        Try
            Dim v As VocePannelloGioco = p.Voci(p.Indice)
            Dim t As String = v.Testo
            If p.Multipla Then t &= If(v.Selezionata, ", scelta", ", non scelta")
            Return t & ", " & (p.Indice + 1).ToString() & " di " & p.Voci.Count.ToString()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.TestoVocePannello", ex)
            Throw
        End Try
    End Function

    Private Sub MuoviPannello(p As PannelloGioco, nuovo As Integer)
        Try
            If nuovo < 0 OrElse nuovo >= p.Voci.Count Then
                Bordo()
                Leggi(TestoVocePannello(p))
                Return
            End If
            p.Indice = nuovo
            Me.Invalidate()
            Leggi(TestoVocePannello(p))
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.MuoviPannello", ex)
            Throw
        End Try
    End Sub

    Private Sub TastoNelPannello(codice As Keys)
        Try
            Dim p As PannelloGioco = _pannelli(_pannelli.Count - 1)
            Select Case codice
                Case Keys.Down : MuoviPannello(p, p.Indice + 1)
                Case Keys.Up : MuoviPannello(p, p.Indice - 1)
                Case Keys.Home : MuoviPannello(p, 0)
                Case Keys.End : MuoviPannello(p, p.Voci.Count - 1)
                Case Keys.PageDown : MuoviPannello(p, Math.Min(p.Voci.Count - 1, p.Indice + 8))
                Case Keys.PageUp : MuoviPannello(p, Math.Max(0, p.Indice - 8))
                Case Keys.Space
                    If p.Multipla Then
                        p.Voci(p.Indice).Selezionata = Not p.Voci(p.Indice).Selezionata
                        Me.Invalidate()
                        Leggi(If(p.Voci(p.Indice).Selezionata, "scelta", "non scelta"))
                    Else
                        Conferma(p)
                    End If
                Case Keys.Enter
                    ' in un elenco a scelta multipla Invio sceglie anche la voce su cui ci si trova
                    If p.Multipla AndAlso p.Voci.Count > 0 Then p.Voci(p.Indice).Selezionata = True
                    Conferma(p)
                Case Keys.Escape
                    _pannelli.Remove(p)
                    Me.Invalidate()
                    If p.Annulla IsNot Nothing Then p.Annulla()
                    If _pannelli.Count > 0 Then AnnunciaIngresso()
                Case Keys.Insert
                    Leggi(p.Titolo & ". " & TestoVocePannello(p))
                Case Else
                    If codice >= Keys.A AndAlso codice <= Keys.Z Then
                        Dim lettera As String = codice.ToString().ToUpperInvariant()
                        For k As Integer = 1 To p.Voci.Count
                            Dim i As Integer = (p.Indice + k) Mod p.Voci.Count
                            If p.Voci(i).Testo.StartsWith(lettera, StringComparison.CurrentCultureIgnoreCase) Then
                                MuoviPannello(p, i)
                                Return
                            End If
                        Next
                        Bordo()
                    End If
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.TastoNelPannello", ex)
            Throw
        End Try
    End Sub

    Private Sub Conferma(p As PannelloGioco)
        Try
            Dim scelte As New List(Of Integer)()
            If p.Multipla Then
                For i As Integer = 0 To p.Voci.Count - 1
                    If p.Voci(i).Selezionata Then scelte.Add(i)
                Next
                If scelte.Count = 0 AndAlso Not p.ConsentiVuoto Then
                    Leggi("Non hai scelto niente.")
                    Return
                End If
            Else
                scelte.Add(p.Indice)
            End If
            ' Il chiamante decide se il pannello resta (un sottomenu si apre sopra) o si chiude tutto.
            If p.Conferma IsNot Nothing Then p.Conferma(scelte)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Conferma", ex)
            Throw
        End Try
    End Sub

    ' ------------------------------------------------------------------ tasti
    Private Sub _tastiera_KeyDown(ByRef tasto As Keys) Handles _tastiera.KeyDown
        Try
            If tasto = Keys.None OrElse Not PuoRicevere() Then Return
            Dim codice As Keys = tasto And Keys.KeyCode
            Dim modificatori As Keys = _tastiera.ModificatoriUltimoTasto
            Dim ctrl As Boolean = (modificatori And Keys.Control) = Keys.Control
            Dim maiuscole As Boolean = (modificatori And Keys.Shift) = Keys.Shift
            If _pannelli.Count > 0 Then
                If Not ctrl Then TastoNelPannello(codice)
                tasto = Keys.None
                Return
            End If
            Dim gestito As Boolean = True
            If _casellario IsNot Nothing AndAlso _scelte.Count = 0 AndAlso GestisciTastoCasellario(codice, ctrl) Then
                tasto = Keys.None
                Return
            End If
            If Not ctrl AndAlso _scelte.Count > 0 AndAlso (codice = Keys.Up OrElse codice = Keys.Down OrElse codice = Keys.Home OrElse codice = Keys.End) Then
                Dim nuovo As Integer = _indiceScelta
                Select Case codice
                    Case Keys.Up : nuovo -= 1
                    Case Keys.Down : nuovo += 1
                    Case Keys.Home : nuovo = 0
                    Case Keys.End : nuovo = _scelte.Count - 1
                End Select
                If nuovo < 0 OrElse nuovo >= _scelte.Count Then
                    Bordo()
                Else
                    _indiceScelta = nuovo
                    Me.Invalidate()
                End If
                Leggi(_scelte(_indiceScelta) & ", " & (_indiceScelta + 1).ToString() & " di " & _scelte.Count.ToString())
            ElseIf Not ctrl AndAlso _scelte.Count > 0 AndAlso (codice = Keys.Enter OrElse codice = Keys.Space) Then
                RaiseEvent SceltaFatta(_indiceScelta)
            ElseIf Not ctrl AndAlso codice = Keys.Escape Then
                RaiseEvent EscPremuto()
            ElseIf Not ctrl AndAlso codice = Keys.PageDown Then
                _scorri = Math.Min(Math.Max(0, _altezzaSezioni - Me.ClientSize.Height \ 2), _scorri + Me.ClientSize.Height \ 3)
                Me.Invalidate()
            ElseIf Not ctrl AndAlso codice = Keys.PageUp Then
                _scorri = Math.Max(0, _scorri - Me.ClientSize.Height \ 3)
                Me.Invalidate()
            Else
                Dim e As New TastoGiocoEventArgs(codice, ctrl, maiuscole)
                RaiseEvent Tasto(Me, e)
                gestito = e.Gestito
            End If
            If gestito Then tasto = Keys.None
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.KeyDown: " & ex.Message)
        End Try
    End Sub

    ' Shift tenuto premuto: la tastiera ripete l'evento, ma conta solo la prima volta (ogni pressione, anche velocissima, conta una volta).
    Private _shiftGiu As Boolean
    Private _ultimoShift As DateTime = DateTime.MinValue

    Private Sub _tastiera_ShiftPremuto(sender As Object, e As EventArgs) Handles _tastiera.ShiftPremuto
        Try
            If Not PuoRicevere() Then Return
            Dim ora As DateTime = DateTime.UtcNow
            If _shiftGiu AndAlso (ora - _ultimoShift).TotalMilliseconds < 1200 Then
                _ultimoShift = ora
                Return
            End If
            _shiftGiu = True
            _ultimoShift = ora
            RaiseEvent SaltaRichiesto()
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.ShiftPremuto: " & ex.Message)
        End Try
    End Sub

    Private Sub _tastiera_ShiftRilasciato(sender As Object, e As EventArgs) Handles _tastiera.ShiftRilasciato
        Try
            _shiftGiu = False
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco._tastiera_ShiftRilasciato", ex)
        End Try
    End Sub

    Private Sub _tastiera_AltPremuto() Handles _tastiera.AltPremuto
        Try
            If Not PuoRicevere() OrElse _pannelli.Count > 0 Then Return
            RaiseEvent OpzioniRichieste()
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.AltPremuto: " & ex.Message)
        End Try
    End Sub

#Region "Disegno con le API di Windows, come la scrivania"

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PAINTSTRUCT
        Public hdc As IntPtr
        Public fErase As Integer
        Public rcPaint As RECT
        Public fRestore As Integer
        Public fIncUpdate As Integer
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=32)>
        Public rgbReserved As Byte()
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function BeginPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function EndPaint(hWnd As IntPtr, ByRef lpPaint As PAINTSTRUCT) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleDC(hDC As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function CreateCompatibleBitmap(hDC As IntPtr, nWidth As Integer, nHeight As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(hDC As IntPtr, hObject As IntPtr) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function DeleteDC(hDC As IntPtr) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function BitBlt(hDestDC As IntPtr, x As Integer, y As Integer, nWidth As Integer, nHeight As Integer, hSrcDC As IntPtr, xSrc As Integer, ySrc As Integer, dwRop As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function CreateSolidBrush(crColor As Integer) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function FillRect(hDC As IntPtr, ByRef lprc As RECT, hbr As IntPtr) As Integer
    End Function
    <DllImport("user32.dll")>
    Private Shared Function FrameRect(hDC As IntPtr, ByRef lprc As RECT, hbr As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(hDC As IntPtr, iBkMode As Integer) As Integer
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(hDC As IntPtr, crColor As Integer) As Integer
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function DrawText(hDC As IntPtr, lpString As String, nCount As Integer, ByRef lpRect As RECT, uFormat As UInteger) As Integer
    End Function
    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function CreateFontW(nHeight As Integer, nWidth As Integer, nEscapement As Integer, nOrientation As Integer, fnWeight As Integer,
                                        fdwItalic As UInteger, fdwUnderline As UInteger, fdwStrikeOut As UInteger, fdwCharSet As UInteger,
                                        fdwOutputPrecision As UInteger, fdwClipPrecision As UInteger, fdwQuality As UInteger,
                                        fdwPitchAndFamily As UInteger, lpszFace As String) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function CreatePen(fnPenStyle As Integer, nWidth As Integer, crColor As Integer) As IntPtr
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function Ellipse(hDC As IntPtr, left As Integer, top As Integer, right As Integer, bottom As Integer) As Boolean
    End Function
    <StructLayout(LayoutKind.Sequential)>
    Private Structure PUNTO
        Public X As Integer
        Public Y As Integer
    End Structure
    <DllImport("gdi32.dll")>
    Private Shared Function Polygon(hDC As IntPtr, punti() As PUNTO, conteggio As Integer) As Boolean
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function RoundRect(hDC As IntPtr, left As Integer, top As Integer, right As Integer, bottom As Integer, width As Integer, height As Integer) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function GetDC(hWnd As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Shared Function ReleaseDC(hWnd As IntPtr, hDC As IntPtr) As Integer
    End Function
    <DllImport("gdi32.dll")>
    Private Shared Function GetDeviceCaps(hDC As IntPtr, nIndex As Integer) As Integer
    End Function
    <DllImport("user32.dll")>
    Private Shared Function GetSystemMetrics(nIndex As Integer) As Integer
    End Function

    Private Const WM_PAINT As Integer = &HF
    Private Const WM_ERASEBKGND As Integer = &H14
    Private Const SRCCOPY As Integer = &HCC0020
    Private Const TRASPARENTE As Integer = 1
    Private Const LOGPIXELSY As Integer = 90
    Private Const SM_CYSCREEN As Integer = 1
    Private Const DT_LEFT As UInteger = &H0
    Private Const DT_CENTER As UInteger = &H1
    Private Const DT_VCENTER As UInteger = &H4
    Private Const DT_WORDBREAK As UInteger = &H10
    Private Const DT_SINGLELINE As UInteger = &H20
    Private Const DT_CALCRECT As UInteger = &H400
    Private Const DT_NOPREFIX As UInteger = &H800
    Private Const DT_END_ELLIPSIS As UInteger = &H8000
    Private Const CLEARTYPE_QUALITY As UInteger = 5

    Private _hMemDC As IntPtr
    Private _hMemBitmap As IntPtr
    Private _hOldMemBitmap As IntPtr
    Private _memLarghezza As Integer
    Private _memAltezza As Integer
    Private _hFont As IntPtr
    Private _hFontTitolo As IntPtr
    Private _hFontCarta As IntPtr
    Private _hFontCartaPiccola As IntPtr
    Private _coloreSfondo As Integer = &HFFFFFF
    Private _coloreTesto As Integer = &H0
    Private _coloreSelezione As Integer = &HFFCCAA
    Private _coloreFuoco As Integer = &HFF0000
    Private _coloreSfondoTitolo As Integer = &H303030
    Private _coloreTestoTitolo As Integer = &HFFFFFF
    Private _coloreCasella As Integer = &HF0F0F0
    Private _altezzaTitolo As Integer = 60
    Private _altezzaLinea As Integer = 24
    Private _margine As Integer = 12
    Private _dimensione As Single = 14

    Private Sub CaricaAspetto()
        Try
            Dim dpi As Integer = 96
            Dim hDC As IntPtr = GetDC(IntPtr.Zero)
            If hDC <> IntPtr.Zero Then
                dpi = GetDeviceCaps(hDC, LOGPIXELSY)
                ReleaseDC(IntPtr.Zero, hDC)
            End If
            Dim scala As Single = dpi / 96.0F
            Dim altezzaSchermo As Integer = GetSystemMetrics(SM_CYSCREEN)
            Dim dimensione As Single = 14 * scala
            If altezzaSchermo >= 2160 Then
                dimensione = 40 * scala
            ElseIf altezzaSchermo >= 1440 Then
                dimensione = 26 * scala
            ElseIf altezzaSchermo >= 1080 Then
                dimensione = 18 * scala
            ElseIf altezzaSchermo >= 768 Then
                dimensione = 15 * scala
            End If
            Dim carattere As String = "Segoe UI"
            Dim contrastoAlto As Boolean = False
            Dim config As New WGConfig()
            Dim valore As Single
            ' Sfondo e testo del tavolo di gioco (non le carte, che restano com'è una carta vera) seguono il tema di
            ' Windows; una voce di configurazione o l'alto contrasto, controllati sotto, restano piu' forti.
            If WGTema.Scuro Then
                _coloreSfondo = &H0
                _coloreTesto = &HFFFFFF
                _coloreSfondoTitolo = &H303030
                _coloreTestoTitolo = &HFFFFFF
            Else
                _coloreSfondo = &HFFFFFF
                _coloreTesto = &H0
                _coloreSfondoTitolo = &HD0D0D0
                _coloreTestoTitolo = &H0
            End If
            If config.EsisteId("FontDimensione") AndAlso Single.TryParse(config.LeggiValore("FontDimensione"), valore) Then dimensione = Math.Min(valore, dimensione * 1.4F) * scala
            If config.EsisteId("ContrastoAlto") Then Boolean.TryParse(config.LeggiValore("ContrastoAlto"), contrastoAlto)
            If config.EsisteId("MenuCarattere") AndAlso Not String.IsNullOrEmpty(config.LeggiValore("MenuCarattere")) Then carattere = config.LeggiValore("MenuCarattere")
            If config.EsisteId("ColoreSfondo") Then Integer.TryParse(config.LeggiValore("ColoreSfondo"), _coloreSfondo)
            If config.EsisteId("ColoreTesto") Then Integer.TryParse(config.LeggiValore("ColoreTesto"), _coloreTesto)
            If config.EsisteId("ColoreSelezione") Then Integer.TryParse(config.LeggiValore("ColoreSelezione"), _coloreSelezione)
            If config.EsisteId("ColoreFuoco") Then Integer.TryParse(config.LeggiValore("ColoreFuoco"), _coloreFuoco)
            If contrastoAlto Then
                _coloreSfondo = &H0
                _coloreTesto = &HFFFFFF
                _coloreSelezione = &HFF0000
                _coloreFuoco = &HFFFF00
                _coloreSfondoTitolo = &H0
                _coloreTestoTitolo = &HFFFFFF
            End If
            _coloreCasella = Sfuma(_coloreSfondo, If(Luminosita(_coloreSfondo) > 128, -18, 28))
            _dimensione = dimensione

            If _hFont <> IntPtr.Zero Then DeleteObject(_hFont)
            If _hFontTitolo <> IntPtr.Zero Then DeleteObject(_hFontTitolo)
            If _hFontCarta <> IntPtr.Zero Then DeleteObject(_hFontCarta)
            If _hFontCartaPiccola <> IntPtr.Zero Then DeleteObject(_hFontCartaPiccola)
            Dim altezzaCarattere As Integer = CInt(-Math.Round(dimensione * dpi / 72.0F))
            _hFont = CreateFontW(altezzaCarattere, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, CLEARTYPE_QUALITY, 0, carattere)
            _hFontTitolo = CreateFontW(CInt(altezzaCarattere * 1.4), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, CLEARTYPE_QUALITY, 0, carattere)
            _hFontCarta = CreateFontW(CInt(altezzaCarattere * 1.2), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, CLEARTYPE_QUALITY, 0, "Segoe UI Symbol")
            _hFontCartaPiccola = CreateFontW(CInt(altezzaCarattere * 0.9), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, CLEARTYPE_QUALITY, 0, "Segoe UI Symbol")
            _altezzaTitolo = Math.Max(40, CInt(dimensione * 2.8F))
            _altezzaLinea = Math.Max(20, CInt(dimensione * 1.9F))
            _margine = Math.Max(8, CInt(dimensione / 1.5F))
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.CaricaAspetto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Luminosita(colore As Integer) As Integer
        Try
            Return ((colore And &HFF) + ((colore >> 8) And &HFF) + ((colore >> 16) And &HFF)) \ 3
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Luminosita", ex)
            Throw
        End Try
    End Function

    Private Shared Function Sfuma(colore As Integer, delta As Integer) As Integer
        Try
            Dim r As Integer = Math.Max(0, Math.Min(255, (colore And &HFF) + delta))
            Dim g As Integer = Math.Max(0, Math.Min(255, ((colore >> 8) And &HFF) + delta))
            Dim b As Integer = Math.Max(0, Math.Min(255, ((colore >> 16) And &HFF) + delta))
            Return r Or (g << 8) Or (b << 16)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Sfuma", ex)
            Throw
        End Try
    End Function

    Protected Overrides Sub WndProc(ByRef m As Message)
        Try
            Select Case m.Msg
                Case WM_PAINT
                    Disegna()
                    m.Result = IntPtr.Zero
                    Return
                Case WM_ERASEBKGND
                    m.Result = New IntPtr(1)
                    Return
            End Select
            MyBase.WndProc(m)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.WndProc", ex)
            Throw
        End Try
    End Sub

    Protected Overrides Sub OnResize(e As EventArgs)
        Try
            MyBase.OnResize(e)
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.OnResize", ex)
            Throw
        End Try
    End Sub

    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        Try
            MyBase.OnVisibleChanged(e)
            Me.Invalidate()
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.OnVisibleChanged", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Rc(left As Integer, top As Integer, right As Integer, bottom As Integer) As RECT
        Try
            Return New RECT With {.Left = left, .Top = top, .Right = right, .Bottom = bottom}
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Rc", ex)
            Throw
        End Try
    End Function

    Private Sub Riempi(hDC As IntPtr, r As RECT, colore As Integer)
        Try
            Dim pennello As IntPtr = CreateSolidBrush(colore)
            FillRect(hDC, r, pennello)
            DeleteObject(pennello)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Riempi", ex)
            Throw
        End Try
    End Sub

    Private Sub Contorno(hDC As IntPtr, r As RECT, colore As Integer, Optional spessore As Integer = 2)
        Try
            Dim pennello As IntPtr = CreateSolidBrush(colore)
            Dim q As RECT = r
            For i As Integer = 1 To spessore
                FrameRect(hDC, q, pennello)
                q = Rc(q.Left + 1, q.Top + 1, q.Right - 1, q.Bottom - 1)
            Next
            DeleteObject(pennello)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Contorno", ex)
            Throw
        End Try
    End Sub

    ' Altezza che occupa un testo a capo nella larghezza data, con il carattere già scelto nel DC.
    Private Shared Function AltezzaTesto(hDC As IntPtr, testo As String, larghezza As Integer) As Integer
        Try
            If String.IsNullOrEmpty(testo) Then Return 0
            Dim q As RECT = Rc(0, 0, Math.Max(10, larghezza), 0)
            DrawText(hDC, testo, testo.Length, q, DT_WORDBREAK Or DT_NOPREFIX Or DT_CALCRECT)
            Return q.Bottom - q.Top
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.AltezzaTesto", ex)
            Throw
        End Try
    End Function

    Private Sub Scrivi(hDC As IntPtr, testo As String, zona As RECT, formato As UInteger)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            DrawText(hDC, testo, testo.Length, zona, formato Or DT_NOPREFIX)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Scrivi", ex)
            Throw
        End Try
    End Sub

    Private Sub Disegna()
        Try
            Dim ps As New PAINTSTRUCT()
            Dim hDC As IntPtr = BeginPaint(Me.Handle, ps)
            If hDC = IntPtr.Zero Then Return
            Try
                Dim larghezza As Integer = Me.ClientSize.Width
                Dim altezza As Integer = Me.ClientSize.Height
                If larghezza <= 0 OrElse altezza <= 0 Then Return
                If _hFont = IntPtr.Zero Then CaricaAspetto()

                If larghezza <> _memLarghezza OrElse altezza <> _memAltezza OrElse _hMemDC = IntPtr.Zero Then
                    RilasciaMemoria()
                    _hMemDC = CreateCompatibleDC(hDC)
                    If _hMemDC = IntPtr.Zero Then Return
                    _hMemBitmap = CreateCompatibleBitmap(hDC, larghezza, altezza)
                    If _hMemBitmap = IntPtr.Zero Then
                        DeleteDC(_hMemDC)
                        _hMemDC = IntPtr.Zero
                        Return
                    End If
                    _hOldMemBitmap = SelectObject(_hMemDC, _hMemBitmap)
                    _memLarghezza = larghezza
                    _memAltezza = altezza
                End If

                Dim fontPrecedente As IntPtr = IntPtr.Zero
                Try
                    Dim dc As IntPtr = _hMemDC
                    Riempi(dc, Rc(0, 0, larghezza, altezza), _coloreSfondo)
                    SetBkMode(dc, TRASPARENTE)
                    fontPrecedente = SelectObject(dc, _hFont)
                    SetTextColor(dc, _coloreTesto)

                    Dim m As Integer = _margine
                    ' ---- zona in basso: istruzione, scelte, aiuto
                    Dim altIstruzione As Integer = If(_istruzione.Length > 0, AltezzaTesto(dc, _istruzione, larghezza - 4 * m) + 2 * m, 0)
                    Dim righeScelte As Integer = Math.Min(_scelte.Count, 6)
                    Dim altRigaScelta As Integer = CInt(_altezzaLinea * 1.15)
                    Dim altScelte As Integer = righeScelte * altRigaScelta + If(righeScelte > 0, m \ 2, 0)
                    Dim altAiuto As Integer = If(_aiuto.Length > 0, _altezzaLinea + m \ 2, 0)
                    Dim inizioBasso As Integer = altezza - (altIstruzione + altScelte + altAiuto)
                    ' ---- il corpo: tavolo a sinistra, messaggi a destra
                    Dim cima As Integer = _altezzaTitolo + m
                    Dim fondo As Integer = inizioBasso - m
                    Dim larghezzaSinistra As Integer = If(MessaggiAPienaLarghezza, 0, CInt((larghezza - 3 * m) * 0.6))
                    Dim xDestra As Integer = If(MessaggiAPienaLarghezza, m, m + larghezzaSinistra + m)
                    Dim larghezzaDestra As Integer = larghezza - xDestra - m

                    Dim altTabellone As Integer = 0
                    If Not MessaggiAPienaLarghezza AndAlso _tabellone IsNot Nothing AndAlso _tabellone.Caselle.Count = 40 Then
                        Dim lato As Integer = Math.Min(larghezzaSinistra, CInt((fondo - cima) * 0.72))
                        lato = Math.Max(lato, Math.Min(larghezzaSinistra, 264))
                        DisegnaTabellone(dc, m + (larghezzaSinistra - lato) \ 2, cima - _scorri, lato)
                        altTabellone = lato + m
                    End If
                    If Not MessaggiAPienaLarghezza AndAlso _casellario IsNot Nothing Then
                        Dim usata As Integer = DisegnaCasellario(dc, m, cima - _scorri, larghezzaSinistra, fondo - cima)
                        altTabellone = usata + m
                    End If
                    If Not MessaggiAPienaLarghezza Then
                        DisegnaSezioni(dc, m, cima - _scorri + altTabellone, larghezzaSinistra)
                        _altezzaSezioni += altTabellone
                    End If
                    DisegnaMessaggi(dc, xDestra, cima, larghezzaDestra, fondo)

                    ' ---- sopra al corpo: titolo e zona in basso (coprono quello che esce dai bordi)
                    Riempi(dc, Rc(0, 0, larghezza, _altezzaTitolo), _coloreSfondoTitolo)
                    SelectObject(dc, _hFontTitolo)
                    SetTextColor(dc, _coloreTestoTitolo)
                    Scrivi(dc, _titolo, Rc(20, 0, larghezza - 20, _altezzaTitolo), DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
                    SelectObject(dc, _hFont)
                    SetTextColor(dc, _coloreTesto)

                    Riempi(dc, Rc(0, inizioBasso, larghezza, altezza), _coloreSfondo)
                    Dim y As Integer = inizioBasso
                    If altIstruzione > 0 Then
                        Dim zona As RECT = Rc(m, y, larghezza - m, y + altIstruzione)
                        Riempi(dc, zona, _coloreSelezione)
                        Riempi(dc, Rc(zona.Left, zona.Top, zona.Left + m \ 2, zona.Bottom), _coloreFuoco)
                        SetTextColor(dc, TestoSu(_coloreSelezione))
                        Scrivi(dc, _istruzione, Rc(m * 2, y + m, larghezza - 2 * m, y + altIstruzione), DT_WORDBREAK)
                        SetTextColor(dc, _coloreTesto)
                        y += altIstruzione
                    End If
                    If righeScelte > 0 Then
                        y += m \ 4
                        Dim primo As Integer = Math.Max(0, Math.Min(_indiceScelta - righeScelte + 1, _scelte.Count - righeScelte))
                        If _indiceScelta < righeScelte Then primo = 0
                        For i As Integer = 0 To righeScelte - 1
                            Dim k As Integer = primo + i
                            Dim riga As RECT = Rc(m, y, larghezza - m, y + altRigaScelta)
                            If k = _indiceScelta Then
                                Riempi(dc, riga, _coloreSelezione)
                                Contorno(dc, riga, _coloreFuoco, 2)
                                SetTextColor(dc, TestoSu(_coloreSelezione))
                            Else
                                Riempi(dc, riga, _coloreCasella)
                                SetTextColor(dc, _coloreTesto)
                            End If
                            Scrivi(dc, _scelte(k), Rc(m * 2, y, larghezza - 2 * m, y + altRigaScelta), DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
                            y += altRigaScelta
                        Next
                        SetTextColor(dc, _coloreTesto)
                        y += m \ 4
                    End If
                    If altAiuto > 0 Then
                        Scrivi(dc, _aiuto, Rc(m, y, larghezza - m, y + altAiuto), DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
                    End If

                    If _pannelli.Count > 0 Then DisegnaPannello(dc, _pannelli(_pannelli.Count - 1), larghezza, altezza)
                Finally
                    If fontPrecedente <> IntPtr.Zero Then SelectObject(_hMemDC, fontPrecedente)
                End Try

                BitBlt(hDC, 0, 0, larghezza, altezza, _hMemDC, 0, 0, SRCCOPY)
            Catch ex As Exception
                log.ScriviLog("SchermoGioco.Disegna: " & ex.Message)
            Finally
                EndPaint(Me.Handle, ps)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Disegna", ex)
            Throw
        End Try
    End Sub

    ' ---- il casellario della battaglia navale: intestazioni con le lettere (colonne) e i numeri (righe), le caselle colorate per stato, il cursore in evidenza
    Private Const COLORE_ACQUA_LIBERA As Integer = &HF8EAD6
    Private Const COLORE_NAVE As Integer = &H806E60
    Private Const COLORE_COLPO_A_VUOTO As Integer = &HFFFFFF
    Private Const COLORE_COLPITA As Integer = &H61A2F4
    Private Const COLORE_AFFONDATA As Integer = &H2222B2
    Private Const COLORE_ANTEPRIMA_BUONA As Integer = &H90EE90
    Private Const COLORE_ANTEPRIMA_CATTIVA As Integer = &HC1B6FF

    ''' <summary>Disegna il casellario in alto a sinistra (larghezza e altezza disponibili date) e dice quanto spazio ha preso.</summary>
    Private Function DisegnaCasellario(dc As IntPtr, x0 As Integer, y0 As Integer, larghezza As Integer, altezzaDisponibile As Integer) As Integer
        Try
            Dim c As CasellarioNavale = _casellario
            If c Is Nothing Then Return 0
            Dim altTitolo As Integer = If(c.Titolo.Length > 0, _altezzaLinea + _margine \ 2, 0)
            If altTitolo > 0 Then Scrivi(dc, c.Titolo, Rc(x0, y0, x0 + larghezza, y0 + _altezzaLinea), DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
            Dim cella As Integer = Math.Max(22, Math.Min(larghezza \ (c.Colonne + 1), (CInt(altezzaDisponibile * 0.78) - altTitolo) \ (c.Righe + 1)))
            Dim gx As Integer = x0
            Dim gy As Integer = y0 + altTitolo
            ' intestazioni
            For col As Integer = 0 To c.Colonne - 1
                Scrivi(dc, CasellarioNavale.Lettera(col), Rc(gx + (col + 1) * cella, gy, gx + (col + 2) * cella, gy + cella), DT_SINGLELINE Or DT_VCENTER Or DT_CENTER)
            Next
            For riga As Integer = 0 To c.Righe - 1
                Scrivi(dc, (riga + 1).ToString(), Rc(gx, gy + (riga + 1) * cella, gx + cella, gy + (riga + 2) * cella), DT_SINGLELINE Or DT_VCENTER Or DT_CENTER)
            Next
            ' anteprima della nave che si sta posizionando
            Dim anteprima As New HashSet(Of Integer)()
            Dim anteprimaBuona As Boolean = False
            If c.LunghezzaNave > 0 Then
                anteprimaBuona = c.PuoPosizionare(c.Riga, c.Colonna)
                For Each x As Integer() In c.CaselleNave(c.Riga, c.Colonna)
                    If x(0) >= 0 AndAlso x(0) < c.Righe AndAlso x(1) >= 0 AndAlso x(1) < c.Colonne Then anteprima.Add(x(0) * 100 + x(1))
                Next
            End If
            For riga As Integer = 0 To c.Righe - 1
                For col As Integer = 0 To c.Colonne - 1
                    Dim r As RECT = Rc(gx + (col + 1) * cella, gy + (riga + 1) * cella, gx + (col + 2) * cella, gy + (riga + 2) * cella)
                    Dim stato As StatoCasella = c.Stato(riga, col)
                    Dim inAnteprima As Boolean = anteprima.Contains(riga * 100 + col) AndAlso stato = StatoCasella.Vuota
                    DisegnaCasellaNavale(dc, r, stato, inAnteprima, anteprimaBuona)
                    Contorno(dc, r, &H8C6E50, 1)
                Next
            Next
            ' il cursore
            Dim cur As RECT = Rc(gx + (c.Colonna + 1) * cella, gy + (c.Riga + 1) * cella, gx + (c.Colonna + 2) * cella, gy + (c.Riga + 2) * cella)
            Contorno(dc, cur, _coloreFuoco, 3)
            Return altTitolo + (c.Righe + 1) * cella
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaCasellario", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una stella a punte (esplosione): centro, raggio esterno e interno, numero di punte.</summary>
    Private Sub Stella(dc As IntPtr, cx As Integer, cy As Integer, rEst As Integer, rInt As Integer, punte As Integer, riempimento As Integer, bordo As Integer)
        Try
            Dim p(punte * 2 - 1) As PUNTO
            For i As Integer = 0 To punte * 2 - 1
                Dim ang As Double = Math.PI * i / punte - Math.PI / 2
                Dim rr As Integer = If(i Mod 2 = 0, rEst, rInt)
                p(i).X = cx + CInt(Math.Cos(ang) * rr)
                p(i).Y = cy + CInt(Math.Sin(ang) * rr)
            Next
            Dim pennello As IntPtr = CreateSolidBrush(riempimento)
            Dim penna As IntPtr = CreatePen(0, 1, bordo)
            Dim vecchioPennello As IntPtr = SelectObject(dc, pennello)
            Dim vecchiaPenna As IntPtr = SelectObject(dc, penna)
            Polygon(dc, p, p.Length)
            SelectObject(dc, vecchioPennello)
            SelectObject(dc, vecchiaPenna)
            DeleteObject(pennello)
            DeleteObject(penna)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Stella", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Una casella del mare disegnata a mano: acqua con le onde, scafo con gli oblò, spruzzo del colpo a vuoto, esplosione sulla nave colpita, nave affondata che fuma.</summary>
    Private Sub DisegnaCasellaNavale(dc As IntPtr, r As RECT, stato As StatoCasella, anteprima As Boolean, anteprimaBuona As Boolean)
        Try
            Dim w As Integer = r.Right - r.Left
            Dim h As Integer = r.Bottom - r.Top
            Dim m As Integer = Math.Max(2, w \ 10)
            ' mare: fondo azzurro e due onde chiare
            Dim mare As Integer = If(stato = StatoCasella.Affondata, &H5A3A1E, &HE6B478)
            Riempi(dc, r, mare)
            If stato <> StatoCasella.Affondata Then
                Riempi(dc, Rc(r.Left + w \ 6, r.Top + h \ 4, r.Left + w \ 2, r.Top + h \ 4 + Math.Max(1, h \ 14)), &HF5D7A6)
                Riempi(dc, Rc(r.Left + w \ 2, r.Top + h * 3 \ 4, r.Right - w \ 6, r.Top + h * 3 \ 4 + Math.Max(1, h \ 14)), &HF5D7A6)
            End If
            Select Case stato
                Case StatoCasella.Nave, StatoCasella.Colpita
                    SoloScafo(dc, r, &H8C7A6A, &H4A3F35, m)
                    If stato = StatoCasella.Colpita Then
                        Stella(dc, r.Left + w \ 2, r.Top + h \ 2, w * 4 \ 10, w \ 6, 8, &H1E64F0, &H0F32A0)
                        Stella(dc, r.Left + w \ 2, r.Top + h \ 2, w \ 5, w \ 11, 7, &H3CD2FF, &H1E96F0)
                    End If
                Case StatoCasella.Affondata
                    SoloScafo(dc, r, &H303030, &H101010, m)
                    Cerchio(dc, Rc(r.Left + w \ 5, r.Top + h \ 6, r.Left + w \ 2, r.Top + h \ 2 + h \ 8), &H808080, &H505050, 1)
                    Cerchio(dc, Rc(r.Left + w \ 2, r.Top + h \ 10, r.Right - w \ 5, r.Top + h \ 3 + h \ 8), &HA0A0A0, &H606060, 1)
                    Stella(dc, r.Left + w \ 2, r.Top + h * 2 \ 3, w \ 4, w \ 9, 6, &H2020C8, &H101080)
                Case StatoCasella.Acqua
                    ' colpo a vuoto: spruzzo con cerchi bianchi che si allargano
                    Cerchio(dc, Rc(r.Left + w \ 6, r.Top + h \ 6, r.Right - w \ 6, r.Bottom - h \ 6), &HF5D7A6, &HFFFFFF, Math.Max(1, w \ 14))
                    Cerchio(dc, Rc(r.Left + w \ 3, r.Top + h \ 3, r.Right - w \ 3, r.Bottom - h \ 3), &HFFFFFF, &HE6B478, 1)
                    Cerchio(dc, Rc(r.Left + w * 7 \ 16, r.Top + h * 7 \ 16, r.Right - w * 7 \ 16, r.Bottom - h * 7 \ 16), &HE6B478, &HFFFFFF, 1)
            End Select
            If anteprima Then
                ' la nave che si sta posizionando: sagoma verde se ci sta, rosa se non ci sta
                SoloScafo(dc, r, If(anteprimaBuona, &H5CC85C, &H7878E6), If(anteprimaBuona, &H206020, &H202090), m)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaCasellaNavale", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un pezzo di scafo: corpo arrotondato, ponte più scuro e due oblò.</summary>
    Private Sub SoloScafo(dc As IntPtr, r As RECT, colore As Integer, bordo As Integer, m As Integer)
        Try
            Dim w As Integer = r.Right - r.Left
            Dim h As Integer = r.Bottom - r.Top
            Arrotondato(dc, Rc(r.Left + m \ 2, r.Top + h \ 5, r.Right - m \ 2, r.Bottom - h \ 5), colore, bordo, 1, Math.Max(3, w \ 8))
            ' il ponte: un rettangolo più chiaro; sotto, tre rivetti
            Dim chiaro As Integer
            Dim cr As Integer = Math.Min(255, (colore And &HFF) + 35)
            Dim cg As Integer = Math.Min(255, ((colore >> 8) And &HFF) + 35)
            Dim cb As Integer = Math.Min(255, ((colore >> 16) And &HFF) + 35)
            chiaro = cr Or (cg << 8) Or (cb << 16)
            Arrotondato(dc, Rc(r.Left + m * 2, r.Top + h \ 5 + m, r.Right - m * 2, r.Top + h \ 2), chiaro, bordo, 1, Math.Max(2, w \ 12))
            Dim o As Integer = Math.Max(2, w \ 12)
            For k As Integer = 1 To 3
                Dim cx As Integer = r.Left + w * k \ 4
                Dim cy As Integer = r.Bottom - h \ 3
                Cerchio(dc, Rc(cx - o \ 2, cy - o \ 2, cx + o \ 2 + 1, cy + o \ 2 + 1), &HE6F0F5, &HE6F0F5, 1)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.SoloScafo", ex)
            Throw
        End Try
    End Sub

    ' Colore del testo leggibile sopra un fondo.
    Private Function TestoSu(fondo As Integer) As Integer
        Try
            Return If(Luminosita(fondo) > 140, &H0, &HFFFFFF)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.TestoSu", ex)
            Throw
        End Try
    End Function

    ' ---- il tabellone del Monopoli: 11 x 11 caselle, angoli più grandi, dadi al centro, pedine con il numero del giocatore
    Private Function FontTabellone(altezzaPixel As Integer, peso As Integer) As IntPtr
        Try
            Return CreateFontW(-Math.Max(8, altezzaPixel), 0, 0, 0, peso, 0, 0, 0, 1, 0, 0, CLEARTYPE_QUALITY, 0, "Segoe UI")
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.FontTabellone", ex)
            Throw
        End Try
    End Function

    Private Sub Cerchio(dc As IntPtr, r As RECT, riempimento As Integer, bordo As Integer, spessore As Integer)
        Try
            Dim pennello As IntPtr = CreateSolidBrush(riempimento)
            Dim penna As IntPtr = CreatePen(0, Math.Max(1, spessore), bordo)
            Dim vecchioPennello As IntPtr = SelectObject(dc, pennello)
            Dim vecchiaPenna As IntPtr = SelectObject(dc, penna)
            Ellipse(dc, r.Left, r.Top, r.Right, r.Bottom)
            SelectObject(dc, vecchioPennello)
            SelectObject(dc, vecchiaPenna)
            DeleteObject(pennello)
            DeleteObject(penna)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Cerchio", ex)
            Throw
        End Try
    End Sub

    Private Sub Arrotondato(dc As IntPtr, r As RECT, riempimento As Integer, bordo As Integer, spessore As Integer, raggio As Integer)
        Try
            Dim pennello As IntPtr = CreateSolidBrush(riempimento)
            Dim penna As IntPtr = CreatePen(0, Math.Max(1, spessore), bordo)
            Dim vecchioPennello As IntPtr = SelectObject(dc, pennello)
            Dim vecchiaPenna As IntPtr = SelectObject(dc, penna)
            RoundRect(dc, r.Left, r.Top, r.Right, r.Bottom, raggio, raggio)
            SelectObject(dc, vecchioPennello)
            SelectObject(dc, vecchiaPenna)
            DeleteObject(pennello)
            DeleteObject(penna)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Arrotondato", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Il rettangolo della casella i (0 = via, in basso a destra; si va in senso orario). c = lato degli angoli, t = larghezza delle altre.</summary>
    Private Shared Function RettCasella(i As Integer, x0 As Integer, y0 As Integer, c As Integer, t As Integer) As RECT
        Try
            Dim lato As Integer = 2 * c + 9 * t
            Select Case i
                Case 0 : Return Rc(x0 + lato - c, y0 + lato - c, x0 + lato, y0 + lato)
                Case 1 To 9 : Return Rc(x0 + lato - c - i * t, y0 + lato - c, x0 + lato - c - (i - 1) * t, y0 + lato)
                Case 10 : Return Rc(x0, y0 + lato - c, x0 + c, y0 + lato)
                Case 11 To 19 : Return Rc(x0, y0 + lato - c - (i - 10) * t, x0 + c, y0 + lato - c - (i - 11) * t)
                Case 20 : Return Rc(x0, y0, x0 + c, y0 + c)
                Case 21 To 29 : Return Rc(x0 + c + (i - 21) * t, y0, x0 + c + (i - 20) * t, y0 + c)
                Case 30 : Return Rc(x0 + lato - c, y0, x0 + lato, y0 + c)
                Case Else : Return Rc(x0 + lato - c, y0 + c + (i - 31) * t, x0 + lato, y0 + c + (i - 30) * t)
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.RettCasella", ex)
            Throw
        End Try
    End Function

    Private Sub DisegnaTabellone(dc As IntPtr, x0 As Integer, y0 As Integer, lato As Integer)
        Try
            Dim tab As TabelloneGioco = _tabellone
            If tab Is Nothing OrElse tab.Caselle.Count <> 40 Then Return
            Dim t As Integer = Math.Max(10, lato \ 12)
            Dim c As Integer = t * 3 \ 2
            Dim chiaro As Boolean = Luminosita(_coloreSfondo) > 128
            Dim coloreCasella As Integer = If(chiaro, &HD0E6CD, &H202020)
            Dim coloreCentro As Integer = If(chiaro, &HE4F0DC, &H101010)
            Dim coloreIpoteca As Integer = If(chiaro, &HA8A8A8, &H505050)
            Dim inchiostro As Integer = TestoSu(coloreCasella)
            Dim fCasella As IntPtr = FontTabellone(CInt(t * 0.23), 600)
            Dim fPedina As IntPtr = FontTabellone(CInt(t * 0.3), 800)
            Dim fCentro As IntPtr = FontTabellone(CInt(t * 0.75), 800)
            Dim fTiro As IntPtr = FontTabellone(CInt(t * 0.4), 700)
            Dim fPrecedente As IntPtr = IntPtr.Zero
            Try
                Dim lato12 As Integer = 2 * c + 9 * t
                Riempi(dc, Rc(x0, y0, x0 + lato12, y0 + lato12), coloreCentro)
                fPrecedente = SelectObject(dc, fCasella)

                ' ---- le caselle
                For i As Integer = 0 To 39
                    Dim cs As CasellaTabellone = tab.Caselle(i)
                    Dim r As RECT = RettCasella(i, x0, y0, c, t)
                    Dim angolo As Boolean = (i Mod 10 = 0)
                    Riempi(dc, r, If(cs.Ipotecata, coloreIpoteca, coloreCasella))
                    Dim fascia As Integer = Math.Max(3, CInt(c * 0.24))
                    Dim testo As RECT = r
                    Dim fasciaColore As RECT = r
                    Dim fasciaPosto As RECT = r
                    If Not angolo Then
                        Dim b As Integer = Math.Max(2, fascia \ 3)
                        Select Case i
                            Case 1 To 9 ' in basso: il colore in alto, il proprietario in basso
                                fasciaColore = Rc(r.Left, r.Top, r.Right, r.Top + fascia)
                                fasciaPosto = Rc(r.Left, r.Bottom - b, r.Right, r.Bottom)
                                testo = Rc(r.Left, r.Top + fascia, r.Right, r.Bottom - b)
                            Case 11 To 19 ' a sinistra: il colore a destra
                                fasciaColore = Rc(r.Right - fascia, r.Top, r.Right, r.Bottom)
                                fasciaPosto = Rc(r.Left, r.Top, r.Left + b, r.Bottom)
                                testo = Rc(r.Left + b, r.Top, r.Right - fascia, r.Bottom)
                            Case 21 To 29 ' in alto: il colore in basso
                                fasciaColore = Rc(r.Left, r.Bottom - fascia, r.Right, r.Bottom)
                                fasciaPosto = Rc(r.Left, r.Top, r.Right, r.Top + b)
                                testo = Rc(r.Left, r.Top + b, r.Right, r.Bottom - fascia)
                            Case Else ' a destra: il colore a sinistra
                                fasciaColore = Rc(r.Left, r.Top, r.Left + fascia, r.Bottom)
                                fasciaPosto = Rc(r.Right - b, r.Top, r.Right, r.Bottom)
                                testo = Rc(r.Left + fascia, r.Top, r.Right - b, r.Bottom)
                        End Select
                        If cs.Colore >= 0 Then
                            Riempi(dc, fasciaColore, cs.Colore)
                            Contorno(dc, fasciaColore, inchiostro, 1)
                            DisegnaEdifici(dc, cs.Edifici, i, fasciaColore, fascia)
                        End If
                        If cs.Proprietario >= 0 AndAlso cs.Proprietario < tab.Pedine.Count Then
                            Riempi(dc, fasciaPosto, tab.Pedine(cs.Proprietario).Colore)
                        End If
                    End If
                    Contorno(dc, r, inchiostro, 1)
                    SetTextColor(dc, If(cs.Ipotecata, TestoSu(coloreIpoteca), inchiostro))
                    Dim margine As Integer = Math.Max(1, t \ 14)
                    testo = Rc(testo.Left + margine, testo.Top + margine, testo.Right - margine, testo.Bottom - margine)
                    ' l'icona (stazione, società, imprevisti, tassa, angoli...) sta sopra al nome
                    If cs.Icona.Length > 0 Then
                        Dim largTesto As Integer = testo.Right - testo.Left
                        Dim altTestoCasella As Integer = testo.Bottom - testo.Top
                        Dim lIcona As Integer = Math.Min(largTesto, CInt(altTestoCasella * If(angolo, 0.6, 0.5)))
                        If lIcona >= 10 AndAlso DisegnaImmagine(dc, "icona-" & cs.Icona, testo.Left + (largTesto - lIcona) \ 2, testo.Top, lIcona, lIcona) Then
                            testo = Rc(testo.Left, testo.Top + lIcona + margine, testo.Right, testo.Bottom)
                        End If
                    End If
                    ' DrawText centra in verticale solo il testo su una riga: per i nomi a capo si calcola a mano.
                    Dim altNome As Integer = AltezzaTesto(dc, cs.Nome, testo.Right - testo.Left)
                    If altNome > 0 AndAlso altNome < testo.Bottom - testo.Top Then
                        Dim giu As Integer = (testo.Bottom - testo.Top - altNome) \ 2
                        testo = Rc(testo.Left, testo.Top + giu, testo.Right, testo.Bottom)
                    End If
                    Scrivi(dc, cs.Nome, testo, DT_CENTER Or DT_WORDBREAK)
                Next

                ' ---- il centro: titolo e dadi
                Dim cx As Integer = x0 + lato12 \ 2
                Dim cy As Integer = y0 + lato12 \ 2
                Dim zonaCentro As RECT = Rc(x0 + c, y0 + c, x0 + lato12 - c, y0 + lato12 - c)
                SelectObject(dc, fCentro)
                SetTextColor(dc, TestoSu(coloreCentro))
                Scrivi(dc, "MONOPOLI", Rc(zonaCentro.Left, zonaCentro.Top + t \ 2, zonaCentro.Right, zonaCentro.Top + CInt(t * 1.8)), DT_CENTER Or DT_VCENTER Or DT_SINGLELINE)
                If tab.Dado1 > 0 AndAlso tab.Dado2 > 0 Then
                    Dim rotola As Boolean = _dadiFotogrammi > 0
                    Dim v1 As Integer = If(rotola, _dadiMostrati(0), tab.Dado1)
                    Dim v2 As Integer = If(rotola, _dadiMostrati(1), tab.Dado2)
                    Dim d As Integer = CInt(t * 1.9)
                    Dim vuoto As Integer = CInt(d * 0.4)
                    Dim xa As Integer = cx - vuoto \ 2 - d
                    Dim xb As Integer = cx + vuoto \ 2
                    Dim ya As Integer = cy - d \ 2 + t \ 4
                    Dim ja As Integer = 0, jb As Integer = 0, ka As Integer = 0, kb As Integer = 0
                    If rotola Then
                        Dim s As Integer = Math.Max(2, d \ 7)
                        ja = _casuale.Next(-s, s + 1) : ka = _casuale.Next(-s, s + 1)
                        jb = _casuale.Next(-s, s + 1) : kb = _casuale.Next(-s, s + 1)
                    End If
                    DisegnaDado(dc, xa + ja, ya + ka, d, v1)
                    DisegnaDado(dc, xb + jb, ya + kb, d, v2)
                    SelectObject(dc, fTiro)
                    SetTextColor(dc, TestoSu(coloreCentro))
                    Dim didascalia As String = If(rotola, "Tiro i dadi…", "Tiro: " & v1.ToString() & " + " & v2.ToString() & " = " & (v1 + v2).ToString())
                    Scrivi(dc, didascalia, Rc(zonaCentro.Left, ya + d + t \ 4, zonaCentro.Right, ya + d + CInt(t * 1.1)), DT_CENTER Or DT_VCENTER Or DT_SINGLELINE)
                End If

                ' ---- le pedine (in tondo, con il numero del giocatore; il giocatore di turno ha un anello)
                SelectObject(dc, fPedina)
                Dim perCasella As New Dictionary(Of Integer, List(Of Integer))()
                For i As Integer = 0 To Math.Min(tab.Pedine.Count, _posVisibili.Count) - 1
                    If tab.Pedine(i).Fuori Then Continue For
                    Dim pos As Integer = ((_posVisibili(i) Mod 40) + 40) Mod 40
                    If Not perCasella.ContainsKey(pos) Then perCasella(pos) = New List(Of Integer)()
                    perCasella(pos).Add(i)
                Next
                For Each coppia As KeyValuePair(Of Integer, List(Of Integer)) In perCasella
                    Dim r As RECT = RettCasella(coppia.Key, x0, y0, c, t)
                    Dim ccx As Integer = (r.Left + r.Right) \ 2
                    Dim ccy As Integer = (r.Top + r.Bottom) \ 2 + If(coppia.Key Mod 10 = 0, 0, t \ 8)
                    Dim n As Integer = coppia.Value.Count
                    Dim colonne As Integer = Math.Min(2, n)
                    Dim righe As Integer = (n + 1) \ 2
                    Dim diametro As Integer = Math.Max(10, CInt(t * If(n > 2, 0.5, 0.6)))
                    Dim passo As Integer = CInt(diametro * 1.05)
                    For k As Integer = 0 To n - 1
                        Dim idx As Integer = coppia.Value(k)
                        Dim px As Integer = ccx + CInt(((k Mod 2) - (colonne - 1) / 2.0) * passo)
                        Dim py As Integer = ccy + CInt(((k \ 2) - (righe - 1) / 2.0) * passo)
                        Dim q As RECT = Rc(px - diametro \ 2, py - diametro \ 2, px + diametro \ 2, py + diametro \ 2)
                        Dim p As PedinaTabellone = tab.Pedine(idx)
                        ' la pedina è un'immagine (una forma e un colore per giocatore); se manca, il cerchio con il numero di prima
                        Dim lPedina As Integer = diametro + diametro \ 3
                        Dim rPedina As RECT = Rc(px - lPedina \ 2, py - lPedina \ 2, px + lPedina \ 2, py + lPedina \ 2)
                        If p.Attiva Then
                            ' un anello intorno alla pedina del giocatore di turno
                            Cerchio(dc, Rc(rPedina.Left - 3, rPedina.Top - 3, rPedina.Right + 3, rPedina.Bottom + 3), _coloreFuoco, _coloreFuoco, 1)
                            Cerchio(dc, Rc(rPedina.Left, rPedina.Top, rPedina.Right, rPedina.Bottom), coloreCasella, coloreCasella, 1)
                        End If
                        If Not DisegnaImmagine(dc, "pedina-" & (idx + 1).ToString(), rPedina.Left, rPedina.Top, lPedina, lPedina) Then
                            Cerchio(dc, q, p.Colore, &H0, 2)
                            SetTextColor(dc, TestoSu(p.Colore))
                            Scrivi(dc, (idx + 1).ToString(), q, DT_CENTER Or DT_VCENTER Or DT_SINGLELINE)
                        End If
                    Next
                Next
                Contorno(dc, Rc(x0, y0, x0 + lato12, y0 + lato12), inchiostro, 2)
            Finally
                If fPrecedente <> IntPtr.Zero Then SelectObject(dc, fPrecedente)
                SetTextColor(dc, _coloreTesto)
                DeleteObject(fCasella)
                DeleteObject(fPedina)
                DeleteObject(fCentro)
                DeleteObject(fTiro)
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaTabellone", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Piccoli quadrati verdi (case) o uno rosso (albergo) sulla fascia di colore.</summary>
    Private Sub DisegnaEdifici(dc As IntPtr, edifici As Integer, casella As Integer, fascia As RECT, spessore As Integer)
        Try
            If edifici <= 0 Then Return
            Dim orizzontale As Boolean = (casella >= 1 AndAlso casella <= 9) OrElse (casella >= 21 AndAlso casella <= 29)
            Dim lunghezza As Integer = If(orizzontale, fascia.Right - fascia.Left, fascia.Bottom - fascia.Top)
            Dim n As Integer = If(edifici >= 5, 1, edifici)
            ' con le immagini le case stanno una accanto all'altra; se sono troppo piccole (schermo piccolo) restano i quadratini
            Dim nomeImm As String = If(edifici >= 5, "albergo", "casa")
            Dim latoImm As Integer = Math.Min(spessore, lunghezza \ n)
            If latoImm >= 9 AndAlso ImmagineMonopoli(nomeImm, latoImm, latoImm) IsNot Nothing Then
                Dim inizioImm As Integer = (lunghezza - n * latoImm) \ 2
                For k As Integer = 0 To n - 1
                    Dim spostamento As Integer = inizioImm + k * latoImm
                    If orizzontale Then
                        DisegnaImmagine(dc, nomeImm, fascia.Left + spostamento, fascia.Top + (spessore - latoImm) \ 2, latoImm, latoImm)
                    Else
                        DisegnaImmagine(dc, nomeImm, fascia.Left + (spessore - latoImm) \ 2, fascia.Top + spostamento, latoImm, latoImm)
                    End If
                Next
                Return
            End If
            Dim lato As Integer = Math.Max(3, Math.Min(spessore - 2, lunghezza \ (n * 2 + 1)))
            Dim usato As Integer = n * lato + (n - 1) * 2
            Dim inizio As Integer = (lunghezza - usato) \ 2
            For k As Integer = 0 To n - 1
                Dim off As Integer = inizio + k * (lato + 2)
                Dim q As RECT
                If orizzontale Then
                    q = Rc(fascia.Left + off, fascia.Top + (spessore - lato) \ 2, fascia.Left + off + lato, fascia.Top + (spessore - lato) \ 2 + lato)
                Else
                    q = Rc(fascia.Left + (spessore - lato) \ 2, fascia.Top + off, fascia.Left + (spessore - lato) \ 2 + lato, fascia.Top + off + lato)
                End If
                Riempi(dc, q, If(edifici >= 5, &H2020E0, &H30B030))
                Contorno(dc, q, &H0, 1)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaEdifici", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Un dado bianco con i puntini del valore (1-6).</summary>
    Private Sub DisegnaDado(dc As IntPtr, x As Integer, y As Integer, d As Integer, valore As Integer)
        Try
            If valore >= 1 AndAlso valore <= 6 AndAlso d >= 12 AndAlso DisegnaImmagine(dc, "dado-" & valore.ToString(), x, y, d, d) Then Return
            Arrotondato(dc, Rc(x, y, x + d, y + d), &HFFFFFF, &H202020, Math.Max(2, d \ 22), d \ 4)
            Dim p As Integer = Math.Max(4, d \ 5)
            Dim sin As Integer = x + d \ 4 - p \ 2 + p \ 6
            Dim cen As Integer = x + d \ 2 - p \ 2
            Dim des As Integer = x + d - d \ 4 - p \ 2 - p \ 6 + 1
            Dim alto As Integer = y + d \ 4 - p \ 2 + p \ 6
            Dim med As Integer = y + d \ 2 - p \ 2
            Dim bas As Integer = y + d - d \ 4 - p \ 2 - p \ 6 + 1
            Dim pos As New List(Of Point)()
            If valore = 1 OrElse valore = 3 OrElse valore = 5 Then pos.Add(New Point(cen, med))
            If valore >= 2 Then
                pos.Add(New Point(sin, alto))
                pos.Add(New Point(des, bas))
            End If
            If valore >= 4 Then
                pos.Add(New Point(des, alto))
                pos.Add(New Point(sin, bas))
            End If
            If valore = 6 Then
                pos.Add(New Point(sin, med))
                pos.Add(New Point(des, med))
            End If
            For Each q As Point In pos
                Cerchio(dc, Rc(q.X, q.Y, q.X + p, q.Y + p), &H202020, &H202020, 1)
            Next
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaDado", ex)
            Throw
        End Try
    End Sub

    Private Sub DisegnaSezioni(dc As IntPtr, x As Integer, yInizio As Integer, larghezza As Integer)
        Try
            Dim m As Integer = _margine
            Dim y As Integer = yInizio
            Dim altIntestazione As Integer = CInt(_altezzaLinea * 1.2)
            For Each s As SezioneGioco In _sezioni
                Riempi(dc, Rc(x, y, x + larghezza, y + altIntestazione), _coloreSfondoTitolo)
                SetTextColor(dc, _coloreTestoTitolo)
                Scrivi(dc, s.Titolo, Rc(x + m, y, x + larghezza - m, y + altIntestazione), DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
                SetTextColor(dc, _coloreTesto)
                y += altIntestazione + 2
                For Each riga As RigaGioco In s.Righe
                    y += DisegnaRiga(dc, riga, x, y, larghezza)
                Next
                y += m
            Next
            _altezzaSezioni = y - yInizio + _scorri
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaSezioni", ex)
            Throw
        End Try
    End Sub

    Private Function DisegnaRiga(dc As IntPtr, riga As RigaGioco, x As Integer, y As Integer, larghezza As Integer) As Integer
        Try
            Dim m As Integer = _margine
            If riga.Carte.Count > 0 Then
                Dim l As Integer = If(riga.Piccola, CInt(_altezzaLinea * 1.6), CInt(_altezzaLinea * 2.4))
                Dim a As Integer = CInt(l * 1.4)
                Dim passo As Integer = l + 4
                Dim perRiga As Integer = Math.Max(1, (larghezza - m) \ passo)
                Dim yy As Integer = y
                If riga.Testo.Length > 0 Then
                    Dim h As Integer = AltezzaTesto(dc, riga.Testo, larghezza - m)
                    Scrivi(dc, riga.Testo, Rc(x + m \ 2, yy, x + larghezza, yy + h), DT_WORDBREAK)
                    yy += h + 2
                End If
                Dim i As Integer = 0
                For Each c As CartaGioco In riga.Carte
                    Dim colonna As Integer = i Mod perRiga
                    If i > 0 AndAlso colonna = 0 Then yy += a + 4
                    DisegnaCarta(dc, c, x + m \ 2 + colonna * passo, yy, l, a, riga.Piccola)
                    i += 1
                Next
                Return yy + a + 6 - y
            End If
            Dim sinistra As Integer = x + m \ 2
            If riga.Colore >= 0 Then
                Dim lato As Integer = CInt(_altezzaLinea * 0.7)
                Dim q As RECT = Rc(sinistra, y + (_altezzaLinea - lato) \ 2, sinistra + lato, y + (_altezzaLinea - lato) \ 2 + lato)
                Riempi(dc, q, riga.Colore)
                Contorno(dc, q, _coloreTesto, 1)
                sinistra += lato + m \ 2
            End If
            Dim altTesto As Integer = Math.Max(_altezzaLinea, AltezzaTesto(dc, riga.Testo, x + larghezza - sinistra - m))
            If riga.Evidenza Then
                Riempi(dc, Rc(x, y, x + larghezza, y + altTesto), _coloreCasella)
                Riempi(dc, Rc(x, y, x + m \ 3, y + altTesto), _coloreFuoco)
            End If
            Scrivi(dc, riga.Testo, Rc(sinistra, y, x + larghezza - m \ 2, y + altTesto), DT_WORDBREAK)
            Return altTesto + 1
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaRiga", ex)
            Throw
        End Try
    End Function

    ' ---- le immagini delle carte (incluse nella libreria come risorse), ridimensionate una volta sola e tenute in memoria
    Private Shared ReadOnly _originali As New Dictionary(Of String, Bitmap)()
    Private ReadOnly _cacheCarte As New Dictionary(Of String, IntPtr)()

    Private Shared Function Originale(chiave As String) As Bitmap
        Try
            Return OriginaleRisorsa("carte." & chiave & ".png")
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Originale", ex)
            Throw
        End Try
    End Function

    ''' <summary>Un'immagine incorporata nella libreria (carte.7H.png, monopoli.dado-3.png...), tenuta in memoria. Nothing se manca.</summary>
    Private Shared Function OriginaleRisorsa(nomeRisorsa As String) As Bitmap
        Try
            SyncLock _originali
                Dim b As Bitmap = Nothing
                If _originali.TryGetValue(nomeRisorsa, b) Then Return b
                Try
                    Using flusso As System.IO.Stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(nomeRisorsa)
                        If flusso IsNot Nothing Then
                            Using temp As New Bitmap(flusso)
                                b = New Bitmap(temp)
                            End Using
                        End If
                    End Using
                Catch ex As Exception
                    log.ScriviLog("SchermoGioco.OriginaleRisorsa (" & nomeRisorsa & "): " & ex.Message)
                End Try
                _originali(nomeRisorsa) = b
                Return b
            End SyncLock
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.OriginaleRisorsa", ex)
            Throw
        End Try
    End Function

    ' ---- le immagini del Monopoli (dadi, case, albergo, pedine, icone delle caselle): si disegnano con GDI+ sopra quello che c'è già, così restano trasparenti.
    ' Le fa scripts\genera-monopoli.py e stanno nella libreria come risorse "monopoli.NOME.png". Se una manca si disegna come prima, con le forme.
    Private ReadOnly _cacheMon As New Dictionary(Of String, Bitmap)()

    Private Function ImmagineMonopoli(nome As String, l As Integer, a As Integer) As Bitmap
        Try
            If l < 4 OrElse a < 4 Then Return Nothing
            Dim k As String = nome & "|" & l.ToString() & "x" & a.ToString()
            Dim b As Bitmap = Nothing
            If _cacheMon.TryGetValue(k, b) Then Return b
            Dim orig As Bitmap = OriginaleRisorsa("monopoli." & nome & ".png")
            If orig Is Nothing Then Return Nothing
            If _cacheMon.Count > 200 Then
                For Each vecchia As Bitmap In _cacheMon.Values
                    vecchia.Dispose()
                Next
                _cacheMon.Clear()
            End If
            b = New Bitmap(l, a, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            Using g As Graphics = Graphics.FromImage(b)
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality
                g.DrawImage(orig, 0, 0, l, a)
            End Using
            _cacheMon(k) = b
            Return b
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.ImmagineMonopoli (" & nome & "): " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>Disegna l'immagine (trasparente) nel quadrato dato. Falso se l'immagine non c'è: chi chiama disegna la forma di prima.</summary>
    Private Function DisegnaImmagine(dc As IntPtr, nome As String, x As Integer, y As Integer, l As Integer, a As Integer) As Boolean
        Try
            Dim b As Bitmap = ImmagineMonopoli(nome, l, a)
            If b Is Nothing Then Return False
            Try
                Using g As Graphics = Graphics.FromHdc(dc)
                    g.DrawImage(b, x, y, l, a)
                End Using
                Return True
            Catch ex As Exception
                log.ScriviLog("SchermoGioco.DisegnaImmagine (" & nome & "): " & ex.Message)
                Return False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaImmagine", ex)
            Throw
        End Try
    End Function

    Private Function ImmagineCarta(chiave As String, l As Integer, a As Integer) As IntPtr
        Try
            Dim k As String = chiave & "|" & l.ToString() & "x" & a.ToString() & "|" & _coloreSfondo.ToString()
            Dim hb As IntPtr
            If _cacheCarte.TryGetValue(k, hb) Then Return hb
            Dim orig As Bitmap = Originale(chiave)
            If orig Is Nothing Then
                ' una carta speciale (7_denari_settebello, R_denari_rebello, A_coppe_pigliatutto) senza immagine propria si disegna con quella normale
                Dim ultimo As Integer = chiave.LastIndexOf("_"c)
                If ultimo > 0 AndAlso chiave.IndexOf("_"c) <> ultimo Then orig = Originale(chiave.Substring(0, ultimo))
            End If
            If orig Is Nothing OrElse l < 8 OrElse a < 8 Then Return IntPtr.Zero
            If _cacheCarte.Count > 300 Then
                For Each v As IntPtr In _cacheCarte.Values
                    DeleteObject(v)
                Next
                _cacheCarte.Clear()
            End If
            Using b2 As New Bitmap(l, a, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                Using g As Graphics = Graphics.FromImage(b2)
                    g.Clear(Color.FromArgb(_coloreSfondo And &HFF, (_coloreSfondo >> 8) And &HFF, (_coloreSfondo >> 16) And &HFF))
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality
                    g.DrawImage(orig, 0, 0, l, a)
                End Using
                hb = b2.GetHbitmap()
            End Using
            _cacheCarte(k) = hb
            Return hb
        Catch ex As Exception
            log.ScriviLog("SchermoGioco.ImmagineCarta: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Sub DisegnaCarta(dc As IntPtr, c As CartaGioco, x As Integer, y As Integer, l As Integer, a As Integer, piccola As Boolean)
        Try
            If Not String.IsNullOrEmpty(c.Immagine) Then
                Dim hb As IntPtr = ImmagineCarta(c.Immagine, l, a)
                If hb <> IntPtr.Zero Then
                    Dim memoria As IntPtr = CreateCompatibleDC(dc)
                    If memoria <> IntPtr.Zero Then
                        Dim vecchia As IntPtr = SelectObject(memoria, hb)
                        BitBlt(dc, x, y, l, a, memoria, 0, 0, SRCCOPY)
                        SelectObject(memoria, vecchia)
                        DeleteDC(memoria)
                        If c.Evidenziata Then Contorno(dc, Rc(x - 2, y - 2, x + l + 2, y + a + 2), _coloreFuoco, 3)
                        Return
                    End If
                End If
            End If
            Dim q As RECT = Rc(x, y, x + l, y + a)
            Dim sfondo As Integer = If(c.Dorso, &HA05020, If(c.Jolly, &H66E0FF, &HFFFFFF))
            Riempi(dc, q, sfondo)
            Contorno(dc, q, &H303030, 1)
            If c.Evidenziata Then Contorno(dc, Rc(q.Left - 1, q.Top - 1, q.Right + 1, q.Bottom + 1), _coloreFuoco, 3)
            If c.Dorso Then
                Contorno(dc, Rc(q.Left + 4, q.Top + 4, q.Right - 4, q.Bottom - 4), &HFFFFFF, 1)
                Return
            End If
            Dim precedente As IntPtr = SelectObject(dc, If(piccola, _hFontCartaPiccola, _hFontCarta))
            SetTextColor(dc, If(c.Rosso, &H2020C0, &H202020))
            Scrivi(dc, c.Testo, q, DT_CENTER Or DT_VCENTER Or DT_SINGLELINE)
            SelectObject(dc, precedente)
            SetTextColor(dc, _coloreTesto)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaCarta", ex)
            Throw
        End Try
    End Sub

    Private Sub DisegnaMessaggi(dc As IntPtr, x As Integer, cima As Integer, larghezza As Integer, fondo As Integer)
        Try
            Dim m As Integer = _margine
            Dim altIntestazione As Integer = CInt(_altezzaLinea * 1.2)
            Riempi(dc, Rc(x, cima, x + larghezza, cima + altIntestazione), _coloreSfondoTitolo)
            SetTextColor(dc, _coloreTestoTitolo)
            Scrivi(dc, TitoloMessaggi, Rc(x + m, cima, x + larghezza - m, cima + altIntestazione), DT_SINGLELINE Or DT_VCENTER)
            SetTextColor(dc, _coloreTesto)
            Dim yFondo As Integer = fondo
            Dim minimo As Integer = cima + altIntestazione + 4
            For i As Integer = _messaggi.Count - 1 To 0 Step -1
                Dim h As Integer = AltezzaTesto(dc, _messaggi(i), larghezza - m)
                If yFondo - h < minimo Then Exit For
                yFondo -= h
                If i = _messaggi.Count - 1 Then
                    Riempi(dc, Rc(x, yFondo, x + larghezza, yFondo + h), _coloreCasella)
                End If
                Scrivi(dc, _messaggi(i), Rc(x + m \ 2, yFondo, x + larghezza - m \ 2, yFondo + h), DT_WORDBREAK)
                yFondo -= 4
            Next
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaMessaggi", ex)
            Throw
        End Try
    End Sub

    Private Sub DisegnaPannello(dc As IntPtr, p As PannelloGioco, larghezza As Integer, altezza As Integer)
        Try
            Dim m As Integer = _margine
            Dim altRiga As Integer = CInt(_altezzaLinea * 1.2)
            Dim larghezzaPannello As Integer = Math.Min(larghezza - 2 * m, Math.Max(CInt(larghezza * 0.6), 420))
            Dim x As Integer = (larghezza - larghezzaPannello) \ 2
            Dim visibili As Integer = Math.Max(3, Math.Min(p.Voci.Count, (altezza - _altezzaTitolo - 4 * m) \ altRiga - 2))
            Dim altTitolo As Integer = Math.Max(_altezzaLinea, AltezzaTesto(dc, p.Titolo, larghezzaPannello - 2 * m)) + m
            Dim altPannello As Integer = altTitolo + visibili * altRiga + m
            Dim y As Integer = Math.Max(_altezzaTitolo + m, (altezza - altPannello) \ 2)
            Riempi(dc, Rc(x, y, x + larghezzaPannello, y + altPannello), _coloreSfondo)
            Contorno(dc, Rc(x, y, x + larghezzaPannello, y + altPannello), _coloreFuoco, 3)
            Riempi(dc, Rc(x + 3, y + 3, x + larghezzaPannello - 3, y + altTitolo), _coloreSfondoTitolo)
            SetTextColor(dc, _coloreTestoTitolo)
            Scrivi(dc, p.Titolo, Rc(x + m, y + m \ 2, x + larghezzaPannello - m, y + altTitolo), DT_WORDBREAK)
            Dim primo As Integer = Math.Max(0, Math.Min(p.Indice - visibili \ 2, p.Voci.Count - visibili))
            Dim yy As Integer = y + altTitolo
            For i As Integer = 0 To visibili - 1
                Dim k As Integer = primo + i
                If k >= p.Voci.Count Then Exit For
                Dim riga As RECT = Rc(x + 4, yy, x + larghezzaPannello - 4, yy + altRiga)
                If k = p.Indice Then
                    Riempi(dc, riga, _coloreSelezione)
                    Contorno(dc, riga, _coloreFuoco, 2)
                    SetTextColor(dc, TestoSu(_coloreSelezione))
                Else
                    SetTextColor(dc, _coloreTesto)
                End If
                Dim testo As String = If(p.Multipla, If(p.Voci(k).Selezionata, "[x] ", "[  ] "), "") & p.Voci(k).Testo
                Scrivi(dc, testo, Rc(x + m, yy, x + larghezzaPannello - m, yy + altRiga), DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS)
                yy += altRiga
            Next
            SetTextColor(dc, _coloreTesto)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.DisegnaPannello", ex)
            Throw
        End Try
    End Sub

    Private Sub RilasciaMemoria()
        Try
            If _hMemDC <> IntPtr.Zero Then
                If _hOldMemBitmap <> IntPtr.Zero Then SelectObject(_hMemDC, _hOldMemBitmap)
                If _hMemBitmap <> IntPtr.Zero Then DeleteObject(_hMemBitmap)
                DeleteDC(_hMemDC)
            End If
            _hMemDC = IntPtr.Zero
            _hMemBitmap = IntPtr.Zero
            _hOldMemBitmap = IntPtr.Zero
            _memLarghezza = 0
            _memAltezza = 0
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.RilasciaMemoria", ex)
            Throw
        End Try
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            Try
                RilasciaMemoria()
                If _timerTabellone IsNot Nothing Then
                    _timerTabellone.Stop()
                    _timerTabellone.Dispose()
                    _timerTabellone = Nothing
                End If
                For Each v As IntPtr In _cacheCarte.Values
                    DeleteObject(v)
                Next
                _cacheCarte.Clear()
                For Each immagine As Bitmap In _cacheMon.Values
                    immagine.Dispose()
                Next
                _cacheMon.Clear()
                For Each f As IntPtr In New IntPtr() {_hFont, _hFontTitolo, _hFontCarta, _hFontCartaPiccola}
                    If f <> IntPtr.Zero Then DeleteObject(f)
                Next
                _hFont = IntPtr.Zero
                _hFontTitolo = IntPtr.Zero
                _hFontCarta = IntPtr.Zero
                _hFontCartaPiccola = IntPtr.Zero
            Catch ex As Exception
                log.ScriviLog("SchermoGioco.Dispose: " & ex.Message)
            End Try
            MyBase.Dispose(disposing)
        Catch ex As Exception
            RegistroErrori.Registra("SchermoGioco.Dispose", ex)
            Throw
        End Try
    End Sub

#End Region

End Class
