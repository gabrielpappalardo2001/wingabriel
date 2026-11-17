Imports System
Imports System.Collections.Generic
Imports System.Text.RegularExpressions
Imports System.Threading
Imports wgcontrols

' I messaggi scritti con la grafica aperti nel browser di WinGabriel ridotto a lettore: una pagina sola,
' senza schede né impostazioni, con in cima al menu delle opzioni le azioni del messaggio (rispondi,
' inoltra, allegati...). Si sceglie in Impostazioni, Posta, Apertura dei messaggi; senza il browser il
' messaggio si legge come sempre nel campo di testo.
Partial Public Class MenuPosta

    Private _lettoreHtml As IWGChiudibile = Nothing
    Private _htmlLettoreDaCaricare As String = ""
    Private _aperturaLettore As Integer = 0

    Friend Function LetturaMessaggiNelBrowser() As Boolean
        Try
            Return New WGImpostazioniComuni().PostaLetturaNelBrowser
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LetturaMessaggiNelBrowser: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AvviaLetturaBrowser()
        Try
            If _messaggioCorrente Is Nothing Then Return
            MostraAttesa("Preparazione del messaggio in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroLetturaBrowser))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaLetturaBrowser: " & ex.Message)
        End Try
    End Sub

    ' Come per la lettura in modalità documento: le immagini incorporate (cid:) vanno prese dal server.
    Private Sub LavoroLetturaBrowser()
        Try
            _htmlLettoreDaCaricare = ""
            Dim html As String = If(_messaggioCorrente.CorpoHtml, "")
            Dim immagini As Dictionary(Of String, Byte()) = Nothing
            If html.IndexOf("cid:", StringComparison.OrdinalIgnoreCase) >= 0 OrElse String.IsNullOrWhiteSpace(html) Then
                Try
                    Dim recuperato As BozzaRecuperata = _posta.RecuperaMessaggioGrezzo(_chiaveAccountCorrente, PercorsoCartellaPerAccount(_chiaveAccountCorrente), _messaggioCorrente.Id)
                    If recuperato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(recuperato.CorpoHtml) Then
                        html = recuperato.CorpoHtml
                        immagini = recuperato.ImmaginiInline
                    End If
                Catch exServer As Exception
                    log.ScriviLog("MenuPosta.LavoroLetturaBrowser (server): " & exServer.Message)
                End Try
            End If
            If String.IsNullOrWhiteSpace(html) Then html = CLSMail.TestoSempliceInHtml(If(_messaggioCorrente.CorpoTesto, ""))
            _htmlLettoreDaCaricare = DocumentoPerLettore(html, immagini)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroLetturaBrowser: " & ex.Message)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaLetturaBrowser)
        End Try
    End Sub

    Private Sub CompletaLetturaBrowser()
        Try
            ChiudiAttesa()
            Dim documento As String = _htmlLettoreDaCaricare
            _htmlLettoreDaCaricare = ""
            If _messaggioCorrente Is Nothing Then Return
            ChiudiLettoreHtml()
            If Not String.IsNullOrWhiteSpace(documento) Then
                Dim numero As Integer = Interlocked.Increment(_aperturaLettore)
                Dim richiesta As New WGRichiestaLettoreHtml()
                richiesta.Titolo = If(String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto), "Messaggio", _messaggioCorrente.Oggetto)
                richiesta.Html = documento
                richiesta.Opzioni.AddRange(OpzioniLettore())
                richiesta.Chiusura = Sub()
                                         If _aperturaLettore = numero Then _lettoreHtml = Nothing
                                     End Sub
                _lettoreHtml = WGLettoreHtml.Mostra(richiesta)
            End If
            If _lettoreHtml Is Nothing Then
                ' Senza il browser il messaggio si legge nel campo di testo, come sempre.
                _forzaLetturaTestuale = True
                MostraCorpoMessaggio()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaLetturaBrowser: " & ex.Message)
        End Try
    End Sub

    ' Le stesse azioni del messaggio aperto nel campo di testo.
    Private Function OpzioniLettore() As List(Of Tuple(Of String, Action))
        Dim voci As New List(Of Tuple(Of String, Action))()
        Try
            Dim aggiungi As Action(Of String, EventHandler) = Sub(nome As String, gestore As EventHandler)
                                                                  voci.Add(Tuple.Create(Of String, Action)(nome, Sub() gestore(Me, EventArgs.Empty)))
                                                              End Sub
            aggiungi("Rispondi", AddressOf OpzioneRispondi)
            aggiungi("Rispondi a tutti", AddressOf OpzioneRispondiTutti)
            aggiungi("Inoltra", AddressOf OpzioneInoltra)
            If IntelligenzaArtificialeDisponibile() Then
                aggiungi("Riassumi con l'intelligenza artificiale", AddressOf OpzioneRiassumiAi)
                aggiungi("Rispondi con l'intelligenza artificiale", AddressOf OpzioneRispondiAi)
                aggiungi("Rispondi a tutti con l'intelligenza artificiale", AddressOf OpzioneRispondiTuttiAi)
            End If
            aggiungi("Info messaggio", AddressOf OpzioneInfoMessaggio)
            If _messaggioCorrente IsNot Nothing AndAlso _messaggioCorrente.Allegati IsNot Nothing AndAlso _messaggioCorrente.Allegati.Count > 0 Then
                aggiungi("Allegati", AddressOf OpzioneAllegatiMessaggio)
            End If
            aggiungi("Stampa", AddressOf OpzioneStampaMessaggio)
            If CartellaCorrenteSonoLeBozze() Then aggiungi("Riprendi la scrittura", AddressOf OpzioneRiprendiBozza)
            aggiungi("Apri con l'editor testuale", AddressOf OpzioneApriConEditorTestuale)
            aggiungi("Apri in modalità documento", AddressOf OpzioneApriInModalitaDocumento)
            aggiungi("Elimina messaggio", AddressOf OpzioneEliminaMessaggioAperto)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioniLettore: " & ex.Message)
        End Try
        Return voci
    End Function

    Private Sub ChiudiLettoreHtml()
        Try
            Dim lettore As IWGChiudibile = _lettoreHtml
            _lettoreHtml = Nothing
            Interlocked.Increment(_aperturaLettore)
            If lettore IsNot Nothing Then lettore.RichiediChiusura()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ChiudiLettoreHtml: " & ex.Message)
        End Try
    End Sub

    ' Il documento da mostrare: intestazione del messaggio in cima, immagini incorporate come dati, niente
    ' script né ricariche automatiche (il lettore mostra, non esegue).
    ' Il tipo dell'immagine dai primi byte: le immagini incorporate arrivano senza.
    Private Shared Function TipoImmagine(dati As Byte()) As String
        If dati.Length >= 4 AndAlso dati(0) = &H89 AndAlso dati(1) = &H50 Then Return "image/png"
        If dati.Length >= 3 AndAlso dati(0) = &HFF AndAlso dati(1) = &HD8 Then Return "image/jpeg"
        If dati.Length >= 3 AndAlso dati(0) = &H47 AndAlso dati(1) = &H49 AndAlso dati(2) = &H46 Then Return "image/gif"
        If dati.Length >= 12 AndAlso dati(0) = &H52 AndAlso dati(1) = &H49 AndAlso dati(8) = &H57 AndAlso dati(9) = &H45 Then Return "image/webp"
        If dati.Length >= 2 AndAlso dati(0) = &H42 AndAlso dati(1) = &H4D Then Return "image/bmp"
        If dati.Length >= 1 AndAlso dati(0) = &H3C Then Return "image/svg+xml"
        Return "image/png"
    End Function

    Private Function DocumentoPerLettore(html As String, immagini As Dictionary(Of String, Byte())) As String
        Try
            Dim corpo As String = If(html, "")
            If immagini IsNot Nothing Then
                For Each immagine As KeyValuePair(Of String, Byte()) In immagini
                    If immagine.Value Is Nothing OrElse immagine.Value.Length = 0 OrElse String.IsNullOrWhiteSpace(immagine.Key) Then Continue For
                    Dim dati As String = "data:" & TipoImmagine(immagine.Value) & ";base64," & Convert.ToBase64String(immagine.Value)
                    Dim cid As String = immagine.Key.Trim().Trim("<"c, ">"c)
                    corpo = Regex.Replace(corpo, "cid:" & Regex.Escape(cid), dati.Replace("$", "$$"), RegexOptions.IgnoreCase)
                Next
            End If
            corpo = Regex.Replace(corpo, "<script\b[^>]*>[\s\S]*?</script\s*>", "", RegexOptions.IgnoreCase)
            corpo = Regex.Replace(corpo, "<script\b[^>]*>", "", RegexOptions.IgnoreCase)
            corpo = Regex.Replace(corpo, "<meta\b[^>]*http-equiv\s*=\s*[""']?refresh[^>]*>", "", RegexOptions.IgnoreCase)
            ' Gestori di eventi dentro i tag (onload, onclick...), tolti uno alla volta finché ce ne sono.
            Dim eventi As New Regex("(<[a-zA-Z][^>]*?)\s+on[a-zA-Z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)
            For giro As Integer = 1 To 20
                Dim senza As String = eventi.Replace(corpo, "$1")
                If senza = corpo Then Exit For
                corpo = senza
            Next

            Dim oggetto As String = If(_messaggioCorrente IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto), _messaggioCorrente.Oggetto, "Messaggio")
            Dim testa As String = "<meta charset=""utf-8""><title>" & System.Net.WebUtility.HtmlEncode(oggetto) & "</title>"
            Dim intestazione As String = GeneraHeaderMessaggioDaVisualizzare()
            Dim inizioCorpo As Match = Regex.Match(corpo, "<body\b[^>]*>", RegexOptions.IgnoreCase)
            If Not inizioCorpo.Success Then
                Return "<!DOCTYPE html><html><head>" & testa & "</head><body>" & intestazione & corpo & "</body></html>"
            End If
            corpo = corpo.Insert(inizioCorpo.Index + inizioCorpo.Length, intestazione)
            Dim inizioTesta As Match = Regex.Match(corpo, "<head\b[^>]*>", RegexOptions.IgnoreCase)
            If inizioTesta.Success AndAlso inizioTesta.Index < inizioCorpo.Index Then
                corpo = corpo.Insert(inizioTesta.Index + inizioTesta.Length, testa)
            Else
                corpo = corpo.Insert(inizioCorpo.Index, "<head>" & testa & "</head>")
            End If
            If Not corpo.TrimStart().StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) Then corpo = "<!DOCTYPE html>" & corpo
            Return corpo
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DocumentoPerLettore: " & ex.Message)
            Return ""
        End Try
    End Function

End Class
