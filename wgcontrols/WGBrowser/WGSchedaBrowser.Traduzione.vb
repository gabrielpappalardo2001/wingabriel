Imports System.Collections.Generic
Imports System.Text
Imports System.Threading.Tasks
Imports WGBrowserManager
Imports wgcontrols

' Traduzione delle pagine: il cursore virtuale legge il testo tradotto al posto dell'originale, la pagina vera
' resta com'è, quindi link, pulsanti e campi continuano a funzionare. Quando una pagina è pronta si decide,
' secondo la configurazione del browser, se tradurla da sola, chiedere (una volta per sito in ogni sessione)
' o lasciarla com'è; i siti tra le eccezioni non vengono mai tradotti.
Partial Public Class WGSchedaBrowser

    Private Const DecisioneNessuna As Integer = 0
    Private Const DecisioneTraduci As Integer = 1
    Private Const DecisioneChiedi As Integer = 2

    Private _traduzioneInPreparazione As Boolean = False
    ' Sito che questa scheda sta traducendo: le pagine successive dello stesso sito si traducono da sole.
    Private _hostTradotto As String = ""
    Private _linguaPaginaTraduzione As String = ""

    Public ReadOnly Property TraduzioneAttiva As Boolean
        Get
            Return _cursore IsNot Nothing AndAlso _cursore.TraduzioneAttiva
        End Get
    End Property

    ''' <summary>Control Shift U e menu delle opzioni: traduce la pagina o torna all'originale.</summary>
    Friend Sub CommutaTraduzione()
        Try
            If _cursore Is Nothing Then Return
            If TraduzioneAttiva Then
                _cursore.FermaTraduzione()
                _hostTradotto = ""
                WGTraduttore.ApplicaLinguaLettura(_linguaPaginaTraduzione)
                Dim riga As RigaVirtuale = _cursore.RigaCorrente
                Annuncia("Pagina originale" & If(riga IsNot Nothing, ". " & riga.CostruisciAnnuncio(False), ""))
                Return
            End If
            AttivaTraduzione(True, "")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CommutaTraduzione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Aggiunge o toglie il sito della pagina dalle eccezioni della traduzione.</summary>
    Friend Sub CommutaEccezioneTraduzione()
        Try
            Dim host As String = HostPagina
            If host = "" Then
                Annuncia("Questa pagina non appartiene a un sito")
                Return
            End If
            If WGImpostazioniBrowser.SitoNonTradotto(host) Then
                WGImpostazioniBrowser.ImpostaSitoNonTradotto(host, False)
                MessaggioBrowser("Le pagine di " & host & " potranno di nuovo essere tradotte")
            Else
                WGImpostazioniBrowser.ImpostaSitoNonTradotto(host, True)
                If TraduzioneAttiva Then _cursore.FermaTraduzione()
                _hostTradotto = ""
                MessaggioBrowser("Le pagine di " & host & " non verranno mai tradotte")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CommutaEccezioneTraduzione: " & ex.Message)
        End Try
    End Sub

    ' Una nuova pagina riparte dal testo originale; DecidiTraduzioneAsync la ritradurrà se serve.
    Private Sub SospendiTraduzionePerNuovaPagina()
        Try
            If _cursore IsNot Nothing AndAlso _cursore.TraduzioneAttiva Then _cursore.FermaTraduzione()
            WGTraduttore.RipristinaLinguaLettura()
            _linguaPaginaTraduzione = ""
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SospendiTraduzionePerNuovaPagina: " & ex.Message)
        End Try
    End Sub

    Private Function TestoCampione() As String
        Dim testo As New StringBuilder()
        Try
            If _cursore Is Nothing Then Return ""
            For Each r As RigaVirtuale In _cursore.Righe
                Dim t As String = If(r.TestoOriginale <> "", r.TestoOriginale, r.Testo)
                If String.IsNullOrWhiteSpace(t) OrElse t.Length < 20 Then Continue For
                testo.Append(t).Append(" ")
                If testo.Length > 600 Then Exit For
            Next
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TestoCampione: " & ex.Message)
        End Try
        Return testo.ToString()
    End Function

    ' Lingua della pagina: quella dichiarata dal sito; se manca, o se non è la lingua di destinazione e il
    ' rilevamento è attivo, quella riconosciuta dal traduttore sul testo. "" se non si sa.
    Private Async Function LinguaPaginaAsync() As Task(Of String)
        Try
            If _linguaPaginaTraduzione <> "" Then Return _linguaPaginaTraduzione
            Dim dichiarata As String = WGTraduttore.NormalizzaCodice(If(_cursore IsNot Nothing, _cursore.LinguaPagina, ""))
            Dim lingua As String = dichiarata
            If WGTraduttore.RilevamentoAttivo AndAlso Not WGTraduttore.StessaLingua(dichiarata, WGTraduttore.LinguaDestinazione) Then
                Dim rilevata As String = Await WGTraduttore.RilevaLinguaAsync(TestoCampione())
                If rilevata <> "" Then lingua = rilevata
            End If
            _linguaPaginaTraduzione = lingua
            Return lingua
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LinguaPaginaAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    ' Cosa fare della traduzione della pagina appena pronta: niente, tradurla o chiedere.
    Private Async Function DecidiTraduzioneAsync() As Task(Of Integer)
        Try
            Dim host As String = HostPagina
            If _cursore Is Nothing OrElse host = "" OrElse Not WGArchivioBrowser.IndirizzoRegistrabile(_indirizzo) Then Return DecisioneNessuna
            If WGImpostazioniBrowser.SitoNonTradotto(host) Then Return DecisioneNessuna
            If _cursore.TraduzioneAttiva Then Return DecisioneNessuna
            Dim continua As Boolean = String.Equals(_hostTradotto, host, StringComparison.OrdinalIgnoreCase)
            Dim sempre As Boolean = WGImpostazioniBrowser.SitoTradottoSempre(host)
            Dim modo As String = WGImpostazioniBrowser.ModoTraduzione
            If Not continua AndAlso Not sempre AndAlso modo = WGImpostazioniBrowser.TraduzioneAComando Then Return DecisioneNessuna
            If modo = WGImpostazioniBrowser.TraduzioneChiedi AndAlso Not continua AndAlso Not sempre AndAlso (Not _attiva OrElse modalitaDiretta) Then Return DecisioneNessuna
            Dim lingua As String = Await LinguaPaginaAsync()
            If lingua = "" OrElse WGTraduttore.StessaLingua(lingua, WGTraduttore.LinguaDestinazione) OrElse WGTraduttore.LinguaEsclusa(lingua) Then Return DecisioneNessuna
            If continua OrElse sempre OrElse modo = WGImpostazioniBrowser.TraduzioneAutomatica Then Return DecisioneTraduci
            If PrimaVolta("traduzione|" & host.ToLowerInvariant()) Then Return DecisioneChiedi
            Return DecisioneNessuna
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DecidiTraduzioneAsync: " & ex.Message)
            Return DecisioneNessuna
        End Try
    End Function

    Private Function OrigineTraduzione(linguaPagina As String) As String
        ' Con Google e il rilevamento attivo ogni riga viene riconosciuta da sola: utile nelle pagine miste.
        If WGTraduttore.RilevamentoAttivo AndAlso WGTraduttore.Motore = WGTraduttore.MotoreGoogle Then Return ""
        If linguaPagina <> "" Then Return linguaPagina
        Return WGTraduttore.LinguaOrigine
    End Function

    ' Traduce la prima parte della pagina prima del riepilogo e restituisce la frase da aggiungergli.
    Private Async Function TraduciPerRiepilogoAsync(navigazione As Integer) As Task(Of String)
        Try
            Dim lingua As String = Await LinguaPaginaAsync()
            Dim destinazione As String = WGTraduttore.LinguaDestinazione
            Dim errore As String = Await _cursore.AvviaTraduzioneAsync(OrigineTraduzione(lingua), destinazione)
            If _chiusa OrElse navigazione <> numeroNavigazione Then Return ""
            If errore <> "" Then Return "Traduzione non riuscita: " & errore
            _hostTradotto = HostPagina
            Return DescriviTraduzione(lingua)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TraduciPerRiepilogoAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function DescriviTraduzione(linguaPagina As String) As String
        Dim da As String = If(_cursore.LinguaRilevataTraduzione <> "", _cursore.LinguaRilevataTraduzione, linguaPagina)
        Return "Pagina tradotta" & If(da <> "", " " & WGTraduttore.DallaLingua(da), "") & " " & WGTraduttore.ALingua(_cursore.LinguaDestinazioneTraduzione)
    End Function

    ''' <summary>Traduce la pagina corrente. richiestaUtente: annuncia l'avvio e rilegge la riga corrente.</summary>
    Private Async Sub AttivaTraduzione(richiestaUtente As Boolean, riepilogo As String)
        If _traduzioneInPreparazione OrElse _cursore Is Nothing Then Return
        _traduzioneInPreparazione = True
        Try
            Dim navigazione As Integer = numeroNavigazione
            If richiestaUtente Then Annuncia("Traduzione della pagina in corso")
            Await _cursore.AssicuraBufferAsync()
            Dim lingua As String = Await LinguaPaginaAsync()
            If _chiusa OrElse navigazione <> numeroNavigazione Then Return
            Dim destinazione As String = WGTraduttore.LinguaDestinazione
            If lingua <> "" AndAlso WGTraduttore.StessaLingua(lingua, destinazione) AndAlso Not (WGTraduttore.RilevamentoAttivo AndAlso WGTraduttore.Motore = WGTraduttore.MotoreGoogle) Then
                Annuncia(ComponiRiepilogo(riepilogo, "La pagina è già " & WGTraduttore.ALingua(destinazione)))
                Return
            End If
            Dim errore As String = Await _cursore.AvviaTraduzioneAsync(OrigineTraduzione(lingua), destinazione)
            If _chiusa OrElse navigazione <> numeroNavigazione Then Return
            If errore <> "" Then
                AnnunciaSempre(ComponiRiepilogo(riepilogo, "Traduzione non riuscita: " & errore))
                Return
            End If
            WGTraduttore.RipristinaLinguaLettura()
            _hostTradotto = HostPagina
            Dim testo As String = DescriviTraduzione(lingua)
            Dim riga As RigaVirtuale = _cursore.RigaCorrente
            If riepilogo = "" AndAlso riga IsNot Nothing Then testo &= ". " & riga.CostruisciAnnuncio(False)
            Annuncia(ComponiRiepilogo(riepilogo, testo))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AttivaTraduzione: " & ex.ToString())
        Finally
            _traduzioneInPreparazione = False
        End Try
    End Sub

    Private Shared Function ComponiRiepilogo(riepilogo As String, aggiunta As String) As String
        If String.IsNullOrWhiteSpace(riepilogo) Then Return aggiunta
        Return riepilogo & ". " & aggiunta
    End Function

    ' Chiede se tradurre il sito; il riepilogo della pagina viene letto dopo la risposta.
    Private Sub ChiediTraduzioneSito(riepilogo As String)
        Try
            Dim host As String = HostPagina
            Dim lingua As String = _linguaPaginaTraduzione
            Dim destinazione As String = WGTraduttore.LinguaDestinazione
            Dim risposto As Boolean = False
            Dim rispondi As Action(Of Action) = Sub(azione As Action)
                                                    risposto = True
                                                    _contenitore.SegnaRitornoDaMenu()
                                                    Try
                                                        azione()
                                                    Catch exAzione As Exception
                                                        log.ScriviLog("WGSchedaBrowser.ChiediTraduzioneSito risposta: " & exAzione.Message)
                                                    End Try
                                                End Sub
            Dim menu As New WGMenu()
            menu.aggiungi("Traduci questa pagina", Sub(s, e) rispondi(Sub() AttivaTraduzione(False, riepilogo)))
            menu.aggiungi("Traduci sempre le pagine di " & host, Sub(s, e) rispondi(Sub()
                                                                                          WGImpostazioniBrowser.ImpostaSitoTradottoSempre(host, True)
                                                                                          AttivaTraduzione(False, riepilogo)
                                                                                      End Sub))
            menu.aggiungi("Non tradurre", Sub(s, e) rispondi(Sub()
                                                                   WGTraduttore.ApplicaLinguaLettura(lingua)
                                                                   Annuncia(riepilogo)
                                                               End Sub))
            menu.aggiungi("Non tradurre mai le pagine di " & host, Sub(s, e) rispondi(Sub()
                                                                                            WGImpostazioniBrowser.ImpostaSitoNonTradotto(host, True)
                                                                                            WGTraduttore.ApplicaLinguaLettura(lingua)
                                                                                            MessaggioBrowser(ComponiRiepilogo(riepilogo, "Le pagine di " & host & " non verranno tradotte"))
                                                                                        End Sub))
            AddHandler menu.abbandonato, Sub(s, e)
                                             If risposto Then Return
                                             _contenitore.SegnaRitornoDaMenu()
                                             SuUI(Sub() Annuncia(riepilogo))
                                         End Sub
            menu.avvia("Pagina " & WGTraduttore.ALingua(lingua) & ": " & Titolo & ". Tradurla " & WGTraduttore.ALingua(destinazione) & "?", True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiediTraduzioneSito: " & ex.ToString())
            Annuncia(riepilogo)
        End Try
    End Sub

End Class
