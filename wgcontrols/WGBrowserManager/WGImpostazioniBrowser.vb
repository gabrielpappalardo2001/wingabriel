Imports System.IO
Imports wgcontrols

Public NotInheritable Class WGImpostazioniBrowser
    Private Shared ReadOnly _log As New Logger()

    Public Const ChiaveIdentita As String = "browser_identita"
    Public Const ChiaveSitiControlloCompleto As String = "browser_controllo_completo"
    Private Const ChiavePaginaIniziale As String = "browser_pagina_iniziale"
    Private Const ChiaveMotoreRicerca As String = "browser_motore_ricerca"
    Private Const ChiaveCartellaDownload As String = "browser_cartella_download"
    Private Const ChiaveChiediDoveSalvare As String = "browser_chiedi_dove_salvare"
    Private Const ChiaveAvanzamentoDownload As String = "browser_avanzamento_download"
    Private Const ChiaveBloccoPubblicita As String = "browser_blocco_pubblicita"
    Private Const ChiaveSalvaCronologia As String = "browser_salva_cronologia"
    Private Const ChiaveProponiPassword As String = "browser_proponi_password"
    Private Const ChiaveNuoviContenuti As String = "browser_annuncia_nuovi_contenuti"
    Private Const ChiaveNuoviContenutiNavigazione As String = "browser_annuncia_nuovi_contenuti_navigazione"
    Private Const ChiaveImmaginiSenzaDescrizione As String = "browser_immagini_senza_descrizione"
    Private Const ChiaveAnnunciaLingua As String = "browser_annuncia_lingua"
    Private Const ChiaveCliccabili As String = "browser_cliccabili"
    Private Const ChiavePdfNelLettore As String = "browser_pdf_nel_lettore"
    Private Const ChiaveRiapriSchede As String = "browser_riapri_schede"
    Private Const ChiaveSchedeSalvate As String = "browser_schede_salvate"
    Private Const ChiaveAnnunciaSelezione As String = "browser_annuncia_selezione"
    Private Const ChiaveLinguaPagine As String = "browser_lingua_pagine"
    Private Const ChiaveRegioniAParte As String = "browser_regioni_a_parte"
    Private Const ChiaveFrecceControlloCompleto As String = "browser_frecce_controllo_completo"
    Private Const ChiaveIdentitaGoogle As String = "browser_identita_google"
    Private Const ChiaveMigrazioneIdentita As String = "browser_identita_migrazione"
    Private Const ChiaveAccelerazioneGrafica As String = "browser_accelerazione_grafica"
    Private Const ChiaveRiproduzioneVideo As String = "browser_riproduzione_video"
    Private Const ChiaveSitiWordPress As String = "browser_siti_wordpress"
    Private Const ChiaveNonSupportatiNelLettore As String = "browser_non_supportati_nel_lettore"
    Private Const ChiaveDescrizioneAutomatica As String = "browser_descrizione_automatica_immagini"
    Private Const ChiaveModoTraduzione As String = "browser_traduzione_modo"
    Private Const ChiaveSitiNonTradotti As String = "browser_traduzione_eccezioni"
    Private Const ChiaveSitiTradottiSempre As String = "browser_traduzione_sempre"

    Public Const TraduzioneChiedi As String = "chiedi"
    Public Const TraduzioneAutomatica As String = "automatica"
    Public Const TraduzioneAComando As String = "comando"
    Private Shared _migrazioneIdentitaFatta As Boolean = False
    Private Shared ReadOnly _lockMigrazione As New Object()

    Public Shared Event LinguaPagineCambiata As EventHandler

    Public Shared ReadOnly LinguePagine As String()() = {
        New String() {"it", "Italiano", "it-IT"},
        New String() {"en", "Inglese", "en-US"},
        New String() {"fr", "Francese", "fr-FR"},
        New String() {"de", "Tedesco", "de-DE"},
        New String() {"es", "Spagnolo", "es-ES"},
        New String() {"pt", "Portoghese", "pt-PT"},
        New String() {"nl", "Olandese", "nl-NL"},
        New String() {"pl", "Polacco", "pl-PL"},
        New String() {"ro", "Rumeno", "ro-RO"},
        New String() {"el", "Greco", "el-GR"},
        New String() {"sv", "Svedese", "sv-SE"},
        New String() {"da", "Danese", "da-DK"},
        New String() {"nb", "Norvegese", "nb-NO"},
        New String() {"fi", "Finlandese", "fi-FI"},
        New String() {"cs", "Ceco", "cs-CZ"},
        New String() {"sk", "Slovacco", "sk-SK"},
        New String() {"hu", "Ungherese", "hu-HU"},
        New String() {"hr", "Croato", "hr-HR"},
        New String() {"sl", "Sloveno", "sl-SI"},
        New String() {"sq", "Albanese", "sq-AL"},
        New String() {"ru", "Russo", "ru-RU"},
        New String() {"uk", "Ucraino", "uk-UA"},
        New String() {"tr", "Turco", "tr-TR"},
        New String() {"ar", "Arabo", "ar-SA"},
        New String() {"he", "Ebraico", "he-IL"},
        New String() {"zh", "Cinese", "zh-CN"},
        New String() {"ja", "Giapponese", "ja-JP"},
        New String() {"ko", "Coreano", "ko-KR"},
        New String() {"hi", "Hindi", "hi-IN"},
        New String() {"ca", "Catalano", "ca-ES"}
    }

    Public Const MotoreGoogle As String = "google"
    Public Const MotoreDuckDuckGo As String = "duckduckgo"
    Public Const MotoreBing As String = "bing"
    Public Const MotoreEcosia As String = "ecosia"
    Public Const MotoreStartpage As String = "startpage"

    Public Const IdentitaAutomatica As String = "automatica"
    Public Const IdentitaChrome As String = "chrome"
    Public Const IdentitaFirefox As String = "firefox"
    Public Const IdentitaEdge As String = "edge"

    Private Shared ReadOnly DominiGoogle As String() = {"youtube.com", "youtube-nocookie.com", "youtu.be", "gmail.com", "googlemail.com", "gstatic.com", "googleusercontent.com", "googleapis.com", "withgoogle.com", "blogger.com", "blogspot.com", "googlevideo.com", "ggpht.com", "gvt1.com", "doubleclick.net"}

    Private Shared ReadOnly DominiContenutiProtetti As String() = {
        "netflix.com", "primevideo.com", "amazonvideo.com", "disneyplus.com", "nowtv.it", "now.tv", "nowtv.com", "dazn.com", "tv.apple.com", "music.apple.com",
        "paramountplus.com", "skygo.sky.it", "skyshowtime.com", "mediasetinfinity.mediaset.it", "infinity.mediaset.it", "timvision.it", "discoveryplus.com",
        "discoveryplus.it", "max.com", "hbomax.com", "crunchyroll.com", "raiplay.it", "rakuten.tv", "chili.com", "mubi.com", "hulu.com", "peacocktv.com",
        "viaplay.com", "canalplus.com", "joyn.de", "zattoo.com", "pluto.tv", "open.spotify.com", "music.amazon.it", "music.amazon.com", "tidal.com", "deezer.com"
    }

    Private Sub New()
    End Sub

    Private Shared Function Leggi(chiave As String, predefinito As String) As String
        Try
            Dim configurazione As New WGConfig()
            Dim valore As String = configurazione.LeggiValore(chiave)
            If String.IsNullOrWhiteSpace(valore) Then Return predefinito
            Return valore.Trim()
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.Leggi " & chiave & ": " & ex.Message)
            Return predefinito
        End Try
    End Function

    Private Shared Sub Scrivi(chiave As String, valore As String)
        Try
            Dim configurazione As New WGConfig()
            configurazione.AggiungiValore(chiave, If(valore, ""))
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.Scrivi " & chiave & ": " & ex.Message)
        End Try
    End Sub

    Private Shared Function LeggiBooleano(chiave As String, predefinito As Boolean) As Boolean
        Try
            Dim valore As String = Leggi(chiave, If(predefinito, "1", "0")).ToLowerInvariant()
            Return valore = "1" OrElse valore = "true" OrElse valore = "si" OrElse valore = "sì"
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.LeggiBooleano " & chiave & ": " & ex.Message)
            Return predefinito
        End Try
    End Function

    Private Shared Sub ScriviBooleano(chiave As String, valore As Boolean)
        Scrivi(chiave, If(valore, "1", "0"))
    End Sub

    ''' <summary>La pagina iniziale scelta dall'utente; se non ne ha scelta una, quella del motore di ricerca in uso.</summary>
    Public Shared Property PaginaIniziale As String
        Get
            Dim scelta As String = Leggi(ChiavePaginaIniziale, "")
            If scelta <> "" Then Return scelta
            Return PaginaDelMotore()
        End Get
        Set(value As String)
            Scrivi(ChiavePaginaIniziale, value)
        End Set
    End Property

    Private Shared Function PaginaDelMotore() As String
        Try
            Select Case MotoreRicerca
                Case MotoreDuckDuckGo : Return "https://duckduckgo.com"
                Case MotoreBing : Return "https://www.bing.com"
                Case MotoreEcosia : Return "https://www.ecosia.org"
                Case MotoreStartpage : Return "https://www.startpage.com"
            End Select
            Return "https://www.google.it"
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.PaginaDelMotore: " & ex.Message)
            Return "https://www.google.it"
        End Try
    End Function

    ''' <summary>I motori di ricerca tra cui si può scegliere.</summary>
    Public Shared ReadOnly Property MotoriDisponibili As String()
        Get
            Return New String() {MotoreGoogle, MotoreDuckDuckGo, MotoreBing, MotoreEcosia, MotoreStartpage}
        End Get
    End Property

    ''' <summary>Vero se l'utente ha già scelto il motore di ricerca: finché non lo fa, il browser glielo chiede all'apertura.</summary>
    Public Shared ReadOnly Property MotoreRicercaScelto As Boolean
        Get
            Try
                Return Leggi(ChiaveMotoreRicerca, "") <> ""
            Catch ex As Exception
                _log.ScriviLog("WGImpostazioniBrowser.MotoreRicercaScelto: " & ex.Message)
                Return True
            End Try
        End Get
    End Property

    Public Shared Property MotoreRicerca As String
        Get
            Dim valore As String = Leggi(ChiaveMotoreRicerca, MotoreGoogle).ToLowerInvariant()
            Select Case valore
                Case MotoreGoogle, MotoreDuckDuckGo, MotoreBing, MotoreEcosia, MotoreStartpage
                    Return valore
            End Select
            Return MotoreGoogle
        End Get
        Set(value As String)
            Scrivi(ChiaveMotoreRicerca, value)
        End Set
    End Property

    Public Shared Function NomeMotore(Optional motore As String = Nothing) As String
        Try
            Select Case If(motore, MotoreRicerca)
                Case MotoreDuckDuckGo : Return "DuckDuckGo"
                Case MotoreBing : Return "Bing"
                Case MotoreEcosia : Return "Ecosia"
                Case MotoreStartpage : Return "Startpage"
            End Select
            Return "Google"
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.NomeMotore: " & ex.Message)
            Return "Google"
        End Try
    End Function

    Public Shared Function IndirizzoRicerca(testo As String) As String
        Try
            Dim q As String = Uri.EscapeDataString(If(testo, "").Trim())
            Dim lingua As String = LinguaPagine
            Select Case MotoreRicerca
                Case MotoreDuckDuckGo : Return "https://duckduckgo.com/?kl=" & RegioneDuckDuckGo(lingua) & "&q=" & q
                Case MotoreBing : Return "https://www.bing.com/search?setlang=" & lingua & "&q=" & q
                Case MotoreEcosia : Return "https://www.ecosia.org/search?q=" & q
                Case MotoreStartpage : Return "https://www.startpage.com/do/search?language=" & LinguaStartpage(lingua) & "&query=" & q
            End Select
            If lingua = "it" Then Return "https://www.google.it/search?hl=it&q=" & q
            Return "https://www.google.com/search?hl=" & lingua & "&q=" & q
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.IndirizzoRicerca: " & ex.Message)
            Return "https://www.google.it"
        End Try
    End Function

    Private Shared Function RegioneDuckDuckGo(lingua As String) As String
        Select Case lingua
            Case "it" : Return "it-it"
            Case "en" : Return "us-en"
            Case "fr" : Return "fr-fr"
            Case "de" : Return "de-de"
            Case "es" : Return "es-es"
            Case "pt" : Return "pt-pt"
            Case "nl" : Return "nl-nl"
            Case "pl" : Return "pl-pl"
            Case "ro" : Return "ro-ro"
            Case "el" : Return "gr-el"
            Case "sv" : Return "se-sv"
            Case "da" : Return "dk-da"
            Case "nb" : Return "no-no"
            Case "fi" : Return "fi-fi"
            Case "cs" : Return "cz-cs"
            Case "sk" : Return "sk-sk"
            Case "hu" : Return "hu-hu"
            Case "hr" : Return "hr-hr"
            Case "sl" : Return "sl-sl"
            Case "ru" : Return "ru-ru"
            Case "uk" : Return "ua-uk"
            Case "tr" : Return "tr-tr"
            Case "ar" : Return "xa-ar"
            Case "he" : Return "il-he"
            Case "zh" : Return "cn-zh"
            Case "ja" : Return "jp-jp"
            Case "ko" : Return "kr-kr"
            Case "hi" : Return "in-en"
            Case "ca" : Return "es-ca"
        End Select
        Return "wt-wt"
    End Function

    Private Shared Function LinguaStartpage(lingua As String) As String
        Select Case lingua
            Case "it" : Return "italiano"
            Case "fr" : Return "francais"
            Case "de" : Return "deutsch"
            Case "es" : Return "espanol"
            Case "pt" : Return "portugues"
            Case "nl" : Return "nederlands"
            Case "pl" : Return "polski"
            Case "sv" : Return "svenska"
            Case "da" : Return "dansk"
            Case "nb" : Return "norsk"
            Case "fi" : Return "suomi"
            Case "ru" : Return "russkiy"
            Case "tr" : Return "turkce"
            Case "ja" : Return "nihongo"
            Case "ko" : Return "hangugeo"
            Case "zh" : Return "jiantizhongwen"
        End Select
        Return "english"
    End Function

    Private Shared Function VoceLingua(codice As String) As String()
        Try
            For Each voce As String() In LinguePagine
                If voce(0) = codice Then Return voce
            Next
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.VoceLingua: " & ex.Message)
        End Try
        Return LinguePagine(0)
    End Function

    Public Shared Property LinguaPagine As String
        Get
            Dim valore As String = Leggi(ChiaveLinguaPagine, "it").ToLowerInvariant()
            Return VoceLingua(valore)(0)
        End Get
        Set(value As String)
            Try
                Dim codice As String = VoceLingua(If(value, "").Trim().ToLowerInvariant())(0)
                Dim precedente As String = LinguaPagine
                Scrivi(ChiaveLinguaPagine, codice)
                If codice <> precedente Then RaiseEvent LinguaPagineCambiata(Nothing, EventArgs.Empty)
            Catch ex As Exception
                _log.ScriviLog("WGImpostazioniBrowser.LinguaPagine Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Shared Function NomeLinguaPagine(Optional codice As String = Nothing) As String
        Try
            Return VoceLingua(If(codice, LinguaPagine))(1)
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.NomeLinguaPagine: " & ex.Message)
            Return "Italiano"
        End Try
    End Function

    Public Shared Function LingueAccettate() As String
        Try
            Dim voce As String() = VoceLingua(LinguaPagine)
            Dim codice As String = voce(0)
            Dim regione As String = voce(2)
            If codice = "it" Then Return "it-IT,it;q=0.9,en-US;q=0.8,en;q=0.7"
            If codice = "en" Then Return "en-US,en;q=0.9"
            Return regione & "," & codice & ";q=0.9,en-US;q=0.8,en;q=0.7"
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.LingueAccettate: " & ex.Message)
            Return "it-IT,it;q=0.9,en-US;q=0.8,en;q=0.7"
        End Try
    End Function

    Public Shared Function CartellaDownloadPredefinita() As String
        Try
            Dim cartella As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            If Not Directory.Exists(cartella) Then cartella = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            Return cartella
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.CartellaDownloadPredefinita: " & ex.Message)
            Return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        End Try
    End Function

    Public Shared Property CartellaDownload As String
        Get
            Try
                Dim valore As String = Leggi(ChiaveCartellaDownload, "")
                If valore <> "" AndAlso Directory.Exists(valore) Then Return valore
            Catch ex As Exception
                _log.ScriviLog("WGImpostazioniBrowser.CartellaDownload: " & ex.Message)
            End Try
            Return CartellaDownloadPredefinita()
        End Get
        Set(value As String)
            Scrivi(ChiaveCartellaDownload, value)
        End Set
    End Property

    Public Shared Property ChiediDoveSalvare As Boolean
        Get
            Return LeggiBooleano(ChiaveChiediDoveSalvare, False)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveChiediDoveSalvare, value)
        End Set
    End Property

    Public Shared Property AnnunciaAvanzamentoDownload As Boolean
        Get
            Return LeggiBooleano(ChiaveAvanzamentoDownload, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveAvanzamentoDownload, value)
        End Set
    End Property

    Public Shared Property BloccoPubblicita As Boolean
        Get
            Return LeggiBooleano(ChiaveBloccoPubblicita, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveBloccoPubblicita, value)
        End Set
    End Property

    Public Shared Property SalvaCronologia As Boolean
        Get
            Return LeggiBooleano(ChiaveSalvaCronologia, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveSalvaCronologia, value)
        End Set
    End Property

    Public Shared Property ProponiSalvataggioPassword As Boolean
        Get
            Return LeggiBooleano(ChiaveProponiPassword, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveProponiPassword, value)
        End Set
    End Property

    Public Shared Property AnnunciaNuoviContenuti As Boolean
        Get
            Return LeggiBooleano(ChiaveNuoviContenuti, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveNuoviContenuti, value)
        End Set
    End Property

    Public Shared Property AnnunciaNuoviContenutiInNavigazione As Boolean
        Get
            Return LeggiBooleano(ChiaveNuoviContenutiNavigazione, False)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveNuoviContenutiNavigazione, value)
        End Set
    End Property

    Public Shared Property AnnunciaSelezione As Boolean
        Get
            Return LeggiBooleano(ChiaveAnnunciaSelezione, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveAnnunciaSelezione, value)
        End Set
    End Property

    Public Shared Property LeggiImmaginiSenzaDescrizione As Boolean
        Get
            Return LeggiBooleano(ChiaveImmaginiSenzaDescrizione, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveImmaginiSenzaDescrizione, value)
        End Set
    End Property

    Public Shared Property AnnunciaLingua As Boolean
        Get
            Return LeggiBooleano(ChiaveAnnunciaLingua, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveAnnunciaLingua, value)
        End Set
    End Property

    Public Shared Property RegioniAParte As Boolean
        Get
            Return LeggiBooleano(ChiaveRegioniAParte, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveRegioniAParte, value)
        End Set
    End Property

    Public Shared Property FrecceLeggonoInControlloCompleto As Boolean
        Get
            Return LeggiBooleano(ChiaveFrecceControlloCompleto, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveFrecceControlloCompleto, value)
        End Set
    End Property

    Public Shared Property RiconosciCliccabili As Boolean
        Get
            Return LeggiBooleano(ChiaveCliccabili, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveCliccabili, value)
        End Set
    End Property

    Public Shared Property ApriPdfNelLettore As Boolean
        Get
            Return LeggiBooleano(ChiavePdfNelLettore, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiavePdfNelLettore, value)
        End Set
    End Property

    Public Shared Property RiapriSchede As Boolean
        Get
            Return LeggiBooleano(ChiaveRiapriSchede, False)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveRiapriSchede, value)
        End Set
    End Property

    Public Shared Property SchedeSalvate As List(Of String)
        Get
            Dim risultato As New List(Of String)()
            Try
                For Each parte As String In Leggi(ChiaveSchedeSalvate, "").Split(New Char() {ChrW(10)}, StringSplitOptions.RemoveEmptyEntries)
                    If parte.Trim() <> "" Then risultato.Add(parte.Trim())
                Next
            Catch ex As Exception
                _log.ScriviLog("WGImpostazioniBrowser.SchedeSalvate: " & ex.Message)
            End Try
            Return risultato
        End Get
        Set(value As List(Of String))
            Try
                If value Is Nothing Then
                    Scrivi(ChiaveSchedeSalvate, "")
                Else
                    Scrivi(ChiaveSchedeSalvate, String.Join(ChrW(10), value))
                End If
            Catch ex As Exception
                _log.ScriviLog("WGImpostazioniBrowser.SchedeSalvate Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Shared Function NormalizzaIdentita(valore As String, predefinito As String) As String
        Try
            Select Case If(valore, "").Trim().ToLowerInvariant()
                Case IdentitaChrome : Return IdentitaChrome
                Case IdentitaFirefox : Return IdentitaFirefox
                Case IdentitaEdge : Return IdentitaEdge
                Case IdentitaAutomatica : Return IdentitaAutomatica
            End Select
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.NormalizzaIdentita: " & ex.Message)
        End Try
        Return predefinito
    End Function

    Public Shared Function NomeIdentita(identita As String) As String
        Select Case NormalizzaIdentita(identita, IdentitaAutomatica)
            Case IdentitaChrome : Return "Chrome"
            Case IdentitaFirefox : Return "Firefox"
            Case IdentitaEdge : Return "Edge"
        End Select
        Return "automatica"
    End Function

    Public Shared Function EIdentitaFissa(identita As String) As Boolean
        Try
            Dim valore As String = NormalizzaIdentita(identita, "")
            Return valore = IdentitaChrome OrElse valore = IdentitaFirefox OrElse valore = IdentitaEdge
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.EIdentitaFissa: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function EServizioConContenutiProtetti(host As String) As Boolean
        Try
            Dim h As String = If(host, "").Trim().ToLowerInvariant().TrimEnd("."c)
            If h = "" Then Return False
            For Each dominio As String In DominiContenutiProtetti
                If h = dominio OrElse h.EndsWith("." & dominio, StringComparison.Ordinal) Then Return True
            Next
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.EServizioConContenutiProtetti: " & ex.Message)
        End Try
        Return False
    End Function

    Public Shared Property IdentitaPredefinita As String
        Get
            Return NormalizzaIdentita(Leggi(ChiaveIdentita, IdentitaAutomatica), IdentitaAutomatica)
        End Get
        Set(value As String)
            Scrivi(ChiaveIdentita, NormalizzaIdentita(value, IdentitaAutomatica))
        End Set
    End Property

    Public Shared Function CartellaDatiBrowser() As String
        Try
            Return WGDb.WGDatabase.CartellaDati("browser")
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.CartellaDatiBrowser: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Sub RegistraPreparazioneBackup(nome As String, azione As Action)
        Try
            WGDb.WGBackup.RegistraPreparazione(nome, azione)
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.RegistraPreparazioneBackup: " & ex.Message)
        End Try
    End Sub

    Public Shared Function EFamigliaGoogle(host As String) As Boolean
        Try
            Dim h As String = If(host, "").Trim().ToLowerInvariant().TrimEnd("."c)
            If h = "" Then Return False
            Dim conPunti As String = "." & h & "."
            If conPunti.Contains(".google.") Then Return True
            For Each dominio As String In DominiGoogle
                If h = dominio OrElse h.EndsWith("." & dominio, StringComparison.Ordinal) Then Return True
            Next
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.EFamigliaGoogle: " & ex.Message)
        End Try
        Return False
    End Function

    Public Shared Property IdentitaGoogle As String
        Get
            Dim valore As String = NormalizzaIdentita(Leggi(ChiaveIdentitaGoogle, IdentitaChrome), IdentitaChrome)
            If valore = IdentitaAutomatica Then valore = IdentitaChrome
            Return valore
        End Get
        Set(value As String)
            Dim valore As String = NormalizzaIdentita(value, IdentitaChrome)
            If valore = IdentitaAutomatica Then valore = IdentitaChrome
            Scrivi(ChiaveIdentitaGoogle, valore)
        End Set
    End Property

    Public Shared Property AccelerazioneGrafica As Boolean
        Get
            Return LeggiBooleano(ChiaveAccelerazioneGrafica, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveAccelerazioneGrafica, value)
        End Set
    End Property

    Public Const RiproduzioneLettore As String = "lettore"
    Public Const RiproduzioneSottofondo As String = "sottofondo"
    Public Const RiproduzionePagina As String = "pagina"

    ' Come si riproducono i video: nel lettore di WinGabriel a schermo (WGPlayerFormInt), in sottofondo
    ' con il sistema del centro di controllo, oppure dal lettore della pagina stessa.
    Public Shared Property RiproduzioneVideo As String
        Get
            Dim valore As String = Leggi(ChiaveRiproduzioneVideo, RiproduzioneLettore).Trim().ToLowerInvariant()
            If valore = RiproduzioneSottofondo OrElse valore = RiproduzionePagina Then Return valore
            Return RiproduzioneLettore
        End Get
        Set(value As String)
            Dim v As String = If(value, "").Trim().ToLowerInvariant()
            If v <> RiproduzioneSottofondo AndAlso v <> RiproduzionePagina Then v = RiproduzioneLettore
            Scrivi(ChiaveRiproduzioneVideo, v)
        End Set
    End Property

    Public Const WordPressChiedi As String = "chiedi"
    Public Const WordPressSemplificata As String = "semplificata"
    Public Const WordPressNormale As String = "normale"

    ' Come si aprono i siti fatti con WordPress (con le API che funzionano): chiedere ogni volta, aprire sempre con la lettura
    ' semplificata (articoli, categorie, pagine, ricerca) oppure sempre con la lettura normale della pagina.
    Public Shared Property SitiWordPress As String
        Get
            Dim valore As String = Leggi(ChiaveSitiWordPress, WordPressChiedi).Trim().ToLowerInvariant()
            If valore = WordPressSemplificata OrElse valore = WordPressNormale Then Return valore
            Return WordPressChiedi
        End Get
        Set(value As String)
            Dim v As String = If(value, "").Trim().ToLowerInvariant()
            If v <> WordPressSemplificata AndAlso v <> WordPressNormale Then v = WordPressChiedi
            Scrivi(ChiaveSitiWordPress, v)
        End Set
    End Property

    Public Shared Function NomeSitiWordPress(Optional modo As String = Nothing) As String
        Select Case If(modo, SitiWordPress)
            Case WordPressSemplificata : Return "apri con la lettura semplificata"
            Case WordPressNormale : Return "apri con la lettura normale"
        End Select
        Return "chiedi ogni volta"
    End Function

    Public Shared Function NomeRiproduzioneVideo(Optional modo As String = Nothing) As String
        Select Case If(modo, RiproduzioneVideo)
            Case RiproduzioneSottofondo : Return "riproduci in sottofondo"
            Case RiproduzionePagina : Return "riproduci dalla pagina"
        End Select
        Return "apri il lettore di WinGabriel"
    End Function

    ''' <summary>
    ''' Descrive da sola con l'intelligenza artificiale le immagini senza descrizione quando il cursore ci arriva.
    ''' Spenta di partenza: ogni immagine è una richiesta e consuma la quota del servizio.
    ''' </summary>
    Public Shared Property DescrizioneAutomaticaImmagini As Boolean
        Get
            Return LeggiBooleano(ChiaveDescrizioneAutomatica, False)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveDescrizioneAutomatica, value)
        End Set
    End Property

    Public Shared Property NonSupportatiNelLettore As Boolean
        Get
            Return LeggiBooleano(ChiaveNonSupportatiNelLettore, True)
        End Get
        Set(value As Boolean)
            ScriviBooleano(ChiaveNonSupportatiNelLettore, value)
        End Set
    End Property

    Public Shared Function IdentitaAutomaticaPerSito(host As String) As String
        Try
            If EServizioConContenutiProtetti(host) Then Return IdentitaEdge
            If EFamigliaGoogle(host) Then Return IdentitaGoogle
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.IdentitaAutomaticaPerSito: " & ex.Message)
        End Try
        Return IdentitaChrome
    End Function

    Private Shared Sub MigraIdentitaGoogle(archivio As WGArchivioBrowser)
        Try
            If _migrazioneIdentitaFatta OrElse archivio Is Nothing Then Return
            SyncLock _lockMigrazione
                If _migrazioneIdentitaFatta Then Return
                _migrazioneIdentitaFatta = True
                If Leggi(ChiaveMigrazioneIdentita, "") = "2" Then Return
                For Each voce As Tuple(Of String, String, String) In archivio.ImpostazioniDeiSiti()
                    If voce.Item2 = "identita" AndAlso EFamigliaGoogle(voce.Item1) Then archivio.ImpostaValoreSito(voce.Item1, "identita", "")
                Next
                Scrivi(ChiaveMigrazioneIdentita, "2")
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.MigraIdentitaGoogle: " & ex.Message)
        End Try
    End Sub

    Public Shared Function IdentitaPerSito(host As String) As String
        Try
            MigraIdentitaGoogle(WGArchivioBrowser.Istanza)
            Dim valore As String = ""
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivio IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(host) Then valore = archivio.ValoreSito(host, "identita")
            valore = NormalizzaIdentita(valore, "")
            If EIdentitaFissa(valore) Then Return valore
            If EServizioConContenutiProtetti(host) Then Return IdentitaEdge
            Dim predefinita As String = IdentitaPredefinita
            If EIdentitaFissa(predefinita) Then Return predefinita
            Return IdentitaAutomaticaPerSito(host)
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.IdentitaPerSito: " & ex.Message)
            Return IdentitaChrome
        End Try
    End Function

    Public Shared Sub ImpostaIdentitaSito(host As String, identita As String)
        Try
            If String.IsNullOrWhiteSpace(host) Then Return
            Dim valore As String = NormalizzaIdentita(identita, "")
            If valore = IdentitaAutomatica Then valore = ""
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivio IsNot Nothing Then archivio.ImpostaValoreSito(host, "identita", valore)
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.ImpostaIdentitaSito: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Pagine in un'altra lingua: chiedere per ogni sito, tradurre senza chiedere o solo a comando.</summary>
    Public Shared Property ModoTraduzione As String
        Get
            Dim valore As String = Leggi(ChiaveModoTraduzione, TraduzioneChiedi)
            If valore = TraduzioneAutomatica OrElse valore = TraduzioneAComando Then Return valore
            Return TraduzioneChiedi
        End Get
        Set(value As String)
            Scrivi(ChiaveModoTraduzione, value)
        End Set
    End Property

    Public Shared Function NomeModoTraduzione(Optional modo As String = Nothing) As String
        Select Case If(modo, ModoTraduzione)
            Case TraduzioneAutomatica : Return "tradurre senza chiedere"
            Case TraduzioneAComando : Return "solo a comando, con Control Shift U"
        End Select
        Return "chiedere per ogni sito"
    End Function

    Private Shared Function ElencoSiti(chiave As String) As HashSet(Of String)
        Dim siti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            For Each parte As String In Leggi(chiave, "").Split(";"c)
                If parte.Trim() <> "" Then siti.Add(parte.Trim())
            Next
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.ElencoSiti " & chiave & ": " & ex.Message)
        End Try
        Return siti
    End Function

    Private Shared Sub ImpostaSitoInElenco(chiave As String, sito As String, presente As Boolean)
        Try
            Dim s As String = WGBloccoPubblicita.NormalizzaSito(sito)
            If s = "" Then Return
            Dim siti As HashSet(Of String) = ElencoSiti(chiave)
            If presente Then siti.Add(s) Else siti.Remove(s)
            Scrivi(chiave, String.Join(";", siti))
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.ImpostaSitoInElenco " & chiave & ": " & ex.Message)
        End Try
    End Sub

    ' Un sito nell'elenco vale anche per i suoi sottodomini: wikipedia.org comprende en.wikipedia.org.
    Private Shared Function SitoInElenco(chiave As String, host As String) As Boolean
        Try
            Dim h As String = WGBloccoPubblicita.NormalizzaSito(host)
            If h = "" Then Return False
            For Each sito As String In ElencoSiti(chiave)
                If h = sito OrElse h.EndsWith("." & sito, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.SitoInElenco " & chiave & ": " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>Eccezioni: siti che non vengono mai tradotti e per cui non si chiede.</summary>
    Public Shared Function SitiNonTradotti() As List(Of String)
        Return ElencoSiti(ChiaveSitiNonTradotti).OrderBy(Function(s) s, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Public Shared Function SitoNonTradotto(host As String) As Boolean
        Return SitoInElenco(ChiaveSitiNonTradotti, host)
    End Function

    Public Shared Sub ImpostaSitoNonTradotto(host As String, presente As Boolean)
        ImpostaSitoInElenco(ChiaveSitiNonTradotti, host, presente)
        If presente Then ImpostaSitoInElenco(ChiaveSitiTradottiSempre, host, False)
    End Sub

    ''' <summary>Siti per cui si è risposto "traduci sempre": vengono tradotti senza chiedere.</summary>
    Public Shared Function SitoTradottoSempre(host As String) As Boolean
        Return SitoInElenco(ChiaveSitiTradottiSempre, host)
    End Function

    Public Shared Sub ImpostaSitoTradottoSempre(host As String, presente As Boolean)
        ImpostaSitoInElenco(ChiaveSitiTradottiSempre, host, presente)
        If presente Then ImpostaSitoInElenco(ChiaveSitiNonTradotti, host, False)
    End Sub

    Public Shared Function SitiInControlloCompleto() As HashSet(Of String)
        Dim siti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            For Each parte As String In Leggi(ChiaveSitiControlloCompleto, "").Split(";"c)
                If parte.Trim() <> "" Then siti.Add(parte.Trim())
            Next
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.SitiInControlloCompleto: " & ex.Message)
        End Try
        Return siti
    End Function

    Public Shared Function SitoInControlloCompleto(host As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(host) Then Return False
            Return SitiInControlloCompleto().Contains(host)
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.SitoInControlloCompleto: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaControlloCompleto(host As String, attivo As Boolean)
        Try
            If String.IsNullOrWhiteSpace(host) Then Return
            Dim siti As HashSet(Of String) = SitiInControlloCompleto()
            If attivo Then siti.Add(host) Else siti.Remove(host)
            Scrivi(ChiaveSitiControlloCompleto, String.Join(";", siti))
        Catch ex As Exception
            _log.ScriviLog("WGImpostazioniBrowser.ImpostaControlloCompleto: " & ex.Message)
        End Try
    End Sub
End Class