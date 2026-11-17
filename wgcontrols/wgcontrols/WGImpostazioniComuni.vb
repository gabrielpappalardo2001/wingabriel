Imports System.Collections.Generic
Imports System.Diagnostics
Imports WGStampante

' Gestione delle preferenze utente, scelta dello screen reader, del browser e delle impostazioni di stampa.
Public Class WGImpostazioniComuni

    Private Shared _nomeProcessoScreenReaderAvviato As String = ""

    Private Shared Function PercorsoScreenReader(nome As String) As String
        Try
            Dim percorsi As New List(Of String)
            Select Case nome
                Case "JAWS"
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles%\Freedom Scientific\JAWS"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\Freedom Scientific\JAWS"))
                Case "NVDA"
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles%\NVDA"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\NVDA"))
                Case "KSR"
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles%\KSR ScreenReader"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\KSR ScreenReader"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%LocalAppData%\Programs\KSR ScreenReader"))
            End Select

            For Each cartellaBase As String In percorsi
                If System.IO.Directory.Exists(cartellaBase) Then
                    Dim sottocartelle() As String = System.IO.Directory.GetDirectories(cartellaBase)
                    Dim cartelleDaControllare As New List(Of String) From {cartellaBase}
                    cartelleDaControllare.AddRange(sottocartelle)
                    For Each cartella As String In cartelleDaControllare
                        Dim eseguibile As String = If(nome = "JAWS", "jfw.exe", If(nome = "KSR", "KSR.exe", "nvda.exe"))
                        Dim percorsoCompleto As String = System.IO.Path.Combine(cartella, eseguibile)
                        If System.IO.File.Exists(percorsoCompleto) Then Return percorsoCompleto
                    Next
                End If
            Next
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("PercorsoScreenReader: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function RilevaScreenReaderMigliore() As String
        If Not String.IsNullOrEmpty(PercorsoScreenReader("JAWS")) Then Return "JAWS"
        If Not String.IsNullOrEmpty(PercorsoScreenReader("NVDA")) Then Return "NVDA"
        If Not String.IsNullOrEmpty(PercorsoScreenReader("KSR")) Then Return "KSR"
        Return "Narrator"
    End Function

    Public Shared Sub AvviaScreenReaderPreferito()
        Try
            Dim impostazioni As New WGImpostazioniComuni()
            Dim scelta As String = impostazioni.ScreenReaderPreferito

            If scelta = "Automatico" Then
                scelta = RilevaScreenReaderMigliore()
                impostazioni.ScreenReaderRilevatoAutomaticamente = scelta
            ElseIf scelta <> "Narrator" AndAlso String.IsNullOrEmpty(PercorsoScreenReader(scelta)) Then
                scelta = RilevaScreenReaderMigliore()
            End If

            Select Case scelta
                Case "JAWS", "NVDA", "KSR"
                    Dim nomeProcesso As String = If(scelta = "JAWS", "jfw", If(scelta = "KSR", "KSR", "nvda"))
                    If Process.GetProcessesByName(nomeProcesso).Length = 0 Then
                        Dim percorsoEseguibile As String = PercorsoScreenReader(scelta)
                        If Not String.IsNullOrEmpty(percorsoEseguibile) Then
                            Try
                                Process.Start(New ProcessStartInfo(percorsoEseguibile) With {.UseShellExecute = True})
                                _nomeProcessoScreenReaderAvviato = nomeProcesso
                            Catch exAvvio As Exception
                                _logger.ScriviLog("AvviaScreenReaderPreferito " & scelta & ": " & exAvvio.Message)
                            End Try
                        End If
                    End If
                Case "Narrator"
                    If Process.GetProcessesByName("narrator").Length = 0 Then
                        Try
                            Dim percorsoNarrator As String = Environment.ExpandEnvironmentVariables("%SystemRoot%\System32\Narrator.exe")
                            Process.Start(New ProcessStartInfo(percorsoNarrator) With {.UseShellExecute = True})
                            _nomeProcessoScreenReaderAvviato = "narrator"
                        Catch exNarrator As Exception
                            _logger.ScriviLog("AvviaScreenReaderPreferito Narrator: " & exNarrator.Message)
                        End Try
                    End If
            End Select
        Catch ex As Exception
            _logger.ScriviLog("AvviaScreenReaderPreferito: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub DisattivaScreenReaderPreferitoSeAvviato()
        Try
            If String.IsNullOrEmpty(_nomeProcessoScreenReaderAvviato) Then Return
            Dim processi() As Process = Process.GetProcessesByName(_nomeProcessoScreenReaderAvviato)
            For Each proc As Process In processi
                Try
                    proc.CloseMainWindow()
                Catch exClose As Exception
                    _logger.ScriviLog("DisattivaScreenReaderPreferitoSeAvviato: " & exClose.Message)
                End Try
            Next
            _nomeProcessoScreenReaderAvviato = ""
        Catch ex As Exception
            _logger.ScriviLog("DisattivaScreenReaderPreferitoSeAvviato: " & ex.Message)
        End Try
    End Sub

    Private Shared Function PercorsoBrowser(nome As String) As String
        Try
            Dim percorsi As New List(Of String)
            Select Case nome
                Case "Chrome"
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles%\Google\Chrome\Application\chrome.exe"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%LocalAppData%\Google\Chrome\Application\chrome.exe"))
                Case "Firefox"
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles%\Mozilla Firefox\firefox.exe"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe"))
                Case "Edge"
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"))
                    percorsi.Add(Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"))
            End Select

            For Each percorsoCompleto As String In percorsi
                If System.IO.File.Exists(percorsoCompleto) Then Return percorsoCompleto
            Next
            Return ""
        Catch ex As Exception
            _logger.ScriviLog("PercorsoBrowser: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function RilevaBrowserMigliore() As String
        If Not String.IsNullOrEmpty(PercorsoBrowser("Chrome")) Then Return "Chrome"
        If Not String.IsNullOrEmpty(PercorsoBrowser("Firefox")) Then Return "Firefox"
        If Not String.IsNullOrEmpty(PercorsoBrowser("Edge")) Then Return "Edge"
        Return "Predefinito"
    End Function

    Public Const BrowserWinGabriel As String = "WinGabriel"

    Private Shared _gestoreBrowserWinGabriel As Action(Of String) = Nothing
    Private Shared _gestoreEmailWinGabriel As Action(Of String, String, String) = Nothing

    Public Shared Property GestoreBrowserWinGabriel As Action(Of String)
        Get
            Return _gestoreBrowserWinGabriel
        End Get
        Set(value As Action(Of String))
            _gestoreBrowserWinGabriel = value
        End Set
    End Property

    Public Shared Property GestoreEmailWinGabriel As Action(Of String, String, String)
        Get
            Return _gestoreEmailWinGabriel
        End Get
        Set(value As Action(Of String, String, String))
            _gestoreEmailWinGabriel = value
        End Set
    End Property

    Public Shared Function EIndirizzoWeb(url As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(url) Then Return False
            Dim testo As String = url.Trim().ToLowerInvariant()
            Return testo.StartsWith("http://") OrElse testo.StartsWith("https://") OrElse testo.StartsWith("www.")
        Catch ex As Exception
            _logger.ScriviLog("EIndirizzoWeb: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function EIndirizzoEmail(url As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(url) Then Return False
            Return url.Trim().StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            _logger.ScriviLog("EIndirizzoEmail: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function GestoreBrowserDaAssembly() As Action(Of String)
        Try
            Dim tipo As Type = Type.GetType("WGBrowser.WGBrowser, WGBrowser", False)
            If tipo Is Nothing Then Return Nothing
            Dim metodo As System.Reflection.MethodInfo = tipo.GetMethod("ApriBrowser", System.Reflection.BindingFlags.Public Or System.Reflection.BindingFlags.Static)
            If metodo Is Nothing Then Return Nothing
            Return Sub(indirizzo As String)
                       Try
                           metodo.Invoke(Nothing, New Object() {indirizzo})
                       Catch exInvoca As Exception
                           _logger.ScriviLog("GestoreBrowserDaAssembly invoca: " & exInvoca.ToString())
                       End Try
                   End Sub
        Catch ex As Exception
            _logger.ScriviLog("GestoreBrowserDaAssembly: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Sub EseguiSulThreadInterfaccia(azione As Action)
        Try
            If azione Is Nothing Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated AndAlso contenitore.InvokeRequired Then
                contenitore.BeginInvoke(azione)
            Else
                azione()
            End If
        Catch ex As Exception
            _logger.ScriviLog("EseguiSulThreadInterfaccia: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub MostraAvvisoLink(testo As String)
        EseguiSulThreadInterfaccia(Sub()
                                       Try
                                           Dim messaggio As New WGMessaggi()
                                           messaggio.Messaggio(testo)
                                           messaggio.ShowDialog()
                                       Catch exMessaggio As Exception
                                           _logger.ScriviLog("MostraAvvisoLink: " & exMessaggio.ToString())
                                       End Try
                                   End Sub)
    End Sub

    Public Shared Function ApriNelBrowserWinGabriel(url As String) As Boolean
        Try
            If Not EIndirizzoWeb(url) Then Return False
            Dim indirizzo As String = url.Trim()
            If indirizzo.StartsWith("www.", StringComparison.OrdinalIgnoreCase) Then indirizzo = "https://" & indirizzo
            Dim gestore As Action(Of String) = _gestoreBrowserWinGabriel
            If gestore Is Nothing Then gestore = GestoreBrowserDaAssembly()
            If gestore Is Nothing Then Return False
            EseguiSulThreadInterfaccia(Sub()
                                           Try
                                               gestore(indirizzo)
                                           Catch exApertura As Exception
                                               _logger.ScriviLog("ApriNelBrowserWinGabriel apertura: " & exApertura.ToString())
                                           End Try
                                       End Sub)
            Return True
        Catch ex As Exception
            _logger.ScriviLog("ApriNelBrowserWinGabriel: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function DecodificaParteEmail(valore As String) As String
        Try
            Return Uri.UnescapeDataString(If(valore, "")).Trim()
        Catch ex As Exception
            _logger.ScriviLog("DecodificaParteEmail: " & ex.Message)
            Return If(valore, "").Trim()
        End Try
    End Function

    Public Shared Function ApriEmailWinGabriel(indirizzoMailto As String) As Boolean
        Try
            If Not EIndirizzoEmail(indirizzoMailto) Then Return False
            Dim gestore As Action(Of String, String, String) = _gestoreEmailWinGabriel
            If gestore Is Nothing Then Return False
            Dim testo As String = indirizzoMailto.Trim().Substring(7)
            Dim destinatari As String = testo
            Dim parametri As String = ""
            Dim inizioParametri As Integer = testo.IndexOf("?"c)
            If inizioParametri >= 0 Then
                destinatari = testo.Substring(0, inizioParametri)
                parametri = testo.Substring(inizioParametri + 1)
            End If
            destinatari = DecodificaParteEmail(destinatari)
            Dim oggetto As String = ""
            Dim corpo As String = ""
            For Each coppia As String In parametri.Split(New Char() {"&"c}, StringSplitOptions.RemoveEmptyEntries)
                Dim uguale As Integer = coppia.IndexOf("="c)
                If uguale <= 0 Then Continue For
                Dim chiave As String = coppia.Substring(0, uguale).Trim().ToLowerInvariant()
                Dim valore As String = DecodificaParteEmail(coppia.Substring(uguale + 1))
                Select Case chiave
                    Case "subject"
                        oggetto = valore
                    Case "body"
                        corpo = valore
                    Case "to"
                        If Not String.IsNullOrWhiteSpace(valore) Then destinatari = If(String.IsNullOrWhiteSpace(destinatari), valore, destinatari & ", " & valore)
                End Select
            Next
            EseguiSulThreadInterfaccia(Sub()
                                           Try
                                               gestore(destinatari, oggetto, corpo)
                                           Catch exApertura As Exception
                                               _logger.ScriviLog("ApriEmailWinGabriel apertura: " & exApertura.ToString())
                                           End Try
                                       End Sub)
            Return True
        Catch ex As Exception
            _logger.ScriviLog("ApriEmailWinGabriel: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Shared Sub ApriLinkConBrowserPreferito(url As String)
        Try
            If String.IsNullOrWhiteSpace(url) Then Return
            Dim indirizzo As String = url.Trim()
            If EIndirizzoEmail(indirizzo) Then
                If ApriEmailWinGabriel(indirizzo) Then Return
                MostraAvvisoLink("La posta di WinGabriel non è disponibile, quindi il messaggio non è stato preparato.")
                Return
            End If
            If Not EIndirizzoWeb(indirizzo) Then
                MostraAvvisoLink("Questo tipo di link non si può aprire in WinGabriel.")
                Return
            End If
            If indirizzo.StartsWith("www.", StringComparison.OrdinalIgnoreCase) Then indirizzo = "https://" & indirizzo
            ' L'indirizzo si ricostruisce: spazi e virgolette diventano %20 e %22, così un link (che può venire da una pagina o da una
            ' mail) non può aggiungere opzioni alla riga di comando del browser.
            Dim uriValido As Uri = Nothing
            If Not Uri.TryCreate(indirizzo, UriKind.Absolute, uriValido) OrElse
               (uriValido.Scheme <> Uri.UriSchemeHttp AndAlso uriValido.Scheme <> Uri.UriSchemeHttps) Then
                MostraAvvisoLink("Questo tipo di link non si può aprire in WinGabriel.")
                Return
            End If
            indirizzo = uriValido.AbsoluteUri

            Dim impostazioni As New WGImpostazioniComuni()
            Dim scelta As String = impostazioni.BrowserPreferito

            If String.Equals(scelta, BrowserWinGabriel, StringComparison.OrdinalIgnoreCase) Then
                If ApriNelBrowserWinGabriel(indirizzo) Then Return
                MostraAvvisoLink("Il browser di WinGabriel non è disponibile, quindi il link non è stato aperto. Dalle impostazioni, alla voce Browser, si può scegliere un altro browser per i link.")
                Return
            End If

            AvviaScreenReaderPreferito()

            If scelta <> "Predefinito" AndAlso String.IsNullOrEmpty(PercorsoBrowser(scelta)) Then
                scelta = RilevaBrowserMigliore()
            End If

            If scelta = "Predefinito" Then
                Process.Start(New ProcessStartInfo(indirizzo) With {.UseShellExecute = True})
            Else
                Dim percorsoBrowserScelto As String = PercorsoBrowser(scelta)
                If String.IsNullOrEmpty(percorsoBrowserScelto) Then
                    Process.Start(New ProcessStartInfo(indirizzo) With {.UseShellExecute = True})
                Else
                    Process.Start(New ProcessStartInfo(percorsoBrowserScelto, """" & indirizzo & """") With {.UseShellExecute = True})
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("ApriLinkConBrowserPreferito: " & ex.ToString())
        End Try
    End Sub

    Private Shared ReadOnly _logger As New Logger()
    Private ReadOnly _config As WGConfig

    Private Shared ReadOnly _defaultImpostazioni As New Dictionary(Of String, String) From {
        {"echo_digitazione", "3"},
        {"leggi_password", "0"},
        {"abilita_correttore", "1"},
        {"lettura_punteggiatura_lettura", "1"},
        {"livello_punteggiatura_lettura", "3"},
        {"livello_punteggiatura_scrittura", "3"},
        {"modalita_lettura_numeri", "intero"},
        {"elimina_nota_con_parola", "1"},
        {"modifica_note_nel_documento", "1"},
        {"screen_reader_preferito", "Automatico"},
        {"browser_preferito", BrowserWinGabriel},
        {"lettura_punteggiatura_scrittura", "1"},
        {"posta_lettura_word", "0"},
        {"posta_scrittura_word", "1"},
        {"stampante_predefinita", ""},
        {"stampa_margine_mm", "15"},
        {"stampa_carattere_nome", "Arial"},
        {"stampa_carattere_dimensione", "11"},
        {"stampa_copie", "1"},
        {"stampa_orientamento", "verticale"},
        {"stampa_fronte_retro", "una_facciata"},
        {"stampa_colore", "colore"},
        {"stampa_qualita", "normale"},
        {"mp3_voce", "0"},
        {"mp3_velocita", "50"},
        {"mp3_volume", "80"}
    }

    Public Sub New()
        _config = New WGConfig()
    End Sub

    Private Function ImpostazionePredefinita(chiave As String) As String
        Try
            If _defaultImpostazioni.ContainsKey(chiave) Then Return _defaultImpostazioni(chiave)
            Return ""
        Catch ex As Exception
            _logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Private Function LeggiImpostazioneStringa(chiave As String) As String
        Try
            If _config Is Nothing Then Return ImpostazionePredefinita(chiave)
            Dim valore As String = _config.LeggiValore(chiave)
            If String.IsNullOrEmpty(valore) Then Return ImpostazionePredefinita(chiave)
            Return valore
        Catch ex As Exception
            _logger.ScriviLog(ex.Message)
            Return ImpostazionePredefinita(chiave)
        End Try
    End Function

    Private Function LeggiImpostazioneBooleana(chiave As String) As Boolean
        Try
            Return LeggiImpostazioneStringa(chiave) <> "0"
        Catch ex As Exception
            _logger.ScriviLog(ex.Message)
            Return True
        End Try
    End Function

    Public Sub ImpostaValore(chiave As String, valore As String)
        Try
            If _config IsNot Nothing AndAlso Not String.IsNullOrEmpty(chiave) Then _config.AggiungiValore(chiave, valore)
        Catch ex As Exception
            _logger.ScriviLog(ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Livello di eco durante la digitazione: "0" nessuno, "1" solo caratteri,
    ''' "2" solo parole, "3" caratteri e parole.
    ''' </summary>
    Public Property EchoDigitazione As String
        Get
            Return LeggiImpostazioneStringa("echo_digitazione")
        End Get
        Set(value As String)
            ImpostaValore("echo_digitazione", value)
        End Set
    End Property

    Public Sub ImpostaEchoSoloCaratteri()
        EchoDigitazione = "1"
    End Sub

    Public Sub ImpostaEchoSoloParole()
        EchoDigitazione = "2"
    End Sub

    Public Sub ImpostaEchoCaratteriEParole()
        EchoDigitazione = "3"
    End Sub

    Public Sub ImpostaEchoNessuno()
        EchoDigitazione = "0"
    End Sub

    ''' <summary>
    ''' Se attivo, i campi password vengono letti in chiaro durante la digitazione
    ''' invece di annunciare "asterisco".
    ''' </summary>
    Public Property LeggiPasswordAttivo As Boolean
        Get
            Return LeggiImpostazioneBooleana("leggi_password")
        End Get
        Set(value As Boolean)
            ImpostaValore("leggi_password", If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>
    ''' Correttore ortografico (SymSpell) abilitato.
    ''' </summary>
    Public Property AbilitaCorrettoreAttivo As Boolean
        Get
            Return LeggiImpostazioneBooleana("abilita_correttore")
        End Get
        Set(value As Boolean)
            ImpostaValore("abilita_correttore", If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>
    ''' Impostazione generale, valida ovunque (Word, Blocco note, campi), se durante
    ''' la LETTURA del testo i segni di punteggiatura vengono pronunciati a voce.
    ''' </summary>
    Public Property LetturaPunteggiaturaLettura As Boolean
        Get
            Return LivelloLetturaPunteggiatura > 0
        End Get
        Set(value As Boolean)
            ImpostaValore("lettura_punteggiatura_lettura", If(value, "1", "0"))
            ImpostaValore("livello_punteggiatura_lettura", If(value, "3", "0"))
        End Set
    End Property

    Public Property LivelloLetturaPunteggiatura As Integer
        Get
            Try
                Dim valoreLivello As String = LeggiImpostazioneStringa("livello_punteggiatura_lettura")
                Dim livelloParse As Integer
                If Integer.TryParse(valoreLivello, livelloParse) Then
                    If livelloParse < 0 Then livelloParse = 0
                    If livelloParse > 3 Then livelloParse = 3
                    Return livelloParse
                End If
                Return If(LeggiImpostazioneBooleana("lettura_punteggiatura_lettura"), 3, 0)
            Catch ex As Exception
                Return 3
            End Try
        End Get
        Set(value As Integer)
            Dim livelloClamp As Integer = System.Math.Max(0, System.Math.Min(3, value))
            ImpostaValore("livello_punteggiatura_lettura", livelloClamp.ToString())
            ImpostaValore("lettura_punteggiatura_lettura", If(livelloClamp > 0, "1", "0"))
        End Set
    End Property

    ''' <summary>
    ''' Impostazione generale, valida ovunque (Word, Blocco note, campi), se durante
    ''' la SCRITTURA i segni di punteggiatura vengono pronunciati a voce (eco digitazione).
    ''' </summary>
    Public Property LetturaPunteggiaturaScrittura As Boolean
        Get
            Return LivelloScritturaPunteggiatura > 0
        End Get
        Set(value As Boolean)
            ImpostaValore("lettura_punteggiatura_scrittura", If(value, "1", "0"))
            ImpostaValore("livello_punteggiatura_scrittura", If(value, "3", "0"))
        End Set
    End Property

    Public Property LivelloScritturaPunteggiatura As Integer
        Get
            Try
                Dim valoreLivello As String = LeggiImpostazioneStringa("livello_punteggiatura_scrittura")
                Dim livelloParse As Integer
                If Integer.TryParse(valoreLivello, livelloParse) Then
                    If livelloParse < 0 Then livelloParse = 0
                    If livelloParse > 3 Then livelloParse = 3
                    Return livelloParse
                End If
                Return If(LeggiImpostazioneBooleana("lettura_punteggiatura_scrittura"), 3, 0)
            Catch ex As Exception
                Return 3
            End Try
        End Get
        Set(value As Integer)
            Dim livelloClamp As Integer = System.Math.Max(0, System.Math.Min(3, value))
            ImpostaValore("livello_punteggiatura_scrittura", livelloClamp.ToString())
            ImpostaValore("lettura_punteggiatura_scrittura", If(livelloClamp > 0, "1", "0"))
        End Set
    End Property

    Public Shared Function LivelloSimboloScrittura(c As Char) As Integer
        Select Case c
            Case "."c, ","c, "?"c, "!"c
                Return 3
            Case "("c, ")"c, "["c, "]"c, "{"c, "}"c, """"c, "'"c, "`"c, "«"c, "»"c, "-"c, "–"c, "—"c, ";"c, ":"c, "…"c
                Return 2
            Case Else
                Return 1
        End Select
    End Function

    Public Property EliminaNotaConParola As Boolean
        Get
            Return LeggiImpostazioneBooleana("elimina_nota_con_parola")
        End Get
        Set(value As Boolean)
            ImpostaValore("elimina_nota_con_parola", If(value, "1", "0"))
        End Set
    End Property

    Public Property ScreenReaderPreferito As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("screen_reader_preferito")
                If String.IsNullOrEmpty(valore) Then Return "Automatico"
                Return valore
            Catch ex As Exception
                Return "Automatico"
            End Try
        End Get
        Set(value As String)
            ImpostaValore("screen_reader_preferito", value)
        End Set
    End Property

    Public Property BrowserPreferito As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("browser_preferito")
                If String.IsNullOrEmpty(valore) Then Return BrowserWinGabriel
                Return valore
            Catch ex As Exception
                _logger.ScriviLog("BrowserPreferito: " & ex.Message)
                Return BrowserWinGabriel
            End Try
        End Get
        Set(value As String)
            ImpostaValore("browser_preferito", value)
        End Set
    End Property

    Public Property ScreenReaderRilevatoAutomaticamente As String
        Get
            Try
                Return LeggiImpostazioneStringa("screen_reader_rilevato")
            Catch ex As Exception
                Return ""
            End Try
        End Get
        Set(value As String)
            ImpostaValore("screen_reader_rilevato", value)
        End Set
    End Property

    Public Property ModificaNoteNelDocumento As Boolean
        Get
            Return LeggiImpostazioneBooleana("modifica_note_nel_documento")
        End Get
        Set(value As Boolean)
            ImpostaValore("modifica_note_nel_documento", If(value, "1", "0"))
        End Set
    End Property

    Public Property PostaLetturaConWord As Boolean
        Get
            Return LeggiImpostazioneStringa("posta_lettura_word") = "1"
        End Get
        Set(value As Boolean)
            ImpostaValore("posta_lettura_word", If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>I messaggi scritti con la grafica si aprono nel browser di WinGabriel ridotto a lettore.</summary>
    Public ReadOnly Property PostaLetturaNelBrowser As Boolean
        Get
            Return LeggiImpostazioneStringa("posta_lettura_word") = "2"
        End Get
    End Property

    Public Property PostaScritturaConWord As Boolean
        Get
            Return LeggiImpostazioneStringa("posta_scrittura_word") = "1"
        End Get
        Set(value As Boolean)
            ImpostaValore("posta_scrittura_word", If(value, "1", "0"))
        End Set
    End Property

    Public Property ModalitaLetturaNumeri As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("modalita_lettura_numeri")
                If String.IsNullOrEmpty(valore) Then Return "intero"
                Return valore
            Catch ex As Exception
                Return "intero"
            End Try
        End Get
        Set(value As String)
            ImpostaValore("modalita_lettura_numeri", value)
        End Set
    End Property

    Public Property StampantePredefinita As String
        Get
            Try
                Return LeggiImpostazioneStringa("stampante_predefinita")
            Catch ex As Exception
                _logger.ScriviLog("StampantePredefinita Get: " & ex.Message)
                Return ""
            End Try
        End Get
        Set(value As String)
            Try
                ImpostaValore("stampante_predefinita", If(value, ""))
            Catch ex As Exception
                _logger.ScriviLog("StampantePredefinita Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaMargineMillimetri As Integer
        Get
            Try
                Dim valore As Integer = 15
                Integer.TryParse(LeggiImpostazioneStringa("stampa_margine_mm"), valore)
                If valore < 0 Then valore = 0
                If valore > 50 Then valore = 50
                Return valore
            Catch ex As Exception
                _logger.ScriviLog("StampaMargineMillimetri Get: " & ex.Message)
                Return 15
            End Try
        End Get
        Set(value As Integer)
            Try
                Dim valore As Integer = value
                If valore < 0 Then valore = 0
                If valore > 50 Then valore = 50
                ImpostaValore("stampa_margine_mm", valore.ToString())
            Catch ex As Exception
                _logger.ScriviLog("StampaMargineMillimetri Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaNomeCarattere As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("stampa_carattere_nome")
                If String.IsNullOrWhiteSpace(valore) Then Return "Arial"
                Return valore.Trim()
            Catch ex As Exception
                _logger.ScriviLog("StampaNomeCarattere Get: " & ex.Message)
                Return "Arial"
            End Try
        End Get
        Set(value As String)
            Try
                Dim valore As String = If(value, "").Trim()
                If String.IsNullOrWhiteSpace(valore) Then valore = "Arial"
                ImpostaValore("stampa_carattere_nome", valore)
            Catch ex As Exception
                _logger.ScriviLog("StampaNomeCarattere Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaDimensioneCarattere As Integer
        Get
            Try
                Dim valore As Integer = 11
                Integer.TryParse(LeggiImpostazioneStringa("stampa_carattere_dimensione"), valore)
                If valore < 6 Then valore = 6
                If valore > 36 Then valore = 36
                Return valore
            Catch ex As Exception
                _logger.ScriviLog("StampaDimensioneCarattere Get: " & ex.Message)
                Return 11
            End Try
        End Get
        Set(value As Integer)
            Try
                Dim valore As Integer = value
                If valore < 6 Then valore = 6
                If valore > 36 Then valore = 36
                ImpostaValore("stampa_carattere_dimensione", valore.ToString())
            Catch ex As Exception
                _logger.ScriviLog("StampaDimensioneCarattere Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaCopie As Integer
        Get
            Try
                Dim valore As Integer = 1
                Integer.TryParse(LeggiImpostazioneStringa("stampa_copie"), valore)
                If valore < 1 Then valore = 1
                If valore > 99 Then valore = 99
                Return valore
            Catch ex As Exception
                _logger.ScriviLog("StampaCopie Get: " & ex.Message)
                Return 1
            End Try
        End Get
        Set(value As Integer)
            Try
                Dim valore As Integer = value
                If valore < 1 Then valore = 1
                If valore > 99 Then valore = 99
                ImpostaValore("stampa_copie", valore.ToString())
            Catch ex As Exception
                _logger.ScriviLog("StampaCopie Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaOrientamento As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("stampa_orientamento")
                If String.Equals(valore, "orizzontale", StringComparison.OrdinalIgnoreCase) Then Return "orizzontale"
                Return "verticale"
            Catch ex As Exception
                _logger.ScriviLog("StampaOrientamento Get: " & ex.Message)
                Return "verticale"
            End Try
        End Get
        Set(value As String)
            Try
                Dim valore As String = "verticale"
                If String.Equals(If(value, ""), "orizzontale", StringComparison.OrdinalIgnoreCase) Then valore = "orizzontale"
                ImpostaValore("stampa_orientamento", valore)
            Catch ex As Exception
                _logger.ScriviLog("StampaOrientamento Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaFronteRetro As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("stampa_fronte_retro")
                If String.Equals(valore, "fronte_retro", StringComparison.OrdinalIgnoreCase) Then Return "fronte_retro"
                Return "una_facciata"
            Catch ex As Exception
                _logger.ScriviLog("StampaFronteRetro Get: " & ex.Message)
                Return "una_facciata"
            End Try
        End Get
        Set(value As String)
            Try
                Dim valore As String = "una_facciata"
                If String.Equals(If(value, ""), "fronte_retro", StringComparison.OrdinalIgnoreCase) Then valore = "fronte_retro"
                ImpostaValore("stampa_fronte_retro", valore)
            Catch ex As Exception
                _logger.ScriviLog("StampaFronteRetro Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaColore As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("stampa_colore")
                If String.Equals(valore, "bianco_nero", StringComparison.OrdinalIgnoreCase) Then Return "bianco_nero"
                Return "colore"
            Catch ex As Exception
                _logger.ScriviLog("StampaColore Get: " & ex.Message)
                Return "colore"
            End Try
        End Get
        Set(value As String)
            Try
                Dim valore As String = "colore"
                If String.Equals(If(value, ""), "bianco_nero", StringComparison.OrdinalIgnoreCase) Then valore = "bianco_nero"
                ImpostaValore("stampa_colore", valore)
            Catch ex As Exception
                _logger.ScriviLog("StampaColore Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property StampaQualita As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("stampa_qualita")
                If String.Equals(valore, "bozza", StringComparison.OrdinalIgnoreCase) Then Return "bozza"
                If String.Equals(valore, "alta", StringComparison.OrdinalIgnoreCase) Then Return "alta"
                Return "normale"
            Catch ex As Exception
                _logger.ScriviLog("StampaQualita Get: " & ex.Message)
                Return "normale"
            End Try
        End Get
        Set(value As String)
            Try
                Dim valore As String = "normale"
                If String.Equals(If(value, ""), "bozza", StringComparison.OrdinalIgnoreCase) Then valore = "bozza"
                If String.Equals(If(value, ""), "alta", StringComparison.OrdinalIgnoreCase) Then valore = "alta"
                ImpostaValore("stampa_qualita", valore)
            Catch ex As Exception
                _logger.ScriviLog("StampaQualita Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Function CostruisciImpostazioniStampa() As WGImpostazioniStampa
        Dim risultato As New WGImpostazioniStampa()
        Try
            risultato.MargineMillimetri = StampaMargineMillimetri
            risultato.NomeCarattere = StampaNomeCarattere
            risultato.DimensioneCarattere = StampaDimensioneCarattere
            risultato.Copie = StampaCopie
            risultato.Orientamento = StampaOrientamento
            risultato.FronteRetro = StampaFronteRetro
            risultato.Colore = StampaColore
            risultato.Qualita = StampaQualita
            risultato.Normalizza()
        Catch ex As Exception
            _logger.ScriviLog("CostruisciImpostazioniStampa: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Function NomeStampanteDaUsare() As String
        Try
            Dim nome As String = StampantePredefinita
            If String.IsNullOrWhiteSpace(nome) Then nome = Global.WGStampante.WGStampante.StampantePredefinitaSistema()
            Return If(nome, "").Trim()
        Catch ex As Exception
            _logger.ScriviLog("NomeStampanteDaUsare: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Property Mp3Voce As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("mp3_voce")
                If String.IsNullOrEmpty(valore) Then Return "0"
                Return valore
            Catch ex As Exception
                _logger.ScriviLog("Mp3Voce Get: " & ex.Message)
                Return "0"
            End Try
        End Get
        Set(value As String)
            Try
                ImpostaValore("mp3_voce", If(value, "0"))
            Catch ex As Exception
                _logger.ScriviLog("Mp3Voce Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property Mp3Velocita As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("mp3_velocita")
                If String.IsNullOrEmpty(valore) Then Return "50"
                Return valore
            Catch ex As Exception
                _logger.ScriviLog("Mp3Velocita Get: " & ex.Message)
                Return "50"
            End Try
        End Get
        Set(value As String)
            Try
                ImpostaValore("mp3_velocita", If(value, "50"))
            Catch ex As Exception
                _logger.ScriviLog("Mp3Velocita Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property Mp3Volume As String
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("mp3_volume")
                If String.IsNullOrEmpty(valore) Then Return "80"
                Return valore
            Catch ex As Exception
                _logger.ScriviLog("Mp3Volume Get: " & ex.Message)
                Return "80"
            End Try
        End Get
        Set(value As String)
            Try
                ImpostaValore("mp3_volume", If(value, "80"))
            Catch ex As Exception
                _logger.ScriviLog("Mp3Volume Set: " & ex.Message)
            End Try
        End Set
    End Property

End Class