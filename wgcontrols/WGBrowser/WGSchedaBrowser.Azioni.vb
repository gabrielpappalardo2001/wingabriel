Imports System.Collections.Generic
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports WGBrowserManager
Imports wgcontrols
Imports WGPlayerMpv

' Gestione delle azioni avanzate del browser: riproduzione video con il lettore multimediale, caricamento file, condivisione dello
' schermo e gestione dei download.
Friend NotInheritable Class RispostaUnica
    Private _data As Integer = 0

    Public Function Prendi() As Boolean
        Return Interlocked.Exchange(_data, 1) = 0
    End Function

    Public ReadOnly Property Data As Boolean
        Get
            Return Interlocked.CompareExchange(_data, 0, 0) = 1
        End Get
    End Property
End Class

Partial Public Class WGSchedaBrowser
    Private ReadOnly _promptPermessi As New Dictionary(Of ULong, WGMenu)()
    Private _origineTrascinamento As RigaVirtuale = Nothing
    Private _menuContestualeAperto As Boolean = False
    Private _zoomPercento As Integer = 100
    Private _listaElementi As WGMenuLista = Nothing
    Private _campoRicerca As WGCampoTesto = Nothing
    Private _consensoCondivisioneFino As DateTime = DateTime.MinValue
    Private _condivisioneInCorso As String = ""
    Private _finestraMarcata As IntPtr = IntPtr.Zero
    Private _tentativoCondivisione As Integer = 0
    Private _ultimaRichiestaCondivisione As String = ""
    Private _catturaAvviata As Boolean = False
    Private _sceltaChiusaDaWinGabriel As Boolean = False
    Private _ultimoPdfIntercettato As String = ""
    Private _istanteUltimoPdf As DateTime = DateTime.MinValue
    Private _istanteUltimaStampaRichiesta As DateTime = DateTime.MinValue
    Private _contatorePrompt As ULong = 0UL
    Private _raccoltaPermessiProgrammata As Boolean = False
    Private ReadOnly _permessiInAttesa As New List(Of PermessoInAttesa)()

    Private Class PermessoInAttesa
        Public Argomenti As CoreWebView2PermissionRequestedEventArgs
        Public Rinvio As CoreWebView2Deferral
        Public Nome As String = ""
        Public Origine As String = ""
        Public ReadOnly Risposta As New RispostaUnica()
    End Class

    Private Shared ReadOnly LivelliZoom As Integer() = {25, 33, 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400, 500}
    Private Shared ReadOnly EstensioniImmaginiCaricamento As String() = {".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".webp", ".heic", ".heif", ".avif", ".tif", ".tiff", ".svg", ".ico"}
    Private Shared ReadOnly EstensioniAudioCaricamento As String() = {".mp3", ".wav", ".ogg", ".oga", ".opus", ".m4a", ".aac", ".flac", ".wma", ".amr", ".mid", ".midi"}
    Private Shared ReadOnly EstensioniVideoCaricamento As String() = {".mp4", ".m4v", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".3gp", ".mpg", ".mpeg"}
    Private Shared ReadOnly EstensioniTestoCaricamento As String() = {".txt", ".csv", ".html", ".htm", ".md", ".rtf", ".xml", ".json", ".log", ".ics", ".vcf"}
    Private Shared ReadOnly EstensioniDocumentiCaricamento As String() = {".pdf", ".doc", ".docx", ".odt", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp", ".pages", ".numbers", ".key", ".epub", ".xps", ".eml", ".msg", ".p7m", ".p7s", ".sig", ".cer", ".crt", ".pem", ".pfx", ".p12"}
    Private Shared ReadOnly EstensioniArchiviCaricamento As String() = {".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz"}
    Private Shared ReadOnly EstensioniAltreCaricamento As String() = {".exe", ".msi", ".apk", ".iso", ".img", ".dmg", ".ttf", ".otf", ".woff", ".woff2", ".gpx", ".kml", ".kmz", ".srt", ".vtt", ".psd", ".ai", ".eps", ".dwg", ".dxf", ".sql", ".db", ".sqlite", ".vb", ".cs", ".js", ".css", ".py", ".java", ".c", ".cpp", ".h", ".ps1", ".bat", ".ini", ".cfg", ".yaml", ".yml", ".torrent", ".bak"}

    Private ReadOnly Property Archivio As WGArchivioBrowser
        Get
            Return WGArchivioBrowser.Istanza
        End Get
    End Property

    Private Sub ChiudiMenuAperti()
        Try
            Dim menu As List(Of WGMenu)
            SyncLock _promptPermessi
                menu = _promptPermessi.Values.ToList()
                _promptPermessi.Clear()
            End SyncLock
            For Each m As WGMenu In menu
                Try
                    m.abbandona()
                Catch exMenu As Exception
                    log.ScriviLog("WGSchedaBrowser.ChiudiMenuAperti menu: " & exMenu.Message)
                End Try
            Next
            If _listaElementi IsNot Nothing Then
                _listaElementi.abbandona()
                _listaElementi = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiudiMenuAperti: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraRisultato(titoloRisultato As String, testo As String)
        Try
            Dim pulito As String = If(testo, "").Trim()
            If pulito = "" Then
                lettura.LeggiTesto("Nessun risultato")
                Return
            End If
            WGAriaMonitor.Registra(pulito, "sistema", Titolo)
            If pulito.Length <= 400 Then
                lettura.LeggiTesto(pulito)
                Return
            End If
            Dim campo As New WGCampo()
            campo.init(titoloRisultato, pulito, True, True)
            AddHandler campo.chiusura, Sub(s As Object, e As EventArgs)
                                           Try
                                               campo.chiudi()
                                           Catch exChiudi As Exception
                                               log.ScriviLog("WGSchedaBrowser.MostraRisultato chiusura: " & exChiudi.Message)
                                           End Try
                                       End Sub
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraRisultato: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub CancellaFile(percorso As String)
        Try
            If Not String.IsNullOrEmpty(percorso) AndAlso File.Exists(percorso) Then File.Delete(percorso)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.CancellaFile: " & ex.Message)
        End Try
    End Sub

    Private Shared Function FileTemporaneo(estensione As String) As String
        Return Path.Combine(Path.GetTempPath(), "WinGabriel_browser_" & Guid.NewGuid().ToString("N") & estensione)
    End Function

    Friend Sub MostraDialogoPagina(argomenti As CoreWebView2ScriptDialogOpeningEventArgs, rinvio As CoreWebView2Deferral)
        Dim risposta As New RispostaUnica()
        Dim concludi As Action(Of Boolean, String) = Sub(accetta As Boolean, testo As String)
                                                         If Not risposta.Prendi() Then Return
                                                         Try
                                                             If accetta AndAlso argomenti IsNot Nothing Then
                                                                 If testo IsNot Nothing Then argomenti.ResultText = testo
                                                                 argomenti.Accept()
                                                             End If
                                                         Catch exRisposta As Exception
                                                             log.ScriviLog("WGSchedaBrowser.MostraDialogoPagina risposta: " & exRisposta.Message)
                                                         End Try
                                                         CompletaRinvio(rinvio)
                                                     End Sub
        Try
            If _chiusa OrElse argomenti Is Nothing Then
                concludi(False, Nothing)
                Return
            End If
            Dim tipo As CoreWebView2ScriptDialogKind = argomenti.Kind
            Dim messaggio As String = If(argomenti.Message, "")
            Dim predefinito As String = If(argomenti.DefaultText, "")
            Dim prefisso As String = If(_attiva, "", "La scheda " & Titolo & ". ")
            Select Case tipo
                Case CoreWebView2ScriptDialogKind.Alert
                    concludi(True, Nothing)
                    WGAriaMonitor.Registra(messaggio, "dialogo", Titolo)
                    Dim avviso As New WGMessaggi()
                    avviso.Messaggio(prefisso & "Avviso dalla pagina: " & messaggio)
                    avviso.Show()
                Case CoreWebView2ScriptDialogKind.Confirm
                    Dim domanda As New WGDomanda()
                    AddHandler domanda.rispostadomanda, Sub(esito As DialogResult) concludi(esito = DialogResult.Yes, Nothing)
                    domanda.avvia(prefisso & "La pagina chiede: " & messaggio)
                Case Else
                    Dim confermato As Boolean = False
                    Dim campo As New WGCampoTesto()
                    campo.init(prefisso & messaggio, predefinito)
                    AddHandler campo.chiusura, Sub(s As Object, ev As EventArgs)
                                                   Dim testoInserito As String = ""
                                                   Try
                                                       testoInserito = If(campo.OttieniTesto(), "")
                                                   Catch exTesto As Exception
                                                       log.ScriviLog("WGSchedaBrowser.MostraDialogoPagina testo: " & exTesto.Message)
                                                   End Try
                                                   confermato = True
                                                   concludi(True, testoInserito)
                                                   Try
                                                       campo.chiudi()
                                                   Catch exChiudi As Exception
                                                       log.ScriviLog("WGSchedaBrowser.MostraDialogoPagina chiusura: " & exChiudi.Message)
                                                   End Try
                                               End Sub
                    AddHandler campo.Disposed, Sub(s As Object, ev As EventArgs)
                                                   If Not confermato Then concludi(False, Nothing)
                                               End Sub
                    campo.avvia()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraDialogoPagina: " & ex.ToString())
            concludi(False, Nothing)
        End Try
    End Sub

    Friend Sub ConfermaUscitaPagina(argomenti As CoreWebView2ScriptDialogOpeningEventArgs, rinvio As CoreWebView2Deferral)
        Dim risposta As New RispostaUnica()
        Dim concludi As Action(Of Boolean) = Sub(esci As Boolean)
                                                 If Not risposta.Prendi() Then Return
                                                 Try
                                                     If esci AndAlso argomenti IsNot Nothing Then argomenti.Accept()
                                                 Catch exRisposta As Exception
                                                     log.ScriviLog("WGSchedaBrowser.ConfermaUscitaPagina risposta: " & exRisposta.Message)
                                                 End Try
                                                 CompletaRinvio(rinvio)
                                             End Sub
        Try
            If _chiusa OrElse argomenti Is Nothing Then
                concludi(True)
                Return
            End If
            Dim ricarica As Boolean = (DateTime.Now - _istanteRicaricaRichiesta).TotalSeconds < 5
            Dim domanda As New WGDomanda()
            AddHandler domanda.rispostadomanda, Sub(esito As DialogResult) concludi(esito = DialogResult.Yes)
            domanda.avvia("La pagina potrebbe contenere modifiche non salvate. " & If(ricarica, "Ricaricarla", "Lasciarla") & " comunque?")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ConfermaUscitaPagina: " & ex.Message)
            concludi(True)
        End Try
    End Sub

    Private Shared Function AmbitoDaSfida(sfida As String) As String
        Try
            Dim corrispondenza As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(If(sfida, ""), "realm\s*=\s*""([^""]*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            If corrispondenza.Success Then Return corrispondenza.Groups(1).Value.Trim()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.AmbitoDaSfida: " & ex.Message)
        End Try
        Return ""
    End Function

    Friend Sub ChiediCredenzialiHttp(argomenti As CoreWebView2BasicAuthenticationRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Dim risposta As New RispostaUnica()
        Dim concludi As Action(Of String, String) = Sub(utente As String, segreto As String)
                                                        If Not risposta.Prendi() Then Return
                                                        Try
                                                            If argomenti IsNot Nothing Then
                                                                If utente Is Nothing Then
                                                                    argomenti.Cancel = True
                                                                Else
                                                                    argomenti.Response.UserName = utente
                                                                    argomenti.Response.Password = If(segreto, "")
                                                                End If
                                                            End If
                                                        Catch exRisposta As Exception
                                                            log.ScriviLog("WGSchedaBrowser.ChiediCredenzialiHttp risposta: " & exRisposta.Message)
                                                        End Try
                                                        CompletaRinvio(rinvio)
                                                    End Sub
        Try
            If _chiusa OrElse argomenti Is Nothing Then
                concludi(Nothing, Nothing)
                Return
            End If
            Dim hostRichiesta As String = WGArchivioBrowser.HostDi(If(argomenti.Uri, ""))
            If hostRichiesta = "" Then hostRichiesta = _hostPagina
            Dim ambito As String = AmbitoDaSfida(argomenti.Challenge)
            Dim utenteConfermato As Boolean = False
            Dim campoUtente As New WGCampoTesto()
            campoUtente.init(If(_attiva, "", "Scheda " & Titolo & ". ") & "Il sito " & hostRichiesta & " chiede di accedere" & If(ambito <> "", " a " & ambito, "") & ". Nome utente")
            AddHandler campoUtente.chiusura, Sub(s As Object, ev As EventArgs)
                                                 Dim utente As String = ""
                                                 Try
                                                     utente = If(campoUtente.OttieniTesto(), "")
                                                 Catch exUtente As Exception
                                                     log.ScriviLog("WGSchedaBrowser.ChiediCredenzialiHttp utente: " & exUtente.Message)
                                                 End Try
                                                 utenteConfermato = True
                                                 Try
                                                     campoUtente.chiudi()
                                                 Catch exChiudi As Exception
                                                     log.ScriviLog("WGSchedaBrowser.ChiediCredenzialiHttp chiusura: " & exChiudi.Message)
                                                 End Try
                                                 If String.IsNullOrWhiteSpace(utente) Then
                                                     concludi(Nothing, Nothing)
                                                     Return
                                                 End If
                                                 Try
                                                     Dim passwordConfermata As Boolean = False
                                                     Dim campoPassword As New WGCampoTesto()
                                                     campoPassword.init("Password di " & utente)
                                                     AddHandler campoPassword.chiusura, Sub(s2 As Object, e2 As EventArgs)
                                                                                            Dim segreto As String = ""
                                                                                            Try
                                                                                                segreto = If(campoPassword.OttieniTesto(), "")
                                                                                            Catch exSegreto As Exception
                                                                                                log.ScriviLog("WGSchedaBrowser.ChiediCredenzialiHttp password: " & exSegreto.Message)
                                                                                            End Try
                                                                                            passwordConfermata = True
                                                                                            Try
                                                                                                campoPassword.chiudi()
                                                                                            Catch exChiudiPassword As Exception
                                                                                                log.ScriviLog("WGSchedaBrowser.ChiediCredenzialiHttp chiusura password: " & exChiudiPassword.Message)
                                                                                            End Try
                                                                                            concludi(utente, segreto)
                                                                                        End Sub
                                                     AddHandler campoPassword.Disposed, Sub(s2 As Object, e2 As EventArgs)
                                                                                            If Not passwordConfermata Then concludi(Nothing, Nothing)
                                                                                        End Sub
                                                     campoPassword.avvia()
                                                 Catch exPassword As Exception
                                                     log.ScriviLog("WGSchedaBrowser.ChiediCredenzialiHttp campo password: " & exPassword.Message)
                                                     concludi(Nothing, Nothing)
                                                 End Try
                                             End Sub
            AddHandler campoUtente.Disposed, Sub(s As Object, ev As EventArgs)
                                                 If Not utenteConfermato Then concludi(Nothing, Nothing)
                                             End Sub
            campoUtente.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiediCredenzialiHttp: " & ex.Message)
            concludi(Nothing, Nothing)
        End Try
    End Sub

    Friend Sub ChiediCertificato(argomenti As CoreWebView2ServerCertificateErrorDetectedEventArgs, rinvio As CoreWebView2Deferral)
        Dim risposta As New RispostaUnica()
        Dim concludi As Action(Of Boolean) = Sub(consenti As Boolean)
                                                 If Not risposta.Prendi() Then Return
                                                 Try
                                                     If argomenti IsNot Nothing Then argomenti.Action = If(consenti, CoreWebView2ServerCertificateErrorAction.AlwaysAllow, CoreWebView2ServerCertificateErrorAction.Cancel)
                                                 Catch exRisposta As Exception
                                                     log.ScriviLog("WGSchedaBrowser.ChiediCertificato risposta: " & exRisposta.Message)
                                                 End Try
                                                 CompletaRinvio(rinvio)
                                             End Sub
        Try
            If _chiusa OrElse argomenti Is Nothing Then
                concludi(False)
                Return
            End If
            Dim url As String = If(argomenti.RequestUri, "")
            Dim errore As CoreWebView2WebErrorStatus = argomenti.ErrorStatus
            Dim menu As New WGMenu()
            menu.aggiungi("Non proseguire, consigliato", Sub(s, ev) concludi(False))
            menu.aggiungi("Prosegui comunque, il collegamento non è sicuro", Sub(s, ev) concludi(True))
            AddHandler menu.abbandonato, Sub(s As Object, ev As EventArgs) concludi(False)
            menu.avvia(If(_attiva, "", "Scheda " & Titolo & ". ") & "Il certificato di sicurezza di " & WGArchivioBrowser.HostDi(url) & " non è valido: " & DescriviErrore(errore), True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiediCertificato: " & ex.Message)
            concludi(False)
        End Try
    End Sub

    Private Function DescriviCertificato(certificato As CoreWebView2ClientCertificate) As String
        Try
            If certificato Is Nothing Then Return "Certificato"
            Dim nome As String = If(certificato.DisplayName, "").Trim()
            If nome = "" Then nome = If(certificato.Subject, "").Trim()
            If nome = "" Then nome = "Certificato"
            Dim testo As String = nome
            Dim emittente As String = If(certificato.Issuer, "").Trim()
            If emittente <> "" Then testo &= ", rilasciato da " & emittente
            testo &= ", valido fino al " & certificato.ValidTo.ToString("d MMMM yyyy", New Globalization.CultureInfo("it-IT"))
            If certificato.Kind = CoreWebView2ClientCertificateKind.SmartCard Then testo &= ", smart card"
            Return testo
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviCertificato: " & ex.Message)
            Return "Certificato"
        End Try
    End Function

    Friend Sub ScegliCertificatoClient(argomenti As CoreWebView2ClientCertificateRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Dim risposta As New RispostaUnica()
        Dim concludi As Action(Of CoreWebView2ClientCertificate) = Sub(certificato As CoreWebView2ClientCertificate)
                                                                       If Not risposta.Prendi() Then Return
                                                                       Try
                                                                           If argomenti IsNot Nothing Then
                                                                               argomenti.SelectedCertificate = certificato
                                                                               argomenti.Handled = True
                                                                           End If
                                                                       Catch exRisposta As Exception
                                                                           log.ScriviLog("WGSchedaBrowser.ScegliCertificatoClient risposta: " & exRisposta.Message)
                                                                       End Try
                                                                       CompletaRinvio(rinvio)
                                                                   End Sub
        Try
            If _chiusa OrElse argomenti Is Nothing Then
                concludi(Nothing)
                Return
            End If
            Dim certificati As New List(Of CoreWebView2ClientCertificate)()
            Try
                Dim elenco As IReadOnlyList(Of CoreWebView2ClientCertificate) = argomenti.MutuallyTrustedCertificates
                If elenco IsNot Nothing Then certificati.AddRange(elenco)
            Catch exElenco As Exception
                log.ScriviLog("WGSchedaBrowser.ScegliCertificatoClient elenco: " & exElenco.Message)
            End Try
            If certificati.Count = 0 Then
                concludi(Nothing)
                Return
            End If
            Dim host As String = If(argomenti.Host, "")
            Dim menu As New WGMenu()
            For Each certificato As CoreWebView2ClientCertificate In certificati
                Dim scelto As CoreWebView2ClientCertificate = certificato
                menu.aggiungi(DescriviCertificato(certificato), Sub(s, ev) concludi(scelto))
            Next
            menu.aggiungi("Non usare nessun certificato", Sub(s, ev) concludi(Nothing))
            AddHandler menu.abbandonato, Sub(s As Object, ev As EventArgs) concludi(Nothing)
            menu.avvia(If(_attiva, "", "Scheda " & Titolo & ". ") & "Il sito " & host & " chiede un certificato personale per riconoscerti. Scegliere quale usare", True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScegliCertificatoClient: " & ex.Message)
            concludi(Nothing)
        End Try
    End Sub

    Private Shared Sub AggiungiEstensioneUnica(estensioni As List(Of String), estensione As String)
        Try
            Dim valore As String = If(estensione, "").Trim().ToLowerInvariant()
            If valore.Length < 2 OrElse Not valore.StartsWith(".") Then Return
            If Not estensioni.Contains(valore) Then estensioni.Add(valore)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.AggiungiEstensioneUnica: " & ex.Message)
        End Try
    End Sub

    Private Shared Function EstensioniDaTipo(tipo As String) As String()
        Select Case tipo
            Case "image/*" : Return EstensioniImmaginiCaricamento
            Case "audio/*" : Return EstensioniAudioCaricamento
            Case "video/*" : Return EstensioniVideoCaricamento
            Case "text/*" : Return EstensioniTestoCaricamento
            Case "application/pdf" : Return {".pdf"}
            Case "image/jpeg", "image/jpg", "image/pjpeg" : Return {".jpg", ".jpeg", ".jpe", ".jfif"}
            Case "image/png" : Return {".png"}
            Case "image/gif" : Return {".gif"}
            Case "image/webp" : Return {".webp"}
            Case "image/heic", "image/heif" : Return {".heic", ".heif"}
            Case "image/avif" : Return {".avif"}
            Case "image/bmp" : Return {".bmp"}
            Case "image/tiff" : Return {".tif", ".tiff"}
            Case "image/svg+xml" : Return {".svg"}
            Case "text/plain" : Return {".txt"}
            Case "text/csv" : Return {".csv"}
            Case "text/html" : Return {".html", ".htm"}
            Case "text/calendar" : Return {".ics"}
            Case "text/vcard", "text/x-vcard" : Return {".vcf"}
            Case "application/rtf", "text/rtf" : Return {".rtf"}
            Case "application/json" : Return {".json"}
            Case "application/xml", "text/xml" : Return {".xml"}
            Case "application/msword" : Return {".doc"}
            Case "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : Return {".docx"}
            Case "application/vnd.ms-excel" : Return {".xls"}
            Case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : Return {".xlsx"}
            Case "application/vnd.ms-powerpoint" : Return {".ppt"}
            Case "application/vnd.openxmlformats-officedocument.presentationml.presentation" : Return {".pptx"}
            Case "application/vnd.oasis.opendocument.text" : Return {".odt"}
            Case "application/vnd.oasis.opendocument.spreadsheet" : Return {".ods"}
            Case "application/vnd.oasis.opendocument.presentation" : Return {".odp"}
            Case "application/epub+zip" : Return {".epub"}
            Case "application/pkcs7-mime" : Return {".p7m"}
            Case "application/pkcs7-signature" : Return {".p7s"}
            Case "application/zip", "application/x-zip-compressed" : Return {".zip"}
            Case "application/x-7z-compressed" : Return {".7z"}
            Case "application/vnd.rar", "application/x-rar-compressed" : Return {".rar"}
            Case "audio/mpeg", "audio/mp3" : Return {".mp3"}
            Case "audio/wav", "audio/x-wav", "audio/wave" : Return {".wav"}
            Case "audio/ogg" : Return {".ogg", ".oga", ".opus"}
            Case "audio/mp4", "audio/x-m4a" : Return {".m4a"}
            Case "video/mp4" : Return {".mp4", ".m4v"}
            Case "video/webm" : Return {".webm"}
            Case "video/quicktime" : Return {".mov"}
        End Select
        Return New String() {}
    End Function

    Private Shared Function EstensioniPerCaricamento(filtri As IEnumerable(Of String)) As List(Of String)
        Dim estensioni As New List(Of String)()
        Try
            If filtri IsNot Nothing Then
                For Each filtro As String In filtri
                    If String.IsNullOrWhiteSpace(filtro) Then Continue For
                    For Each parte As String In filtro.Split(New Char() {";"c, ","c, " "c, "|"c}, StringSplitOptions.RemoveEmptyEntries)
                        Dim voce As String = parte.Trim().ToLowerInvariant()
                        If voce.StartsWith("*.") Then voce = voce.Substring(1)
                        If voce.StartsWith(".") AndAlso voce.Length > 1 Then
                            AggiungiEstensioneUnica(estensioni, voce)
                        ElseIf voce.Contains("/") Then
                            For Each estensione As String In EstensioniDaTipo(voce)
                                AggiungiEstensioneUnica(estensioni, estensione)
                            Next
                        End If
                    Next
                Next
            End If
            If estensioni.Count = 0 Then
                For Each gruppo As String() In New String()() {EstensioniDocumentiCaricamento, EstensioniImmaginiCaricamento, EstensioniTestoCaricamento, EstensioniAudioCaricamento, EstensioniVideoCaricamento, EstensioniArchiviCaricamento, EstensioniAltreCaricamento}
                    For Each estensione As String In gruppo
                        AggiungiEstensioneUnica(estensioni, estensione)
                    Next
                Next
            End If
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.EstensioniPerCaricamento: " & ex.Message)
        End Try
        Return estensioni
    End Function

    Private Shared Function FileEsistenti(elementi As List(Of String), massimo As Integer) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If elementi Is Nothing Then Return risultato
            For Each elemento As String In elementi
                If String.IsNullOrWhiteSpace(elemento) OrElse Not File.Exists(elemento) Then Continue For
                If Not risultato.Contains(elemento) Then risultato.Add(elemento)
                If massimo > 0 AndAlso risultato.Count >= massimo Then Exit For
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.FileEsistenti: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function CartellaEsistente(elemento As String) As String
        Try
            Dim percorso As String = If(elemento, "").Trim()
            If percorso = "" Then Return ""
            If Directory.Exists(percorso) Then Return percorso
            If File.Exists(percorso) Then
                Dim cartella As String = Path.GetDirectoryName(percorso)
                If Not String.IsNullOrEmpty(cartella) AndAlso Directory.Exists(cartella) Then Return cartella
            End If
            Return ""
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.CartellaEsistente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Const FunzioneDescriviCampoFile As String =
        "function(){try{return {a:String(this.accept||''),m:!!this.multiple," &
        "d:!!(this.webkitdirectory||(this.hasAttribute&&this.hasAttribute('webkitdirectory')))," &
        "t:String((this.getAttribute&&(this.getAttribute('aria-label')||this.getAttribute('title')))||'')};}catch(e){return null;}}"

    Friend Sub SceltaFileRichiesta(parametri As JObject, sessione As String)
        SuUI(Async Sub()
                 Try
                     If parametri Is Nothing OrElse _devTools Is Nothing OrElse _chiusa Then Return
                     Dim idNodo As Integer = parametri.Value(Of Integer?)("backendNodeId").GetValueOrDefault()
                     If idNodo <= 0 Then
                         AnnunciaSempre("La pagina ha chiesto di scegliere un file in un modo che il browser non gestisce")
                         Return
                     End If
                     Dim multipli As Boolean = String.Equals(parametri.Value(Of String)("mode"), "selectMultiple", StringComparison.OrdinalIgnoreCase)
                     Dim accettati As String = ""
                     Dim cartella As Boolean = False
                     Dim informazioni As JObject = TryCast(Await _devTools.ChiamaSuNodoAsync(If(sessione, ""), idNodo, FunzioneDescriviCampoFile, 3000), JObject)
                     If informazioni IsNot Nothing Then
                         accettati = If(informazioni.Value(Of String)("a"), "")
                         multipli = multipli OrElse informazioni.Value(Of Boolean?)("m").GetValueOrDefault()
                         cartella = informazioni.Value(Of Boolean?)("d").GetValueOrDefault()
                     End If
                     Dim modo As String = If(cartella, "cartella", If(multipli, "multipli", "singolo"))
                     Dim sessioneFile As String = If(sessione, "")
                     Await Task.Yield()
                     If _chiusa Then Return
                     ScegliFile(modo, New List(Of String) From {accettati}, Sub(percorsi As List(Of String)) ImpostaFileScelti(sessioneFile, idNodo, percorsi, cartella))
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.SceltaFileRichiesta: " & ex.ToString())
                 End Try
             End Sub)
    End Sub

    Private Shared Function FileDellaCartella(cartella As String, massimo As Integer) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then Return risultato
            For Each percorso As String In Directory.EnumerateFiles(cartella, "*", SearchOption.AllDirectories)
                risultato.Add(percorso)
                If risultato.Count >= massimo Then Exit For
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.FileDellaCartella: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Async Sub ImpostaFileScelti(sessione As String, idNodo As Integer, percorsi As List(Of String), cartella As Boolean)
        Try
            If percorsi Is Nothing OrElse percorsi.Count = 0 OrElse _devTools Is Nothing OrElse _chiusa Then Return
            Dim file As List(Of String) = percorsi
            If cartella Then
                Dim radice As String = percorsi(0)
                file = Await Task.Run(Function() FileDellaCartella(radice, 2000))
                If file.Count = 0 Then
                    lettura.LeggiTesto("La cartella scelta non contiene file")
                    Return
                End If
            End If
            Dim elenco As New JArray()
            For Each percorso As String In file
                elenco.Add(percorso)
            Next
            Dim parametri As New JObject()
            parametri("files") = elenco
            parametri("backendNodeId") = idNodo
            Dim esito As JObject = Await _devTools.InviaAsync("DOM.setFileInputFiles", parametri, If(sessione, ""), 15000)
            If esito Is Nothing Then lettura.LeggiTesto("Impossibile consegnare il file alla pagina")
            If _cursore IsNot Nothing Then _cursore.BufferSporco = True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ImpostaFileScelti: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliFile(modo As String, filtri As List(Of String), consegna As Action(Of List(Of String)))
        Dim risposta As New RispostaUnica()
        Dim rispondi As Action(Of List(Of String)) = Sub(percorsi As List(Of String))
                                                         If Not risposta.Prendi() Then Return
                                                         Try
                                                             If consegna IsNot Nothing Then consegna(percorsi)
                                                         Catch exRisposta As Exception
                                                             log.ScriviLog("WGSchedaBrowser.ScegliFile risposta: " & exRisposta.Message)
                                                         End Try
                                                     End Sub
        Try
            Dim dialogo As New WGFileDialog()
            If modo = "cartella" Then
                dialogo.MultiselezioneConsentita = False
                Dim cartellaScelta As Action(Of String) = Sub(elemento As String)
                                                              Try
                                                                  Dim cartella As String = CartellaEsistente(elemento)
                                                                  If cartella = "" Then
                                                                      rispondi(Nothing)
                                                                      lettura.LeggiTesto("Nessuna cartella scelta")
                                                                  Else
                                                                      rispondi(New List(Of String) From {cartella})
                                                                      lettura.LeggiTesto("Cartella scelta: " & WGSceltaSalvataggio.NomeCartella(cartella))
                                                                  End If
                                                              Catch exCartella As Exception
                                                                  log.ScriviLog("WGSchedaBrowser.ScegliFile cartella: " & exCartella.Message)
                                                                  rispondi(Nothing)
                                                              End Try
                                                          End Sub
                AddHandler dialogo.singolaselezione, Sub(elemento As String) cartellaScelta(elemento)
                AddHandler dialogo.selezioneterminata, Sub(elementi As List(Of String)) cartellaScelta(If(elementi IsNot Nothing AndAlso elementi.Count > 0, elementi(0), ""))
            Else
                Dim multipli As Boolean = (modo = "multipli")
                dialogo.MultiselezioneConsentita = multipli
                For Each estensione As String In EstensioniPerCaricamento(filtri)
                    dialogo.AggiungiEstensione(estensione)
                Next
                AddHandler dialogo.selezioneterminata, Sub(elementi As List(Of String))
                                                           Try
                                                               Dim scelti As List(Of String) = FileEsistenti(elementi, If(multipli, 0, 1))
                                                               If scelti.Count = 0 Then
                                                                   rispondi(Nothing)
                                                                   lettura.LeggiTesto("Nessun file scelto")
                                                               Else
                                                                   rispondi(scelti)
                                                                   lettura.LeggiTesto(If(scelti.Count = 1, "File scelto: " & Path.GetFileName(scelti(0)), scelti.Count & " file scelti"))
                                                               End If
                                                           Catch exScelta As Exception
                                                               log.ScriviLog("WGSchedaBrowser.ScegliFile file: " & exScelta.Message)
                                                               rispondi(Nothing)
                                                           End Try
                                                       End Sub
            End If
            AddHandler dialogo.annullata, Sub() rispondi(Nothing)
            dialogo.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScegliFile: " & ex.ToString())
            rispondi(Nothing)
        End Try
    End Sub

    Private Shared Function NomePermesso(tipo As CoreWebView2PermissionKind) As String
        Select Case tipo
            Case CoreWebView2PermissionKind.Microphone : Return "microfono"
            Case CoreWebView2PermissionKind.Camera : Return "fotocamera"
            Case CoreWebView2PermissionKind.Geolocation : Return "posizione"
            Case CoreWebView2PermissionKind.Notifications : Return "notifiche"
            Case CoreWebView2PermissionKind.OtherSensors : Return "sensori"
            Case CoreWebView2PermissionKind.ClipboardRead : Return "appunti"
            Case CoreWebView2PermissionKind.MultipleAutomaticDownloads : Return "download"
            Case CoreWebView2PermissionKind.FileReadWrite : Return "file"
            Case CoreWebView2PermissionKind.MidiSystemExclusiveMessages : Return "midi"
            Case CoreWebView2PermissionKind.WindowManagement : Return "finestre"
        End Select
        Return ""
    End Function

    Private Shared Function DescriviPermessi(nomi As IEnumerable(Of String)) As String
        Dim descrizioni As New List(Of String)()
        For Each nome As String In nomi
            Select Case nome
                Case "attivita" : descrizioni.Add("sapere quando usi il computer")
                Case "file" : descrizioni.Add("accedere ai file")
                Case "rete" : descrizioni.Add("accedere alla rete locale")
                Case Else : descrizioni.Add(WGGestoreBrowser.NomePermesso(nome))
            End Select
        Next
        If descrizioni.Count = 0 Then Return "un permesso"
        If descrizioni.Count = 1 Then Return descrizioni(0)
        Return String.Join(", ", descrizioni.Take(descrizioni.Count - 1)) & " e " & descrizioni.Last()
    End Function

    Private Async Function ImpostaPosizioneAsync() As Task(Of Boolean)
        Try
            If _devTools Is Nothing OrElse Not _devTools.ECollegato Then Return False
            Dim posizione As Tuple(Of Double, Double, Double) = Await WGPosizioneWindows.OttieniAsync(6000)
            If posizione Is Nothing Then
                AnnunciaSempre("La posizione del computer non è disponibile, quindi il sito non la riceve")
                Return False
            End If
            Dim parametri As New JObject()
            parametri("latitude") = posizione.Item1
            parametri("longitude") = posizione.Item2
            parametri("accuracy") = posizione.Item3
            Await _devTools.InviaAsync("Emulation.setGeolocationOverride", CType(parametri.DeepClone(), JObject), "", 3000)
            For Each info As WGDevTools.SessioneFrame In _devTools.Frame.ToList()
                Await _devTools.InviaAsync("Emulation.setGeolocationOverride", CType(parametri.DeepClone(), JObject), info.IdSessione, 3000)
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ImpostaPosizioneAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RispondiVoce(voce As PermessoInAttesa, stato As CoreWebView2PermissionState)
        Try
            If voce Is Nothing OrElse Not voce.Risposta.Prendi() Then Return
            Try
                If voce.Argomenti IsNot Nothing Then voce.Argomenti.State = stato
            Catch exStato As Exception
                log.ScriviLog("WGSchedaBrowser.RispondiVoce stato: " & exStato.Message)
            End Try
            CompletaRinvio(voce.Rinvio)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RispondiVoce: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub RichiestaPermesso(argomenti As CoreWebView2PermissionRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Dim voce As New PermessoInAttesa() With {.Argomenti = argomenti, .Rinvio = rinvio}
        Try
            If _chiusa OrElse argomenti Is Nothing Then
                RispondiVoce(voce, CoreWebView2PermissionState.Deny)
                Return
            End If
            Dim tipo As CoreWebView2PermissionKind = argomenti.PermissionKind
            voce.Origine = WGArchivioBrowser.OrigineDi(If(argomenti.Uri, ""))
            If voce.Origine = "" Then voce.Origine = WGArchivioBrowser.OrigineDi(_indirizzo)
            Select Case tipo
                Case CoreWebView2PermissionKind.Autoplay, CoreWebView2PermissionKind.LocalFonts, CoreWebView2PermissionKind.UnknownPermission
                    RispondiVoce(voce, CoreWebView2PermissionState.Allow)
                    Return
            End Select
            voce.Nome = NomePermesso(tipo)
            If voce.Nome = "" Then
                RispondiVoce(voce, CoreWebView2PermissionState.Allow)
                Return
            End If
            Dim decisione As String = LeggiPermesso(Archivio, voce.Origine, voce.Nome)
            If decisione = "blocca" Then
                RispondiVoce(voce, CoreWebView2PermissionState.Deny)
                AnnunciaSempre("Permesso negato al sito per " & DescriviPermessi(New String() {voce.Nome}) & ", come stabilito in precedenza")
                Return
            End If
            If decisione = "consenti" Then
                If voce.Nome = "posizione" Then Await ImpostaPosizioneAsync()
                RispondiVoce(voce, CoreWebView2PermissionState.Allow)
                Return
            End If
            SyncLock _permessiInAttesa
                _permessiInAttesa.Add(voce)
            End SyncLock
            If _raccoltaPermessiProgrammata Then Return
            _raccoltaPermessiProgrammata = True
            Await Task.Delay(180)
            _raccoltaPermessiProgrammata = False
            ElaboraPermessiInAttesa()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RichiestaPermesso: " & ex.ToString())
            _raccoltaPermessiProgrammata = False
            RispondiVoce(voce, CoreWebView2PermissionState.Deny)
        End Try
    End Sub

    Private Sub ElaboraPermessiInAttesa()
        Try
            Dim elenco As List(Of PermessoInAttesa)
            SyncLock _permessiInAttesa
                elenco = _permessiInAttesa.ToList()
                _permessiInAttesa.Clear()
            End SyncLock
            If elenco.Count = 0 Then Return
            If _chiusa Then
                For Each voce As PermessoInAttesa In elenco
                    RispondiVoce(voce, CoreWebView2PermissionState.Deny)
                Next
                Return
            End If
            For Each gruppo As IGrouping(Of String, PermessoInAttesa) In elenco.GroupBy(Function(v) If(v.Origine, ""), StringComparer.OrdinalIgnoreCase)
                MostraRichiestaPermessi(gruppo.Key, gruppo.ToList())
            Next
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ElaboraPermessiInAttesa: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraRichiestaPermessi(origine As String, voci As List(Of PermessoInAttesa))
        Dim risposta As New RispostaUnica()
        Dim idPrompt As ULong = 0UL
        Dim rispondiTutti As Action(Of CoreWebView2PermissionState) = Sub(stato As CoreWebView2PermissionState)
                                                                          If Not risposta.Prendi() Then Return
                                                                          SyncLock _promptPermessi
                                                                              _promptPermessi.Remove(idPrompt)
                                                                          End SyncLock
                                                                          For Each voce As PermessoInAttesa In voci
                                                                              RispondiVoce(voce, stato)
                                                                          Next
                                                                      End Sub
        Try
            Dim nomi As List(Of String) = voci.Select(Function(v) v.Nome).Where(Function(n) Not String.IsNullOrEmpty(n)).Distinct().ToList()
            Dim soloMedia As Boolean = nomi.Count > 0 AndAlso nomi.All(Function(n) n = "microfono" OrElse n = "fotocamera")
            Dim archivioSiti As WGArchivioBrowser = Archivio
            Dim concedi As Func(Of Boolean, Task) = Async Function(ricorda As Boolean) As Task
                                                        Try
                                                            If ricorda Then
                                                                For Each n As String In nomi
                                                                    ScriviPermesso(archivioSiti, origine, n, "consenti")
                                                                Next
                                                            End If
                                                            If nomi.Contains("posizione") Then Await ImpostaPosizioneAsync()
                                                            rispondiTutti(CoreWebView2PermissionState.Allow)
                                                            If Not soloMedia Then lettura.LeggiTesto("Permesso concesso")
                                                        Catch exConcedi As Exception
                                                            log.ScriviLog("WGSchedaBrowser.MostraRichiestaPermessi concedi: " & exConcedi.Message)
                                                            rispondiTutti(CoreWebView2PermissionState.Deny)
                                                        End Try
                                                    End Function
            Dim menu As New WGMenu()
            menu.aggiungi("Consenti " & VoceSempre, Sub(s, ev)
                                                        Dim compito As Task = concedi(True)
                                                    End Sub)
            menu.aggiungi("Consenti solo questa volta", Sub(s, ev)
                                                            Dim compito As Task = concedi(False)
                                                        End Sub)
            menu.aggiungi("Blocca " & VoceSempre, Sub(s, ev)
                                                      For Each n As String In nomi
                                                          ScriviPermesso(archivioSiti, origine, n, "blocca")
                                                      Next
                                                      rispondiTutti(CoreWebView2PermissionState.Deny)
                                                      If Not soloMedia Then lettura.LeggiTesto("Permesso bloccato")
                                                  End Sub)
            menu.aggiungi("Non ora", Sub(s, ev) rispondiTutti(CoreWebView2PermissionState.Deny))
            AddHandler menu.abbandonato, Sub(s As Object, ev As EventArgs)
                                             If Not risposta.Data Then rispondiTutti(CoreWebView2PermissionState.Deny)
                                         End Sub
            SyncLock _promptPermessi
                _contatorePrompt += 1UL
                idPrompt = _contatorePrompt
                _promptPermessi(idPrompt) = menu
            End SyncLock
            Dim sito As String = WGArchivioBrowser.HostDi(origine)
            If sito = "" Then sito = _hostPagina
            Dim testo As String = If(soloMedia, "Il sito " & sito & " vuole usare " & DescriviPermessi(nomi), "Il sito " & sito & " chiede il permesso di usare " & DescriviPermessi(nomi))
            menu.avvia(If(_attiva, "", "Scheda " & Titolo & ". ") & testo, True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraRichiestaPermessi: " & ex.ToString())
            rispondiTutti(CoreWebView2PermissionState.Deny)
        End Try
    End Sub

    Friend Sub CatturaSchermoRichiesta(argomenti As CoreWebView2ScreenCaptureStartingEventArgs, rinvio As CoreWebView2Deferral)
        Dim risposta As New RispostaUnica()
        Dim concludi As Action(Of Boolean) = Sub(consenti As Boolean)
                                                 If Not risposta.Prendi() Then Return
                                                 If consenti Then PreparaSorveglianzaCondivisione()
                                                 Try
                                                     If argomenti IsNot Nothing Then argomenti.Cancel = Not consenti
                                                 Catch exRisposta As Exception
                                                     log.ScriviLog("WGSchedaBrowser.CatturaSchermoRichiesta risposta: " & exRisposta.Message)
                                                 End Try
                                                 CompletaRinvio(rinvio)
                                             End Sub
        Try
            If _chiusa OrElse argomenti Is Nothing Then
                concludi(False)
                Return
            End If
            If DateTime.Now <= _consensoCondivisioneFino Then
                _consensoCondivisioneFino = DateTime.MinValue
                concludi(True)
                Return
            End If
            Dim origine As String = ""
            Try
                Dim informazioni As CoreWebView2FrameInfo = argomenti.OriginalSourceFrameInfo
                If informazioni IsNot Nothing Then origine = WGArchivioBrowser.OrigineDi(If(informazioni.Source, ""))
            Catch exOrigine As Exception
                log.ScriviLog("WGSchedaBrowser.CatturaSchermoRichiesta origine: " & exOrigine.Message)
            End Try
            If origine = "" Then origine = WGArchivioBrowser.OrigineDi(_indirizzo)
            Dim sito As String = WGArchivioBrowser.HostDi(origine)
            If sito = "" Then sito = _hostPagina
            Dim archivioSiti As WGArchivioBrowser = Archivio
            If LeggiPermesso(archivioSiti, origine, "schermo") = "blocca" Then
                concludi(False)
                AnnunciaSempre("Condivisione dello schermo negata a " & sito & ", come stabilito in precedenza")
                Return
            End If
            Dim menu As New WGMenu()
            menu.aggiungi("Condividi lo schermo intero", Sub(s, ev)
                                                             _condivisioneInCorso = "lo schermo intero"
                                                             concludi(True)
                                                             Annuncia("Avvio della condivisione dello schermo intero")
                                                         End Sub)
            menu.aggiungi("Non condividere nulla", Sub(s, ev)
                                                       concludi(False)
                                                       Annuncia("Condivisione dello schermo annullata")
                                                   End Sub)
            menu.aggiungi("Blocca " & VoceSempre, Sub(s, ev)
                                                      ScriviPermesso(archivioSiti, origine, "schermo", "blocca")
                                                      concludi(False)
                                                      lettura.LeggiTesto("Condivisione dello schermo bloccata per " & sito)
                                                  End Sub)
            AddHandler menu.abbandonato, Sub(s As Object, ev As EventArgs) concludi(False)
            menu.avvia(If(_attiva, "", "Scheda " & Titolo & ". ") & sito & " vuole condividere lo schermo intero", True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CatturaSchermoRichiesta: " & ex.ToString())
            concludi(False)
        End Try
    End Sub

    Private Function IdProcessoMotore() As Integer
        Try
            Dim c As CoreWebView2 = Core
            If c IsNot Nothing Then Return CInt(c.BrowserProcessId)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.IdProcessoMotore: " & ex.Message)
        End Try
        Return 0
    End Function

    Private Sub PreparaSorveglianzaCondivisione()
        Try
            _tentativoCondivisione += 1
            _catturaAvviata = False
            _sceltaChiusaDaWinGabriel = False
            Dim idProcesso As Integer = IdProcessoMotore()
            If idProcesso <= 0 Then Return
            SorvegliaFinestreCondivisione(_tentativoCondivisione, WGCondivisioneSchermo.FinestreDelProcesso(idProcesso), idProcesso)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.PreparaSorveglianzaCondivisione: " & ex.Message)
        End Try
    End Sub

    Private Async Sub SorvegliaFinestreCondivisione(tentativo As Integer, precedenti As HashSet(Of IntPtr), idProcesso As Integer)
        Try
            Dim gestite As New HashSet(Of IntPtr)(precedenti)
            Dim candidate As New Dictionary(Of IntPtr, DateTime)()
            Dim sceltaSuperata As Boolean = False
            Dim avvioVisto As Boolean = False
            Dim fine As DateTime = DateTime.Now.AddSeconds(25)
            While DateTime.Now < fine
                If _chiusa OrElse tentativo <> _tentativoCondivisione Then Return
                Dim avviata As Boolean = _catturaAvviata OrElse sceltaSuperata
                If avviata AndAlso Not avvioVisto Then
                    avvioVisto = True
                    fine = DateTime.Now.AddSeconds(5)
                End If
                Dim presenti As HashSet(Of IntPtr) = WGCondivisioneSchermo.FinestreDelProcesso(idProcesso)
                For Each candidata As IntPtr In candidate.Keys.ToList()
                    If presenti.Contains(candidata) Then Continue For
                    candidate.Remove(candidata)
                    gestite.Add(candidata)
                    sceltaSuperata = True
                Next
                For Each finestra As IntPtr In presenti
                    If gestite.Contains(finestra) Then Continue For
                    If WGCondivisioneSchermo.EBarraDiCondivisione(finestra) OrElse (avviata AndAlso Not WGCondivisioneSchermo.ESceltaDiCondivisione(finestra)) Then
                        gestite.Add(finestra)
                        candidate.Remove(finestra)
                        WGCondivisioneSchermo.Nascondi(finestra)
                        If _contenitore IsNot Nothing Then _contenitore.AttivaFinestraPrincipale()
                        Continue For
                    End If
                    If WGCondivisioneSchermo.Titolo(finestra).Trim() = "" Then Continue For
                    Dim comparsa As DateTime
                    If Not candidate.TryGetValue(finestra, comparsa) Then
                        candidate(finestra) = DateTime.Now
                    ElseIf Not _catturaAvviata AndAlso (DateTime.Now - comparsa).TotalSeconds >= 8 Then
                        candidate.Remove(finestra)
                        gestite.Add(finestra)
                        _sceltaChiusaDaWinGabriel = True
                        WGCondivisioneSchermo.Chiudi(finestra)
                        RipristinaFinestraMarcata()
                        If _contenitore IsNot Nothing Then _contenitore.AttivaFinestraPrincipale()
                        AnnunciaSempre("La scelta automatica di cosa condividere non ha funzionato con questo motore: WinGabriel ha chiuso la finestra di Windows e la condivisione non è partita. Riprovare, oppure scegliere lo schermo intero")
                    End If
                Next
                Await Task.Delay(100)
            End While
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SorvegliaFinestreCondivisione: " & ex.Message)
        Finally
            Try
                If tentativo = _tentativoCondivisione Then RipristinaFinestraMarcata()
            Catch exFine As Exception
                log.ScriviLog("WGSchedaBrowser.SorvegliaFinestreCondivisione fine: " & exFine.Message)
            End Try
        End Try
    End Sub

    Private Sub RipristinaFinestraMarcata()
        Try
            Dim finestra As IntPtr = _finestraMarcata
            If finestra = IntPtr.Zero Then Return
            _finestraMarcata = IntPtr.Zero
            WGCondivisioneSchermo.TogliMarcatore(finestra)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RipristinaFinestraMarcata: " & ex.Message)
        End Try
    End Sub

    Private Async Sub RipristinaFinestraMarcataDopo(finestra As IntPtr, millisecondi As Integer)
        Try
            Await Task.Delay(millisecondi)
            If finestra <> IntPtr.Zero AndAlso _finestraMarcata = finestra Then RipristinaFinestraMarcata()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RipristinaFinestraMarcataDopo: " & ex.Message)
        End Try
    End Sub

    Friend Sub RichiestaCondivisioneSchermo(id As String, audio As Boolean, origine As String)
        SuUI(Sub() MostraSceltaCondivisione(id, audio, origine))
    End Sub

    Private Sub MostraSceltaCondivisione(id As String, audio As Boolean, origine As String)
        Dim risposta As New RispostaUnica()
        Try
            If String.IsNullOrEmpty(id) Then Return
            If id = _ultimaRichiestaCondivisione Then Return
            _ultimaRichiestaCondivisione = id
            Dim sito As String = WGArchivioBrowser.HostDi(origine)
            If sito = "" Then sito = _hostPagina
            Dim archivioSiti As WGArchivioBrowser = Archivio
            If LeggiPermesso(archivioSiti, origine, "schermo") = "blocca" Then
                risposta.Prendi()
                RispondiCondivisione(id, "")
                AnnunciaSempre("Condivisione dello schermo negata a " & sito & ", come stabilito in precedenza")
                Return
            End If
            Dim sorgenti As List(Of WGCondivisioneSchermo.Sorgente) = WGCondivisioneSchermo.Sorgenti()
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo(If(_attiva, "", "Scheda " & Titolo & ". ") & sito & " vuole condividere lo schermo" & If(audio, " e chiede anche l'audio", "") & ". Scegliere cosa mostrare, Esc per non condividere")
            For i As Integer = 0 To sorgenti.Count - 1
                lista.aggiungiElemento(sorgenti(i).Descrizione, "c|" & i.ToString())
            Next
            lista.aggiungiElemento("Non condividere nulla", "no")
            Dim concludi As Action(Of WGCondivisioneSchermo.Sorgente) = Sub(scelta As WGCondivisioneSchermo.Sorgente)
                                                                            If Not risposta.Prendi() Then Return
                                                                            Try
                                                                                lista.abbandona()
                                                                            Catch exChiudi As Exception
                                                                                log.ScriviLog("WGSchedaBrowser.MostraSceltaCondivisione chiusura: " & exChiudi.Message)
                                                                            End Try
                                                                            Try
                                                                                If scelta Is Nothing Then
                                                                                    RispondiCondivisione(id, "")
                                                                                    Annuncia("Condivisione dello schermo annullata")
                                                                                    Return
                                                                                End If
                                                                                Dim superficie As String = "schermo"
                                                                                If Not scelta.Schermo Then
                                                                                    RipristinaFinestraMarcata()
                                                                                    If Not WGCondivisioneSchermo.MarcaFinestra(scelta.Finestra) Then
                                                                                        RispondiCondivisione(id, "")
                                                                                        AnnunciaSempre("La finestra scelta non si può condividere: appartiene a un programma avviato come amministratore, è stata chiusa oppure non permette di essere scelta. Riprovare scegliendo lo schermo intero")
                                                                                        Return
                                                                                    End If
                                                                                    _finestraMarcata = scelta.Finestra
                                                                                    RipristinaFinestraMarcataDopo(scelta.Finestra, 40000)
                                                                                    superficie = "finestra"
                                                                                End If
                                                                                _consensoCondivisioneFino = DateTime.Now.AddSeconds(30)
                                                                                _condivisioneInCorso = scelta.Descrizione
                                                                                RispondiCondivisione(id, superficie)
                                                                                Annuncia("Avvio della condivisione: " & scelta.Descrizione)
                                                                            Catch exScelta As Exception
                                                                                log.ScriviLog("WGSchedaBrowser.MostraSceltaCondivisione scelta: " & exScelta.ToString())
                                                                            End Try
                                                                        End Sub
            AddHandler lista.ElementoSelezionato, Sub(scelto As String)
                                                      Try
                                                          Dim indice As Integer
                                                          If scelto IsNot Nothing AndAlso scelto.StartsWith("c|", StringComparison.Ordinal) AndAlso Integer.TryParse(scelto.Substring(2), indice) AndAlso indice >= 0 AndAlso indice < sorgenti.Count Then
                                                              concludi(sorgenti(indice))
                                                          Else
                                                              concludi(Nothing)
                                                          End If
                                                      Catch exElemento As Exception
                                                          log.ScriviLog("WGSchedaBrowser.MostraSceltaCondivisione elemento: " & exElemento.Message)
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) concludi(Nothing)
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraSceltaCondivisione: " & ex.ToString())
            If risposta.Prendi() Then RispondiCondivisione(id, "")
        End Try
    End Sub

    Private Async Function InviaAllAgenteAsync(script As String) As Task(Of Integer)
        Dim positivi As Integer = 0
        Try
            If _pagina Is Nothing OrElse Not _pagina.Pronta OrElse String.IsNullOrEmpty(script) Then Return 0
            Dim risultati As List(Of String) = Await _pagina.ValutaInTuttiIFrameAsync(script, 3000)
            For Each risultato As String In risultati
                positivi += ValorePositivo(risultato)
            Next
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InviaAllAgenteAsync: " & ex.Message)
        End Try
        Return positivi
    End Function

    Private Shared Function ValorePositivo(risultato As String) As Integer
        Try
            Dim testo As String = If(risultato, "").Trim()
            If testo = "true" Then Return 1
            Dim numero As Double
            If Double.TryParse(testo, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, numero) AndAlso numero > 0 Then Return CInt(Math.Min(numero, 1000))
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.ValorePositivo: " & ex.Message)
        End Try
        Return 0
    End Function

    Private Async Function InviaAllAgenteConGestoAsync(script As String) As Task(Of Integer)
        Try
            Dim dt As WGDevTools = _devTools
            If dt IsNot Nothing AndAlso dt.ECollegato Then
                Dim r As JToken = Await dt.ValutaAsync("", script, 3000)
                If r IsNot Nothing AndAlso ValorePositivo(r.ToString().ToLowerInvariant()) > 0 Then Return 1
                For Each f As WGDevTools.SessioneFrame In dt.Frame.ToList()
                    r = Await dt.ValutaAsync(f.IdSessione, script, 3000)
                    If r IsNot Nothing AndAlso ValorePositivo(r.ToString().ToLowerInvariant()) > 0 Then Return 1
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InviaAllAgenteConGestoAsync: " & ex.Message)
        End Try
        Return Await InviaAllAgenteAsync(script)
    End Function

    Private Async Sub RispondiCondivisione(id As String, superficie As String)
        Try
            Dim script As String = "!!(window.__wgAgente&&window.__wgAgente.rispostaCondivisione&&window.__wgAgente.rispostaCondivisione(" & JsonConvert.SerializeObject(If(id, "")) & "," & JsonConvert.SerializeObject(If(superficie, "")) & "))"
            Dim ricevuta As Integer = Await InviaAllAgenteConGestoAsync(script)
            If ricevuta > 0 OrElse String.IsNullOrEmpty(superficie) Then Return
            log.ScriviLog("WGSchedaBrowser.RispondiCondivisione: la pagina non ha ricevuto la scelta della condivisione")
            _consensoCondivisioneFino = DateTime.MinValue
            _condivisioneInCorso = ""
            RipristinaFinestraMarcata()
            AnnunciaSempre("La pagina non ha ricevuto la scelta: la condivisione non è partita. Riprovare dal comando della riunione")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RispondiCondivisione: " & ex.Message)
        End Try
    End Sub

    Friend Sub CondivisioneAvviata(superficie As String)
        SuUI(Sub()
                 Try
                     _catturaAvviata = True
                     _consensoCondivisioneFino = DateTime.MinValue
                     RipristinaFinestraMarcata()
                     If _condivisioneInCorso = "" Then _condivisioneInCorso = If(superficie = "window", "una finestra", "lo schermo intero")
                     AnnunciaSempre("Condivisione avviata: " & _condivisioneInCorso & ". Per interromperla: Alt, Strumenti della pagina, Interrompi la condivisione, oppure il comando della riunione")
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.CondivisioneAvviata: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend ReadOnly Property CondivisioneAttiva As Boolean
        Get
            Return _condivisioneInCorso <> ""
        End Get
    End Property

    Friend Async Sub InterrompiCondivisione()
        Try
            If _condivisioneInCorso = "" Then
                lettura.LeggiTesto("In questa scheda non c'è nessuna condivisione dello schermo")
                Return
            End If
            Dim fermate As Integer = Await InviaAllAgenteAsync("(window.__wgAgente&&window.__wgAgente.fermaCondivisione)?window.__wgAgente.fermaCondivisione():0")
            If fermate > 0 Then
                CondivisioneTerminata()
            Else
                AnnunciaSempre("Non è stato possibile interrompere la condivisione da WinGabriel: usare il comando della riunione, per esempio Interrompi presentazione, oppure chiudere la scheda")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InterrompiCondivisione: " & ex.Message)
        End Try
    End Sub

    Friend Sub CondivisioneTerminata()
        SuUI(Sub()
                 Try
                     If _condivisioneInCorso = "" Then Return
                     _condivisioneInCorso = ""
                     _catturaAvviata = False
                     _consensoCondivisioneFino = DateTime.MinValue
                     AnnunciaSempre("Condivisione dello schermo terminata")
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.CondivisioneTerminata: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub CondivisioneNonRiuscita(motivo As String)
        SuUI(Sub()
                 Try
                     Dim chiusaDaWinGabriel As Boolean = _sceltaChiusaDaWinGabriel
                     _sceltaChiusaDaWinGabriel = False
                     _condivisioneInCorso = ""
                     _catturaAvviata = False
                     _consensoCondivisioneFino = DateTime.MinValue
                     _tentativoCondivisione += 1
                     RipristinaFinestraMarcata()
                     If chiusaDaWinGabriel Then Return
                     Select Case If(motivo, "")
                         Case "superficie"
                             AnnunciaSempre("Il motore del browser stava condividendo qualcosa di diverso da quello scelto, quindi la condivisione è stata fermata subito per sicurezza. Riprovare")
                         Case "NotReadableError"
                             AnnunciaSempre("Windows non ha permesso di catturare quello che è stato scelto: la condivisione non è partita. Riprovare, oppure scegliere lo schermo intero")
                         Case "NotFoundError"
                             AnnunciaSempre("Quello che è stato scelto non è più disponibile: la condivisione non è partita. Riprovare")
                         Case Else
                             AnnunciaSempre("La condivisione non è partita. Riprovare, oppure scegliere lo schermo intero")
                     End Select
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.CondivisioneNonRiuscita: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Shared ReadOnly ColoriNominati As String()() = {
        New String() {"Nero", "#000000"},
        New String() {"Bianco", "#ffffff"},
        New String() {"Grigio", "#808080"},
        New String() {"Grigio chiaro", "#d3d3d3"},
        New String() {"Grigio scuro", "#404040"},
        New String() {"Rosso", "#ff0000"},
        New String() {"Rosso scuro", "#8b0000"},
        New String() {"Bordeaux", "#800020"},
        New String() {"Rosa", "#ffc0cb"},
        New String() {"Fucsia", "#ff00ff"},
        New String() {"Arancione", "#ffa500"},
        New String() {"Corallo", "#ff7f50"},
        New String() {"Giallo", "#ffff00"},
        New String() {"Oro", "#ffd700"},
        New String() {"Beige", "#f5f5dc"},
        New String() {"Marrone", "#8b4513"},
        New String() {"Verde", "#008000"},
        New String() {"Verde chiaro", "#90ee90"},
        New String() {"Verde scuro", "#006400"},
        New String() {"Verde oliva", "#808000"},
        New String() {"Turchese", "#40e0d0"},
        New String() {"Azzurro", "#007fff"},
        New String() {"Celeste", "#87ceeb"},
        New String() {"Blu", "#0000ff"},
        New String() {"Blu scuro", "#00008b"},
        New String() {"Blu marino", "#000080"},
        New String() {"Viola", "#800080"},
        New String() {"Lilla", "#c8a2c8"}
    }

    Private Shared Function NormalizzaColore(valore As String) As String
        Try
            Dim testo As String = If(valore, "").Trim().ToLowerInvariant()
            If testo = "" Then Return ""
            For Each voce As String() In ColoriNominati
                If voce(0).ToLowerInvariant() = testo Then Return voce(1)
            Next
            If testo.StartsWith("#", StringComparison.Ordinal) Then testo = testo.Substring(1)
            If Not System.Text.RegularExpressions.Regex.IsMatch(testo, "^([0-9a-f]{3}|[0-9a-f]{6})$") Then Return ""
            If testo.Length = 3 Then testo = String.Concat(testo(0), testo(0), testo(1), testo(1), testo(2), testo(2))
            Return "#" & testo
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.NormalizzaColore: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function DescriviColore(codice As String) As String
        Try
            Dim colore As String = NormalizzaColore(codice)
            If colore = "" Then Return "non indicato"
            Dim rosso As Integer = Convert.ToInt32(colore.Substring(1, 2), 16)
            Dim verde As Integer = Convert.ToInt32(colore.Substring(3, 2), 16)
            Dim blu As Integer = Convert.ToInt32(colore.Substring(5, 2), 16)
            Dim migliore As String = ""
            Dim distanzaMinima As Double = Double.MaxValue
            For Each voce As String() In ColoriNominati
                If voce(1) = colore Then Return voce(0)
                Dim r As Integer = Convert.ToInt32(voce(1).Substring(1, 2), 16)
                Dim g As Integer = Convert.ToInt32(voce(1).Substring(3, 2), 16)
                Dim b As Integer = Convert.ToInt32(voce(1).Substring(5, 2), 16)
                Dim distanza As Double = Math.Pow(rosso - r, 2) * 0.3 + Math.Pow(verde - g, 2) * 0.59 + Math.Pow(blu - b, 2) * 0.11
                If distanza < distanzaMinima Then
                    distanzaMinima = distanza
                    migliore = voce(0)
                End If
            Next
            Return "codice " & colore.ToUpperInvariant() & ", simile a " & migliore
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.DescriviColore: " & ex.Message)
            Return If(codice, "")
        End Try
    End Function

    Friend Sub SceltaColoreRichiesta(id As String, valore As String, etichetta As String, origine As String)
        SuUI(Sub() MostraSceltaColore(id, valore, etichetta, origine))
    End Sub

    Private Sub MostraSceltaColore(id As String, valore As String, etichetta As String, origine As String)
        Dim risposta As New RispostaUnica()
        Dim concludi As Action(Of String) = Sub(colore As String)
                                                If Not risposta.Prendi() Then Return
                                                RispondiColore(id, colore)
                                            End Sub
        Try
            If String.IsNullOrEmpty(id) Then Return
            If _chiusa Then
                concludi("")
                Return
            End If
            Dim attuale As String = NormalizzaColore(valore)
            Dim codiceInCorso As Boolean = False
            Dim menu As New WGMenu()
            Dim posizione As Integer = -1
            For i As Integer = 0 To ColoriNominati.Length - 1
                Dim nome As String = ColoriNominati(i)(0)
                Dim codice As String = ColoriNominati(i)(1)
                If codice = attuale Then posizione = i
                menu.aggiungi(nome, Sub(s, ev)
                                        concludi(codice)
                                        Annuncia("Colore scelto: " & nome)
                                    End Sub)
            Next
            menu.aggiungi("Scrivi il codice del colore, per esempio #1A73E8", Sub(s, ev)
                                                                                  codiceInCorso = True
                                                                                  ChiediCodiceColore(attuale, concludi)
                                                                              End Sub)
            menu.aggiungi("Lascia il colore com'è", Sub(s, ev) concludi(""))
            AddHandler menu.abbandonato, Sub(s As Object, ev As EventArgs)
                                             If Not codiceInCorso Then concludi("")
                                         End Sub
            If posizione >= 0 Then menu.posiziona(posizione)
            Dim sito As String = WGArchivioBrowser.HostDi(origine)
            Dim descrizione As String
            If Not String.IsNullOrWhiteSpace(etichetta) Then
                descrizione = "Scelta del colore per " & etichetta.Trim()
            ElseIf sito <> "" AndAlso sito <> _hostPagina Then
                descrizione = sito & " chiede di scegliere un colore"
            Else
                descrizione = "Scelta del colore"
            End If
            menu.avvia(If(_attiva, "", "Scheda " & Titolo & ". ") & descrizione & ". Colore attuale: " & DescriviColore(attuale), True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraSceltaColore: " & ex.ToString())
            concludi("")
        End Try
    End Sub

    Private Sub ChiediCodiceColore(attuale As String, concludi As Action(Of String))
        Try
            Dim confermato As Boolean = False
            Dim campo As New WGCampoTesto()
            campo.init("Codice del colore: il cancelletto seguito da sei cifre esadecimali, per esempio #1A73E8, oppure il nome di un colore", If(attuale, "").ToUpperInvariant())
            AddHandler campo.chiusura, Sub(s As Object, ev As EventArgs)
                                           Dim testo As String = ""
                                           Try
                                               testo = If(campo.OttieniTesto(), "")
                                           Catch exTesto As Exception
                                               log.ScriviLog("WGSchedaBrowser.ChiediCodiceColore testo: " & exTesto.Message)
                                           End Try
                                           confermato = True
                                           Try
                                               campo.chiudi()
                                           Catch exChiudi As Exception
                                               log.ScriviLog("WGSchedaBrowser.ChiediCodiceColore chiusura: " & exChiudi.Message)
                                           End Try
                                           Dim colore As String = NormalizzaColore(testo)
                                           If colore = "" Then
                                               concludi("")
                                               AnnunciaSempre("Codice del colore non riconosciuto: il colore resta com'era")
                                           Else
                                               concludi(colore)
                                               Annuncia("Colore scelto: " & DescriviColore(colore))
                                           End If
                                       End Sub
            AddHandler campo.Disposed, Sub(s As Object, ev As EventArgs)
                                           If Not confermato Then concludi("")
                                       End Sub
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiediCodiceColore: " & ex.ToString())
            If concludi IsNot Nothing Then concludi("")
        End Try
    End Sub

    Private Async Sub RispondiColore(id As String, colore As String)
        Try
            Dim script As String = "!!(window.__wgAgente&&window.__wgAgente.impostaColore&&window.__wgAgente.impostaColore(" & JsonConvert.SerializeObject(If(id, "")) & "," & JsonConvert.SerializeObject(If(colore, "")) & "))"
            Dim ricevuta As Integer = Await InviaAllAgenteAsync(script)
            If ricevuta = 0 AndAlso Not String.IsNullOrEmpty(colore) Then
                log.ScriviLog("WGSchedaBrowser.RispondiColore: la pagina non ha ricevuto il colore scelto")
                AnnunciaSempre("La pagina non ha ricevuto il colore scelto")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RispondiColore: " & ex.Message)
        End Try
    End Sub

    Private Sub RispondiPermessoNotifiche(id As Integer, origine As String, valore As String)
        Try
            If _pagina Is Nothing OrElse Not _pagina.Pronta Then Return
            Dim script As String = "window.__wgAgente&&window.__wgAgente.rispostaPermesso(" & id & "," & JsonConvert.SerializeObject(valore) & "," & JsonConvert.SerializeObject(origine) & ");"
            _pagina.EseguiScriptInTuttiIFrame(script)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RispondiPermessoNotifiche: " & ex.Message)
        End Try
    End Sub

    Friend Sub RichiestaPermessoNotifiche(id As Integer, origine As String)
        SuUI(Sub()
                 Try
                     Dim archivioSiti As WGArchivioBrowser = Archivio
                     Dim decisione As String = LeggiPermesso(archivioSiti, origine, "notifiche")
                     If decisione = "consenti" Then
                         RispondiPermessoNotifiche(id, origine, "granted")
                         Return
                     End If
                     If decisione = "blocca" Then
                         RispondiPermessoNotifiche(id, origine, "denied")
                         Return
                     End If
                     Dim risposta As New RispostaUnica()
                     Dim menu As New WGMenu()
                     menu.aggiungi("Consenti, le notifiche verranno lette dalla sintesi", Sub(s, e)
                                                                                              If Not risposta.Prendi() Then Return
                                                                                              ScriviPermesso(archivioSiti, origine, "notifiche", "consenti")
                                                                                              RispondiPermessoNotifiche(id, origine, "granted")
                                                                                          End Sub)
                     menu.aggiungi("Blocca", Sub(s, e)
                                                 If Not risposta.Prendi() Then Return
                                                 ScriviPermesso(archivioSiti, origine, "notifiche", "blocca")
                                                 RispondiPermessoNotifiche(id, origine, "denied")
                                             End Sub)
                     menu.aggiungi("Non ora", Sub(s, e)
                                                  If Not risposta.Prendi() Then Return
                                                  RispondiPermessoNotifiche(id, origine, "default")
                                              End Sub)
                     AddHandler menu.abbandonato, Sub(s As Object, e As EventArgs)
                                                      If risposta.Prendi() Then RispondiPermessoNotifiche(id, origine, "default")
                                                  End Sub
                     menu.avvia(If(_attiva, "", "Scheda " & Titolo & ". ") & "Il sito " & WGArchivioBrowser.HostDi(origine) & " vuole inviare notifiche", True)
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.RichiestaPermessoNotifiche: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub MostraMenuContestuale(dati As DatiMenuContestuale, argomenti As CoreWebView2ContextMenuRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Dim risposta As New RispostaUnica()
        Dim rispondi As Action(Of Integer) = Sub(comando As Integer)
                                                 If Not risposta.Prendi() Then Return
                                                 _menuContestualeAperto = False
                                                 Try
                                                     If argomenti IsNot Nothing Then
                                                         argomenti.Handled = True
                                                         If comando >= 0 Then argomenti.SelectedCommandId = comando
                                                     End If
                                                 Catch exRisposta As Exception
                                                     log.ScriviLog("WGSchedaBrowser.MostraMenuContestuale risposta: " & exRisposta.Message)
                                                 End Try
                                                 CompletaRinvio(rinvio)
                                             End Sub
        Try
            If _chiusa OrElse dati Is Nothing Then
                rispondi(-1)
                Return
            End If
            _menuContestualeAperto = True
            Dim menu As New WGMenu()
            For Each voce As Tuple(Of Integer, String) In dati.Voci
                Dim comando As Integer = voce.Item1
                menu.aggiungi(voce.Item2, Sub(s, ev) rispondi(comando))
            Next
            If dati.Link <> "" Then
                Dim link As String = dati.Link
                menu.aggiungi("Apri il link in una nuova scheda", Sub(s, ev)
                                                                      rispondi(-1)
                                                                      _contenitore.NuovaScheda(link, False, Me)
                                                                  End Sub)
                menu.aggiungi("Apri il link in una scheda in secondo piano", Sub(s, ev)
                                                                                 rispondi(-1)
                                                                                 _contenitore.NuovaScheda(link, True, Me)
                                                                             End Sub)
                menu.aggiungi("Copia l'indirizzo del link", Sub(s, ev)
                                                                rispondi(-1)
                                                                CopiaNegliAppunti(link, "Indirizzo del link copiato")
                                                            End Sub)
                menu.aggiungi("Scarica il file del link", Sub(s, ev)
                                                              rispondi(-1)
                                                              Scarica(link)
                                                          End Sub)
            End If
            Dim media As Boolean = dati.Tipo = CoreWebView2ContextMenuTargetKind.Image OrElse dati.Tipo = CoreWebView2ContextMenuTargetKind.Video OrElse dati.Tipo = CoreWebView2ContextMenuTargetKind.Audio
            If dati.Sorgente <> "" AndAlso media Then
                Dim sorgente As String = dati.Sorgente
                Dim x As Integer = dati.X
                Dim y As Integer = dati.Y
                If dati.Tipo = CoreWebView2ContextMenuTargetKind.Image Then
                    menu.aggiungi("Descrivi l'immagine con l'intelligenza artificiale", Sub(s, ev)
                                                                                            rispondi(-1)
                                                                                            DescriviInPunto(x, y)
                                                                                        End Sub)
                Else
                    menu.aggiungi("Riproduci con il lettore di WinGabriel", Sub(s, ev)
                                                                                rispondi(-1)
                                                                                RiproduciConWinGabriel(sorgente)
                                                                            End Sub)
                End If
                menu.aggiungi("Copia l'indirizzo del file", Sub(s, ev)
                                                                rispondi(-1)
                                                                CopiaNegliAppunti(sorgente, "Indirizzo copiato")
                                                            End Sub)
                menu.aggiungi("Salva il file", Sub(s, ev)
                                                   rispondi(-1)
                                                   Scarica(sorgente)
                                               End Sub)
            End If
            If dati.Selezione <> "" Then
                Dim selezione As String = dati.Selezione
                menu.aggiungi("Leggi il testo selezionato", Sub(s, ev)
                                                                rispondi(-1)
                                                                lettura.LeggiTesto(selezione)
                                                            End Sub)
            End If
            AddHandler menu.abbandonato, Sub(s As Object, ev As EventArgs) rispondi(-1)
            menu.avvia("Menu contestuale della pagina", True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraMenuContestuale: " & ex.ToString())
            rispondi(-1)
        End Try
    End Sub

    Friend Sub ChiediDoveSalvare(argomenti As CoreWebView2DownloadStartingEventArgs, rinvio As CoreWebView2Deferral, elemento As ElementoDownload, operazione As CoreWebView2DownloadOperation, cartella As String, nome As String)
        Dim risposta As New RispostaUnica()
        Try
            Dim salvataggio As New WGSceltaSalvataggio("Nome con cui salvare il file scaricato", nome)
            AddHandler salvataggio.Confermato, Sub(percorso As String)
                                                   If Not risposta.Prendi() Then Return
                                                   Try
                                                       argomenti.ResultFilePath = percorso
                                                       DownloadAvviato(elemento, percorso, operazione)
                                                   Catch exConferma As Exception
                                                       log.ScriviLog("WGSchedaBrowser.ChiediDoveSalvare conferma: " & exConferma.Message)
                                                   End Try
                                                   CompletaRinvio(rinvio)
                                               End Sub
            AddHandler salvataggio.Annullato, Sub(s As Object, ev As EventArgs)
                                                  If Not risposta.Prendi() Then Return
                                                  Try
                                                      argomenti.Cancel = True
                                                  Catch exAnnulla As Exception
                                                      log.ScriviLog("WGSchedaBrowser.ChiediDoveSalvare annulla: " & exAnnulla.Message)
                                                  End Try
                                                  CompletaRinvio(rinvio)
                                                  lettura.LeggiTesto("Scaricamento annullato")
                                              End Sub
            salvataggio.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ChiediDoveSalvare: " & ex.ToString())
            If risposta.Prendi() Then
                Try
                    Dim percorso As String = WGBrowserGestoreDownload.PercorsoLibero(cartella, nome)
                    argomenti.ResultFilePath = percorso
                    DownloadAvviato(elemento, percorso, operazione)
                Catch exRiserva As Exception
                    log.ScriviLog("WGSchedaBrowser.ChiediDoveSalvare riserva: " & exRiserva.Message)
                End Try
                CompletaRinvio(rinvio)
            End If
        End Try
    End Sub

    Friend Sub DownloadAvviato(elemento As ElementoDownload, percorso As String, operazione As CoreWebView2DownloadOperation)
        Try
            If elemento Is Nothing Then Return
            _istanteUltimoDownload = DateTime.Now
            elemento.PercorsoCompleto = percorso
            Dim registro As WGRegistroDownload = _contenitore.RegistroDownload
            Dim gestione As New WGDownloadWebView2(operazione, elemento, AddressOf registro.Notifica)
            registro.Registra(elemento, percorso, Me, gestione)
            Dim dimensione As String = If(elemento.Totali > 0, ", " & WGGestoreBrowser.DescriviDimensione(elemento.Totali), "")
            MostraInizioDownload("Scaricamento di " & Path.GetFileName(percorso) & dimensione & " avviato")
            If _creataDallaPagina Then
                Dim indirizzoAttuale As String = _indirizzo
                If String.IsNullOrEmpty(indirizzoAttuale) OrElse indirizzoAttuale = "about:blank" OrElse String.Equals(indirizzoAttuale, elemento.Indirizzo, StringComparison.OrdinalIgnoreCase) Then
                    Task.Delay(400).ContinueWith(Sub(t) SuUI(Sub() _contenitore.ChiudiScheda(Me, True)))
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DownloadAvviato: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraInizioDownload(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim completo As String = If(_attiva, "", "Scheda " & Titolo & ": ") & testo
            SuUI(Sub()
                     Try
                         Dim avviso As New WGMessaggi()
                         avviso.Messaggio(completo)
                         avviso.Show()
                     Catch exAvviso As Exception
                         log.ScriviLog("WGSchedaBrowser.MostraInizioDownload avviso: " & exAvviso.Message)
                         _contenitore.Annuncia(completo)
                     End Try
                 End Sub)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraInizioDownload: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub Scarica(url As String)
        Try
            If String.IsNullOrWhiteSpace(url) Then
                lettura.LeggiTesto("Indirizzo del file non disponibile")
                Return
            End If
            If CoreAttivo() Is Nothing Then Return
            _contenitore.RegistroDownload.SegnaEsplicito(url)
            If PuoScaricareConCollegamento(url) AndAlso _devTools IsNot Nothing AndAlso _devTools.ECollegato Then
                Dim esito As JToken = Await _devTools.ValutaAsync("", ScriptScaricaCollegamento(url), 3000)
                If esito IsNot Nothing AndAlso esito.Type = JTokenType.Boolean AndAlso esito.Value(Of Boolean)() Then Return
            End If
            Await ScaricaDirettamenteAsync(url)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Scarica: " & ex.Message)
        End Try
    End Sub

    Private Function PuoScaricareConCollegamento(url As String) As Boolean
        Try
            If url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) OrElse url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then Return True
            Dim origineFile As String = WGArchivioBrowser.OrigineDi(url)
            Return origineFile <> "" AndAlso String.Equals(origineFile, WGArchivioBrowser.OrigineDi(_indirizzo), StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.PuoScaricareConCollegamento: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ScriptScaricaCollegamento(url As String) As String
        Return "(function(u){try{var a=document.createElement('a');a.href=u;a.download='';a.rel='noopener';a.style.display='none';" &
               "(document.body||document.documentElement).appendChild(a);a.click();setTimeout(function(){try{a.remove();}catch(e){}},2000);return true;}catch(e){return false;}})(" &
               JsonConvert.SerializeObject(url) & ")"
    End Function

    Private Shared Function NomeFileDaIndirizzo(url As String) As String
        Try
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(If(url, ""), UriKind.Absolute, uri) Then Return ""
            Dim nome As String = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath))
            Return If(nome, "").Trim()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.NomeFileDaIndirizzo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function NomePdfDaIndirizzo(indirizzo As String) As String
        Try
            Dim nome As String = NomeFileDaIndirizzo(indirizzo)
            If String.IsNullOrWhiteSpace(nome) Then Return "documento.pdf"
            If Not nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then nome &= ".pdf"
            Return WGBrowserGestoreDownload.NomeFileSicuro(nome)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.NomePdfDaIndirizzo: " & ex.Message)
            Return "documento.pdf"
        End Try
    End Function

    Friend Sub PdfDaAprireNelLettore(indirizzo As String)
        Try
            If _chiusa OrElse String.IsNullOrEmpty(indirizzo) Then Return
            If String.Equals(indirizzo, _ultimoPdfIntercettato, StringComparison.OrdinalIgnoreCase) AndAlso (DateTime.Now - _istanteUltimoPdf).TotalSeconds < 5 Then Return
            _ultimoPdfIntercettato = indirizzo
            _istanteUltimoPdf = DateTime.Now
            DownloadInArrivo()
            Dim c As CoreWebView2 = Core
            If c IsNot Nothing Then
                Try
                    WGTracciaMotore.Scrivi("ferma per pdf")
                    c.Stop()
                Catch exFerma As Exception
                    log.ScriviLog("WGSchedaBrowser.PdfDaAprireNelLettore ferma: " & exFerma.Message)
                End Try
            End If
            Dim compito As Task = ApriPdfDaIndirizzoAsync(indirizzo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.PdfDaAprireNelLettore: " & ex.Message)
        End Try
    End Sub

    Private Async Function ApriPdfDaIndirizzoAsync(indirizzo As String) As Task
        Try
            Await ScaricaDirettamenteAsync(indirizzo, NomePdfDaIndirizzo(indirizzo), "application/pdf")
            Await Task.Delay(300)
            If _chiusa Then Return
            Dim c As CoreWebView2 = Core
            If c Is Nothing Then Return
            Dim sorgente As String = If(c.Source, "")
            If String.Equals(SenzaAncora(sorgente), SenzaAncora(indirizzo), StringComparison.OrdinalIgnoreCase) Then
                If c.CanGoBack Then
                    WGTracciaMotore.Scrivi("indietro dopo pdf")
                    c.GoBack()
                ElseIf _creataDallaPagina Then
                    _contenitore.ChiudiScheda(Me, True)
                End If
            ElseIf _creataDallaPagina AndAlso (sorgente = "" OrElse sorgente.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) Then
                _contenitore.ChiudiScheda(Me, True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ApriPdfDaIndirizzoAsync: " & ex.Message)
        End Try
    End Function

    Private Async Function ScaricaDirettamenteAsync(url As String, Optional nomeImposto As String = "", Optional tipoMime As String = "") As Task
        Try
            If Not (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Then
                lettura.LeggiTesto("Questo file non si può scaricare direttamente")
                Return
            End If
            Dim intestazioni As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            intestazioni("User-Agent") = WGIdentitaBrowser.UserAgent(_identitaCorrente)
            intestazioni("Accept-Language") = WGIdentitaBrowser.LingueAccettate()
            If _indirizzo.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then intestazioni("Referer") = _indirizzo
            Dim core As CoreWebView2 = CoreAttivo()
            If core IsNot Nothing Then
                Try
                    Dim cookies As List(Of CoreWebView2Cookie) = Await core.CookieManager.GetCookiesAsync(url)
                    If cookies IsNot Nothing AndAlso cookies.Count > 0 Then
                        Dim parti As New List(Of String)()
                        For Each cookie As CoreWebView2Cookie In cookies
                            If cookie Is Nothing OrElse String.IsNullOrEmpty(cookie.Name) Then Continue For
                            parti.Add(cookie.Name & "=" & If(cookie.Value, ""))
                        Next
                        If parti.Count > 0 Then intestazioni("Cookie") = String.Join("; ", parti)
                    End If
                Catch exCookie As Exception
                    log.ScriviLog("WGSchedaBrowser.ScaricaDirettamenteAsync cookie: " & exCookie.Message)
                End Try
            End If
            Dim nomeDaIndirizzo As String = If(String.IsNullOrWhiteSpace(nomeImposto), NomeFileDaIndirizzo(url), nomeImposto)
            Dim nome As String = WGBrowserGestoreDownload.NomeFileSicuro(If(nomeDaIndirizzo = "", "file scaricato", nomeDaIndirizzo))
            Dim nomeProvvisorio As Boolean = nomeDaIndirizzo = "" OrElse Path.GetExtension(nome) = ""
            Dim cartella As String = WGBrowserGestoreDownload.CartellaScaricamenti()
            If WGImpostazioniBrowser.ChiediDoveSalvare Then
                Await Task.Yield()
                Dim risposta As New RispostaUnica()
                Dim salvataggio As New WGSceltaSalvataggio("Nome con cui salvare il file scaricato", nome)
                AddHandler salvataggio.Confermato, Sub(percorso As String)
                                                       If Not risposta.Prendi() Then Return
                                                       AvviaScaricamentoDiretto(url, percorso, intestazioni, False, tipoMime)
                                                   End Sub
                AddHandler salvataggio.Annullato, Sub(s As Object, ev As EventArgs)
                                                      If Not risposta.Prendi() Then Return
                                                      lettura.LeggiTesto("Scaricamento annullato")
                                                  End Sub
                salvataggio.Avvia()
                Return
            End If
            AvviaScaricamentoDiretto(url, WGBrowserGestoreDownload.PercorsoLibero(cartella, nome), intestazioni, nomeProvvisorio, tipoMime)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScaricaDirettamenteAsync: " & ex.Message)
        End Try
    End Function

    Private Sub AvviaScaricamentoDiretto(url As String, percorso As String, intestazioni As Dictionary(Of String, String), accettaNomeServer As Boolean, Optional tipoMime As String = "")
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            Dim elemento As New ElementoDownload() With {
                .Id = WGRegistroDownload.NuovoId(),
                .Indirizzo = url,
                .IndirizzoOriginale = url,
                .NomeProposto = If(accettaNomeServer, "", Path.GetFileName(percorso)),
                .PercorsoCompleto = percorso,
                .TipoMime = If(tipoMime, "")
            }
            Dim registro As WGRegistroDownload = _contenitore.RegistroDownload
            Dim diretto As New WGDownloadDiretto(elemento, intestazioni, AddressOf registro.Notifica, accettaNomeServer)
            registro.Registra(elemento, percorso, Nothing, diretto)
            _istanteUltimoDownload = DateTime.Now
            MostraInizioDownload("Scaricamento di " & Path.GetFileName(percorso) & " avviato")
            diretto.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AvviaScaricamentoDiretto: " & ex.Message)
        End Try
    End Sub

    Private Sub CopiaNegliAppunti(testo As String, conferma As String)
        Try
            If String.IsNullOrEmpty(testo) Then
                lettura.LeggiTesto("Niente da copiare")
                Return
            End If
            Clipboard.SetText(testo)
            lettura.LeggiTesto(conferma)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CopiaNegliAppunti: " & ex.Message)
            lettura.LeggiTesto("Impossibile copiare negli appunti")
        End Try
    End Sub

    Friend Sub CredenzialiInviate(origine As String, utente As String, password As String)
        SuUI(Sub()
                 Try
                     If _privata OrElse Not WGImpostazioniBrowser.ProponiSalvataggioPassword OrElse String.IsNullOrEmpty(password) OrElse String.IsNullOrWhiteSpace(origine) Then Return
                     Dim archivioSiti As WGArchivioBrowser = Archivio
                     If archivioSiti Is Nothing Then Return
                     Dim host As String = WGArchivioBrowser.HostDi(origine)
                     If archivioSiti.ValoreSito(host, "password_mai") = "1" Then Return
                     Dim esistente As CredenzialeBrowser = archivioSiti.CredenzialiPer(origine).FirstOrDefault(Function(c) String.Equals(c.Utente, If(utente, "").Trim(), StringComparison.Ordinal))
                     If esistente IsNot Nothing AndAlso esistente.Password = password Then
                         archivioSiti.SegnaUsoCredenziale(esistente.Chiave)
                         Return
                     End If
                     Dim menu As New WGMenu()
                     menu.aggiungi(If(esistente IsNot Nothing, "Aggiorna la password salvata", "Salva la password"), Sub(s, e)
                                                                                                                         If archivioSiti.SalvaCredenziali(origine, utente, password) Then
                                                                                                                             lettura.LeggiTesto("Password salvata, Control Shift P per inserirla la prossima volta")
                                                                                                                         Else
                                                                                                                             lettura.LeggiTesto("Impossibile salvare la password")
                                                                                                                         End If
                                                                                                                     End Sub)
                     menu.aggiungi("Non ora", Sub(s, e) lettura.LeggiTesto("Password non salvata"))
                     menu.aggiungi("Mai per questo sito", Sub(s, e)
                                                              archivioSiti.ImpostaValoreSito(host, "password_mai", "1")
                                                              MessaggioBrowser("Il browser non proporrà più di salvare le password di " & host)
                                                          End Sub)
                     menu.avvia(If(esistente IsNot Nothing, "Aggiornare la password di " & utente & " per " & host & "?", "Salvare la password" & If(String.IsNullOrWhiteSpace(utente), "", " di " & utente) & " per " & host & "?"), True)
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.CredenzialiInviate: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Function CredenzialiDisponibili(origine As String) As List(Of CredenzialeBrowser)
        Dim risultato As New List(Of CredenzialeBrowser)()
        Try
            Dim archivioSiti As WGArchivioBrowser = Archivio
            If archivioSiti Is Nothing OrElse String.IsNullOrWhiteSpace(origine) Then Return risultato
            risultato.AddRange(archivioSiti.CredenzialiPer(origine))
            If risultato.Count = 0 Then
                Dim alternativa As String = ""
                If origine.Contains("://www.") Then
                    alternativa = origine.Replace("://www.", "://")
                ElseIf origine.Contains("://") Then
                    alternativa = origine.Replace("://", "://www.")
                End If
                If alternativa <> "" Then risultato.AddRange(archivioSiti.CredenzialiPer(alternativa))
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CredenzialiDisponibili: " & ex.Message)
        End Try
        Return risultato
    End Function

    Friend Sub CampoLoginRaggiunto(origine As String)
        SuUI(Sub()
                 Try
                     If _passwordAnnunciataPer = origine Then Return
                     Dim elenco As List(Of CredenzialeBrowser) = CredenzialiDisponibili(origine)
                     If elenco.Count = 0 Then elenco = CredenzialiDisponibili(WGArchivioBrowser.OrigineDi(_indirizzo))
                     If elenco.Count = 0 Then Return
                     _passwordAnnunciataPer = origine
                     Annuncia("Password salvata disponibile" & If(elenco.Count > 1, ", " & elenco.Count & " utenti", " per " & elenco(0).Utente) & ". Control Shift P per inserirla")
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.CampoLoginRaggiunto: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Async Function EseguiNelFrameAttivoAsync(script As String) As Task(Of JToken)
        Try
            If _devTools IsNot Nothing AndAlso _devTools.ECollegato Then
                Dim sessione As String = (Await _devTools.ElementoAttivoAsync()).Item1
                Dim valore As JToken = Await _devTools.ValutaAsync(sessione, script, 4000)
                If valore IsNot Nothing Then Return valore
                If sessione <> "" Then Return Await _devTools.ValutaAsync("", script, 4000)
                Return Nothing
            End If
            Dim testo As String = Await _cdp.ValutaAsync(script, 4000)
            If testo Is Nothing Then Return Nothing
            Return JToken.FromObject(testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EseguiNelFrameAttivoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function NumeroDa(valore As JToken) As Integer
        Try
            If valore Is Nothing Then Return 0
            Dim numero As Integer
            If Integer.TryParse(valore.ToString(), numero) Then Return numero
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.NumeroDa: " & ex.Message)
        End Try
        Return 0
    End Function

    Friend Sub InserisciPassword()
        Try
            Dim elenco As List(Of CredenzialeBrowser) = CredenzialiDisponibili(WGArchivioBrowser.OrigineDi(_indirizzo))
            If elenco.Count = 0 Then
                lettura.LeggiTesto("Nessuna password salvata per " & WGArchivioBrowser.HostDi(_indirizzo))
                Return
            End If
            If elenco.Count = 1 Then
                CompilaCredenziali(elenco(0))
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Password salvate per " & WGArchivioBrowser.HostDi(_indirizzo) & ", scegli l'utente da inserire")
            Dim mappa As New Dictionary(Of String, CredenzialeBrowser)()
            For i As Integer = 0 To elenco.Count - 1
                Dim id As String = "c" & i.ToString()
                mappa(id) = elenco(i)
                lista.aggiungiElemento(If(String.IsNullOrWhiteSpace(elenco(i).Utente), "Utente senza nome", elenco(i).Utente), id)
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          lista.abbandona()
                                                          Dim scelta As CredenzialeBrowser = Nothing
                                                          If id IsNot Nothing AndAlso mappa.TryGetValue(id, scelta) Then CompilaCredenziali(scelta)
                                                      Catch exScelta As Exception
                                                          log.ScriviLog("WGSchedaBrowser.InserisciPassword scelta: " & exScelta.Message)
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                             Catch exEsc As Exception
                                                 log.ScriviLog("WGSchedaBrowser.InserisciPassword esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InserisciPassword: " & ex.Message)
        End Try
    End Sub

    Private Async Sub CompilaCredenziali(credenziale As CredenzialeBrowser)
        Try
            If credenziale Is Nothing Then Return
            ' Si compila solo se il documento attivo è dello stesso sito della pagina: mai dentro una cornice di un altro sito (per esempio una pubblicità).
            Dim hostPagina As String = WGArchivioBrowser.HostDi(_indirizzo).ToLowerInvariant()
            If hostPagina.StartsWith("www.") Then hostPagina = hostPagina.Substring(4)
            Dim script As String = "(location.hostname.toLowerCase().replace(/^www\./, '') === " & JsonConvert.SerializeObject(hostPagina) & " && window.__wgAgente) ? window.__wgAgente.compilaCredenziali(" & JsonConvert.SerializeObject(credenziale.Utente) & "," & JsonConvert.SerializeObject(credenziale.Password) & ") : 0"
            Dim compilati As Integer = NumeroDa(Await EseguiNelFrameAttivoAsync(script))
            If compilati > 0 Then
                If Archivio IsNot Nothing Then Archivio.SegnaUsoCredenziale(credenziale.Chiave)
                lettura.LeggiTesto(If(compilati = 1, "Compilato un campo", "Compilati " & compilati & " campi") & " con i dati di " & credenziale.Utente)
                If inCampoTesto Then ProgrammaSincronizzazioneCampo()
                If _cursore IsNot Nothing Then _cursore.BufferSporco = True
            Else
                lettura.LeggiTesto("Nessun campo di accesso trovato nella pagina")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CompilaCredenziali: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub CompilaModulo()
        Try
            Dim archivioSiti As WGArchivioBrowser = Archivio
            Dim dati As Dictionary(Of String, String) = If(archivioSiti IsNot Nothing, archivioSiti.DatiPersonali(), New Dictionary(Of String, String)())
            If dati Is Nothing OrElse dati.Values.All(Function(v) String.IsNullOrWhiteSpace(v)) Then
                lettura.LeggiTesto("Nessun dato personale salvato. Si inseriscono dal menu Gestione del browser, Dati per la compilazione dei moduli")
                Return
            End If
            Dim oggetto As New JObject()
            For Each coppia As KeyValuePair(Of String, String) In dati
                If Not String.IsNullOrWhiteSpace(coppia.Value) Then oggetto(coppia.Key) = coppia.Value
            Next
            Dim script As String = "window.__wgAgente ? window.__wgAgente.compilaModulo(" & oggetto.ToString(Formatting.None) & ") : 0"
            Dim compilati As Integer = NumeroDa(Await EseguiNelFrameAttivoAsync(script))
            If compilati > 0 Then
                lettura.LeggiTesto(If(compilati = 1, "Compilato un campo", "Compilati " & compilati & " campi") & " con i dati personali. Controllarli prima di inviare il modulo")
                If _cursore IsNot Nothing Then _cursore.BufferSporco = True
                If inCampoTesto Then ProgrammaSincronizzazioneCampo()
            Else
                lettura.LeggiTesto("Nessun campo vuoto riconosciuto nel modulo")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CompilaModulo: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaZoom(percento As Integer, salva As Boolean, annunciare As Boolean)
        Try
            If _pagina Is Nothing OrElse Not _pagina.Pronta Then Return
            _pagina.Zoom = percento / 100.0
            _zoomPercento = percento
            If salva AndAlso Not _privata AndAlso Archivio IsNot Nothing Then Archivio.ImpostaValoreSito(_hostPagina, "zoom", If(percento = 100, "", percento.ToString()))
            If annunciare Then Annuncia("Zoom " & percento & " percento")
            If _cursore IsNot Nothing Then _cursore.BufferSporco = True
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ImpostaZoom: " & ex.Message)
        End Try
    End Sub

    Friend Sub CambiaZoom(direzione As Integer)
        Try
            If direzione = 0 Then
                ImpostaZoom(100, True, True)
                Return
            End If
            Dim nuovo As Integer = _zoomPercento
            If direzione > 0 Then
                Dim maggiori As Integer() = LivelliZoom.Where(Function(l) l > _zoomPercento).ToArray()
                If maggiori.Length > 0 Then nuovo = maggiori.First()
            Else
                Dim minori As Integer() = LivelliZoom.Where(Function(l) l < _zoomPercento).ToArray()
                If minori.Length > 0 Then nuovo = minori.Last()
            End If
            If nuovo = _zoomPercento Then
                Annuncia(If(direzione > 0, "Zoom massimo", "Zoom minimo"))
                Return
            End If
            ImpostaZoom(nuovo, True, True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CambiaZoom: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaZoomSito()
        Try
            If Archivio Is Nothing Then Return
            Dim valore As String = Archivio.ValoreSito(_hostPagina, "zoom")
            Dim percento As Integer
            If Integer.TryParse(valore, percento) AndAlso percento >= 25 AndAlso percento <= 500 Then
                If percento <> _zoomPercento Then ImpostaZoom(percento, False, False)
            ElseIf _zoomPercento <> 100 Then
                ImpostaZoom(100, False, False)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ApplicaZoomSito: " & ex.Message)
        End Try
    End Sub

    Friend Sub CercaNellaPagina()
        Try
            _campoRicerca = New WGCampoTesto()
            _campoRicerca.init("Testo da cercare nella pagina", ultimaRicerca)
            AddHandler _campoRicerca.chiusura, AddressOf RicercaConfermata
            _campoRicerca.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CercaNellaPagina: " & ex.Message)
        End Try
    End Sub

    Private Async Sub RicercaConfermata(sender As Object, e As EventArgs)
        Try
            Dim campo As WGCampoTesto = _campoRicerca
            _campoRicerca = Nothing
            If campo Is Nothing Then Return
            Dim testo As String = campo.OttieniTesto()
            campo.chiudi()
            If String.IsNullOrWhiteSpace(testo) Then Return
            ultimaRicerca = testo.Trim()
            Await CercaDiNuovoAsync(False)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RicercaConfermata: " & ex.Message)
        End Try
    End Sub

    Friend Async Function CercaDiNuovoAsync(indietro As Boolean) As Task
        Try
            If String.IsNullOrWhiteSpace(ultimaRicerca) Then
                CercaNellaPagina()
                Return
            End If
            Await _cursore.AssicuraBufferAsync()
            Dim trovato As Tuple(Of Integer, Integer) = _cursore.CercaOccorrenza(ultimaRicerca, indietro)
            If trovato Is Nothing Then
                lettura.LeggiTesto("Testo non trovato: " & ultimaRicerca)
            Else
                MostraRisultatoRicerca(trovato.Item1, trovato.Item2)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CercaDiNuovoAsync: " & ex.Message)
        End Try
    End Function

    Friend Sub LeggiInformazioniPagina()
        Try
            Dim testo As New StringBuilder()
            testo.Append("Titolo: ").Append(If(String.IsNullOrWhiteSpace(_titolo), "nessuno", _titolo)).Append(". ")
            testo.Append("Indirizzo: ").Append(_indirizzo).Append(". ")
            testo.Append("Il browser si presenta come ").Append(WGImpostazioniBrowser.NomeIdentita(_identitaCorrente)).Append(". ")
            testo.Append("Motore: Microsoft Edge WebView2 ").Append(WGMotoreBrowser.VersioneMotore()).Append(". ")
            If _risorseBloccate > 0 Then testo.Append("Elementi pubblicitari bloccati: ").Append(_risorseBloccate).Append(". ")
            If _zoomPercento <> 100 Then testo.Append("Zoom: ").Append(_zoomPercento).Append(" percento. ")
            If _cursore IsNot Nothing Then
                testo.Append("Righe nel buffer: ").Append(_cursore.Righe.Count).Append(". ")
                If _cursore.LinguaPagina <> "" Then testo.Append("Lingua: ").Append(WGLayoutPagina.NomeLingua(_cursore.LinguaPagina)).Append(". ")
            End If
            If modalitaDiretta Then testo.Append("Controllo completo attivo.")
            Dim info As New WGMessaggi()
            info.Messaggio(testo.ToString().Trim())
            info.Show()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LeggiInformazioniPagina: " & ex.Message)
        End Try
    End Sub

    Private Async Function CreaPdfAsync() As Task(Of Byte())
        Try
            Dim core As CoreWebView2 = CoreAttivo()
            If core IsNot Nothing Then
                Try
                    Dim impostazioniPdf As CoreWebView2PrintSettings = core.Environment.CreatePrintSettings()
                    impostazioniPdf.ShouldPrintBackgrounds = True
                    impostazioniPdf.ShouldPrintHeaderAndFooter = False
                    Using flusso As Stream = Await core.PrintToPdfStreamAsync(impostazioniPdf)
                        If flusso IsNot Nothing Then
                            Using memoria As New MemoryStream()
                                Await flusso.CopyToAsync(memoria)
                                If memoria.Length > 0 Then Return memoria.ToArray()
                            End Using
                        End If
                    End Using
                Catch exNativo As Exception
                    log.ScriviLog("WGSchedaBrowser.CreaPdfAsync motore: " & exNativo.Message)
                End Try
            End If
            If _devTools Is Nothing OrElse Not _devTools.ECollegato Then Return Nothing
            Dim parametri As New JObject()
            parametri("printBackground") = True
            parametri("preferCSSPageSize") = True
            Dim risultato As JObject = Await _devTools.InviaAsync("Page.printToPDF", parametri, "", 60000)
            If risultato Is Nothing Then Return Nothing
            Dim dati As String = risultato.Value(Of String)("data")
            If String.IsNullOrEmpty(dati) Then Return Nothing
            Return Convert.FromBase64String(dati)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CreaPdfAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Friend Async Sub Stampa()
        Dim percorso As String = ""
        Try
            Dim impostazioniComuni As New WGImpostazioniComuni()
            Dim nomeStampante As String = impostazioniComuni.NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampante) Then
                MostraErrore("Nessuna stampante impostata. Sceglierla dalle impostazioni di WinGabriel.")
                Return
            End If
            lettura.LeggiTesto("Preparazione della stampa")
            Dim impostazioniStampa As Global.WGStampante.WGImpostazioniStampa = impostazioniComuni.CostruisciImpostazioniStampa()
            Dim titoloDocumento As String = Titolo
            Dim riuscita As Boolean = False
            Dim messaggio As String = ""
            Dim pdf As Byte() = Nothing
            If Global.WGStampante.WGStampante.PdfiumDisponibile() Then pdf = Await CreaPdfAsync()
            If pdf IsNot Nothing Then
                percorso = FileTemporaneo(".pdf")
                File.WriteAllBytes(percorso, pdf)
                Dim percorsoPdf As String = percorso
                Dim esito As Global.WGStampante.WGEsitoStampa = Await Task.Run(Function() Global.WGStampante.WGStampante.StampaPdf(percorsoPdf, nomeStampante, titoloDocumento, impostazioniStampa, True))
                riuscita = esito IsNot Nothing AndAlso esito.Riuscita
                If esito IsNot Nothing Then messaggio = If(riuscita, esito.StatoFinale, esito.Errore)
            Else
                Dim blocco As New Global.WGStampante.WGBloccoStampa()
                blocco.Tipo = "testo"
                blocco.Testo = _cursore.TestoSemplice()
                Dim elenco As New List(Of Global.WGStampante.WGBloccoStampa) From {blocco}
                Dim esito As Global.WGStampante.WGEsitoStampa = Await Task.Run(Function() Global.WGStampante.WGStampante.StampaBlocchi(elenco, nomeStampante, titoloDocumento, impostazioniStampa, True))
                riuscita = esito IsNot Nothing AndAlso esito.Riuscita
                If esito IsNot Nothing Then messaggio = If(riuscita, esito.StatoFinale, esito.Errore)
            End If
            Dim avviso As New WGMessaggi()
            If riuscita Then
                avviso.Messaggio(If(String.IsNullOrWhiteSpace(messaggio), "Stampa avviata.", messaggio & "."))
            Else
                avviso.Messaggio(If(String.IsNullOrWhiteSpace(messaggio), "La stampa non è riuscita.", messaggio))
            End If
            avviso.Show()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.Stampa: " & ex.ToString())
            MostraErrore("La stampa non è riuscita: " & ex.Message)
        Finally
            Dim daCancellare As String = percorso
            If daCancellare <> "" Then
                Dim pulizia As Task = Task.Delay(120000).ContinueWith(Sub(t) CancellaFile(daCancellare))
            End If
        End Try
    End Sub

    Friend Sub StampaRichiestaDallaPagina(origine As String)
        SuUI(Sub()
                 Try
                     If _chiusa OrElse (DateTime.Now - _istanteUltimaStampaRichiesta).TotalSeconds < 3 Then Return
                     _istanteUltimaStampaRichiesta = DateTime.Now
                     Dim domanda As New WGDomanda()
                     AddHandler domanda.rispostadomanda, Sub(esito As DialogResult)
                                                             Try
                                                                 If esito = DialogResult.Yes Then Stampa()
                                                             Catch exRisposta As Exception
                                                                 log.ScriviLog("WGSchedaBrowser.StampaRichiestaDallaPagina risposta: " & exRisposta.Message)
                                                             End Try
                                                         End Sub
                     domanda.avvia(If(_attiva, "", "La scheda " & Titolo & ". ") & "La pagina chiede di essere stampata. Stamparla con la stampante di WinGabriel?")
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.StampaRichiestaDallaPagina: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Friend Sub FunzioneNonDisponibile(funzione As String)
        SuUI(Sub()
                 Try
                     If _chiusa OrElse Not PrimaVolta("funzione|" & _hostPagina & "|" & If(funzione, "")) Then Return
                     Select Case funzione
                         Case "immagineNellImmagine"
                             AnnunciaSempre("La pagina voleva aprire una finestra immagine nell'immagine: tutto resta in questa scheda")
                         Case "condivisione"
                             AnnunciaSempre("La pagina voleva condividere un contenuto con le altre app di Windows: la funzione non è disponibile in WinGabriel. Si può copiare l'indirizzo della pagina dal menu Alt, Strumenti della pagina")
                         Case "pagamento"
                             AnnunciaSempre("La pagina voleva usare il pagamento del browser, che non è disponibile in WinGabriel: scegliere un altro metodo di pagamento offerto dal sito")
                         Case "contagocce"
                             AnnunciaSempre("La pagina voleva far scegliere un colore dallo schermo, funzione non disponibile in WinGabriel")
                         Case Else
                             AnnunciaSempre("La pagina ha chiesto una funzione del sistema che non è disponibile in WinGabriel")
                     End Select
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.FunzioneNonDisponibile: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    Private Function NomeFileDaTitolo(estensione As String) As String
        Try
            Dim nome As String = WGBrowserGestoreDownload.NomeFileSicuro(Titolo)
            If nome.Length > 80 Then nome = nome.Substring(0, 80).Trim()
            Return nome & estensione
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.NomeFileDaTitolo: " & ex.Message)
            Return "pagina" & estensione
        End Try
    End Function

    Friend Sub SalvaPagina()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Salva come documento PDF", Sub(s, e) SalvaComeFile(".pdf"))
            menu.aggiungi("Salva come testo", Sub(s, e) SalvaComeFile(".txt"))
            menu.avvia("Salva la pagina", True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SalvaPagina: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaComeFile(estensione As String)
        Try
            Dim salvataggio As New WGSceltaSalvataggio("Nome del file in cui salvare la pagina", NomeFileDaTitolo(estensione), estensione)
            AddHandler salvataggio.Confermato, Async Sub(percorso As String)
                                                   Try
                                                       If String.IsNullOrWhiteSpace(percorso) Then Return
                                                       If estensione = ".pdf" Then
                                                           Dim pdf As Byte() = Await CreaPdfAsync()
                                                           If pdf Is Nothing Then
                                                               MostraErrore("Impossibile creare il PDF della pagina")
                                                               Return
                                                           End If
                                                           File.WriteAllBytes(percorso, pdf)
                                                       Else
                                                           File.WriteAllText(percorso, _titolo & vbCrLf & _indirizzo & vbCrLf & vbCrLf & _cursore.TestoSemplice(), New UTF8Encoding(True))
                                                       End If
                                                       lettura.LeggiTesto("Pagina salvata: " & Path.GetFileName(percorso))
                                                   Catch exSalva As Exception
                                                       log.ScriviLog("WGSchedaBrowser.SalvaComeFile scrittura: " & exSalva.Message)
                                                       MostraErrore("Impossibile salvare la pagina: " & exSalva.Message)
                                                   End Try
                                               End Sub
            salvataggio.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SalvaComeFile: " & ex.Message)
        End Try
    End Sub

    Friend Sub CopiaRigaCorrente()
        Try
            If _cursore IsNot Nothing AndAlso _cursore.SelezioneAttiva Then
                CopiaNegliAppunti(_cursore.TestoSelezione(), "Selezione copiata negli appunti")
                Return
            End If
            Dim riga As RigaVirtuale = _cursore.RigaCorrente
            If riga Is Nothing Then
                lettura.LeggiTesto("Nessuna riga da copiare")
                Return
            End If
            Dim testo As String = If(String.IsNullOrWhiteSpace(riga.Testo), riga.TestoAnnuncio, riga.Testo)
            If Not String.IsNullOrWhiteSpace(riga.Valore) AndAlso Not riga.Password Then testo = (testo & " " & riga.Valore).Trim()
            CopiaNegliAppunti(testo, "Riga copiata")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CopiaRigaCorrente: " & ex.Message)
        End Try
    End Sub

    Friend Sub CopiaIndirizzo()
        CopiaNegliAppunti(_indirizzo, "Indirizzo copiato negli appunti")
    End Sub

    Friend Async Sub CopiaTestoPagina()
        Try
            Await _cursore.AssicuraBufferAsync()
            CopiaNegliAppunti(_cursore.TestoSemplice(), "Testo della pagina copiato negli appunti")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CopiaTestoPagina: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub LeggiDallInizio()
        Try
            Await _cursore.AssicuraBufferAsync()
            If _cursore.Righe.Count = 0 Then Return
            _cursore.SpostaA(0, False)
            _cursore.AvviaLetturaContinua()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LeggiDallInizio: " & ex.Message)
        End Try
    End Sub

    Friend Sub RiproduciConWinGabriel(sorgente As String)
        Try
            Dim indirizzoMedia As String = _indirizzo
            If Not String.IsNullOrWhiteSpace(sorgente) AndAlso (sorgente.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse sorgente.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) Then indirizzoMedia = sorgente
            If String.IsNullOrWhiteSpace(indirizzoMedia) OrElse Not indirizzoMedia.StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                lettura.LeggiTesto("Nessun contenuto da riprodurre")
                Return
            End If
            Dim idVideo As String = IdVideoYoutube(indirizzoMedia)
            If idVideo <> "" Then indirizzoMedia = "https://www.youtube.com/watch?v=" & idVideo
            _videoDallaPagina = ""
            AffidaAlLettore(indirizzoMedia, "Riproduzione con il lettore di WinGabriel")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiproduciConWinGabriel: " & ex.Message)
        End Try
    End Sub

    Private Sub AffidaAlLettore(indirizzoMedia As String, annuncio As String)
        Try
            FermaVideoDellaPagina()
            ' "Dalla pagina" non ha senso per chi arriva qui (YouTube e' gia' escluso a monte, e un formato non
            ' supportato dalla pagina non si riproduce): in quel caso si usa il lettore di WinGabriel a schermo.
            If WGImpostazioniBrowser.RiproduzioneVideo = WGImpostazioniBrowser.RiproduzioneSottofondo Then
                AffidaAlLettoreInSottofondo(indirizzoMedia, annuncio)
            Else
                AffidaAlPlayerInterno(indirizzoMedia, annuncio)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AffidaAlLettore: " & ex.ToString())
        End Try
    End Sub

    ' Sistema in sottofondo: il suono va avanti senza schermata e la pagina resta a fuoco.
    Private Sub AffidaAlLettoreInSottofondo(indirizzoMedia As String, annuncio As String)
        Try
            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro Is Nothing Then
                lettura.LeggiTesto("Il lettore di WinGabriel non è disponibile")
                Return
            End If
            If centro._inEsecuzioneAudio Then centro.TerminaAscoltoAudio()
            If Not String.IsNullOrWhiteSpace(annuncio) Then lettura.LeggiTesto(annuncio)
            centro.RiproduciFileAudio(New List(Of String) From {indirizzoMedia})
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AffidaAlLettoreInSottofondo: " & ex.ToString())
        End Try
    End Sub

    ' Riproduttore di WinGabriel a schermo (WGPlayerFormInt): comandi da tastiera, volume, velocita',
    ' scorrimento, capitoli. Alla chiusura il contenitore riporta in primo piano il browser.
    Private Sub AffidaAlPlayerInterno(indirizzoMedia As String, annuncio As String)
        SuUI(Sub()
                 Try
                     If Not String.IsNullOrWhiteSpace(annuncio) Then lettura.LeggiTesto(annuncio)
                     Dim lettore As New WGPlayerFormInt()
                     lettore.AggiungiElemento(indirizzoMedia)
                     AddHandler lettore.chiudiplayer, Sub()
                                                          Try
                                                              log.ScriviLog("WGSchedaBrowser: lettore di WinGabriel chiuso, ritorno alla pagina")
                                                          Catch
                                                          End Try
                                                      End Sub
                     lettore.avvia("Video")
                 Catch ex As Exception
                     log.ScriviLog("WGSchedaBrowser.AffidaAlPlayerInterno: " & ex.ToString())
                     MostraErrore("Impossibile aprire il lettore di WinGabriel: " & ex.Message)
                 End Try
             End Sub)
    End Sub

    ' Control Shift Y: riproduce il video con il lettore della pagina (senza passare dal lettore di WinGabriel).
    ' Per YouTube ricorda l'id del video, cosi' la pagina non lo rimanda al lettore ne' lo mette in pausa
    ' quando viene ricaricata.
    Private Const ScriptRiproduciDallaPagina As String =
        "(function(){try{window.__wgVideoAlLettore=false;var id='__ID__';" &
        "try{if(id)sessionStorage.setItem('__wgPaginaYt',id);else sessionStorage.removeItem('__wgPaginaYt');}catch(e){}" &
        "var el=document.querySelectorAll('video');var v=null;for(var i=0;i<el.length;i++){if(el[i].offsetWidth>0){v=el[i];break;}}" &
        "if(!v&&el.length)v=el[0];if(!v)return 'nessuno';v.muted=false;var p=v.play();if(p&&p.catch)p.catch(function(){});return 'ok';}catch(e){return 'errore';}})();"

    Private Const ScriptVideoInPausa As String =
        "(function(){try{var v=document.querySelector('video');return (v&&v.paused)?'pausa':'ok';}catch(e){return 'ok';}})();"

    Friend Async Sub RiproduciDallaPagina()
        Try
            If _cdp Is Nothing Then Return
            Dim idVideo As String = IdVideoYoutube(_indirizzo)
            Dim centro As WGCentroDiControllo = WGCentroDiControllo.GetInstance()
            If centro IsNot Nothing AndAlso centro._inEsecuzioneAudio Then centro.TerminaAscoltoAudio()
            _ultimoVideoAlLettore = ""
            _videoDallaPagina = idVideo
            Dim esito As String = Await _cdp.ValutaAsync(ScriptRiproduciDallaPagina.Replace("__ID__", idVideo), 3000)
            esito = If(esito, "").Trim(""""c)
            If esito = "nessuno" Then
                _videoDallaPagina = ""
                lettura.LeggiTesto("Nessun video da riprodurre in questa pagina")
                Return
            End If
            lettura.LeggiTesto("Riproduzione con il lettore della pagina")
            ' Il browser puo' rifiutare l'avvio automatico: in quel caso su YouTube si manda il tasto K, che
            ' per la pagina e' un vero input dell'utente.
            If idVideo <> "" Then
                Await Task.Delay(600)
                If _chiusa OrElse _cdp Is Nothing Then Return
                Dim stato As String = Await _cdp.ValutaAsync(ScriptVideoInPausa, 2000)
                If If(stato, "").Trim(""""c) = "pausa" Then _cdp.InviaTasto(75, AscW("k"c), ModificatoriInput.Nessuno)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiproduciDallaPagina: " & ex.ToString())
        End Try
    End Sub

    Private Const ScriptFermaVideo As String =
        "(function(){try{var ferma=function(v){try{v.muted=true;if(!v.paused)v.pause();}catch(e){}};" &
        "if(!window.__wgVideoAlLettore){window.__wgVideoAlLettore=true;document.addEventListener('play',function(e){var v=e.target;if(window.__wgVideoAlLettore&&v&&v.localName==='video'){ferma(v);}},true);}" &
        "window.__wgVideoAlLettore=true;var el=document.querySelectorAll('video');for(var i=0;i<el.length;i++){ferma(el[i]);}}catch(e){}})();"

    Private Const ScriptLiberaVideo As String =
        "(function(){try{window.__wgVideoAlLettore=false;}catch(e){}})();"

    Private Async Sub FermaVideoDellaPagina()
        Try
            If _cdp Is Nothing Then Return
            Await _cdp.ValutaAsync("try{sessionStorage.removeItem('__wgPaginaYt');}catch(e){}" & ScriptFermaVideo, 2000)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.FermaVideoDellaPagina: " & ex.Message)
        End Try
    End Sub

    Private Async Sub LiberaVideoDellaPagina()
        Try
            If _cdp Is Nothing Then Return
            Await _cdp.ValutaAsync(ScriptLiberaVideo, 2000)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LiberaVideoDellaPagina: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ParametroQuery(query As String, nome As String) As String
        Try
            For Each parte As String In If(query, "").TrimStart("?"c).Split("&"c)
                Dim uguale As Integer = parte.IndexOf("="c)
                If uguale <= 0 Then Continue For
                If String.Equals(parte.Substring(0, uguale), nome, StringComparison.Ordinal) Then Return Uri.UnescapeDataString(parte.Substring(uguale + 1).Replace("+", " "))
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.ParametroQuery: " & ex.Message)
        End Try
        Return ""
    End Function

    Friend Shared Function IdVideoYoutube(indirizzo As String) As String
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return ""
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(indirizzo.Trim(), UriKind.Absolute, uri) Then Return ""
            If uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps Then Return ""
            Dim host As String = uri.Host.ToLowerInvariant()
            Dim parti As String() = uri.AbsolutePath.Split(New Char() {"/"c}, StringSplitOptions.RemoveEmptyEntries)
            Dim id As String = ""
            If host = "youtu.be" OrElse host = "www.youtu.be" Then
                If parti.Length >= 1 Then id = parti(0)
            ElseIf host = "youtube.com" OrElse host.EndsWith(".youtube.com", StringComparison.Ordinal) Then
                If parti.Length = 1 AndAlso String.Equals(parti(0), "watch", StringComparison.OrdinalIgnoreCase) Then
                    id = ParametroQuery(uri.Query, "v")
                ElseIf parti.Length >= 2 AndAlso (String.Equals(parti(0), "shorts", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(parti(0), "live", StringComparison.OrdinalIgnoreCase)) Then
                    id = parti(1)
                End If
            End If
            If Not System.Text.RegularExpressions.Regex.IsMatch(If(id, ""), "^[A-Za-z0-9_-]{11}$") Then Return ""
            Return id
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGSchedaBrowser.IdVideoYoutube: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub ControllaVideoYoutube(indirizzo As String)
        Try
            Dim idVideo As String = IdVideoYoutube(indirizzo)
            If idVideo <> _videoDallaPagina Then _videoDallaPagina = ""
            If idVideo = "" OrElse WGImpostazioniBrowser.RiproduzioneVideo = WGImpostazioniBrowser.RiproduzionePagina Then
                If _ultimoVideoAlLettore <> "" Then
                    _ultimoVideoAlLettore = ""
                    LiberaVideoDellaPagina()
                End If
                Return
            End If
            ' Scelto con Control Shift Y: questo video resta nel lettore della pagina.
            If idVideo = _videoDallaPagina Then Return
            If idVideo = _ultimoVideoAlLettore Then
                FermaVideoDellaPagina()
                Return
            End If
            _ultimoVideoAlLettore = idVideo
            Dim testo As String
            If WGImpostazioniBrowser.RiproduzioneVideo = WGImpostazioniBrowser.RiproduzioneSottofondo Then
                testo = "Video di YouTube nel lettore di WinGabriel, senza pubblicità. La pagina resta aperta per leggere titolo, descrizione e commenti"
            Else
                testo = "Video di YouTube nel lettore di WinGabriel, senza pubblicità. Chiudendo il lettore torni alla pagina. Control Shift Y per usare invece il lettore della pagina"
            End If
            AffidaAlLettore("https://www.youtube.com/watch?v=" & idVideo, testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ControllaVideoYoutube: " & ex.ToString())
        End Try
    End Sub

    Friend Sub MostraElenchiElementi()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Intestazioni", Sub(s, e) MostraElenco("Intestazioni della pagina", Function(r) r.Ruolo = "heading", False, "livello"))
            menu.aggiungi("Link", Sub(s, e) MostraElenco("Link della pagina", AddressOf WGNavigatoreDOM.ELink, True, ""))
            menu.aggiungi("Pulsanti ed elementi cliccabili", Sub(s, e) MostraElenco("Pulsanti ed elementi cliccabili", AddressOf WGNavigatoreDOM.EPulsante, True, "tipo"))
            menu.aggiungi("Campi dei moduli", Sub(s, e) MostraElenco("Campi della pagina", Function(r) WGNavigatoreDOM.ECampoModulo(r) OrElse r.Ruolo = "button", False, "tipo"))
            menu.aggiungi("Regioni", Sub(s, e) MostraElenco("Regioni della pagina", Function(r) Not String.IsNullOrEmpty(r.Regione), False, "regione"))
            menu.aggiungi("Tabelle", Sub(s, e) MostraElenco("Tabelle della pagina", Function(r) Not String.IsNullOrEmpty(r.Tabella), False, "tabella"))
            menu.aggiungi("Immagini", Sub(s, e) MostraElenco("Immagini della pagina", Function(r) r.Ruolo = "img", False, "tipo"))
            menu.aggiungi("Video, audio e aree disegnate", Sub(s, e) MostraElenco("Contenuti multimediali", AddressOf WGNavigatoreDOM.EMultimediale, False, "tipo"))
            menu.aggiungi("Contenuti nuovi dall'ultimo aggiornamento", Sub(s, e) MostraElenco("Contenuti nuovi", Function(r) _cursore.UltimeRigheNuove.Contains(r), False, ""))
            menu.avvia("Elenchi degli elementi", True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraElenchiElementi: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub MostraElenco(titoloElenco As String, condizione As Func(Of RigaVirtuale, Boolean), distinguiAttivazione As Boolean, dettaglio As String)
        Try
            Await _cursore.AssicuraBufferAsync()
            Await Task.Yield()
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.impostaTitolo(titoloElenco)
            Dim trovati As Integer = 0
            Dim ultimoId As Integer = 0
            For i As Integer = 0 To _cursore.Righe.Count - 1
                Dim r As RigaVirtuale = _cursore.Righe(i)
                If Not condizione(r) Then Continue For
                If distinguiAttivazione AndAlso r.IdDaAttivare = ultimoId Then Continue For
                ultimoId = r.IdDaAttivare
                Dim voce As String = r.Testo
                Select Case dettaglio
                    Case "livello" : voce = r.Livello & " " & voce
                    Case "tipo"
                        If r.RuoloDaAttivare = "button" Then
                            Dim tipoMinuscolo As String = r.TipoElemento
                            If tipoMinuscolo.Length > 0 Then tipoMinuscolo = Char.ToLowerInvariant(tipoMinuscolo(0)) & tipoMinuscolo.Substring(1)
                            voce &= " " & tipoMinuscolo
                        Else
                            voce = r.TipoElemento & " " & voce
                        End If
                    Case "regione" : voce = r.Regione
                    Case "tabella" : voce = r.Tabella
                End Select
                If String.IsNullOrWhiteSpace(voce) Then Continue For
                lista.aggiungiElemento(voce, i.ToString())
                trovati += 1
            Next
            If trovati = 0 Then
                lettura.LeggiTesto("Nessun elemento trovato")
                Return
            End If
            _listaElementi = lista
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          Dim indice As Integer
                                                          lista.abbandona()
                                                          If ReferenceEquals(_listaElementi, lista) Then _listaElementi = Nothing
                                                          If Integer.TryParse(id, indice) Then SuUI(Sub() _cursore.SpostaA(indice, True))
                                                      Catch exScelta As Exception
                                                          log.ScriviLog("WGSchedaBrowser.MostraElenco scelta: " & exScelta.Message)
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs)
                                             Try
                                                 lista.abbandona()
                                                 If ReferenceEquals(_listaElementi, lista) Then _listaElementi = Nothing
                                             Catch exEsc As Exception
                                                 log.ScriviLog("WGSchedaBrowser.MostraElenco esc: " & exEsc.Message)
                                             End Try
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraElenco: " & ex.Message)
        End Try
    End Sub

    Private Async Function SchermataRigaAsync(riga As RigaVirtuale) As Task(Of String)
        Try
            Dim rettangolo As RectangleF? = Await _cdp.RettangoloRigaAsync(riga)
            If Not rettangolo.HasValue Then Return ""
            Dim dati As Byte() = Await _cdp.SchermataAsync(rettangolo)
            If dati Is Nothing OrElse dati.Length = 0 Then Return ""
            Dim percorso As String = FileTemporaneo(".png")
            File.WriteAllBytes(percorso, dati)
            Return percorso
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SchermataRigaAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function SchermataVistaAsync() As Task(Of String)
        Try
            Dim dati As Byte() = Await _cdp.SchermataAsync(Nothing)
            If dati Is Nothing OrElse dati.Length = 0 Then Return ""
            Dim percorso As String = FileTemporaneo(".png")
            File.WriteAllBytes(percorso, dati)
            Return percorso
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SchermataVistaAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function RiconosciTestoAsync(percorso As String) As Task(Of String)
        Try
            If String.IsNullOrEmpty(percorso) Then Return ""
            Dim testo As String = Await Task.Run(Function()
                                                     Try
                                                         Dim ocr As New Global.WGOcr.CLSOcr()
                                                         Return ocr.EstraiTestoDaImmagine(percorso)
                                                     Catch exOcr As Exception
                                                         log.ScriviLog("WGSchedaBrowser.RiconosciTestoAsync ocr: " & exOcr.Message)
                                                         Return ""
                                                     End Try
                                                 End Function)
            Return WGAlberoAccessibilita.Normalizza(testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiconosciTestoAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function DescriviImmagineAsync(percorso As String) As Task(Of String)
        Try
            If String.IsNullOrEmpty(percorso) Then Return ""
            Dim risultato As Tuple(Of String, String) = Await WGAi.DescriviImmagineAsync(percorso, "L'immagine è presa da una pagina web: descrivila in modo conciso e, se contiene testo, riportalo fedelmente.")
            If risultato Is Nothing Then Return ""
            If String.IsNullOrWhiteSpace(risultato.Item1) Then
                If Not String.IsNullOrWhiteSpace(risultato.Item2) Then lettura.LeggiTesto("Descrizione non riuscita: " & risultato.Item2)
                Return ""
            End If
            Return risultato.Item1.Trim()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviImmagineAsync: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function IntelligenzaArtificialeDisponibile() As Boolean
        Try
            If WGAi.AlmenoUnProviderConfigurato() Then Return True
            lettura.LeggiTesto("Per questa funzione serve una chiave dell'intelligenza artificiale nelle impostazioni di WinGabriel")
            Return False
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.IntelligenzaArtificialeDisponibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub AggiornaRigaDescritta(riga As RigaVirtuale, descrizione As String)
        Try
            If riga Is Nothing OrElse String.IsNullOrWhiteSpace(descrizione) Then Return
            If Not String.IsNullOrEmpty(riga.Sorgente) Then WGCursorVirtuale.DescrizioniImmagini(riga.Sorgente) = descrizione
            If riga.Ruolo = "img" OrElse riga.Ruolo = "canvas" Then
                riga.Testo = descrizione
                riga.SenzaNome = False
                RigaVirtuale.Completa(riga)
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.AggiornaRigaDescritta: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub RiconosciTestoRiga(riga As RigaVirtuale)
        Dim percorso As String = ""
        Try
            If riga Is Nothing Then Return
            lettura.LeggiTesto("Riconoscimento del testo in corso")
            percorso = Await SchermataRigaAsync(riga)
            If percorso = "" Then
                lettura.LeggiTesto("Impossibile catturare l'elemento")
                Return
            End If
            Dim testo As String = Await RiconosciTestoAsync(percorso)
            If testo = "" Then
                lettura.LeggiTesto("Nessun testo riconosciuto")
                Return
            End If
            If riga.SenzaNome OrElse riga.Ruolo = "canvas" Then AggiornaRigaDescritta(riga, testo)
            MostraRisultato("Testo riconosciuto", testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiconosciTestoRiga: " & ex.Message)
        Finally
            CancellaFile(percorso)
        End Try
    End Sub

    Friend Async Sub DescriviRiga(riga As RigaVirtuale)
        Dim percorso As String = ""
        Try
            If riga Is Nothing OrElse Not IntelligenzaArtificialeDisponibile() Then Return
            Dim memorizzata As String = Nothing
            If Not String.IsNullOrEmpty(riga.Sorgente) AndAlso WGCursorVirtuale.DescrizioniImmagini.TryGetValue(riga.Sorgente, memorizzata) Then
                MostraRisultato("Descrizione dell'immagine", memorizzata)
                Return
            End If
            lettura.LeggiTesto("Descrizione in corso, attendere")
            percorso = Await SchermataRigaAsync(riga)
            If percorso = "" Then
                lettura.LeggiTesto("Impossibile catturare l'elemento")
                Return
            End If
            Dim descrizione As String = Await DescriviImmagineAsync(percorso)
            If descrizione = "" Then Return
            AggiornaRigaDescritta(riga, descrizione)
            MostraRisultato("Descrizione dell'immagine", descrizione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviRiga: " & ex.Message)
        Finally
            CancellaFile(percorso)
        End Try
    End Sub

    Private Async Sub DescriviInPunto(x As Integer, y As Integer)
        Try
            If _devTools Is Nothing OrElse Not _devTools.ECollegato Then Return
            Dim zoom As Double = If(_pagina IsNot Nothing, _pagina.Zoom, 1.0)
            If zoom <= 0 Then zoom = 1.0
            Dim parametri As New JObject()
            parametri("x") = CInt(Math.Round(x / zoom))
            parametri("y") = CInt(Math.Round(y / zoom))
            parametri("includeUserAgentShadowDOM") = False
            Dim risultato As JObject = Await _devTools.InviaAsync("DOM.getNodeForLocation", parametri, "", 3000)
            If risultato Is Nothing Then Return
            Dim idNodo As Integer = risultato.Value(Of Integer?)("backendNodeId").GetValueOrDefault()
            If idNodo <= 0 Then Return
            Dim riga As New RigaVirtuale() With {.DaAlbero = True, .Sessione = "", .IdElemento = idNodo, .Ruolo = "img"}
            DescriviRiga(riga)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviInPunto: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub RiconosciTestoVista()
        Dim percorso As String = ""
        Try
            lettura.LeggiTesto("Riconoscimento del testo della parte visibile in corso")
            percorso = Await SchermataVistaAsync()
            If percorso = "" Then
                lettura.LeggiTesto("Impossibile catturare la pagina")
                Return
            End If
            Dim testo As String = Await RiconosciTestoAsync(percorso)
            If testo = "" Then
                lettura.LeggiTesto("Nessun testo riconosciuto")
                Return
            End If
            MostraRisultato("Testo riconosciuto nella pagina", testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiconosciTestoVista: " & ex.Message)
        Finally
            CancellaFile(percorso)
        End Try
    End Sub

    Friend Async Sub DescriviVista()
        Dim percorso As String = ""
        Try
            If Not IntelligenzaArtificialeDisponibile() Then Return
            lettura.LeggiTesto("Descrizione della parte visibile in corso, attendere")
            percorso = Await SchermataVistaAsync()
            If percorso = "" Then
                lettura.LeggiTesto("Impossibile catturare la pagina")
                Return
            End If
            Dim descrizione As String = Await DescriviImmagineAsync(percorso)
            If descrizione <> "" Then MostraRisultato("Descrizione della pagina", descrizione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DescriviVista: " & ex.Message)
        Finally
            CancellaFile(percorso)
        End Try
    End Sub

    Friend Async Sub ModalitaLettura()
        Try
            If _cdp Is Nothing Then Return
            lettura.LeggiTesto("Modalità lettura, preparazione dell'articolo")
            Dim html As String = Await _cdp.ValutaAsync("document.documentElement ? document.documentElement.outerHTML : ''", 10000)
            If String.IsNullOrWhiteSpace(html) Then
                lettura.LeggiTesto("Impossibile leggere il contenuto della pagina")
                Return
            End If
            Dim indirizzoPagina As String = _indirizzo
            Dim titoloPagina As String = Titolo
            Dim articolo As ArticoloEstratto = Await Task.Run(Function() WGLettoreArticoli.Estrai(indirizzoPagina, html))
            If _chiusa Then Return
            If articolo Is Nothing OrElse Not articolo.Valido Then
                If articolo IsNot Nothing AndAlso articolo.APagamento Then
                    lettura.LeggiTesto("L'articolo è riservato agli abbonati")
                Else
                    lettura.LeggiTesto("In questa pagina non è stato trovato un articolo da leggere")
                End If
                Return
            End If
            If String.IsNullOrWhiteSpace(articolo.Titolo) Then articolo.Titolo = titoloPagina
            MostraArticolo(articolo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ModalitaLettura: " & ex.ToString())
            lettura.LeggiTesto("Modalità lettura non riuscita")
        End Try
    End Sub

    Private Sub MostraArticolo(articolo As ArticoloEstratto)
        Try
            Dim campo As New WGCampo()
            campo.CaricaHtml(If(String.IsNullOrWhiteSpace(articolo.Titolo), "Articolo", articolo.Titolo), articolo.HtmlCompleto())
            campo.aggiungiopzione("Riassumi con l'intelligenza artificiale", Sub(s, e) RiassumiArticolo(articolo))
            campo.aggiungiopzione("Copia il testo dell'articolo", Sub(s, e) CopiaNegliAppunti((If(articolo.Titolo, "") & vbCrLf & articolo.Testo).Trim(), "Articolo copiato negli appunti"))
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraArticolo: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub RiassumiArticolo(articolo As ArticoloEstratto)
        Try
            If articolo Is Nothing OrElse Not IntelligenzaArtificialeDisponibile() Then Return
            Dim testo As String = If(articolo.Testo, "")
            If testo.Length > 60000 Then testo = testo.Substring(0, 60000)
            lettura.LeggiTesto("Riassunto in corso, attendere")
            Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiTestoAsync(testo, "Il testo è l'articolo " & If(articolo.Titolo, "") & If(String.IsNullOrWhiteSpace(articolo.Sito), "", " di " & articolo.Sito) & ". Rispondi in italiano.")
            If risultato Is Nothing OrElse String.IsNullOrWhiteSpace(risultato.Item1) Then
                lettura.LeggiTesto("Riassunto non riuscito" & If(risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2), ": " & risultato.Item2, ""))
                Return
            End If
            MostraRisultato("Riassunto di " & If(articolo.Titolo, "articolo"), risultato.Item1)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiassumiArticolo: " & ex.ToString())
        End Try
    End Sub

    Friend Async Sub RiassumiPagina()
        Try
            If Not IntelligenzaArtificialeDisponibile() Then Return
            Await _cursore.AssicuraBufferAsync()
            Dim testo As String = _cursore.TestoSemplice()
            If String.IsNullOrWhiteSpace(testo) Then
                lettura.LeggiTesto("La pagina non contiene testo da riassumere")
                Return
            End If
            If testo.Length > 60000 Then testo = testo.Substring(0, 60000)
            lettura.LeggiTesto("Riassunto in corso, attendere")
            Dim risultato As Tuple(Of String, String) = Await WGAi.RiassumiTestoAsync(testo, "Il testo proviene dalla pagina web " & Titolo & ". Rispondi in italiano.")
            If risultato Is Nothing OrElse String.IsNullOrWhiteSpace(risultato.Item1) Then
                lettura.LeggiTesto("Riassunto non riuscito" & If(risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2), ": " & risultato.Item2, ""))
                Return
            End If
            MostraRisultato("Riassunto di " & Titolo, risultato.Item1)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiassumiPagina: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub TraduciPagina()
        Try
            If Not IntelligenzaArtificialeDisponibile() Then Return
            Await _cursore.AssicuraBufferAsync()
            Dim testo As String = _cursore.TestoSemplice()
            If String.IsNullOrWhiteSpace(testo) Then
                lettura.LeggiTesto("La pagina non contiene testo da tradurre")
                Return
            End If
            Dim blocchi As List(Of String) = WGAi.DividiInBlocchi(testo, 1200)
            If blocchi Is Nothing OrElse blocchi.Count = 0 Then blocchi = New List(Of String) From {testo}
            Dim totale As Integer = Math.Min(blocchi.Count, 8)
            Dim traduzione As New StringBuilder()
            For i As Integer = 0 To totale - 1
                lettura.LeggiTesto(If(totale = 1, "Traduzione in corso, attendere", "Traduzione, parte " & (i + 1) & " di " & totale))
                Dim risultato As Tuple(Of String, String) = Await WGAi.OttieniRispostaAsync("Traduci in italiano il testo seguente, tratto da una pagina web. Mantieni la divisione in righe e rispondi solo con la traduzione." & vbLf & vbLf & blocchi(i))
                If risultato Is Nothing OrElse String.IsNullOrWhiteSpace(risultato.Item1) Then
                    If traduzione.Length = 0 Then
                        lettura.LeggiTesto("Traduzione non riuscita" & If(risultato IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risultato.Item2), ": " & risultato.Item2, ""))
                        Return
                    End If
                    Exit For
                End If
                traduzione.AppendLine(risultato.Item1.Trim())
            Next
            If blocchi.Count > totale Then traduzione.AppendLine().AppendLine("La pagina continua: sono state tradotte le prime " & totale & " parti.")
            MostraRisultato("Traduzione di " & Titolo, traduzione.ToString())
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TraduciPagina: " & ex.Message)
        End Try
    End Sub

    Private Async Sub PassaSopraRiga(riga As RigaVirtuale)
        Try
            _istanteUltimoPassaggio = DateTime.Now
            _ultimoSuggerimento = ""
            Dim punto As PointF? = Await _cdp.PassaSopraAsync(riga)
            If Not punto.HasValue Then
                lettura.LeggiTesto("Impossibile raggiungere l'elemento con il mouse")
                Return
            End If
            Await _cdp.AttendiInputAsync()
            Await AttendiStabilitaAsync(1500)
            If _chiusa Then Return
            Dim suggerimento As String = Await _cdp.SuggerimentoAlPuntoAsync(punto.Value.X, punto.Value.Y)
            If Not String.IsNullOrWhiteSpace(suggerimento) Then SuggerimentoCambiato(suggerimento)
            Await MostraCambiamentiAsync("Passaggio del mouse eseguito")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.PassaSopraRiga: " & ex.Message)
        End Try
    End Sub

    Private Async Function MostraCambiamentiAsync(senzaCambiamenti As String) As Task
        Try
            If _menuContestualeAperto Then Return
            If Not Await _cursore.AggiornaAsync(False) Then Return
            If _menuContestualeAperto Then Return
            Dim nuove As List(Of RigaVirtuale) = _cursore.UltimeRigheNuove
            If nuove.Count > 0 Then
                Dim indice As Integer = _cursore.Righe.IndexOf(nuove(0))
                If indice >= 0 Then _cursore.SpostaA(indice, False)
                lettura.LeggiTesto("Comparso: " & nuove(0).CostruisciAnnuncio(True) & If(nuove.Count > 1, ", e altre " & (nuove.Count - 1) & If(nuove.Count = 2, " riga", " righe"), ""))
            ElseIf _ultimoSuggerimento = "" Then
                lettura.LeggiTesto(senzaCambiamenti & ", nessun cambiamento nella pagina")
            End If
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraCambiamentiAsync: " & ex.Message)
        End Try
    End Function

    Private Async Sub ClicSpeciale(riga As RigaVirtuale, pulsante As PulsanteMouse, conteggio As Integer, modificatori As ModificatoriInput, descrizione As String)
        Try
            _menuContestualeAperto = False
            Dim navigazione As Integer = numeroNavigazione
            If Not Await _cdp.ClicAsync(riga, pulsante, conteggio, modificatori) Then
                lettura.LeggiTesto("Impossibile raggiungere l'elemento con il mouse")
                Return
            End If
            Await _cdp.AttendiInputAsync()
            Await AttendiStabilitaAsync(1500)
            If navigazione <> numeroNavigazione OrElse _chiusa Then Return
            Await MostraCambiamentiAsync(descrizione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ClicSpeciale: " & ex.Message)
        End Try
    End Sub

    Private Async Sub ScorriSuRiga(riga As RigaVirtuale, versoIlBasso As Boolean)
        Try
            If Not Await _cdp.ScorriSuRigaAsync(riga, versoIlBasso) Then
                lettura.LeggiTesto("Impossibile raggiungere l'elemento con il mouse")
                Return
            End If
            Await AttendiStabilitaAsync(2000)
            Await MostraCambiamentiAsync("Scorrimento eseguito")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScorriSuRiga: " & ex.Message)
        End Try
    End Sub

    Private Async Sub RilasciaSu(destinazione As RigaVirtuale)
        Try
            Dim origine As RigaVirtuale = _origineTrascinamento
            _origineTrascinamento = Nothing
            If origine Is Nothing Then Return
            lettura.LeggiTesto("Trascinamento in corso")
            Dim errore As String = Await _cdp.TrascinaAsync(origine, destinazione)
            If errore <> "" Then
                lettura.LeggiTesto(errore)
                Return
            End If
            Await AttendiStabilitaAsync(1500)
            Await _cursore.AggiornaAsync(False)
            lettura.LeggiTesto("Elemento rilasciato")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RilasciaSu: " & ex.Message)
        End Try
    End Sub

    Private Async Sub ComandoMedia(riga As RigaVirtuale, comando As String)
        Try
            Dim esito As String = Await _cdp.ComandoMediaAsync(riga, comando)
            Select Case esito
                Case "avviato" : lettura.LeggiTesto("Riproduzione avviata")
                Case "pausa" : lettura.LeggiTesto("In pausa")
                Case "muto" : lettura.LeggiTesto("Audio disattivato")
                Case "audio" : lettura.LeggiTesto("Audio attivato")
                Case "avanti" : lettura.LeggiTesto("Avanti di 10 secondi")
                Case "indietro" : lettura.LeggiTesto("Indietro di 10 secondi")
                Case "nessun", "" : lettura.LeggiTesto("Nessun video o audio in questo elemento")
                Case "errore" : lettura.LeggiTesto("Comando non riuscito")
                Case Else
                    Dim parti As String() = esito.Split("|"c)
                    If parti.Length >= 4 Then
                        Dim posizione As Integer = 0
                        Dim durata As Integer = 0
                        Integer.TryParse(parti(1), posizione)
                        Integer.TryParse(parti(2), durata)
                        Dim testo As String = parti(0) & ", " & DescriviDurata(posizione) & If(durata > 0, " di " & DescriviDurata(durata), "")
                        If parti(3) <> "0" Then testo &= ". Il formato non è supportato: Control Shift V per usare il lettore di WinGabriel"
                        lettura.LeggiTesto(testo)
                    Else
                        lettura.LeggiTesto(esito)
                    End If
            End Select
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ComandoMedia: " & ex.Message)
        End Try
    End Sub

    Private Shared Function DescriviDurata(secondi As Integer) As String
        Dim tempo As TimeSpan = TimeSpan.FromSeconds(Math.Max(0, secondi))
        If tempo.TotalHours >= 1 Then Return CInt(Math.Floor(tempo.TotalHours)) & " ore " & tempo.Minutes & " minuti"
        If tempo.TotalMinutes >= 1 Then Return tempo.Minutes & " minuti " & tempo.Seconds & " secondi"
        Return tempo.Seconds & " secondi"
    End Function

    Private Async Sub ScaricaDaRiga(riga As RigaVirtuale)
        Try
            Dim indirizzoFile As String = Await _cdp.IndirizzoRigaAsync(riga)
            Scarica(indirizzoFile)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScaricaDaRiga: " & ex.Message)
        End Try
    End Sub

    Private Async Sub CopiaIndirizzoRiga(riga As RigaVirtuale)
        Try
            CopiaNegliAppunti(Await _cdp.IndirizzoRigaAsync(riga), "Indirizzo copiato negli appunti")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CopiaIndirizzoRiga: " & ex.Message)
        End Try
    End Sub

    Private Async Sub ApriRigaInScheda(riga As RigaVirtuale, sfondo As Boolean)
        Try
            Dim indirizzoLink As String = Await _cdp.IndirizzoRigaAsync(riga)
            If String.IsNullOrWhiteSpace(indirizzoLink) OrElse indirizzoLink.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) Then
                lettura.LeggiTesto("Questo elemento non ha un indirizzo da aprire")
                Return
            End If
            _contenitore.NuovaScheda(indirizzoLink, sfondo, Me)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ApriRigaInScheda: " & ex.Message)
        End Try
    End Sub

    Private Async Sub RiproduciRiga(riga As RigaVirtuale)
        Try
            Dim sorgente As String = If(riga.Sorgente <> "", riga.Sorgente, Await _cdp.SorgenteMediaAsync(riga))
            If sorgente.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) Then sorgente = ""
            RiproduciConWinGabriel(sorgente)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.RiproduciRiga: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraDettagliRiga(riga As RigaVirtuale)
        Try
            Dim testo As New StringBuilder()
            testo.Append(riga.CostruisciAnnuncio(True)).Append(". ")
            If riga.RuoloDaAttivare <> "" AndAlso riga.RuoloDaAttivare <> riga.Ruolo Then testo.Append("Si attiva come ").Append(RigaVirtuale.DescriviTipo(New RigaVirtuale() With {.Ruolo = riga.RuoloDaAttivare})).Append(". ")
            If riga.Indirizzo <> "" Then testo.Append("Indirizzo: ").Append(riga.Indirizzo).Append(". ")
            If riga.Sorgente <> "" Then testo.Append("File: ").Append(riga.Sorgente).Append(". ")
            If riga.Lingua <> "" Then testo.Append("Lingua: ").Append(WGLayoutPagina.NomeLingua(riga.Lingua)).Append(". ")
            If riga.IdTabella <> "" Then testo.Append(_cursore.DescriviCellaCorrente()).Append(". ")
            testo.Append("Riga ").Append(_cursore.IndiceCorrente + 1).Append(" di ").Append(_cursore.Righe.Count).Append(".")
            Dim info As New WGMessaggi()
            info.Messaggio(testo.ToString())
            info.Show()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraDettagliRiga: " & ex.Message)
        End Try
    End Sub

    Friend Sub MostraAzioniElemento()
        Try
            Dim riga As RigaVirtuale = _cursore.RigaCorrente
            If riga Is Nothing Then
                lettura.LeggiTesto("Nessun elemento selezionato")
                Return
            End If
            Dim menu As New WGMenu()
            If riga.IsCampo Then
                menu.aggiungi("Entra nel campo", Sub(s, e) SuUI(Async Sub() Await AttivaRigaCorrenteAsync()))
            Else
                menu.aggiungi("Attiva, come Invio", Sub(s, e) SuUI(Async Sub() Await AttivaRigaCorrenteAsync()))
            End If
            If riga.ELink Then
                menu.aggiungi("Apri in una nuova scheda", Sub(s, e) ApriRigaInScheda(riga, False))
                menu.aggiungi("Apri in una scheda in secondo piano", Sub(s, e) ApriRigaInScheda(riga, True))
                menu.aggiungi("Copia l'indirizzo del link", Sub(s, e) CopiaIndirizzoRiga(riga))
                menu.aggiungi("Scarica il file del link", Sub(s, e) ScaricaDaRiga(riga))
            End If
            menu.aggiungi("Passa sopra con il mouse", Sub(s, e) PassaSopraRiga(riga))
            menu.aggiungi("Clic con il tasto destro", Sub(s, e) ClicSpeciale(riga, PulsanteMouse.Destro, 1, ModificatoriInput.Nessuno, "Clic destro eseguito"))
            menu.aggiungi("Doppio clic", Sub(s, e) ClicSpeciale(riga, PulsanteMouse.Sinistro, 2, ModificatoriInput.Nessuno, "Doppio clic eseguito"))
            menu.aggiungi("Clic tenendo premuto Control", Sub(s, e) ClicSpeciale(riga, PulsanteMouse.Sinistro, 1, ModificatoriInput.Control, "Clic eseguito"))
            menu.aggiungi("Clic con il tasto centrale", Sub(s, e) ClicSpeciale(riga, PulsanteMouse.Centrale, 1, ModificatoriInput.Nessuno, "Clic centrale eseguito"))
            menu.aggiungi("Scorri con la rotellina verso il basso", Sub(s, e) ScorriSuRiga(riga, True))
            menu.aggiungi("Scorri con la rotellina verso l'alto", Sub(s, e) ScorriSuRiga(riga, False))
            If _origineTrascinamento Is Nothing Then
                menu.aggiungi("Trascina questo elemento", Sub(s, e)
                                                              _origineTrascinamento = riga
                                                              lettura.LeggiTesto("Elemento preso. Spostarsi sulla destinazione e scegliere Rilascia qui dal tasto Applicazioni")
                                                          End Sub)
            Else
                Dim nomeOrigine As String = _origineTrascinamento.Testo
                menu.aggiungi("Rilascia qui l'elemento trascinato: " & nomeOrigine, Sub(s, e) RilasciaSu(riga))
                menu.aggiungi("Annulla il trascinamento", Sub(s, e)
                                                              _origineTrascinamento = Nothing
                                                              lettura.LeggiTesto("Trascinamento annullato")
                                                          End Sub)
            End If
            If riga.Ruolo = "img" OrElse riga.Ruolo = "canvas" Then
                menu.aggiungi("Descrivi con l'intelligenza artificiale", Sub(s, e) DescriviRiga(riga))
            End If
            menu.aggiungi("Riconosci il testo dell'elemento", Sub(s, e) RiconosciTestoRiga(riga))
            If riga.Ruolo = "img" AndAlso riga.Sorgente <> "" Then
                menu.aggiungi("Salva l'immagine", Sub(s, e) Scarica(riga.Sorgente))
                menu.aggiungi("Copia l'indirizzo dell'immagine", Sub(s, e) CopiaNegliAppunti(riga.Sorgente, "Indirizzo dell'immagine copiato"))
            End If
            If riga.Ruolo = "video" OrElse riga.Ruolo = "audio" Then
                menu.aggiungi("Riproduci", Sub(s, e) ComandoMedia(riga, "play"))
                menu.aggiungi("Metti in pausa", Sub(s, e) ComandoMedia(riga, "pause"))
                menu.aggiungi("Avanti di 10 secondi", Sub(s, e) ComandoMedia(riga, "avanti"))
                menu.aggiungi("Indietro di 10 secondi", Sub(s, e) ComandoMedia(riga, "indietro"))
                menu.aggiungi("Attiva o disattiva l'audio", Sub(s, e) ComandoMedia(riga, "muto"))
                menu.aggiungi("Stato della riproduzione", Sub(s, e) ComandoMedia(riga, "stato"))
                menu.aggiungi("Riproduci con il lettore di WinGabriel", Sub(s, e) RiproduciRiga(riga))
            End If
            menu.aggiungi("Copia il testo della riga", Sub(s, e) CopiaRigaCorrente())
            menu.aggiungi("Dettagli dell'elemento", Sub(s, e) MostraDettagliRiga(riga))
            menu.avvia("Azioni su " & If(String.IsNullOrWhiteSpace(riga.TipoElemento), "", riga.TipoElemento & " ") & riga.Testo, True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraAzioniElemento: " & ex.ToString())
        End Try
    End Sub

    Friend Sub MostraImpostazioniSito()
        Try
            Dim host As String = WGArchivioBrowser.HostDi(_indirizzo)
            If host = "" Then
                lettura.LeggiTesto("Nessun sito aperto in questa scheda")
                Return
            End If
            Dim archivioSiti As WGArchivioBrowser = Archivio
            Dim identitaSito As String = If(archivioSiti IsNot Nothing, archivioSiti.ValoreSito(host, "identita"), "")
            Dim pubblicitaConsentita As Boolean = WGBloccoPubblicita.SitoEscluso(host)
            Dim controlloCompleto As Boolean = WGImpostazioniBrowser.SitoInControlloCompleto(host)
            Dim passwordMai As Boolean = archivioSiti IsNot Nothing AndAlso archivioSiti.ValoreSito(host, "password_mai") = "1"
            Dim menu As New WGMenu()
            menu.aggiungi("Identità verso il sito: " & If(WGImpostazioniBrowser.EIdentitaFissa(identitaSito), WGImpostazioniBrowser.NomeIdentita(identitaSito), "automatica, ora " & WGImpostazioniBrowser.NomeIdentita(WGImpostazioniBrowser.IdentitaPerSito(host))), Sub(s, e) ScegliIdentitaSito(host))
            menu.aggiungi("Pubblicità: " & If(pubblicitaConsentita, "consentita su questo sito", "bloccata"), Sub(s, e)
                                                                                                                  WGBloccoPubblicita.ImpostaSitoEscluso(host, Not pubblicitaConsentita)
                                                                                                                  MessaggioBrowser(If(pubblicitaConsentita, "Pubblicità di nuovo bloccata su ", "Pubblicità consentita su ") & host & ". Ricaricare la pagina per applicare")
                                                                                                              End Sub)
            menu.aggiungi("Controllo completo all'apertura del sito: " & If(controlloCompleto, "sì", "no"), Sub(s, e)
                                                                                                                WGImpostazioniBrowser.ImpostaControlloCompleto(host, Not controlloCompleto)
                                                                                                                MessaggioBrowser(If(controlloCompleto, "Il sito si aprirà in navigazione", "Il sito si aprirà con il controllo completo"))
                                                                                                            End Sub)
            menu.aggiungi("Proponi di salvare le password: " & If(passwordMai, "no", "sì"), Sub(s, e)
                                                                                                If archivioSiti IsNot Nothing Then archivioSiti.ImpostaValoreSito(host, "password_mai", If(passwordMai, "", "1"))
                                                                                                MessaggioBrowser(If(passwordMai, "Il browser proporrà di salvare le password", "Il browser non proporrà di salvare le password"))
                                                                                            End Sub)
            menu.aggiungi("Dimentica i permessi concessi o negati al sito", Sub(s, e)
                                                                                If archivioSiti Is Nothing Then Return
                                                                                Dim origine As String = WGArchivioBrowser.OrigineDi(_indirizzo)
                                                                                For Each tipo As String In New String() {"microfono", "fotocamera", "posizione", "notifiche", "appunti", "download", "schermo", "midi", "sensori", "archiviazione", "finestre", "attivita", "file", "rete"}
                                                                                    archivioSiti.ImpostaPermessoSito(origine, tipo, "")
                                                                                Next
                                                                                _contenitore.AggiornaScriptIniziali()
                                                                                MessaggioBrowser("Il sito chiederà di nuovo i permessi")
                                                                            End Sub)
            menu.aggiungi("Zoom del sito: " & _zoomPercento & " percento, riporta al 100", Sub(s, e) CambiaZoom(0))
            menu.avvia("Impostazioni di " & host, True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.MostraImpostazioniSito: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliIdentitaSito(host As String)
        Try
            Dim applica As Action(Of String, String) = Async Sub(valore As String, descrizione As String)
                                                           Try
                                                               WGImpostazioniBrowser.ImpostaIdentitaSito(host, valore)
                                                               _identitaCorrente = WGImpostazioniBrowser.IdentitaPerSito(host)
                                                               Await ApplicaIdentitaAsync()
                                                               MessaggioBrowser("Identità " & descrizione & ". La pagina viene ricaricata")
                                                               Ricarica()
                                                           Catch exApplica As Exception
                                                               log.ScriviLog("WGSchedaBrowser.ScegliIdentitaSito applica: " & exApplica.Message)
                                                           End Try
                                                       End Sub
            Dim menu As New WGMenu()
            menu.aggiungi("Automatica, consigliata: segue l'impostazione generale, con Edge sui servizi di video protetti", Sub(s, e) applica("", "automatica"))
            menu.aggiungi("Presentati come Chrome", Sub(s, e) applica(WGImpostazioniBrowser.IdentitaChrome, "Chrome"))
            menu.aggiungi("Presentati come Firefox", Sub(s, e) applica(WGImpostazioniBrowser.IdentitaFirefox, "Firefox"))
            menu.aggiungi("Presentati come Edge, utile per i video protetti che usano PlayReady come Netflix", Sub(s, e) applica(WGImpostazioniBrowser.IdentitaEdge, "Edge"))
            Dim archivioSiti As WGArchivioBrowser = Archivio
            Select Case WGImpostazioniBrowser.NormalizzaIdentita(If(archivioSiti IsNot Nothing, archivioSiti.ValoreSito(host, "identita"), ""), "")
                Case WGImpostazioniBrowser.IdentitaChrome : menu.posiziona(1)
                Case WGImpostazioniBrowser.IdentitaFirefox : menu.posiziona(2)
                Case WGImpostazioniBrowser.IdentitaEdge : menu.posiziona(3)
                Case Else : menu.posiziona(0)
            End Select
            menu.avvia("Come presentarsi a " & host, True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ScegliIdentitaSito: " & ex.Message)
        End Try
    End Sub
End Class