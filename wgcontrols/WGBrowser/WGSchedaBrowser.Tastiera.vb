Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq
Imports WGBrowserManager
Imports wgcontrols

' Gestione della tastiera e dell'interazione con i widget all'interno delle pagine web, con lettura vocale della digitazione e
' navigazione tra i controlli.
Public Structure PressioneTastoBrowser
    Public Tasto As Keys
    Public Shift As Boolean
    Public Ctrl As Boolean
    Public Alt As Boolean
    Public AltGr As Boolean
    Public Passato As Boolean
End Structure

Partial Public Class WGSchedaBrowser
    Private _accentoInSospeso As String = ""
    Private _ruoloFocusDiretto As String = ""
    Private _focusDirettoConDiscendente As Boolean = False
    Private _ruoliContestoFocusDiretto As New List(Of String)()
    Private _cursoreMossoInDiretta As Boolean = False

    Private Shared ReadOnly RuoliCheUsanoFrecce As New HashSet(Of String)(StringComparer.Ordinal) From {
        "option", "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "treeitem", "radio", "slider", "spinbutton",
        "listbox", "combobox", "textbox", "searchbox", "gridcell", "row", "rowheader", "columnheader", "menu", "menubar",
        "tree", "grid", "treegrid", "tablist", "radiogroup", "toolbar", "application", "scrollbar", "video", "audio", "canvas"
    }

    Private Shared ReadOnly ContenitoriCheUsanoFrecce As New HashSet(Of String)(StringComparer.Ordinal) From {
        "listbox", "menu", "menubar", "tablist", "tree", "grid", "treegrid", "radiogroup", "toolbar", "application"
    }

    Private Shared ReadOnly RuoliSenzaLettura As New HashSet(Of String)(StringComparer.Ordinal) From {
        "application", "canvas", "video", "audio", "textbox", "searchbox", "combobox"
    }

    Private Shared ReadOnly AtteseWidget As Integer() = {60, 90, 150, 250}

    Private Shared ReadOnly RuoliVociWidget As New HashSet(Of String)(StringComparer.Ordinal) From {
        "option", "treeitem", "menuitem", "menuitemcheckbox", "menuitemradio", "gridcell", "row", "rowheader", "columnheader", "tab", "radio"
    }

    Private _widgetGestitoFino As DateTime = DateTime.MinValue
    Private _versioneWidget As Integer = 0
    Private _chiaveFocusRimandata As Tuple(Of String, Integer) = Nothing
    Private _rigaFocusRimandata As RigaVirtuale = Nothing

    Private Class StatoWidget
        Public Sessione As String = ""
        Public IdNodo As Integer = 0
        Public IdDiscendente As Integer = 0
        Public Stati As String = ""
        Public Valore As String = ""
        Public Testo As String = ""
        Public StatiDiscendente As String = ""
        Public Riga As RigaVirtuale = Nothing
    End Class

    Private Shared Function Modificatori(p As PressioneTastoBrowser) As ModificatoriInput
        Dim flags As ModificatoriInput = ModificatoriInput.Nessuno
        If p.Shift Then flags = flags Or ModificatoriInput.Shift
        If p.Ctrl Then flags = flags Or ModificatoriInput.Control
        If p.Alt Then flags = flags Or ModificatoriInput.Alt
        Return flags
    End Function

    Private Shared Function ModificatoriSemplici(shift As Boolean, ctrl As Boolean) As ModificatoriInput
        Dim flags As ModificatoriInput = ModificatoriInput.Nessuno
        If shift Then flags = flags Or ModificatoriInput.Shift
        If ctrl Then flags = flags Or ModificatoriInput.Control
        Return flags
    End Function

    Private Async Function AttendiInputAsync() As Task
        Try
            If _cdp IsNot Nothing Then Await _cdp.AttendiInputAsync()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttendiInputAsync: " & ex.Message)
        End Try
    End Function

    Friend Async Function GestisciTastoAsync(p As PressioneTastoBrowser) As Task
        Try
            _istanteUltimoTasto = DateTime.Now
            SegnaUso()
            If _sospesa Then
                lettura.LeggiTesto("Riattivazione della scheda")
                Riattiva()
                Return
            End If
            If _cursore Is Nothing OrElse _cdp Is Nothing Then Return
            If _cursore.IsLetturaContinuaAttiva Then
                _cursore.FermaLetturaContinua()
                Return
            End If
            If p.Passato Then
                InviaTastoGrezzo(p)
                Return
            End If
            If modalitaDiretta Then
                Await GestisciTastoDiretto(p)
                Return
            End If
            If inCampoTesto Then
                If p.Alt Then
                    InviaTastoGrezzo(p)
                    Return
                End If
                If p.Ctrl Then
                    Await GestisciTastoConCtrl(p)
                Else
                    Await GestisciTastoInCampo(p)
                End If
                Return
            End If
            If inCasellaCombinata Then
                If p.Alt Then
                    InviaTastoGrezzo(p)
                    Return
                End If
                Await GestisciTastoInCombo(p)
                Return
            End If
            If p.Ctrl AndAlso p.Alt Then
                Await GestisciTastoTabella(p)
                Return
            End If
            If p.Alt Then
                GestisciAltInNavigazione(p)
                Return
            End If
            If Await GestisciTastoSelezioneAsync(p) Then Return
            If p.Ctrl Then
                Await GestisciTastoNavigazioneConCtrl(p)
                Return
            End If
            Await GestisciTastoNavigazione(p)
            ControllaDescrizioneAutomatica()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoAsync: " & ex.ToString())
        End Try
    End Function

    Private Async Function GestisciTastoSelezioneAsync(p As PressioneTastoBrowser) As Task(Of Boolean)
        Try
            If Not p.Shift OrElse p.Alt OrElse _cursore Is Nothing Then Return False
            Select Case p.Tasto
                Case Keys.Left, Keys.Right, Keys.Home, Keys.End
                Case Keys.Up, Keys.Down
                    If p.Ctrl Then Return False
                Case Else
                    Return False
            End Select
            Await _cursore.AssicuraBufferAsync()
            Select Case p.Tasto
                Case Keys.Right
                    If p.Ctrl Then _cursore.EstendiSelezioneParola(True) Else _cursore.EstendiSelezioneCarattere(True)
                Case Keys.Left
                    If p.Ctrl Then _cursore.EstendiSelezioneParola(False) Else _cursore.EstendiSelezioneCarattere(False)
                Case Keys.Down
                    _cursore.EstendiSelezioneRiga(True)
                Case Keys.Up
                    _cursore.EstendiSelezioneRiga(False)
                Case Keys.Home
                    If p.Ctrl Then _cursore.EstendiSelezioneAllaPagina(False) Else _cursore.EstendiSelezioneAllaRiga(False)
                Case Keys.End
                    If p.Ctrl Then _cursore.EstendiSelezioneAllaPagina(True) Else _cursore.EstendiSelezioneAllaRiga(True)
            End Select
            Return True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoSelezioneAsync: " & ex.ToString())
            Return True
        End Try
    End Function

    Private Async Function GestisciTastoLetturaAsync(p As PressioneTastoBrowser) As Task(Of Boolean)
        Try
            If p.Alt OrElse p.Shift OrElse _cursore Is Nothing Then Return False
            Select Case p.Tasto
                Case Keys.Down
                    If p.Ctrl Then Return False
                    Await _cursore.LeggiSuccessivaAsync()
                Case Keys.Up
                    If p.Ctrl Then Return False
                    Await _cursore.LeggiPrecedenteAsync()
                Case Keys.Right
                    Await _cursore.AssicuraBufferAsync()
                    If p.Ctrl Then _cursore.LeggiParola(True) Else _cursore.LeggiCarattere(True)
                Case Keys.Left
                    Await _cursore.AssicuraBufferAsync()
                    If p.Ctrl Then _cursore.LeggiParola(False) Else _cursore.LeggiCarattere(False)
                Case Keys.PageDown
                    If p.Ctrl Then Return False
                    Await _cursore.SaltaRigheAsync(15)
                Case Keys.PageUp
                    If p.Ctrl Then Return False
                    Await _cursore.SaltaRigheAsync(-15)
                Case Keys.Home
                    Await _cursore.VaiAllInizioAsync()
                Case Keys.End
                    Await _cursore.VaiAllaFineAsync()
                Case Else
                    Return False
            End Select
            Return True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoLetturaAsync: " & ex.ToString())
            Return True
        End Try
    End Function

    Private Async Function AttivaDaTastieraAsync(modificatori As ModificatoriInput) As Task
        Try
            If _cursore IsNot Nothing AndAlso _cursore.SuContestoIngresso Then
                _cursore.EsciDalContesto(True)
                Return
            End If
            Await AttivaRigaCorrenteAsync(modificatori)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttivaDaTastieraAsync: " & ex.ToString())
        End Try
    End Function

    Private ReadOnly Property WidgetGestitoInCorso As Boolean
        Get
            Return DateTime.Now < _widgetGestitoFino
        End Get
    End Property

    Private Function FocusSuApplicazione() As Boolean
        Try
            If RuoliSenzaLettura.Contains(If(_ruoloFocusDiretto, "")) Then Return True
            Return _ruoliContestoFocusDiretto IsNot Nothing AndAlso _ruoliContestoFocusDiretto.Contains("application")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.FocusSuApplicazione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FocusSuWidget() As Boolean
        Try
            If _focusDirettoConDiscendente Then Return True
            If RuoliCheUsanoFrecce.Contains(If(_ruoloFocusDiretto, "")) Then Return True
            If _ruoliContestoFocusDiretto Is Nothing Then Return False
            For Each ruolo As String In _ruoliContestoFocusDiretto
                If ContenitoriCheUsanoFrecce.Contains(ruolo) Then Return True
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.FocusSuWidget: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function TastoDiSpostamento(p As PressioneTastoBrowser) As Boolean
        Try
            If p.Alt Then Return False
            Select Case p.Tasto
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown
                    Return True
            End Select
            Return False
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.TastoDiSpostamento: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function TastoPerIlWidget(p As PressioneTastoBrowser) As Boolean
        Try
            If p.Alt Then Return False
            If TastoDiSpostamento(p) Then Return True
            Select Case p.Tasto
                Case Keys.Space, Keys.Enter, Keys.Escape
                    Return Not p.Ctrl
            End Select
            If p.Ctrl Then Return False
            Return Not WGCaratteri.TastoSenzaCarattere(p.Tasto)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.TastoPerIlWidget: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ElementoDentroWidget(focus As FocusDescritto) As Boolean
        Try
            If focus Is Nothing OrElse focus.Riga Is Nothing Then Return False
            If RuoliVociWidget.Contains(If(focus.Riga.Ruolo, "")) Then Return True
            If focus.Ruoli Is Nothing Then Return False
            For Each ruolo As String In focus.Ruoli
                If ruolo <> "application" AndAlso ContenitoriCheUsanoFrecce.Contains(ruolo) Then Return True
            Next
            Return False
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.ElementoDentroWidget: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RimandaFocusDiretto(chiave As Tuple(Of String, Integer), riga As RigaVirtuale)
        Try
            _chiaveFocusRimandata = chiave
            _rigaFocusRimandata = riga
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RimandaFocusDiretto: " & ex.Message)
        End Try
    End Sub

    Private Async Sub ProgrammaFocusRimandato()
        Try
            Dim versione As Integer = _versioneWidget
            Await Task.Delay(450)
            If versione <> _versioneWidget OrElse WidgetGestitoInCorso OrElse Not modalitaDiretta OrElse _chiusa Then Return
            Dim chiave As Tuple(Of String, Integer) = _chiaveFocusRimandata
            Dim riga As RigaVirtuale = _rigaFocusRimandata
            _chiaveFocusRimandata = Nothing
            _rigaFocusRimandata = Nothing
            If chiave Is Nothing Then Return
            If chiave.Item2 <> idUltimoFocus OrElse chiave.Item1 <> sessioneUltimoFocus Then Return
            Await AnnunciaFocusDirettoAsync(chiave, riga)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ProgrammaFocusRimandato: " & ex.Message)
        End Try
    End Sub

    Private Sub InviaTastoAlWidget(p As PressioneTastoBrowser)
        Try
            Dim flags As ModificatoriInput = Modificatori(p)
            Dim vk As Integer = CInt(p.Tasto And Keys.KeyCode)
            Select Case p.Tasto
                Case Keys.Enter
                    _cdp.InviaTasto(vk, 13, flags)
                    Return
                Case Keys.Escape
                    _accentoInSospeso = ""
                    _cdp.InviaTasto(vk, 27, flags)
                    Return
                Case Keys.Space
                    _cdp.InviaTasto(vk, 32, flags)
                    Return
            End Select
            If Not p.Ctrl AndAlso Not p.Alt AndAlso Not WGCaratteri.TastoSenzaCarattere(p.Tasto) Then
                Dim c As String = WGCaratteri.RisolviCarattere(p.Tasto, p.Shift, p.AltGr, _accentoInSospeso)
                If c <> "" Then _cdp.InviaCarattere(c, vk, p.AltGr, p.Shift)
                Return
            End If
            _cdp.InviaTasto(vk, 0, flags)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InviaTastoAlWidget: " & ex.Message)
        End Try
    End Sub

    Private Async Function LeggiStatoWidgetAsync() As Task(Of StatoWidget)
        Dim stato As New StatoWidget()
        Try
            If AlberoDisponibile Then
                Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
                stato.Sessione = If(chiave.Item1, "")
                stato.IdNodo = chiave.Item2
                If chiave.Item2 <= 0 Then Return stato
                Dim descrizione As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, chiave.Item2, False)
                If descrizione Is Nothing OrElse descrizione.Riga Is Nothing Then Return stato
                stato.Riga = descrizione.Riga
                stato.IdDiscendente = descrizione.IdDiscendenteAttivo
                stato.Stati = If(descrizione.Riga.Stato, "")
                stato.Valore = If(descrizione.Riga.Valore, "")
                stato.Testo = If(descrizione.Riga.Testo, "")
                If stato.IdDiscendente > 0 Then
                    Dim discendente As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, stato.IdDiscendente, False)
                    If discendente IsNot Nothing AndAlso discendente.Riga IsNot Nothing Then stato.StatiDiscendente = If(discendente.Riga.Stato, "")
                End If
                Return stato
            End If
            If _cdp Is Nothing Then Return stato
            Dim rigaAgente As RigaVirtuale = RigaVirtuale.DaJson(Await _cdp.DescriviAttivoAsync())
            If rigaAgente Is Nothing Then Return stato
            stato.IdNodo = rigaAgente.IdElemento
            stato.Riga = rigaAgente
            stato.Stati = If(rigaAgente.Stato, "")
            stato.Valore = If(rigaAgente.Valore, "")
            stato.Testo = If(rigaAgente.Testo, "")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LeggiStatoWidgetAsync: " & ex.Message)
        End Try
        Return stato
    End Function

    Private Shared Function StatoWidgetCambiato(prima As StatoWidget, dopo As StatoWidget) As Boolean
        Try
            If dopo Is Nothing Then Return False
            If prima Is Nothing Then Return True
            Return prima.IdNodo <> dopo.IdNodo OrElse prima.Sessione <> dopo.Sessione OrElse prima.IdDiscendente <> dopo.IdDiscendente OrElse
                   prima.Stati <> dopo.Stati OrElse prima.Valore <> dopo.Valore OrElse prima.StatiDiscendente <> dopo.StatiDiscendente OrElse prima.Testo <> dopo.Testo
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.StatoWidgetCambiato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Function InviaTastoNelWidgetAsync(p As PressioneTastoBrowser, leggiSempre As Boolean) As Task
        Try
            _versioneWidget += 1
            Dim versione As Integer = _versioneWidget
            _widgetGestitoFino = DateTime.Now.AddMilliseconds(1500)
            If _ruoloFocusDiretto = "slider" Then _istanteSliderTempo = DateTime.Now
            If _contenitore IsNot Nothing AndAlso _contenitore.TastiInCoda > 0 Then
                InviaTastoAlWidget(p)
                Return
            End If
            Dim prima As StatoWidget = Await LeggiStatoWidgetAsync()
            If prima IsNot Nothing AndAlso ESliderTempo(prima.Riga) Then _istanteSliderTempo = DateTime.Now
            InviaTastoAlWidget(p)
            Await AttendiInputAsync()
            Dim dopo As StatoWidget = Nothing
            Dim cambiato As Boolean = False
            For Each attesa As Integer In AtteseWidget
                Await Task.Delay(attesa)
                If _chiusa OrElse Not modalitaDiretta OrElse versione <> _versioneWidget Then Return
                If _contenitore IsNot Nothing AndAlso _contenitore.TastiInCoda > 0 Then Return
                _widgetGestitoFino = DateTime.Now.AddMilliseconds(1500)
                dopo = Await LeggiStatoWidgetAsync()
                If StatoWidgetCambiato(prima, dopo) Then
                    cambiato = True
                    Exit For
                End If
            Next
            If dopo Is Nothing OrElse versione <> _versioneWidget Then Return
            If ESliderTempo(dopo.Riga) OrElse ESliderTempo(If(prima, dopo).Riga) Then
                _istanteSliderTempo = DateTime.Now
                Dim stabile As StatoWidget = Await AttendiValoreStabileSliderAsync(prima, dopo, versione)
                If stabile Is Nothing OrElse versione <> _versioneWidget Then Return
                dopo = stabile
                cambiato = StatoWidgetCambiato(prima, dopo)
                _istanteSliderTempo = DateTime.Now
            End If
            If _chiaveFocusRimandata IsNot Nothing AndAlso _chiaveFocusRimandata.Item2 = dopo.IdNodo AndAlso _chiaveFocusRimandata.Item1 = dopo.Sessione Then
                _chiaveFocusRimandata = Nothing
                _rigaFocusRimandata = Nothing
            End If
            Await AnnunciaStatoWidgetAsync(prima, dopo, cambiato, leggiSempre)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InviaTastoNelWidgetAsync: " & ex.ToString())
        Finally
            _widgetGestitoFino = DateTime.Now.AddMilliseconds(400)
            ProgrammaFocusRimandato()
        End Try
    End Function

    ' ---- Slider del tempo dei lettori (YouTube e simili) -------------------------------------
    Private _istanteSliderTempo As DateTime = DateTime.MinValue

    Private Shared ReadOnly RegexValoreTempo As New System.Text.RegularExpressions.Regex(
        "(\b(second[oi]?|seconds?|minut[oi]|minutes?|ore|hours?|secondes?|sekunden?|segundos?)\b|\d+:\d{2})",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase Or System.Text.RegularExpressions.RegexOptions.CultureInvariant)

    Private Shared ReadOnly RegexPosizioneDiTotale As New System.Text.RegularExpressions.Regex(
        "^\s*(.+?)\s+(?:di|of|von|de|sur|su|em)\s+(.+?)\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase Or System.Text.RegularExpressions.RegexOptions.Singleline Or System.Text.RegularExpressions.RegexOptions.CultureInvariant)

    Private ReadOnly Property SliderTempoRecente As Boolean
        Get
            Return (DateTime.Now - _istanteSliderTempo).TotalMilliseconds < 2500
        End Get
    End Property

    Private Shared Function EValoreTempo(valore As String) As Boolean
        Try
            Return Not String.IsNullOrWhiteSpace(valore) AndAlso RegexValoreTempo.IsMatch(valore)
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Shared Function ESliderTempo(riga As RigaVirtuale) As Boolean
        Try
            Return riga IsNot Nothing AndAlso riga.Ruolo = "slider" AndAlso EValoreTempo(riga.Valore)
        Catch ex As Exception
            Return False
        End Try
    End Function

    ' "1 minuto 5 secondi di 3 minuti 20 secondi" -> "1 minuto 5 secondi": dopo uno spostamento si
    ' legge solo il punto raggiunto, la durata totale si sente gia' quando si arriva sullo slider.
    Private Shared Function PosizioneDaValoreTempo(valore As String) As String
        Try
            Dim v As String = If(valore, "").Trim()
            Dim m As System.Text.RegularExpressions.Match = RegexPosizioneDiTotale.Match(v)
            If m.Success AndAlso m.Groups(1).Value.Trim() <> "" Then Return m.Groups(1).Value.Trim()
            Return v
        Catch ex As Exception
            Return If(valore, "")
        End Try
    End Function

    ' Dopo un tasto su uno slider del tempo la pagina aggiorna il valore a scatti (e intanto la
    ' riproduzione lo fa avanzare): aspetta che si fermi per due letture consecutive e legge una volta sola.
    Private Async Function AttendiValoreStabileSliderAsync(prima As StatoWidget, iniziale As StatoWidget, versione As Integer) As Task(Of StatoWidget)
        Try
            Dim ultimo As StatoWidget = iniziale
            Dim stabili As Integer = 0
            For i As Integer = 1 To 9
                Await Task.Delay(140)
                If _chiusa OrElse Not modalitaDiretta OrElse versione <> _versioneWidget Then Return Nothing
                If _contenitore IsNot Nothing AndAlso _contenitore.TastiInCoda > 0 Then Return Nothing
                _widgetGestitoFino = DateTime.Now.AddMilliseconds(1500)
                _istanteSliderTempo = DateTime.Now
                Dim letto As StatoWidget = Await LeggiStatoWidgetAsync()
                If letto Is Nothing OrElse letto.Riga Is Nothing Then Continue For
                If letto.Valore = ultimo.Valore Then
                    stabili += 1
                    Dim cambiatoRispettoAPrima As Boolean = prima Is Nothing OrElse letto.Valore <> prima.Valore
                    If stabili >= 2 AndAlso (cambiatoRispettoAPrima OrElse stabili >= 4) Then Return letto
                Else
                    stabili = 0
                    ultimo = letto
                End If
            Next
            Return ultimo
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttendiValoreStabileSliderAsync: " & ex.Message)
            Return iniziale
        End Try
    End Function

    Private Async Function AnnunciaStatoWidgetAsync(prima As StatoWidget, dopo As StatoWidget, cambiato As Boolean, leggiSempre As Boolean) As Task
        Try
            If dopo.IdNodo > 0 AndAlso (prima Is Nothing OrElse dopo.IdNodo <> prima.IdNodo OrElse dopo.Sessione <> prima.Sessione) Then
                sessioneUltimoFocus = dopo.Sessione
                idUltimoFocus = dopo.IdNodo
                istanteUltimoFocus = DateTime.Now
                Await AnnunciaFocusDirettoAsync(Tuple.Create(dopo.Sessione, dopo.IdNodo), dopo.Riga)
                Return
            End If
            statiUltimoAttivo = dopo.Stati
            _valoreUltimoAttivo = dopo.Valore
            If cambiato Then
                If dopo.IdDiscendente > 0 AndAlso dopo.IdDiscendente <> prima.IdDiscendente Then
                    Dim testoDiscendente As String = Await TestoDiscendenteAsync(dopo.Sessione, dopo.IdDiscendente, Nothing, False)
                    If testoDiscendente <> "" Then
                        Annuncia(testoDiscendente)
                        Return
                    End If
                End If
                If dopo.StatiDiscendente <> prima.StatiDiscendente AndAlso dopo.StatiDiscendente <> "" Then
                    Annuncia(dopo.StatiDiscendente)
                    Return
                End If
                If dopo.Stati <> prima.Stati Then
                    Annuncia(If(dopo.Stati <> "", dopo.Stati, If(dopo.Riga IsNot Nothing, dopo.Riga.CostruisciAnnuncio(False), "")))
                    Return
                End If
                If dopo.Valore <> prima.Valore AndAlso dopo.Valore <> "" Then
                    If ESliderTempo(dopo.Riga) Then Annuncia(PosizioneDaValoreTempo(dopo.Valore)) Else Annuncia(dopo.Valore)
                    Return
                End If
                If dopo.Testo <> prima.Testo AndAlso dopo.Testo <> "" Then
                    Annuncia(dopo.Testo)
                    Return
                End If
            End If
            If Not leggiSempre Then Return
            If ESliderTempo(dopo.Riga) Then
                ' Valore invariato (inizio o fine del brano): una riga breve, non tutta la descrizione dello slider.
                Annuncia(PosizioneDaValoreTempo(dopo.Valore))
                Return
            End If
            If dopo.IdDiscendente > 0 Then
                Dim testoAttuale As String = Await TestoDiscendenteAsync(dopo.Sessione, dopo.IdDiscendente, Nothing, False)
                If testoAttuale <> "" Then
                    Annuncia(testoAttuale)
                    Return
                End If
            End If
            If dopo.Riga IsNot Nothing Then Annuncia(dopo.Riga.CostruisciAnnuncio(False))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AnnunciaStatoWidgetAsync: " & ex.ToString())
        End Try
    End Function

    Private Sub DimenticaFocusDiretto()
        Try
            _ruoloFocusDiretto = ""
            _focusDirettoConDiscendente = False
            _ruoliContestoFocusDiretto = New List(Of String)()
            _cursoreMossoInDiretta = False
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DimenticaFocusDiretto: " & ex.Message)
        End Try
    End Sub

    Private Sub RicordaFocusDiretto(riga As RigaVirtuale, idDiscendente As Integer, ruoliContesto As List(Of String))
        Try
            _ruoloFocusDiretto = If(riga IsNot Nothing, If(riga.Ruolo, ""), "")
            _focusDirettoConDiscendente = idDiscendente > 0
            _ruoliContestoFocusDiretto = If(ruoliContesto, New List(Of String)())
            _cursoreMossoInDiretta = False
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RicordaFocusDiretto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function RuoliDiContesto(descrizione As WGAlberoAccessibilita.DescrizioneNodo) As List(Of String)
        Dim ruoli As New List(Of String)()
        Try
            If descrizione Is Nothing OrElse descrizione.Contesti Is Nothing Then Return ruoli
            For Each contesto As Tuple(Of String, String) In descrizione.Contesti
                Dim parti As String() = If(contesto.Item1, "").Split("|"c)
                If parti.Length >= 3 AndAlso parti(2) <> "" Then ruoli.Add(parti(2))
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.RuoliDiContesto: " & ex.Message)
        End Try
        Return ruoli
    End Function

    Private Async Function AggiornaFocusDirettoAsync() As Task
        Try
            DimenticaFocusDiretto()
            If AlberoDisponibile Then
                Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
                If chiave.Item2 > 0 Then
                    Dim descrizione As WGAlberoAccessibilita.DescrizioneNodo = Await DescriviDaAlberoAsync(chiave.Item1, chiave.Item2, True)
                    If descrizione IsNot Nothing AndAlso descrizione.Riga IsNot Nothing Then
                        RicordaFocusDiretto(descrizione.Riga, descrizione.IdDiscendenteAttivo, RuoliDiContesto(descrizione))
                        Return
                    End If
                End If
            End If
            If _cdp Is Nothing Then Return
            Dim rigaAgente As RigaVirtuale = RigaVirtuale.DaJson(Await _cdp.DescriviAttivoAsync())
            If rigaAgente IsNot Nothing AndAlso rigaAgente.IdElemento > 0 Then RicordaFocusDiretto(rigaAgente, 0, Nothing)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AggiornaFocusDirettoAsync: " & ex.Message)
        End Try
    End Function

    Private Async Function GestisciTastoLetturaDirettaAsync(p As PressioneTastoBrowser) As Task(Of Boolean)
        Try
            If p.Alt OrElse _cursore Is Nothing Then Return False
            If p.Tasto = Keys.Tab AndAlso Not p.Ctrl Then
                If Not _cursoreMossoInDiretta Then Return False
                _cursoreMossoInDiretta = False
                Await InviaTabAsync(p.Shift)
                Return True
            End If
            If p.Ctrl AndAlso Not p.Shift AndAlso (p.Tasto = Keys.C OrElse p.Tasto = Keys.Insert) AndAlso _cursore.SelezioneAttiva Then
                CopiaRigaCorrente()
                Return True
            End If
            If FocusSuApplicazione() Then Return False
            Dim suWidget As Boolean = FocusSuWidget()
            If Not suWidget AndAlso WGImpostazioniBrowser.FrecceLeggonoInControlloCompleto Then
                If Await GestisciTastoSelezioneAsync(p) Then
                    _cursoreMossoInDiretta = True
                    Return True
                End If
                If Await GestisciTastoLetturaAsync(p) Then
                    _cursoreMossoInDiretta = True
                    Return True
                End If
                If _cursoreMossoInDiretta AndAlso Not p.Ctrl AndAlso (p.Tasto = Keys.Enter OrElse p.Tasto = Keys.Space) AndAlso _cursore.RigaCorrente IsNot Nothing Then
                    Dim restaSulContesto As Boolean = _cursore.SuContestoIngresso
                    Await AttivaDaTastieraAsync(If(p.Shift AndAlso p.Tasto = Keys.Enter, ModificatoriInput.Shift, ModificatoriInput.Nessuno))
                    If Not restaSulContesto Then _cursoreMossoInDiretta = False
                    Return True
                End If
            End If
            If TastoDiSpostamento(p) OrElse (suWidget AndAlso TastoPerIlWidget(p)) Then
                _cursoreMossoInDiretta = False
                Await InviaTastoNelWidgetAsync(p, suWidget AndAlso TastoDiSpostamento(p))
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoLetturaDirettaAsync: " & ex.ToString())
            Return False
        End Try
    End Function

    Friend Sub CommutaModalitaDiretta()
        Try
            If modalitaDiretta Then
                DisattivaModalitaDiretta(True, True)
            Else
                AttivaModalitaDiretta(True, True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CommutaModalitaDiretta: " & ex.Message)
        End Try
    End Sub

    Private Async Sub AttivaModalitaDiretta(conAnnuncio As Boolean, ricorda As Boolean)
        Try
            modalitaDiretta = True
            DimenticaFocusDiretto()
            _contestiFocus = New List(Of String)()
            _ultimaCellaFocus = Nothing
            statiUltimoAttivo = ""
            _valoreUltimoAttivo = ""
            If _cursore IsNot Nothing Then _cursore.FermaLetturaContinua()
            If ricorda Then WGImpostazioniBrowser.ImpostaControlloCompleto(WGArchivioBrowser.HostDi(_indirizzo), True)
            _cdp.ImpostaFocusMotore(True)
            If conAnnuncio Then Annuncia(If(WGImpostazioniBrowser.FrecceLeggonoInControlloCompleto, "Controllo completo alla pagina: i tasti vanno al sito. H e Shift H spostano all'intestazione, come in navigazione. Le frecce leggono la pagina; su elenchi, menu, alberi, griglie e schede muovono l'elemento e WinGabriel legge la voce. F9 per tornare alla navigazione, Shift F8 per gli ultimi annunci", "Controllo completo alla pagina: i tasti vanno al sito e WinGabriel legge quello che cambia. H e Shift H spostano all'intestazione, come in navigazione. F9 per tornare alla navigazione, Shift F8 per gli ultimi annunci"))
            Await AggiornaFocusDirettoAsync()
            If Not modalitaDiretta Then Return
            Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
            If stato IsNot Nothing AndAlso stato.Value(Of Integer?)("i").GetValueOrDefault() <> 0 AndAlso stato.Value(Of Integer?)("ed").GetValueOrDefault() = 1 Then
                Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
                EntraNelCampo(stato, False, chiave.Item1, chiave.Item2)
            End If
            ProgrammaAggiornamentoSfondo()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttivaModalitaDiretta: " & ex.Message)
        End Try
    End Sub

    Private Async Sub DisattivaModalitaDiretta(conAnnuncio As Boolean, ricorda As Boolean)
        Try
            modalitaDiretta = False
            DimenticaFocusDiretto()
            If ricorda Then WGImpostazioniBrowser.ImpostaControlloCompleto(WGArchivioBrowser.HostDi(_indirizzo), False)
            EsciDalCampo(True)
            If Not conAnnuncio OrElse _cursore Is Nothing Then Return
            Dim chiave As Tuple(Of String, Integer) = Await ChiaveElementoAttivoAsync(0)
            Await _cursore.AggiornaAsync(False)
            Dim trovato As Boolean = chiave.Item2 > 0 AndAlso _cursore.SincronizzaConNodo(chiave.Item1, chiave.Item2)
            Dim riga As RigaVirtuale = _cursore.RigaCorrente
            Annuncia("Navigazione" & If(trovato AndAlso riga IsNot Nothing, ". " & riga.CostruisciAnnuncio(False), ""))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DisattivaModalitaDiretta: " & ex.Message)
        End Try
    End Sub

    Private Async Function GestisciTastoDiretto(p As PressioneTastoBrowser) As Task
        Try
            If Not p.Ctrl AndAlso Not p.Alt Then
                Select Case p.Tasto
                    Case Keys.Insert, Keys.F8
                        If p.Shift Then Exit Select
                        If inCampoTesto Then
                            LeggiContenutoCampo()
                        Else
                            Annuncia(Await DescriviElementoAFuocoAsync())
                            _cursoreMossoInDiretta = False
                        End If
                        Return
                End Select
            End If
            If p.Ctrl AndAlso p.Shift AndAlso Not p.Alt AndAlso Not inCampoTesto AndAlso (p.Tasto = Keys.Up OrElse p.Tasto = Keys.Down) Then
                If p.Tasto = Keys.Down Then
                    Await _cursore.LeggiSuccessivaAsync()
                Else
                    Await _cursore.LeggiPrecedenteAsync()
                End If
                _cursoreMossoInDiretta = True
                Return
            End If
            If inCampoTesto AndAlso Not p.Alt Then
                If p.Tasto = Keys.Escape Then
                    _cdp.InviaTasto(WGCdpInterazione.VK_ESCAPE, 27, Modificatori(p))
                    Return
                End If
                If p.Ctrl Then
                    Select Case p.Tasto
                        Case Keys.A, Keys.C, Keys.X, Keys.V, Keys.Z, Keys.Y, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.Back, Keys.Delete
                            If Not p.Shift OrElse p.Tasto = Keys.Left OrElse p.Tasto = Keys.Right OrElse p.Tasto = Keys.Home OrElse p.Tasto = Keys.End OrElse p.Tasto = Keys.Z Then
                                Await GestisciTastoConCtrl(p)
                                Return
                            End If
                    End Select
                    If p.Tasto = Keys.Enter Then RicordaTestoInviato()
                    InviaTastoGrezzo(p)
                    Return
                End If
                Await GestisciTastoInCampo(p)
                Return
            End If
            If inCasellaCombinata AndAlso Not p.Ctrl AndAlso Not p.Alt Then
                Select Case p.Tasto
                    Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown
                        Await SpostaOpzioneComboConScript(p.Tasto)
                        Return
                End Select
            End If
            ' In controllo completo le lettere vanno al sito, ma H resta la navigazione per intestazioni, come fuori da F9.
            ' Nei campi, nelle caselle combinate e nelle applicazioni che gestiscono la tastiera da sole si continua a scrivere.
            If Not p.Ctrl AndAlso Not p.Alt AndAlso p.Tasto = Keys.H AndAlso Not inCampoTesto AndAlso Not inCasellaCombinata AndAlso Not FocusSuApplicazione() Then
                Await _navigatore.SaltaAIntestazioneSuccessiva("qualsiasi", p.Shift)
                _cursoreMossoInDiretta = True
                Return
            End If
            If Not inCampoTesto AndAlso Not inCasellaCombinata AndAlso Await GestisciTastoLetturaDirettaAsync(p) Then Return
            InviaTastoGrezzo(p)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoDiretto: " & ex.ToString())
        End Try
    End Function

    Private Sub RicordaTestoInviato()
        Try
            If String.IsNullOrWhiteSpace(testoCampo) OrElse campoPassword Then Return
            _ultimoTestoInviato = testoCampo.Trim()
            If _ultimoTestoInviato.Length > 200 Then _ultimoTestoInviato = _ultimoTestoInviato.Substring(0, 200)
            _istanteUltimoTestoInviato = DateTime.Now
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RicordaTestoInviato: " & ex.Message)
        End Try
    End Sub

    Friend Sub InviaTastoGrezzo(p As PressioneTastoBrowser)
        Try
            Dim flags As ModificatoriInput = Modificatori(p)
            Dim vk As Integer = CInt(p.Tasto And Keys.KeyCode)
            Select Case p.Tasto
                Case Keys.Enter
                    If Not p.Shift Then RicordaTestoInviato()
                    _cdp.InviaTasto(vk, 13, flags)
                    Return
                Case Keys.Tab
                    attesaFocusUtenteFino = DateTime.Now.AddMilliseconds(1500)
                    _cdp.InviaTasto(vk, 9, flags)
                    Return
                Case Keys.Escape
                    _accentoInSospeso = ""
                    _cdp.InviaTasto(vk, 27, flags)
                    Return
                Case Keys.Back
                    _accentoInSospeso = ""
                    _cdp.InviaTasto(vk, 8, flags)
                    Return
                Case Keys.Space
                    If Not p.Ctrl AndAlso Not p.Alt Then
                        _cdp.InviaTasto(vk, 32, flags)
                        If EchoDigitazione() = "1" OrElse EchoDigitazione() = "3" Then Annuncia("spazio")
                        VerificaStatoDopoAttivazione()
                        Return
                    End If
            End Select
            If Not p.Ctrl AndAlso Not p.Alt Then
                Dim c As String = WGCaratteri.RisolviCarattere(p.Tasto, p.Shift, p.AltGr, _accentoInSospeso)
                If c <> "" Then
                    _cdp.InviaCarattere(c, vk, p.AltGr, p.Shift)
                    Dim eco As String = EchoDigitazione()
                    If eco = "1" OrElse eco = "3" Then
                        Dim nome As String = WGCaratteri.NomeCarattere(c(0), SimboloDaLeggereInScrittura(c(0)))
                        If nome <> "" Then Annuncia(nome)
                    End If
                    Return
                End If
                If _accentoInSospeso <> "" Then
                    If EchoDigitazione() <> "0" Then Annuncia(WGCaratteri.NomeAccento(_accentoInSospeso))
                    Return
                End If
            End If
            If p.Alt AndAlso Not p.Ctrl Then
                Dim carattere As Integer = 0
                If p.Tasto >= Keys.A AndAlso p.Tasto <= Keys.Z Then carattere = AscW(Char.ToLowerInvariant(ChrW(vk)))
                If p.Tasto >= Keys.D0 AndAlso p.Tasto <= Keys.D9 Then carattere = vk
                _cdp.InviaTasto(vk, carattere, flags)
                Return
            End If
            _cdp.InviaTasto(vk, 0, flags)
            If p.Tasto = Keys.Enter OrElse p.Tasto = Keys.Space Then VerificaStatoDopoAttivazione()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InviaTastoGrezzo: " & ex.Message)
        End Try
    End Sub

    Private Async Sub VerificaStatoDopoAttivazione()
        Try
            If Not modalitaDiretta OrElse inCampoTesto Then Return
            Await AttendiInputAsync()
            Await Task.Delay(250)
            If Not modalitaDiretta OrElse inCampoTesto Then Return
            Await AnnunciaCambioStatoAsync(Nothing)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.VerificaStatoDopoAttivazione: " & ex.Message)
        End Try
    End Sub

    Private Function SimboloDaLeggereInScrittura(c As Char) As Boolean
        Try
            Return impostazioni.LivelloScritturaPunteggiatura >= WGImpostazioniComuni.LivelloSimboloScrittura(c)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SimboloDaLeggereInScrittura: " & ex.Message)
            Return True
        End Try
    End Function

    Private Function EchoDigitazione() As String
        Dim valore As String = ""
        Try
            valore = impostazioni.EchoDigitazione
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EchoDigitazione: " & ex.Message)
        End Try
        If valore <> "0" AndAlso valore <> "1" AndAlso valore <> "2" AndAlso valore <> "3" Then valore = "3"
        Return valore
    End Function

    Private Sub GestisciCharMessaggio(c As Char)
        Try
            If c = vbCr OrElse c = vbLf Then Return
            Dim echoSetting As String = EchoDigitazione()
            If echoSetting = "0" Then Return
            If campoPassword AndAlso Not impostazioni.LeggiPasswordAttivo Then
                If echoSetting <> "2" Then Annuncia("asterisco")
                Return
            End If

            Dim txt As String = testoCampo
            Dim cursoreCampo As Integer = Math.Max(0, Math.Min(inizioSelezione, txt.Length))
            Dim isFineParola As Boolean = (c = " "c OrElse Char.IsPunctuation(c))
            Dim spazioDopoSpazio As Boolean = False
            If c = " "c Then
                Dim prevIdx As Integer = cursoreCampo - 2
                If prevIdx < 0 Then
                    spazioDopoSpazio = True
                ElseIf prevIdx < txt.Length AndAlso Char.IsWhiteSpace(txt(prevIdx)) Then
                    spazioDopoSpazio = True
                End If
            End If

            Dim parola As String = ""
            If (echoSetting = "2" OrElse echoSetting = "3") AndAlso Not spazioDopoSpazio AndAlso cursoreCampo > 0 Then
                Dim i As Integer = cursoreCampo - 1
                If i >= 0 AndAlso i < txt.Length AndAlso txt(i) = c Then i -= 1
                While i >= 0 AndAlso Char.IsWhiteSpace(txt(i))
                    i -= 1
                End While
                Dim fine As Integer = i + 1
                While i >= 0 AndAlso Not Char.IsWhiteSpace(txt(i)) AndAlso Not Char.IsPunctuation(txt(i))
                    i -= 1
                End While
                Dim inizio As Integer = i + 1
                If fine > inizio Then parola = txt.Substring(inizio, fine - inizio)
            End If

            Dim leggiSimbolo As Boolean = SimboloDaLeggereInScrittura(c)
            Dim nomeSimbolo As String = WGCaratteri.NomeCarattere(c, leggiSimbolo)
            If (Char.IsPunctuation(c) OrElse Char.IsSymbol(c)) AndAlso Not leggiSimbolo Then nomeSimbolo = ""

            If echoSetting = "1" Then
                If c = " "c Then
                    Annuncia("spazio")
                ElseIf nomeSimbolo <> "" Then
                    Annuncia(nomeSimbolo)
                End If
                Return
            End If

            If echoSetting = "2" Then
                If isFineParola AndAlso parola.Length > 0 Then
                    If Char.IsPunctuation(c) AndAlso nomeSimbolo <> "" Then
                        Annuncia(parola & " " & nomeSimbolo)
                    Else
                        Annuncia(parola)
                    End If
                End If
                Return
            End If

            If spazioDopoSpazio Then
                Annuncia("spazio")
                Return
            End If
            Dim simboliDaLeggere As String = "!""£$%&/()=?^€@#][}{§*_:;<>"
            Dim isSimbolo As Boolean = simboliDaLeggere.IndexOf(c) >= 0
            If (isFineParola OrElse isSimbolo) AndAlso parola.Length > 1 Then
                If c = " "c Then
                    Annuncia(parola & " spazio")
                ElseIf nomeSimbolo <> "" Then
                    Annuncia(parola & " " & nomeSimbolo)
                Else
                    Annuncia(parola)
                End If
            ElseIf c = " "c Then
                Annuncia("spazio")
            ElseIf nomeSimbolo <> "" Then
                Annuncia(nomeSimbolo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciCharMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviCampo(stato As JObject) As String
        Try
            Dim riga As RigaVirtuale = RigaVirtuale.DaJson(stato)
            If riga Is Nothing Then Return "Campo"
            If riga.Password Then riga.LunghezzaPassword = stato.Value(Of Integer?)("n").GetValueOrDefault()
            Dim testo As String = riga.CostruisciAnnuncio(False)
            Dim posizione As Integer = stato.Value(Of Integer?)("x").GetValueOrDefault()
            Dim totale As Integer = stato.Value(Of Integer?)("n").GetValueOrDefault()
            If Not riga.Password AndAlso posizione > 0 AndAlso totale > 0 Then testo &= ", " & posizione & " di " & totale
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviCampo: " & ex.Message)
            Return "Campo"
        End Try
    End Function

    Private Sub AggiornaTestoCampo(stato As JObject)
        Try
            If stato Is Nothing Then Return
            Dim id As Integer = stato.Value(Of Integer?)("i").GetValueOrDefault()
            If id <> 0 AndAlso idCampo <> 0 AndAlso id <> idCampo Then Return
            If stato.Value(Of Integer?)("pw").GetValueOrDefault() = 1 Then
                testoCampo = New String("*"c, stato.Value(Of Integer?)("n").GetValueOrDefault())
            Else
                testoCampo = If(stato.Value(Of String)("v"), "")
            End If
            inizioSelezione = Math.Max(0, Math.Min(stato.Value(Of Integer?)("a").GetValueOrDefault(testoCampo.Length), testoCampo.Length))
            fineSelezione = Math.Max(inizioSelezione, Math.Min(stato.Value(Of Integer?)("b").GetValueOrDefault(inizioSelezione), testoCampo.Length))
            versioneTestoCampo += 1
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AggiornaTestoCampo: " & ex.Message)
        End Try
    End Sub

    ' Suoni del browser (clic su un link, entrata e uscita dai campi): WGSuoni rispetta da solo
    ' l'impostazione dei suoni di WinGabriel e il volume. Un'unica istanza condivisa da tutte le schede.
    Private Shared _suoniBrowser As WGSuoni = Nothing
    Private Shared ReadOnly _lockSuoniBrowser As New Object()

    Friend Shared ReadOnly Property SuoniBrowser As WGSuoni
        Get
            SyncLock _lockSuoniBrowser
                If _suoniBrowser Is Nothing Then _suoniBrowser = New WGSuoni()
                Return _suoniBrowser
            End SyncLock
        End Get
    End Property

    Private Sub EntraNelCampo(stato As JObject, conAnnuncio As Boolean, sessioneChiave As String, idChiave As Integer, Optional rigaAlbero As RigaVirtuale = Nothing)
        Try
            If stato Is Nothing OrElse stato.Value(Of Integer?)("i").GetValueOrDefault() = 0 Then
                If conAnnuncio Then Annuncia("Impossibile entrare nel campo")
                Return
            End If
            If Not (inCampoTesto OrElse inCasellaCombinata) AndAlso _attiva Then SuoniBrowser.EntrataCampo()
            sessioneCampoChiave = If(sessioneChiave, "")
            idCampoChiave = idChiave
            idCampo = stato.Value(Of Integer)("i")
            ruoloCampo = If(stato.Value(Of String)("r"), "")
            Dim modificabile As Boolean = stato.Value(Of Integer?)("ed").GetValueOrDefault() = 1 AndAlso stato.Value(Of Integer?)("ro").GetValueOrDefault() = 0
            campoMultiriga = stato.Value(Of Integer?)("ml").GetValueOrDefault() = 1
            campoPassword = stato.Value(Of Integer?)("pw").GetValueOrDefault() = 1
            campoConSuggerimenti = modificabile AndAlso ruoloCampo = "combobox"
            inCampoTesto = modificabile
            inCasellaCombinata = Not modificabile
            _accentoInSospeso = ""
            AggiornaTestoCampo(stato)
            _cursore.SincronizzaConNodo(sessioneCampoChiave, idCampoChiave)
            _cdp.ImpostaFocusMotore(True)
            If conAnnuncio Then
                Dim testo As String = If(rigaAlbero IsNot Nothing, DescriviCampoDaAlbero(rigaAlbero, stato), DescriviCampo(stato))
                If inCampoTesto Then
                    testo &= ". Modifica"
                Else
                    testo &= ". Usare le frecce per scegliere"
                End If
                Annuncia(testo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EntraNelCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub EsciDalCampo(Optional silenzioso As Boolean = False)
        Try
            If (inCampoTesto OrElse inCasellaCombinata) AndAlso _attiva Then SuoniBrowser.UscitaCampo()
            _versioneSincronizzazione += 1
            inCampoTesto = False
            inCasellaCombinata = False
            campoMultiriga = False
            campoPassword = False
            campoConSuggerimenti = False
            idCampo = 0
            idCampoChiave = 0
            sessioneCampoChiave = ""
            _accentoInSospeso = ""
            If _cdp IsNot Nothing Then _cdp.SessioneCampo = Nothing
            If _cursore IsNot Nothing Then _cursore.BufferSporco = True
            If Not silenzioso Then Annuncia("Uscito dal campo")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EsciDalCampo: " & ex.Message)
        End Try
    End Sub

    Private Async Function SincronizzaCampoAsync() As Task(Of JObject)
        Try
            Dim versione As Integer = versioneTestoCampo
            Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
            If stato IsNot Nothing AndAlso versione = versioneTestoCampo Then AggiornaTestoCampo(stato)
            Return stato
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SincronizzaCampoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub ProgrammaSincronizzazioneCampo()
        Try
            _versioneSincronizzazione += 1
            Dim versione As Integer = _versioneSincronizzazione
            Task.Delay(450).ContinueWith(Sub(t) SuUI(Async Sub()
                                                         Try
                                                             If versione <> _versioneSincronizzazione OrElse Not inCampoTesto Then Return
                                                             Await SincronizzaCampoAsync()
                                                         Catch exSincronizza As Exception
                                                             log.ScriviLog("WGSchedaBrowser.ProgrammaSincronizzazioneCampo sincronizza: " & exSincronizza.Message)
                                                         End Try
                                                     End Sub))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ProgrammaSincronizzazioneCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub InserisciCarattereNelCampo(carattere As String, tasto As Keys, altGr As Boolean, shift As Boolean)
        Try
            _cdp.InviaCarattere(carattere, CInt(tasto And Keys.KeyCode), altGr, shift)
            RegistraTestoInserito(carattere)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InserisciCarattereNelCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub RegistraTestoInserito(testo As String)
        Try
            Dim a As Integer = Math.Max(0, Math.Min(inizioSelezione, testoCampo.Length))
            Dim b As Integer = Math.Max(a, Math.Min(fineSelezione, testoCampo.Length))
            Dim inserito As String = If(campoPassword, New String("*"c, testo.Length), testo)
            testoCampo = testoCampo.Substring(0, a) & inserito & testoCampo.Substring(b)
            inizioSelezione = a + inserito.Length
            fineSelezione = inizioSelezione
            versioneTestoCampo += 1
            ProgrammaSincronizzazioneCampo()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RegistraTestoInserito: " & ex.Message)
        End Try
    End Sub

    Private Sub CancellaCarattereNelCampo(modalita As String)
        Try
            _accentoInSospeso = ""
            Dim a As Integer = Math.Max(0, Math.Min(inizioSelezione, testoCampo.Length))
            Dim b As Integer = Math.Max(a, Math.Min(fineSelezione, testoCampo.Length))
            Dim cancellato As String = ""
            If b > a Then
                cancellato = testoCampo.Substring(a, b - a)
                testoCampo = testoCampo.Remove(a, b - a)
                inizioSelezione = a
            ElseIf modalita = "back" Then
                If a > 0 Then
                    cancellato = testoCampo.Substring(a - 1, 1)
                    testoCampo = testoCampo.Remove(a - 1, 1)
                    inizioSelezione = a - 1
                End If
            Else
                If a < testoCampo.Length Then
                    cancellato = testoCampo.Substring(a, 1)
                    testoCampo = testoCampo.Remove(a, 1)
                    inizioSelezione = a
                End If
            End If
            fineSelezione = inizioSelezione
            versioneTestoCampo += 1
            _cdp.InviaTasto(If(modalita = "back", WGCdpInterazione.VK_BACK, WGCdpInterazione.VK_DELETE), If(modalita = "back", 8, 0), ModificatoriInput.Nessuno)
            ProgrammaSincronizzazioneCampo()
            If cancellato.Length = 0 Then
                Annuncia(If(modalita = "back", "Inizio testo", "Fine testo"))
            ElseIf campoPassword AndAlso Not impostazioni.LeggiPasswordAttivo Then
                Annuncia(If(cancellato.Length = 1, "asterisco", "Selezione cancellata"))
            ElseIf cancellato.Length = 1 Then
                Annuncia(WGCaratteri.NomeCarattere(cancellato(0), True))
            Else
                Annuncia("Cancellato " & cancellato)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CancellaCarattereNelCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiCarattereAlCursore()
        Try
            If campoPassword AndAlso Not impostazioni.LeggiPasswordAttivo Then
                Annuncia(If(inizioSelezione < testoCampo.Length, "asterisco", "Fine testo"))
                Return
            End If
            If testoCampo.Length = 0 Then
                Annuncia("Campo vuoto")
            ElseIf inizioSelezione < testoCampo.Length Then
                Annuncia(WGCaratteri.NomeCarattere(testoCampo(inizioSelezione), True))
            Else
                Annuncia("Fine testo")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LeggiCarattereAlCursore: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiContenutoCampo()
        Try
            If campoPassword AndAlso Not impostazioni.LeggiPasswordAttivo Then
                Annuncia(If(testoCampo.Length = 0, "Campo vuoto", "Password di " & testoCampo.Length & " caratteri"))
            ElseIf String.IsNullOrEmpty(testoCampo) Then
                Annuncia("Campo vuoto")
            Else
                Annuncia(testoCampo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LeggiContenutoCampo: " & ex.Message)
        End Try
    End Sub

    Private Async Function SpostaCorsoreCampoConScript(tasto As Keys, shift As Boolean, ctrl As Boolean) As Task
        Try
            Dim vecchioInizio As Integer = inizioSelezione
            Dim vecchiaFine As Integer = fineSelezione
            Dim vk As Integer = CInt(tasto And Keys.KeyCode)
            _cdp.InviaTasto(vk, 0, ModificatoriSemplici(shift, ctrl))
            Await AttendiInputAsync()
            Await Task.Delay(40)
            Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
            If stato Is Nothing Then Return
            AggiornaTestoCampo(stato)
            Dim contenteditable As String = If(stato.Value(Of String)("rc"), "")
            If shift Then
                AnnunciaSelezione(vecchioInizio, vecchiaFine)
                Return
            End If
            If campoPassword AndAlso Not impostazioni.LeggiPasswordAttivo Then
                LeggiCarattereAlCursore()
                Return
            End If
            If ctrl AndAlso (tasto = Keys.Left OrElse tasto = Keys.Right) Then
                Dim parola As String = WGNavigatoreDOM.ParolaAlCursore(testoCampo, inizioSelezione).Trim()
                Annuncia(If(parola = "", If(inizioSelezione >= testoCampo.Length, "Fine testo", "Spazio"), parola))
                Return
            End If
            If ctrl AndAlso (tasto = Keys.Home OrElse tasto = Keys.End) Then
                Dim riga As String = If(contenteditable <> "", contenteditable, WGNavigatoreDOM.RigaAlCursore(testoCampo, inizioSelezione))
                Annuncia(If(String.IsNullOrWhiteSpace(riga), "Riga vuota", riga))
                Return
            End If
            Select Case tasto
                Case Keys.Home
                    If campoMultiriga Then
                        LeggiCarattereAlCursore()
                    Else
                        Annuncia("Inizio riga")
                    End If
                Case Keys.End
                    Annuncia("Fine riga")
                Case Else
                    LeggiCarattereAlCursore()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SpostaCorsoreCampoConScript: " & ex.Message)
        End Try
    End Function

    Private Sub AnnunciaSelezione(vecchioInizio As Integer, vecchiaFine As Integer)
        Try
            Dim a As Integer = inizioSelezione, b As Integer = fineSelezione
            Dim testoSelezione As String = ""
            Dim prefisso As String = "Selezionato "
            If b > vecchiaFine AndAlso vecchiaFine <= testoCampo.Length Then
                testoSelezione = testoCampo.Substring(vecchiaFine, b - vecchiaFine)
            ElseIf a < vecchioInizio AndAlso vecchioInizio <= testoCampo.Length Then
                testoSelezione = testoCampo.Substring(a, vecchioInizio - a)
            ElseIf b < vecchiaFine AndAlso vecchiaFine <= testoCampo.Length Then
                testoSelezione = testoCampo.Substring(b, vecchiaFine - b)
                prefisso = "Deselezionato "
            ElseIf a > vecchioInizio AndAlso a <= testoCampo.Length Then
                testoSelezione = testoCampo.Substring(vecchioInizio, a - vecchioInizio)
                prefisso = "Deselezionato "
            End If
            If testoSelezione.Length = 0 Then
                Annuncia(If(a = b, "Nessuna selezione", "Selezione invariata"))
            ElseIf campoPassword AndAlso Not impostazioni.LeggiPasswordAttivo Then
                Annuncia(prefisso & testoSelezione.Length & " caratteri")
            ElseIf testoSelezione.Length = 1 Then
                Annuncia(prefisso & WGCaratteri.NomeCarattere(testoSelezione(0), True))
            Else
                Annuncia(prefisso & testoSelezione)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AnnunciaSelezione: " & ex.Message)
        End Try
    End Sub

    Private Async Function SpostaRigaTextareaConScript(tasto As Keys) As Task
        Try
            _cdp.InviaTasto(CInt(tasto And Keys.KeyCode), 0, ModificatoriInput.Nessuno)
            Await AttendiInputAsync()
            Await Task.Delay(40)
            Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
            If stato Is Nothing Then Return
            AggiornaTestoCampo(stato)
            Dim riga As String = If(stato.Value(Of String)("rc"), "")
            If riga = "" Then riga = WGNavigatoreDOM.RigaAlCursore(testoCampo, inizioSelezione)
            Annuncia(If(String.IsNullOrWhiteSpace(riga), "Riga vuota", riga))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SpostaRigaTextareaConScript: " & ex.Message)
        End Try
    End Function

    Private Async Function SpostaSuggerimentoConScript(tasto As Keys) As Task
        Try
            Dim valorePrima As String = testoCampo
            _cdp.InviaTasto(CInt(tasto And Keys.KeyCode), 0, ModificatoriInput.Nessuno)
            Await AttendiInputAsync()
            Await Task.Delay(90)
            Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
            If stato Is Nothing Then Return
            AggiornaTestoCampo(stato)
            Dim opzione As String = If(stato.Value(Of String)("d"), "")
            If opzione <> "" Then
                Dim statiOpzione As String = If(stato.Value(Of String)("ds"), "")
                Annuncia(opzione & If(statiOpzione <> "", ", " & statiOpzione, ""))
            ElseIf testoCampo <> valorePrima Then
                Annuncia(If(testoCampo = "", "Campo vuoto", testoCampo))
            ElseIf stato.Value(Of Integer?)("e").GetValueOrDefault() = 0 Then
                Annuncia(If(testoCampo = "", "Campo vuoto, nessun suggerimento", testoCampo))
            Else
                LeggiContenutoCampo()
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SpostaSuggerimentoConScript: " & ex.Message)
        End Try
    End Function

    Private Async Function SpostaOpzioneComboConScript(tasto As Keys, Optional carattere As String = "") As Task
        Try
            _widgetGestitoFino = DateTime.Now.AddMilliseconds(1500)
            Dim inCoda As Boolean = _contenitore IsNot Nothing AndAlso _contenitore.TastiInCoda > 0
            Dim prima As String = Nothing
            If Not inCoda AndAlso AlberoDisponibile Then prima = Await DescriviOpzioneDaAlberoAsync()
            If carattere <> "" Then
                _cdp.InviaCarattere(carattere, CInt(tasto And Keys.KeyCode), False, False)
            Else
                _cdp.InviaTasto(CInt(tasto And Keys.KeyCode), If(tasto = Keys.Space, 32, 0), ModificatoriInput.Nessuno)
            End If
            If inCoda Then Return
            Await AttendiInputAsync()
            Dim dalAlbero As String = Nothing
            Dim primoGiro As Boolean = True
            For Each attesa As Integer In AtteseWidget
                Await Task.Delay(If(primoGiro, 120, attesa))
                primoGiro = False
                If _chiusa Then Return
                If _contenitore IsNot Nothing AndAlso _contenitore.TastiInCoda > 0 Then Return
                _widgetGestitoFino = DateTime.Now.AddMilliseconds(1500)
                dalAlbero = Await DescriviOpzioneDaAlberoAsync()
                If dalAlbero Is Nothing OrElse prima Is Nothing Then Exit For
                If Not String.Equals(dalAlbero, prima, StringComparison.Ordinal) Then Exit For
            Next
            If dalAlbero IsNot Nothing Then
                If dalAlbero <> "" Then Annuncia(dalAlbero)
                Return
            End If
            Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
            If stato Is Nothing Then Return
            Dim opzione As String = If(stato.Value(Of String)("d"), "")
            If opzione = "" Then opzione = If(stato.Value(Of String)("v"), "")
            Dim posizione As Integer = stato.Value(Of Integer?)("x").GetValueOrDefault()
            Dim totale As Integer = stato.Value(Of Integer?)("n").GetValueOrDefault()
            If posizione > 0 AndAlso totale > 0 Then opzione &= ", " & posizione & " di " & totale
            Dim stati As String = If(stato.Value(Of String)("ds"), "")
            If stati <> "" Then opzione &= ", " & stati
            Annuncia(If(String.IsNullOrWhiteSpace(opzione), "Nessun valore", opzione))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SpostaOpzioneComboConScript: " & ex.Message)
        Finally
            _widgetGestitoFino = DateTime.Now.AddMilliseconds(400)
            _chiaveFocusRimandata = Nothing
            _rigaFocusRimandata = Nothing
        End Try
    End Function

    Friend Async Function AttivaRigaCorrenteAsync(Optional modificatoriLink As ModificatoriInput = ModificatoriInput.Nessuno) As Task
        Try
            Await _cursore.AssicuraBufferAsync()
            Dim riga As RigaVirtuale = _cursore.RigaCorrente
            If riga Is Nothing Then
                If _cursore.Righe.Count > 0 Then _cursore.SpostaA(0, True)
                Return
            End If
            If riga.Ruolo = "math" Then
                Await EsploraFormulaAsync(riga)
                Return
            End If
            If riga.IsCampo Then
                Dim stato As JObject = Await _cdp.FocalizzaRigaAsync(riga)
                EntraNelCampo(stato, True, riga.Sessione, riga.IdElemento, If(riga.DaAlbero, riga.Copia(), Nothing))
                Return
            End If
            If riga.ELink Then SuoniBrowser.LinkAttivato()
            If modificatoriLink <> ModificatoriInput.Nessuno AndAlso riga.ELink Then
                Dim indirizzoLink As String = Await _cdp.IndirizzoRigaAsync(riga)
                If indirizzoLink <> "" AndAlso Not indirizzoLink.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) Then
                    Dim sfondo As Boolean = (modificatoriLink And ModificatoriInput.Control) = ModificatoriInput.Control
                    _contenitore.NuovaScheda(indirizzoLink, sfondo, Me)
                    Return
                End If
            End If
            If riga.ELink AndAlso riga.Indirizzo <> "" Then
                Dim frammento As String = Ancora(riga.Indirizzo)
                If frammento <> "" AndAlso String.Equals(SenzaAncora(riga.Indirizzo), SenzaAncora(_indirizzo), StringComparison.OrdinalIgnoreCase) Then
                    Await _cursore.AttivaCorrenteAsync()
                    Await AttendiInputAsync()
                    Await Task.Delay(200)
                    If Await SpostaAllAncoraAsync(frammento) Then Return
                    Return
                End If
            End If
            attesaFocusUtenteFino = DateTime.Now.AddMilliseconds(1500)
            Dim ruolo As String = riga.RuoloDaAttivare
            Dim leggiStato As Boolean = Not String.IsNullOrEmpty(riga.Stato) OrElse ruolo = "checkbox" OrElse ruolo = "radio" OrElse ruolo = "switch" OrElse ruolo = "tab" OrElse ruolo = "option" OrElse ruolo = "treeitem" OrElse ruolo = "menuitemcheckbox" OrElse ruolo = "menuitemradio"
            _cdp.ImpostaFocusMotore(True)
            Dim navigazione As Integer = numeroNavigazione
            Await _cursore.AttivaCorrenteAsync()
            Await AttendiInputAsync()
            If leggiStato Then
                Await Task.Delay(250)
                Dim statoNuovo As String = Await _cdp.StatiRigaAsync(riga)
                If Not String.IsNullOrEmpty(statoNuovo) Then Annuncia(statoNuovo)
            End If
            Await AggiornaDopoAttivazioneAsync(riga, navigazione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttivaRigaCorrenteAsync: " & ex.ToString())
        End Try
    End Function

    Private Async Function AggiornaDopoAttivazioneAsync(riga As RigaVirtuale, navigazione As Integer) As Task
        Try
            Await AttendiStabilitaAsync(1500)
            If _chiusa OrElse navigazione <> numeroNavigazione OrElse inCampoTesto OrElse inCasellaCombinata Then Return
            Dim modalePrima As Integer = _cursore.IdModale
            If Not Await _cursore.AggiornaAsync(False) Then Return
            If navigazione <> numeroNavigazione Then Return
            If _cursore.IdModale <> modalePrima Then Return
            If riga.IdDialogo > 0 AndAlso Not _cursore.Righe.Exists(Function(r) r.IdDialogo = riga.IdDialogo) Then
                If _cursore.Righe.Count > 0 Then
                    _cursore.SpostaA(0, False)
                    Annuncia("Finestra di dialogo chiusa. " & _cursore.RigaCorrente.CostruisciAnnuncio(True))
                Else
                    Annuncia("Finestra di dialogo chiusa")
                End If
                Return
            End If
            If _cursore.ContieneRiga(riga) Then
                Dim nuove As List(Of RigaVirtuale) = _cursore.UltimeRigheNuove
                If nuove.Count > 0 AndAlso nuove.Count <= 60 AndAlso (riga.Stato.Contains("compresso") OrElse riga.Stato.Contains("apre") OrElse riga.RuoloDaAttivare = "button" OrElse riga.RuoloDaAttivare = "cliccabile") Then
                    Dim prima As RigaVirtuale = nuove(0)
                    Dim indice As Integer = _cursore.Righe.IndexOf(prima)
                    If indice >= 0 AndAlso indice > _cursore.IndiceCorrente Then Annuncia("Nuovo contenuto sotto, " & nuove.Count & If(nuove.Count = 1, " riga", " righe") & ". Z per andarci")
                End If
                Return
            End If
            If _cursore.RigaCorrente IsNot Nothing Then
                _cursore.LeggiCorrente(True)
            ElseIf _cursore.Righe.Count > 0 Then
                Annuncia("Pagina aggiornata, " & _cursore.Righe.Count & " righe")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AggiornaDopoAttivazioneAsync: " & ex.ToString())
        End Try
    End Function

    Private Async Function InviaTabAsync(indietro As Boolean) As Task
        Try
            Dim riga As RigaVirtuale = _cursore.RigaCorrente
            If Not inCampoTesto AndAlso Not inCasellaCombinata AndAlso riga IsNot Nothing Then
                idFocusDaIgnorare = riga.IdDaAttivare
                sessioneFocusDaIgnorare = riga.Sessione
                ignoraFocusFino = DateTime.Now.AddMilliseconds(800)
                Await _cdp.PreparaTabAsync(riga)
            End If
            _cdp.SessioneCampo = Nothing
            attesaFocusUtenteFino = DateTime.Now.AddMilliseconds(1500)
            _cdp.ImpostaFocusMotore(True)
            _cdp.InviaTasto(WGCdpInterazione.VK_TAB, 9, If(indietro, ModificatoriInput.Shift, ModificatoriInput.Nessuno))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InviaTabAsync: " & ex.Message)
        End Try
    End Function

    Private Async Function GestisciTastoConCtrl(p As PressioneTastoBrowser) As Task
        Try
            Dim flags As ModificatoriInput = ModificatoriSemplici(p.Shift, True)
            Select Case p.Tasto
                Case Keys.A
                    _cdp.InviaTasto(Asc("A"c), 0, flags)
                    inizioSelezione = 0
                    fineSelezione = testoCampo.Length
                    Annuncia("Selezionato tutto")
                Case Keys.C
                    _cdp.InviaTasto(Asc("C"c), 0, flags)
                    Annuncia("Copiato negli appunti")
                Case Keys.X
                    _cdp.InviaTasto(Asc("X"c), 0, flags)
                    Annuncia("Tagliato")
                    Await AttendiInputAsync()
                    Await Task.Delay(80)
                    Await SincronizzaCampoAsync()
                Case Keys.V
                    _cdp.InviaTasto(Asc("V"c), 0, flags)
                    Annuncia("Incollato")
                    Await AttendiInputAsync()
                    Await Task.Delay(120)
                    Await SincronizzaCampoAsync()
                Case Keys.Z
                    _cdp.InviaTasto(Asc("Z"c), 0, flags)
                    Annuncia(If(p.Shift, "Ripristinato", "Annullato"))
                    Await AttendiInputAsync()
                    Await Task.Delay(80)
                    Await SincronizzaCampoAsync()
                Case Keys.Y
                    _cdp.InviaTasto(Asc("Y"c), 0, flags)
                    Annuncia("Ripristinato")
                    Await AttendiInputAsync()
                    Await Task.Delay(80)
                    Await SincronizzaCampoAsync()
                Case Keys.Left, Keys.Right, Keys.Home, Keys.End
                    Await SpostaCorsoreCampoConScript(p.Tasto, p.Shift, True)
                Case Keys.Back, Keys.Delete
                    _cdp.InviaTasto(If(p.Tasto = Keys.Back, WGCdpInterazione.VK_BACK, WGCdpInterazione.VK_DELETE), 0, flags)
                    Annuncia("Parola cancellata")
                    Await AttendiInputAsync()
                    Await Task.Delay(60)
                    Await SincronizzaCampoAsync()
                Case Keys.M
                    If p.Shift Then
                        CompilaModulo()
                    Else
                        InviaTastoGrezzo(p)
                    End If
                Case Keys.ControlKey, Keys.LControlKey, Keys.RControlKey
                    lettura.FermaVoce()
                Case Else
                    If p.Tasto = Keys.Enter Then RicordaTestoInviato()
                    InviaTastoGrezzo(p)
                    If p.Tasto = Keys.Enter OrElse p.Tasto = Keys.B OrElse p.Tasto = Keys.I OrElse p.Tasto = Keys.U Then ProgrammaSincronizzazioneCampo()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoConCtrl: " & ex.Message)
        End Try
    End Function

    Private Async Function GestisciTastoInCampo(p As PressioneTastoBrowser) As Task
        Try
            Select Case p.Tasto
                Case Keys.Escape
                    _accentoInSospeso = ""
                    If campoConSuggerimenti Then
                        Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
                        If stato IsNot Nothing AndAlso stato.Value(Of Integer?)("e").GetValueOrDefault() = 1 Then
                            _cdp.InviaTasto(WGCdpInterazione.VK_ESCAPE, 27, ModificatoriInput.Nessuno)
                            Annuncia("Suggerimenti chiusi")
                            Return
                        End If
                    End If
                    If modalitaDiretta Then
                        _cdp.InviaTasto(WGCdpInterazione.VK_ESCAPE, 27, ModificatoriInput.Nessuno)
                        Return
                    End If
                    EsciDalCampo()
                Case Keys.Enter
                    _accentoInSospeso = ""
                    If Not p.Shift AndAlso Not campoMultiriga Then RicordaTestoInviato()
                    _cdp.InviaTasto(WGCdpInterazione.VK_RETURN, 13, ModificatoriSemplici(p.Shift, False))
                    If campoMultiriga Then
                        RegistraTestoInserito(vbLf)
                        If EchoDigitazione() <> "0" Then Annuncia("A capo")
                    ElseIf modalitaDiretta Then
                        ProgrammaSincronizzazioneCampo()
                    End If
                Case Keys.Tab
                    Await InviaTabAsync(p.Shift)
                Case Keys.Back
                    CancellaCarattereNelCampo("back")
                Case Keys.Delete
                    CancellaCarattereNelCampo("delete")
                Case Keys.Left, Keys.Right, Keys.Home, Keys.End
                    Await SpostaCorsoreCampoConScript(p.Tasto, p.Shift, False)
                Case Keys.Up, Keys.Down
                    If campoMultiriga Then
                        Await SpostaRigaTextareaConScript(p.Tasto)
                    ElseIf campoConSuggerimenti Then
                        Await SpostaSuggerimentoConScript(p.Tasto)
                    ElseIf ruoloCampo = "spinbutton" Then
                        Await SpostaOpzioneComboConScript(p.Tasto)
                    ElseIf modalitaDiretta Then
                        _cdp.InviaTasto(CInt(p.Tasto And Keys.KeyCode), 0, ModificatoriInput.Nessuno)
                    Else
                        LeggiContenutoCampo()
                    End If
                Case Keys.PageUp, Keys.PageDown
                    If campoMultiriga Then
                        Await SpostaRigaTextareaConScript(p.Tasto)
                    Else
                        _cdp.InviaTasto(CInt(p.Tasto And Keys.KeyCode), 0, ModificatoriInput.Nessuno)
                    End If
                Case Keys.Insert, Keys.F8
                    LeggiContenutoCampo()
                Case Keys.Apps
                    _cdp.InviaTasto(CInt(Keys.Apps), 0, ModificatoriInput.Nessuno)
                Case Else
                    If p.Tasto >= Keys.F1 AndAlso p.Tasto <= Keys.F24 Then
                        _cdp.InviaTasto(CInt(p.Tasto And Keys.KeyCode), 0, ModificatoriSemplici(p.Shift, False))
                        Return
                    End If
                    Dim c As String = WGCaratteri.RisolviCarattere(p.Tasto, p.Shift, p.AltGr, _accentoInSospeso)
                    If Not String.IsNullOrEmpty(c) Then
                        InserisciCarattereNelCampo(c, p.Tasto, p.AltGr, p.Shift)
                        GestisciCharMessaggio(c(c.Length - 1))
                    ElseIf _accentoInSospeso <> "" AndAlso EchoDigitazione() <> "0" Then
                        Annuncia(WGCaratteri.NomeAccento(_accentoInSospeso))
                    End If
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoInCampo: " & ex.Message)
        End Try
    End Function

    Private Async Function GestisciTastoInCombo(p As PressioneTastoBrowser) As Task
        Try
            Select Case p.Tasto
                Case Keys.Escape
                    EsciDalCampo()
                Case Keys.Tab
                    Await InviaTabAsync(p.Shift)
                Case Keys.Enter
                    _cdp.InviaTasto(WGCdpInterazione.VK_RETURN, 13, ModificatoriInput.Nessuno)
                    Await AttendiInputAsync()
                    Await Task.Delay(100)
                    Dim stato As JObject = Await _navigatore.LeggiStatoCampoAsync()
                    Dim valore As String = If(stato IsNot Nothing, If(stato.Value(Of String)("v"), ""), "")
                    EsciDalCampo(True)
                    Annuncia(If(valore <> "", valore & ". ", "") & "Uscito dal campo")
                Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Space
                    Await SpostaOpzioneComboConScript(p.Tasto)
                Case Else
                    If p.Ctrl Then
                        InviaTastoGrezzo(p)
                        Return
                    End If
                    Dim c As String = WGCaratteri.RisolviCarattere(p.Tasto, p.Shift, p.AltGr, _accentoInSospeso)
                    If Not String.IsNullOrEmpty(c) Then Await SpostaOpzioneComboConScript(p.Tasto, c)
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoInCombo: " & ex.Message)
        End Try
    End Function

    Private Async Function GestisciTastoTabella(p As PressioneTastoBrowser) As Task
        Try
            Await _cursore.AssicuraBufferAsync()
            Dim testo As String = ""
            Select Case p.Tasto
                Case Keys.Left : testo = _cursore.SpostaInTabella(0, -1)
                Case Keys.Right : testo = _cursore.SpostaInTabella(0, 1)
                Case Keys.Up : testo = _cursore.SpostaInTabella(-1, 0)
                Case Keys.Down : testo = _cursore.SpostaInTabella(1, 0)
                Case Keys.Space, Keys.Enter : testo = _cursore.DescriviCellaCorrente()
                Case Else
                    InviaTastoGrezzo(p)
                    Return
            End Select
            If testo <> "" Then Annuncia(testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoTabella: " & ex.Message)
        End Try
    End Function

    Private Sub GestisciAltInNavigazione(p As PressioneTastoBrowser)
        Try
            Select Case p.Tasto
                Case Keys.Left
                    NavigaIndietro()
                Case Keys.Right
                    NavigaAvanti()
                Case Keys.Home
                    Naviga(WGImpostazioniBrowser.PaginaIniziale)
                Case Keys.D
                    _contenitore.ChiediIndirizzo()
                Case Else
                    InviaTastoGrezzo(p)
                    Dim nomeTasto As String = p.Tasto.ToString()
                    If (p.Tasto >= Keys.A AndAlso p.Tasto <= Keys.Z) OrElse (p.Tasto >= Keys.D0 AndAlso p.Tasto <= Keys.D9) Then nomeTasto = ChrW(CInt(p.Tasto And Keys.KeyCode)).ToString()
                    Annuncia("Alt " & If(p.Shift, "Shift ", "") & nomeTasto & " inviato alla pagina")
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciAltInNavigazione: " & ex.Message)
        End Try
    End Sub

    Private Async Function GestisciTastoNavigazioneConCtrl(p As PressioneTastoBrowser) As Task
        Try
            Select Case p.Tasto
                Case Keys.Home
                    Await _cursore.VaiAllInizioAsync()
                Case Keys.End
                    Await _cursore.VaiAllaFineAsync()
                Case Keys.Left
                    _cursore.LeggiParola(False)
                Case Keys.Right
                    _cursore.LeggiParola(True)
                Case Keys.Enter
                    Await AttivaDaTastieraAsync(If(p.Shift, ModificatoriInput.Shift, ModificatoriInput.Control))
                Case Keys.A
                    If p.Shift Then
                        lettura.FermaVoce()
                    Else
                        Await _cursore.AssicuraBufferAsync()
                        _cursore.SelezionaTutto()
                    End If
                Case Keys.F
                    CercaNellaPagina()
                Case Keys.G
                    Await CercaDiNuovoAsync(p.Shift)
                Case Keys.R
                    Ricarica()
                Case Keys.I
                    LeggiInformazioniPagina()
                Case Keys.P
                    Stampa()
                Case Keys.S
                    SalvaPagina()
                Case Keys.C, Keys.Insert
                    CopiaRigaCorrente()
                Case Keys.Oemplus, Keys.Add
                    CambiaZoom(1)
                Case Keys.OemMinus, Keys.Subtract
                    CambiaZoom(-1)
                Case Keys.D0, Keys.NumPad0
                    CambiaZoom(0)
                Case Keys.M
                    If p.Shift Then CompilaModulo() Else lettura.FermaVoce()
                Case Keys.V
                    If p.Shift Then RiproduciConWinGabriel("") Else lettura.FermaVoce()
                Case Keys.Y
                    If p.Shift Then RiproduciDallaPagina() Else lettura.FermaVoce()
                Case Keys.U
                    If p.Shift Then CommutaTraduzione() Else lettura.FermaVoce()
                Case Keys.K
                    If p.Shift Then Await GestisciVerificaAntiRobotAsync() Else lettura.FermaVoce()
                Case Else
                    lettura.FermaVoce()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoNavigazioneConCtrl: " & ex.Message)
        End Try
    End Function

    Private Async Function GestisciTastoNavigazione(p As PressioneTastoBrowser) As Task
        Try
            Dim indietro As Boolean = p.Shift
            Select Case p.Tasto
                Case Keys.Escape
                    If _cursore.SelezioneAttiva Then
                        _cursore.AnnullaSelezione()
                        Annuncia("Selezione annullata")
                    Else
                        _contenitore.RichiestaChiusuraDaEsc()
                    End If
                Case Keys.Down
                    Await _cursore.LeggiSuccessivaAsync()
                Case Keys.Up
                    Await _cursore.LeggiPrecedenteAsync()
                Case Keys.Right
                    _cursore.LeggiCarattere(True)
                Case Keys.Left
                    _cursore.LeggiCarattere(False)
                Case Keys.PageDown
                    Await _cursore.SaltaRigheAsync(15)
                Case Keys.PageUp
                    Await _cursore.SaltaRigheAsync(-15)
                Case Keys.Home
                    Await _cursore.VaiAllInizioAsync()
                Case Keys.End
                    Await _cursore.VaiAllaFineAsync()
                Case Keys.Enter
                    Await AttivaDaTastieraAsync(If(p.Shift, ModificatoriInput.Shift, ModificatoriInput.Nessuno))
                Case Keys.Space
                    Await AttivaDaTastieraAsync(ModificatoriInput.Nessuno)
                Case Keys.Insert, Keys.F8
                    Await _cursore.AssicuraBufferAsync()
                    _cursore.AvviaLetturaContinua()
                Case Keys.Tab
                    Await InviaTabAsync(indietro)
                Case Keys.Back
                    If indietro Then NavigaAvanti() Else NavigaIndietro()
                Case Keys.F7
                    MostraElenchiElementi()
                Case Keys.F10
                    If p.Shift Then MostraAzioniElemento()
                Case Keys.Apps
                    MostraAzioniElemento()
                Case Keys.H
                    Await _navigatore.SaltaAIntestazioneSuccessiva("qualsiasi", indietro)
                Case Keys.D1 : Await _navigatore.SaltaAIntestazioneSuccessiva("h1", indietro)
                Case Keys.D2 : Await _navigatore.SaltaAIntestazioneSuccessiva("h2", indietro)
                Case Keys.D3 : Await _navigatore.SaltaAIntestazioneSuccessiva("h3", indietro)
                Case Keys.D4 : Await _navigatore.SaltaAIntestazioneSuccessiva("h4", indietro)
                Case Keys.D5 : Await _navigatore.SaltaAIntestazioneSuccessiva("h5", indietro)
                Case Keys.D6 : Await _navigatore.SaltaAIntestazioneSuccessiva("h6", indietro)
                Case Keys.K
                    Await _navigatore.SaltaALinkSuccessivo(indietro)
                Case Keys.B
                    Await _navigatore.SaltaAPulsanteSuccessivo(indietro)
                Case Keys.V
                    Await _navigatore.SaltaACliccabileSuccessivo(indietro)
                Case Keys.F
                    Await _navigatore.SaltaACampoSuccessivo(indietro)
                Case Keys.E
                    Await _navigatore.SaltaACampoModificaSuccessivo(indietro)
                Case Keys.C
                    Await _navigatore.SaltaACasellaCombinataSuccessiva(indietro)
                Case Keys.X
                    Await _navigatore.SaltaACasellaControlloSuccessiva(indietro)
                Case Keys.O
                    Await _navigatore.SaltaAPulsanteOpzioneSuccessivo(indietro)
                Case Keys.G
                    Await _navigatore.SaltaAImmagineSuccessiva(indietro)
                Case Keys.J
                    Await _navigatore.SaltaAContenutoMultimedialeSuccessivo(indietro)
                Case Keys.R, Keys.D
                    Await _navigatore.SaltaARegioneLandmarkSuccessiva(indietro)
                Case Keys.L
                    Await _navigatore.SaltaAListaSuccessiva(indietro)
                Case Keys.I
                    Await _navigatore.SaltaAElementoListaSuccessivo(indietro)
                Case Keys.T
                    Await _navigatore.SaltaATabellaSuccessiva(indietro)
                Case Keys.M
                    Await _navigatore.SaltaAVoceDiMenuSuccessiva(indietro)
                Case Keys.Y
                    Await _navigatore.SaltaASchedaSuccessiva(indietro)
                Case Keys.A
                    Await _navigatore.SaltaAdArticoloSuccessivo(indietro)
                Case Keys.Q
                    Await _navigatore.SaltaACitazioneSuccessiva(indietro)
                Case Keys.P
                    Await _navigatore.SaltaAParagrafoSuccessivo(indietro)
                Case Keys.W
                    Await _navigatore.SaltaAFrameSuccessivo(indietro)
                Case Keys.N
                    Await _navigatore.SaltaATestoNonLinkSuccessivo(indietro)
                Case Keys.Z
                    VaiAiNuoviContenuti()
                Case Keys.F3
                    Await CercaDiNuovoAsync(indietro)
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciTastoNavigazione: " & ex.ToString())
        End Try
    End Function

    Private Sub VaiAiNuoviContenuti()
        Try
            Dim nuove As List(Of RigaVirtuale) = _cursore.UltimeRigheNuove
            If nuove Is Nothing OrElse nuove.Count = 0 Then
                Annuncia("Nessun contenuto nuovo dall'ultimo aggiornamento")
                Return
            End If
            Dim indice As Integer = _cursore.Righe.IndexOf(nuove(0))
            If indice < 0 Then
                Annuncia("Il contenuto nuovo non è più nella pagina")
                Return
            End If
            _cursore.SpostaA(indice, True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.VaiAiNuoviContenuti: " & ex.Message)
        End Try
    End Sub
End Class