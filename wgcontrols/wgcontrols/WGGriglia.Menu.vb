' ============================================================================================
' WGGriglia - menu disegnato a mano dentro la griglia (Wi-Fi e Bluetooth della barra di stato)
' ============================================================================================
'
' Dalla barra di stato il Wi-Fi e il Bluetooth non aprono un'altra finestra: la griglia stessa
' disegna un menu sopra le caselle, lo legge con la sua voce e ne gestisce tasti e tocco. Così resta
' tutto nella finestra che ha già il fuoco, senza passaggi da una finestra all'altra.
'
' Un menu è un titolo e un elenco di voci; ogni voce ha il testo, cosa fare con Invio e, se servono,
' le sue opzioni (Alt, o un colpo in su), che si aprono come sottomenu. Esc torna indietro di un
' livello e dall'ultimo chiude il menu, tornando alla voce della barra di stato.
'
'   Dim g As WGGriglia = WGGriglia.PerMenu                 ' la griglia da cui si è premuto Invio
'   g.ApriMenu("Wi-Fi, connesso a Casa", voci)             ' menu nuovo
'   g.ApriSottomenu("Casa", opzioni)                       ' un livello in più
'   g.AggiornaMenu("Wi-Fi, spento", nuoveVoci)             ' stesso menu, contenuto cambiato
'   g.ChiudiSottomenu() / g.ChiudiMenu()
'
' Come gli elenchi di WGListaView: ciclico (dall'ultima voce si torna alla prima, salvo l'impostazione
' CicloLista), all'apertura dice "menù" e il titolo (impostazione LeggiTitoloIngresso) e quante voci ci sono
' (LeggiNumeroVoci), le lettere cercano la voce che comincia così (più lettere in fila cercano dentro il testo).
' Tasti: frecce su e giù (o destra e sinistra), Home e Fine, Invio o spazio, Alt per le opzioni della voce,
' Esc o Backspace per tornare indietro, Insert per riascoltare la voce. Tocco: colpo a destra e a sinistra,
' doppio tocco, colpo in su per le opzioni, doppio tocco con due dita per tornare indietro, esplorazione col dito.
' ============================================================================================
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

''' <summary>Una voce del menu disegnato dalla griglia.</summary>
Public Class VoceMenuGriglia
    ''' <summary>Testo mostrato e letto.</summary>
    Public Testo As String = ""
    ''' <summary>Invio o doppio tocco; Nothing per una voce che si legge soltanto.</summary>
    Public Invio As Action
    ''' <summary>Le opzioni della voce (Alt o colpo in su), aperte come sottomenu; Nothing se non ne ha.</summary>
    Public Opzioni As Func(Of List(Of VoceMenuGriglia))

    Public Sub New(testo As String, Optional invio As Action = Nothing, Optional opzioni As Func(Of List(Of VoceMenuGriglia)) = Nothing)
        Me.Testo = If(testo, "")
        Me.Invio = invio
        Me.Opzioni = opzioni
    End Sub
End Class

Partial Public Class WGGriglia

    Private Class LivelloMenu
        Public Titolo As String = ""
        Public Voci As New List(Of VoceMenuGriglia)()
        Public Indice As Integer
        ' Prima voce mostrata, quando non entrano tutte.
        Public Prima As Integer
    End Class

    ' La pila dei menu aperti: l'ultimo è quello mostrato.
    Private ReadOnly _menuAMano As New List(Of LivelloMenu)()
    Private ReadOnly _rettangoliMenu As New Dictionary(Of Integer, Rectangle)()
    ' Il contenuto è cambiato mentre la griglia era dietro un'attesa: al ritorno si rilegge con il titolo.
    Private _menuDaRileggere As Boolean
    Private Shared _grigliaPerMenu As WGGriglia

    ''' <summary>La griglia da cui si è appena premuto Invio nella barra di stato (Nothing se non c'è più).</summary>
    Public Shared ReadOnly Property PerMenu As WGGriglia
        Get
            Dim g As WGGriglia = _grigliaPerMenu
            If g Is Nothing OrElse g.IsDisposed Then Return Nothing
            Return g
        End Get
    End Property

    Public ReadOnly Property MenuAperto As Boolean
        Get
            Return _menuAMano.Count > 0
        End Get
    End Property

    ''' <summary>
    ''' La voce scelta nel menu di quel livello (0 il menu, 1 il sottomenu aperto da una sua voce...);
    ''' -1 se quel livello non è aperto.
    ''' </summary>
    Public Function PosizioneNelMenu(livello As Integer) As Integer
        If livello < 0 OrElse livello >= _menuAMano.Count Then Return -1
        Return _menuAMano(livello).Indice
    End Function

    Private Function MenuCorrente() As LivelloMenu
        Return If(_menuAMano.Count = 0, Nothing, _menuAMano(_menuAMano.Count - 1))
    End Function

    Private Shared Function NuovoLivello(titolo As String, voci As IEnumerable(Of VoceMenuGriglia)) As LivelloMenu
        Dim livello As New LivelloMenu() With {.Titolo = If(titolo, "")}
        If voci IsNot Nothing Then livello.Voci.AddRange(voci.Where(Function(v) v IsNot Nothing))
        Return livello
    End Function

    ''' <summary>Apre un menu nuovo al posto di quello eventualmente aperto. Senza voci non apre niente.</summary>
    Public Sub ApriMenu(titolo As String, voci As IEnumerable(Of VoceMenuGriglia))
        Try
            Dim livello As LivelloMenu = NuovoLivello(titolo, voci)
            _menuAMano.Clear()
            If livello.Voci.Count = 0 Then
                Me.Invalidate()
                Return
            End If
            _menuAMano.Add(livello)
            MostraMenuCambiato()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.ApriMenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Apre un menu sopra quello mostrato, sulla voce indicata: Esc torna a quello di prima.</summary>
    Public Sub ApriSottomenu(titolo As String, voci As IEnumerable(Of VoceMenuGriglia), Optional indice As Integer = 0)
        Try
            Dim livello As LivelloMenu = NuovoLivello(titolo, voci)
            If livello.Voci.Count = 0 OrElse _menuAMano.Count = 0 Then Return
            livello.Indice = Math.Max(0, Math.Min(indice, livello.Voci.Count - 1))
            _menuAMano.Add(livello)
            MostraMenuCambiato()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.ApriSottomenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Cambia il contenuto del menu (dopo aver acceso il Wi-Fi, collegato una rete...): i sottomenu si
    ''' chiudono e si resta, se c'è ancora, sulla voce di prima. Se il menu è stato chiuso non fa niente.
    ''' Con leggi falso non dice niente: serve a chi subito dopo riapre un sottomenu, che si legge da sé.
    ''' </summary>
    Public Sub AggiornaMenu(titolo As String, voci As IEnumerable(Of VoceMenuGriglia), Optional leggi As Boolean = True)
        Try
            If _menuAMano.Count = 0 Then Return
            Dim vecchio As LivelloMenu = _menuAMano(0)
            Dim testoScelto As String = If(vecchio.Indice >= 0 AndAlso vecchio.Indice < vecchio.Voci.Count, vecchio.Voci(vecchio.Indice).Testo, "")
            Dim livello As LivelloMenu = NuovoLivello(titolo, voci)
            _menuAMano.Clear()
            If livello.Voci.Count = 0 Then
                Me.Invalidate()
                Return
            End If
            Dim stessa As Integer = livello.Voci.FindIndex(Function(v) v.Testo = testoScelto)
            livello.Indice = If(stessa >= 0, stessa, Math.Max(0, Math.Min(vecchio.Indice, livello.Voci.Count - 1)))
            _menuAMano.Add(livello)
            If leggi Then
                MostraMenuCambiato()
            Else
                TieniVisibileNelMenu()
                Me.Invalidate()
            End If
        Catch ex As Exception
            log.ScriviLog("WGGriglia.AggiornaMenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Chiude il sottomenu mostrato e torna al menu di prima (senza leggere: di solito segue un'azione).</summary>
    Public Sub ChiudiSottomenu()
        Try
            If _menuAMano.Count > 1 Then _menuAMano.RemoveAt(_menuAMano.Count - 1)
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.ChiudiSottomenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Chiude il menu, tutto, senza leggere niente.</summary>
    Public Sub ChiudiMenu()
        Try
            _menuAMano.Clear()
            _menuDaRileggere = False
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.ChiudiMenu: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Rilegge il menu con il titolo (dopo un messaggio che lo ha coperto); niente se è chiuso.</summary>
    Public Sub RileggiMenu()
        If MenuAperto AndAlso Not Me.IsDisposed Then LeggiMenu(True)
    End Sub

    ' Finché il menu è aperto questo timer guarda se la griglia può ricevere i tasti: quando torna davanti
    ' dopo un'attesa, una domanda o un campo, il menu viene riletto con il titolo.
    Private WithEvents _timerMenu As New Timer() With {.Interval = 200}
    Private _menuRiceveva As Boolean

    ' Il menu è appena comparso o cambiato: si ridisegna e si legge con il titolo. Se la griglia è dietro
    ' a un'attesa o a un'altra finestra, lo si legge quando torna davanti.
    Private Sub MostraMenuCambiato()
        TieniVisibileNelMenu()
        Me.Invalidate()
        _menuRiceveva = PuoRicevere()
        If _menuRiceveva Then
            LeggiMenu(True)
        Else
            _menuDaRileggere = True
        End If
        _timerMenu.Start()
    End Sub

    Private Sub _timerMenu_Tick(sender As Object, e As EventArgs) Handles _timerMenu.Tick
        Try
            If Me.IsDisposed OrElse Not MenuAperto Then
                _timerMenu.Stop()
                Return
            End If
            Dim riceve As Boolean = PuoRicevere()
            If riceve AndAlso (_menuDaRileggere OrElse (Not _menuRiceveva AndAlso Environment.TickCount - _tickAnnuncio >= 600)) Then LeggiMenu(True)
            _menuRiceveva = riceve
        Catch ex As Exception
            log.ScriviLog("WGGriglia._timerMenu_Tick: " & ex.Message)
        End Try
    End Sub

    Private _configMenu As WGConfig

    ' Le stesse impostazioni degli elenchi (WGListaView): vero se non sono state scritte.
    Private Function ImpostazioneMenu(nome As String) As Boolean
        Try
            If _configMenu Is Nothing Then _configMenu = New WGConfig()
            If _configMenu.EsisteId(nome) Then
                Dim valore As Boolean = True
                Boolean.TryParse(_configMenu.LeggiValore(nome), valore)
                Return valore
            End If
        Catch ex As Exception
            log.ScriviLog("WGGriglia.ImpostazioneMenu: " & ex.Message)
        End Try
        Return True
    End Function

    ' "menù" davanti al titolo, come negli elenchi; niente se il titolo lo dice già.
    Private Shared Function NomeTitoloMenu(titolo As String) As String
        If String.IsNullOrEmpty(titolo) Then Return "menù"
        If titolo.IndexOf("menu", StringComparison.OrdinalIgnoreCase) >= 0 OrElse titolo.IndexOf("menù", StringComparison.OrdinalIgnoreCase) >= 0 Then Return titolo
        Return "menù " & titolo
    End Function

    Private Sub LeggiMenu(conTitolo As Boolean)
        Try
            Dim livello As LivelloMenu = MenuCorrente()
            If livello Is Nothing OrElse livello.Voci.Count = 0 Then Return
            _tickAnnuncio = Environment.TickCount
            Dim voce As String = livello.Voci(livello.Indice).Testo
            Dim testo As String = voce
            If conTitolo Then
                _menuDaRileggere = False
                Dim parti As New List(Of String)()
                If ImpostazioneMenu("LeggiTitoloIngresso") Then parti.Add(NomeTitoloMenu(livello.Titolo))
                If ImpostazioneMenu("LeggiNumeroVoci") Then parti.Add(If(livello.Voci.Count = 1, "una voce", livello.Voci.Count & " voci"))
                If parti.Count > 0 Then testo = String.Join(", ", parti) & ". " & voce
            End If
            sintesivocale.GetInstance().LeggiTesto(testo, escludiPunteggiatura:=True)
        Catch ex As Exception
            log.ScriviLog("WGGriglia.LeggiMenu: " & ex.Message)
        End Try
    End Sub

    ' Avanti o indietro di una voce; ai bordi si ricomincia dall'altra parte, se il ciclo è acceso.
    Private Sub SpostaNelMenu(passo As Integer)
        Dim livello As LivelloMenu = MenuCorrente()
        If livello Is Nothing OrElse livello.Voci.Count = 0 Then Return
        _cercatoMenu = ""
        If livello.Voci.Count = 1 Then
            LeggiMenu(False)
            Return
        End If
        Dim nuovo As Integer = livello.Indice + passo
        If nuovo < 0 OrElse nuovo >= livello.Voci.Count Then
            If Not ImpostazioneMenu("CicloLista") Then
                VaiNelMenu(nuovo)
                Return
            End If
            nuovo = If(nuovo < 0, livello.Voci.Count - 1, 0)
        End If
        VaiNelMenu(nuovo)
    End Sub

    ' Lettere in fila (meno di un secondo l'una dall'altra): una sola cerca la prossima voce che comincia
    ' così, più lettere cercano dentro il testo.
    Private _cercatoMenu As String = ""
    Private _tickCercatoMenu As Integer

    Private Function CercaNelMenu(codice As Keys) As Boolean
        Dim lettera As Char
        If codice >= Keys.A AndAlso codice <= Keys.Z Then
            lettera = Char.ToLowerInvariant(ChrW(AscW("A"c) + (codice - Keys.A)))
        ElseIf codice >= Keys.D0 AndAlso codice <= Keys.D9 Then
            lettera = ChrW(AscW("0"c) + (codice - Keys.D0))
        ElseIf codice >= Keys.NumPad0 AndAlso codice <= Keys.NumPad9 Then
            lettera = ChrW(AscW("0"c) + (codice - Keys.NumPad0))
        Else
            Return False
        End If
        Dim livello As LivelloMenu = MenuCorrente()
        If livello Is Nothing Then Return True
        Dim adesso As Integer = Environment.TickCount
        If adesso - _tickCercatoMenu > 1000 Then _cercatoMenu = ""
        _tickCercatoMenu = adesso
        _cercatoMenu &= lettera
        Dim soloInizio As Boolean = (_cercatoMenu.Length = 1)
        For passo As Integer = 1 To livello.Voci.Count
            Dim i As Integer = (livello.Indice + passo) Mod livello.Voci.Count
            Dim trovata As Boolean = If(soloInizio,
                livello.Voci(i).Testo.StartsWith(_cercatoMenu, StringComparison.OrdinalIgnoreCase),
                livello.Voci(i).Testo.IndexOf(_cercatoMenu, StringComparison.OrdinalIgnoreCase) >= 0)
            If trovata Then
                Dim ricerca As String = _cercatoMenu
                VaiNelMenu(i)
                _cercatoMenu = ricerca
                Return True
            End If
        Next
        WGSuoni.EmettiDin()
        sintesivocale.GetInstance().LeggiTesto("nessuna voce trovata")
        Return True
    End Function

    Private Sub VaiNelMenu(indice As Integer)
        Dim livello As LivelloMenu = MenuCorrente()
        If livello Is Nothing Then Return
        If indice < 0 OrElse indice >= livello.Voci.Count Then
            Try
                If _suoni Is Nothing Then _suoni = New WGSuoni()
                _suoni.LimiteRaggiunto()
            Catch ex As Exception
                log.ScriviLog("WGGriglia.VaiNelMenu: " & ex.Message)
            End Try
            LeggiMenu(False)
            Return
        End If
        livello.Indice = indice
        TieniVisibileNelMenu()
        Me.Invalidate()
        LeggiMenu(False)
    End Sub

    Private Sub InvioNelMenu()
        Try
            Dim livello As LivelloMenu = MenuCorrente()
            If livello Is Nothing OrElse livello.Voci.Count = 0 Then Return
            Dim voce As VoceMenuGriglia = livello.Voci(livello.Indice)
            If voce.Invio Is Nothing Then
                LeggiMenu(False)
                Return
            End If
            voce.Invio.Invoke()
            Me.Invalidate()
        Catch ex As Exception
            log.ScriviLog("WGGriglia.InvioNelMenu: " & ex.Message)
        End Try
    End Sub

    ' Alt o colpo in su: le opzioni della voce, come sottomenu.
    Private Sub OpzioniNelMenu()
        Try
            Dim livello As LivelloMenu = MenuCorrente()
            If livello Is Nothing OrElse livello.Voci.Count = 0 Then Return
            Dim voce As VoceMenuGriglia = livello.Voci(livello.Indice)
            Dim opzioni As List(Of VoceMenuGriglia) = If(voce.Opzioni Is Nothing, Nothing, voce.Opzioni.Invoke())
            If opzioni Is Nothing OrElse opzioni.Count = 0 Then
                sintesivocale.GetInstance().LeggiTesto("Nessuna opzione per questa voce")
                Return
            End If
            ApriSottomenu("Opzioni", opzioni)
        Catch ex As Exception
            log.ScriviLog("WGGriglia.OpzioniNelMenu: " & ex.Message)
        End Try
    End Sub

    ' Esc: dal sottomenu al menu, dal menu alla barra di stato.
    Private Sub IndietroNelMenu()
        If _menuAMano.Count = 0 Then Return
        _menuAMano.RemoveAt(_menuAMano.Count - 1)
        Me.Invalidate()
        If _menuAMano.Count > 0 Then
            LeggiMenu(True)
        ElseIf _inBarraStato AndAlso _vociStato.Count > 0 Then
            LeggiStato()
        Else
            LeggiCorrente()
        End If
    End Sub

    ' Tasti mentre il menu è aperto: li usa tutti il menu, così la griglia sotto non si muove.
    Private Sub TastoNelMenu(codice As Keys)
        Dim livello As LivelloMenu = MenuCorrente()
        If livello Is Nothing Then Return
        Select Case codice
            Case Keys.Down, Keys.Right : SpostaNelMenu(1)
            Case Keys.Up, Keys.Left : SpostaNelMenu(-1)
            Case Keys.Home, Keys.PageUp
                _cercatoMenu = ""
                VaiNelMenu(0)
            Case Keys.End, Keys.PageDown
                _cercatoMenu = ""
                VaiNelMenu(livello.Voci.Count - 1)
            Case Keys.Enter, Keys.Space : InvioNelMenu()
            Case Keys.Escape, Keys.Back : IndietroNelMenu()
            Case Keys.Insert : LeggiMenu(False)
            Case Else : CercaNelMenu(codice)
        End Select
    End Sub

    ' Righe del menu che entrano nel riquadro, e scorrimento per tenere visibile la voce scelta.
    Private Function AltezzaRigaMenu() As Integer
        Return Math.Max(36, CInt(_altezzaTitolo * 0.8))
    End Function

    Private Function RiquadroMenu() As Rectangle
        Dim larghezza As Integer = Me.ClientSize.Width
        Dim altezza As Integer = Me.ClientSize.Height
        Dim barra As Integer = AltezzaBarraStato()
        Dim alto As Integer = If(BarraStatoInAlto, barra, 0) + _altezzaTitolo + _margine
        Dim basso As Integer = altezza - If(BarraStatoInAlto, 0, barra) - _margine
        Dim lato As Integer = Math.Max(_margine, larghezza \ 10)
        Return Rectangle.FromLTRB(lato, alto, Math.Max(lato + 40, larghezza - lato), Math.Max(alto + 40, basso))
    End Function

    Private Function RigheMenuVisibili() As Integer
        Return Math.Max(1, (RiquadroMenu().Height - _altezzaTitolo) \ AltezzaRigaMenu())
    End Function

    Private Sub TieniVisibileNelMenu()
        Dim livello As LivelloMenu = MenuCorrente()
        If livello Is Nothing Then Return
        Dim visibili As Integer = RigheMenuVisibili()
        If livello.Indice < livello.Prima Then livello.Prima = livello.Indice
        If livello.Indice >= livello.Prima + visibili Then livello.Prima = livello.Indice - visibili + 1
        livello.Prima = Math.Max(0, Math.Min(livello.Prima, Math.Max(0, livello.Voci.Count - visibili)))
    End Sub

    ' Esplorazione col dito: la voce sotto il dito. Vero se il dito è sul menu (la griglia sotto non risponde).
    Private Function EsploraMenu(punto As Point) As Boolean
        If Not MenuAperto Then Return False
        For Each coppia As KeyValuePair(Of Integer, Rectangle) In _rettangoliMenu
            If coppia.Value.Contains(punto) Then
                Dim livello As LivelloMenu = MenuCorrente()
                If livello IsNot Nothing AndAlso coppia.Key <> livello.Indice Then VaiNelMenu(coppia.Key)
                Exit For
            End If
        Next
        Return True
    End Function

    ' Il menu sopra le caselle: riquadro con bordo, titolo in alto e una riga per voce; la voce scelta ha
    ' lo sfondo della selezione e il bordo del fuoco, come le caselle della griglia.
    Private Sub DisegnaMenuAMano(pennelloSfondo As IntPtr, pennelloTitolo As IntPtr, pennelloSelezione As IntPtr, pennelloFuoco As IntPtr)
        _rettangoliMenu.Clear()
        Dim livello As LivelloMenu = MenuCorrente()
        If livello Is Nothing Then Return
        Dim riquadro As Rectangle = RiquadroMenu()
        Dim zona As New RECT With {.Left = riquadro.Left, .Top = riquadro.Top, .Right = riquadro.Right, .Bottom = riquadro.Bottom}
        FillRect(_hMemDC, zona, pennelloSfondo)
        For spessore As Integer = 0 To 2
            Dim bordo As New RECT With {.Left = zona.Left + spessore, .Top = zona.Top + spessore, .Right = zona.Right - spessore, .Bottom = zona.Bottom - spessore}
            FrameRect(_hMemDC, bordo, pennelloFuoco)
        Next

        Dim fasciaTitolo As New RECT With {.Left = zona.Left + 3, .Top = zona.Top + 3, .Right = zona.Right - 3, .Bottom = zona.Top + _altezzaTitolo}
        FillRect(_hMemDC, fasciaTitolo, pennelloTitolo)
        SelectObject(_hMemDC, _hFontTitolo)
        SetTextColor(_hMemDC, _coloreTestoTitolo)
        Dim testoTitolo As New RECT With {.Left = fasciaTitolo.Left + 16, .Top = fasciaTitolo.Top, .Right = fasciaTitolo.Right - 16, .Bottom = fasciaTitolo.Bottom}
        DrawText(_hMemDC, livello.Titolo, livello.Titolo.Length, testoTitolo, DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS Or DT_NOPREFIX)

        SelectObject(_hMemDC, _hFont)
        Dim altezzaRiga As Integer = AltezzaRigaMenu()
        Dim visibili As Integer = RigheMenuVisibili()
        For i As Integer = livello.Prima To Math.Min(livello.Voci.Count - 1, livello.Prima + visibili - 1)
            Dim riga As New RECT With {.Left = zona.Left + 6, .Top = fasciaTitolo.Bottom + (i - livello.Prima) * altezzaRiga, .Right = zona.Right - 6}
            riga.Bottom = riga.Top + altezzaRiga
            If riga.Bottom > zona.Bottom - 3 Then Exit For
            _rettangoliMenu(i) = Rectangle.FromLTRB(riga.Left, riga.Top, riga.Right, riga.Bottom)
            If i = livello.Indice Then
                FillRect(_hMemDC, riga, pennelloSelezione)
                FrameRect(_hMemDC, riga, pennelloFuoco)
            End If
            SetTextColor(_hMemDC, _coloreTesto)
            Dim testo As String = livello.Voci(i).Testo
            Dim zonaTesto As New RECT With {.Left = riga.Left + 12, .Top = riga.Top, .Right = riga.Right - 12, .Bottom = riga.Bottom}
            DrawText(_hMemDC, testo, testo.Length, zonaTesto, DT_SINGLELINE Or DT_VCENTER Or DT_END_ELLIPSIS Or DT_NOPREFIX)
        Next
    End Sub

End Class
