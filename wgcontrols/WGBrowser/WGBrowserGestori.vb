Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Microsoft.Web.WebView2.Core
Imports WGBrowserManager
Imports wgcontrols

' Gestori degli eventi del browser: cicli di vita, dialoghi, richieste di rete, download e menu contestuali.
Friend NotInheritable Class WGBrowserGestoreVita
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.NewWindowRequested, AddressOf NuovaFinestraRichiesta
            AddHandler core.WindowCloseRequested, AddressOf ChiusuraRichiesta
            AddHandler core.ProcessFailed, AddressOf ProcessoFallito
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVita.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.NewWindowRequested, AddressOf NuovaFinestraRichiesta
            RemoveHandler core.WindowCloseRequested, AddressOf ChiusuraRichiesta
            RemoveHandler core.ProcessFailed, AddressOf ProcessoFallito
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVita.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub NuovaFinestraRichiesta(sender As Object, e As CoreWebView2NewWindowRequestedEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento NewWindowRequested " & If(e IsNot Nothing, If(e.Uri, ""), ""))
            If e Is Nothing Then Return
            rinvio = e.GetDeferral()
            Dim argomenti As CoreWebView2NewWindowRequestedEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            _scheda.DopoEvento(Sub() _scheda.GestisciNuovaFinestra(argomenti, rinvioPreso), Sub() RifiutaNuovaFinestra(argomenti, rinvioPreso))
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVita.NuovaFinestraRichiesta: " & ex.ToString())
            Try
                If e IsNot Nothing Then e.Handled = True
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestoreVita.NuovaFinestraRichiesta rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub

    Private Sub RifiutaNuovaFinestra(argomenti As CoreWebView2NewWindowRequestedEventArgs, rinvio As CoreWebView2Deferral)
        Try
            If argomenti IsNot Nothing Then argomenti.Handled = True
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVita.RifiutaNuovaFinestra: " & ex.Message)
        End Try
        Try
            If rinvio IsNot Nothing Then rinvio.Complete()
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVita.RifiutaNuovaFinestra rinvio: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiusuraRichiesta(sender As Object, e As Object)
        Try
            WGTracciaMotore.Scrivi("evento WindowCloseRequested")
            _scheda.DopoEvento(Sub() _scheda.BrowserChiuso())
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVita.ChiusuraRichiesta: " & ex.Message)
        End Try
    End Sub

    Private Sub ProcessoFallito(sender As Object, e As CoreWebView2ProcessFailedEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento ProcessFailed " & If(e IsNot Nothing, e.ProcessFailedKind.ToString(), ""))
            If e Is Nothing Then Return
            Dim motivo As String = ""
            Try
                motivo = e.Reason.ToString() & ", codice " & e.ExitCode.ToString()
            Catch exMotivo As Exception
                _log.ScriviLog("WGBrowserGestoreVita.ProcessoFallito motivo: " & exMotivo.Message)
            End Try
            Select Case e.ProcessFailedKind
                Case CoreWebView2ProcessFailedKind.BrowserProcessExited
                    _log.ScriviLog("WGBrowserGestoreVita: il processo principale del motore è terminato, " & motivo)
                    _scheda.DopoEvento(Sub() _scheda.MotoreArrestato())
                Case CoreWebView2ProcessFailedKind.RenderProcessExited
                    _log.ScriviLog("WGBrowserGestoreVita: il processo della pagina è terminato, " & motivo)
                    _scheda.DopoEvento(Sub() _scheda.ProcessoTerminato(False))
                Case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive
                    _log.ScriviLog("WGBrowserGestoreVita: la pagina non risponde, " & motivo)
                    _scheda.DopoEvento(Sub() _scheda.ProcessoTerminato(True))
                Case Else
                    _log.ScriviLog("WGBrowserGestoreVita: processo del motore terminato, tipo " & e.ProcessFailedKind.ToString() & ", " & motivo)
            End Select
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVita.ProcessoFallito: " & ex.Message)
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestoreDialoghi
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.ScriptDialogOpening, AddressOf DialogoInApertura
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDialoghi.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.ScriptDialogOpening, AddressOf DialogoInApertura
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDialoghi.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub DialogoInApertura(sender As Object, e As CoreWebView2ScriptDialogOpeningEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento ScriptDialogOpening")
            If e Is Nothing Then Return
            rinvio = e.GetDeferral()
            Dim argomenti As CoreWebView2ScriptDialogOpeningEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            Dim uscita As Boolean = e.Kind = CoreWebView2ScriptDialogKind.Beforeunload
            _scheda.DopoEvento(Sub()
                                   If uscita Then
                                       _scheda.ConfermaUscitaPagina(argomenti, rinvioPreso)
                                   Else
                                       _scheda.MostraDialogoPagina(argomenti, rinvioPreso)
                                   End If
                               End Sub, Sub() ChiudiDialogo(argomenti, rinvioPreso, uscita))
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDialoghi.DialogoInApertura: " & ex.Message)
            Try
                If e IsNot Nothing AndAlso e.Kind = CoreWebView2ScriptDialogKind.Beforeunload Then e.Accept()
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestoreDialoghi.DialogoInApertura rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub

    Private Sub ChiudiDialogo(argomenti As CoreWebView2ScriptDialogOpeningEventArgs, rinvio As CoreWebView2Deferral, uscita As Boolean)
        Try
            If uscita AndAlso argomenti IsNot Nothing Then argomenti.Accept()
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDialoghi.ChiudiDialogo: " & ex.Message)
        End Try
        Try
            If rinvio IsNot Nothing Then rinvio.Complete()
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDialoghi.ChiudiDialogo rinvio: " & ex.Message)
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestoreRichieste
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.BasicAuthenticationRequested, AddressOf AutenticazioneRichiesta
            AddHandler core.ServerCertificateErrorDetected, AddressOf ErroreCertificato
            AddHandler core.ClientCertificateRequested, AddressOf CertificatoClientRichiesto
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRichieste.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.BasicAuthenticationRequested, AddressOf AutenticazioneRichiesta
            RemoveHandler core.ServerCertificateErrorDetected, AddressOf ErroreCertificato
            RemoveHandler core.ClientCertificateRequested, AddressOf CertificatoClientRichiesto
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRichieste.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub AutenticazioneRichiesta(sender As Object, e As CoreWebView2BasicAuthenticationRequestedEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento BasicAuthenticationRequested")
            If e Is Nothing Then Return
            rinvio = e.GetDeferral()
            Dim argomenti As CoreWebView2BasicAuthenticationRequestedEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            _scheda.DopoEvento(Sub() _scheda.ChiediCredenzialiHttp(argomenti, rinvioPreso), Sub()
                                                                                                Try
                                                                                                    argomenti.Cancel = True
                                                                                                Catch exAnnulla As Exception
                                                                                                    _log.ScriviLog("WGBrowserGestoreRichieste.AutenticazioneRichiesta annulla: " & exAnnulla.Message)
                                                                                                End Try
                                                                                                CompletaRinvio(rinvioPreso)
                                                                                            End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRichieste.AutenticazioneRichiesta: " & ex.Message)
            Try
                If e IsNot Nothing Then e.Cancel = True
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestoreRichieste.AutenticazioneRichiesta rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub

    Private Sub ErroreCertificato(sender As Object, e As CoreWebView2ServerCertificateErrorDetectedEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento ServerCertificateErrorDetected")
            If e Is Nothing Then Return
            If Not _scheda.ENavigazionePrincipale(e.RequestUri) Then
                e.Action = CoreWebView2ServerCertificateErrorAction.Cancel
                Return
            End If
            rinvio = e.GetDeferral()
            Dim argomenti As CoreWebView2ServerCertificateErrorDetectedEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            _scheda.DopoEvento(Sub() _scheda.ChiediCertificato(argomenti, rinvioPreso), Sub()
                                                                                            Try
                                                                                                argomenti.Action = CoreWebView2ServerCertificateErrorAction.Cancel
                                                                                            Catch exAnnulla As Exception
                                                                                                _log.ScriviLog("WGBrowserGestoreRichieste.ErroreCertificato annulla: " & exAnnulla.Message)
                                                                                            End Try
                                                                                            CompletaRinvio(rinvioPreso)
                                                                                        End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRichieste.ErroreCertificato: " & ex.Message)
            Try
                If e IsNot Nothing Then e.Action = CoreWebView2ServerCertificateErrorAction.Cancel
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestoreRichieste.ErroreCertificato rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub

    Private Sub CertificatoClientRichiesto(sender As Object, e As CoreWebView2ClientCertificateRequestedEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento ClientCertificateRequested")
            If e Is Nothing Then Return
            rinvio = e.GetDeferral()
            Dim argomenti As CoreWebView2ClientCertificateRequestedEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            _scheda.DopoEvento(Sub() _scheda.ScegliCertificatoClient(argomenti, rinvioPreso), Sub()
                                                                                                  Try
                                                                                                      argomenti.SelectedCertificate = Nothing
                                                                                                      argomenti.Handled = True
                                                                                                  Catch exAnnulla As Exception
                                                                                                      _log.ScriviLog("WGBrowserGestoreRichieste.CertificatoClientRichiesto annulla: " & exAnnulla.Message)
                                                                                                  End Try
                                                                                                  CompletaRinvio(rinvioPreso)
                                                                                              End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRichieste.CertificatoClientRichiesto: " & ex.Message)
            Try
                If e IsNot Nothing Then
                    e.SelectedCertificate = Nothing
                    e.Handled = True
                End If
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestoreRichieste.CertificatoClientRichiesto rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub

    Private Sub CompletaRinvio(rinvio As CoreWebView2Deferral)
        Try
            If rinvio IsNot Nothing Then rinvio.Complete()
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRichieste.CompletaRinvio: " & ex.Message)
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestoreRisorse
    Private Shared ReadOnly IntestazioniMarchi As String() = {
        "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform", "sec-ch-ua-full-version-list", "sec-ch-ua-full-version",
        "sec-ch-ua-arch", "sec-ch-ua-bitness", "sec-ch-ua-model", "sec-ch-ua-platform-version", "sec-ch-ua-wow64", "sec-ch-ua-form-factors"
    }

    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All)
            AddHandler core.WebResourceRequested, AddressOf RisorsaRichiesta
            AddHandler core.WebResourceResponseReceived, AddressOf RispostaRicevuta
            AddHandler core.LaunchingExternalUriScheme, AddressOf ProgrammaEsternoRichiesto
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRisorse.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.WebResourceRequested, AddressOf RisorsaRichiesta
            RemoveHandler core.WebResourceResponseReceived, AddressOf RispostaRicevuta
            RemoveHandler core.LaunchingExternalUriScheme, AddressOf ProgrammaEsternoRichiesto
            Try
                core.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All)
            Catch exFiltro As Exception
                _log.ScriviLog("WGBrowserGestoreRisorse.Scollega filtro: " & exFiltro.Message)
            End Try
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRisorse.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Intestazione(intestazioni As CoreWebView2HttpResponseHeaders, nome As String) As String
        Try
            If intestazioni Is Nothing OrElse Not intestazioni.Contains(nome) Then Return ""
            Return If(intestazioni.GetHeader(nome), "")
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowserGestoreRisorse.Intestazione " & nome & ": " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub RispostaRicevuta(sender As Object, e As CoreWebView2WebResourceResponseReceivedEventArgs)
        Try
            If e Is Nothing Then Return
            Dim richiesta As CoreWebView2WebResourceRequest = e.Request
            If richiesta Is Nothing Then Return
            Dim indirizzo As String = If(richiesta.Uri, "")
            If Not EIndirizzoWeb(indirizzo) OrElse Not _scheda.ENavigazionePrincipale(indirizzo) Then Return
            If Not WGImpostazioniBrowser.ApriPdfNelLettore Then Return
            Dim risposta As CoreWebView2WebResourceResponseView = e.Response
            If risposta Is Nothing Then Return
            Dim stato As Integer = risposta.StatusCode
            If stato < 200 OrElse stato >= 300 Then Return
            Dim intestazioni As CoreWebView2HttpResponseHeaders = risposta.Headers
            If Intestazione(intestazioni, "Content-Type").IndexOf("application/pdf", StringComparison.OrdinalIgnoreCase) < 0 Then Return
            If Intestazione(intestazioni, "Content-Disposition").TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase) Then Return
            WGTracciaMotore.Scrivi("evento WebResourceResponseReceived pdf " & indirizzo)
            _scheda.DopoEvento(Sub() _scheda.PdfDaAprireNelLettore(indirizzo))
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRisorse.RispostaRicevuta: " & ex.Message)
        End Try
    End Sub

    Private Shared Function EIndirizzoWeb(indirizzo As String) As Boolean
        Return Not String.IsNullOrEmpty(indirizzo) AndAlso (indirizzo.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse indirizzo.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    End Function

    Private Sub RisorsaRichiesta(sender As Object, e As CoreWebView2WebResourceRequestedEventArgs)
        Try
            If e Is Nothing Then Return
            Dim richiesta As CoreWebView2WebResourceRequest = e.Request
            If richiesta Is Nothing Then Return
            Dim indirizzo As String = richiesta.Uri
            If Not EIndirizzoWeb(indirizzo) Then Return
            Dim browser As WGBrowser = _scheda.Contenitore
            If browser IsNot Nothing AndAlso browser.ModoLettore Then
                ' Nel lettore niente dalla rete: le immagini esterne dei messaggi dicono al mittente che sono stati aperti.
                Dim coreLettore As CoreWebView2 = _core
                If coreLettore IsNot Nothing Then e.Response = coreLettore.Environment.CreateWebResourceResponse(Nothing, 403, "Forbidden", "Content-Type: text/plain")
                Return
            End If
            Dim principale As Boolean = e.ResourceContext = CoreWebView2WebResourceContext.Document AndAlso _scheda.ENavigazionePrincipale(indirizzo)
            If e.ResourceContext = CoreWebView2WebResourceContext.Document Then WGTracciaMotore.Scrivi("evento WebResourceRequested documento " & If(principale, "principale ", "") & indirizzo)
            If Not principale AndAlso WGBloccoPubblicita.DaBloccare(indirizzo, _scheda.HostPagina) Then
                Dim core As CoreWebView2 = _core
                If core IsNot Nothing Then
                    e.Response = core.Environment.CreateWebResourceResponse(Nothing, 403, "Forbidden", "Content-Type: text/plain")
                    _scheda.RisorsaBloccata()
                    Return
                End If
            End If
            Dim intestazioni As CoreWebView2HttpRequestHeaders = richiesta.Headers
            If intestazioni Is Nothing Then Return
            Dim identita As String = _scheda.IdentitaCorrente
            Dim agente As String = WGIdentitaBrowser.UserAgent(identita)
            If Not String.Equals(intestazioni.GetHeader("User-Agent"), agente, StringComparison.Ordinal) Then intestazioni.SetHeader("User-Agent", agente)
            If intestazioni.Contains("Accept-Language") Then
                Dim lingue As String = WGIdentitaBrowser.LingueAccettate()
                If Not String.Equals(intestazioni.GetHeader("Accept-Language"), lingue, StringComparison.Ordinal) Then intestazioni.SetHeader("Accept-Language", lingue)
            End If
            If identita = WGImpostazioniBrowser.IdentitaFirefox Then
                For Each nome As String In IntestazioniMarchi
                    If intestazioni.Contains(nome) Then intestazioni.RemoveHeader(nome)
                Next
            Else
                Dim principaleMotore As Integer = WGIdentitaBrowser.VersionePrincipaleMotore()
                If intestazioni.Contains("sec-ch-ua") Then intestazioni.SetHeader("sec-ch-ua", WGIdentitaBrowser.IntestazioneMarchi(identita, principaleMotore))
                If intestazioni.Contains("sec-ch-ua-full-version-list") Then
                    Dim parti As New List(Of String)()
                    For Each voce As Newtonsoft.Json.Linq.JToken In WGIdentitaBrowser.MarchiBrowser(identita, principaleMotore, WGIdentitaBrowser.VersioneCompletaMotore(), True)
                        Dim o As Newtonsoft.Json.Linq.JObject = TryCast(voce, Newtonsoft.Json.Linq.JObject)
                        If o Is Nothing Then Continue For
                        parti.Add("""" & If(o.Value(Of String)("brand"), "").Replace("""", "") & """;v=""" & If(o.Value(Of String)("version"), "").Replace("""", "") & """")
                    Next
                    intestazioni.SetHeader("sec-ch-ua-full-version-list", String.Join(", ", parti))
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRisorse.RisorsaRichiesta: " & ex.Message)
        End Try
    End Sub

    Private Sub ProgrammaEsternoRichiesto(sender As Object, e As CoreWebView2LaunchingExternalUriSchemeEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento LaunchingExternalUriScheme")
            If e Is Nothing Then Return
            e.Cancel = True
            Dim indirizzo As String = If(e.Uri, "")
            If WGImpostazioniComuni.EIndirizzoEmail(indirizzo) Then
                _scheda.DopoEvento(Sub()
                                       If WGImpostazioniComuni.ApriEmailWinGabriel(indirizzo) Then
                                           _scheda.AnnunciaDaThread("Nuovo messaggio nella posta di WinGabriel")
                                       Else
                                           _scheda.AnnunciaDaThread("La posta di WinGabriel non è disponibile, quindi il messaggio non è stato preparato")
                                       End If
                                   End Sub)
                Return
            End If
            Dim schema As String = ""
            Dim duePunti As Integer = indirizzo.IndexOf(":"c)
            If duePunti > 0 Then schema = indirizzo.Substring(0, duePunti).ToLowerInvariant()
            Select Case schema
                Case "zoommtg", "zoomus", "msteams", "webex", "webexteams", "gotomeeting"
                    _scheda.AnnunciaDaThread("La pagina chiede il programma della riunione, che non viene aperto. Se la pagina lo offre, scegli di partecipare dal browser")
                Case Else
                    _scheda.AnnunciaDaThread("Questo collegamento chiede un programma esterno e non si può aprire in WinGabriel")
            End Select
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreRisorse.ProgrammaEsternoRichiesto: " & ex.Message)
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestoreDownload
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Shared Function NomeFileSicuro(nome As String) As String
        Try
            Dim risultato As String = If(nome, "").Trim()
            For Each c As Char In Path.GetInvalidFileNameChars()
                risultato = risultato.Replace(c, "_"c)
            Next
            If String.IsNullOrWhiteSpace(risultato) Then risultato = "file scaricato"
            If risultato.Length > 150 Then
                Dim estensione As String = Path.GetExtension(risultato)
                If estensione.Length > 10 Then estensione = ""
                risultato = risultato.Substring(0, 150 - estensione.Length) & estensione
            End If
            Return risultato
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowserGestoreDownload.NomeFileSicuro: " & ex.Message)
            Return "file scaricato"
        End Try
    End Function

    Public Shared Function PercorsoLibero(cartella As String, nome As String) As String
        Try
            Dim percorso As String = Path.Combine(cartella, nome)
            Dim contatore As Integer = 1
            While (File.Exists(percorso) OrElse File.Exists(percorso & ".scaricamento")) AndAlso contatore < 10000
                percorso = Path.Combine(cartella, Path.GetFileNameWithoutExtension(nome) & " (" & contatore & ")" & Path.GetExtension(nome))
                contatore += 1
            End While
            Return percorso
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowserGestoreDownload.PercorsoLibero: " & ex.Message)
            Return Path.Combine(cartella, nome)
        End Try
    End Function

    Public Shared Function CartellaScaricamenti() As String
        Dim cartella As String = ""
        Try
            cartella = WGImpostazioniBrowser.CartellaDownload
            If String.IsNullOrWhiteSpace(cartella) Then cartella = WGImpostazioniBrowser.CartellaDownloadPredefinita()
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Return cartella
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowserGestoreDownload.CartellaScaricamenti: " & ex.Message)
            Try
                cartella = WGImpostazioniBrowser.CartellaDownloadPredefinita()
                If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
                Return cartella
            Catch exRiserva As Exception
                registro.ScriviLog("WGBrowserGestoreDownload.CartellaScaricamenti riserva: " & exRiserva.Message)
                Return Path.GetTempPath()
            End Try
        End Try
    End Function

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.DownloadStarting, AddressOf DownloadRichiestoDallaPagina
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDownload.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.DownloadStarting, AddressOf DownloadRichiestoDallaPagina
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDownload.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub DownloadRichiestoDallaPagina(sender As Object, e As CoreWebView2DownloadStartingEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento DownloadStarting")
            If e Is Nothing Then Return
            _scheda.DownloadInArrivo()
            e.Handled = True
            Dim operazione As CoreWebView2DownloadOperation = e.DownloadOperation
            If operazione Is Nothing Then Return
            Dim proposto As String = ""
            Try
                proposto = Path.GetFileName(If(e.ResultFilePath, ""))
            Catch exNome As Exception
                _log.ScriviLog("WGBrowserGestoreDownload.DownloadRichiestoDallaPagina nome: " & exNome.Message)
            End Try
            Dim nome As String = NomeFileSicuro(proposto)
            Dim cartella As String = CartellaScaricamenti()
            Dim elemento As New ElementoDownload()
            elemento.Id = WGRegistroDownload.NuovoId()
            Try
                elemento.Indirizzo = If(operazione.Uri, "")
                elemento.IndirizzoOriginale = elemento.Indirizzo
                elemento.TipoMime = If(operazione.MimeType, "")
                Dim totale As ULong? = operazione.TotalBytesToReceive
                If totale.HasValue AndAlso totale.Value > 0UL Then elemento.Totali = CLng(Math.Min(CDec(totale.Value), CDec(Long.MaxValue)))
            Catch exOperazione As Exception
                _log.ScriviLog("WGBrowserGestoreDownload.DownloadRichiestoDallaPagina operazione: " & exOperazione.Message)
            End Try
            elemento.NomeProposto = nome
            If WGImpostazioniBrowser.ChiediDoveSalvare Then
                Dim rinvio As CoreWebView2Deferral = e.GetDeferral()
                Dim argomenti As CoreWebView2DownloadStartingEventArgs = e
                _scheda.DopoEvento(Sub() _scheda.ChiediDoveSalvare(argomenti, rinvio, elemento, operazione, cartella, nome), Sub()
                                                                                                                                   Try
                                                                                                                                       argomenti.Cancel = True
                                                                                                                                   Catch exAnnulla As Exception
                                                                                                                                       _log.ScriviLog("WGBrowserGestoreDownload.DownloadRichiestoDallaPagina annulla: " & exAnnulla.Message)
                                                                                                                                   End Try
                                                                                                                                   Try
                                                                                                                                       rinvio.Complete()
                                                                                                                                   Catch exRinvio As Exception
                                                                                                                                       _log.ScriviLog("WGBrowserGestoreDownload.DownloadRichiestoDallaPagina rinvio: " & exRinvio.Message)
                                                                                                                                   End Try
                                                                                                                               End Sub)
                Return
            End If
            Dim percorso As String = PercorsoLibero(cartella, nome)
            e.ResultFilePath = percorso
            elemento.PercorsoCompleto = percorso
            _scheda.DownloadAvviato(elemento, percorso, operazione)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreDownload.DownloadRichiestoDallaPagina: " & ex.ToString())
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestorePermessi
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.PermissionRequested, AddressOf PermessoRichiesto
            AddHandler core.ScreenCaptureStarting, AddressOf CatturaSchermoRichiestaDallaPagina
            AddHandler core.NotificationReceived, AddressOf NotificaRicevuta
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestorePermessi.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.PermissionRequested, AddressOf PermessoRichiesto
            RemoveHandler core.ScreenCaptureStarting, AddressOf CatturaSchermoRichiestaDallaPagina
            RemoveHandler core.NotificationReceived, AddressOf NotificaRicevuta
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestorePermessi.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub PermessoRichiesto(sender As Object, e As CoreWebView2PermissionRequestedEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento PermissionRequested " & If(e IsNot Nothing, e.PermissionKind.ToString(), ""))
            If e Is Nothing Then Return
            Try
                e.SavesInProfile = False
            Catch exSalva As Exception
                _log.ScriviLog("WGBrowserGestorePermessi.PermessoRichiesto profilo: " & exSalva.Message)
            End Try
            rinvio = e.GetDeferral()
            Dim argomenti As CoreWebView2PermissionRequestedEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            _scheda.DopoEvento(Sub() _scheda.RichiestaPermesso(argomenti, rinvioPreso), Sub()
                                                                                            Try
                                                                                                argomenti.State = CoreWebView2PermissionState.Deny
                                                                                            Catch exNega As Exception
                                                                                                _log.ScriviLog("WGBrowserGestorePermessi.PermessoRichiesto nega: " & exNega.Message)
                                                                                            End Try
                                                                                            CompletaRinvio(rinvioPreso)
                                                                                        End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestorePermessi.PermessoRichiesto: " & ex.Message)
            Try
                If e IsNot Nothing Then e.State = CoreWebView2PermissionState.Deny
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestorePermessi.PermessoRichiesto rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub

    Private Sub CatturaSchermoRichiestaDallaPagina(sender As Object, e As CoreWebView2ScreenCaptureStartingEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento ScreenCaptureStarting")
            If e Is Nothing Then Return
            rinvio = e.GetDeferral()
            Dim argomenti As CoreWebView2ScreenCaptureStartingEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            _scheda.DopoEvento(Sub() _scheda.CatturaSchermoRichiesta(argomenti, rinvioPreso), Sub()
                                                                                                  Try
                                                                                                      argomenti.Cancel = True
                                                                                                  Catch exAnnulla As Exception
                                                                                                      _log.ScriviLog("WGBrowserGestorePermessi.CatturaSchermoRichiestaDallaPagina annulla: " & exAnnulla.Message)
                                                                                                  End Try
                                                                                                  CompletaRinvio(rinvioPreso)
                                                                                              End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestorePermessi.CatturaSchermoRichiestaDallaPagina: " & ex.Message)
            Try
                If e IsNot Nothing Then e.Cancel = True
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestorePermessi.CatturaSchermoRichiestaDallaPagina rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub

    Private Sub NotificaRicevuta(sender As Object, e As CoreWebView2NotificationReceivedEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento NotificationReceived")
            If e Is Nothing Then Return
            e.Handled = True
            Dim notifica As CoreWebView2Notification = e.Notification
            If notifica Is Nothing Then Return
            Dim titolo As String = ""
            Dim corpo As String = ""
            Try
                titolo = If(notifica.Title, "")
                corpo = If(notifica.Body, "")
            Catch exTesto As Exception
                _log.ScriviLog("WGBrowserGestorePermessi.NotificaRicevuta testo: " & exTesto.Message)
            End Try
            Try
                notifica.ReportShown()
            Catch exMostrata As Exception
                _log.ScriviLog("WGBrowserGestorePermessi.NotificaRicevuta mostrata: " & exMostrata.Message)
            End Try
            Dim origine As String = If(e.SenderOrigin, "")
            _scheda.DopoEvento(Sub() _scheda.NotificaDelSito(titolo, corpo, origine))
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestorePermessi.NotificaRicevuta: " & ex.Message)
        End Try
    End Sub

    Private Sub CompletaRinvio(rinvio As CoreWebView2Deferral)
        Try
            If rinvio IsNot Nothing Then rinvio.Complete()
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestorePermessi.CompletaRinvio: " & ex.Message)
        End Try
    End Sub
End Class

Public Class DatiMenuContestuale
    Public Property Tipo As CoreWebView2ContextMenuTargetKind = CoreWebView2ContextMenuTargetKind.Page
    Public Property Link As String = ""
    Public Property TestoLink As String = ""
    Public Property Sorgente As String = ""
    Public Property Selezione As String = ""
    Public Property Modificabile As Boolean = False
    Public Property X As Integer = 0
    Public Property Y As Integer = 0
    Public Property Voci As New List(Of Tuple(Of Integer, String))()
End Class

Friend NotInheritable Class WGBrowserGestoreMenuContestuale
    Private Shared ReadOnly VociEscluse As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "print", "emoji", "inspectElement", "webCapture", "webSelect", "pictureInPicture", "share", "saveAs", "saveImageAs",
        "saveLinkAs", "saveMediaAs", "saveFrameAs", "openLinkInNewWindow", "openLinkInInPrivateWindow"
    }

    Private Shared ReadOnly PartiEscluse As String() = {
        "collection", "readaloud", "immersive", "translate", "createqrcode", "qrcode", "sidebar", "copilot", "visualsearch",
        "inprivate", "profile", "save", "share", "print", "emoji", "devtools", "inspect"
    }

    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.ContextMenuRequested, AddressOf MenuRichiesto
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreMenuContestuale.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.ContextMenuRequested, AddressOf MenuRichiesto
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreMenuContestuale.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Shared Function VoceEsclusa(nome As String) As Boolean
        Try
            Dim valore As String = If(nome, "").Trim()
            If valore = "" Then Return False
            If VociEscluse.Contains(valore) Then Return True
            Dim minuscolo As String = valore.ToLowerInvariant()
            For Each parte As String In PartiEscluse
                If minuscolo.Contains(parte) Then Return True
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowserGestoreMenuContestuale.VoceEsclusa: " & ex.Message)
        End Try
        Return False
    End Function

    Private Shared Function PulisciEtichetta(etichetta As String) As String
        Try
            Return If(etichetta, "").Replace("&&", ChrW(1)).Replace("&", "").Replace(ChrW(1), "&").Trim()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGBrowserGestoreMenuContestuale.PulisciEtichetta: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AggiungiVoci(voci As IList(Of CoreWebView2ContextMenuItem), dati As DatiMenuContestuale)
        Try
            If voci Is Nothing Then Return
            For Each voce As CoreWebView2ContextMenuItem In voci
                Try
                    If voce Is Nothing Then Continue For
                    Dim tipo As CoreWebView2ContextMenuItemKind = voce.Kind
                    If tipo = CoreWebView2ContextMenuItemKind.Separator OrElse tipo = CoreWebView2ContextMenuItemKind.Submenu Then Continue For
                    If Not voce.IsEnabled Then Continue For
                    If VoceEsclusa(voce.Name) Then Continue For
                    Dim etichetta As String = PulisciEtichetta(voce.Label)
                    If etichetta = "" Then Continue For
                    If tipo = CoreWebView2ContextMenuItemKind.CheckBox OrElse tipo = CoreWebView2ContextMenuItemKind.Radio Then etichetta &= If(voce.IsChecked, ", attivo", ", non attivo")
                    dati.Voci.Add(Tuple.Create(voce.CommandId, etichetta))
                Catch exVoce As Exception
                    _log.ScriviLog("WGBrowserGestoreMenuContestuale voce: " & exVoce.Message)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreMenuContestuale.AggiungiVoci: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuRichiesto(sender As Object, e As CoreWebView2ContextMenuRequestedEventArgs)
        Dim rinvio As CoreWebView2Deferral = Nothing
        Try
            WGTracciaMotore.Scrivi("evento ContextMenuRequested")
            If e Is Nothing Then Return
            rinvio = e.GetDeferral()
            e.Handled = True
            Dim dati As New DatiMenuContestuale()
            Try
                Dim bersaglio As CoreWebView2ContextMenuTarget = e.ContextMenuTarget
                If bersaglio IsNot Nothing Then
                    dati.Tipo = bersaglio.Kind
                    If bersaglio.HasLinkUri Then dati.Link = If(bersaglio.LinkUri, "")
                    If bersaglio.HasLinkText Then dati.TestoLink = If(bersaglio.LinkText, "")
                    If bersaglio.HasSourceUri Then dati.Sorgente = If(bersaglio.SourceUri, "")
                    If bersaglio.HasSelection Then dati.Selezione = If(bersaglio.SelectionText, "")
                    dati.Modificabile = bersaglio.IsEditable
                End If
                dati.X = e.Location.X
                dati.Y = e.Location.Y
            Catch exBersaglio As Exception
                _log.ScriviLog("WGBrowserGestoreMenuContestuale.MenuRichiesto bersaglio: " & exBersaglio.Message)
            End Try
            AggiungiVoci(e.MenuItems, dati)
            Dim argomenti As CoreWebView2ContextMenuRequestedEventArgs = e
            Dim rinvioPreso As CoreWebView2Deferral = rinvio
            _scheda.DopoEvento(Sub() _scheda.MostraMenuContestuale(dati, argomenti, rinvioPreso), Sub()
                                                                                                      Try
                                                                                                          rinvioPreso.Complete()
                                                                                                      Catch exRinvio As Exception
                                                                                                          _log.ScriviLog("WGBrowserGestoreMenuContestuale.MenuRichiesto chiusura: " & exRinvio.Message)
                                                                                                      End Try
                                                                                                  End Sub)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreMenuContestuale.MenuRichiesto: " & ex.ToString())
            Try
                If e IsNot Nothing Then e.Handled = True
                If rinvio IsNot Nothing Then rinvio.Complete()
            Catch exRinvio As Exception
                _log.ScriviLog("WGBrowserGestoreMenuContestuale.MenuRichiesto rinvio: " & exRinvio.Message)
            End Try
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestoreVisualizzazione
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.SourceChanged, AddressOf IndirizzoCambiato
            AddHandler core.DocumentTitleChanged, AddressOf TitoloCambiato
            AddHandler core.ContainsFullScreenElementChanged, AddressOf SchermoInteroCambiato
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVisualizzazione.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.SourceChanged, AddressOf IndirizzoCambiato
            RemoveHandler core.DocumentTitleChanged, AddressOf TitoloCambiato
            RemoveHandler core.ContainsFullScreenElementChanged, AddressOf SchermoInteroCambiato
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVisualizzazione.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub IndirizzoCambiato(sender As Object, e As CoreWebView2SourceChangedEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento SourceChanged")
            Dim core As CoreWebView2 = TryCast(sender, CoreWebView2)
            If core Is Nothing Then core = _core
            If core Is Nothing Then Return
            _scheda.IndirizzoCambiato(If(core.Source, ""))
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVisualizzazione.IndirizzoCambiato: " & ex.Message)
        End Try
    End Sub

    Private Sub TitoloCambiato(sender As Object, e As Object)
        Try
            WGTracciaMotore.Scrivi("evento DocumentTitleChanged")
            Dim core As CoreWebView2 = TryCast(sender, CoreWebView2)
            If core Is Nothing Then core = _core
            If core Is Nothing Then Return
            _scheda.TitoloCambiato(If(core.DocumentTitle, ""))
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVisualizzazione.TitoloCambiato: " & ex.Message)
        End Try
    End Sub

    Private Sub SchermoInteroCambiato(sender As Object, e As Object)
        Try
            WGTracciaMotore.Scrivi("evento ContainsFullScreenElementChanged")
            Dim core As CoreWebView2 = TryCast(sender, CoreWebView2)
            If core Is Nothing Then core = _core
            If core Is Nothing Then Return
            _scheda.SchermoInteroCambiato(core.ContainsFullScreenElement)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreVisualizzazione.SchermoInteroCambiato: " & ex.Message)
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestoreCaricamento
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser)
        _scheda = scheda
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.NavigationStarting, AddressOf NavigazioneIniziata
            AddHandler core.ContentLoading, AddressOf ContenutoInCaricamento
            AddHandler core.DOMContentLoaded, AddressOf DocumentoPronto
            AddHandler core.NavigationCompleted, AddressOf NavigazioneCompletata
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreCaricamento.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core Is Nothing Then Return
            RemoveHandler core.NavigationStarting, AddressOf NavigazioneIniziata
            RemoveHandler core.ContentLoading, AddressOf ContenutoInCaricamento
            RemoveHandler core.DOMContentLoaded, AddressOf DocumentoPronto
            RemoveHandler core.NavigationCompleted, AddressOf NavigazioneCompletata
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreCaricamento.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub NavigazioneIniziata(sender As Object, e As CoreWebView2NavigationStartingEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento NavigationStarting " & If(e IsNot Nothing, e.NavigationId.ToString() & " " & If(e.Uri, ""), ""))
            If e Is Nothing Then Return
            Dim browser As WGBrowser = _scheda.Contenitore
            If browser IsNot Nothing AndAlso browser.ModoLettore AndAlso Not browser.EIndirizzoDelLettore(e.Uri) Then
                ' Nel lettore i collegamenti non cambiano la pagina: si aprono con il browser scelto.
                e.Cancel = True
                Dim destinazione As String = If(e.Uri, "")
                _scheda.DopoEvento(Sub() WGImpostazioniComuni.ApriLinkConBrowserPreferito(destinazione))
                Return
            End If
            ' Qualunque navigazione verso un sito WordPress mai visto (anche un collegamento in una pagina) si ferma e si controlla prima:
            ' con la lettura semplificata la pagina non si carica affatto, altrimenti si apre subito dopo.
            If Not e.IsRedirected AndAlso _scheda.ControllaWordPressPrimaDiAprire(If(e.Uri, "")) Then
                e.Cancel = True
                Return
            End If
            _scheda.NavigazioneIniziata(If(e.Uri, ""), e.NavigationId, e.IsRedirected)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreCaricamento.NavigazioneIniziata: " & ex.Message)
        End Try
    End Sub

    Private Sub ContenutoInCaricamento(sender As Object, e As CoreWebView2ContentLoadingEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento ContentLoading " & If(e IsNot Nothing, e.NavigationId.ToString(), ""))
            If e Is Nothing Then Return
            _scheda.ContenutoInCaricamento(e.NavigationId, e.IsErrorPage)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreCaricamento.ContenutoInCaricamento: " & ex.Message)
        End Try
    End Sub

    Private Sub DocumentoPronto(sender As Object, e As CoreWebView2DOMContentLoadedEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento DOMContentLoaded " & If(e IsNot Nothing, e.NavigationId.ToString(), ""))
            If e Is Nothing Then Return
            _scheda.DocumentoPronto(e.NavigationId)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreCaricamento.DocumentoPronto: " & ex.Message)
        End Try
    End Sub

    Private Sub NavigazioneCompletata(sender As Object, e As CoreWebView2NavigationCompletedEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento NavigationCompleted " & If(e IsNot Nothing, e.NavigationId.ToString() & " " & e.IsSuccess.ToString() & " " & e.WebErrorStatus.ToString(), ""))
            If e Is Nothing Then Return
            Dim codiceHttp As Integer = 0
            Try
                codiceHttp = e.HttpStatusCode
            Catch exCodice As Exception
                _log.ScriviLog("WGBrowserGestoreCaricamento.NavigazioneCompletata codice: " & exCodice.Message)
            End Try
            _scheda.NavigazioneCompletata(e.NavigationId, e.IsSuccess, e.WebErrorStatus, codiceHttp)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreCaricamento.NavigazioneCompletata: " & ex.Message)
        End Try
    End Sub
End Class

Friend NotInheritable Class WGBrowserGestoreFrame
    Private ReadOnly _scheda As WGSchedaBrowser
    Private ReadOnly _pagina As WGPaginaWeb
    Private ReadOnly _log As New Logger()
    Private _core As CoreWebView2 = Nothing

    Public Sub New(scheda As WGSchedaBrowser, pagina As WGPaginaWeb)
        _scheda = scheda
        _pagina = pagina
    End Sub

    Public Sub Collega(core As CoreWebView2)
        Try
            If core Is Nothing Then Return
            Scollega()
            _core = core
            AddHandler core.WebMessageReceived, AddressOf MessaggioRicevuto
            AddHandler core.FrameCreated, AddressOf FrameCreato
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreFrame.Collega: " & ex.Message)
        End Try
    End Sub

    Public Sub Scollega()
        Try
            Dim core As CoreWebView2 = _core
            _core = Nothing
            If core IsNot Nothing Then
                RemoveHandler core.WebMessageReceived, AddressOf MessaggioRicevuto
                RemoveHandler core.FrameCreated, AddressOf FrameCreato
            End If
            If _pagina Is Nothing Then Return
            For Each frame As CoreWebView2Frame In _pagina.FrameAttivi()
                ScollegaFrame(frame)
            Next
            _pagina.DimenticaFrame()
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreFrame.Scollega: " & ex.Message)
        End Try
    End Sub

    Private Sub CollegaFrame(frame As CoreWebView2Frame)
        Try
            If frame Is Nothing Then Return
            AddHandler frame.WebMessageReceived, AddressOf MessaggioRicevuto
            AddHandler frame.FrameCreated, AddressOf FrameCreato
            AddHandler frame.Destroyed, AddressOf FrameDistrutto
            If _pagina IsNot Nothing Then _pagina.AggiungiFrame(frame)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreFrame.CollegaFrame: " & ex.Message)
        End Try
    End Sub

    Private Sub ScollegaFrame(frame As CoreWebView2Frame)
        Try
            If frame Is Nothing Then Return
            If _pagina IsNot Nothing Then _pagina.RimuoviFrame(frame)
            If frame.IsDestroyed() <> 0 Then Return
            RemoveHandler frame.WebMessageReceived, AddressOf MessaggioRicevuto
            RemoveHandler frame.FrameCreated, AddressOf FrameCreato
            RemoveHandler frame.Destroyed, AddressOf FrameDistrutto
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreFrame.ScollegaFrame: " & ex.Message)
        End Try
    End Sub

    Private Sub FrameCreato(sender As Object, e As CoreWebView2FrameCreatedEventArgs)
        Try
            WGTracciaMotore.Scrivi("evento FrameCreated")
            If e Is Nothing Then Return
            CollegaFrame(e.Frame)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreFrame.FrameCreato: " & ex.Message)
        End Try
    End Sub

    Private Sub FrameDistrutto(sender As Object, e As Object)
        Try
            WGTracciaMotore.Scrivi("evento FrameDestroyed")
            Dim frame As CoreWebView2Frame = TryCast(sender, CoreWebView2Frame)
            If frame IsNot Nothing AndAlso _pagina IsNot Nothing Then _pagina.RimuoviFrame(frame)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreFrame.FrameDistrutto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function TestoMessaggio(e As CoreWebView2WebMessageReceivedEventArgs) As String
        Try
            Return e.TryGetWebMessageAsString()
        Catch exTesto As Exception
            Try
                Dim json As String = e.WebMessageAsJson
                If String.IsNullOrEmpty(json) Then Return ""
                If json.StartsWith("{", StringComparison.Ordinal) Then Return json
                Return ""
            Catch exJson As Exception
                Dim registro As New Logger()
                registro.ScriviLog("WGBrowserGestoreFrame.TestoMessaggio: " & exJson.Message)
                Return ""
            End Try
        End Try
    End Function

    Private Sub MessaggioRicevuto(sender As Object, e As CoreWebView2WebMessageReceivedEventArgs)
        Try
            If e Is Nothing Then Return
            Dim testo As String = TestoMessaggio(e)
            If String.IsNullOrEmpty(testo) Then Return
            WGTracciaMotore.Scrivi("evento WebMessageReceived " & If(TypeOf sender Is CoreWebView2Frame, "cornice ", "") & If(testo.Length > 60, testo.Substring(0, 60), testo))
            ' L'origine del messaggio la dice WebView2 (e.Source), non la pagina: un sito non può farsi passare per un altro.
            Dim origineVera As String = ""
            Try
                origineVera = WGArchivioBrowser.OrigineDi(e.Source)
            Catch exOrigine As Exception
                _log.ScriviLog("WGBrowserGestoreFrame.MessaggioRicevuto origine: " & exOrigine.Message)
            End Try
            _scheda.MessaggioDallaPagina(testo, origineVera)
        Catch ex As Exception
            _log.ScriviLog("WGBrowserGestoreFrame.MessaggioRicevuto: " & ex.Message)
        End Try
    End Sub
End Class