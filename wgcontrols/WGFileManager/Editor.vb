Imports System.Diagnostics.Eventing.Reader
Imports wgcontrols

Public Class editor
    Private log As New Logger()
    Private menusalvataggiogiaaperto As Boolean = False
    Private editorfile As New EditorSystem
    Private WithEvents campo As WGCampo
    Private WithEvents sceltadirectory As WGFileDialog
    Private WithEvents campi As Campi
    Private percorsosalvataggio As String = ""
    Private testosalvato As String = ""
    Private WithEvents domanda As WGDomanda
    Private isWordMode As Boolean = False
    Private _solaLettura As Boolean = False
    Private _chiudiCampoDopoSalvataggio As Boolean = False

    Private menuFormati As WGMenuLista
    Private _tempDirectory As String = ""
    Private _nomeFileTemporaneo As String = ""
    Private _formatoScelto As String = ""
    Private WithEvents _domandaFormattazione As WGDomanda
    Private _tipoEsportazione As String = ""

    Private WithEvents _domandaTxt As WGDomanda
    Private WithEvents _domandaPdfImport As WGDomanda
    Private _percorsoPdfDaImportare As String = ""
    Private _percorsoFileDaAprire As String = ""

    ''' <summary>
    ''' Sollevato ogni volta che un file di testo viene scritto con successo su disco
    ''' (sia con "Salva" sia con "Salva con nome"). L'editor non sa nulla dell'origine
    ''' del file: è compito di chi lo usa decidere se e come sincronizzare altrove.
    ''' </summary>
    Public Event FileSalvato(percorsoLocale As String)

    Public Sub avvia(Optional percorsofile As String = "", Optional testo As String = "", Optional solalettura As Boolean = False)
        Try
            _solaLettura = solalettura
            If percorsofile <> "" Then
                Dim estensione As String = System.IO.Path.GetExtension(percorsofile).ToLower()
                If solalettura Then
                    ApriFileDiretto(percorsofile, estensione, True)
                Else
                    MostraMenuAperturaFile(percorsofile, estensione)
                End If
            ElseIf testo <> "" Then
                If campo IsNot Nothing Then
                    campo.Dispose()
                    campo = Nothing
                End If
                campo = New WGCampo()
                isWordMode = False
                testosalvato = testo
                campo.init("Editor", testo, True, False)
                ImpostaOpzioniEAvvia()
            Else
                Dim menu As New WGMenu()
                menu.aggiungi("Nuovo testo", AddressOf nuovotesto)
                menu.aggiungi("Nuovo Documento Di Microsoft Word", AddressOf word)
                menu.Avvia("Menù creazione nuovo testo", True)
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuAperturaFile(percorsofile As String, estensione As String)
        Try
            _percorsoFileDaAprire = percorsofile
            _percorsoPdfDaImportare = percorsofile

            Dim menuApertura As New WGMenu()
            menuApertura.aggiungi("Apri in sola lettura", AddressOf ApriFileSolaLettura)
            menuApertura.aggiungi("Apri in modifica", AddressOf ApriFileModifica)
            menuApertura.Avvia("scegli come aprire il file", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApriFileSolaLettura()
        Try
            _solaLettura = True
            Dim estensione As String = System.IO.Path.GetExtension(_percorsoFileDaAprire).ToLower()
            ApriFileDiretto(_percorsoFileDaAprire, estensione, True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApriFileModifica()
        Try
            _solaLettura = False
            Dim estensione As String = System.IO.Path.GetExtension(_percorsoFileDaAprire).ToLower()
            If estensione = ".pdf" Then
                _domandaPdfImport = New WGDomanda()
                _domandaPdfImport.avvia("Verrà importato il file PDF, ma dovrà essere creato un nuovo documento. I salvataggi successivi creeranno un nuovo file senza sovrascrivere l'originale. Vuoi procedere?", True)
            Else
                ApriFileDiretto(_percorsoFileDaAprire, estensione, False)
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ApriFileDiretto(percorsofile As String, estensione As String, solalettura As Boolean)
        Try
            If campo IsNot Nothing Then
                campo.Dispose()
                campo = Nothing
            End If
            campo = New WGCampo()

            If estensione = ".docx" Then
                isWordMode = True
                percorsosalvataggio = percorsofile
                campo.CaricaWord("Editor", percorsofile, solalettura)
                ImpostaOpzioniEAvvia()
                RilevaLinguaFileEOffriTraduzione(percorsofile)
            ElseIf estensione = ".pdf" Then
                percorsosalvataggio = ""
                isWordMode = True
                campo.ApriPdf(percorsofile, "Editor", solalettura)
                ImpostaOpzioniEAvvia()
            ElseIf estensione = ".rtf" Then
                percorsosalvataggio = percorsofile
                isWordMode = False
                If solalettura Then
                    campo.Initrtf("Editor", "", True, True)
                End If
                campo.LoadFile(percorsofile)
                ImpostaOpzioniEAvvia()
                RilevaLinguaFileEOffriTraduzione(percorsofile)
            Else
                percorsosalvataggio = percorsofile
                isWordMode = False
                Dim testocampo As String = ""
                editorfile.LeggiFile(percorsofile, testocampo)
                testosalvato = testocampo
                campo.init("Editor", testocampo, True, solalettura)
                ImpostaOpzioniEAvvia()
                RilevaLinguaFileEOffriTraduzione(percorsofile)
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    ''' <summary>Se il rilevamento della lingua è attivo e il file (non nuovo, non pdf) risulta in una lingua diversa da
    ''' quella di destinazione, offre la scelta: tradurlo, leggerlo nella lingua originale (cambiando voce e tabella
    ''' braille da sole, se le impostazioni lo prevedono) o lasciarlo com'è. Pensato per chi studia in più lingue.</summary>
    Private Async Sub RilevaLinguaFileEOffriTraduzione(percorsofile As String)
        Try
            If Not WGTraduttore.RilevamentoAttivo OrElse Not WGTraduttore.FileTraducibile(percorsofile) Then Return
            Dim testo As String = WGTraduttore.EstraiTestoDaFile(percorsofile)
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim lingua As String = Await WGTraduttore.RilevaLinguaAsync(testo)
            If lingua = "" OrElse WGTraduttore.StessaLingua(lingua, WGTraduttore.LinguaDestinazione) Then Return
            If WGTraduttore.LinguaEsclusa(lingua) Then
                WGTraduttore.ApplicaLinguaLettura(lingua)
                Return
            End If
            Dim nomeFile As String = System.IO.Path.GetFileName(percorsofile)
            Dim menu As New WGMenu()
            menu.aggiungi("Traduci questo documento", Sub(s, e) WGTraduttore.TraduciEMostra(testo, "Traduzione di " & nomeFile, percorsofile))
            menu.aggiungi("Leggilo in lingua originale", Sub(s, e) WGTraduttore.ApplicaLinguaLettura(lingua))
            menu.aggiungi("Non tradurre", Sub(s, e)
                                           End Sub)
            menu.avvia("Documento " & WGTraduttore.ALingua(lingua) & ": tradurlo " & WGTraduttore.ALingua(WGTraduttore.LinguaDestinazione) & "?", True)
        Catch ex As Exception
            log.ScriviLog("editor.RilevaLinguaFileEOffriTraduzione: " & ex.Message)
        End Try
    End Sub

    Private WithEvents _timerAvvioPdf As System.Windows.Forms.Timer

    Private Sub _domandaPdfImport_rispostadomanda(risposta As DialogResult) Handles _domandaPdfImport.rispostadomanda
        Try
            If risposta = DialogResult.Yes Then
                _timerAvvioPdf = New System.Windows.Forms.Timer()
                _timerAvvioPdf.Interval = 50
                _timerAvvioPdf.Start()
            Else
                If campo IsNot Nothing Then
                    campo.chiudi()
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub _timerAvvioPdf_Tick(sender As Object, e As EventArgs) Handles _timerAvvioPdf.Tick
        Try
            _timerAvvioPdf.Stop()
            _timerAvvioPdf.Dispose()
            _timerAvvioPdf = Nothing

            If campo IsNot Nothing Then
                campo.Dispose()
                campo = Nothing
            End If
            campo = New WGCampo()
            percorsosalvataggio = ""
            isWordMode = True
            campo.ApriPdf(_percorsoPdfDaImportare, "Editor", False)
            ImpostaOpzioniEAvvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub ImpostaOpzioniEAvvia()
        Try
            If isWordMode Then
                campo.WordSolaLettura = _solaLettura
            End If

            If Not _solaLettura Then
                campo.aggiungiopzione("Salva", AddressOf salva)
                campo.aggiungiopzione("Salva con nome", AddressOf salvanome)
            End If

            If isWordMode Then
                campo.aggiungiopzione("Menù esportazione documento word", AddressOf apriMenuEsportazione)
            End If
            campo.aggiungiopzione("Apri nuovo testo", AddressOf ApriNuovoTesto)

            ' Ogni testo è un documento dell'editor: Control Tab passa da uno all'altro.
            campo.EDocumento = True
            AggiornaNomeDocumento()

            AddHandler campo.chiusura, AddressOf chiuditesto
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Shared _nuoviTesti As Integer = 0

    Private Sub AggiornaNomeDocumento()
        Try
            If campo Is Nothing Then Return
            If percorsosalvataggio <> "" Then
                campo.NomeDocumento = System.IO.Path.GetFileName(percorsosalvataggio)
            ElseIf campo.NomeDocumento = "" OrElse campo.NomeDocumento = "Editor" Then
                _nuoviTesti += 1
                campo.NomeDocumento = If(isWordMode, "Nuovo documento ", "Nuovo testo ") & _nuoviTesti
            End If
        Catch ex As Exception
            log.ScriviLog("editor.AggiornaNomeDocumento: " & ex.Message)
        End Try
    End Sub

    ' Prima di aprire un altro testo si controlla la memoria: i documenti Word grandi ne usano molta.
    ' Nessun limite al numero di testi, ma sotto una soglia di memoria libera non se ne aprono altri.
    Private Shared Function MemoriaSufficiente() As Boolean
        Try
            Dim info As New Microsoft.VisualBasic.Devices.ComputerInfo()
            Dim liberaMb As Double = info.AvailablePhysicalMemory / 1048576.0
            If liberaMb < 700 Then Return False
            ' In un processo a 32 bit il limite vero è lo spazio di indirizzi, non la memoria del computer.
            If Not Environment.Is64BitProcess Then
                Dim usataMb As Double = Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0
                If usataMb > 1400 Then Return False
            End If
            Return True
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("editor.MemoriaSufficiente: " & ex.Message)
            Return True
        End Try
    End Function

    Private Sub ApriNuovoTesto()
        Try
            If Not MemoriaSufficiente() Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("La memoria del computer sta per esaurirsi: non è possibile aprire un altro testo. Chiudere qualche testo o programma e riprovare.")
                messaggio.ShowDialog()
                Return
            End If
            Dim menu As New WGMenu()
            menu.aggiungi("Nuovo testo", Sub(s, e) Call New editor().ApriTestoVuoto(False))
            menu.aggiungi("Nuovo documento di Microsoft Word", Sub(s, e) Call New editor().ApriTestoVuoto(True))
            menu.aggiungi("Apri un testo esistente", Sub(s, e) ScegliTestoEsistente())
            menu.avvia("Apri nuovo testo", True)
        Catch ex As Exception
            log.ScriviLog("editor.ApriNuovoTesto: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Apre un testo vuoto con tutte le funzioni dell'editor, compresi Salva e Salva con nome.</summary>
    Public Sub ApriTestoVuoto(documentoWord As Boolean)
        If documentoWord Then word() Else nuovotesto()
    End Sub

    Private Sub ScegliTestoEsistente()
        Try
            Dim dialogo As New WGFileDialog()
            dialogo.MultiselezioneConsentita = False
            dialogo.allowedExtensions.AddRange({".txt", ".docx", ".rtf", ".pdf", ".md", ".odt", ".html", ".htm", ".xml", ".json", ".log", ".ini", ".csv"})
            Dim apri As Action(Of String) = Sub(percorso As String)
                                                If String.IsNullOrEmpty(percorso) OrElse Not WGRete.EsisteFile(percorso, 8000) Then Return
                                                Dim nuovo As New editor()
                                                nuovo.avvia(percorso)
                                            End Sub
            AddHandler dialogo.singolaselezione, Sub(elemento As String) apri(elemento)
            AddHandler dialogo.selezioneterminata, Sub(elementi As List(Of String))
                                                       If elementi IsNot Nothing AndAlso elementi.Count > 0 Then apri(elementi(0))
                                                   End Sub
            dialogo.Avvia()
        Catch ex As Exception
            log.ScriviLog("editor.ScegliTestoEsistente: " & ex.Message)
        End Try
    End Sub

    Private Sub apriMenuEsportazione()
        Try
            Dim menuE As New WGMenu()
            menuE.aggiungi("Esporta in html", AddressOf esportahtml)
            menuE.aggiungi("Esporta in pdf", AddressOf esportapdf)
            menuE.aggiungi("Esporta in rtf", AddressOf Esportartf)
            menuE.aggiungi("Esporta in txt", AddressOf Esportatxt)
            menuE.aggiungi("Esporta in md", AddressOf Esportamd)
            menuE.aggiungi("Esporta in odt", AddressOf Esportaodt)
            menuE.aggiungi("Esporta in epub", AddressOf Esportaepub)
            menuE.Avvia("Menù esportazione documento word", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub nuovotesto()
        Try
            If campo IsNot Nothing Then
                campo.Dispose()
                campo = Nothing
            End If
            campo = New WGCampo()
            isWordMode = False
            testosalvato = ""
            _solaLettura = False
            campo.init("Editor", "", True, False)
            ImpostaOpzioniEAvvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub word()
        Try
            If campo IsNot Nothing Then
                campo.Dispose()
                campo = Nothing
            End If
            campo = New WGCampo()
            isWordMode = True
            _solaLettura = False
            campo.NuovoWord("Editor", False)
            ImpostaOpzioniEAvvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub salva()
        Try
            If _solaLettura Then Return
            If percorsosalvataggio = "" Then
                salvanome()
            Else
                If isWordMode Then
                    Try
                        campo.SalvaWord(percorsosalvataggio)
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Salvataggio completato")
                        messaggio.ShowDialog()
                    Catch ex As Exception
                        log.ScriviLog(ex.Message)
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Durante il salvataggio si è verificato un errore, prego controllare il log.")
                        messaggio.ShowDialog()
                    End Try
                Else
                    Dim risultatosalvataggio As Boolean
                    Dim testoDaSalvare As String = ""

                    Try
                        If percorsosalvataggio.ToLower().EndsWith(".rtf") Then
                            Try
                                campo.SaveFile(percorsosalvataggio)
                            Catch
                                testoDaSalvare = campo.ottienitesto()
                            End Try
                            If String.IsNullOrEmpty(testoDaSalvare) Then testoDaSalvare = campo.ottienitesto()
                        Else
                            testoDaSalvare = campo.ottienitesto()
                        End If
                    Catch
                        testoDaSalvare = campo.ottienitesto()
                    End Try

                    risultatosalvataggio = editorfile.ScriviFile(percorsosalvataggio, testoDaSalvare)

                    If risultatosalvataggio = True Then
                        testosalvato = campo.ottienitesto()
                        AggiornaNomeDocumento()
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Salvataggio completato")
                        messaggio.ShowDialog()
                        RaiseEvent FileSalvato(percorsosalvataggio)
                    Else
                        Dim messaggio As New WGMessaggi()
                        messaggio.Messaggio("Durante il salvataggio si è verificato un errore, prego controllare il log presente nella root dell'applicazione o nella cartella dati")
                        messaggio.ShowDialog()
                    End If
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub salvanome()
        Try
            If _solaLettura Then Return
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub selezioneterminata(e As String, Optional nomeSuggerito As String = "")
        Try
            _tempDirectory = e
            campi = New Campi()
            Dim promptNome As String = "Scrivi il nome che vuoi dare al file"
            If _tipoEsportazione = "HTML" Then
                promptNome = "Scrivi il nome che vuoi dare al file HTML"
            ElseIf _tipoEsportazione = "PDF" Then
                promptNome = "Scrivi il nome che vuoi dare al file PDF"
            ElseIf _tipoEsportazione = "RTF" Then
                promptNome = "Scrivi il nome che vuoi dare al file RTF"
            ElseIf _tipoEsportazione = "TXT" Then
                promptNome = "Scrivi il nome che vuoi dare al file di testo"
            ElseIf _tipoEsportazione = "MD" Then
                promptNome = "Scrivi il nome che vuoi dare al file Markdown"
            ElseIf _tipoEsportazione = "ODT" Then
                promptNome = "Scrivi il nome che vuoi dare al file ODT"
            ElseIf _tipoEsportazione = "EPUB" Then
                promptNome = "Scrivi il nome che vuoi dare al file EPUB"
            End If

            campi.Aggiungi(promptNome, "v.filename", True,, nomeSuggerito)
            AddHandler campi.Fine, AddressOf chiudicampi
            campi.avvia()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub campo_RichiediNuovoPercorsoSalvataggio(nomeSuggerito As String) Handles campo.RichiediNuovoPercorsoSalvataggio
        Try
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, Sub(percorsoScelto As String) selezioneterminata(percorsoScelto, nomeSuggerito)
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            log.ScriviLog("campo_RichiediNuovoPercorsoSalvataggio eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub esportahtml()
        Try
            _tipoEsportazione = "HTML"
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            _tipoEsportazione = ""
            log.ScriviLog(ex.Message)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Errore nell'apertura della finestra di esportazione: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Sub esportapdf()
        Try
            _tipoEsportazione = "PDF"
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            _tipoEsportazione = ""
            log.ScriviLog(ex.Message)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Errore nell'apertura della finestra di esportazione: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Sub Esportartf()
        Try
            _tipoEsportazione = "RTF"
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            _tipoEsportazione = ""
            log.ScriviLog(ex.Message)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Errore nell'apertura della finestra di esportazione: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Sub Esportatxt()
        Try
            _domandaTxt = New WGDomanda()
            _domandaTxt.avvia("Sei sicuro di voler esportare in formato testo? Tutta la formattazione andrà persa in modo definitivo.", True)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub Esportamd()
        Try
            _tipoEsportazione = "MD"
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            _tipoEsportazione = ""
            log.ScriviLog(ex.Message)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Errore nell'apertura della finestra di esportazione: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Sub Esportaodt()
        Try
            _tipoEsportazione = "ODT"
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            _tipoEsportazione = ""
            log.ScriviLog(ex.Message)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Errore nell'apertura della finestra di esportazione: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Sub Esportaepub()
        Try
            _tipoEsportazione = "EPUB"
            sceltadirectory = New WGFileDialog()
            AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
            sceltadirectory.MultiselezioneConsentita = False
            sceltadirectory.Avvia()
        Catch ex As Exception
            _tipoEsportazione = ""
            log.ScriviLog(ex.Message)
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Errore nell'apertura della finestra di esportazione: " & ex.Message)
            messaggio.ShowDialog()
        End Try
    End Sub

    Private Sub _domandaTxt_rispostadomanda(risposta As DialogResult) Handles _domandaTxt.rispostadomanda
        If risposta = DialogResult.Yes Then
            Try
                _tipoEsportazione = "TXT"
                sceltadirectory = New WGFileDialog()
                AddHandler sceltadirectory.singolaselezione, AddressOf selezioneterminata
                sceltadirectory.MultiselezioneConsentita = False
                sceltadirectory.Avvia()
            Catch ex As Exception
                _tipoEsportazione = ""
                log.ScriviLog(ex.Message)
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Errore nell'apertura della finestra di esportazione: " & ex.Message)
                messaggio.ShowDialog()
            End Try
        End If
    End Sub

    Private Sub chiudicampi()
        Try
            Dim nome As String = campi.RecuperaValore("v.filename")
            campi = Nothing

            If String.IsNullOrWhiteSpace(nome) Then
                _tipoEsportazione = ""
                Return
            End If

            If _tipoEsportazione <> "" Then
                Dim tipoCurrent As String = _tipoEsportazione
                _tipoEsportazione = ""

                Dim estensione As String = "." & tipoCurrent.ToLower()
                If Not nome.ToLower().EndsWith(estensione) AndAlso Not (tipoCurrent = "HTML" AndAlso nome.ToLower().EndsWith(".htm")) Then
                    nome &= estensione
                End If

                Dim percorsoOut As String = System.IO.Path.Combine(_tempDirectory, nome)
                Try
                    If tipoCurrent = "HTML" Then
                        campo.EsportaHtml(percorsoOut)
                    ElseIf tipoCurrent = "PDF" Then
                        campo.EsportaPdf(percorsoOut)
                    ElseIf tipoCurrent = "RTF" Then
                        campo.EsportaRtf(percorsoOut)
                    ElseIf tipoCurrent = "TXT" Then
                        campo.EsportaTxt(percorsoOut)
                    ElseIf tipoCurrent = "MD" Then
                        campo.EsportaMd(percorsoOut)
                    ElseIf tipoCurrent = "ODT" Then
                        campo.EsportaOdt(percorsoOut)
                    ElseIf tipoCurrent = "EPUB" Then
                        campo.EsportaEpub(percorsoOut)
                    End If
                Catch ex As Exception
                    log.ScriviLog(ex.Message)
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Durante l'esportazione si è verificato un errore, prego controllare il log.")
                    messaggio.ShowDialog()
                End Try
                Return
            End If

            If isWordMode Then
                Dim percorsoBase As String = System.IO.Path.Combine(_tempDirectory, nome)
                Dim chiudiDopo As Boolean = _chiudiCampoDopoSalvataggio
                _chiudiCampoDopoSalvataggio = False
                campo.SalvaDocumento(percorsoBase, chiudiDopo)
            Else
                _nomeFileTemporaneo = nome
                If menuFormati IsNot Nothing Then
                    menuFormati.abbandona()
                End If
                menuFormati = New WGMenuLista()
                menuFormati.impostaTitolo("Scegli il formato di salvataggio")
                menuFormati.multiselezioneConsentita = False
                menuFormati.aggiungiElemento("Testo normale (.txt)", ".txt")
                menuFormati.aggiungiElemento("Rich Text Format (.rtf)", ".rtf")
                menuFormati.aggiungiElemento("Pagina Web (.html)", ".html")
                menuFormati.aggiungiElemento("Visual Basic (.vb)", ".vb")
                menuFormati.aggiungiElemento("C# (.cs)", ".cs")
                menuFormati.aggiungiElemento("Log (.log)", ".log")
                menuFormati.aggiungiElemento("File INI (.ini)", ".ini")
                AddHandler menuFormati.ElementoSelezionato, AddressOf FormatoSelezionato
                AddHandler menuFormati.escPremuto, AddressOf AnnullaSceltaFormato
                menuFormati.avvia()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub FormatoSelezionato()
        Try
            Dim ids As List(Of String) = menuFormati.ottieniElementi()
            If ids.Count > 0 Then
                _formatoScelto = ids(0)
                menuFormati.abbandona()

                If _formatoScelto <> ".rtf" AndAlso HaFormattazioneRtf() Then
                    _domandaFormattazione = New WGDomanda()
                    _domandaFormattazione.avvia("Il documento sembra contenere formattazione RTF. Salvando in " & _formatoScelto & " si perderà la formattazione. Vuoi salvare comunque in " & _formatoScelto & "?", True)
                Else
                    SalvaTestoFormato()
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub AnnullaSceltaFormato()
        Try
            If menuFormati IsNot Nothing Then
                menuFormati.abbandona()
                menuFormati = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Function HaFormattazioneRtf() As Boolean
        Try
            Return campo.HaFormattazioniAttive()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return False
        End Try
    End Function

    Private Sub _domandaFormattazione_rispostadomanda(risposta As DialogResult) Handles _domandaFormattazione.rispostadomanda
        Try
            If risposta = DialogResult.Yes Then
                SalvaTestoFormato()
            ElseIf risposta = DialogResult.No Then
                _formatoScelto = ".rtf"
                SalvaTestoFormato()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub SalvaTestoFormato()
        Try
            If Not _nomeFileTemporaneo.ToLower().EndsWith(_formatoScelto) Then
                Dim ext As String = System.IO.Path.GetExtension(_nomeFileTemporaneo)
                If Not String.IsNullOrEmpty(ext) Then
                    _nomeFileTemporaneo = _nomeFileTemporaneo.Substring(0, _nomeFileTemporaneo.Length - ext.Length)
                End If
                _nomeFileTemporaneo &= _formatoScelto
            End If

            percorsosalvataggio = System.IO.Path.Combine(_tempDirectory, _nomeFileTemporaneo)
            salva()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub chiuditesto(sender As Object, e As EventArgs)
        Try
            campo.SopprimiChiusuraAutomatica = True

            If _solaLettura Then
                campo.chiudi()
                Return
            End If

            Dim ciSonoModifiche As Boolean = False

            If isWordMode Then
                ciSonoModifiche = campo.HaModifiche()
            Else
                ciSonoModifiche = (testosalvato <> campo.ottienitesto())
            End If

            If Not ciSonoModifiche Then
                campo.chiudi()
            Else
                If menusalvataggiogiaaperto = True Then
                    Exit Sub
                Else
                    menusalvataggiogiaaperto = True
                    domanda = New WGDomanda
                    domanda.avvia("Ci sono delle modifiche al testo non salvate, si vogliono salvare?", True)
                End If
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub campo_SalvataggioCompletato(percorso As String) Handles campo.SalvataggioCompletato
        Try
            percorsosalvataggio = percorso
            AggiornaNomeDocumento()
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Private Sub risposta(rispostadom As DialogResult) Handles domanda.rispostadomanda
        Try
            menusalvataggiogiaaperto = False
            If rispostadom = DialogResult.Yes Then
                If percorsosalvataggio = "" Then
                    _chiudiCampoDopoSalvataggio = True
                    salvanome()
                Else
                    salva()
                    campo.chiudi()
                End If
            ElseIf rispostadom = DialogResult.No Then
                campo.chiudi()
            End If
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub
End Class