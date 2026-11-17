Imports System.Collections.Generic
Imports System.IO
Imports System.Text
Imports System.Threading
Imports UIAutomationClient

' Gestione dei comandi estesi, configurazione delle preferenze di lettura, ricerca testuale, aiuto interattivo e supporto per la
' revisione dei testi.
Partial Public NotInheritable Class WGLettoreSchermo

    Private _codaVoce As CodaVoce
    Private _impostazioniLettore As ImpostazioniLettore
    Private _registroEventi As RegistroEventiUia
    Private _segnali As SegnaleAccessibilita
    Private _nomi As NomiAffidabili
    Private _attributi As LetturaAttributi
    Private _navigatore As NavigatoreOggetti
    Private _ocr As LetturaOcrSchermo
    Private _braille As UscitaBraille
    Private _watchdog As WatchdogAutomazione
    Private _supporto As SupportoSistema
    Private _diagnostica As DiagnosticaAlbero
    Private _ultimoTastoTick As Integer
    Private _ultimoTastoFreccetta As Integer
    Private _ultimoTastoScrittura As Integer
    Private _ultimoAnnuncioCampo As Integer
    Private _origineUtente As Integer
    Private _inRicerca As Integer
    Private _testoRicerca As String = ""
    Private _continuaAttiva As Integer
    Private _continuaPausa As Integer
    Private _continuaSentita As Integer
    Private _continuaTick As Integer
    Private _continuaVirtuale As Integer
    Private _intervalloContinua As IUIAutomationTextRange
    Private _righeContinua As List(Of String)
    Private _indiceContinua As Integer
    Private _revisionePiatta As Integer
    Private _righePiatte As List(Of String)
    Private _indicePiatto As Integer = -1
    Private _browserControllato As IntPtr
    Private _ultimaFinestraEventi As IntPtr
    Private _ultimaStrutturaEventi As Boolean
    Private _riavvioInCorso As Integer
    Private _brailleCollegato As Boolean

    Public Event BrailleCambiato(testo As String)

    Public Property LivelloPunteggiaturaLettore As Integer
        Get
            If _impostazioniLettore Is Nothing Then Return -1
            Return _impostazioniLettore.LivelloPunteggiatura
        End Get
        Set(value As Integer)
            AssicuraImpostazioni()
            If value < -1 Then value = -1
            If value > 3 Then value = 3
            _impostazioniLettore.LivelloPunteggiatura = value
            _impostazioniLettore.Salva()
        End Set
    End Property

    Public Property LetturaNumeri As String
        Get
            If _impostazioniLettore Is Nothing Then Return "eredita"
            Return _impostazioniLettore.Numeri
        End Get
        Set(value As String)
            AssicuraImpostazioni()
            _impostazioniLettore.Numeri = NormalizzaScelta(value, "eredita", "intero", "cifra", "coppie", "triplette")
            _impostazioniLettore.Salva()
        End Set
    End Property

    Public Property IndicazioneMaiuscole As String
        Get
            If _impostazioniLettore Is Nothing Then Return "parla"
            Return _impostazioniLettore.Maiuscole
        End Get
        Set(value As String)
            AssicuraImpostazioni()
            _impostazioniLettore.Maiuscole = NormalizzaScelta(value, "nessuna", "parla", "beep", "tutto")
            _impostazioniLettore.Salva()
        End Set
    End Property

    Public Property Prolissita As Integer
        Get
            If _impostazioniLettore Is Nothing Then Return 1
            Return _impostazioniLettore.Prolissita
        End Get
        Set(value As Integer)
            AssicuraImpostazioni()
            If value < 0 Then value = 0
            If value > 2 Then value = 2
            _impostazioniLettore.Prolissita = value
            DescrittoreElementi.ProlissitaApplicata = value
            _impostazioniLettore.Salva()
        End Set
    End Property

    Public Property LayoutTastiera As String
        Get
            If _impostazioniLettore Is Nothing Then Return "portatile"
            Return _impostazioniLettore.Layout
        End Get
        Set(value As String)
            AssicuraImpostazioni()
            _impostazioniLettore.Layout = NormalizzaScelta(value, "portatile", "desktop")
            _impostazioniLettore.Salva()
        End Set
    End Property

    Public Property OcrAutomatico As Boolean
        Get
            Return _impostazioniLettore Is Nothing OrElse _impostazioniLettore.OcrAutomatico
        End Get
        Set(value As Boolean)
            AssicuraImpostazioni()
            _impostazioniLettore.OcrAutomatico = value
            _impostazioniLettore.Salva()
        End Set
    End Property

    Public Property SuoniIndicatori As Boolean
        Get
            Return _impostazioniLettore Is Nothing OrElse _impostazioniLettore.Suoni
        End Get
        Set(value As Boolean)
            AssicuraImpostazioni()
            _impostazioniLettore.Suoni = value
            SuoniLettore.Abilitati = value
            _impostazioniLettore.Salva()
        End Set
    End Property

    Public Property BrailleAttivo As Boolean
        Get
            Return _impostazioniLettore IsNot Nothing AndAlso _impostazioniLettore.BrailleAttivo
        End Get
        Set(value As Boolean)
            AssicuraImpostazioni()
            _impostazioniLettore.BrailleAttivo = value
            _impostazioniLettore.Salva()
            If value AndAlso _braille IsNot Nothing Then _braille.ImpostaPorta(_impostazioniLettore.PortaBraille)
        End Set
    End Property

    Public Property PortaBraille As String
        Get
            If _impostazioniLettore Is Nothing Then Return ""
            Return _impostazioniLettore.PortaBraille
        End Get
        Set(value As String)
            AssicuraImpostazioni()
            _impostazioniLettore.PortaBraille = If(value, "")
            _impostazioniLettore.Salva()
            If _braille IsNot Nothing Then _braille.ImpostaPorta(_impostazioniLettore.PortaBraille)
        End Set
    End Property

    Public Sub RimappaComando(nomeComando As String, gesto As String)
        Try
            AssicuraImpostazioni()
            Dim comando As ComandoLettore
            If Not [Enum].TryParse(If(nomeComando, ""), True, comando) Then Return
            _impostazioniLettore.ImpostaRimappa(comando, gesto)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RimappaComando: " & ex.Message)
        End Try
    End Sub

    Public Function TestoSupportoSistema() As String
        Try
            If _supporto Is Nothing Then Return ""
            Return _supporto.Riassunto()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoSupportoSistema: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function VerificaInterna() As String
        Try
            Dim braille As String = UscitaBraille.Verifica()
            If braille <> "" Then Return "braille " & braille
            Dim filtro As New FiltroEventi()
            If Not filtro.Accetta("a", 100) Then Return "filtro"
            If filtro.Accetta("a", 100) Then Return "filtro antirimbalzo"
            If Not filtro.Accetta("b", 100) Then Return "filtro chiave"
            Return ""
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.VerificaInterna", ex)
            Return ex.Message
        End Try
    End Function

    Friend ReadOnly Property AiutoTastiAttivo As Boolean
        Get
            Return _impostazioniLettore IsNot Nothing AndAlso _impostazioniLettore.AiutoTasti
        End Get
    End Property

    Friend ReadOnly Property InRicercaAttiva As Boolean
        Get
            Return Volatile.Read(_inRicerca) = 1
        End Get
    End Property

    Friend Function CatturaTastierino() As Boolean
        Return _impostazioniLettore IsNot Nothing AndAlso _impostazioniLettore.Layout = "desktop"
    End Function

    Friend Function ComandoDaGesto(codice As Integer, ctrl As Boolean, maiuscolo As Boolean, ByRef comando As ComandoLettore) As Boolean
        Try
            If _impostazioniLettore Is Nothing Then Return False
            Return _impostazioniLettore.ProvaGesto(FormatoGesto(codice, ctrl, maiuscolo), comando)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ComandoDaGesto", exAtteso)
            Return False
        End Try
    End Function

    Friend Function IgnoraPredefinito(comando As ComandoLettore) As Boolean
        Try
            Return _impostazioniLettore IsNot Nothing AndAlso _impostazioniLettore.ComandoHaRimappa(comando)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.IgnoraPredefinito", exAtteso)
            Return False
        End Try
    End Function

    Friend Sub ParlaAiuto(testo As String)
        AccodaVoceUtente(If(testo = "", "nessuna azione", testo))
    End Sub

    Friend Sub AccodaCarattereRicerca(carattere As String)
        Try
            If String.IsNullOrEmpty(carattere) Then Return
            _testoRicerca &= carattere
            AccodaVoceUtente(carattere)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaCarattereRicerca: " & ex.Message)
        End Try
    End Sub

    Friend Sub CancellaRicerca()
        Try
            If _testoRicerca = "" Then
                AccodaVoceUtente("vuoto")
                Return
            End If
            Dim tolto As String = _testoRicerca.Substring(_testoRicerca.Length - 1)
            _testoRicerca = _testoRicerca.Substring(0, _testoRicerca.Length - 1)
            AccodaVoceUtente(tolto)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CancellaRicerca: " & ex.Message)
        End Try
    End Sub

    Friend Sub ConfermaRicerca()
        Try
            Volatile.Write(_inRicerca, 0)
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Ricerca
            evento.Testo = _testoRicerca
            _testoRicerca = ""
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ConfermaRicerca: " & ex.Message)
        End Try
    End Sub

    Friend Sub AnnullaRicerca()
        Volatile.Write(_inRicerca, 0)
        _testoRicerca = ""
        AccodaVoceUtente("ricerca annullata")
    End Sub

    Private Sub AccodaVoceUtente(testo As String)
        Try
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.VoceUtente
            evento.Testo = If(testo, "")
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AccodaVoceUtente: " & ex.Message)
        End Try
    End Sub

    Private Sub AlCambioBraille(testo As String)
        Try
            RaiseEvent BrailleCambiato(testo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AlCambioBraille: " & ex.Message)
        End Try
    End Sub

    Friend Sub SegnaTasto()
        Volatile.Write(_ultimoTastoTick, Environment.TickCount)
        If _codaVoce IsNot Nothing Then _codaVoce.TogliPausaUtente()
        FermaLetturaContinua(False)
    End Sub

    Private Sub CollegaEstensioni()
        Try
            AssicuraImpostazioni()
            ApplicaImpostazioniCorrenti()
            If _codaVoce Is Nothing Then
                _codaVoce = New CodaVoce(_impostazioniLettore)
                _codaVoce.FiltroSilenzio = AddressOf SilenzioEsterno
                _codaVoce.Avvia()
            End If
            If _segnali Is Nothing Then
                _segnali = New SegnaleAccessibilita()
                _segnali.Attiva()
            End If
            If _supporto Is Nothing Then _supporto = New SupportoSistema()
            If _watchdog Is Nothing Then _watchdog = New WatchdogAutomazione()
            If _ocr Is Nothing Then _ocr = New LetturaOcrSchermo()
            If _braille Is Nothing Then _braille = New UscitaBraille()
            If Not _brailleCollegato Then
                AddHandler _braille.Aggiornato, AddressOf AlCambioBraille
                _brailleCollegato = True
            End If
            If _impostazioniLettore.BrailleAttivo Then _braille.ImpostaPorta(_impostazioniLettore.PortaBraille)
            If _diagnostica Is Nothing Then _diagnostica = New DiagnosticaAlbero()
            _attributi = New LetturaAttributi()
            _nomi = New NomiAffidabili(_automazione, _descrittore, _richiestaCache)
            If _descrittore IsNot Nothing Then _descrittore.RisolutoreNomi = AddressOf _nomi.Risolvi
            _navigatore = New NavigatoreOggetti(_automazione, _richiestaCache)
            If _registroEventi IsNot Nothing Then _registroEventi.Ferma()
            _registroEventi = New RegistroEventiUia()
            _registroEventi.Avvia(_automazione, If(_richiestaCacheEventi, _richiestaCache), AddressOf Accoda, 0)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CollegaEstensioni: " & ex.Message)
        End Try
    End Sub

    Private Sub ScollegaEstensioni()
        Try
            FermaLetturaContinua(False)
            If _registroEventi IsNot Nothing Then
                _registroEventi.Ferma()
                _registroEventi = Nothing
            End If
            If _descrittore IsNot Nothing Then _descrittore.RisolutoreNomi = Nothing
            _nomi = Nothing
            _navigatore = Nothing
            If Volatile.Read(_inEsecuzione) = 0 Then
                If _codaVoce IsNot Nothing Then
                    _codaVoce.Ferma()
                    _codaVoce = Nothing
                End If
                If _segnali IsNot Nothing Then
                    _segnali.Ripristina()
                    _segnali = Nothing
                End If
                If _braille IsNot Nothing Then
                    If _brailleCollegato Then RemoveHandler _braille.Aggiornato, AddressOf AlCambioBraille
                    _brailleCollegato = False
                    _braille.Chiudi()
                    _braille = Nothing
                End If
                If _impostazioniLettore IsNot Nothing Then _impostazioniLettore.Salva()
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ScollegaEstensioni: " & ex.Message)
        End Try
    End Sub

    Private Sub BattitoLettore()
        Try
            If SilenzioEsterno() Then
                If Volatile.Read(_continuaAttiva) = 1 Then FermaLetturaContinua(False)
            Else
                AvanzaLetturaContinua()
                OttieniModuloForegroundApp().AllBattito()
                If _impostazioniLettore IsNot Nothing AndAlso _impostazioniLettore.TracciamentoMouse Then LeggiSottoMouse()
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.BattitoLettore: " & ex.Message)
        End Try
    End Sub

    Public Sub RicaricaImpostazioni()
        Try
            If _impostazioniLettore Is Nothing Then
                AssicuraImpostazioni()
            Else
                _impostazioniLettore.Carica()
                ApplicaImpostazioniCorrenti()
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RicaricaImpostazioni", ex)
        End Try
    End Sub

    Private Sub AssicuraImpostazioni()
        If _impostazioniLettore IsNot Nothing Then Return
        _impostazioniLettore = New ImpostazioniLettore()
        _impostazioniLettore.Carica()
        ApplicaImpostazioniCorrenti()
    End Sub

    Private Sub ApplicaImpostazioniCorrenti()
        If _impostazioniLettore Is Nothing Then Return
        DescrittoreElementi.ProlissitaApplicata = _impostazioniLettore.Prolissita
        LetturaCampi.RientriApplicati = _impostazioniLettore.Rientri
        SuoniLettore.Abilitati = _impostazioniLettore.Suoni
    End Sub

    Private Function NormalizzaScelta(valore As String, ParamArray ammessi() As String) As String
        Dim testo As String = If(valore, "").Trim().ToLowerInvariant()
        For Each ammesso In ammessi
            If testo = ammesso Then Return ammesso
        Next
        Return ammessi(0)
    End Function

    Private Function FormatoGesto(codice As Integer, ctrl As Boolean, maiuscolo As Boolean) As String
        Dim parti As New List(Of String)()
        If ctrl Then parti.Add("ctrl")
        If maiuscolo Then parti.Add("shift")
        parti.Add(ImpostazioniLettore.NomeTasto(codice).Replace(" "c, "-"c))
        Return String.Join("+", parti)
    End Function

    Private Sub SegnaErroreAutomazione(ex As Exception)
        Try
            If _watchdog Is Nothing Then Return
            If _watchdog.Segna(ex) Then RiavviaAutomazione(ex.Message)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SegnaErroreAutomazione", exAtteso)
        End Try
    End Sub

    Private Sub AzzeraErroriAutomazione()
        Try
            If _watchdog IsNot Nothing Then _watchdog.Successo()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AzzeraErroriAutomazione", exAtteso)
        End Try
    End Sub

    Private Sub RiavviaAutomazione(motivo As String)
        Try
            If Interlocked.CompareExchange(_riavvioInCorso, 1, 0) <> 0 Then Return
            RilasciaAutomazione()
            If InizializzaAutomazione() Then
                If _watchdog IsNot Nothing Then _watchdog.SegnaRiavvio()
                Parla("Automazione riavviata", True)
            Else
                Parla("Automazione non disponibile", True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RiavviaAutomazione: " & ex.Message)
        Finally
            Interlocked.Exchange(_riavvioInCorso, 0)
        End Try
    End Sub

    Private Sub AnnullaParlato()
        Try
            FermaLetturaContinua(False)
            If _codaVoce IsNot Nothing Then _codaVoce.Annulla()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnullaParlato: " & ex.Message)
        End Try
    End Sub

    Private Function ElaboraEventoEsteso(evento As EventoLettore) As Boolean
        Try
            If evento Is Nothing Then Return False
            Select Case evento.Tipo
                Case TipoEventoLettore.Notifica
                    If evento.Elemento IsNot Nothing AndAlso OttieniModuloApp(evento.Elemento).AllNotifica(evento) Then Return True
                    ParlaPriorita(evento.Testo, evento.PrioritaVoce)
                    Return True
                Case TipoEventoLettore.TestoModificato
                    Dim chiamataTesto As New Chiamata(Of EventoLettore)(AddressOf AnnunciaTestoModificato, evento)
                    EseguiSuOperazioni("testoModificato", AddressOf chiamataTesto.Esegui, sostituisci:=True)
                    Return True
                Case TipoEventoLettore.ProprietaCambiata
                    Dim chiamataProprieta As New Chiamata(Of EventoLettore)(AddressOf AnnunciaProprieta, evento)
                    EseguiSuOperazioni("proprieta", AddressOf chiamataProprieta.Esegui, sostituisci:=True)
                    Return True
                Case TipoEventoLettore.StrutturaCambiata
                    If _navigazione IsNot Nothing Then _navigazione.SegnaStrutturaObsoleta()
                    Return True
                Case TipoEventoLettore.MenuAperto
                    If OttieniModuloForegroundApp().AllMenuAperto(evento) Then Return True
                    If evento.Elemento IsNot Nothing AndAlso Not EVirtualePerMenu(evento.Elemento) Then
                        MenuApertoNativo(evento.Elemento)
                        Return True
                    End If
                    Dim chiamataMenu As New Chiamata(Of EventoLettore)(AddressOf ApriMenuDaEvento, evento)
                    EseguiSuOperazioni("menuAperto", AddressOf chiamataMenu.Esegui, sostituisci:=True)
                    Return True
                Case TipoEventoLettore.MenuChiuso
                    EseguiSuOperazioni("", AddressOf ChiudiMenuVirtualeSilenzioso)
                    EseguiSuOperazioni("", AddressOf ControllaMenuChiusoNativo)
                    Return True
                Case TipoEventoLettore.FinestraChiusa
                    Return True
                Case TipoEventoLettore.Tooltip
                    If Not TastoRecente(400) AndAlso TooltipDaLeggere(evento.Testo) Then ParlaPriorita(evento.Testo, 0)
                    Return True
                Case TipoEventoLettore.PosizioneTesto
                    If TastoRecente(220) OrElse Volatile.Read(_continuaAttiva) = 1 Then Return True
                    Dim chiamataPosizione As New Chiamata(Of IUIAutomationElement)(AddressOf LeggiPosizioneTesto, evento.Elemento)
                    EseguiSuOperazioni("posizioneTesto", AddressOf chiamataPosizione.Esegui, sostituisci:=True)
                    Return True
                Case TipoEventoLettore.VoceUtente
                    Volatile.Write(_origineUtente, 1)
                    Try
                        Parla(If(evento.Testo = "", "nessuna azione", evento.Testo), True)
                    Finally
                        Volatile.Write(_origineUtente, 0)
                    End Try
                    Return True
                Case TipoEventoLettore.FocusJava
                    If evento.Testo <> "" Then Parla(evento.Testo, True)
                    Return True
                Case TipoEventoLettore.Ricerca
                    Dim chiamataRicerca As New Chiamata(Of String)(AddressOf RicercaDopoUscita, evento.Testo)
                    EseguiSuOperazioni("", AddressOf chiamataRicerca.Esegui)
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ElaboraEventoEsteso: " & ex.Message)
            Return True
        End Try
    End Function

    Private _ultimoTooltip As String = ""
    Private _ultimoTooltipTick As Integer

    Private Shared Function NormalizzaTesto(testo As String) As String
        If testo Is Nothing Then Return ""
        Return String.Join(" ", testo.Split(New Char() {" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)).Trim()
    End Function

    Private Function TooltipDaLeggere(testo As String) As Boolean
        Try
            Dim pulito As String = NormalizzaTesto(testo)
            If pulito = "" Then Return False
            Dim adesso As Integer = Environment.TickCount
            If String.Equals(pulito, _ultimoTooltip, StringComparison.CurrentCultureIgnoreCase) AndAlso adesso - _ultimoTooltipTick < 15000 Then Return False
            If adesso - _ultimoFocusTick < 6000 Then
                Dim annuncio As String = NormalizzaTesto(_ultimoAnnuncioAlbero)
                If annuncio <> "" AndAlso annuncio.IndexOf(pulito, StringComparison.CurrentCultureIgnoreCase) >= 0 Then Return False
                If String.Equals(NormalizzaTesto(_ultimoNome), pulito, StringComparison.CurrentCultureIgnoreCase) Then Return False
            End If
            _ultimoTooltip = pulito
            _ultimoTooltipTick = adesso
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.TooltipDaLeggere", exAtteso)
            Return True
        End Try
    End Function

    Private _ultimoProgresso As Integer = -1000
    Private _ultimoProgressoTick As Integer
    Private _ultimaChiaveProgresso As String = ""

    Private Function AnnunciaIntervallo(evento As EventoLettore) As Boolean
        Try
            If evento.IdProprieta <> 30047 OrElse evento.Elemento Is Nothing Then Return False
            Dim tipo As Integer = _descrittore.TipoControllo(evento.Elemento)
            If tipo = DescrittoreElementi.ControlloBarraAvanzamento Then
                Dim valore As Integer = CInt(Math.Round(_descrittore.LeggiIntero(evento.Elemento, DescrittoreElementi.ProprietaValoreIntervallo, -1)))
                If valore < 0 Then Return True
                If BipAvanzamento Then
                    SuonaProgresso(Math.Min(100, valore))
                    Return True
                End If
                Dim chiave As String = ChiaveElemento(evento.Elemento)
                Dim adesso As Integer = Environment.TickCount
                If chiave <> _ultimaChiaveProgresso Then
                    _ultimaChiaveProgresso = chiave
                    _ultimoProgresso = -1000
                End If
                Dim passo As Boolean = Math.Abs(valore - _ultimoProgresso) >= 10 OrElse (valore >= 100 AndAlso _ultimoProgresso < 100)
                If Not passo OrElse adesso - _ultimoProgressoTick < 300 Then Return True
                _ultimoProgresso = valore
                _ultimoProgressoTick = adesso
                ParlaPriorita(valore.ToString() & " per cento", 0)
                If _codaVoce IsNot Nothing AndAlso SuoniIndicatori Then _codaVoce.SuonaLimite()
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaIntervallo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AnnunciaProprieta(evento As EventoLettore)
        Try
            If AnnunciaIntervallo(evento) Then Return
            If evento.Elemento Is Nothing OrElse Not RiguardaUtente(evento.Elemento) Then Return
            If OttieniModuloApp(evento.Elemento).EventoSoppresso(evento.Elemento) Then Return
            If TastoRecente(280) AndAlso (evento.IdProprieta = 30005 OrElse evento.IdProprieta = 30045) Then Return
            If Environment.TickCount - Volatile.Read(_ultimoFocusTick) < 250 Then Return
            If evento.IdProprieta = 30079 Then
                If Environment.TickCount - Volatile.Read(_ultimoFocusTick) < 450 Then Return
                If evento.Testo <> "" Then Parla(evento.Testo, False)
                Return
            End If
            Dim testo As String = _descrittore.ParteVariabile(evento.Elemento)
            If evento.IdProprieta = 30005 Then testo = _descrittore.Descrivi(evento.Elemento)
            If testo = "" Then Return
            Parla(testo, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AnnunciaProprieta: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiPosizioneTesto(elemento As IUIAutomationElement)
        Try
            If _attributi Is Nothing Then Return
            Dim carattere As String = _attributi.UnitaAlCursore(elemento, TextUnit.TextUnit_Character)
            If carattere = "" AndAlso _campi IsNot Nothing Then carattere = _campi.LeggiCarattere(elemento)
            If carattere = "" Then Return
            Dim frase As String = _descrittore.NomeCarattere(carattere.Substring(0, 1))
            If _impostazioniLettore IsNot Nothing AndAlso _impostazioniLettore.AnnunciaFormato Then
                Dim formato As String = _attributi.FormatoCambiato(elemento)
                If formato <> "" Then frase &= ", " & formato
            End If
            Parla(frase, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiPosizioneTesto: " & ex.Message)
        End Try
    End Sub

    Private Function RiguardaUtente(elemento As IUIAutomationElement) As Boolean
        Try
            If StessoElemento(elemento, _ultimoElemento) OrElse StessoElemento(elemento, _revisione) Then Return True
            Return elemento.CurrentHasKeyboardFocus <> 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RiguardaUtente", exAtteso)
            Return False
        End Try
    End Function

    Private Sub EseguiRicerca(ago As String)
        Volatile.Write(_origineUtente, 1)
        Try
            SilenziaFocusBreve()
            If _navigazione Is Nothing OrElse String.IsNullOrEmpty(ago) Then
                Parla("ricerca annullata", True)
                Return
            End If
            Dim prima As IUIAutomationElement = _navigazione.ElementoCorrente()
            Dim frase As String = _navigazione.CercaTesto(ago)
            _revisione = _navigazione.ElementoCorrente()
            If Not ReferenceEquals(prima, _revisione) Then SegnaCursoreDaTasti()
            Parla(If(frase = "", "non trovato", frase), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiRicerca: " & ex.Message)
        Finally
            Volatile.Write(_origineUtente, 0)
        End Try
    End Sub

    Private Function TastoRecente(millisecondi As Integer) As Boolean
        Return MillisecondiDa(Volatile.Read(_ultimoTastoTick)) < CLng(millisecondi)
    End Function

    Private _elementiPagina As Dictionary(Of String, List(Of VoceElementoPagina))

    Private NotInheritable Class RichiestaElementoPagina
        Private ReadOnly _lettore As WGLettoreSchermo
        Private ReadOnly _voce As VoceElementoPagina
        Private ReadOnly _attiva As Boolean

        Public Sub New(lettore As WGLettoreSchermo, voce As VoceElementoPagina, attiva As Boolean)
            _lettore = lettore
            _voce = voce
            _attiva = attiva
        End Sub

        Public Sub Esegui()
            Try
                If _voce Is Nothing Then Return
                _lettore.EseguiElementoPagina(_voce.Elemento, _attiva)
            Catch ex As Exception
                _lettore.log.ScriviLog("WGLettoreSchermo.RichiestaElementoPagina.Esegui: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Sub ApriElencoElementi()
        Try
            If _navigazione Is Nothing OrElse Not _navAttiva OrElse _navigazione.NumeroVoci = 0 Then
                Parla("Nessuna pagina", True)
                Return
            End If
            EseguiSuOperazioni("elencoElementi", AddressOf PreparaElencoElementi, sostituisci:=True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ApriElencoElementi: " & ex.Message)
        End Try
    End Sub

    Private Sub PreparaElencoElementi()
        Try
            If _navigazione Is Nothing Then Return
            _elementiPagina = _navigazione.ElementiPagina()
            Dim finestre As FormCollection = Application.OpenForms
            If finestre IsNot Nothing AndAlso finestre.Count > 0 AndAlso finestre(0) IsNot Nothing AndAlso finestre(0).IsHandleCreated Then
                finestre(0).BeginInvoke(New Action(AddressOf MostraElencoElementi))
                Return
            End If
            Dim filo As New Thread(AddressOf MostraElencoElementi)
            filo.IsBackground = True
            filo.SetApartmentState(ApartmentState.STA)
            filo.Start()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.PreparaElencoElementi: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraElencoElementi()
        Try
            Dim voci As Dictionary(Of String, List(Of VoceElementoPagina)) = _elementiPagina
            _elementiPagina = Nothing
            If voci Is Nothing Then Return
            Dim attiva As Boolean = False
            Dim scelta As VoceElementoPagina = FinestraElementi.Scegli(voci, AddressOf SilenziaFocusBreve, attiva)
            If scelta Is Nothing Then Return
            Dim richiesta As New RichiestaElementoPagina(Me, scelta, attiva)
            EseguiSuOperazioni("", AddressOf richiesta.Esegui)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MostraElencoElementi: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiElementoPagina(elemento As IUIAutomationElement, attiva As Boolean)
        Try
            If elemento Is Nothing OrElse _navigazione Is Nothing Then Return
            Thread.Sleep(150)
            Volatile.Write(_ultimoMovimentoVirtuale, Environment.TickCount)
            Volatile.Write(_cursoreDaTasti, 1)
            Dim frase As String = _navigazione.PortaCursoreSu(elemento)
            _revisione = elemento
            If attiva Then
                Volatile.Write(_ultimoFuocoChiesto, Environment.TickCount)
                Dim esito As String = _navigazione.Attiva(elemento)
                If esito = "scrittura" Then Return
                Parla(If(esito <> "", esito, frase), True)
                Return
            End If
            Parla(frase, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiElementoPagina: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampoRicerca()
        Try
            If Not BufferInPrimoPiano() OrElse Not _navigazione.EWeb Then
                Parla("Nessuna pagina da cercare", True)
                Return
            End If
            Dim finestre As FormCollection = Application.OpenForms
            If finestre IsNot Nothing AndAlso finestre.Count > 0 AndAlso finestre(0) IsNot Nothing AndAlso finestre(0).IsHandleCreated Then
                finestre(0).BeginInvoke(New Action(AddressOf MostraCampoRicerca))
                Return
            End If
            Dim filo As New Thread(AddressOf MostraCampoRicerca)
            filo.IsBackground = True
            filo.SetApartmentState(ApartmentState.STA)
            filo.Start()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ApriCampoRicerca: " & ex.Message)
            Volatile.Write(_inRicerca, 1)
            _testoRicerca = ""
            Parla("Cerca", True)
        End Try
    End Sub

    Private Sub MostraCampoRicerca()
        Try
            Dim ago As String = FinestraCerca.Chiedi(AddressOf SilenziaFocusBreve)
            If ago = "" Then Return
            Dim evento As New EventoLettore()
            evento.Tipo = TipoEventoLettore.Ricerca
            evento.Testo = ago
            Accoda(evento)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.MostraCampoRicerca: " & ex.Message)
            Volatile.Write(_inRicerca, 1)
            _testoRicerca = ""
            Parla("Cerca", True)
        End Try
    End Sub

    Private Sub SilenziaFocusBreve()
        Try
            Dim adesso As Integer = Environment.TickCount
            Dim fino As Long = CLng(adesso) + 1800L
            If fino > CLng(Integer.MaxValue) Then fino -= 4294967296L
            Volatile.Write(_silenziaFocusDa, adesso)
            Volatile.Write(_silenziaFocusFino, CInt(fino))
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SilenziaFocusBreve: " & ex.Message)
        End Try
    End Sub

    Private Function FocusSilenziato() As Boolean
        Try
            Dim da As Integer = Volatile.Read(_silenziaFocusDa)
            Dim fino As Integer = Volatile.Read(_silenziaFocusFino)
            If da = 0 AndAlso fino = 0 Then Return False
            Dim trascorso As Long = MillisecondiDa(da)
            If trascorso >= 1800L Then Return False
            Return MillisecondiDa(Volatile.Read(_ultimoTastoTick)) > trascorso
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.FocusSilenziato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ParlaPriorita(testo As String, priorita As Integer)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            If AnnuncioDaSopprimere(testo) Then Return
            If SilenzioEsterno() Then Return
            If _codaVoce Is Nothing Then
                Parla(testo, priorita >= 2)
                Return
            End If
            Dim livello As PrioritaAnnuncio = PrioritaAnnuncio.Normale
            If priorita <= 0 Then livello = PrioritaAnnuncio.Bassa
            If priorita = 2 Then livello = PrioritaAnnuncio.Interruzione
            If priorita >= 3 Then livello = PrioritaAnnuncio.Allarme
            Dim interrompi As Boolean = livello >= PrioritaAnnuncio.Interruzione AndAlso (livello >= PrioritaAnnuncio.Allarme OrElse InterruzioneConsentita())
            _codaVoce.Accoda(testo, livello, interrompi, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ParlaPriorita: " & ex.Message)
        End Try
    End Sub

    Private Function EseguiComandoEsteso(comando As ComandoLettore) As Boolean
        Try
            Select Case comando
                Case ComandoLettore.AiutoTasti
                    AssicuraImpostazioni()
                    _impostazioniLettore.AiutoTasti = Not _impostazioniLettore.AiutoTasti
                    Parla(If(_impostazioniLettore.AiutoTasti, "Aiuto tasti attivo", "Aiuto tasti disattivato"), True)
                    Return True
                Case ComandoLettore.Formattazione
                    Parla(TestoFormato(), True)
                    Return True
                Case ComandoLettore.PausaLettura
                    AlternaPausa()
                    Return True
                Case ComandoLettore.TracciaMouse
                    AssicuraImpostazioni()
                    _impostazioniLettore.TracciamentoMouse = Not _impostazioniLettore.TracciamentoMouse
                    _impostazioniLettore.Salva()
                    Parla(If(_impostazioniLettore.TracciamentoMouse, "Tracciamento del mouse attivo", "Tracciamento del mouse disattivato"), True)
                    Return True
                Case ComandoLettore.PortaMouse
                    If _navigatore IsNot Nothing AndAlso _navigatore.PortaMouse(ElementoRevisione()) Then
                        Parla("Puntatore spostato", True)
                    Else
                        Parla("Non posso spostare il puntatore", True)
                    End If
                    Return True
                Case ComandoLettore.Clicca
                    If _navigatore IsNot Nothing AndAlso _navigatore.Clicca(ElementoRevisione()) Then
                        Parla("Clic", True)
                    Else
                        Parla("Clic non riuscito", True)
                    End If
                    Return True
                Case ComandoLettore.ElencoElementi
                    ApriElencoElementi()
                    Return True
                Case ComandoLettore.CercaPagina
                    ApriCampoRicerca()
                    Return True
                Case ComandoLettore.ScaricaAlbero
                    ScaricaAlbero()
                    Return True
                Case ComandoLettore.Prolissita
                    Dim livello As Integer = Prolissita + 1
                    If livello > 2 Then livello = 0
                    Prolissita = livello
                    Parla("Prolissità " & If(livello = 0, "bassa", If(livello = 1, "normale", "alta")), True)
                    Return True
                Case ComandoLettore.Braille
                    BrailleAttivo = Not BrailleAttivo
                    Parla(If(BrailleAttivo, "Braille attivo", "Braille disattivato"), True)
                    Return True
                Case ComandoLettore.RevisionePiatta
                    AlternaRevisionePiatta()
                    Return True
                Case ComandoLettore.Ocr
                    Parla(TestoOcr(True), True)
                    Return True
                Case ComandoLettore.MenuLettore
                    RaiseEvent MenuRichiesto()
                    Return True
                Case ComandoLettore.SalvaProfilo
                    SalvaProfiloProgramma()
                    Return True
                Case ComandoLettore.TogliProfilo
                    TogliProfiloProgramma()
                    Return True
                Case ComandoLettore.RipetiUltimo
                    ParlaDalloStorico(False)
                    Return True
                Case ComandoLettore.StoricoVoce
                    ParlaDalloStorico(True)
                    Return True
                Case ComandoLettore.CopiaUltimo
                    CopiaUltimaFrase()
                    Return True
                Case ComandoLettore.Appunti
                    LeggiAppunti()
                    Return True
                Case ComandoLettore.LettereRapide
                    Dim spente As Boolean = Not LettereRapideSpente
                    Volatile.Write(_lettereRapideSpente, If(spente, 1, 0))
                    Parla(If(spente, "navigazione con le lettere disattivata", "navigazione con le lettere attivata"), True)
                    Return True
            End Select
            Return False
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.EseguiComandoEsteso: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub ParlaSenzaStorico(testo As String)
        Try
            If _codaVoce IsNot Nothing Then _codaVoce.SenzaStorico = True
            Try
                Parla(testo, True)
            Finally
                If _codaVoce IsNot Nothing Then _codaVoce.SenzaStorico = False
            End Try
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ParlaSenzaStorico", ex)
        End Try
    End Sub

    Private Sub ParlaDalloStorico(indietro As Boolean)
        Try
            If _codaVoce Is Nothing Then Return
            Dim frase As String = _codaVoce.FraseDelloStorico(indietro)
            ParlaSenzaStorico(If(frase = "", If(indietro, "non c'è altro", "niente da ripetere"), frase))
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.ParlaDalloStorico", ex)
        End Try
    End Sub

    Private Sub CopiaUltimaFrase()
        Try
            If _codaVoce Is Nothing Then Return
            Dim frase As String = _codaVoce.FraseDelloStorico(False)
            If frase = "" Then
                ParlaSenzaStorico("niente da copiare")
            ElseIf NativoLettore.CopiaTesto(frase) Then
                ParlaSenzaStorico("copiato")
            Else
                ParlaSenzaStorico("copia non riuscita")
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.CopiaUltimaFrase", ex)
        End Try
    End Sub

    Private Sub LeggiAppunti()
        Try
            Dim testo As String = NativoLettore.LeggiTestoAppunti().Trim()
            If testo = "" Then
                ParlaSenzaStorico("gli appunti sono vuoti")
            ElseIf testo.Length > 1500 Then
                ParlaSenzaStorico("appunti, " & testo.Length.ToString() & " caratteri. " & testo.Substring(0, 1500))
            Else
                ParlaSenzaStorico("appunti, " & testo)
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.LeggiAppunti", ex)
        End Try
    End Sub

    Public Event MenuRichiesto()

    Private Function ElementoRevisione() As IUIAutomationElement
        If _revisione IsNot Nothing Then Return _revisione
        Return ElementoConFocus()
    End Function

    Private Function TestoFormato() As String
        Try
            Dim elemento As IUIAutomationElement = ElementoConFocus()
            If elemento Is Nothing Then Return "nessun formato"
            Dim formatoApp As String = OttieniModuloApp(elemento).FormatoApp(elemento)
            If formatoApp <> "" Then Return formatoApp
            If _attributi Is Nothing Then Return "nessun formato"
            Dim testo As String = _attributi.DescriviFormato(elemento)
            If testo = "" Then Return "nessun formato"
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoFormato: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ScaricaAlbero()
        Try
            If _diagnostica Is Nothing OrElse _automazione Is Nothing Then
                Parla("Diagnostica non disponibile", True)
                Return
            End If
            Dim percorso As String = _diagnostica.Scrivi(_automazione, _descrittore, _richiestaCache, ElementoConFocus())
            If percorso = "" Then
                Parla("Albero non scritto", True)
                Return
            End If
            Parla("Albero scritto, " & Path.GetFileName(percorso), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ScaricaAlbero: " & ex.Message)
        End Try
    End Sub

    Private Function TestoOcr(forzato As Boolean) As String
        Try
            If _ocr Is Nothing OrElse _automazione Is Nothing Then Return ""
            Dim assente As String = _ocr.MessaggioAssente()
            If assente <> "" AndAlso Not _ocr.Disponibile Then Return assente
            Dim elemento As IUIAutomationElement = ElementoRevisione()
            Dim testo As String = _ocr.LeggiElemento(_automazione, elemento)
            If testo = "" Then Return If(forzato, "nessun testo riconosciuto", "")
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoOcr: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ProgrammaOcrElemento(elemento As IUIAutomationElement)
        Try
            If _ocr Is Nothing OrElse _operazioni Is Nothing Then Return
            Dim assente As String = _ocr.MessaggioAssente()
            If assente <> "" AndAlso Not _ocr.Disponibile Then Return
            Dim chiamataOcr As New Chiamata(Of IUIAutomationElement)(AddressOf LeggiOcrElemento, elemento)
            _operazioni.Esegui("ocr", AddressOf chiamataOcr.Esegui)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ProgrammaOcrElemento: " & ex.Message)
        End Try
    End Sub

    Private Function CompletaAnnuncio(elemento As IUIAutomationElement, testo As String) As String
        Try
            If elemento Is Nothing Then Return testo
            If _descrittore IsNot Nothing AndAlso _walkerControllo IsNot Nothing AndAlso AnnuncioDebole(testo) AndAlso LetturaRiquadroCollegato.InRiquadro(elemento, _automazione, _descrittore, _walkerControllo, _richiestaCache) Then
                Dim corpo As String = LetturaRiquadroCollegato.Anteprima(LetturaRiquadroCollegato.TestoDelRiquadro(elemento, _automazione, _descrittore, _walkerControllo, _richiestaCache, 500))
                If corpo <> "" Then Return "dettaglio, " & corpo
            End If
            Dim nome As String = _descrittore.Nome(elemento)
            Dim rotto As Boolean = _attributi IsNot Nothing AndAlso LetturaAttributi.ProviderSembraRotto(elemento, _descrittore)
            If nome <> "" AndAlso Not rotto Then Return testo
            If testo <> "" AndAlso nome <> "" Then Return testo
            Dim daMsaa As String = AccessibilitaMsaa.DescriviElemento(elemento)
            If daMsaa <> "" AndAlso daMsaa <> testo Then
                If testo = "" Then Return daMsaa
            End If
            Dim hwnd As IntPtr = IntPtr.Zero
            Try
                hwnd = CType(elemento.CurrentNativeWindowHandle, IntPtr)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("WGLettoreSchermo.CompletaAnnuncio", exAtteso)
                hwnd = IntPtr.Zero
            End Try
            If hwnd <> IntPtr.Zero AndAlso nome = "" Then
                Dim nativo As String = NativoLettore.LeggiTestoFinestra(hwnd, 240)
                If nativo <> "" AndAlso nativo.Length < 180 Then
                    If testo = "" Then Return nativo
                End If
            End If
            Dim immagine As Boolean = _descrittore.TipoControllo(elemento) = DescrittoreElementi.ControlloImmagine
            If (nome = "" AndAlso (rotto OrElse immagine)) AndAlso OcrAutomatico Then
                ProgrammaOcrElemento(elemento)
            End If
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.CompletaAnnuncio: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function AnnuncioDebole(testo As String) As Boolean
        Dim t As String = If(testo, "").Trim().ToLowerInvariant()
        If t.Length < 2 Then Return True
        If t = "documento" OrElse t = "gruppo" OrElse t = "riquadro" OrElse t = "pannello" OrElse t = "messaggio" Then Return True
        If t.EndsWith(", documento") OrElse t.EndsWith(", gruppo") OrElse t.EndsWith(", riquadro") OrElse t.EndsWith(", pannello") Then Return t.Length < 48
        Return False
    End Function

    Private Function StrutturaUtile(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero OrElse _navigazione Is Nothing OrElse Not NavigazioneWebAbilitata Then Return False
            Dim radice As IntPtr = NativoLettore.GetAncestor(finestra, NativoLettore.GA_ROOT)
            If radice = IntPtr.Zero Then radice = finestra
            Return _navigazione.EFinestraWeb(radice)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.StrutturaUtile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AggiornaEventiFuoco(elemento As IUIAutomationElement)
        Try
            If _registroEventi Is Nothing OrElse elemento Is Nothing OrElse _automazione Is Nothing Then Return
            Dim finestra As IntPtr = ManigliaFinestra(elemento)
            If finestra = IntPtr.Zero Then finestra = NativoLettore.GetForegroundWindow()
            Dim conStruttura As Boolean = StrutturaUtile(finestra)
            If finestra <> _ultimaFinestraEventi OrElse conStruttura <> _ultimaStrutturaEventi Then
                Dim radice As IUIAutomationElement = Nothing
                If finestra <> IntPtr.Zero Then radice = _automazione.ElementFromHandleBuildCache(finestra, _richiestaCache)
                If radice IsNot Nothing Then _registroEventi.AggiornaFinestra(radice, conStruttura)
                _ultimaFinestraEventi = finestra
                _ultimaStrutturaEventi = conStruttura
            End If
            _registroEventi.AggiornaFuoco(elemento)
            Dim web As Boolean = ElementoWeb(elemento)
            Dim conTesto As Boolean = _descrittore.ECampoScrittura(elemento) OrElse (Not web AndAlso _descrittore.TipoControllo(elemento) = DescrittoreElementi.ControlloDocumento)
            If Not conTesto AndAlso Not web Then conTesto = _descrittore.HaPatternTesto(elemento) OrElse _descrittore.EditabileConTesto(elemento)
            If conTesto Then
                _registroEventi.AggiornaTesto(elemento)
            Else
                _registroEventi.AggiornaTesto(Nothing)
            End If
            If _navigatore IsNot Nothing Then _navigatore.SegnaElemento(elemento)
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AggiornaEventiFuoco", ex)
        End Try
    End Sub

    Private Sub VerificaFinestra(finestra As IntPtr)
        Try
            If finestra = IntPtr.Zero Then Return
            If _supporto IsNot Nothing Then
                Dim avviso As String = _supporto.AvvisoPrivilegi(_supporto.FinestraPiuElevata(finestra))
                If avviso <> "" Then Parla(avviso, False)
            End If
            If _segnali Is Nothing Then Return
            If finestra = _browserControllato Then Return
            _browserControllato = finestra
            If FinestraChromium(finestra) OrElse EBrowserGecko(finestra) Then Return
            _segnali.SvegliaFinestra(finestra)
            If _navigazione Is Nothing OrElse _automazione Is Nothing Then Return
            Dim contesto As ContestoNavigazione = _navigazione.ContestoFinestra(finestra)
            If contesto Is Nothing OrElse contesto.Radice Is Nothing Then
                Dim messaggio As String = _segnali.MessaggioSeManca()
                If messaggio <> "" Then Parla(messaggio, False)
                Return
            End If
            Dim walker As IUIAutomationTreeWalker = _automazione.ControlViewWalker
            If walker Is Nothing Then Return
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(contesto.Radice, _richiestaCache)
            If figlio Is Nothing Then
                _segnali.SvegliaFinestra(finestra)
                If Not _segnali.HaProviderUia(finestra) Then Return
                Dim messaggio As String = _segnali.MessaggioSeManca()
                If messaggio <> "" Then Parla(messaggio, False)
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.VerificaFinestra: " & ex.Message)
        End Try
    End Sub

    Private Function DividiRighe(testo As String) As List(Of String)
        Dim righe As New List(Of String)()
        Try
            If String.IsNullOrEmpty(testo) Then Return righe
            righe.AddRange(testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ChrW(10)))
            While righe.Count > 0 AndAlso righe(righe.Count - 1).Trim().Length = 0
                righe.RemoveAt(righe.Count - 1)
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.DividiRighe", exAtteso)
        End Try
        Return righe
    End Function

    Private Sub AvviaLetturaContinua()
        Try
            If Volatile.Read(_continuaAttiva) = 1 Then
                AlternaPausa()
                Return
            End If
            FermaLetturaContinua(False)
            Dim prima As String = ""
            If InVirtuale() AndAlso _navigazione IsNot Nothing Then
                Volatile.Write(_continuaVirtuale, 1)
                prima = _navigazione.RigaAttualeTesto()
                If prima = "" Then prima = _navigazione.AnnuncioCorrente()
            ElseIf _attributi IsNot Nothing Then
                _intervalloContinua = _attributi.PreparaLetturaContinua(ElementoConFocus())
                If _intervalloContinua IsNot Nothing Then
                    prima = _attributi.TestoRiga(_intervalloContinua)
                    If prima = "" Then
                        While prima = "" AndAlso _attributi.AvanzaRiga(_intervalloContinua, True)
                            prima = _attributi.TestoRiga(_intervalloContinua)
                        End While
                    End If
                ElseIf _campi IsNot Nothing Then
                    Dim resto As String = _campi.LeggiFinoAllaFine(ElementoConFocus())
                    _righeContinua = DividiRighe(resto)
                    _indiceContinua = 0
                    If _righeContinua.Count > 0 Then prima = _righeContinua(0)
                End If
            End If
            If prima = "" Then
                Parla("vuoto", True)
                Return
            End If
            Volatile.Write(_continuaAttiva, 1)
            Volatile.Write(_continuaPausa, 0)
            Volatile.Write(_continuaSentita, 0)
            _continuaTick = Environment.TickCount
            If _codaVoce IsNot Nothing Then _codaVoce.InPausa = False
            ParlaDiretta(prima, True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AvviaLetturaContinua: " & ex.Message)
        End Try
    End Sub

    Private Sub AvanzaLetturaContinua()
        Try
            If Volatile.Read(_continuaAttiva) = 0 Then
                If _intervalloContinua IsNot Nothing Then
                    ComLettore.Rilascia(_intervalloContinua)
                    _intervalloContinua = Nothing
                End If
                Return
            End If
            If Volatile.Read(_continuaPausa) = 1 Then Return
            Dim parla As Boolean = _codaVoce IsNot Nothing AndAlso _codaVoce.StaParlando
            If parla Then
                Volatile.Write(_continuaSentita, 1)
                Return
            End If
            If Volatile.Read(_continuaSentita) = 0 AndAlso Environment.TickCount - _continuaTick < 700 Then Return
            Dim testo As String = RigaSuccessivaContinua()
            If testo = "" OrElse testo = "fine" OrElse testo = "inizio" OrElse testo = "fine del messaggio" Then
                FermaLetturaContinua(False)
                Return
            End If
            If testo = "pagina in lettura" Then Return
            Volatile.Write(_continuaSentita, 0)
            _continuaTick = Environment.TickCount
            Volatile.Write(_proteggiCursoreFino, Environment.TickCount + 900)
            If Volatile.Read(_continuaVirtuale) = 1 Then SegnaCursoreDaTasti()
            ParlaDiretta(testo, False)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AvanzaLetturaContinua: " & ex.Message)
        End Try
    End Sub

    Private Function RigaSuccessivaContinua() As String
        Try
            If Volatile.Read(_continuaVirtuale) = 1 AndAlso _navigazione IsNot Nothing Then
                Return _navigazione.Muovi(&H28, False, False, False)
            End If
            If _righeContinua IsNot Nothing Then
                While _indiceContinua + 1 < _righeContinua.Count
                    _indiceContinua += 1
                    If Not String.IsNullOrWhiteSpace(_righeContinua(_indiceContinua)) Then Return _righeContinua(_indiceContinua)
                End While
                Return ""
            End If
            If _intervalloContinua Is Nothing OrElse _attributi Is Nothing Then Return ""
            For salto As Integer = 1 To 200
                If Not _attributi.AvanzaRiga(_intervalloContinua, True) Then Return ""
                Dim riga As String = _attributi.TestoRiga(_intervalloContinua)
                If Not String.IsNullOrWhiteSpace(riga) Then Return riga
            Next
            Return ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.RigaSuccessivaContinua", exAtteso)
            Return ""
        End Try
    End Function

    Friend Sub AlternaPausaMaiuscolo()
        Try
            If Sospeso OrElse SilenzioEsterno() Then Return
            If FinestraTerminale(NativoLettore.GetForegroundWindow()) Then
                If _codaVoce IsNot Nothing Then _codaVoce.SaltaFrase()
                Return
            End If
            If Volatile.Read(_continuaAttiva) = 1 Then
                AlternaPausa()
                Return
            End If
            If _codaVoce IsNot Nothing Then _codaVoce.AlternaPausaUtente()
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AlternaPausaMaiuscolo: " & ex.Message)
        End Try
    End Sub

    Private Sub AlternaPausa()
        Try
            Dim pausa As Boolean = Volatile.Read(_continuaPausa) = 0
            Volatile.Write(_continuaPausa, If(pausa, 1, 0))
            If _codaVoce IsNot Nothing Then _codaVoce.InPausa = pausa
            If Not pausa AndAlso Volatile.Read(_continuaAttiva) = 0 Then
                If _codaVoce IsNot Nothing Then _codaVoce.InPausa = False
            End If
            Parla(If(pausa, "pausa", "ripresa"), True)
            If Not pausa Then _continuaTick = Environment.TickCount
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AlternaPausa: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaLetturaContinua(annuncia As Boolean)
        Try
            Volatile.Write(_continuaAttiva, 0)
            Volatile.Write(_continuaPausa, 0)
            Volatile.Write(_continuaVirtuale, 0)
            _righeContinua = Nothing
            _indiceContinua = 0
            If _codaVoce IsNot Nothing Then _codaVoce.InPausa = False
            If annuncia Then Parla("lettura interrotta", True)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.FermaLetturaContinua", exAtteso)
        End Try
    End Sub

    Private Sub ParlaDiretta(testo As String, interrompi As Boolean)
        If String.IsNullOrWhiteSpace(testo) Then Return
        If AnnuncioDaSopprimere(testo) Then Return
        If SilenzioEsterno() Then Return
        If _codaVoce Is Nothing Then
            Parla(testo, interrompi)
            Return
        End If
        If interrompi AndAlso Not InterruzioneConsentita() Then interrompi = False
        _codaVoce.Accoda(testo, If(interrompi, PrioritaAnnuncio.Interruzione, PrioritaAnnuncio.Normale), interrompi, True)
        If _braille IsNot Nothing AndAlso BrailleAttivo Then _braille.Mostra(testo, 0)
    End Sub

    Private Sub AlternaRevisionePiatta()
        Try
            If Volatile.Read(_revisionePiatta) = 1 Then
                Volatile.Write(_revisionePiatta, 0)
                _righePiatte = Nothing
                Parla("Revisione ad albero", True)
                Return
            End If
            _righePiatte = RighePiatte()
            _indicePiatto = 0
            Volatile.Write(_revisionePiatta, 1)
            If _righePiatte Is Nothing OrElse _righePiatte.Count = 0 Then
                Parla("Revisione piatta, nessun testo", True)
                Return
            End If
            Parla("Revisione piatta, " & _righePiatte(0), True)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AlternaRevisionePiatta: " & ex.Message)
        End Try
    End Sub

    Private Function RevisionePiattaAttiva() As Boolean
        Return Volatile.Read(_revisionePiatta) = 1 AndAlso _righePiatte IsNot Nothing AndAlso _righePiatte.Count > 0
    End Function

    Private Function SpostaRevisionePiatta(comando As ComandoLettore) As String
        Try
            If comando <> ComandoLettore.Precedente AndAlso comando <> ComandoLettore.Successivo AndAlso comando <> ComandoLettore.Contenuto AndAlso comando <> ComandoLettore.Contenitore Then Return ""
            Dim delta As Integer = If(comando = ComandoLettore.Successivo OrElse comando = ComandoLettore.Contenuto, 1, -1)
            Dim nuovo As Integer = _indicePiatto + delta
            If nuovo < 0 Then
                If _codaVoce IsNot Nothing Then _codaVoce.SuonaLimite()
                Return "inizio"
            End If
            If nuovo >= _righePiatte.Count Then
                If _codaVoce IsNot Nothing Then _codaVoce.SuonaLimite()
                Return "fine"
            End If
            _indicePiatto = nuovo
            Return _righePiatte(nuovo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.SpostaRevisionePiatta: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function RighePiatte() As List(Of String)
        Dim righe As New List(Of String)()
        Dim intervallo As IUIAutomationTextRange = Nothing
        Try
            If _attributi Is Nothing Then Return righe
            intervallo = _attributi.PreparaLetturaContinua(ElementoConFocus())
            If intervallo Is Nothing Then Return righe
            For passo As Integer = 1 To 160
                Dim testo As String = _attributi.TestoRiga(intervallo)
                If testo <> "" Then righe.Add(testo)
                If Not _attributi.AvanzaRiga(intervallo, False) Then Exit For
            Next
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.RighePiatte: " & ex.Message)
        Finally
            ComLettore.Rilascia(intervallo)
        End Try
        Return righe
    End Function

    Private Sub LeggiSottoMouse()
        Try
            If _navigatore Is Nothing OrElse Volatile.Read(_continuaAttiva) = 1 Then Return
            Dim spostato As Boolean = False
            Dim elemento As IUIAutomationElement = _navigatore.ElementoSottoPuntatore(spostato)
            If elemento Is Nothing Then Return
            If EProcessoWinGabriel(CUInt(_descrittore.IdProcesso(elemento))) Then Return
            _revisione = elemento
            Dim testo As String = _descrittore.Descrivi(elemento)
            If testo = "" Then testo = CompletaAnnuncio(elemento, testo)
            If testo <> "" Then ParlaPriorita(testo, 0)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.LeggiSottoMouse: " & ex.Message)
        End Try
    End Sub

    Private Sub SuonaModalita(focus As Boolean)
        Try
            If _codaVoce IsNot Nothing Then _codaVoce.SuonaCampo(focus)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SuonaModalita", exAtteso)
        End Try
    End Sub

    Private Sub SuonaConfine(frase As String)
        Try
            If _codaVoce Is Nothing OrElse frase = "" Then Return
            If frase = "fine" OrElse frase = "inizio" OrElse frase = "fine del messaggio" OrElse frase = "non ci sono altri" OrElse frase.StartsWith("non ci sono", StringComparison.CurrentCultureIgnoreCase) Then
                _codaVoce.SuonaLimite()
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.SuonaConfine", exAtteso)
        End Try
    End Sub

End Class