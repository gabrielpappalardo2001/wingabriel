Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

''' <summary>
''' Esporta e importa i dati e la configurazione di WinGabriel.
'''   Dati: un file zip con tutta la cartella dei dati (scrivere in una cartella scelta, leggere da un file zip scelto). I file copiati sulla
'''   scrivania si esportano solo se lo si vuole.
'''   Configurazione: il file config.xml, esportato in una cartella scelta e importato da un file scelto; oppure azzerata per ricominciare
'''   dalla configurazione guidata.
''' Tutto passa dai menu, dalle domande e dal file dialog di WinGabriel; le attese da WGProcessoInCorso e gli errori da un semplice WGMessaggi.
''' </summary>
Friend NotInheritable Class ScambioDati

    Private Shared ReadOnly log As New Logger()
    Private Shared _inCorso As Boolean

    Private Sub New()
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Piccoli aiuti
    ' ------------------------------------------------------------------------------------------

    Private Shared Sub Messaggio(testo As String)
        ConfigurazioneGuidata.Messaggio(testo)
    End Sub

    ''' <summary>Sceglie una cartella con il file dialog. Restituisce "" se si annulla.</summary>
    Private Shared Function ScegliCartellaAsync() As Task(Of String)
        Dim esito As New TaskCompletionSource(Of String)()
        Dim dialogo As New WGFileDialog()
        AddHandler dialogo.singolaselezione, Sub(elemento As String) esito.TrySetResult(elemento)
        AddHandler dialogo.annullata, Sub() esito.TrySetResult("")
        dialogo.Avvia("")
        Return esito.Task
    End Function

    ''' <summary>Sceglie un file con l'estensione data. Restituisce "" se si annulla.</summary>
    Private Shared Function ScegliFileAsync(ParamArray estensioni As String()) As Task(Of String)
        Dim esito As New TaskCompletionSource(Of String)()
        Dim dialogo As New WGFileDialog()
        For Each estensione As String In estensioni
            dialogo.AggiungiEstensione(estensione)
        Next
        dialogo.MultiselezioneConsentita = False
        AddHandler dialogo.selezioneterminata, Sub(elementi As System.Collections.Generic.List(Of String))
                                                   esito.TrySetResult(If(elementi Is Nothing OrElse elementi.Count = 0, "", elementi(0)))
                                               End Sub
        AddHandler dialogo.annullata, Sub() esito.TrySetResult("")
        dialogo.Avvia()
        Return esito.Task
    End Function

    ''' <summary>
    ''' Chiede una password con i campi di WinGabriel. Con conferma chiede di ripeterla. Restituisce Nothing se si annulla
    ''' o se le due password non sono uguali (in quel caso lo dice).
    ''' </summary>
    Private Shared Function ChiediPasswordAsync(titolo As String, conConferma As Boolean) As Task(Of String)
        Dim esito As New TaskCompletionSource(Of String)()
        Dim campi As New Campi()
        campi.AggiungiPassword(titolo, "scambio.password", True)
        If conConferma Then campi.AggiungiPassword("Ripeti la password", "scambio.password2", True)
        AddHandler campi.Fine, Sub()
                                   Try
                                       Dim prima As String = campi.RecuperaValore("scambio.password")
                                       If conConferma AndAlso Not String.Equals(prima, campi.RecuperaValore("scambio.password2"), StringComparison.Ordinal) Then
                                           Messaggio("Le due password non sono uguali.")
                                           esito.TrySetResult(Nothing)
                                       Else
                                           esito.TrySetResult(prima)
                                       End If
                                   Catch
                                       esito.TrySetResult(Nothing)
                                   End Try
                               End Sub
        ' La chiusura può arrivare insieme alla conferma: le si dà il tempo di lasciar passare prima la password.
        AddHandler campi.Chiusura, Sub() Task.Delay(600).ContinueWith(Sub(t) esito.TrySetResult(Nothing))
        campi.Avvia()
        Return esito.Task
    End Function

    Private Shared Function Marca() As String
        Return DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
    End Function

    Private Shared Function PercorsiUguali(a As String, b As String) As Boolean
        Try
            Return String.Equals(Path.GetFullPath(a).TrimEnd("\"c), Path.GetFullPath(b).TrimEnd("\"c), StringComparison.OrdinalIgnoreCase)
        Catch
            Return False
        End Try
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Dati
    ' ------------------------------------------------------------------------------------------

    Private Const PercorsoScrivaniaFile As String = "scrivania\File"

    Private Shared Function CartellaDati() As String
        Dim cartella As String = WGDb.WGDatabase.CartellaBaseDatiEffettiva()
        If String.IsNullOrWhiteSpace(cartella) Then cartella = WGDb.WGDatabase.CartellaBaseDatiPredefinita()
        Return cartella
    End Function

    Private Shared Function HaFileScrivania(radice As String) As Boolean
        Try
            Dim cartella As String = Path.Combine(radice, PercorsoScrivaniaFile)
            If Not Directory.Exists(cartella) Then Return False
            For Each f As String In Directory.EnumerateFiles(cartella, "*", SearchOption.AllDirectories)
                Return True
            Next
        Catch
        End Try
        Return False
    End Function

    Private Shared Function DaEsportare(relativo As String, conScrivania As Boolean, escluse As System.Collections.Generic.List(Of String)) As Boolean
        Dim r As String = relativo.Replace("/"c, "\"c).TrimStart("\"c)
        Dim primo As String = r.Split("\"c)(0)
        For Each esclusa As String In escluse
            If String.Equals(primo, esclusa, StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        If Not conScrivania AndAlso (String.Equals(r, PercorsoScrivaniaFile, StringComparison.OrdinalIgnoreCase) OrElse r.StartsWith(PercorsoScrivaniaFile & "\", StringComparison.OrdinalIgnoreCase)) Then Return False
        Dim nome As String = Path.GetFileName(r)
        If nome.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) OrElse nome.EndsWith(".inCorso", StringComparison.OrdinalIgnoreCase) Then Return False
        Return True
    End Function

    ''' <summary>Crea lo zip con i dati. Restituisce "" se è andata bene, altrimenti il testo dell'errore.</summary>
    Private Shared Function CreaZipDati(radice As String, percorsoZip As String, conScrivania As Boolean, ByRef quanti As Integer, ByRef saltati As Integer) As String
        Try
            quanti = 0
            saltati = 0
            Dim escluse As System.Collections.Generic.List(Of String) = WGDb.WGBackup.CartelleEscluseDalSalvataggio()
            Dim zipCompleto As String = Path.GetFullPath(percorsoZip)
            Dim temporaneo As String = percorsoZip & ".inCorso"
            Using flusso As New FileStream(temporaneo, FileMode.Create, FileAccess.Write, FileShare.None)
                Using archivio As New ZipArchive(flusso, ZipArchiveMode.Create)
                    For Each percorso As String In Directory.EnumerateFiles(radice, "*", SearchOption.AllDirectories)
                        Try
                            If String.Equals(Path.GetFullPath(percorso), zipCompleto, StringComparison.OrdinalIgnoreCase) Then Continue For
                            Dim relativo As String = percorso.Substring(radice.TrimEnd("\"c).Length).TrimStart("\"c)
                            If Not DaEsportare(relativo, conScrivania, escluse) Then Continue For
                            Dim voce As ZipArchiveEntry = archivio.CreateEntry(relativo.Replace("\"c, "/"c), CompressionLevel.Optimal)
                            Using origine As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                                Using destinazione As Stream = voce.Open()
                                    origine.CopyTo(destinazione)
                                End Using
                            End Using
                            quanti += 1
                        Catch exFile As Exception
                            saltati += 1
                            log.ScriviLog("ScambioDati.CreaZipDati file (" & percorso & "): " & exFile.Message)
                        End Try
                    Next
                    Dim manifesto As ZipArchiveEntry = archivio.CreateEntry("wingabriel-dati.txt", CompressionLevel.Optimal)
                    Using scrittore As New StreamWriter(manifesto.Open(), New UTF8Encoding(False))
                        scrittore.WriteLine("Dati di WinGabriel esportati il " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                        scrittore.WriteLine("File: " & quanti.ToString(CultureInfo.InvariantCulture))
                        scrittore.WriteLine("File della scrivania: " & If(conScrivania, "inclusi", "non inclusi"))
                    End Using
                End Using
            End Using
            If File.Exists(percorsoZip) Then File.Delete(percorsoZip)
            File.Move(temporaneo, percorsoZip)
            Return ""
        Catch ex As Exception
            log.ScriviLog("ScambioDati.CreaZipDati: " & ex.ToString())
            Try
                If File.Exists(percorsoZip & ".inCorso") Then File.Delete(percorsoZip & ".inCorso")
            Catch
            End Try
            Return ex.Message
        End Try
    End Function

    ''' <summary>
    ''' Crea lo zip dei dati; con una password lo cifra con la libreria nativa nel file finale e toglie lo zip di lavoro.
    ''' Restituisce "" se è andata bene, altrimenti il testo dell'errore.
    ''' </summary>
    Private Shared Function CreaDatiEsportati(radice As String, zipLavoro As String, percorsoFinale As String, password As String, conScrivania As Boolean, ByRef quanti As Integer, ByRef saltati As Integer) As String
        Try
            Dim errore As String = CreaZipDati(radice, zipLavoro, conScrivania, quanti, saltati)
            If errore <> "" OrElse String.IsNullOrEmpty(password) Then Return errore
            Dim esito As Integer = WGDb.WGCifraturaNativa.CifraFile(zipLavoro, percorsoFinale, password)
            If esito <> 0 Then
                Return "la cifratura non è riuscita (" & If(esito = WGDb.WGCifraturaNativa.ErroreLibreria, "manca la libreria di cifratura", "codice " & esito.ToString(CultureInfo.InvariantCulture)) & ")."
            End If
            Return ""
        Catch ex As Exception
            log.ScriviLog("ScambioDati.CreaDatiEsportati: " & ex.ToString())
            Return ex.Message
        Finally
            Try
                If Not String.IsNullOrEmpty(password) AndAlso File.Exists(zipLavoro) Then File.Delete(zipLavoro)
            Catch exPulizia As Exception
                log.ScriviLog("ScambioDati.CreaDatiEsportati pulizia: " & exPulizia.Message)
            End Try
        End Try
    End Function

    Friend Shared Async Sub EsportaDati()
        If _inCorso Then Return
        _inCorso = True
        Try
            Dim radice As String = CartellaDati()
            If String.IsNullOrWhiteSpace(radice) OrElse Not Directory.Exists(radice) Then
                Messaggio("Non trovo la cartella dei dati di WinGabriel.")
                Return
            End If
            Dim conScrivania As Boolean = False
            If HaFileScrivania(radice) Then
                conScrivania = Await WGDomanda.ChiediAsync("Vuoi esportare anche i file copiati sulla scrivania?")
            End If
            Dim cartella As String = Await ScegliCartellaAsync()
            If cartella = "" Then Return
            If Not WGDb.WGDatabase.VerificaCartellaScrivibile(cartella) Then
                Messaggio("In " & cartella & " non riesco a scrivere.")
                Return
            End If
            ' Password, chiavi e accessi salvati dai vari servizi sono già protetti con la libreria di cifratura e viaggiano nei dati:
            ' sullo stesso WinGabriel si leggono su qualunque computer. Con una password si cifra anche tutto il file esportato.
            Dim password As String = ""
            If Await WGDomanda.ChiediAsync("Vuoi proteggere l'esportazione con una password? Senza la password il file non si potrà aprire.") Then
                password = Await ChiediPasswordAsync("Password con cui proteggere i dati esportati. Se la perdi, i dati non si recuperano", True)
                If String.IsNullOrEmpty(password) Then Return
            End If
            Dim conPassword As Boolean = password <> ""
            Dim percorsoZip As String = Path.Combine(cartella, "dati-wingabriel-" & Marca() & If(conPassword, ".wgdati", ".zip"))
            Dim zipLavoro As String = If(conPassword, Path.Combine(Path.GetTempPath(), "wgdati-" & Guid.NewGuid().ToString("N") & ".zip"), percorsoZip)
            Dim quanti As Integer = 0
            Dim saltati As Integer = 0
            Dim errore As String = Await WGProcessoInCorso.EseguiAsync("Esporto i dati di WinGabriel, attendi.",
                                                                       Function() Task.Run(Function() CreaDatiEsportati(radice, zipLavoro, percorsoZip, password, conScrivania, quanti, saltati)))
            If errore <> "" Then
                Messaggio("Non è stato possibile esportare i dati: " & errore)
                Return
            End If
            Messaggio("Dati esportati in " & percorsoZip & ": " & quanti & " file." & If(conPassword, " Il file è protetto dalla password che hai scelto.", "") & If(saltati > 0, " " & saltati & " file in uso non sono stati inclusi.", ""))
        Catch ex As Exception
            log.ScriviLog("ScambioDati.EsportaDati: " & ex.ToString())
            Messaggio("Non è stato possibile esportare i dati.")
        Finally
            _inCorso = False
        End Try
    End Sub

    ''' <summary>Estrae lo zip nella cartella dei dati. Restituisce "" se è andata bene, altrimenti il testo dell'errore.</summary>
    Private Shared Function EstraiZipDati(percorsoZip As String, radice As String, ByRef quanti As Integer, ByRef saltati As Integer) As String
        Try
            quanti = 0
            saltati = 0
            Dim radiceCompleta As String = Path.GetFullPath(radice).TrimEnd("\"c) & "\"
            Using archivio As ZipArchive = ZipFile.OpenRead(percorsoZip)
                If archivio.GetEntry("wingabriel-dati.txt") Is Nothing Then Return "il file scelto non è un'esportazione dei dati di WinGabriel."
                For Each voce As ZipArchiveEntry In archivio.Entries
                    Try
                        If String.IsNullOrEmpty(voce.Name) Then Continue For
                        If String.Equals(voce.FullName, "wingabriel-dati.txt", StringComparison.OrdinalIgnoreCase) Then Continue For
                        Dim destinazione As String = Path.GetFullPath(Path.Combine(radice, voce.FullName.Replace("/"c, "\"c)))
                        If Not destinazione.StartsWith(radiceCompleta, StringComparison.OrdinalIgnoreCase) Then Continue For
                        Directory.CreateDirectory(Path.GetDirectoryName(destinazione))
                        Using origine As Stream = voce.Open()
                            Using file As New FileStream(destinazione, FileMode.Create, FileAccess.Write, FileShare.None)
                                origine.CopyTo(file)
                            End Using
                        End Using
                        Try
                            If File.Exists(destinazione & "-wal") Then File.Delete(destinazione & "-wal")
                            If File.Exists(destinazione & "-shm") Then File.Delete(destinazione & "-shm")
                        Catch
                        End Try
                        quanti += 1
                    Catch exVoce As Exception
                        saltati += 1
                        log.ScriviLog("ScambioDati.EstraiZipDati voce (" & voce.FullName & "): " & exVoce.Message)
                    End Try
                Next
            End Using
            Return ""
        Catch ex As Exception
            log.ScriviLog("ScambioDati.EstraiZipDati: " & ex.ToString())
            Return ex.Message
        End Try
    End Function

    ''' <summary>Importa un'esportazione: se è cifrata la apre prima in un file di lavoro. Restituisce "" se è andata bene, altrimenti l'errore.</summary>
    Private Shared Function ImportaDatiEsportati(percorso As String, cifrato As Boolean, password As String, radice As String, ByRef quanti As Integer, ByRef saltati As Integer) As String
        Dim zipLavoro As String = ""
        Try
            If Not cifrato Then Return EstraiZipDati(percorso, radice, quanti, saltati)
            zipLavoro = Path.Combine(Path.GetTempPath(), "wgdati-" & Guid.NewGuid().ToString("N") & ".zip")
            Dim esito As Integer = WGDb.WGCifraturaNativa.DecifraFile(percorso, zipLavoro, password)
            If esito = WGDb.WGCifraturaNativa.ErroreAutenticazione Then Return "la password non è corretta, oppure il file è stato alterato."
            If esito = WGDb.WGCifraturaNativa.ErroreLibreria Then Return "manca la libreria di cifratura."
            If esito <> 0 Then Return "il file non si riesce ad aprire (codice " & esito.ToString(CultureInfo.InvariantCulture) & ")."
            Return EstraiZipDati(zipLavoro, radice, quanti, saltati)
        Catch ex As Exception
            log.ScriviLog("ScambioDati.ImportaDatiEsportati: " & ex.ToString())
            Return ex.Message
        Finally
            Try
                If zipLavoro <> "" AndAlso File.Exists(zipLavoro) Then File.Delete(zipLavoro)
            Catch exPulizia As Exception
                log.ScriviLog("ScambioDati.ImportaDatiEsportati pulizia: " & exPulizia.Message)
            End Try
        End Try
    End Function

    Friend Shared Async Sub ImportaDati()
        If _inCorso Then Return
        _inCorso = True
        Try
            Dim percorsoZip As String = Await ScegliFileAsync("zip", "wgdati")
            If percorsoZip = "" Then Return
            Dim radice As String = CartellaDati()
            If String.IsNullOrWhiteSpace(radice) Then
                Messaggio("Non trovo la cartella dei dati di WinGabriel.")
                Return
            End If
            Dim password As String = ""
            Dim cifrato As Boolean = WGDb.WGCifraturaNativa.TipoFileCifrato(percorsoZip) <> ""
            If cifrato Then
                If WGDb.WGCifraturaNativa.TipoFileCifrato(percorsoZip) = "P" Then
                    password = Await ChiediPasswordAsync("Password con cui sono protetti i dati esportati", False)
                    If String.IsNullOrEmpty(password) Then Return
                End If
            End If
            If Not Await WGDomanda.ChiediAsync("Importando i dati i file con lo stesso nome nella cartella dei dati di WinGabriel vengono sostituiti. Vuoi continuare?") Then Return
            Dim quanti As Integer = 0
            Dim saltati As Integer = 0
            Dim errore As String = Await WGProcessoInCorso.EseguiAsync("Importo i dati di WinGabriel, attendi.",
                                                                       Function() Task.Run(Function() ImportaDatiEsportati(percorsoZip, cifrato, password, radice, quanti, saltati)))
            If errore <> "" Then
                Messaggio("Non è stato possibile importare i dati: " & errore)
                Return
            End If
            Messaggio("Dati importati in " & radice & ": " & quanti & " file." & If(saltati > 0, " " & saltati & " file in uso non sono stati sostituiti.", "") & " Chiudi e riapri WinGabriel per usare i dati importati.")
        Catch ex As Exception
            log.ScriviLog("ScambioDati.ImportaDati: " & ex.ToString())
            Messaggio("Non è stato possibile importare i dati.")
        Finally
            _inCorso = False
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Configurazione
    ' ------------------------------------------------------------------------------------------

    Friend Shared Async Sub EsportaConfigurazione()
        If _inCorso Then Return
        _inCorso = True
        Try
            Dim config As String = WGDb.WGPercorsiApplicazione.PercorsoConfig()
            If Not File.Exists(config) Then
                Messaggio("Non trovo il file della configurazione di WinGabriel.")
                Return
            End If
            Dim cartella As String = Await ScegliCartellaAsync()
            If cartella = "" Then Return
            If Not WGDb.WGDatabase.VerificaCartellaScrivibile(cartella) Then
                Messaggio("In " & cartella & " non riesco a scrivere.")
                Return
            End If
            Dim destinazione As String = Path.Combine(cartella, "configurazione-wingabriel-" & Marca() & ".xml")
            Try
                Using origine As New FileStream(config, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                    Using file As New FileStream(destinazione, FileMode.Create, FileAccess.Write, FileShare.None)
                        origine.CopyTo(file)
                    End Using
                End Using
            Catch ex As Exception
                log.ScriviLog("ScambioDati.EsportaConfigurazione copia: " & ex.ToString())
                Messaggio("Non è stato possibile esportare la configurazione: " & ex.Message)
                Return
            End Try
            Messaggio("Configurazione esportata in " & destinazione & ".")
        Catch ex As Exception
            log.ScriviLog("ScambioDati.EsportaConfigurazione: " & ex.ToString())
            Messaggio("Non è stato possibile esportare la configurazione.")
        Finally
            _inCorso = False
        End Try
    End Sub

    ''' <summary>Tiene una copia della configurazione attuale nella cache, per poterla recuperare.</summary>
    Private Shared Sub CopiaDiSicurezzaConfigurazione(config As String)
        Try
            If Not File.Exists(config) Then Return
            Dim cartella As String = Path.Combine(WGDb.WGPercorsiApplicazione.CartellaCache(), "configurazione")
            Directory.CreateDirectory(cartella)
            File.Copy(config, Path.Combine(cartella, "config-" & Marca() & ".xml"), True)
        Catch ex As Exception
            log.ScriviLog("ScambioDati.CopiaDiSicurezzaConfigurazione: " & ex.Message)
        End Try
    End Sub

    Friend Shared Async Sub ImportaConfigurazione()
        If _inCorso Then Return
        _inCorso = True
        Try
            Dim sorgente As String = Await ScegliFileAsync("xml")
            If sorgente = "" Then Return
            Try
                Dim doc As New System.Xml.XmlDocument()
                doc.XmlResolver = Nothing
                doc.Load(sorgente)
                If doc.DocumentElement Is Nothing OrElse Not String.Equals(doc.DocumentElement.Name, "Configurazioni", StringComparison.Ordinal) Then
                    Messaggio("Il file scelto non è una configurazione di WinGabriel.")
                    Return
                End If
            Catch
                Messaggio("Il file scelto non è una configurazione di WinGabriel.")
                Return
            End Try
            If Not Await WGDomanda.ChiediAsync("Importando la configurazione quella attuale viene sostituita e WinGabriel si riavvia. Vuoi continuare?") Then Return
            Dim config As String = WGDb.WGPercorsiApplicazione.PercorsoConfig()
            Try
                CopiaDiSicurezzaConfigurazione(config)
                Directory.CreateDirectory(Path.GetDirectoryName(config))
                File.Copy(sorgente, config, True)
            Catch ex As Exception
                log.ScriviLog("ScambioDati.ImportaConfigurazione copia: " & ex.ToString())
                Messaggio("Non è stato possibile importare la configurazione: " & ex.Message)
                Return
            End Try
            Messaggio("Configurazione importata. WinGabriel viene riavviato.")
            Riavvia()
        Catch ex As Exception
            log.ScriviLog("ScambioDati.ImportaConfigurazione: " & ex.ToString())
            Messaggio("Non è stato possibile importare la configurazione.")
        Finally
            _inCorso = False
        End Try
    End Sub

    Friend Shared Async Sub ReinizializzaConfigurazione()
        If _inCorso Then Return
        _inCorso = True
        Try
            If Not Await WGDomanda.ChiediAsync("Reinizializzando la configurazione tutte le impostazioni tornano a quelle iniziali, WinGabriel si riavvia e riparte la configurazione guidata. I dati restano dove sono. Vuoi continuare?") Then Return
            Dim config As String = WGDb.WGPercorsiApplicazione.PercorsoConfig()
            Try
                CopiaDiSicurezzaConfigurazione(config)
                Directory.CreateDirectory(Path.GetDirectoryName(config))
                File.WriteAllText(config, "<?xml version=""1.0"" encoding=""UTF-8""?><Configurazioni />", New UTF8Encoding(False))
            Catch ex As Exception
                log.ScriviLog("ScambioDati.ReinizializzaConfigurazione: " & ex.ToString())
                Messaggio("Non è stato possibile reinizializzare la configurazione: " & ex.Message)
                Return
            End Try
            Messaggio("Configurazione reinizializzata. WinGabriel viene riavviato.")
            Riavvia()
        Catch ex As Exception
            log.ScriviLog("ScambioDati.ReinizializzaConfigurazione: " & ex.ToString())
            Messaggio("Non è stato possibile reinizializzare la configurazione.")
        Finally
            _inCorso = False
        End Try
    End Sub

    ''' <summary>Riavvia WinGabriel: esce subito, senza salvare altro, e lo fa ripartire a istanza chiusa.</summary>
    Private Shared Sub Riavvia()
        Try
            If IntegrazioneWindows.EInstallato() Then
                IntegrazioneWindows.RiavviaDallaCopiaInstallata()
                Return
            End If
            Dim info As New ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & start """" """ & Application.ExecutablePath & """")
            info.UseShellExecute = False
            info.CreateNoWindow = True
            Process.Start(info)
        Catch ex As Exception
            log.ScriviLog("ScambioDati.Riavvia: " & ex.Message)
        End Try
        Application.Exit()
        Environment.Exit(0)
    End Sub

End Class
