' ============================================================================================
' WGTraduttore - traduzione dei testi di WinGabriel
' ============================================================================================
'
' A cosa serve
'   Traduce testi, elenchi di righe (il browser) e file di testo con due motori:
'   - Google: online, tutte le lingue, riconosce da solo la lingua di partenza.
'   - Offline: il motore Bergamot di Firefox eseguito in un WebView2 invisibile, con i modelli che
'     Mozilla pubblica per Firefox (più di 50 lingue). Funziona senza internet dopo aver scaricato una
'     volta il motore (circa 5 megabyte) e i modelli delle lingue usate (circa 40 megabyte per ogni
'     direzione). Le lingue diverse dall'inglese passano dall'inglese. I modelli si aggiornano da soli
'     quando Mozilla ne pubblica una versione nuova.
'
' Come si usa
'   Dim r As WGTraduttore.Risultato = Await WGTraduttore.TraduciAsync(testo)
'   If r.Riuscito Then ... r.Testo, r.LinguaOrigine
'   Dim elenco = Await WGTraduttore.TraduciElencoAsync(righe, "en", "")   ' browser
'   WGTraduttore.TraduciEMostra(testo, "Traduzione")                      ' attesa + campo con il risultato
'   WGTraduttore.MostraMenuTraduttore()                                   ' menu Web, Traduttore
'   WGTraduttore.MostraImpostazioni()                                     ' Impostazioni, Traduttore
'
' Impostazioni (WGConfig)
'   traduttore_motore                google | offline
'   traduttore_rilevamento           1 | 0: se 0 si usa traduttore_lingua_origine
'   traduttore_lingua_destinazione   codice, predefinito it
'   traduttore_lingua_origine        codice, predefinito en
'
' Errori
'   Nessun metodo solleva eccezioni: gli errori finiscono nel log e nel campo Errore del risultato.
' ============================================================================================
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports WGDb

Public NotInheritable Class WGTraduttore

    Public Class Risultato
        Public Property Testo As String = ""
        Public Property LinguaOrigine As String = ""
        Public Property LinguaDestinazione As String = ""
        Public Property Errore As String = ""
        Public ReadOnly Property Riuscito As Boolean
            Get
                Return String.IsNullOrEmpty(Errore)
            End Get
        End Property
    End Class

    Public Class RisultatoElenco
        Public Property Testi As New List(Of String)()
        Public Property LinguaOrigine As String = ""
        Public Property Errore As String = ""
        Public ReadOnly Property Riuscito As Boolean
            Get
                Return String.IsNullOrEmpty(Errore)
            End Get
        End Property
    End Class

    Public Const MotoreGoogle As String = "google"
    Public Const MotoreOffline As String = "offline"
    Public Const Automatica As String = "auto"

    Private Const ChiaveMotore As String = "traduttore_motore"
    Private Const ChiaveRilevamento As String = "traduttore_rilevamento"
    Private Const ChiaveDestinazione As String = "traduttore_lingua_destinazione"
    Private Const ChiaveOrigine As String = "traduttore_lingua_origine"
    Private Const ChiaveCambioVoce As String = "traduttore_cambio_voce"
    Private Const ChiaveCambioBraille As String = "traduttore_cambio_braille"
    Private Const ChiaveLingueEscluse As String = "traduttore_lingue_escluse"

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _http As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(40)}
    Private Shared ReadOnly _cache As New ConcurrentDictionary(Of String, String)(StringComparer.Ordinal)
    Private Const MassimoCache As Integer = 30000

    ''' <summary>Codice e nome italiano delle lingue proposte nelle scelte: quelle più diffuse per prime.</summary>
    Public Shared ReadOnly Lingue As String()() = {
        New String() {"it", "Italiano"}, New String() {"en", "Inglese"}, New String() {"fr", "Francese"},
        New String() {"de", "Tedesco"}, New String() {"es", "Spagnolo"}, New String() {"pt", "Portoghese"},
        New String() {"nl", "Olandese"}, New String() {"pl", "Polacco"}, New String() {"ro", "Rumeno"},
        New String() {"el", "Greco"}, New String() {"sv", "Svedese"}, New String() {"da", "Danese"},
        New String() {"nb", "Norvegese"}, New String() {"fi", "Finlandese"}, New String() {"cs", "Ceco"},
        New String() {"sk", "Slovacco"}, New String() {"hu", "Ungherese"}, New String() {"hr", "Croato"},
        New String() {"sl", "Sloveno"}, New String() {"sr", "Serbo"}, New String() {"bs", "Bosniaco"},
        New String() {"sq", "Albanese"}, New String() {"bg", "Bulgaro"}, New String() {"et", "Estone"},
        New String() {"lv", "Lettone"}, New String() {"lt", "Lituano"}, New String() {"is", "Islandese"},
        New String() {"ru", "Russo"}, New String() {"uk", "Ucraino"}, New String() {"tr", "Turco"},
        New String() {"az", "Azero"}, New String() {"ar", "Arabo"}, New String() {"he", "Ebraico"},
        New String() {"fa", "Persiano"}, New String() {"ur", "Urdu"}, New String() {"hi", "Hindi"},
        New String() {"bn", "Bengalese"}, New String() {"mr", "Marathi"}, New String() {"gu", "Gujarati"},
        New String() {"ta", "Tamil"}, New String() {"te", "Telugu"}, New String() {"kn", "Kannada"},
        New String() {"ml", "Malayalam"}, New String() {"zh", "Cinese"}, New String() {"ja", "Giapponese"},
        New String() {"ko", "Coreano"}, New String() {"th", "Thailandese"}, New String() {"vi", "Vietnamita"},
        New String() {"id", "Indonesiano"}, New String() {"ms", "Malese"}, New String() {"ca", "Catalano"},
        New String() {"gl", "Galiziano"}, New String() {"eu", "Basco"}, New String() {"af", "Afrikaans"}
    }

    Private Sub New()
    End Sub

    ' I servizi di traduzione e dei modelli accettano solo connessioni TLS 1.2 o successive.
    Friend Shared Sub AssicuraTls()
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12 Or CType(12288, SecurityProtocolType)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.AssicuraTls: " & ex.Message)
            Try
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Catch exRiserva As Exception
                log.ScriviLog("WGTraduttore.AssicuraTls riserva: " & exRiserva.Message)
            End Try
        End Try
    End Sub

#Region "Impostazioni"

    Private Shared Function Leggi(chiave As String, predefinito As String) As String
        Try
            Dim valore As String = New WGConfig().LeggiValore(chiave)
            If String.IsNullOrWhiteSpace(valore) Then Return predefinito
            Return valore.Trim()
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.Leggi " & chiave & ": " & ex.Message)
            Return predefinito
        End Try
    End Function

    Private Shared Sub Scrivi(chiave As String, valore As String)
        Try
            Dim configurazione As New WGConfig()
            configurazione.AggiungiValore(chiave, If(valore, ""))
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.Scrivi " & chiave & ": " & ex.Message)
        End Try
    End Sub

    Public Shared Property Motore As String
        Get
            Return If(Leggi(ChiaveMotore, MotoreGoogle) = MotoreOffline, MotoreOffline, MotoreGoogle)
        End Get
        Set(value As String)
            Scrivi(ChiaveMotore, If(value = MotoreOffline, MotoreOffline, MotoreGoogle))
        End Set
    End Property

    Public Shared Property RilevamentoAttivo As Boolean
        Get
            Return Leggi(ChiaveRilevamento, "1") <> "0"
        End Get
        Set(value As Boolean)
            Scrivi(ChiaveRilevamento, If(value, "1", "0"))
        End Set
    End Property

    Public Shared Property LinguaDestinazione As String
        Get
            Return NormalizzaCodice(Leggi(ChiaveDestinazione, "it"))
        End Get
        Set(value As String)
            Scrivi(ChiaveDestinazione, NormalizzaCodice(value))
        End Set
    End Property

    Public Shared Property LinguaOrigine As String
        Get
            Return NormalizzaCodice(Leggi(ChiaveOrigine, "en"))
        End Get
        Set(value As String)
            Scrivi(ChiaveOrigine, NormalizzaCodice(value))
        End Set
    End Property

    ''' <summary>Riduce un codice come en-US o EN_gb al codice di lingua usato dal traduttore.</summary>
    Public Shared Function NormalizzaCodice(codice As String) As String
        Try
            Dim c As String = If(codice, "").Trim().ToLowerInvariant().Replace("_"c, "-"c)
            If c = "" Then Return ""
            If c = Automatica Then Return Automatica
            If c.StartsWith("zh", StringComparison.Ordinal) Then Return "zh"
            Dim trattino As Integer = c.IndexOf("-"c)
            If trattino > 0 Then c = c.Substring(0, trattino)
            Select Case c
                Case "no", "nn" : Return "nb"
                Case "iw" : Return "he"
            End Select
            Return c
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.NormalizzaCodice: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function NomeLingua(codice As String) As String
        Try
            Dim c As String = NormalizzaCodice(codice)
            For Each voce As String() In Lingue
                If voce(0) = c Then Return voce(1)
            Next
            Return If(c = "", "lingua sconosciuta", c)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.NomeLingua: " & ex.Message)
            Return codice
        End Try
    End Function

    ''' <summary>"dall'inglese", "dal tedesco": per annunci come "Pagina tradotta dall'inglese".</summary>
    Public Shared Function DallaLingua(codice As String) As String
        Dim nome As String = NomeLingua(codice).ToLowerInvariant()
        If nome.Length > 0 AndAlso "aeiou".IndexOf(nome(0)) >= 0 Then Return "dall'" & nome
        If nome.StartsWith("s") AndAlso nome.Length > 1 AndAlso "aeiou".IndexOf(nome(1)) < 0 Then Return "dallo " & nome
        Return "dal " & nome
    End Function

    Public Shared Function StessaLingua(a As String, b As String) As Boolean
        Dim x As String = NormalizzaCodice(a)
        Dim y As String = NormalizzaCodice(b)
        Return x <> "" AndAlso x = y
    End Function

    ''' <summary>Vero se, leggendo un testo in lingua originale (senza tradurlo), si passa da soli alla voce di quella
    ''' lingua quando e' installata su questo computer.</summary>
    Public Shared Property CambioVoceAutomatico As Boolean
        Get
            Return Leggi(ChiaveCambioVoce, "0") <> "0"
        End Get
        Set(value As Boolean)
            Scrivi(ChiaveCambioVoce, If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>Vero se, leggendo un testo in lingua originale (senza tradurlo) con il braille acceso, si passa da soli
    ''' alla tabella braille di quella lingua quando c'e'.</summary>
    Public Shared Property CambioTabellaBrailleAutomatico As Boolean
        Get
            Return Leggi(ChiaveCambioBraille, "0") <> "0"
        End Get
        Set(value As Boolean)
            Scrivi(ChiaveCambioBraille, If(value, "1", "0"))
        End Set
    End Property

    ''' <summary>Le lingue che non vanno mai tradotte da sole (nei siti e nei documenti): chi le conosce già può
    ''' escluderle, cosi' il traduttore non chiede piu' nulla per quelle, anche se sono diverse dalla lingua di
    ''' destinazione. Non impedisce di tradurre a mano quando lo si chiede esplicitamente.</summary>
    Public Shared Function ElencoLingueEscluse() As List(Of String)
        Try
            Dim grezzo As String = Leggi(ChiaveLingueEscluse, "")
            If grezzo = "" Then Return New List(Of String)()
            Return grezzo.Split(","c).Select(Function(c) NormalizzaCodice(c)).Where(Function(c) c <> "").Distinct().ToList()
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ElencoLingueEscluse: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    Public Shared Function LinguaEsclusa(codice As String) As Boolean
        Try
            Dim c As String = NormalizzaCodice(codice)
            If c = "" Then Return False
            Return ElencoLingueEscluse().Contains(c)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.LinguaEsclusa: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub ImpostaLinguaEsclusa(codice As String, esclusa As Boolean)
        Try
            Dim c As String = NormalizzaCodice(codice)
            If c = "" Then Return
            Dim elenco As List(Of String) = ElencoLingueEscluse()
            If esclusa Then
                If Not elenco.Contains(c) Then elenco.Add(c)
            Else
                elenco.Remove(c)
            End If
            Scrivi(ChiaveLingueEscluse, String.Join(",", elenco))
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ImpostaLinguaEsclusa: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Voce e braille secondo la lingua letta"

    ' Cosa c'era prima di passare alla voce o alla tabella della lingua rilevata, per tornarci quando si smette di
    ' leggere quel testo (si torna alla lingua di destinazione, si traduce, o si cambia pagina/documento).
    Private Shared _vocePrecedente As Sintesi = Nothing
    Private Shared _tabellaBraillePrecedente As String = Nothing
    Private Shared _linguaApplicata As String = ""

    ''' <summary>Si legge un testo nella lingua indicata senza tradurlo: se le impostazioni lo prevedono, passa alla
    ''' voce e alla tabella braille di quella lingua. Non fa niente se la lingua e' la stessa di prima.</summary>
    Public Shared Sub ApplicaLinguaLettura(codiceLingua As String)
        Try
            Dim c As String = NormalizzaCodice(codiceLingua)
            If c = "" OrElse c = Automatica OrElse StessaLingua(c, _linguaApplicata) Then Return
            If CambioVoceAutomatico Then
                Try
                    Dim s As sintesivocale = sintesivocale.GetInstance()
                    If s IsNot Nothing Then
                        Dim voce As Sintesi = s.TrovaSintesiSapiPerLingua(c)
                        If voce IsNot Nothing AndAlso Not voce.Attiva Then
                            If _vocePrecedente Is Nothing Then
                                For Each v As Sintesi In s.OttieniSintesiSapi()
                                    If v.Attiva Then
                                        _vocePrecedente = v
                                        Exit For
                                    End If
                                Next
                            End If
                            s.ImpostaSintesiSapi(voce)
                        End If
                    End If
                Catch exVoce As Exception
                    log.ScriviLog("WGTraduttore.ApplicaLinguaLettura (voce): " & exVoce.Message)
                End Try
            End If
            If CambioTabellaBrailleAutomatico AndAlso WGBraille.Attivo Then
                Try
                    Dim t As TabellaBraille = WGBrailleLouis.TabellaPerLingua(c, WGBraille.Punti)
                    If t IsNot Nothing AndAlso Not String.Equals(t.File, WGBraille.TabellaFile, StringComparison.OrdinalIgnoreCase) Then
                        If _tabellaBraillePrecedente Is Nothing Then _tabellaBraillePrecedente = WGBraille.TabellaFile
                        WGBraille.TabellaFile = t.File
                    End If
                Catch exTabella As Exception
                    log.ScriviLog("WGTraduttore.ApplicaLinguaLettura (braille): " & exTabella.Message)
                End Try
            End If
            _linguaApplicata = c
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ApplicaLinguaLettura: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Torna alla voce e alla tabella braille di prima di ApplicaLinguaLettura, se erano state cambiate.</summary>
    Public Shared Sub RipristinaLinguaLettura()
        Try
            If _linguaApplicata = "" Then Return
            Try
                If _vocePrecedente IsNot Nothing Then
                    sintesivocale.GetInstance()?.ImpostaSintesiSapi(_vocePrecedente)
                End If
            Catch exVoce As Exception
                log.ScriviLog("WGTraduttore.RipristinaLinguaLettura (voce): " & exVoce.Message)
            Finally
                _vocePrecedente = Nothing
            End Try
            Try
                If _tabellaBraillePrecedente IsNot Nothing Then WGBraille.TabellaFile = _tabellaBraillePrecedente
            Catch exTabella As Exception
                log.ScriviLog("WGTraduttore.RipristinaLinguaLettura (braille): " & exTabella.Message)
            Finally
                _tabellaBraillePrecedente = Nothing
            End Try
            _linguaApplicata = ""
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.RipristinaLinguaLettura: " & ex.Message)
        End Try
    End Sub

    Private Const LunghezzaMinimaTestoRilevabile As Integer = 40
    Private Shared _ultimoTestoVerificatoPerLingua As String = ""

    ''' <summary>Si chiama su ogni pezzo di testo che sta per essere detto (da sintesivocale.LeggiTesto, l'unico punto
    ''' da cui passa tutto): se è lungo abbastanza da non confondersi con un annuncio dell'interfaccia e risulta in una
    ''' lingua diversa da quella di destinazione, cambia voce e tabella braille per quel pezzo, anche dentro lo stesso
    ''' documento o pagina. Locale e veloce (nessuna richiesta di rete): non deve mai far aspettare la voce.</summary>
    Public Shared Sub ApplicaLinguaTestoLetto(testo As String)
        Try
            If Not RilevamentoAttivo OrElse (Not CambioVoceAutomatico AndAlso Not CambioTabellaBrailleAutomatico) Then Return
            Dim t As String = If(testo, "").Trim()
            If t.Length < LunghezzaMinimaTestoRilevabile OrElse t = _ultimoTestoVerificatoPerLingua Then Return
            _ultimoTestoVerificatoPerLingua = t
            Dim campione As String = If(t.Length > 400, t.Substring(0, 400), t)
            Dim lingua As String = RilevaLinguaLocale(campione)
            If lingua = "" Then Return
            If StessaLingua(lingua, LinguaDestinazione) Then
                RipristinaLinguaLettura()
            Else
                ApplicaLinguaLettura(lingua)
            End If
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ApplicaLinguaTestoLetto: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Traduzione"

    Private Shared Function ChiaveCache(motoreUsato As String, origine As String, destinazione As String, testo As String) As String
        Return motoreUsato & "|" & origine & "|" & destinazione & "|" & testo
    End Function

    Private Shared Sub MemorizzaInCache(chiave As String, valore As String)
        Try
            If _cache.Count > MassimoCache Then _cache.Clear()
            _cache(chiave) = valore
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.MemorizzaInCache: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Traduce un testo anche lungo mantenendo la divisione in righe.
    ''' origine vuota: rilevamento automatico se attivo, altrimenti la lingua di origine impostata.
    ''' destinazione vuota: la lingua di destinazione impostata.
    ''' </summary>
    Public Shared Async Function TraduciAsync(testo As String, Optional origine As String = "", Optional destinazione As String = "") As Task(Of Risultato)
        Dim risultato As New Risultato()
        Try
            Dim normale As String = If(testo, "").Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            risultato.LinguaDestinazione = If(String.IsNullOrWhiteSpace(destinazione), LinguaDestinazione, NormalizzaCodice(destinazione))
            If String.IsNullOrWhiteSpace(normale) Then
                risultato.Errore = "Non c'è testo da tradurre"
                Return risultato
            End If
            Dim righe As String() = normale.Split(ControlChars.Lf)
            Dim unita As New List(Of String)()
            Dim struttura As New List(Of List(Of Integer))()
            For Each riga As String In righe
                Dim indici As New List(Of Integer)()
                If riga.Trim() <> "" Then
                    For Each pezzo As String In SpezzaRiga(riga.Trim(), 1500)
                        indici.Add(unita.Count)
                        unita.Add(pezzo)
                    Next
                End If
                struttura.Add(indici)
            Next
            Dim elenco As RisultatoElenco = Await TraduciElencoAsync(unita, origine, risultato.LinguaDestinazione)
            risultato.LinguaOrigine = elenco.LinguaOrigine
            If Not elenco.Riuscito Then
                risultato.Errore = elenco.Errore
                Return risultato
            End If
            Dim sb As New StringBuilder()
            For i As Integer = 0 To struttura.Count - 1
                If i > 0 Then sb.Append(vbCrLf)
                Dim parti As New List(Of String)()
                For Each indice As Integer In struttura(i)
                    parti.Add(elenco.Testi(indice))
                Next
                sb.Append(String.Join(" ", parti))
            Next
            risultato.Testo = sb.ToString()
            Return risultato
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.TraduciAsync: " & ex.ToString())
            risultato.Errore = "Errore durante la traduzione: " & ex.Message
            Return risultato
        End Try
    End Function

    ''' <summary>
    ''' Traduce un elenco di testi brevi (righe del browser, paragrafi) restituendo un elenco allineato.
    ''' I testi già tradotti vengono presi dalla memoria, gli altri inviati al motore a blocchi.
    ''' </summary>
    Public Shared Async Function TraduciElencoAsync(testi As IList(Of String), Optional origine As String = "", Optional destinazione As String = "") As Task(Of RisultatoElenco)
        Dim risultato As New RisultatoElenco()
        Try
            If testi Is Nothing OrElse testi.Count = 0 Then Return risultato
            Dim dest As String = If(String.IsNullOrWhiteSpace(destinazione), LinguaDestinazione, NormalizzaCodice(destinazione))
            Dim motoreUsato As String = Motore
            Dim orig As String = NormalizzaCodice(origine)
            If orig = "" Then orig = If(RilevamentoAttivo, Automatica, LinguaOrigine)
            If orig = Automatica AndAlso motoreUsato = MotoreOffline Then
                orig = RilevaLinguaLocale(String.Join(" ", testi.Take(60)))
                If orig = "" Then orig = LinguaOrigine
            End If
            risultato.LinguaOrigine = If(orig = Automatica, "", orig)
            For Each t As String In testi
                risultato.Testi.Add(If(t, ""))
            Next
            If orig <> Automatica AndAlso StessaLingua(orig, dest) Then Return risultato

            Dim daTradurre As New List(Of Integer)()
            For i As Integer = 0 To testi.Count - 1
                Dim t As String = If(testi(i), "")
                If t.Trim() = "" OrElse Not ContieneLettere(t) Then Continue For
                Dim memorizzato As String = Nothing
                If _cache.TryGetValue(ChiaveCache(motoreUsato, orig, dest, t), memorizzato) Then
                    risultato.Testi(i) = memorizzato
                Else
                    daTradurre.Add(i)
                End If
            Next
            If daTradurre.Count = 0 Then Return risultato

            Dim lingueRilevate As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            Dim blocco As New List(Of Integer)()
            Dim caratteri As Integer = 0
            Dim limiteCaratteri As Integer = If(motoreUsato = MotoreGoogle, 4000, 6000)
            Dim limiteElementi As Integer = If(motoreUsato = MotoreGoogle, 120, 60)
            Dim indiceBlocco As Integer = 0
            While indiceBlocco <= daTradurre.Count
                Dim fine As Boolean = indiceBlocco = daTradurre.Count
                If Not fine Then
                    Dim lunghezza As Integer = testi(daTradurre(indiceBlocco)).Length
                    If blocco.Count > 0 AndAlso (caratteri + lunghezza > limiteCaratteri OrElse blocco.Count >= limiteElementi) Then
                        fine = True
                    Else
                        blocco.Add(daTradurre(indiceBlocco))
                        caratteri += lunghezza
                        indiceBlocco += 1
                        Continue While
                    End If
                End If
                If blocco.Count > 0 Then
                    Dim sorgenti As List(Of String) = blocco.Select(Function(i) testi(i)).ToList()
                    Dim tradotti As List(Of String)
                    Dim lingue As New List(Of String)()
                    Dim errore As String = ""
                    If motoreUsato = MotoreOffline Then
                        Dim esito As Tuple(Of List(Of String), String) = Await WGMotoreTraduzioneOffline.TraduciAsync(sorgenti, orig, dest)
                        tradotti = esito.Item1
                        errore = esito.Item2
                    Else
                        Dim esito As Tuple(Of List(Of String), List(Of String), String) = Await TraduciConGoogleAsync(sorgenti, orig, dest)
                        tradotti = esito.Item1
                        lingue = esito.Item2
                        errore = esito.Item3
                    End If
                    If tradotti Is Nothing OrElse tradotti.Count <> blocco.Count Then
                        risultato.Errore = If(errore <> "", errore, "Il servizio di traduzione non ha risposto")
                        Return risultato
                    End If
                    For k As Integer = 0 To blocco.Count - 1
                        risultato.Testi(blocco(k)) = tradotti(k)
                        MemorizzaInCache(ChiaveCache(motoreUsato, orig, dest, testi(blocco(k))), tradotti(k))
                        If k < lingue.Count AndAlso lingue(k) <> "" Then
                            Dim n As Integer = 0
                            lingueRilevate.TryGetValue(lingue(k), n)
                            lingueRilevate(lingue(k)) = n + 1
                        End If
                    Next
                End If
                blocco = New List(Of Integer)()
                caratteri = 0
                If indiceBlocco >= daTradurre.Count Then Exit While
                ' Una breve pausa tra una richiesta e l'altra: Google limita chi invia troppe richieste di fila.
                If motoreUsato = MotoreGoogle Then Await Task.Delay(150)
            End While
            If risultato.LinguaOrigine = "" AndAlso lingueRilevate.Count > 0 Then
                risultato.LinguaOrigine = lingueRilevate.OrderByDescending(Function(p) p.Value).First().Key
            End If
            Return risultato
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.TraduciElencoAsync: " & ex.ToString())
            risultato.Errore = "Errore durante la traduzione: " & ex.Message
            Return risultato
        End Try
    End Function

    Private Shared Function ContieneLettere(testo As String) As Boolean
        For Each c As Char In testo
            If Char.IsLetter(c) Then Return True
        Next
        Return False
    End Function

    Private Shared ReadOnly _fineFrase As New Regex("(?<=[\.\!\?;:…])\s+", RegexOptions.Compiled)

    Private Shared Function SpezzaRiga(riga As String, massimo As Integer) As List(Of String)
        Dim parti As New List(Of String)()
        Try
            If riga.Length <= massimo Then
                parti.Add(riga)
                Return parti
            End If
            Dim corrente As New StringBuilder()
            For Each frase As String In _fineFrase.Split(riga)
                Dim f As String = frase
                While f.Length > massimo
                    If corrente.Length > 0 Then
                        parti.Add(corrente.ToString())
                        corrente.Clear()
                    End If
                    Dim taglio As Integer = f.LastIndexOf(" "c, massimo)
                    If taglio <= 0 Then taglio = massimo
                    parti.Add(f.Substring(0, taglio).Trim())
                    f = f.Substring(taglio).Trim()
                End While
                If corrente.Length + f.Length + 1 > massimo AndAlso corrente.Length > 0 Then
                    parti.Add(corrente.ToString())
                    corrente.Clear()
                End If
                If corrente.Length > 0 Then corrente.Append(" ")
                corrente.Append(f)
            Next
            If corrente.Length > 0 Then parti.Add(corrente.ToString())
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.SpezzaRiga: " & ex.Message)
            parti.Clear()
            parti.Add(riga)
        End Try
        Return parti
    End Function

    Private Shared Function CodiceGoogle(codice As String) As String
        Select Case codice
            Case "zh" : Return "zh-CN"
            Case "nb" : Return "no"
            Case "he" : Return "iw"
        End Select
        Return codice
    End Function

    ' Servizio pubblico di Google Traduttore usato dalle estensioni dei browser: accetta più testi per
    ' richiesta (un parametro q per testo) e restituisce, con la lingua automatica, anche la lingua rilevata.
    ' Il testo viene inviato con i caratteri speciali dell'HTML protetti e la risposta viene decodificata.
    Private Shared Async Function TraduciConGoogleAsync(testi As List(Of String), origine As String, destinazione As String) As Task(Of Tuple(Of List(Of String), List(Of String), String))
        Dim ultimoErrore As String = ""
        WGTraduttore.AssicuraTls()
        For tentativo As Integer = 1 To 3
            Try
                Dim indirizzo As String = "https://translate.googleapis.com/translate_a/t?client=gtx&sl=" & Uri.EscapeDataString(If(origine = Automatica, "auto", CodiceGoogle(origine))) &
                                          "&tl=" & Uri.EscapeDataString(CodiceGoogle(destinazione))
                Dim corpo As New StringBuilder()
                For Each t As String In testi
                    If corpo.Length > 0 Then corpo.Append("&"c)
                    corpo.Append("q=").Append(Uri.EscapeDataString(WebUtility.HtmlEncode(t)))
                Next
                Using contenuto As New StringContent(corpo.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded")
                    Using risposta As HttpResponseMessage = Await _http.PostAsync(indirizzo, contenuto).ConfigureAwait(False)
                        Dim testoRisposta As String = Await risposta.Content.ReadAsStringAsync().ConfigureAwait(False)
                        If Not risposta.IsSuccessStatusCode OrElse Not testoRisposta.TrimStart().StartsWith("[") Then
                            ultimoErrore = If(CInt(risposta.StatusCode) = 429 OrElse testoRisposta.Contains("Sorry"), "Google ha limitato temporaneamente le traduzioni, riprovare fra qualche minuto", "Google non ha risposto correttamente (HTTP " & CInt(risposta.StatusCode) & ")")
                            Await Task.Delay(800 * tentativo).ConfigureAwait(False)
                            Continue For
                        End If
                        Dim elenco As JArray = JArray.Parse(testoRisposta)
                        Dim tradotti As New List(Of String)()
                        Dim lingue As New List(Of String)()
                        For Each elemento As JToken In elenco
                            If elemento.Type = JTokenType.Array Then
                                Dim coppia As JArray = CType(elemento, JArray)
                                tradotti.Add(WebUtility.HtmlDecode(If(coppia.Count > 0, coppia(0).ToString(), "")))
                                lingue.Add(If(coppia.Count > 1, NormalizzaCodice(coppia(1).ToString()), ""))
                            Else
                                tradotti.Add(WebUtility.HtmlDecode(elemento.ToString()))
                                lingue.Add("")
                            End If
                        Next
                        ' Con un solo testo e la lingua indicata Google risponde con la sola stringa.
                        If tradotti.Count = 1 AndAlso testi.Count = 1 Then Return Tuple.Create(tradotti, lingue, "")
                        If tradotti.Count <> testi.Count Then
                            ultimoErrore = "La risposta di Google non corrisponde al testo inviato"
                            Continue For
                        End If
                        Return Tuple.Create(tradotti, lingue, "")
                    End Using
                End Using
            Catch ex As HttpRequestException
                log.ScriviLog("WGTraduttore.TraduciConGoogleAsync rete: " & ex.Message)
                ultimoErrore = "Connessione a internet non disponibile o Google non raggiungibile"
            Catch ex As TaskCanceledException
                log.ScriviLog("WGTraduttore.TraduciConGoogleAsync tempo scaduto")
                ultimoErrore = "Google non risponde"
            Catch ex As Exception
                log.ScriviLog("WGTraduttore.TraduciConGoogleAsync: " & ex.ToString())
                ultimoErrore = "Errore nella traduzione con Google: " & ex.Message
            End Try
        Next
        Return Tuple.Create(Of List(Of String), List(Of String), String)(Nothing, Nothing, ultimoErrore)
    End Function

#End Region

#Region "Rilevamento della lingua"

    ''' <summary>Lingua di un testo: con Google la chiede al servizio, altrimenti usa il rilevamento locale.</summary>
    Public Shared Async Function RilevaLinguaAsync(testo As String) As Task(Of String)
        Try
            Dim campione As String = If(testo, "").Trim()
            If campione.Length > 400 Then campione = campione.Substring(0, 400)
            If campione = "" Then Return ""
            If Motore = MotoreGoogle Then
                Dim esito As Tuple(Of List(Of String), List(Of String), String) = Await TraduciConGoogleAsync(New List(Of String) From {campione}, Automatica, LinguaDestinazione)
                If esito.Item2 IsNot Nothing AndAlso esito.Item2.Count > 0 AndAlso esito.Item2(0) <> "" Then Return esito.Item2(0)
            End If
            Return RilevaLinguaLocale(campione)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.RilevaLinguaAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared ReadOnly _paroleComuni As New Dictionary(Of String, String()) From {
        {"it", {"il", "lo", "la", "gli", "le", "di", "che", "è", "non", "per", "una", "un", "sono", "del", "della", "con", "anche", "questo", "nel", "alla", "come", "più", "ma", "si"}},
        {"en", {"the", "and", "of", "to", "is", "in", "that", "it", "for", "you", "with", "on", "this", "are", "was", "be", "have", "not", "by", "from", "or", "an", "will", "your"}},
        {"fr", {"le", "la", "les", "de", "des", "et", "est", "un", "une", "que", "pour", "dans", "pas", "qui", "sur", "avec", "au", "du", "ce", "vous", "nous", "sont", "il", "elle"}},
        {"de", {"der", "die", "das", "und", "ist", "nicht", "mit", "ein", "eine", "zu", "den", "von", "sie", "es", "auf", "für", "ich", "auch", "dem", "sich", "wir", "sind", "wird", "oder"}},
        {"es", {"el", "la", "los", "las", "de", "que", "y", "en", "es", "un", "una", "por", "con", "para", "no", "se", "del", "al", "lo", "como", "más", "pero", "su", "está"}},
        {"pt", {"o", "a", "os", "as", "de", "que", "e", "do", "da", "em", "um", "uma", "para", "com", "não", "no", "na", "se", "por", "mais", "dos", "das", "é", "ao"}},
        {"nl", {"de", "het", "een", "en", "van", "is", "dat", "niet", "op", "te", "zijn", "met", "voor", "je", "ik", "die", "er", "maar", "ook", "aan", "wordt", "bij", "om", "hij"}},
        {"pl", {"i", "w", "nie", "na", "się", "z", "jest", "że", "do", "to", "jak", "od", "po", "co", "tak", "za", "ale", "jego", "przez", "oraz", "być", "już", "dla", "tylko"}},
        {"cs", {"a", "je", "se", "v", "na", "že", "to", "s", "z", "do", "jsou", "pro", "jak", "ale", "by", "o", "tak", "jsem", "už", "který", "která", "také", "nebo", "při"}},
        {"et", {"ja", "on", "ei", "et", "see", "ka", "oli", "kui", "mis", "aga", "ta", "või", "nii", "siis", "kes", "oma", "seda", "mida", "veel", "pole", "olen", "kas", "juba", "ning"}},
        {"is", {"og", "að", "er", "í", "á", "ekki", "sem", "til", "við", "með", "um", "það", "hann", "hún", "var", "fyrir", "af", "eru", "eða", "en", "þetta", "þá", "frá", "hafa"}},
        {"nb", {"og", "i", "er", "det", "som", "på", "en", "til", "av", "at", "for", "med", "ikke", "har", "den", "de", "om", "et", "fra", "kan", "vi", "jeg", "var", "men"}},
        {"sv", {"och", "i", "att", "det", "som", "en", "på", "är", "av", "för", "med", "till", "den", "inte", "har", "de", "om", "ett", "var", "jag", "vi", "från", "men", "så"}},
        {"da", {"og", "i", "at", "det", "er", "en", "til", "på", "af", "med", "for", "den", "ikke", "som", "de", "har", "et", "om", "var", "jeg", "vi", "fra", "men", "så"}},
        {"ro", {"și", "în", "de", "la", "este", "nu", "cu", "pe", "o", "un", "că", "din", "se", "pentru", "mai", "care", "sunt", "ca", "sau", "dar", "fost", "acest", "această", "lui"}},
        {"tr", {"ve", "bir", "bu", "da", "de", "için", "ile", "çok", "ne", "var", "olarak", "daha", "gibi", "ama", "en", "değil", "o", "kadar", "sonra", "her", "mi", "ben", "olan", "yok"}},
        {"fi", {"ja", "on", "ei", "se", "että", "oli", "hän", "ovat", "kun", "mutta", "tai", "myös", "jos", "niin", "kuin", "minä", "sen", "ole", "olla", "vain", "joka", "nyt", "tämä", "mitä"}},
        {"hu", {"a", "az", "és", "hogy", "nem", "is", "egy", "van", "meg", "de", "ez", "csak", "már", "ha", "mint", "még", "vagy", "volt", "el", "kell", "azt", "sem", "lesz", "itt"}},
        {"id", {"yang", "dan", "di", "ini", "itu", "dengan", "untuk", "tidak", "dari", "dalam", "akan", "pada", "ada", "juga", "saya", "ke", "bisa", "kami", "karena", "sudah", "atau", "mereka", "lebih", "oleh"}},
        {"vi", {"và", "của", "là", "có", "không", "được", "một", "những", "cho", "trong", "này", "với", "các", "người", "đã", "để", "khi", "thì", "cũng", "như", "từ", "đến", "tôi", "sẽ"}},
        {"hr", {"i", "je", "u", "se", "na", "da", "za", "su", "od", "ne", "s", "koji", "kao", "ali", "što", "sam", "to", "biti", "iz", "ili", "bio", "će", "nije", "samo"}},
        {"sk", {"a", "je", "sa", "v", "na", "že", "to", "s", "z", "do", "sú", "pre", "ako", "ale", "by", "o", "tak", "som", "už", "ktorý", "ktorá", "tiež", "alebo", "pri"}},
        {"sl", {"in", "je", "v", "se", "na", "da", "za", "so", "ki", "ne", "z", "pa", "kot", "tudi", "bi", "sem", "to", "iz", "ali", "bil", "bo", "še", "ni", "samo"}},
        {"ca", {"el", "la", "els", "les", "de", "que", "i", "en", "és", "un", "una", "per", "amb", "no", "es", "del", "al", "com", "més", "però", "seu", "està", "aquest", "també"}},
        {"lt", {"ir", "yra", "kad", "su", "į", "iš", "ne", "bet", "tai", "jis", "ji", "buvo", "kaip", "o", "ar", "už", "apie", "po", "dar", "tik", "jo", "kuris", "labai", "mes"}},
        {"lv", {"un", "ir", "ka", "ar", "uz", "no", "par", "bet", "tas", "kā", "vai", "arī", "bija", "to", "viņš", "nav", "es", "mēs", "tikai", "kas", "lai", "pēc", "jau", "ļoti"}},
        {"sq", {"dhe", "në", "të", "është", "për", "një", "me", "nga", "që", "nuk", "si", "por", "ka", "do", "u", "janë", "më", "edhe", "ai", "ajo", "kjo", "ky", "duke", "ishte"}},
        {"af", {"die", "en", "van", "is", "het", "in", "nie", "wat", "vir", "met", "op", "dit", "te", "om", "sy", "hy", "was", "ek", "ons", "hulle", "maar", "ook", "kan", "word"}}
    }

    ''' <summary>Rilevamento senza internet: alfabeto per le scritture non latine, parole comuni per le altre.</summary>
    Public Shared Function RilevaLinguaLocale(testo As String) As String
        Try
            Dim t As String = If(testo, "")
            If t.Trim() = "" Then Return ""
            Dim cirillico As Integer = 0, arabo As Integer = 0, greco As Integer = 0, cjk As Integer = 0, kana As Integer = 0, hangul As Integer = 0, ebraico As Integer = 0, devanagari As Integer = 0, latino As Integer = 0
            ' Scritture che appartengono a una sola lingua: bengalese, gujarati, tamil, telugu, kannada, malayalam, thailandese.
            Dim scritture As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            Dim intervalli As Tuple(Of Integer, Integer, String)() = {
                Tuple.Create(&H980, &H9FF, "bn"), Tuple.Create(&HA80, &HAFF, "gu"), Tuple.Create(&HB80, &HBFF, "ta"),
                Tuple.Create(&HC00, &HC7F, "te"), Tuple.Create(&HC80, &HCFF, "kn"), Tuple.Create(&HD00, &HD7F, "ml"), Tuple.Create(&HE00, &HE7F, "th")
            }
            For Each c As Char In t
                Dim n As Integer = AscW(c)
                Dim unica As Tuple(Of Integer, Integer, String) = intervalli.FirstOrDefault(Function(i) n >= i.Item1 AndAlso n <= i.Item2)
                If unica IsNot Nothing Then
                    Dim conteggio As Integer = 0
                    scritture.TryGetValue(unica.Item3, conteggio)
                    scritture(unica.Item3) = conteggio + 1
                ElseIf n >= &H400 AndAlso n <= &H4FF Then
                    cirillico += 1
                ElseIf n >= &H600 AndAlso n <= &H6FF Then
                    arabo += 1
                ElseIf n >= &H370 AndAlso n <= &H3FF Then
                    greco += 1
                ElseIf n >= &H3040 AndAlso n <= &H30FF Then
                    kana += 1
                ElseIf n >= &H4E00 AndAlso n <= &H9FFF Then
                    cjk += 1
                ElseIf n >= &HAC00 AndAlso n <= &HD7AF Then
                    hangul += 1
                ElseIf n >= &H590 AndAlso n <= &H5FF Then
                    ebraico += 1
                ElseIf n >= &H900 AndAlso n <= &H97F Then
                    devanagari += 1
                ElseIf Char.IsLetter(c) Then
                    latino += 1
                End If
            Next
            Dim massimaScrittura As Integer = If(scritture.Count > 0, scritture.Values.Max(), 0)
            Dim massimo As Integer = {cirillico, arabo, greco, cjk + kana, hangul, ebraico, devanagari, latino, massimaScrittura}.Max()
            If massimo = 0 Then Return ""
            If massimaScrittura > 0 AndAlso massimo = massimaScrittura Then Return scritture.First(Function(p) p.Value = massimaScrittura).Key
            If massimo = kana + cjk Then Return If(kana > 0, "ja", "zh")
            If massimo = hangul Then Return "ko"
            If massimo = ebraico Then Return "he"
            If massimo = devanagari Then Return If(Regex.IsMatch(t, "(आहे|आणि|नाही|हे|आहेत)"), "mr", "hi")
            If massimo = greco Then Return "el"
            If massimo = arabo Then
                If Regex.IsMatch(t, "[ٹڈڑںے]") Then Return "ur"
                Return If(Regex.IsMatch(t, "[پچژگی]"), "fa", "ar")
            End If
            If massimo = cirillico Then
                Dim minuscolo As String = t.ToLowerInvariant()
                If Regex.IsMatch(minuscolo, "[іїєґ]") Then Return "uk"
                If Regex.IsMatch(minuscolo, "[ђћџљњј]") Then Return "sr"
                If Regex.IsMatch(minuscolo, "[ыэё]") Then Return "ru"
                If minuscolo.Count(Function(c) c = "ъ"c) > 0 Then Return "bg"
                Return "ru"
            End If
            Dim parole As String() = Regex.Split(t.ToLowerInvariant(), "[^\p{L}]+").Where(Function(p) p <> "").ToArray()
            Dim migliore As String = ""
            Dim punteggioMigliore As Integer = 0
            For Each coppia As KeyValuePair(Of String, String()) In _paroleComuni
                Dim insieme As New HashSet(Of String)(coppia.Value)
                Dim punteggio As Integer = parole.Count(Function(p) insieme.Contains(p))
                If punteggio > punteggioMigliore Then
                    punteggioMigliore = punteggio
                    migliore = coppia.Key
                End If
            Next
            Return migliore
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.RilevaLinguaLocale: " & ex.Message)
            Return ""
        End Try
    End Function

#End Region

#Region "File"

    Private Shared ReadOnly EstensioniTraducibili As String() = {".txt", ".docx", ".rtf", ".md", ".html", ".htm", ".xml", ".json", ".log", ".ini", ".csv", ".odt"}

    ''' <summary>File di testo che il traduttore sa leggere: testo semplice, docx, rtf, html. Mai i pdf.</summary>
    Public Shared Function FileTraducibile(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            Return EstensioniTraducibili.Contains(Path.GetExtension(percorso).ToLowerInvariant())
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.FileTraducibile: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function EstraiTestoDaFile(percorso As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return ""
            Select Case Path.GetExtension(percorso).ToLowerInvariant()
                Case ".docx", ".odt"
                    Return WGWord.EstraiTestoCompleto(percorso)
                Case ".rtf"
                    Using rtf As New RichTextBox()
                        rtf.Rtf = File.ReadAllText(percorso, Encoding.Default)
                        Return rtf.Text
                    End Using
                Case ".html", ".htm"
                    Return WGAi.TestoDaHtml(File.ReadAllText(percorso, Encoding.UTF8))
                Case Else
                    Return File.ReadAllText(percorso, Encoding.UTF8)
            End Select
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.EstraiTestoDaFile: " & ex.Message)
            Return ""
        End Try
    End Function

#End Region

#Region "Interfaccia"

    ''' <summary>Traduce con l'attesa sullo schermo e mostra il risultato in un campo di sola lettura.</summary>
    Public Shared Async Sub TraduciEMostra(testo As String, titolo As String, Optional percorsoOrigine As String = "")
        Dim processo As WGProcessoInCorso = Nothing
        Try
            If String.IsNullOrWhiteSpace(testo) Then
                Messaggio("Non c'è testo da tradurre.")
                Return
            End If
            processo = New WGProcessoInCorso()
            processo.Avvia("Traduzione in corso, attendere", False)
            Dim r As Risultato = Await TraduciAsync(testo)
            processo.Fine()
            processo = Nothing
            If Not r.Riuscito Then
                Messaggio("Traduzione non riuscita: " & r.Errore)
                Return
            End If
            Dim intestazione As String = If(String.IsNullOrWhiteSpace(titolo), "Traduzione", titolo)
            If r.LinguaOrigine <> "" Then intestazione &= ", " & DallaLingua(r.LinguaOrigine) & " " & ALingua(r.LinguaDestinazione)
            Dim campo As New WGCampo()
            campo.init(intestazione, r.Testo, True, True, False, False)
            If Not String.IsNullOrWhiteSpace(percorsoOrigine) Then
                Dim testoTradotto As String = r.Testo
                campo.aggiungiopzione("Salva la traduzione accanto al file originale", Sub(s, e) SalvaTraduzioneFile(percorsoOrigine, testoTradotto, r.LinguaDestinazione))
            End If
            campo.aggiungiopzione("Copia la traduzione negli appunti", Sub(s, e) CopiaNegliAppunti(r.Testo))
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.TraduciEMostra: " & ex.ToString())
        Finally
            Try
                If processo IsNot Nothing Then processo.Fine()
            Catch exFine As Exception
                log.ScriviLog("WGTraduttore.TraduciEMostra fine: " & exFine.Message)
            End Try
        End Try
    End Sub

    ''' <summary>"in italiano", "in inglese".</summary>
    Public Shared Function ALingua(codice As String) As String
        Return "in " & NomeLingua(codice).ToLowerInvariant()
    End Function

    ''' <summary>Traduce un file di testo, docx o rtf e ne mostra la traduzione.</summary>
    Public Shared Sub TraduciFile(percorso As String)
        Try
            If Not FileTraducibile(percorso) Then
                Messaggio("Questo tipo di file non si può tradurre. Si possono tradurre file di testo, documenti Word e rtf.")
                Return
            End If
            Dim testo As String = EstraiTestoDaFile(percorso)
            If String.IsNullOrWhiteSpace(testo) Then
                Messaggio("Il file non contiene testo da tradurre.")
                Return
            End If
            TraduciEMostra(testo, "Traduzione di " & Path.GetFileName(percorso), percorso)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.TraduciFile: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub SalvaTraduzioneFile(percorsoOrigine As String, testo As String, lingua As String)
        Try
            Dim cartella As String = Path.GetDirectoryName(percorsoOrigine)
            Dim nome As String = Path.GetFileNameWithoutExtension(percorsoOrigine) & " (" & NomeLingua(lingua).ToLowerInvariant() & ")"
            Dim destinazione As String = Path.Combine(cartella, nome & ".txt")
            Dim n As Integer = 2
            While File.Exists(destinazione)
                destinazione = Path.Combine(cartella, nome & " " & n & ".txt")
                n += 1
            End While
            File.WriteAllText(destinazione, testo, New UTF8Encoding(True))
            Messaggio("Traduzione salvata nel file " & Path.GetFileName(destinazione))
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.SalvaTraduzioneFile: " & ex.ToString())
            Messaggio("Impossibile salvare la traduzione: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub CopiaNegliAppunti(testo As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            Clipboard.SetText(testo)
            Messaggio("Traduzione copiata negli appunti")
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.CopiaNegliAppunti: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.Messaggio: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Menu Web, Traduttore.</summary>
    Public Shared Sub MostraMenuTraduttore(Optional indice As Integer = 0)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Scrivi o incolla il testo da tradurre", Sub(s, e) ChiediTestoDaTradurre())
            menu.aggiungi("Traduci il testo degli appunti", Sub(s, e) TraduciAppunti())
            menu.aggiungi("Traduci un file", Sub(s, e) ScegliFileDaTradurre())
            menu.aggiungi("Lingua di destinazione: " & NomeLingua(LinguaDestinazione), Sub(s, e) ScegliLingua(True, Sub() MostraMenuTraduttore(3)))
            menu.aggiungi("Impostazioni del traduttore", Sub(s, e) MostraImpostazioni())
            menu.posiziona(indice)
            menu.avvia("Traduttore", True)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.MostraMenuTraduttore: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ChiediTestoDaTradurre()
        Try
            Dim campi As New Campi()
            campi.AggiungiMultiriga("Testo da tradurre " & ALingua(LinguaDestinazione), "testo", True)
            AddHandler campi.Fine, Sub(s, e)
                                       Try
                                           Dim testo As String = campi.RecuperaValore("testo")
                                           If Not String.IsNullOrWhiteSpace(testo) Then TraduciEMostra(testo, "Traduzione")
                                       Catch exFine As Exception
                                           log.ScriviLog("WGTraduttore.ChiediTestoDaTradurre fine: " & exFine.Message)
                                       End Try
                                   End Sub
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ChiediTestoDaTradurre: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub TraduciAppunti()
        Try
            Dim testo As String = If(Clipboard.ContainsText(), Clipboard.GetText(), "")
            If String.IsNullOrWhiteSpace(testo) Then
                Messaggio("Negli appunti non c'è testo da tradurre.")
                Return
            End If
            TraduciEMostra(testo, "Traduzione degli appunti")
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.TraduciAppunti: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScegliFileDaTradurre()
        Try
            Dim dialogo As New WGFileDialog()
            dialogo.MultiselezioneConsentita = False
            dialogo.allowedExtensions.AddRange(EstensioniTraducibili)
            AddHandler dialogo.singolaselezione, Sub(elemento As String)
                                                     If File.Exists(elemento) Then TraduciFile(elemento)
                                                 End Sub
            AddHandler dialogo.selezioneterminata, Sub(elementi As List(Of String))
                                                       If elementi IsNot Nothing AndAlso elementi.Count > 0 AndAlso File.Exists(elementi(0)) Then TraduciFile(elementi(0))
                                                   End Sub
            dialogo.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ScegliFileDaTradurre: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Impostazioni, Traduttore: motore, rilevamento della lingua, lingua di destinazione.</summary>
    Public Shared Sub MostraImpostazioni(Optional indice As Integer = 0)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Motore di traduzione: " & If(Motore = MotoreOffline, "Offline, senza internet", "Google, online"), Sub(s, e) ScegliMotore())
            menu.aggiungi("Rilevamento della lingua: " & If(RilevamentoAttivo, "attivo", "disattivato"), Sub(s, e) ScegliRilevamento())
            menu.aggiungi("Lingua di destinazione: " & NomeLingua(LinguaDestinazione), Sub(s, e) ScegliLingua(True, Sub() MostraImpostazioni(2)))
            menu.aggiungi("Lingua di partenza quando il rilevamento è disattivato: " & NomeLingua(LinguaOrigine), Sub(s, e) ScegliLingua(False, Sub() MostraImpostazioni(3)))
            menu.aggiungi("Voce della lingua letta, quando non si traduce: " & If(CambioVoceAutomatico, "cambia automaticamente se installata", "resta quella predefinita"), Sub(s, e) ScegliCambioVoce())
            menu.aggiungi("Tabella braille della lingua letta, quando non si traduce: " & If(CambioTabellaBrailleAutomatico, "cambia automaticamente se disponibile", "resta quella predefinita"), Sub(s, e) ScegliCambioTabellaBraille())
            Dim escluse As List(Of String) = ElencoLingueEscluse()
            menu.aggiungi("Lingue da non tradurre mai: " & If(escluse.Count = 0, "nessuna", String.Join(", ", escluse.Select(Function(c) NomeLingua(c)))), Sub(s, e) ScegliLingueEscluse())
            If Motore = MotoreOffline Then
                Dim scaricate As Integer = WGMotoreTraduzioneOffline.CoppieScaricate().Count
                menu.aggiungi("Elimina i modelli offline scaricati: " & scaricate & If(scaricate = 1, " direzione di traduzione", " direzioni di traduzione"), Sub(s, e) EliminaModelliOffline())
            End If
            menu.posiziona(indice)
            menu.avvia("Traduttore", True)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.MostraImpostazioni: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScegliMotore()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Google: online, tutte le lingue, traduzione più accurata", Sub(s, e)
                                                                                           Motore = MotoreGoogle
                                                                                           Messaggio("Motore di traduzione: Google")
                                                                                           MostraImpostazioni(0)
                                                                                       End Sub)
            menu.aggiungi("Offline: funziona senza internet con i modelli di Firefox in oltre 50 lingue; scarica il motore e circa 40 megabyte per ciascuna lingua utilizzata", Sub(s, e) AttivaOffline())
            menu.posiziona(If(Motore = MotoreOffline, 1, 0))
            menu.avvia("Motore di traduzione", True)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ScegliMotore: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScegliRilevamento()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attivo: la lingua dei testi viene riconosciuta automaticamente", Sub(s, e)
                                                                                         RilevamentoAttivo = True
                                                                                         Messaggio("Rilevamento della lingua attivato")
                                                                                         MostraImpostazioni(1)
                                                                                     End Sub)
            menu.aggiungi("Disattivato: i testi vengono considerati sempre nella lingua scelta", Sub(s, e)
                                                                                                 RilevamentoAttivo = False
                                                                                                 Messaggio("Rilevamento della lingua disattivato: i testi vengono considerati in " & NomeLingua(LinguaOrigine).ToLowerInvariant())
                                                                                                 MostraImpostazioni(1)
                                                                                             End Sub)
            menu.posiziona(If(RilevamentoAttivo, 0, 1))
            menu.avvia("Rilevamento della lingua", True)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ScegliRilevamento: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScegliCambioVoce()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Cambia automaticamente, se la voce per quella lingua è installata sul computer", Sub(s, e)
                                                                                                  CambioVoceAutomatico = True
                                                                                                  Messaggio("La voce cambierà automaticamente in base alla lingua del testo letto, se installata sul computer")
                                                                                                  MostraImpostazioni(4)
                                                                                              End Sub)
            menu.aggiungi("Resta sempre la voce scelta", Sub(s, e)
                                                              CambioVoceAutomatico = False
                                                              Messaggio("La voce rimarrà sempre quella scelta")
                                                              MostraImpostazioni(4)
                                                          End Sub)
            menu.posiziona(If(CambioVoceAutomatico, 0, 1))
            menu.avvia("Voce della lingua letta, quando non si traduce", True)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ScegliCambioVoce: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScegliCambioTabellaBraille()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Cambia automaticamente, se è presente una tabella per quella lingua", Sub(s, e)
                                                                                           CambioTabellaBrailleAutomatico = True
                                                                                           Messaggio("La tabella braille cambierà automaticamente in base alla lingua del testo letto, se disponibile")
                                                                                           MostraImpostazioni(5)
                                                                                       End Sub)
            menu.aggiungi("Resta sempre la tabella scelta", Sub(s, e)
                                                              CambioTabellaBrailleAutomatico = False
                                                              Messaggio("La tabella braille rimarrà sempre quella scelta")
                                                              MostraImpostazioni(5)
                                                          End Sub)
            menu.posiziona(If(CambioTabellaBrailleAutomatico, 0, 1))
            menu.avvia("Tabella braille della lingua letta, quando non si traduce", True)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ScegliCambioTabellaBraille: " & ex.ToString())
        End Try
    End Sub

    Private Shared Async Sub AttivaOffline()
        Try
            Dim destinazione As String = LinguaDestinazione
            If Not WGMotoreTraduzioneOffline.LinguaDisponibile(destinazione) Then
                Messaggio("Il traduttore offline non comprende il " & NomeLingua(destinazione).ToLowerInvariant() & ". Lingue disponibili senza internet: " & WGMotoreTraduzioneOffline.DescrizioneLingueDisponibili() & ".")
                MostraImpostazioni(0)
                Return
            End If
            Dim origine As String = If(StessaLingua(destinazione, "en"), LinguaOrigine, "en")
            Dim errore As String = Await WGMotoreTraduzioneOffline.PreparaAsync(origine, destinazione,
                Sub(esito As String)
                    If esito <> "" Then Return
                    Motore = MotoreOffline
                    Messaggio("Traduttore offline pronto. I modelli delle altre lingue vengono scaricati la prima volta che servono.")
                End Sub)
            If errore = WGMotoreTraduzioneOffline.ScaricamentoAvviato Then
                Messaggio("Ho avviato lo scaricamento del traduttore offline: puoi continuare a lavorare, lo trovi nel centro di controllo. Quando è pronto lo attivo e ti avviso.")
                Return
            End If
            If errore <> "" Then
                Messaggio("Impossibile preparare il traduttore offline: " & errore)
                MostraImpostazioni(0)
                Return
            End If
            Motore = MotoreOffline
            Messaggio("Traduttore offline pronto. I modelli delle altre lingue vengono scaricati la prima volta che servono.")
            MostraImpostazioni(0)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.AttivaOffline: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub EliminaModelliOffline()
        Try
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, Sub(risposta As DialogResult)
                                                    If risposta <> DialogResult.Yes Then Return
                                                    WGMotoreTraduzioneOffline.EliminaModelli()
                                                    Motore = MotoreGoogle
                                                    Messaggio("Modelli offline eliminati. Il motore di traduzione torna a essere Google.")
                                                    MostraImpostazioni(0)
                                                End Sub
            domanda.avvia("Eliminare i modelli offline scaricati? Il traduttore tornerà a usare Google")
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.EliminaModelliOffline: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ScegliLingua(destinazione As Boolean, dopo As Action)
        Try
            Dim menu As New WGMenu()
            Dim attuale As String = If(destinazione, LinguaDestinazione, LinguaOrigine)
            Dim offline As Boolean = Motore = MotoreOffline
            Dim posizione As Integer = 0
            Dim contatore As Integer = 0
            For Each voce As String() In Lingue
                Dim codice As String = voce(0)
                Dim nome As String = voce(1)
                If offline AndAlso Not WGMotoreTraduzioneOffline.LinguaDisponibile(codice, destinazione) Then Continue For
                If codice = attuale Then posizione = contatore
                contatore += 1
                menu.aggiungi(nome, Sub(s, e)
                                        Try
                                            If destinazione Then LinguaDestinazione = codice Else LinguaOrigine = codice
                                            Messaggio(If(destinazione, "I testi verranno tradotti " & ALingua(codice), "Con il rilevamento disattivato i testi vengono considerati " & ALingua(codice)))
                                            If dopo IsNot Nothing Then dopo()
                                        Catch exScelta As Exception
                                            log.ScriviLog("WGTraduttore.ScegliLingua scelta: " & exScelta.Message)
                                        End Try
                                    End Sub)
            Next
            menu.posiziona(posizione)
            menu.avvia(If(destinazione, "Lingua di destinazione", "Lingua dei testi"), True)
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ScegliLingua: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Elenco con la spunta: le lingue scelte qui non vengono piu' tradotte da sole (nei siti o nei
    ''' documenti), anche se diverse dalla lingua di destinazione. Non impedisce di tradurre a mano su richiesta.</summary>
    Private Shared Sub ScegliLingueEscluse()
        Try
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = True
            lista.impostaTitolo("Lingue da non tradurre mai")
            Dim escluseAttuali As List(Of String) = ElencoLingueEscluse()
            For Each voce As String() In Lingue
                lista.aggiungiElemento(voce(1), voce(0), escluseAttuali.Contains(voce(0)))
            Next
            AddHandler lista.escPremuto, Sub() lista.abbandona()
            AddHandler lista.ElementiSelezionati, Sub(ids As List(Of String))
                                                       Try
                                                           lista.abbandona()
                                                           Dim nuoveEscluse As New HashSet(Of String)(ids)
                                                           For Each voce As String() In Lingue
                                                               ImpostaLinguaEsclusa(voce(0), nuoveEscluse.Contains(voce(0)))
                                                           Next
                                                           Dim elencoFinale As List(Of String) = ElencoLingueEscluse()
                                                           Messaggio(If(elencoFinale.Count = 0, "Nessuna lingua esclusa dalla traduzione", "Lingue escluse dalla traduzione automatica: " & String.Join(", ", elencoFinale.Select(Function(c) NomeLingua(c)))))
                                                           MostraImpostazioni(5)
                                                       Catch exScelta As Exception
                                                           log.ScriviLog("WGTraduttore.ScegliLingueEscluse scelta: " & exScelta.Message)
                                                       End Try
                                                   End Sub
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGTraduttore.ScegliLingueEscluse: " & ex.ToString())
        End Try
    End Sub

#End Region

End Class

''' <summary>
''' Motore di traduzione offline: il traduttore Bergamot (lo stesso di Firefox, in WebAssembly) eseguito
''' in un WebView2 invisibile su un thread suo. Il motore e i modelli si scaricano una volta nella cartella
''' dati "traduttore"; il WebView2 ha un profilo separato da quello del browser e si chiude da solo dopo
''' qualche minuto di inattività per liberare la memoria.
''' </summary>
Friend NotInheritable Class WGMotoreTraduzioneOffline

    Private Shared ReadOnly log As New Logger()
    Private Const VersioneMotore As String = "0.4.9"
    Private Const IndirizzoMotore As String = "https://cdn.jsdelivr.net/npm/@browsermt/bergamot-translator@" & VersioneMotore & "/worker/"
    ' Catalogo dei modelli di traduzione di Firefox (Mozilla Remote Settings) e server dei file allegati.
    Private Const IndirizzoCatalogo As String = "https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/translations-models/records"
    Private Const IndirizzoAllegati As String = "https://firefox-settings-attachments.cdn.mozilla.net/"
    Private Const GiorniValiditaCatalogo As Integer = 30
    Private Const NomeHost As String = "wgtraduttore.wingabriel"
    Private Const NomeHostModelli As String = "wgmodelli.wingabriel"
    Private Const MinutiInattivita As Integer = 5

    Private Shared ReadOnly _http As New HttpClient() With {.Timeout = TimeSpan.FromMinutes(10)}
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _lockScaricamento As New SemaphoreSlim(1, 1)
    Private Shared ReadOnly _attese As New ConcurrentDictionary(Of Integer, TaskCompletionSource(Of JObject))()
    Private Shared _contatore As Integer = 0
    Private Shared _thread As Thread
    Private Shared _form As Form
    Private Shared _webView As WebView2
    Private Shared _pronto As TaskCompletionSource(Of Boolean)
    Private Shared _timerInattivita As System.Windows.Forms.Timer
    Private Shared _ultimoUso As DateTime = DateTime.Now
    Private Shared _catalogo As Dictionary(Of String, ModelloCoppia)

    Private Class FileModello
        Public Tipo As String = ""
        Public Nome As String = ""
        Public Posizione As String = ""
        Public Hash As String = ""
    End Class

    Private Class ModelloCoppia
        Public Da As String = ""
        Public A As String = ""
        Public Versione As String = ""
        Public File As New List(Of FileModello)()
    End Class

    ' Lingue dei modelli di Firefox in versione di rilascio, usate finché il catalogo non è stato letto.
    ' Tutte passano dall'inglese: verso l'inglese e dall'inglese (l'azero solo come lingua di arrivo).
    Private Shared ReadOnly LingueVersoInglese As String() = {"af", "ar", "bg", "bn", "bs", "ca", "cs", "da", "de", "el", "es", "et", "eu", "fa", "fi", "fr", "gl", "gu", "he", "hi", "hr", "hu", "id", "is", "it", "ja", "kn", "ko", "lt", "lv", "ml", "mr", "ms", "nb", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sq", "sr", "sv", "ta", "te", "th", "tr", "uk", "ur", "vi", "zh-Hans", "zh-Hant"}
    Private Shared ReadOnly LingueDallInglese As String() = {"af", "ar", "az", "bg", "bn", "bs", "ca", "cs", "da", "de", "el", "es", "et", "eu", "fa", "fi", "fr", "gl", "gu", "he", "hi", "hr", "hu", "id", "is", "it", "ja", "kn", "ko", "lt", "lv", "ml", "mr", "ms", "nb", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sr", "sv", "ta", "te", "th", "tr", "uk", "ur", "vi", "zh-Hans", "zh-Hant"}

    ''' <summary>Codice della lingua nei modelli di Firefox: il cinese è il cinese semplificato.</summary>
    Private Shared Function CodiceModello(codice As String) As String
        Dim c As String = WGTraduttore.NormalizzaCodice(codice)
        Return If(c = "zh", "zh-Hans", c)
    End Function

    Private Shared Function ChiaveCoppia(da As String, a As String) As String
        Return da & "-" & a
    End Function

    Public Shared Function LinguaDisponibile(codice As String, Optional comeDestinazione As Boolean = True) As Boolean
        Dim c As String = CodiceModello(codice)
        If c = "en" Then Return True
        Dim catalogo As Dictionary(Of String, ModelloCoppia) = CatalogoLocale()
        If catalogo IsNot Nothing AndAlso catalogo.Count > 0 Then
            Return catalogo.ContainsKey(If(comeDestinazione, ChiaveCoppia("en", c), ChiaveCoppia(c, "en")))
        End If
        Return If(comeDestinazione, LingueDallInglese, LingueVersoInglese).Contains(c)
    End Function

    Public Shared Function DescrizioneLingueDisponibili() As String
        Dim nomi As New List(Of String)()
        For Each voce As String() In WGTraduttore.Lingue
            If LinguaDisponibile(voce(0)) Then nomi.Add(voce(1).ToLowerInvariant())
        Next
        Return String.Join(", ", nomi)
    End Function

    ' I modelli di traduzione sono modelli di intelligenza artificiale: stanno in models\traduttore, come tutti gli altri.
    Private Shared Function CartellaModelliTraduzione() As String
        Dim percorso As String = Path.Combine(WGDb.WGPercorsiApplicazione.CartellaModelli(), "traduttore")
        Directory.CreateDirectory(percorso)
        Return percorso
    End Function

    Private Shared Function Cartella(Optional sottocartella As String = "") As String
        ' il traduttore (motore, modelli, profilo) è un programma: sta nella radice dell'applicazione, non nei dati
        Dim baseDati As String = WGDb.WGPercorsiApplicazione.CartellaComponente("traduttore")
        Dim percorso As String = If(sottocartella = "", baseDati, Path.Combine(baseDati, sottocartella))
        Directory.CreateDirectory(percorso)
        Return percorso
    End Function

    ''' <summary>Direzioni di traduzione già scaricate, come "en-it".</summary>
    Public Shared Function CoppieScaricate() As List(Of String)
        Try
            Dim modelli As String = CartellaModelliTraduzione()
            Return Directory.GetDirectories(modelli).Select(Function(d) Path.GetFileName(d)).Where(Function(n) File.Exists(Path.Combine(modelli, n, "completo"))).ToList()
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.CoppieScaricate: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    Public Shared Sub EliminaModelli()
        Try
            Chiudi()
            Dim modelli As String = CartellaModelliTraduzione()
            For Each d As String In Directory.GetDirectories(modelli)
                Try
                    Directory.Delete(d, True)
                Catch exCartella As Exception
                    log.ScriviLog("WGMotoreTraduzioneOffline.EliminaModelli " & d & ": " & exCartella.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.EliminaModelli: " & ex.Message)
        End Try
    End Sub

#Region "Scaricamento"

    ' Dal catalogo si prende, per ogni direzione, la versione più recente pubblicata per Firefox desktop:
    ' si scartano le versioni di prova (con la "a") e i modelli riservati a Nightly o ad Android.
    Private Shared Function LeggiCatalogo(testo As String) As Dictionary(Of String, ModelloCoppia)
        Dim risultato As New Dictionary(Of String, ModelloCoppia)(StringComparer.Ordinal)
        Try
            Dim dati As JArray = TryCast(JObject.Parse(testo)("data"), JArray)
            If dati Is Nothing Then Return risultato
            Dim filtriAmmessi As String() = {"", "env.appinfo.OS != 'Android' || env.channel != 'release'"}
            Dim versioni As New Dictionary(Of String, Version)(StringComparer.Ordinal)
            Dim ammessi As New List(Of JObject)()
            For Each record As JObject In dati
                Dim versione As String = If(record.Value(Of String)("version"), "")
                Dim filtro As String = If(record.Value(Of String)("filter_expression"), "").Trim()
                Dim numero As Version = Nothing
                If versione.Contains("a") OrElse Not filtriAmmessi.Contains(filtro) OrElse Not Version.TryParse(versione, numero) Then Continue For
                Dim chiave As String = ChiaveCoppia(record.Value(Of String)("fromLang"), record.Value(Of String)("toLang"))
                ammessi.Add(record)
                Dim attuale As Version = Nothing
                If Not versioni.TryGetValue(chiave, attuale) OrElse numero > attuale Then versioni(chiave) = numero
            Next
            For Each record As JObject In ammessi
                Dim chiave As String = ChiaveCoppia(record.Value(Of String)("fromLang"), record.Value(Of String)("toLang"))
                If Version.Parse(record.Value(Of String)("version")) <> versioni(chiave) Then Continue For
                Dim allegato As JObject = TryCast(record("attachment"), JObject)
                If allegato Is Nothing Then Continue For
                Dim modello As ModelloCoppia = Nothing
                If Not risultato.TryGetValue(chiave, modello) Then
                    modello = New ModelloCoppia() With {.Da = record.Value(Of String)("fromLang"), .A = record.Value(Of String)("toLang"), .Versione = record.Value(Of String)("version")}
                    risultato(chiave) = modello
                End If
                modello.File.Add(New FileModello() With {
                    .Tipo = record.Value(Of String)("fileType"), .Nome = record.Value(Of String)("name"),
                    .Posizione = allegato.Value(Of String)("location"), .Hash = allegato.Value(Of String)("hash")
                })
            Next
            ' Una direzione è utilizzabile solo con modello, lessico e vocabolario (unico o separato).
            For Each chiave As String In risultato.Keys.ToList()
                Dim tipi As HashSet(Of String) = New HashSet(Of String)(risultato(chiave).File.Select(Function(f) f.Tipo))
                Dim completo As Boolean = tipi.Contains("model") AndAlso tipi.Contains("lex") AndAlso (tipi.Contains("vocab") OrElse (tipi.Contains("srcvocab") AndAlso tipi.Contains("trgvocab")))
                If Not completo Then risultato.Remove(chiave)
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.LeggiCatalogo: " & ex.Message)
        End Try
        Return risultato
    End Function

    ' Catalogo già salvato sul disco, senza andare in rete: serve alle scelte dei menu.
    Private Shared Function CatalogoLocale() As Dictionary(Of String, ModelloCoppia)
        Try
            If _catalogo IsNot Nothing Then Return _catalogo
            Dim percorso As String = Path.Combine(Cartella(), "catalogo.json")
            If Not File.Exists(percorso) Then Return Nothing
            _catalogo = LeggiCatalogo(File.ReadAllText(percorso, Encoding.UTF8))
            Return _catalogo
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.CatalogoLocale: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ' Catalogo aggiornato: si riscarica dopo un mese; senza internet si usa quello salvato.
    Private Shared Async Function CatalogoAsync() As Task(Of Dictionary(Of String, ModelloCoppia))
        Dim percorso As String = Path.Combine(Cartella(), "catalogo.json")
        Try
            Dim scaduto As Boolean = Not File.Exists(percorso) OrElse (DateTime.Now - File.GetLastWriteTime(percorso)).TotalDays > GiorniValiditaCatalogo
            If scaduto Then
                WGTraduttore.AssicuraTls()
                Dim testo As String
                Using limite As New CancellationTokenSource(TimeSpan.FromSeconds(20))
                    Using rispostaCatalogo As HttpResponseMessage = Await _http.GetAsync(IndirizzoCatalogo, limite.Token).ConfigureAwait(False)
                        rispostaCatalogo.EnsureSuccessStatusCode()
                        testo = Await rispostaCatalogo.Content.ReadAsStringAsync().ConfigureAwait(False)
                    End Using
                End Using
                Dim nuovo As Dictionary(Of String, ModelloCoppia) = LeggiCatalogo(testo)
                If nuovo.Count > 0 Then
                    File.WriteAllText(percorso, testo, Encoding.UTF8)
                    _catalogo = nuovo
                    Return _catalogo
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.CatalogoAsync: " & ex.ToString())
        End Try
        Return CatalogoLocale()
    End Function

    ''' <summary>Direzioni da usare: diretta verso o dall'inglese, altrimenti passando dall'inglese.</summary>
    Private Shared Function Percorso(origine As String, destinazione As String) As List(Of String)
        Dim o As String = CodiceModello(origine)
        Dim d As String = CodiceModello(destinazione)
        If o = "en" OrElse d = "en" Then Return New List(Of String) From {ChiaveCoppia(o, d)}
        Return New List(Of String) From {ChiaveCoppia(o, "en"), ChiaveCoppia("en", d)}
    End Function

    Private Shared Function DescrizioneCoppia(chiave As String) As String
        Dim parti As String() = chiave.Split("-"c)
        Dim da As String = parti(0)
        Dim a As String = parti(parti.Length - 1)
        If a = "Hans" OrElse a = "Hant" Then a = parti(parti.Length - 2)
        Return WGTraduttore.DallaLingua(da) & " " & WGTraduttore.ALingua(a)
    End Function

    ' La prima versione usava i modelli di prova di Bergamot, in cartelle come "enit": non servono più.
    Private Shared Sub PulisciVecchiModelli()
        Try
            Dim registroVecchio As String = Path.Combine(Cartella(), "registro.json")
            If File.Exists(registroVecchio) Then File.Delete(registroVecchio)
            For Each d As String In Directory.GetDirectories(CartellaModelliTraduzione())
                If Path.GetFileName(d).Contains("-") Then Continue For
                Directory.Delete(d, True)
            Next
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.PulisciVecchiModelli: " & ex.Message)
        End Try
    End Sub

    Private Class PianoScaricamento
        Public Errore As String = ""
        Public Elementi As New List(Of ElementoModello)()
        Public Installa As Func(Of String, String)
    End Class

    Private Shared ReadOnly NomiFileMotore As String() = {"bergamot-translator-worker.js", "bergamot-translator-worker.wasm"}

    '
    Public Const ScaricamentoAvviato As String = "@scaricamento-avviato"

    ' Cosa manca per tradurre con queste direzioni: il motore, e per ogni direzione i modelli (se mancano o, con conAggiornamenti, se il catalogo ne
    ' ha una versione più recente). Senza internet si usa quello che c'è.
    Private Shared Async Function PianoAsync(coppie As List(Of String), conAggiornamenti As Boolean) As Task(Of PianoScaricamento)
        Dim piano As New PianoScaricamento()
        Try
            Dim cartellaMotore As String = Cartella("motore")
            Dim motoreMancante As Boolean = False
            For Each nome As String In NomiFileMotore
                If File.Exists(Path.Combine(cartellaMotore, nome)) Then Continue For
                motoreMancante = True
                piano.Elementi.Add(New ElementoModello() With {.Indirizzo = IndirizzoMotore & nome, .NomeFile = "motore/" & nome})
            Next

            Dim daInstallare As New List(Of Tuple(Of String, ModelloCoppia, Boolean))()
            Dim catalogo As Dictionary(Of String, ModelloCoppia) = Nothing
            For Each chiave As String In coppie
                Dim fileCompleto As String = Path.Combine(CartellaModelliTraduzione(), chiave, "completo")
                Dim versioneScaricata As String = ""
                If File.Exists(fileCompleto) Then
                    Try
                        versioneScaricata = If(JObject.Parse(File.ReadAllText(fileCompleto, Encoding.UTF8)).Value(Of String)("versione"), "")
                    Catch exLettura As Exception
                        log.ScriviLog("WGMotoreTraduzioneOffline.PianoAsync lettura: " & exLettura.Message)
                    End Try
                End If
                If versioneScaricata <> "" AndAlso Not conAggiornamenti Then Continue For

                If catalogo Is Nothing Then catalogo = Await CatalogoAsync().ConfigureAwait(False)
                Dim modello As ModelloCoppia = Nothing
                If catalogo Is Nothing OrElse Not catalogo.TryGetValue(chiave, modello) Then
                    If versioneScaricata <> "" Then Continue For
                    piano.Errore = If(catalogo Is Nothing, "impossibile leggere l'elenco dei modelli di traduzione, controllare la connessione a internet", "la traduzione " & DescrizioneCoppia(chiave) & " non è disponibile offline")
                    Return piano
                End If
                If versioneScaricata = modello.Versione Then Continue For
                daInstallare.Add(Tuple.Create(chiave, modello, versioneScaricata <> ""))
                For Each f As FileModello In modello.File
                    piano.Elementi.Add(New ElementoModello() With {.Indirizzo = IndirizzoAllegati & f.Posizione, .NomeFile = "modelli/" & chiave & "/" & f.Nome, .ImprontaSha256 = f.Hash})
                Next
            Next

            ' con il motore già a posto bastano i suoi due file di appoggio
            If Not motoreMancante Then ScriviFileDiAppoggioMotore()
            piano.Installa = Function(temporanea As String) As String
                                 Return InstallaTraduttore(temporanea, motoreMancante, daInstallare)
                             End Function
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.PianoAsync: " & ex.ToString())
            piano.Errore = ex.Message
        End Try
        Return piano
    End Function

    Private Shared Sub ScriviFileDiAppoggioMotore()
        Dim cartellaMotore As String = Cartella("motore")
        File.WriteAllText(Path.Combine(cartellaMotore, "wgworker.js"), ScriptWorker, New UTF8Encoding(False))
        File.WriteAllText(Path.Combine(cartellaMotore, "index.html"), PaginaMotore, New UTF8Encoding(False))
        PulisciVecchiModelli()
    End Sub

    ' Mette al loro posto i file scaricati nella cartella temporanea. Restituisce "" se è riuscito. Gira sul thread del centro di controllo.
    Private Shared Function InstallaTraduttore(temporanea As String, motoreMancante As Boolean, daInstallare As List(Of Tuple(Of String, ModelloCoppia, Boolean))) As String
        Try
            If motoreMancante Then
                Dim cartellaMotore As String = Cartella("motore")
                For Each nome As String In NomiFileMotore
                    Dim origine As String = Path.Combine(temporanea, "motore", nome)
                    Dim destinazione As String = Path.Combine(cartellaMotore, nome)
                    If Not File.Exists(origine) Then
                        If File.Exists(destinazione) Then Continue For
                        Return "Nello scaricamento del motore di traduzione manca il file " & nome & "."
                    End If
                    If File.Exists(destinazione) Then File.Delete(destinazione)
                    File.Move(origine, destinazione)
                Next
                ScriviFileDiAppoggioMotore()
            End If

            For Each voce As Tuple(Of String, ModelloCoppia, Boolean) In daInstallare
                Dim chiave As String = voce.Item1
                Dim modello As ModelloCoppia = voce.Item2
                Dim cartellaCoppia As String = Path.Combine(CartellaModelliTraduzione(), chiave)
                Directory.CreateDirectory(cartellaCoppia)
                Dim descrizione As New JObject()
                descrizione("versione") = modello.Versione
                descrizione("from") = modello.Da
                descrizione("to") = modello.A
                For Each f As FileModello In modello.File
                    Dim origine As String = Path.Combine(temporanea, "modelli", chiave, f.Nome)
                    Dim destinazione As String = Path.Combine(cartellaCoppia, f.Nome)
                    If Not File.Exists(origine) Then Return "Nello scaricamento del modello " & DescrizioneCoppia(chiave) & " manca il file " & f.Nome & "."
                    If File.Exists(destinazione) Then File.Delete(destinazione)
                    File.Move(origine, destinazione)
                    descrizione(f.Tipo) = f.Nome
                Next
                ' I file della versione precedente con nomi diversi non servono più.
                Dim nomiValidi As New HashSet(Of String)(modello.File.Select(Function(f) f.Nome), StringComparer.OrdinalIgnoreCase)
                For Each vecchio As String In Directory.GetFiles(cartellaCoppia)
                    If Path.GetFileName(vecchio) = "completo" OrElse nomiValidi.Contains(Path.GetFileName(vecchio)) Then Continue For
                    Try
                        File.Delete(vecchio)
                    Catch exVecchio As Exception
                        log.ScriviLog("WGMotoreTraduzioneOffline.InstallaTraduttore pulizia: " & exVecchio.Message)
                    End Try
                Next
                File.WriteAllText(Path.Combine(cartellaCoppia, "completo"), descrizione.ToString(Formatting.None), Encoding.UTF8)
                ' Se il motore aveva già caricato la versione precedente la deve rileggere.
                If voce.Item3 Then Chiudi()
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.InstallaTraduttore: " & ex.ToString())
            Return "Installazione del traduttore offline non riuscita: " & ex.Message
        End Try
    End Function

    '
    ' Controlla cosa manca per tradurre tra le due lingue e lo fa scaricare dal centro di controllo, senza attese. Restituisce "" se è già tutto
    ' pronto, ScaricamentoAvviato se lo scaricamento è partito (alTermine riceve "" a fine lavoro, oppure "annullato" o il motivo del
    ' fallimento), altrimenti il motivo per cui non si può preparare.
    '
    Public Shared Async Function PreparaAsync(origine As String, destinazione As String, alTermine As Action(Of String)) As Task(Of String)
        Await _lockScaricamento.WaitAsync().ConfigureAwait(False)
        Try
            Dim coppie As New List(Of String)()
            If Not WGTraduttore.StessaLingua(origine, destinazione) Then coppie.AddRange(Percorso(origine, destinazione))
            Dim piano As PianoScaricamento = Await PianoAsync(coppie, False).ConfigureAwait(False)
            If piano.Errore <> "" Then Return piano.Errore
            If piano.Elementi.Count = 0 Then Return ""
            WGTraduttore.AssicuraTls()
            If Not WGCentroDiControllo.GetInstance().AggiungiScaricamentoModelli("traduttore", "traduttore offline", piano.Elementi, piano.Installa, alTermine) Then Return "lo scaricamento del traduttore offline è già in corso: lo trovi nel centro di controllo"
            Return ScaricamentoAvviato
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.PreparaAsync: " & ex.ToString())
            Return ex.Message
        Finally
            _lockScaricamento.Release()
        End Try
    End Function

#End Region

#Region "Esecuzione"

    Public Shared Async Function TraduciAsync(testi As List(Of String), origine As String, destinazione As String) As Task(Of Tuple(Of List(Of String), String))
        Try
            If Not LinguaDisponibile(origine, False) Then Return Tuple.Create(Of List(Of String), String)(Nothing, "il " & WGTraduttore.NomeLingua(origine).ToLowerInvariant() & " non si può tradurre offline")
            If Not LinguaDisponibile(destinazione) Then Return Tuple.Create(Of List(Of String), String)(Nothing, "il " & WGTraduttore.NomeLingua(destinazione).ToLowerInvariant() & " non è disponibile offline")
            Dim coppie As List(Of String) = Percorso(origine, destinazione)
            Dim errore As String = Await PreparaAsync(origine, destinazione,
                Sub(esito As String)
                    If esito <> "" Then Return
                    Try
                        Dim pronto As New WGMessaggi()
                        pronto.Messaggio("Il traduttore offline è pronto: ripeti la traduzione.")
                        pronto.ShowDialog()
                    Catch exPronto As Exception
                        log.ScriviLog("WGMotoreTraduzioneOffline.TraduciAsync (avviso): " & exPronto.Message)
                    End Try
                End Sub).ConfigureAwait(False)
            If errore = ScaricamentoAvviato Then Return Tuple.Create(Of List(Of String), String)(Nothing, "mancano i modelli per questa traduzione: li sto scaricando dal centro di controllo, quando sono pronti ripeti la traduzione")
            If errore <> "" Then Return Tuple.Create(Of List(Of String), String)(Nothing, errore)
            If Not Await AvviaAsync().ConfigureAwait(False) Then Return Tuple.Create(Of List(Of String), String)(Nothing, "il motore di traduzione offline non si è avviato")
            Dim dati As New JObject()
            Dim elencoCoppie As New JArray()
            For Each coppia As String In coppie
                elencoCoppie.Add(DescriviCoppia(coppia))
            Next
            dati("coppie") = elencoCoppie
            dati("testi") = New JArray(testi)
            Dim risposta As JObject = Await ChiamaAsync(dati, 180000).ConfigureAwait(False)
            If risposta Is Nothing Then Return Tuple.Create(Of List(Of String), String)(Nothing, "il motore di traduzione offline non risponde")
            If risposta("errore") IsNot Nothing Then
                log.ScriviLog("WGMotoreTraduzioneOffline.TraduciAsync motore: " & risposta.Value(Of String)("errore"))
                Return Tuple.Create(Of List(Of String), String)(Nothing, "errore del motore offline: " & risposta.Value(Of String)("errore"))
            End If
            Dim tradotti As List(Of String) = CType(risposta("testi"), JArray).Select(Function(t) t.ToString()).ToList()
            ' I modelli aggiungono a volte un trattino di dialogo davanti ai saluti ("- Ciao mondo"): si toglie
            ' quando il testo originale non ne ha uno.
            For i As Integer = 0 To Math.Min(tradotti.Count, testi.Count) - 1
                If tradotti(i).StartsWith("- ", StringComparison.Ordinal) AndAlso Not testi(i).TrimStart().StartsWith("-", StringComparison.Ordinal) Then tradotti(i) = tradotti(i).Substring(2)
            Next
            Return Tuple.Create(tradotti, "")
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.TraduciAsync: " & ex.ToString())
            Return Tuple.Create(Of List(Of String), String)(Nothing, ex.Message)
        End Try
    End Function

    Private Shared Function DescriviCoppia(chiave As String) As JObject
        Dim voce As JObject = JObject.Parse(File.ReadAllText(Path.Combine(CartellaModelliTraduzione(), chiave, "completo"), Encoding.UTF8))
        Dim risultato As New JObject()
        risultato("from") = voce.Value(Of String)("from")
        risultato("to") = voce.Value(Of String)("to")
        Dim radice As String = "https://" & NomeHostModelli & "/" & chiave & "/"
        Dim modello As String = voce.Value(Of String)("model")
        risultato("model") = radice & modello
        risultato("lex") = radice & voce.Value(Of String)("lex")
        Dim vocabolari As New JArray()
        If voce("vocab") IsNot Nothing Then
            vocabolari.Add(radice & voce.Value(Of String)("vocab"))
        Else
            vocabolari.Add(radice & voce.Value(Of String)("srcvocab"))
            vocabolari.Add(radice & voce.Value(Of String)("trgvocab"))
        End If
        risultato("vocabs") = vocabolari
        Dim configurazione As New JObject()
        If modello.EndsWith("intgemm8.bin", StringComparison.OrdinalIgnoreCase) Then configurazione("gemm-precision") = "int8shiftAll"
        risultato("config") = configurazione
        Return risultato
    End Function

    Private Shared Function AvviaAsync() As Task(Of Boolean)
        SyncLock _lock
            _ultimoUso = DateTime.Now
            If _pronto IsNot Nothing AndAlso (Not _pronto.Task.IsCompleted OrElse _pronto.Task.Result) Then Return _pronto.Task
            Dim attesa As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            _pronto = attesa
            _thread = New Thread(Sub() CicloThread(attesa)) With {.IsBackground = True, .Name = "WGTraduttoreOffline"}
            _thread.SetApartmentState(ApartmentState.STA)
            _thread.Start()
            Return attesa.Task
        End SyncLock
    End Function

    Private Shared Sub CicloThread(pronto As TaskCompletionSource(Of Boolean))
        Dim form As Form = Nothing
        Dim vista As WebView2 = Nothing
        Dim timer As System.Windows.Forms.Timer = Nothing
        Try
            form = New Form() With {
                .ShowInTaskbar = False, .FormBorderStyle = FormBorderStyle.FixedToolWindow, .StartPosition = FormStartPosition.Manual,
                .Location = New Point(-32000, -32000), .Size = New Size(400, 300), .Opacity = 0
            }
            vista = New WebView2() With {.Dock = DockStyle.Fill}
            form.Controls.Add(vista)
            form.Show()
            form.Hide()
            timer = New System.Windows.Forms.Timer() With {.Interval = 30000}
            AddHandler timer.Tick, AddressOf ControllaInattivita
            timer.Start()
            SyncLock _lock
                _form = form
                _webView = vista
            End SyncLock
            InizializzaAsync(vista, pronto)
            Application.Run()
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.CicloThread: " & ex.ToString())
        Finally
            pronto.TrySetResult(False)
            SyncLock _lock
                If _pronto Is pronto Then _pronto = Nothing
                If _form Is form Then
                    _form = Nothing
                    _webView = Nothing
                    For Each attesa As TaskCompletionSource(Of JObject) In _attese.Values
                        attesa.TrySetResult(Nothing)
                    Next
                End If
            End SyncLock
            Try
                If timer IsNot Nothing Then timer.Dispose()
                If vista IsNot Nothing Then vista.Dispose()
                If form IsNot Nothing Then form.Dispose()
            Catch exChiusura As Exception
                log.ScriviLog("WGMotoreTraduzioneOffline.CicloThread chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Sub

    Private Shared Async Sub InizializzaAsync(vista As WebView2, pronto As TaskCompletionSource(Of Boolean))
        Try
            Dim opzioni As New CoreWebView2EnvironmentOptions()
            Dim ambiente As CoreWebView2Environment = Await CoreWebView2Environment.CreateAsync(WGMotoreBrowser.CartellaRuntimeFissa(), Path.Combine(WGDb.WGPercorsiApplicazione.CartellaDatiBrowser(), "traduttore"), opzioni)
            Await vista.EnsureCoreWebView2Async(ambiente)
            Dim core As CoreWebView2 = vista.CoreWebView2
            core.Settings.AreDevToolsEnabled = False
            core.Settings.AreDefaultContextMenusEnabled = False
            core.Settings.AreDefaultScriptDialogsEnabled = False
            core.SetVirtualHostNameToFolderMapping(NomeHost, Cartella(), CoreWebView2HostResourceAccessKind.Allow)
            core.SetVirtualHostNameToFolderMapping(NomeHostModelli, CartellaModelliTraduzione(), CoreWebView2HostResourceAccessKind.Allow)
            AddHandler core.WebMessageReceived, Sub(s, e) MessaggioRicevuto(e, pronto)
            AddHandler core.ProcessFailed, Sub(s, e)
                                               log.ScriviLog("WGMotoreTraduzioneOffline: processo del motore terminato, " & e.ProcessFailedKind.ToString())
                                               Application.ExitThread()
                                           End Sub
            core.Navigate("https://" & NomeHost & "/motore/index.html")
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.InizializzaAsync: " & ex.ToString())
            Application.ExitThread()
        End Try
    End Sub

    Private Shared Sub MessaggioRicevuto(e As CoreWebView2WebMessageReceivedEventArgs, pronto As TaskCompletionSource(Of Boolean))
        Try
            Dim messaggio As JObject = JObject.Parse(e.TryGetWebMessageAsString())
            If messaggio("pronto") IsNot Nothing Then
                pronto.TrySetResult(True)
                Return
            End If
            Dim id As Integer = messaggio.Value(Of Integer)("id")
            Dim attesa As TaskCompletionSource(Of JObject) = Nothing
            If _attese.TryRemove(id, attesa) Then attesa.TrySetResult(messaggio)
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.MessaggioRicevuto: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Function ChiamaAsync(dati As JObject, millisecondi As Integer) As Task(Of JObject)
        Dim id As Integer = Interlocked.Increment(_contatore)
        Dim attesa As New TaskCompletionSource(Of JObject)(TaskCreationOptions.RunContinuationsAsynchronously)
        _attese(id) = attesa
        Try
            Dim form As Form = _form
            If form Is Nothing OrElse form.IsDisposed Then Return Nothing
            Dim script As String = "wgTraduci(" & id & "," & dati.ToString(Formatting.None) & ");"
            form.BeginInvoke(New Action(Sub()
                                            Try
                                                _ultimoUso = DateTime.Now
                                                If _webView IsNot Nothing AndAlso _webView.CoreWebView2 IsNot Nothing Then
                                                    Dim esecuzione As Task(Of String) = _webView.CoreWebView2.ExecuteScriptAsync(script)
                                                Else
                                                    attesa.TrySetResult(Nothing)
                                                End If
                                            Catch exScript As Exception
                                                log.ScriviLog("WGMotoreTraduzioneOffline.ChiamaAsync script: " & exScript.Message)
                                                attesa.TrySetResult(Nothing)
                                            End Try
                                        End Sub))
            Dim finito As Task = Await Task.WhenAny(attesa.Task, Task.Delay(millisecondi)).ConfigureAwait(False)
            If finito IsNot attesa.Task Then Return Nothing
            _ultimoUso = DateTime.Now
            Return attesa.Task.Result
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.ChiamaAsync: " & ex.Message)
            Return Nothing
        Finally
            Dim rimossa As TaskCompletionSource(Of JObject) = Nothing
            _attese.TryRemove(id, rimossa)
        End Try
    End Function

    Private Shared Sub ControllaInattivita(sender As Object, e As EventArgs)
        Try
            If _attese.Count > 0 Then Return
            If (DateTime.Now - _ultimoUso).TotalMinutes < MinutiInattivita Then Return
            Chiudi()
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.ControllaInattivita: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Chiude il WebView2 e il suo thread; si riaprirà alla prossima traduzione.</summary>
    Public Shared Sub Chiudi()
        Try
            SyncLock _lock
                _pronto = Nothing
            End SyncLock
            Dim form As Form = _form
            If form Is Nothing OrElse form.IsDisposed Then Return
            form.BeginInvoke(New Action(Sub()
                                            Try
                                                If _timerInattivita IsNot Nothing Then _timerInattivita.Stop()
                                                Application.ExitThread()
                                            Catch exUscita As Exception
                                                log.ScriviLog("WGMotoreTraduzioneOffline.Chiudi uscita: " & exUscita.Message)
                                            End Try
                                        End Sub))
        Catch ex As Exception
            log.ScriviLog("WGMotoreTraduzioneOffline.Chiudi: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Pagina e worker"

    ' Pagina caricata nel WebView2: avvia il worker, carica i modelli richiesti e risponde con postMessage.
    Private Const PaginaMotore As String = "<!doctype html><html><head><meta charset='utf-8'><title>WGTraduttore</title></head><body><script>
const w = new Worker('wgworker.js');
let n = 0;
const attese = new Map();
w.onmessage = (e) => { const d = e.data; const a = attese.get(d.id); if (!a) return; attese.delete(d.id); if (d.errore !== undefined) a.no(new Error(d.errore)); else a.si(d.risultato); };
w.onerror = (e) => { for (const a of attese.values()) a.no(new Error(e.message || 'errore del motore')); attese.clear(); };
function chiama(nome, arg, trasferibili) { return new Promise((si, no) => { const id = ++n; attese.set(id, { si, no }); w.postMessage({ id, nome, arg }, trasferibili || []); }); }
let avvio = null;
const caricati = new Set();
async function file(p) { const r = await fetch(p.startsWith('http') ? p : '/' + p); if (!r.ok) throw new Error('file mancante ' + p); return r.arrayBuffer(); }
async function assicura(c) {
  const k = c.from + '-' + c.to;
  if (caricati.has(k)) return;
  const model = await file(c.model);
  const shortlist = await file(c.lex);
  const vocabs = [];
  for (const v of c.vocabs) vocabs.push(await file(v));
  await chiama('carica', { from: c.from, to: c.to, model, shortlist, vocabs, config: c.config || {} }, [model, shortlist, ...vocabs]);
  caricati.add(k);
}
function rispondi(o) { chrome.webview.postMessage(JSON.stringify(o)); }
window.wgTraduci = async function (id, dati) {
  try {
    if (!avvio) avvio = chiama('inizializza', {});
    try { await avvio; } catch (e) { avvio = null; throw e; }
    for (const c of dati.coppie) await assicura(c);
    const testi = await chiama('traduci', { coppie: dati.coppie.map((c) => ({ from: c.from, to: c.to })), testi: dati.testi });
    rispondi({ id, testi });
  } catch (e) {
    rispondi({ id, errore: String((e && e.message) || e) });
  }
};
rispondi({ pronto: 1 });
</script></body></html>"

    ' Worker ricavato da translator-worker.js di bergamot-translator (MPL-2.0): il WebAssembly viene
    ' caricato da un ArrayBuffer invece che in streaming, così non dipende dal tipo MIME del file.
    Private Const ScriptWorker As String = "var Module = {};
const GEMM = {
  'int8_prepare_a': 'int8PrepareAFallback',
  'int8_prepare_b': 'int8PrepareBFallback',
  'int8_prepare_b_from_transposed': 'int8PrepareBFromTransposedFallback',
  'int8_prepare_b_from_quantized_transposed': 'int8PrepareBFromQuantizedTransposedFallback',
  'int8_prepare_bias': 'int8PrepareBiasFallback',
  'int8_multiply_and_add_bias': 'int8MultiplyAndAddBiasFallback',
  'int8_select_columns_of_b': 'int8SelectColumnsOfBFallback'
};
let modulo = null;
let servizio = null;
const modelli = new Map();
function collegaGemm() { const m = {}; for (const [k, nome] of Object.entries(GEMM)) m[k] = (...a) => Module['asm'][nome](...a); return m; }
function caricaModulo() {
  return new Promise(async (resolve, reject) => {
    try {
      const r = await fetch(new URL('bergamot-translator-worker.wasm', self.location));
      if (!r.ok) throw new Error('motore WebAssembly mancante');
      const bytes = await r.arrayBuffer();
      Object.assign(Module, {
        instantiateWasm: (info, accept) => {
          WebAssembly.instantiate(bytes, { ...info, 'wasm_gemm': collegaGemm() }).then(({ instance }) => accept(instance)).catch(reject);
          return {};
        },
        onRuntimeInitialized: () => resolve(Module)
      });
      self.Module = Module;
      self.importScripts('bergamot-translator-worker.js');
    } catch (e) { reject(e); }
  });
}
function yaml(dati) { return Object.entries(dati).map(([k, v]) => k + ': ' + v).join('\n') + '\n'; }
function memoria(buffer, allineamento) { const b = new Int8Array(buffer); const m = new modulo.AlignedMemory(b.byteLength, allineamento); m.getByteArrayView().set(b); return m; }
const azioni = {
  async inizializza() {
    if (modulo) return true;
    modulo = await caricaModulo();
    servizio = new modulo.BlockingService({ cacheSize: 20000 });
    return true;
  },
  carica({ from, to, model, shortlist, vocabs, config }) {
    const chiave = from + '-' + to;
    if (modelli.has(chiave)) return true;
    const memoriaModello = memoria(model, 256);
    const memoriaLessico = memoria(shortlist, 64);
    const elenco = new modulo.AlignedMemoryList();
    vocabs.forEach((v) => elenco.push_back(memoria(v, 64)));
    const cfg = { 'beam-size': 1, 'normalize': 1.0, 'word-penalty': 0, 'cpu-threads': 0, 'gemm-precision': 'int8shiftAlphaAll', 'skip-cost': true };
    Object.assign(cfg, config || {});
    Object.assign(cfg, { 'alignment': 'soft', 'quiet': true, 'quiet-translation': true, 'max-length-break': 128, 'mini-batch-words': 1024, 'workspace': 128, 'max-length-factor': 2.0 });
    modelli.set(chiave, new modulo.TranslationModel(yaml(cfg), memoriaModello, memoriaLessico, elenco, null));
    return true;
  },
  traduci({ coppie, testi }) {
    const ingresso = new modulo.VectorString();
    testi.forEach((t) => ingresso.push_back(t));
    const opzioni = new modulo.VectorResponseOptions();
    testi.forEach(() => opzioni.push_back({ alignment: false, html: false, qualityScores: false }));
    const usati = coppie.map((c) => modelli.get(c.from + '-' + c.to));
    const risposte = usati.length > 1 ? servizio.translateViaPivoting(usati[0], usati[1], ingresso, opzioni) : servizio.translate(usati[0], ingresso, opzioni);
    ingresso.delete();
    opzioni.delete();
    const uscita = [];
    for (let i = 0; i < testi.length; i++) uscita.push(risposte.get(i).getTranslatedText());
    risposte.delete();
    return uscita;
  }
};
self.addEventListener('message', async ({ data }) => {
  const { id, nome, arg } = data;
  try { const r = await Promise.resolve(azioni[nome](arg)); self.postMessage({ id, risultato: r }); }
  catch (e) { self.postMessage({ id, errore: String((e && e.message) || e) }); }
});
"

#End Region

End Class
