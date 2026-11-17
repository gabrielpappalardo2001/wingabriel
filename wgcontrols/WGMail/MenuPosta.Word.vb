Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Windows.Forms
Imports wgcontrols
Imports System.Text.StringBuilder
Partial Public Class MenuPosta

    Private _corpoHtmlComposizione As String = ""
    Private _corpoTestoComposizione As String = ""
    Private _immaginiComposizione As New List(Of ImmagineInlineMessaggio)
    Private _identificativoBozzaServer As String = ""
    Private _bozzaSalvata As Boolean = False
    Private _erroreBozza As String = ""

    Private _forzaLetturaTestuale As Boolean = False

    Friend Function LetturaMessaggiConWord() As Boolean
        Try
            Dim impostazioni As New WGImpostazioniComuni()
            Return impostazioni.PostaLetturaConWord
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LetturaMessaggiConWord: " & ex.Message)
            Return False
        End Try
    End Function

    Friend Function ScritturaMessaggiConWord() As Boolean
        Try
            Dim impostazioni As New WGImpostazioniComuni()
            Return impostazioni.PostaScritturaConWord
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ScritturaMessaggiConWord: " & ex.Message)
            Return True
        End Try
    End Function

    Friend Sub AzzeraForzaturaLettura()
        Try
            _forzaLetturaTestuale = False
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AzzeraForzaturaLettura: " & ex.Message)
        End Try
    End Sub

    Friend Function LetturaTestualeForzata() As Boolean
        Try
            Return _forzaLetturaTestuale
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LetturaTestualeForzata: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub OpzioneApriConEditorTestuale(sender As Object, e As EventArgs)
        Try
            _forzaLetturaTestuale = True
            ChiudiCampoMessaggio()
            MostraCorpoMessaggio()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneApriConEditorTestuale: " & ex.Message)
        End Try
    End Sub

    Private Function CartellaBozzeLocali() As String
        Try
            Dim cartella As String = ""
            Try
                cartella = WGDb.WGDatabase.CartellaDati(Path.Combine("posta", "bozze"))
            Catch exConfigurata As Exception
                log.ScriviLog("MenuPosta.CartellaBozzeLocali (cartella configurata): " & exConfigurata.Message)
                cartella = ""
            End Try
            If String.IsNullOrWhiteSpace(cartella) Then
                cartella = Path.Combine(Application.StartupPath, "dati", "posta", "bozze")
            End If
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Return cartella
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaBozzeLocali: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function PercorsoBozzaLocale() As String
        Try
            Dim cartella As String = CartellaBozzeLocali()
            If String.IsNullOrWhiteSpace(cartella) Then Return ""
            Dim chiave As String = If(_chiaveAccountCorrente, "")
            For Each carattere As Char In Path.GetInvalidFileNameChars()
                chiave = chiave.Replace(carattere, "_"c)
            Next
            If String.IsNullOrWhiteSpace(chiave) Then chiave = "predefinito"
            Return Path.Combine(cartella, "composizione_" & chiave & ".docx")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PercorsoBozzaLocale: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub RipristinaCorpoWord(campi As Campi)
        Try
            If campi Is Nothing Then Return
            If Not ScritturaMessaggiConWord() Then Return

            If Not String.IsNullOrWhiteSpace(_htmlBozzaDaCaricare) Then
                campi.CaricaWordDaHtml("msg.corpo", _htmlBozzaDaCaricare, _immaginiBozzaDaCaricare, "Messaggio")
                _htmlBozzaDaCaricare = ""
                _immaginiBozzaDaCaricare = Nothing
                Return
            End If

            Dim percorso As String = PercorsoBozzaLocale()
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return
            campi.CaricaWordDaFile("msg.corpo", percorso)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RipristinaCorpoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub CatturaCorpoWord(campi As Campi)
        Try
            If campi Is Nothing Then Return

            If Not ScritturaMessaggiConWord() Then
                _composizioneCorpo = campi.RecuperaValore("msg.corpo")
                _corpoTestoComposizione = _composizioneCorpo
                _corpoHtmlComposizione = ""
                _immaginiComposizione = New List(Of ImmagineInlineMessaggio)
                Return
            End If

            Dim contenuto As WGContenutoPosta = campi.RecuperaContenutoPosta("msg.corpo")
            If contenuto Is Nothing Then Return

            _corpoHtmlComposizione = If(contenuto.Html, "")
            _corpoTestoComposizione = If(contenuto.Testo, "")
            _composizioneCorpo = _corpoTestoComposizione

            _immaginiComposizione = New List(Of ImmagineInlineMessaggio)
            If contenuto.Immagini IsNot Nothing Then
                For Each immagine As WGImmaginePosta In contenuto.Immagini
                    If immagine Is Nothing OrElse immagine.Dati Is Nothing OrElse immagine.Dati.Length = 0 Then Continue For
                    Dim inline As New ImmagineInlineMessaggio()
                    inline.Cid = immagine.Cid
                    inline.NomeFile = immagine.NomeFile
                    inline.TipoMime = immagine.TipoMime
                    inline.Dati = immagine.Dati
                    _immaginiComposizione.Add(inline)
                Next
            End If

            Try
                Dim percorso As String = PercorsoBozzaLocale()
                If Not String.IsNullOrWhiteSpace(percorso) Then campi.SalvaWordSuFile("msg.corpo", percorso)
            Catch exSalva As Exception
                log.ScriviLog("MenuPosta.CatturaCorpoWord (copia locale): " & exSalva.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CatturaCorpoWord: " & ex.Message)
        End Try
    End Sub

    Private Sub PulisciBozzaComposizione()
        Try
            _corpoHtmlComposizione = ""
            _corpoTestoComposizione = ""
            _immaginiComposizione = New List(Of ImmagineInlineMessaggio)
            _identificativoBozzaServer = ""
            _htmlBozzaDaCaricare = ""
            _immaginiBozzaDaCaricare = Nothing
            Try
                Dim percorso As String = PercorsoBozzaLocale()
                If Not String.IsNullOrWhiteSpace(percorso) AndAlso File.Exists(percorso) Then File.Delete(percorso)
            Catch exFile As Exception
                log.ScriviLog("MenuPosta.PulisciBozzaComposizione (file): " & exFile.Message)
            End Try
        Catch ex As Exception
            log.ScriviLog("MenuPosta.PulisciBozzaComposizione: " & ex.Message)
        End Try
    End Sub

    Private Function CostruisciContenutoInvio() As ContenutoMessaggio
        Dim contenuto As New ContenutoMessaggio()
        Try
            contenuto.Destinatari = If(_invioDestinatari, New List(Of String))
            contenuto.Cc = If(_invioCc, New List(Of String))
            contenuto.Ccn = If(_invioCcn, New List(Of String))
            contenuto.Oggetto = If(_invioOggetto, "")
            contenuto.CorpoHtml = If(_corpoHtmlComposizione, "")
            contenuto.CorpoTesto = If(_invioCorpo, "")
            contenuto.ImmaginiInline = If(_immaginiComposizione, New List(Of ImmagineInlineMessaggio))
            contenuto.PercorsiAllegati = If(_invioAllegati, New List(Of String))
            contenuto.InRispostaA = If(_composizioneInRispostaA, "")
            contenuto.Riferimenti = If(_composizioneRiferimenti, "")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CostruisciContenutoInvio: " & ex.Message)
        End Try
        Return contenuto
    End Function

    Private Sub RegistraAzioniUscitaComposizione(campi As Campi)
        Try
            If campi Is Nothing Then Return
            campi.SvuotaAzioniUscita()
            campi.EtichettaAnnullaUscita = "Continua a scrivere"
            campi.TitoloMenuUscita = "Cosa vuoi fare con il messaggio?"
            campi.AggiungiAzioneUscita("Invia il messaggio", AddressOf AzioneUscitaInvia)
            campi.AggiungiAzioneUscita("Salva nelle bozze", AddressOf AzioneUscitaSalvaBozza)
            campi.AggiungiAzioneUscita("Elimina il messaggio", AddressOf AzioneUscitaElimina)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.RegistraAzioniUscitaComposizione: " & ex.Message)
        End Try
    End Sub

    Private Sub AzioneUscitaInvia(sender As Object, e As EventArgs)
        Try
            SalvaDatiComposizioneDaCampi()
            If _campiComposizione IsNot Nothing Then _campiComposizione.Chiudi()
            _campiComposizione = Nothing
            DifferisciSuInterfaccia(AddressOf AvviaInvioMessaggio)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AzioneUscitaInvia: " & ex.Message)
        End Try
    End Sub

    Private Sub AzioneUscitaSalvaBozza(sender As Object, e As EventArgs)
        Try
            SalvaDatiComposizioneDaCampi()
            If _campiComposizione IsNot Nothing Then _campiComposizione.Chiudi()
            _campiComposizione = Nothing
            DifferisciSuInterfaccia(AddressOf AvviaSalvataggioBozza)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AzioneUscitaSalvaBozza: " & ex.Message)
        End Try
    End Sub

    Private Sub AzioneUscitaElimina(sender As Object, e As EventArgs)
        Try
            If _campiComposizione IsNot Nothing Then _campiComposizione.Chiudi()
            _campiComposizione = Nothing
            DifferisciSuInterfaccia(AddressOf MostraConfermaEliminazione)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AzioneUscitaElimina: " & ex.Message)
        End Try
    End Sub

    Private Sub DifferisciSuInterfaccia(azione As MethodInvoker)
        Try
            If azione Is Nothing Then Return
            If _marshal Is Nothing OrElse _marshal.IsDisposed OrElse Not _marshal.IsHandleCreated Then
                azione.Invoke()
                Return
            End If
            _marshal.BeginInvoke(azione)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.DifferisciSuInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraConfermaEliminazione()
        Try
            _menuEliminaBozza = New WGMenu()
            _menuEliminaBozza.aggiungi("Si, elimina il messaggio", AddressOf ConfermaEliminaComposizione)
            _menuEliminaBozza.aggiungi("No, torna indietro", AddressOf AnnullaEliminaComposizione)
            _menuEliminaBozza.avvia("Vuoi eliminare il messaggio senza inviarlo?", True)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.MostraConfermaEliminazione: " & ex.Message)
        End Try
    End Sub

    Private _menuEliminaBozza As WGMenu

    Private Sub ConfermaEliminaComposizione(sender As Object, e As EventArgs)
        Try
            _menuEliminaBozza = Nothing
            Dim identificativo As String = _identificativoBozzaServer
            PulisciBozzaComposizione()
            _allegatiComposizione = New List(Of String)
            _composizioneDestinatari = ""
            _composizioneCc = ""
            _composizioneCcn = ""
            _composizioneOggetto = ""
            _composizioneCorpo = ""
            If Not String.IsNullOrWhiteSpace(identificativo) Then
                Try
                    _posta.EliminaBozzaSulServer(_chiaveAccountCorrente, identificativo)
                Catch exServer As Exception
                    log.ScriviLog("MenuPosta.ConfermaEliminaComposizione (server): " & exServer.Message)
                End Try
            End If
            MostraMessaggio("Messaggio eliminato.")
        Catch ex As Exception
            log.ScriviLog("MenuPosta.ConfermaEliminaComposizione: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnullaEliminaComposizione(sender As Object, e As EventArgs)
        Try
            _menuEliminaBozza = Nothing
            ApriComposizioneDettagli()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AnnullaEliminaComposizione: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaSalvataggioBozza()
        Try
            If String.IsNullOrWhiteSpace(_chiaveAccountCorrente) Then
                MostraMessaggio("Nessun account selezionato: la bozza resta solo sul computer.")
                Return
            End If
            MostraAttesa("Salvataggio della bozza in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroSalvataggioBozza))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaSalvataggioBozza: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroSalvataggioBozza()
        Try
            Dim contenuto As New ContenutoMessaggio()
            Dim destinatari As New List(Of String)
            destinatari.AddRange(SpezzaIndirizzi(_composizioneDestinatari))
            destinatari.AddRange(SpezzaListaIndirizzi(_composizioneDestinatariMulti))
            contenuto.Destinatari = destinatari
            contenuto.Cc = SpezzaListaIndirizzi(_composizioneCcLista)
            contenuto.Ccn = SpezzaListaIndirizzi(_composizioneCcnLista)
            contenuto.Oggetto = If(_composizioneOggetto, "")
            contenuto.CorpoHtml = If(_corpoHtmlComposizione, "")
            contenuto.CorpoTesto = If(_corpoTestoComposizione, "")
            contenuto.ImmaginiInline = If(_immaginiComposizione, New List(Of ImmagineInlineMessaggio))
            contenuto.PercorsiAllegati = New List(Of String)(_allegatiComposizione)

            Dim nuovo As String = _posta.SalvaBozzaSulServer(_chiaveAccountCorrente, contenuto, _identificativoBozzaServer)
            _bozzaSalvata = Not String.IsNullOrWhiteSpace(nuovo)
            If _bozzaSalvata Then _identificativoBozzaServer = nuovo
            _erroreBozza = ""
            If Not _bozzaSalvata Then
                Try
                    _erroreBozza = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.LavoroSalvataggioBozza (errore): " & exErrore.Message)
                End Try
            End If
        Catch ex As Exception
            _bozzaSalvata = False
            _erroreBozza = ex.Message
        Finally
            EseguiSuInterfaccia(AddressOf CompletaSalvataggioBozza)
        End Try
    End Sub

    Private Sub CompletaSalvataggioBozza()
        Try
            ChiudiAttesa()
            If _bozzaSalvata Then
                MostraMessaggio("Bozza salvata nelle bozze del server.")
            Else
                Dim descrizione As String = _erroreBozza
                If String.IsNullOrWhiteSpace(descrizione) Then descrizione = "Salvataggio della bozza non riuscito."
                MostraMessaggio(descrizione & " Il messaggio resta salvato sul computer.")
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaSalvataggioBozza: " & ex.Message)
        End Try
    End Sub

    Private _htmlBozzaDaCaricare As String = ""
    Private _immaginiBozzaDaCaricare As Dictionary(Of String, Byte()) = Nothing
    Private _bozzaRecuperata As BozzaRecuperata = Nothing
    Private _campiLettura As Campi
    Private _htmlLetturaDaCaricare As String = ""
    Private _immaginiLetturaDaCaricare As Dictionary(Of String, Byte()) = Nothing
    Private _titoloLettura As String = ""

    Private Function CartellaCorrenteSonoLeBozze() As Boolean
        Try
            Dim p As String = If(_cartellaCorrente, "").ToLowerInvariant()
            If p.Contains("draft") OrElse p.Contains("bozz") Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CartellaCorrenteSonoLeBozze: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub OpzioneRiprendiBozza(sender As Object, e As EventArgs)
        Try
            If _messaggioCorrente Is Nothing Then Return
            ChiudiCampoMessaggio()
            MostraAttesa("Recupero della bozza in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroRecuperoBozza))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneRiprendiBozza: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroRecuperoBozza()
        Try
            _bozzaRecuperata = _posta.RecuperaMessaggioGrezzo(_chiaveAccountCorrente, PercorsoCartellaPerAccount(_chiaveAccountCorrente), _messaggioCorrente.Id)
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroRecuperoBozza: " & ex.Message)
            _bozzaRecuperata = Nothing
        Finally
            EseguiSuInterfaccia(AddressOf CompletaRecuperoBozza)
        End Try
    End Sub

    Private Sub CompletaRecuperoBozza()
        Try
            ChiudiAttesa()
            Dim bozza As BozzaRecuperata = _bozzaRecuperata
            _bozzaRecuperata = Nothing

            If bozza Is Nothing Then
                Dim descrizione As String = ""
                Try
                    descrizione = _posta.UltimoErrore
                Catch exErrore As Exception
                    log.ScriviLog("MenuPosta.CompletaRecuperoBozza (errore): " & exErrore.Message)
                End Try
                MostraMessaggio(If(String.IsNullOrWhiteSpace(descrizione), "Bozza non recuperabile.", descrizione))
                Return
            End If

            PulisciBozzaComposizione()

            _composizioneDestinatari = ""
            _composizioneDestinatariMulti = New List(Of String)
            If bozza.Destinatari IsNot Nothing AndAlso bozza.Destinatari.Count > 0 Then
                _composizioneDestinatari = bozza.Destinatari(0)
                For i As Integer = 1 To bozza.Destinatari.Count - 1
                    _composizioneDestinatariMulti.Add(bozza.Destinatari(i))
                Next
            End If

            _composizioneCcLista = If(bozza.Cc, New List(Of String))
            _composizioneCcnLista = If(bozza.Ccn, New List(Of String))
            _composizioneOggetto = If(bozza.Oggetto, "")
            _allegatiComposizione = If(bozza.PercorsiAllegati, New List(Of String))

            _identificativoBozzaServer = If(bozza.Identificativo, "")
            _htmlBozzaDaCaricare = If(bozza.CorpoHtml, "")
            _immaginiBozzaDaCaricare = bozza.ImmaginiInline
            _corpoTestoComposizione = If(bozza.CorpoTesto, "")
            If String.IsNullOrWhiteSpace(_corpoTestoComposizione) AndAlso Not String.IsNullOrWhiteSpace(_htmlBozzaDaCaricare) Then
                _corpoTestoComposizione = CLSMail.HtmlVersoTesto(_htmlBozzaDaCaricare)
            End If
            _composizioneCorpo = _corpoTestoComposizione

            If String.IsNullOrWhiteSpace(_composizioneDestinatari) Then
                ApriComposizioneDestinatario()
            Else
                ApriComposizioneDettagli()
            End If
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaRecuperoBozza: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneApriInModalitaDocumento(sender As Object, e As EventArgs)
        Try
            _forzaLetturaTestuale = False
            ChiudiCampoMessaggio()
            AvviaLetturaDocumento()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneApriInModalitaDocumento: " & ex.Message)
        End Try
    End Sub

    Friend Sub AvviaLetturaDocumento()
        Try
            If _messaggioCorrente Is Nothing Then Return
            _titoloLettura = If(String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto), "Messaggio", _messaggioCorrente.Oggetto)
            MostraAttesa("Preparazione del documento in corso, attendere")
            AvviaLavoro(New Action(AddressOf LavoroLetturaDocumento))
        Catch ex As Exception
            log.ScriviLog("MenuPosta.AvviaLetturaDocumento: " & ex.Message)
        End Try
    End Sub

    Private Function GeneraHeaderMessaggioDaVisualizzare() As String
        Try
            If _messaggioCorrente Is Nothing Then Return ""

            Dim header As New System.Text.StringBuilder()
            header.Append("<div style='border-bottom: 1px solid #999; padding-bottom: 10px; margin-bottom: 20px; font-family: Arial; font-size: 10pt;'>")
            
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Mittente) Then
                header.Append("<p style='margin: 5px 0;'><strong>Da:</strong> " & System.Web.HttpUtility.HtmlEncode(_messaggioCorrente.Mittente) & "</p>")
            End If
            
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Destinatari) Then
                header.Append("<p style='margin: 5px 0;'><strong>A:</strong> " & System.Web.HttpUtility.HtmlEncode(_messaggioCorrente.Destinatari) & "</p>")
            End If
            
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Cc) Then
                header.Append("<p style='margin: 5px 0;'><strong>Cc:</strong> " & System.Web.HttpUtility.HtmlEncode(_messaggioCorrente.Cc) & "</p>")
            End If
            
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Ccn) Then
                header.Append("<p style='margin: 5px 0;'><strong>Bcc:</strong> " & System.Web.HttpUtility.HtmlEncode(_messaggioCorrente.Ccn) & "</p>")
            End If
            
            If Not String.IsNullOrWhiteSpace(_messaggioCorrente.Oggetto) Then
                header.Append("<p style='margin: 5px 0;'><strong>Oggetto:</strong> " & System.Web.HttpUtility.HtmlEncode(_messaggioCorrente.Oggetto) & "</p>")
            End If
            
            header.Append("</div>")
            Return header.ToString()
        Catch ex As Exception
            log.ScriviLog("MenuPosta.GeneraHeaderMessaggioDaVisualizzare: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub LavoroLetturaDocumento()
        Try
            _htmlLetturaDaCaricare = ""
            _immaginiLetturaDaCaricare = Nothing
            Dim htmlLocale As String = If(_messaggioCorrente.CorpoHtml, "")
            Dim servonoImmagini As Boolean = htmlLocale.IndexOf("cid:", StringComparison.OrdinalIgnoreCase) >= 0

            Dim recuperato As BozzaRecuperata = Nothing
            If servonoImmagini OrElse String.IsNullOrWhiteSpace(htmlLocale) Then
                Try
                    recuperato = _posta.RecuperaMessaggioGrezzo(_chiaveAccountCorrente, PercorsoCartellaPerAccount(_chiaveAccountCorrente), _messaggioCorrente.Id)
                Catch exServer As Exception
                    log.ScriviLog("MenuPosta.LavoroLetturaDocumento (server): " & exServer.Message)
                End Try
            End If

            If recuperato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(recuperato.CorpoHtml) Then
                _htmlLetturaDaCaricare = recuperato.CorpoHtml
                _immaginiLetturaDaCaricare = recuperato.ImmaginiInline
            ElseIf Not String.IsNullOrWhiteSpace(_messaggioCorrente.CorpoHtml) Then
                _htmlLetturaDaCaricare = _messaggioCorrente.CorpoHtml
            Else
                _htmlLetturaDaCaricare = CLSMail.TestoSempliceInHtml(If(_messaggioCorrente.CorpoTesto, ""))
            End If
            
            _htmlLetturaDaCaricare = GeneraHeaderMessaggioDaVisualizzare() & _htmlLetturaDaCaricare
        Catch ex As Exception
            log.ScriviLog("MenuPosta.LavoroLetturaDocumento: " & ex.Message)
        Finally
            EseguiSuInterfaccia(AddressOf CompletaLetturaDocumento)
        End Try
    End Sub

    Private Sub CompletaLetturaDocumento()
        Try
            ChiudiAttesa()
            If String.IsNullOrWhiteSpace(_htmlLetturaDaCaricare) Then
                MostraMessaggio("Il messaggio non ha un contenuto formattato da mostrare.")
                Return
            End If

            _campiLettura = Nothing
            _campiLettura = New Campi()
            _campiLettura.SalvaSuEscape = False
            _campiLettura.AggiungiWord(_titoloLettura, "msg.lettura", False, True)
            _campiLettura.AggiungiOpzione("Stampa", AddressOf OpzioneStampaMessaggio)
            _campiLettura.AggiungiOpzione("Rispondi", AddressOf OpzioneRispondi)
            _campiLettura.AggiungiOpzione("Rispondi a tutti", AddressOf OpzioneRispondiTutti)
            _campiLettura.AggiungiOpzione("Inoltra", AddressOf OpzioneInoltra)
            AggiungiOpzioniAiLettura(_campiLettura)
            _campiLettura.AggiungiOpzione("Info messaggio", AddressOf OpzioneInfoMessaggio)
            If _messaggioCorrente IsNot Nothing AndAlso _messaggioCorrente.Allegati IsNot Nothing AndAlso _messaggioCorrente.Allegati.Count > 0 Then
                _campiLettura.AggiungiOpzione("Allegati", AddressOf OpzioneAllegatiMessaggio)
            End If
            If CartellaCorrenteSonoLeBozze() Then
                _campiLettura.AggiungiOpzione("Riprendi la scrittura", AddressOf OpzioneRiprendiBozza)
            End If
            _campiLettura.AggiungiOpzione("Apri con l'editor testuale", AddressOf OpzioneApriConEditorTestuale)
            _campiLettura.AggiungiOpzione("Elimina messaggio", AddressOf OpzioneEliminaMessaggioAperto)
            AddHandler _campiLettura.Chiusura, AddressOf CampiLetturaChiusura
            AddHandler _campiLettura.Fine, AddressOf CampiLetturaChiusura
            _campiLettura.Avvia()
            _campiLettura.CaricaWordDaHtml("msg.lettura", _htmlLetturaDaCaricare, _immaginiLetturaDaCaricare, _titoloLettura)

            _htmlLetturaDaCaricare = ""
            _immaginiLetturaDaCaricare = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CompletaLetturaDocumento: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneChiudiLettura(sender As Object, e As EventArgs)
        Try
            _campiLettura = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.OpzioneChiudiLettura: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiLetturaChiusura(sender As Object, e As EventArgs)
        Try
            _campiLettura = Nothing
        Catch ex As Exception
            log.ScriviLog("MenuPosta.CampiLetturaChiusura: " & ex.Message)
        End Try
    End Sub

End Class