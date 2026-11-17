Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols

<Flags>
Public Enum ModificatoriInput
    Nessuno = 0
    Alt = 1
    Control = 2
    Meta = 4
    Shift = 8
End Enum

Public Enum PulsanteMouse
    Sinistro = 0
    Centrale = 1
    Destro = 2
End Enum

Public Class WGCdpInterazione
    Private ReadOnly log As New Logger()
    Private ReadOnly _pagina As WGPaginaWeb
    Private _devTools As WGDevTools
    Private ReadOnly _lockTrascinamento As New Object()
    Private ReadOnly _lockInput As New Object()
    Private _attesaTrascinamento As TaskCompletionSource(Of JObject) = Nothing
    Private _codaInput As Task = Task.CompletedTask
    Private _istanteClicSfondo As DateTime = DateTime.MinValue
    Private _istanteClicNuovaScheda As DateTime = DateTime.MinValue

    Public Const VK_BACK As Integer = &H8
    Public Const VK_TAB As Integer = &H9
    Public Const VK_RETURN As Integer = &HD
    Public Const VK_ESCAPE As Integer = &H1B
    Public Const VK_SPACE As Integer = &H20
    Public Const VK_PRIOR As Integer = &H21
    Public Const VK_NEXT As Integer = &H22
    Public Const VK_END As Integer = &H23
    Public Const VK_HOME As Integer = &H24
    Public Const VK_LEFT As Integer = &H25
    Public Const VK_UP As Integer = &H26
    Public Const VK_RIGHT As Integer = &H27
    Public Const VK_DOWN As Integer = &H28
    Public Const VK_DELETE As Integer = &H2E

    Private Const AttesaMassimaInputMs As Integer = 1500

    Public Property SessioneCampo As String = Nothing
    Public Property LarghezzaVista As Integer = WGMotoreBrowser.LarghezzaVista
    Public Property AltezzaVista As Integer = WGMotoreBrowser.AltezzaVista

    Public Sub New(pagina As WGPaginaWeb)
        Try
            _pagina = pagina
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.New: " & ex.ToString())
        End Try
    End Sub

    Public Property DevTools As WGDevTools
        Get
            Return _devTools
        End Get
        Set(value As WGDevTools)
            Try
                If _devTools IsNot Nothing Then RemoveHandler _devTools.EventoRicevuto, AddressOf EventoDevTools
                _devTools = value
                If _devTools IsNot Nothing Then AddHandler _devTools.EventoRicevuto, AddressOf EventoDevTools
            Catch ex As Exception
                log.ScriviLog("WGCdpInterazione.DevTools: " & ex.Message)
            End Try
        End Set
    End Property

    Private Sub EventoDevTools(metodo As String, parametri As JObject, sessione As String)
        Try
            If metodo <> "Input.dragIntercepted" Then Return
            Dim attesa As TaskCompletionSource(Of JObject) = Nothing
            SyncLock _lockTrascinamento
                attesa = _attesaTrascinamento
            End SyncLock
            If attesa IsNot Nothing AndAlso parametri IsNot Nothing Then attesa.TrySetResult(TryCast(parametri("data"), JObject))
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.EventoDevTools: " & ex.Message)
        End Try
    End Sub

    Private Function DevToolsPronto() As Boolean
        Return _devTools IsNot Nothing AndAlso _devTools.ECollegato
    End Function

    Private Function PaginaPronta() As Boolean
        Return _pagina IsNot Nothing AndAlso _pagina.Pronta AndAlso _devTools IsNot Nothing
    End Function

    Private Sub AccodaInput(metodo As String, parametri As JObject)
        Try
            If Not PaginaPronta() Then Return
            SyncLock _lockInput
                Dim precedente As Task = _codaInput
                _codaInput = InviaDopoAsync(precedente, metodo, parametri)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.AccodaInput " & metodo & ": " & ex.Message)
        End Try
    End Sub

    Private Async Function InviaDopoAsync(precedente As Task, metodo As String, parametri As JObject) As Task
        Try
            If precedente IsNot Nothing AndAlso Not precedente.IsCompleted Then Await Task.WhenAny(precedente, Task.Delay(AttesaMassimaInputMs))
            Dim strumenti As WGDevTools = _devTools
            If strumenti Is Nothing Then Return
            Await strumenti.InviaAsync(metodo, parametri, "", AttesaMassimaInputMs)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.InviaDopoAsync " & metodo & ": " & ex.Message)
        End Try
    End Function

    Public Async Function AttendiInputAsync(Optional millisecondi As Integer = AttesaMassimaInputMs) As Task
        Try
            Dim ultima As Task
            SyncLock _lockInput
                ultima = _codaInput
            End SyncLock
            If ultima Is Nothing OrElse ultima.IsCompleted Then Return
            Await Task.WhenAny(ultima, Task.Delay(Math.Max(50, millisecondi)))
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.AttendiInputAsync: " & ex.Message)
        End Try
    End Function

    Private Shared Function EventoMouse(tipo As String, x As Double, y As Double, pulsante As String, pulsanti As Integer, conteggio As Integer, modificatori As ModificatoriInput) As JObject
        Dim parametri As New JObject()
        parametri("type") = tipo
        parametri("x") = x
        parametri("y") = y
        parametri("button") = pulsante
        parametri("buttons") = pulsanti
        parametri("clickCount") = conteggio
        parametri("modifiers") = CInt(modificatori)
        Return parametri
    End Function

    Private Shared Function NomePulsante(pulsante As PulsanteMouse) As String
        Select Case pulsante
            Case PulsanteMouse.Destro : Return "right"
            Case PulsanteMouse.Centrale : Return "middle"
        End Select
        Return "left"
    End Function

    Private Shared Function BitPulsante(pulsante As PulsanteMouse) As Integer
        Select Case pulsante
            Case PulsanteMouse.Destro : Return 2
            Case PulsanteMouse.Centrale : Return 4
        End Select
        Return 1
    End Function

    Public Function IntenzioneNuovaFinestra() As String
        Try
            Dim ora As DateTime = DateTime.Now
            If (ora - _istanteClicSfondo).TotalMilliseconds <= 2500 Then
                _istanteClicSfondo = DateTime.MinValue
                Return "sfondo"
            End If
            If (ora - _istanteClicNuovaScheda).TotalMilliseconds <= 2500 Then
                _istanteClicNuovaScheda = DateTime.MinValue
                Return "primo"
            End If
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.IntenzioneNuovaFinestra: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Sub RicordaIntenzione(pulsante As PulsanteMouse, modificatori As ModificatoriInput)
        Try
            If pulsante = PulsanteMouse.Centrale OrElse (modificatori And ModificatoriInput.Control) = ModificatoriInput.Control Then
                _istanteClicSfondo = DateTime.Now
            ElseIf (modificatori And ModificatoriInput.Shift) = ModificatoriInput.Shift Then
                _istanteClicNuovaScheda = DateTime.Now
            End If
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.RicordaIntenzione: " & ex.Message)
        End Try
    End Sub

    Public Sub InviaClickCDP(x As Long, y As Long)
        InviaClic(CInt(x), CInt(y), PulsanteMouse.Sinistro, 1, ModificatoriInput.Nessuno)
    End Sub

    Public Sub SpostaMouse(x As Integer, y As Integer, Optional modificatori As ModificatoriInput = ModificatoriInput.Nessuno)
        Try
            AccodaInput("Input.dispatchMouseEvent", EventoMouse("mouseMoved", x, y, "none", 0, 0, modificatori))
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.SpostaMouse: " & ex.Message)
        End Try
    End Sub

    Public Sub InviaClic(x As Integer, y As Integer, pulsante As PulsanteMouse, conteggio As Integer, modificatori As ModificatoriInput)
        Try
            RicordaIntenzione(pulsante, modificatori)
            Dim nome As String = NomePulsante(pulsante)
            Dim bit As Integer = BitPulsante(pulsante)
            AccodaInput("Input.dispatchMouseEvent", EventoMouse("mouseMoved", x, y, "none", 0, 0, modificatori))
            For i As Integer = 1 To Math.Max(1, conteggio)
                AccodaInput("Input.dispatchMouseEvent", EventoMouse("mousePressed", x, y, nome, bit, i, modificatori))
                AccodaInput("Input.dispatchMouseEvent", EventoMouse("mouseReleased", x, y, nome, 0, i, modificatori))
            Next
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.InviaClic: " & ex.Message)
        End Try
    End Sub

    Public Sub Rotellina(x As Integer, y As Integer, deltaY As Integer)
        Try
            AccodaInput("Input.dispatchMouseEvent", EventoMouse("mouseMoved", x, y, "none", 0, 0, ModificatoriInput.Nessuno))
            Dim parametri As JObject = EventoMouse("mouseWheel", x, y, "none", 0, 0, ModificatoriInput.Nessuno)
            parametri("deltaX") = 0
            parametri("deltaY") = -deltaY
            AccodaInput("Input.dispatchMouseEvent", parametri)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.Rotellina: " & ex.Message)
        End Try
    End Sub

    Public Sub InviaInvioCDP()
        InviaTasto(VK_RETURN, 13, ModificatoriInput.Nessuno)
    End Sub

    Private Shared Function EventoTastiera(tipo As String, codiceVirtuale As Integer, testo As String, modificatori As ModificatoriInput, sistema As Boolean) As JObject
        Dim parametri As New JObject()
        parametri("type") = tipo
        parametri("modifiers") = CInt(modificatori)
        If codiceVirtuale > 0 Then
            parametri("windowsVirtualKeyCode") = codiceVirtuale
            parametri("nativeVirtualKeyCode") = codiceVirtuale
            Dim codice As String = WGCaratteri.CodiceDom(codiceVirtuale)
            If codice <> "" Then parametri("code") = codice
            Dim posizione As Integer = WGCaratteri.PosizioneTasto(codiceVirtuale)
            If posizione > 0 Then parametri("location") = posizione
            If posizione = 3 Then parametri("isKeypad") = True
        End If
        Dim shift As Boolean = (modificatori And ModificatoriInput.Shift) = ModificatoriInput.Shift
        parametri("key") = WGCaratteri.ChiaveDom(codiceVirtuale, testo, shift)
        If Not String.IsNullOrEmpty(testo) AndAlso tipo <> "keyUp" Then
            parametri("text") = testo
            parametri("unmodifiedText") = testo
        End If
        If sistema Then parametri("isSystemKey") = True
        Return parametri
    End Function

    Public Sub InviaTasto(codiceVirtuale As Integer, codiceCarattere As Integer, modificatori As ModificatoriInput)
        Try
            Dim sistema As Boolean = (modificatori And ModificatoriInput.Alt) = ModificatoriInput.Alt AndAlso (modificatori And ModificatoriInput.Control) = ModificatoriInput.Nessuno
            Dim testo As String = ""
            If codiceCarattere = 13 Then
                testo = vbCr
            ElseIf codiceCarattere >= 32 AndAlso codiceCarattere <> 127 Then
                testo = ChrW(codiceCarattere).ToString()
            End If
            Dim tipoGiu As String = If(testo <> "", "keyDown", "rawKeyDown")
            Dim giu As JObject = EventoTastiera(tipoGiu, codiceVirtuale, testo, modificatori, sistema)
            If testo = "" AndAlso codiceVirtuale >= &H41 AndAlso codiceVirtuale <= &H5A Then
                Dim lettera As String = ChrW(codiceVirtuale).ToString().ToLowerInvariant()
                giu("unmodifiedText") = lettera
            End If
            AccodaInput("Input.dispatchKeyEvent", giu)
            AccodaInput("Input.dispatchKeyEvent", EventoTastiera("keyUp", codiceVirtuale, testo, modificatori, sistema))
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.InviaTasto: " & ex.Message)
        End Try
    End Sub

    Public Sub InviaCarattere(carattere As String, codiceVirtuale As Integer, conAltGr As Boolean, conShift As Boolean)
        Try
            If String.IsNullOrEmpty(carattere) Then Return
            Dim modificatori As ModificatoriInput = If(conShift, ModificatoriInput.Shift, ModificatoriInput.Nessuno)
            If conAltGr OrElse codiceVirtuale <= 0 OrElse carattere.Length > 1 Then
                For Each c As Char In carattere
                    Dim soloCarattere As New JObject()
                    soloCarattere("type") = "char"
                    soloCarattere("text") = c.ToString()
                    soloCarattere("unmodifiedText") = c.ToString()
                    soloCarattere("key") = c.ToString()
                    soloCarattere("modifiers") = CInt(modificatori)
                    AccodaInput("Input.dispatchKeyEvent", soloCarattere)
                Next
                Return
            End If
            AccodaInput("Input.dispatchKeyEvent", EventoTastiera("keyDown", codiceVirtuale, carattere, modificatori, False))
            AccodaInput("Input.dispatchKeyEvent", EventoTastiera("keyUp", codiceVirtuale, carattere, modificatori, False))
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.InviaCarattere: " & ex.Message)
        End Try
    End Sub

    Public Async Function InserisciTestoAsync(testo As String) As Task(Of Boolean)
        Try
            If String.IsNullOrEmpty(testo) OrElse Not PaginaPronta() Then Return False
            Await AttendiInputAsync()
            If Await _devTools.InserisciTestoAsync("", testo) Then Return True
            If Not String.IsNullOrEmpty(SessioneCampo) Then Return Await _devTools.InserisciTestoAsync(SessioneCampo, testo)
            Return False
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.InserisciTestoAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub ImpostaFocusMotore(conFocus As Boolean)
        Try
            If _devTools Is Nothing Then Return
            _devTools.ImpostaFocus(conFocus)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.ImpostaFocusMotore: " & ex.Message)
        End Try
    End Sub

    Public Async Function ValutaAsync(script As String, Optional millisecondi As Integer = 4000) As Task(Of String)
        Try
            If _pagina Is Nothing OrElse Not _pagina.Pronta Then Return Nothing
            Return Await _pagina.ValutaAsync(script, millisecondi)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.ValutaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function ValutaOggettoAsync(script As String, Optional millisecondi As Integer = 4000) As Task(Of JObject)
        Try
            Dim testo As String = Await ValutaAsync(script, millisecondi)
            If String.IsNullOrEmpty(testo) OrElse testo = "null" Then Return Nothing
            Return TryCast(WGPaginaWeb.AnalizzaJson(testo), JObject)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.ValutaOggettoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Sub EseguiNellaPagina(script As String)
        Try
            If _pagina Is Nothing OrElse Not _pagina.Pronta Then Return
            _pagina.EseguiScript(script)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.EseguiNellaPagina: " & ex.Message)
        End Try
    End Sub

    Public Async Function AttivaElementoPerId(idElemento As Integer) As Task(Of Boolean)
        Try
            If idElemento <= 0 Then Return False
            Dim punto As JObject = Await ValutaOggettoAsync("window.__wgAgente ? window.__wgAgente.punto(" & idElemento & ") : null")
            If punto Is Nothing Then Return False
            If punto.Value(Of Integer?)("manca").GetValueOrDefault() = 1 Then Return False
            If punto.Value(Of Integer?)("ok").GetValueOrDefault() = 1 Then
                InviaClickCDP(punto.Value(Of Long)("x"), punto.Value(Of Long)("y"))
                Return True
            End If
            Dim esito As String = Await ValutaAsync("window.__wgAgente ? window.__wgAgente.clic(" & idElemento & ") : false")
            Return String.Equals(esito, "True", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.AttivaElementoPerId: " & ex.Message)
            Return False
        End Try
    End Function

    Public Async Function FocalizzaElementoPerId(idElemento As Integer) As Task(Of JObject)
        Try
            If idElemento <= 0 Then Return Nothing
            ImpostaFocusMotore(True)
            Return Await ValutaOggettoAsync("window.__wgAgente ? window.__wgAgente.focalizza(" & idElemento & ") : null")
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.FocalizzaElementoPerId: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Const FunzionePunto As String =
        "function(){var el=this;try{el.scrollIntoView({block:'center',inline:'nearest',behavior:'instant'});}catch(e){}" &
        "var r=el.getBoundingClientRect();var x=r.left+r.width/2,y=r.top+r.height/2;var d=el.ownerDocument;var h=null;" &
        "try{h=d.elementFromPoint(x,y);}catch(e){}var ok=false,n=h,g=0;" &
        "while(n&&g<60){g++;if(n===el){ok=true;break;}n=n.parentElement||(n.parentNode&&n.parentNode.host)||null;}" &
        "if(!ok&&h&&el.contains&&el.contains(h))ok=true;if(!ok&&h&&h.localName==='label'&&h.control===el)ok=true;" &
        "var v=d.defaultView;var cima=true;while(v&&v!==v.top){var f=null;try{f=v.frameElement;}catch(e){f=null;}" &
        "if(!f){cima=false;break;}var rf=f.getBoundingClientRect();x+=rf.left+(f.clientLeft||0);y+=rf.top+(f.clientTop||0);v=v.parent;}" &
        "var vw=cima?v.innerWidth:0,vh=cima?v.innerHeight:0;" &
        "return {ok:(ok&&cima&&r.width>=1&&r.height>=1&&x>=0&&y>=0&&x<vw&&y<vh)?1:0,x:Math.round(x),y:Math.round(y)};}"

    Private Const FunzioneClic As String =
        "function(){try{this.focus({preventScroll:true});}catch(e){}" &
        "if(typeof this.click==='function'){this.click();}else{this.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,view:this.ownerDocument.defaultView}));}return true;}"

    Private Const FunzioneFuoco As String =
        "function(){try{this.scrollIntoView({block:'center',inline:'nearest',behavior:'instant'});}catch(e){}try{this.focus();}catch(e){}" &
        "var a=(this.ownerDocument.defaultView||window).__wgAgente;return a?a.statoCampo():null;}"

    Private Const FunzionePreparaTab As String =
        "function(){var a=(this.ownerDocument.defaultView||window).__wgAgente;return a?a.preparaTabElemento(this):false;}"

    Private Const FunzioneScorri As String =
        "function(){try{var r=this.getBoundingClientRect();var v=this.ownerDocument.defaultView;" &
        "if(r.bottom<0||r.top>v.innerHeight||r.right<0||r.left>v.innerWidth)this.scrollIntoView({block:'center',inline:'nearest',behavior:'instant'});}catch(e){}return true;}"

    Private Const FunzioneIndirizzo As String =
        "function(){var e=this,g=0;while(e&&g<8){g++;if(e.href&&typeof e.href==='string')return e.href;" &
        "if(e.currentSrc)return e.currentSrc;if(e.src&&typeof e.src==='string')return e.src;e=e.parentElement;}return '';}"

    Private Const FunzioneSorgenteMedia As String =
        "function(){var e=this;var m=null;if(e.localName==='video'||e.localName==='audio'){m=e;}else{try{m=e.querySelector('video,audio');}catch(x){m=null;}}" &
        "if(!m)return '';return m.currentSrc||m.src||'';}"

    Private Const EspressioneSuggerimento As String =
        "(function(x,y){try{var d=document,e=null,g=0;e=d.elementFromPoint(x,y);" &
        "while(e&&g<20&&(e.localName==='iframe'||e.localName==='frame')){var r=e.getBoundingClientRect();var cd=null;try{cd=e.contentDocument;}catch(z){cd=null;}if(!cd)break;x-=r.left;y-=r.top;d=cd;e=d.elementFromPoint(x,y);g++;}" &
        "while(e&&e.shadowRoot&&g<40){var s=e.shadowRoot.elementFromPoint(x,y);if(!s||s===e)break;e=s;g++;}" &
        "var n=e,k=0;while(n&&k<30){k++;if(n.getAttribute){var t=n.getAttribute('title');if(t&&t.trim())return t.trim();" &
        "if(n.localName==='svg'||n.ownerSVGElement){var st=n.querySelector?n.querySelector(':scope>title'):null;if(st&&st.textContent&&st.textContent.trim())return st.textContent.trim();}}" &
        "n=n.parentElement||(n.parentNode&&n.parentNode.host)||null;}return '';}catch(ex){return '';}})"

    Private Function AlberoDisponibile(riga As RigaVirtuale) As Boolean
        Return riga IsNot Nothing AndAlso riga.DaAlbero AndAlso DevToolsPronto()
    End Function

    ' Lo scorrimento parte solo quando ci si ferma su una riga: saltando in fretta tra intestazioni o link
    ' non si manda un comando alla pagina per ogni riga attraversata (su YouTube ogni scorrimento carica altri contenuti).
    Private _versioneMostra As Integer = 0

    Public Sub MostraRiga(riga As RigaVirtuale)
        Try
            If riga Is Nothing Then Return
            _versioneMostra += 1
            Dim compito As Task = MostraRigaDopoPausaAsync(riga, _versioneMostra)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.MostraRiga: " & ex.Message)
        End Try
    End Sub

    Private Async Function MostraRigaDopoPausaAsync(riga As RigaVirtuale, versione As Integer) As Task
        Try
            Await Task.Delay(120)
            If versione <> _versioneMostra Then Return
            If AlberoDisponibile(riga) Then
                Dim compito As Task(Of JToken) = _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdElemento, FunzioneScorri, 2000)
                Return
            End If
            MostraElementoPerId(riga.IdElemento)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.MostraRigaDopoPausaAsync: " & ex.Message)
        End Try
    End Function

    Public Async Function AttivaRigaAsync(riga As RigaVirtuale) As Task(Of Boolean)
        Try
            If riga Is Nothing Then Return False
            If Not AlberoDisponibile(riga) Then Return Await AttivaElementoPerId(riga.IdDaAttivare)
            Dim id As Integer = riga.IdDaAttivare
            If String.IsNullOrEmpty(riga.Sessione) Then
                Dim punto As JToken = Await _devTools.ChiamaSuNodoAsync("", id, FunzionePunto)
                If punto Is Nothing Then Return False
                If punto.Type = JTokenType.Object AndAlso punto.Value(Of Integer?)("ok").GetValueOrDefault() = 1 Then
                    InviaClickCDP(punto.Value(Of Long)("x"), punto.Value(Of Long)("y"))
                    Return True
                End If
            Else
                Dim coordinate As PointF? = Await CoordinateNodoAsync(riga.Sessione, id, True)
                If coordinate.HasValue AndAlso Await PuntoLiberoAsync(riga.Sessione, id) Then
                    InviaClickCDP(CLng(coordinate.Value.X), CLng(coordinate.Value.Y))
                    Return True
                End If
            End If
            Dim esito As JToken = Await _devTools.ChiamaSuNodoAsync(riga.Sessione, id, FunzioneClic)
            Return esito IsNot Nothing AndAlso esito.Type = JTokenType.Boolean AndAlso esito.Value(Of Boolean)()
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.AttivaRigaAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Function PuntoLiberoAsync(sessione As String, idNodo As Integer) As Task(Of Boolean)
        Try
            Dim funzione As String = "function(){var el=this;var r=el.getBoundingClientRect();var x=r.left+r.width/2,y=r.top+r.height/2;" &
                "var h=null;try{h=el.ownerDocument.elementFromPoint(x,y);}catch(e){}if(!h)return false;" &
                "var n=h,g=0;while(n&&g<60){g++;if(n===el)return true;n=n.parentElement||(n.parentNode&&n.parentNode.host)||null;}" &
                "return !!(el.contains&&el.contains(h))||(h.localName==='label'&&h.control===el);}"
            Dim esito As JToken = Await _devTools.ChiamaSuNodoAsync(sessione, idNodo, funzione, 2000)
            Return esito IsNot Nothing AndAlso esito.Type = JTokenType.Boolean AndAlso esito.Value(Of Boolean)()
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.PuntoLiberoAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Public Async Function FocalizzaRigaAsync(riga As RigaVirtuale) As Task(Of JObject)
        Try
            If riga Is Nothing Then Return Nothing
            ImpostaFocusMotore(True)
            If Not AlberoDisponibile(riga) Then
                SessioneCampo = Nothing
                Return Await FocalizzaElementoPerId(riga.IdElemento)
            End If
            Dim risultato As JToken = Await _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdElemento, FunzioneFuoco)
            SessioneCampo = riga.Sessione
            Dim stato As JObject = AnalizzaStato(risultato)
            If stato Is Nothing Then stato = Await StatoCampoAsync()
            Return stato
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.FocalizzaRigaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function AnalizzaStato(valore As JToken) As JObject
        Try
            If valore Is Nothing OrElse valore.Type <> JTokenType.String Then Return Nothing
            Dim testo As String = valore.Value(Of String)()
            If String.IsNullOrEmpty(testo) Then Return Nothing
            Dim oggetto As JObject = TryCast(WGPaginaWeb.AnalizzaJson(testo), JObject)
            If oggetto Is Nothing OrElse oggetto.Value(Of Integer?)("i").GetValueOrDefault() = 0 Then Return Nothing
            Return oggetto
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.AnalizzaStato: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function StatoCampoAsync() As Task(Of JObject)
        Try
            If DevToolsPronto() Then
                Dim sessione As String = SessioneCampo
                If sessione Is Nothing Then
                    Dim attivo As Tuple(Of String, Integer) = Await _devTools.ElementoAttivoAsync()
                    sessione = attivo.Item1
                End If
                Dim valore As JToken = Await _devTools.ValutaAsync(sessione, "window.__wgAgente ? window.__wgAgente.statoCampo() : null")
                Dim stato As JObject = AnalizzaStato(valore)
                If stato IsNot Nothing Then Return stato
            End If
            Return Await ValutaOggettoAsync("window.__wgAgente ? window.__wgAgente.statoCampo() : null", 3000)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.StatoCampoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function DescriviAttivoAsync() As Task(Of JObject)
        Try
            If DevToolsPronto() Then
                Dim sessione As String = (Await _devTools.ElementoAttivoAsync()).Item1
                Dim valore As JToken = Await _devTools.ValutaAsync(sessione, "window.__wgAgente ? window.__wgAgente.descriviAttivo() : null")
                Dim oggetto As JObject = AnalizzaStato(valore)
                If oggetto IsNot Nothing Then Return oggetto
            End If
            Return Await ValutaOggettoAsync("window.__wgAgente ? window.__wgAgente.descriviAttivo() : null", 3000)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.DescriviAttivoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function StatiRigaAsync(riga As RigaVirtuale) As Task(Of String)
        Try
            If riga Is Nothing Then Return Nothing
            If Not AlberoDisponibile(riga) Then
                Dim descrizione As JObject = Await ValutaOggettoAsync("window.__wgAgente ? window.__wgAgente.descrivi(" & riga.IdDaAttivare & ") : null", 3000)
                If descrizione Is Nothing OrElse descrizione.Value(Of Integer?)("i").GetValueOrDefault() = 0 Then Return Nothing
                Return If(descrizione.Value(Of String)("s"), "")
            End If
            Dim parametri As New JObject()
            parametri("backendNodeId") = riga.IdDaAttivare
            parametri("fetchRelatives") = False
            Dim risposta As JObject = Await _devTools.InviaAsync("Accessibility.getPartialAXTree", parametri, riga.Sessione, 3000)
            If risposta Is Nothing OrElse risposta("nodes") Is Nothing Then Return Nothing
            For Each nodo As JObject In risposta("nodes")
                If WGAlberoAccessibilita.IdDom(nodo) = riga.IdDaAttivare Then
                    Return WGAlberoAccessibilita.StatiDi(nodo, WGAlberoAccessibilita.RuoloNormale(WGAlberoAccessibilita.RuoloDi(nodo)))
                End If
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.StatiRigaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function PreparaTabAsync(riga As RigaVirtuale) As Task
        Try
            If riga Is Nothing Then Return
            If AlberoDisponibile(riga) Then
                Await _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdDaAttivare, FunzionePreparaTab, 2000)
            Else
                Await ValutaAsync("window.__wgAgente ? window.__wgAgente.preparaTab(" & riga.IdDaAttivare & ") : false", 2000)
            End If
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.PreparaTabAsync: " & ex.Message)
        End Try
    End Function

    Public Sub MostraElementoPerId(idElemento As Integer)
        Try
            If idElemento <= 0 OrElse _pagina Is Nothing OrElse Not _pagina.Pronta Then Return
            _pagina.EseguiScript("window.__wgAgente && window.__wgAgente.mostra(" & idElemento & ");")
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.MostraElementoPerId: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CentroQuad(quad As JArray) As PointF?
        Try
            If quad Is Nothing OrElse quad.Count < 8 Then Return Nothing
            Dim xs As Double() = {CDbl(quad(0)), CDbl(quad(2)), CDbl(quad(4)), CDbl(quad(6))}
            Dim ys As Double() = {CDbl(quad(1)), CDbl(quad(3)), CDbl(quad(5)), CDbl(quad(7))}
            If xs.Max() - xs.Min() < 1 OrElse ys.Max() - ys.Min() < 1 Then Return Nothing
            Return New PointF(CSng(xs.Average()), CSng(ys.Average()))
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGCdpInterazione.CentroQuad: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function RettangoloQuad(quad As JArray) As RectangleF?
        Try
            If quad Is Nothing OrElse quad.Count < 8 Then Return Nothing
            Dim xs As Double() = {CDbl(quad(0)), CDbl(quad(2)), CDbl(quad(4)), CDbl(quad(6))}
            Dim ys As Double() = {CDbl(quad(1)), CDbl(quad(3)), CDbl(quad(5)), CDbl(quad(7))}
            Dim larghezza As Double = xs.Max() - xs.Min()
            Dim altezza As Double = ys.Max() - ys.Min()
            If larghezza < 1 OrElse altezza < 1 Then Return Nothing
            Return New RectangleF(CSng(xs.Min()), CSng(ys.Min()), CSng(larghezza), CSng(altezza))
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGCdpInterazione.RettangoloQuad: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Async Function RettangoloLocaleAsync(sessione As String, idNodo As Integer, scorri As Boolean) As Task(Of RectangleF?)
        Try
            If idNodo <= 0 OrElse Not DevToolsPronto() Then Return Nothing
            If scorri Then
                Dim parScorri As New JObject()
                parScorri("backendNodeId") = idNodo
                Await _devTools.InviaAsync("DOM.scrollIntoViewIfNeeded", parScorri, sessione, 2000)
            End If
            Dim parQuad As New JObject()
            parQuad("backendNodeId") = idNodo
            Dim quads As JObject = Await _devTools.InviaAsync("DOM.getContentQuads", parQuad, sessione, 2000)
            If quads IsNot Nothing Then
                Dim elenco As JArray = TryCast(quads("quads"), JArray)
                If elenco IsNot Nothing Then
                    For Each quad As JToken In elenco
                        Dim rettangolo As RectangleF? = RettangoloQuad(TryCast(quad, JArray))
                        If rettangolo.HasValue Then Return rettangolo
                    Next
                End If
            End If
            Dim parBox As New JObject()
            parBox("backendNodeId") = idNodo
            Dim box As JObject = Await _devTools.InviaAsync("DOM.getBoxModel", parBox, sessione, 2000)
            If box Is Nothing OrElse box("model") Is Nothing Then Return Nothing
            Return RettangoloQuad(TryCast(box("model")("border"), JArray))
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.RettangoloLocaleAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function RettangoloNodoAsync(sessione As String, idNodo As Integer, scorri As Boolean) As Task(Of RectangleF?)
        Try
            Dim rettangolo As RectangleF? = Await RettangoloLocaleAsync(sessione, idNodo, scorri)
            If Not rettangolo.HasValue Then Return Nothing
            Dim x As Single = rettangolo.Value.X
            Dim y As Single = rettangolo.Value.Y
            Dim corrente As String = If(sessione, "")
            Dim giri As Integer = 0
            While corrente <> "" AndAlso giri < 8
                giri += 1
                Dim sessioneCorrente As String = corrente
                Dim info As WGDevTools.SessioneFrame = _devTools.Frame.FirstOrDefault(Function(f) f.IdSessione = sessioneCorrente)
                If info Is Nothing Then Return Nothing
                Dim proprietario As Integer = Await _devTools.ProprietarioAsync(info)
                If proprietario = 0 Then Return Nothing
                If scorri Then
                    Dim parScorri As New JObject()
                    parScorri("backendNodeId") = proprietario
                    Await _devTools.InviaAsync("DOM.scrollIntoViewIfNeeded", parScorri, info.SessionePadre, 2000)
                End If
                Dim parBox As New JObject()
                parBox("backendNodeId") = proprietario
                Dim box As JObject = Await _devTools.InviaAsync("DOM.getBoxModel", parBox, info.SessionePadre, 2000)
                If box Is Nothing OrElse box("model") Is Nothing Then Return Nothing
                Dim contenuto As JArray = TryCast(box("model")("content"), JArray)
                If contenuto Is Nothing OrElse contenuto.Count < 2 Then Return Nothing
                x += CSng(CDbl(contenuto(0)))
                y += CSng(CDbl(contenuto(1)))
                corrente = If(info.SessionePadre, "")
            End While
            Return New RectangleF(x, y, rettangolo.Value.Width, rettangolo.Value.Height)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.RettangoloNodoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function CoordinateNodoAsync(sessione As String, idNodo As Integer, scorri As Boolean) As Task(Of PointF?)
        Try
            Dim rettangolo As RectangleF? = Await RettangoloNodoAsync(sessione, idNodo, scorri)
            If Not rettangolo.HasValue Then Return Nothing
            Dim x As Single = rettangolo.Value.X + rettangolo.Value.Width / 2
            Dim y As Single = rettangolo.Value.Y + rettangolo.Value.Height / 2
            If x < 0 OrElse y < 0 OrElse x >= LarghezzaVista OrElse y >= AltezzaVista Then Return Nothing
            Return New PointF(x, y)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.CoordinateNodoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function CoordinateRigaAsync(riga As RigaVirtuale, Optional scorri As Boolean = True) As Task(Of PointF?)
        Try
            If riga Is Nothing Then Return Nothing
            If AlberoDisponibile(riga) Then Return Await CoordinateNodoAsync(riga.Sessione, riga.IdDaAttivare, scorri)
            Dim rettangolo As JObject = Await ValutaOggettoAsync("window.__wgAgente ? window.__wgAgente.rettangolo(" & riga.IdDaAttivare & "," & If(scorri, "true", "false") & ") : null", 3000)
            If rettangolo Is Nothing OrElse rettangolo.Value(Of Integer?)("ok").GetValueOrDefault() = 0 Then Return Nothing
            Dim x As Single = CSng(rettangolo.Value(Of Double)("x") + rettangolo.Value(Of Double)("w") / 2)
            Dim y As Single = CSng(rettangolo.Value(Of Double)("y") + rettangolo.Value(Of Double)("h") / 2)
            If x < 0 OrElse y < 0 OrElse x >= LarghezzaVista OrElse y >= AltezzaVista Then Return Nothing
            Return New PointF(x, y)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.CoordinateRigaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function RettangoloRigaAsync(riga As RigaVirtuale) As Task(Of RectangleF?)
        Try
            If riga Is Nothing Then Return Nothing
            If AlberoDisponibile(riga) Then Return Await RettangoloNodoAsync(riga.Sessione, riga.IdElemento, True)
            Dim rettangolo As JObject = Await ValutaOggettoAsync("window.__wgAgente ? window.__wgAgente.rettangolo(" & riga.IdElemento & ",true) : null", 3000)
            If rettangolo Is Nothing OrElse rettangolo.Value(Of Integer?)("ok").GetValueOrDefault() = 0 Then Return Nothing
            Return New RectangleF(CSng(rettangolo.Value(Of Double)("x")), CSng(rettangolo.Value(Of Double)("y")), CSng(rettangolo.Value(Of Double)("w")), CSng(rettangolo.Value(Of Double)("h")))
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.RettangoloRigaAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function PassaSopraAsync(riga As RigaVirtuale) As Task(Of PointF?)
        Try
            Dim punto As PointF? = Await CoordinateRigaAsync(riga)
            If Not punto.HasValue Then Return Nothing
            SpostaMouse(Math.Max(0, CInt(punto.Value.X) - 40), Math.Max(0, CInt(punto.Value.Y) - 40))
            Await AttendiInputAsync()
            Await Task.Delay(40)
            SpostaMouse(CInt(punto.Value.X), CInt(punto.Value.Y))
            Await AttendiInputAsync()
            Return punto
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.PassaSopraAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function SuggerimentoAlPuntoAsync(x As Single, y As Single) As Task(Of String)
        Try
            Dim espressione As String = EspressioneSuggerimento & "(" & CInt(x).ToString() & "," & CInt(y).ToString() & ")"
            Return If(Await ValutaAsync(espressione, 2000), "")
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.SuggerimentoAlPuntoAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Async Function ClicAsync(riga As RigaVirtuale, pulsante As PulsanteMouse, conteggio As Integer, modificatori As ModificatoriInput) As Task(Of Boolean)
        Try
            Dim punto As PointF? = Await CoordinateRigaAsync(riga)
            If Not punto.HasValue Then Return False
            ImpostaFocusMotore(True)
            InviaClic(CInt(punto.Value.X), CInt(punto.Value.Y), pulsante, conteggio, modificatori)
            Await AttendiInputAsync()
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.ClicAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Public Async Function ScorriSuRigaAsync(riga As RigaVirtuale, versoIlBasso As Boolean) As Task(Of Boolean)
        Try
            Dim punto As PointF? = Await CoordinateRigaAsync(riga)
            If Not punto.HasValue Then Return False
            Rotellina(CInt(punto.Value.X), CInt(punto.Value.Y), If(versoIlBasso, -360, 360))
            Await AttendiInputAsync()
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.ScorriSuRigaAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Private Async Function MouseCdpAsync(tipo As String, x As Single, y As Single, pulsante As String, pulsanti As Integer) As Task
        Try
            Await _devTools.InviaAsync("Input.dispatchMouseEvent", EventoMouse(tipo, x, y, pulsante, pulsanti, If(tipo = "mouseMoved", 0, 1), ModificatoriInput.Nessuno), "", 2000)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.MouseCdpAsync: " & ex.Message)
        End Try
    End Function

    Public Async Function TrascinaAsync(origine As RigaVirtuale, destinazione As RigaVirtuale) As Task(Of String)
        Dim intercettazione As Boolean = False
        Try
            If origine Is Nothing OrElse destinazione Is Nothing Then Return "Elemento da trascinare o destinazione mancante"
            If Not DevToolsPronto() Then Return "Trascinamento non disponibile in questa pagina"
            Await AttendiInputAsync()
            Dim puntoDestinazione As PointF? = Await CoordinateRigaAsync(destinazione, True)
            Dim puntoOrigine As PointF? = Await CoordinateRigaAsync(origine, False)
            If Not puntoOrigine.HasValue Then
                puntoOrigine = Await CoordinateRigaAsync(origine, True)
                puntoDestinazione = Await CoordinateRigaAsync(destinazione, False)
            End If
            If Not puntoOrigine.HasValue OrElse Not puntoDestinazione.HasValue Then Return "L'elemento da trascinare e la destinazione non sono visibili insieme nella pagina"
            Dim attesa As New TaskCompletionSource(Of JObject)(TaskCreationOptions.RunContinuationsAsynchronously)
            SyncLock _lockTrascinamento
                _attesaTrascinamento = attesa
            End SyncLock
            Dim parIntercetta As New JObject()
            parIntercetta("enabled") = True
            intercettazione = (Await _devTools.InviaAsync("Input.setInterceptDrags", parIntercetta, "", 2000)) IsNot Nothing
            Dim a As PointF = puntoOrigine.Value
            Dim b As PointF = puntoDestinazione.Value
            Await MouseCdpAsync("mouseMoved", a.X, a.Y, "none", 0)
            Await MouseCdpAsync("mousePressed", a.X, a.Y, "left", 1)
            Await MouseCdpAsync("mouseMoved", a.X + 6, a.Y + 6, "left", 1)
            Dim dati As JObject = Nothing
            Dim finito As Task = Await Task.WhenAny(attesa.Task, Task.Delay(400))
            If finito Is attesa.Task Then dati = attesa.Task.Result
            If dati IsNot Nothing Then
                For Each tipo As String In New String() {"dragEnter", "dragOver", "drop"}
                    Dim parDrag As New JObject()
                    parDrag("type") = tipo
                    parDrag("x") = b.X
                    parDrag("y") = b.Y
                    parDrag("data") = dati
                    Await _devTools.InviaAsync("Input.dispatchDragEvent", parDrag, "", 3000)
                Next
                Await MouseCdpAsync("mouseReleased", b.X, b.Y, "left", 0)
                Return ""
            End If
            For passo As Integer = 1 To 8
                Dim x As Single = a.X + (b.X - a.X) * passo / 8.0F
                Dim y As Single = a.Y + (b.Y - a.Y) * passo / 8.0F
                Await MouseCdpAsync("mouseMoved", x, y, "left", 1)
                Await Task.Delay(30)
            Next
            Await MouseCdpAsync("mouseReleased", b.X, b.Y, "left", 0)
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.TrascinaAsync: " & ex.Message)
            Return "Trascinamento non riuscito"
        Finally
            Try
                SyncLock _lockTrascinamento
                    _attesaTrascinamento = Nothing
                End SyncLock
                If intercettazione AndAlso _devTools IsNot Nothing Then
                    Dim parFine As New JObject()
                    parFine("enabled") = False
                    _devTools.InviaSenzaAttesa("Input.setInterceptDrags", parFine, "")
                End If
            Catch exFine As Exception
                log.ScriviLog("WGCdpInterazione.TrascinaAsync fine: " & exFine.Message)
            End Try
        End Try
    End Function

    Public Async Function SchermataAsync(rettangolo As RectangleF?) As Task(Of Byte())
        Try
            If Not DevToolsPronto() Then Return Nothing
            If Not rettangolo.HasValue Then Return Await _devTools.CatturaSchermataAsync("", 0, 0, 0, 0)
            Dim metriche As JObject = Await _devTools.InviaAsync("Page.getLayoutMetrics", Nothing, "", 3000)
            Dim pageX As Double = 0
            Dim pageY As Double = 0
            If metriche IsNot Nothing Then
                Dim vista As JObject = TryCast(If(metriche("cssVisualViewport"), metriche("visualViewport")), JObject)
                If vista IsNot Nothing Then
                    pageX = vista.Value(Of Double?)("pageX").GetValueOrDefault()
                    pageY = vista.Value(Of Double?)("pageY").GetValueOrDefault()
                End If
            End If
            Dim r As RectangleF = rettangolo.Value
            Dim larghezza As Double = Math.Max(1, Math.Min(r.Width, 4000))
            Dim altezza As Double = Math.Max(1, Math.Min(r.Height, 6000))
            Return Await _devTools.CatturaSchermataAsync("", r.X + pageX, r.Y + pageY, larghezza, altezza)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.SchermataAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Async Function IndirizzoRigaAsync(riga As RigaVirtuale) As Task(Of String)
        Try
            If riga Is Nothing Then Return ""
            If Not String.IsNullOrEmpty(riga.Indirizzo) Then Return riga.Indirizzo
            If AlberoDisponibile(riga) Then
                Dim valore As JToken = Await _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdDaAttivare, FunzioneIndirizzo, 2000)
                If valore IsNot Nothing AndAlso valore.Type = JTokenType.String Then Return valore.Value(Of String)()
                If riga.IdElemento <> riga.IdDaAttivare Then
                    valore = Await _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdElemento, FunzioneIndirizzo, 2000)
                    If valore IsNot Nothing AndAlso valore.Type = JTokenType.String Then Return valore.Value(Of String)()
                End If
                Return ""
            End If
            Return If(Await ValutaAsync("window.__wgAgente ? window.__wgAgente.indirizzo(" & riga.IdDaAttivare & ") : ''", 2000), "")
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.IndirizzoRigaAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Async Function SorgenteMediaAsync(riga As RigaVirtuale) As Task(Of String)
        Try
            If riga Is Nothing Then Return ""
            If AlberoDisponibile(riga) Then
                Dim valore As JToken = Await _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdElemento, FunzioneSorgenteMedia, 2000)
                If valore IsNot Nothing AndAlso valore.Type = JTokenType.String Then Return valore.Value(Of String)()
                Return ""
            End If
            Return If(Await ValutaAsync("window.__wgAgente ? window.__wgAgente.sorgenteMedia(" & riga.IdElemento & ") : ''", 2000), "")
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.SorgenteMediaAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Async Function ComandoMediaAsync(riga As RigaVirtuale, comando As String) As Task(Of String)
        Try
            If riga Is Nothing Then Return ""
            Dim funzione As String = "function(c){var e=this;var m=null;if(e.localName==='video'||e.localName==='audio'){m=e;}else{try{m=e.querySelector('video,audio');}catch(x){m=null;}}" &
                "if(!m)return 'nessun';try{if(c==='play'){var p=m.play();if(p&&p.catch)p.catch(function(){});return 'avviato';}" &
                "if(c==='pause'){m.pause();return 'pausa';}if(c==='muto'){m.muted=!m.muted;return m.muted?'muto':'audio';}" &
                "if(c==='avanti'){m.currentTime=Math.min((m.duration||0),m.currentTime+10);return 'avanti';}" &
                "if(c==='indietro'){m.currentTime=Math.max(0,m.currentTime-10);return 'indietro';}" &
                "if(c==='stato'){var d=isFinite(m.duration)?Math.round(m.duration):0;return (m.paused?'in pausa':'in riproduzione')+'|'+Math.round(m.currentTime)+'|'+d+'|'+(m.error?m.error.code:0);}}catch(x){return 'errore';}return '';}"
            If AlberoDisponibile(riga) Then
                Dim argomenti As New JArray()
                Dim argomento As New JObject()
                argomento("value") = comando
                argomenti.Add(argomento)
                Dim valore As JToken = Await _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdElemento, funzione, 3000, argomenti)
                If valore IsNot Nothing AndAlso valore.Type = JTokenType.String Then Return valore.Value(Of String)()
                Return ""
            End If
            Return If(Await ValutaAsync("window.__wgAgente ? window.__wgAgente.comandoMedia(" & riga.IdElemento & "," & JsonConvert.SerializeObject(comando) & ") : ''", 3000), "")
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.ComandoMediaAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Async Function IdDestinazioneAncoraAsync(frammento As String) As Task(Of Integer)
        Try
            If String.IsNullOrEmpty(frammento) OrElse Not DevToolsPronto() Then Return 0
            Dim decodificato As String = frammento
            Try
                decodificato = Uri.UnescapeDataString(frammento)
            Catch exDecodifica As Exception
                log.ScriviLog("WGCdpInterazione.IdDestinazioneAncoraAsync decodifica: " & exDecodifica.Message)
            End Try
            Dim valoreJs As String = JsonConvert.SerializeObject(decodificato)
            Dim espressione As String = "(function(f){if(!f)return null;var e=document.getElementById(f);if(!e){var n=document.getElementsByName(f);e=n&&n.length?n[0]:null;}" &
                "if(!e&&f.indexOf(':~:text=')===0)return null;return e;})(" & valoreJs & ")"
            Return Await _devTools.IdNodoDaEspressioneAsync("", espressione)
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.IdDestinazioneAncoraAsync: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Async Function TestoSelezionatoAsync() As Task(Of String)
        Try
            If DevToolsPronto() Then
                Dim sessione As String = (Await _devTools.ElementoAttivoAsync()).Item1
                Dim valore As JToken = Await _devTools.ValutaAsync(sessione, "(function(){try{var a=document.activeElement;if(a&&(a.localName==='input'||a.localName==='textarea')&&typeof a.selectionStart==='number'){return a.value.substring(a.selectionStart,a.selectionEnd);}return String(window.getSelection()||'');}catch(e){return '';}})()", 2000)
                If valore IsNot Nothing AndAlso valore.Type = JTokenType.String Then Return valore.Value(Of String)()
            End If
            Return If(Await ValutaAsync("String(window.getSelection()||'')", 2000), "")
        Catch ex As Exception
            log.ScriviLog("WGCdpInterazione.TestoSelezionatoAsync: " & ex.Message)
            Return ""
        End Try
    End Function
End Class
