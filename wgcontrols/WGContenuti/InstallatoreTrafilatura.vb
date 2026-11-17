Imports System.IO
Imports System.IO.Compression
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Diagnostics
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' Installazione e configurazione automatica del motore Python Trafilatura per l'estrazione dei contenuti web.
Public Class InstallatoreTrafilatura
    Private Shared ReadOnly loggerCondiviso As New Logger()
    Private Shared ReadOnly lockInstallazione As New Object()
    Private ReadOnly logger As New Logger()
    Private ReadOnly lockUscita As New Object()
    Private uscitaProcesso As New StringBuilder()

    Public Const ChiaveConfigMotore As String = "motore_estrazione_quotidiani"
    Public Const MotoreInterno As String = "interno"
    Public Const MotoreTrafilatura As String = "trafilatura"

    Private Const IndirizzoPython As String = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-win32.zip"
    Private Const IndirizzoGetPip As String = "https://bootstrap.pypa.io/get-pip.py"
    Private Const NomeCartella As String = "trafilatura"
    Private Const NomeCartellaNuova As String = "trafilatura.installazione"
    Private Const NomeCartellaPrecedente As String = "trafilatura.precedente"
    Private Const TimeoutScaricamentoMinuti As Integer = 15
    Private Const TimeoutPipMinuti As Integer = 10
    Private Const TimeoutPacchettiMinuti As Integer = 30
    Private Const TimeoutProvaSecondi As Integer = 180
    Private Const LunghezzaMinimaProva As Integer = 200
    Private Const RigheErroreRegistrate As Integer = 15

    Friend Shared ReadOnly ScriptEstrazione As String = String.Join(vbLf, New String() {
        "import json",
        "import logging",
        "import sys",
        "",
        "logging.disable(logging.CRITICAL)",
        "",
        "try:",
        "    import trafilatura",
        "    ERRORE_AVVIO = None",
        "except Exception as eccezione:",
        "    trafilatura = None",
        "    ERRORE_AVVIO = ""importazione di trafilatura non riuscita: "" + repr(eccezione)",
        "",
        "",
        "def rispondi(dati):",
        "    sys.stdout.write(json.dumps(dati, ensure_ascii=True) + ""\n"")",
        "    sys.stdout.flush()",
        "",
        "",
        "def estrai_json(html, indirizzo, completo):",
        "    parametri = dict(",
        "        url=indirizzo,",
        "        output_format=""json"",",
        "        with_metadata=True,",
        "        include_comments=False,",
        "        include_tables=True,",
        "        include_images=False,",
        "        include_links=False,",
        "        deduplicate=True,",
        "    )",
        "    try:",
        "        risultato = trafilatura.extract(html, favor_recall=completo, **parametri)",
        "    except TypeError:",
        "        risultato = trafilatura.extract(html, **parametri)",
        "    if not risultato:",
        "        return {}",
        "    try:",
        "        return json.loads(risultato)",
        "    except Exception:",
        "        return {}",
        "",
        "",
        "def estrai_struttura(html, indirizzo, completo):",
        "    parametri = dict(",
        "        url=indirizzo,",
        "        output_format=""xml"",",
        "        include_comments=False,",
        "        include_tables=True,",
        "        include_images=False,",
        "        include_links=False,",
        "        include_formatting=True,",
        "        deduplicate=True,",
        "    )",
        "    try:",
        "        return trafilatura.extract(html, favor_recall=completo, **parametri) or """"",
        "    except TypeError:",
        "        try:",
        "            return trafilatura.extract(html, **parametri) or """"",
        "        except Exception:",
        "            return """"",
        "    except Exception:",
        "        return """"",
        "",
        "",
        "def metadati(html, indirizzo):",
        "    try:",
        "        documento = trafilatura.extract_metadata(html, default_url=indirizzo)",
        "    except Exception:",
        "        return {}",
        "    if documento is None:",
        "        return {}",
        "    risultato = {}",
        "    for chiave in (""title"", ""author"", ""date"", ""sitename"", ""description"", ""language""):",
        "        try:",
        "            valore = getattr(documento, chiave, None)",
        "        except Exception:",
        "            valore = None",
        "        if valore:",
        "            risultato[chiave] = str(valore)",
        "    return risultato",
        "",
        "",
        "def testo_di(dati):",
        "    return dati.get(""text"") or dati.get(""raw_text"") or """"",
        "",
        "",
        "def estrai(richiesta):",
        "    html = richiesta.get(""html"") or """"",
        "    indirizzo = richiesta.get(""url"") or None",
        "    modo = richiesta.get(""modo"") or ""testo""",
        "    if not html.strip():",
        "        return {""ok"": True, ""titolo"": """", ""testo"": """"}",
        "    dati = estrai_json(html, indirizzo, False)",
        "    testo = testo_di(dati)",
        "    completo = False",
        "    if len(testo) < 1500:",
        "        alternativi = estrai_json(html, indirizzo, True)",
        "        testo_alternativo = testo_di(alternativi)",
        "        if len(testo_alternativo) > len(testo) * 1.2:",
        "            dati = alternativi",
        "            testo = testo_alternativo",
        "            completo = True",
        "    base = False",
        "    if len(testo) < 200:",
        "        try:",
        "            _, testo_base, _ = trafilatura.baseline(html)",
        "        except Exception:",
        "            testo_base = """"",
        "        if testo_base and len(testo_base) > len(testo):",
        "            testo = testo_base",
        "            base = True",
        "    meta = metadati(html, indirizzo) if modo == ""completo"" or not dati.get(""title"") else {}",
        "    risposta = {",
        "        ""ok"": True,",
        "        ""titolo"": dati.get(""title"") or meta.get(""title"") or """",",
        "        ""testo"": testo,",
        "        ""autore"": dati.get(""author"") or meta.get(""author"") or """",",
        "        ""data"": dati.get(""date"") or meta.get(""date"") or """",",
        "        ""lingua"": dati.get(""language"") or meta.get(""language"") or """",",
        "        ""sito"": dati.get(""sitename"") or meta.get(""sitename"") or """",",
        "        ""descrizione"": dati.get(""description"") or meta.get(""description"") or """",",
        "        ""completo"": completo,",
        "        ""base"": base,",
        "    }",
        "    if modo == ""completo"" and testo and not base:",
        "        risposta[""xml""] = estrai_struttura(html, indirizzo, completo)",
        "    return risposta",
        "",
        "",
        "def principale():",
        "    if ERRORE_AVVIO is not None:",
        "        rispondi({""pronto"": False, ""errore"": ERRORE_AVVIO})",
        "        return",
        "    rispondi({""pronto"": True, ""versione"": 2})",
        "    for riga in sys.stdin:",
        "        riga = riga.strip()",
        "        if not riga:",
        "            continue",
        "        identificativo = None",
        "        try:",
        "            richiesta = json.loads(riga)",
        "            identificativo = richiesta.get(""id"")",
        "            risposta = estrai(richiesta)",
        "        except Exception as eccezione:",
        "            risposta = {""ok"": False, ""errore"": repr(eccezione)}",
        "        risposta[""id""] = identificativo",
        "        rispondi(risposta)",
        "",
        "",
        "if __name__ == ""__main__"":",
        "    try:",
        "        principale()",
        "    except KeyboardInterrupt:",
        "        pass"
    }) & vbLf

    Public Shared Function CartellaApplicazione() As String
        Try
            ' la radice dell'applicazione: la cartella del programma (o, se in Programmi non si può scrivere, i dati locali dell'utente)
            Return WGDb.WGPercorsiApplicazione.RadiceApplicazione()
        Catch ex As Exception
            loggerCondiviso.ScriviLog($"Eccezione in InstallatoreTrafilatura.CartellaApplicazione: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Public Shared Function CartellaMotore() As String
        Try
            Dim radice As String = CartellaApplicazione()
            If String.IsNullOrEmpty(radice) Then Return String.Empty
            Return Path.Combine(radice, NomeCartella)
        Catch ex As Exception
            loggerCondiviso.ScriviLog($"Eccezione in InstallatoreTrafilatura.CartellaMotore: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function

    Public Shared Function Installato() As Boolean
        Try
            Dim cartella As String = CartellaMotore()
            If String.IsNullOrEmpty(cartella) Then Return False
            If Not File.Exists(Path.Combine(cartella, "python.exe")) Then Return False
            If Not File.Exists(Path.Combine(cartella, "estrai.py")) Then Return False
            Return File.Exists(Path.Combine(cartella, "Lib", "site-packages", "trafilatura", "__init__.py"))
        Catch ex As Exception
            loggerCondiviso.ScriviLog($"Eccezione in InstallatoreTrafilatura.Installato: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function MotoreScelto() As String
        Try
            Dim configurazione As New WGConfig()
            If Not configurazione.EsisteId(ChiaveConfigMotore) Then Return MotoreInterno
            Dim valore As String = configurazione.LeggiValore(ChiaveConfigMotore)
            If String.IsNullOrWhiteSpace(valore) Then Return MotoreInterno
            Return valore.Trim().ToLowerInvariant()
        Catch ex As Exception
            loggerCondiviso.ScriviLog($"Eccezione in InstallatoreTrafilatura.MotoreScelto: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return MotoreInterno
        End Try
    End Function

    Public Shared Function MotoreTrafilaturaAttivo() As Boolean
        Try
            If Not String.Equals(MotoreScelto(), MotoreTrafilatura, StringComparison.OrdinalIgnoreCase) Then Return False
            Return Installato()
        Catch ex As Exception
            loggerCondiviso.ScriviLog($"Eccezione in InstallatoreTrafilatura.MotoreTrafilaturaAttivo: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Function Installa(ByVal token As CancellationToken, ByVal avanzamento As Action(Of String)) As String
        Dim fase As String = "preparazione"
        Dim cartellaNuova As String = String.Empty
        Dim bloccato As Boolean = False
        Try
            bloccato = Monitor.TryEnter(lockInstallazione)
            If Not bloccato Then Return "un'altra installazione del motore Trafilatura è già in corso"

            Dim radice As String = CartellaApplicazione()
            If String.IsNullOrEmpty(radice) Then Throw New InvalidOperationException("cartella dell'applicazione non trovata")
            cartellaNuova = Path.Combine(radice, NomeCartellaNuova)

            Notifica(avanzamento, "Preparazione della cartella")
            EliminaCartella(cartellaNuova)
            EliminaCartella(Path.Combine(radice, NomeCartellaPrecedente))
            Directory.CreateDirectory(cartellaNuova)

            fase = "scaricamento di Python"
            Notifica(avanzamento, "Scaricamento di Python")
            Dim fileZip As String = Path.Combine(cartellaNuova, "python-embed-win32.zip")
            Scarica(IndirizzoPython, fileZip, token)

            fase = "estrazione di Python"
            Notifica(avanzamento, "Estrazione di Python")
            EstraiArchivio(fileZip, cartellaNuova, token)
            File.Delete(fileZip)
            ConfiguraPercorsiPython(cartellaNuova)

            fase = "installazione di pip"
            Notifica(avanzamento, "Installazione di pip")
            Dim fileGetPip As String = Path.Combine(cartellaNuova, "get-pip.py")
            Scarica(IndirizzoGetPip, fileGetPip, token)
            EseguiPython(cartellaNuova, """" & fileGetPip & """ --no-warn-script-location --disable-pip-version-check", TimeoutPipMinuti, token, "installazione di pip")
            File.Delete(fileGetPip)

            fase = "installazione di Trafilatura"
            Notifica(avanzamento, "Installazione di Trafilatura, qualche minuto")
            EseguiPython(cartellaNuova, "-m pip install --prefer-binary --no-warn-script-location --disable-pip-version-check trafilatura", TimeoutPacchettiMinuti, token, "installazione di Trafilatura")
            File.WriteAllText(Path.Combine(cartellaNuova, "estrai.py"), ScriptEstrazione, New UTF8Encoding(False))

            fase = "prova del motore"
            Notifica(avanzamento, "Prova del motore")
            ProvaMotore(cartellaNuova, token)

            fase = "completamento"
            Notifica(avanzamento, "Completamento")
            SostituisciCartella(cartellaNuova, radice)
            Return String.Empty
        Catch ex As OperationCanceledException
            EliminaCartellaSilenziosa(cartellaNuova)
            Return "installazione annullata"
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.Installa durante {fase}: {ex.Message} - StackTrace: {ex.StackTrace}")
            EliminaCartellaSilenziosa(cartellaNuova)
            Return "errore durante " & fase & ", i dettagli sono nel registro"
        Finally
            If bloccato Then Monitor.Exit(lockInstallazione)
        End Try
    End Function

    Private Sub Notifica(ByVal avanzamento As Action(Of String), ByVal testo As String)
        Try
            If avanzamento IsNot Nothing Then avanzamento(testo)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.Notifica: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub Scarica(ByVal indirizzo As String, ByVal destinazione As String, ByVal token As CancellationToken)
        Try
            Dim attuali As SecurityProtocolType = ServicePointManager.SecurityProtocol
            If CInt(attuali) <> 0 AndAlso (attuali And SecurityProtocolType.Tls12) <> SecurityProtocolType.Tls12 Then
                ServicePointManager.SecurityProtocol = attuali Or SecurityProtocolType.Tls12
            End If
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(TimeoutScaricamentoMinuti)
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UtilitaHttp.UserAgent)
                Using risposta As HttpResponseMessage = client.GetAsync(indirizzo, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult()
                    If Not risposta.IsSuccessStatusCode Then
                        Throw New InvalidOperationException($"il server ha risposto {CInt(risposta.StatusCode)} per {indirizzo}")
                    End If
                    Using ingresso As Stream = risposta.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                        Using uscita As New FileStream(destinazione, FileMode.Create, FileAccess.Write, FileShare.None)
                            ingresso.CopyToAsync(uscita, 81920, token).GetAwaiter().GetResult()
                        End Using
                    End Using
                End Using
            End Using
            Dim informazioni As New FileInfo(destinazione)
            If Not informazioni.Exists OrElse informazioni.Length = 0 Then Throw New InvalidOperationException($"file scaricato vuoto: {indirizzo}")
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.Scarica: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Sub

    Private Sub EstraiArchivio(ByVal fileZip As String, ByVal destinazione As String, ByVal token As CancellationToken)
        Try
            Dim radice As String = Path.GetFullPath(destinazione).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Using flusso As FileStream = File.OpenRead(fileZip)
                Using archivio As New ZipArchive(flusso, ZipArchiveMode.Read)
                    For Each voce As ZipArchiveEntry In archivio.Entries
                        token.ThrowIfCancellationRequested()
                        Dim percorso As String = Path.GetFullPath(Path.Combine(destinazione, voce.FullName))
                        If Not percorso.StartsWith(radice, StringComparison.OrdinalIgnoreCase) Then
                            Throw New InvalidDataException("voce non valida nell'archivio di Python: " & voce.FullName)
                        End If
                        If String.IsNullOrEmpty(voce.Name) Then
                            Directory.CreateDirectory(percorso)
                            Continue For
                        End If
                        Directory.CreateDirectory(Path.GetDirectoryName(percorso))
                        Using ingresso As Stream = voce.Open()
                            Using uscita As New FileStream(percorso, FileMode.Create, FileAccess.Write, FileShare.None)
                                ingresso.CopyTo(uscita)
                            End Using
                        End Using
                    Next
                End Using
            End Using
            If Not File.Exists(Path.Combine(destinazione, "python.exe")) Then Throw New InvalidOperationException("python.exe non trovato dopo l'estrazione")
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.EstraiArchivio: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Sub

    Private Sub ConfiguraPercorsiPython(ByVal cartella As String)
        Try
            Dim filePth As String() = Directory.GetFiles(cartella, "python*._pth")
            Dim libreria As String() = Directory.GetFiles(cartella, "python3*.zip")
            If filePth.Length = 0 OrElse libreria.Length = 0 Then Throw New InvalidOperationException("file ._pth o libreria standard non trovati nel pacchetto di Python")
            Dim contenuto As String = Path.GetFileName(libreria(0)) & vbCrLf & "." & vbCrLf & "Lib\site-packages" & vbCrLf & "import site" & vbCrLf
            File.WriteAllText(filePth(0), contenuto, New UTF8Encoding(False))
            Directory.CreateDirectory(Path.Combine(cartella, "Lib", "site-packages"))
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.ConfiguraPercorsiPython: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Sub

    Private Function CreaAvvio(ByVal cartella As String, ByVal argomenti As String) As ProcessStartInfo
        Try
            Dim avvio As New ProcessStartInfo(Path.Combine(cartella, "python.exe"), argomenti)
            avvio.WorkingDirectory = cartella
            avvio.UseShellExecute = False
            avvio.CreateNoWindow = True
            avvio.RedirectStandardOutput = True
            avvio.RedirectStandardError = True
            avvio.StandardOutputEncoding = New UTF8Encoding(False)
            avvio.StandardErrorEncoding = New UTF8Encoding(False)
            If avvio.EnvironmentVariables.ContainsKey("PYTHONHOME") Then avvio.EnvironmentVariables.Remove("PYTHONHOME")
            If avvio.EnvironmentVariables.ContainsKey("PYTHONPATH") Then avvio.EnvironmentVariables.Remove("PYTHONPATH")
            avvio.EnvironmentVariables("PYTHONUTF8") = "1"
            avvio.EnvironmentVariables("PIP_NO_INPUT") = "1"
            Return avvio
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.CreaAvvio: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Function

    Private Sub EseguiPython(ByVal cartella As String, ByVal argomenti As String, ByVal minuti As Integer, ByVal token As CancellationToken, ByVal descrizione As String)
        Try
            SyncLock lockUscita
                uscitaProcesso = New StringBuilder()
            End SyncLock
            Using processo As New Process()
                processo.StartInfo = CreaAvvio(cartella, argomenti)
                AddHandler processo.OutputDataReceived, AddressOf RaccogliUscita
                AddHandler processo.ErrorDataReceived, AddressOf RaccogliUscita
                Try
                    If Not processo.Start() Then Throw New InvalidOperationException("avvio di python.exe non riuscito per " & descrizione)
                    processo.BeginOutputReadLine()
                    processo.BeginErrorReadLine()
                    Dim scadenza As DateTime = DateTime.Now.AddMinutes(minuti)
                    While Not processo.WaitForExit(500)
                        If token.IsCancellationRequested Then
                            TerminaProcesso(processo)
                            token.ThrowIfCancellationRequested()
                        End If
                        If DateTime.Now > scadenza Then
                            TerminaProcesso(processo)
                            Throw New TimeoutException($"{descrizione} non terminata entro {minuti} minuti")
                        End If
                    End While
                    processo.WaitForExit()
                    If processo.ExitCode <> 0 Then
                        Throw New InvalidOperationException($"{descrizione} terminata con codice {processo.ExitCode}: {UltimeRighe()}")
                    End If
                Finally
                    RemoveHandler processo.OutputDataReceived, AddressOf RaccogliUscita
                    RemoveHandler processo.ErrorDataReceived, AddressOf RaccogliUscita
                End Try
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.EseguiPython: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Sub

    Private Sub ProvaMotore(ByVal cartella As String, ByVal token As CancellationToken)
        Try
            Dim paragrafo As New StringBuilder()
            For indice As Integer = 1 To 6
                paragrafo.Append("Questo paragrafo di prova è abbastanza lungo per essere riconosciuto come testo principale dell'articolo. ")
            Next
            Dim html As String = "<html><head><title>Prova</title></head><body><nav>Menu Home Contatti</nav><article><h1>Titolo di prova</h1><p>" &
                paragrafo.ToString() & "</p><p>" & paragrafo.ToString() & "</p></article><footer>Copyright</footer></body></html>"

            Dim richiesta As New Dictionary(Of String, Object)()
            richiesta.Add("id", 1)
            richiesta.Add("url", "https://esempio.it/articolo")
            richiesta.Add("html", html)
            Dim impostazioni As New JsonSerializerSettings With {.StringEscapeHandling = StringEscapeHandling.EscapeNonAscii, .Formatting = Formatting.None}
            Dim riga As String = JsonConvert.SerializeObject(richiesta, impostazioni)

            SyncLock lockUscita
                uscitaProcesso = New StringBuilder()
            End SyncLock
            Dim avvio As ProcessStartInfo = CreaAvvio(cartella, "-X utf8 -u -I """ & Path.Combine(cartella, "estrai.py") & """")
            avvio.RedirectStandardInput = True
            Using processo As New Process()
                processo.StartInfo = avvio
                AddHandler processo.ErrorDataReceived, AddressOf RaccogliUscita
                Try
                    If Not processo.Start() Then Throw New InvalidOperationException("avvio del motore non riuscito durante la prova")
                    processo.BeginErrorReadLine()
                    processo.StandardInput.WriteLine(riga)
                    processo.StandardInput.Close()
                    Dim lettura As Task(Of String) = processo.StandardOutput.ReadToEndAsync()
                    Dim scadenza As DateTime = DateTime.Now.AddSeconds(TimeoutProvaSecondi)
                    While Not lettura.Wait(500)
                        If token.IsCancellationRequested Then
                            TerminaProcesso(processo)
                            token.ThrowIfCancellationRequested()
                        End If
                        If DateTime.Now > scadenza Then
                            TerminaProcesso(processo)
                            Throw New TimeoutException($"il motore non ha risposto alla prova entro {TimeoutProvaSecondi} secondi")
                        End If
                    End While
                    processo.WaitForExit(10000)

                    Dim righe As String() = lettura.Result.Replace(vbCr, String.Empty).Split(New String() {vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    If righe.Length < 2 Then Throw New InvalidOperationException("risposta incompleta del motore durante la prova: " & lettura.Result & " " & UltimeRighe())

                    Dim avviato As JObject = JObject.Parse(righe(0))
                    Dim nodoPronto As JToken = avviato("pronto")
                    If nodoPronto Is Nothing OrElse nodoPronto.Type <> JTokenType.Boolean OrElse Not nodoPronto.Value(Of Boolean)() Then
                        Throw New InvalidOperationException("motore non pronto durante la prova: " & righe(0))
                    End If

                    Dim risposta As JObject = JObject.Parse(righe(1))
                    Dim nodoEsito As JToken = risposta("ok")
                    Dim nodoTesto As JToken = risposta("testo")
                    If nodoEsito Is Nothing OrElse nodoEsito.Type <> JTokenType.Boolean OrElse Not nodoEsito.Value(Of Boolean)() Then
                        Throw New InvalidOperationException("estrazione di prova non riuscita: " & righe(1))
                    End If
                    If nodoTesto Is Nothing OrElse nodoTesto.Type <> JTokenType.String OrElse nodoTesto.Value(Of String)().Length < LunghezzaMinimaProva Then
                        Throw New InvalidOperationException("testo di prova troppo corto: " & righe(1))
                    End If
                Finally
                    RemoveHandler processo.ErrorDataReceived, AddressOf RaccogliUscita
                End Try
            End Using
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.ProvaMotore: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Sub

    Private Sub SostituisciCartella(ByVal cartellaNuova As String, ByVal radice As String)
        Try
            Dim cartella As String = Path.Combine(radice, NomeCartella)
            Dim cartellaPrecedente As String = Path.Combine(radice, NomeCartellaPrecedente)
            EstrattoreTrafilatura.Ferma()
            EliminaCartella(cartellaPrecedente)
            If Directory.Exists(cartella) Then Directory.Move(cartella, cartellaPrecedente)
            Directory.Move(cartellaNuova, cartella)
            EliminaCartellaSilenziosa(cartellaPrecedente)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.SostituisciCartella: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Sub

    Private Sub EliminaCartella(ByVal cartella As String)
        Try
            If String.IsNullOrEmpty(cartella) OrElse Not Directory.Exists(cartella) Then Return
            For Each percorsoFile As String In Directory.GetFiles(cartella, "*", SearchOption.AllDirectories)
                File.SetAttributes(percorsoFile, FileAttributes.Normal)
            Next
            Directory.Delete(cartella, True)
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.EliminaCartella: {ex.Message} - StackTrace: {ex.StackTrace}")
            Throw
        End Try
    End Sub

    Private Sub EliminaCartellaSilenziosa(ByVal cartella As String)
        Try
            EliminaCartella(cartella)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.EliminaCartellaSilenziosa per {cartella}: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub TerminaProcesso(ByVal processo As Process)
        Try
            If processo IsNot Nothing AndAlso Not processo.HasExited Then processo.Kill()
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.TerminaProcesso: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Sub RaccogliUscita(ByVal sender As Object, ByVal e As DataReceivedEventArgs)
        Try
            If e Is Nothing OrElse e.Data Is Nothing Then Return
            SyncLock lockUscita
                uscitaProcesso.AppendLine(e.Data)
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.RaccogliUscita: {ex.Message} - StackTrace: {ex.StackTrace}")
        End Try
    End Sub

    Private Function UltimeRighe() As String
        Try
            Dim testo As String
            SyncLock lockUscita
                testo = uscitaProcesso.ToString()
            End SyncLock
            Dim righe As String() = testo.Replace(vbCr, String.Empty).Split(New String() {vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim inizio As Integer = Math.Max(0, righe.Length - RigheErroreRegistrate)
            Dim scelte As New List(Of String)()
            For indice As Integer = inizio To righe.Length - 1
                scelte.Add(righe(indice))
            Next
            Return String.Join(" | ", scelte)
        Catch ex As Exception
            logger.ScriviLog($"Eccezione in InstallatoreTrafilatura.UltimeRighe: {ex.Message} - StackTrace: {ex.StackTrace}")
            Return String.Empty
        End Try
    End Function
End Class