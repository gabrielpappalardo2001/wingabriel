Imports System.Collections.Generic
Imports System.Collections.Specialized
Imports System.ComponentModel
Imports System.Configuration
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Security
Imports System.Security.AccessControl
Imports System.Security.Permissions
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Windows.Forms.VisualStyles.VisualStyleElement
Imports wgcontrols
Imports WGFtp
Imports WGDati
Imports WGPlayerMpv
Imports WGStampante

Public Class MenuFile
    Private Enum TipoElencoServerFtp
        Preferito
        Recente
        Salvato
    End Enum

    Private successo As Boolean
    Private errore As String
    Private elementi As New List(Of String)
    Private id As String
    Private elimina As Boolean = True
    Private processoterminato As Boolean = False
    Private lista As New WGMenuLista()
    Private currentDirectory As String
    Private ricaricadirectory As String
    Private fileecartelle As New List(Of String)()
    Private taglia As Boolean = False
    Private copia As Boolean = False
    Private drive As List(Of String)
    Private history As New List(Of NavigazioneStato)()
    Private _percorsoDaEvidenziareDopoCaricamento As String = ""
    Private WithEvents campocartella As WGCampoTesto
    Private domanda As WGDomanda
    Private domandaImprontaFtp As WGDomanda
    Private domandaImpostazioniFtp As WGDomanda
    Private driveListCache As New List(Of String)()
    Private suonoAttesa As New WGSuoni()
    Private config As WGConfig = New WGConfig()
    Private favoritesManager As Preferiti
    Private recentsManager As Recenti
    Private uiContext As SynchronizationContext
    Private campi As Campi
    Private currentExtensionFilter As String = ""
    Private processoincorso As WGProcessoInCorso
    Private isDriveSelectionMode As Boolean = False
    Private isExtensionSelectionMode As Boolean = False
    Private logic As New CLSFile()
    Private Shared log As New wgcontrols.Logger()
    Private ordinamentoCorrente As CLSFile.OrdineDiVisualizzazione = CLSFile.OrdineDiVisualizzazione.Nome
    Private _tempTagFile As String
    Private _tempTagData As CLSTagMp3.DatiTag
    Private _tempZipDestinazione As String
    Private _tempZipItems As List(Of String)
    Private _tempSearchTagFile As String
    Private _tempPreferitoPath As String
    Private _tempFileDaAprire As String
    Private wgInternalList As New WGList()
    Private _editorAperto As editor
    Private _dialogoConversioneMp3 As WGFileDialog
    Private _campiNomeMp3 As Campi
    Private _cartellaMp3 As String = ""
    Private _fileMp3DaConvertire As New List(Of String)()
    Private _tempTagFiles As New List(Of String)()
    Private _tempPreferitiMultipli As New List(Of String)()
    Private _listaInformazioni As WGMenuLista
    Private _dirSalvataInformazioni As String = ""
    Private _listaInformazioniFtp As WGMenuLista
    Private _percorsoInformazioniFtp As String = ""
    Private _listaPermessiFtp As WGMenuLista
    Private _tempElementiPermessiFtp As New List(Of InfoElementoFtp)()
    Private _ctsCaricamentoDirectory As CancellationTokenSource
    Private _generazioneCaricamentoDirectory As Long = 0
    Private _processoAiFile As WGProcessoInCorso
    Private _campoTestoAiFile As WGCampo
    Private _campiRiscrittura As Campi
    Private _fileRiscrittura As List(Of String)
    Private _menuModelloRiscrittura As WGMenuLista
    Private _processoModelliRiscrittura As WGProcessoInCorso
    Private _providerRiscrittura As WGAi.ProviderAi? = Nothing
    Private _modelloRiscrittura As String = ""
    Private ReadOnly EstensioniImmagineAi As String() = {".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"}
    Private ReadOnly EstensioniAudioTrascrivibili As String() = {".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".opus", ".aif", ".aiff", ".mp2", ".amr"}

    ' Tracciamento dell'origine FTP del file attualmente aperto nell'editor, per poterlo
    ' ricaricare aggiornato sul server dopo ogni salvataggio (vedi OnFileSalvatoEditor).
    Private _ftpServerOrigineEditor As InfoServerFtp
    Private _ftpCartellaRemotaOrigineEditor As String = ""
    Private _ftpPercorsoLocaleTracciatoEditor As String = ""
    Private domandaUploadEditor As WGDomanda
    Private _percorsoLocaleUploadInSospeso As String = ""

    Private isFtpServerListMode As Boolean = False
    Private ftpEngine As New CLSFtp()
    Private isFtpBrowseMode As Boolean = False
    Private currentServerFtp As InfoServerFtp
    Private currentFtpSession As SessioneFtp
    Private currentFtpPath As String = "/"
    Private currentFtpExtensionFilter As String = ""
    Private ftpHistory As New List(Of String)()
    Private ftpServerCache As New Dictionary(Of String, InfoServerFtp)()
    Private ftpElementCache As New Dictionary(Of String, InfoElementoFtp)()
    Private _tempPercorsiFtpSelezionati As New List(Of String)()
    Private _tempChiediSalvataggioServerFtp As Boolean = False
    Private _tempEsitoImprontaSftp As Boolean = True
    Private _tempAvvisoImprontaSftp As String = ""
    Private _tempImprontaSftpCorrente As String = ""
    Private _tempServerFtpInModifica As InfoServerFtp
    Private _tempTipoElencoServerFtpInModifica As TipoElencoServerFtp = TipoElencoServerFtp.Salvato
    Private _currentServerFtpTipoElenco As TipoElencoServerFtp? = Nothing
    Private _tempServerFtpTogglePreferito As InfoServerFtp
    Private isFtpElencoServerMode As Boolean = False
    Private _tempTokenOpzioniContestualiFtp As Long = 0
    Private _percorsoDaEvidenziareDopoCaricamentoFtp As String = ""
    Private _tempServerFtpSenzaPassword As InfoServerFtp
    ' scelto dalle opzioni del campo della password: dopo la connessione il server si salva, con la password, nella configurazione
    Private _tempSalvaServerFtpConPassword As Boolean = False
    Private _tempNuovoServerFtp As InfoServerFtp
    Private _origineElencoServerFtp As OrigineElencoServerFtp = OrigineElencoServerFtp.MenuPrincipale
    Private _tempAutenticazioneFallitaFtp As Boolean = False
    Private _tempTentativiPasswordFtp As Integer = 0
    Private _tempRitentativoPasswordFtp As Boolean = False
    Private _tempPasswordReinseritaFtp As Boolean = False
    Private _tempServerFtpInfoDaElenco As InfoServerFtp
    Private Const TENTATIVI_MASSIMI_PASSWORD_FTP As Integer = 3
    Private _tempSessioneFtpTest As SessioneFtp
    Private _tempProtocolloNuovoServerFtp As TipoProtocolloFtp = TipoProtocolloFtp.Ftp
    Private _tempElementoFtpDaRinominare As String = ""
    Private _tempPermessiFtpDaApplicare As Integer = 0
    Private _tempPercorsoFtpPermessiSalvato As String = ""
    Private processoIncorsoConnessioneFtp As WGProcessoInCorso
    Private connessioneFtpInCorso As Boolean = False
    Private ReadOnly lockConnessioneFtp As New Object()
    Private Const ID_FTP_ROOT As String = "FTP_ROOT"
    Private Const PREFISSO_FTP_PREFERITO As String = "FTP_SRV_PREF_"
    Private Const PREFISSO_FTP_RECENTE As String = "FTP_SRV_REC_"
    Private Const PREFISSO_FTP_SALVATO As String = "FTP_SRV_SALV_"
    Private Const ID_FTP_NUOVO As String = "FTP_MENU_NUOVO"
    Private Const ID_FTP_ELENCO As String = "FTP_MENU_ELENCO"
    Private Const ID_FTP_PREFERITI As String = "FTP_MENU_PREFERITI"
    Private Const ID_FTP_RECENTI As String = "FTP_MENU_RECENTI"
    Private Const ID_FTP_IMPOSTAZIONI As String = "FTP_MENU_IMPOSTAZIONI"
    Private isFtpMenuPrincipaleMode As Boolean = False
    Private _tempImpostazioniFtpInModifica As InfoImpostazioniFtp

    Private Const ID_RETE_ROOT As String = "RETE_ROOT"
    Private Const ID_RETE_APRI As String = "RETE_MENU_APRI"
    Private Const ID_RETE_CONDIVISIONI As String = "RETE_MENU_CONDIVISIONI"
    Private Const ID_RETE_CONNETTI As String = "RETE_MENU_CONNETTI"
    Private Const ID_RETE_DISCONNETTI As String = "RETE_MENU_DISCONNETTI"
    Private Const ID_RETE_RICOLLEGA As String = "RETE_MENU_RICOLLEGA"
    Private isReteMenuMode As Boolean = False
    Private isReteCondivisioniMode As Boolean = False
    Private _tempReteUnc As String = ""
    Private _tempReteLettera As String = ""
    Private _tempReteUtente As String = ""
    Private _tempRetePassword As String = ""
    Private _tempRetePersistente As Boolean = False
    Private _tempReteDaDisconnettere As String = ""
    Private _tempReteForzaDisconnessione As Boolean = False
    Private processoIncorsoRete As WGProcessoInCorso
    Private domandaRete As WGDomanda
    Private _listaReport As WGMenuLista
    Private _risultatoPulizia As WGPuliziaPC.RisultatoPulizia
    Private _domandaPulizia As WGDomanda
    Private _tipoPuliziaDaConfermare As String = ""

    ' Appunti FTP: elementi remoti copiati/tagliati, in attesa di essere incollati
    ' in una cartella locale. La sessione va tenuta "in prestito" finché non si
    ' esegue l'incolla (o finché non viene sostituita da una nuova copia/taglio),
    ' cosi' la connessione resta valida anche se nel frattempo si esce dal server FTP.
    Private elementiFtp As New List(Of String)()
    Private ftpClipboardTaglia As Boolean = False
    Private ftpClipboardServer As InfoServerFtp
    Private ftpClipboardSessione As SessioneFtp

    ' Monitoraggio della cartella FTP corrente: rileva cambiamenti sul server e
    ' aggiorna automaticamente l'elenco a video.
    Private ftpMonitor As MonitorCartellaFtp
    Private ftpMonitorPercorso As String = ""
    Private _ftpOrdinatiCorrente As New List(Of InfoElementoFtp)()

    ' Per i primi 2 secondi dopo l'avvio del monitoraggio su una cartella, qualunque
    ' aggiornamento riportato dal monitor viene ignorato: il monitor esegue un primo
    ' controllo subito dopo essere stato avviato (a ridosso del caricamento "normale"
    ' appena concluso) e, senza questa finestra di tolleranza, quel primo controllo
    ' provoca una ricostruzione a vuoto della lista - con conseguente doppia lettura
    ' del titolo da parte dello screen reader - anche quando sul server non è cambiato
    ' assolutamente nulla.
    Private Const IGNORA_MONITOR_FTP_PRIMI_MS As Double = 2000
    Private _ftpMonitorAvviatoAlle As DateTime = DateTime.MinValue


    Private Shared Sub ScriviLogCompleto(messaggio As String, ex As Exception)
        Try
            If ex Is Nothing Then
                Return
            End If
            Dim stackTrace As New System.Diagnostics.StackTrace(ex, True)
            Dim frame As System.Diagnostics.StackFrame = stackTrace.GetFrame(0)
            Dim linea As Integer = 0
            Dim nomeFile As String = "N/A"
            Dim metodo As String = "N/A"
            If frame IsNot Nothing Then
                linea = frame.GetFileLineNumber()
                nomeFile = frame.GetFileName()
                metodo = frame.GetMethod()?.Name
            End If
            Dim eccezioneDettagliata As New StringBuilder()
            eccezioneDettagliata.Append($"{messaggio} - ")
            eccezioneDettagliata.Append($"Tipo: {ex.GetType().FullName} | ")
            eccezioneDettagliata.Append($"Messaggio: {ex.Message} | ")
            eccezioneDettagliata.Append($"HResult: 0x{ex.HResult:X8} | ")
            If ex.InnerException IsNot Nothing Then
                eccezioneDettagliata.Append($"InnerException: {ex.InnerException.Message} | ")
            End If
            If TypeOf ex Is Win32Exception Then
                Dim w32ex As Win32Exception = CType(ex, Win32Exception)
                eccezioneDettagliata.Append($"Codice Errore Nativo: {w32ex.NativeErrorCode} | ")
            End If
            eccezioneDettagliata.Append($"File: {nomeFile}, Linea: {linea}, Metodo: {metodo}")
            If Not String.IsNullOrEmpty(ex.StackTrace) Then
                eccezioneDettagliata.Append($" | StackTrace: {ex.StackTrace}")
            End If
            log.ScriviLog(eccezioneDettagliata.ToString())
        Catch logEx As Exception
            System.Diagnostics.Debug.WriteLine($"FALLIMENTO LOG CRITICO: {logEx.Message}")
        End Try
    End Sub

    Private Class NavigazioneStato
        Public Property IndiceSelezionato As Integer
        Public Property Directory As String
        Public Sub New(directory As String, indiceSelezionato As Integer)
            Try
                Me.Directory = directory
                Me.IndiceSelezionato = indiceSelezionato
            Catch ex As Exception
                ScriviLogCompleto("NavigazioneStato.New - Errore", ex)
            End Try
        End Sub
    End Class

    Public Sub ApplicaFiltroEstensioni(estensioni As List(Of String))
        Try
            currentExtensionFilter = String.Join("|", estensioni)
            lista.impostaTitolo("Directory: " & System.IO.Path.GetFileName(currentDirectory.TrimEnd("\")))
            CaricaDirectory(currentDirectory)
        Catch ex As Exception
            ScriviLogCompleto("ApplicaFiltroEstensioni - Errore", ex)
        End Try
    End Sub

    Public Sub Avvia(Optional ByVal percorsoIniziale As String = Nothing)
        Try
            uiContext = SynchronizationContext.Current
            favoritesManager = New Preferiti(Me)
            recentsManager = New Recenti(Me)

            If config.EsisteId("eliminacestino") Then
                Dim valore As String = config.LeggiValore("eliminacestino")
                If Not String.IsNullOrEmpty(valore) Then
                    elimina = Boolean.Parse(valore)
                End If
            Else
                config.AggiungiValore("eliminacestino", "True")
                elimina = True
            End If
            WGCentroDiControllo.GetInstance().UsaCestino = elimina
            If config.EsisteId("ordinamento") Then
                Dim valore As String = config.LeggiValore("ordinamento")
                If Not String.IsNullOrEmpty(valore) AndAlso [Enum].IsDefined(GetType(CLSFile.OrdineDiVisualizzazione), valore) Then
                    ordinamentoCorrente = CType([Enum].Parse(GetType(CLSFile.OrdineDiVisualizzazione), valore), CLSFile.OrdineDiVisualizzazione)
                End If
            Else
                config.AggiungiValore("ordinamento", CLSFile.OrdineDiVisualizzazione.Nome.ToString())
            End If

            AddHandler lista.ElementoSelezionato, AddressOf Selezionato
            AddHandler lista.ElementiSelezionati, AddressOf ElementiSelezionati
            AddHandler lista.escPremuto, AddressOf Me.CartellaPrecedente
            AddHandler lista.VoceCambiata, AddressOf Lista_VoceCambiata
            AddHandler lista.ScorciatoiaCtrl, AddressOf OnScorciatoiaCtrl

            AddHandler logic.AggiornamentoCartella, AddressOf OnDirectoryChanged
            AddHandler logic.DriveAdded, AddressOf OnDriveAdded
            AddHandler logic.DriveRemoved, AddressOf OnDriveRemoved

            logic.AvviaMonitoraggioDrive()
            RegistraRicercaNelCentro()

            If _ricercaDaAprire IsNot Nothing Then
                ' Aperta dal centro di controllo sui risultati di una ricerca: Esc chiude la gestione file.
                Dim ricerca As WGRicercaFile = _ricercaDaAprire
                _ricercaDaAprire = Nothing
                history.Add(New NavigazioneStato("EXIT_ON_BACK", 0))
                lista.impostaTitolo(TitoloRisultati(ricerca))
                MostraRisultatiRicerca(ricerca, False)
                lista.avvia()
            ElseIf String.IsNullOrWhiteSpace(percorsoIniziale) Then
                sceltadrive()
            Else
                CaricaPercorso(percorsoIniziale)
            End If
        Catch ex As ConfigurationException
            ScriviLogCompleto("FileManager.Avvia - Errore di configurazione critico", ex)
        Catch ex As InvalidOperationException
            ScriviLogCompleto("FileManager.Avvia - Stato invalido durante l'avvio", ex)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.Avvia - Errore generico irreversibile durante l'avvio", ex)
        End Try
    End Sub

    Private Sub OnDriveAdded(path As String)
        uiContext.Post(Sub(s)
                           If isDriveSelectionMode Then
                               sceltadrive()
                           End If
                       End Sub, Nothing)
    End Sub

    Private Sub OnDriveRemoved(path As String)
        uiContext.Post(Sub(s)
                           If isDriveSelectionMode Then
                               If lista IsNot Nothing Then
                                   lista.RimuoviElemento(path)
                               End If
                           End If
                           Return
                           If Not String.IsNullOrEmpty(currentDirectory) AndAlso currentDirectory.StartsWith(path, StringComparison.OrdinalIgnoreCase) Then
                               GestioneDisconnessione(Nothing)
                           End If
                       End Sub, Nothing)
    End Sub

    Private Sub GestioneDisconnessione(state As Object)
        Try
            If currentDirectory = "sceltadisco" Then Return
            currentDirectory = ""

            If lista IsNot Nothing Then
                lista.pulisci()
                history.Clear()
                sceltadrive()
            End If
        Catch ex As Exception
            ScriviLogCompleto("GestioneDisconnessione - Errore", ex)
        End Try
    End Sub

    Private Sub CaricaPercorso(percorso As String)
        Try
            currentExtensionFilter = ""
            If logic.IsWpdPath(percorso) Then
                If favoritesManager.IsPreferito(percorso) OrElse recentsManager.IsRecente(percorso) Then
                    history.Add(New NavigazioneStato("EXIT_ON_BACK", 0))
                Else
                    history.Add(New NavigazioneStato("sceltadisco", 0))
                End If
                currentDirectory = percorso
                Dim parts = percorso.Split("|"c)
                If parts.Length >= 2 Then
                    lista.impostaTitolo("Dispositivo portatile")
                End If
                CaricaDirectory(currentDirectory)
                lista.avvia()
                Return
            End If
            If Not String.IsNullOrEmpty(percorso) AndAlso (WGRete.EsisteDirectory(percorso) OrElse WGRete.EsisteFile(percorso)) Then
                If favoritesManager.IsPreferito(percorso) OrElse recentsManager.IsRecente(percorso) Then
                    history.Add(New NavigazioneStato("EXIT_ON_BACK", 0))
                Else
                    history.Add(New NavigazioneStato("sceltadisco", 0))
                End If
                currentDirectory = System.IO.Path.GetFullPath(percorso)
                lista.impostaTitolo("Directory: " & System.IO.Path.GetFileName(currentDirectory.TrimEnd("\")))
                CaricaDirectory(currentDirectory)
                lista.avvia()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Percorso iniziale non valido o inesistente.")
                msg.ShowDialog()
                sceltadrive()
            End If
        Catch ex As PathTooLongException
            ScriviLogCompleto($"FileManager.CaricaPercorso - Percorso troppo lungo: '{percorso}'", ex)
            sceltadrive()
        Catch ex As SecurityException
            ScriviLogCompleto($"FileManager.CaricaPercorso - Errore di sicurezza accesso percorso '{percorso}'", ex)
            sceltadrive()
        Catch ex As Exception
            ScriviLogCompleto($"FileManager.CaricaPercorso - Errore inatteso durante il caricamento del percorso '{percorso}'", ex)
            sceltadrive()
        End Try
    End Sub

    Public Sub SettaEliminaCestino(value As Boolean)
        Try
            elimina = value
            config.ModificaValore("eliminacestino", value.ToString())
            WGCentroDiControllo.GetInstance().UsaCestino = elimina
        Catch ex As Exception
            ScriviLogCompleto("FileManager.SettaEliminaCestino - Errore durante il salvataggio dell'impostazione", ex)
        End Try
    End Sub

    Public Sub SettaOrdinamento(nuovoOrdine As String)
        Try
            If [Enum].IsDefined(GetType(CLSFile.OrdineDiVisualizzazione), nuovoOrdine) Then
                ordinamentoCorrente = CType([Enum].Parse(GetType(CLSFile.OrdineDiVisualizzazione), nuovoOrdine), CLSFile.OrdineDiVisualizzazione)
                config.ModificaValore("ordinamento", nuovoOrdine)
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Opzione di ordinamento non valida.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.SettaOrdinamento - Errore durante il salvataggio dell'impostazione", ex)
        End Try
    End Sub

    Private Function OttieniModalitaAperturaMedia() As String
        Try
            If config.EsisteId("modalitaAperturaMedia") Then
                Return config.LeggiValore("modalitaAperturaMedia")
            End If
            Return ""
        Catch ex As Exception
            ScriviLogCompleto("OttieniModalitaAperturaMedia - Errore", ex)
            Return ""
        End Try
    End Function

    Private Function OttieniModalitaAperturaTesto() As String
        Try
            If config.EsisteId("modalitaAperturaTesto") Then
                Dim valore As String = config.LeggiValore("modalitaAperturaTesto")
                If Not String.IsNullOrEmpty(valore) Then Return valore
            End If
            Return "Modifica"
        Catch ex As Exception
            ScriviLogCompleto("OttieniModalitaAperturaTesto - Errore", ex)
            Return "Modifica"
        End Try
    End Function

    Private Function SelezionaPercorsoCompleto(x As CLSFile.FileItem) As String
        Try
            Return x.PercorsoCompleto
        Catch ex As Exception
            ScriviLogCompleto("SelezionaPercorsoCompleto - Errore", ex)
            Return String.Empty
        End Try
    End Function

    Private Sub OnDirectoryChanged(path As String)
        Try
            uiContext.Post(AddressOf OnDirectoryChangedCallback, path)
        Catch ex As Exception
            ScriviLogCompleto("OnDirectoryChanged - Errore", ex)
        End Try
    End Sub

    Private Sub OnDirectoryChangedCallback(state As Object)
        Try
            Dim changedPath As String = CStr(state)
            If lista Is Nothing Then Return

            If lista.VoceEsiste(changedPath) Then
                lista.RimuoviElemento(changedPath)
                wgInternalList.Rimuovi(changedPath)
            Else
                Dim exists As Boolean = False
                If logic.IsWpdPath(changedPath) Then
                    Dim dict = logic.GetWpdDetails(changedPath)
                    exists = (dict("Type") <> "Error" AndAlso dict("Type") <> "Unknown")
                Else
                    exists = WGRete.EsisteFile(changedPath) OrElse WGRete.EsisteDirectory(changedPath)
                End If

                If exists Then
                    Dim predecessorId As String = wgInternalList.InserisciOrdinato(changedPath, CInt(ordinamentoCorrente))
                    Dim nome As String = System.IO.Path.GetFileName(changedPath)
                    If String.IsNullOrEmpty(predecessorId) OrElse Not lista.VoceEsiste(predecessorId) Then
                        lista.aggiungiElemento(nome, changedPath)
                    Else
                        lista.AggiungiElementoDopo(predecessorId, nome, changedPath)
                    End If
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.OnDirectoryChangedCallback - Errore", ex)
        End Try
    End Sub

    Public Sub sceltadrive()
        Try
            If lista Is Nothing Then Return
            AnnullaCaricamentoDirectoryInCorso()
            currentExtensionFilter = ""
            isDriveSelectionMode = True
            isFtpServerListMode = False
            isFtpBrowseMode = False
            isReteMenuMode = False
            isReteCondivisioniMode = False
            suonoAttesa.Attesa()

            Try
                lista.pulisci()
                lista.impostaTitolo("Scelta risorsa")
                lista.multiselezioneConsentita = False
                lista.ciclono = False
                InizializzaOpzioniSceltaDrive()
            Catch ex As NullReferenceException
                Return
            Catch ex As Exception
                ScriviLogCompleto("Errore critico su lista in sceltadrive", ex)
            End Try

            Task.Run(Sub()
                         Try
                             Dim items = logic.OttieniDrives()
                             uiContext.Post(Sub(state)
                                                Try
                                                    If Not isDriveSelectionMode Then Return
                                                    If CaricamentoLocaleNonConsentito() Then Return
                                                    Dim drives = CType(state, List(Of CLSFile.FileItem))
                                                    driveListCache.Clear()


                                                    For Each item In drives
                                                        Try
                                                            lista.aggiungiElemento(item.Nome, item.PercorsoCompleto)
                                                            driveListCache.Add(item.PercorsoCompleto)
                                                        Catch ex As Exception
                                                            ScriviLogCompleto("sceltadrive - Errore aggiunta drive " & item.Nome, ex)
                                                        End Try
                                                    Next

                                                    Try
                                                        lista.aggiungiElemento("Server FTP", ID_FTP_ROOT)
                                                        driveListCache.Add(ID_FTP_ROOT)
                                                    Catch ex As Exception
                                                        ScriviLogCompleto("sceltadrive - Errore aggiunta voce Server FTP", ex)
                                                    End Try

                                                    Try
                                                        lista.aggiungiElemento("Rete e unità di rete", ID_RETE_ROOT)
                                                        driveListCache.Add(ID_RETE_ROOT)
                                                    Catch ex As Exception
                                                        ScriviLogCompleto("sceltadrive - Errore aggiunta voce Rete", ex)
                                                    End Try

                                                    lista.invioNonConsentito = False
                                                    Dim firstId As String = lista.ottieniElementoIndice(0)
                                                    AggiornaOpzioniDrive(firstId)

                                                    suonoAttesa.StopAttesa()
                                                    Try : lista.avvia() : Catch : End Try
                                                Catch exUI As Exception
                                                    ScriviLogCompleto("sceltadrive UI Update - Errore", exUI)
                                                End Try
                                            End Sub, items)
                         Catch ex As Exception
                             ScriviLogCompleto("sceltadrive background - Errore", ex)
                         End Try
                     End Sub)

        Catch ex As NullReferenceException
            Return
        Catch ex As Exception
            ScriviLogCompleto("sceltadrive generico - Errore critico", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Ricorda da quale elenco si e' entrati nel server, cosi' che uscendo dalla navigazione
    ''' si torni allo stesso elenco invece che al menu principale dei server.
    ''' </summary>
    Private Enum OrigineElencoServerFtp
        MenuPrincipale
        ElencoServer
        Preferiti
        Recenti
    End Enum

    ''' <summary>
    ''' Chiude la sessione corrente e riporta l'utente all'elenco da cui era partito:
    ''' elenco server, preferiti o recenti. Se non risulta nessuna provenienza si torna al
    ''' menu principale dei server.
    ''' </summary>
    Private Sub TornaAllElencoServerFtp()
        Try
            isFtpBrowseMode = False
            ChiudiSessioneFtpCorrente()
            Select Case _origineElencoServerFtp
                Case OrigineElencoServerFtp.ElencoServer
                    MostraElencoServerFtp()
                Case OrigineElencoServerFtp.Preferiti
                    MostraServerPreferitiFtp()
                Case OrigineElencoServerFtp.Recenti
                    MostraServerRecentiFtp()
                Case Else
                    MostraMenuServerFtp()
            End Select
        Catch ex As Exception
            ScriviLogCompleto("TornaAllElencoServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub AggiornaSceltaDrive()
        Try
            WGRete.AzzeraStatoOffline()
            sceltadrive()
        Catch ex As Exception
            ScriviLogCompleto("AggiornaSceltaDrive - Errore", ex)
        End Try
    End Sub

    Private Sub ChiudiProcessoRete()
        Try
            If processoIncorsoRete IsNot Nothing Then
                processoIncorsoRete.Fine()
                processoIncorsoRete = Nothing
            End If
        Catch ex As Exception
            ScriviLogCompleto("ChiudiProcessoRete - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMessaggioSemplice(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("MostraMessaggioSemplice - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMenuRete()
        Try
            If lista Is Nothing Then Return
            AnnullaCaricamentoDirectoryInCorso()
            isReteMenuMode = True
            isReteCondivisioniMode = False
            isFtpMenuPrincipaleMode = False
            isFtpServerListMode = False
            isFtpElencoServerMode = False
            isFtpBrowseMode = False
            isDriveSelectionMode = False

            Dim elementiLista As New List(Of ElementoLista)()
            elementiLista.Add(New ElementoLista("Apri un percorso di rete", ID_RETE_APRI))
            elementiLista.Add(New ElementoLista("Elenca le condivisioni di un computer", ID_RETE_CONDIVISIONI))
            elementiLista.Add(New ElementoLista("Connetti unità di rete", ID_RETE_CONNETTI))
            elementiLista.Add(New ElementoLista("Disconnetti unità di rete", ID_RETE_DISCONNETTI))
            elementiLista.Add(New ElementoLista("Ricollega le unità di rete salvate", ID_RETE_RICOLLEGA))

            Try
                lista.pulisci()
                lista.impostaTitolo("Rete e unità di rete")
                lista.multiselezioneConsentita = False
                lista.opzioniPulisci()
                lista.invioNonConsentito = False
                lista.CaricaListaDinamica(elementiLista)
                lista.avvia()
            Catch exUI As Exception
                ScriviLogCompleto("MostraMenuRete UI Update - Errore", exUI)
            End Try
        Catch ex As NullReferenceException
            Return
        Catch ex As Exception
            ScriviLogCompleto("MostraMenuRete - Errore", ex)
        End Try
    End Sub

    Private Sub SelezionatoMenuRete(path As String)
        Try
            Select Case path
                Case ID_RETE_APRI
                    ApriPercorsoRete()
                Case ID_RETE_CONDIVISIONI
                    ChiediServerRete()
                Case ID_RETE_CONNETTI
                    NuovaConnessioneRete()
                Case ID_RETE_DISCONNETTI
                    MostraDisconnessioneRete()
                Case ID_RETE_RICOLLEGA
                    RicollegaUnitaSalvate()
            End Select
        Catch ex As Exception
            ScriviLogCompleto("SelezionatoMenuRete - Errore", ex)
        End Try
    End Sub

    Private Sub ApriPercorsoRete()
        Try
            campi = New Campi()
            AddHandler campi.Fine, AddressOf consultadirectory
            campi.Aggiungi("Scrivi il percorso di rete, per esempio \\server\condivisione", "c.percorso", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("ApriPercorsoRete - Errore", ex)
        End Try
    End Sub

    Private Sub ChiediServerRete()
        Try
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnServerReteInserito
            campi.Aggiungi("Scrivi il nome o l'indirizzo del computer, per esempio server oppure 192.168.1.10", "rete.server", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("ChiediServerRete - Errore", ex)
        End Try
    End Sub

    Private Sub OnServerReteInserito()
        Try
            Dim campiCorrente As Campi = campi
            If campiCorrente Is Nothing Then Return
            RemoveHandler campiCorrente.Fine, AddressOf OnServerReteInserito
            campi = Nothing
            Dim server As String = campiCorrente.RecuperaValore("rete.server")
            campiCorrente = Nothing
            If String.IsNullOrWhiteSpace(server) Then Return
            Dim normalizzato As String = WGRete.NormalizzaPercorsoUnc(server)
            If Not normalizzato.StartsWith("\\", StringComparison.Ordinal) Then
                normalizzato = "\\" & normalizzato.TrimStart("\"c)
            End If
            MostraCondivisioniServer(normalizzato)
        Catch ex As Exception
            ScriviLogCompleto("OnServerReteInserito - Errore", ex)
        End Try
    End Sub

    Private Sub MostraCondivisioniServer(server As String)
        Try
            ChiudiProcessoRete()
            processoIncorsoRete = New WGProcessoInCorso()
            processoIncorsoRete.Avvia("Ricerca delle condivisioni in corso...")
            Dim t As New Thread(AddressOf ElencaCondivisioniThread)
            t.IsBackground = True
            t.Start(server)
        Catch ex As Exception
            ChiudiProcessoRete()
            ScriviLogCompleto("MostraCondivisioniServer - Errore", ex)
        End Try
    End Sub

    Private Sub ElencaCondivisioniThread(state As Object)
        Dim server As String = CStr(state)
        Dim codice As Integer = 0
        Dim elenco As List(Of WGRete.InfoCondivisione) = New List(Of WGRete.InfoCondivisione)()
        Try
            elenco = WGRete.ElencaCondivisioni(server, codice)
        Catch ex As Exception
            codice = -1
            ScriviLogCompleto("ElencaCondivisioniThread - Errore", ex)
        End Try
        Try
            Dim elencoFinale As List(Of WGRete.InfoCondivisione) = elenco
            Dim codiceFinale As Integer = codice
            uiContext.Post(Sub(s) CompletaElencoCondivisioni(server, elencoFinale, codiceFinale), Nothing)
        Catch exPost As Exception
            ScriviLogCompleto("ElencaCondivisioniThread - Errore notifica interfaccia", exPost)
        End Try
    End Sub

    Private Sub CompletaElencoCondivisioni(server As String, elenco As List(Of WGRete.InfoCondivisione), codice As Integer)
        Try
            ChiudiProcessoRete()
            If lista Is Nothing Then Return
            If codice <> 0 Then
                Dim testoErrore As String = "Non è stato possibile elencare le condivisioni di " & server & ". " & WGRete.DescriviErrore(codice)
                If codice = 5 OrElse codice = 1219 OrElse codice = 1244 Then
                    testoErrore &= " Puoi collegarti con nome utente e password dal menu Connetti unità di rete, oppure scrivere il percorso completo di una condivisione."
                End If
                MostraMessaggioSemplice(testoErrore)
                Return
            End If
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                MostraMessaggioSemplice("Il computer " & server & " non ha condivisioni visibili. Se conosci il nome di una condivisione, aprila scrivendo il percorso completo.")
                Return
            End If

            AnnullaCaricamentoDirectoryInCorso()
            isReteMenuMode = False
            isReteCondivisioniMode = True
            isFtpMenuPrincipaleMode = False
            isFtpServerListMode = False
            isFtpElencoServerMode = False
            isFtpBrowseMode = False
            isDriveSelectionMode = False

            Dim elementiLista As New List(Of ElementoLista)()
            For Each condivisione As WGRete.InfoCondivisione In elenco
                Dim etichetta As String = condivisione.Nome
                If Not String.IsNullOrWhiteSpace(condivisione.Commento) Then etichetta &= " - " & condivisione.Commento
                elementiLista.Add(New ElementoLista(etichetta, condivisione.Percorso))
            Next

            lista.pulisci()
            lista.impostaTitolo("Condivisioni di " & server)
            lista.multiselezioneConsentita = False
            lista.opzioniPulisci()
            lista.invioNonConsentito = False
            lista.CaricaListaDinamica(elementiLista)
            lista.avvia()
        Catch ex As Exception
            ScriviLogCompleto("CompletaElencoCondivisioni - Errore", ex)
        End Try
    End Sub

    Private Sub SelezionatoCondivisioneRete(path As String)
        Try
            Dim codiceAccesso As Integer = WGRete.VerificaAccesso(path, 10000)
            If codiceAccesso <> 0 Then
                MostraMessaggioSemplice("Impossibile aprire la condivisione. " & WGRete.DescriviErrore(codiceAccesso))
                Return
            End If
            isReteCondivisioniMode = False
            currentDirectory = ""
            recentsManager.AggiungiARecenti(path)
            CartellaSuccessiva(path)
        Catch ex As Exception
            ScriviLogCompleto("SelezionatoCondivisioneRete - Errore", ex)
        End Try
    End Sub

    Private Sub NuovaConnessioneRete()
        Try
            Dim libere As List(Of String) = WGRete.LettereLibere()
            Dim proposta As String = If(libere.Count > 0, libere(0), "")
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnNuovaConnessioneReteInserita
            campi.Aggiungi("Percorso di rete, per esempio \\server\condivisione", "rete.percorso", True)
            campi.Aggiungi("Lettera dell'unità da assegnare, lascia vuoto per collegarti senza assegnare una lettera", "rete.lettera", False, , proposta)
            campi.Aggiungi("Nome utente, lascia vuoto per usare l'utente corrente", "rete.utente", False)
            campi.AggiungiPassword("Password, lascia vuoto se non serve", "rete.password", False)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("NuovaConnessioneRete - Errore", ex)
        End Try
    End Sub

    Private Sub OnNuovaConnessioneReteInserita()
        Try
            Dim campiCorrente As Campi = campi
            If campiCorrente Is Nothing Then Return
            RemoveHandler campiCorrente.Fine, AddressOf OnNuovaConnessioneReteInserita
            campi = Nothing

            Dim percorso As String = WGRete.NormalizzaPercorsoUnc(campiCorrente.RecuperaValore("rete.percorso"))
            Dim lettera As String = campiCorrente.RecuperaValore("rete.lettera")
            Dim utente As String = campiCorrente.RecuperaValore("rete.utente")
            Dim password As String = campiCorrente.RecuperaValore("rete.password")
            campiCorrente = Nothing

            If Not WGRete.IsUncPath(percorso) OrElse WGRete.IsServerRoot(percorso) Then
                MostraMessaggioSemplice("Il percorso di rete deve indicare computer e condivisione, per esempio \\server\condivisione.")
                Return
            End If

            lettera = If(lettera, "").Trim().TrimEnd(":"c)
            If lettera.Length > 1 OrElse (lettera.Length = 1 AndAlso Not Char.IsLetter(lettera(0))) Then
                MostraMessaggioSemplice("La lettera dell'unità non è valida. Scrivi una sola lettera, per esempio Z.")
                Return
            End If
            If lettera.Length = 1 Then
                For Each radice As String In Environment.GetLogicalDrives()
                    If radice.StartsWith(lettera, StringComparison.OrdinalIgnoreCase) Then
                        MostraMessaggioSemplice("La lettera " & lettera.ToUpperInvariant() & " è già in uso. Scegline un'altra.")
                        Return
                    End If
                Next
            End If

            _tempReteUnc = percorso
            _tempReteLettera = lettera
            _tempReteUtente = utente
            _tempRetePassword = password
            _tempRetePersistente = False

            If lettera.Length = 1 Then
                domandaRete = New WGDomanda()
                AddHandler domandaRete.rispostadomanda, AddressOf RispostaPersistenzaRete
                domandaRete.avvia("Vuoi che l'unità venga ricollegata automaticamente ad ogni accesso a Windows? La password non viene salvata dal programma.")
            Else
                AvviaConnessioneRete()
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnNuovaConnessioneReteInserita - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaPersistenzaRete(risposta As DialogResult)
        Try
            _tempRetePersistente = (risposta = DialogResult.Yes)
            AvviaConnessioneRete()
        Catch ex As Exception
            ScriviLogCompleto("RispostaPersistenzaRete - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaConnessioneRete()
        Try
            ChiudiProcessoRete()
            processoIncorsoRete = New WGProcessoInCorso()
            processoIncorsoRete.Avvia("Connessione alla risorsa di rete in corso...")
            Dim t As New Thread(AddressOf ConnessioneReteThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ChiudiProcessoRete()
            ScriviLogCompleto("AvviaConnessioneRete - Errore", ex)
        End Try
    End Sub

    Private Sub ConnessioneReteThread()
        Dim codice As Integer = -1
        Try
            codice = WGRete.Connetti(_tempReteUnc, _tempReteLettera, _tempReteUtente, _tempRetePassword, _tempRetePersistente)
        Catch ex As Exception
            ScriviLogCompleto("ConnessioneReteThread - Errore", ex)
        End Try
        Try
            uiContext.Post(AddressOf OnConnessioneReteCompletata, codice)
        Catch exPost As Exception
            ScriviLogCompleto("ConnessioneReteThread - Errore notifica interfaccia", exPost)
        End Try
    End Sub

    Private Sub OnConnessioneReteCompletata(state As Object)
        Try
            ChiudiProcessoRete()
            _tempRetePassword = ""
            Dim codice As Integer = CInt(state)
            If codice = 0 Then
                If _tempReteLettera.Length = 1 Then
                    MostraMessaggioSemplice("Unità di rete collegata. " & _tempReteUnc & " è ora disponibile come unità " & _tempReteLettera.ToUpperInvariant() & ".")
                Else
                    MostraMessaggioSemplice("Connessione a " & _tempReteUnc & " stabilita. Ora puoi aprire il percorso dal menu Apri un percorso di rete.")
                End If
            Else
                MostraMessaggioSemplice("Connessione non riuscita. " & WGRete.DescriviErrore(codice))
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnConnessioneReteCompletata - Errore", ex)
        End Try
    End Sub

    Private Sub MostraDisconnessioneRete()
        Try
            Dim unita As List(Of WGRete.UnitaDiRete) = WGRete.UnitaMappate()
            If unita.Count = 0 Then
                MostraMessaggioSemplice("Non ci sono unità di rete collegate.")
                Return
            End If
            Dim menuUnita As New WGMenu()
            For Each voce As WGRete.UnitaDiRete In unita
                Dim letteraUnita As String = voce.Lettera
                Dim descrizione As String = letteraUnita & ": " & If(String.IsNullOrEmpty(voce.PercorsoUnc), "non raggiungibile", voce.PercorsoUnc)
                menuUnita.aggiungi("Disconnetti " & descrizione, Sub()
                                                                     Try
                                                                         menuUnita.abbandona()
                                                                         DisconnettiUnitaRete(letteraUnita, False)
                                                                     Catch exVoce As Exception
                                                                         ScriviLogCompleto("MostraDisconnessioneRete - Errore selezione unità", exVoce)
                                                                     End Try
                                                                 End Sub)
            Next
            menuUnita.avvia("Unità di rete collegate", True)
        Catch ex As Exception
            ScriviLogCompleto("MostraDisconnessioneRete - Errore", ex)
        End Try
    End Sub

    Private Sub DisconnettiUnitaSelezionata()
        Try
            Dim selezionati As List(Of String) = lista.ottieniElementi()
            If selezionati Is Nothing OrElse selezionati.Count = 0 Then
                MostraMessaggioSemplice("Nessuna unità selezionata.")
                Return
            End If
            Dim percorso As String = selezionati(0)
            If String.IsNullOrEmpty(percorso) OrElse percorso.Length < 2 OrElse Not WGRete.IsRemoto(percorso) Then
                MostraMessaggioSemplice("L'elemento selezionato non è un'unità di rete.")
                Return
            End If
            DisconnettiUnitaRete(percorso.Substring(0, 1), False)
        Catch ex As Exception
            ScriviLogCompleto("DisconnettiUnitaSelezionata - Errore", ex)
        End Try
    End Sub

    Private Sub DisconnettiUnitaRete(lettera As String, forza As Boolean)
        Try
            _tempReteDaDisconnettere = lettera
            _tempReteForzaDisconnessione = forza
            ChiudiProcessoRete()
            processoIncorsoRete = New WGProcessoInCorso()
            processoIncorsoRete.Avvia("Disconnessione dell'unità di rete in corso...")
            Dim t As New Thread(AddressOf DisconnessioneReteThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ChiudiProcessoRete()
            ScriviLogCompleto("DisconnettiUnitaRete - Errore", ex)
        End Try
    End Sub

    Private Sub DisconnessioneReteThread()
        Dim codice As Integer = -1
        Try
            codice = WGRete.Disconnetti(_tempReteDaDisconnettere & ":", _tempReteForzaDisconnessione)
        Catch ex As Exception
            ScriviLogCompleto("DisconnessioneReteThread - Errore", ex)
        End Try
        Try
            uiContext.Post(AddressOf OnDisconnessioneReteCompletata, codice)
        Catch exPost As Exception
            ScriviLogCompleto("DisconnessioneReteThread - Errore notifica interfaccia", exPost)
        End Try
    End Sub

    Private Sub OnDisconnessioneReteCompletata(state As Object)
        Try
            ChiudiProcessoRete()
            Dim codice As Integer = CInt(state)
            If codice = 0 Then
                MostraMessaggioSemplice("Unità " & _tempReteDaDisconnettere.ToUpperInvariant() & " disconnessa.")
                If isDriveSelectionMode Then sceltadrive()
            ElseIf codice = 2401 AndAlso Not _tempReteForzaDisconnessione Then
                domandaRete = New WGDomanda()
                AddHandler domandaRete.rispostadomanda, AddressOf RispostaForzaDisconnessioneRete
                domandaRete.avvia("Ci sono file aperti sull'unità di rete. Vuoi disconnetterla comunque? I dati non salvati potrebbero andare persi.")
            Else
                MostraMessaggioSemplice("Disconnessione non riuscita. " & WGRete.DescriviErrore(codice))
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnDisconnessioneReteCompletata - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaForzaDisconnessioneRete(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes Then
                DisconnettiUnitaRete(_tempReteDaDisconnettere, True)
            End If
        Catch ex As Exception
            ScriviLogCompleto("RispostaForzaDisconnessioneRete - Errore", ex)
        End Try
    End Sub

    Private Sub RicollegaUnitaSalvate()
        Try
            ChiudiProcessoRete()
            processoIncorsoRete = New WGProcessoInCorso()
            processoIncorsoRete.Avvia("Ricollegamento delle unità di rete in corso...")
            Dim t As New Thread(AddressOf RicollegaUnitaSalvateThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ChiudiProcessoRete()
            ScriviLogCompleto("RicollegaUnitaSalvate - Errore", ex)
        End Try
    End Sub

    Private Sub RicollegaUnitaSalvateThread()
        Dim righe As New List(Of String)()
        Try
            righe = WGRete.RicollegaMappatureSalvate()
        Catch ex As Exception
            righe.Add("Errore imprevisto durante il ricollegamento. Controlla il log.")
            ScriviLogCompleto("RicollegaUnitaSalvateThread - Errore", ex)
        End Try
        Try
            uiContext.Post(AddressOf OnRicollegaUnitaSalvateCompletato, righe)
        Catch exPost As Exception
            ScriviLogCompleto("RicollegaUnitaSalvateThread - Errore notifica interfaccia", exPost)
        End Try
    End Sub

    Private Sub OnRicollegaUnitaSalvateCompletato(state As Object)
        Try
            ChiudiProcessoRete()
            Dim righe As List(Of String) = TryCast(state, List(Of String))
            If righe Is Nothing OrElse righe.Count = 0 Then
                MostraMessaggioSemplice("Nessuna unità di rete da ricollegare.")
                Return
            End If
            MostraReport("Esito del ricollegamento", righe)
        Catch ex As Exception
            ScriviLogCompleto("OnRicollegaUnitaSalvateCompletato - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMenuServerFtp()
        Try
            If lista Is Nothing Then Return
            AnnullaCaricamentoDirectoryInCorso()
            FermaMonitorFtp()
            isFtpBrowseMode = False
            isFtpServerListMode = False
            isFtpElencoServerMode = False
            isFtpMenuPrincipaleMode = True
            isDriveSelectionMode = False
            currentServerFtp = Nothing
            _origineElencoServerFtp = OrigineElencoServerFtp.MenuPrincipale
            ftpHistory.Clear()
            ftpServerCache.Clear()

            Dim elementiLista As New List(Of ElementoLista)()
            elementiLista.Add(New ElementoLista("Nuovo server FTP", ID_FTP_NUOVO))
            elementiLista.Add(New ElementoLista("Elenco server", ID_FTP_ELENCO))
            elementiLista.Add(New ElementoLista("Server preferiti", ID_FTP_PREFERITI))
            elementiLista.Add(New ElementoLista("Server recenti", ID_FTP_RECENTI))
            elementiLista.Add(New ElementoLista("Impostazioni FTP", ID_FTP_IMPOSTAZIONI))

            Try
                lista.pulisci()
                lista.impostaTitolo("Server FTP")
                lista.multiselezioneConsentita = False
                lista.opzioniPulisci()
                lista.invioNonConsentito = False
                lista.CaricaListaDinamica(elementiLista)
                Try : lista.avvia() : Catch : End Try
            Catch exUI As Exception
                ScriviLogCompleto("MostraMenuServerFtp UI Update - Errore", exUI)
            End Try
        Catch ex As NullReferenceException
            Return
        Catch ex As Exception
            ScriviLogCompleto("MostraMenuServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub SelezionatoMenuPrincipaleFtp(path As String)
        Try
            Select Case path
                Case ID_FTP_NUOVO
                    NuovoServerFtp()
                Case ID_FTP_ELENCO
                    MostraElencoServerFtp()
                Case ID_FTP_PREFERITI
                    MostraServerPreferitiFtp()
                Case ID_FTP_RECENTI
                    MostraServerRecentiFtp()
                Case ID_FTP_IMPOSTAZIONI
                    MostraImpostazioniFtp()
            End Select
        Catch ex As Exception
            ScriviLogCompleto("SelezionatoMenuPrincipaleFtp - Errore", ex)
        End Try
    End Sub

    Private Sub MostraImpostazioniFtp()
        Try
            Dim t As New Thread(Sub()
                                    Try
                                        Dim impostazioni As InfoImpostazioniFtp = ftpEngine.OttieniImpostazioniFtp()
                                        uiContext.Post(Sub(state)
                                                           Try
                                                               _tempImpostazioniFtpInModifica = impostazioni
                                                               ChiediSalvaSempreServerFtp()
                                                           Catch exUI As Exception
                                                               ScriviLogCompleto("MostraImpostazioniFtp UI Update - Errore", exUI)
                                                           End Try
                                                       End Sub, Nothing)
                                    Catch ex As Exception
                                        ScriviLogCompleto("MostraImpostazioniFtp thread - Errore", ex)
                                    End Try
                                End Sub)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("MostraImpostazioniFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ChiediSalvaSempreServerFtp()
        Try
            Dim testoStato As String = If(_tempImpostazioniFtpInModifica.SalvaSempreServer, "Attualmente: sì.", "Attualmente: no.")
            If domandaImpostazioniFtp IsNot Nothing Then RemoveHandler domandaImpostazioniFtp.rispostadomanda, AddressOf RispostaSalvaSempreServerFtp
            domandaImpostazioniFtp = New WGDomanda()
            AddHandler domandaImpostazioniFtp.rispostadomanda, AddressOf RispostaSalvaSempreServerFtp
            domandaImpostazioniFtp.avvia("Vuoi salvare sempre automaticamente i nuovi server FTP, senza che te lo chieda ogni volta? " & testoStato)
        Catch ex As Exception
            ScriviLogCompleto("ChiediSalvaSempreServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaSalvaSempreServerFtp(rispostadom As DialogResult)
        Try
            If domandaImpostazioniFtp Is Nothing Then Return
            RemoveHandler domandaImpostazioniFtp.rispostadomanda, AddressOf RispostaSalvaSempreServerFtp
            domandaImpostazioniFtp = Nothing

            _tempImpostazioniFtpInModifica.SalvaSempreServer = (rispostadom = DialogResult.Yes)
            If _tempImpostazioniFtpInModifica.SalvaSempreServer Then
                ChiediSalvaSemprePasswordFtp()
            Else
                _tempImpostazioniFtpInModifica.SalvaSemprePassword = False
                ChiediLimiteVelocitaFtp()
            End If
        Catch ex As Exception
            ScriviLogCompleto("RispostaSalvaSempreServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ChiediSalvaSemprePasswordFtp()
        Try
            Dim testoStato As String = If(_tempImpostazioniFtpInModifica.SalvaSemprePassword, "Attualmente: sì.", "Attualmente: no.")
            If domandaImpostazioniFtp IsNot Nothing Then RemoveHandler domandaImpostazioniFtp.rispostadomanda, AddressOf RispostaSalvaSemprePasswordFtp
            domandaImpostazioniFtp = New WGDomanda()
            AddHandler domandaImpostazioniFtp.rispostadomanda, AddressOf RispostaSalvaSemprePasswordFtp
            domandaImpostazioniFtp.avvia("Vuoi salvare sempre anche la password dei nuovi server? " & testoStato)
        Catch ex As Exception
            ScriviLogCompleto("ChiediSalvaSemprePasswordFtp - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaSalvaSemprePasswordFtp(rispostadom As DialogResult)
        Try
            If domandaImpostazioniFtp Is Nothing Then Return
            RemoveHandler domandaImpostazioniFtp.rispostadomanda, AddressOf RispostaSalvaSemprePasswordFtp
            domandaImpostazioniFtp = Nothing

            _tempImpostazioniFtpInModifica.SalvaSemprePassword = (rispostadom = DialogResult.Yes)
            ChiediLimiteVelocitaFtp()
        Catch ex As Exception
            ScriviLogCompleto("RispostaSalvaSemprePasswordFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ChiediLimiteVelocitaFtp()
        Try
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnLimiteVelocitaFtpInserito
            campi.Aggiungi("Limite di velocità per tutti i server, in KB/s (lascia vuoto per nessun limite)", "ftp.limitevelocitaglobale", False, , If(_tempImpostazioniFtpInModifica.LimiteVelocitaKBs > 0, _tempImpostazioniFtpInModifica.LimiteVelocitaKBs.ToString(), ""))
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("ChiediLimiteVelocitaFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnLimiteVelocitaFtpInserito()
        Try
            Dim campiCorrente As Campi = campi
            If campiCorrente Is Nothing Then Return
            RemoveHandler campiCorrente.Fine, AddressOf OnLimiteVelocitaFtpInserito
            campi = Nothing

            Dim limiteTesto As String = campiCorrente.RecuperaValore("ftp.limitevelocitaglobale")
            campiCorrente = Nothing

            Dim limiteVelocita As Integer = 0
            If Not String.IsNullOrWhiteSpace(limiteTesto) Then
                If Not Integer.TryParse(limiteTesto, limiteVelocita) OrElse limiteVelocita < 0 Then
                    Dim msgLimite As New WGMessaggi()
                    msgLimite.Messaggio("Il limite di velocità specificato non è valido.")
                    msgLimite.ShowDialog()
                    MostraImpostazioniFtp()
                    Return
                End If
            End If
            _tempImpostazioniFtpInModifica.LimiteVelocitaKBs = limiteVelocita

            SalvaImpostazioniFtpCorrente()
        Catch ex As Exception
            ScriviLogCompleto("OnLimiteVelocitaFtpInserito - Errore", ex)
        End Try
    End Sub

    Private Sub SalvaImpostazioniFtpCorrente()
        Try
            Dim impostazioni As InfoImpostazioniFtp = _tempImpostazioniFtpInModifica
            Dim t As New Thread(Sub()
                                    Try
                                        Dim esito As Boolean = ftpEngine.SalvaImpostazioniFtp(impostazioni)
                                        uiContext.Post(Sub(state)
                                                           Try
                                                               Dim msg As New WGMessaggi()
                                                               msg.Messaggio(If(esito, "Impostazioni FTP salvate correttamente.", "Non è stato possibile salvare le impostazioni FTP."))
                                                               msg.ShowDialog()
                                                               MostraMenuServerFtp()
                                                           Catch exUI As Exception
                                                               ScriviLogCompleto("SalvaImpostazioniFtpCorrente UI Update - Errore", exUI)
                                                           End Try
                                                       End Sub, Nothing)
                                    Catch ex As Exception
                                        ScriviLogCompleto("SalvaImpostazioniFtpCorrente thread - Errore", ex)
                                    End Try
                                End Sub)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("SalvaImpostazioniFtpCorrente - Errore", ex)
        End Try
    End Sub

    Private Sub MostraElencoServerFtp()
        Try
            If lista Is Nothing Then Return
            AnnullaCaricamentoDirectoryInCorso()
            isFtpBrowseMode = False
            isFtpServerListMode = True
            isFtpElencoServerMode = True
            isFtpMenuPrincipaleMode = False
            isDriveSelectionMode = False
            currentServerFtp = Nothing
            _origineElencoServerFtp = OrigineElencoServerFtp.ElencoServer
            ftpHistory.Clear()
            ftpServerCache.Clear()
            suonoAttesa.Attesa()

            Task.Run(Sub()
                         Try
                             Dim salvati As List(Of InfoServerFtp) = ftpEngine.OttieniServerSalvati()
                             uiContext.Post(Sub(state)
                                                Try
                                                    If salvati.Count = 0 Then
                                                        ' niente da elencare: un messaggio, e si resta nel menu dei server
                                                        suonoAttesa.StopAttesa()
                                                        MostraMessaggioSemplice("Non ci sono server salvati.")
                                                        MostraMenuServerFtp()
                                                        Return
                                                    End If
                                                    lista.pulisci()
                                                    lista.impostaTitolo("Elenco server")
                                                    lista.multiselezioneConsentita = False
                                                    lista.opzioniPulisci()
                                                    lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf AggiungiRimuoviPreferitoServerFtp)
                                                    lista.aggiungiOpzione("Modifica server", AddressOf ModificaServerFtpSelezionato)
                                                    lista.aggiungiOpzione("Elimina server", AddressOf EliminaServerFtpSelezionato)
                                                    lista.aggiungiOpzione("Informazioni server", AddressOf InformazioniServerFtpDaElenco)
                                                    lista.aggiungiOpzione("Aggiorna", AddressOf MostraElencoServerFtp)

                                                        lista.invioNonConsentito = False
                                                        Dim elementiLista As New List(Of ElementoLista)()
                                                        For Each s As InfoServerFtp In salvati
                                                            Dim chiave As String = PREFISSO_FTP_SALVATO & s.Id.ToString()
                                                            ftpServerCache(chiave) = s
                                                            Dim suffissoPassword As String = If(s.SalvaPassword, "", " - password richiesta")
                                                            elementiLista.Add(New ElementoLista(s.Nome & " (" & TestoProtocolloFtp(s.Protocollo) & " - " & s.Host & suffissoPassword & ")", chiave))
                                                        Next
                                                        lista.CaricaListaDinamica(elementiLista)

                                                    suonoAttesa.StopAttesa()
                                                    Try : lista.avvia() : Catch : End Try
                                                Catch exUI As Exception
                                                    ScriviLogCompleto("MostraElencoServerFtp UI Update - Errore", exUI)
                                                End Try
                                            End Sub, Nothing)
                         Catch ex As Exception
                             ScriviLogCompleto("MostraElencoServerFtp background - Errore", ex)
                         End Try
                     End Sub)
        Catch ex As NullReferenceException
            Return
        Catch ex As Exception
            ScriviLogCompleto("MostraElencoServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub AggiornaOpzioniContestualiServerFtp(idElemento As String)
        Try
            If String.IsNullOrEmpty(idElemento) OrElse Not ftpServerCache.ContainsKey(idElemento) Then Return
            Dim s As InfoServerFtp = ftpServerCache(idElemento)
            Dim tokenRichiesta As Long = Interlocked.Increment(_tempTokenOpzioniContestualiFtp)

            Dim t As New Thread(Sub()
                                    Try
                                        Dim ePreferito As Boolean = ftpEngine.EsisteServerPreferito(s.Host, s.Utente, s.Porta, s.Protocollo)
                                        uiContext.Post(Sub(state)
                                                           Try
                                                               If tokenRichiesta <> _tempTokenOpzioniContestualiFtp Then Return
                                                               If lista Is Nothing Then Return
                                                               If ePreferito Then
                                                                   lista.sostituisciOpzione("Aggiungi ai preferiti", "Rimuovi dai preferiti", AddressOf AggiungiRimuoviPreferitoServerFtp)
                                                               Else
                                                                   lista.sostituisciOpzione("Rimuovi dai preferiti", "Aggiungi ai preferiti", AddressOf AggiungiRimuoviPreferitoServerFtp)
                                                               End If
                                                           Catch exUI As Exception
                                                               ScriviLogCompleto("AggiornaOpzioniContestualiServerFtp UI Update - Errore", exUI)
                                                           End Try
                                                       End Sub, Nothing)
                                    Catch ex As Exception
                                        ScriviLogCompleto("AggiornaOpzioniContestualiServerFtp thread - Errore", ex)
                                    End Try
                                End Sub)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("AggiornaOpzioniContestualiServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub AggiungiRimuoviPreferitoServerFtp()
        Try
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim idElemento As String = elementi(0)
            If Not ftpServerCache.ContainsKey(idElemento) Then Return

            Dim s As InfoServerFtp = ftpServerCache(idElemento)
            Dim ePreferito As Boolean = ftpEngine.EsisteServerPreferito(s.Host, s.Utente, s.Porta, s.Protocollo)

            If ePreferito Then
                _tempServerFtpTogglePreferito = s
                domanda = New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf RispostaRimuoviPreferitoServerFtp
                domanda.avvia("Vuoi rimuovere questo server dai preferiti?")
            Else
                Dim serverDaSalvare As InfoServerFtp = CloneServerFtp(s)
                ftpEngine.AggiungiServerPreferito(serverDaSalvare)
                AggiornaOpzioniContestualiServerFtp(idElemento)
            End If
        Catch ex As Exception
            ScriviLogCompleto("AggiungiRimuoviPreferitoServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaRimuoviPreferitoServerFtp(rispostadom As DialogResult)
        Try
            If rispostadom <> DialogResult.Yes Then Return
            If _tempServerFtpTogglePreferito Is Nothing Then Return
            ftpEngine.RimuoviServerPreferitoPerCorrispondenza(_tempServerFtpTogglePreferito.Host, _tempServerFtpTogglePreferito.Utente, _tempServerFtpTogglePreferito.Porta, _tempServerFtpTogglePreferito.Protocollo)
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi IsNot Nothing AndAlso elementi.Count > 0 Then
                AggiornaOpzioniContestualiServerFtp(elementi(0))
            End If
        Catch ex As Exception
            ScriviLogCompleto("RispostaRimuoviPreferitoServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub EliminaServerFtpSelezionato()
        Try
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim idElemento As String = elementi(0)
            If Not idElemento.StartsWith(PREFISSO_FTP_SALVATO) Then Return
            If Not ftpServerCache.ContainsKey(idElemento) Then Return

            ftpEngine.RimuoviServerSalvato(ftpServerCache(idElemento).Id)
            MostraElencoServerFtp()
        Catch ex As Exception
            ScriviLogCompleto("EliminaServerFtpSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub MostraServerPreferitiFtp()
        Try
            If lista Is Nothing Then Return
            AnnullaCaricamentoDirectoryInCorso()
            isFtpBrowseMode = False
            isFtpServerListMode = True
            isFtpElencoServerMode = False
            isFtpMenuPrincipaleMode = False
            isDriveSelectionMode = False
            currentServerFtp = Nothing
            _origineElencoServerFtp = OrigineElencoServerFtp.Preferiti
            ftpHistory.Clear()
            ftpServerCache.Clear()
            suonoAttesa.Attesa()

            Task.Run(Sub()
                         Try
                             Dim preferiti As List(Of InfoServerFtp) = ftpEngine.OttieniServerPreferiti()
                             uiContext.Post(Sub(state)
                                                Try
                                                    If preferiti.Count = 0 Then
                                                        ' niente da elencare: un messaggio, e si resta nel menu dei server
                                                        suonoAttesa.StopAttesa()
                                                        MostraMessaggioSemplice("Non ci sono server preferiti.")
                                                        MostraMenuServerFtp()
                                                        Return
                                                    End If
                                                    lista.pulisci()
                                                    lista.impostaTitolo("Server preferiti")
                                                    lista.multiselezioneConsentita = False
                                                    lista.opzioniPulisci()
                                                    lista.aggiungiOpzione("Modifica server", AddressOf ModificaServerFtpSelezionato)
                                                    lista.aggiungiOpzione("Rimuovi dai preferiti", AddressOf RimuoviServerFtpPreferitoSelezionato)
                                                    lista.aggiungiOpzione("Informazioni server", AddressOf InformazioniServerFtpDaElenco)
                                                    lista.aggiungiOpzione("Aggiorna", AddressOf MostraServerPreferitiFtp)

                                                        lista.invioNonConsentito = False
                                                        Dim elementiLista As New List(Of ElementoLista)()
                                                        For Each s As InfoServerFtp In preferiti
                                                            Dim chiave As String = PREFISSO_FTP_PREFERITO & s.Id.ToString()
                                                            ftpServerCache(chiave) = s
                                                            Dim suffissoPassword As String = If(s.SalvaPassword, "", " - password richiesta")
                                                            elementiLista.Add(New ElementoLista(s.Nome & " (" & TestoProtocolloFtp(s.Protocollo) & " - " & s.Host & suffissoPassword & ")", chiave))
                                                        Next
                                                        lista.CaricaListaDinamica(elementiLista)

                                                    suonoAttesa.StopAttesa()
                                                    Try : lista.avvia() : Catch : End Try
                                                Catch exUI As Exception
                                                    ScriviLogCompleto("MostraServerPreferitiFtp UI Update - Errore", exUI)
                                                End Try
                                            End Sub, Nothing)
                         Catch ex As Exception
                             ScriviLogCompleto("MostraServerPreferitiFtp background - Errore", ex)
                         End Try
                     End Sub)
        Catch ex As NullReferenceException
            Return
        Catch ex As Exception
            ScriviLogCompleto("MostraServerPreferitiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub MostraServerRecentiFtp()
        Try
            If lista Is Nothing Then Return
            AnnullaCaricamentoDirectoryInCorso()
            isFtpBrowseMode = False
            isFtpServerListMode = True
            isFtpElencoServerMode = False
            isFtpMenuPrincipaleMode = False
            isDriveSelectionMode = False
            currentServerFtp = Nothing
            _origineElencoServerFtp = OrigineElencoServerFtp.Recenti
            ftpHistory.Clear()
            ftpServerCache.Clear()
            suonoAttesa.Attesa()

            Task.Run(Sub()
                         Try
                             Dim recenti As List(Of InfoServerFtp) = ftpEngine.OttieniServerRecenti()
                             uiContext.Post(Sub(state)
                                                Try
                                                    If recenti.Count = 0 Then
                                                        ' niente da elencare: un messaggio, e si resta nel menu dei server
                                                        suonoAttesa.StopAttesa()
                                                        MostraMessaggioSemplice("Non ci sono server recenti.")
                                                        MostraMenuServerFtp()
                                                        Return
                                                    End If
                                                    lista.pulisci()
                                                    lista.impostaTitolo("Server recenti")
                                                    lista.multiselezioneConsentita = False
                                                    lista.opzioniPulisci()
                                                    lista.aggiungiOpzione("Aggiungi ai preferiti", AddressOf PromuoviServerRecenteAPreferitoFtp)
                                                    lista.aggiungiOpzione("Modifica server", AddressOf ModificaServerFtpSelezionato)
                                                    lista.aggiungiOpzione("Rimuovi dai recenti", AddressOf RimuoviServerFtpRecenteSelezionato)
                                                    lista.aggiungiOpzione("Svuota recenti", AddressOf SvuotaRecentiFtp)
                                                    lista.aggiungiOpzione("Informazioni server", AddressOf InformazioniServerFtpDaElenco)
                                                    lista.aggiungiOpzione("Aggiorna", AddressOf MostraServerRecentiFtp)

                                                        lista.invioNonConsentito = False
                                                        Dim elementiLista As New List(Of ElementoLista)()
                                                        For Each s As InfoServerFtp In recenti
                                                            Dim chiave As String = PREFISSO_FTP_RECENTE & s.Id.ToString()
                                                            ftpServerCache(chiave) = s
                                                            Dim suffissoPassword As String = If(s.SalvaPassword, "", " - password richiesta")
                                                            elementiLista.Add(New ElementoLista(s.Nome & " (" & TestoProtocolloFtp(s.Protocollo) & " - " & s.Host & suffissoPassword & ")", chiave))
                                                        Next
                                                        lista.CaricaListaDinamica(elementiLista)

                                                    suonoAttesa.StopAttesa()
                                                    Try : lista.avvia() : Catch : End Try
                                                Catch exUI As Exception
                                                    ScriviLogCompleto("MostraServerRecentiFtp UI Update - Errore", exUI)
                                                End Try
                                            End Sub, Nothing)
                         Catch ex As Exception
                             ScriviLogCompleto("MostraServerRecentiFtp background - Errore", ex)
                         End Try
                     End Sub)
        Catch ex As NullReferenceException
            Return
        Catch ex As Exception
            ScriviLogCompleto("MostraServerRecentiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub PromuoviServerRecenteAPreferitoFtp()
        Try
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim idElemento As String = elementi(0)
            If Not idElemento.StartsWith(PREFISSO_FTP_RECENTE) Then Return
            If Not ftpServerCache.ContainsKey(idElemento) Then Return

            Dim serverDaSalvare As InfoServerFtp = CloneServerFtp(ftpServerCache(idElemento))
            ftpEngine.AggiungiServerPreferito(serverDaSalvare)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Server aggiunto ai preferiti.")
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("PromuoviServerRecenteAPreferitoFtp - Errore", ex)
        End Try
    End Sub

    Private Sub SelezionatoServerFtp(idElemento As String)
        Try
            If Not ftpServerCache.ContainsKey(idElemento) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Il server selezionato non è più disponibile.")
                msg.ShowDialog()
                Return
            End If
            Dim server As InfoServerFtp = ftpServerCache(idElemento)
            If idElemento.StartsWith(PREFISSO_FTP_PREFERITO) Then
                _currentServerFtpTipoElenco = TipoElencoServerFtp.Preferito
            ElseIf idElemento.StartsWith(PREFISSO_FTP_RECENTE) Then
                _currentServerFtpTipoElenco = TipoElencoServerFtp.Recente
            ElseIf idElemento.StartsWith(PREFISSO_FTP_SALVATO) Then
                _currentServerFtpTipoElenco = TipoElencoServerFtp.Salvato
            Else
                _currentServerFtpTipoElenco = Nothing
            End If

            If Not server.SalvaPassword Then
                RichiediPasswordServerFtp(server)
                Return
            End If

            AvviaTestEConnessioneServerFtp(server, False)
        Catch ex As Exception
            ScriviLogCompleto("SelezionatoServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub RichiediPasswordServerFtp(server As InfoServerFtp)
        Try
            _tempServerFtpSenzaPassword = server
            _tempSalvaServerFtpConPassword = False
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnPasswordServerFtpInserita
            campi.AggiungiOpzione("Salva il server con la password", AddressOf OpzioneSalvaServerFtpConPassword)
            campi.AggiungiPassword($"Password per il server ""{server.Nome}""", "ftp.passwordrichiesta", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("RichiediPasswordServerFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Opzione del campo della password: il server viene salvato nella configurazione insieme alla password, ma solo dopo che la
    ''' connessione è riuscita, così non si salva una password sbagliata. Scegliendola di nuovo si cambia idea.
    ''' </summary>
    Private Sub OpzioneSalvaServerFtpConPassword(sender As Object, e As EventArgs)
        Try
            _tempSalvaServerFtpConPassword = Not _tempSalvaServerFtpConPassword
            MostraMessaggioSemplice(If(_tempSalvaServerFtpConPassword,
                                       "Dopo la connessione il server verrà salvato nella configurazione, con la password. Scegli di nuovo questa voce per cambiare idea.",
                                       "Il server non verrà salvato."))
        Catch ex As Exception
            ScriviLogCompleto("OpzioneSalvaServerFtpConPassword - Errore", ex)
        End Try
    End Sub

    Private Sub OnPasswordServerFtpInserita()
        Try
            Dim password As String = campi.RecuperaValore("ftp.passwordrichiesta")
            

            If String.IsNullOrEmpty(password) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("La password è obbligatoria per connettersi a questo server.")
                msg.ShowDialog()
                Return
            End If

            Dim serverConPassword As InfoServerFtp = CloneServerFtp(_tempServerFtpSenzaPassword)
            serverConPassword.PasswordCriptata = ftpEngine.CriptaPasswordFtp(password)

            Dim salvaConPassword As Boolean = _tempSalvaServerFtpConPassword
            _tempSalvaServerFtpConPassword = False
            If salvaConPassword Then serverConPassword.SalvaPassword = True

            AvviaTestEConnessioneServerFtp(serverConPassword, salvaConPassword)
        Catch ex As Exception
            ScriviLogCompleto("OnPasswordServerFtpInserita - Errore", ex)
        End Try
    End Sub

    Private Sub RimuoviServerFtpPreferitoSelezionato()
        Try
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim idElemento As String = elementi(0)
            If Not idElemento.StartsWith(PREFISSO_FTP_PREFERITO) Then Return
            If ftpServerCache.ContainsKey(idElemento) Then
                ftpEngine.RimuoviServerPreferito(ftpServerCache(idElemento).Id)
                MostraServerPreferitiFtp()
            End If
        Catch ex As Exception
            ScriviLogCompleto("RimuoviServerFtpPreferitoSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub RimuoviServerFtpRecenteSelezionato()
        Try
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim idElemento As String = elementi(0)
            If Not idElemento.StartsWith(PREFISSO_FTP_RECENTE) Then Return
            If ftpServerCache.ContainsKey(idElemento) Then
                ftpEngine.RimuoviServerRecente(ftpServerCache(idElemento).Id)
                MostraServerRecentiFtp()
            End If
        Catch ex As Exception
            ScriviLogCompleto("RimuoviServerFtpRecenteSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub SvuotaRecentiFtp()
        Try
            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaSvuotaRecentiFtp
            domanda.avvia("Vuoi svuotare l'elenco dei server recenti? I server preferiti non verranno toccati.")
        Catch ex As Exception
            ScriviLogCompleto("SvuotaRecentiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaSvuotaRecentiFtp(rispostadom As DialogResult)
        Try
            If rispostadom <> DialogResult.Yes Then Return
            ftpEngine.SvuotaServerRecenti()
            MostraServerRecentiFtp()
        Catch ex As Exception
            ScriviLogCompleto("RispostaSvuotaRecentiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ModificaServerFtpSelezionato()
        Try
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona un server salvato da modificare.")
                msg.ShowDialog()
                Return
            End If

            Dim idElemento As String = elementi(0)
            Dim tipoElenco As TipoElencoServerFtp
            If idElemento.StartsWith(PREFISSO_FTP_PREFERITO) Then
                tipoElenco = TipoElencoServerFtp.Preferito
            ElseIf idElemento.StartsWith(PREFISSO_FTP_RECENTE) Then
                tipoElenco = TipoElencoServerFtp.Recente
            ElseIf idElemento.StartsWith(PREFISSO_FTP_SALVATO) Then
                tipoElenco = TipoElencoServerFtp.Salvato
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona un server salvato da modificare, non è possibile modificare questa voce.")
                msg.ShowDialog()
                Return
            End If

            If Not ftpServerCache.ContainsKey(idElemento) Then Return

            _tempServerFtpInModifica = ftpServerCache(idElemento)
            _tempTipoElencoServerFtpInModifica = tipoElenco

            Dim sceltaProtocollo As New WGMenuLista()
            sceltaProtocollo.impostaTitolo("Scegli il protocollo del server (attuale: " & TestoProtocolloFtp(_tempServerFtpInModifica.Protocollo) & ")")
            sceltaProtocollo.multiselezioneConsentita = False
            sceltaProtocollo.invioNonConsentito = False
            sceltaProtocollo.aggiungiElemento("FTP", "FTP")
            sceltaProtocollo.aggiungiElemento("FTPS (FTP su SSL/TLS)", "FTPS")
            sceltaProtocollo.aggiungiElemento("SFTP (SSH File Transfer Protocol)", "SFTP")

            AddHandler sceltaProtocollo.ElementoSelezionato, Sub(e As String)
                                                                 Try
                                                                     Dim scelte As List(Of String) = sceltaProtocollo.ottieniElementi()
                                                                     If scelte Is Nothing OrElse scelte.Count = 0 Then Return
                                                                     Dim protocolloScelto As String = scelte(0)
                                                                     sceltaProtocollo.abbandona()
                                                                     ProseguiModificaServerFtp(protocolloScelto)
                                                                 Catch ex As Exception
                                                                     ScriviLogCompleto("ModificaServerFtpSelezionato - Errore selezione protocollo", ex)
                                                                 End Try
                                                             End Sub

            AddHandler sceltaProtocollo.escPremuto, Sub()
                                                        Try
                                                            sceltaProtocollo.abbandona()
                                                            lista.avvia()
                                                        Catch ex As Exception
                                                            ScriviLogCompleto("ModificaServerFtpSelezionato - Errore annullamento", ex)
                                                        End Try
                                                    End Sub

            sceltaProtocollo.avvia()
        Catch ex As Exception
            ScriviLogCompleto("ModificaServerFtpSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub ProseguiModificaServerFtp(protocolloTesto As String)
        Try
            Select Case protocolloTesto
                Case "SFTP"
                    _tempProtocolloNuovoServerFtp = TipoProtocolloFtp.Sftp
                Case "FTPS"
                    _tempProtocolloNuovoServerFtp = TipoProtocolloFtp.Ftps
                Case Else
                    _tempProtocolloNuovoServerFtp = TipoProtocolloFtp.Ftp
            End Select

            Dim passwordChiaro As String = ftpEngine.DecriptaPasswordFtp(_tempServerFtpInModifica.PasswordCriptata)

            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnModificaServerFtpInserito
            campi.Aggiungi("Nome del server", "ftp.nome", True, , _tempServerFtpInModifica.Nome)
            campi.AggiungiHost("Indirizzo del server (host)", "ftp.host", True, , _tempServerFtpInModifica.Host)
            campi.Aggiungi("Nome utente", "ftp.utente", True, , _tempServerFtpInModifica.Utente)
            campi.AggiungiPassword("Password", "ftp.password", True, , passwordChiaro)
            campi.AggiungiNumerico("Porta", "ftp.porta", True, , _tempServerFtpInModifica.Porta.ToString())
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("ProseguiModificaServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnModificaServerFtpInserito()
        Try
            Dim nome As String = campi.RecuperaValore("ftp.nome")
            Dim host As String = campi.RecuperaValore("ftp.host")
            Dim utente As String = campi.RecuperaValore("ftp.utente")
            Dim password As String = campi.RecuperaValore("ftp.password")
            Dim portaTesto As String = campi.RecuperaValore("ftp.porta")
            

            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(utente) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Indirizzo del server e nome utente sono obbligatori.")
                msg.ShowDialog()
                Return
            End If

            Dim porta As Integer = ftpEngine.PortaPredefinita(_tempProtocolloNuovoServerFtp)
            If Not Integer.TryParse(portaTesto, porta) OrElse porta <= 0 OrElse porta > 65535 Then
                Dim msgPorta As New WGMessaggi()
                msgPorta.Messaggio("La porta specificata non è valida.")
                msgPorta.ShowDialog()
                Return
            End If

            _tempServerFtpInModifica.Nome = If(String.IsNullOrWhiteSpace(nome), host, nome)
            _tempServerFtpInModifica.Host = host
            _tempServerFtpInModifica.Porta = porta
            _tempServerFtpInModifica.Utente = utente
            _tempServerFtpInModifica.Protocollo = _tempProtocolloNuovoServerFtp
            _tempServerFtpInModifica.PasswordCriptata = ftpEngine.CriptaPasswordFtp(password)

            AvviaTestEConnessioneModificaServerFtp()
        Catch ex As Exception
            ScriviLogCompleto("OnModificaServerFtpInserito - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaTestEConnessioneModificaServerFtp()
        Try
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Verifica delle modifiche in corso...")
            Dim t As New Thread(AddressOf TestConnessioneModificaFtpThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("AvviaTestEConnessioneModificaServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub TestConnessioneModificaFtpThread()
        Try
            Dim improntaSftp As String = ""
            Dim esito As Boolean = ftpEngine.TestaConnessioneFtp(_tempServerFtpInModifica, improntaSftp)
            If esito Then
                Dim avviso As String = ""
                _tempImprontaSftpCorrente = improntaSftp
                _tempEsitoImprontaSftp = ftpEngine.VerificaImprontaHostSftp(_tempServerFtpInModifica, improntaSftp, avviso)
                _tempAvvisoImprontaSftp = avviso
            Else
                _tempEsitoImprontaSftp = True
                _tempAvvisoImprontaSftp = ""
            End If
            uiContext.Post(AddressOf OnTestConnessioneModificaFtpCompletato, esito)
        Catch ex As Exception
            ScriviLogCompleto("TestConnessioneModificaFtpThread - Errore", ex)
            uiContext.Post(AddressOf OnTestConnessioneModificaFtpCompletato, False)
        End Try
    End Sub

    Private Sub OnTestConnessioneModificaFtpCompletato(state As Object)
        Try
            processoincorso.Fine()
            Dim esito As Boolean = CBool(state)
            If Not esito Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile connettersi con i nuovi dati. Le modifiche non sono state salvate. Controlla indirizzo, nome utente, password e porta.")
                msg.ShowDialog()
                Return
            End If

            If Not _tempEsitoImprontaSftp Then
                domanda = New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf RispostaAvvisoImprontaModificaSftp
                domanda.avvia(_tempAvvisoImprontaSftp & vbCrLf & vbCrLf & "Vuoi salvare comunque?")
                Return
            End If

            ChiediSalvaPasswordModificaFtp()
        Catch ex As Exception
            ScriviLogCompleto("OnTestConnessioneModificaFtpCompletato - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaAvvisoImprontaModificaSftp(rispostadom As DialogResult)
        Try
            If rispostadom <> DialogResult.Yes Then Return
            ftpEngine.AggiornaImprontaHostSftp(_tempServerFtpInModifica, _tempImprontaSftpCorrente)
            ChiediSalvaPasswordModificaFtp()
        Catch ex As Exception
            ScriviLogCompleto("RispostaAvvisoImprontaModificaSftp - Errore", ex)
        End Try
    End Sub

    Private Sub ChiediSalvaPasswordModificaFtp()
        Try
            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaSalvaPasswordModificaFtp
            domanda.avvia("Vuoi salvare la password di questo server per le prossime connessioni? Se scegli no, ti verrà richiesta a ogni connessione.")
        Catch ex As Exception
            ScriviLogCompleto("ChiediSalvaPasswordModificaFtp - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaSalvaPasswordModificaFtp(rispostadom As DialogResult)
        Try
            _tempServerFtpInModifica.SalvaPassword = (rispostadom = DialogResult.Yes)
            SalvaModificheServerFtp()
        Catch ex As Exception
            ScriviLogCompleto("RispostaSalvaPasswordModificaFtp - Errore", ex)
        End Try
    End Sub

    Private Sub SalvaModificheServerFtp()
        Try
            Dim serverDaSalvare As InfoServerFtp = CloneServerFtp(_tempServerFtpInModifica)
            If Not serverDaSalvare.SalvaPassword Then
                serverDaSalvare.PasswordCriptata = ""
            End If

            Dim esito As Boolean
            Select Case _tempTipoElencoServerFtpInModifica
                Case TipoElencoServerFtp.Preferito
                    esito = ftpEngine.AggiornaServerPreferito(serverDaSalvare)
                Case TipoElencoServerFtp.Recente
                    esito = ftpEngine.AggiornaServerRecente(serverDaSalvare)
                Case Else
                    esito = ftpEngine.AggiornaServerSalvato(serverDaSalvare)
            End Select

            If Not esito Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile salvare le modifiche al server.")
                msg.ShowDialog()
                Return
            End If

            Dim msgOk As New WGMessaggi()
            msgOk.Messaggio("Server aggiornato correttamente.")
            msgOk.ShowDialog()
            Select Case _tempTipoElencoServerFtpInModifica
                Case TipoElencoServerFtp.Preferito
                    MostraServerPreferitiFtp()
                Case TipoElencoServerFtp.Recente
                    MostraServerRecentiFtp()
                Case Else
                    MostraElencoServerFtp()
            End Select
        Catch ex As Exception
            ScriviLogCompleto("SalvaModificheServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub NuovoServerFtp()
        Try
            Dim sceltaProtocollo As New WGMenuLista()
            sceltaProtocollo.impostaTitolo("Scegli il protocollo del nuovo server")
            sceltaProtocollo.multiselezioneConsentita = False
            sceltaProtocollo.invioNonConsentito = False
            sceltaProtocollo.aggiungiElemento("FTP", "FTP")
            sceltaProtocollo.aggiungiElemento("FTPS (FTP su SSL/TLS)", "FTPS")
            sceltaProtocollo.aggiungiElemento("SFTP (SSH File Transfer Protocol)", "SFTP")

            AddHandler sceltaProtocollo.ElementoSelezionato, Sub(e As String)
                                                                 Try
                                                                     Dim elementi As List(Of String) = sceltaProtocollo.ottieniElementi()
                                                                     If elementi Is Nothing OrElse elementi.Count = 0 Then Return
                                                                     Dim protocolloScelto As String = elementi(0)
                                                                     sceltaProtocollo.abbandona()
                                                                     ProseguiNuovoServerFtp(protocolloScelto)
                                                                 Catch ex As Exception
                                                                     ScriviLogCompleto("NuovoServerFtp - Errore selezione protocollo", ex)
                                                                 End Try
                                                             End Sub

            AddHandler sceltaProtocollo.escPremuto, Sub()
                                                        Try
                                                            sceltaProtocollo.abbandona()
                                                            lista.avvia()
                                                        Catch ex As Exception
                                                            ScriviLogCompleto("NuovoServerFtp - Errore annullamento scelta protocollo", ex)
                                                        End Try
                                                    End Sub

            sceltaProtocollo.avvia()
        Catch ex As Exception
            ScriviLogCompleto("NuovoServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ProseguiNuovoServerFtp(protocolloTesto As String)
        Try
            Select Case protocolloTesto
                Case "SFTP"
                    _tempProtocolloNuovoServerFtp = TipoProtocolloFtp.Sftp
                Case "FTPS"
                    _tempProtocolloNuovoServerFtp = TipoProtocolloFtp.Ftps
                Case Else
                    _tempProtocolloNuovoServerFtp = TipoProtocolloFtp.Ftp
            End Select

            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnNuovoServerFtpInserito
            campi.Aggiungi("Nome del server", "ftp.nome", True)
            campi.AggiungiHost("Indirizzo del server (host)", "ftp.host", True)
            campi.Aggiungi("Nome utente", "ftp.utente", True)
            campi.AggiungiPassword("Password", "ftp.password", True)
            campi.AggiungiNumerico("Porta", "ftp.porta", True, , ftpEngine.PortaPredefinita(_tempProtocolloNuovoServerFtp).ToString())
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("ProseguiNuovoServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnNuovoServerFtpInserito()
        Try
            Dim campiCorrente As Campi = campi
            If campiCorrente Is Nothing Then Return
            RemoveHandler campiCorrente.Fine, AddressOf OnNuovoServerFtpInserito
            campi = Nothing

            Dim nome As String = campiCorrente.RecuperaValore("ftp.nome")
            Dim host As String = campiCorrente.RecuperaValore("ftp.host")
            Dim utente As String = campiCorrente.RecuperaValore("ftp.utente")
            Dim password As String = campiCorrente.RecuperaValore("ftp.password")
            Dim portaTesto As String = campiCorrente.RecuperaValore("ftp.porta")
            campiCorrente = Nothing

            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(utente) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Indirizzo del server e nome utente sono obbligatori.")
                msg.ShowDialog()
                Return
            End If

            Dim porta As Integer = ftpEngine.PortaPredefinita(_tempProtocolloNuovoServerFtp)
            If Not Integer.TryParse(portaTesto, porta) OrElse porta <= 0 OrElse porta > 65535 Then
                Dim msgPorta As New WGMessaggi()
                msgPorta.Messaggio("La porta specificata non è valida.")
                msgPorta.ShowDialog()
                Return
            End If

            Dim impostazioni As InfoImpostazioniFtp = ftpEngine.OttieniImpostazioniFtp()

            Dim server As New InfoServerFtp() With {
                .Nome = If(String.IsNullOrWhiteSpace(nome), host, nome),
                .Host = host,
                .Porta = porta,
                .Utente = utente,
                .PasswordCriptata = ftpEngine.CriptaPasswordFtp(password),
                .Protocollo = _tempProtocolloNuovoServerFtp,
                .CartellaIniziale = "/",
                .LimiteVelocitaKBs = impostazioni.LimiteVelocitaKBs,
                .SalvaPassword = impostazioni.SalvaSemprePassword
            }

            _currentServerFtpTipoElenco = Nothing
            _tempNuovoServerFtp = server
            AvviaTestEConnessioneServerFtp(server, impostazioni.SalvaSempreServer)
        Catch ex As Exception
            ScriviLogCompleto("OnNuovoServerFtpInserito - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaTestEConnessioneServerFtp(server As InfoServerFtp, salvaServerDopoConnessione As Boolean)
        Try
            SyncLock lockConnessioneFtp
                If connessioneFtpInCorso Then
                    Return
                End If
                connessioneFtpInCorso = True
            End SyncLock

            server.LimiteVelocitaKBs = ftpEngine.OttieniImpostazioniFtp().LimiteVelocitaKBs
            If _tempRitentativoPasswordFtp Then
                _tempRitentativoPasswordFtp = False
            Else
                _tempTentativiPasswordFtp = 0
                _tempPasswordReinseritaFtp = False
            End If
            _tempNuovoServerFtp = server
            _tempChiediSalvataggioServerFtp = salvaServerDopoConnessione
            processoIncorsoConnessioneFtp = New WGProcessoInCorso()
            processoIncorsoConnessioneFtp.Avvia("Connessione al server in corso...")
            Dim t As New Thread(AddressOf TestConnessioneFtpThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            SyncLock lockConnessioneFtp
                connessioneFtpInCorso = False
            End SyncLock
            ScriviLogCompleto("AvviaTestEConnessioneServerFtp - Errore", ex)
        End Try
    End Sub

    Private Function EseguiConAttesaMassima(azione As Action, timeoutMs As Integer) As Boolean
        Dim completato As Boolean = False
        Try
            Dim t As New Thread(Sub()
                                    Try
                                        azione()
                                    Catch exInterno As Exception
                                        ScriviLogCompleto("EseguiConAttesaMassima - Errore nell'azione", exInterno)
                                    Finally
                                        completato = True
                                    End Try
                                End Sub)
            t.IsBackground = True
            t.Start()

            Dim attesaMs As Integer = 0
            While Not completato AndAlso attesaMs < timeoutMs
                Thread.Sleep(200)
                attesaMs += 200
            End While
        Catch ex As Exception
            ScriviLogCompleto("EseguiConAttesaMassima - Errore", ex)
        End Try
        Return completato
    End Function

    Private Sub TestConnessioneFtpThread()
        Try
            ChiudiSessioneFtpTest()

            Dim messaggioErrore As String = ""
            Dim autenticazioneFallita As Boolean = False
            Dim sessioneAperta As SessioneFtp = Nothing
            _tempAutenticazioneFallitaFtp = False
            Dim completato As Boolean = EseguiConAttesaMassima(Sub() sessioneAperta = ftpEngine.ApriSessione(_tempNuovoServerFtp, messaggioErrore, autenticazioneFallita), 25000)

            If Not completato Then
                uiContext.Post(AddressOf OnTestConnessioneFtpCompletato, False)
                Return
            End If

            _tempSessioneFtpTest = sessioneAperta
            Dim esito As Boolean = (sessioneAperta IsNot Nothing)

            If esito Then
                Dim avviso As String = ""
                _tempImprontaSftpCorrente = sessioneAperta.ImprontaSftp
                _tempEsitoImprontaSftp = ftpEngine.VerificaImprontaHostSftp(_tempNuovoServerFtp, sessioneAperta.ImprontaSftp, avviso)
                _tempAvvisoImprontaSftp = avviso
            Else
                _tempEsitoImprontaSftp = True
                _tempAvvisoImprontaSftp = ""
                _tempAutenticazioneFallitaFtp = autenticazioneFallita OrElse CLSFtp.MessaggioIndicaAutenticazione(messaggioErrore)
            End If
            uiContext.Post(AddressOf OnTestConnessioneFtpCompletato, esito)
        Catch ex As Exception
            ScriviLogCompleto("TestConnessioneFtpThread - Errore", ex)
            uiContext.Post(AddressOf OnTestConnessioneFtpCompletato, False)
        End Try
    End Sub

    Private Sub ChiudiSessioneFtpTest()
        Try
            If _tempSessioneFtpTest IsNot Nothing Then
                Dim sessioneDaChiudere As SessioneFtp = _tempSessioneFtpTest
                _tempSessioneFtpTest = Nothing
                Dim completato As Boolean = EseguiConAttesaMassima(Sub() ftpEngine.ChiudiSessione(sessioneDaChiudere), 8000)
                If Not completato Then
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("ChiudiSessioneFtpTest - Errore", ex)
        End Try
    End Sub

    Private Sub OnTestConnessioneFtpCompletato(state As Object)
        Try
            SyncLock lockConnessioneFtp
                connessioneFtpInCorso = False
            End SyncLock

            Dim esito As Boolean = CBool(state)
            If Not esito Then
                If processoIncorsoConnessioneFtp IsNot Nothing Then
                    processoIncorsoConnessioneFtp.Fine()
                    processoIncorsoConnessioneFtp = Nothing
                End If

                If _tempAutenticazioneFallitaFtp AndAlso _tempTentativiPasswordFtp < TENTATIVI_MASSIMI_PASSWORD_FTP Then
                    RichiediNuovaPasswordDopoErroreFtp()
                    Return
                End If

                Dim msg As New WGMessaggi()
                If _tempAutenticazioneFallitaFtp Then
                    msg.Messaggio("Il server continua a rifiutare le credenziali. Controlla nome utente e password e riprova.")
                Else
                    msg.Messaggio("Impossibile connettersi al server. Controlla indirizzo, nome utente, password e porta.")
                End If
                msg.ShowDialog()
                Return
            End If

            _tempTentativiPasswordFtp = 0

            If Not _tempEsitoImprontaSftp Then
                If processoIncorsoConnessioneFtp IsNot Nothing Then
                    processoIncorsoConnessioneFtp.Fine()
                    processoIncorsoConnessioneFtp = Nothing
                End If
                If domandaImprontaFtp IsNot Nothing Then RemoveHandler domandaImprontaFtp.rispostadomanda, AddressOf RispostaAvvisoImprontaSftp
                domandaImprontaFtp = New WGDomanda()
                AddHandler domandaImprontaFtp.rispostadomanda, AddressOf RispostaAvvisoImprontaSftp
                domandaImprontaFtp.avvia(_tempAvvisoImprontaSftp & vbCrLf & vbCrLf & "Vuoi procedere comunque?")
                Return
            End If

            ProseguiDopoConnessioneFtp()
        Catch ex As Exception
            ScriviLogCompleto("OnTestConnessioneFtpCompletato - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaAvvisoImprontaSftp(rispostadom As DialogResult)
        Try
            If domandaImprontaFtp Is Nothing Then Return
            RemoveHandler domandaImprontaFtp.rispostadomanda, AddressOf RispostaAvvisoImprontaSftp
            domandaImprontaFtp = Nothing

            If rispostadom <> DialogResult.Yes Then
                ChiudiSessioneFtpTest()
                Return
            End If
            ftpEngine.AggiornaImprontaHostSftp(_tempNuovoServerFtp, _tempImprontaSftpCorrente)
            ProseguiDopoConnessioneFtp()
        Catch ex As Exception
            ScriviLogCompleto("RispostaAvvisoImprontaSftp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Quando il server rifiuta le credenziali, chiede di reinserire la password e ritenta
    ''' la connessione con gli stessi dati. Lasciando il campo vuoto si rinuncia.
    ''' </summary>
    Private Sub RichiediNuovaPasswordDopoErroreFtp()
        Try
            If _tempNuovoServerFtp Is Nothing Then Return
            _tempTentativiPasswordFtp += 1
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnNuovaPasswordDopoErroreFtpInserita
            campi.AggiungiPassword($"Password non accettata dal server. Reinseriscila per {_tempNuovoServerFtp.Utente}@{_tempNuovoServerFtp.Host}", "ftp.passwordriprova", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("RichiediNuovaPasswordDopoErroreFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnNuovaPasswordDopoErroreFtpInserita()
        Try
            Dim campiCorrente As Campi = campi
            If campiCorrente Is Nothing Then Return
            RemoveHandler campiCorrente.Fine, AddressOf OnNuovaPasswordDopoErroreFtpInserita
            campi = Nothing

            Dim password As String = campiCorrente.RecuperaValore("ftp.passwordriprova")
            campiCorrente = Nothing

            If String.IsNullOrEmpty(password) Then
                _tempTentativiPasswordFtp = 0
                Return
            End If

            Dim serverConPassword As InfoServerFtp = CloneServerFtp(_tempNuovoServerFtp)
            serverConPassword.PasswordCriptata = ftpEngine.CriptaPasswordFtp(password)
            _tempPasswordReinseritaFtp = True
            _tempRitentativoPasswordFtp = True
            AvviaTestEConnessioneServerFtp(serverConPassword, _tempChiediSalvataggioServerFtp)
        Catch ex As Exception
            ScriviLogCompleto("OnNuovaPasswordDopoErroreFtpInserita - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Se la connessione e' andata a buon fine con una password reinserita a mano e il
    ''' server e' fra quelli salvati con la password memorizzata, aggiorna la password
    ''' registrata cosi' che al prossimo accesso non venga richiesta di nuovo.
    ''' </summary>
    Private Sub AggiornaPasswordServerSalvatoFtp()
        Try
            If Not _tempPasswordReinseritaFtp Then Return
            _tempPasswordReinseritaFtp = False
            If _tempNuovoServerFtp Is Nothing Then Return
            If _tempNuovoServerFtp.Id <= 0 OrElse Not _tempNuovoServerFtp.SalvaPassword Then Return
            If Not _currentServerFtpTipoElenco.HasValue OrElse _currentServerFtpTipoElenco.Value <> TipoElencoServerFtp.Salvato Then Return

            Dim serverDaAggiornare As InfoServerFtp = CloneServerFtp(_tempNuovoServerFtp)
            Dim t As New Thread(Sub()
                                    Try
                                        ftpEngine.AggiornaServerSalvato(serverDaAggiornare)
                                    Catch exAggiorna As Exception
                                        ScriviLogCompleto("AggiornaPasswordServerSalvatoFtp thread - Errore", exAggiorna)
                                    End Try
                                End Sub)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("AggiornaPasswordServerSalvatoFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ProseguiDopoConnessioneFtp()
        Try
            AggiornaPasswordServerSalvatoFtp()
            TracciaConnessioneRecenteFtp(_tempNuovoServerFtp)
            If _tempChiediSalvataggioServerFtp Then
                SalvaEApriServerFtp()
            Else
                ApriServerFtp(_tempNuovoServerFtp)
            End If
        Catch ex As Exception
            ScriviLogCompleto("ProseguiDopoConnessioneFtp - Errore", ex)
        End Try
    End Sub

    Private Sub TracciaConnessioneRecenteFtp(server As InfoServerFtp)
        Try
            Dim serverDaTracciare As InfoServerFtp = CloneServerFtp(server)
            serverDaTracciare.SalvaPassword = False
            serverDaTracciare.PasswordCriptata = ""
            ftpEngine.AggiungiServerRecente(serverDaTracciare)
        Catch ex As Exception
            ScriviLogCompleto("TracciaConnessioneRecenteFtp - Errore", ex)
        End Try
    End Sub

    Private Sub SalvaEApriServerFtp()
        Try
            Dim serverDaSalvare As InfoServerFtp = CloneServerFtp(_tempNuovoServerFtp)
            If Not serverDaSalvare.SalvaPassword Then
                serverDaSalvare.PasswordCriptata = ""
            End If
            ftpEngine.AggiungiServerSalvato(serverDaSalvare)
            ApriServerFtp(_tempNuovoServerFtp)
        Catch ex As Exception
            ScriviLogCompleto("SalvaEApriServerFtp - Errore", ex)
        End Try
    End Sub

    Private Function CloneServerFtp(origine As InfoServerFtp) As InfoServerFtp
        Try
            Return New InfoServerFtp() With {
                .Id = origine.Id,
                .Nome = origine.Nome,
                .Host = origine.Host,
                .Porta = origine.Porta,
                .Utente = origine.Utente,
                .PasswordCriptata = origine.PasswordCriptata,
                .Protocollo = origine.Protocollo,
                .CartellaIniziale = origine.CartellaIniziale,
                .LimiteVelocitaKBs = origine.LimiteVelocitaKBs,
                .SalvaPassword = origine.SalvaPassword,
                .UltimaCartella = origine.UltimaCartella
            }
        Catch ex As Exception
            ScriviLogCompleto("CloneServerFtp - Errore", ex)
            Return origine
        End Try
    End Function

    Private Sub ApriServerFtp(server As InfoServerFtp)
        Try
            currentServerFtp = server
            currentFtpExtensionFilter = ""
            ftpHistory.Clear()
            isFtpServerListMode = False
            isFtpElencoServerMode = False

            If _tempSessioneFtpTest Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Sessione non disponibile. Riprova la connessione.")
                msg.ShowDialog()
                MostraMenuServerFtp()
                Return
            End If

            Dim sessionePronta As SessioneFtp = _tempSessioneFtpTest
            _tempSessioneFtpTest = Nothing
            ImpostaSessioneEApriDirectory(server, sessionePronta)
        Catch ex As Exception
            ScriviLogCompleto("ApriServerFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ImpostaSessioneEApriDirectory(server As InfoServerFtp, sessione As SessioneFtp)
        Try
            ChiudiSessioneFtpCorrente()
            currentFtpSession = sessione
            isFtpBrowseMode = True
            isFtpServerListMode = False
            isFtpElencoServerMode = False
            isFtpMenuPrincipaleMode = False
            currentFtpPath = If(Not String.IsNullOrWhiteSpace(server.UltimaCartella), server.UltimaCartella, If(String.IsNullOrWhiteSpace(server.CartellaIniziale), "/", server.CartellaIniziale))
            lista.impostaTitolo("Directory: " & NomeCartellaFtp(currentFtpPath))
            CaricaDirectoryFtp(currentFtpPath)
        Catch ex As Exception
            ScriviLogCompleto("ImpostaSessioneEApriDirectory - Errore", ex)
        End Try
    End Sub

    Private Sub ChiudiSessioneFtpCorrente()
        Try
            FermaMonitorFtp()
            If currentFtpSession IsNot Nothing Then
                Dim sessioneDaChiudere As SessioneFtp = currentFtpSession
                currentFtpSession = Nothing

                If ftpClipboardSessione IsNot Nothing AndAlso ReferenceEquals(sessioneDaChiudere, ftpClipboardSessione) Then
                    ' Sessione "in prestito" a una copia/taglio FTP in sospeso: non va chiusa,
                    ' serve ancora per l'incolla nella cartella locale di destinazione.
                    Return
                End If

                Dim completato As Boolean = EseguiConAttesaMassima(Sub() ftpEngine.ChiudiSessione(sessioneDaChiudere), 8000)
                If Not completato Then
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("ChiudiSessioneFtpCorrente - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Svuota gli appunti FTP (elementi remoti copiati/tagliati). Se la sessione
    ''' associata non e' quella attualmente in uso per la navigazione, viene chiusa.
    ''' </summary>
    Private Sub SvuotaAppuntiFtp(Optional chiudiSessioneSeNonInUso As Boolean = True)
        Try
            If elementiFtp.Count = 0 AndAlso ftpClipboardServer Is Nothing AndAlso ftpClipboardSessione Is Nothing Then Return
            Dim sessioneDaEventualmenteChiudere As SessioneFtp = ftpClipboardSessione
            elementiFtp.Clear()
            ftpClipboardTaglia = False
            ftpClipboardServer = Nothing
            ftpClipboardSessione = Nothing
            If chiudiSessioneSeNonInUso AndAlso sessioneDaEventualmenteChiudere IsNot Nothing AndAlso Not ReferenceEquals(sessioneDaEventualmenteChiudere, currentFtpSession) Then
                Dim completato As Boolean = EseguiConAttesaMassima(Sub() ftpEngine.ChiudiSessione(sessioneDaEventualmenteChiudere), 8000)
                If Not completato Then
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("SvuotaAppuntiFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Da chiamare nei punti in cui il gestore file viene chiuso definitivamente:
    ''' ferma il monitoraggio, svuota gli appunti FTP chiudendo l'eventuale sessione
    ''' in prestito e chiude la sessione FTP di navigazione corrente.
    ''' </summary>
    Private Sub ChiudiTuttoFtpAllUscita()
        Try
            FermaMonitorFtp()
            SvuotaAppuntiFtp(True)
            ChiudiSessioneFtpCorrente()
        Catch ex As Exception
            ScriviLogCompleto("ChiudiTuttoFtpAllUscita - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Restituisce solo il nome dell'ultima cartella del percorso remoto (senza il
    ''' percorso completo e senza prefissi come "FTP"), coerentemente con come viene
    ''' mostrato il nome delle cartelle locali.
    ''' </summary>
    Private Function NomeCartellaFtp(percorso As String) As String
        Try
            If currentServerFtp Is Nothing Then Return percorso
            If String.IsNullOrEmpty(percorso) Then Return currentServerFtp.Nome
            If ftpEngine Is Nothing Then Return percorso

            Dim nome As String = ftpEngine.NomeElementoRemoto(percorso)
            If ftpEngine.EPercorsoRadiceRemoto(percorso) Then
                If String.IsNullOrEmpty(nome) OrElse nome = "/" Then Return currentServerFtp.Nome
                Return currentServerFtp.Nome & " - " & nome
            End If
            If String.IsNullOrEmpty(nome) Then Return currentServerFtp.Nome
            Return nome
        Catch ex As Exception
            ScriviLogCompleto("NomeCartellaFtp - Errore", ex)
            Return percorso
        End Try
    End Function

    ''' <summary>
    ''' Ricava l'estensione di un nome remoto senza usare System.IO.Path, che sui nomi validi
    ''' su Linux ma non su Windows (due punti, asterischi, barre verticali) e sulle voci di
    ''' unita' dei server Windows solleva un'eccezione invece di rispondere.
    ''' </summary>
    Private Function EstensioneRemotaDaNome(nome As String) As String
        Try
            If String.IsNullOrEmpty(nome) Then Return ""
            Dim n As String = nome.Replace("\", "/").TrimEnd("/"c)
            Dim posizioneBarra As Integer = n.LastIndexOf("/"c)
            If posizioneBarra >= 0 Then n = n.Substring(posizioneBarra + 1)
            Dim posizionePunto As Integer = n.LastIndexOf("."c)
            If posizionePunto <= 0 OrElse posizionePunto = n.Length - 1 Then Return ""
            Return n.Substring(posizionePunto).ToLowerInvariant()
        Catch ex As Exception
            ScriviLogCompleto("EstensioneRemotaDaNome - Errore", ex)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Dice se due server configurati puntano alla stessa destinazione, cosi' da capire se
    ''' un incolla fra cartelle remote puo' essere risolto con una rinomina sul server invece
    ''' che scaricando e ricaricando i dati.
    ''' </summary>
    Private Function StessoServerFtp(primo As InfoServerFtp, secondo As InfoServerFtp) As Boolean
        Try
            If primo Is Nothing OrElse secondo Is Nothing Then Return False
            If primo.Protocollo <> secondo.Protocollo Then Return False
            If primo.Porta <> secondo.Porta Then Return False
            If Not String.Equals(primo.Host, secondo.Host, StringComparison.OrdinalIgnoreCase) Then Return False
            If Not String.Equals(primo.Utente, secondo.Utente, StringComparison.Ordinal) Then Return False
            Return True
        Catch ex As Exception
            ScriviLogCompleto("StessoServerFtp - Errore", ex)
            Return False
        End Try
    End Function

    ''' <summary>Avvia (riavviando se necessario) il monitoraggio della cartella FTP indicata.</summary>
    Private Sub AvviaMonitorFtp(percorso As String)
        Try
            FermaMonitorFtp()
            If currentFtpSession Is Nothing OrElse Not currentFtpSession.Connessa Then Return
            ftpMonitor = New MonitorCartellaFtp(ftpEngine, currentFtpSession, percorso, intervalloMs:=5000)
            ftpMonitorPercorso = percorso
            _ftpMonitorAvviatoAlle = DateTime.Now
            AddHandler ftpMonitor.ElencoAggiornato, AddressOf OnFtpMonitorElencoAggiornato
            AddHandler ftpMonitor.ErroreMonitoraggio, AddressOf OnFtpMonitorErrore
            ftpMonitor.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("AvviaMonitorFtp - Errore", ex)
        End Try
    End Sub

    Private Sub FermaMonitorFtp()
        Try
            If ftpMonitor IsNot Nothing Then
                RemoveHandler ftpMonitor.ElencoAggiornato, AddressOf OnFtpMonitorElencoAggiornato
                RemoveHandler ftpMonitor.ErroreMonitoraggio, AddressOf OnFtpMonitorErrore
                ftpMonitor.Dispose()
                ftpMonitor = Nothing
            End If
            ftpMonitorPercorso = ""
        Catch ex As Exception
            ScriviLogCompleto("FermaMonitorFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnFtpMonitorElencoAggiornato(sender As Object, e As ElencoFtpAggiornatoEventArgs)
        Try
            If uiContext Is Nothing Then Return
            uiContext.Post(Sub(s) ApplicaAggiornamentoMonitorFtp(e.PercorsoRemoto, e.Elenco), Nothing)
        Catch ex As Exception
            ScriviLogCompleto("OnFtpMonitorElencoAggiornato - Errore", ex)
        End Try
    End Sub

    Private Sub OnFtpMonitorErrore(sender As Object, e As ErroreMonitoraggioFtpEventArgs)
        ScriviLogCompleto("OnFtpMonitorErrore - " & e.Messaggio, e.Eccezione)
    End Sub

    ''' <summary>
    ''' Applica a video l'elenco ricevuto dal monitor in background. Se l'elemento su
    ''' cui era posizionato l'utente non esiste piu', prova a posizionarsi sull'elemento
    ''' che lo precedeva nell'elenco precedente (e che esiste ancora).
    ''' </summary>
    Private Sub ApplicaAggiornamentoMonitorFtp(percorso As String, elementi As List(Of InfoElementoFtp))
        Try
            If Not isFtpBrowseMode Then Return
            If lista Is Nothing OrElse currentServerFtp Is Nothing Then Return
            If Not String.Equals(percorso, currentFtpPath, StringComparison.Ordinal) Then Return

            If (DateTime.Now - _ftpMonitorAvviatoAlle).TotalMilliseconds < IGNORA_MONITOR_FTP_PRIMI_MS Then Return

            ' Se il monitor riporta esattamente lo stesso elenco già a video (tipico del primo
            ' controllo che il monitor esegue subito dopo essere stato avviato, appena dopo un
            ' caricamento "normale"), non c'è nulla da aggiornare: ripopolare comunque la lista
            ' costringerebbe pulisci() a svuotarla e ricostruirla da capo, con il rischio
            ' che lo screen reader annunci di nuovo il titolo a pochi istanti dal primo annuncio.
            If ElencoFtpInvariato(elementi) Then Return

            Dim idSelezionatoPrima As String = Nothing
            Try
                Dim elementiCorrenti As List(Of String) = lista.ottieniElementi()
                If elementiCorrenti IsNot Nothing AndAlso elementiCorrenti.Count > 0 Then
                    idSelezionatoPrima = elementiCorrenti(0)
                End If
            Catch ex As Exception
                ScriviLogCompleto("ApplicaAggiornamentoMonitorFtp - Errore lettura elemento corrente", ex)
                idSelezionatoPrima = Nothing
            End Try

            Dim idDaEvidenziare As String = ""
            If Not String.IsNullOrEmpty(idSelezionatoPrima) Then
                If elementi.Any(Function(el) el.PercorsoCompleto = idSelezionatoPrima) Then
                    idDaEvidenziare = idSelezionatoPrima
                Else
                    idDaEvidenziare = TrovaElementoFtpPrecedenteAncoraPresente(idSelezionatoPrima, elementi)
                End If
            End If

            PopolaListaFtp(percorso, elementi, idDaEvidenziare)
        Catch ex As Exception
            ScriviLogCompleto("ApplicaAggiornamentoMonitorFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Confronta l'elenco appena ricevuto dal monitor con quello attualmente a video
    ''' (<see cref="_ftpOrdinatiCorrente"/>). Serve a distinguere un vero cambiamento sul
    ''' server da un aggiornamento "a vuoto" (ad es. il primo controllo che il monitor
    ''' esegue non appena avviato), per evitare di ricostruire la lista - e quindi far
    ''' ri-annunciare il titolo dallo screen reader - senza motivo.
    ''' </summary>
    Private Function ElencoFtpInvariato(nuovi As List(Of InfoElementoFtp)) As Boolean
        Try
            Dim nuoviNonNull As List(Of InfoElementoFtp) = If(nuovi, New List(Of InfoElementoFtp)())
            If nuoviNonNull.Count <> _ftpOrdinatiCorrente.Count Then Return False

            Dim mappaCorrente As New Dictionary(Of String, InfoElementoFtp)(StringComparer.Ordinal)
            For Each el As InfoElementoFtp In _ftpOrdinatiCorrente
                mappaCorrente(el.PercorsoCompleto) = el
            Next

            For Each el As InfoElementoFtp In nuoviNonNull
                Dim esistente As InfoElementoFtp = Nothing
                If Not mappaCorrente.TryGetValue(el.PercorsoCompleto, esistente) Then Return False
                If esistente.EDirectory <> el.EDirectory Then Return False
                If esistente.Dimensione <> el.Dimensione Then Return False
                If esistente.DataModifica <> el.DataModifica Then Return False
            Next

            Return True
        Catch ex As Exception
            ScriviLogCompleto("ElencoFtpInvariato - Errore", ex)
            Return False
        End Try
    End Function

    Private Function TrovaElementoFtpPrecedenteAncoraPresente(idNonPiuPresente As String, nuoviElementi As List(Of InfoElementoFtp)) As String
        Try
            Dim indiceVecchio As Integer = _ftpOrdinatiCorrente.FindIndex(Function(el) el.PercorsoCompleto = idNonPiuPresente)
            If indiceVecchio < 0 Then Return ""
            Dim nuoviPercorsi As New HashSet(Of String)(nuoviElementi.Select(Function(el) el.PercorsoCompleto))
            For i As Integer = indiceVecchio - 1 To 0 Step -1
                Dim candidato As String = _ftpOrdinatiCorrente(i).PercorsoCompleto
                If nuoviPercorsi.Contains(candidato) Then Return candidato
            Next
            Return ""
        Catch ex As Exception
            ScriviLogCompleto("TrovaElementoFtpPrecedenteAncoraPresente - Errore", ex)
            Return ""
        End Try
    End Function

    Private Sub IndietroFtp()
        Try
            currentFtpExtensionFilter = ""
            If ftpHistory.Count > 0 Then
                Dim precedente As String = ftpHistory(ftpHistory.Count - 1)
                ftpHistory.RemoveAt(ftpHistory.Count - 1)
                _percorsoDaEvidenziareDopoCaricamentoFtp = currentFtpPath
                lista.impostaTitolo("Directory: " & NomeCartellaFtp(precedente))
                CaricaDirectoryFtp(precedente)
            Else
                TornaAllElencoServerFtp()
            End If
        Catch ex As Exception
            ScriviLogCompleto("IndietroFtp - Errore", ex)
        End Try
    End Sub

    Private Sub CaricaDirectoryFtp(percorso As String)
        Try
            If lista Is Nothing OrElse currentServerFtp Is Nothing Then Return
            suonoAttesa.Attesa()
            uiContext.Post(AddressOf PreparaCaricamentoDirectoryFtp, percorso)
            Dim t As New Thread(Sub() EseguiElencoFtpThread(percorso))
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("CaricaDirectoryFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Svuota la lista e ricostruisce le opzioni subito dopo l'impostazione del titolo,
    ''' esattamente come avviene per le cartelle locali in PreparaCaricamentoDirectory.
    ''' In questo modo lo screen reader annuncia il titolo una sola volta, invece di
    ''' riannunciarlo quando l'elenco remoto arriva dalla rete.
    ''' </summary>
    Private Sub PreparaCaricamentoDirectoryFtp(state As Object)
        Try
            If lista Is Nothing Then Return
            lista.pulisci()
            InizializzaOpzioniFtpBrowse()
        Catch ex As Exception
            ScriviLogCompleto("PreparaCaricamentoDirectoryFtp - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiElencoFtpThread(percorso As String)
        Try
            If currentFtpSession IsNot Nothing AndAlso Not currentFtpSession.Connessa Then
                Dim messaggioErrore As String = ""
                Dim nuovaSessione As SessioneFtp = ftpEngine.ApriSessione(currentServerFtp, messaggioErrore)
                ChiudiSessioneFtpCorrente()
                currentFtpSession = nuovaSessione
                If currentFtpSession Is Nothing Then
                    uiContext.Post(Sub(s) CompletaCaricamentoDirectoryFtp(percorso, New List(Of InfoElementoFtp)()), Nothing)
                    Return
                End If
            End If

            Dim elementi As List(Of InfoElementoFtp) = ftpEngine.ElencaFileECartelle(currentFtpSession, percorso, currentFtpExtensionFilter)
            uiContext.Post(Sub(s) CompletaCaricamentoDirectoryFtp(percorso, elementi), Nothing)
            Task.Run(Sub() AggiornaUltimaCartellaServerCorrente(percorso))
        Catch ex As Exception
            ScriviLogCompleto($"EseguiElencoFtpThread - Errore '{percorso}'", ex)
            uiContext.Post(Sub(s) CompletaCaricamentoDirectoryFtp(percorso, New List(Of InfoElementoFtp)()), Nothing)
        End Try
    End Sub

    Private Sub AggiornaUltimaCartellaServerCorrente(percorso As String)
        Try
            If currentServerFtp Is Nothing OrElse currentServerFtp.Id <= 0 Then Return
            If Not _currentServerFtpTipoElenco.HasValue Then Return
            Select Case _currentServerFtpTipoElenco.Value
                Case TipoElencoServerFtp.Preferito
                    ftpEngine.AggiornaUltimaCartellaPreferito(currentServerFtp.Id, percorso)
                Case TipoElencoServerFtp.Recente
                    ftpEngine.AggiornaUltimaCartellaRecente(currentServerFtp.Id, percorso)
                Case TipoElencoServerFtp.Salvato
                    ftpEngine.AggiornaUltimaCartellaSalvato(currentServerFtp.Id, percorso)
            End Select
        Catch ex As Exception
            ScriviLogCompleto("AggiornaUltimaCartellaServerCorrente - Errore", ex)
        End Try
    End Sub

    Private Sub CompletaCaricamentoDirectoryFtp(percorso As String, elementi As List(Of InfoElementoFtp))
        Dim idDaEvidenziare As String = _percorsoDaEvidenziareDopoCaricamentoFtp
        _percorsoDaEvidenziareDopoCaricamentoFtp = ""
        Try
            suonoAttesa.StopAttesa()
            If lista Is Nothing Then
                If processoIncorsoConnessioneFtp IsNot Nothing Then
                    processoIncorsoConnessioneFtp.Fine()
                    processoIncorsoConnessioneFtp = Nothing
                End If
                Return
            End If

            PopolaListaFtp(percorso, elementi, idDaEvidenziare, False)
            AvviaMonitorFtp(percorso)

            If processoIncorsoConnessioneFtp IsNot Nothing Then
                processoIncorsoConnessioneFtp.Fine()
                processoIncorsoConnessioneFtp = Nothing
            End If
        Catch ex As Exception
            ScriviLogCompleto($"CompletaCaricamentoDirectoryFtp - Errore '{percorso}'", ex)
            If processoIncorsoConnessioneFtp IsNot Nothing Then
                processoIncorsoConnessioneFtp.Fine()
                processoIncorsoConnessioneFtp = Nothing
            End If
        End Try
    End Sub

    ''' <summary>
    ''' Costruisce a video l'elenco della cartella FTP corrente, con lo stesso meccanismo
    ''' usato per le cartelle locali in CompletaCaricamentoDirectory: gli elementi vengono
    ''' aggiunti uno alla volta con aggiungiElemento, senza ricostruire la lista da capo.
    ''' Il titolo NON viene toccato qui: va impostato dal chiamante prima di avviare il
    ''' caricamento (stesso schema usato per le cartelle locali in CartellaSuccessiva/
    ''' CartellaPrecedente), cosi' risulta gia' corretto ovunque venga letto nel frattempo.
    ''' Con preparaLista a False si da' per scontato che pulisci e opzioni siano gia' stati
    ''' eseguiti da PreparaCaricamentoDirectoryFtp all'avvio del caricamento.
    ''' </summary>
    Private Sub PopolaListaFtp(percorso As String, elementi As List(Of InfoElementoFtp), idDaEvidenziare As String, Optional preparaLista As Boolean = True)
        Try
            currentFtpPath = percorso
            ftpElementCache.Clear()
            If preparaLista Then
                lista.pulisci()
                InizializzaOpzioniFtpBrowse()
            End If

            If elementi Is Nothing OrElse elementi.Count = 0 Then
                _ftpOrdinatiCorrente = New List(Of InfoElementoFtp)()
                lista.aggiungiElemento("La cartella è vuota")
                lista.invioNonConsentito = True
            Else
                lista.invioNonConsentito = False
                Dim ordinati As List(Of InfoElementoFtp) = elementi.OrderByDescending(Function(el) el.EDirectory).ThenBy(Function(el) el.Nome, StringComparer.OrdinalIgnoreCase).ToList()
                _ftpOrdinatiCorrente = ordinati
                For Each el As InfoElementoFtp In ordinati
                    Try
                        ftpElementCache(el.PercorsoCompleto) = el
                        lista.aggiungiElemento(el.Nome, el.PercorsoCompleto)
                    Catch ex As Exception
                        ScriviLogCompleto($"PopolaListaFtp - Errore aggiunta elemento '{el.Nome}'", ex)
                    End Try
                Next
            End If

            If Not String.IsNullOrEmpty(idDaEvidenziare) Then
                Try
                    lista.posiziona(idDaEvidenziare)
                Catch exPos As Exception
                    ScriviLogCompleto("PopolaListaFtp - Errore posizionamento elemento", exPos)
                End Try
            End If
        Catch ex As Exception
            ScriviLogCompleto($"PopolaListaFtp - Errore '{percorso}'", ex)
        End Try
    End Sub

    Private Sub InizializzaOpzioniFtpBrowse()
        Try
            lista.opzioniPulisci()
            lista.multiselezioneConsentita = True
            lista.aggiungiOpzione("Apri", AddressOf ApriElementoFtpSelezionato)
            lista.aggiungiOpzione("Copia", AddressOf CopiaElementoFtpSelezionato)
            lista.aggiungiOpzione("Taglia", AddressOf TagliaElementoFtpSelezionato)
            lista.aggiungiOpzione("Incolla", AddressOf IncollaFtp)
            lista.aggiungiOpzione("Crea nuova cartella", AddressOf CreaCartellaFtpEvent)
            lista.aggiungiOpzione("Rinomina", AddressOf RinominaElementoFtpSelezionato)
            lista.aggiungiOpzione("Elimina", AddressOf EliminaElementoFtpSelezionato)
            lista.aggiungiOpzione("Filtra per estensione", AddressOf MostraMenuEstensioniFtp)
            lista.aggiungiOpzione("Informazioni", AddressOf InformazioniFtp)
            lista.aggiungiOpzione("Informazioni server", AddressOf InformazioniServerFtp)
            lista.aggiungiOpzione("Modifica permessi", AddressOf ModificaPermessiElementoFtpSelezionato)
            lista.aggiungiOpzione("Aggiorna", AddressOf AggiornaCartellaFtpCorrente)
            lista.aggiungiOpzione("Torna alla lista server", AddressOf TornaAllElencoServerFtp)
        Catch ex As Exception
            ScriviLogCompleto("InizializzaOpzioniFtpBrowse - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMenuEstensioniFtp()
        Try
            If currentServerFtp Is Nothing Then Return
            Dim percorsoAlMomentoDellaScelta As String = currentFtpPath
            suonoAttesa.Attesa()
            Dim t As New Thread(Sub() EseguiRecuperoEstensioniFtpThread(percorsoAlMomentoDellaScelta))
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("MostraMenuEstensioniFtp - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiRecuperoEstensioniFtpThread(percorso As String)
        Try
            Dim elementi As List(Of InfoElementoFtp) = ftpEngine.ElencaFileECartelle(currentFtpSession, percorso, "")
            uiContext.Post(Sub(s) CompletaMenuEstensioniFtp(percorso, elementi), Nothing)
        Catch ex As Exception
            ScriviLogCompleto($"EseguiRecuperoEstensioniFtpThread - Errore '{percorso}'", ex)
            uiContext.Post(Sub(s) CompletaMenuEstensioniFtp(percorso, New List(Of InfoElementoFtp)()), Nothing)
        End Try
    End Sub

    Private Sub CompletaMenuEstensioniFtp(percorso As String, elementi As List(Of InfoElementoFtp))
        Try
            suonoAttesa.StopAttesa()
            Dim estensioni As New HashSet(Of String)
            For Each el As InfoElementoFtp In elementi
                If Not el.EDirectory Then
                    estensioni.Add(EstensioneRemotaDaNome(el.Nome))
                End If
            Next

            Dim sceltaEstensioni As New WGMenuLista()
            sceltaEstensioni.impostaTitolo("Scegli una o più estensioni")
            sceltaEstensioni.multiselezioneConsentita = True
            sceltaEstensioni.aggiungiElemento("Mostra tutti", "*")
            For Each ext In estensioni.OrderBy(Function(x) x)
                If Not String.IsNullOrEmpty(ext) Then
                    sceltaEstensioni.aggiungiElemento(ext, ext)
                End If
            Next

            AddHandler sceltaEstensioni.ElementoSelezionato, Sub(e As String)
                                                                 Try
                                                                     Dim scelte As List(Of String) = sceltaEstensioni.ottieniElementi()
                                                                     If scelte Is Nothing OrElse scelte.Count = 0 Then Return
                                                                     sceltaEstensioni.abbandona()
                                                                     currentFtpExtensionFilter = If(scelte.Contains("*"), "", String.Join("|", scelte))
                                                                     CaricaDirectoryFtp(percorso)
                                                                 Catch ex As Exception
                                                                     ScriviLogCompleto("MostraMenuEstensioniFtp - Errore selezione", ex)
                                                                 End Try
                                                             End Sub

            ' Con multiselezioneConsentita = True la conferma di piu' estensioni spuntate
            ' arriva tramite ElementiSelezionati (non ElementoSelezionato, che riguarda la
            ' singola voce): senza questo handler, spuntare piu' estensioni e confermare
            ' non produceva alcun effetto.
            AddHandler sceltaEstensioni.ElementiSelezionati, Sub(idsEstensioni As List(Of String))
                                                                  Try
                                                                      Dim scelte As List(Of String) = idsEstensioni
                                                                      If scelte Is Nothing OrElse scelte.Count = 0 Then Return
                                                                      sceltaEstensioni.abbandona()
                                                                      currentFtpExtensionFilter = If(scelte.Contains("*"), "", String.Join("|", scelte))
                                                                      CaricaDirectoryFtp(percorso)
                                                                  Catch ex As Exception
                                                                      ScriviLogCompleto("MostraMenuEstensioniFtp - Errore selezione multipla", ex)
                                                                  End Try
                                                              End Sub

            AddHandler sceltaEstensioni.escPremuto, Sub()
                                                        Try
                                                            sceltaEstensioni.abbandona()
                                                            lista.avvia()
                                                        Catch ex As Exception
                                                            ScriviLogCompleto("MostraMenuEstensioniFtp - Errore annullamento", ex)
                                                        End Try
                                                    End Sub

            sceltaEstensioni.avvia()
        Catch ex As Exception
            ScriviLogCompleto("CompletaMenuEstensioniFtp - Errore", ex)
        End Try
    End Sub

    Private Sub SelezionatoFtp(idElemento As String)
        Try
            NavigaOAgisciElementoFtp(idElemento, False)
        Catch ex As Exception
            ScriviLogCompleto("SelezionatoFtp - Errore", ex)
        End Try
    End Sub

    Private Sub NavigaOAgisciElementoFtp(idElemento As String, Optional forzaScelta As Boolean = False)
        Try
            If Not ftpElementCache.ContainsKey(idElemento) Then Return
            Dim elemento As InfoElementoFtp = ftpElementCache(idElemento)
            If elemento.EDirectory Then
                ftpHistory.Add(currentFtpPath)
                currentFtpExtensionFilter = ""
                lista.impostaTitolo("Directory: " & NomeCartellaFtp(elemento.PercorsoCompleto))
                CaricaDirectoryFtp(elemento.PercorsoCompleto)
            Else
                ApriFileFtp(elemento, forzaScelta)
            End If
        Catch ex As Exception
            ScriviLogCompleto("NavigaOAgisciElementoFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Scarica il file dal server in una cartella temporanea e lo apre con lo stesso
    ''' meccanismo usato per i file locali (editor di testo, player multimediale,
    ''' decompressione archivi, applicazione predefinita di sistema, ecc.), invece di
    ''' aprirlo sempre e solo con l'applicazione predefinita.
    ''' </summary>
    Private Sub ApriFileFtp(elemento As InfoElementoFtp, Optional forzaScelta As Boolean = False)
        Try
            If currentServerFtp Is Nothing Then Return
            ' Cattura server e cartella remota "di casa" del file: serviranno più avanti
            ' per ricaricarlo sul server dopo un salvataggio nell'editor, indipendentemente
            ' da dove l'utente si sposterà nel frattempo nella navigazione FTP.
            Dim serverOrigine As InfoServerFtp = currentServerFtp
            Dim cartellaRemotaOrigine As String = currentFtpPath
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Scaricamento e apertura di """ & elemento.Nome & """ in corso...")
            Dim t As New Thread(Sub() EseguiApriFileFtpThread(elemento, forzaScelta, serverOrigine, cartellaRemotaOrigine))
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("ApriFileFtp - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiApriFileFtpThread(elemento As InfoElementoFtp, forzaScelta As Boolean, serverOrigine As InfoServerFtp, cartellaRemotaOrigine As String)
        Try
            Dim cartellaTemp As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinGabriel_Ftp_Temp")
            If Not System.IO.Directory.Exists(cartellaTemp) Then
                System.IO.Directory.CreateDirectory(cartellaTemp)
            End If
            Dim percorsoLocale As String = System.IO.Path.Combine(cartellaTemp, elemento.Nome)

            Dim standardError As String = ""
            Dim esito As Boolean = ftpEngine.ScaricaFile(currentFtpSession, elemento.PercorsoCompleto, percorsoLocale, standardError)

            uiContext.Post(Sub(s) CompletaApriFileFtp(esito, percorsoLocale, standardError, forzaScelta, serverOrigine, cartellaRemotaOrigine), Nothing)
        Catch ex As Exception
            ScriviLogCompleto("EseguiApriFileFtpThread - Errore", ex)
            uiContext.Post(Sub(s) CompletaApriFileFtp(False, "", ex.Message, forzaScelta, serverOrigine, cartellaRemotaOrigine), Nothing)
        End Try
    End Sub

    Private Sub CompletaApriFileFtp(esito As Boolean, percorsoLocale As String, erroreTesto As String, forzaScelta As Boolean, serverOrigine As InfoServerFtp, cartellaRemotaOrigine As String)
        Try
            processoincorso.Fine()
            If Not esito Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile scaricare il file per l'apertura: " & erroreTesto)
                msg.ShowDialog()
                Return
            End If
            ' Stesso meccanismo di apertura dei file locali: editor di testo, player
            ' multimediale, decompressione archivi o applicazione predefinita di sistema.
            ' Server e cartella remota di origine vengono passati solo per abilitare, nel
            ' caso dei file di testo, il ricaricamento automatico sul server dopo il
            ' salvataggio (vedi ApriEditor/OnFileSalvatoEditor); per gli altri tipi di file
            ' vengono semplicemente ignorati più a valle.
            If forzaScelta Then
                ApriConScelta(percorsoLocale, serverOrigine, cartellaRemotaOrigine)
            Else
                Apri(percorsoLocale, serverOrigine, cartellaRemotaOrigine)
            End If
        Catch ex As Exception
            ScriviLogCompleto("CompletaApriFileFtp - Errore", ex)
        End Try
    End Sub

    Private Sub ApriElementoFtpSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            NavigaOAgisciElementoFtp(elementiSelezionati(0), True)
        Catch ex As Exception
            ScriviLogCompleto("ApriElementoFtpSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub AggiornaCartellaFtpCorrente()
        Try
            CaricaDirectoryFtp(currentFtpPath)
        Catch ex As Exception
            ScriviLogCompleto("AggiornaCartellaFtpCorrente - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' "Copia" su elemento/i remoto/i selezionato/i: identico nel comportamento al
    ''' "copia" locale. Non scarica subito nulla: memorizza i percorsi remoti e tiene
    ''' viva la sessione FTP, in attesa che l'utente scelga "Incolla" nella cartella
    ''' locale di destinazione (gestione file).
    ''' </summary>
    Private Sub CopiaElementoFtpSelezionato()
        Try
            PreparaAppuntiFtp(False)
        Catch ex As Exception
            ScriviLogCompleto("CopiaElementoFtpSelezionato - Errore", ex)
        End Try
    End Sub

    ''' <summary>"Taglia" su elemento/i remoto/i selezionato/i: come Copia, ma l'elemento remoto viene eliminato dopo l'incolla.</summary>
    Private Sub TagliaElementoFtpSelezionato()
        Try
            PreparaAppuntiFtp(True)
        Catch ex As Exception
            ScriviLogCompleto("TagliaElementoFtpSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub PreparaAppuntiFtp(eliminaSorgenteRemotaDopoIncolla As Boolean)
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Seleziona prima uno o più elementi.")
                messaggio.ShowDialog()
                Return
            End If
            If currentServerFtp Is Nothing OrElse currentFtpSession Is Nothing OrElse Not currentFtpSession.Connessa Then Return

            ' Gli appunti locali e quelli FTP sono mutuamente esclusivi, come sul filesystem locale.
            elementi.Clear()
            taglia = False
            copia = False

            SvuotaAppuntiFtp(True)
            elementiFtp.Clear()
            elementiFtp.AddRange(elementiSelezionati)
            ftpClipboardTaglia = eliminaSorgenteRemotaDopoIncolla
            ftpClipboardServer = currentServerFtp
            ftpClipboardSessione = currentFtpSession
        Catch ex As Exception
            ScriviLogCompleto("PreparaAppuntiFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' "Incolla" durante la navigazione FTP: carica sul server, nella cartella
    ''' corrente, gli elementi locali copiati/tagliati in precedenza (stesso
    ''' meccanismo del copia/taglia/incolla locale).
    ''' </summary>
    Private Sub IncollaFtp()
        Try
            If currentServerFtp Is Nothing OrElse currentFtpSession Is Nothing OrElse Not currentFtpSession.Connessa Then Return

            If elementiFtp.Count > 0 AndAlso ftpClipboardServer IsNot Nothing Then
                IncollaElementiRemotiFtp()
                Return
            End If

            Dim elementiDaCaricare As New List(Of String)
            Dim isClipboardSystem As Boolean = False
            For i As Integer = 1 To 5
                Try
                    IncollaDaClipboardAction(elementiDaCaricare, isClipboardSystem)
                    Exit For
                Catch ex As Exception
                    log.ScriviLog($"IncollaFtp - Tentativo {i}/5 lettura clipboard fallito: {ex.Message}")
                    Thread.Sleep(100)
                End Try
            Next
            If elementiDaCaricare.Count = 0 AndAlso elementi.Count > 0 Then
                elementiDaCaricare.AddRange(elementi)
                isClipboardSystem = False
            End If
            If elementiDaCaricare.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun elemento negli appunti da incollare.")
                msg.ShowDialog()
                Return
            End If
            Dim sorgentiValide As List(Of String) = elementiDaCaricare.Where(Function(p) Not logic.IsWpdPath(p) AndAlso (WGRete.EsisteFile(p) OrElse WGRete.EsisteDirectory(p))).ToList()
            If sorgentiValide.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun file o cartella locale valida da caricare.")
                msg.ShowDialog()
                Return
            End If

            Dim eliminaSorgenteLocale As Boolean = taglia
            WGCentroDiControllo.GetInstance().AggiungiUploadFtp(sorgentiValide, currentFtpPath, currentServerFtp, eliminaSorgenteLocale)

            If eliminaSorgenteLocale AndAlso Not isClipboardSystem Then
                taglia = False
                elementi.Clear()
            End If
        Catch ex As Exception
            ScriviLogCompleto("IncollaFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Incolla nella cartella remota corrente gli elementi remoti copiati o tagliati in
    ''' precedenza. Quando origine e destinazione sono sullo stesso server, sia il taglio
    ''' che la copia vengono messi in coda come lavoro FTP in background (vedi
    ''' AggiungiSpostaRemotoFtp/AggiungiCopiaRemotoFtp): compaiono nel Centro di Controllo
    ''' con percentuale, velocità e tempo residuo, esattamente come un upload o un download.
    ''' Se invece origine e destinazione sono su server diversi, il protocollo non prevede
    ''' un trasferimento diretto fra due server, quindi l'utente viene avvisato di passare
    ''' per una cartella locale.
    ''' </summary>
    Private Sub IncollaElementiRemotiFtp()
        Try
            If elementiFtp.Count = 0 OrElse ftpClipboardServer Is Nothing Then Return

            If Not StessoServerFtp(ftpClipboardServer, currentServerFtp) Then
                Dim messaggioAltroServer As New WGMessaggi()
                messaggioAltroServer.Messaggio("Gli elementi negli appunti sono su un altro server. Per spostarli scaricali prima in una cartella locale e poi caricali sul server di destinazione.")
                messaggioAltroServer.ShowDialog()
                Return
            End If

            Dim daElaborare As New List(Of String)(elementiFtp)
            Dim destinazione As String = currentFtpPath

            If ftpClipboardTaglia Then
                WGCentroDiControllo.GetInstance().AggiungiSpostaRemotoFtp(daElaborare, destinazione, ftpClipboardServer)
            Else
                WGCentroDiControllo.GetInstance().AggiungiCopiaRemotoFtp(daElaborare, destinazione, ftpClipboardServer)
            End If

            SvuotaAppuntiFtp(True)
        Catch ex As Exception
            ScriviLogCompleto("IncollaElementiRemotiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub CreaCartellaFtpEvent()
        Try
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnNomeCartellaFtpInserito
            campi.Aggiungi("Scrivi il nome della nuova cartella", "ftp.cartellanuova", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("CreaCartellaFtpEvent - Errore", ex)
        End Try
    End Sub

    Private Sub OnNomeCartellaFtpInserito()
        Try
            Dim nome As String = campi.RecuperaValore("ftp.cartellanuova")
            
            If String.IsNullOrWhiteSpace(nome) Then Return
            Dim percorsoNuovo As String = currentFtpPath.TrimEnd("/"c) & "/" & nome
            Dim t As New Thread(Sub() EseguiCreaCartellaFtpThread(percorsoNuovo))
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("OnNomeCartellaFtpInserito - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiCreaCartellaFtpThread(percorsoNuovo As String)
        Try
            Dim esito As Boolean = ftpEngine.CreaCartellaFtp(currentFtpSession, percorsoNuovo)
            uiContext.Post(Sub(s) OnOperazioneFtpSempliceCompletata(esito, "Impossibile creare la cartella sul server FTP."), Nothing)
        Catch ex As Exception
            ScriviLogCompleto("EseguiCreaCartellaFtpThread - Errore", ex)
            uiContext.Post(Sub(s) OnOperazioneFtpSempliceCompletata(False, "Impossibile creare la cartella sul server FTP."), Nothing)
        End Try
    End Sub

    Private Sub OnOperazioneFtpSempliceCompletata(esito As Boolean, messaggioErrore As String)
        Try
            If Not esito Then
                Dim msg As New WGMessaggi()
                msg.Messaggio(messaggioErrore)
                msg.ShowDialog()
            End If
            CaricaDirectoryFtp(currentFtpPath)
        Catch ex As Exception
            ScriviLogCompleto("OnOperazioneFtpSempliceCompletata - Errore", ex)
        End Try
    End Sub

    Private Sub RinominaElementoFtpSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona prima un elemento da rinominare.")
                msg.ShowDialog()
                Return
            End If
            _tempElementoFtpDaRinominare = elementiSelezionati(0)
            Dim nomeAttuale As String = If(ftpElementCache.ContainsKey(_tempElementoFtpDaRinominare), ftpElementCache(_tempElementoFtpDaRinominare).Nome, System.IO.Path.GetFileName(_tempElementoFtpDaRinominare))
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnNuovoNomeFtpInserito
            campi.Aggiungi("Scrivi il nuovo nome per " & nomeAttuale, "ftp.nuovonome", True, , nomeAttuale)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("RinominaElementoFtpSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub OnNuovoNomeFtpInserito()
        Try
            Dim nuovoNome As String = campi.RecuperaValore("ftp.nuovonome")
            If String.IsNullOrWhiteSpace(nuovoNome) OrElse String.IsNullOrEmpty(_tempElementoFtpDaRinominare) Then Return
            Dim percorsoNuovo As String = currentFtpPath.TrimEnd("/"c) & "/" & nuovoNome
            If String.Equals(percorsoNuovo, _tempElementoFtpDaRinominare, StringComparison.Ordinal) Then Return
            Dim t As New Thread(AddressOf EseguiRinominaFtpThread)
            t.IsBackground = True
            t.Start(Tuple.Create(_tempElementoFtpDaRinominare, percorsoNuovo))
        Catch ex As Exception
            ScriviLogCompleto("OnNuovoNomeFtpInserito - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiRinominaFtpThread(state As Object)
        Dim esito As Boolean = False
        Try
            Dim coppia As Tuple(Of String, String) = TryCast(state, Tuple(Of String, String))
            If coppia IsNot Nothing Then
                esito = ftpEngine.RinominaFtp(currentFtpSession, coppia.Item1, coppia.Item2)
                If Not esito Then log.ScriviLog($"EseguiRinominaFtpThread - Rinomina non riuscita di '{coppia.Item1}' in '{coppia.Item2}'")
            End If
        Catch ex As Exception
            esito = False
            ScriviLogCompleto("EseguiRinominaFtpThread - Errore", ex)
        Finally
            Try
                uiContext.Post(AddressOf OnRinominaFtpTerminata, esito)
            Catch exPost As Exception
                ScriviLogCompleto("EseguiRinominaFtpThread - Errore notifica interfaccia", exPost)
            End Try
        End Try
    End Sub

    Private Sub OnRinominaFtpTerminata(state As Object)
        Try
            OnOperazioneFtpSempliceCompletata(CBool(state), "Impossibile rinominare l'elemento sul server FTP.")
        Catch ex As Exception
            ScriviLogCompleto("OnRinominaFtpTerminata - Errore", ex)
        End Try
    End Sub

    Private Sub EliminaElementoFtpSelezionato()
        Try
            Dim elementi As List(Of String) = lista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona prima uno o più elementi da eliminare.")
                msg.ShowDialog()
                Return
            End If
            _tempPercorsiFtpSelezionati = elementi
            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaEliminaFtp
            domanda.avvia("Sei sicuro di voler eliminare gli elementi selezionati dal server FTP? L'operazione non è reversibile.")
        Catch ex As Exception
            ScriviLogCompleto("EliminaElementoFtpSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaEliminaFtp(rispostadom As DialogResult)
        Try
            If rispostadom <> DialogResult.Yes Then Return
            Dim t As New Thread(AddressOf EseguiEliminaFtpThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("RispostaEliminaFtp - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiEliminaFtpThread()
        Try
            Dim tuttoOk As Boolean = True
            For Each percorso As String In _tempPercorsiFtpSelezionati
                Dim elemento As InfoElementoFtp = Nothing
                ftpElementCache.TryGetValue(percorso, elemento)
                If elemento IsNot Nothing AndAlso elemento.EDirectory Then
                    If Not ftpEngine.EliminaCartellaFtpRicorsiva(currentFtpSession, percorso) Then tuttoOk = False
                Else
                    If Not ftpEngine.EliminaFileFtp(currentFtpSession, percorso) Then tuttoOk = False
                End If
            Next
            uiContext.Post(Sub(s) OnOperazioneFtpSempliceCompletata(tuttoOk, "Impossibile eliminare uno o più elementi dal server FTP."), Nothing)
        Catch ex As Exception
            ScriviLogCompleto("EseguiEliminaFtpThread - Errore", ex)
            uiContext.Post(Sub(s) OnOperazioneFtpSempliceCompletata(False, "Impossibile eliminare uno o più elementi dal server FTP."), Nothing)
        End Try
    End Sub

    Private Sub InformazioniFtp()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Per visualizzare le informazioni, devi prima selezionare un elemento dalla lista.")
                msg.ShowDialog()
                Return
            End If
            Dim elementiRemoti As New List(Of InfoElementoFtp)()
            For Each idElemento As String In elementiSelezionati
                If String.IsNullOrEmpty(idElemento) Then Continue For
                If ftpElementCache.ContainsKey(idElemento) Then elementiRemoti.Add(ftpElementCache(idElemento))
            Next
            If elementiRemoti.Count = 0 Then Return

            Dim infoLines As New List(Of String)()
            Dim indice As Integer = 0
            For Each elemento As InfoElementoFtp In elementiRemoti
                indice += 1
                Try
                    If elementiRemoti.Count > 1 Then infoLines.Add($"Elemento {indice} di {elementiRemoti.Count}")
                    infoLines.Add("Nome: " & elemento.Nome)
                    infoLines.Add("Percorso: " & elemento.PercorsoCompleto)
                    infoLines.Add("Tipo: " & If(elemento.EDirectory, "Cartella", "File"))
                    If Not elemento.EDirectory Then
                        infoLines.Add("Dimensione: " & FormattaDimensioneFtp(elemento.Dimensione))
                    End If
                    If elemento.DataModifica <> DateTime.MinValue Then
                        infoLines.Add("Data modifica: " & elemento.DataModifica.ToString("dd/MM/yyyy HH:mm"))
                    End If
                    If Not String.IsNullOrEmpty(elemento.Permessi) Then
                        infoLines.Add("Permessi: " & ftpEngine.PermessiLeggibili(elemento.Permessi))
                    End If
                Catch exElemento As Exception
                    ScriviLogCompleto($"InformazioniFtp - Errore su '{elemento.PercorsoCompleto}'", exElemento)
                End Try
            Next
            If currentServerFtp IsNot Nothing Then
                infoLines.Add("Server: " & currentServerFtp.Nome & " (" & TestoProtocolloFtp(currentServerFtp.Protocollo) & " - " & currentServerFtp.Host & ":" & currentServerFtp.Porta & ")")
            End If

            _percorsoInformazioniFtp = currentFtpPath
            _listaInformazioniFtp = New WGMenuLista()
            _listaInformazioniFtp.impostaTitolo("Informazioni")
            _listaInformazioniFtp.invioNonConsentito = True
            AddHandler _listaInformazioniFtp.escPremuto, AddressOf OnEscListaInformazioniFtp
            For Each riga In infoLines
                _listaInformazioniFtp.aggiungiElemento(riga)
            Next
            _listaInformazioniFtp.avvia()
        Catch ex As Exception
            ScriviLogCompleto("InformazioniFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnEscListaInformazioniFtp()
        Try
            If _listaInformazioniFtp IsNot Nothing Then
                RemoveHandler _listaInformazioniFtp.escPremuto, AddressOf OnEscListaInformazioniFtp
                _listaInformazioniFtp.abbandona()
                _listaInformazioniFtp = Nothing
            End If
            CaricaDirectoryFtp(_percorsoInformazioniFtp)
            lista.avvia()
        Catch ex As Exception
            ScriviLogCompleto("OnEscListaInformazioniFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Mostra le informazioni generali del server FTP/FTPS/SFTP corrente: sistema
    ''' operativo, spazio totale e spazio disponibile. Il recupero avviene in
    ''' background perché per SFTP richiede l'esecuzione di comandi remoti.
    ''' </summary>
    Private Sub InformazioniServerFtp()
        Try
            If currentServerFtp Is Nothing OrElse currentFtpSession Is Nothing Then Return
            Dim sessioneCorrente As SessioneFtp = currentFtpSession
            Dim percorsoSalvato As String = currentFtpPath
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Recupero informazioni sul server...")
            Dim t As New Thread(Sub()
                                    Try
                                        Dim info As InfoServerRemoto = ftpEngine.OttieniInfoServer(sessioneCorrente)
                                        uiContext.Post(Sub(s) OnInformazioniServerFtpCompletate(info, percorsoSalvato), Nothing)
                                    Catch ex As Exception
                                        uiContext.Post(Sub(s) OnInformazioniServerFtpErrore(ex, percorsoSalvato), Nothing)
                                    End Try
                                End Sub)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("InformazioniServerFtp - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Compone le righe descrittive delle informazioni di un server, usate sia durante la
    ''' navigazione sia dalle opzioni degli elenchi dei server.
    ''' </summary>
    Private Function CostruisciRigheInfoServerFtp(server As InfoServerFtp, info As InfoServerRemoto) As List(Of String)
        Dim righe As New List(Of String)()
        Try
            If server IsNot Nothing Then
                righe.Add("Server: " & server.Nome & " (" & TestoProtocolloFtp(server.Protocollo) & " - " & server.Host & ":" & server.Porta & ")")
                righe.Add("Utente: " & server.Utente)
            End If
            If info Is Nothing Then
                righe.Add("Non è stato possibile recuperare le informazioni dal server.")
                Return righe
            End If
            righe.Add("Sistema operativo: " & If(String.IsNullOrEmpty(info.SistemaOperativo), "Non disponibile", info.SistemaOperativo))
            righe.Add("Spazio totale: " & If(info.SpazioTotale.HasValue, FormattaDimensioneFtp(info.SpazioTotale.Value), "Non disponibile"))
            righe.Add("Spazio disponibile: " & If(info.SpazioDisponibile.HasValue, FormattaDimensioneFtp(info.SpazioDisponibile.Value), "Non disponibile"))
            If info.SpazioUtilizzato.HasValue Then
                righe.Add("Spazio utilizzato: " & FormattaDimensioneFtp(info.SpazioUtilizzato.Value))
            End If
            If Not String.IsNullOrEmpty(info.MessaggioErrore) Then
                righe.Add("Nota: " & info.MessaggioErrore)
            End If
        Catch ex As Exception
            ScriviLogCompleto("CostruisciRigheInfoServerFtp - Errore", ex)
        End Try
        Return righe
    End Function

    ''' <summary>
    ''' Mostra le informazioni del server selezionato in uno degli elenchi, aprendo una
    ''' sessione temporanea solo per interrogarlo e richiudendola subito dopo.
    ''' </summary>
    Private Sub InformazioniServerFtpDaElenco()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim msgSelezione As New WGMessaggi()
                msgSelezione.Messaggio("Seleziona prima un server.")
                msgSelezione.ShowDialog()
                Return
            End If

            Dim idElemento As String = elementiSelezionati(0)
            If Not ftpServerCache.ContainsKey(idElemento) Then Return
            Dim server As InfoServerFtp = ftpServerCache(idElemento)

            If Not server.SalvaPassword OrElse String.IsNullOrEmpty(server.PasswordCriptata) Then
                Dim msgPassword As New WGMessaggi()
                msgPassword.Messaggio("Per questo server la password non è salvata: connettiti al server e usa la voce ""Informazioni server"" durante la navigazione.")
                msgPassword.ShowDialog()
                Return
            End If

            _tempServerFtpInfoDaElenco = CloneServerFtp(server)
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Connessione al server e recupero informazioni...")

            Dim t As New Thread(AddressOf InformazioniServerFtpDaElencoThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("InformazioniServerFtpDaElenco - Errore", ex)
        End Try
    End Sub

    Private Sub InformazioniServerFtpDaElencoThread()
        Dim sessione As SessioneFtp = Nothing
        Dim info As InfoServerRemoto = Nothing
        Dim server As InfoServerFtp = _tempServerFtpInfoDaElenco
        Try
            Dim messaggioErrore As String = ""
            sessione = ftpEngine.ApriSessione(server, messaggioErrore)
            If sessione Is Nothing Then
            Else
                info = ftpEngine.OttieniInfoServer(sessione)
            End If
        Catch ex As Exception
            ScriviLogCompleto("InformazioniServerFtpDaElencoThread - Errore", ex)
        Finally
            Try
                If sessione IsNot Nothing Then
                    ftpEngine.ChiudiSessione(sessione)
                End If
            Catch exChiusura As Exception
                ScriviLogCompleto("InformazioniServerFtpDaElencoThread - Errore chiusura sessione", exChiusura)
            End Try
            Try
                Dim risultato As InfoServerRemoto = info
                uiContext.Post(Sub(s) CompletaInformazioniServerFtpDaElenco(server, risultato), Nothing)
            Catch exPost As Exception
                ScriviLogCompleto("InformazioniServerFtpDaElencoThread - Errore notifica", exPost)
            End Try
        End Try
    End Sub

    Private Sub CompletaInformazioniServerFtpDaElenco(server As InfoServerFtp, info As InfoServerRemoto)
        Try
            If processoincorso IsNot Nothing Then processoincorso.Fine()

            If info Is Nothing Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile connettersi al server per recuperarne le informazioni.")
                msg.ShowDialog()
                Return
            End If

            Dim infoList As New WGMenuLista()
            infoList.impostaTitolo("Informazioni server")
            infoList.invioNonConsentito = True
            AddHandler infoList.escPremuto, Sub()
                                                Try
                                                    infoList.abbandona()
                                                    lista.avvia()
                                                Catch ex As Exception
                                                    ScriviLogCompleto("CompletaInformazioniServerFtpDaElenco escPremuto - Errore", ex)
                                                End Try
                                            End Sub
            For Each riga As String In CostruisciRigheInfoServerFtp(server, info)
                infoList.aggiungiElemento(riga)
            Next
            infoList.avvia()
        Catch ex As Exception
            ScriviLogCompleto("CompletaInformazioniServerFtpDaElenco - Errore", ex)
        End Try
    End Sub

    Private Sub OnInformazioniServerFtpCompletate(info As InfoServerRemoto, percorsoSalvato As String)
        Try
            processoincorso.Fine()

            Dim infoLines As List(Of String) = CostruisciRigheInfoServerFtp(currentServerFtp, info)

            Dim infoList As New WGMenuLista()
            infoList.impostaTitolo("Informazioni server")
            infoList.invioNonConsentito = True
            AddHandler infoList.escPremuto, Sub()
                                                Try
                                                    infoList.abbandona()
                                                    CaricaDirectoryFtp(percorsoSalvato)
                                                    lista.avvia()
                                                Catch ex As Exception
                                                    ScriviLogCompleto("InformazioniServerFtp escPremuto - Errore", ex)
                                                End Try
                                            End Sub
            For Each riga In infoLines
                infoList.aggiungiElemento(riga)
            Next
            infoList.avvia()
        Catch ex As Exception
            ScriviLogCompleto("OnInformazioniServerFtpCompletate - Errore", ex)
        End Try
    End Sub

    Private Sub OnInformazioniServerFtpErrore(ex As Exception, percorsoSalvato As String)
        Try
            processoincorso.Fine()
            ScriviLogCompleto("InformazioniServerFtp - Errore recupero", ex)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile recuperare le informazioni del server.")
            msg.ShowDialog()
        Catch exGeneric As Exception
            ScriviLogCompleto("OnInformazioniServerFtpErrore - Errore", exGeneric)
        End Try
    End Sub

    Private Sub ModificaPermessiElementoFtpSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona prima un file o una cartella per modificarne i permessi.")
                msg.ShowDialog()
                Return
            End If
            Dim elementiRemoti As New List(Of InfoElementoFtp)()
            For Each idElemento As String In elementiSelezionati
                If String.IsNullOrEmpty(idElemento) Then Continue For
                If ftpElementCache.ContainsKey(idElemento) Then elementiRemoti.Add(ftpElementCache(idElemento))
            Next
            If elementiRemoti.Count = 0 Then Return
            MostraMenuPermessiFtp(elementiRemoti)
        Catch ex As Exception
            ScriviLogCompleto("ModificaPermessiElementoFtpSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMenuPermessiFtp(elementiRemoti As List(Of InfoElementoFtp))
        Try
            If elementiRemoti Is Nothing OrElse elementiRemoti.Count = 0 Then Return
            Dim primo As InfoElementoFtp = elementiRemoti(0)
            Dim raw As String = primo.Permessi
            If Not String.IsNullOrEmpty(raw) AndAlso raw.Length = 10 Then raw = raw.Substring(1)
            If String.IsNullOrEmpty(raw) OrElse raw.Length <> 9 Then raw = "---------"

            _tempElementiPermessiFtp = New List(Of InfoElementoFtp)(elementiRemoti)
            _tempPercorsoFtpPermessiSalvato = currentFtpPath

            _listaPermessiFtp = New WGMenuLista()
            If elementiRemoti.Count = 1 Then
                _listaPermessiFtp.impostaTitolo("Permessi di '" & primo.Nome & "' - attuali: " & ftpEngine.PermessiLeggibili(primo.Permessi))
            Else
                _listaPermessiFtp.impostaTitolo("Permessi di " & elementiRemoti.Count.ToString() & " elementi - attuali di '" & primo.Nome & "': " & ftpEngine.PermessiLeggibili(primo.Permessi))
            End If
            _listaPermessiFtp.multiselezioneConsentita = True

            Dim etichette As New List(Of Tuple(Of String, String, Char)) From {
                Tuple.Create("OR", "Proprietario - Lettura", raw(0)),
                Tuple.Create("OW", "Proprietario - Scrittura", raw(1)),
                Tuple.Create("OX", "Proprietario - Esecuzione", raw(2)),
                Tuple.Create("GR", "Gruppo - Lettura", raw(3)),
                Tuple.Create("GW", "Gruppo - Scrittura", raw(4)),
                Tuple.Create("GX", "Gruppo - Esecuzione", raw(5)),
                Tuple.Create("AR", "Altri utenti - Lettura", raw(6)),
                Tuple.Create("AW", "Altri utenti - Scrittura", raw(7)),
                Tuple.Create("AX", "Altri utenti - Esecuzione", raw(8))
            }
            For Each voce In etichette
                Dim attivo As Boolean = (voce.Item3 <> "-"c)
                _listaPermessiFtp.aggiungiElemento(voce.Item2, voce.Item1, attivo)
            Next

            AddHandler _listaPermessiFtp.ElementoSelezionato, AddressOf OnPermessiFtpScelti
            AddHandler _listaPermessiFtp.escPremuto, AddressOf OnEscPermessiFtp
            _listaPermessiFtp.avvia()
        Catch ex As Exception
            ScriviLogCompleto("MostraMenuPermessiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnPermessiFtpScelti(e As String)
        Try
            If _listaPermessiFtp Is Nothing Then Return
            Dim selezionati As List(Of String) = _listaPermessiFtp.ottieniElementi()
            RemoveHandler _listaPermessiFtp.ElementoSelezionato, AddressOf OnPermessiFtpScelti
            RemoveHandler _listaPermessiFtp.escPremuto, AddressOf OnEscPermessiFtp
            _listaPermessiFtp.abbandona()
            _listaPermessiFtp = Nothing
            RichiediConfermaPermessiFtp(If(selezionati, New List(Of String)()), _tempPercorsoFtpPermessiSalvato)
        Catch ex As Exception
            ScriviLogCompleto("OnPermessiFtpScelti - Errore selezione", ex)
        End Try
    End Sub

    Private Sub OnEscPermessiFtp()
        Try
            If _listaPermessiFtp IsNot Nothing Then
                RemoveHandler _listaPermessiFtp.ElementoSelezionato, AddressOf OnPermessiFtpScelti
                RemoveHandler _listaPermessiFtp.escPremuto, AddressOf OnEscPermessiFtp
                _listaPermessiFtp.abbandona()
                _listaPermessiFtp = Nothing
            End If
            CaricaDirectoryFtp(_tempPercorsoFtpPermessiSalvato)
            lista.avvia()
        Catch ex As Exception
            ScriviLogCompleto("OnEscPermessiFtp - Errore annullamento", ex)
        End Try
    End Sub

    Private Sub RichiediConfermaPermessiFtp(selezionati As List(Of String), percorsoSalvato As String)
        Try
            If _tempElementiPermessiFtp Is Nothing OrElse _tempElementiPermessiFtp.Count = 0 Then Return
            Dim chmod As Integer = 0
            If selezionati.Contains("OR") Then chmod += 400
            If selezionati.Contains("OW") Then chmod += 200
            If selezionati.Contains("OX") Then chmod += 100
            If selezionati.Contains("GR") Then chmod += 40
            If selezionati.Contains("GW") Then chmod += 20
            If selezionati.Contains("GX") Then chmod += 10
            If selezionati.Contains("AR") Then chmod += 4
            If selezionati.Contains("AW") Then chmod += 2
            If selezionati.Contains("AX") Then chmod += 1

            _tempPermessiFtpDaApplicare = chmod
            _tempPercorsoFtpPermessiSalvato = percorsoSalvato

            Dim testoOttale As String = chmod.ToString().PadLeft(3, "0"c)
            Dim anteprimaLeggibile As String = ftpEngine.PermessiLeggibili(ftpEngine.PermessiRwxDaChmod(chmod))

            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaApplicaPermessiFtp
            If _tempElementiPermessiFtp.Count = 1 Then
                domanda.avvia($"Confermi i nuovi permessi per '{_tempElementiPermessiFtp(0).Nome}'? {testoOttale} - {anteprimaLeggibile}")
            Else
                domanda.avvia($"Confermi i nuovi permessi per {_tempElementiPermessiFtp.Count} elementi? {testoOttale} - {anteprimaLeggibile}")
            End If
        Catch ex As Exception
            ScriviLogCompleto("RichiediConfermaPermessiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaApplicaPermessiFtp(rispostadom As DialogResult)
        Try
            If rispostadom <> DialogResult.Yes Then
                CaricaDirectoryFtp(_tempPercorsoFtpPermessiSalvato)
                lista.avvia()
                Return
            End If
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Applicazione permessi in corso...")
            Dim t As New Thread(AddressOf EseguiApplicaPermessiFtpThread)
            t.IsBackground = True
            t.Start()
        Catch ex As Exception
            ScriviLogCompleto("RispostaApplicaPermessiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiApplicaPermessiFtpThread()
        Dim tuttoOk As Boolean = True
        Dim ultimoErrore As String = ""
        Try
            For Each elemento As InfoElementoFtp In _tempElementiPermessiFtp
                Try
                    Dim messaggioErrore As String = ""
                    If Not ftpEngine.ImpostaPermessi(currentFtpSession, elemento.PercorsoCompleto, _tempPermessiFtpDaApplicare, messaggioErrore) Then
                        tuttoOk = False
                        If Not String.IsNullOrEmpty(messaggioErrore) Then ultimoErrore = messaggioErrore
                        log.ScriviLog($"EseguiApplicaPermessiFtpThread - Permessi non applicati a '{elemento.PercorsoCompleto}': {messaggioErrore}")
                    End If
                Catch exElemento As Exception
                    tuttoOk = False
                    ultimoErrore = exElemento.Message
                    ScriviLogCompleto($"EseguiApplicaPermessiFtpThread - Errore su '{elemento.PercorsoCompleto}'", exElemento)
                End Try
            Next
        Catch ex As Exception
            tuttoOk = False
            ultimoErrore = ex.Message
            ScriviLogCompleto("EseguiApplicaPermessiFtpThread - Errore", ex)
        Finally
            Try
                uiContext.Post(AddressOf OnPermessiFtpApplicatiDaThread, Tuple.Create(tuttoOk, ultimoErrore))
            Catch exPost As Exception
                ScriviLogCompleto("EseguiApplicaPermessiFtpThread - Errore notifica interfaccia", exPost)
            End Try
        End Try
    End Sub

    Private Sub OnPermessiFtpApplicatiDaThread(state As Object)
        Try
            Dim esito As Tuple(Of Boolean, String) = TryCast(state, Tuple(Of Boolean, String))
            If esito Is Nothing Then Return
            OnPermessiFtpApplicati(esito.Item1, esito.Item2)
        Catch ex As Exception
            ScriviLogCompleto("OnPermessiFtpApplicatiDaThread - Errore", ex)
        End Try
    End Sub

    Private Sub OnPermessiFtpApplicati(esito As Boolean, messaggioErrore As String)
        Try
            processoincorso.Fine()
            Dim msg As New WGMessaggi()
            If esito Then
                msg.Messaggio("Permessi aggiornati.")
            Else
                msg.Messaggio("Impossibile aggiornare i permessi." & If(String.IsNullOrEmpty(messaggioErrore), "", " " & messaggioErrore & " (alcuni server FTP non supportano la modifica remota dei permessi)."))
            End If
            msg.ShowDialog()
            CaricaDirectoryFtp(_tempPercorsoFtpPermessiSalvato)
            lista.avvia()
        Catch ex As Exception
            ScriviLogCompleto("OnPermessiFtpApplicati - Errore", ex)
        End Try
    End Sub

    Private Function TestoProtocolloFtp(protocollo As TipoProtocolloFtp) As String
        Try
            Select Case protocollo
                Case TipoProtocolloFtp.Sftp
                    Return "SFTP"
                Case TipoProtocolloFtp.Ftps
                    Return "FTPS"
                Case Else
                    Return "FTP"
            End Select
        Catch ex As Exception
            ScriviLogCompleto("TestoProtocolloFtp - Errore", ex)
            Return "FTP"
        End Try
    End Function

    Private Function FormattaDimensioneFtp(bytes As Long) As String
        Try
            Dim unita As String() = {"B", "KB", "MB", "GB", "TB"}
            Dim dimensione As Double = bytes
            Dim indice As Integer = 0
            While dimensione >= 1024 AndAlso indice < unita.Length - 1
                dimensione /= 1024
                indice += 1
            End While
            Return dimensione.ToString("0.##") & " " & unita(indice)
        Catch ex As Exception
            ScriviLogCompleto("FormattaDimensioneFtp - Errore", ex)
            Return bytes.ToString() & " B"
        End Try
    End Function

    Private Sub rimuovidisco()
        Try
            Dim dischi As List(Of String) = lista.ottieniElementi()
            If dischi Is Nothing OrElse dischi.Count = 0 Then
                Dim messaggiononvalido As New WGMessaggi()
                messaggiononvalido.Messaggio("Nessun elemento selezionato.")
                messaggiononvalido.ShowDialog()
                Return
            End If
            Dim rimossi As Integer = 0
            Dim falliti As Integer = 0
            Dim nonDischi As Integer = 0
            Dim nonEsterni As Integer = 0
            For Each disco As String In dischi
                Try
                    If Not logic.ÈDisco(disco) Then
                        nonDischi += 1
                    ElseIf Not logic.IsExternalDisk(disco) Then
                        nonEsterni += 1
                    ElseIf logic.RimuoviDevice(disco) Then
                        rimossi += 1
                    Else
                        falliti += 1
                    End If
                Catch exDisco As Exception
                    falliti += 1
                    ScriviLogCompleto($"FileManager.rimuovidisco - Errore durante la rimozione di '{disco}'", exDisco)
                End Try
            Next
            Dim testo As String
            If dischi.Count = 1 Then
                If nonDischi = 1 Then
                    testo = "Non è stato selezionato un disco."
                ElseIf nonEsterni = 1 Then
                    testo = "Non è stato selezionato un disco esterno."
                ElseIf rimossi = 1 Then
                    testo = "L'unità è stata rimossa."
                Else
                    testo = "Non è stato possibile rimuovere l'unità. Controlla il log."
                End If
            Else
                Dim parti As New List(Of String)()
                If rimossi > 0 Then parti.Add($"Unità rimosse: {rimossi}.")
                If falliti > 0 Then parti.Add($"Unità non rimosse per errore: {falliti}, controlla il log.")
                If nonEsterni > 0 Then parti.Add($"Dischi non esterni ignorati: {nonEsterni}.")
                If nonDischi > 0 Then parti.Add($"Elementi che non sono dischi ignorati: {nonDischi}.")
                testo = String.Join(" ", parti)
            End If
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("FileManager.rimuovidisco - Errore durante la rimozione del disco", ex)
        End Try
    End Sub

    Private Sub vaia()
        Try
            campi = New Campi()
            AddHandler campi.Fine, AddressOf consultadirectory
            campi.Aggiungi("Scrivi il percorso a cui vuoi andare", "c.percorso", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("FileManager.vaia - Errore durante l'avvio del campo", ex)
        End Try
    End Sub

    Private Sub consultadirectory()
        Dim dir As String = String.Empty
        Try
            dir = campi.RecuperaValore("c.percorso")
            If Not String.IsNullOrWhiteSpace(dir) Then
                dir = WGRete.NormalizzaPercorsoUnc(dir)
                If WGRete.IsServerRoot(dir) Then
                    MostraCondivisioniServer(dir)
                    Return
                End If
                If ÈPercorsoValido(dir) Then
                    If WGRete.EsisteDirectory(dir, 10000) Then
                        If currentDirectory Is Nothing OrElse currentDirectory = "" OrElse logic.IsRadiceRisorsa(currentDirectory) Then
                            lista.pulisci()
                        End If
                        history.Add(New NavigazioneStato(If(String.IsNullOrEmpty(currentDirectory), "sceltadisco", currentDirectory), 0))
                        isReteMenuMode = False
                        isReteCondivisioniMode = False
                        currentDirectory = dir
                        currentExtensionFilter = ""
                        lista.impostaTitolo("Directory: " & System.IO.Path.GetFileName(dir.TrimEnd("\")))
                        CaricaDirectory(dir)
                    Else
                        Dim messaggiononesiste As New WGMessaggi()
                        If WGRete.IsRemoto(dir) Then
                            messaggiononesiste.Messaggio("La cartella di rete non esiste, non è raggiungibile oppure non hai i permessi per accedervi.")
                        Else
                            messaggiononesiste.Messaggio("La directory specificata non esiste o è un file.")
                        End If
                        messaggiononesiste.ShowDialog()
                    End If
                Else
                    Dim messaggiopercorso As New WGMessaggi()
                    messaggiopercorso.Messaggio("Il percorso specificato non è valido.")
                    messaggiopercorso.ShowDialog()
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto($"FileManager.consultadirectory - Errore consultazione directory '{dir}'", ex)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Si è verificato un errore inatteso. Controlla il log.")
            msg.ShowDialog()
        Finally
            Try
                
            Catch ex As Exception
                ScriviLogCompleto("FileManager.consultadirectory - Errore dispose campo", ex)
            End Try
        End Try
    End Sub

    Private Function ÈPercorsoValido(ByVal path As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(path) Then Return False
            If path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 Then
                Return False
            End If
            If WGRete.IsUncPath(path.Trim()) Then
                Return WGRete.NomeServer(path.Trim()).Length > 2
            End If
            Dim fullPath As String = System.IO.Path.GetFullPath(path)
            Dim root As String = System.IO.Path.GetPathRoot(fullPath)
            If String.IsNullOrEmpty(root) OrElse Not Char.IsLetter(root(0)) OrElse root(1) <> ":"c Then
                Return False
            End If
            Return True
        Catch ex As Exception
            ScriviLogCompleto($"FileManager.ÈPercorsoValido - Errore verifica percorso '{path}'", ex)
            Return False
        End Try
    End Function

    Private Sub Selezionato(e As String)
        Try
            If isExtensionSelectionMode Then
                Dim scelta As String = e
                If String.IsNullOrEmpty(scelta) Then
                    Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
                    If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
                    scelta = elementiSelezionati(0)
                End If
                GestisciSelezioneEstensioni(New List(Of String) From {scelta})
                Return
            End If

            ' Usiamo direttamente l'id passato dall'evento di selezione singola invece di
            ' rileggere lista.ottieniElementi(): durante il ripopolamento asincrono di una
            ' lista (es. sceltadrive, che ricostruisce gli elementi in background) quella
            ' rilettura può restituire ancora la selezione della lista precedente (es. la
            ' cartella da cui si e' tornati indietro), facendo riaprire quella invece
            ' dell'elemento appena scelto (es. "Server FTP").
            Dim path As String = e
            If String.IsNullOrEmpty(path) Then
                Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
                If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                    Dim messaggio As New WGMessaggi()
                    messaggio.Messaggio("Non è stato selezionato nessun'elemento.")
                    messaggio.ShowDialog()
                    Return
                End If
                path = elementiSelezionati(0)
            End If

            If path = ID_FTP_ROOT Then
                MostraMenuServerFtp()
                Return
            End If
            If path = ID_RETE_ROOT Then
                MostraMenuRete()
                Return
            End If
            If isReteMenuMode Then
                SelezionatoMenuRete(path)
                Return
            End If
            If isReteCondivisioniMode Then
                SelezionatoCondivisioneRete(path)
                Return
            End If
            If isFtpMenuPrincipaleMode Then
                SelezionatoMenuPrincipaleFtp(path)
                Return
            End If
            If isFtpServerListMode Then
                SelezionatoServerFtp(path)
                Return
            End If
            If isFtpBrowseMode Then
                SelezionatoFtp(path)
                Return
            End If

            recentsManager.AggiungiARecenti(path)
            CartellaSuccessiva(path)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.Selezionato - Errore selezione elemento", ex)
        End Try
    End Sub

    Private Function IsArchiveFile(f As String) As Boolean
        Try
            If logic.IsWpdPath(f) Then Return False
            Return logic.estensioniArchivio.Contains(System.IO.Path.GetExtension(f).ToLower())
        Catch ex As Exception
            ScriviLogCompleto("IsArchiveFile - Errore", ex)
            Return False
        End Try
    End Function

    Private Sub ElementiSelezionati(ids As List(Of String))
        Try
            If isExtensionSelectionMode Then
                GestisciSelezioneEstensioni(ids)
                Return
            End If

            Dim elementiSelezionati As List(Of String) = ids
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim zipFiles = elementiSelezionati.Where(AddressOf IsArchiveFile).ToList()
            If zipFiles.Any() Then
                Try
                    If Not System.IO.File.Exists("7z.exe") Then
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("7z.exe non trovato. Impossibile decomprimere.")
                        msg.ShowDialog()
                        Return
                    End If
                    For Each file In zipFiles
                        If WGCentroDiControllo.GetInstance().IsArchivioProtetto(file) Then
                            _tempZipDestinazione = file
                            campi = New Campi()
                            AddHandler campi.Fine, AddressOf OnPasswordDecompressioneInserita
                            campi.AggiungiPassword("Password per " & System.IO.Path.GetFileName(file), "archivio_password", True)
                            campi.Avvia()
                        Else
                            WGCentroDiControllo.GetInstance().Decomprimi(file, System.IO.Path.GetDirectoryName(file))
                        End If
                    Next
                Catch ex As Exception
                    ScriviLogCompleto("Errore decompressione selezione multipla", ex)
                End Try
            Else
                ricaricadirectory = currentDirectory
                ApriBrani(elementiSelezionati)
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.ElementiSelezionati - Errore elaborazione elementi selezionati", ex)
        End Try
    End Sub

    Private Sub OnPasswordDecompressioneInserita()
        Try
            Dim password As String = campi.RecuperaValore("archivio_password")
            
            If String.IsNullOrWhiteSpace(password) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Password non valida. Decompressione annullata.")
                msg.ShowDialog()
                Return
            End If
            Dim dest As String = System.IO.Path.GetDirectoryName(_tempZipDestinazione)
            WGCentroDiControllo.GetInstance().Decomprimi(_tempZipDestinazione, dest, password)
        Catch ex As Exception
            ScriviLogCompleto("Errore callback password decompressione", ex)
        End Try
    End Sub

    Public Sub ApriBrani(brani As List(Of String), Optional forzaScelta As Boolean = True)
        Try
            Dim listaFileFinali As New List(Of String)
            Dim ciSonoVideo As Boolean = False
            Dim videoExts As String() = {".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm"}
            For Each elemento In brani
                If logic.IsWpdPath(elemento) Then
                    listaFileFinali.Add(elemento)
                    Dim name = logic.GetWpdFileName(elemento)
                    If videoExts.Contains(System.IO.Path.GetExtension(name).ToLower()) Then ciSonoVideo = True
                ElseIf WGRete.EsisteDirectory(elemento) Then
                    Dim filesInDir = System.IO.Directory.GetFiles(elemento, "*.*", System.IO.SearchOption.AllDirectories)
                    For Each file In filesInDir
                        Dim ext = System.IO.Path.GetExtension(file).ToLower()
                        If logic.estensioniAudioVideo.Contains(ext) Then
                            listaFileFinali.Add(file)
                            If videoExts.Contains(ext) Then ciSonoVideo = True
                        End If
                    Next
                ElseIf WGRete.EsisteFile(elemento) Then
                    Dim ext = System.IO.Path.GetExtension(elemento).ToLower()
                    If logic.estensioniAudioVideo.Contains(ext) Then
                        listaFileFinali.Add(elemento)
                        If videoExts.Contains(ext) Then ciSonoVideo = True
                    End If
                End If
            Next
            If listaFileFinali.Count = 0 Then Return

            If WGCentroDiControllo.GetInstance().CiSonoDownloadDipendenzeInCorso() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Le librerie per il player sono ancora in fase di download. Attendere il completamento.")
                msg.ShowDialog()
                Return
            End If

            If ciSonoVideo Then
                AvviaPlayerGui(listaFileFinali)
                Return
            End If

            If Not forzaScelta Then
                Dim modalita As String = OttieniModalitaAperturaMedia()
                If modalita = "Sottofondo" Then
                    WGCentroDiControllo.GetInstance().RiproduciFileAudio(listaFileFinali)
                    Return
                ElseIf modalita = "Player" Then
                    AvviaPlayerGui(listaFileFinali)
                    Return
                End If
            End If

            Dim menuAudio As New WGMenu()
            Dim listaPerClosure As List(Of String) = listaFileFinali
            menuAudio.aggiungi("Ascolto in background", Sub()
                                                            Try
                                                                WGCentroDiControllo.GetInstance().RiproduciFileAudio(listaPerClosure)
                                                                menuAudio.abbandona()
                                                            Catch ex As Exception
                                                                ScriviLogCompleto("ApriBrani.Lambda1 - Errore", ex)
                                                            End Try
                                                        End Sub)
            menuAudio.aggiungi("Apri lettore multimediale", Sub()
                                                                Try
                                                                    AvviaPlayerGui(listaPerClosure)
                                                                    menuAudio.abbandona()
                                                                Catch ex As Exception
                                                                    ScriviLogCompleto("ApriBrani.Lambda2 - Errore", ex)
                                                                End Try
                                                            End Sub)
            menuAudio.avvia("Modalità di ascolto", True)

        Catch ex As Exception
            ScriviLogCompleto($"FileManager.ApriBrani - Errore elaborazione brani", ex)
        End Try
    End Sub

    Private Sub AvviaPlayerGui(files As List(Of String))
        Try
            Dim player As New WGPlayerFormInt()
            For Each file In files
                player.AggiungiElemento(file, wgcontrols.ElementType.File)
            Next
            player.avvia("Media player")
        Catch ex As Exception
            ScriviLogCompleto("AvviaPlayerGui - Errore", ex)
        End Try
    End Sub

    Private Sub CartellaPrecedente()
        Try
            If isExtensionSelectionMode Then
                ' Annullamento della scelta: si torna alla cartella cosi' come si trovava prima
                ' di aprire la scelta delle estensioni, senza toccare un eventuale filtro gia'
                ' attivo in precedenza.
                isExtensionSelectionMode = False
                lista.impostaTitolo("Directory: " & System.IO.Path.GetFileName(currentDirectory.TrimEnd("\")))
                CaricaDirectory(currentDirectory)
                Return
            End If

            currentExtensionFilter = ""
            If isFtpBrowseMode Then
                IndietroFtp()
                Return
            End If
            If isFtpServerListMode Then
                isFtpServerListMode = False
                MostraMenuServerFtp()
                Return
            End If
            If isFtpMenuPrincipaleMode Then
                ' Uscendo dal menu principale dei server FTP si deve tornare a "Scelta
                ' risorsa" (drive locali + voce "Server FTP"), non chiudere il gestore file.
                isFtpMenuPrincipaleMode = False
                sceltadrive()
                Return
            End If
            If isReteCondivisioniMode Then
                isReteCondivisioniMode = False
                MostraMenuRete()
                Return
            End If
            If isReteMenuMode Then
                isReteMenuMode = False
                sceltadrive()
                Return
            End If
            If history.Count > 0 Then
                Dim previousState As NavigazioneStato = history(history.Count - 1)
                history.RemoveAt(history.Count - 1)
                Dim percorsoDaEvidenziare As String = currentDirectory
                currentDirectory = previousState.Directory
                If currentDirectory = "sceltadisco" Then
                    currentDirectory = ""
                    sceltadrive()
                ElseIf currentDirectory = "EXIT_ON_BACK" Then
                    AnnullaCaricamentoDirectoryInCorso()
                    ChiudiTuttoFtpAllUscita()
                    lista.abbandona()
                    lista = Nothing
                ElseIf currentDirectory.StartsWith(PrefissoRicerca, StringComparison.Ordinal) Then
                    ' Uscendo da una cartella aperta dai risultati si torna ai risultati.
                    CaricaDirectory(currentDirectory)
                Else
                    _percorsoDaEvidenziareDopoCaricamento = percorsoDaEvidenziare
                    If logic.IsWpdPath(currentDirectory) Then
                        lista.impostaTitolo("Directory")
                        CaricaDirectory(currentDirectory)
                    ElseIf WGRete.EsisteDirectory(currentDirectory) Then
                        lista.impostaTitolo("Directory: " & System.IO.Path.GetFileName(currentDirectory))
                        CaricaDirectory(currentDirectory)
                    Else
                        _percorsoDaEvidenziareDopoCaricamento = ""
                        CartellaPrecedente()
                    End If
                End If
            Else
                AnnullaCaricamentoDirectoryInCorso()
                ChiudiTuttoFtpAllUscita()
                lista.abbandona()
                lista = Nothing
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.CartellaPrecedente - Errore ritorno cartella precedente", ex)
            sceltadrive()
        End Try
    End Sub

    Private Sub CartellaSuccessiva(selectedPath As String, Optional forzaScelta As Boolean = False)
        Try
            currentExtensionFilter = ""
            If logic.IsWpdPath(selectedPath) Then
                If selectedPath.EndsWith("|\") OrElse logic.IsWpdDirectory(selectedPath) Then
                    If String.IsNullOrEmpty(currentDirectory) OrElse logic.IsRadiceRisorsa(currentDirectory) Then
                        lista.pulisci()
                    End If
                    history.Add(New NavigazioneStato(If(String.IsNullOrEmpty(currentDirectory), "sceltadisco", currentDirectory), lista.ottieniIndice()))
                    currentDirectory = selectedPath
                    CaricaDirectory(selectedPath)
                Else
                    If forzaScelta Then ApriConScelta(selectedPath) Else Apri(selectedPath)
                End If
                Return
            End If
            If WGRete.EsisteDirectory(System.IO.Path.GetFullPath(selectedPath)) Then
                If String.IsNullOrEmpty(currentDirectory) OrElse logic.IsRadiceRisorsa(currentDirectory) Then
                    lista.pulisci()
                End If
                history.Add(New NavigazioneStato(If(String.IsNullOrEmpty(currentDirectory), "sceltadisco", currentDirectory), lista.ottieniIndice()))
                currentDirectory = selectedPath
                lista.impostaTitolo("Directory: " & System.IO.Path.GetFileName(selectedPath.TrimEnd("\")))
                CaricaDirectory(selectedPath)
            Else
                If forzaScelta Then ApriConScelta(selectedPath) Else Apri(selectedPath)
            End If
        Catch ex As UnauthorizedAccessException
            Dim msg As New WGMessaggi()
            msg.Messaggio("Accesso negato alla cartella selezionata.")
            msg.ShowDialog()
            ScriviLogCompleto($"CartellaSuccessiva - Accesso negato: {selectedPath}", ex)
        Catch ex As PathTooLongException
            Dim msg As New WGMessaggi()
            msg.Messaggio("Percorso troppo lungo per essere gestito.")
            msg.ShowDialog()
            ScriviLogCompleto($"CartellaSuccessiva - Percorso troppo lungo: {selectedPath}", ex)
        Catch ex As Exception
            ScriviLogCompleto($"CartellaSuccessiva - Errore navigazione '{selectedPath}'", ex)
        End Try
    End Sub

    ''' <summary>Apre un file partendo dalla sua cartella, come se lo si scegliesse dalla gestione file (scrivania).</summary>
    Public Sub ApriFileDallaCartella(percorsoFile As String)
        Try
            Avvia(System.IO.Path.GetDirectoryName(percorsoFile))
            Apri(percorsoFile)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.ApriFileDallaCartella - Errore", ex)
        End Try
    End Sub

    Public Sub Apri(file As String, Optional serverFtpOrigine As InfoServerFtp = Nothing, Optional cartellaRemotaOrigine As String = "")
        ApriInterno(file, False, serverFtpOrigine, cartellaRemotaOrigine)
    End Sub

    Public Sub ApriConScelta(file As String, Optional serverFtpOrigine As InfoServerFtp = Nothing, Optional cartellaRemotaOrigine As String = "")
        ApriInterno(file, True, serverFtpOrigine, cartellaRemotaOrigine)
    End Sub

    Private Sub ApriInterno(file As String, forzaScelta As Boolean, Optional serverFtpOrigine As InfoServerFtp = Nothing, Optional cartellaRemotaOrigine As String = "")
        Try
            Dim selectedPath As String = file
            Dim isWpd As Boolean = logic.IsWpdPath(file)
            ' Ultimi file aperti: la scrivania li mostra in Recenti e la ricerca globale li trova.
            If Not isWpd AndAlso serverFtpOrigine Is Nothing Then WGRecenti.Registra(file)
            If isWpd Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Dai dispositivi portatili non è consentita la riproduzione ma solamente il trasferimento dei file.")
                msg.ShowDialog()
                Return
            End If
            If WGRete.IsRemoto(file) AndAlso Not WGRete.EsisteFile(file, 8000) Then
                Dim msgRete As New WGMessaggi()
                msgRete.Messaggio("Il file non è raggiungibile. Controlla la connessione di rete e i permessi sulla condivisione.")
                msgRete.ShowDialog()
                Return
            End If
            Dim estensione As String
            If logic.IsWpdPath(selectedPath) Then
                Dim name = logic.GetWpdFileName(selectedPath)
                estensione = System.IO.Path.GetExtension(name).ToLower()
            Else
                estensione = System.IO.Path.GetExtension(selectedPath).ToLower()
            End If
            If logic.estensioniAudioVideo.Contains(estensione) Then
                ApriBrani(New List(Of String) From {selectedPath}, forzaScelta)
            ElseIf logic.estensioniTesto.Contains(estensione) Then
                GestisciAperturaTesto(selectedPath, forzaScelta, serverFtpOrigine, cartellaRemotaOrigine)
            ElseIf logic.estensioniArchivio.Contains(estensione) Then
                Try
                    If serverFtpOrigine IsNot Nothing Then
                        ' Il file è stato scaricato in una cartella temporanea solo per
                        ' l'apertura: estrarlo lì significherebbe far finire i contenuti
                        ' dell'archivio in un posto che l'utente non conosce e non
                        ' ritroverebbe. Va prima scaricato esplicitamente in una cartella
                        ' scelta da lui.
                        Dim msgArchivioFtp As New WGMessaggi()
                        msgArchivioFtp.Messaggio("Questo è un file compresso: per estrarlo devi prima scaricarlo in una cartella locale.")
                        msgArchivioFtp.ShowDialog()
                        Return
                    End If
                    If Not System.IO.File.Exists("7z.exe") Then
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("7z.exe non trovato. Impossibile decomprimere.")
                        msg.ShowDialog()
                        Return
                    End If
                    If WGCentroDiControllo.GetInstance().IsArchivioProtetto(selectedPath) Then
                        _tempZipDestinazione = selectedPath
                        campi = New Campi()
                        AddHandler campi.Fine, AddressOf OnPasswordDecompressioneSingolaInserita
                        campi.AggiungiPassword("Password per " & System.IO.Path.GetFileName(selectedPath), "archivio_password", True)
                        campi.Avvia()
                    Else
                        WGCentroDiControllo.GetInstance().Decomprimi(selectedPath, System.IO.Path.GetDirectoryName(selectedPath))
                    End If
                Catch ex As Exception
                    ScriviLogCompleto("Errore apertura archivio", ex)
                End Try
            Else
                Dim msgNonSupportato As New WGMessaggi()
                msgNonSupportato.Messaggio("Questo tipo di file non è supportato dal programma.")
                msgNonSupportato.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto($"FileManager.Apri - Errore apertura file '{file}'", ex)
        End Try
    End Sub

    Private Sub GestisciAperturaTesto(percorso As String, forzaScelta As Boolean, Optional serverFtpOrigine As InfoServerFtp = Nothing, Optional cartellaRemotaOrigine As String = "")
        Try
            If forzaScelta Then
                MostraSceltaAperturaTesto(percorso, serverFtpOrigine, cartellaRemotaOrigine)
                Return
            End If
            Dim modalita As String = OttieniModalitaAperturaTesto()
            ApriEditor(percorso, modalita = "SolaLettura", serverFtpOrigine, cartellaRemotaOrigine)
        Catch ex As Exception
            ScriviLogCompleto("GestisciAperturaTesto - Errore", ex)
        End Try
    End Sub

    Private Sub MostraSceltaAperturaTesto(percorso As String, Optional serverFtpOrigine As InfoServerFtp = Nothing, Optional cartellaRemotaOrigine As String = "")
        Try
            Dim menuTesto As New WGMenu()
            menuTesto.aggiungi("Apri in sola lettura", Sub()
                                                           Try
                                                               ApriEditor(percorso, True, serverFtpOrigine, cartellaRemotaOrigine)
                                                               menuTesto.abbandona()
                                                           Catch ex As Exception
                                                               ScriviLogCompleto("MostraSceltaAperturaTesto.Lambda1 - Errore", ex)
                                                           End Try
                                                       End Sub)
            menuTesto.aggiungi("Apri in modifica", Sub()
                                                       Try
                                                           ApriEditor(percorso, False, serverFtpOrigine, cartellaRemotaOrigine)
                                                           menuTesto.abbandona()
                                                       Catch ex As Exception
                                                           ScriviLogCompleto("MostraSceltaAperturaTesto.Lambda2 - Errore", ex)
                                                       End Try
                                                   End Sub)
            menuTesto.avvia("Modalità di apertura", True)
        Catch ex As Exception
            ScriviLogCompleto("MostraSceltaAperturaTesto - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Apre l'editor di testo. Se il file proviene da una cartella FTP (serverFtpOrigine
    ''' valorizzato), ogni salvataggio verso il percorso locale tracciato viene
    ''' automaticamente ricaricato sul server nella cartella remota di origine
    ''' (vedi OnFileSalvatoEditor).
    ''' </summary>
    Private Sub ApriEditor(percorso As String, soloLettura As Boolean, Optional serverFtpOrigine As InfoServerFtp = Nothing, Optional cartellaRemotaOrigine As String = "")
        Try
            _ftpServerOrigineEditor = serverFtpOrigine
            _ftpCartellaRemotaOrigineEditor = cartellaRemotaOrigine
            _ftpPercorsoLocaleTracciatoEditor = If(serverFtpOrigine IsNot Nothing, percorso, "")

            _editorAperto = New editor()
            AddHandler _editorAperto.FileSalvato, AddressOf OnFileSalvatoEditor
            _editorAperto.avvia(percorso, soloLettura)
        Catch ex As Exception
            ScriviLogCompleto("ApriEditor - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Reagisce al salvataggio di un file di testo nell'editor. Se il file proviene da
    ''' FTP, ricarica automaticamente sul server la versione appena salvata quando il
    ''' percorso coincide con quello originariamente scaricato; se invece è stato
    ''' salvato altrove (Salva con nome) chiede conferma prima di caricarlo.
    ''' </summary>
    Private Sub OnFileSalvatoEditor(percorsoLocale As String)
        Try
            If _ftpServerOrigineEditor Is Nothing Then Return

            If String.Equals(percorsoLocale, _ftpPercorsoLocaleTracciatoEditor, StringComparison.OrdinalIgnoreCase) Then
                AvviaUploadEditorSuFtp(percorsoLocale)
            Else
                _percorsoLocaleUploadInSospeso = percorsoLocale
                domandaUploadEditor = New WGDomanda()
                AddHandler domandaUploadEditor.rispostadomanda, AddressOf RispostaUploadEditor
                domandaUploadEditor.avvia("Vuoi caricare anche """ & System.IO.Path.GetFileName(percorsoLocale) & """ sul server FTP, nella cartella di origine del file aperto?", True)
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnFileSalvatoEditor - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaUploadEditor(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes Then
                _ftpPercorsoLocaleTracciatoEditor = _percorsoLocaleUploadInSospeso
                AvviaUploadEditorSuFtp(_percorsoLocaleUploadInSospeso)
            Else
                ' L'utente non vuole collegare questo file al server: si smette di
                ' tracciarlo, così non verrà più richiesto per i prossimi salvataggi.
                _ftpServerOrigineEditor = Nothing
                _ftpCartellaRemotaOrigineEditor = ""
                _ftpPercorsoLocaleTracciatoEditor = ""
            End If
            _percorsoLocaleUploadInSospeso = ""
        Catch ex As Exception
            ScriviLogCompleto("RispostaUploadEditor - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Mette in coda il ricaricamento del file locale sul server FTP di origine,
    ''' riusando lo stesso meccanismo (con relativa UI di avanzamento) già usato per
    ''' "Incolla" di file locali dentro una cartella FTP.
    ''' </summary>
    Private Sub AvviaUploadEditorSuFtp(percorsoLocale As String)
        Try
            If _ftpServerOrigineEditor Is Nothing OrElse String.IsNullOrEmpty(_ftpCartellaRemotaOrigineEditor) Then Return
            WGCentroDiControllo.GetInstance().AggiungiUploadFtp(percorsoLocale, _ftpCartellaRemotaOrigineEditor, _ftpServerOrigineEditor, False)

            If isFtpBrowseMode AndAlso currentServerFtp IsNot Nothing AndAlso ReferenceEquals(currentServerFtp, _ftpServerOrigineEditor) AndAlso String.Equals(currentFtpPath, _ftpCartellaRemotaOrigineEditor, StringComparison.Ordinal) Then
                AggiornaCartellaFtpCorrente()
            End If
        Catch ex As Exception
            ScriviLogCompleto("AvviaUploadEditorSuFtp - Errore", ex)
        End Try
    End Sub

    Private Sub OnPasswordDecompressioneSingolaInserita()
        Try
            Dim password As String = campi.RecuperaValore("archivio_password")
            
            If String.IsNullOrWhiteSpace(password) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Password non valida. Decompressione annullata.")
                msg.ShowDialog()
                Return
            End If
            Dim dest As String = System.IO.Path.GetDirectoryName(_tempZipDestinazione)
            WGCentroDiControllo.GetInstance().Decomprimi(_tempZipDestinazione, dest, password)
        Catch ex As Exception
            ScriviLogCompleto("Errore callback password singolo archivio", ex)
        End Try
    End Sub

    Private Sub InizializzaOpzioniSceltaDrive()
        Try
            lista.opzioniPulisci()
            lista.aggiungiOpzione("Vai a percorso", AddressOf vaia)
            lista.aggiungiOpzione("Cerca file e cartelle in tutti i dischi", AddressOf AvviaRicercaDaOpzioni)
            lista.aggiungiOpzione("Connetti unità di rete", AddressOf NuovaConnessioneRete)
            lista.aggiungiOpzione("Aggiorna", AddressOf AggiornaSceltaDrive)
            lista.aggiungiOpzione("Informazioni", AddressOf informazioni)
        Catch ex As Exception
            ScriviLogCompleto("InizializzaOpzioniSceltaDrive - Errore", ex)
        End Try
    End Sub

    Private Sub InizializzaOpzioniStandard()
        Try
            lista.opzioniPulisci()
            lista.multiselezioneConsentita = True
            lista.aggiungiOpzione("Apri", AddressOf ApriElementoSelezionato)
            lista.aggiungiOpzione("Cerca file e cartelle", AddressOf AvviaRicercaDaOpzioni)
            lista.aggiungiOpzione("Aggiorna", AddressOf AggiornaCorrente)
            lista.aggiungiOpzione("Mostra per estensione", AddressOf MostraMenuEstensioni)
            lista.aggiungiOpzione("Ordina per...", AddressOf MostraMenuOrdinamento)
            lista.aggiungiOpzione("Comprimi...", AddressOf AvviaCompressione)
            lista.aggiungiOpzione("Aggiungi ai Preferiti", AddressOf AggiungiRimuoviPreferiti)
            lista.aggiungiOpzione("Taglia", AddressOf tagliaevent)
            lista.aggiungiOpzione("Copia", AddressOf copiaevent)
            lista.aggiungiOpzione("Incolla", AddressOf incolla)
            lista.aggiungiOpzione("Elimina", AddressOf eliminaevent)
            lista.aggiungiOpzione("Rinomina", AddressOf rinomina)
            lista.aggiungiOpzione("Crea nuova cartella", AddressOf creacartellaevent)
            lista.aggiungiOpzione("Informazioni", AddressOf informazioni)
            lista.aggiungiOpzione("Cripta", AddressOf CriptaElementoSelezionato)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.InizializzaOpzioniStandard - Errore inizializzazione opzioni", ex)
        End Try
    End Sub

    Private Sub MostraTuttiEstensioni()
        Try
            currentExtensionFilter = ""
            lista.impostaTitolo("Directory: " & System.IO.Path.GetFileName(currentDirectory.TrimEnd("\")))
            CaricaDirectory(currentDirectory)
        Catch ex As Exception
            ScriviLogCompleto("MostraTuttiEstensioni - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMenuEstensioni()
        Try
            Dim extensions As New HashSet(Of String)
            Dim items = logic.OttieniContenutoCartella(currentDirectory, "", CLSFile.OrdineDiVisualizzazione.Nome)
            For Each item In items
                If Not item.IsDirectory Then
                    extensions.Add(System.IO.Path.GetExtension(item.Nome).ToLower())
                End If
            Next

            isExtensionSelectionMode = True

            ' Questa opzione viene scelta mentre il menu opzioni e' ancora aperto: subito dopo
            ' aver eseguito questo gestore, il controllo chiude comunque il menu opzioni e
            ' ricostruisce "lista" con il contenuto della cartella salvato prima di aprirlo.
            ' Bisogna aspettare che quella chiusura sia finita prima di sostituire il contenuto
            ' con le estensioni, altrimenti si scrive su una lista che non esiste ancora oppure
            ' che viene subito dopo sovrascritta.
            uiContext.Post(Sub(state)
                               Try
                                   If Not isExtensionSelectionMode Then Return
                                   lista.opzioniPulisci()
                                   lista.pulisci()
                                   lista.impostaTitolo("Scegli una o più estensioni")
                                   lista.multiselezioneConsentita = True
                                   lista.aggiungiElemento("Mostra tutti", "*")
                                   For Each ext In extensions.OrderBy(Function(x) x)
                                       If Not String.IsNullOrEmpty(ext) Then
                                           lista.aggiungiElemento(ext, ext)
                                       End If
                                   Next
                               Catch exUI As Exception
                                   ScriviLogCompleto("MostraMenuEstensioni (aggiornamento lista)", exUI)
                                   isExtensionSelectionMode = False
                               End Try
                           End Sub, Nothing)
        Catch ex As Exception
            ScriviLogCompleto("Errore MostraMenuEstensioni", ex)
            isExtensionSelectionMode = False
        End Try
    End Sub

    ' Chiamato sia dalla selezione singola (Invio su una voce, nessuna spuntata) sia dalla
    ' selezione multipla (una o più estensioni spuntate e confermate): applica il filtro sulla
    ' cartella corrente, oppure mostra tutto se e' stata scelta "Mostra tutti" o non e' stato
    ' scelto nulla.
    Private Sub GestisciSelezioneEstensioni(scelte As List(Of String))
        Try
            isExtensionSelectionMode = False
            If scelte Is Nothing OrElse scelte.Count = 0 OrElse scelte.Contains("*") Then
                MostraTuttiEstensioni()
            Else
                ApplicaFiltroEstensioni(scelte)
            End If
        Catch ex As Exception
            ScriviLogCompleto("GestisciSelezioneEstensioni - Errore", ex)
            isExtensionSelectionMode = False
            MostraTuttiEstensioni()
        End Try
    End Sub

    Private Sub AggiornaCorrente()
        Try
            CaricaDirectory(currentDirectory)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AggiornaCorrente - Errore", ex)
        End Try
    End Sub

    Private Sub OrdinaNome()
        Try
            ApplicaOrdinamento(CLSFile.OrdineDiVisualizzazione.Nome.ToString())
        Catch ex As Exception
            ScriviLogCompleto("OrdinaNome - Errore", ex)
        End Try
    End Sub

    Private Sub OrdinaDataRecente()
        Try
            ApplicaOrdinamento(CLSFile.OrdineDiVisualizzazione.DataRecente.ToString())
        Catch ex As Exception
            ScriviLogCompleto("OrdinaDataRecente - Errore", ex)
        End Try
    End Sub

    Private Sub OrdinaDataMenoRecente()
        Try
            ApplicaOrdinamento(CLSFile.OrdineDiVisualizzazione.DataMenoRecente.ToString())
        Catch ex As Exception
            ScriviLogCompleto("OrdinaDataMenoRecente - Errore", ex)
        End Try
    End Sub

    Private Sub OrdinaDimensioneCrescente()
        Try
            ApplicaOrdinamento(CLSFile.OrdineDiVisualizzazione.DimensioneCrescente.ToString())
        Catch ex As Exception
            ScriviLogCompleto("OrdinaDimensioneCrescente - Errore", ex)
        End Try
    End Sub

    Private Sub OrdinaDimensioneDecrescente()
        Try
            ApplicaOrdinamento(CLSFile.OrdineDiVisualizzazione.DimensioneDecrescente.ToString())
        Catch ex As Exception
            ScriviLogCompleto("OrdinaDimensioneDecrescente - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMenuOrdinamento()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Nome", AddressOf OrdinaNome)
            menu.aggiungi("Data (più recente)", AddressOf OrdinaDataRecente)
            menu.aggiungi("Data (meno recente)", AddressOf OrdinaDataMenoRecente)
            menu.aggiungi("Dimensione (crescente)", AddressOf OrdinaDimensioneCrescente)
            menu.aggiungi("Dimensione (decrescente)", AddressOf OrdinaDimensioneDecrescente)
            menu.avvia("Scegli ordinamento", True)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.MostraMenuOrdinamento - Errore durante visualizzazione menu", ex)
        End Try
    End Sub

    Private Sub ApplicaOrdinamento(nuovoOrdine As String)
        Try
            If [Enum].IsDefined(GetType(CLSFile.OrdineDiVisualizzazione), nuovoOrdine) Then
                ordinamentoCorrente = CType([Enum].Parse(GetType(CLSFile.OrdineDiVisualizzazione), nuovoOrdine), CLSFile.OrdineDiVisualizzazione)
                config.ModificaValore("ordinamento", nuovoOrdine)
                If Not String.IsNullOrEmpty(currentDirectory) Then
                    CaricaDirectory(currentDirectory)
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.ApplicaOrdinamento - Errore durante applicazione ordinamento", ex)
        End Try
    End Sub

    Private Function CaricamentoLocaleNonConsentito() As Boolean
        Try
            Return isFtpMenuPrincipaleMode OrElse isFtpElencoServerMode OrElse isFtpServerListMode OrElse isFtpBrowseMode OrElse isReteMenuMode OrElse isReteCondivisioniMode
        Catch ex As Exception
            ScriviLogCompleto("CaricamentoLocaleNonConsentito - Errore", ex)
            Return False
        End Try
    End Function

    Private Sub AnnullaCaricamentoDirectoryInCorso()
        Try
            _generazioneCaricamentoDirectory += 1
            If _ctsCaricamentoDirectory IsNot Nothing Then
                _ctsCaricamentoDirectory.Cancel()
                _ctsCaricamentoDirectory.Dispose()
                _ctsCaricamentoDirectory = Nothing
            End If
        Catch ex As Exception
            ScriviLogCompleto("AnnullaCaricamentoDirectoryInCorso - Errore", ex)
        End Try
    End Sub

    Private Sub CaricaDirectory(directory As String)
        Try
            AnnullaCaricamentoDirectoryInCorso()
            Dim generazione As Long = _generazioneCaricamentoDirectory
            uiContext.Post(Sub(state)
                               Try
                                   If generazione <> _generazioneCaricamentoDirectory Then Return
                                   PreparaCaricamentoDirectory(directory)
                               Catch exPost As Exception
                                   ScriviLogCompleto("CaricaDirectory Post - Errore", exPost)
                               End Try
                           End Sub, Nothing)
        Catch ex As Exception
            ScriviLogCompleto("CaricaDirectory - Errore", ex)
        End Try
    End Sub

    Private Function IsNotDirectory(x As CLSFile.FileItem) As Boolean
        Try
            Return Not x.IsDirectory
        Catch ex As Exception
            ScriviLogCompleto("IsNotDirectory - Errore", ex)
            Return False
        End Try
    End Function

    Private Function IsAudioVideoFile(f As CLSFile.FileItem) As Boolean
        Try
            Return logic.estensioniAudioVideo.Contains(System.IO.Path.GetExtension(f.Nome).ToLower())
        Catch ex As Exception
            ScriviLogCompleto("IsAudioVideoFile - Errore", ex)
            Return False
        End Try
    End Function

    Private Sub PreparaCaricamentoDirectory(state As Object)
        Dim directory As String = CStr(state)
        Try
            If lista Is Nothing Then Return
            ' I risultati di una ricerca sono una cartella virtuale.
            If directory IsNot Nothing AndAlso directory.StartsWith(PrefissoRicerca, StringComparison.Ordinal) Then
                RicaricaRisultatiRicerca(directory)
                Return
            End If
            If CaricamentoLocaleNonConsentito() Then Return
            isDriveSelectionMode = False
            isReteMenuMode = False
            isReteCondivisioniMode = False
            wgInternalList.Pulisci()
            lista.pulisci()
            InizializzaOpzioniStandard()

            If Not WGRete.IsRemoto(directory) Then
                logic.SetupFileSystemWatcher(directory)
            End If

            If logic.IsRadiceRisorsa(directory) Then
                If Not logic.IsDriveReady(directory) Then
                    Dim messaggio2 As New WGMessaggi()
                    messaggio2.Messaggio("Il disco non è pronto.")
                    messaggio2.ShowDialog()
                    Return
                End If
            End If

            Dim cts As New CancellationTokenSource()
            _ctsCaricamentoDirectory = cts
            Dim token As CancellationToken = cts.Token

            Dim t As New Thread(Sub() EseguiLetturaDirectoryThread(directory, token))
            t.IsBackground = True
            t.Start()
        Catch ex As UnauthorizedAccessException
            ScriviLogCompleto($"FileManager.CaricaDirectory - Accesso negato directory '{directory}'", ex)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile accedere alla cartella. Accesso negato.")
            msg.ShowDialog()
            CartellaPrecedente()
        Catch ex As PathTooLongException
            ScriviLogCompleto($"FileManager.CaricaDirectory - Percorso troppo lungo '{directory}'", ex)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Il percorso è troppo lungo.")
            msg.ShowDialog()
            CartellaPrecedente()
        Catch ex As DirectoryNotFoundException
            ScriviLogCompleto($"FileManager.CaricaDirectory - Directory non trovata '{directory}'", ex)
            Dim msg As New WGMessaggi()
            msg.Messaggio("La cartella non esiste più.")
            msg.ShowDialog()
            CartellaPrecedente()
        Catch ex As IOException
            ScriviLogCompleto($"FileManager.CaricaDirectory - Errore I/O directory '{directory}'", ex)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impossibile leggere la cartella. Risorsa non disponibile.")
            msg.ShowDialog()
            CartellaPrecedente()
        Catch ex As Exception
            ScriviLogCompleto($"FileManager.CaricaDirectory - Errore generale caricamento directory '{directory}'", ex)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Si è verificato un errore inatteso. Controlla il log.")
            msg.ShowDialog()
            CartellaPrecedente()
        End Try
    End Sub

    Private Sub EseguiLetturaDirectoryThread(directory As String, token As CancellationToken)
        Try
            If WGRete.IsRemoto(directory) Then
                Dim codiceAccesso As Integer = WGRete.VerificaAccesso(directory, 10000)
                If token.IsCancellationRequested Then Return
                Select Case codiceAccesso
                    Case 0
                    Case 5
                        Throw New UnauthorizedAccessException(WGRete.DescriviErrore(codiceAccesso))
                    Case 2, 3
                        Throw New DirectoryNotFoundException(WGRete.DescriviErrore(codiceAccesso))
                    Case Else
                        Throw New IOException(WGRete.DescriviErrore(codiceAccesso))
                End Select
            End If
            Dim items As List(Of CLSFile.FileItem) = logic.OttieniContenutoCartella(directory, currentExtensionFilter, ordinamentoCorrente, token)
            If token.IsCancellationRequested Then Return
            uiContext.Post(Sub(s) CompletaCaricamentoDirectory(directory, items, token), Nothing)
        Catch ex As OperationCanceledException
            ScriviLogCompleto($"FileManager.CaricaDirectory - Lettura annullata (uscita dalla cartella): {directory}", Nothing)
        Catch ex As UnauthorizedAccessException
            If Not token.IsCancellationRequested Then
                uiContext.Post(Sub(s) MostraErroreCaricamentoDirectory("Impossibile accedere alla cartella. Accesso negato."), Nothing)
            End If
            ScriviLogCompleto($"FileManager.CaricaDirectory - Accesso negato directory '{directory}'", ex)
        Catch ex As PathTooLongException
            If Not token.IsCancellationRequested Then
                uiContext.Post(Sub(s) MostraErroreCaricamentoDirectory("Il percorso è troppo lungo."), Nothing)
            End If
            ScriviLogCompleto($"FileManager.CaricaDirectory - Percorso troppo lungo '{directory}'", ex)
        Catch ex As DirectoryNotFoundException
            If Not token.IsCancellationRequested Then
                uiContext.Post(Sub(s) MostraErroreCaricamentoDirectory("La cartella non esiste più."), Nothing)
            End If
            ScriviLogCompleto($"FileManager.CaricaDirectory - Directory non trovata '{directory}'", ex)
        Catch ex As IOException
            If Not token.IsCancellationRequested Then
                uiContext.Post(Sub(s) MostraErroreCaricamentoDirectory("Impossibile leggere la cartella. Risorsa non disponibile."), Nothing)
            End If
            ScriviLogCompleto($"FileManager.CaricaDirectory - Errore I/O directory '{directory}'", ex)
        Catch ex As Exception
            If Not token.IsCancellationRequested Then
                uiContext.Post(Sub(s) MostraErroreCaricamentoDirectory("Si è verificato un errore inatteso. Controlla il log."), Nothing)
            End If
            ScriviLogCompleto($"FileManager.CaricaDirectory - Errore generale caricamento directory '{directory}'", ex)
        End Try
    End Sub

    Private Sub MostraErroreCaricamentoDirectory(messaggioTesto As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggioTesto)
            msg.ShowDialog()
            CartellaPrecedente()
        Catch ex As Exception
            ScriviLogCompleto("MostraErroreCaricamentoDirectory - Errore", ex)
        End Try
    End Sub

    Private Sub CompletaCaricamentoDirectory(directory As String, items As List(Of CLSFile.FileItem), token As CancellationToken)
        Dim idDaEvidenziare As String = _percorsoDaEvidenziareDopoCaricamento
        _percorsoDaEvidenziareDopoCaricamento = ""
        Try
            If token.IsCancellationRequested Then Return
            If lista Is Nothing Then Return
            If items Is Nothing Then Return
            If CaricamentoLocaleNonConsentito() Then Return

            Dim files = items.Where(AddressOf IsNotDirectory).ToList()
            If files.Any(AddressOf IsAudioVideoFile) Then
                Dim opzioneDiRiferimento As String = ""
                If lista.opzioneEsiste("Riproduci") Then
                    opzioneDiRiferimento = "Riproduci"
                ElseIf lista.opzioneEsiste("Apri nell'editor") Then
                    opzioneDiRiferimento = "Apri nell'editor"
                ElseIf lista.opzioneEsiste("Apri") Then
                    opzioneDiRiferimento = "Apri"
                End If
                If Not String.IsNullOrEmpty(opzioneDiRiferimento) Then
                    lista.aggiungiOpzioneDopo(opzioneDiRiferimento, "Riproduci tutta la cartella", AddressOf RiproduciCartellaCorrente)
                Else
                    lista.aggiungiOpzione("Riproduci tutta la cartella", AddressOf RiproduciCartellaCorrente)
                End If
            End If
            If items.Count = 0 Then
                lista.aggiungiElemento("La cartella è vuota")
                lista.invioNonConsentito = True
            Else
                lista.invioNonConsentito = False
                For Each item In items
                    If token.IsCancellationRequested Then Exit For
                    Try
                        wgInternalList.Aggiungi(item.PercorsoCompleto)
                        lista.aggiungiElemento(item.Nome, item.PercorsoCompleto)
                    Catch ex As Exception
                        ScriviLogCompleto($"FileManager.CaricaDirectory - Errore aggiunta item '{item.Nome}'", ex)
                    End Try
                Next
            End If

            If Not String.IsNullOrEmpty(idDaEvidenziare) Then
                Try
                    lista.posiziona(idDaEvidenziare)
                Catch exPos As Exception
                    ScriviLogCompleto("CompletaCaricamentoDirectory - Errore posizionamento elemento", exPos)
                End Try
            End If
        Catch ex As Exception
            ScriviLogCompleto($"FileManager.CompletaCaricamentoDirectory - Errore generale '{directory}'", ex)
        End Try
    End Sub

    Private Sub CopiaUnitaEsterna()
        Try
            Dim unitaSelezionate As List(Of String) = lista.ottieniElementi()
            If unitaSelezionate Is Nothing OrElse unitaSelezionate.Count = 0 Then
                Dim msgVuoto As New WGMessaggi()
                msgVuoto.Messaggio("Nessuna unità selezionata.")
                msgVuoto.ShowDialog()
                Return
            End If
            SvuotaAppuntiFtp(True)
            elementi.Clear()
            elementi.AddRange(unitaSelezionate)
            copia = True
            taglia = False
            InserisciNellaClipboard("copiare")
            Dim msg As New WGMessaggi()
            If unitaSelezionate.Count = 1 Then
                msg.Messaggio("Unità copiata negli appunti. Vai nella cartella di destinazione e seleziona 'Incolla'.")
            Else
                msg.Messaggio(unitaSelezionate.Count.ToString() & " unità copiate negli appunti. Vai nella cartella di destinazione e seleziona 'Incolla'.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("FileManager.CopiaUnitaEsterna - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMenuPulizia()
        Try
            Dim menuPulizia As New WGMenu()
            menuPulizia.aggiungi("Eliminazione file temporanei", AddressOf AvviaPuliziaTemporanei)
            menuPulizia.aggiungi("Svuota Cestino", AddressOf AvviaPuliziaCestino)
            menuPulizia.aggiungi("Eliminazione file duplicati (Download/Doc/Foto)", AddressOf AvviaPuliziaDuplicati)
            menuPulizia.aggiungi("Pulizia avanzata di Windows (richiede amministratore)", AddressOf AvviaPuliziaAvanzata)
            menuPulizia.aggiungi("Pulizia dei componenti di Windows con DISM (richiede amministratore, può durare diversi minuti)", AddressOf AvviaPuliziaComponenti)
            menuPulizia.aggiungi("Pulizia completa (temporanei, cestino e pulizia avanzata)", AddressOf AvviaPuliziaCompleta)
            menuPulizia.avvia("Pulizia Computer", True)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.MostraMenuPulizia - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaPuliziaTemporanei()
        Try
            ProcessaPulizia("temp")
        Catch ex As Exception
            ScriviLogCompleto("AvviaPuliziaTemporanei - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaPuliziaCestino()
        Try
            ProcessaPulizia("cestino")
        Catch ex As Exception
            ScriviLogCompleto("AvviaPuliziaCestino - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaPuliziaDuplicati()
        Try
            ProcessaPulizia("duplicati")
        Catch ex As Exception
            ScriviLogCompleto("AvviaPuliziaDuplicati - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaPuliziaAvanzata()
        Try
            If Not logic.IsUserAdministrator() Then
                MostraMessaggioAmministratore()
                Return
            End If
            ChiediConfermaPulizia("avanzata", "La pulizia avanzata elimina la cache degli aggiornamenti di Windows, la cache di Ottimizzazione recapito, i rapporti degli errori, i file di dump della memoria, i vecchi registri di servizio e i file temporanei di tutti gli utenti. Per qualche istante Windows Update viene fermato e poi riavviato. Vuoi continuare?")
        Catch ex As Exception
            ScriviLogCompleto("AvviaPuliziaAvanzata - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaPuliziaComponenti()
        Try
            If Not logic.IsUserAdministrator() Then
                MostraMessaggioAmministratore()
                Return
            End If
            ChiediConfermaPulizia("componenti", "La pulizia dei componenti di Windows rimuove le vecchie versioni dei componenti sostituite dagli aggiornamenti. Può durare diversi minuti. Vuoi continuare?")
        Catch ex As Exception
            ScriviLogCompleto("AvviaPuliziaComponenti - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaPuliziaCompleta()
        Try
            If logic.IsUserAdministrator() Then
                ChiediConfermaPulizia("completa", "La pulizia completa elimina i file temporanei, svuota il cestino ed esegue la pulizia avanzata di Windows. Per qualche istante Windows Update viene fermato e poi riavviato. Vuoi continuare?")
            Else
                ChiediConfermaPulizia("completa", "Il programma non è stato avviato come amministratore: verranno eliminati solo i file temporanei dell'utente e il cestino, mentre la pulizia avanzata di Windows verrà saltata. Vuoi continuare?")
            End If
        Catch ex As Exception
            ScriviLogCompleto("AvviaPuliziaCompleta - Errore", ex)
        End Try
    End Sub

    Private Sub MostraMessaggioAmministratore()
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio("Questa pulizia richiede i diritti di amministratore. Chiudi il programma e riavvialo come amministratore.")
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("MostraMessaggioAmministratore - Errore", ex)
        End Try
    End Sub

    Private Sub ChiediConfermaPulizia(tipo As String, testo As String)
        Try
            _tipoPuliziaDaConfermare = tipo
            _domandaPulizia = New WGDomanda()
            AddHandler _domandaPulizia.rispostadomanda, AddressOf RispostaConfermaPulizia
            _domandaPulizia.avvia(testo)
        Catch ex As Exception
            ScriviLogCompleto("ChiediConfermaPulizia - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaConfermaPulizia(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes AndAlso Not String.IsNullOrEmpty(_tipoPuliziaDaConfermare) Then
                ProcessaPulizia(_tipoPuliziaDaConfermare)
            End If
            _tipoPuliziaDaConfermare = ""
        Catch ex As Exception
            ScriviLogCompleto("RispostaConfermaPulizia - Errore", ex)
        End Try
    End Sub

    Private Sub ProcessaPulizia(tipo As String)
        Try
            processoincorso = New WGProcessoInCorso()
            If tipo = "componenti" Then
                processoincorso.Avvia("Pulizia dei componenti di Windows in corso, può richiedere diversi minuti...")
            Else
                processoincorso.Avvia("Pulizia del computer in corso, attendere...")
            End If
            Dim t As New Thread(AddressOf PuliziaThreadProc)
            t.IsBackground = True
            t.Start(tipo)
        Catch ex As Exception
            ScriviLogCompleto("ProcessaPulizia - Errore", ex)
        End Try
    End Sub

    Private Sub PuliziaThreadProc(state As Object)
        Dim tipo As String = CStr(state)
        Try
            Dim risultato As WGPuliziaPC.RisultatoPulizia = logic.EseguiPuliziaDettagliata(tipo)
            uiContext.Post(AddressOf PuliziaCompletataCallback, risultato)
        Catch ex As Exception
            uiContext.Post(AddressOf PuliziaErroreCallback, Tuple.Create(tipo, ex))
        End Try
    End Sub

    Private Sub PuliziaCompletataCallback(state As Object)
        Try
            processoincorso.Fine()
            Dim risultato As WGPuliziaPC.RisultatoPulizia = TryCast(state, WGPuliziaPC.RisultatoPulizia)
            Dim msg As New WGMessaggi()
            If risultato Is Nothing Then
                msg.Messaggio("Errore durante la pulizia. Controlla il log.")
                msg.ShowDialog()
                Return
            End If
            _risultatoPulizia = risultato
            Dim formattatore As New WGPuliziaPC()
            msg.Messaggio(formattatore.CostruisciTestoBreve(risultato))
            msg.ShowDialog()
            _domandaPulizia = New WGDomanda()
            AddHandler _domandaPulizia.rispostadomanda, AddressOf RispostaDettaglioPulizia
            _domandaPulizia.avvia("Vuoi consultare il dettaglio della pulizia?")
        Catch ex As Exception
            ScriviLogCompleto("FileManager.PuliziaCompletataCallback - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaDettaglioPulizia(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes AndAlso _risultatoPulizia IsNot Nothing Then
                Dim formattatore As New WGPuliziaPC()
                MostraReport("Dettaglio della pulizia", formattatore.CostruisciDettaglio(_risultatoPulizia))
            End If
        Catch ex As Exception
            ScriviLogCompleto("RispostaDettaglioPulizia - Errore", ex)
        End Try
    End Sub

    Private Sub MostraReport(titolo As String, righe As List(Of String))
        Try
            _listaReport = New WGMenuLista()
            _listaReport.impostaTitolo(titolo)
            _listaReport.invioNonConsentito = True
            AddHandler _listaReport.escPremuto, AddressOf OnEscListaReport
            For Each riga As String In righe
                _listaReport.aggiungiElemento(riga)
            Next
            _listaReport.avvia()
        Catch ex As Exception
            ScriviLogCompleto("MostraReport - Errore", ex)
        End Try
    End Sub

    Private Sub OnEscListaReport()
        Try
            If _listaReport IsNot Nothing Then
                RemoveHandler _listaReport.escPremuto, AddressOf OnEscListaReport
                _listaReport.abbandona()
                _listaReport = Nothing
            End If
            If lista IsNot Nothing Then lista.avvia()
        Catch ex As Exception
            ScriviLogCompleto("OnEscListaReport - Errore", ex)
        End Try
    End Sub

    Private Sub PuliziaErroreCallback(state As Object)
        Try
            processoincorso.Fine()
            Dim info = CType(state, Tuple(Of String, Exception))
            Dim msg As New WGMessaggi()
            msg.Messaggio("Errore durante la pulizia.")
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("FileManager.PuliziaErroreCallback - Errore", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Ctrl C, Ctrl X e Ctrl V copiano, tagliano e incollano gli elementi, come le voci Copia, Taglia e Incolla delle opzioni.
    ''' Valgono solo nell'elenco di file e cartelle, sul computer e sul server FTP.
    ''' </summary>
    Private Sub OnScorciatoiaCtrl(sender As Object, e As ScorciatoiaCtrlEventArgs)
        Try
            If e.Maiusc Then Return
            If e.Codice <> Keys.C AndAlso e.Codice <> Keys.X AndAlso e.Codice <> Keys.V Then Return
            If isDriveSelectionMode OrElse isExtensionSelectionMode OrElse isFtpServerListMode OrElse isFtpElencoServerMode OrElse isFtpMenuPrincipaleMode OrElse isReteMenuMode OrElse isReteCondivisioniMode Then Return
            e.Gestita = True
            Select Case e.Codice
                Case Keys.C
                    If isFtpBrowseMode Then CopiaElementoFtpSelezionato() Else copiaevent()
                Case Keys.X
                    If isFtpBrowseMode Then TagliaElementoFtpSelezionato() Else tagliaevent()
                Case Keys.V
                    If isFtpBrowseMode Then IncollaFtp() Else incolla()
            End Select
        Catch ex As Exception
            ScriviLogCompleto("FileManager.OnScorciatoiaCtrl - Errore", ex)
        End Try
    End Sub

    Private Sub tagliaevent()
        Try
            elementi.Clear()
            elementi.AddRange(lista.ottieniElementi())
            If elementi.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non sono stati selezionati elementi da spostare.")
                messaggio.ShowDialog()
            Else
                SvuotaAppuntiFtp(True) ' gli appunti locali e quelli FTP sono mutuamente esclusivi
                taglia = True
                copia = False
                InserisciNellaClipboard("spostare")
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.tagliaevent - Errore", ex)
        End Try
    End Sub

    Private Sub copiaevent()
        Try
            elementi.Clear()
            elementi.AddRange(lista.ottieniElementi())
            If elementi.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non sono stati selezionati elementi da copiare")
                messaggio.ShowDialog()
            Else
                SvuotaAppuntiFtp(True) ' gli appunti locali e quelli FTP sono mutuamente esclusivi
                copia = True
                taglia = False
                InserisciNellaClipboard("copiare")
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.copiaevent - Errore", ex)
        End Try
    End Sub

    Private Function VerificaAccessoPath(path As String) As Boolean
        Try
            If logic.IsWpdPath(path) Then Return True
            If WGRete.IsRemoto(path) Then Return WGRete.EsisteDirectory(path, 8000)
            Dim permission As New FileIOPermission(FileIOPermissionAccess.Write, path)
            permission.Demand()
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Function EseguiConRiprova(azione As Action, tentativi As Integer, attesaMs As Integer) As Boolean
        For i As Integer = 1 To tentativi
            Try
                azione()
                Return True
            Catch ex As Exception
                If i = tentativi Then
                    ScriviLogCompleto($"EseguiConRiprova - Fallito dopo {tentativi} tentativi.", ex)
                    Return False
                End If
                Thread.Sleep(attesaMs)
            End Try
        Next
        Return False
    End Function

    Private Sub TentativoLetturaClipboard()
        Try
            If System.Windows.Forms.Clipboard.ContainsFileDropList() Then
                Dim dropList = System.Windows.Forms.Clipboard.GetFileDropList()
                If dropList IsNot Nothing AndAlso dropList.Count > 0 Then
                    For Each path In dropList
                        elementi.Add(path)
                    Next
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("TentativoLetturaClipboard - Errore", ex)
        End Try
    End Sub

    Private Sub IncollaDaClipboardAction(elementiDaIncollare As List(Of String), ByRef isClipboardSystem As Boolean)
        Try
            If System.Windows.Forms.Clipboard.ContainsFileDropList() Then
                Dim dropList = System.Windows.Forms.Clipboard.GetFileDropList()
                If dropList IsNot Nothing AndAlso dropList.Count > 0 Then
                    elementiDaIncollare.Clear()
                    For Each path In dropList
                        elementiDaIncollare.Add(path)
                    Next
                    isClipboardSystem = True
                    Dim dataObj = System.Windows.Forms.Clipboard.GetDataObject()
                    If dataObj IsNot Nothing AndAlso dataObj.GetDataPresent("Preferred DropEffect") Then
                        Dim ms As MemoryStream = TryCast(dataObj.GetData("Preferred DropEffect"), MemoryStream)
                        If ms IsNot Nothing Then
                            Dim effect As Byte = ms.ToArray()(0)
                            If effect = 2 Then
                                taglia = True
                                copia = False
                            ElseIf effect = 5 Then
                                copia = True
                                taglia = False
                            End If
                        End If
                    End If
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("IncollaDaClipboardAction - Errore", ex)
        End Try
    End Sub

    Private Sub incolla()
        Try
            If elementiFtp.Count > 0 AndAlso ftpClipboardServer IsNot Nothing Then
                IncollaDaAppuntiFtp()
                Return
            End If

            Dim elementiDaIncollare As New List(Of String)
            Dim isClipboardSystem As Boolean = False
            Dim azioneRilevata As String = If(taglia, "spostare", "copiare")
            Dim successoLetturaClipboard As Boolean = False
            For i As Integer = 1 To 5
                Try
                    IncollaDaClipboardAction(elementiDaIncollare, isClipboardSystem)
                    successoLetturaClipboard = True
                    Exit For
                Catch ex As Exception
                    log.ScriviLog($"incolla - Tentativo {i}/5 lettura clipboard fallito: {ex.Message}")
                    Thread.Sleep(100)
                End Try
            Next
            If Not successoLetturaClipboard Then
            End If
            If elementiDaIncollare.Count = 0 AndAlso elementi.Count > 0 Then
                elementiDaIncollare.AddRange(elementi)
                isClipboardSystem = False
            End If
            If elementiDaIncollare.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun elemento negli appunti da incollare.")
                msg.ShowDialog()
                Return
            End If
            If Not logic.IsWpdPath(currentDirectory) AndAlso Not VerificaAccessoPath(currentDirectory) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non hai i permessi necessari per scrivere in questa cartella, oppure la cartella non è raggiungibile.")
                msg.ShowDialog()
                Return
            End If
            Dim centro = WGCentroDiControllo.GetInstance()
            Dim operazioneAvviata As Boolean = False
            If logic.IsWpdPath(currentDirectory) Then
                Dim parts = currentDirectory.Split("|"c)
                Dim deviceId = parts(1)
                Dim objectId = If(parts.Length > 2, parts(2), "\")
                If taglia Then
                    centro.spostaportatile(elementiDaIncollare, deviceId, objectId)
                Else
                    centro.copiaportatile(elementiDaIncollare, deviceId, objectId)
                End If
                operazioneAvviata = True
            Else
                If logic.IsWpdPath(elementiDaIncollare(0)) Then
                    If taglia Then
                        centro.spostaportatile(elementiDaIncollare, currentDirectory, "")
                    Else
                        centro.copiaportatile(elementiDaIncollare, currentDirectory, "")
                    End If
                    operazioneAvviata = True
                Else
                    If taglia Then
                        centro.AggiungiSpostamento(elementiDaIncollare, currentDirectory)
                    Else
                        centro.AggiungiCopia(elementiDaIncollare, currentDirectory)
                    End If
                    operazioneAvviata = True
                End If
            End If
            If operazioneAvviata Then
                centro.AvviaOperazioniFile()
                If taglia AndAlso Not isClipboardSystem Then
                    taglia = False
                    elementi.Clear()
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.incolla - Errore durante l'incolla", ex)
        End Try
    End Sub

    ''' <summary>
    ''' Esegue l'incolla nella cartella locale corrente di elementi copiati/tagliati
    ''' in precedenza da una cartella FTP (vedi CopiaElementoFtpSelezionato/
    ''' TagliaElementoFtpSelezionato). Il download avviene solo ora, quando l'utente
    ''' sceglie dove metterlo nella gestione file.
    ''' </summary>
    Private Sub IncollaDaAppuntiFtp()
        Try
            If ftpClipboardSessione Is Nothing OrElse Not ftpClipboardSessione.Connessa Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("La sessione FTP di origine non è più attiva. Ripeti la copia.")
                msg.ShowDialog()
                SvuotaAppuntiFtp(True)
                Return
            End If
            If logic.IsWpdPath(currentDirectory) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è possibile scaricare elementi FTP direttamente su un dispositivo portatile.")
                msg.ShowDialog()
                Return
            End If
            If Not VerificaAccessoPath(currentDirectory) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non hai i permessi necessari per scrivere in questa cartella, oppure la cartella non è raggiungibile.")
                msg.ShowDialog()
                Return
            End If

            Dim eliminaSorgenteRemota As Boolean = ftpClipboardTaglia
            WGCentroDiControllo.GetInstance().AggiungiDownloadFtp(New List(Of String)(elementiFtp), currentDirectory, ftpClipboardServer, eliminaSorgenteRemota)

            If eliminaSorgenteRemota Then
                ' Come per il taglia locale, dopo l'uso gli appunti si svuotano.
                SvuotaAppuntiFtp(True)
            End If
        Catch ex As Exception
            ScriviLogCompleto("IncollaDaAppuntiFtp - Errore", ex)
        End Try
    End Sub

    Private Sub eliminaevent()
        Try
            fileecartelle.Clear()
            fileecartelle.AddRange(lista.ottieniElementi())
            If fileecartelle.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Impossibile eliminare. Nessun elemento selezionato.")
                messaggio.ShowDialog()
            Else
                Dim domandaTesto As String
                If fileecartelle.Count = 1 Then
                    domandaTesto = "Sei sicuro di voler eliminare l'elemento selezionato?"
                Else
                    domandaTesto = "Sei sicuro di voler eliminare tutti gli elementi selezionati?"
                End If
                If fileecartelle.Any(Function(p) WGRete.IsRemoto(p)) Then
                    domandaTesto &= " Gli elementi su unità di rete non passano dal cestino e potrebbero non essere recuperabili."
                End If
                domanda = New WGDomanda()
                AddHandler domanda.rispostadomanda, AddressOf rispostaelimina
                domanda.avvia(domandaTesto)
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.eliminaevent - Errore", ex)
        End Try
    End Sub

    Private Sub rispostaelimina(rispostadom As DialogResult)
        Try
            If rispostadom = DialogResult.Yes Then
                Dim centro = WGCentroDiControllo.GetInstance()
                centro.UsaCestino = elimina
                Dim operazioneAvviata As Boolean = False
                Dim wpdPaths As New List(Of String)
                Dim localPaths As New List(Of String)
                Dim lastDeviceId As String = ""
                For Each path In fileecartelle
                    If logic.IsWpdPath(path) Then
                        Dim parts = path.Split("|"c)
                        lastDeviceId = parts(1)
                        Dim objectId = parts(2)
                        wpdPaths.Add(objectId)
                    Else
                        localPaths.Add(path)
                    End If
                Next
                If wpdPaths.Count > 0 AndAlso Not String.IsNullOrEmpty(lastDeviceId) Then
                    centro.eliminaportatile(lastDeviceId, wpdPaths)
                    operazioneAvviata = True
                End If
                If localPaths.Count > 0 Then
                    centro.AggiungiEliminazione(localPaths)
                    operazioneAvviata = True
                End If
                If operazioneAvviata Then
                    centro.AvviaOperazioniFile()
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.rispostaelimina - Errore", ex)
        End Try
    End Sub

    Private Sub AggiungiRimuoviPreferiti()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Seleziona un elemento da aggiungere o rimuovere dai preferiti.")
                msg.ShowDialog()
                Return
            End If
            If elementiSelezionati.Count = 1 Then
                _tempPreferitoPath = elementiSelezionati(0)
                Dim t As New Thread(AddressOf GestionePreferitiThread)
                t.IsBackground = True
                t.Start()
                Return
            End If
            _tempPreferitiMultipli = New List(Of String)(elementiSelezionati)
            Dim menu As New WGMenu()
            menu.aggiungi("Aggiungi tutti ai preferiti", AddressOf AggiungiPreferitiMultipli)
            menu.aggiungi("Rimuovi tutti dai preferiti", AddressOf RimuoviPreferitiMultipli)
            menu.avvia("Preferiti", True)
        Catch ex As Exception
            ScriviLogCompleto("Errore avvio preferiti", ex)
        End Try
    End Sub

    Private Class ContestoPreferiti
        Public Percorsi As New List(Of String)()
        Public Aggiungi As Boolean = True
        Public Modificati As Integer = 0
        Public GiaInStato As Integer = 0
        Public Falliti As Integer = 0
    End Class

    Private Sub AggiungiPreferitiMultipli()
        Try
            AvviaGestionePreferitiMultipli(True)
        Catch ex As Exception
            ScriviLogCompleto("AggiungiPreferitiMultipli - Errore", ex)
        End Try
    End Sub

    Private Sub RimuoviPreferitiMultipli()
        Try
            AvviaGestionePreferitiMultipli(False)
        Catch ex As Exception
            ScriviLogCompleto("RimuoviPreferitiMultipli - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaGestionePreferitiMultipli(aggiungi As Boolean)
        Try
            If _tempPreferitiMultipli Is Nothing OrElse _tempPreferitiMultipli.Count = 0 Then Return
            Dim contesto As New ContestoPreferiti()
            contesto.Percorsi.AddRange(_tempPreferitiMultipli)
            contesto.Aggiungi = aggiungi
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia(If(aggiungi, "Aggiunta ai preferiti in corso...", "Rimozione dai preferiti in corso..."))
            Dim t As New Thread(AddressOf GestionePreferitiMultipliThread)
            t.IsBackground = True
            t.Start(contesto)
        Catch ex As Exception
            ScriviLogCompleto("AvviaGestionePreferitiMultipli - Errore", ex)
            Try
                If processoincorso IsNot Nothing Then processoincorso.Fine()
            Catch exFine As Exception
                ScriviLogCompleto("AvviaGestionePreferitiMultipli - Errore chiusura attesa", exFine)
            End Try
        End Try
    End Sub

    Private Sub GestionePreferitiMultipliThread(state As Object)
        Dim contesto As ContestoPreferiti = TryCast(state, ContestoPreferiti)
        Try
            If contesto Is Nothing Then Return
            For Each percorso As String In contesto.Percorsi
                Try
                    If logic.IsDirectory(percorso) Then
                        Dim ePreferito As Boolean = favoritesManager.IsPreferito(percorso)
                        If contesto.Aggiungi = ePreferito Then
                            contesto.GiaInStato += 1
                        ElseIf contesto.Aggiungi Then
                            favoritesManager.AggiungiAPreferito(percorso)
                            contesto.Modificati += 1
                        Else
                            favoritesManager.RimuoviPreferito(percorso)
                            contesto.Modificati += 1
                        End If
                    ElseIf logic.IsMediaFile(percorso) Then
                        Dim manager As New CLSBrani()
                        Dim ePreferito As Boolean = manager.IsPreferitoAsync(percorso).GetAwaiter().GetResult()
                        If contesto.Aggiungi = ePreferito Then
                            contesto.GiaInStato += 1
                        ElseIf contesto.Aggiungi Then
                            manager.AggiungiBranoPreferitoAsync(percorso).GetAwaiter().GetResult()
                            contesto.Modificati += 1
                        Else
                            manager.RimuoviBranoPreferitoAsync(percorso).GetAwaiter().GetResult()
                            contesto.Modificati += 1
                        End If
                    ElseIf logic.IsFile(percorso) Then
                        Dim manager As New FilePreferiti()
                        Dim ePreferito As Boolean = manager.IsPreferito(percorso)
                        If contesto.Aggiungi = ePreferito Then
                            contesto.GiaInStato += 1
                        ElseIf contesto.Aggiungi Then
                            manager.AggiungiAPreferito(percorso)
                            contesto.Modificati += 1
                        Else
                            manager.RimuoviPreferito(percorso)
                            contesto.Modificati += 1
                        End If
                    Else
                        contesto.Falliti += 1
                    End If
                Catch exPercorso As Exception
                    contesto.Falliti += 1
                    ScriviLogCompleto($"GestionePreferitiMultipliThread - Errore su '{percorso}'", exPercorso)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("GestionePreferitiMultipliThread - Errore", ex)
        Finally
            Try
                uiContext.Post(AddressOf OnPreferitiMultipliTerminati, contesto)
            Catch exPost As Exception
                ScriviLogCompleto("GestionePreferitiMultipliThread - Errore notifica interfaccia", exPost)
            End Try
        End Try
    End Sub

    Private Sub OnPreferitiMultipliTerminati(state As Object)
        Try
            If processoincorso IsNot Nothing Then processoincorso.Fine()
            Dim contesto As ContestoPreferiti = TryCast(state, ContestoPreferiti)
            If contesto Is Nothing Then Return
            If contesto.Percorsi.Count > 0 Then AggiornaOpzioniContestuali(contesto.Percorsi(0))
            Dim parti As New List(Of String)()
            If contesto.Aggiungi Then
                parti.Add("Elementi aggiunti ai preferiti: " & contesto.Modificati.ToString() & ".")
                If contesto.GiaInStato > 0 Then parti.Add("Già presenti: " & contesto.GiaInStato.ToString() & ".")
            Else
                parti.Add("Elementi rimossi dai preferiti: " & contesto.Modificati.ToString() & ".")
                If contesto.GiaInStato > 0 Then parti.Add("Non presenti nei preferiti: " & contesto.GiaInStato.ToString() & ".")
            End If
            If contesto.Falliti > 0 Then parti.Add("Non gestiti per errore: " & contesto.Falliti.ToString() & ".")
            Dim msg As New WGMessaggi()
            msg.Messaggio(String.Join(" ", parti))
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("OnPreferitiMultipliTerminati - Errore", ex)
        End Try
    End Sub

    Private Sub GestionePreferitiThread()
        Try
            If logic.IsDirectory(_tempPreferitoPath) Then
                Dim isFavorite As Boolean = favoritesManager.IsPreferito(_tempPreferitoPath)
                uiContext.Post(AddressOf MostraDomandaPreferitoDirectory, isFavorite)
            ElseIf logic.IsMediaFile(_tempPreferitoPath) Then
                Dim manager As New CLSBrani()
                Dim isFavorite As Boolean = manager.IsPreferitoAsync(_tempPreferitoPath).GetAwaiter().GetResult()
                uiContext.Post(AddressOf MostraDomandaPreferitoMedia, isFavorite)
            ElseIf logic.IsFile(_tempPreferitoPath) Then
                Dim manager As New FilePreferiti()
                Dim isFavorite As Boolean = manager.IsPreferito(_tempPreferitoPath)
                uiContext.Post(AddressOf MostraDomandaPreferitoFile, isFavorite)
            End If
        Catch ex As Exception
            ScriviLogCompleto("Errore thread preferiti", ex)
        End Try
    End Sub

    Private Sub MostraDomandaPreferitoDirectory(state As Object)
        Try
            Dim isFavorite As Boolean = CBool(state)
            If isFavorite Then
                Dim d As New WGDomanda()
                AddHandler d.rispostadomanda, AddressOf RispostaRimuoviPreferitoDir
                d.avvia("La cartella è già tra i preferiti. Vuoi rimuoverla?")
            Else
                Dim d As New WGDomanda()
                AddHandler d.rispostadomanda, AddressOf RispostaAggiungiPreferitoDir
                d.avvia("La cartella non è tra i preferiti. Vuoi aggiungerla?")
            End If
        Catch ex As Exception
            ScriviLogCompleto("MostraDomandaPreferitoDirectory - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaRimuoviPreferitoDir(res As DialogResult)
        Try
            If res = DialogResult.Yes Then
                favoritesManager.RimuoviPreferito(_tempPreferitoPath)
                AggiornaOpzioniContestuali(_tempPreferitoPath)
                Dim m As New WGMessaggi()
                m.Messaggio("Cartella rimossa dai preferiti.")
                m.ShowDialog()
            End If
        Catch ex As Exception
            Dim m As New WGMessaggi()
            m.Messaggio("Errore: " & ex.Message)
            m.ShowDialog()
            ScriviLogCompleto("RispostaRimuoviPreferitoDir - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaAggiungiPreferitoDir(res As DialogResult)
        Try
            If res = DialogResult.Yes Then
                favoritesManager.AggiungiAPreferito(_tempPreferitoPath)
                AggiornaOpzioniContestuali(_tempPreferitoPath)
                Dim m As New WGMessaggi()
                m.Messaggio("Cartella aggiunta ai preferiti.")
                m.ShowDialog()
            End If
        Catch ex As Exception
            Dim m As New WGMessaggi()
            m.Messaggio("Errore: " & ex.Message)
            m.ShowDialog()
            ScriviLogCompleto("RispostaAggiungiPreferitoDir - Errore", ex)
        End Try
    End Sub

    Private Sub MostraDomandaPreferitoMedia(state As Object)
        Try
            Dim isFavorite As Boolean = CBool(state)
            If isFavorite Then
                Dim d As New WGDomanda()
                AddHandler d.rispostadomanda, AddressOf RispostaRimuoviPreferitoMedia
                d.avvia("Il brano è già tra i preferiti. Vuoi rimuoverlo?")
            Else
                Dim d As New WGDomanda()
                AddHandler d.rispostadomanda, AddressOf RispostaAggiungiPreferitoMedia
                d.avvia("Il brano non è tra i preferiti. Vuoi aggiungerlo?")
            End If
        Catch ex As Exception
            ScriviLogCompleto("MostraDomandaPreferitoMedia - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaRimuoviPreferitoMedia(res As DialogResult)
        Try
            If res = DialogResult.Yes Then
                Dim t As New Thread(Sub()
                                        Try
                                            Dim manager As New CLSBrani()
                                            manager.RimuoviBranoPreferitoAsync(_tempPreferitoPath).GetAwaiter().GetResult()
                                            uiContext.Post(Sub(s)
                                                               Try
                                                                   AggiornaOpzioniContestuali(_tempPreferitoPath)
                                                                   Dim m As New WGMessaggi()
                                                                   m.Messaggio("Brano rimosso.")
                                                                   m.ShowDialog()
                                                               Catch exUI As Exception
                                                                   ScriviLogCompleto("RispostaRimuoviPreferitoMedia UI - Errore", exUI)
                                                               End Try
                                                           End Sub, Nothing)
                                        Catch ex As Exception
                                            ScriviLogCompleto("RispostaRimuoviPreferitoMedia - Errore thread", ex)
                                        End Try
                                    End Sub)
                t.IsBackground = True
                t.Start()
            End If
        Catch ex As Exception
            ScriviLogCompleto("RispostaRimuoviPreferitoMedia - Errore avvio", ex)
        End Try
    End Sub

    Private Sub RispostaAggiungiPreferitoMedia(res As DialogResult)
        Try
            If res = DialogResult.Yes Then
                Dim t As New Thread(Sub()
                                        Try
                                            Dim manager As New CLSBrani()
                                            manager.AggiungiBranoPreferitoAsync(_tempPreferitoPath).GetAwaiter().GetResult()
                                            uiContext.Post(Sub(s)
                                                               Try
                                                                   AggiornaOpzioniContestuali(_tempPreferitoPath)
                                                                   Dim m As New WGMessaggi()
                                                                   m.Messaggio("Brano aggiunto.")
                                                                   m.ShowDialog()
                                                               Catch exUI As Exception
                                                                   ScriviLogCompleto("RispostaAggiungiPreferitoMedia UI - Errore", exUI)
                                                               End Try
                                                           End Sub, Nothing)
                                        Catch ex As Exception
                                            ScriviLogCompleto("RispostaAggiungiPreferitoMedia - Errore thread", ex)
                                        End Try
                                    End Sub)
                t.IsBackground = True
                t.Start()
            End If
        Catch ex As Exception
            ScriviLogCompleto("RispostaAggiungiPreferitoMedia - Errore avvio", ex)
        End Try
    End Sub

    Private Sub MostraDomandaPreferitoFile(state As Object)
        Try
            Dim isFavorite As Boolean = CBool(state)
            If isFavorite Then
                Dim d As New WGDomanda()
                AddHandler d.rispostadomanda, AddressOf RispostaRimuoviPreferitoFile
                d.avvia("Il file è già tra i preferiti. Vuoi rimuoverlo?")
            Else
                Dim d As New WGDomanda()
                AddHandler d.rispostadomanda, AddressOf RispostaAggiungiPreferitoFile
                d.avvia("Il file non è tra i preferiti. Vuoi aggiungerlo?")
            End If
        Catch ex As Exception
            ScriviLogCompleto("MostraDomandaPreferitoFile - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaRimuoviPreferitoFile(res As DialogResult)
        Try
            If res = DialogResult.Yes Then
                Dim manager As New FilePreferiti()
                manager.RimuoviPreferito(_tempPreferitoPath)
                AggiornaOpzioniContestuali(_tempPreferitoPath)
                Dim m As New WGMessaggi()
                m.Messaggio("File rimosso.")
                m.ShowDialog()
            End If
        Catch ex As Exception
            Dim m As New WGMessaggi()
            m.Messaggio("Errore.")
            m.ShowDialog()
            ScriviLogCompleto("RispostaRimuoviPreferitoFile - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaAggiungiPreferitoFile(res As DialogResult)
        Try
            If res = DialogResult.Yes Then
                Dim manager As New FilePreferiti()
                manager.AggiungiAPreferito(_tempPreferitoPath)
                AggiornaOpzioniContestuali(_tempPreferitoPath)
                Dim m As New WGMessaggi()
                m.Messaggio("File aggiunto.")
                m.ShowDialog()
            End If
        Catch ex As Exception
            Dim m As New WGMessaggi()
            m.Messaggio("Errore.")
            m.ShowDialog()
            ScriviLogCompleto("RispostaAggiungiPreferitoFile - Errore", ex)
        End Try
    End Sub

    Private Sub rinomina()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Impossibile rinominare. Nessun elemento selezionato.")
                messaggio.ShowDialog()
                Return
            End If
            Dim idelemento As String = elementiSelezionati(0)
            Dim etichetta As String
            If logic.IsWpdPath(idelemento) Then
                etichetta = "Scrivi il nuovo nome per l'elemento " & logic.GetWpdFileName(idelemento)
            ElseIf WGRete.EsisteDirectory(idelemento) Then
                etichetta = "Scrivi il nuovo nome per la cartella " & System.IO.Path.GetFileName(idelemento.TrimEnd("\"c))
            ElseIf WGRete.EsisteFile(idelemento) Then
                etichetta = "Scrivi il nuovo nome per il file " & System.IO.Path.GetFileNameWithoutExtension(idelemento)
            Else
                Dim messaggiononesiste As New WGMessaggi()
                messaggiononesiste.Messaggio("L'elemento selezionato non esiste più.")
                messaggiononesiste.ShowDialog()
                Return
            End If
            id = idelemento
            campi = New Campi()
            campi.Aggiungi(etichetta, "c.nome", True)
            AddHandler campi.Fine, AddressOf campo_chiuso
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("FileManager.rinomina - Errore richiesta rinomina", ex)
        End Try
    End Sub

    Private Sub campo_chiuso(sender As Object, e As EventArgs)
        Try
            Dim nuovonome As String = campi.RecuperaValore("c.nome")
            If String.IsNullOrEmpty(nuovonome) Then Return
            If Not WGCentroDiControllo.GetInstance().RinominaElemento(currentDirectory, id, nuovonome) Then
                log.ScriviLog($"FileManager.campo_chiuso - Rinomina non riuscita di '{id}' in '{nuovonome}'")
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile rinominare l'elemento.")
                msg.ShowDialog()
            ElseIf EsisteRicercaMostrata() AndAlso _ricercaMostrata IsNot Nothing Then
                ' Nei risultati di una ricerca l'elemento resta, con il nome nuovo.
                _ricercaMostrata.SostituisciPercorso(id, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(id.TrimEnd("\"c)), nuovonome))
            End If
            CaricaDirectory(currentDirectory)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.campo_chiuso - Errore rinomina", ex)
        End Try
    End Sub

    Private Sub creacartellaevent()
        Try
            campi = New Campi()
            AddHandler campi.Fine, AddressOf creacartella
            campi.Aggiungi("Scrivi il nome che vuoi dare alla nuova cartella", "c.cartellanuova", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("FileManager.creacartellaevent - Errore richiesta creazione cartella", ex)
        End Try
    End Sub

    Private Sub creacartella()
        Try
            Dim nome As String = campi.RecuperaValore("c.cartellanuova")
            

            If String.IsNullOrEmpty(nome) Then Return
            If Not WGCentroDiControllo.GetInstance().CreaCartella(currentDirectory, nome) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Impossibile creare la cartella.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto($"FileManager.creacartella - Errore creazione cartella", ex)
        Finally
            Try
                CaricaDirectory(currentDirectory)
            Catch ex As Exception
                ScriviLogCompleto("FileManager.creacartella - Errore ricarica", ex)
            End Try
        End Try
    End Sub

    Private Sub InserisciNellaClipboard(azione As String)
        Try
            EseguiConRiprovaAction(azione)
        Catch ex As Exception
            ScriviLogCompleto("InserisciNellaClipboard - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiConRiprovaAction(azione As String)
        Dim attempt As Integer = 1
        While attempt <= 5
            Try
                Dim dataObject As New System.Windows.Forms.DataObject()
                Dim fileDropList = logic.FiltraFilePerClipboard(elementi)
                If fileDropList.Count > 0 Then
                    dataObject.SetFileDropList(fileDropList)
                    If azione.ToLower() = "spostare" Then
                        Dim moveEffect As Byte() = {2, 0, 0, 0}
                        dataObject.SetData("Preferred DropEffect", New System.IO.MemoryStream(moveEffect))
                    ElseIf azione.ToLower() = "copiare" Then
                        Dim copyEffect As Byte() = {5, 0, 0, 0}
                        dataObject.SetData("Preferred DropEffect", New System.IO.MemoryStream(copyEffect))
                    End If
                    System.Windows.Forms.Clipboard.SetDataObject(dataObject, True)
                Else
                    System.Windows.Forms.Clipboard.Clear()
                End If
                Exit While
            Catch ex As Exception
                Thread.Sleep(100)
                attempt += 1
                If attempt > 5 Then ScriviLogCompleto("EseguiConRiprovaAction - Errore dopo 5 tentativi", ex)
            End Try
        End While
    End Sub

    Private Sub RipristinaListaDopoInfo(dirSalvata As String, Optional elementoSelezionatoSalvato As String = Nothing)
        Try
            If String.IsNullOrEmpty(dirSalvata) OrElse dirSalvata = "sceltadisco" Then
                sceltadrive()
            Else
                CaricaDirectory(dirSalvata)
                lista.avvia()
            End If
        Catch ex As Exception
            ScriviLogCompleto("RipristinaListaDopoInfo - Errore", ex)
            Try
                lista.avvia()
            Catch exInterno As Exception
                ScriviLogCompleto("RipristinaListaDopoInfo - Errore anche nel tentativo di fallback", exInterno)
            End Try
        End Try
    End Sub

    Private Class ContestoInformazioni
        Public Percorsi As New List(Of String)()
        Public RighePerPercorso As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
        Public ContieneAudio As Boolean = False
        Public AttesaAvviata As Boolean = False
    End Class

    Private Sub informazioni()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Per visualizzare le informazioni, devi prima selezionare un file o una cartella dalla lista.")
                msg.ShowDialog()
                Return
            End If
            Dim contesto As New ContestoInformazioni()
            For Each elemento As String In elementiSelezionati
                Try
                    If String.IsNullOrEmpty(elemento) Then Continue For
                    If contesto.Percorsi.Contains(elemento) Then Continue For
                    If logic.IsWpdPath(elemento) Then
                        contesto.Percorsi.Add(elemento)
                        contesto.RighePerPercorso(elemento) = CostruisciRigheInformazioniWpd(elemento)
                    ElseIf ElementoInformazioniValido(elemento) Then
                        contesto.Percorsi.Add(elemento)
                    End If
                Catch exElemento As Exception
                    ScriviLogCompleto($"informazioni - Errore su '{elemento}'", exElemento)
                End Try
            Next
            If contesto.Percorsi.Count = 0 Then
                Dim msg As New WGMessaggi()
                If elementiSelezionati.Count = 1 Then
                    msg.Messaggio("L'elemento selezionato non è più valido.")
                Else
                    msg.Messaggio("Gli elementi selezionati non sono più validi.")
                End If
                msg.ShowDialog()
                Return
            End If
            Dim tuttiCalcolati As Boolean = True
            For Each percorso As String In contesto.Percorsi
                If Not contesto.RighePerPercorso.ContainsKey(percorso) Then
                    tuttiCalcolati = False
                    Exit For
                End If
            Next
            If tuttiCalcolati Then
                MostraInformazioniUI(contesto)
                Return
            End If
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Calcolo informazioni in corso...")
            contesto.AttesaAvviata = True
            Dim t As New Thread(AddressOf CalcolaInformazioniThread)
            t.IsBackground = True
            t.Start(contesto)
        Catch ex As Exception
            ScriviLogCompleto("informazioni - Errore", ex)
        End Try
    End Sub

    Private Function ElementoInformazioniValido(elemento As String) As Boolean
        Try
            If WGRete.EsisteFile(elemento) OrElse WGRete.EsisteDirectory(elemento) Then Return True
            Dim rootPath As String = System.IO.Path.GetPathRoot(elemento)
            Return Not String.IsNullOrEmpty(rootPath) AndAlso elemento.Trim().Equals(rootPath, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogCompleto($"ElementoInformazioniValido - Errore su '{elemento}'", ex)
            Return False
        End Try
    End Function

    Private Function CostruisciRigheInformazioniWpd(elementoSelezionato As String) As List(Of String)
        Dim infoLinesWpd As New List(Of String)
        Try
            Dim details = logic.GetWpdDetails(elementoSelezionato)
            For Each kvp In details
                Dim chiave As String = kvp.Key
                Dim valore As String = kvp.Value
                Select Case chiave
                    Case "Name" : chiave = "Nome"
                    Case "Manufacturer" : chiave = "Produttore"
                    Case "Model" : chiave = "Modello"
                    Case "SerialNumber" : chiave = "Numero di serie"
                    Case "FirmwareVersion" : chiave = "Versione Firmware"
                    Case "PowerLevel" : chiave = "Livello Batteria"
                    Case "Size" : chiave = "Dimensione"
                    Case "Type" : chiave = "Tipo"
                    Case "CreationTime" : chiave = "Data creazione"
                    Case "LastWriteTime" : chiave = "Ultima modifica"
                End Select
                infoLinesWpd.Add(chiave & ": " & valore)
            Next
        Catch ex As Exception
            ScriviLogCompleto($"Informazioni - Impossibile recuperare informazioni WPD per '{elementoSelezionato}'", ex)
        End Try
        Return infoLinesWpd
    End Function

    Private Sub CalcolaInformazioniThread(state As Object)
        Dim contesto As ContestoInformazioni = TryCast(state, ContestoInformazioni)
        Try
            If contesto Is Nothing Then Return
            For Each percorso As String In contesto.Percorsi
                If contesto.RighePerPercorso.ContainsKey(percorso) Then Continue For
                Try
                    contesto.RighePerPercorso(percorso) = CostruisciRigheInformazioni(percorso, contesto)
                Catch exPercorso As Exception
                    ScriviLogCompleto($"CalcolaInformazioniThread - Errore su '{percorso}'", exPercorso)
                    contesto.RighePerPercorso(percorso) = New List(Of String) From {"Nome: " & System.IO.Path.GetFileName(percorso.TrimEnd("\"c)), "Informazioni non disponibili."}
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("CalcolaInformazioniThread - Errore", ex)
        Finally
            Try
                uiContext.Post(AddressOf MostraInformazioniUI, contesto)
            Catch exPost As Exception
                ScriviLogCompleto("CalcolaInformazioniThread - Errore notifica interfaccia", exPost)
            End Try
        End Try
    End Sub

    Private Function CostruisciRigheInformazioni(percorso As String, contesto As ContestoInformazioni) As List(Of String)
        Dim infoLines As New List(Of String)
        Dim details As New PathDetails(percorso)
        If details.IsDrive Then
            Dim driveName As String = details.Name
            If Not String.IsNullOrEmpty(details.VolumeLabel) Then
                driveName = details.VolumeLabel & " (" & details.Name & ")"
            End If
            infoLines.Add($"Nome: {driveName}")
            If details.IsNetworkShare OrElse details.DriveType = System.IO.DriveType.Network Then
                infoLines.Add("Tipologia: Unità di rete")
                If Not String.IsNullOrEmpty(details.NetworkPath) Then infoLines.Add($"Percorso di rete: {details.NetworkPath}")
            Else
                infoLines.Add($"Tipologia: Disco locale ({details.DriveType.ToString()})")
            End If
            If details.DriveIsReady Then
                infoLines.Add($"File system: {details.DriveFormat}")
                infoLines.Add($"Spazio totale: {details.FormattedTotalDriveSize}")
                infoLines.Add($"Spazio disponibile: {details.FormattedFreeSpace}")
            Else
                infoLines.Add("Stato: Non pronto")
            End If
            Return infoLines
        End If
        Dim size = details.SizeInBytes
        infoLines.Add($"Nome: {details.Name}")
        If Not String.IsNullOrEmpty(details.NetworkPath) Then infoLines.Add($"Percorso di rete: {details.NetworkPath}")
        infoLines.Add($"Ultima modifica: {details.LastModified}")
        infoLines.Add($"Data di creazione: {details.CreationTime}")
        infoLines.Add($"Dimensione: {details.FormattedSize}")
        If details.IsDirectory Then
            infoLines.Add($"File contenuti: {details.FileCount:N0}")
            infoLines.Add($"Sottocartelle: {details.DirectoryCount:N0}")
            infoLines.Add("Tipologia: Cartella")
        Else
            infoLines.Add($"Tipologia: File ({System.IO.Path.GetExtension(details.FullPath)})")
            Dim ext As String = System.IO.Path.GetExtension(details.FullPath).ToLower()
            If ext = ".mp3" OrElse ext = ".m4a" Then
                contesto.ContieneAudio = True
                Try
                    Dim tags = logic.GetTagManager().LeggiTag(details.FullPath)
                    If tags.Successo Then
                        If Not String.IsNullOrEmpty(tags.Titolo) Then infoLines.Add($"Titolo: {tags.Titolo}")
                        If Not String.IsNullOrEmpty(tags.Artista) Then infoLines.Add($"Artista: {tags.Artista}")
                        If Not String.IsNullOrEmpty(tags.Album) Then infoLines.Add($"Album: {tags.Album}")
                        If tags.Anno > 0 Then infoLines.Add($"Anno: {tags.Anno}")
                        If Not String.IsNullOrEmpty(tags.Genere) Then infoLines.Add($"Genere: {tags.Genere}")
                    End If
                Catch exTag As Exception
                    ScriviLogCompleto($"CostruisciRigheInformazioni - Errore lettura tag '{details.FullPath}'", exTag)
                End Try
            End If
        End If
        Return infoLines
    End Function

    Private Sub MostraInformazioniUI(state As Object)
        Try
            Dim contesto As ContestoInformazioni = TryCast(state, ContestoInformazioni)
            If contesto Is Nothing Then Return
            If contesto.AttesaAvviata AndAlso processoincorso IsNot Nothing Then processoincorso.Fine()
            Dim infoLines As New List(Of String)
            Dim totale As Integer = contesto.Percorsi.Count
            Dim indice As Integer = 0
            For Each percorso As String In contesto.Percorsi
                indice += 1
                Dim righe As List(Of String) = Nothing
                contesto.RighePerPercorso.TryGetValue(percorso, righe)
                If totale > 1 Then
                    infoLines.Add($"Elemento {indice} di {totale}")
                    If righe Is Nothing OrElse righe.Count = 0 Then
                        infoLines.Add("Informazioni non disponibili.")
                        Continue For
                    End If
                End If
                If righe IsNot Nothing Then infoLines.AddRange(righe)
            Next
            If infoLines.Count = 0 Then Return

            _dirSalvataInformazioni = currentDirectory
            _listaInformazioni = New WGMenuLista()
            _listaInformazioni.impostaTitolo("Informazioni")
            _listaInformazioni.invioNonConsentito = True
            If contesto.ContieneAudio Then
                _listaInformazioni.aggiungiOpzione("Modifica tag", AddressOf MenuModificaTagMp3)
            End If
            AddHandler _listaInformazioni.escPremuto, AddressOf OnEscListaInformazioni
            For Each line In infoLines
                _listaInformazioni.aggiungiElemento(line)
            Next
            _listaInformazioni.avvia()
        Catch ex As Exception
            ScriviLogCompleto("MostraInformazioniUI - Errore", ex)
        End Try
    End Sub

    Private Sub OnEscListaInformazioni()
        Try
            If _listaInformazioni IsNot Nothing Then
                RemoveHandler _listaInformazioni.escPremuto, AddressOf OnEscListaInformazioni
                _listaInformazioni.abbandona()
                _listaInformazioni = Nothing
            End If
            RipristinaListaDopoInfo(_dirSalvataInformazioni)
        Catch ex As Exception
            ScriviLogCompleto("OnEscListaInformazioni - Errore", ex)
        End Try
    End Sub

    Private Sub MenuModificaTagMp3()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim fileAudio As List(Of String) = FiltraFileEsistenti(elementiSelezionati, New String() {".mp3", ".m4a"})
            If fileAudio.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio(If(elementiSelezionati.Count = 1, "Seleziona un file MP3 o M4A valido.", "Nessuno dei file selezionati è un file MP3 o M4A valido."))
                msg.ShowDialog()
                Return
            End If
            _tempTagFiles = fileAudio
            Dim etichettaModifica As String = "Modifica tag"
            If fileAudio.Count = 1 Then
                Dim tags = logic.GetTagManager().LeggiTag(fileAudio(0))
                etichettaModifica = "Aggiungi tag"
                If tags.Successo AndAlso (Not String.IsNullOrEmpty(tags.Titolo) OrElse Not String.IsNullOrEmpty(tags.Artista)) Then
                    etichettaModifica = "Modifica tag"
                End If
            End If
            Dim menu As New WGMenu()
            menu.aggiungi(etichettaModifica, AddressOf AvviaModificaManualeTag)
            menu.aggiungi("Recupera da internet", AddressOf AvviaRecuperoOnlineTag)
            menu.avvia("Modifica tag", True)
        Catch ex As Exception
            ScriviLogCompleto("MenuModificaTagMp3 - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaModificaManualeTag()
        Try
            If _tempTagFiles Is Nothing OrElse _tempTagFiles.Count = 0 Then Return
            campi = New Campi()
            If _tempTagFiles.Count = 1 Then
                _tempTagFile = _tempTagFiles(0)
                Dim tags = logic.GetTagManager().LeggiTag(_tempTagFile)
                _tempTagData = New CLSTagMp3.DatiTag()
                _tempTagData.Titolo = tags.Titolo
                _tempTagData.Artista = tags.Artista
                _tempTagData.Album = tags.Album
                _tempTagData.Anno = tags.Anno
                _tempTagData.Genere = tags.Genere
                AddHandler campi.Fine, AddressOf OnTagInseriti
                campi.Aggiungi("Scrivi il titolo", "c.titolo", True, , tags.Titolo)
                campi.Aggiungi("Scrivi l'artista", "c.artista", False, , tags.Artista)
                campi.Aggiungi("Scrivi l'album", "c.album", False, , tags.Album)
                campi.Aggiungi("Scrivi l'anno", "c.anno", False, , tags.Anno.ToString())
                campi.Aggiungi("Scrivi il genere", "c.genere", False, , tags.Genere)
            Else
                AddHandler campi.Fine, AddressOf OnTagInseritiMultipli
                campi.Aggiungi("Scrivi il titolo per tutti i " & _tempTagFiles.Count.ToString() & " file, lascia vuoto per non modificarlo", "c.titolo", False)
                campi.Aggiungi("Scrivi l'artista per tutti i file, lascia vuoto per non modificarlo", "c.artista", False)
                campi.Aggiungi("Scrivi l'album per tutti i file, lascia vuoto per non modificarlo", "c.album", False)
                campi.Aggiungi("Scrivi l'anno per tutti i file, lascia vuoto per non modificarlo", "c.anno", False)
                campi.Aggiungi("Scrivi il genere per tutti i file, lascia vuoto per non modificarlo", "c.genere", False)
            End If
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("AvviaModificaManualeTag - Errore", ex)
        End Try
    End Sub

    Private Sub OnTagInseriti(sender As Object, e As EventArgs)
        Try
            Dim titolo = campi.RecuperaValore("c.titolo")
            Dim artista = campi.RecuperaValore("c.artista")
            Dim album = campi.RecuperaValore("c.album")
            Dim annoStr = campi.RecuperaValore("c.anno")
            Dim genere = campi.RecuperaValore("c.genere")
            
            If Not String.IsNullOrEmpty(titolo) Then _tempTagData.Titolo = titolo
            If Not String.IsNullOrEmpty(artista) Then _tempTagData.Artista = artista
            If Not String.IsNullOrEmpty(album) Then _tempTagData.Album = album
            Dim anno As UInteger
            If UInteger.TryParse(annoStr, anno) Then _tempTagData.Anno = anno
            If Not String.IsNullOrEmpty(genere) Then _tempTagData.Genere = genere
            If logic.GetTagManager().ScriviTag(_tempTagFile, _tempTagData) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Tag aggiornati.")
                msg.ShowDialog()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Errore durante l'aggiornamento dei tag.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnTagInseriti - Errore", ex)
        End Try
    End Sub

    Private Sub OnTagInseritiMultipli(sender As Object, e As EventArgs)
        Try
            Dim titolo As String = campi.RecuperaValore("c.titolo")
            Dim artista As String = campi.RecuperaValore("c.artista")
            Dim album As String = campi.RecuperaValore("c.album")
            Dim annoStr As String = campi.RecuperaValore("c.anno")
            Dim genere As String = campi.RecuperaValore("c.genere")
            Dim anno As UInteger
            Dim annoValido As Boolean = UInteger.TryParse(annoStr, anno)
            If String.IsNullOrEmpty(titolo) AndAlso String.IsNullOrEmpty(artista) AndAlso String.IsNullOrEmpty(album) AndAlso Not annoValido AndAlso String.IsNullOrEmpty(genere) Then Return
            Dim aggiornati As Integer = 0
            Dim falliti As Integer = 0
            For Each fileAudio As String In _tempTagFiles
                Try
                    Dim attuali = logic.GetTagManager().LeggiTag(fileAudio)
                    Dim dati As New CLSTagMp3.DatiTag()
                    dati.Titolo = attuali.Titolo
                    dati.Artista = attuali.Artista
                    dati.Album = attuali.Album
                    dati.Anno = attuali.Anno
                    dati.Genere = attuali.Genere
                    If Not String.IsNullOrEmpty(titolo) Then dati.Titolo = titolo
                    If Not String.IsNullOrEmpty(artista) Then dati.Artista = artista
                    If Not String.IsNullOrEmpty(album) Then dati.Album = album
                    If annoValido Then dati.Anno = anno
                    If Not String.IsNullOrEmpty(genere) Then dati.Genere = genere
                    If logic.GetTagManager().ScriviTag(fileAudio, dati) Then
                        aggiornati += 1
                    Else
                        falliti += 1
                        log.ScriviLog($"OnTagInseritiMultipli - Scrittura tag non riuscita per '{fileAudio}'")
                    End If
                Catch exFile As Exception
                    falliti += 1
                    ScriviLogCompleto($"OnTagInseritiMultipli - Errore su '{fileAudio}'", exFile)
                End Try
            Next
            Dim msg As New WGMessaggi()
            If falliti = 0 Then
                msg.Messaggio("Tag aggiornati in " & aggiornati.ToString() & " file.")
            Else
                msg.Messaggio("Tag aggiornati in " & aggiornati.ToString() & " file su " & _tempTagFiles.Count.ToString() & ". Per gli altri si è verificato un errore.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("OnTagInseritiMultipli - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaRecuperoOnlineTag()
        Try
            If _tempTagFiles Is Nothing OrElse _tempTagFiles.Count = 0 Then Return
            If _tempTagFiles.Count > 1 Then
                RecuperaTagOnlineMultipli(New List(Of String)(_tempTagFiles))
                Return
            End If
            _tempSearchTagFile = _tempTagFiles(0)
            Dim nomeFileDefault As String = System.IO.Path.GetFileNameWithoutExtension(_tempSearchTagFile)
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnTagOnlineQueryInserita
            campi.Aggiungi("Cerca brano online (Titolo/Artista):", "c.ricerca", True, False, nomeFileDefault)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("AvviaRecuperoOnlineTag - Errore", ex)
        End Try
    End Sub

    Private Async Sub RecuperaTagOnlineMultipli(fileAudio As List(Of String))
        Dim attesa As WGProcessoInCorso = Nothing
        Try
            attesa = New WGProcessoInCorso()
            attesa.Avvia("Recupero dei tag da internet per " & fileAudio.Count.ToString() & " file in corso...")
            Dim aggiornati As Integer = 0
            Dim nonTrovati As Integer = 0
            Dim falliti As Integer = 0
            For Each percorso As String In fileAudio
                Try
                    Dim query As String = System.IO.Path.GetFileNameWithoutExtension(percorso)
                    Dim risultatiOnline As List(Of CLSTagMp3.DatiTag) = Await logic.GetTagManager().CercaSuiTunes(query)
                    If risultatiOnline Is Nothing OrElse risultatiOnline.Count = 0 OrElse Not risultatiOnline(0).Successo Then
                        nonTrovati += 1
                    ElseIf logic.GetTagManager().ScriviTag(percorso, risultatiOnline(0)) Then
                        aggiornati += 1
                    Else
                        falliti += 1
                        log.ScriviLog($"RecuperaTagOnlineMultipli - Scrittura tag non riuscita per '{percorso}'")
                    End If
                Catch exFile As Exception
                    falliti += 1
                    ScriviLogCompleto($"RecuperaTagOnlineMultipli - Errore su '{percorso}'", exFile)
                End Try
            Next
            attesa.Fine()
            attesa = Nothing
            Dim parti As New List(Of String)()
            parti.Add("Tag aggiornati da iTunes: " & aggiornati.ToString() & " file su " & fileAudio.Count.ToString() & ".")
            If nonTrovati > 0 Then parti.Add("Nessuna informazione trovata per " & nonTrovati.ToString() & " file.")
            If falliti > 0 Then parti.Add("Errore di scrittura su " & falliti.ToString() & " file.")
            Dim msg As New WGMessaggi()
            msg.Messaggio(String.Join(" ", parti))
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("RecuperaTagOnlineMultipli - Errore", ex)
            Try
                If attesa IsNot Nothing Then attesa.Fine()
            Catch exFine As Exception
                ScriviLogCompleto("RecuperaTagOnlineMultipli - Errore chiusura attesa", exFine)
            End Try
        End Try
    End Sub

    Private Async Sub OnTagOnlineQueryInserita(sender As Object, e As EventArgs)
        Try
            Dim query As String = campi.RecuperaValore("c.ricerca")
            
            If String.IsNullOrWhiteSpace(query) Then Return

            Dim risultatiOnline As List(Of CLSTagMp3.DatiTag) = Await logic.GetTagManager().CercaSuiTunes(query)

            If risultatiOnline Is Nothing OrElse risultatiOnline.Count = 0 Then
                Dim msgNessunRisultato As New WGMessaggi()
                msgNessunRisultato.Messaggio("Nessuna informazione trovata online.")
                msgNessunRisultato.ShowDialog()
                Return
            End If

            Dim tagScelto As CLSTagMp3.DatiTag = risultatiOnline(0)

            If Not tagScelto.Successo Then
                Dim msgErroreRisultato As New WGMessaggi()
                msgErroreRisultato.Messaggio("Nessuna informazione trovata online.")
                msgErroreRisultato.ShowDialog()
                Return
            End If

            If logic.GetTagManager().ScriviTag(_tempSearchTagFile, tagScelto) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Tag recuperati da iTunes e aggiornati.")
                msg.ShowDialog()
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Errore durante la scrittura dei tag.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnTagOnlineQueryInserita - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaCompressione()
        Try
            Dim elementiSelezionati = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun elemento selezionato da comprimere.")
                msg.ShowDialog()
                Return
            End If
            _tempZipItems = elementiSelezionati
            Dim menuFormato As New WGMenu()
            menuFormato.aggiungi("Formato .zip", AddressOf AvviaCompressioneZipAction)
            menuFormato.aggiungi("Formato .tar.gz", AddressOf AvviaCompressioneTarGzAction)
            menuFormato.avvia("Scegli il formato di compressione", True)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AvviaCompressione - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaCompressioneZipAction()
        Try
            AvviaCompressioneZip(_tempZipItems)
        Catch ex As Exception
            ScriviLogCompleto("AvviaCompressioneZipAction - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaCompressioneTarGzAction()
        Try
            AvviaCompressioneTarGz(_tempZipItems)
        Catch ex As Exception
            ScriviLogCompleto("AvviaCompressioneTarGzAction - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaCompressioneZip(elementiDaComprimere As List(Of String))
        Try
            campi = New Campi()
            AddHandler campi.Fine, AddressOf OnNomeZipInserito
            campi.Aggiungi("Nome del file ZIP:", "c.nome", True)
            campi.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AvviaCompressioneZip - Errore", ex)
        End Try
    End Sub

    Private _tempZipName As String

    Private Sub OnNomeZipInserito(sender As Object, e As EventArgs)
        Try
            If logic.IsRootPath(currentDirectory) AndAlso Not logic.IsUserAdministrator() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Per creare un archivio nella directory principale sono necessari i diritti di amministratore.")
                msg.ShowDialog()
                
                Return
            End If
            _tempZipName = campi.RecuperaValore("c.nome")
            If String.IsNullOrWhiteSpace(_tempZipName) Then
                
                Return
            End If
            If Not _tempZipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then
                _tempZipName &= ".zip"
            End If
            Dim domandaPassword As New WGDomanda()
            AddHandler domandaPassword.rispostadomanda, AddressOf OnDomandaPasswordZip
            domandaPassword.avvia("Vuoi proteggere l'archivio con una password?")
        Catch ex As Exception
            ScriviLogCompleto("OnNomeZipInserito - Errore", ex)
        End Try
    End Sub

    Private Sub OnDomandaPasswordZip(risposta As DialogResult)
        Try
            If risposta = DialogResult.Yes Then
                campi = New Campi()
                AddHandler campi.Fine, AddressOf OnPasswordZipInserita
                campi.AggiungiPassword("Password per l'archivio " & _tempZipName, "archivio_password", True)
                campi.Avvia()
            Else
                Dim dest As String = System.IO.Path.Combine(currentDirectory, _tempZipName)
                WGCentroDiControllo.GetInstance().Comprimi(_tempZipItems, dest, "")
            End If
        Catch ex As Exception
            ScriviLogCompleto("OnDomandaPasswordZip - Errore", ex)
        End Try
    End Sub

    Private Sub OnPasswordZipInserita()
        Try
            Dim password As String = campi.RecuperaValore("archivio_password")
            
            If String.IsNullOrWhiteSpace(password) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Password non valida. Compressione annullata.")
                msg.ShowDialog()
                Return
            End If
            Dim dest As String = System.IO.Path.Combine(currentDirectory, _tempZipName)
            WGCentroDiControllo.GetInstance().Comprimi(_tempZipItems, dest, password)
        Catch ex As Exception
            ScriviLogCompleto("OnPasswordZipInserita - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaCompressioneTarGz(elementiDaComprimere As List(Of String))
        Try
            If logic.IsRootPath(currentDirectory) AndAlso Not logic.IsUserAdministrator() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Per creare un archivio nella directory principale sono necessari i diritti di amministratore.")
                msg.ShowDialog()
                Return
            End If
            If elementiDaComprimere.Count > 1 OrElse (elementiDaComprimere.Count = 1 AndAlso WGRete.EsisteFile(elementiDaComprimere(0))) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("La compressione in formato .tar.gz è supportata solo per una singola cartella.")
                msg.ShowDialog()
                Return
            End If
            Dim defaultName As String = System.IO.Path.GetFileName(elementiDaComprimere(0))
            Dim nomeArchivio As String = defaultName & ".tar.gz"
            Dim dest As String = System.IO.Path.Combine(currentDirectory, nomeArchivio)
            WGCentroDiControllo.GetInstance().Comprimi(elementiDaComprimere, dest, "")
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AvviaCompressioneTarGz - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaDecompressioneZip(percorsoArchivio As String)
        Try
            Dim dest As String = System.IO.Path.GetDirectoryName(percorsoArchivio)
            WGCentroDiControllo.GetInstance().Decomprimi(percorsoArchivio, dest)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AvviaDecompressioneZip - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaDecompressioneSharpZipLib(percorsoArchivio As String)
        Try
            Dim dest As String = System.IO.Path.GetDirectoryName(percorsoArchivio)
            WGCentroDiControllo.GetInstance().Decomprimi(percorsoArchivio, dest)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AvviaDecompressioneSharpZipLib - Errore", ex)
        End Try
    End Sub

    Private Sub Lista_VoceCambiata(sender As Object, e As VoceCambiataEventArgs)
        Try
            If isExtensionSelectionMode Then
                Return
            End If
            If isReteMenuMode OrElse isReteCondivisioniMode Then
                Return
            End If
            If isDriveSelectionMode Then
                AggiornaOpzioniDrive(e.IdVoce)
            ElseIf isFtpElencoServerMode Then
                AggiornaOpzioniContestualiServerFtp(e.IdVoce)
            Else
                AggiornaOpzioniContestuali(e.IdVoce)
            End If
        Catch ex As Exception
            ScriviLogCompleto("Errore in Lista_VoceCambiata", ex)
        End Try
    End Sub

    Private Sub AggiornaOpzioneDisconnettiRete(percorso As String)
        Try
            Dim isUnitaRete As Boolean = Not logic.IsWpdPath(percorso) AndAlso percorso.Length = 3 AndAlso percorso.EndsWith(":\") AndAlso WGRete.IsRemoto(percorso)
            If isUnitaRete Then
                If Not lista.opzioneEsiste("Disconnetti unità di rete") Then
                    lista.aggiungiOpzione("Disconnetti unità di rete", AddressOf DisconnettiUnitaSelezionata)
                End If
            ElseIf lista.opzioneEsiste("Disconnetti unità di rete") Then
                lista.rimuoviOpzione("Disconnetti unità di rete")
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AggiornaOpzioneDisconnettiRete - Errore", ex)
        End Try
    End Sub

    Private Sub AggiornaOpzioniDrive(percorso As String)
        Try
            If String.IsNullOrEmpty(percorso) Then Return
            If percorso = ID_RETE_ROOT Then
                If lista.opzioneEsiste("Pulizia Computer") Then lista.rimuoviOpzione("Pulizia Computer")
                If lista.opzioneEsiste("Copia Unità") Then lista.rimuoviOpzione("Copia Unità")
                If lista.opzioneEsiste("Rimuovi disco") Then lista.rimuoviOpzione("Rimuovi disco")
                If lista.opzioneEsiste("Disconnetti unità di rete") Then lista.rimuoviOpzione("Disconnetti unità di rete")
                Return
            End If
            AggiornaOpzioneDisconnettiRete(percorso)
            If logic.IsWpdPath(percorso) Then
                If lista.opzioneEsiste("Pulizia Computer") Then
                    lista.rimuoviOpzione("Pulizia Computer")
                End If
                If lista.opzioneEsiste("Copia Unità") Then
                    lista.rimuoviOpzione("Copia Unità")
                End If
                If lista.opzioneEsiste("Rimuovi disco") Then
                    lista.rimuoviOpzione("Rimuovi disco")
                End If
                Return
            End If
            If logic.IsSystemDrive(percorso) Then
                If Not lista.opzioneEsiste("Pulizia Computer") Then
                    lista.aggiungiOpzione("Pulizia Computer", AddressOf MostraMenuPulizia)
                End If
                If lista.opzioneEsiste("Copia Unità") Then
                    lista.rimuoviOpzione("Copia Unità")
                End If
                If lista.opzioneEsiste("Rimuovi disco") Then
                    lista.rimuoviOpzione("Rimuovi disco")
                End If
            Else
                If Not lista.opzioneEsiste("Copia Unità") Then
                    lista.aggiungiOpzione("Copia Unità", AddressOf CopiaUnitaEsterna)
                End If
                If lista.opzioneEsiste("Pulizia Computer") Then
                    lista.rimuoviOpzione("Pulizia Computer")
                End If
                If logic.IsExternalDisk(percorso) Then
                    If Not lista.opzioneEsiste("Rimuovi disco") Then
                        lista.aggiungiOpzione("Rimuovi disco", AddressOf rimuovidisco)
                    End If
                Else
                    If lista.opzioneEsiste("Rimuovi disco") Then
                        lista.rimuoviOpzione("Rimuovi disco")
                    End If
                End If
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AggiornaOpzioniDrive - Errore", ex)
        End Try
    End Sub

    Private Sub AggiornaOpzioniContestuali(percorso As String)
        Try
            If String.IsNullOrEmpty(percorso) Then Return
            If percorso.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0 Then
                If Not logic.IsWpdPath(percorso) Then Return
            End If
            If logic.IsWpdPath(percorso) Then
                If lista.opzioneEsiste("Traduci") Then lista.rimuoviOpzione("Traduci")
                If lista.opzioneEsiste("Stampa") Then lista.rimuoviOpzione("Stampa")
                If lista.opzioneEsiste("Converti in mp3") Then lista.rimuoviOpzione("Converti in mp3")
                If lista.opzioneEsiste("Riassumi con ai") Then lista.rimuoviOpzione("Riassumi con ai")
                If lista.opzioneEsiste("Riscrivi con ai") Then lista.rimuoviOpzione("Riscrivi con ai")
                If lista.opzioneEsiste("Descrivi immagine con intelligenza artificiale") Then lista.rimuoviOpzione("Descrivi immagine con intelligenza artificiale")
                If lista.opzioneEsiste("Trascrivi audio") Then lista.rimuoviOpzione("Trascrivi audio")
                If logic.IsWpdDirectory(percorso) Then
                    lista.sostituisciOpzione("Decomprimi", "Comprimi...", AddressOf AvviaCompressione)
                    lista.sostituisciOpzione("Riproduci", "Apri", AddressOf ApriElementoSelezionato)
                    lista.sostituisciOpzione("Apri nell'editor", "Apri", AddressOf ApriElementoSelezionato)
                Else
                    If IsMediaFile(percorso) Then
                        lista.sostituisciOpzione("Apri", "Riproduci", AddressOf RiproduciElementoSelezionato)
                    Else
                        lista.sostituisciOpzione("Riproduci", "Apri", AddressOf ApriElementoSelezionato)
                    End If
                End If
                If lista.opzioneEsiste("Cripta") Then lista.rimuoviOpzione("Cripta")
                If lista.opzioneEsiste("Decripta") Then lista.rimuoviOpzione("Decripta")
                Return
            End If
            If Not (WGRete.EsisteFile(percorso) OrElse WGRete.EsisteDirectory(percorso)) Then
                lista.sostituisciOpzione("Decomprimi", "Comprimi...", AddressOf AvviaCompressione)
                lista.sostituisciOpzione("Riproduci", "Apri", AddressOf ApriElementoSelezionato)
                lista.sostituisciOpzione("Apri nell'editor", "Apri", AddressOf ApriElementoSelezionato)
                lista.sostituisciOpzione("Rimuovi dai Preferiti", "Aggiungi ai Preferiti", AddressOf AggiungiRimuoviPreferiti)
                If lista.opzioneEsiste("Traduci") Then lista.rimuoviOpzione("Traduci")
                If lista.opzioneEsiste("Stampa") Then lista.rimuoviOpzione("Stampa")
                If lista.opzioneEsiste("Converti in mp3") Then lista.rimuoviOpzione("Converti in mp3")
                If lista.opzioneEsiste("Riassumi con ai") Then lista.rimuoviOpzione("Riassumi con ai")
                If lista.opzioneEsiste("Riscrivi con ai") Then lista.rimuoviOpzione("Riscrivi con ai")
                If lista.opzioneEsiste("Descrivi immagine con intelligenza artificiale") Then lista.rimuoviOpzione("Descrivi immagine con intelligenza artificiale")
                If lista.opzioneEsiste("Trascrivi audio") Then lista.rimuoviOpzione("Trascrivi audio")
                Return
            End If
            Dim isFileVal As Boolean = WGRete.EsisteFile(percorso)
            If Not lista.opzioneEsiste("Aggiungi alla scrivania") Then lista.aggiungiOpzione("Aggiungi alla scrivania", AddressOf AggiungiSelezionatoAllaScrivania)
            If Not lista.opzioneEsiste("Copia sulla scrivania") Then lista.aggiungiOpzione("Copia sulla scrivania", AddressOf CopiaSelezionatiSullaScrivania)
            Dim isEncrypted As Boolean = False
            Try
                Dim attributes = System.IO.File.GetAttributes(percorso)
                isEncrypted = (attributes And FileAttributes.Encrypted) = FileAttributes.Encrypted
            Catch ex As Exception
                ScriviLogCompleto("AggiornaOpzioniContestuali - Errore lettura attributi " & percorso, ex)
            End Try
            If isEncrypted Then
                If Not lista.opzioneEsiste("Decripta") Then lista.aggiungiOpzione("Decripta", AddressOf DecriptaElementoSelezionato)
                If lista.opzioneEsiste("Cripta") Then lista.rimuoviOpzione("Cripta")
            Else
                If Not lista.opzioneEsiste("Cripta") Then lista.aggiungiOpzione("Cripta", AddressOf CriptaElementoSelezionato)
                If lista.opzioneEsiste("Decripta") Then lista.rimuoviOpzione("Decripta")
            End If
            Dim estensione As String = If(isFileVal, System.IO.Path.GetExtension(percorso).ToLower(), "")
            If isFileVal AndAlso logic.estensioniArchivio.Contains(estensione) Then
                lista.sostituisciOpzione("Comprimi...", "Decomprimi", AddressOf DecomprimiElementoSelezionato)
            Else
                lista.sostituisciOpzione("Decomprimi", "Comprimi...", AddressOf AvviaCompressione)
            End If
            lista.sostituisciOpzione("Riproduci", "Apri", AddressOf ApriElementoSelezionato)
            lista.sostituisciOpzione("Apri nell'editor", "Apri", AddressOf ApriElementoSelezionato)
            If isFileVal Then
                If logic.estensioniAudioVideo.Contains(estensione) Then
                    lista.sostituisciOpzione("Apri", "Riproduci", AddressOf RiproduciElementoSelezionato)
                ElseIf logic.estensioniTesto.Contains(estensione) Then
                    lista.sostituisciOpzione("Apri", "Apri nell'editor", AddressOf ApriInEditor)
                End If
                If logic.estensioniTesto.Contains(estensione) OrElse String.Equals(estensione, ".pdf", StringComparison.OrdinalIgnoreCase) OrElse Global.WGStampante.WGStampante.EImmagineStampabile(estensione) Then
                    If Not lista.opzioneEsiste("Stampa") Then
                        lista.aggiungiOpzione("Stampa", AddressOf StampaFileSelezionato)
                    End If
                Else
                    If lista.opzioneEsiste("Stampa") Then lista.rimuoviOpzione("Stampa")
                End If
                If WGTraduttore.FileTraducibile(percorso) Then
                    If Not lista.opzioneEsiste("Traduci") Then lista.aggiungiOpzione("Traduci", AddressOf TraduciFileSelezionato)
                Else
                    If lista.opzioneEsiste("Traduci") Then lista.rimuoviOpzione("Traduci")
                End If
                If logic.estensioniTesto.Contains(estensione) Then
                    If Not lista.opzioneEsiste("Converti in mp3") Then
                        lista.aggiungiOpzione("Converti in mp3", AddressOf ConvertiFileSelezionatoInMp3)
                    End If
                    If Not lista.opzioneEsiste("Riassumi con ai") Then
                        lista.aggiungiOpzione("Riassumi con ai", AddressOf RiassumiFileSelezionatoConAi)
                    End If
                Else
                    If lista.opzioneEsiste("Converti in mp3") Then lista.rimuoviOpzione("Converti in mp3")
                    If lista.opzioneEsiste("Riassumi con ai") Then lista.rimuoviOpzione("Riassumi con ai")
                    If lista.opzioneEsiste("Riscrivi con ai") Then lista.rimuoviOpzione("Riscrivi con ai")
                End If
                If WGAi.EFileRiscrivibile(percorso) Then
                    If Not lista.opzioneEsiste("Riscrivi con ai") Then lista.aggiungiOpzione("Riscrivi con ai", AddressOf RiscriviFileSelezionatiConAi)
                Else
                    If lista.opzioneEsiste("Riscrivi con ai") Then lista.rimuoviOpzione("Riscrivi con ai")
                End If
                If EstensioniImmagineAi.Contains(estensione) Then
                    If Not lista.opzioneEsiste("Descrivi immagine con intelligenza artificiale") Then
                        lista.aggiungiOpzione("Descrivi immagine con intelligenza artificiale", AddressOf DescriviImmagineSelezionataConAi)
                    End If
                Else
                    If lista.opzioneEsiste("Descrivi immagine con intelligenza artificiale") Then
                        lista.rimuoviOpzione("Descrivi immagine con intelligenza artificiale")
                    End If
                End If
                If EstensioniAudioTrascrivibili.Contains(estensione) Then
                    If Not lista.opzioneEsiste("Trascrivi audio") Then
                        lista.aggiungiOpzione("Trascrivi audio", AddressOf TrascriviAudioSelezionato)
                    End If
                Else
                    If lista.opzioneEsiste("Trascrivi audio") Then lista.rimuoviOpzione("Trascrivi audio")
                End If
                If estensione = ".mp3" OrElse estensione = ".m4a" Then
                    If Not lista.opzioneEsiste("Modifica tag") Then
                        lista.aggiungiOpzione("Modifica tag", AddressOf MenuModificaTagMp3)
                    End If
                Else
                    If lista.opzioneEsiste("Modifica tag") Then
                        lista.rimuoviOpzione("Modifica tag")
                    End If
                End If
            End If
            If Not isFileVal Then
                If lista.opzioneEsiste("Traduci") Then lista.rimuoviOpzione("Traduci")
                If lista.opzioneEsiste("Stampa") Then lista.rimuoviOpzione("Stampa")
                If lista.opzioneEsiste("Converti in mp3") Then lista.rimuoviOpzione("Converti in mp3")
                If lista.opzioneEsiste("Riassumi con ai") Then lista.rimuoviOpzione("Riassumi con ai")
                If lista.opzioneEsiste("Riscrivi con ai") Then lista.rimuoviOpzione("Riscrivi con ai")
                If lista.opzioneEsiste("Descrivi immagine con intelligenza artificiale") Then lista.rimuoviOpzione("Descrivi immagine con intelligenza artificiale")
                If lista.opzioneEsiste("Trascrivi audio") Then lista.rimuoviOpzione("Trascrivi audio")
            End If
            If favoritesManager.IsPreferito(percorso) Then
                lista.sostituisciOpzione("Aggiungi ai Preferiti", "Rimuovi dai Preferiti", AddressOf AggiungiRimuoviPreferiti)
            Else
                lista.sostituisciOpzione("Rimuovi dai Preferiti", "Aggiungi ai Preferiti", AddressOf AggiungiRimuoviPreferiti)
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.AggiornaOpzioniContestuali - Errore", ex)
        End Try
    End Sub

    Private Class ContestoCriptazione
        Public Percorsi As New List(Of String)()
        Public Cripta As Boolean = True
        Public Riusciti As Integer = 0
        Public Falliti As Integer = 0
        Public UltimoErrore As String = ""
    End Class

    Private Sub CriptaElementoSelezionato()
        Try
            AvviaCriptazioneSelezionati(True)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.CriptaElementoSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub DecriptaElementoSelezionato()
        Try
            AvviaCriptazioneSelezionati(False)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.DecriptaElementoSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaCriptazioneSelezionati(cripta As Boolean)
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim percorsiValidi As New List(Of String)()
            Dim portatili As Integer = 0
            Dim inRete As Integer = 0
            For Each percorso As String In elementiSelezionati
                If String.IsNullOrEmpty(percorso) Then Continue For
                If logic.IsWpdPath(percorso) Then
                    portatili += 1
                ElseIf WGRete.IsRemoto(percorso) Then
                    inRete += 1
                Else
                    percorsiValidi.Add(percorso)
                End If
            Next
            If inRete > 0 Then
                Dim msgRete As New WGMessaggi()
                msgRete.Messaggio("La crittografia dei file non è disponibile sulle unità di rete. Gli elementi su rete sono stati ignorati.")
                msgRete.ShowDialog()
            End If
            If percorsiValidi.Count = 0 Then
                If portatili > 0 AndAlso cripta Then
                    Dim msg As New WGMessaggi()
                    msg.Messaggio("Impossibile criptare file su dispositivi portatili.")
                    msg.ShowDialog()
                End If
                Return
            End If
            Dim contesto As New ContestoCriptazione()
            contesto.Percorsi.AddRange(percorsiValidi)
            contesto.Cripta = cripta
            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia(If(cripta, "Criptazione in corso...", "Decriptazione in corso..."))
            Dim t As New Thread(AddressOf EseguiCriptazioneThread)
            t.IsBackground = True
            t.Start(contesto)
        Catch ex As Exception
            ScriviLogCompleto("AvviaCriptazioneSelezionati - Errore", ex)
            Try
                If processoincorso IsNot Nothing Then processoincorso.Fine()
            Catch exFine As Exception
                ScriviLogCompleto("AvviaCriptazioneSelezionati - Errore chiusura attesa", exFine)
            End Try
        End Try
    End Sub

    Private Sub EseguiCriptazioneThread(state As Object)
        Dim contesto As ContestoCriptazione = TryCast(state, ContestoCriptazione)
        Try
            If contesto Is Nothing Then Return
            For Each percorso As String In contesto.Percorsi
                Try
                    If contesto.Cripta Then
                        logic.CriptaPercorsoAsync(percorso).GetAwaiter().GetResult()
                    Else
                        logic.DecriptaPercorsoAsync(percorso).GetAwaiter().GetResult()
                    End If
                    contesto.Riusciti += 1
                Catch exPercorso As Exception
                    contesto.Falliti += 1
                    contesto.UltimoErrore = exPercorso.Message
                    ScriviLogCompleto($"EseguiCriptazioneThread - Errore su '{percorso}'", exPercorso)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("EseguiCriptazioneThread - Errore", ex)
        Finally
            Try
                uiContext.Post(AddressOf OnCriptazioneTerminata, contesto)
            Catch exPost As Exception
                ScriviLogCompleto("EseguiCriptazioneThread - Errore notifica interfaccia", exPost)
            End Try
        End Try
    End Sub

    Private Sub OnCriptazioneTerminata(state As Object)
        Try
            If processoincorso IsNot Nothing Then processoincorso.Fine()
            Dim contesto As ContestoCriptazione = TryCast(state, ContestoCriptazione)
            If contesto Is Nothing Then Return
            If contesto.Percorsi.Count > 0 Then AggiornaOpzioniContestuali(contesto.Percorsi(0))
            Dim verboSingolare As String = If(contesto.Cripta, "criptato", "decriptato")
            Dim verboPlurale As String = If(contesto.Cripta, "criptati", "decriptati")
            Dim nomeOperazione As String = If(contesto.Cripta, "la criptazione", "la decriptazione")
            Dim testo As String
            If contesto.Percorsi.Count = 1 Then
                If contesto.Falliti = 0 Then
                    testo = "Elemento " & verboSingolare & "."
                Else
                    testo = "Errore durante " & nomeOperazione & ": " & contesto.UltimoErrore
                End If
            ElseIf contesto.Falliti = 0 Then
                testo = contesto.Riusciti.ToString() & " elementi " & verboPlurale & "."
            Else
                testo = "Elementi " & verboPlurale & ": " & contesto.Riusciti.ToString() & " su " & contesto.Percorsi.Count.ToString() & ". Per gli altri si è verificato un errore durante " & nomeOperazione & "."
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("OnCriptazioneTerminata - Errore", ex)
        End Try
    End Sub

    Private Sub ApriElementoSelezionato()
        Try
            Dim selezionati As List(Of String) = lista.ottieniElementi()
            If selezionati Is Nothing OrElse selezionati.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Non è stato selezionato nessun'elemento.")
                messaggio.ShowDialog()
                Return
            End If
            Dim percorso As String = selezionati(0)
            recentsManager.AggiungiARecenti(percorso)
            CartellaSuccessiva(percorso, True)
        Catch ex As Exception
            ScriviLogCompleto("FileManager.ApriElementoSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub RiproduciElementoSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            If elementiSelezionati.Count = 1 Then
                ApriConScelta(elementiSelezionati(0))
            Else
                ricaricadirectory = currentDirectory
                ApriBrani(New List(Of String)(elementiSelezionati), True)
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.RiproduciElementoSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub ApriInEditor()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            ApriConScelta(elementiSelezionati(0))
        Catch ex As Exception
            ScriviLogCompleto("FileManager.ApriInEditor - Errore", ex)
        End Try
    End Sub

    Private Function FiltraFileTesto(percorsi As List(Of String)) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If percorsi Is Nothing Then Return risultato
            For Each percorso As String In percorsi
                Try
                    If String.IsNullOrEmpty(percorso) Then Continue For
                    If logic.IsWpdPath(percorso) Then Continue For
                    If Not WGRete.EsisteFile(percorso) Then Continue For
                    If logic.estensioniTesto.Contains(Path.GetExtension(percorso).ToLower()) Then risultato.Add(percorso)
                Catch exFile As Exception
                    ScriviLogCompleto($"FiltraFileTesto - Errore su '{percorso}'", exFile)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("FiltraFileTesto - Errore", ex)
        End Try
        Return risultato
    End Function

    Private Function FiltraFileStampabili(percorsi As List(Of String)) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If percorsi Is Nothing Then Return risultato
            For Each percorso As String In percorsi
                Try
                    If String.IsNullOrEmpty(percorso) Then Continue For
                    If logic.IsWpdPath(percorso) Then Continue For
                    If Not WGRete.EsisteFile(percorso) Then Continue For
                    Dim estensione As String = Path.GetExtension(percorso).ToLower()
                    If EFilePdf(percorso) OrElse logic.estensioniTesto.Contains(estensione) OrElse Global.WGStampante.WGStampante.EImmagineStampabile(percorso) Then risultato.Add(percorso)
                Catch exFile As Exception
                    ScriviLogCompleto($"FiltraFileStampabili - Errore su '{percorso}'", exFile)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("FiltraFileStampabili - Errore", ex)
        End Try
        Return risultato
    End Function

    Private Function EFilePdf(percorso As String) As Boolean
        Try
            If String.IsNullOrEmpty(percorso) Then Return False
            Return String.Equals(Path.GetExtension(percorso), ".pdf", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogCompleto($"EFilePdf - Errore su '{percorso}'", ex)
            Return False
        End Try
    End Function

    Private Function FiltraFileEsistenti(percorsi As List(Of String), Optional estensioniAmmesse As IEnumerable(Of String) = Nothing) As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If percorsi Is Nothing Then Return risultato
            For Each percorso As String In percorsi
                Try
                    If String.IsNullOrEmpty(percorso) Then Continue For
                    If logic.IsWpdPath(percorso) Then Continue For
                    If Not WGRete.EsisteFile(percorso) Then Continue For
                    If estensioniAmmesse IsNot Nothing AndAlso Not estensioniAmmesse.Contains(Path.GetExtension(percorso).ToLower()) Then Continue For
                    risultato.Add(percorso)
                Catch exFile As Exception
                    ScriviLogCompleto($"FiltraFileEsistenti - Errore su '{percorso}'", exFile)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("FiltraFileEsistenti - Errore", ex)
        End Try
        Return risultato
    End Function

    Private Class ContestoStampa
        Public Percorsi As New List(Of String)()
        Public NomeStampante As String = ""
        Public Impostazioni As WGImpostazioniStampa
        Public Stampati As New List(Of String)()
        Public Errori As New List(Of String)()
        Public Avvisi As New List(Of String)()
        Public StatiFinali As New List(Of String)()
    End Class

    Private Class ContestoConteggioPagine
        Public Percorsi As New List(Of String)()
        Public NomeStampante As String = ""
        Public Impostazioni As WGImpostazioniStampa
        Public TotalePagine As Integer = 0
        Public ContestoStampaAssociato As ContestoStampa
        Public ErroriConteggio As New List(Of String)()
    End Class

    Private _contestoStampaInAttesaConferma As ContestoStampa

    Private Sub StampaFileSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            AvviaStampaFile(elementiSelezionati)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.StampaFileSelezionato - Errore", ex)
        End Try
    End Sub

    Public Sub StampaFile(percorsi As List(Of String))
        Try
            If uiContext Is Nothing Then uiContext = SynchronizationContext.Current
            If percorsi Is Nothing OrElse percorsi.Count = 0 Then Return
            AvviaStampaFile(New List(Of String)(percorsi))
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.StampaFile - Errore", ex)
        End Try
    End Sub

    Private Sub AvviaStampaFile(elementiSelezionati As List(Of String))
        Try
            Dim fileDaStampare As List(Of String) = FiltraFileStampabili(elementiSelezionati)
            If fileDaStampare.Count = 0 Then
                Dim msgManca As New WGMessaggi()
                If elementiSelezionati.Count = 1 Then
                    msgManca.Messaggio("File non trovato o formato non stampabile.")
                Else
                    msgManca.Messaggio("Nessuno dei file selezionati può essere stampato.")
                End If
                msgManca.ShowDialog()
                Return
            End If
            Dim impostazioniComuni As New WGImpostazioniComuni()
            Dim nomeStampante As String = impostazioniComuni.NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampante) Then
                Dim msgNessuna As New WGMessaggi()
                msgNessuna.Messaggio("Nessuna stampante impostata. Sceglila dalle impostazioni.")
                msgNessuna.ShowDialog()
                Return
            End If
            Dim contesto As New ContestoStampa()
            contesto.Percorsi.AddRange(fileDaStampare)
            contesto.NomeStampante = nomeStampante
            contesto.Impostazioni = impostazioniComuni.CostruisciImpostazioniStampa()

            Dim contestoConteggio As New ContestoConteggioPagine()
            contestoConteggio.Percorsi.AddRange(fileDaStampare)
            contestoConteggio.NomeStampante = nomeStampante
            contestoConteggio.Impostazioni = contesto.Impostazioni
            contestoConteggio.ContestoStampaAssociato = contesto

            processoincorso = New WGProcessoInCorso()
            processoincorso.Avvia("Calcolo del numero di pagine in corso, attendere")
            Dim tConteggio As New Thread(AddressOf EseguiConteggioPagineThread)
            tConteggio.IsBackground = True
            tConteggio.SetApartmentState(ApartmentState.STA)
            tConteggio.Start(contestoConteggio)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.AvviaStampaFile - Errore", ex)
            Try
                If processoincorso IsNot Nothing Then processoincorso.Fine()
            Catch exFine As Exception
                ScriviLogCompleto("MenuFile.AvviaStampaFile - Errore chiusura attesa", exFine)
            End Try
            Try
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Errore durante l'avvio della stampa: " & ex.Message)
                msgErrore.ShowDialog()
            Catch exMsg As Exception
                ScriviLogCompleto("MenuFile.AvviaStampaFile - Errore messaggio", exMsg)
            End Try
        End Try
    End Sub

    Private Sub EseguiConteggioPagineThread(state As Object)
        Dim contestoConteggio As ContestoConteggioPagine = TryCast(state, ContestoConteggioPagine)
        Try
            If contestoConteggio Is Nothing Then Return
            For Each percorso As String In contestoConteggio.Percorsi
                Try
                    If EFilePdf(percorso) Then
                        Dim paginePdf As Integer = Global.WGStampante.WGStampante.ContaPaginePdf(percorso)
                        If paginePdf > 0 Then
                            Dim copie As Integer = 1
                            If contestoConteggio.Impostazioni IsNot Nothing AndAlso contestoConteggio.Impostazioni.Copie > 1 Then copie = contestoConteggio.Impostazioni.Copie
                            contestoConteggio.TotalePagine += paginePdf * copie
                        Else
                            Dim erroreConteggio As String = Global.WGStampante.WGStampante.UltimoErrore()
                            If String.IsNullOrWhiteSpace(erroreConteggio) Then erroreConteggio = "il PDF non contiene pagine da stampare"
                            contestoConteggio.ErroriConteggio.Add(Path.GetFileName(percorso) & ": " & erroreConteggio)
                            If contestoConteggio.ContestoStampaAssociato IsNot Nothing Then contestoConteggio.ContestoStampaAssociato.Percorsi.Remove(percorso)
                        End If
                        Continue For
                    End If
                    If Global.WGStampante.WGStampante.EImmagineStampabile(percorso) Then
                        Dim pagineImmagine As Integer = Global.WGStampante.WGStampante.ContaPagineImmagini(New List(Of String) From {percorso}, contestoConteggio.Impostazioni)
                        If pagineImmagine > 0 Then
                            contestoConteggio.TotalePagine += pagineImmagine
                        Else
                            contestoConteggio.ErroriConteggio.Add(Path.GetFileName(percorso) & ": l'immagine non si può leggere")
                            If contestoConteggio.ContestoStampaAssociato IsNot Nothing Then contestoConteggio.ContestoStampaAssociato.Percorsi.Remove(percorso)
                        End If
                        Continue For
                    End If
                    Dim convertitore As New WGConversioneMp3()
                    Dim blocchi As List(Of WGBloccoStampa) = convertitore.EstraiBlocchiDaFile(percorso)
                    If blocchi IsNot Nothing AndAlso blocchi.Count > 0 Then
                        Dim pagine As Integer = Global.WGStampante.WGStampante.ContaPagine(blocchi, contestoConteggio.NomeStampante, contestoConteggio.Impostazioni)
                        If pagine > 0 Then contestoConteggio.TotalePagine += pagine
                    End If
                Catch exFile As Exception
                    ScriviLogCompleto($"MenuFile.EseguiConteggioPagineThread - Errore conteggio '{percorso}'", exFile)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.EseguiConteggioPagineThread - Errore", ex)
        Finally
            Try
                uiContext.Post(AddressOf OnConteggioPagineTerminato, contestoConteggio)
            Catch exPost As Exception
                ScriviLogCompleto("MenuFile.EseguiConteggioPagineThread - Errore notifica interfaccia", exPost)
            End Try
        End Try
    End Sub

    Private Sub OnConteggioPagineTerminato(state As Object)
        Try
            If processoincorso IsNot Nothing Then processoincorso.Fine()
            Dim contestoConteggio As ContestoConteggioPagine = TryCast(state, ContestoConteggioPagine)
            If contestoConteggio Is Nothing OrElse contestoConteggio.ContestoStampaAssociato Is Nothing Then Return
            Dim fileStampabili As Integer = contestoConteggio.ContestoStampaAssociato.Percorsi.Count
            If fileStampabili = 0 Then
                Dim msgNessuno As New WGMessaggi()
                If contestoConteggio.ErroriConteggio.Count = 1 Then
                    msgNessuno.Messaggio("Impossibile stampare. " & contestoConteggio.ErroriConteggio(0))
                Else
                    msgNessuno.Messaggio("Nessuno dei file può essere stampato. " & String.Join("; ", contestoConteggio.ErroriConteggio))
                End If
                msgNessuno.ShowDialog()
                Return
            End If
            _contestoStampaInAttesaConferma = contestoConteggio.ContestoStampaAssociato
            Dim testoDomanda As String
            If fileStampabili > 1 Then
                testoDomanda = "Verranno stampate " & contestoConteggio.TotalePagine.ToString() & " pagine per " & fileStampabili.ToString() & " file."
            Else
                testoDomanda = "Verranno stampate " & contestoConteggio.TotalePagine.ToString() & " pagine."
            End If
            If contestoConteggio.ErroriConteggio.Count > 0 Then
                testoDomanda &= " Esclusi dalla stampa: " & String.Join("; ", contestoConteggio.ErroriConteggio) & "."
            End If
            testoDomanda &= " Continuare?"
            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf RispostaConfermaStampaFile
            domanda.avvia(testoDomanda)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.OnConteggioPagineTerminato - Errore", ex)
        End Try
    End Sub

    Private Sub RispostaConfermaStampaFile(rispostadom As DialogResult)
        Try
            Dim contesto As ContestoStampa = _contestoStampaInAttesaConferma
            _contestoStampaInAttesaConferma = Nothing
            If rispostadom <> DialogResult.Yes Then Return
            If contesto Is Nothing Then Return
            processoincorso = New WGProcessoInCorso()
            If contesto.Percorsi.Count = 1 Then
                processoincorso.Avvia("Stampa in corso, attendere")
            Else
                processoincorso.Avvia("Stampa di " & contesto.Percorsi.Count.ToString() & " file in corso, attendere")
            End If
            Dim t As New Thread(AddressOf EseguiStampaThread)
            t.IsBackground = True
            t.SetApartmentState(ApartmentState.STA)
            t.Start(contesto)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.RispostaConfermaStampaFile - Errore", ex)
        End Try
    End Sub

    Private Sub EseguiStampaThread(state As Object)
        Dim contesto As ContestoStampa = TryCast(state, ContestoStampa)
        Try
            If contesto Is Nothing Then Return
            For Each percorso As String In contesto.Percorsi
                Dim nomeFile As String = Path.GetFileName(percorso)
                Try
                    Dim esito As Global.WGStampante.WGEsitoStampa = Nothing
                    Dim contenutoPresente As Boolean = True
                    If EFilePdf(percorso) Then
                        esito = Global.WGStampante.WGStampante.StampaPdf(percorso, contesto.NomeStampante, nomeFile, contesto.Impostazioni, True)
                    ElseIf Global.WGStampante.WGStampante.EImmagineStampabile(percorso) Then
                        esito = Global.WGStampante.WGStampante.StampaImmagini(New List(Of String) From {percorso}, contesto.NomeStampante, nomeFile, contesto.Impostazioni, True)
                    Else
                        Dim convertitore As New WGConversioneMp3()
                        Dim blocchi As List(Of WGBloccoStampa) = convertitore.EstraiBlocchiDaFile(percorso)
                        If blocchi Is Nothing OrElse blocchi.Count = 0 Then
                            contenutoPresente = False
                        Else
                            esito = Global.WGStampante.WGStampante.StampaBlocchi(blocchi, contesto.NomeStampante, nomeFile, contesto.Impostazioni, True)
                        End If
                    End If
                    If Not contenutoPresente Then
                        contesto.Errori.Add(nomeFile & ": il file non contiene testo da stampare")
                    Else
                        If esito IsNot Nothing AndAlso esito.Riuscita Then
                            contesto.Stampati.Add(nomeFile)
                            If Not String.IsNullOrWhiteSpace(esito.StatoFinale) Then contesto.StatiFinali.Add(nomeFile & ": " & esito.StatoFinale)
                            If Not String.IsNullOrWhiteSpace(esito.Avviso) AndAlso Not contesto.Avvisi.Contains(esito.Avviso) Then contesto.Avvisi.Add(esito.Avviso)
                        Else
                            Dim errore As String = If(esito IsNot Nothing, esito.Errore, "")
                            If String.IsNullOrWhiteSpace(errore) Then errore = "la stampa non è riuscita"
                            log.ScriviLog($"MenuFile.EseguiStampaThread - Stampa non riuscita per '{percorso}' su '{contesto.NomeStampante}': {errore}")
                            contesto.Errori.Add(nomeFile & ": " & errore)
                            If esito IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(esito.StatoFinale) Then contesto.StatiFinali.Add(nomeFile & ": " & esito.StatoFinale)
                        End If
                    End If
                Catch exFile As Exception
                    ScriviLogCompleto($"MenuFile.EseguiStampaThread - Errore stampa '{percorso}'", exFile)
                    contesto.Errori.Add(nomeFile & ": " & exFile.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.EseguiStampaThread - Errore", ex)
            If contesto IsNot Nothing Then contesto.Errori.Add(ex.Message)
        Finally
            Try
                uiContext.Post(AddressOf OnStampaTerminata, contesto)
            Catch exPost As Exception
                ScriviLogCompleto("MenuFile.EseguiStampaThread - Errore notifica interfaccia", exPost)
            End Try
        End Try
    End Sub

    Private Sub OnStampaTerminata(state As Object)
        Try
            If processoincorso IsNot Nothing Then processoincorso.Fine()
            Dim contesto As ContestoStampa = TryCast(state, ContestoStampa)
            If contesto Is Nothing Then Return
            Dim testo As New StringBuilder()
            If contesto.Errori.Count = 0 Then
                If contesto.Stampati.Count = 1 Then
                    testo.Append("Stampa avviata.")
                Else
                    testo.Append("Stampa avviata per " & contesto.Stampati.Count.ToString() & " file.")
                End If
            Else
                If contesto.Stampati.Count > 0 Then
                    testo.Append("Stampa avviata per " & contesto.Stampati.Count.ToString() & " file su " & contesto.Percorsi.Count.ToString() & ". ")
                End If
                If contesto.Errori.Count = 1 AndAlso contesto.Percorsi.Count = 1 Then
                    testo.Append("La stampa non è riuscita. " & contesto.Errori(0))
                Else
                    testo.Append("Non stampati: " & String.Join("; ", contesto.Errori))
                End If
            End If
            If contesto.StatiFinali.Count > 0 Then
                testo.Append(" Stato: " & String.Join("; ", contesto.StatiFinali) & ".")
            End If
            If contesto.Avvisi.Count > 0 Then
                testo.Append(" Stato della stampante: " & String.Join(" ", contesto.Avvisi) & "")
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo.ToString())
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.OnStampaTerminata - Errore", ex)
        End Try
    End Sub

    Private Sub AggiungiSelezionatoAllaScrivania()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            WGScrivania.AggiungiPercorso(elementiSelezionati(0))
        Catch ex As Exception
            ScriviLogCompleto("AggiungiSelezionatoAllaScrivania - Errore", ex)
        End Try
    End Sub

    Private Sub CopiaSelezionatiSullaScrivania()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            WGScrivania.CopiaSullaScrivania(elementiSelezionati)
        Catch ex As Exception
            ScriviLogCompleto("CopiaSelezionatiSullaScrivania - Errore", ex)
        End Try
    End Sub

    Private Sub TraduciFileSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim percorsoFile As String = elementiSelezionati(0)
            If String.IsNullOrEmpty(percorsoFile) OrElse Not WGRete.EsisteFile(percorsoFile) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("File non trovato.")
                msgManca.ShowDialog()
                Return
            End If
            WGTraduttore.TraduciFile(percorsoFile)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.TraduciFileSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub RiassumiFileSelezionatoConAi()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim percorsoFile As String = elementiSelezionati(0)
            If String.IsNullOrEmpty(percorsoFile) OrElse Not WGRete.EsisteFile(percorsoFile) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("File non trovato.")
                msgManca.ShowDialog()
                Return
            End If
            If Not WGAi.AlmenoUnProviderConfigurato() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msg.ShowDialog()
                Return
            End If
            AvviaElaborazioneAiFile(percorsoFile, "Sto elaborando il riassunto, attendere", "Riassunto di " & Path.GetFileName(percorsoFile), False)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.RiassumiFileSelezionatoConAi - Errore", ex)
        End Try
    End Sub

    Private Function DescrizioneModelloRiscrittura() As String
        Try
            If String.IsNullOrWhiteSpace(_modelloRiscrittura) OrElse Not _providerRiscrittura.HasValue Then Return "automatico"
            Return WGAi.NomeProviderInChiaro(_providerRiscrittura.Value) & " " & _modelloRiscrittura
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.DescrizioneModelloRiscrittura - Errore", ex)
            Return "automatico"
        End Try
    End Function

    Private Sub RiscriviFileSelezionatiConAi()
        Try
            Dim selezionati As List(Of String) = lista.ottieniElementi()
            If selezionati Is Nothing OrElse selezionati.Count = 0 Then Return
            Dim validi As New List(Of String)()
            For Each percorso As String In selezionati
                If String.IsNullOrWhiteSpace(percorso) Then Continue For
                If logic.IsWpdPath(percorso) Then Continue For
                If Not WGRete.EsisteFile(percorso) Then Continue For
                If Not WGAi.EFileRiscrivibile(percorso) Then Continue For
                validi.Add(percorso)
            Next
            If validi.Count = 0 Then
                Dim msgNessuno As New WGMessaggi()
                msgNessuno.Messaggio("Seleziona uno o più file di testo da riscrivere.")
                msgNessuno.ShowDialog()
                Return
            End If
            If Not WGAi.AlmenoUnProviderConfigurato() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msg.ShowDialog()
                Return
            End If
            _fileRiscrittura = validi
            Dim provider As WGAi.ProviderAi = WGAi.LeggiProviderPreferito()
            Dim modello As String = ""
            If WGAi.LeggiModelloRiscrittura(provider, modello) Then
                _providerRiscrittura = provider
                _modelloRiscrittura = modello
            Else
                _providerRiscrittura = Nothing
                _modelloRiscrittura = ""
            End If
            LiberaCampiRiscrittura()
            _campiRiscrittura = New Campi()
            _campiRiscrittura.SalvaSuEscape = False
            Dim titolo As String = "Istruzioni"
            _campiRiscrittura.AggiungiMultiriga(titolo, "ai.istruzioni", True, False, "")
            _campiRiscrittura.AggiungiOpzione("Scegli il modello AI", AddressOf ScegliModelloRiscrittura)
            AddHandler _campiRiscrittura.Fine, AddressOf RiscritturaConfermata
            AddHandler _campiRiscrittura.Chiusura, AddressOf RiscritturaAnnullata
            _campiRiscrittura.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.RiscriviFileSelezionatiConAi - Errore", ex)
        End Try
    End Sub

    Private Sub LiberaCampiRiscrittura()
        Try
            If _campiRiscrittura Is Nothing Then Return
            RemoveHandler _campiRiscrittura.Fine, AddressOf RiscritturaConfermata
            RemoveHandler _campiRiscrittura.Chiusura, AddressOf RiscritturaAnnullata
            _campiRiscrittura = Nothing
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.LiberaCampiRiscrittura - Errore", ex)
            _campiRiscrittura = Nothing
        End Try
    End Sub

    Private Sub RiscritturaAnnullata()
        Try
            LiberaCampiRiscrittura()
            _fileRiscrittura = Nothing
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.RiscritturaAnnullata - Errore", ex)
        End Try
    End Sub

    Private Sub RiscritturaConfermata()
        Try
            If _campiRiscrittura Is Nothing Then Return
            Dim istruzioni As String = _campiRiscrittura.RecuperaValore("ai.istruzioni")
            Dim files As List(Of String) = _fileRiscrittura
            Dim provider As WGAi.ProviderAi? = _providerRiscrittura
            Dim modello As String = _modelloRiscrittura
            LiberaCampiRiscrittura()
            _fileRiscrittura = Nothing
            If files Is Nothing OrElse files.Count = 0 OrElse String.IsNullOrWhiteSpace(istruzioni) Then Return
            WGCentroDiControllo.GetInstance().RiscriviFileConAi(files, istruzioni, provider, modello)
            Dim msg As New WGMessaggi()
            msg.Messaggio("La riscrittura è partita in sottofondo. Puoi seguirla dal centro di controllo.")
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.RiscritturaConfermata - Errore", ex)
        End Try
    End Sub

    Private Async Sub ScegliModelloRiscrittura()
        Try
            Dim voci As New List(Of String)()
            _processoModelliRiscrittura = New WGProcessoInCorso()
            _processoModelliRiscrittura.Avvia("Recupero dell'elenco dei modelli, attendere", False)
            Try
                For Each provider As WGAi.ProviderAi In {WGAi.ProviderAi.Gemini, WGAi.ProviderAi.Groq, WGAi.ProviderAi.Ovh, WGAi.ProviderAi.Horde}
                    If Not WGAi.ChiaveApiConfigurata(provider) Then Continue For
                    Dim modelli As List(Of String) = Await WGAi.ElencaModelliAsync(provider)
                    For Each modello As String In modelli
                        voci.Add(WGAi.NomeProviderInChiaro(provider) & ": " & modello)
                    Next
                Next
            Finally
                If _processoModelliRiscrittura IsNot Nothing Then
                    _processoModelliRiscrittura.Fine()
                    _processoModelliRiscrittura = Nothing
                End If
            End Try
            If voci.Count = 0 Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Impossibile recuperare l'elenco dei modelli disponibili.")
                msgErrore.ShowDialog()
                Return
            End If
            _menuModelloRiscrittura = New WGMenuLista()
            _menuModelloRiscrittura.multiselezioneConsentita = False
            _menuModelloRiscrittura.impostaTitolo("Scegli il modello AI per la riscrittura")
            _menuModelloRiscrittura.aggiungiElemento("Automatico", "Automatico")
            For Each voce As String In voci
                _menuModelloRiscrittura.aggiungiElemento(voce, voce)
            Next
            If Not String.IsNullOrWhiteSpace(_modelloRiscrittura) AndAlso _providerRiscrittura.HasValue Then
                Try
                    _menuModelloRiscrittura.posiziona(WGAi.NomeProviderInChiaro(_providerRiscrittura.Value) & ": " & _modelloRiscrittura)
                Catch exPosiziona As Exception
                    ScriviLogCompleto("MenuFile.ScegliModelloRiscrittura (posiziona) - Errore", exPosiziona)
                End Try
            End If
            AddHandler _menuModelloRiscrittura.escPremuto, AddressOf ChiudiMenuModelloRiscrittura
            AddHandler _menuModelloRiscrittura.ElementoSelezionato, AddressOf ModelloRiscritturaSelezionato
            _menuModelloRiscrittura.avvia()
        Catch ex As Exception
            Try
                If _processoModelliRiscrittura IsNot Nothing Then
                    _processoModelliRiscrittura.Fine()
                    _processoModelliRiscrittura = Nothing
                End If
            Catch exPulizia As Exception
                ScriviLogCompleto("MenuFile.ScegliModelloRiscrittura (pulizia) - Errore", exPulizia)
            End Try
            ScriviLogCompleto("MenuFile.ScegliModelloRiscrittura - Errore", ex)
        End Try
    End Sub

    Private Sub ChiudiMenuModelloRiscrittura()
        Try
            If _menuModelloRiscrittura IsNot Nothing Then _menuModelloRiscrittura.abbandona()
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.ChiudiMenuModelloRiscrittura - Errore", ex)
        End Try
    End Sub

    Private Sub ModelloRiscritturaSelezionato()
        Try
            If _menuModelloRiscrittura Is Nothing Then Return
            Dim elementi As List(Of String) = _menuModelloRiscrittura.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return
            Dim scelta As String = elementi(0)
            If scelta.StartsWith("Gemini: ", StringComparison.Ordinal) Then
                _providerRiscrittura = WGAi.ProviderAi.Gemini
                _modelloRiscrittura = scelta.Substring(8)
            ElseIf scelta.StartsWith("Groq: ", StringComparison.Ordinal) Then
                _providerRiscrittura = WGAi.ProviderAi.Groq
                _modelloRiscrittura = scelta.Substring(6)
            ElseIf scelta.StartsWith("OVHcloud: ", StringComparison.Ordinal) Then
                _providerRiscrittura = WGAi.ProviderAi.Ovh
                _modelloRiscrittura = scelta.Substring(10)
            ElseIf scelta.StartsWith("AI Horde: ", StringComparison.Ordinal) Then
                _providerRiscrittura = WGAi.ProviderAi.Horde
                _modelloRiscrittura = scelta.Substring(10)
            Else
                _providerRiscrittura = Nothing
                _modelloRiscrittura = ""
            End If
            WGAi.SalvaModelloRiscrittura(If(_providerRiscrittura.HasValue, _providerRiscrittura.Value, WGAi.ProviderAi.Gemini), _modelloRiscrittura)
            Dim menuChiuso As WGMenuLista = _menuModelloRiscrittura
            _menuModelloRiscrittura = Nothing
            menuChiuso.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Modello scelto: " & DescrizioneModelloRiscrittura() & ".")
            msg.ShowDialog()
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.ModelloRiscritturaSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub DescriviImmagineSelezionataConAi()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim percorsoFile As String = elementiSelezionati(0)
            If String.IsNullOrEmpty(percorsoFile) OrElse Not WGRete.EsisteFile(percorsoFile) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("File non trovato.")
                msgManca.ShowDialog()
                Return
            End If
            If Not WGAi.AlmenoUnProviderConfigurato() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna chiave API configurata. Impostala dalle impostazioni, sezione Intelligenza artificiale.")
                msg.ShowDialog()
                Return
            End If
            AvviaElaborazioneAiFile(percorsoFile, "Sto elaborando la descrizione dell'immagine, attendere", "Descrizione di " & Path.GetFileName(percorsoFile), True)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.DescriviImmagineSelezionataConAi - Errore", ex)
        End Try
    End Sub

    Private Async Sub AvviaElaborazioneAiFile(percorsoFile As String, messaggioAttesa As String, titoloRisultato As String, eImmagine As Boolean)
        Try
            _processoAiFile = New WGProcessoInCorso()
            _processoAiFile.Avvia(messaggioAttesa, False)

            Dim risultato As Tuple(Of String, String)
            If eImmagine Then
                risultato = Await WGAi.DescriviImmagineAsync(percorsoFile)
            Else
                risultato = Await WGAi.RiassumiFileAsync(percorsoFile)
            End If

            If _processoAiFile IsNot Nothing Then
                _processoAiFile.Fine()
                _processoAiFile = Nothing
            End If

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim motivo As String = If(risultato IsNot Nothing AndAlso Not String.IsNullOrEmpty(risultato.Item2), risultato.Item2, "errore sconosciuto")
                log.ScriviLog($"MenuFile.AvviaElaborazioneAiFile - Nessuna risposta per '{percorsoFile}': {motivo}")
                Dim msg As New WGMessaggi()
                msg.Messaggio("Non è stato possibile ottenere una risposta: " & motivo)
                msg.ShowDialog()
                Return
            End If

            MostraTestoRisultatoFile(titoloRisultato, risultato.Item1)
        Catch ex As Exception
            Try
                If _processoAiFile IsNot Nothing Then
                    _processoAiFile.Fine()
                    _processoAiFile = Nothing
                End If
            Catch exPulizia As Exception
                ScriviLogCompleto("MenuFile.AvviaElaborazioneAiFile (pulizia processo) - Errore", exPulizia)
            End Try
            ScriviLogCompleto("MenuFile.AvviaElaborazioneAiFile - Errore", ex)
        End Try
    End Sub

    Private Sub MostraTestoRisultatoFile(titolo As String, testo As String)
        Try
            _campoTestoAiFile = New WGCampo()
            _campoTestoAiFile.init(titolo, testo, True, True, False, False)
            AddHandler _campoTestoAiFile.chiusura, AddressOf OnChiusuraCampoTestoAiFile
            _campoTestoAiFile.avvia()
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.MostraTestoRisultatoFile - Errore", ex)
        End Try
    End Sub

    Private Sub TrascriviAudioSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim percorsoFile As String = elementiSelezionati(0)
            If String.IsNullOrEmpty(percorsoFile) OrElse Not WGRete.EsisteFile(percorsoFile) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("File non trovato.")
                msgManca.ShowDialog()
                Return
            End If
            If Not WGDettatura.ChiaveApiGroqConfigurata() Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Per trascrivere i file audio serve la chiave API di Groq. Impostala dalle impostazioni di digitazione vocale.")
                msg.ShowDialog()
                Return
            End If
            AvviaTrascrizioneAudio(percorsoFile)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.TrascriviAudioSelezionato - Errore", ex)
        End Try
    End Sub

    Private Async Sub AvviaTrascrizioneAudio(percorsoFile As String)
        Dim dettatura As WGDettatura = Nothing
        Try
            _processoAiFile = New WGProcessoInCorso()
            _processoAiFile.Avvia("Trascrizione dell'audio in corso, attendere", False)

            dettatura = New WGDettatura()
            Dim risultato As Tuple(Of String, String) = Await dettatura.TrascriviFileAudioAsync(percorsoFile)

            If _processoAiFile IsNot Nothing Then
                _processoAiFile.Fine()
                _processoAiFile = Nothing
            End If

            If risultato Is Nothing OrElse String.IsNullOrEmpty(risultato.Item1) Then
                Dim motivo As String = If(risultato IsNot Nothing AndAlso Not String.IsNullOrEmpty(risultato.Item2), risultato.Item2, "errore sconosciuto")
                log.ScriviLog($"MenuFile.AvviaTrascrizioneAudio - Trascrizione non riuscita per '{percorsoFile}': {motivo}")
                Dim msg As New WGMessaggi()
                msg.Messaggio("Trascrizione non riuscita: " & motivo)
                msg.ShowDialog()
                Return
            End If

            MostraTestoRisultatoFile("Trascrizione di " & Path.GetFileName(percorsoFile), risultato.Item1)
        Catch ex As Exception
            Try
                If _processoAiFile IsNot Nothing Then
                    _processoAiFile.Fine()
                    _processoAiFile = Nothing
                End If
            Catch exPulizia As Exception
                ScriviLogCompleto("MenuFile.AvviaTrascrizioneAudio (pulizia processo) - Errore", exPulizia)
            End Try
            ScriviLogCompleto("MenuFile.AvviaTrascrizioneAudio - Errore", ex)
        Finally
            Try
                If dettatura IsNot Nothing Then dettatura.Dispose()
            Catch exDispose As Exception
                ScriviLogCompleto("MenuFile.AvviaTrascrizioneAudio (rilascio dettatura) - Errore", exDispose)
            End Try
        End Try
    End Sub

    Private Sub OnChiusuraCampoTestoAiFile(sender As Object, e As EventArgs)
        Try
            If _campoTestoAiFile IsNot Nothing Then
                RemoveHandler _campoTestoAiFile.chiusura, AddressOf OnChiusuraCampoTestoAiFile
                _campoTestoAiFile = Nothing
            End If
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.OnChiusuraCampoTestoAiFile - Errore", ex)
        End Try
    End Sub

    Private Sub ConvertiFileSelezionatoInMp3()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim fileValidi As List(Of String) = FiltraFileTesto(elementiSelezionati)
            If fileValidi.Count = 0 Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio(If(elementiSelezionati.Count = 1, "File non trovato.", "Nessuno dei file selezionati può essere convertito in mp3."))
                msgManca.ShowDialog()
                Return
            End If
            _fileMp3DaConvertire = fileValidi
            _dialogoConversioneMp3 = New WGFileDialog()
            _dialogoConversioneMp3.MultiselezioneConsentita = False
            AddHandler _dialogoConversioneMp3.singolaselezione, AddressOf CartellaMp3FileScelta
            _dialogoConversioneMp3.Avvia()
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.ConvertiFileSelezionatoInMp3 - Errore", ex)
        End Try
    End Sub

    Private Sub CartellaMp3FileScelta(cartella As String)
        Try
            If String.IsNullOrWhiteSpace(cartella) Then Return
            If _fileMp3DaConvertire Is Nothing OrElse _fileMp3DaConvertire.Count = 0 Then Return
            _cartellaMp3 = cartella
            If _fileMp3DaConvertire.Count = 1 Then
                _campiNomeMp3 = New Campi()
                Dim nomeSuggerito As String = Path.GetFileNameWithoutExtension(_fileMp3DaConvertire(0))
                _campiNomeMp3.Aggiungi("Scrivi il nome del file mp3", "v.nomemp3", True,, nomeSuggerito)
                AddHandler _campiNomeMp3.Fine, AddressOf NomeMp3FileInserito
                _campiNomeMp3.avvia()
                Return
            End If
            Dim nomiUsati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim accodati As Integer = 0
            For Each percorsoFile As String In _fileMp3DaConvertire
                Try
                    Dim destinazione As String = DestinazioneMp3Univoca(cartella, Path.GetFileNameWithoutExtension(percorsoFile), nomiUsati)
                    WGCentroDiControllo.GetInstance().AggiungiConversioneMp3DaFile(percorsoFile, destinazione)
                    accodati += 1
                Catch exFile As Exception
                    ScriviLogCompleto($"MenuFile.CartellaMp3FileScelta - Errore accodamento '{percorsoFile}'", exFile)
                End Try
            Next
            If accodati < _fileMp3DaConvertire.Count Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Conversioni avviate: " & accodati.ToString() & " su " & _fileMp3DaConvertire.Count.ToString() & ". Controlla il log per gli altri file.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.CartellaMp3FileScelta - Errore", ex)
        End Try
    End Sub

    Private Function DestinazioneMp3Univoca(cartella As String, nomeBase As String, nomiUsati As HashSet(Of String)) As String
        Try
            If String.IsNullOrWhiteSpace(nomeBase) Then nomeBase = "conversione"
            Dim candidato As String = Path.Combine(cartella, nomeBase & ".mp3")
            Dim contatore As Integer = 2
            While nomiUsati.Contains(candidato) OrElse WGRete.EsisteFile(candidato)
                candidato = Path.Combine(cartella, nomeBase & " (" & contatore.ToString() & ").mp3")
                contatore += 1
            End While
            nomiUsati.Add(candidato)
            Return candidato
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.DestinazioneMp3Univoca - Errore", ex)
            Return Path.Combine(cartella, nomeBase & ".mp3")
        End Try
    End Function

    Private Sub NomeMp3FileInserito()
        Try
            Dim nome As String = ""
            If _campiNomeMp3 IsNot Nothing Then
                nome = _campiNomeMp3.RecuperaValore("v.nomemp3")
                _campiNomeMp3 = Nothing
            End If
            If String.IsNullOrWhiteSpace(nome) Then Return
            If _fileMp3DaConvertire Is Nothing OrElse _fileMp3DaConvertire.Count = 0 Then Return
            If Not nome.ToLowerInvariant().EndsWith(".mp3") Then nome &= ".mp3"
            Dim destinazione As String = Path.Combine(_cartellaMp3, nome)
            WGCentroDiControllo.GetInstance().AggiungiConversioneMp3DaFile(_fileMp3DaConvertire(0), destinazione)
        Catch ex As Exception
            ScriviLogCompleto("MenuFile.NomeMp3FileInserito - Errore", ex)
        End Try
    End Sub

    Private Sub DecomprimiElementoSelezionato()
        Try
            Dim elementiSelezionati As List(Of String) = lista.ottieniElementi()
            If elementiSelezionati Is Nothing OrElse elementiSelezionati.Count = 0 Then Return
            Dim formatiTarGz As String() = {".tar", ".gz", ".tgz", ".bz2", ".xz"}
            Dim formatiZip As String() = {".zip", ".7z", ".rar"}
            Dim avviati As Integer = 0
            For Each elemento As String In elementiSelezionati
                Try
                    If String.IsNullOrEmpty(elemento) OrElse logic.IsWpdPath(elemento) Then Continue For
                    If Not WGRete.EsisteFile(elemento) Then Continue For
                    Dim estensione As String = Path.GetExtension(elemento).ToLower()
                    If formatiZip.Contains(estensione) Then
                        AvviaDecompressioneZip(elemento)
                        avviati += 1
                    ElseIf formatiTarGz.Contains(estensione) OrElse logic.estensioniArchivio.Contains(estensione) Then
                        AvviaDecompressioneSharpZipLib(elemento)
                        avviati += 1
                    End If
                Catch exElemento As Exception
                    ScriviLogCompleto($"FileManager.DecomprimiElementoSelezionato - Errore su '{elemento}'", exElemento)
                End Try
            Next
            If avviati = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuno degli elementi selezionati è un archivio da decomprimere.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.DecomprimiElementoSelezionato - Errore", ex)
        End Try
    End Sub

    Private Sub RiproduciCartellaCorrente()
        Try
            Dim files = System.IO.Directory.GetFiles(currentDirectory, "*.*", System.IO.SearchOption.AllDirectories)
            Dim mediaFiles = files.Where(Function(f) logic.estensioniAudioVideo.Contains(System.IO.Path.GetExtension(f).ToLower())).ToList()
            If mediaFiles.Any() Then
                ApriBrani(mediaFiles)
            Else
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun file multimediale trovato in questa cartella.")
                msg.ShowDialog()
            End If
        Catch ex As Exception
            ScriviLogCompleto("FileManager.RiproduciCartellaCorrente - Errore", ex)
        End Try
    End Sub

    Private Function IsMediaFile(path As String) As Boolean
        Try
            Return logic.estensioniAudioVideo.Contains(System.IO.Path.GetExtension(path).ToLower())
        Catch ex As Exception
            ScriviLogCompleto("IsMediaFile - Errore", ex)
            Return False
        End Try
    End Function

    
    
End Class