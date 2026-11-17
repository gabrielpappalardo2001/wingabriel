Imports System.Collections.Generic
Imports System.Linq
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports WGBrowserManager
Imports wgcontrols

' Contenuti particolari delle pagine: formule matematiche da esplorare a pezzi, verifiche anti-robot
' (reCAPTCHA, hCaptcha, Cloudflare) da raggiungere e da passare alla versione audio, risultati della
' ricerca nella pagina mostrati, evidenziati e contati.
Partial Public Class WGSchedaBrowser

#Region "Formule"

    Private Async Function EsploraFormulaAsync(riga As RigaVirtuale) As Task
        Try
            Dim formula As WGFormula = Await _cursore.FormulaDiAsync(riga)
            If formula Is Nothing OrElse formula.Radice Is Nothing Then
                Annuncia("Formula " & riga.Testo & ". Questa formula non si può esplorare a pezzi")
                Return
            End If
            EsploraNodoFormula(formula.Radice, "Formula " & formula.Parlato, formula, True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EsploraFormulaAsync: " & ex.ToString())
        End Try
    End Function

    ' Ogni pezzo è una voce; Invio su un pezzo composto ne apre le parti in un altro menu, Esc torna al
    ' livello di prima. I menu restano aperti dopo la scelta, così si può rileggere un pezzo semplice.
    Private Sub EsploraNodoFormula(nodo As WGFormula.NodoFormula, titolo As String, formula As WGFormula, radice As Boolean)
        Try
            Dim menu As New WGMenu()
            If radice AndAlso formula.LaTeX <> "" Then
                menu.aggiungi("Copia la formula in LaTeX: " & formula.LaTeX, Sub(s, e) CopiaNegliAppunti(formula.LaTeX, "Formula copiata in LaTeX"))
            End If
            Dim parti As List(Of WGFormula.NodoFormula) = If(nodo.Figli.Count > 0, nodo.Figli, New List(Of WGFormula.NodoFormula) From {nodo})
            Dim usate As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            For Each parte As WGFormula.NodoFormula In parti
                Dim pezzo As WGFormula.NodoFormula = parte
                Dim voce As String = pezzo.Descrizione & If(pezzo.Figli.Count > 0, ", " & pezzo.Figli.Count & " parti", "")
                ' I menu scartano le voci uguali: le ripetizioni, come un secondo "più", prendono un numero.
                Dim volte As Integer = 0
                usate.TryGetValue(voce, volte)
                usate(voce) = volte + 1
                If volte > 0 Then voce &= ", " & (volte + 1)
                menu.aggiungi(voce, Sub(s, e)
                                        If pezzo.Figli.Count > 0 Then
                                            EsploraNodoFormula(pezzo, pezzo.Descrizione, formula, False)
                                        Else
                                            lettura.LeggiTesto(pezzo.Descrizione)
                                        End If
                                    End Sub)
            Next
            If radice Then AddHandler menu.abbandonato, Sub(s, e) _contenitore.SegnaRitornoDaMenu()
            menu.avvia(titolo, False)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EsploraNodoFormula: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Verifiche anti-robot"

    ' Tipo di verifica già annunciato in questa pagina, per non ripeterlo a ogni aggiornamento.
    Private _verificaAnnunciata As String = ""

    Private Shared ReadOnly SegniVerifica As String()() = {
        New String() {"recaptcha", "reCAPTCHA"},
        New String() {"hcaptcha", "hCaptcha"},
        New String() {"cloudflare security challenge", "verifica di Cloudflare"},
        New String() {"verifica di sicurezza di cloudflare", "verifica di Cloudflare"},
        New String() {"turnstile", "verifica di Cloudflare"},
        New String() {"non sono un robot", "verifica anti-robot"},
        New String() {"i'm not a robot", "verifica anti-robot"},
        New String() {"i am not a robot", "verifica anti-robot"},
        New String() {"verify you are human", "verifica anti-robot"},
        New String() {"verifica di essere un essere umano", "verifica anti-robot"},
        New String() {"captcha", "captcha"}
    }

    Private Sub DimenticaVerificaAntiRobot()
        _verificaAnnunciata = ""
    End Sub

    ' Prima riga che appartiene a una verifica anti-robot, con il nome della verifica.
    Private Function TrovaVerificaAntiRobot() As Tuple(Of String, Integer)
        Try
            If _cursore Is Nothing Then Return Nothing
            Dim righe As List(Of RigaVirtuale) = _cursore.Righe
            For i As Integer = 0 To righe.Count - 1
                Dim r As RigaVirtuale = righe(i)
                Dim testo As String = (If(r.Testo, "") & " " & If(r.Descrizione, "")).ToLowerInvariant()
                If testo.Length < 4 OrElse testo.Length > 400 Then Continue For
                For Each segno As String() In SegniVerifica
                    If testo.Contains(segno(0)) Then Return Tuple.Create(segno(1), i)
                Next
            Next
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TrovaVerificaAntiRobot: " & ex.Message)
        End Try
        Return Nothing
    End Function

    ' Frase da aggiungere al riepilogo della pagina; le pagine di verifica a tutto schermo hanno già il loro avviso.
    Private Function AvvisoVerificaAntiRobot() As String
        Try
            If EPaginaDiVerifica() Then Return ""
            Dim trovata As Tuple(Of String, Integer) = TrovaVerificaAntiRobot()
            If trovata Is Nothing Then Return ""
            _verificaAnnunciata = trovata.Item1
            Return "Contiene una verifica anti-robot, " & trovata.Item1 & ": Control Shift K per raggiungerla"
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AvvisoVerificaAntiRobot: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Verifiche che compaiono dopo, per esempio inviando un modulo.
    Private Sub ControllaVerificaAntiRobotComparsa()
        Try
            If _verificaAnnunciata <> "" OrElse Not paginaPronta OrElse EPaginaDiVerifica() Then Return
            Dim trovata As Tuple(Of String, Integer) = TrovaVerificaAntiRobot()
            If trovata Is Nothing Then Return
            _verificaAnnunciata = trovata.Item1
            Annuncia("È comparsa una verifica anti-robot, " & trovata.Item1 & ". Control Shift K per raggiungerla")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ControllaVerificaAntiRobotComparsa: " & ex.Message)
        End Try
    End Sub

    Private Function IndiceRiga(condizione As Func(Of RigaVirtuale, Boolean)) As Integer
        Dim righe As List(Of RigaVirtuale) = _cursore.Righe
        For i As Integer = 0 To righe.Count - 1
            If condizione(righe(i)) Then Return i
        Next
        Return -1
    End Function

    Private Shared Function TestoMinuscolo(r As RigaVirtuale) As String
        Return If(r.Testo, "").ToLowerInvariant()
    End Function

    ''' <summary>
    ''' Control Shift K: se la verifica con le immagini è aperta passa a quella audio e porta sul pulsante di
    ''' riproduzione; altrimenti porta sulla casella "Non sono un robot". La verifica la risolve l'utente:
    ''' il browser rende solo raggiungibile la versione audio che i siti offrono per chi non vede.
    ''' </summary>
    Friend Async Function GestisciVerificaAntiRobotAsync() As Task
        Try
            If _cursore Is Nothing Then Return
            Await _cursore.AssicuraBufferAsync()
            Dim trovata As Tuple(Of String, Integer) = TrovaVerificaAntiRobot()
            If trovata Is Nothing Then
                Annuncia("Nessuna verifica anti-robot in questa pagina")
                Return
            End If
            Dim audio As Integer = IndiceRiga(Function(r) r.RuoloDaAttivare = "button" AndAlso TestoMinuscolo(r).Contains("audio"))
            If audio >= 0 Then
                Dim navigazione As Integer = numeroNavigazione
                _cursore.SpostaA(audio, False)
                Annuncia("Passo alla verifica audio, attendere")
                Await _cursore.AttivaCorrenteAsync()
                Await AttendiStabilitaAsync(2500)
                If _chiusa OrElse navigazione <> numeroNavigazione Then Return
                Await _cursore.AggiornaAsync(False)
                Dim riproduci As Integer = IndiceRiga(Function(r) r.RuoloDaAttivare = "button" AndAlso (TestoMinuscolo(r).Contains("riproduci") OrElse TestoMinuscolo(r).Contains("play") OrElse TestoMinuscolo(r).Contains("ascolta")))
                If riproduci >= 0 Then
                    _cursore.SpostaA(riproduci, False)
                    Annuncia("Verifica audio pronta. " & _cursore.RigaCorrente.CostruisciAnnuncio(False) & ". Invio per ascoltare, poi scrivere nel campo quello che si è sentito e premere il pulsante Verifica")
                Else
                    Annuncia("Verifica audio richiesta. Il pulsante per ascoltarla si raggiunge con B")
                End If
                Return
            End If
            Dim casella As Integer = IndiceRiga(Function(r) r.Ruolo = "checkbox" AndAlso SegniVerifica.Any(Function(s) TestoMinuscolo(r).Contains(s(0))))
            If casella < 0 Then casella = trovata.Item2
            _cursore.SpostaA(casella, False)
            Annuncia(trovata.Item1 & ". " & _cursore.RigaCorrente.CostruisciAnnuncio(False) & ". Invio per selezionarla; se poi compare la verifica con le immagini, Control Shift K passa a quella audio")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.GestisciVerificaAntiRobotAsync: " & ex.ToString())
        End Try
    End Function

#End Region

#Region "Immagini"

    Private Const MassimoDescrizioniAutomatiche As Integer = 20
    Private _descrizioniAutomatiche As Integer = 0
    Private _navigazioneDescrizioni As Integer = -1
    Private _descrizioneAutomaticaInCorso As Boolean = False
    Private _descrizioniAutomaticheSospese As Boolean = False
    Private ReadOnly _immaginiGiaProvate As New HashSet(Of String)(StringComparer.Ordinal)

    ''' <summary>Riga corrente se è un'immagine o un'area disegnata, altrimenti Nothing.</summary>
    Friend ReadOnly Property ImmagineCorrente As RigaVirtuale
        Get
            Try
                If _cursore Is Nothing Then Return Nothing
                Dim riga As RigaVirtuale = _cursore.RigaCorrente
                If riga IsNot Nothing AndAlso (riga.Ruolo = "img" OrElse riga.Ruolo = "canvas") Then Return riga
            Catch ex As Exception
                log.ScriviLog("WGSchedaBrowser.ImmagineCorrente: " & ex.Message)
            End Try
            Return Nothing
        End Get
    End Property

    Friend Sub DescriviImmagineCorrente()
        Dim riga As RigaVirtuale = ImmagineCorrente
        If riga Is Nothing Then
            Annuncia("Il cursore non è su un'immagine")
            Return
        End If
        DescriviRiga(riga)
    End Sub

    Friend Sub RiconosciTestoImmagineCorrente()
        Dim riga As RigaVirtuale = ImmagineCorrente
        If riga Is Nothing Then
            Annuncia("Il cursore non è su un'immagine")
            Return
        End If
        RiconosciTestoRiga(riga)
    End Sub

    Private Shared Function ChiaveImmagine(riga As RigaVirtuale) As String
        Return If(riga.Sorgente <> "", riga.Sorgente, If(riga.Sessione, "") & "|" & riga.IdElemento)
    End Function

    ' Con la descrizione automatica attiva, l'immagine senza descrizione su cui è arrivato il cursore viene
    ' descritta in sottofondo e letta appena pronta, se il cursore è ancora lì. Una alla volta, ognuna una
    ' volta sola, al massimo 20 per pagina; al primo errore (per esempio la quota finita) ci si ferma.
    Private Sub ControllaDescrizioneAutomatica()
        Try
            If Not WGImpostazioniBrowser.DescrizioneAutomaticaImmagini OrElse modalitaDiretta OrElse _chiusa Then Return
            If _navigazioneDescrizioni <> numeroNavigazione Then
                _navigazioneDescrizioni = numeroNavigazione
                _descrizioniAutomatiche = 0
                _descrizioniAutomaticheSospese = False
            End If
            If _descrizioneAutomaticaInCorso OrElse _descrizioniAutomaticheSospese OrElse _descrizioniAutomatiche >= MassimoDescrizioniAutomatiche Then Return
            Dim riga As RigaVirtuale = ImmagineCorrente
            If riga Is Nothing OrElse Not riga.SenzaNome Then Return
            If Not WGAi.AlmenoUnProviderConfigurato() Then Return
            If Not _immaginiGiaProvate.Add(ChiaveImmagine(riga)) Then Return
            _descrizioniAutomatiche += 1
            DescriviAutomaticamente(riga)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ControllaDescrizioneAutomatica: " & ex.Message)
        End Try
    End Sub

    Private Async Sub DescriviAutomaticamente(riga As RigaVirtuale)
        Dim percorso As String = ""
        _descrizioneAutomaticaInCorso = True
        Try
            Dim navigazione As Integer = numeroNavigazione
            percorso = Await SchermataRigaAsync(riga)
            If percorso = "" OrElse _chiusa OrElse navigazione <> numeroNavigazione Then Return
            Dim descrizione As String = Await DescriviImmagineAsync(percorso)
            If _chiusa OrElse navigazione <> numeroNavigazione Then Return
            If descrizione = "" Then
                _descrizioniAutomaticheSospese = True
                Return
            End If
            AggiornaRigaDescritta(riga, descrizione)
            Dim corrente As RigaVirtuale = If(_cursore IsNot Nothing, _cursore.RigaCorrente, Nothing)
            If corrente IsNot Nothing AndAlso (corrente Is riga OrElse (corrente.Ruolo = riga.Ruolo AndAlso ChiaveImmagine(corrente) = ChiaveImmagine(riga))) Then
                If corrente IsNot riga Then AggiornaRigaDescritta(corrente, descrizione)
                Annuncia("Descrizione: " & descrizione)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviAutomaticamente: " & ex.Message)
        Finally
            _descrizioneAutomaticaInCorso = False
            CancellaFile(percorso)
        End Try
    End Sub

#End Region

#Region "Ricerca nella pagina"

    ' Seleziona nella pagina l'occorrenza trovata e la porta al centro dello schermo: chi vede la trova
    ' evidenziata, e la pagina scorre come nella ricerca di Chrome. Riceve il testo cercato e quante
    ' occorrenze lo precedono nell'elemento.
    Private Const FunzioneEvidenzia As String =
        "function(cercato,prima){try{var w=this.ownerDocument.createTreeWalker(this,4,null);var nodi=[],testo='';var n;" &
        "while((n=w.nextNode())){nodi.push({n:n,i:testo.length});testo+=n.nodeValue;}" &
        "var basso=testo.toLocaleLowerCase(),c=cercato.toLocaleLowerCase(),p=-1,k=0;" &
        "while(true){var q=basso.indexOf(c,p+1);if(q<0)break;p=q;if(k>=prima)break;k++;}" &
        "if(p<0){this.scrollIntoView({block:'center',inline:'nearest',behavior:'instant'});return false;}" &
        "function punto(pos){for(var j=nodi.length-1;j>=0;j--){if(nodi[j].i<=pos)return {n:nodi[j].n,o:pos-nodi[j].i};}return {n:nodi[0].n,o:0};}" &
        "var a=punto(p),b=punto(p+c.length);var r=this.ownerDocument.createRange();r.setStart(a.n,Math.min(a.o,a.n.nodeValue.length));" &
        "r.setEnd(b.n,Math.min(b.o,b.n.nodeValue.length));var s=this.ownerDocument.getSelection();s.removeAllRanges();s.addRange(r);" &
        "var e=a.n.parentElement||this;e.scrollIntoView({block:'center',inline:'nearest',behavior:'instant'});return true;}catch(x){return false;}}"

    Private Sub MostraRisultatoRicerca(indice As Integer, carattere As Integer)
        Try
            Dim riga As RigaVirtuale = _cursore.Righe(indice)
            _cursore.SpostaA(indice, False)
            If carattere >= 0 Then _cursore.PosizionaCarattere(carattere)
            Dim conteggio As Tuple(Of Integer, Integer) = _cursore.ContaOccorrenze(ultimaRicerca, indice, carattere)
            Dim testo As String = riga.CostruisciAnnuncio(False)
            ' Nelle righe lunghe si legge dal punto trovato, dall'inizio della parola.
            If carattere > 80 AndAlso riga.Testo.Length > 200 Then
                Dim inizio As Integer = riga.Testo.LastIndexOf(" "c, Math.Max(0, carattere - 40))
                testo = "…" & riga.Testo.Substring(Math.Max(0, inizio + 1))
            End If
            If testo.Length > 400 Then testo = testo.Substring(0, 400)
            Dim posizione As String = If(conteggio.Item1 > 1, "Risultato " & conteggio.Item2 & " di " & conteggio.Item1 & ": ", "")
            lettura.LeggiTesto(posizione & testo)
            EvidenziaNellaPagina(riga, carattere)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraRisultatoRicerca: " & ex.Message)
        End Try
    End Sub

    Private Async Sub EvidenziaNellaPagina(riga As RigaVirtuale, carattere As Integer)
        Try
            If _devTools Is Nothing OrElse riga Is Nothing OrElse riga.IdElemento <= 0 Then Return
            Dim precedenti As Integer = 0
            If carattere > 0 Then
                Dim prima As String = riga.Testo.Substring(0, Math.Min(carattere, riga.Testo.Length)).ToLowerInvariant()
                Dim cercato As String = ultimaRicerca.ToLowerInvariant()
                Dim p As Integer = prima.IndexOf(cercato, StringComparison.Ordinal)
                While p >= 0
                    precedenti += 1
                    p = prima.IndexOf(cercato, p + 1, StringComparison.Ordinal)
                End While
            End If
            Dim argomenti As New JArray(New JObject(New JProperty("value", ultimaRicerca)), New JObject(New JProperty("value", precedenti)))
            Dim esito As JToken = Await _devTools.ChiamaSuNodoAsync(riga.Sessione, riga.IdElemento, FunzioneEvidenzia, 2000, argomenti)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EvidenziaNellaPagina: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
