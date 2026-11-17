Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Menu principale delle impostazioni suddiviso per categorie, per la configurazione del sistema, della voce, della lettura e delle
' connessioni.
Public Class Impostazioni
    Private configManager As New WGConfig()
    Private impostazioniComuni As New WGImpostazioniComuni()
    Private logger As New wgcontrols.Logger()
    Private menufont As WGMenuLista
    Private menudimensione As WGMenuLista
    Private menutipo As WGMenuLista
    Private menuvoce As WGMenuLista
    Private processoincorso As WGProcessoInCorso
    Private processoTrafilatura As WGProcessoInCorso
    Private processoSvuotamentoCoda As WGProcessoInCorso
    Private contestoSvuotamentoCoda As SynchronizationContext
    Private nomeStampanteSvuotamento As String = ""
    Private svuotamentoCodaInCorso As Boolean = False
    Private processoCapacitaStampante As WGProcessoInCorso
    Private contestoCapacitaStampante As SynchronizationContext
    Private nomeStampanteCapacita As String = ""
    Private letturaCapacitaInCorso As Boolean = False
    Private campiStampanteRete As Campi
    Private processoCollegamentoStampante As WGProcessoInCorso
    Private contestoCollegamentoStampante As SynchronizationContext
    Private percorsoStampanteRete As String = ""
    Private collegamentoStampanteInCorso As Boolean = False
    Private annullaTrafilatura As CancellationTokenSource
    Private contestoTrafilatura As SynchronizationContext
    Private esitoTrafilatura As String = String.Empty
    Private installazioneTrafilaturaInCorso As Boolean = False
    Private menuvelocita As WGMenuLista
    Private menuvolume As WGMenuLista

    Private menuLum As WGMenuLista
    Private menuVolWin As WGMenuLista
    Private menuEnergiaWin As WGMenuLista
    Private _ultimoNvdaRateMenu As Integer = -1
    Private _ultimoNvdaVolumeMenu As Integer = -1
    Private _volumeSuoniOriginale As Integer = -1
    Private _volumeSuoniPercentualePrecisa As Integer = -1
    Private _volumeSistemaOriginale As Integer = -1
    Private _luminositaOriginale As Integer = -1
    Private ReadOnly _luminositaLock As New Object()
    Private _luminositaValoreRichiesto As Integer = -1
    Private _luminositaTaskAttivo As Boolean = False
    Private menuFontWord As WGMenuLista
    Private menuDimWord As WGMenuLista
    Private campiAutoreWord As Campi
    Private menuStampante As WGMenuLista
    Private _annullaStampanti As CancellationTokenSource
    Private campiImpostazioniStampa As Campi
    Private menuOrientamentoStampa As WGMenuLista
    Private menuFronteRetroStampa As WGMenuLista
    Private menuColoreStampa As WGMenuLista
    Private menuQualitaStampa As WGMenuLista
    Private menuCopieStampa As WGMenuLista
    Private menuMargineStampa As WGMenuLista
    Private menuNomeCarattereStampa As WGMenuLista
    Private menuDimensioneCarattereStampa As WGMenuLista
    Private _configuraSintesiMp3 As Boolean = False
    Private _conversioneMp3Anteprima As WGConversioneMp3 = Nothing

    Private Function EseguiCmd(comando As String, argomenti As String) As String
        Const ATTESA_MASSIMA_COMANDO_MS As Integer = 60000

        Dim output As String = ""
        Dim testoErrore As New StringBuilder()

        Try
            Using proc As New Process()
                proc.StartInfo.FileName = comando
                proc.StartInfo.Arguments = argomenti
                proc.StartInfo.RedirectStandardOutput = True
                proc.StartInfo.RedirectStandardError = True
                proc.StartInfo.StandardOutputEncoding = Encoding.Default
                proc.StartInfo.StandardErrorEncoding = Encoding.Default
                proc.StartInfo.UseShellExecute = False
                proc.StartInfo.CreateNoWindow = True

                AddHandler proc.ErrorDataReceived, Sub(mittente As Object, datiErrore As DataReceivedEventArgs)
                                                       Try
                                                           If datiErrore IsNot Nothing AndAlso datiErrore.Data IsNot Nothing Then
                                                               SyncLock testoErrore
                                                                   testoErrore.AppendLine(datiErrore.Data)
                                                               End SyncLock
                                                           End If
                                                       Catch exRiga As Exception
                                                           logger.ScriviLog("EseguiCmd (riga di errore): " & exRiga.Message)
                                                       End Try
                                                   End Sub

                proc.Start()
                proc.BeginErrorReadLine()

                output = proc.StandardOutput.ReadToEnd()

                If Not proc.WaitForExit(ATTESA_MASSIMA_COMANDO_MS) Then
                    Try
                        proc.Kill()
                    Catch exChiusura As Exception
                        logger.ScriviLog("EseguiCmd (chiusura forzata): " & exChiusura.Message)
                    End Try
                End If

                Dim errOutput As String
                SyncLock testoErrore
                    errOutput = testoErrore.ToString()
                End SyncLock

                If Not String.IsNullOrEmpty(errOutput) AndAlso errOutput.Trim().Length > 0 Then
                End If
            End Using
        Catch ex As Exception
            logger.ScriviLog("EseguiCmd eccezione (" & comando & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

        Return output
    End Function

    Private Sub SalvaConfig(chiave As String, valore As String)
        Try
            If Not configManager.EsisteId(chiave) Then
                configManager.AggiungiValore(chiave, valore)

            Else
                configManager.ModificaValore(chiave, valore)
            End If
        Catch ex As Exception
            logger.ScriviLog("SalvaConfig eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

    End Sub

    Private Const VOCE_LETTORE_SCHERMO As String = "Lettore di schermo"
    Private Const VOCE_IMPOSTAZIONI_LETTORE As String = "Impostazioni lettore di schermo"

    ' Menu principale delle impostazioni, in categorie. La voce delle impostazioni del lettore di schermo
    ' di WinGabriel compare solo quando quel lettore è scelto, e si aggiorna al ritorno da un altro menu.
    Public Sub MostraMenuPrincipale()
        Try
            Dim menuImpostazioni As New WGMenu()
            menuImpostazioni.aggiungi("Sistema", AddressOf MostraCategoriaSistema)
            menuImpostazioni.aggiungi("Connessioni", AddressOf MostraMenuConnessioni)
            menuImpostazioni.aggiungi("Voce e audio", AddressOf MostraCategoriaVoceEAudio)
            menuImpostazioni.aggiungi("Braille", AddressOf MenuBraille.Mostra)
            menuImpostazioni.aggiungi("Tastiera: " & WGTastiera.NomeLinguaTastiera(), AddressOf ScegliLinguaTastiera)
            menuImpostazioni.aggiungi("Giochi e chat", AddressOf MostraMenuGiochiRete)
            menuImpostazioni.aggiungi("Lettura e scrittura", AddressOf MostraCategoriaLetturaEScrittura)
            menuImpostazioni.aggiungi("Mouse, touch e digitazione vocale", AddressOf MostraCategoriaIngresso)
            menuImpostazioni.aggiungi("Applicazioni", AddressOf MostraCategoriaApplicazioni)
            menuImpostazioni.aggiungi("Plugin", AddressOf WGGestorePlugin.MostraMenu)
            menuImpostazioni.aggiungi(VOCE_LETTORE_SCHERMO, AddressOf MenuSceltaLettoreSchermo)
            If LettoreSchermoWinGabrielPreferito() Then
                menuImpostazioni.aggiungi(VOCE_IMPOSTAZIONI_LETTORE, AddressOf MenuImpostazioniLettoreWinGabriel)
            End If
            AddHandler menuImpostazioni.MenuRiattivato, Sub() AggiornaVoceImpostazioniLettore(menuImpostazioni)
            menuImpostazioni.aggiungiOpzione("Esporta configurazione", Sub() ScambioDati.EsportaConfigurazione())
            menuImpostazioni.aggiungiOpzione("Importa configurazione", Sub() ScambioDati.ImportaConfigurazione())
            menuImpostazioni.aggiungiOpzione("Reinizializza configurazione", Sub() ScambioDati.ReinizializzaConfigurazione())
            menuImpostazioni.avvia("Menu Impostazioni")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuPrincipale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaVoceImpostazioniLettore(menu As WGMenu)
        Try
            If menu Is Nothing Then Return
            If LettoreSchermoWinGabrielPreferito() Then
                menu.AggiungiDopo(VOCE_LETTORE_SCHERMO, VOCE_IMPOSTAZIONI_LETTORE, AddressOf MenuImpostazioniLettoreWinGabriel)
            Else
                menu.RimuoviVoce(VOCE_IMPOSTAZIONI_LETTORE)
            End If
        Catch ex As Exception
            logger.ScriviLog("AggiornaVoceImpostazioniLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCategoriaSistema()
        Try
            Dim m As New WGMenu()
            If WGModalita.Smart Then
                m.aggiungi("Sistema", AddressOf MostraMenuSistema)
                m.aggiungi("Scrivania", AddressOf MostraMenuImpostazioniScrivania)
            Else
                m.aggiungi("Modalità: classica", AddressOf MostraMenuModalita)
            End If
            m.aggiungi("Menu", AddressOf MostraMenuConfigurazioniMenu)
            m.aggiungi("Avvio", AddressOf MostraMenuAvvio)
            m.aggiungi("Aggiornamento", AddressOf MostraMenuAggiornamento)
            m.aggiungi("Cloud", AddressOf MostraMenuCloud)
            m.aggiungi("Configurazione guidata", Sub() ConfigurazioneGuidata.Avvia())
            ' installazione: da portatile si installa subito; con WinGabriel già installato si può aggiornare l'installazione (da una copia
            ' portatile) o disinstallarlo
            If Not IntegrazioneWindows.InstallatoNelSistema() Then
                m.aggiungi("Installa WinGabriel", Async Sub() Await ConfigurazioneGuidata.InstallaAsync())
            Else
                If Not IntegrazioneWindows.EInstallato() Then m.aggiungi("Aggiorna l'installazione", Async Sub() Await ConfigurazioneGuidata.AggiornaInstallazioneAsync())
                m.aggiungi("Disinstalla WinGabriel", Async Sub() Await ConfigurazioneGuidata.DisinstallaAsync())
            End If
            m.aggiungi("Impostazioni di Windows", AddressOf MostraMenuImpostazioniWindows)
            m.avvia("Sistema e avvio")
        Catch ex As Exception
            logger.ScriviLog("MostraCategoriaSistema eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCategoriaVoceEAudio()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Sintesi vocale", AddressOf MostraMenuSintesiVocale)
            m.aggiungi("Suoni", AddressOf MostraMenuSuoni)
            m.aggiungi("Audio", AddressOf MostraMenuAudio)
            m.avvia("Voce e audio")
        Catch ex As Exception
            logger.ScriviLog("MostraCategoriaVoceEAudio eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCategoriaLetturaEScrittura()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Lettura", AddressOf MostraMenuLettura)
            m.aggiungi("Scrittura", AddressOf MostraMenuScrittura)
            m.avvia("Lettura e scrittura")
        Catch ex As Exception
            logger.ScriviLog("MostraCategoriaLetturaEScrittura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCategoriaIngresso()
        Try
            Dim m As New WGMenu()
            If WGTouk.GetInstance().TouchDisponibile Then
                m.aggiungi("Touch e Mouse", AddressOf MostraMenuTouchEMouse)
            Else
                m.aggiungi("Mouse", AddressOf MostraMenuMouse)
            End If
            If WGDettatura.SistemaSupportato Then
                m.aggiungi("Digitazione vocale", AddressOf MostraMenuDigitazioneVocale)
            End If
            m.avvia("Mouse, touch e digitazione vocale")
        Catch ex As Exception
            logger.ScriviLog("MostraCategoriaIngresso eccezione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>La tastiera con cui si scrive nei campi, in Word e negli altri editor: non e' quella che Windows ha
    ''' attiva (il cambio lingua di Windows non si legge), ma quella scelta qui. Cambia i simboli sopra i numeri e le
    ''' combinazioni AltGr; le lettere semplici seguono sempre la disposizione fisica dei tasti di quella tastiera.</summary>
    Private Sub ScegliLinguaTastiera()
        Try
            Dim lista As New WGMenuLista()
            lista.invioNonConsentito = False
            lista.multiselezioneConsentita = False
            lista.impostaTitolo("Tastiera")
            Dim corrente As String = WGTastiera.CodiceLinguaTastiera
            For Each voce As String() In WGTastiera.LingueTastiera
                lista.aggiungiElemento(voce(1), voce(0), voce(0) = corrente)
            Next
            AddHandler lista.escPremuto, Sub() lista.abbandona()
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                       Try
                                                           lista.abbandona()
                                                           WGTastiera.CodiceLinguaTastiera = id
                                                           Dim msg As New WGMessaggi()
                                                           msg.Messaggio("Tastiera: " & WGTastiera.NomeLinguaTastiera(id) & ".")
                                                           msg.ShowDialog()
                                                           MostraMenuPrincipale()
                                                       Catch exScelta As Exception
                                                           logger.ScriviLog("ScegliLinguaTastiera scelta: " & exScelta.Message)
                                                       End Try
                                                   End Sub
            lista.avvia()
        Catch ex As Exception
            logger.ScriviLog("ScegliLinguaTastiera: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCategoriaApplicazioni()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Browser", AddressOf MenuBrowser)
            m.aggiungi("Traduttore", AddressOf MenuTraduttore)
            m.aggiungi("Lettore multimediale", AddressOf MostraMenuPlayerMultimediale)
            m.aggiungi("YouTube", AddressOf MenuImpostazioniYoutube)
            m.aggiungi("Quotidiani", AddressOf MenuImpostazioniQuotidiani)
            m.aggiungi("Posta", AddressOf MenuImpostazioniPosta)
            m.aggiungi("File", AddressOf MostraMenuFile)
            m.aggiungi("Stampante", AddressOf MostraMenuStampante)
            m.aggiungi("Intelligenza artificiale", AddressOf MostraMenuIntelligenzaArtificiale)
            m.aggiungi("Musica", AddressOf ConfigurazioneMusica.Mostra)
            m.aggiungi("Registratore vocale", AddressOf ConfigurazioneRegistratore.Mostra)
            m.aggiungi("Fotocamera", AddressOf ConfigurazioneFotocamera.Mostra)
            m.aggiungi("Podcast", AddressOf MenuPodcast.MostraImpostazioni)
            m.avvia("Impostazioni delle applicazioni")
        Catch ex As Exception
            logger.ScriviLog("MostraCategoriaApplicazioni eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuVerificaAggiornamenti()
        Try
            Dim menuVerifica As New WGMenu()
            menuVerifica.aggiungi("Attivato", AddressOf AttivaVerificaAggiornamenti)
            menuVerifica.aggiungi("Disattivato", AddressOf DisattivaVerificaAggiornamenti)
            menuVerifica.posiziona(IndiceBinario(ID_CONFIG_VERIFICA_AGGIORNAMENTI, "true", "true"))
            menuVerifica.avvia("Verifica disponibilità aggiornamenti", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuVerificaAggiornamenti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaVerificaAggiornamenti()
        Try
            SalvaConfig(ID_CONFIG_VERIFICA_AGGIORNAMENTI, "true")

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("La disponibilità di aggiornamenti verrà verificata a ogni avvio del programma.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaVerificaAggiornamenti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaVerificaAggiornamenti()
        Try
            SalvaConfig(ID_CONFIG_VERIFICA_AGGIORNAMENTI, "false")

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("La verifica automatica all'avvio è disattivata. Puoi comunque cercare gli aggiornamenti dal menu Aggiornamento.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaVerificaAggiornamenti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAggiornamento()
        Try
            Dim menuAggiornamento As New WGMenu()
            menuAggiornamento.aggiungi("Verifica disponibilità aggiornamenti", AddressOf MostraMenuVerificaAggiornamenti)
            menuAggiornamento.aggiungi("Canale degli aggiornamenti", AddressOf MostraMenuCanaleAggiornamenti)
            menuAggiornamento.aggiungi("Notizie", AddressOf MostraMenuNotizie)
            menuAggiornamento.avvia("Aggiornamento")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAggiornamento eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuCanaleAggiornamenti()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Stabile: le versioni pubblicate per tutti", Sub() SalvaConfigurazioneGenerica(ID_CONFIG_CANALE_AGGIORNAMENTI, "stable", "Riceverai le versioni stabili, quelle pubblicate per tutti."))
            m.aggiungi("Beta: le versioni di prova, in anticipo", Sub() SalvaConfigurazioneGenerica(ID_CONFIG_CANALE_AGGIORNAMENTI, "beta", "Riceverai in anticipo le versioni beta, che possono avere ancora dei difetti. Se non c'è una beta, resta la versione stabile."))
            m.posiziona(IndiceMultiplo(ID_CONFIG_CANALE_AGGIORNAMENTI, New List(Of String) From {"stable", "beta"}, "stable"))
            m.avvia("Canale degli aggiornamenti", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuCanaleAggiornamenti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuNotizie()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Avviso ultime notizie", AddressOf MostraMenuAvvisoUltimeNotizie)
            m.aggiungi("Intervallo di controllo", AddressOf MostraMenuIntervalloControlloNotizie)
            m.avvia("Notizie")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuNotizie eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuIntervalloControlloNotizie()
        Try
            Dim m As New WGMenu()
            Dim valori As New List(Of String) From {"15", "30", "60", "120", "240"}
            Dim etichette As New List(Of String) From {"15 minuti", "30 minuti", "1 ora", "2 ore", "4 ore"}
            For indice As Integer = 0 To valori.Count - 1
                Dim valoreLocale As String = valori(indice)
                Dim etichettaLocale As String = etichette(indice)
                m.aggiungi("Ogni " & etichettaLocale, Sub() SalvaConfigurazioneGenerica(ID_CONFIG_INTERVALLO_CONTROLLO_NOTIZIE, valoreLocale, "Controllo nuove notizie impostato ogni " & etichettaLocale & "."))
            Next
            m.posiziona(IndiceMultiplo(ID_CONFIG_INTERVALLO_CONTROLLO_NOTIZIE, valori, "15"))
            m.avvia("Intervallo di controllo", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuIntervalloControlloNotizie eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAvvisoUltimeNotizie()
        Try
            Dim menuAvviso As New WGMenu()
            menuAvviso.aggiungi("Attivato", AddressOf AttivaAvvisoUltimeNotizie)
            menuAvviso.aggiungi("Disattivato", AddressOf DisattivaAvvisoUltimeNotizie)
            menuAvviso.posiziona(IndiceBinario(ID_CONFIG_AVVISO_ULTIME_NOTIZIE, "true", "true"))
            menuAvviso.avvia("Avviso ultime notizie", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAvvisoUltimeNotizie eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaAvvisoUltimeNotizie()
        Try
            SalvaConfig(ID_CONFIG_AVVISO_ULTIME_NOTIZIE, "true")

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("All'avvio di WinGabriel verrà segnalata la presenza di nuove notizie non ancora lette.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaAvvisoUltimeNotizie eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaAvvisoUltimeNotizie()
        Try
            SalvaConfig(ID_CONFIG_AVVISO_ULTIME_NOTIZIE, "false")

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("L'avviso delle ultime notizie all'avvio del programma è disattivato.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaAvvisoUltimeNotizie eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAvvio()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Avvio del programma", AddressOf MostraMenuAvvioProgramma)
            m.aggiungi("Avvio come amministratore", AddressOf MostraMenuAvvioAmministratore)
            m.aggiungi("Suoni e volume all'avvio", AddressOf MostraMenuSuoniVolumeAvvio)
            m.aggiungi("Volume preferito all'avvio", AddressOf MenuVolumePreferitoAvvio)
            m.aggiungi("Luminosità schermo", AddressOf ImpostaLuminosita)
            m.avvia("Avvio")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAvvioProgramma()
        Try
            Dim menuAvvio As New WGMenu()
            menuAvvio.aggiungi("Attivo", AddressOf AttivaAvvioAutomatico)
            menuAvvio.aggiungi("Disattivo", AddressOf DisattivaAvvioAutomatico)

            If AvvioAutomaticoAttivo() Then
                menuAvvio.posiziona(0)
            Else
                menuAvvio.posiziona(1)
            End If

            menuAvvio.avvia("Avvio all'accensione", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAvvioProgramma eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' All'avvio WinGabriel chiede la conferma di amministratore. Se non la si dà, parte lo stesso, senza privilegi: con i privilegi
    ' il lettore di schermo legge anche le applicazioni avviate come amministratore. Con i privilegi Windows nasconde però i
    ' dischi di rete collegati dal programma non elevato.
    Private Sub MostraMenuAvvioAmministratore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivo", Sub() SalvaConfigurazioneGenerica(AvvioAmministratore.ID_CONFIG, "true", "All'avvio WinGabriel chiederà la conferma di amministratore. Se non la dai, partirà lo stesso senza privilegi."))
            m.aggiungi("Disattivo", Sub() SalvaConfigurazioneGenerica(AvvioAmministratore.ID_CONFIG, "false", "WinGabriel partirà senza chiedere la conferma di amministratore."))
            m.posiziona(IndiceBinario(AvvioAmministratore.ID_CONFIG, "true", "true"))
            m.avvia("Avvio come amministratore", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAvvioAmministratore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuSuoniVolumeAvvio()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivo", Sub() SalvaConfigurazioneGenerica(ID_CONFIG_DISATTIVA_SUONI_VOLUME_AVVIO, "true", "I suoni e la modifica automatica del volume all'avvio sono disattivati."))
            m.aggiungi("Disattivo", Sub() SalvaConfigurazioneGenerica(ID_CONFIG_DISATTIVA_SUONI_VOLUME_AVVIO, "false", "I suoni e la modifica automatica del volume all'avvio sono attivi."))
            m.posiziona(IndiceBinario(ID_CONFIG_DISATTIVA_SUONI_VOLUME_AVVIO, "true", "false"))
            m.avvia("Disattiva suoni e volume all'avvio", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSuoniVolumeAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private menuVolumeAvvioLista As WGMenuLista

    Private Sub MenuVolumePreferitoAvvio()
        Try
            menuVolumeAvvioLista = New WGMenuLista()
            menuVolumeAvvioLista.multiselezioneConsentita = False
            menuVolumeAvvioLista.impostaTitolo("Scegli il volume preferito all'avvio")
            menuVolumeAvvioLista.aggiungiElemento("Nessuna preferenza (mantieni il volume attuale)", "-1")
            menuVolumeAvvioLista.aggiungiElemento("10%", "10")
            menuVolumeAvvioLista.aggiungiElemento("20%", "20")
            menuVolumeAvvioLista.aggiungiElemento("30%", "30")
            menuVolumeAvvioLista.aggiungiElemento("40%", "40")
            menuVolumeAvvioLista.aggiungiElemento("50%", "50")
            menuVolumeAvvioLista.aggiungiElemento("60%", "60")
            menuVolumeAvvioLista.aggiungiElemento("70%", "70")
            menuVolumeAvvioLista.aggiungiElemento("80%", "80")
            menuVolumeAvvioLista.aggiungiElemento("90%", "90")
            menuVolumeAvvioLista.aggiungiElemento("100%", "100")
            menuVolumeAvvioLista.aggiungiOpzione("Regolazione affinata", AddressOf OpzioneAffinataVolumeAvvio)

            menuVolumeAvvioLista.posiziona(VolumePreferitoAvvio().ToString())

            AddHandler menuVolumeAvvioLista.escPremuto, AddressOf ChiudiMenuVolumeAvvio
            AddHandler menuVolumeAvvioLista.ElementoSelezionato, AddressOf VolumeAvvioSelezionato
            menuVolumeAvvioLista.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuVolumePreferitoAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OpzioneAffinataVolumeAvvio(sender As Object, e As EventArgs)
        Try
            RicaricaAffinata(menuVolumeAvvioLista, VolumePreferitoAvvio())
        Catch ex As Exception
            logger.ScriviLog("OpzioneAffinataVolumeAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuVolumeAvvio()
        Try
            If menuVolumeAvvioLista IsNot Nothing Then menuVolumeAvvioLista.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuVolumeAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VolumeAvvioSelezionato()
        Try
            Dim elementi As List(Of String) = menuVolumeAvvioLista.ottieniElementi()
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim valoreScelto As String = elementi(0)
            Dim messaggio As New WGMessaggi()

            If valoreScelto = "-1" Then
                SalvaConfig(ID_CONFIG_VOLUME_PREFERITO_AVVIO, "-1")
                messaggio.Messaggio("All'avvio verrà mantenuto il volume attuale del suono di caricamento.")
            Else
                SalvaConfig(ID_CONFIG_VOLUME_PREFERITO_AVVIO, valoreScelto)
                messaggio.Messaggio("Volume del suono di avvio impostato al " & valoreScelto & "%.")
            End If

            messaggio.ShowDialog()
            menuVolumeAvvioLista.abbandona()
        Catch ex As Exception
            logger.ScriviLog("VolumeAvvioSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaAvvioAutomatico()
        Try
            Dim messaggio As New WGMessaggi()

            If ImpostaAvvioAutomatico(True) Then
                SalvaConfig(ID_CONFIG_AVVIO_AUTOMATICO, "true")
                messaggio.Messaggio("WinGabriel verrà avviato automaticamente a ogni accesso al computer.")
            Else
                messaggio.Messaggio("Non è stato possibile impostare l'avvio automatico. Il dettaglio si trova nel file di log.")
            End If

            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaAvvioAutomatico eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaAvvioAutomatico()
        Try
            Dim messaggio As New WGMessaggi()

            If ImpostaAvvioAutomatico(False) Then
                SalvaConfig(ID_CONFIG_AVVIO_AUTOMATICO, "false")
                messaggio.Messaggio("WinGabriel non verrà avviato automaticamente all'accensione del computer.")
            Else
                messaggio.Messaggio("Non è stato possibile modificare l'avvio automatico. Il dettaglio si trova nel file di log.")
            End If

            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaAvvioAutomatico eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuConnessioni()
        Try
            Dim menuConnessioni As New WGMenu()
            ' Wi-Fi e Bluetooth con la libreria WGConnessioni: API di Windows, senza diritti di amministratore.
            menuConnessioni.aggiungi("Wi-Fi", Sub() WGConnessioni.WGConnessioni.MostraWifi())
            menuConnessioni.aggiungi("Bluetooth", Sub() WGConnessioni.WGConnessioni.MostraBluetooth())
            menuConnessioni.aggiungi("Dove ascoltare l'audio, altoparlanti o cuffie", Sub() WGUscitaAudio.Scegli())
            menuConnessioni.avvia("Connessioni")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuConnessioni eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuTouchEMouse()
        Try
            Dim menuTouchEMouse As New WGMenu()
            menuTouchEMouse.aggiungi("Mouse", AddressOf MostraMenuMouse)
            menuTouchEMouse.aggiungi("Touch", AddressOf MostraMenuTouch)
            menuTouchEMouse.avvia("Touch e Mouse")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuTouchEMouse eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuTouch()
        Try
            Dim menuTouch As New WGMenu()
            menuTouch.aggiungi("Attivazione touk", AddressOf MostraMenuAttivazioneTouk)
            menuTouch.aggiungi("Adattamento al dispositivo", AddressOf MostraMenuAdattamentoDispositivo)
            menuTouch.aggiungi("Orientamento", AddressOf MostraMenuOrientamento)
            menuTouch.aggiungi("Tastiera", AddressOf MostraMenuTastiera)
            menuTouch.aggiungi("Stile digitazione", AddressOf MostraMenuModalitaDigitazione)
            menuTouch.avvia("Impostazioni touch")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuTouch eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAttivazioneTouk()
        Try
            Dim statoAttuale As Boolean = WGTouk.GetInstance().TouchAttivo
            Dim menuAttivazione As New WGMenu()
            menuAttivazione.aggiungi("Attivato", AddressOf AttivaTouch)
            menuAttivazione.aggiungi("Disattivato", AddressOf DisattivaTouch)
            menuAttivazione.posiziona(If(statoAttuale, 0, 1))
            menuAttivazione.avvia("Attivazione touk", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAttivazioneTouk eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAdattamentoDispositivo()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch Is Nothing Then Return
            Dim statoAttuale As Boolean = gestoreTouch.AdattaAlDispositivo
            Dim menuAdattamento As New WGMenu()
            menuAdattamento.aggiungi("Attivato", AddressOf AttivaAdattamentoDispositivo)
            menuAdattamento.aggiungi("Disattivato", AddressOf DisattivaAdattamentoDispositivo)
            menuAdattamento.posiziona(If(statoAttuale, 0, 1))
            menuAdattamento.avvia("Adattamento al dispositivo", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAdattamentoDispositivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaAdattamentoDispositivo()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch IsNot Nothing Then gestoreTouch.AdattaAlDispositivo = True
            SalvaConfig(ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO, "true")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Adattamento al dispositivo attivato e salvato nella configurazione. La dimensione di un dito e la cadenza dei campioni vengono ricavate dallo schermo durante l'uso.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaAdattamentoDispositivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaAdattamentoDispositivo()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch IsNot Nothing Then gestoreTouch.AdattaAlDispositivo = False
            SalvaConfig(ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO, "false")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Adattamento al dispositivo disattivato e salvato nella configurazione. Valgono i valori fissi delle impostazioni del tocco.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaAdattamentoDispositivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuOrientamento()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch Is Nothing Then Return
            Dim bloccato As Boolean = gestoreTouch.OrientamentoBloccato
            Dim menuOrientamento As New WGMenu()
            menuOrientamento.aggiungi("Automatico", AddressOf ImpostaOrientamentoAutomatico)
            menuOrientamento.aggiungi("Bloccato", AddressOf ImpostaOrientamentoBloccato)
            menuOrientamento.posiziona(If(bloccato, 1, 0))
            menuOrientamento.avvia("Orientamento", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuOrientamento eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaOrientamentoAutomatico()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch IsNot Nothing Then gestoreTouch.OrientamentoBloccato = False
            SalvaConfig(ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO, "false")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Orientamento automatico attivato e salvato nella configurazione. La disposizione di menu, campi e tastiera segue la rotazione dello schermo.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaOrientamentoAutomatico eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaOrientamentoBloccato()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch IsNot Nothing Then gestoreTouch.OrientamentoBloccato = True
            SalvaConfig(ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO, "true")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Orientamento bloccato e salvato nella configurazione. La disposizione resta quella fissa, qualunque sia la rotazione dello schermo.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaOrientamentoBloccato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuTastiera()
        Try
            Dim menuTastiera As New WGMenu()
            menuTastiera.aggiungi("Grandezza dei tasti", AddressOf MostraMenuGrandezzaTasti)
            menuTastiera.aggiungi("Tolleranza al movimento", AddressOf MostraMenuTolleranzaMovimento)
            menuTastiera.aggiungi("Intervallo fra le lettere", AddressOf MostraMenuIntervalloLettura)
            Dim suggerimentiAttivi As Boolean = WGPredizione.SuggerimentiAttivi()
            menuTastiera.aggiungi("Suggerimenti di scrittura: " & If(suggerimentiAttivi, "attivi", "disattivati"), AddressOf ScegliSuggerimenti)
            menuTastiera.avvia("Tastiera")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuTastiera eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuTolleranzaMovimento()
        Try
            Dim attuale As WGTastieraTouch.TolleranzaMovimento = WGTastieraTouch.LeggiTolleranzaMovimento()
            Dim menu As New WGMenu()
            menu.aggiungi("Bassa: il dito cambia tasto subito", AddressOf ImpostaTolleranzaBassa)
            menu.aggiungi("Media", AddressOf ImpostaTolleranzaMedia)
            menu.aggiungi("Alta: serve uno spostamento ampio per cambiare tasto", AddressOf ImpostaTolleranzaAlta)
            Dim indice As Integer
            Select Case attuale
                Case WGTastieraTouch.TolleranzaMovimento.Bassa
                    indice = 0
                Case WGTastieraTouch.TolleranzaMovimento.Alta
                    indice = 2
                Case Else
                    indice = 1
            End Select
            menu.posiziona(indice)
            menu.avvia("Tolleranza al movimento", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuTolleranzaMovimento eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaTolleranzaBassa()
        Try
            WGTastieraTouch.SalvaTolleranzaMovimento(WGTastieraTouch.TolleranzaMovimento.Bassa)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Tolleranza al movimento bassa impostata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaTolleranzaBassa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaTolleranzaMedia()
        Try
            WGTastieraTouch.SalvaTolleranzaMovimento(WGTastieraTouch.TolleranzaMovimento.Media)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Tolleranza al movimento media impostata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaTolleranzaMedia eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaTolleranzaAlta()
        Try
            WGTastieraTouch.SalvaTolleranzaMovimento(WGTastieraTouch.TolleranzaMovimento.Alta)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Tolleranza al movimento alta impostata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaTolleranzaAlta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuIntervalloLettura()
        Try
            Dim attuale As WGTastieraTouch.IntervalloLettura = WGTastieraTouch.LeggiIntervalloLettura()
            Dim menu As New WGMenu()
            menu.aggiungi("Breve", AddressOf ImpostaIntervalloBreve)
            menu.aggiungi("Medio", AddressOf ImpostaIntervalloMedio)
            menu.aggiungi("Lungo: le lettere si susseguono piu' distanziate", AddressOf ImpostaIntervalloLungo)
            Dim indice As Integer
            Select Case attuale
                Case WGTastieraTouch.IntervalloLettura.Breve
                    indice = 0
                Case WGTastieraTouch.IntervalloLettura.Lungo
                    indice = 2
                Case Else
                    indice = 1
            End Select
            menu.posiziona(indice)
            menu.avvia("Intervallo fra le lettere", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuIntervalloLettura eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaIntervalloBreve()
        Try
            WGTastieraTouch.SalvaIntervalloLettura(WGTastieraTouch.IntervalloLettura.Breve)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Intervallo breve impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaIntervalloBreve eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaIntervalloMedio()
        Try
            WGTastieraTouch.SalvaIntervalloLettura(WGTastieraTouch.IntervalloLettura.Medio)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Intervallo medio impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaIntervalloMedio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaIntervalloLungo()
        Try
            WGTastieraTouch.SalvaIntervalloLettura(WGTastieraTouch.IntervalloLettura.Lungo)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Intervallo lungo impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaIntervalloLungo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliSuggerimenti()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attivi", Sub(s, e)
                                         WGPredizione.ImpostaSuggerimentiAttivi(True)
                                         Dim msg As New WGMessaggi()
                                         msg.Messaggio("Suggerimenti di scrittura attivati.")
                                         msg.ShowDialog()
                                     End Sub)
            menu.aggiungi("Disattivati", Sub(s, e)
                                              WGPredizione.ImpostaSuggerimentiAttivi(False)
                                              Dim msg As New WGMessaggi()
                                              msg.Messaggio("Suggerimenti di scrittura disattivati.")
                                              msg.ShowDialog()
                                          End Sub)
            menu.posiziona(If(WGPredizione.SuggerimentiAttivi(), 0, 1))
            menu.avvia("Suggerimenti di scrittura", True)
        Catch ex As Exception
            logger.ScriviLog("ScegliSuggerimenti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuModalitaDigitazione()
        Try
            Dim attuale As WGTastieraTouch.ModalitaDigitazione = WGTastieraTouch.LeggiModalitaDigitazioneSalvata()
            Dim menu As New WGMenu()
            menu.aggiungi("Digitazione standard: si seleziona la lettera e si tocca due volte per inserirla", AddressOf ImpostaDigitazioneStandard)
            menu.aggiungi("Digitazione a tocco: si scorre sulle lettere e si alza il dito per inserirla", AddressOf ImpostaDigitazioneATocco)
            Dim indice As Integer = If(attuale = WGTastieraTouch.ModalitaDigitazione.Tocco, 1, 0)
            menu.posiziona(indice)
            menu.avvia("Stile digitazione", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuModalitaDigitazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaDigitazioneStandard()
        Try
            WGTastieraTouch.SalvaModalitaDigitazione(WGTastieraTouch.ModalitaDigitazione.DoppioTocco)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Digitazione standard impostata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaDigitazioneStandard eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaDigitazioneATocco()
        Try
            WGTastieraTouch.SalvaModalitaDigitazione(WGTastieraTouch.ModalitaDigitazione.Tocco)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Digitazione a tocco impostata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaDigitazioneATocco eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuGrandezzaTasti()
        Try
            Dim attuale As WGTastieraTouch.GrandezzaTasti = WGTastieraTouch.LeggiGrandezzaTasti()
            Dim menu As New WGMenu()
            menu.aggiungi("Piccoli", AddressOf ImpostaTastiPiccoli)
            menu.aggiungi("Medi", AddressOf ImpostaTastiMedi)
            menu.aggiungi("Grandi", AddressOf ImpostaTastiGrandi)
            Dim indice As Integer
            Select Case attuale
                Case WGTastieraTouch.GrandezzaTasti.Piccoli
                    indice = 0
                Case WGTastieraTouch.GrandezzaTasti.Grandi
                    indice = 2
                Case Else
                    indice = 1
            End Select
            menu.posiziona(indice)
            menu.avvia("Grandezza dei tasti", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuGrandezzaTasti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaTastiPiccoli()
        Try
            WGTastieraTouch.SalvaGrandezzaTasti(WGTastieraTouch.GrandezzaTasti.Piccoli)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Tasti piccoli impostati.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaTastiPiccoli eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaTastiMedi()
        Try
            WGTastieraTouch.SalvaGrandezzaTasti(WGTastieraTouch.GrandezzaTasti.Medi)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Tasti medi impostati.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaTastiMedi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaTastiGrandi()
        Try
            WGTastieraTouch.SalvaGrandezzaTasti(WGTastieraTouch.GrandezzaTasti.Grandi)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Tasti grandi impostati.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaTastiGrandi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraStatoTouchAttuale()
        Try
            Dim statoAttuale As Boolean = WGTouk.GetInstance().TouchAttivo
            Dim statoTesto As String = If(statoAttuale, "attivo", "disattivo")
            MostraMessaggioStato("Il touch è attualmente " & statoTesto & ".")
        Catch ex As Exception
            logger.ScriviLog("MostraStatoTouchAttuale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAudio()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Altoparlanti", AddressOf MostraMenuAltoparlanti)
            menu.aggiungi("Microfono", AddressOf MostraMenuMicrofono)
            menu.avvia("Audio")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAudio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAltoparlanti()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Dispositivo audio predefinito", AddressOf MostraListaAltoparlanti)
            menu.aggiungi("Volume", AddressOf GestisciVolumeAltoparlante)
            Dim disattivato As Boolean = WGAudio.AltoparlanteDisattivato()
            menu.aggiungi(If(disattivato, "Attiva altoparlante", "Disattiva altoparlante"), AddressOf CambiaStatoAltoparlante)
            menu.avvia("Altoparlanti", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAltoparlanti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuMicrofono()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Dispositivo audio predefinito", AddressOf MostraListaMicrofoni)
            menu.aggiungi("Volume", AddressOf GestisciVolumeMicrofono)
            Dim disattivato As Boolean = WGAudio.MicrofonoDisattivato()
            menu.aggiungi(If(disattivato, "Attiva microfono", "Disattiva microfono"), AddressOf CambiaStatoMicrofono)
            menu.avvia("Microfono", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuMicrofono eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private menuListaAltoparlanti As WGMenuLista

    Private Sub MostraListaAltoparlanti()
        Try
            menuListaAltoparlanti = New WGMenuLista()
            menuListaAltoparlanti.multiselezioneConsentita = False
            menuListaAltoparlanti.impostaTitolo("Scegli il dispositivo audio predefinito")
            Dim dispositivi As List(Of WGDispositivoAudio) = WGAudio.ElencaAltoparlanti()
            For Each dispositivo As WGDispositivoAudio In dispositivi
                menuListaAltoparlanti.aggiungiElemento(dispositivo.Nome, dispositivo.Id)
            Next
            AddHandler menuListaAltoparlanti.escPremuto, AddressOf ChiudiListaAltoparlanti
            AddHandler menuListaAltoparlanti.ElementoSelezionato, AddressOf AltoparlanteSelezionato
            Dim idPredefinito As String = WGAudio.AltoparlantePredefinitoId()
            If Not String.IsNullOrWhiteSpace(idPredefinito) Then menuListaAltoparlanti.posiziona(idPredefinito)
            menuListaAltoparlanti.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraListaAltoparlanti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaAltoparlanti()
        Try
            menuListaAltoparlanti.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiListaAltoparlanti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AltoparlanteSelezionato()
        Try
            Dim elementi As List(Of String) = menuListaAltoparlanti.ottieniElementi()
            If elementi.Count > 0 Then
                Dim elemento As String = elementi(0)
                WGAudio.ImpostaAltoparlantePredefinito(elemento)
                Dim msg As New WGMessaggi()
                msg.Messaggio("Dispositivo audio predefinito impostato.")
                msg.ShowDialog()
            End If
            menuListaAltoparlanti.abbandona()
        Catch ex As Exception
            logger.ScriviLog("AltoparlanteSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private menuListaMicrofoni As WGMenuLista

    Private Sub MostraListaMicrofoni()
        Try
            menuListaMicrofoni = New WGMenuLista()
            menuListaMicrofoni.multiselezioneConsentita = False
            menuListaMicrofoni.impostaTitolo("Scegli il dispositivo audio predefinito")
            Dim dispositivi As List(Of WGDispositivoAudio) = WGAudio.ElencaMicrofoni()
            For Each dispositivo As WGDispositivoAudio In dispositivi
                menuListaMicrofoni.aggiungiElemento(dispositivo.Nome, dispositivo.Id)
            Next
            AddHandler menuListaMicrofoni.escPremuto, AddressOf ChiudiListaMicrofoni
            AddHandler menuListaMicrofoni.ElementoSelezionato, AddressOf MicrofonoSelezionato
            Dim idPredefinito As String = WGAudio.MicrofonoPredefinitoId()
            If Not String.IsNullOrWhiteSpace(idPredefinito) Then menuListaMicrofoni.posiziona(idPredefinito)
            menuListaMicrofoni.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraListaMicrofoni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaMicrofoni()
        Try
            menuListaMicrofoni.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiListaMicrofoni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MicrofonoSelezionato()
        Try
            Dim elementi As List(Of String) = menuListaMicrofoni.ottieniElementi()
            If elementi.Count > 0 Then
                Dim elemento As String = elementi(0)
                WGAudio.ImpostaMicrofonoPredefinito(elemento)
                Dim msg As New WGMessaggi()
                msg.Messaggio("Dispositivo audio predefinito impostato.")
                msg.ShowDialog()
            End If
            menuListaMicrofoni.abbandona()
        Catch ex As Exception
            logger.ScriviLog("MicrofonoSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private menuVolumeAltoparlante As WGMenuLista
    Private _volumeAltoparlanteOriginale As Integer = -1

    Private Sub GestisciVolumeAltoparlante()
        Try
            menuVolumeAltoparlante = New WGMenuLista()
            menuVolumeAltoparlante.multiselezioneConsentita = False
            menuVolumeAltoparlante.impostaTitolo("Scegli il volume dell'altoparlante")
            menuVolumeAltoparlante.aggiungiElemento("0%", "0")
            menuVolumeAltoparlante.aggiungiElemento("10%", "10")
            menuVolumeAltoparlante.aggiungiElemento("20%", "20")
            menuVolumeAltoparlante.aggiungiElemento("30%", "30")
            menuVolumeAltoparlante.aggiungiElemento("40%", "40")
            menuVolumeAltoparlante.aggiungiElemento("50%", "50")
            menuVolumeAltoparlante.aggiungiElemento("60%", "60")
            menuVolumeAltoparlante.aggiungiElemento("70%", "70")
            menuVolumeAltoparlante.aggiungiElemento("80%", "80")
            menuVolumeAltoparlante.aggiungiElemento("90%", "90")
            menuVolumeAltoparlante.aggiungiElemento("100%", "100")
            AddHandler menuVolumeAltoparlante.escPremuto, AddressOf ChiudiMenuVolumeAltoparlante
            AddHandler menuVolumeAltoparlante.ElementoSelezionato, AddressOf VolumeAltoparlanteSelezionato

            Dim attuale As Integer = WGAudio.VolumeAltoparlante()
            If attuale < 0 Then attuale = 100
            _volumeAltoparlanteOriginale = attuale
            Dim percentualeArrotondata As Integer = CInt(Math.Round(attuale / 10.0)) * 10
            percentualeArrotondata = Math.Max(0, Math.Min(100, percentualeArrotondata))
            menuVolumeAltoparlante.posiziona(percentualeArrotondata.ToString())

            menuVolumeAltoparlante.aggiungiOpzione("Regolazione affinata", AddressOf OpzioneAffinataVolumeAltoparlante)

            AddHandler menuVolumeAltoparlante.VoceCambiata, AddressOf AnteprimaVolumeAltoparlante

            menuVolumeAltoparlante.avvia()
        Catch ex As Exception
            logger.ScriviLog("GestisciVolumeAltoparlante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OpzioneAffinataVolumeAltoparlante(sender As Object, e As EventArgs)
        Try
            RicaricaAffinata(menuVolumeAltoparlante, _volumeAltoparlanteOriginale)
        Catch ex As Exception
            logger.ScriviLog("OpzioneAffinataVolumeAltoparlante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuVolumeAltoparlante()
        Try
            If _volumeAltoparlanteOriginale >= 0 Then
                WGAudio.ImpostaVolumeAltoparlante(_volumeAltoparlanteOriginale)
                _volumeAltoparlanteOriginale = -1
            End If
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuVolumeAltoparlante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        menuVolumeAltoparlante.abbandona()
    End Sub

    Private Sub AnteprimaVolumeAltoparlante(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Return
            Dim percentuale As Integer = -1
            If Not Integer.TryParse(e.IdVoce, percentuale) Then Return
            WGAudio.ImpostaVolumeAltoparlante(percentuale)
        Catch ex As Exception
            logger.ScriviLog("AnteprimaVolumeAltoparlante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VolumeAltoparlanteSelezionato()
        Try
            Dim elementi As List(Of String) = menuVolumeAltoparlante.ottieniElementi()
            If elementi.Count > 0 Then
                Dim elemento As String = elementi(0)
                Dim percentuale As Integer = 100
                Integer.TryParse(elemento, percentuale)
                WGAudio.ImpostaVolumeAltoparlante(percentuale)
                _volumeAltoparlanteOriginale = -1
                Dim msg As New WGMessaggi()
                msg.Messaggio("Volume dell'altoparlante impostato al " & elemento & "%.")
                msg.ShowDialog()
            End If
            menuVolumeAltoparlante.abbandona()
        Catch ex As Exception
            logger.ScriviLog("VolumeAltoparlanteSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private menuVolumeMicrofono As WGMenuLista
    Private _volumeMicrofonoOriginale As Integer = -1

    Private Sub GestisciVolumeMicrofono()
        Try
            menuVolumeMicrofono = New WGMenuLista()
            menuVolumeMicrofono.multiselezioneConsentita = False
            menuVolumeMicrofono.impostaTitolo("Scegli il volume del microfono")
            menuVolumeMicrofono.aggiungiElemento("0%", "0")
            menuVolumeMicrofono.aggiungiElemento("10%", "10")
            menuVolumeMicrofono.aggiungiElemento("20%", "20")
            menuVolumeMicrofono.aggiungiElemento("30%", "30")
            menuVolumeMicrofono.aggiungiElemento("40%", "40")
            menuVolumeMicrofono.aggiungiElemento("50%", "50")
            menuVolumeMicrofono.aggiungiElemento("60%", "60")
            menuVolumeMicrofono.aggiungiElemento("70%", "70")
            menuVolumeMicrofono.aggiungiElemento("80%", "80")
            menuVolumeMicrofono.aggiungiElemento("90%", "90")
            menuVolumeMicrofono.aggiungiElemento("100%", "100")
            AddHandler menuVolumeMicrofono.escPremuto, AddressOf ChiudiMenuVolumeMicrofono
            AddHandler menuVolumeMicrofono.ElementoSelezionato, AddressOf VolumeMicrofonoSelezionato

            Dim attuale As Integer = WGAudio.VolumeMicrofono()
            If attuale < 0 Then attuale = 100
            _volumeMicrofonoOriginale = attuale
            Dim percentualeArrotondata As Integer = CInt(Math.Round(attuale / 10.0)) * 10
            percentualeArrotondata = Math.Max(0, Math.Min(100, percentualeArrotondata))
            menuVolumeMicrofono.posiziona(percentualeArrotondata.ToString())

            menuVolumeMicrofono.aggiungiOpzione("Regolazione affinata", AddressOf OpzioneAffinataVolumeMicrofono)

            AddHandler menuVolumeMicrofono.VoceCambiata, AddressOf AnteprimaVolumeMicrofono

            menuVolumeMicrofono.avvia()
        Catch ex As Exception
            logger.ScriviLog("GestisciVolumeMicrofono eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OpzioneAffinataVolumeMicrofono(sender As Object, e As EventArgs)
        Try
            RicaricaAffinata(menuVolumeMicrofono, _volumeMicrofonoOriginale)
        Catch ex As Exception
            logger.ScriviLog("OpzioneAffinataVolumeMicrofono eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuVolumeMicrofono()
        Try
            If _volumeMicrofonoOriginale >= 0 Then
                WGAudio.ImpostaVolumeMicrofono(_volumeMicrofonoOriginale)
                _volumeMicrofonoOriginale = -1
            End If
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuVolumeMicrofono eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        menuVolumeMicrofono.abbandona()
    End Sub

    Private Sub AnteprimaVolumeMicrofono(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Return
            Dim percentuale As Integer = -1
            If Not Integer.TryParse(e.IdVoce, percentuale) Then Return
            WGAudio.ImpostaVolumeMicrofono(percentuale)
        Catch ex As Exception
            logger.ScriviLog("AnteprimaVolumeMicrofono eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VolumeMicrofonoSelezionato()
        Try
            Dim elementi As List(Of String) = menuVolumeMicrofono.ottieniElementi()
            If elementi.Count > 0 Then
                Dim elemento As String = elementi(0)
                Dim percentuale As Integer = 100
                Integer.TryParse(elemento, percentuale)
                WGAudio.ImpostaVolumeMicrofono(percentuale)
                _volumeMicrofonoOriginale = -1
                Dim msg As New WGMessaggi()
                msg.Messaggio("Volume del microfono impostato al " & elemento & "%.")
                msg.ShowDialog()
            End If
            menuVolumeMicrofono.abbandona()
        Catch ex As Exception
            logger.ScriviLog("VolumeMicrofonoSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CambiaStatoAltoparlante()
        Try
            Dim disattivato As Boolean = WGAudio.AltoparlanteDisattivato()
            WGAudio.ImpostaAltoparlanteDisattivato(Not disattivato)
            Dim msg As New WGMessaggi()
            msg.Messaggio(If(disattivato, "Altoparlante attivato.", "Altoparlante disattivato."))
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CambiaStatoAltoparlante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CambiaStatoMicrofono()
        Try
            Dim disattivato As Boolean = WGAudio.MicrofonoDisattivato()
            WGAudio.ImpostaMicrofonoDisattivato(Not disattivato)
            Dim msg As New WGMessaggi()
            msg.Messaggio(If(disattivato, "Microfono attivato.", "Microfono disattivato."))
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CambiaStatoMicrofono eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuDigitazioneVocale()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Motore vocale predefinito", AddressOf MostraMenuMotoreVocalePredefinito)
            menu.avvia("Digitazione vocale", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuDigitazioneVocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuMotoreVocalePredefinito()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Gemini", AddressOf MostraMenuProviderGemini)
            menu.aggiungi("Groq", AddressOf MostraMenuProviderGroq)
            menu.aggiungi("Riconoscimento locale Parakeet, senza internet", AddressOf MostraMenuProviderLocale)
            Select Case WGDettatura.LeggiProviderPreferito()
                Case WGDettatura.ProviderDettatura.Groq
                    menu.posiziona(1)
                Case WGDettatura.ProviderDettatura.Locale
                    menu.posiziona(2)
                Case Else
                    menu.posiziona(0)
            End Select
            menu.avvia("Motore vocale predefinito", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuMotoreVocalePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Const VOCE_IMPOSTA_MOTORE_VOCE As String = "Imposta come motore predefinito"
    Private Const VOCE_RIMUOVI_MOTORE_VOCE As String = "Rimuovi impostazione come predefinito"
    Private _costruisciProviderVoce As Action

    Private Sub AggiungiVoceMotoreVoce(menu As WGMenu, aggiunte As List(Of String), testo As String, azione As Action)
        menu.aggiungi(testo, Sub(mittente As Object, e As EventArgs) azione.Invoke())
        aggiunte.Add(testo)
    End Sub

    Private Sub TogliVociMotoreVoce(menu As WGMenu, aggiunte As List(Of String))
        Try
            For Each voce As String In aggiunte
                menu.RimuoviVoce(voce)
            Next
            aggiunte.Clear()
        Catch ex As Exception
            logger.ScriviLog("TogliVociMotoreVoce eccezione: " & ex.Message)
        End Try
    End Sub

    ' Se il motore è già quello predefinito, la voce per impostarlo sparisce e compare quella per togliere l'impostazione
    ' (Gemini è il motore di base: per lui non c'è niente da togliere).
    Private Sub AggiungiVocePredefinitoVoce(menu As WGMenu, aggiunte As List(Of String), provider As WGDettatura.ProviderDettatura, imposta As Action)
        Try
            If WGDettatura.LeggiProviderPreferito() <> provider Then
                AggiungiVoceMotoreVoce(menu, aggiunte, VOCE_IMPOSTA_MOTORE_VOCE, imposta)
            ElseIf provider <> WGDettatura.ProviderDettatura.Gemini Then
                AggiungiVoceMotoreVoce(menu, aggiunte, VOCE_RIMUOVI_MOTORE_VOCE, AddressOf RimuoviMotoreVocePredefinito)
            End If
        Catch ex As Exception
            logger.ScriviLog("AggiungiVocePredefinitoVoce eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiornaMenuProviderVoce()
        Try
            If _costruisciProviderVoce IsNot Nothing Then _costruisciProviderVoce.Invoke()
        Catch ex As Exception
            logger.ScriviLog("AggiornaMenuProviderVoce eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RimuoviMotoreVocePredefinito()
        Try
            WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Gemini)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impostazione rimossa: il motore vocale predefinito torna a essere Gemini.")
            msg.ShowDialog()
            AggiornaMenuProviderVoce()
        Catch ex As Exception
            logger.ScriviLog("RimuoviMotoreVocePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuProviderGemini()
        Try
            Dim menu As New WGMenu()
            Dim aggiunte As New List(Of String)()
            Dim costruisci As Action = Sub()
                                           TogliVociMotoreVoce(menu, aggiunte)
                                           If WGDettatura.ChiaveApiGeminiConfigurata() Then
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Rimuovi chiave API", AddressOf RimuoviChiaveApiGemini)
                                               AggiungiVocePredefinitoVoce(menu, aggiunte, WGDettatura.ProviderDettatura.Gemini, AddressOf ImpostaGeminiComeMotorePredefinito)
                                           Else
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Imposta chiave API", AddressOf ImpostaChiaveApiGemini)
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Come ottenere la chiave API", AddressOf MostraInfoChiaveApiGemini)
                                               AggiungiVocePredefinitoVoce(menu, aggiunte, WGDettatura.ProviderDettatura.Gemini, AddressOf VerificaEImpostaGeminiComeMotorePredefinito)
                                           End If
                                       End Sub
            costruisci.Invoke()
            _costruisciProviderVoce = costruisci
            menu.avvia("Gemini", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuProviderGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuProviderGroq()
        Try
            Dim menu As New WGMenu()
            Dim aggiunte As New List(Of String)()
            Dim costruisci As Action = Sub()
                                           TogliVociMotoreVoce(menu, aggiunte)
                                           If WGDettatura.ChiaveApiGroqConfigurata() Then
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Rimuovi chiave API", AddressOf RimuoviChiaveApiGroq)
                                               AggiungiVocePredefinitoVoce(menu, aggiunte, WGDettatura.ProviderDettatura.Groq, AddressOf ImpostaGroqComeMotorePredefinito)
                                           Else
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Imposta chiave API", AddressOf ImpostaChiaveApiGroq)
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Come ottenere la chiave API", AddressOf MostraInfoChiaveApiGroq)
                                               AggiungiVocePredefinitoVoce(menu, aggiunte, WGDettatura.ProviderDettatura.Groq, AddressOf VerificaEImpostaGroqComeMotorePredefinito)
                                           End If
                                       End Sub
            costruisci.Invoke()
            _costruisciProviderVoce = costruisci
            menu.avvia("Groq", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuProviderGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuProviderLocale()
        Try
            Dim motivo As String = ""
            If Not WGTrascrittoreLocale.SistemaSupportato(motivo) Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("Il riconoscimento vocale locale non è disponibile su questo computer: " & motivo & ".")
                avviso.ShowDialog()
                Exit Sub
            End If

            If WGTrascrittoreLocale.VarianteScelta <> WGTrascrittoreLocale.VarianteModello.Standard Then WGTrascrittoreLocale.VarianteScelta = WGTrascrittoreLocale.VarianteModello.Standard
            Dim menu As New WGMenu()
            Dim aggiunte As New List(Of String)()
            Dim costruisci As Action = Sub()
                                           TogliVociMotoreVoce(menu, aggiunte)
                                           If WGTrascrittoreLocale.ModelloPresente() Then
                                               AggiungiVocePredefinitoVoce(menu, aggiunte, WGDettatura.ProviderDettatura.Locale, AddressOf ImpostaLocaleComeMotorePredefinito)
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Elimina il modello vocale locale", AddressOf EliminaModelloVocaleLocale)
                                           Else
                                               AggiungiVoceMotoreVoce(menu, aggiunte, "Scarica il modello vocale locale, " & WGTrascrittoreLocale.NomeVariante(WGTrascrittoreLocale.VarianteScelta) & ", " & WGTrascrittoreLocale.DESCRIZIONE_DIMENSIONE, AddressOf ScaricaModelloVocaleLocale)
                                               AggiungiVocePredefinitoVoce(menu, aggiunte, WGDettatura.ProviderDettatura.Locale, AddressOf ScaricaEImpostaLocaleComeMotorePredefinito)
                                           End If
                                           If WGTrascrittoreLocale.AltreVariantiPresenti() Then AggiungiVoceMotoreVoce(menu, aggiunte, "Elimina le versioni del modello non in uso", AddressOf EliminaVersioniModelloNonInUso)
                                       End Sub
            costruisci.Invoke()
            _costruisciProviderVoce = costruisci
            menu.avvia("Riconoscimento locale Parakeet", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuProviderLocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function VoceVersioneModello(variante As WGTrascrittoreLocale.VarianteModello, descrizione As String) As String
        Try
            Dim voce As String = WGTrascrittoreLocale.NomeVariante(variante) & ": " & descrizione
            If Not WGTrascrittoreLocale.MemoriaSufficiente(variante) Then
                voce &= ", richiede almeno " & WGTrascrittoreLocale.DescrizioneMemoriaMinima(variante) & " di memoria, non utilizzabile su questo computer"
            ElseIf WGTrascrittoreLocale.ModelloPresente(variante) Then
                voce &= ", già scaricato"
            Else
                voce &= ", scarica " & WGTrascrittoreLocale.DescrizioneVariante(variante)
            End If
            If WGTrascrittoreLocale.MemoriaSufficiente(variante) AndAlso Not WGTrascrittoreLocale.MemoriaConsigliataRaggiunta(variante) Then
                voce &= ", con meno di " & WGTrascrittoreLocale.DescrizioneMemoriaConsigliata(variante) & " di memoria può rallentare il computer"
            End If
            Return voce
        Catch ex As Exception
            logger.ScriviLog("VoceVersioneModello eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return WGTrascrittoreLocale.NomeVariante(variante)
        End Try
    End Function

    Private Sub MostraMenuVersioneModelloLocale()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi(VoceVersioneModello(WGTrascrittoreLocale.VarianteModello.Standard, "versione compressa originale di NVIDIA, la più leggera"), AddressOf ScegliVersioneModelloStandard)
            menu.aggiungi(VoceVersioneModello(WGTrascrittoreLocale.VarianteModello.Ultra, "versione compressa riaddestrata, sbaglia meno parole a parità di peso"), AddressOf ScegliVersioneModelloUltra)
            menu.aggiungi(VoceVersioneModello(WGTrascrittoreLocale.VarianteModello.Completo, "versione a precisione piena, la più precisa, più lenta e più pesante"), AddressOf ScegliVersioneModelloCompleto)
            menu.aggiungi(VoceVersioneModello(WGTrascrittoreLocale.VarianteModello.Cohere, "modello diverso da Parakeet, il più preciso in assoluto, fissato sull'italiano, il più lento e il più pesante"), AddressOf ScegliVersioneModelloCohere)
            menu.posiziona(CInt(WGTrascrittoreLocale.VarianteScelta))
            menu.avvia("Versione del modello vocale locale", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuVersioneModelloLocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliVersioneModelloStandard()
        ScegliVersioneModello(WGTrascrittoreLocale.VarianteModello.Standard)
    End Sub

    Private Sub ScegliVersioneModelloUltra()
        ScegliVersioneModello(WGTrascrittoreLocale.VarianteModello.Ultra)
    End Sub

    Private Sub ScegliVersioneModelloCompleto()
        ScegliVersioneModello(WGTrascrittoreLocale.VarianteModello.Completo)
    End Sub

    Private Sub ScegliVersioneModelloCohere()
        ScegliVersioneModello(WGTrascrittoreLocale.VarianteModello.Cohere)
    End Sub

    Private Sub ScegliVersioneModello(variante As WGTrascrittoreLocale.VarianteModello)
        Try
            If Not WGTrascrittoreLocale.MemoriaSufficiente(variante) Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("La versione " & WGTrascrittoreLocale.NomeVariante(variante) & " richiede almeno " & WGTrascrittoreLocale.DescrizioneMemoriaMinima(variante) & " di memoria: su questo computer non è utilizzabile.")
                avviso.ShowDialog()
                Exit Sub
            End If
            WGTrascrittoreLocale.VarianteScelta = variante
            If WGTrascrittoreLocale.ModelloPresente(variante) Then
                Dim msg As New WGMessaggi()
                Dim avvisoMemoria As String = If(WGTrascrittoreLocale.MemoriaConsigliataRaggiunta(variante), "", " Questo computer ha meno di " & WGTrascrittoreLocale.DescrizioneMemoriaConsigliata(variante) & " di memoria: se il riconoscimento diventa lento, scegliere una versione più leggera.")
                msg.Messaggio("Versione " & WGTrascrittoreLocale.NomeVariante(variante) & " attiva." & avvisoMemoria)
                msg.ShowDialog()
                Exit Sub
            End If
            ScaricamentoModelloLocale.Avvia(New Action(Of Boolean)(AddressOf ModelloVocaleLocaleScaricato))
        Catch ex As Exception
            logger.ScriviLog("ScegliVersioneModello eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EliminaVersioniModelloNonInUso()
        Try
            Dim eliminate As Boolean = WGTrascrittoreLocale.EliminaVariantiNonInUso()
            Dim msg As New WGMessaggi()
            msg.Messaggio(If(eliminate, "Versioni del modello non in uso eliminate.", "Non è stato possibile eliminare tutte le versioni del modello non in uso."))
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("EliminaVersioniModelloNonInUso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaLocaleComeMotorePredefinito()
        Try
            WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Locale)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Riconoscimento locale Parakeet impostato come motore vocale predefinito.")
            msg.ShowDialog()
            AggiornaMenuProviderVoce()
        Catch ex As Exception
            logger.ScriviLog("ImpostaLocaleComeMotorePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScaricaModelloVocaleLocale()
        Try
            ScaricamentoModelloLocale.Avvia(New Action(Of Boolean)(AddressOf ModelloVocaleLocaleScaricato))
        Catch ex As Exception
            logger.ScriviLog("ScaricaModelloVocaleLocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ModelloVocaleLocaleScaricato(riuscito As Boolean)
        Try
            If Not riuscito Then Exit Sub
            Dim msg As New WGMessaggi()
            msg.Messaggio("Modello vocale locale " & WGTrascrittoreLocale.NomeVariante(WGTrascrittoreLocale.VarianteScelta) & " scaricato e attivo.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ModelloVocaleLocaleScaricato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScaricaEImpostaLocaleComeMotorePredefinito()
        Try
            ScaricamentoModelloLocale.Avvia(New Action(Of Boolean)(AddressOf ModelloVocaleLocaleScaricatoEImpostato))
        Catch ex As Exception
            logger.ScriviLog("ScaricaEImpostaLocaleComeMotorePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ModelloVocaleLocaleScaricatoEImpostato(riuscito As Boolean)
        Try
            If Not riuscito Then Exit Sub
            WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Locale)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Modello vocale locale scaricato e impostato come motore vocale predefinito.")
            msg.ShowDialog()
            AggiornaMenuProviderVoce()
        Catch ex As Exception
            logger.ScriviLog("ModelloVocaleLocaleScaricatoEImpostato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EliminaModelloVocaleLocale()
        Try
            Dim eliminato As Boolean = WGTrascrittoreLocale.EliminaModello()
            If eliminato AndAlso WGDettatura.LeggiProviderPreferito() = WGDettatura.ProviderDettatura.Locale Then
                WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Gemini)
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio(If(eliminato, "Modello vocale locale eliminato.", "Non è stato possibile eliminare il modello vocale locale."))
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("EliminaModelloVocaleLocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaGeminiComeMotorePredefinito()
        Try
            WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Gemini)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Gemini impostato come motore vocale predefinito.")
            msg.ShowDialog()
            AggiornaMenuProviderVoce()
        Catch ex As Exception
            logger.ScriviLog("ImpostaGeminiComeMotorePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaGroqComeMotorePredefinito()
        Try
            WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Groq)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Groq impostato come motore vocale predefinito.")
            msg.ShowDialog()
            AggiornaMenuProviderVoce()
        Catch ex As Exception
            logger.ScriviLog("ImpostaGroqComeMotorePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _providerDaImpostarePredefinitoDopoChiave As WGDettatura.ProviderDettatura?
    Private _providerAiDaImpostarePredefinitoDopoChiave As CLSAi.ProviderAi?
    Private _domandaImpostaChiaveMotore As WGDomanda

    Private Sub VerificaEImpostaGeminiComeMotorePredefinito()
        Try
            If WGDettatura.ChiaveApiGeminiConfigurata() Then
                ImpostaGeminiComeMotorePredefinito()
            Else
                _providerDaImpostarePredefinitoDopoChiave = WGDettatura.ProviderDettatura.Gemini
                _domandaImpostaChiaveMotore = New WGDomanda()
                AddHandler _domandaImpostaChiaveMotore.rispostadomanda, AddressOf RispostaDomandaImpostaChiaveMotore
                _domandaImpostaChiaveMotore.avvia("Non hai ancora impostato una chiave API di Gemini: vuoi impostarne una adesso?", True)
            End If
        Catch ex As Exception
            logger.ScriviLog("VerificaEImpostaGeminiComeMotorePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VerificaEImpostaGroqComeMotorePredefinito()
        Try
            If WGDettatura.ChiaveApiGroqConfigurata() Then
                ImpostaGroqComeMotorePredefinito()
            Else
                _providerDaImpostarePredefinitoDopoChiave = WGDettatura.ProviderDettatura.Groq
                _domandaImpostaChiaveMotore = New WGDomanda()
                AddHandler _domandaImpostaChiaveMotore.rispostadomanda, AddressOf RispostaDomandaImpostaChiaveMotore
                _domandaImpostaChiaveMotore.avvia("Non hai ancora impostato una chiave API di Groq: vuoi impostarne una adesso?", True)
            End If
        Catch ex As Exception
            logger.ScriviLog("VerificaEImpostaGroqComeMotorePredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RispostaDomandaImpostaChiaveMotore(e As DialogResult)
        Try
            Try
                RemoveHandler _domandaImpostaChiaveMotore.rispostadomanda, AddressOf RispostaDomandaImpostaChiaveMotore
            Catch exRimozione As Exception
                logger.ScriviLog("RispostaDomandaImpostaChiaveMotore rimozione gestore eccezione: " & exRimozione.Message)
            End Try

            If e <> DialogResult.Yes Then
                _providerDaImpostarePredefinitoDopoChiave = Nothing
                _providerAiDaImpostarePredefinitoDopoChiave = Nothing
                Return
            End If

            If _providerDaImpostarePredefinitoDopoChiave.HasValue Then
                Select Case _providerDaImpostarePredefinitoDopoChiave
                    Case WGDettatura.ProviderDettatura.Gemini
                        ImpostaChiaveApiGemini()
                    Case WGDettatura.ProviderDettatura.Groq
                        ImpostaChiaveApiGroq()
                End Select
            ElseIf _providerAiDaImpostarePredefinitoDopoChiave.HasValue Then
                Select Case _providerAiDaImpostarePredefinitoDopoChiave
                    Case CLSAi.ProviderAi.Gemini
                        ImpostaChiaveApiGemini()
                    Case CLSAi.ProviderAi.Groq
                        ImpostaChiaveApiGroq()
                End Select
            End If
        Catch ex As Exception
            logger.ScriviLog("RispostaDomandaImpostaChiaveMotore eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _campoChiaveApiGemini As WGCampoTesto

    Private Sub ImpostaChiaveApiGemini()
        Try
            _campoChiaveApiGemini = New WGCampoTesto()
            _campoChiaveApiGemini.init("Chiave API di Gemini", WGDettatura.LeggiChiaveApiGemini(), False, False)
            AddHandler _campoChiaveApiGemini.chiusura, AddressOf OnChiusuraChiaveApiGemini
            _campoChiaveApiGemini.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaChiaveApiGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnChiusuraChiaveApiGemini(sender As Object, e As EventArgs)
        Try
            If _campoChiaveApiGemini IsNot Nothing Then
                Dim testoInserito As String = _campoChiaveApiGemini.OttieniTesto()
                Dim chiaveSalvata As String = If(testoInserito, "").Trim()
                WGDettatura.SalvaChiaveApiGemini(chiaveSalvata)
                RemoveHandler _campoChiaveApiGemini.chiusura, AddressOf OnChiusuraChiaveApiGemini
                _campoChiaveApiGemini = Nothing
                CompletaElaborazioneDopoChiave(AssistenteVocale.ElaborazioneComandi.Gemini, chiaveSalvata)

                If _providerDaImpostarePredefinitoDopoChiave = WGDettatura.ProviderDettatura.Gemini Then
                    _providerDaImpostarePredefinitoDopoChiave = Nothing
                    If Not String.IsNullOrWhiteSpace(chiaveSalvata) Then
                        WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Gemini)
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("Chiave API di Gemini salvata e impostata come motore vocale predefinito.")
                        msg.ShowDialog()
                    End If
                ElseIf _providerAiDaImpostarePredefinitoDopoChiave = CLSAi.ProviderAi.Gemini Then
                    _providerAiDaImpostarePredefinitoDopoChiave = Nothing
                    If Not String.IsNullOrWhiteSpace(chiaveSalvata) Then
                        CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Gemini)
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("Chiave API di Gemini salvata e impostata come motore AI predefinito.")
                        msg.ShowDialog()
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("OnChiusuraChiaveApiGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RimuoviChiaveApiGemini()
        Try
            WGDettatura.SalvaChiaveApiGemini("")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Chiave API di Gemini rimossa.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("RimuoviChiaveApiGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _campoChiaveApiGroq As WGCampoTesto

    Private Sub ImpostaChiaveApiGroq()
        Try
            _campoChiaveApiGroq = New WGCampoTesto()
            _campoChiaveApiGroq.init("Chiave API di Groq", WGDettatura.LeggiChiaveApiGroq(), False, False)
            AddHandler _campoChiaveApiGroq.chiusura, AddressOf OnChiusuraChiaveApiGroq
            _campoChiaveApiGroq.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaChiaveApiGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnChiusuraChiaveApiGroq(sender As Object, e As EventArgs)
        Try
            If _campoChiaveApiGroq IsNot Nothing Then
                Dim testoInserito As String = _campoChiaveApiGroq.OttieniTesto()
                Dim chiaveSalvata As String = If(testoInserito, "").Trim()
                WGDettatura.SalvaChiaveApiGroq(chiaveSalvata)
                RemoveHandler _campoChiaveApiGroq.chiusura, AddressOf OnChiusuraChiaveApiGroq
                _campoChiaveApiGroq = Nothing
                CompletaElaborazioneDopoChiave(AssistenteVocale.ElaborazioneComandi.Groq, chiaveSalvata)

                If _providerDaImpostarePredefinitoDopoChiave = WGDettatura.ProviderDettatura.Groq Then
                    _providerDaImpostarePredefinitoDopoChiave = Nothing
                    If Not String.IsNullOrWhiteSpace(chiaveSalvata) Then
                        WGDettatura.SalvaProviderPreferito(WGDettatura.ProviderDettatura.Groq)
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("Chiave API di Groq salvata e impostata come motore vocale predefinito.")
                        msg.ShowDialog()
                    End If
                ElseIf _providerAiDaImpostarePredefinitoDopoChiave = CLSAi.ProviderAi.Groq Then
                    _providerAiDaImpostarePredefinitoDopoChiave = Nothing
                    If Not String.IsNullOrWhiteSpace(chiaveSalvata) Then
                        CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Groq)
                        Dim msg As New WGMessaggi()
                        msg.Messaggio("Chiave API di Groq salvata e impostata come motore AI predefinito.")
                        msg.ShowDialog()
                    End If
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("OnChiusuraChiaveApiGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RimuoviChiaveApiGroq()
        Try
            WGDettatura.SalvaChiaveApiGroq("")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Chiave API di Groq rimossa.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("RimuoviChiaveApiGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _campoInfoChiaveGemini As WGCampo

    Private Sub MostraInfoChiaveApiGemini()
        Try
            _campoInfoChiaveGemini = New WGCampo()
            _campoInfoChiaveGemini.CaricaHtml("Come ottenere la chiave API di Gemini", CostruisciHtmlInfoChiaveGemini())
            AddHandler _campoInfoChiaveGemini.chiusura, AddressOf OnChiusuraInfoChiaveGemini
            _campoInfoChiaveGemini.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraInfoChiaveApiGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnChiusuraInfoChiaveGemini(sender As Object, e As EventArgs)
        Try
            If _campoInfoChiaveGemini IsNot Nothing Then
                RemoveHandler _campoInfoChiaveGemini.chiusura, AddressOf OnChiusuraInfoChiaveGemini
                _campoInfoChiaveGemini = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("OnChiusuraInfoChiaveGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function CostruisciHtmlInfoChiaveGemini() As String
        Return "<h3>Come ottenere una chiave API gratuita di Gemini</h3><ol><li>Vai su <a href=""https://aistudio.google.com/apikey"">aistudio.google.com/apikey</a>.</li><li>Accedi con un account Google, oppure creane uno se non lo possiedi gia'.</li><li>Nella pagina che si apre premi il pulsante per creare una nuova chiave API.</li><li>Copia la chiave generata.</li><li>Torna in questo menu, apri la voce ""Imposta chiave API"" e incolla la chiave nel campo.</li></ol><p>La chiave e' gratuita entro i limiti di utilizzo mensile stabiliti da Google.</p>"
    End Function

    Private _campoInfoChiaveGroq As WGCampo

    Private Sub MostraInfoChiaveApiGroq()
        Try
            _campoInfoChiaveGroq = New WGCampo()
            _campoInfoChiaveGroq.CaricaHtml("Come ottenere la chiave API di Groq", CostruisciHtmlInfoChiaveGroq())
            AddHandler _campoInfoChiaveGroq.chiusura, AddressOf OnChiusuraInfoChiaveGroq
            _campoInfoChiaveGroq.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraInfoChiaveApiGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub OnChiusuraInfoChiaveGroq(sender As Object, e As EventArgs)
        Try
            If _campoInfoChiaveGroq IsNot Nothing Then
                RemoveHandler _campoInfoChiaveGroq.chiusura, AddressOf OnChiusuraInfoChiaveGroq
                _campoInfoChiaveGroq = Nothing
            End If
        Catch ex As Exception
            logger.ScriviLog("OnChiusuraInfoChiaveGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function CostruisciHtmlInfoChiaveGroq() As String
        Return "<h3>Come ottenere una chiave API gratuita di Groq</h3><ol><li>Vai su <a href=""https://console.groq.com/keys"">console.groq.com/keys</a>.</li><li>Accedi con un account esistente oppure registrane uno nuovo.</li><li>Premi il pulsante per creare una nuova chiave API.</li><li>Copia la chiave generata: viene mostrata una sola volta.</li><li>Torna in questo menu, apri la voce ""Imposta chiave API"" e incolla la chiave nel campo.</li></ol><p>Il livello gratuito di Groq ha dei limiti di richieste al minuto e al giorno.</p>"
    End Function

    Private Sub MostraMenuIntelligenzaArtificiale()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Motore AI predefinito", AddressOf MostraMenuMotoreAiPredefinito)
            If AssistenteVocale.SistemaSupportato Then
                menu.aggiungi("Assistente vocale", AddressOf MostraMenuImpostazioniAssistenteVocale)
            End If
            menu.avvia("Intelligenza artificiale", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuIntelligenzaArtificiale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _menuImpostazioniAssistente As WGMenu
    Private _vociMenuAssistente As New Dictionary(Of String, String)

    Private Function TestiVociMenuAssistente() As Dictionary(Of String, String)
        Dim testi As New Dictionary(Of String, String)
        Try
            testi("parola") = "Parola di attivazione: " & AssistenteVocale.DescrizioneParola(AssistenteVocale.ParolaImpostata())
            If AssistenteVocale.ParolaImpostata() = AssistenteVocale.ParolaAttivazione.HeyJarvis Then
                testi("sensibilita") = "Sensibilità di Hey Jarvis: " & AssistenteVocale.DescrizioneSensibilita(AssistenteVocale.SensibilitaImpostata())
            End If
            testi("qualita") = "Motore di riconoscimento: " & AssistenteVocale.DescrizioneQualita(AssistenteVocale.QualitaImpostata())
            testi("motore") = "Motore vocale dell'assistente: " & AssistenteVocale.DescrizioneMotoreComandi(AssistenteVocale.MotoreLocaleScelto())
            testi("elaborazione") = "Elaborazione dei comandi: " & AssistenteVocale.DescrizioneElaborazione(AssistenteVocale.ElaborazioneImpostata())
            testi("scarica") = If(AssistenteVocale.ModelloPresente(), "Scarica di nuovo il modello vocale", "Scarica il modello vocale")
        Catch ex As Exception
            logger.ScriviLog("TestiVociMenuAssistente eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return testi
    End Function

    Private Sub MostraMenuImpostazioniAssistenteVocale()
        Try
            Dim testi As Dictionary(Of String, String) = TestiVociMenuAssistente()
            Dim menu As New WGMenu()
            menu.aggiungi("Attivazione", AddressOf MostraMenuAssistenteVocale)
            If testi.ContainsKey("parola") Then menu.aggiungi(testi("parola"), AddressOf MostraMenuParolaAttivazione)
            If testi.ContainsKey("sensibilita") Then menu.aggiungi(testi("sensibilita"), AddressOf MostraMenuSensibilitaJarvis)
            If testi.ContainsKey("qualita") Then menu.aggiungi(testi("qualita"), AddressOf MostraMenuMotoreVocaleLocale)
            If testi.ContainsKey("motore") Then menu.aggiungi(testi("motore"), AddressOf MostraMenuMotoreComandiLocale)
            If testi.ContainsKey("elaborazione") Then menu.aggiungi(testi("elaborazione"), AddressOf MostraMenuElaborazioneComandi)
            If testi.ContainsKey("scarica") Then menu.aggiungi(testi("scarica"), AddressOf ScaricaModelloAssistenteVocale)
            _vociMenuAssistente = testi
            _menuImpostazioniAssistente = menu
            AddHandler menu.MenuRiattivato, AddressOf MenuImpostazioniAssistenteRiattivato
            menu.avvia("Assistente vocale")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuImpostazioniAssistenteVocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MenuImpostazioniAssistenteRiattivato(sender As Object, e As EventArgs)
        Try
            Dim menu As WGMenu = _menuImpostazioniAssistente
            If menu Is Nothing Then Exit Sub
            Dim precedenti As Dictionary(Of String, String) = _vociMenuAssistente
            Dim nuovi As Dictionary(Of String, String) = TestiVociMenuAssistente()

            For Each chiave As String In New String() {"parola", "qualita", "motore", "elaborazione", "scarica"}
                If precedenti.ContainsKey(chiave) AndAlso nuovi.ContainsKey(chiave) AndAlso precedenti(chiave) <> nuovi(chiave) Then
                    menu.RinominaVoce(precedenti(chiave), nuovi(chiave))
                End If
            Next

            Dim presenteSensibilita As Boolean = precedenti.ContainsKey("sensibilita")
            Dim richiestaSensibilita As Boolean = nuovi.ContainsKey("sensibilita")
            If presenteSensibilita AndAlso richiestaSensibilita Then
                If precedenti("sensibilita") <> nuovi("sensibilita") Then menu.RinominaVoce(precedenti("sensibilita"), nuovi("sensibilita"))
            ElseIf richiestaSensibilita AndAlso nuovi.ContainsKey("parola") Then
                menu.AggiungiDopo(nuovi("parola"), nuovi("sensibilita"), AddressOf MostraMenuSensibilitaJarvis)
            ElseIf presenteSensibilita Then
                menu.RimuoviVoce(precedenti("sensibilita"))
            End If

            _vociMenuAssistente = nuovi
        Catch ex As Exception
            logger.ScriviLog("MenuImpostazioniAssistenteRiattivato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAssistenteVocale()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attivo", AddressOf AttivaAssistenteVocale)
            menu.aggiungi("Disattivo", AddressOf DisattivaAssistenteVocale)
            menu.posiziona(If(AssistenteVocale.Attivo(), 0, 1))
            menu.avvia("Attivazione dell'assistente vocale", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAssistenteVocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuMotoreMinerva()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Vosk: usa il modello scelto in Motore di riconoscimento, in bassa o in alta qualità", AddressOf ImpostaMinervaVosk)
            If WGTrascrittoreLocale.ModelloPresente() Then
                menu.aggiungi("Parakeet: più preciso, usa più processore e memoria, già scaricato", AddressOf ImpostaMinervaParakeet)
            Else
                menu.aggiungi("Parakeet: più preciso, usa più processore e memoria, scarica " & WGTrascrittoreLocale.DESCRIZIONE_DIMENSIONE, AddressOf ImpostaMinervaParakeet)
            End If
            menu.posiziona(If(AssistenteVocale.MotoreMinervaImpostato() = AssistenteVocale.MotoreMinerva.Parakeet, 1, 0))
            menu.avvia("Motore per Ehi Minerva", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuMotoreMinerva eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaMinervaVosk()
        Try
            AssistenteVocale.ImpostaMotoreMinervaDaImpostazioni(AssistenteVocale.MotoreMinerva.Vosk)
        Catch ex As Exception
            logger.ScriviLog("ImpostaMinervaVosk eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaMinervaParakeet()
        Try
            AssistenteVocale.ImpostaMotoreMinervaDaImpostazioni(AssistenteVocale.MotoreMinerva.Parakeet)
        Catch ex As Exception
            logger.ScriviLog("ImpostaMinervaParakeet eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _elaborazioneAssistenteDopoChiave As AssistenteVocale.ElaborazioneComandi?
    Private _domandaChiaveElaborazione As WGDomanda

    Private Sub MostraMenuElaborazioneComandi()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Gemini, online: ascolta direttamente l'audio ed esegue i comandi, richiede la chiave API di Gemini", AddressOf ElaborazioneConGemini)
            menu.aggiungi("Groq, online: trascrive l'audio e poi interpreta la richiesta, richiede la chiave API di Groq", AddressOf ElaborazioneConGroq)
            menu.aggiungi("Sul computer, senza internet: solo i comandi del programma, con il motore scelto in Motore vocale dell'assistente", AddressOf ElaborazioneSulComputer)
            Select Case AssistenteVocale.ElaborazioneImpostata()
                Case AssistenteVocale.ElaborazioneComandi.Groq
                    menu.posiziona(1)
                Case AssistenteVocale.ElaborazioneComandi.Locale
                    menu.posiziona(2)
                Case Else
                    menu.posiziona(0)
            End Select
            menu.avvia("Elaborazione dei comandi", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuElaborazioneComandi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ElaborazioneConGemini()
        Try
            If WGDettatura.ChiaveApiGeminiConfigurata() Then
                AssistenteVocale.ElaborazioneImpostataDaImpostazioni(AssistenteVocale.ElaborazioneComandi.Gemini)
                Exit Sub
            End If
            ChiediChiavePerElaborazione(AssistenteVocale.ElaborazioneComandi.Gemini, "Non hai ancora impostato una chiave API di Gemini: vuoi impostarne una adesso?")
        Catch ex As Exception
            logger.ScriviLog("ElaborazioneConGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ElaborazioneConGroq()
        Try
            If WGDettatura.ChiaveApiGroqConfigurata() Then
                AssistenteVocale.ElaborazioneImpostataDaImpostazioni(AssistenteVocale.ElaborazioneComandi.Groq)
                Exit Sub
            End If
            ChiediChiavePerElaborazione(AssistenteVocale.ElaborazioneComandi.Groq, "Non hai ancora impostato una chiave API di Groq: vuoi impostarne una adesso?")
        Catch ex As Exception
            logger.ScriviLog("ElaborazioneConGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ElaborazioneSulComputer()
        Try
            AssistenteVocale.ImpostaElaborazioneLocaleDaImpostazioni()
        Catch ex As Exception
            logger.ScriviLog("ElaborazioneSulComputer eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediChiavePerElaborazione(elaborazione As AssistenteVocale.ElaborazioneComandi, domanda As String)
        Try
            _elaborazioneAssistenteDopoChiave = elaborazione
            _domandaChiaveElaborazione = New WGDomanda()
            AddHandler _domandaChiaveElaborazione.rispostadomanda, AddressOf RispostaChiavePerElaborazione
            _domandaChiaveElaborazione.avvia(domanda, True)
        Catch ex As Exception
            logger.ScriviLog("ChiediChiavePerElaborazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RispostaChiavePerElaborazione(e As DialogResult)
        Try
            Try
                RemoveHandler _domandaChiaveElaborazione.rispostadomanda, AddressOf RispostaChiavePerElaborazione
            Catch exRimozione As Exception
                logger.ScriviLog("RispostaChiavePerElaborazione rimozione gestore eccezione: " & exRimozione.Message)
            End Try

            If e <> DialogResult.Yes OrElse Not _elaborazioneAssistenteDopoChiave.HasValue Then
                _elaborazioneAssistenteDopoChiave = Nothing
                Exit Sub
            End If

            If _elaborazioneAssistenteDopoChiave.Value = AssistenteVocale.ElaborazioneComandi.Groq Then
                ImpostaChiaveApiGroq()
            Else
                ImpostaChiaveApiGemini()
            End If
        Catch ex As Exception
            logger.ScriviLog("RispostaChiavePerElaborazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CompletaElaborazioneDopoChiave(provider As AssistenteVocale.ElaborazioneComandi, chiaveSalvata As String)
        Try
            If Not _elaborazioneAssistenteDopoChiave.HasValue OrElse _elaborazioneAssistenteDopoChiave.Value <> provider Then Exit Sub
            _elaborazioneAssistenteDopoChiave = Nothing
            If String.IsNullOrWhiteSpace(chiaveSalvata) Then Exit Sub
            AssistenteVocale.ElaborazioneImpostataDaImpostazioni(provider)
        Catch ex As Exception
            logger.ScriviLog("CompletaElaborazioneDopoChiave eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuMotoreComandiLocale()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Vosk: modello leggero nella qualità scelta in Motore di riconoscimento, meno preciso", AddressOf ImpostaComandiLocaliVosk)
            If WGTrascrittoreLocale.ModelloPresente() Then
                menu.aggiungi("Parakeet: riconoscimento di alta qualità, " & WGTrascrittoreLocale.NomeVariante(WGTrascrittoreLocale.VarianteScelta) & ", già scaricato", AddressOf ImpostaComandiLocaliParakeet)
            Else
                menu.aggiungi("Parakeet: riconoscimento di alta qualità, " & WGTrascrittoreLocale.NomeVariante(WGTrascrittoreLocale.VarianteScelta) & ", scarica " & WGTrascrittoreLocale.DESCRIZIONE_DIMENSIONE & ", richiede almeno 4 nuclei e 6 gigabyte di memoria", AddressOf ImpostaComandiLocaliParakeet)
            End If
            menu.posiziona(If(AssistenteVocale.MotoreLocaleScelto() = AssistenteVocale.MotoreComandiLocale.Parakeet, 1, 0))
            menu.avvia("Motore vocale dell'assistente", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuMotoreComandiLocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaComandiLocaliVosk()
        Try
            AssistenteVocale.ImpostaMotoreComandiDaImpostazioni(AssistenteVocale.MotoreComandiLocale.Vosk)
        Catch ex As Exception
            logger.ScriviLog("ImpostaComandiLocaliVosk eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaComandiLocaliParakeet()
        Try
            AssistenteVocale.ImpostaMotoreComandiDaImpostazioni(AssistenteVocale.MotoreComandiLocale.Parakeet)
        Catch ex As Exception
            logger.ScriviLog("ImpostaComandiLocaliParakeet eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuParolaAttivazione()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Hey Jarvis: motore dedicato openWakeWord, il più preciso, si pronuncia all'inglese, scarica circa 4 megabyte", AddressOf ImpostaParolaHeyJarvis)
            menu.aggiungi("Ehi Minerva: parola italiana riconosciuta dal modello vocale Vosk, meno precisa, richiede il modello vocale scaricato", AddressOf ImpostaParolaEhiMinerva)
            menu.posiziona(If(AssistenteVocale.ParolaImpostata() = AssistenteVocale.ParolaAttivazione.EhiMinerva, 1, 0))
            menu.avvia("Parola di attivazione", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuParolaAttivazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuSensibilitaJarvis()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Alta: accetta punteggi a partire da 0,15 se si ripetono su due blocchi audio di fila, riconosce anche le pronunce meno nette con qualche attivazione involontaria in più", AddressOf ImpostaSensibilitaJarvisAlta)
            menu.aggiungi("Media: equilibrio tra riconoscimento e attivazioni involontarie, consigliata", AddressOf ImpostaSensibilitaJarvisMedia)
            menu.aggiungi("Bassa: si attiva solo con una pronuncia molto chiara", AddressOf ImpostaSensibilitaJarvisBassa)
            Select Case AssistenteVocale.SensibilitaImpostata()
                Case AssistenteVocale.SensibilitaParola.Alta
                    menu.posiziona(0)
                Case AssistenteVocale.SensibilitaParola.Bassa
                    menu.posiziona(2)
                Case Else
                    menu.posiziona(1)
            End Select
            menu.avvia("Sensibilità di Hey Jarvis", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSensibilitaJarvis eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaSensibilitaJarvisAlta()
        Try
            AssistenteVocale.ImpostaSensibilitaDaImpostazioni(AssistenteVocale.SensibilitaParola.Alta)
        Catch ex As Exception
            logger.ScriviLog("ImpostaSensibilitaJarvisAlta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaSensibilitaJarvisMedia()
        Try
            AssistenteVocale.ImpostaSensibilitaDaImpostazioni(AssistenteVocale.SensibilitaParola.Media)
        Catch ex As Exception
            logger.ScriviLog("ImpostaSensibilitaJarvisMedia eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaSensibilitaJarvisBassa()
        Try
            AssistenteVocale.ImpostaSensibilitaDaImpostazioni(AssistenteVocale.SensibilitaParola.Bassa)
        Catch ex As Exception
            logger.ScriviLog("ImpostaSensibilitaJarvisBassa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaParolaHeyJarvis()
        Try
            AssistenteVocale.ImpostaParolaDaImpostazioni(AssistenteVocale.ParolaAttivazione.HeyJarvis)
        Catch ex As Exception
            logger.ScriviLog("ImpostaParolaHeyJarvis eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaParolaEhiMinerva()
        Try
            AssistenteVocale.ImpostaParolaDaImpostazioni(AssistenteVocale.ParolaAttivazione.EhiMinerva)
        Catch ex As Exception
            logger.ScriviLog("ImpostaParolaEhiMinerva eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuMotoreVocaleLocale()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Bassa qualità: modello leggero, circa 60 megabyte, per la parola di attivazione Ehi Minerva e per i comandi senza internet, adatto alla maggior parte dei computer", AddressOf ImpostaMotoreVocaleBassaQualita)
            menu.aggiungi("Alta qualità: modello completo, circa 1,2 gigabyte, per la parola di attivazione Ehi Minerva e per i comandi senza internet, richiede almeno 8 gigabyte di memoria e un processore a 4 nuclei", AddressOf ImpostaMotoreVocaleAltaQualita)
            menu.posiziona(If(AssistenteVocale.QualitaImpostata() = AssistenteVocale.QualitaVosk.Alta, 1, 0))
            menu.avvia("Motore di riconoscimento", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuMotoreVocaleLocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaMotoreVocaleBassaQualita()
        Try
            AssistenteVocale.ImpostaQualitaDaImpostazioni(AssistenteVocale.QualitaVosk.Bassa)
        Catch ex As Exception
            logger.ScriviLog("ImpostaMotoreVocaleBassaQualita eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaMotoreVocaleAltaQualita()
        Try
            AssistenteVocale.ImpostaQualitaDaImpostazioni(AssistenteVocale.QualitaVosk.Alta)
        Catch ex As Exception
            logger.ScriviLog("ImpostaMotoreVocaleAltaQualita eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaAssistenteVocale()
        Try
            AssistenteVocale.AttivaDaImpostazioni()
        Catch ex As Exception
            logger.ScriviLog("AttivaAssistenteVocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaAssistenteVocale()
        Try
            AssistenteVocale.ImpostaAttivo(False)
            AssistenteVocale.Ferma()

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Assistente vocale disattivato.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaAssistenteVocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScaricaModelloAssistenteVocale()
        Try
            AssistenteVocale.ScaricaModello()
        Catch ex As Exception
            logger.ScriviLog("ScaricaModelloAssistenteVocale eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuMotoreAiPredefinito()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Gemini", AddressOf MostraMenuProviderAiGemini)
            menu.aggiungi("Groq", AddressOf MostraMenuProviderAiGroq)
            menu.aggiungi("OVHcloud, modelli europei", AddressOf MostraMenuProviderAiOvh)
            menu.aggiungi("AI Horde, rete di volontari", AddressOf MostraMenuProviderAiHorde)
            menu.avvia("Motore AI predefinito", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Const VOCE_IMPOSTA_MOTORE_AI As String = "Imposta come motore AI predefinito"
    Private Const VOCE_RIMUOVI_MOTORE_AI As String = "Rimuovi impostazione come predefinito"
    Private _costruisciProviderAi As Action

    Private Sub TogliVociMotoreAi(menu As WGMenu)
        Try
            For Each voce As String In New String() {VOCE_IMPOSTA_MOTORE_AI, VOCE_RIMUOVI_MOTORE_AI, "Rimuovi chiave API", "Imposta chiave API", "Come ottenere la chiave API", "Modello predefinito", "Non usare più OVHcloud", "Non usare più AI Horde"}
                menu.RimuoviVoce(voce)
            Next
        Catch ex As Exception
            logger.ScriviLog("TogliVociMotoreAi eccezione: " & ex.Message)
        End Try
    End Sub

    ' Se il motore è già quello predefinito, la voce per impostarlo sparisce e compare quella per togliere l'impostazione
    ' (Gemini è il motore di base: per lui non c'è niente da togliere).
    Private Sub AggiungiVocePredefinitoAi(menu As WGMenu, provider As CLSAi.ProviderAi, imposta As Action)
        Try
            If CLSAi.LeggiProviderPreferito() <> provider Then
                menu.aggiungi(VOCE_IMPOSTA_MOTORE_AI, Sub(mittente As Object, e As EventArgs) imposta.Invoke())
            ElseIf provider <> CLSAi.ProviderAi.Gemini Then
                menu.aggiungi(VOCE_RIMUOVI_MOTORE_AI, AddressOf RimuoviMotoreAiPredefinito)
            End If
        Catch ex As Exception
            logger.ScriviLog("AggiungiVocePredefinitoAi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiornaMenuProviderAi()
        Try
            If _costruisciProviderAi IsNot Nothing Then _costruisciProviderAi.Invoke()
        Catch ex As Exception
            logger.ScriviLog("AggiornaMenuProviderAi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RimuoviMotoreAiPredefinito()
        Try
            CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Gemini)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Impostazione rimossa: il motore AI predefinito torna a essere Gemini.")
            msg.ShowDialog()
            AggiornaMenuProviderAi()
        Catch ex As Exception
            logger.ScriviLog("RimuoviMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuProviderAiGemini()
        Try
            Dim menu As New WGMenu()
            Dim costruisci As Action = Sub()
                                           TogliVociMotoreAi(menu)
                                           If WGDettatura.ChiaveApiGeminiConfigurata() Then
                                               menu.aggiungi("Rimuovi chiave API", AddressOf RimuoviChiaveApiGemini)
                                               AggiungiVocePredefinitoAi(menu, CLSAi.ProviderAi.Gemini, AddressOf ImpostaGeminiComeMotoreAiPredefinito)
                                               menu.aggiungi("Modello predefinito", AddressOf MostraMenuModelloAiGemini)
                                           Else
                                               menu.aggiungi("Imposta chiave API", AddressOf ImpostaChiaveApiGemini)
                                               menu.aggiungi("Come ottenere la chiave API", AddressOf MostraInfoChiaveApiGemini)
                                               AggiungiVocePredefinitoAi(menu, CLSAi.ProviderAi.Gemini, AddressOf VerificaEImpostaGeminiComeMotoreAiPredefinito)
                                           End If
                                       End Sub
            costruisci.Invoke()
            _costruisciProviderAi = costruisci
            menu.avvia("Gemini", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuProviderAiGemini eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuProviderAiGroq()
        Try
            Dim menu As New WGMenu()
            Dim costruisci As Action = Sub()
                                           TogliVociMotoreAi(menu)
                                           If WGDettatura.ChiaveApiGroqConfigurata() Then
                                               menu.aggiungi("Rimuovi chiave API", AddressOf RimuoviChiaveApiGroq)
                                               AggiungiVocePredefinitoAi(menu, CLSAi.ProviderAi.Groq, AddressOf ImpostaGroqComeMotoreAiPredefinito)
                                               menu.aggiungi("Modello predefinito", AddressOf MostraMenuModelloAiGroq)
                                           Else
                                               menu.aggiungi("Imposta chiave API", AddressOf ImpostaChiaveApiGroq)
                                               menu.aggiungi("Come ottenere la chiave API", AddressOf MostraInfoChiaveApiGroq)
                                               AggiungiVocePredefinitoAi(menu, CLSAi.ProviderAi.Groq, AddressOf VerificaEImpostaGroqComeMotoreAiPredefinito)
                                           End If
                                       End Sub
            costruisci.Invoke()
            _costruisciProviderAi = costruisci
            menu.avvia("Groq", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuProviderAiGroq eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' OVHcloud AI Endpoints: API gratuita, senza chiave né registrazione, con i modelli europei (Mistral e simili) ospitati in Europa.
    ' Ha un limite di richieste al minuto. I testi vi vengono mandati solo dopo che l'utente ha scelto di usarlo.
    Private Sub MostraMenuProviderAiOvh()
        Try
            Dim menu As New WGMenu()
            Dim costruisci As Action = Sub()
                                           TogliVociMotoreAi(menu)
                                           AggiungiVocePredefinitoAi(menu, CLSAi.ProviderAi.Ovh, AddressOf ImpostaOvhComeMotoreAiPredefinito)
                                           menu.aggiungi("Modello predefinito", AddressOf MostraMenuModelloAiOvh)
                                           If WGDettatura.OvhAttivo() Then menu.aggiungi("Non usare più OVHcloud", AddressOf DisattivaOvh)
                                       End Sub
            costruisci.Invoke()
            _costruisciProviderAi = costruisci
            menu.avvia("OVHcloud, modelli europei", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuProviderAiOvh eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaOvh()
        Try
            WGDettatura.ImpostaOvhAttivo(False)
            If CLSAi.LeggiProviderPreferito() = CLSAi.ProviderAi.Ovh Then CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Gemini)
            Dim msg As New WGMessaggi()
            msg.Messaggio("OVHcloud non verrà più usato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaOvh eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaOvhComeMotoreAiPredefinito()
        Try
            WGDettatura.ImpostaOvhAttivo(True)
            CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Ovh)
            Dim msg As New WGMessaggi()
            msg.Messaggio("OVHcloud impostato come motore AI predefinito. Non serve nessuna chiave. Il limite gratuito è di poche richieste al minuto.")
            msg.ShowDialog()
            AggiornaMenuProviderAi()
        Catch ex As Exception
            logger.ScriviLog("ImpostaOvhComeMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub MostraMenuModelloAiOvh()
        Try
            Await MostraMenuModelloAi(CLSAi.ProviderAi.Ovh)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuModelloAiOvh eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' AI Horde: rete di volontari che mettono a disposizione i propri computer. API gratuita, usata con la chiave anonima.
    ' I modelli cambiano di continuo e le risposte possono farsi attendere. I testi vi vengono mandati solo dopo che l'utente ha scelto di usarlo.
    Private Sub MostraMenuProviderAiHorde()
        Try
            Dim menu As New WGMenu()
            Dim costruisci As Action = Sub()
                                           TogliVociMotoreAi(menu)
                                           AggiungiVocePredefinitoAi(menu, CLSAi.ProviderAi.Horde, AddressOf ImpostaHordeComeMotoreAiPredefinito)
                                           menu.aggiungi("Modello predefinito", AddressOf MostraMenuModelloAiHorde)
                                           If WGDettatura.HordeAttivo() Then menu.aggiungi("Non usare più AI Horde", AddressOf DisattivaHorde)
                                       End Sub
            costruisci.Invoke()
            _costruisciProviderAi = costruisci
            menu.avvia("AI Horde, rete di volontari", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuProviderAiHorde eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaHorde()
        Try
            WGDettatura.ImpostaHordeAttivo(False)
            If CLSAi.LeggiProviderPreferito() = CLSAi.ProviderAi.Horde Then CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Gemini)
            Dim msg As New WGMessaggi()
            msg.Messaggio("AI Horde non verrà più usato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaHorde eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaHordeComeMotoreAiPredefinito()
        Try
            WGDettatura.ImpostaHordeAttivo(True)
            CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Horde)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Motore AI impostato correttamente.")
            msg.ShowDialog()
            AggiornaMenuProviderAi()
        Catch ex As Exception
            logger.ScriviLog("ImpostaHordeComeMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Sub MostraMenuModelloAiHorde()
        Try
            Await MostraMenuModelloAi(CLSAi.ProviderAi.Horde)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuModelloAiHorde eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaGeminiComeMotoreAiPredefinito()
        Try
            CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Gemini)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Gemini impostato come motore AI predefinito.")
            msg.ShowDialog()
            AggiornaMenuProviderAi()
        Catch ex As Exception
            logger.ScriviLog("ImpostaGeminiComeMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaGroqComeMotoreAiPredefinito()
        Try
            CLSAi.SalvaProviderPreferito(CLSAi.ProviderAi.Groq)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Groq impostato come motore AI predefinito.")
            msg.ShowDialog()
            AggiornaMenuProviderAi()
        Catch ex As Exception
            logger.ScriviLog("ImpostaGroqComeMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VerificaEImpostaGeminiComeMotoreAiPredefinito()
        Try
            If WGDettatura.ChiaveApiGeminiConfigurata() Then
                ImpostaGeminiComeMotoreAiPredefinito()
            Else
                _providerAiDaImpostarePredefinitoDopoChiave = CLSAi.ProviderAi.Gemini
                _domandaImpostaChiaveMotore = New WGDomanda()
                AddHandler _domandaImpostaChiaveMotore.rispostadomanda, AddressOf RispostaDomandaImpostaChiaveMotore
                _domandaImpostaChiaveMotore.avvia("Non hai ancora impostato una chiave API di Gemini: vuoi impostarne una adesso?", True)
            End If
        Catch ex As Exception
            logger.ScriviLog("VerificaEImpostaGeminiComeMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub VerificaEImpostaGroqComeMotoreAiPredefinito()
        Try
            If WGDettatura.ChiaveApiGroqConfigurata() Then
                ImpostaGroqComeMotoreAiPredefinito()
            Else
                _providerAiDaImpostarePredefinitoDopoChiave = CLSAi.ProviderAi.Groq
                _domandaImpostaChiaveMotore = New WGDomanda()
                AddHandler _domandaImpostaChiaveMotore.rispostadomanda, AddressOf RispostaDomandaImpostaChiaveMotore
                _domandaImpostaChiaveMotore.avvia("Non hai ancora impostato una chiave API di Groq: vuoi impostarne una adesso?", True)
            End If
        Catch ex As Exception
            logger.ScriviLog("VerificaEImpostaGroqComeMotoreAiPredefinito eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _menuModelloAi As WGMenuLista
    Private _providerModelloAiCorrente As CLSAi.ProviderAi

    Private Async Sub MostraMenuModelloAiGemini()
        Await MostraMenuModelloAi(CLSAi.ProviderAi.Gemini)
    End Sub

    Private Async Sub MostraMenuModelloAiGroq()
        Await MostraMenuModelloAi(CLSAi.ProviderAi.Groq)
    End Sub

    Private Async Function MostraMenuModelloAi(provider As CLSAi.ProviderAi) As Task
        Try
            Dim modelli As List(Of String) = Await CLSAi.ElencaModelliAsync(provider)
            If modelli Is Nothing OrElse modelli.Count = 0 Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Impossibile recuperare l'elenco dei modelli disponibili.")
                msgErrore.ShowDialog()
                Return
            End If

            _providerModelloAiCorrente = provider
            _menuModelloAi = New WGMenuLista()
            _menuModelloAi.multiselezioneConsentita = False
            _menuModelloAi.impostaTitolo("Scegli il modello predefinito")
            For Each modello As String In modelli
                _menuModelloAi.aggiungiElemento(modello, modello)
            Next

            Dim modelloAttuale As String = CLSAi.LeggiModelloPredefinito(provider)
            If Not String.IsNullOrWhiteSpace(modelloAttuale) Then
                Try
                    _menuModelloAi.posiziona(modelloAttuale)
                Catch exPosiziona As Exception
                    logger.ScriviLog("MostraMenuModelloAi posiziona eccezione: " & exPosiziona.Message)
                End Try
            End If

            AddHandler _menuModelloAi.escPremuto, AddressOf ChiudiMenuModelloAi
            AddHandler _menuModelloAi.ElementoSelezionato, AddressOf ModelloAiSelezionato
            _menuModelloAi.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraMenuModelloAi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Function

    Private Sub ChiudiMenuModelloAi()
        Try
            _menuModelloAi.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuModelloAi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ModelloAiSelezionato()
        Try
            Dim elementi As List(Of String) = _menuModelloAi.ottieniElementi()
            If elementi.Count > 0 Then
                CLSAi.SalvaModelloPredefinito(_providerModelloAiCorrente, elementi(0))
                Dim msg As New WGMessaggi()
                msg.Messaggio("Modello predefinito impostato su " & elementi(0) & ".")
                msg.ShowDialog()
            End If
            _menuModelloAi.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ModelloAiSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaTouch()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed OrElse Not contenitore.IsHandleCreated Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Impossibile attivare il touch: la finestra principale non è pronta.")
                msgErrore.ShowDialog()
                Return
            End If
            ApplicaAdattamentoTouchSalvato()
            WGTouk.GetInstance().Attiva(contenitore.Handle)
            SalvaConfig(ID_CONFIG_TOUCH_ATTIVO, "true")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Touch attivato e salvato nella configurazione.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaTouch eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaTouch()
        Try
            WGTouk.GetInstance().Disattiva()
            SalvaConfig(ID_CONFIG_TOUCH_ATTIVO, "false")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Touch disattivato e salvato nella configurazione.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaTouch eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuMouse()
        Try
            Dim statoAttuale As Boolean = WGUtils.WGMouseLock.MouseDisattivato
            Dim menuMouse As New WGMenu()
            menuMouse.aggiungi("Attiva blocco mouse", AddressOf AttivaBloccoMouse)
            menuMouse.aggiungi("Disattiva blocco mouse", AddressOf DisattivaBloccoMouse)
            Dim statoTesto As String = If(statoAttuale, "attivo", "disattivo")
            menuMouse.aggiungi("Stato attuale: blocco mouse " & statoTesto, Sub() MostraMessaggioStato("Il blocco mouse è attualmente " & statoTesto & "."))
            menuMouse.posiziona(If(statoAttuale, 0, 1))
            menuMouse.avvia("Impostazioni mouse", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuMouse eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaBloccoMouse()
        Try
            WGUtils.WGMouseLock.AttivaMouse()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Blocco mouse attivato e salvato nella configurazione.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaBloccoMouse eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaBloccoMouse()
        Try
            WGUtils.WGMouseLock.DisattivaMouse()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Blocco mouse disattivato e salvato nella configurazione.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaBloccoMouse eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMessaggioStato(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("MostraMessaggioStato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RicaricaAffinata(lista As WGMenuLista, valoreAttuale As Integer)
        Try
            lista.pulisci()
            For i As Integer = 0 To 100
                lista.aggiungiElemento(i.ToString() & "%", i.ToString())
            Next
            If valoreAttuale >= 0 AndAlso valoreAttuale <= 100 Then
                lista.posiziona(valoreAttuale.ToString())
            End If
        Catch ex As Exception
            logger.ScriviLog("RicaricaAffinata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuGiochiRete()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Online o offline", AddressOf MostraMenuGiochiOnlineOffline)
            m.aggiungi("Suoni dei giochi e della chat", AddressOf MostraMenuSuoniGiochi)
            m.aggiungi("Istruzioni dei giochi", AddressOf MostraMenuIstruzioniGiochi)
            m.aggiungi("Livello del computer", AddressOf MostraMenuLivelloGiochi)
            m.avvia("Impostazioni giochi e chat")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuGiochiRete eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuGiochiOnlineOffline()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Gioca online, con il server su internet", Sub() SalvaConfigurazioneGenerica("GiochiOffline", "false", "I giochi useranno il server su internet: puoi giocare anche con gli altri."))
            m.aggiungi("Gioca offline, sul computer, senza internet", Sub() SalvaConfigurazioneGenerica("GiochiOffline", "true", "I giochi useranno il motore sul computer, senza internet: si gioca contro il computer."))
            m.posiziona(IndiceBinario("GiochiOffline", "false", "false"))
            m.avvia("Online o offline")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuGiochiOnlineOffline eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuLivelloGiochi()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Chiedi a ogni partita", Sub() SalvaConfigurazioneGenerica("GiochiLivello", "chiedi", "Il livello del computer si sceglie a ogni partita."))
            m.aggiungi("Sempre facile", Sub() SalvaConfigurazioneGenerica("GiochiLivello", "facile", "Il computer gioca sempre a livello facile."))
            m.aggiungi("Sempre normale", Sub() SalvaConfigurazioneGenerica("GiochiLivello", "normale", "Il computer gioca sempre a livello normale."))
            m.aggiungi("Sempre difficile", Sub() SalvaConfigurazioneGenerica("GiochiLivello", "difficile", "Il computer gioca sempre a livello difficile."))
            m.posiziona(IndiceMultiplo("GiochiLivello", New List(Of String) From {"chiedi", "facile", "normale", "difficile"}, "chiedi"))
            m.avvia("Livello del computer")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuLivelloGiochi eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuIstruzioniGiochi()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva le istruzioni dei giochi", Sub() SalvaConfigurazioneGenerica("GiochiIstruzioni", "true", "Istruzioni dei giochi attivate."))
            m.aggiungi("Disattiva le istruzioni dei giochi", Sub() SalvaConfigurazioneGenerica("GiochiIstruzioni", "false", "Istruzioni dei giochi disattivate."))
            m.posiziona(IndiceBinario("GiochiIstruzioni", "true", "false"))
            m.avvia("Istruzioni dei giochi")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuIstruzioniGiochi eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuSuoniGiochi()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva i suoni dei giochi e della chat", Sub() SalvaConfigurazioneGenerica("SuoniGiochi", "true", "Suoni dei giochi e della chat attivati."))
            m.aggiungi("Disattiva i suoni dei giochi e della chat", Sub() SalvaConfigurazioneGenerica("SuoniGiochi", "false", "Suoni dei giochi e della chat disattivati."))
            m.posiziona(IndiceBinario("SuoniGiochi", "true", "true"))
            m.avvia("Suoni dei giochi e della chat")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSuoniGiochi eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuSuoni()
        Try
            Dim menuSuoni As New WGMenu()
            menuSuoni.aggiungi("Attiva suoni", Sub() SalvaConfigurazioneGenerica("SuoniAttivi", "true", "Suoni attivati."))
            menuSuoni.aggiungi("Disattiva suoni", Sub() SalvaConfigurazioneGenerica("SuoniAttivi", "false", "Suoni disattivati."))
            menuSuoni.aggiungi("Volume suoni", AddressOf GestisciVolumeSuoni)
            menuSuoni.aggiungi("Suono di attesa personalizzato", AddressOf MenuSuonoAttesaPersonalizzato)
            menuSuoni.posiziona(IndiceBinario("SuoniAttivi", "true", "true"))
            menuSuoni.avvia("Impostazioni suoni")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSuoni eccezione: " & ex.Message)
        End Try
    End Sub

    Private menuVolumeSuoni As WGMenuLista

    Private Sub GestisciVolumeSuoni()
        Try
            menuVolumeSuoni = New WGMenuLista()
            menuVolumeSuoni.multiselezioneConsentita = False
            menuVolumeSuoni.impostaTitolo("Scegli il volume dei suoni")
            menuVolumeSuoni.aggiungiElemento("10%", "10")
            menuVolumeSuoni.aggiungiElemento("20%", "20")
            menuVolumeSuoni.aggiungiElemento("30%", "30")
            menuVolumeSuoni.aggiungiElemento("40%", "40")
            menuVolumeSuoni.aggiungiElemento("50%", "50")
            menuVolumeSuoni.aggiungiElemento("60%", "60")
            menuVolumeSuoni.aggiungiElemento("70%", "70")
            menuVolumeSuoni.aggiungiElemento("80%", "80")
            menuVolumeSuoni.aggiungiElemento("90%", "90")
            menuVolumeSuoni.aggiungiElemento("100%", "100")
            AddHandler menuVolumeSuoni.escPremuto, AddressOf ChiudiMenuVolumeSuoni
            AddHandler menuVolumeSuoni.ElementoSelezionato, AddressOf VolumeSuoniSelezionato

            Dim valoreAttualeMidi As Integer = 127
            Integer.TryParse(LeggiConfigValore("VolumeGlobale", "127"), valoreAttualeMidi)
            _volumeSuoniOriginale = valoreAttualeMidi
            Dim percentualePrecisa As Integer = CInt(Math.Round(valoreAttualeMidi * 100.0 / 127.0))
            percentualePrecisa = Math.Max(0, Math.Min(100, percentualePrecisa))
            _volumeSuoniPercentualePrecisa = percentualePrecisa
            Dim percentualeAttuale As Integer = CInt(Math.Round(valoreAttualeMidi * 100.0 / 127.0 / 10.0) * 10)
            percentualeAttuale = Math.Max(10, Math.Min(100, percentualeAttuale))
            menuVolumeSuoni.posiziona(percentualeAttuale.ToString())

            menuVolumeSuoni.aggiungiOpzione("Regolazione affinata", AddressOf OpzioneAffinataVolumeSuoni)

            AddHandler menuVolumeSuoni.VoceCambiata, AddressOf AnteprimaVolumeSuoni

            menuVolumeSuoni.avvia()
        Catch ex As Exception
            logger.ScriviLog("GestisciVolumeSuoni eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAffinataVolumeSuoni(sender As Object, e As EventArgs)
        Try
            RicaricaAffinata(menuVolumeSuoni, _volumeSuoniPercentualePrecisa)
        Catch ex As Exception
            logger.ScriviLog("OpzioneAffinataVolumeSuoni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuVolumeSuoni()
        Try
            RipristinaVolumeSuoni()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuVolumeSuoni eccezione: " & ex.Message)
        End Try
        menuVolumeSuoni.abbandona()
    End Sub

    Private Sub AnteprimaVolumeSuoni(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Return
            Dim percentuale As Integer = -1
            If Not Integer.TryParse(e.IdVoce, percentuale) Then Return
            If percentuale < 0 Then percentuale = 0
            If percentuale > 100 Then percentuale = 100
            WGSuoni.VolumeGlobale = CInt(percentuale * 127.0 / 100.0)
        Catch ex As Exception
            logger.ScriviLog("AnteprimaVolumeSuoni eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RipristinaVolumeSuoni()
        Try
            If _volumeSuoniOriginale < 0 Then Return
            WGSuoni.VolumeGlobale = _volumeSuoniOriginale
            _volumeSuoniOriginale = -1
        Catch ex As Exception
            logger.ScriviLog("RipristinaVolumeSuoni eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub VolumeSuoniSelezionato()
        Try
            Dim ids As List(Of String) = menuVolumeSuoni.ottieniElementi()
            If ids.Count > 0 Then
                Dim valStr As String = ids(0)
                Dim val As Integer = 100
                If Integer.TryParse(valStr, val) Then
                    Dim valoreMidi As Integer = CInt(val * 127.0 / 100.0)
                    SalvaConfig("VolumeGlobale", valoreMidi.ToString())
                    WGSuoni.VolumeGlobale = valoreMidi
                    _volumeSuoniOriginale = -1
                End If
                Dim msg As New WGMessaggi()
                msg.Messaggio("Volume suoni impostato al " & valStr & "%.")
                msg.ShowDialog()
                menuVolumeSuoni.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog("VolumeSuoniSelezionato eccezione: " & ex.Message)
        End Try
    End Sub

    Private WithEvents sceltaSuonoAttesa As WGFileDialog

    Private Sub MenuSuonoAttesaPersonalizzato()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Scegli un file audio", AddressOf ScegliFileSuonoAttesa)
            m.aggiungi("Usa il suono predefinito", AddressOf RipristinaSuonoAttesaPredefinito)
            m.avvia("Suono di attesa personalizzato")
        Catch ex As Exception
            logger.ScriviLog("MenuSuonoAttesaPersonalizzato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliFileSuonoAttesa()
        Try
            sceltaSuonoAttesa = New WGFileDialog()
            sceltaSuonoAttesa.AggiungiEstensione("mp3")
            sceltaSuonoAttesa.MultiselezioneConsentita = False
            AddHandler sceltaSuonoAttesa.selezioneterminata, AddressOf FileSuonoAttesaSceltoLista
            sceltaSuonoAttesa.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ScegliFileSuonoAttesa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub FileSuonoAttesaSceltoLista(elementi As List(Of String))
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                Return
            End If
            FileSuonoAttesaScelto(elementi(0))
        Catch ex As Exception
            logger.ScriviLog("FileSuonoAttesaSceltoLista eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub FileSuonoAttesaScelto(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not IO.File.Exists(percorso) Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Non è stato selezionato un file valido.")
                msgErrore.ShowDialog()
                Return
            End If
            Dim messaggioErrore As String = ""
            Dim msg As New WGMessaggi()
            If WGSuoni.ImpostaSuonoAttesaPersonalizzato(percorso, messaggioErrore) Then
                msg.Messaggio("Il suono di attesa personalizzato è stato impostato su " & IO.Path.GetFileName(percorso) & ".")
            Else
                msg.Messaggio("Non è stato possibile impostare il suono personalizzato. " & messaggioErrore)
            End If
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("FileSuonoAttesaScelto eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RipristinaSuonoAttesaPredefinito()
        Try
            WGSuoni.RipristinaSuonoAttesaPredefinito()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Verrà riprodotto di nuovo il suono di attesa predefinito.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("RipristinaSuonoAttesaPredefinito eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuScrittura()
        Try
            Dim menuScr As New WGMenu()
            menuScr.aggiungi("Eco digitazione", AddressOf MenuEcoDigitazione)
            menuScr.aggiungi("Lettura punteggiatura durante la scrittura", AddressOf MenuLetturaPunteggiaturaScritturaGenerale)
            menuScr.aggiungi("Controllo ortografico", AddressOf MenuControlloOrtografico)
            menuScr.aggiungi("Lettura password durante la digitazione", AddressOf MenuLetturaPassword)
            menuScr.aggiungi("Blocco note", AddressOf MostraMenuScritturaBloccoNote)
            menuScr.aggiungi("Word", AddressOf MostraMenuScritturaWord)
            menuScr.avvia("Scrittura")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuScrittura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuLettura()
        Try
            Dim menuLet As New WGMenu()
            menuLet.aggiungi("Lettura punteggiatura", AddressOf MenuLetturaPunteggiaturaGenerale)
            menuLet.aggiungi("Lettura dei numeri", AddressOf MenuModalitaLetturaNumeri)
            menuLet.aggiungi("Blocco note", AddressOf MostraMenuLetturaBloccoNote)
            menuLet.aggiungi("Word", AddressOf MostraMenuLetturaWord)
            menuLet.avvia("Lettura")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuLettura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuScritturaBloccoNote()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Tipo di carattere", AddressOf MenuFontPredefinitoBloccoNote)
            m.aggiungi("Dimensione carattere", AddressOf MenuDimensionePredefinitaBloccoNote)
            m.aggiungi("Maiuscola automatica", AddressOf MenuMaiuscolaAutomaticaBloccoNote)
            m.aggiungi("Lettura punteggiatura durante la scrittura", AddressOf MenuLetturaPunteggiaturaScritturaBloccoNote)
            m.avvia("Blocco note")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuScritturaBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuLetturaBloccoNote()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Lettura punteggiatura", AddressOf MenuLetturaPunteggiaturaBloccoNote)
            m.aggiungi("Pausa tra le righe in lettura continua", AddressOf MenuPausaLetturaContinuaBloccoNote)
            m.avvia("Blocco note")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuLetturaBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuScritturaWord()
        Try
            Dim menuWordS As New WGMenu()
            menuWordS.aggiungi("Documento e salvataggio", AddressOf MostraMenuWordDocumento)
            menuWordS.aggiungi("Carattere e paragrafo", AddressOf MostraMenuWordCarattere)
            menuWordS.aggiungi("Zoom", AddressOf MostraMenuWordZoom)
            menuWordS.aggiungi("Maiuscola automatica", AddressOf MenuMaiuscolaAutomaticaWordProcessor)
            menuWordS.aggiungi("Lettura punteggiatura durante la scrittura", AddressOf MenuLetturaPunteggiaturaScritturaWord)
            menuWordS.avvia("Word")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuScritturaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuLetturaWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Lettura punteggiatura", AddressOf MenuLetturaPunteggiaturaWord)
            m.aggiungi("Lettura numero nota durante lo scorrimento", AddressOf MenuLeggiNumeroNotaScorrimento)
            m.aggiungi("Elimina nota quando elimino la parola collegata", AddressOf MenuEliminaNotaConParola)
            m.aggiungi("Apertura nota: nel documento o campo separato", AddressOf MenuModificaNoteNelDocumento)
            m.aggiungi("Annuncio formattazione", AddressOf MenuAnnuncioFormattazioneWord)
            m.aggiungi("Effetto sonoro al cambio pagina", AddressOf MenuFruscioPaginaWord)
            m.aggiungi("Lettura intestazione e piè di pagina al cambio pagina", AddressOf MenuLeggiIntestazioneCambioPagina)
            m.avvia("Word")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuLetturaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuEcoDigitazione()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Disattivato", Sub() ImpostaEcoDigitazione("0", "Eco di digitazione disattivato."))
            m.aggiungi("Solo caratteri", Sub() ImpostaEcoDigitazione("1", "Eco di digitazione impostato su: Solo caratteri."))
            m.aggiungi("Solo parole", Sub() ImpostaEcoDigitazione("2", "Eco di digitazione impostato su: Solo parole."))
            m.aggiungi("Caratteri e parole", Sub() ImpostaEcoDigitazione("3", "Eco di digitazione impostato su: Caratteri e parole."))
            Dim idxEco As Integer = 0
            Integer.TryParse(impostazioniComuni.EchoDigitazione, idxEco)
            If idxEco < 0 OrElse idxEco > 3 Then idxEco = 0
            m.posiziona(idxEco)
            m.avvia("Eco di digitazione", True)
        Catch ex As Exception
            logger.ScriviLog("MenuEcoDigitazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaEcoDigitazione(valore As String, messaggioFeedback As String)
        Try
            impostazioniComuni.EchoDigitazione = valore
            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggioFeedback)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaEcoDigitazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLetturaPunteggiaturaGenerale()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nessuna", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "0", "La punteggiatura non verrà letta."))
            m.aggiungi("Poca", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "1", "Verranno letti solo i simboli tecnici, come chiocciola, cancelletto e percentuale."))
            m.aggiungi("Molta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "2", "Verranno letti simboli tecnici, parentesi, virgolette, trattini, punto e virgola e due punti."))
            m.aggiungi("Tutta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "3", "Verrà letta tutta la punteggiatura, compresi punti, virgole e punti interrogativi."))
            m.posiziona(IndiceLivelloPunteggiatura())
            m.avvia("Lettura punteggiatura", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLetturaPunteggiaturaGenerale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLetturaPunteggiaturaScritturaGenerale()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nessuna", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "0", "Durante la scrittura non verranno letti simboli o punteggiatura."))
            m.aggiungi("Poca", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "1", "Durante la scrittura verranno letti solo i simboli tecnici, come chiocciola, cancelletto e percentuale."))
            m.aggiungi("Molta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "2", "Durante la scrittura verranno letti simboli tecnici, parentesi, virgolette, trattini, punto e virgola e due punti."))
            m.aggiungi("Tutta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "3", "Durante la scrittura verrà letta tutta la punteggiatura, compresi punti, virgole e punti interrogativi."))
            m.posiziona(IndiceLivelloPunteggiaturaScrittura())
            m.avvia("Lettura punteggiatura durante la scrittura", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLetturaPunteggiaturaScritturaGenerale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuModificaNoteNelDocumento()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Apri la nota nel documento", Sub() SalvaConfigurazioneGenerica("modifica_note_nel_documento", "1", "Quando confermi una nota con Invio, ti sposterai direttamente nel documento per modificarla."))
            m.aggiungi("Apri la nota nel suo campo separato", Sub() SalvaConfigurazioneGenerica("modifica_note_nel_documento", "0", "Quando confermi una nota con Invio, si aprirà il campo di modifica separato."))
            Dim impostazioni As New WGImpostazioniComuni()
            m.posiziona(If(impostazioni.ModificaNoteNelDocumento, 0, 1))
            m.avvia("Apertura nota: nel documento o campo separato", True)
        Catch ex As Exception
            logger.ScriviLog("MenuModificaNoteNelDocumento eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuEliminaNotaConParola()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva", Sub() SalvaConfigurazioneGenerica("elimina_nota_con_parola", "1", "Cancellando la parola o il collegamento a cui è agganciata una nota, la nota verrà eliminata automaticamente."))
            m.aggiungi("Disattiva", Sub() SalvaConfigurazioneGenerica("elimina_nota_con_parola", "0", "Le note non verranno più eliminate automaticamente cancellando la parola a cui sono agganciate."))
            Dim impostazioni As New WGImpostazioniComuni()
            m.posiziona(If(impostazioni.EliminaNotaConParola, 0, 1))
            m.avvia("Elimina nota quando elimino la parola collegata", True)
        Catch ex As Exception
            logger.ScriviLog("MenuEliminaNotaConParola eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLeggiNumeroNotaScorrimento()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Leggi il numero della nota", Sub() SalvaConfigurazioneGenerica("leggi_numero_nota_scorrimento", "1", "Durante lo scorrimento del testo con le frecce verrà annunciato il numero delle note incontrate."))
            m.aggiungi("Non leggere il numero della nota", Sub() SalvaConfigurazioneGenerica("leggi_numero_nota_scorrimento", "0", "Durante lo scorrimento del testo con le frecce le note non verranno più annunciate."))
            m.posiziona(IndiceBinario("leggi_numero_nota_scorrimento", "1", "1"))
            m.avvia("Lettura numero nota", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLeggiNumeroNotaScorrimento eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuMaiuscolaAutomaticaBloccoNote()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva maiuscola automatica", Sub() SalvaConfigurazioneGenerica("maiuscola_automatica_campo", "1", "Nel blocco note, l'inserimento automatico della maiuscola è attivo."))
            m.aggiungi("Disattiva maiuscola automatica", Sub() SalvaConfigurazioneGenerica("maiuscola_automatica_campo", "0", "Nel blocco note, l'inserimento automatico della maiuscola è disattivato."))
            m.posiziona(IndiceBinario("maiuscola_automatica_campo", "1", "1"))
            m.avvia("Maiuscola automatica", True)
        Catch ex As Exception
            logger.ScriviLog("MenuMaiuscolaAutomaticaBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuMaiuscolaAutomaticaWordProcessor()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva maiuscola automatica", Sub() SalvaConfigurazioneGenerica("maiuscola_automatica", "1", "Nel word processor, l'inserimento automatico della maiuscola è attivo."))
            m.aggiungi("Disattiva maiuscola automatica", Sub() SalvaConfigurazioneGenerica("maiuscola_automatica", "0", "Nel word processor, l'inserimento automatico della maiuscola è disattivato."))
            m.posiziona(IndiceBinario("maiuscola_automatica", "1", "1"))
            m.avvia("Maiuscola automatica", True)
        Catch ex As Exception
            logger.ScriviLog("MenuMaiuscolaAutomaticaWordProcessor eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuControlloOrtografico()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva controllo ortografico", Sub() ImpostaControlloOrtografico(True, "Controllo ortografico in background attivato."))
            m.aggiungi("Disattiva controllo ortografico", Sub() ImpostaControlloOrtografico(False, "Controllo ortografico in background disattivato."))
            m.posiziona(If(impostazioniComuni.AbilitaCorrettoreAttivo, 0, 1))
            m.avvia("Controllo ortografico", True)
        Catch ex As Exception
            logger.ScriviLog("MenuControlloOrtografico eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaControlloOrtografico(attivo As Boolean, messaggioFeedback As String)
        Try
            impostazioniComuni.AbilitaCorrettoreAttivo = attivo
            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggioFeedback)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaControlloOrtografico eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLetturaPassword()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Annuncia ""asterisco"" (non leggere il carattere)", Sub() ImpostaLetturaPassword(False, "Durante la digitazione nei campi password verrà annunciato ""asterisco"" invece del carattere digitato."))
            m.aggiungi("Leggi il carattere digitato in chiaro", Sub() ImpostaLetturaPassword(True, "Durante la digitazione nei campi password verrà annunciato il carattere digitato in chiaro."))
            m.posiziona(If(impostazioniComuni.LeggiPasswordAttivo, 1, 0))
            m.avvia("Lettura password durante la digitazione", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLetturaPassword eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaLetturaPassword(attiva As Boolean, messaggioFeedback As String)
        Try
            impostazioniComuni.LeggiPasswordAttivo = attiva
            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggioFeedback)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaLetturaPassword eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuSalvataggioAutomaticoWord()
        Dim m As New WGMenu()
        m.aggiungi("Disattivato", Sub() SalvaConfigurazioneGenerica("intervallo_salvataggio", "0", "Salvataggio automatico disattivato."))
        m.aggiungi("Ogni minuto", Sub() SalvaConfigurazioneGenerica("intervallo_salvataggio", "60000", "Salvataggio automatico impostato ogni minuto."))
        m.aggiungi("Ogni 3 minuti", Sub() SalvaConfigurazioneGenerica("intervallo_salvataggio", "180000", "Salvataggio automatico impostato ogni 3 minuti."))
        m.aggiungi("Ogni 5 minuti", Sub() SalvaConfigurazioneGenerica("intervallo_salvataggio", "300000", "Salvataggio automatico impostato ogni 5 minuti."))
        m.aggiungi("Ogni 10 minuti", Sub() SalvaConfigurazioneGenerica("intervallo_salvataggio", "600000", "Salvataggio automatico impostato ogni 10 minuti."))
        Dim valSalva As String = LeggiConfigValore("intervallo_salvataggio", "0")
        Dim idxSalva As Integer = 0
        Select Case valSalva
            Case "60000"
                idxSalva = 1
            Case "180000"
                idxSalva = 2
            Case "300000"
                idxSalva = 3
            Case "600000"
                idxSalva = 4
        End Select
        m.posiziona(idxSalva)
        m.avvia("Salvataggio automatico", True)
    End Sub

    Private Sub MostraMenuSintesiVocale()
        Try
            Dim menuScelta As New WGMenu()
            menuScelta.aggiungi("Uso normale di WinGabriel", AddressOf MostraMenuSintesiNormale)
            menuScelta.aggiungi("Conversione in mp3", AddressOf MostraMenuSintesiMp3)
            menuScelta.avvia("Sintesi vocale")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSintesiVocale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuSintesiNormale()
        Try
            _configuraSintesiMp3 = False
            Dim menuSintesi As New WGMenu()
            menuSintesi.aggiungi("Scegli sistema di lettura", AddressOf GestisciTipoSintesi)
            menuSintesi.aggiungi("Scegli voce", AddressOf GestisciSelezioneVoce)
            menuSintesi.aggiungi("Velocità voce", AddressOf GestisciVelocitaVoce)
            menuSintesi.aggiungi("Volume voce", AddressOf GestisciVolumeVoce)
            menuSintesi.avvia("Sintesi vocale, uso normale")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSintesiNormale eccezione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' La sintesi vocale per la configurazione guidata: lo stesso menu dell'uso normale, con in più "Ho finito, continua". Quando il
    ''' menu si chiude, in qualunque modo, chiama alTermine.
    ''' </summary>
    Public Sub MostraSintesiPerProcedura(alTermine As Action)
        Try
            _configuraSintesiMp3 = False
            Dim menuSintesi As New WGMenu()
            menuSintesi.aggiungi("Scegli sistema di lettura", AddressOf GestisciTipoSintesi)
            menuSintesi.aggiungi("Scegli voce", AddressOf GestisciSelezioneVoce)
            menuSintesi.aggiungi("Velocità voce", AddressOf GestisciVelocitaVoce)
            menuSintesi.aggiungi("Volume voce", AddressOf GestisciVolumeVoce)
            menuSintesi.aggiungi("Ho finito, continua", Sub() menuSintesi.abbandona())
            AddHandler menuSintesi.abbandonato, Sub(s As Object, e As EventArgs)
                                                    If alTermine IsNot Nothing Then alTermine()
                                                End Sub
            menuSintesi.avvia("Sintesi vocale")
        Catch ex As Exception
            logger.ScriviLog("MostraSintesiPerProcedura eccezione: " & ex.Message)
            If alTermine IsNot Nothing Then alTermine()
        End Try
    End Sub

    Private Sub MostraMenuSintesiMp3()
        Try
            _configuraSintesiMp3 = True
            Dim menuSintesi As New WGMenu()
            menuSintesi.aggiungi("Scegli voce", AddressOf GestisciSelezioneVoce)
            menuSintesi.aggiungi("Velocità voce", AddressOf GestisciVelocitaVoce)
            menuSintesi.aggiungi("Volume voce", AddressOf GestisciVolumeVoce)
            menuSintesi.avvia("Sintesi vocale, conversione in mp3")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSintesiMp3 eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuStampante()
        Try
            If letturaCapacitaInCorso Then Return

            Dim nomeStampanteCorrente As String = impostazioniComuni.NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nomeStampanteCorrente) Then
                CostruisciMenuStampante(New Global.WGStampante.CapacitaStampanteInfo())
                Return
            End If

            Dim capacitaMemorizzata As Global.WGStampante.CapacitaStampanteInfo = Nothing
            Try
                If Global.WGStampante.WGStampante.CapacitaStampanteInCache(nomeStampanteCorrente, capacitaMemorizzata) AndAlso capacitaMemorizzata IsNot Nothing Then
                    CostruisciMenuStampante(capacitaMemorizzata)
                    Return
                End If
            Catch exCache As Exception
                logger.ScriviLog("MostraMenuStampante cache capacità eccezione: " & exCache.Message & Environment.NewLine & exCache.StackTrace)
            End Try

            letturaCapacitaInCorso = True
            nomeStampanteCapacita = nomeStampanteCorrente
            contestoCapacitaStampante = SynchronizationContext.Current

            Try
                processoCapacitaStampante = New WGProcessoInCorso()
                processoCapacitaStampante.Avvia("Lettura delle caratteristiche della stampante in corso, attendere")
            Catch exProcesso As Exception
                logger.ScriviLog("MostraMenuStampante avvio processo eccezione: " & exProcesso.Message & Environment.NewLine & exProcesso.StackTrace)
            End Try

            Task.Run(New Action(AddressOf LavoroCapacitaStampante))
        Catch ex As Exception
            logger.ScriviLog("MostraMenuStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ChiudiProcessoCapacitaStampante()
            letturaCapacitaInCorso = False
        End Try
    End Sub

    Private Sub LavoroCapacitaStampante()
        Dim capacita As Global.WGStampante.CapacitaStampanteInfo = Nothing
        Try
            capacita = Global.WGStampante.WGStampante.CapacitaStampante(nomeStampanteCapacita)
        Catch ex As Exception
            logger.ScriviLog("LavoroCapacitaStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            capacita = Nothing
        Finally
            Try
                If capacita Is Nothing Then capacita = New Global.WGStampante.CapacitaStampanteInfo()
                Dim contesto As SynchronizationContext = contestoCapacitaStampante
                If contesto IsNot Nothing Then
                    contesto.Post(New SendOrPostCallback(AddressOf CompletaCapacitaStampante), capacita)
                Else
                    Dim contenitore As WGContenitore = WGContenitore.Istanza
                    If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                        contenitore.BeginInvoke(New SendOrPostCallback(AddressOf CompletaCapacitaStampante), New Object() {capacita})
                    Else
                        letturaCapacitaInCorso = False
                    End If
                End If
            Catch exChiusura As Exception
                logger.ScriviLog("LavoroCapacitaStampante chiusura eccezione: " & exChiusura.Message & Environment.NewLine & exChiusura.StackTrace)
                letturaCapacitaInCorso = False
            End Try
        End Try
    End Sub

    Private Sub CompletaCapacitaStampante(stato As Object)
        Try
            ChiudiProcessoCapacitaStampante()
            contestoCapacitaStampante = Nothing
            Dim capacita As Global.WGStampante.CapacitaStampanteInfo = TryCast(stato, Global.WGStampante.CapacitaStampanteInfo)
            If capacita Is Nothing Then capacita = New Global.WGStampante.CapacitaStampanteInfo()
            CostruisciMenuStampante(capacita)
        Catch ex As Exception
            logger.ScriviLog("CompletaCapacitaStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            letturaCapacitaInCorso = False
        End Try
    End Sub

    Private Sub ChiudiProcessoCapacitaStampante()
        Try
            If processoCapacitaStampante Is Nothing Then Return
            Dim processo As WGProcessoInCorso = processoCapacitaStampante
            processoCapacitaStampante = Nothing
            processo.Fine()
        Catch ex As Exception
            logger.ScriviLog("ChiudiProcessoCapacitaStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuAggiungiStampanteRete()
        Try
            Dim menuRete As New WGMenu()
            menuRete.aggiungi("Stampante condivisa da un altro computer", AddressOf ChiediPercorsoStampanteCondivisa)
            menuRete.aggiungi("Stampante Wi-Fi o di rete", AddressOf CercaStampantiWiFi)
            menuRete.avvia("Aggiungi stampante di rete")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAggiungiStampanteRete eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private ReadOnly _stampantiReteTrovate As New Dictionary(Of String, WGStampanteTrovata)()
    Private _menuStampantiRete As WGMenuLista
    Private _processoStampantiRete As WGProcessoInCorso
    Private _operazioneStampantiReteInCorso As Boolean = False

    Private Sub ChiudiProcessoStampantiRete()
        Try
            If _processoStampantiRete Is Nothing Then Return
            Dim processo As WGProcessoInCorso = _processoStampantiRete
            _processoStampantiRete = Nothing
            processo.Fine()
        Catch ex As Exception
            logger.ScriviLog("ChiudiProcessoStampantiRete eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaProcessoStampantiRete(testo As String)
        Try
            ChiudiProcessoStampantiRete()
            _processoStampantiRete = New WGProcessoInCorso()
            _processoStampantiRete.Avvia(testo)
        Catch ex As Exception
            logger.ScriviLog("AvviaProcessoStampantiRete eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CercaStampantiWiFi()
        Try
            If _operazioneStampantiReteInCorso Then Return
            Dim contesto As SynchronizationContext = SynchronizationContext.Current
            If contesto Is Nothing Then Return
            _operazioneStampantiReteInCorso = True
            Task.Run(Sub()
                         Dim errore As String = ""
                         Dim elenco As List(Of WGStampanteTrovata) = Nothing
                         Try
                             elenco = Global.WGStampante.WGStampante.CercaStampantiRete(errore)
                         Catch exRicerca As Exception
                             logger.ScriviLog("CercaStampantiWiFi ricerca: " & exRicerca.Message & Environment.NewLine & exRicerca.StackTrace)
                             errore = "La ricerca delle stampanti in rete non è riuscita."
                         End Try
                         contesto.Post(Sub(stato)
                                           Try
                                               ChiudiProcessoStampantiRete()
                                               _operazioneStampantiReteInCorso = False
                                               MostraStampantiReteTrovate(elenco, errore)
                                           Catch exMostra As Exception
                                               logger.ScriviLog("CercaStampantiWiFi risultato: " & exMostra.Message & Environment.NewLine & exMostra.StackTrace)
                                           End Try
                                       End Sub, Nothing)
                     End Sub)
            AvviaProcessoStampantiRete("Ricerca delle stampanti Wi-Fi e di rete in corso, attendere")
        Catch ex As Exception
            _operazioneStampantiReteInCorso = False
            ChiudiProcessoStampantiRete()
            logger.ScriviLog("CercaStampantiWiFi eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraStampantiReteTrovate(elenco As List(Of WGStampanteTrovata), errore As String)
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                Dim testo As String = "Nessuna stampante trovata in rete. Controlla che la stampante sia accesa e collegata alla stessa rete Wi-Fi del computer, poi riprova."
                If Not String.IsNullOrWhiteSpace(errore) Then testo = errore & " " & testo
                Dim msg As New WGMessaggi()
                msg.Messaggio(testo)
                msg.ShowDialog()
                Return
            End If
            ChiudiMenuStampantiRete()
            _stampantiReteTrovate.Clear()
            _menuStampantiRete = New WGMenuLista()
            _menuStampantiRete.impostaTitolo("Stampanti trovate in rete")
            _menuStampantiRete.multiselezioneConsentita = False
            For indice As Integer = 0 To elenco.Count - 1
                Dim id As String = "S" & indice.ToString()
                _stampantiReteTrovate(id) = elenco(indice)
                _menuStampantiRete.aggiungiElemento(elenco(indice).Descrizione, id)
            Next
            _menuStampantiRete.aggiungiOpzione("Aggiungi", AddressOf AggiungiStampanteReteSelezionata)
            _menuStampantiRete.aggiungiOpzione("Cerca di nuovo", AddressOf CercaDiNuovoStampantiRete)
            AddHandler _menuStampantiRete.escPremuto, AddressOf EscMenuStampantiRete
            AddHandler _menuStampantiRete.ElementoSelezionato, AddressOf StampanteReteScelta
            _menuStampantiRete.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraStampantiReteTrovate eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuStampantiRete()
        Try
            If _menuStampantiRete Is Nothing Then Return
            Dim menu As WGMenuLista = _menuStampantiRete
            _menuStampantiRete = Nothing
            RemoveHandler menu.escPremuto, AddressOf EscMenuStampantiRete
            RemoveHandler menu.ElementoSelezionato, AddressOf StampanteReteScelta
            menu.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuStampantiRete eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub EscMenuStampantiRete(sender As Object, e As EventArgs)
        ChiudiMenuStampantiRete()
    End Sub

    Private Sub CercaDiNuovoStampantiRete(sender As Object, e As EventArgs)
        Try
            ChiudiMenuStampantiRete()
            CercaStampantiWiFi()
        Catch ex As Exception
            logger.ScriviLog("CercaDiNuovoStampantiRete eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiStampanteReteSelezionata(sender As Object, e As EventArgs)
        Try
            If _menuStampantiRete Is Nothing Then Return
            Dim selezione As List(Of String) = _menuStampantiRete.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then Return
            StampanteReteScelta(selezione(0))
        Catch ex As Exception
            logger.ScriviLog("AggiungiStampanteReteSelezionata eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub StampanteReteScelta(id As String)
        Try
            Dim stampante As WGStampanteTrovata = Nothing
            If String.IsNullOrEmpty(id) OrElse Not _stampantiReteTrovate.TryGetValue(id, stampante) Then Return
            ChiudiMenuStampantiRete()
            AggiungiStampanteRete(stampante)
        Catch ex As Exception
            logger.ScriviLog("StampanteReteScelta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AggiungiStampanteRete(stampante As WGStampanteTrovata)
        Try
            If stampante Is Nothing OrElse _operazioneStampantiReteInCorso Then Return
            Dim contesto As SynchronizationContext = SynchronizationContext.Current
            If contesto Is Nothing Then Return
            _operazioneStampantiReteInCorso = True
            Task.Run(Sub()
                         Dim riuscita As Boolean = False
                         Dim nomeInstallato As String = ""
                         Dim errore As String = ""
                         Try
                             riuscita = Global.WGStampante.WGStampante.AggiungiStampanteTrovata(stampante, nomeInstallato, errore)
                         Catch exAggiunta As Exception
                             logger.ScriviLog("AggiungiStampanteRete aggiunta: " & exAggiunta.Message & Environment.NewLine & exAggiunta.StackTrace)
                             riuscita = False
                             errore = "L'aggiunta della stampante non è riuscita."
                         End Try
                         contesto.Post(Sub(stato)
                                           Try
                                               ChiudiProcessoStampantiRete()
                                               _operazioneStampantiReteInCorso = False
                                               CompletaAggiuntaStampanteRete(riuscita, nomeInstallato, errore)
                                           Catch exFine As Exception
                                               logger.ScriviLog("AggiungiStampanteRete fine: " & exFine.Message & Environment.NewLine & exFine.StackTrace)
                                           End Try
                                       End Sub, Nothing)
                     End Sub)
            AvviaProcessoStampantiRete("Aggiunta di " & stampante.NomeMostrato & " in corso. Può volerci qualche minuto, attendere")
        Catch ex As Exception
            _operazioneStampantiReteInCorso = False
            ChiudiProcessoStampantiRete()
            logger.ScriviLog("AggiungiStampanteRete eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CompletaAggiuntaStampanteRete(riuscita As Boolean, nomeInstallato As String, errore As String)
        Try
            Dim msg As New WGMessaggi()
            If Not riuscita Then
                msg.Messaggio(If(String.IsNullOrWhiteSpace(errore), "Non è stato possibile aggiungere la stampante.", errore))
            ElseIf String.IsNullOrWhiteSpace(nomeInstallato) Then
                msg.Messaggio("La stampante è stata aggiunta, ma non è ancora pronta. Tra qualche istante sceglila con Scegli stampante.")
            Else
                SalvaConfig("stampante_predefinita", nomeInstallato)
                impostazioniComuni.StampantePredefinita = nomeInstallato
                msg.Messaggio("Stampante " & nomeInstallato & " aggiunta e impostata come stampante di WinGabriel.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CompletaAggiuntaStampanteRete eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiediPercorsoStampanteCondivisa()
        Try
            If collegamentoStampanteInCorso Then Return
            campiStampanteRete = New Campi()
            campiStampanteRete.Aggiungi("Percorso della stampante condivisa, per esempio \\UFFICIO\Stampante", "percorso", True)
            AddHandler campiStampanteRete.Fine, AddressOf ConfermaPercorsoStampanteCondivisa
            campiStampanteRete.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ChiediPercorsoStampanteCondivisa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>ConfermaPercorsoStampanteCondivisa con la firma esatta dell'evento: così AddHandler e RemoveHandler agganciano e staccano lo stesso gestore.</summary>
    Private Sub ConfermaPercorsoStampanteCondivisa(senderEvento As Object, eEvento As EventArgs)
        ConfermaPercorsoStampanteCondivisa()
    End Sub

    Private Sub ConfermaPercorsoStampanteCondivisa()
        Try
            If campiStampanteRete Is Nothing Then Return
            Dim percorso As String = If(campiStampanteRete.RecuperaValore("percorso"), "")
            RemoveHandler campiStampanteRete.Fine, AddressOf ConfermaPercorsoStampanteCondivisa
            campiStampanteRete = Nothing

            percorso = percorso.Trim().Replace("/"c, "\"c).TrimStart("\"c)
            If percorso.Length = 0 Then Return
            percorso = "\\" & percorso.TrimEnd("\"c)

            If collegamentoStampanteInCorso Then Return
            collegamentoStampanteInCorso = True
            percorsoStampanteRete = percorso
            contestoCollegamentoStampante = SynchronizationContext.Current

            Try
                processoCollegamentoStampante = New WGProcessoInCorso()
                processoCollegamentoStampante.Avvia("Collegamento della stampante in corso, attendere")
            Catch exProcesso As Exception
                logger.ScriviLog("ConfermaPercorsoStampanteCondivisa avvio processo eccezione: " & exProcesso.Message & Environment.NewLine & exProcesso.StackTrace)
            End Try

            Task.Run(New Action(AddressOf LavoroCollegamentoStampante))
        Catch ex As Exception
            logger.ScriviLog("ConfermaPercorsoStampanteCondivisa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ChiudiProcessoCollegamentoStampante()
            collegamentoStampanteInCorso = False
        End Try
    End Sub

    Private Sub LavoroCollegamentoStampante()
        Dim riuscito As Boolean = False
        Dim errore As String = ""
        Try
            riuscito = Global.WGStampante.WGStampante.AggiungiStampanteRete(percorsoStampanteRete)
            If Not riuscito Then errore = Global.WGStampante.WGStampante.UltimoErrore()
        Catch ex As Exception
            logger.ScriviLog("LavoroCollegamentoStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            riuscito = False
            errore = ""
        Finally
            Try
                Dim esito As New Tuple(Of Boolean, String)(riuscito, If(errore, ""))
                Dim contesto As SynchronizationContext = contestoCollegamentoStampante
                If contesto IsNot Nothing Then
                    contesto.Post(New SendOrPostCallback(AddressOf CompletaCollegamentoStampante), esito)
                Else
                    Dim contenitore As WGContenitore = WGContenitore.Istanza
                    If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                        contenitore.BeginInvoke(New SendOrPostCallback(AddressOf CompletaCollegamentoStampante), New Object() {esito})
                    Else
                        collegamentoStampanteInCorso = False
                    End If
                End If
            Catch exChiusura As Exception
                logger.ScriviLog("LavoroCollegamentoStampante chiusura eccezione: " & exChiusura.Message & Environment.NewLine & exChiusura.StackTrace)
                collegamentoStampanteInCorso = False
            End Try
        End Try
    End Sub

    Private Sub CompletaCollegamentoStampante(stato As Object)
        Try
            ChiudiProcessoCollegamentoStampante()
            contestoCollegamentoStampante = Nothing

            Dim esito As Tuple(Of Boolean, String) = TryCast(stato, Tuple(Of Boolean, String))
            Dim riuscito As Boolean = esito IsNot Nothing AndAlso esito.Item1
            Dim msg As New WGMessaggi()
            If riuscito Then
                SalvaConfig("stampante_predefinita", percorsoStampanteRete)
                impostazioniComuni.StampantePredefinita = percorsoStampanteRete
                msg.Messaggio("Stampante collegata e impostata come stampante di WinGabriel.")
            Else
                Dim errore As String = If(esito IsNot Nothing, esito.Item2, "")
                If String.IsNullOrWhiteSpace(errore) Then errore = "Impossibile collegare la stampante."
                msg.Messaggio(errore)
            End If
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CompletaCollegamentoStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            collegamentoStampanteInCorso = False
        End Try
    End Sub

    Private Sub ChiudiProcessoCollegamentoStampante()
        Try
            If processoCollegamentoStampante Is Nothing Then Return
            Dim processo As WGProcessoInCorso = processoCollegamentoStampante
            processoCollegamentoStampante = Nothing
            processo.Fine()
        Catch ex As Exception
            logger.ScriviLog("ChiudiProcessoCollegamentoStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CostruisciMenuStampante(capacita As Global.WGStampante.CapacitaStampanteInfo)
        Try
            If capacita Is Nothing Then capacita = New Global.WGStampante.CapacitaStampanteInfo()
            Dim menu As New WGMenu()
            menu.aggiungi("Scegli stampante", AddressOf ScegliStampantePredefinita)
            menu.aggiungi("Aggiungi stampante di rete", AddressOf MostraMenuAggiungiStampanteRete)
            menu.aggiungi("Copie", AddressOf ImpostaCopieStampa)
            menu.aggiungi("Margine", AddressOf ImpostaMargineStampa)
            menu.aggiungi("Nome carattere", AddressOf ImpostaNomeCarattereStampa)
            menu.aggiungi("Dimensione carattere", AddressOf ImpostaDimensioneCarattereStampa)
            menu.aggiungi("Orientamento", AddressOf ImpostaOrientamentoStampa)
            If capacita.SupportaFronteRetro Then
                menu.aggiungi("Fronte e retro", AddressOf ImpostaFronteRetroStampa)
            End If
            If capacita.SupportaColore Then
                menu.aggiungi("Colore", AddressOf ImpostaColoreStampa)
            End If
            menu.aggiungi("Qualità di stampa", AddressOf ImpostaQualitaStampa)
            menu.aggiungi("Svuota coda di stampa", AddressOf SvuotaCodaStampaImpostazioni)
            menu.avvia("Stampante")
        Catch ex As Exception
            logger.ScriviLog("CostruisciMenuStampante eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliStampantePredefinita()
        Try
            Dim preferita As String = impostazioniComuni.StampantePredefinita
            If String.IsNullOrWhiteSpace(preferita) Then
                preferita = Global.WGStampante.WGStampante.StampantePredefinitaSistema()
            End If

            If _annullaStampanti IsNot Nothing Then
                Try
                    _annullaStampanti.Cancel()
                    _annullaStampanti.Dispose()
                Catch
                End Try
                _annullaStampanti = Nothing
            End If
            _annullaStampanti = New CancellationTokenSource()
            Dim token As CancellationToken = _annullaStampanti.Token

            Dim syncContext As SynchronizationContext = SynchronizationContext.Current
            processoincorso = New WGProcessoInCorso()
            AddHandler processoincorso.cancella, AddressOf AnnullaRicercaStampanti
            Dim processoLocale As WGProcessoInCorso = processoincorso

            Task.Run(Sub()
                         Dim elenco As List(Of InfoStampante) = Nothing
                         Try
                             elenco = Global.WGStampante.WGStampante.ElencoStampanti(preferita, token)
                         Catch exElenco As Exception
                             logger.ScriviLog("ScegliStampantePredefinita elenco: " & exElenco.Message)
                         End Try
                         If token.IsCancellationRequested Then Return
                         If syncContext Is Nothing Then Return
                         syncContext.Post(Sub(state)
                                              CompletaRicercaStampanti(elenco, preferita, processoLocale)
                                          End Sub, Nothing)
                     End Sub)

            processoincorso.Avvia("Ricerca stampanti in corso, attendere", True)
        Catch ex As Exception
            logger.ScriviLog("ScegliStampantePredefinita eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnullaRicercaStampanti(sender As Object, e As EventArgs)
        Try
            If _annullaStampanti IsNot Nothing Then _annullaStampanti.Cancel()
            processoincorso = Nothing
            logger.ScriviLog("Impostazioni.AnnullaRicercaStampanti")
        Catch ex As Exception
            logger.ScriviLog("AnnullaRicercaStampanti eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub CompletaRicercaStampanti(elenco As List(Of InfoStampante), preferita As String, processo As WGProcessoInCorso)
        Try
            Try
                If processo IsNot Nothing Then processo.Fine()
            Catch exFine As Exception
                logger.ScriviLog("CompletaRicercaStampanti Fine: " & exFine.Message)
            End Try
            processoincorso = Nothing

            If _annullaStampanti IsNot Nothing AndAlso _annullaStampanti.IsCancellationRequested Then Return

            If elenco Is Nothing OrElse elenco.Count = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessuna stampante disponibile. Le stampanti di rete vengono cercate da sole.")
                msg.ShowDialog()
                Return
            End If

            menuStampante = New WGMenuLista()
            menuStampante.multiselezioneConsentita = False
            menuStampante.impostaTitolo("Scegli la stampante predefinita")

            For Each info As InfoStampante In elenco
                menuStampante.aggiungiElemento(Global.WGStampante.WGStampante.EtichettaStampante(info, preferita), info.Nome)
            Next

            If Not String.IsNullOrEmpty(preferita) Then
                Try
                    menuStampante.posiziona(preferita)
                Catch exPos As Exception
                    logger.ScriviLog("CompletaRicercaStampanti posiziona eccezione: " & exPos.Message)
                End Try
            End If

            AddHandler menuStampante.escPremuto, AddressOf ChiudiMenuStampante
            AddHandler menuStampante.ElementoSelezionato, AddressOf StampanteImpostazioniSelezionata
            menuStampante.avvia()
        Catch ex As Exception
            logger.ScriviLog("CompletaRicercaStampanti eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaCopieStampa()
        Try
            menuCopieStampa = New WGMenuLista()
            menuCopieStampa.multiselezioneConsentita = False
            menuCopieStampa.impostaTitolo("Numero di copie")
            Dim attuale As String = impostazioniComuni.StampaCopie.ToString()
            For i As Integer = 1 To 10
                Dim id As String = i.ToString()
                menuCopieStampa.aggiungiElemento(id, id, String.Equals(attuale, id, StringComparison.Ordinal))
            Next
            Try
                menuCopieStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuCopieStampa.escPremuto, AddressOf ChiudiMenuCopieStampa
            AddHandler menuCopieStampa.ElementoSelezionato, AddressOf CopieStampaSelezionate
            menuCopieStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaCopieStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuCopieStampa()
        Try
            If menuCopieStampa IsNot Nothing Then menuCopieStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuCopieStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub CopieStampaSelezionate(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            Dim copie As Integer = 1
            Integer.TryParse(valore, copie)
            impostazioniComuni.StampaCopie = copie
            If menuCopieStampa IsNot Nothing Then menuCopieStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Copie impostate.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CopieStampaSelezionate eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaMargineStampa()
        Try
            menuMargineStampa = New WGMenuLista()
            menuMargineStampa.multiselezioneConsentita = False
            menuMargineStampa.impostaTitolo("Margine in millimetri")
            Dim attuale As String = impostazioniComuni.StampaMargineMillimetri.ToString()
            For Each mm As Integer In New Integer() {0, 5, 10, 15, 20, 25, 30, 40, 50}
                Dim id As String = mm.ToString()
                menuMargineStampa.aggiungiElemento(id & " millimetri", id, String.Equals(attuale, id, StringComparison.Ordinal))
            Next
            Try
                menuMargineStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuMargineStampa.escPremuto, AddressOf ChiudiMenuMargineStampa
            AddHandler menuMargineStampa.ElementoSelezionato, AddressOf MargineStampaSelezionato
            menuMargineStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaMargineStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuMargineStampa()
        Try
            If menuMargineStampa IsNot Nothing Then menuMargineStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuMargineStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MargineStampaSelezionato(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            Dim margine As Integer = 15
            Integer.TryParse(valore, margine)
            impostazioniComuni.StampaMargineMillimetri = margine
            If menuMargineStampa IsNot Nothing Then menuMargineStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Margine impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("MargineStampaSelezionato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaNomeCarattereStampa()
        Try
            menuNomeCarattereStampa = New WGMenuLista()
            menuNomeCarattereStampa.multiselezioneConsentita = False
            menuNomeCarattereStampa.impostaTitolo("Nome del carattere")
            Dim attuale As String = impostazioniComuni.StampaNomeCarattere
            Dim nomi As New List(Of String)
            Try
                Using collezione As New System.Drawing.Text.InstalledFontCollection()
                    If collezione.Families IsNot Nothing Then
                        For Each famiglia As FontFamily In collezione.Families
                            If famiglia Is Nothing OrElse String.IsNullOrWhiteSpace(famiglia.Name) Then Continue For
                            nomi.Add(famiglia.Name)
                        Next
                    End If
                End Using
            Catch exFont As Exception
                logger.ScriviLog("ImpostaNomeCarattereStampa font: " & exFont.Message)
            End Try
            If nomi.Count = 0 Then
                nomi.AddRange(New String() {"Arial", "Calibri", "Times New Roman", "Verdana", "Tahoma", "Courier New"})
            End If
            nomi.Sort(StringComparer.CurrentCultureIgnoreCase)
            For Each nome As String In nomi
                menuNomeCarattereStampa.aggiungiElemento(nome, nome, String.Equals(attuale, nome, StringComparison.OrdinalIgnoreCase))
            Next
            Try
                menuNomeCarattereStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuNomeCarattereStampa.escPremuto, AddressOf ChiudiMenuNomeCarattereStampa
            AddHandler menuNomeCarattereStampa.ElementoSelezionato, AddressOf NomeCarattereStampaSelezionato
            menuNomeCarattereStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaNomeCarattereStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuNomeCarattereStampa()
        Try
            If menuNomeCarattereStampa IsNot Nothing Then menuNomeCarattereStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuNomeCarattereStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub NomeCarattereStampaSelezionato(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            impostazioniComuni.StampaNomeCarattere = valore
            If menuNomeCarattereStampa IsNot Nothing Then menuNomeCarattereStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Carattere impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("NomeCarattereStampaSelezionato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaDimensioneCarattereStampa()
        Try
            menuDimensioneCarattereStampa = New WGMenuLista()
            menuDimensioneCarattereStampa.multiselezioneConsentita = False
            menuDimensioneCarattereStampa.impostaTitolo("Dimensione del carattere")
            Dim attuale As String = impostazioniComuni.StampaDimensioneCarattere.ToString()
            For Each punti As Integer In New Integer() {6, 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 28, 36}
                Dim id As String = punti.ToString()
                menuDimensioneCarattereStampa.aggiungiElemento(id, id, String.Equals(attuale, id, StringComparison.Ordinal))
            Next
            Try
                menuDimensioneCarattereStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuDimensioneCarattereStampa.escPremuto, AddressOf ChiudiMenuDimensioneCarattereStampa
            AddHandler menuDimensioneCarattereStampa.ElementoSelezionato, AddressOf DimensioneCarattereStampaSelezionata
            menuDimensioneCarattereStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaDimensioneCarattereStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuDimensioneCarattereStampa()
        Try
            If menuDimensioneCarattereStampa IsNot Nothing Then menuDimensioneCarattereStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuDimensioneCarattereStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DimensioneCarattereStampaSelezionata(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            Dim dimensione As Integer = 11
            Integer.TryParse(valore, dimensione)
            impostazioniComuni.StampaDimensioneCarattere = dimensione
            If menuDimensioneCarattereStampa IsNot Nothing Then menuDimensioneCarattereStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Dimensione del carattere impostata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DimensioneCarattereStampaSelezionata eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaOrientamentoStampa()
        Try
            menuOrientamentoStampa = New WGMenuLista()
            menuOrientamentoStampa.multiselezioneConsentita = False
            menuOrientamentoStampa.impostaTitolo("Orientamento della pagina")
            Dim attuale As String = impostazioniComuni.StampaOrientamento
            menuOrientamentoStampa.aggiungiElemento("Verticale", "verticale", String.Equals(attuale, "verticale", StringComparison.OrdinalIgnoreCase))
            menuOrientamentoStampa.aggiungiElemento("Orizzontale", "orizzontale", String.Equals(attuale, "orizzontale", StringComparison.OrdinalIgnoreCase))
            Try
                menuOrientamentoStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuOrientamentoStampa.escPremuto, AddressOf ChiudiMenuOrientamentoStampa
            AddHandler menuOrientamentoStampa.ElementoSelezionato, AddressOf OrientamentoStampaSelezionato
            menuOrientamentoStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaOrientamentoStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuOrientamentoStampa()
        Try
            If menuOrientamentoStampa IsNot Nothing Then menuOrientamentoStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuOrientamentoStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub OrientamentoStampaSelezionato(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            impostazioniComuni.StampaOrientamento = valore
            If menuOrientamentoStampa IsNot Nothing Then menuOrientamentoStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Orientamento impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("OrientamentoStampaSelezionato eccezione: " & ex.Message)
            Try
                If menuOrientamentoStampa IsNot Nothing Then menuOrientamentoStampa.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ImpostaFronteRetroStampa()
        Try
            menuFronteRetroStampa = New WGMenuLista()
            menuFronteRetroStampa.multiselezioneConsentita = False
            menuFronteRetroStampa.impostaTitolo("Fronte e retro")
            Dim attuale As String = impostazioniComuni.StampaFronteRetro
            menuFronteRetroStampa.aggiungiElemento("Una facciata", "una_facciata", String.Equals(attuale, "una_facciata", StringComparison.OrdinalIgnoreCase))
            menuFronteRetroStampa.aggiungiElemento("Fronte e retro", "fronte_retro", String.Equals(attuale, "fronte_retro", StringComparison.OrdinalIgnoreCase))
            Try
                menuFronteRetroStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuFronteRetroStampa.escPremuto, AddressOf ChiudiMenuFronteRetroStampa
            AddHandler menuFronteRetroStampa.ElementoSelezionato, AddressOf FronteRetroStampaSelezionato
            menuFronteRetroStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaFronteRetroStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuFronteRetroStampa()
        Try
            If menuFronteRetroStampa IsNot Nothing Then menuFronteRetroStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuFronteRetroStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub FronteRetroStampaSelezionato(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            impostazioniComuni.StampaFronteRetro = valore
            If menuFronteRetroStampa IsNot Nothing Then menuFronteRetroStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Fronte e retro impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("FronteRetroStampaSelezionato eccezione: " & ex.Message)
            Try
                If menuFronteRetroStampa IsNot Nothing Then menuFronteRetroStampa.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ImpostaColoreStampa()
        Try
            menuColoreStampa = New WGMenuLista()
            menuColoreStampa.multiselezioneConsentita = False
            menuColoreStampa.impostaTitolo("Colore di stampa")
            Dim attuale As String = impostazioniComuni.StampaColore
            menuColoreStampa.aggiungiElemento("Colore", "colore", String.Equals(attuale, "colore", StringComparison.OrdinalIgnoreCase))
            menuColoreStampa.aggiungiElemento("Bianco e nero", "bianco_nero", String.Equals(attuale, "bianco_nero", StringComparison.OrdinalIgnoreCase))
            Try
                menuColoreStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuColoreStampa.escPremuto, AddressOf ChiudiMenuColoreStampa
            AddHandler menuColoreStampa.ElementoSelezionato, AddressOf ColoreStampaSelezionato
            menuColoreStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaColoreStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuColoreStampa()
        Try
            If menuColoreStampa IsNot Nothing Then menuColoreStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuColoreStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ColoreStampaSelezionato(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            impostazioniComuni.StampaColore = valore
            If menuColoreStampa IsNot Nothing Then menuColoreStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Colore di stampa impostato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ColoreStampaSelezionato eccezione: " & ex.Message)
            Try
                If menuColoreStampa IsNot Nothing Then menuColoreStampa.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Sub ImpostaQualitaStampa()
        Try
            menuQualitaStampa = New WGMenuLista()
            menuQualitaStampa.multiselezioneConsentita = False
            menuQualitaStampa.impostaTitolo("Qualità di stampa")
            Dim attuale As String = impostazioniComuni.StampaQualita
            menuQualitaStampa.aggiungiElemento("Bozza", "bozza", String.Equals(attuale, "bozza", StringComparison.OrdinalIgnoreCase))
            menuQualitaStampa.aggiungiElemento("Normale", "normale", String.Equals(attuale, "normale", StringComparison.OrdinalIgnoreCase))
            menuQualitaStampa.aggiungiElemento("Alta", "alta", String.Equals(attuale, "alta", StringComparison.OrdinalIgnoreCase))
            Try
                menuQualitaStampa.posiziona(attuale)
            Catch
            End Try
            AddHandler menuQualitaStampa.escPremuto, AddressOf ChiudiMenuQualitaStampa
            AddHandler menuQualitaStampa.ElementoSelezionato, AddressOf QualitaStampaSelezionata
            menuQualitaStampa.avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaQualitaStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuQualitaStampa()
        Try
            If menuQualitaStampa IsNot Nothing Then menuQualitaStampa.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuQualitaStampa eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub QualitaStampaSelezionata(valore As String)
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return
            impostazioniComuni.StampaQualita = valore
            If menuQualitaStampa IsNot Nothing Then menuQualitaStampa.abbandona()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Qualità di stampa impostata.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("QualitaStampaSelezionata eccezione: " & ex.Message)
            Try
                If menuQualitaStampa IsNot Nothing Then menuQualitaStampa.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Sub SvuotaCodaStampaImpostazioni()
        Try
            If svuotamentoCodaInCorso Then Return

            Dim nome As String = impostazioniComuni.NomeStampanteDaUsare()
            If String.IsNullOrWhiteSpace(nome) Then
                Dim msgManca As New WGMessaggi()
                msgManca.Messaggio("Nessuna stampante impostata. Sceglila prima da questo menu.")
                msgManca.ShowDialog()
                Return
            End If

            svuotamentoCodaInCorso = True
            nomeStampanteSvuotamento = nome
            contestoSvuotamentoCoda = SynchronizationContext.Current

            Try
                processoSvuotamentoCoda = New WGProcessoInCorso()
                processoSvuotamentoCoda.Avvia("Svuotamento della coda di stampa in corso, attendere")
            Catch exProcesso As Exception
                logger.ScriviLog("SvuotaCodaStampaImpostazioni avvio processo eccezione: " & exProcesso.Message & Environment.NewLine & exProcesso.StackTrace)
            End Try

            Task.Run(New Action(AddressOf LavoroSvuotaCodaStampa))
        Catch ex As Exception
            logger.ScriviLog("SvuotaCodaStampaImpostazioni eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ChiudiProcessoSvuotamentoCoda()
            svuotamentoCodaInCorso = False
        End Try
    End Sub

    Private Sub LavoroSvuotaCodaStampa()
        Dim riuscito As Boolean = False
        Dim errore As String = ""
        Try
            riuscito = Global.WGStampante.WGStampante.SvuotaCodaStampa(nomeStampanteSvuotamento)
            If Not riuscito Then errore = Global.WGStampante.WGStampante.UltimoErrore()
        Catch ex As Exception
            logger.ScriviLog("LavoroSvuotaCodaStampa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            riuscito = False
            errore = ""
        Finally
            Try
                Dim esito As New Tuple(Of Boolean, String)(riuscito, If(errore, ""))
                Dim contesto As SynchronizationContext = contestoSvuotamentoCoda
                If contesto IsNot Nothing Then
                    contesto.Post(New SendOrPostCallback(AddressOf CompletaSvuotaCodaStampa), esito)
                Else
                    Dim contenitore As WGContenitore = WGContenitore.Istanza
                    If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                        contenitore.BeginInvoke(New SendOrPostCallback(AddressOf CompletaSvuotaCodaStampa), New Object() {esito})
                    Else
                        svuotamentoCodaInCorso = False
                    End If
                End If
            Catch exChiusura As Exception
                logger.ScriviLog("LavoroSvuotaCodaStampa chiusura eccezione: " & exChiusura.Message & Environment.NewLine & exChiusura.StackTrace)
                svuotamentoCodaInCorso = False
            End Try
        End Try
    End Sub

    Private Sub CompletaSvuotaCodaStampa(stato As Object)
        Try
            ChiudiProcessoSvuotamentoCoda()
            contestoSvuotamentoCoda = Nothing

            Dim esito As Tuple(Of Boolean, String) = TryCast(stato, Tuple(Of Boolean, String))
            Dim riuscito As Boolean = esito IsNot Nothing AndAlso esito.Item1
            Dim msg As New WGMessaggi()
            If riuscito Then
                msg.Messaggio("Coda di stampa svuotata.")
            Else
                Dim errore As String = If(esito IsNot Nothing, esito.Item2, "")
                If String.IsNullOrWhiteSpace(errore) Then errore = "Impossibile svuotare la coda di stampa."
                msg.Messaggio(errore)
            End If
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CompletaSvuotaCodaStampa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            svuotamentoCodaInCorso = False
        End Try
    End Sub

    Private Sub ChiudiProcessoSvuotamentoCoda()
        Try
            If processoSvuotamentoCoda Is Nothing Then Return
            Dim processo As WGProcessoInCorso = processoSvuotamentoCoda
            processoSvuotamentoCoda = Nothing
            processo.Fine()
        Catch ex As Exception
            logger.ScriviLog("ChiudiProcessoSvuotamentoCoda eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuStampante()
        Try
            If menuStampante IsNot Nothing Then menuStampante.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuStampante eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub StampanteImpostazioniSelezionata(nomeStampante As String)
        Try
            If String.IsNullOrWhiteSpace(nomeStampante) Then Return
            SalvaConfig("stampante_predefinita", nomeStampante)
            impostazioniComuni.StampantePredefinita = nomeStampante
            Dim msg As New WGMessaggi()
            msg.Messaggio("Stampante impostata.")
            msg.ShowDialog()
            If menuStampante IsNot Nothing Then menuStampante.abbandona()
        Catch ex As Exception
            logger.ScriviLog("StampanteImpostazioniSelezionata eccezione: " & ex.Message)
            Try
                If menuStampante IsNot Nothing Then menuStampante.abbandona()
            Catch
            End Try
        End Try
    End Sub

    Private Function LeggiModalitaLetturaNormalizzata() As String
        Try
            Dim modalita As String = configManager.LeggiValore("ModalitaLettura")
            If modalita = "ScreenReader" Then Return "Nessun lettore"
            Return modalita
        Catch ex As Exception
            logger.ScriviLog("LeggiModalitaLetturaNormalizzata eccezione: " & ex.Message)
            Return "Nessun lettore"
        End Try
    End Function

    Private Sub GestisciTipoSintesi()
        Try
            menutipo = New WGMenuLista()
            menutipo.multiselezioneConsentita = False
            menutipo.impostaTitolo("Scegli sistema di lettura")

            menutipo.aggiungiElemento("Usa eSpeak, la voce di WinGabriel" & If(WGEspeak.DaUsare(), ", attivo", ""), "espeak")
            menutipo.aggiungiElemento("Usa le voci di Windows, SAPI5" & If(WGEspeak.Motore() = "espeak", "", ", attivo"), "SAPI")
            menutipo.aggiungiElemento("Nessun sistema di sintesi vocale", "Nessun lettore")
            AddHandler menutipo.escPremuto, AddressOf ChiudiMenuTipo

            AddHandler menutipo.ElementoSelezionato, AddressOf TipoSelezionato
            menutipo.avvia()
        Catch ex As Exception
            logger.ScriviLog("GestisciTipoSintesi eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuTipo()
        Try
            If menutipo IsNot Nothing Then menutipo.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuTipo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ' eSpeak: se i file non ci sono li scarica, poi diventa la voce di WinGabriel.
    Private Async Sub ScegliEspeak()
        Try
            If Not WGEspeak.Installato() Then
                If Not Await WGEspeak.InstallaConAvvisiAsync() Then Return
            End If
            WGEspeak.RendiPredefinito(False)
            Dim msg As New WGMessaggi()
            msg.Messaggio("eSpeak attivato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ScegliEspeak eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub TipoSelezionato(scelta As String)
        Try
            If scelta = "espeak" Then
                menutipo.abbandona()
                ScegliEspeak()
                Return
            End If
            If Not String.IsNullOrEmpty(scelta) Then

                If scelta = "SAPI" Then
                    ' Le voci di Windows, per scelta: eSpeak non si scarica più da solo.
                    WGEspeak.UsaLeVociDiWindows()
                Else
                    SalvaConfig("ModalitaLettura", scelta)
                    sintesivocale.GetInstance().RicaricaConfigurazione()
                End If

                If scelta = "ScreenReader" Then
                    menutipo.abbandona()
                    AttivaNvdaConAttesa()
                Else
                    Dim msg As New WGMessaggi()
                    If scelta = "SAPI" Then
                        msg.Messaggio("Sintesi vocali SAPI5 attivate.")
                    Else
                        msg.Messaggio("Nessun sistema di sintesi vocale attivato.")
                    End If
                    msg.ShowDialog()
                    menutipo.abbandona()
                End If
            End If
        Catch ex As Exception
            logger.ScriviLog("TipoSelezionato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)

            menutipo.abbandona()
        End Try
    End Sub

    Private Sub AttivaNvdaConAttesa()
        Try
            processoincorso = New WGProcessoInCorso()
            Dim syncContext As SynchronizationContext = SynchronizationContext.Current

            Task.Run(Sub()
                         Dim attivo As Boolean = False
                         Try
                             Dim tentativi As Integer = 0
                             While tentativi < 60
                                 If sintesivocale.GetInstance().NvdaInEsecuzione() Then
                                     attivo = True
                                     Exit While
                                 End If
                                 Thread.Sleep(500)
                                 tentativi += 1
                             End While
                         Catch ex As Exception
                             logger.ScriviLog("AttivaNvdaConAttesa (Attesa) eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         End Try

                         Try
                             If syncContext IsNot Nothing Then
                                 syncContext.Post(Sub(state)
                                                      Try
                                                          If processoincorso IsNot Nothing Then processoincorso.Fine()

                                                          Dim msg As New WGMessaggi()
                                                          If attivo Then
                                                              msg.Messaggio("Modalità screen reader NVDA attivata.")
                                                          Else
                                                              msg.Messaggio("Impossibile attivare NVDA.")
                                                          End If
                                                          msg.ShowDialog()
                                                      Catch ex As Exception
                                                          logger.ScriviLog("AttivaNvdaConAttesa (Post) eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                                                      End Try
                                                  End Sub, Nothing)
                             End If
                         Catch ex As Exception
                             logger.ScriviLog("AttivaNvdaConAttesa (SyncContext) eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
                         End Try
                     End Sub)

            processoincorso.Avvia("Attivazione nvda in corso, attendere")
        Catch ex As Exception
            logger.ScriviLog("AttivaNvdaConAttesa eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                If processoincorso IsNot Nothing Then processoincorso.Fine()
            Catch exFine As Exception
                logger.ScriviLog("AttivaNvdaConAttesa (Fine) eccezione: " & exFine.Message & Environment.NewLine & exFine.StackTrace)
            End Try
        End Try
    End Sub
    Private Sub GestisciSelezioneVoce()
        Try
            Dim modalita As String = LeggiModalitaLetturaNormalizzata()
            If Not _configuraSintesiMp3 AndAlso modalita = "Nessun lettore" Then
                Dim msgControllo As New WGMessaggi()
                msgControllo.Messaggio("Nessun sistema di sintesi vocale attivo.")
                msgControllo.ShowDialog()
                Return
            End If

            If _configuraSintesiMp3 AndAlso sintesivocale.GetInstance().UsaEspeak Then
                Dim msgEspeak As New WGMessaggi()
                msgEspeak.Messaggio("La conversione in mp3 usa le voci di Windows. Scegli prima le voci di Windows nel sistema di lettura.")
                msgEspeak.ShowDialog()
                Return
            End If

            Dim sintesiDisponibili As List(Of Sintesi)
            If _configuraSintesiMp3 Then
                sintesiDisponibili = sintesivocale.GetInstance().OttieniSintesiSapi()
            Else
                sintesiDisponibili = sintesivocale.GetInstance().OttieniSintesiAttive()
            End If
            If sintesiDisponibili Is Nothing OrElse sintesiDisponibili.Count = 0 Then
                Dim msg As New WGMessaggi()
                If Not _configuraSintesiMp3 AndAlso modalita = "ScreenReader" Then
                    msg.Messaggio("Nessuna sintesi vocale NVDA rilevata." & Environment.NewLine & "Assicurati che NVDA sia in esecuzione.")
                Else
                    msg.Messaggio("Nessuna sintesi vocale disponibile.")
                End If
                msg.ShowDialog()
                Return
            End If

            menuvoce = New WGMenuLista()
            menuvoce.multiselezioneConsentita = False
            If _configuraSintesiMp3 Then
                menuvoce.impostaTitolo("Voce per la conversione in mp3")
            ElseIf modalita = "ScreenReader" Then
                menuvoce.impostaTitolo("Sintesi vocali disponibili in nvda")
            Else
                menuvoce.impostaTitolo("Sintesi vocali disponibili")
            End If

            Dim idVoceAttiva As String = ""
            If _configuraSintesiMp3 Then
                idVoceAttiva = impostazioniComuni.Mp3Voce
            End If
            For Each voceItem As Sintesi In sintesiDisponibili
                Dim testoVoce As String = voceItem.Nome
                If _configuraSintesiMp3 Then
                    If Not String.IsNullOrEmpty(idVoceAttiva) AndAlso voceItem.Id = idVoceAttiva Then
                        testoVoce &= " attivo"
                    End If
                ElseIf voceItem.Attiva Then
                    testoVoce &= " attivo"
                    idVoceAttiva = voceItem.Id
                End If
                menuvoce.aggiungiElemento(testoVoce, voceItem.Id)
            Next

            If Not String.IsNullOrEmpty(idVoceAttiva) Then
                Try
                    menuvoce.posiziona(idVoceAttiva)
                Catch ex As Exception
                    logger.ScriviLog("GestisciSelezioneVoce posiziona eccezione: " & ex.Message)
                End Try
            End If

            AddHandler menuvoce.escPremuto, AddressOf ChiudiMenuVoce
            AddHandler menuvoce.ElementoSelezionato, AddressOf VoceSelezionata
            menuvoce.avvia()
        Catch ex As Exception
            logger.ScriviLog("Errore durante il recupero delle voci: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuVoce()
        Try
            If menuvoce IsNot Nothing Then menuvoce.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuVoce eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private _menuPercentualeAffinataCorrente As WGMenuLista
    Private _valorePercentualeAffinataCorrente As Integer

    Private Sub OpzioneAffinataPercentualeGenerica(sender As Object, e As EventArgs)
        Try
            RicaricaAffinata(_menuPercentualeAffinataCorrente, _valorePercentualeAffinataCorrente)
        Catch ex As Exception
            logger.ScriviLog("OpzioneAffinataPercentualeGenerica eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub PopolaMenuPercentuali(menu As WGMenuLista, valoreAttuale As Integer)
        Try
            For i As Integer = 0 To 100 Step 10
                menu.aggiungiElemento(i.ToString() & " %", i.ToString())
            Next

            _menuPercentualeAffinataCorrente = menu
            _valorePercentualeAffinataCorrente = valoreAttuale
            menu.aggiungiOpzione("Regolazione affinata", AddressOf OpzioneAffinataPercentualeGenerica)

            If valoreAttuale >= 0 AndAlso valoreAttuale <= 100 Then
                Try
                    Dim percentualeArrotondata As Integer = CInt(Math.Round(valoreAttuale / 10.0)) * 10
                    percentualeArrotondata = Math.Max(0, Math.Min(100, percentualeArrotondata))
                    menu.posiziona(percentualeArrotondata.ToString())
                Catch ex As Exception
                    logger.ScriviLog("PopolaMenuPercentuali posiziona eccezione: " & ex.Message)
                End Try
            End If
        Catch ex As Exception
            logger.ScriviLog("PopolaMenuPercentuali eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub
    Private Sub VoceSelezionata(idVoce As String)
        Try
            If String.IsNullOrEmpty(idVoce) Then Return

            Dim modalita As String = LeggiModalitaLetturaNormalizzata()

            If _configuraSintesiMp3 Then
                SalvaConfig("mp3_voce", idVoce)
            ElseIf modalita = "SAPI" Then
                SalvaConfig("SapiVoice", idVoce)
                sintesivocale.GetInstance().ApplicaImpostazioniSapi()
            End If

            Dim msgSapi As New WGMessaggi()
            msgSapi.Messaggio("La voce è stata impostata.")
            msgSapi.ShowDialog()
            menuvoce.abbandona()
        Catch ex As Exception
            logger.ScriviLog("VoceSelezionata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            menuvoce.abbandona()
        End Try
    End Sub
    Private Sub GestisciVelocitaVoce()
        Dim modalitaControllo As String = LeggiModalitaLetturaNormalizzata()
        If Not _configuraSintesiMp3 AndAlso modalitaControllo = "Nessun lettore" Then
            Dim msgControllo As New WGMessaggi()
            msgControllo.Messaggio("Nessun sistema di sintesi vocale attivo.")
            msgControllo.ShowDialog()
            Return
        End If

        menuvelocita = New WGMenuLista()
        menuvelocita.multiselezioneConsentita = False
        If _configuraSintesiMp3 Then
            menuvelocita.impostaTitolo("Velocità voce per la conversione in mp3")
        Else
            menuvelocita.impostaTitolo("Scegli la velocità della voce")
        End If

        Dim valoreCorrenteRate As Integer = -1
        If _configuraSintesiMp3 Then
            Integer.TryParse(impostazioniComuni.Mp3Velocita, valoreCorrenteRate)
        ElseIf modalitaControllo = "ScreenReader" Then
            valoreCorrenteRate = sintesivocale.GetInstance().OttieniUltimaVelocitaNvda()
        ElseIf modalitaControllo = "SAPI" Then
            Integer.TryParse(configManager.LeggiValore("SapiRate"), valoreCorrenteRate)
        End If

        PopolaMenuPercentuali(menuvelocita, valoreCorrenteRate)

        If _configuraSintesiMp3 Then
            Try
                _conversioneMp3Anteprima = New WGConversioneMp3()
            Catch ex As Exception
                logger.ScriviLog("GestisciVelocitaVoce anteprima mp3 eccezione: " & ex.Message)
            End Try
        Else
            Try
                sintesivocale.GetInstance().AvviaAnteprimaImpostazioni()
            Catch ex As Exception
                logger.ScriviLog("GestisciVelocitaVoce anteprima eccezione: " & ex.Message)
            End Try
        End If

        AddHandler menuvelocita.escPremuto, AddressOf ChiudiMenuVelocita
        AddHandler menuvelocita.ElementoSelezionato, AddressOf VelocitaSelezionata
        AddHandler menuvelocita.VoceCambiata, AddressOf AnteprimaVelocitaVoce
        menuvelocita.avvia()
    End Sub

    Private Sub ChiudiMenuVelocita()
        If _configuraSintesiMp3 Then
            Try
                If _conversioneMp3Anteprima IsNot Nothing Then _conversioneMp3Anteprima.FermaAnteprimaVoceMp3()
            Catch ex As Exception
                logger.ScriviLog("ChiudiMenuVelocita anteprima mp3 eccezione: " & ex.Message)
            End Try
        Else
            Try
                sintesivocale.GetInstance().AnnullaAnteprimaImpostazioni()
            Catch ex As Exception
                logger.ScriviLog("ChiudiMenuVelocita eccezione: " & ex.Message)
            End Try
        End If
        menuvelocita.abbandona()
    End Sub

    Private Sub AnteprimaVelocitaVoce(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Return
            Dim valore As Integer = -1
            If Not Integer.TryParse(e.IdVoce, valore) Then Return
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            If _configuraSintesiMp3 Then
                If _conversioneMp3Anteprima IsNot Nothing Then
                    Dim volumeCorrente As Integer = 80
                    Integer.TryParse(impostazioniComuni.Mp3Volume, volumeCorrente)
                    Dim indiceVoceFallback As Integer = 0
                    Integer.TryParse(impostazioniComuni.Mp3Voce, indiceVoceFallback)
                    _conversioneMp3Anteprima.AnteprimaVoceMp3(impostazioniComuni.Mp3Voce, indiceVoceFallback, valore, volumeCorrente)
                End If
            Else
                sintesivocale.GetInstance().AnteprimaVelocita(valore)
            End If
        Catch ex As Exception
            logger.ScriviLog("AnteprimaVelocitaVoce eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub VelocitaSelezionata()
        Try
            Dim ids As List(Of String) = menuvelocita.ottieniElementi()
            If ids.Count = 0 Then Return

            Dim velocita As Integer = 0
            If Not Integer.TryParse(ids(0), velocita) Then Return
            If velocita < 0 Then velocita = 0
            If velocita > 100 Then velocita = 100

            If _configuraSintesiMp3 Then
                Try
                    If _conversioneMp3Anteprima IsNot Nothing Then _conversioneMp3Anteprima.FermaAnteprimaVoceMp3()
                Catch exAnteprima As Exception
                    logger.ScriviLog("VelocitaSelezionata anteprima mp3 eccezione: " & exAnteprima.Message)
                End Try
            Else
                Try
                    sintesivocale.GetInstance().ConfermaAnteprimaImpostazioni()
                Catch exAnteprima As Exception
                    logger.ScriviLog("VelocitaSelezionata conferma anteprima eccezione: " & exAnteprima.Message)
                End Try
            End If

            Dim modalita As String = LeggiModalitaLetturaNormalizzata()
            If _configuraSintesiMp3 Then
                SalvaConfig("mp3_velocita", velocita.ToString())
            ElseIf modalita = "SAPI" Then
                SalvaConfig("SapiRate", velocita.ToString())
                sintesivocale.GetInstance().ApplicaImpostazioniSapi()
            End If

            Dim msgSapi As New WGMessaggi()
            msgSapi.Messaggio("Velocità voce aggiornata.")
            msgSapi.ShowDialog()
            menuvelocita.abbandona()
        Catch ex As Exception
            logger.ScriviLog("VelocitaSelezionata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            menuvelocita.abbandona()
        End Try
    End Sub

    Private Sub GestisciVolumeVoce()
        Dim modalitaControllo As String = LeggiModalitaLetturaNormalizzata()
        If Not _configuraSintesiMp3 AndAlso modalitaControllo = "Nessun lettore" Then
            Dim msgControllo As New WGMessaggi()
            msgControllo.Messaggio("Nessun sistema di sintesi vocale attivo.")
            msgControllo.ShowDialog()
            Return
        End If

        menuvolume = New WGMenuLista()
        menuvolume.multiselezioneConsentita = False
        If _configuraSintesiMp3 Then
            menuvolume.impostaTitolo("Volume voce per la conversione in mp3")
        Else
            menuvolume.impostaTitolo("Scegli il volume della voce")
        End If

        Dim valoreCorrenteVolume As Integer = -1
        If _configuraSintesiMp3 Then
            Integer.TryParse(impostazioniComuni.Mp3Volume, valoreCorrenteVolume)
        ElseIf modalitaControllo = "ScreenReader" Then
            valoreCorrenteVolume = sintesivocale.GetInstance().OttieniUltimoVolumeNvda()
        ElseIf modalitaControllo = "SAPI" Then
            Integer.TryParse(configManager.LeggiValore("SapiVolume"), valoreCorrenteVolume)
        End If

        PopolaMenuPercentuali(menuvolume, valoreCorrenteVolume)

        If _configuraSintesiMp3 Then
            Try
                _conversioneMp3Anteprima = New WGConversioneMp3()
            Catch ex As Exception
                logger.ScriviLog("GestisciVolumeVoce anteprima mp3 eccezione: " & ex.Message)
            End Try
        Else
            Try
                sintesivocale.GetInstance().AvviaAnteprimaImpostazioni()
            Catch ex As Exception
                logger.ScriviLog("GestisciVolumeVoce anteprima eccezione: " & ex.Message)
            End Try
        End If

        AddHandler menuvolume.escPremuto, AddressOf ChiudiMenuVolume
        AddHandler menuvolume.ElementoSelezionato, AddressOf VolumeSelezionata
        AddHandler menuvolume.VoceCambiata, AddressOf AnteprimaVolumeVoce
        menuvolume.avvia()
    End Sub

    Private Sub ChiudiMenuVolume()
        If _configuraSintesiMp3 Then
            Try
                If _conversioneMp3Anteprima IsNot Nothing Then _conversioneMp3Anteprima.FermaAnteprimaVoceMp3()
            Catch ex As Exception
                logger.ScriviLog("ChiudiMenuVolume anteprima mp3 eccezione: " & ex.Message)
            End Try
        Else
            Try
                sintesivocale.GetInstance().AnnullaAnteprimaImpostazioni()
            Catch ex As Exception
                logger.ScriviLog("ChiudiMenuVolume eccezione: " & ex.Message)
            End Try
        End If
        menuvolume.abbandona()
    End Sub

    Private Sub AnteprimaVolumeVoce(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Return
            Dim valore As Integer = -1
            If Not Integer.TryParse(e.IdVoce, valore) Then Return
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            If _configuraSintesiMp3 Then
                If _conversioneMp3Anteprima IsNot Nothing Then
                    Dim rateCorrente As Integer = 50
                    Integer.TryParse(impostazioniComuni.Mp3Velocita, rateCorrente)
                    Dim indiceVoceFallback As Integer = 0
                    Integer.TryParse(impostazioniComuni.Mp3Voce, indiceVoceFallback)
                    _conversioneMp3Anteprima.AnteprimaVoceMp3(impostazioniComuni.Mp3Voce, indiceVoceFallback, rateCorrente, valore)
                End If
            Else
                sintesivocale.GetInstance().AnteprimaVolume(valore, True)
            End If
        Catch ex As Exception
            logger.ScriviLog("AnteprimaVolumeVoce eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub VolumeSelezionata()
        Try
            Dim ids As List(Of String) = menuvolume.ottieniElementi()
            If ids.Count = 0 Then Return

            Dim vol As Integer = 0
            If Not Integer.TryParse(ids(0), vol) Then Return
            If vol < 0 Then vol = 0
            If vol > 100 Then vol = 100

            If _configuraSintesiMp3 Then
                Try
                    If _conversioneMp3Anteprima IsNot Nothing Then _conversioneMp3Anteprima.FermaAnteprimaVoceMp3()
                Catch exAnteprima As Exception
                    logger.ScriviLog("VolumeSelezionata anteprima mp3 eccezione: " & exAnteprima.Message)
                End Try
            Else
                Try
                    sintesivocale.GetInstance().ConfermaAnteprimaImpostazioni()
                Catch exAnteprima As Exception
                    logger.ScriviLog("VolumeSelezionata conferma anteprima eccezione: " & exAnteprima.Message)
                End Try
            End If

            Dim modalita As String = LeggiModalitaLetturaNormalizzata()
            If _configuraSintesiMp3 Then
                SalvaConfig("mp3_volume", vol.ToString())
            ElseIf modalita = "SAPI" Then
                SalvaConfig("SapiVolume", vol.ToString())
                sintesivocale.GetInstance().ApplicaImpostazioniSapi()
            End If

            Dim msgSapi As New WGMessaggi()
            msgSapi.Messaggio("Volume voce aggiornato.")
            msgSapi.ShowDialog()

            menuvolume.abbandona()
        Catch ex As Exception
            logger.ScriviLog("VolumeSelezionata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            menuvolume.abbandona()
        End Try
    End Sub

    Private Sub MostraMenuConfigurazioniMenu()
        Dim menuConfig As New WGMenu()
        menuConfig.aggiungi("Imposta tipo di carattere", AddressOf ScegliFontMenu)
        menuConfig.aggiungi("Imposta dimensione carattere", AddressOf ScegliDimensioneMenu)
        menuConfig.aggiungi("Gestisci navigazione ciclica", AddressOf MostraMenuCicloLista)
        menuConfig.aggiungi("Lettura titolo menù", AddressOf MostraMenuLetturaTitolo)
        menuConfig.aggiungi("Lettura numero voci", AddressOf MostraMenuLetturaNumeroVoci)
        menuConfig.avvia("Menu")
    End Sub

    ' Impostazioni della modalità smart: il sistema a applicazioni separate e le sue novità.
    Private Sub MostraMenuSistema()
        Try
            Dim menuSistema As New WGMenu()
            menuSistema.aggiungi("Modalità: " & WGModalita.Nome, AddressOf MostraMenuModalita)
            menuSistema.aggiungi("Applicazioni separate, Control freccia destra e sinistra per passare dall'una all'altra: " & If(WGContenitore.ApplicazioniSeparate, "attive", "disattivate"), AddressOf ScegliApplicazioniSeparate)
            menuSistema.aggiungi("Volume della musica mentre parla la sintesi: " & DescriviVolumeDuranteSintesi(WGRiproduzione.PercentualeDuranteSintesi), AddressOf ScegliVolumeDuranteSintesi)
            menuSistema.aggiungi("Riapri all'avvio le applicazioni lasciate aperte: " & If(WGSessione.Attiva, "sì", "no"), AddressOf ScegliRipresaSessione)
            menuSistema.aggiungi("Avvisi di connessione, memoria e disco", Sub() WGAvvisiSistema.MostraImpostazioni())
            menuSistema.aggiungi("Conferma dell'uscita con applicazioni aperte: " & If(WGContenitore.ConfermaUscitaConApplicazioni, "sì", "no"), AddressOf ScegliConfermaUscita)
            menuSistema.aggiungi("Notifiche, non disturbare: " & WGNotifiche.WGCentroNotifiche.DescriviNonDisturbare(), Sub() WGNotifiche.WGCentroNotifiche.ScegliNonDisturbare())
            menuSistema.aggiungi("Suono delle notifiche per applicazione", Sub() WGNotifiche.WGCentroNotifiche.ScegliSuoni())
            menuSistema.aggiungi("Prova guidata dei tasti", Sub() WGAccoglienza.AvviaProva())
            menuSistema.aggiungi("Guida delle applicazioni", Sub() WGContenitore.MostraGuidaApplicazioni())
            menuSistema.avvia("Sistema")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSistema eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuImpostazioniScrivania()
        Try
            Dim menuScrivania As New WGMenu()
            menuScrivania.aggiungi("Scrivania all'avvio e con Control Alt D: " & If(WGScrivania.Attiva, "attiva", "disattivata"), AddressOf ScegliScrivania)
            menuScrivania.avvia("Scrivania")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuImpostazioniScrivania eccezione: " & ex.Message)
        End Try
    End Sub

    ' Classica o smart: vale dal prossimo avvio, perché applicazioni e finestre si organizzano secondo la modalità.
    Private Sub MostraMenuModalita()
        Try
            Dim menuModalita As New WGMenu()
            menuModalita.aggiungi("Smart" & If(WGModalita.SmartAlProssimoAvvio, ", scelta", ""), Sub() ScegliModalita(True))
            menuModalita.aggiungi("Classica" & If(WGModalita.SmartAlProssimoAvvio, "", ", scelta"), Sub() ScegliModalita(False))
            menuModalita.posiziona(If(WGModalita.SmartAlProssimoAvvio, 0, 1))
            menuModalita.avvia("Modalità", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuModalita eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliModalita(smart As Boolean)
        Try
            WGModalita.ScegliAlProssimoAvvio(smart)
            Dim messaggio As New WGMessaggi()
            Dim descrizione As String = If(smart, WGModalita.DescrizioneSmart, WGModalita.DescrizioneClassica)
            If smart = WGModalita.Smart Then
                messaggio.Messaggio(descrizione)
            Else
                messaggio.Messaggio(descrizione & " Vale dal prossimo avvio di WinGabriel.")
            End If
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ScegliModalita eccezione: " & ex.Message)
        End Try
    End Sub

    ' Uscendo con applicazioni aperte: chiedere conferma (con i loro nomi) oppure chiuderle direttamente.
    Private Sub ScegliConfermaUscita()
        Try
            Dim menuConferma As New WGMenu()
            menuConferma.aggiungi("No, chiudi le applicazioni aperte senza chiedere, salvo i testi modificati", Sub()
                                                                                                                    WGContenitore.ConfermaUscitaConApplicazioni = False
                                                                                                                    sintesivocale.GetInstance().LeggiTesto("Conferma dell'uscita disattivata")
                                                                                                                End Sub)
            menuConferma.aggiungi("Sì, chiedi conferma se c'è almeno un'applicazione aperta", Sub()
                                                                                                  WGContenitore.ConfermaUscitaConApplicazioni = True
                                                                                                  sintesivocale.GetInstance().LeggiTesto("Conferma dell'uscita attivata")
                                                                                              End Sub)
            menuConferma.posiziona(If(WGContenitore.ConfermaUscitaConApplicazioni, 1, 0))
            menuConferma.avvia("Conferma uscita", True)
        Catch ex As Exception
            logger.ScriviLog("ScegliConfermaUscita eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliRipresaSessione()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Sì", Sub(s, e)
                                     WGSessione.Attiva = True
                                     Dim messaggio As New WGMessaggi()
                                     messaggio.Messaggio("Ripresa attivata: al prossimo avvio si riaprono le applicazioni lasciate aperte, anche dopo un blocco del programma. I menu si riaprono fino al punto più interno raggiunto, i file e i collegamenti della scrivania come erano.")
                                     messaggio.ShowDialog()
                                 End Sub)
            menu.aggiungi("No", Sub(s, e)
                                     WGSessione.Attiva = False
                                     Dim messaggio As New WGMessaggi()
                                     messaggio.Messaggio("Ripresa disattivata: all'avvio si parte dalla scrivania.")
                                     messaggio.ShowDialog()
                                 End Sub)
            menu.posiziona(If(WGSessione.Attiva, 0, 1))
            menu.avvia("Riapri all'avvio le applicazioni lasciate aperte", True)
        Catch ex As Exception
            logger.ScriviLog("ScegliRipresaSessione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliScrivania()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attiva", Sub(s, e)
                                         WGScrivania.Attiva = True
                                         Dim messaggio As New WGMessaggi()
                                         messaggio.Messaggio("Scrivania attivata: è la prima cosa che si vede all'avvio, ci si torna con Control Alt D e con Esc dal menu applicazioni.")
                                         messaggio.ShowDialog()
                                     End Sub)
            menu.aggiungi("Disattivata", Sub(s, e)
                                              WGScrivania.Attiva = False
                                              Dim messaggio As New WGMessaggi()
                                              messaggio.Messaggio("Scrivania disattivata: all'avvio si apre il menu e Esc dal menu chiede di uscire, come nel WinGabriel classico. I collegamenti restano salvati.")
                                              messaggio.ShowDialog()
                                          End Sub)
            menu.posiziona(If(WGScrivania.Attiva, 0, 1))
            menu.avvia("Scrivania all'avvio e con Control Alt D", True)
        Catch ex As Exception
            logger.ScriviLog("ScegliScrivania eccezione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function DescriviVolumeDuranteSintesi(percentuale As Integer) As String
        Return If(percentuale >= 100, "non abbassare", percentuale & " per cento")
    End Function

    Private Sub ScegliVolumeDuranteSintesi()
        Try
            Dim menuVolume As New WGMenu()
            For Each percentuale As Integer In {100, 70, 50, 35, 20, 10}
                Dim scelta As Integer = percentuale
                menuVolume.aggiungi(If(scelta >= 100, "Non abbassare la musica", "Abbassa al " & scelta & " per cento"), Sub(s, e)
                                                                                                                             WGRiproduzione.PercentualeDuranteSintesi = scelta
                                                                                                                             Dim conferma As New WGMessaggi()
                                                                                                                             conferma.Messaggio("Volume della musica mentre parla la sintesi: " & DescriviVolumeDuranteSintesi(scelta) & ".")
                                                                                                                             conferma.ShowDialog()
                                                                                                                         End Sub)
            Next
            menuVolume.avvia("Volume durante la sintesi", True)
        Catch ex As Exception
            logger.ScriviLog("ScegliVolumeDuranteSintesi eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliApplicazioniSeparate()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Attive", Sub(s, e)
                                         WGContenitore.ApplicazioniSeparate = True
                                         Dim messaggio As New WGMessaggi()
                                         messaggio.Messaggio("Applicazioni separate attivate: ogni voce del menu applicazioni apre un'applicazione a sé e con Control freccia destra e sinistra si passa dall'una all'altra. Vale dalla prossima applicazione aperta.")
                                         messaggio.ShowDialog()
                                     End Sub)
            menu.aggiungi("Disattivate", Sub(s, e)
                                              WGContenitore.ApplicazioniSeparate = False
                                              Dim messaggio As New WGMessaggi()
                                              messaggio.Messaggio("Applicazioni separate disattivate: le finestre tornano una sopra l'altra e Control freccia destra e sinistra tornano a spostarsi di una parola nei testi.")
                                              messaggio.ShowDialog()
                                          End Sub)
            menu.posiziona(If(WGContenitore.ApplicazioniSeparate, 0, 1))
            menu.avvia("Applicazioni separate", True)
        Catch ex As Exception
            logger.ScriviLog("ScegliApplicazioniSeparate eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliFontMenu()
        menufont = New WGMenuLista()
        menufont.multiselezioneConsentita = False

        menufont.impostaTitolo("Scegli il carattere per i menù")
        menufont.aggiungiElemento("Segoe UI", "Segoe UI")
        menufont.aggiungiElemento("Verdana", "Verdana")
        menufont.aggiungiElemento("Arial", "Arial")
        menufont.aggiungiElemento("Courier New", "Courier New")
        menufont.aggiungiElemento("Times New Roman", "Times New Roman")
        AddHandler menufont.escPremuto, AddressOf ChiudiMenuFont

        AddHandler menufont.ElementoSelezionato, AddressOf FontSelezionato
        menufont.posiziona(LeggiConfigValore("MenuCarattere", "Segoe UI"))
        menufont.avvia()
    End Sub

    Private Sub ChiudiMenuFont()
        Try
            If menufont IsNot Nothing Then menufont.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuFont eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub FontSelezionato()
        Dim ids As List(Of String) = menufont.ottieniElementi()

        If ids.Count > 0 Then
            Dim fontScelto As String = ids(0).ToString()
            SalvaConfig("MenuCarattere", fontScelto)

            Dim msg As New WGMessaggi()
            msg.Messaggio("Il carattere è stato modificato in " & fontScelto)

            msg.ShowDialog()
            menufont.abbandona()
        End If
    End Sub

    Private Sub ScegliDimensioneMenu()
        menudimensione = New WGMenuLista()
        menudimensione.multiselezioneConsentita = False
        menudimensione.impostaTitolo("Scegli la dimensione del testo")

        menudimensione.aggiungiElemento("Piccola (14)", "14")
        menudimensione.aggiungiElemento("Media (18)", "18")
        menudimensione.aggiungiElemento("Grande (24)", "24")
        menudimensione.aggiungiElemento("Molto grande (32)", "32")
        menudimensione.aggiungiElemento("Gigante (48)", "48")
        AddHandler menudimensione.escPremuto, AddressOf ChiudiMenuDimensione
        AddHandler menudimensione.ElementoSelezionato, AddressOf DimensioneSelezionata
        menudimensione.posiziona(LeggiConfigValore("FontDimensione", "18"))
        menudimensione.avvia()
    End Sub

    Private Sub ChiudiMenuDimensione()
        Try
            If menudimensione IsNot Nothing Then menudimensione.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuDimensione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DimensioneSelezionata()
        Dim ids As List(Of String) = menudimensione.ottieniElementi()
        If ids.Count > 0 Then

            Dim dimScelta As String = ids(0).ToString()
            SalvaConfig("FontDimensione", dimScelta)

            Dim msg As New WGMessaggi()
            msg.Messaggio("La dimensione del testo è stata modificata.")
            msg.ShowDialog()

            menudimensione.abbandona()
        End If
    End Sub

    Private Sub MostraMenuCicloLista()
        Dim menuCiclo As New WGMenu()
        menuCiclo.aggiungi("Attiva navigazione ciclica", Sub() SalvaConfigurazioneGenerica("CicloLista", "true", "Navigazione ciclica attivata."))
        menuCiclo.aggiungi("Disattiva navigazione ciclica", Sub() SalvaConfigurazioneGenerica("CicloLista", "false", "Navigazione ciclica disattivata. La selezione si fermerà ai bordi del menù."))
        menuCiclo.posiziona(IndiceBinario("CicloLista", "true", "true"))
        menuCiclo.avvia("Gestisci navigazione ciclica", True)
    End Sub

    Private Sub MostraMenuLetturaTitolo()
        Dim menuLettura As New WGMenu()
        menuLettura.aggiungi("Attiva lettura del titolo", Sub() SalvaConfigurazioneGenerica("LeggiTitoloIngresso", "true", "La lettura del titolo all'apertura dei menù è attiva."))
        menuLettura.aggiungi("Disattiva lettura del titolo", Sub() SalvaConfigurazioneGenerica("LeggiTitoloIngresso", "false", "La lettura del titolo all'apertura dei menù è disattivata."))
        menuLettura.posiziona(IndiceBinario("LeggiTitoloIngresso", "true", "true"))
        menuLettura.avvia("Lettura titolo menù", True)
    End Sub

    Private Sub MostraMenuLetturaNumeroVoci()
        Dim menuVoci As New WGMenu()
        menuVoci.aggiungi("Attiva lettura numero voci", Sub() SalvaConfigurazioneGenerica("LeggiNumeroVoci", "true", "La lettura del numero di voci all'apertura dei menù è attiva."))
        menuVoci.aggiungi("Disattiva lettura numero voci", Sub() SalvaConfigurazioneGenerica("LeggiNumeroVoci", "false", "La lettura del numero di voci all'apertura dei menù è disattivata."))
        menuVoci.posiziona(IndiceBinario("LeggiNumeroVoci", "true", "true"))
        menuVoci.avvia("Lettura numero voci", True)
    End Sub

    Private Function LeggiConfigValore(chiave As String, defaultVal As String) As String
        Try
            Dim v As String = configManager.LeggiValore(chiave)
            If String.IsNullOrWhiteSpace(v) Then Return defaultVal
            Return v.Trim()
        Catch ex As Exception
            logger.ScriviLog("LeggiConfigValore eccezione: " & ex.Message)
            Return defaultVal
        End Try
    End Function

    Private Function IndiceLivelloPunteggiatura() As Integer
        Try
            Dim impostazioni As New WGImpostazioniComuni()
            Return impostazioni.LivelloLetturaPunteggiatura
        Catch ex As Exception
            logger.ScriviLog("IndiceLivelloPunteggiatura eccezione: " & ex.Message)
            Return 3
        End Try
    End Function

    Private Function IndiceLivelloPunteggiaturaScrittura() As Integer
        Try
            Dim impostazioni As New WGImpostazioniComuni()
            Return impostazioni.LivelloScritturaPunteggiatura
        Catch ex As Exception
            logger.ScriviLog("IndiceLivelloPunteggiaturaScrittura eccezione: " & ex.Message)
            Return 3
        End Try
    End Function

    ' La finestra delle impostazioni del lettore è del lettore stesso (un programma a parte): se non è avviato, lo si avvia.
    Private Sub ApriFinestraImpostazioniLettore()
        Try
            If Not LettoreEsterno.AvviaOAggiorna(True) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Il programma del lettore di schermo, WGLettore.exe, non si trova accanto a WinGabriel né installato.")
                msg.ShowDialog()
                Return
            End If
            Dim msg2 As New WGMessaggi()
            msg2.Messaggio("Ho aperto la finestra delle impostazioni del lettore. Il lettore parla quando WinGabriel passa in secondo piano.")
            msg2.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ApriFinestraImpostazioniLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuAvvioAutomaticoLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivo", Sub() ImpostaAvvioAutomaticoLettore(True))
            m.aggiungi("Disattivo", Sub() ImpostaAvvioAutomaticoLettore(False))
            m.posiziona(If(LettoreEsterno.AvvioAutomaticoAttivo(), 0, 1))
            m.avvia("Avvio automatico del lettore", True)
        Catch ex As Exception
            logger.ScriviLog("MenuAvvioAutomaticoLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaAvvioAutomaticoLettore(attivo As Boolean)
        Try
            Dim msg As New WGMessaggi()
            If LettoreEsterno.ImpostaAvvioAutomatico(attivo) Then
                msg.Messaggio(If(attivo, "Il lettore di schermo partirà da solo quando accedi a Windows. All'accesso non chiede i privilegi di amministratore, quindi non potrà leggere le applicazioni avviate come amministratore.", "Il lettore di schermo non partirà più da solo quando accedi a Windows."))
            Else
                msg.Messaggio("Non è stato possibile cambiare l'avvio automatico del lettore: il programma WGLettore.exe non si trova.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ImpostaAvvioAutomaticoLettore eccezione: " & ex.Message)
        End Try
    End Sub

    ' Impostazioni del lettore di schermo, raggruppate: scrittura, tastiera, pagine web, applicazioni.
    Private Sub MenuImpostazioniLettoreWinGabriel()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Finestra delle impostazioni del lettore", AddressOf ApriFinestraImpostazioniLettore)
            m.aggiungi("Avvio automatico all'accesso a Windows", AddressOf MenuAvvioAutomaticoLettore)
            m.aggiungi("Installazione del lettore", Sub() InstallatoreLettore.MostraMenu())
            m.aggiungi("Scrittura", AddressOf MenuLettoreScrittura)
            m.aggiungi("Tastiera e comandi", AddressOf MenuLettoreTastiera)
            m.aggiungi("Pagine web", AddressOf MenuLettorePagineWeb)
            m.aggiungi("Applicazioni", AddressOf MenuLettoreApplicazioni)
            m.avvia("Impostazioni lettore di schermo")
        Catch ex As Exception
            logger.ScriviLog("MenuImpostazioniLettoreWinGabriel eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLettoreScrittura()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Eco dei caratteri digitati", AddressOf MenuEcoCaratteriLettore)
            m.aggiungi("Eco delle parole", AddressOf MenuEcoParoleLettore)
            m.aggiungi("Campi protetti", AddressOf MenuPasswordLettore)
            m.avvia("Lettore di schermo, scrittura")
        Catch ex As Exception
            logger.ScriviLog("MenuLettoreScrittura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLettoreTastiera()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Tasto dei comandi", AddressOf MenuTastoComandiLettore)
            m.avvia("Lettore di schermo, tastiera e comandi")
        Catch ex As Exception
            logger.ScriviLog("MenuLettoreTastiera eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLettorePagineWeb()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Navigazione delle pagine web", AddressOf MenuNavigazioneWebLettore)
            m.aggiungi("Annunci nella navigazione", AddressOf MenuAnnunciNavigazioneLettore)
            m.aggiungi("Lettura dei link", AddressOf MenuLinkLettore)
            m.avvia("Lettore di schermo, pagine web")
        Catch ex As Exception
            logger.ScriviLog("MenuLettorePagineWeb eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLettoreApplicazioni()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Colonne nelle liste", AddressOf MenuColonneLettore)
            m.aggiungi("Posizione negli elenchi", AddressOf MenuPosizioneElenchiLettore)
            m.aggiungi("Avvisi delle applicazioni", AddressOf MenuAvvisiLettore)
            m.avvia("Lettore di schermo, applicazioni")
        Catch ex As Exception
            logger.ScriviLog("MenuLettoreApplicazioni eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuAvvisiLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaAvvisiLettore)
            m.aggiungi("Disattivata", AddressOf DisattivaAvvisiLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_AVVISI_LETTORE, "true", "true"))
            m.avvia("Avvisi delle applicazioni", True)
        Catch ex As Exception
            logger.ScriviLog("MenuAvvisiLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPosizioneElenchiLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaPosizioneElenchiLettore)
            m.aggiungi("Disattivata", AddressOf DisattivaPosizioneElenchiLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_POSIZIONE_ELENCHI_LETTORE, "true", "true"))
            m.avvia("Posizione negli elenchi", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPosizioneElenchiLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaPosizioneElenchiLettore()
        SalvaOpzioneLettore(ID_CONFIG_POSIZIONE_ELENCHI_LETTORE, "true", "Spostandosi con le frecce negli elenchi, come in Esplora file e in Teams, il lettore dirà anche la posizione della voce, per esempio ""3 di 20"".")
    End Sub

    Private Sub DisattivaPosizioneElenchiLettore()
        SalvaOpzioneLettore(ID_CONFIG_POSIZIONE_ELENCHI_LETTORE, "false", "Spostandosi con le frecce negli elenchi il lettore non dirà più la posizione della voce.")
    End Sub

    Private Sub AttivaAvvisiLettore()
        SalvaOpzioneLettore(ID_CONFIG_AVVISI_LETTORE, "true", "Il lettore leggerà gli avvisi delle altre applicazioni, come i messaggi di Teams e le notifiche di Windows, anche quando non sei in quell'applicazione.")
    End Sub

    Private Sub DisattivaAvvisiLettore()
        SalvaOpzioneLettore(ID_CONFIG_AVVISI_LETTORE, "false", "Il lettore non leggerà gli avvisi delle altre applicazioni.")
    End Sub

    Private Sub MenuSceltaLettoreSchermo()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Lettore di schermo, sperimentale", AddressOf SceltaLettoreWinGabriel)
            m.aggiungi("Automatico", AddressOf SceltaLettoreAutomatico)
            m.aggiungi("JAWS", AddressOf SceltaLettoreJaws)
            m.aggiungi("NVDA", AddressOf SceltaLettoreNvda)
            m.aggiungi("KSR", AddressOf SceltaLettoreKsr)
            m.aggiungi("Assistente vocale", AddressOf SceltaLettoreAssistenteVocale)
            Dim indiceCorrente As Integer = 1
            Dim preferito As String = LeggiConfigValore(ID_CONFIG_LETTORE_SCHERMO, "Automatico")
            If String.Equals(preferito, VALORE_LETTORE_WINGABRIEL, StringComparison.OrdinalIgnoreCase) Then
                indiceCorrente = 0
            ElseIf String.Equals(preferito, "JAWS", StringComparison.OrdinalIgnoreCase) Then
                indiceCorrente = 2
            ElseIf String.Equals(preferito, "NVDA", StringComparison.OrdinalIgnoreCase) Then
                indiceCorrente = 3
            ElseIf String.Equals(preferito, "KSR", StringComparison.OrdinalIgnoreCase) Then
                indiceCorrente = 4
            ElseIf String.Equals(preferito, "Narrator", StringComparison.OrdinalIgnoreCase) Then
                indiceCorrente = 5
            End If
            m.posiziona(indiceCorrente)
            m.avvia("Lettore di schermo preferito")
        Catch ex As Exception
            logger.ScriviLog("MenuSceltaLettoreSchermo eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SceltaLettoreWinGabriel()
        Try
            SalvaConfig(ID_CONFIG_LETTORE_SCHERMO, VALORE_LETTORE_WINGABRIEL)
            ApplicaLettoreSchermoWinGabriel()
            Dim testo As String = "Attivato il lettore di schermo sperimentale."
            If LeggiModalitaLetturaNormalizzata() = "Nessun lettore" Then
                testo &= " La sintesi vocale di WinGabriel è disattivata: per sentire il lettore scegliere le voci di Windows nelle impostazioni della sintesi vocale."
            End If
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("SceltaLettoreWinGabriel eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SceltaLettoreAutomatico()
        Try
            Dim percorso As String = ""
            Dim trovato As String = RilevatoreLettoreSchermo.Rileva(percorso)
            SalvaConfig(RilevatoreLettoreSchermo.ID_CONFIG_RILEVATO, trovato)
            Dim annuncio As String
            If trovato = "" Then
                annuncio = "Automatico. Nessun lettore di schermo trovato installato nel sistema."
            Else
                annuncio = "Automatico. Trovato " & RilevatoreLettoreSchermo.Descrivi(trovato) & "."
            End If
            ScegliLettoreEsterno("Automatico", annuncio)
        Catch ex As Exception
            logger.ScriviLog("SceltaLettoreAutomatico eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SceltaLettoreJaws()
        Try
            ScegliLettoreEsterno("JAWS", "JAWS verrà avviato quando apri un link o quando WinGabriel va in secondo piano.")
        Catch ex As Exception
            logger.ScriviLog("SceltaLettoreJaws eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SceltaLettoreNvda()
        Try
            ScegliLettoreEsterno("NVDA", "NVDA verrà avviato quando apri un link o quando WinGabriel va in secondo piano.")
        Catch ex As Exception
            logger.ScriviLog("SceltaLettoreNvda eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SceltaLettoreKsr()
        Try
            ScegliLettoreEsterno("KSR", "KSR verrà avviato quando apri un link o quando WinGabriel va in secondo piano.")
        Catch ex As Exception
            logger.ScriviLog("SceltaLettoreKsr eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SceltaLettoreAssistenteVocale()
        Try
            ScegliLettoreEsterno("Narrator", "L'assistente vocale di Windows verrà avviato quando apri un link o quando WinGabriel va in secondo piano.")
        Catch ex As Exception
            logger.ScriviLog("SceltaLettoreAssistenteVocale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliLettoreEsterno(valore As String, messaggio As String)
        Try
            SalvaConfig(ID_CONFIG_LETTORE_SCHERMO, valore)
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggio)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("ScegliLettoreEsterno eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuEcoCaratteriLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivato", AddressOf AttivaEcoCaratteriLettore)
            m.aggiungi("Disattivato", AddressOf DisattivaEcoCaratteriLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_ECO_CARATTERI_LETTORE, "true", "true"))
            m.avvia("Eco dei caratteri digitati", True)
        Catch ex As Exception
            logger.ScriviLog("MenuEcoCaratteriLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaEcoCaratteriLettore()
        Try
            SalvaConfig(ID_CONFIG_ECO_CARATTERI_LETTORE, "true")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Il lettore di schermo ripeterà ogni carattere scritto nelle altre applicazioni.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaEcoCaratteriLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaEcoCaratteriLettore()
        Try
            SalvaConfig(ID_CONFIG_ECO_CARATTERI_LETTORE, "false")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Il lettore di schermo non ripeterà i caratteri scritti nelle altre applicazioni. Le lettere cancellate vengono comunque annunciate.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaEcoCaratteriLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuEcoParoleLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivato", AddressOf AttivaEcoParoleLettore)
            m.aggiungi("Disattivato", AddressOf DisattivaEcoParoleLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_ECO_PAROLE_LETTORE, "true", "true"))
            m.avvia("Eco delle parole", True)
        Catch ex As Exception
            logger.ScriviLog("MenuEcoParoleLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaEcoParoleLettore()
        Try
            SalvaConfig(ID_CONFIG_ECO_PAROLE_LETTORE, "true")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Dopo uno spazio, il lettore ripete la parola appena scritta.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaEcoParoleLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaEcoParoleLettore()
        Try
            SalvaConfig(ID_CONFIG_ECO_PAROLE_LETTORE, "false")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Il lettore non ripeterà la parola dopo lo spazio.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaEcoParoleLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuTastoComandiLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Insert", AddressOf TastoLettoreInsert)
            m.aggiungi("Bloc maiuscole", AddressOf TastoLettoreCaps)
            m.aggiungi("Insert e Bloc maiuscole", AddressOf TastoLettoreEntrambi)
            Dim valore As String = LeggiConfigValore(ID_CONFIG_TASTO_LETTORE, "entrambi")
            Dim indice As Integer = 2
            If String.Equals(valore, "insert", StringComparison.OrdinalIgnoreCase) Then indice = 0
            If String.Equals(valore, "capslock", StringComparison.OrdinalIgnoreCase) Then indice = 1
            m.posiziona(indice)
            m.avvia("Tasto dei comandi", True)
        Catch ex As Exception
            logger.ScriviLog("MenuTastoComandiLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub TastoLettoreInsert()
        Try
            SalvaTastoLettore("insert", "I comandi del lettore si danno tenendo premuto Insert. Un doppio tocco invia Insert all'applicazione.")
        Catch ex As Exception
            logger.ScriviLog("TastoLettoreInsert eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub TastoLettoreCaps()
        Try
            SalvaTastoLettore("capslock", "I comandi del lettore si danno tenendo premuto Bloc maiuscole, al posto di Insert. Un doppio tocco attiva o disattiva le maiuscole.")
        Catch ex As Exception
            logger.ScriviLog("TastoLettoreCaps eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub TastoLettoreEntrambi()
        Try
            SalvaTastoLettore("entrambi", "I comandi del lettore si danno con Insert oppure con Bloc maiuscole. Si può cambiare questa scelta in qualsiasi momento. Un doppio tocco invia il tasto all'applicazione.")
        Catch ex As Exception
            logger.ScriviLog("TastoLettoreEntrambi eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaTastoLettore(valore As String, messaggio As String)
        Try
            SalvaConfig(ID_CONFIG_TASTO_LETTORE, valore)
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggio)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("SalvaTastoLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPasswordLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Leggi i caratteri", AddressOf AttivaPasswordLettore)
            m.aggiungi("Dì solo stella", AddressOf DisattivaPasswordLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_PASSWORD_LETTORE, "true", "false"))
            m.avvia("Campi protetti", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPasswordLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaPasswordLettore()
        Try
            SalvaConfig(ID_CONFIG_PASSWORD_LETTORE, "true")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nei campi protetti il lettore dirà le lettere scritte e cancellate.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaPasswordLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaPasswordLettore()
        Try
            SalvaConfig(ID_CONFIG_PASSWORD_LETTORE, "false")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("Nei campi protetti il lettore dirà stella, senza leggere la lettera.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaPasswordLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuNavigazioneWebLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaNavigazioneWebLettore)
            m.aggiungi("Disattivata", AddressOf DisattivaNavigazioneWebLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_NAVIGAZIONE_WEB_LETTORE, "true", "true"))
            m.avvia("Navigazione delle pagine web", True)
        Catch ex As Exception
            logger.ScriviLog("MenuNavigazioneWebLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaNavigazioneWebLettore()
        Try
            SalvaConfig(ID_CONFIG_NAVIGAZIONE_WEB_LETTORE, "true")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("In Chrome, Edge, Firefox e negli altri programmi le frecce leggono gli elementi dell'albero di accessibilità. Il tasto dei comandi con la barra spaziatrice entra o esce da questa navigazione. Arrivati all'ultimo titolo o all'ultimo elemento di un tipo si ricomincia dal primo. Backspace e Canc, in questa navigazione, leggono il carattere accanto e non cancellano.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaNavigazioneWebLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaNavigazioneWebLettore()
        Try
            SalvaConfig(ID_CONFIG_NAVIGAZIONE_WEB_LETTORE, "false")
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio("La navigazione delle pagine web è disattivata. I campi e la selezione si leggono comunque.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaNavigazioneWebLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuAnnunciNavigazioneLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Annuncio delle regioni", AddressOf MenuRegioniLettore)
            m.aggiungi("Livello delle intestazioni", AddressOf MenuLivelloIntestazioniLettore)
            m.aggiungi("Navigazione ciclica", AddressOf MenuCiclicaLettore)
            m.avvia("Annunci nella navigazione")
        Catch ex As Exception
            logger.ScriviLog("MenuAnnunciNavigazioneLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SalvaOpzioneLettore(id As String, valore As String, messaggio As String)
        Try
            SalvaConfig(id, valore)
            ApplicaLettoreSchermoWinGabriel()
            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggio)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("SalvaOpzioneLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuRegioniLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Prima la sola regione, poi con la freccia l'elemento", AddressOf RegioniSeparataLettore)
            m.aggiungi("Regione e elemento insieme, quando si entra in una nuova regione", AddressOf RegioniNuovaLettore)
            m.aggiungi("Regione e elemento insieme, ad ogni elemento", AddressOf RegioniSempreLettore)
            m.aggiungi("Mai", AddressOf RegioniMaiLettore)
            m.posiziona(IndiceMultiplo(ID_CONFIG_REGIONI_LETTORE, New List(Of String) From {"separata", "nuova", "sempre", "mai"}, "nuova"))
            m.avvia("Annuncio delle regioni", True)
        Catch ex As Exception
            logger.ScriviLog("MenuRegioniLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RegioniSeparataLettore()
        SalvaOpzioneLettore(ID_CONFIG_REGIONI_LETTORE, "separata", "Scorrendo le pagine con le frecce, entrando in una nuova regione il lettore dice solo la regione. Con la freccia successiva dice l'elemento, per esempio l'intestazione.")
    End Sub

    Private Sub RegioniNuovaLettore()
        SalvaOpzioneLettore(ID_CONFIG_REGIONI_LETTORE, "nuova", "Scorrendo le pagine, entrando in una nuova regione il lettore dice la regione insieme al primo elemento.")
    End Sub

    Private Sub RegioniSempreLettore()
        SalvaOpzioneLettore(ID_CONFIG_REGIONI_LETTORE, "sempre", "Scorrendo le pagine, il lettore dirà la regione prima di ogni elemento.")
    End Sub

    Private Sub RegioniMaiLettore()
        SalvaOpzioneLettore(ID_CONFIG_REGIONI_LETTORE, "mai", "Scorrendo le pagine, il lettore non dirà le regioni.")
    End Sub

    Private Sub MenuLivelloIntestazioniLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Dì il livello", AddressOf AttivaLivelloIntestazioniLettore)
            m.aggiungi("Non dire il livello", AddressOf DisattivaLivelloIntestazioniLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_LIVELLO_TITOLI_LETTORE, "true", "true"))
            m.avvia("Livello delle intestazioni", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLivelloIntestazioniLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaLivelloIntestazioniLettore()
        SalvaOpzioneLettore(ID_CONFIG_LIVELLO_TITOLI_LETTORE, "true", "Le intestazioni verranno lette con il loro livello, per esempio intestazione di livello 2.")
    End Sub

    Private Sub DisattivaLivelloIntestazioniLettore()
        SalvaOpzioneLettore(ID_CONFIG_LIVELLO_TITOLI_LETTORE, "false", "Le intestazioni verranno lette senza il livello.")
    End Sub

    Private Sub MenuCiclicaLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaCiclicaLettore)
            m.aggiungi("Disattivata", AddressOf DisattivaCiclicaLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_CICLICA_LETTORE, "true", "true"))
            m.avvia("Navigazione ciclica", True)
        Catch ex As Exception
            logger.ScriviLog("MenuCiclicaLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaCiclicaLettore()
        SalvaOpzioneLettore(ID_CONFIG_CICLICA_LETTORE, "true", "Nelle pagine, con le frecce e con le lettere dei tipi di elemento, dopo l'ultimo si ricomincia dal primo e viceversa.")
    End Sub

    Private Sub DisattivaCiclicaLettore()
        SalvaOpzioneLettore(ID_CONFIG_CICLICA_LETTORE, "false", "Nelle pagine, arrivati all'ultimo elemento o all'ultimo di un tipo, il lettore dice che non ce ne sono altri.")
    End Sub

    Private Sub MenuLinkLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Prima del nome, per esempio link Google", AddressOf LinkPrimaLettore)
            m.aggiungi("Dopo il nome, per esempio Google link", AddressOf LinkDopoLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_LINK_PRIMA_LETTORE, "true", "true"))
            m.avvia("Lettura dei link", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLinkLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub LinkPrimaLettore()
        SalvaOpzioneLettore(ID_CONFIG_LINK_PRIMA_LETTORE, "true", "Il lettore dirà link prima del nome, per esempio link Google.")
    End Sub

    Private Sub LinkDopoLettore()
        SalvaOpzioneLettore(ID_CONFIG_LINK_PRIMA_LETTORE, "false", "Il lettore dirà link dopo il nome, per esempio Google link.")
    End Sub

    Private Sub MenuColonneLettore()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaColonneLettore)
            m.aggiungi("Disattivata", AddressOf DisattivaColonneLettore)
            m.posiziona(IndiceBinario(ID_CONFIG_COLONNE_LETTORE, "true", "false"))
            m.avvia("Colonne nelle liste", True)
        Catch ex As Exception
            logger.ScriviLog("MenuColonneLettore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AttivaColonneLettore()
        SalvaOpzioneLettore(ID_CONFIG_COLONNE_LETTORE, "true", "Nelle liste e nelle tabelle delle altre applicazioni, come Esplora risorse, il lettore dirà dopo il nome anche le colonne, per esempio data e dimensione.")
    End Sub

    Private Sub DisattivaColonneLettore()
        SalvaOpzioneLettore(ID_CONFIG_COLONNE_LETTORE, "false", "Nelle liste e nelle tabelle delle altre applicazioni il lettore dirà solo il nome dell'elemento, senza le colonne.")
    End Sub

    Private Sub MenuTraduttore()
        Try
            WGTraduttore.MostraImpostazioni()
        Catch ex As Exception
            logger.ScriviLog("MenuTraduttore eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuBrowser()
        Try
            Global.WGBrowser.WGBrowser.MostraConfigurazione()
        Catch ex As Exception
            logger.ScriviLog("MenuBrowser eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuImpostazioniPosta()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Apertura dei messaggi", AddressOf MenuPostaEditorLettura)
            m.aggiungi("Scrittura dei messaggi", AddressOf MenuPostaEditorScrittura)
            m.avvia("Posta")
        Catch ex As Exception
            logger.ScriviLog("MenuImpostazioniPosta eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPostaEditorLettura()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Editor testuale di WinGabriel", AddressOf ImpostaPostaLetturaTestuale)
            m.aggiungi("Editor documento con Word", AddressOf ImpostaPostaLetturaWord)
            m.aggiungi("Browser di WinGabriel, per i messaggi scritti con la grafica", AddressOf ImpostaPostaLetturaBrowser)
            Dim impostazioni As New WGImpostazioniComuni()
            m.posiziona(If(impostazioni.PostaLetturaNelBrowser, 2, If(impostazioni.PostaLetturaConWord, 1, 0)))
            m.avvia("Apertura dei messaggi", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPostaEditorLettura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaPostaLetturaTestuale()
        Try
            SalvaConfigurazioneGenerica("posta_lettura_word", "0", "I messaggi verranno aperti con l'editor testuale di WinGabriel.")
        Catch ex As Exception
            logger.ScriviLog("ImpostaPostaLetturaTestuale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaPostaLetturaWord()
        Try
            SalvaConfigurazioneGenerica("posta_lettura_word", "1", "I messaggi verranno aperti con l'editor documento, con tabelle, elenchi e immagini.")
        Catch ex As Exception
            logger.ScriviLog("ImpostaPostaLetturaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaPostaLetturaBrowser()
        Try
            SalvaConfigurazioneGenerica("posta_lettura_word", "2", "I messaggi scritti con la grafica verranno aperti nel browser di WinGabriel, come una pagina web, con in cima alle opzioni rispondi, inoltra e le altre azioni. I messaggi di solo testo restano nell'editor testuale.")
        Catch ex As Exception
            logger.ScriviLog("ImpostaPostaLetturaBrowser eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPostaEditorScrittura()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Editor testuale di WinGabriel", AddressOf ImpostaPostaScritturaTestuale)
            m.aggiungi("Editor documento con Word", AddressOf ImpostaPostaScritturaWord)
            Dim impostazioni As New WGImpostazioniComuni()
            m.posiziona(If(impostazioni.PostaScritturaConWord, 1, 0))
            m.avvia("Scrittura dei messaggi", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPostaEditorScrittura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaPostaScritturaTestuale()
        Try
            SalvaConfigurazioneGenerica("posta_scrittura_word", "0", "I messaggi verranno scritti con l'editor testuale di WinGabriel, in solo testo.")
        Catch ex As Exception
            logger.ScriviLog("ImpostaPostaScritturaTestuale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaPostaScritturaWord()
        Try
            SalvaConfigurazioneGenerica("posta_scrittura_word", "1", "I messaggi verranno scritti con l'editor documento, con tabelle, elenchi e immagini.")
        Catch ex As Exception
            logger.ScriviLog("ImpostaPostaScritturaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private _listaRubricaSincronizzazione As WGMenuLista
    Private _listaAgendaSincronizzazione As WGMenuLista
    Private _processoSincronizzazioneCloud As WGProcessoInCorso = Nothing
    Private _esitoSincronizzazioneCloud As WGSincronizzazioneCloud.EsitoSincronizzazione = Nothing
    Private _campiAccessoFilen As Campi
    Private _campiRegistrazioneFilen As Campi
    Private _campiConfermaFilen As Campi
    Private Const IdNessunaSceltaSincronizzazione As String = "nessuna_scelta_sincronizzazione"

    Private Sub MostraMenuSincronizzazioneCloud()
        Try
            Dim m As New WGMenu()
            If Not WGSincronizzazioneCloud.Collegato Then
                m.aggiungi("Accedi con il tuo account Filen", AddressOf MostraCampiAccessoFilen)
                m.aggiungi("Crea un account Filen", AddressOf MostraCampiRegistrazioneFilen)
                m.aggiungi("Reinvia l'email di conferma dell'account", AddressOf MostraCampiConfermaFilen)
            Else
                m.aggiungi("Stato della sincronizzazione", AddressOf MenuAttivazioneSincronizzazione)
                m.aggiungi("Rubrica", AddressOf MostraListaRubricaSincronizzazione)
                m.aggiungi("Agenda", AddressOf MostraListaAgendaSincronizzazione)
                m.aggiungi("Radio", AddressOf MenuRadioSincronizzazione)
                m.aggiungi("Testate dei quotidiani", AddressOf MenuTestateSincronizzazione)
                m.aggiungi("Sincronizza adesso", AddressOf SincronizzaCloudAdesso)
                m.aggiungi("Esci dall'account Filen", AddressOf EsciDaFilen)
            End If
            m.aggiungi("Informazioni sulla sincronizzazione", AddressOf InformazioniSincronizzazioneCloud)
            m.avvia("Sincronizzazione tra computer")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuSincronizzazioneCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraAvvisoSincronizzazione(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("MostraAvvisoSincronizzazione eccezione: " & ex.Message)
        End Try
    End Sub

    ' ---------- Account Filen: accesso, creazione, conferma ----------

    ''' <summary>Esegue un'operazione lunga (rete e calcolo delle chiavi) fuori dall'interfaccia, con il messaggio "attendi", poi rientra nell'interfaccia.</summary>
    Private Sub EseguiOperazioneAccountFilen(testoAttesa As String, lavoro As Func(Of Boolean), alTermine As Action(Of Boolean))
        Try
            If _processoSincronizzazioneCloud IsNot Nothing Then
                MostraAvvisoSincronizzazione("C'è già un'operazione in corso, attendi che finisca.")
                Return
            End If

            Try
                _processoSincronizzazioneCloud = New WGProcessoInCorso()
                _processoSincronizzazioneCloud.Avvia(testoAttesa, False)
            Catch exProcesso As Exception
                logger.ScriviLog("EseguiOperazioneAccountFilen avvio processo eccezione: " & exProcesso.Message)
            End Try

            Task.Run(Sub()
                         Dim riuscita As Boolean = False
                         Try
                             riuscita = lavoro()
                         Catch exLavoro As Exception
                             logger.ScriviLog("EseguiOperazioneAccountFilen lavoro eccezione: " & exLavoro.Message & Environment.NewLine & exLavoro.StackTrace)
                         Finally
                             Try
                                 EseguiSuInterfacciaPrincipale(New MethodInvoker(Sub() CompletaOperazioneAccountFilen(riuscita, alTermine)))
                             Catch exRientro As Exception
                                 logger.ScriviLog("EseguiOperazioneAccountFilen rientro eccezione: " & exRientro.Message)
                             End Try
                         End Try
                     End Sub)
        Catch ex As Exception
            logger.ScriviLog("EseguiOperazioneAccountFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CompletaOperazioneAccountFilen(riuscita As Boolean, alTermine As Action(Of Boolean))
        Try
            Try
                If _processoSincronizzazioneCloud IsNot Nothing Then _processoSincronizzazioneCloud.Fine()
            Catch exFine As Exception
                logger.ScriviLog("CompletaOperazioneAccountFilen chiusura processo eccezione: " & exFine.Message)
            End Try
            _processoSincronizzazioneCloud = Nothing
            If alTermine IsNot Nothing Then alTermine(riuscita)
        Catch ex As Exception
            logger.ScriviLog("CompletaOperazioneAccountFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function TestoErroreAccountFilen(predefinito As String) As String
        Dim errore As String = WGSincronizzazioneCloud.UltimoErrore
        Return If(String.IsNullOrWhiteSpace(errore), predefinito, errore)
    End Function

    Private Sub MostraCampiAccessoFilen()
        ApriCampiAccessoFilen("")
    End Sub

    Private Sub ApriCampiAccessoFilen(emailPredefinita As String)
        Try
            _campiAccessoFilen = New Campi()
            _campiAccessoFilen.Aggiungi("Indirizzo email dell'account Filen", "filen.email", True, False, If(emailPredefinita, ""))
            _campiAccessoFilen.AggiungiPassword("Password", "filen.password", True)
            _campiAccessoFilen.Aggiungi("Codice a due fattori, da scrivere solo se è attivo sul tuo account", "filen.codice", False)
            AddHandler _campiAccessoFilen.Fine, AddressOf ConfermaAccessoFilen
            _campiAccessoFilen.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ApriCampiAccessoFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaAccessoFilen()
        Try
            Dim email As String = ""
            Dim password As String = ""
            Dim codice As String = ""
            Try
                email = _campiAccessoFilen.RecuperaValore("filen.email")
                password = _campiAccessoFilen.RecuperaValore("filen.password")
                codice = _campiAccessoFilen.RecuperaValore("filen.codice")
            Catch exLettura As Exception
                logger.ScriviLog("ConfermaAccessoFilen lettura eccezione: " & exLettura.Message)
            End Try
            _campiAccessoFilen = Nothing

            email = If(email, "").Trim()
            If email.Length = 0 OrElse String.IsNullOrEmpty(password) Then
                MostraAvvisoSincronizzazione("Servono l'indirizzo email e la password.")
                ApriCampiAccessoFilen(email)
                Return
            End If

            EseguiOperazioneAccountFilen("Accesso in corso, attendi",
                                         Function() WGSincronizzazioneCloud.Accedi(email, password, codice),
                                         Sub(riuscita As Boolean) AccessoFilenTerminato(riuscita, email))
        Catch ex As Exception
            logger.ScriviLog("ConfermaAccessoFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AccessoFilenTerminato(riuscita As Boolean, email As String)
        Try
            If riuscita Then
                MostraAvvisoSincronizzazione("Accesso eseguito. Apri di nuovo questo menu: ora puoi attivare la sincronizzazione e scegliere cosa sincronizzare.")
            Else
                MostraAvvisoSincronizzazione(TestoErroreAccountFilen("Non è stato possibile eseguire l'accesso."))
                ApriCampiAccessoFilen(email)
            End If
        Catch ex As Exception
            logger.ScriviLog("AccessoFilenTerminato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCampiRegistrazioneFilen()
        ApriCampiRegistrazioneFilen("")
    End Sub

    Private Sub ApriCampiRegistrazioneFilen(emailPredefinita As String)
        Try
            _campiRegistrazioneFilen = New Campi()
            _campiRegistrazioneFilen.Aggiungi("Indirizzo email per il nuovo account Filen", "filen.email", True, False, If(emailPredefinita, ""))
            _campiRegistrazioneFilen.AggiungiPassword("Password. Filen cifra i dati con questa password: se la perdi, i dati non si recuperano", "filen.password", True)
            _campiRegistrazioneFilen.AggiungiPassword("Ripeti la password", "filen.password2", True)
            AddHandler _campiRegistrazioneFilen.Fine, AddressOf ConfermaRegistrazioneFilen
            _campiRegistrazioneFilen.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ApriCampiRegistrazioneFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaRegistrazioneFilen()
        Try
            Dim email As String = ""
            Dim password As String = ""
            Dim ripetuta As String = ""
            Try
                email = _campiRegistrazioneFilen.RecuperaValore("filen.email")
                password = _campiRegistrazioneFilen.RecuperaValore("filen.password")
                ripetuta = _campiRegistrazioneFilen.RecuperaValore("filen.password2")
            Catch exLettura As Exception
                logger.ScriviLog("ConfermaRegistrazioneFilen lettura eccezione: " & exLettura.Message)
            End Try
            _campiRegistrazioneFilen = Nothing

            email = If(email, "").Trim()
            If email.Length = 0 OrElse String.IsNullOrEmpty(password) Then
                MostraAvvisoSincronizzazione("Servono l'indirizzo email e la password.")
                ApriCampiRegistrazioneFilen(email)
                Return
            End If
            If Not String.Equals(password, ripetuta, StringComparison.Ordinal) Then
                MostraAvvisoSincronizzazione("Le due password non sono uguali. Riprova.")
                ApriCampiRegistrazioneFilen(email)
                Return
            End If

            EseguiOperazioneAccountFilen("Creazione dell'account in corso, attendi",
                                         Function() WGSincronizzazioneCloud.Registra(email, password),
                                         Sub(riuscita As Boolean) RegistrazioneFilenTerminata(riuscita, email))
        Catch ex As Exception
            logger.ScriviLog("ConfermaRegistrazioneFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub RegistrazioneFilenTerminata(riuscita As Boolean, email As String)
        Try
            If riuscita Then
                MostraAvvisoSincronizzazione("Account creato. Filen ha inviato un'email di conferma a " & email & ": aprila e conferma l'indirizzo, poi torna qui e scegli Accedi con il tuo account Filen.")
            Else
                MostraAvvisoSincronizzazione(TestoErroreAccountFilen("Non è stato possibile creare l'account."))
                ApriCampiRegistrazioneFilen(email)
            End If
        Catch ex As Exception
            logger.ScriviLog("RegistrazioneFilenTerminata eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCampiConfermaFilen()
        Try
            _campiConfermaFilen = New Campi()
            _campiConfermaFilen.Aggiungi("Indirizzo email dell'account Filen", "filen.email", True)
            AddHandler _campiConfermaFilen.Fine, AddressOf ConfermaReinvioConfermaFilen
            _campiConfermaFilen.Avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraCampiConfermaFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ConfermaReinvioConfermaFilen()
        Try
            Dim email As String = ""
            Try
                email = _campiConfermaFilen.RecuperaValore("filen.email")
            Catch exLettura As Exception
                logger.ScriviLog("ConfermaReinvioConfermaFilen lettura eccezione: " & exLettura.Message)
            End Try
            _campiConfermaFilen = Nothing

            email = If(email, "").Trim()
            If email.Length = 0 Then
                MostraAvvisoSincronizzazione("Serve l'indirizzo email.")
                Return
            End If

            EseguiOperazioneAccountFilen("Invio dell'email in corso, attendi",
                                         Function() WGSincronizzazioneCloud.ReinviaConferma(email),
                                         Sub(riuscita As Boolean) ReinvioConfermaFilenTerminato(riuscita, email))
        Catch ex As Exception
            logger.ScriviLog("ConfermaReinvioConfermaFilen eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ReinvioConfermaFilenTerminato(riuscita As Boolean, email As String)
        Try
            If riuscita Then
                MostraAvvisoSincronizzazione("Ho chiesto a Filen di inviare di nuovo l'email di conferma a " & email & ".")
            Else
                MostraAvvisoSincronizzazione(TestoErroreAccountFilen("Non è stato possibile reinviare l'email di conferma."))
            End If
        Catch ex As Exception
            logger.ScriviLog("ReinvioConfermaFilenTerminato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EsciDaFilen()
        Try
            WGSincronizzazioneCloud.Esci()
            MostraAvvisoSincronizzazione("Hai eseguito l'uscita dall'account Filen e la sincronizzazione è disattivata. I dati restano su questo computer e su Filen.")
        Catch ex As Exception
            logger.ScriviLog("EsciDaFilen eccezione: " & ex.Message)
        End Try
    End Sub

    ' ---------- Attivazione ----------

    Private Sub MenuAttivazioneSincronizzazione()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaSincronizzazioneCloud)
            m.aggiungi("Disattivata", AddressOf DisattivaSincronizzazioneCloud)
            m.posiziona(If(WGSincronizzazioneCloud.SincronizzazioneAbilitata, 0, 1))
            m.avvia("Stato della sincronizzazione", True)
        Catch ex As Exception
            logger.ScriviLog("MenuAttivazioneSincronizzazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaSincronizzazioneCloud()
        Try
            WGSincronizzazioneCloud.SincronizzazioneAbilitata = True
            Dim testo As New StringBuilder()
            testo.Append("La sincronizzazione tra computer è attivata. ")
            If String.IsNullOrWhiteSpace(WGSincronizzazioneCloud.RubricaSincronizzata) AndAlso String.IsNullOrWhiteSpace(WGSincronizzazioneCloud.AgendaSincronizzata) AndAlso Not WGSincronizzazioneCloud.RadioSincronizzata AndAlso Not WGSincronizzazioneCloud.NotizieSincronizzate Then
                testo.Append("Scegli ora nello stesso menu la rubrica, l'agenda, le radio e le testate da sincronizzare.")
            Else
                WGSincronizzazioneCloud.RichiediSincronizzazione()
            End If
            MostraAvvisoSincronizzazione(testo.ToString().Trim())
        Catch ex As Exception
            logger.ScriviLog("AttivaSincronizzazioneCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaSincronizzazioneCloud()
        Try
            WGSincronizzazioneCloud.SincronizzazioneAbilitata = False
            MostraAvvisoSincronizzazione("La sincronizzazione tra computer è disattivata. I dati restano su questo computer.")
        Catch ex As Exception
            logger.ScriviLog("DisattivaSincronizzazioneCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraListaRubricaSincronizzazione()
        Dim contatti As CLSContatti = Nothing
        Try
            contatti = New CLSContatti()
            Dim rubriche As System.Data.DataTable = contatti.OttieniRubricheSincronizzabili()
            If rubriche Is Nothing OrElse rubriche.Rows.Count = 0 Then
                MostraAvvisoSincronizzazione("Sul computer non ci sono rubriche da sincronizzare.")
                Return
            End If

            _listaRubricaSincronizzazione = New WGMenuLista()
            _listaRubricaSincronizzazione.multiselezioneConsentita = False
            _listaRubricaSincronizzazione.impostaTitolo("Rubrica da sincronizzare")
            _listaRubricaSincronizzazione.aggiungiElemento("Nessuna rubrica", IdNessunaSceltaSincronizzazione)
            For Each riga As System.Data.DataRow In rubriche.Rows
                Dim chiave As String = Convert.ToString(riga("ChiaveWG"))
                Dim nome As String = Convert.ToString(riga("Nome"))
                If String.IsNullOrWhiteSpace(chiave) Then Continue For
                If String.IsNullOrWhiteSpace(nome) Then nome = "Rubrica senza nome"
                If Not String.IsNullOrWhiteSpace(Convert.ToString(riga("ChiaveAccount"))) Then nome &= ", collegata a un account"
                _listaRubricaSincronizzazione.aggiungiElemento(nome, chiave)
            Next
            AddHandler _listaRubricaSincronizzazione.escPremuto, AddressOf ChiudiListaRubricaSincronizzazione
            AddHandler _listaRubricaSincronizzazione.ElementoSelezionato, AddressOf RubricaSincronizzazioneSelezionata
            Dim attuale As String = WGSincronizzazioneCloud.RubricaSincronizzata
            _listaRubricaSincronizzazione.posiziona(If(String.IsNullOrWhiteSpace(attuale), IdNessunaSceltaSincronizzazione, attuale))
            _listaRubricaSincronizzazione.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraListaRubricaSincronizzazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If contatti IsNot Nothing Then contatti.Dispose()
            Catch exChiusura As Exception
                logger.ScriviLog("MostraListaRubricaSincronizzazione chiusura eccezione: " & exChiusura.Message)
            End Try
        End Try
    End Sub

    Private Sub ChiudiListaRubricaSincronizzazione()
        Try
            _listaRubricaSincronizzazione.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiListaRubricaSincronizzazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RubricaSincronizzazioneSelezionata()
        Try
            Dim elementi As List(Of String) = _listaRubricaSincronizzazione.ottieniElementi()
            If elementi.Count > 0 Then
                Dim elemento As String = elementi(0)
                If elemento = IdNessunaSceltaSincronizzazione Then
                    WGSincronizzazioneCloud.RubricaSincronizzata = ""
                    MostraAvvisoSincronizzazione("La rubrica non verrà sincronizzata.")
                Else
                    WGSincronizzazioneCloud.RubricaSincronizzata = elemento
                    WGSincronizzazioneCloud.RichiediSincronizzazione()
                    MostraAvvisoSincronizzazione(TestoSceltaSincronizzata("La rubrica scelta verrà sincronizzata con gli altri computer."))
                End If
            End If
            _listaRubricaSincronizzazione.abbandona()
        Catch ex As Exception
            logger.ScriviLog("RubricaSincronizzazioneSelezionata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraListaAgendaSincronizzazione()
        Try
            Dim agende As List(Of CLSAgenda.DescrizioneAgenda) = CLSAgenda.GetInstance().OttieniAgendeSincronizzabili()
            If agende Is Nothing OrElse agende.Count = 0 Then
                MostraAvvisoSincronizzazione("Sul computer non ci sono agende da sincronizzare.")
                Return
            End If

            _listaAgendaSincronizzazione = New WGMenuLista()
            _listaAgendaSincronizzazione.multiselezioneConsentita = False
            _listaAgendaSincronizzazione.impostaTitolo("Agenda da sincronizzare")
            _listaAgendaSincronizzazione.aggiungiElemento("Nessuna agenda", IdNessunaSceltaSincronizzazione)
            For Each agenda As CLSAgenda.DescrizioneAgenda In agende
                If agenda Is Nothing OrElse String.IsNullOrWhiteSpace(agenda.Chiave) Then Continue For
                Dim nome As String = If(String.IsNullOrWhiteSpace(agenda.Nome), "Agenda senza nome", agenda.Nome)
                If Not String.IsNullOrWhiteSpace(agenda.ChiaveAccount) Then nome &= ", collegata a un account"
                _listaAgendaSincronizzazione.aggiungiElemento(nome, agenda.Chiave)
            Next
            AddHandler _listaAgendaSincronizzazione.escPremuto, AddressOf ChiudiListaAgendaSincronizzazione
            AddHandler _listaAgendaSincronizzazione.ElementoSelezionato, AddressOf AgendaSincronizzazioneSelezionata
            Dim attuale As String = WGSincronizzazioneCloud.AgendaSincronizzata
            _listaAgendaSincronizzazione.posiziona(If(String.IsNullOrWhiteSpace(attuale), IdNessunaSceltaSincronizzazione, attuale))
            _listaAgendaSincronizzazione.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraListaAgendaSincronizzazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiListaAgendaSincronizzazione()
        Try
            _listaAgendaSincronizzazione.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiListaAgendaSincronizzazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AgendaSincronizzazioneSelezionata()
        Try
            Dim elementi As List(Of String) = _listaAgendaSincronizzazione.ottieniElementi()
            If elementi.Count > 0 Then
                Dim elemento As String = elementi(0)
                If elemento = IdNessunaSceltaSincronizzazione Then
                    WGSincronizzazioneCloud.AgendaSincronizzata = ""
                    MostraAvvisoSincronizzazione("L'agenda non verrà sincronizzata.")
                Else
                    WGSincronizzazioneCloud.AgendaSincronizzata = elemento
                    WGSincronizzazioneCloud.RichiediSincronizzazione()
                    MostraAvvisoSincronizzazione(TestoSceltaSincronizzata("L'agenda scelta verrà sincronizzata con gli altri computer."))
                End If
            End If
            _listaAgendaSincronizzazione.abbandona()
        Catch ex As Exception
            logger.ScriviLog("AgendaSincronizzazioneSelezionata eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function TestoSceltaSincronizzata(testo As String) As String
        Try
            If WGSincronizzazioneCloud.Attiva Then Return testo
            Return testo & " Attiva anche la sincronizzazione, altrimenti non parte."
        Catch ex As Exception
            logger.ScriviLog("TestoSceltaSincronizzata eccezione: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Sub MenuRadioSincronizzazione()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivato", AddressOf AttivaRadioSincronizzazione)
            m.aggiungi("Disattivato", AddressOf DisattivaRadioSincronizzazione)
            m.posiziona(If(WGSincronizzazioneCloud.RadioSincronizzata, 0, 1))
            m.avvia("Sincronizzazione delle radio", True)
        Catch ex As Exception
            logger.ScriviLog("MenuRadioSincronizzazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaRadioSincronizzazione()
        Try
            WGSincronizzazioneCloud.RadioSincronizzata = True
            WGSincronizzazioneCloud.RichiediSincronizzazione()
            MostraAvvisoSincronizzazione(TestoSceltaSincronizzata("Le radio preferite e quelle ascoltate di recente verranno sincronizzate con gli altri computer."))
        Catch ex As Exception
            logger.ScriviLog("AttivaRadioSincronizzazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaRadioSincronizzazione()
        Try
            WGSincronizzazioneCloud.RadioSincronizzata = False
            MostraAvvisoSincronizzazione("Le radio non verranno sincronizzate.")
        Catch ex As Exception
            logger.ScriviLog("DisattivaRadioSincronizzazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuTestateSincronizzazione()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivato", AddressOf AttivaTestateSincronizzazione)
            m.aggiungi("Disattivato", AddressOf DisattivaTestateSincronizzazione)
            m.posiziona(If(WGSincronizzazioneCloud.NotizieSincronizzate, 0, 1))
            m.avvia("Sincronizzazione delle testate", True)
        Catch ex As Exception
            logger.ScriviLog("MenuTestateSincronizzazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaTestateSincronizzazione()
        Try
            WGSincronizzazioneCloud.NotizieSincronizzate = True
            WGSincronizzazioneCloud.RichiediSincronizzazione()
            MostraAvvisoSincronizzazione(TestoSceltaSincronizzata("Le testate dei quotidiani e le loro categorie verranno sincronizzate con gli altri computer."))
        Catch ex As Exception
            logger.ScriviLog("AttivaTestateSincronizzazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaTestateSincronizzazione()
        Try
            WGSincronizzazioneCloud.NotizieSincronizzate = False
            MostraAvvisoSincronizzazione("Le testate dei quotidiani non verranno sincronizzate.")
        Catch ex As Exception
            logger.ScriviLog("DisattivaTestateSincronizzazione eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SincronizzaCloudAdesso()
        Try
            If Not WGSincronizzazioneCloud.Attiva Then
                MostraAvvisoSincronizzazione("Prima attiva la sincronizzazione.")
                Return
            End If
            If _processoSincronizzazioneCloud IsNot Nothing Then
                MostraAvvisoSincronizzazione("C'è già una sincronizzazione in corso, attendi che finisca.")
                Return
            End If

            _esitoSincronizzazioneCloud = Nothing
            Try
                _processoSincronizzazioneCloud = New WGProcessoInCorso()
                _processoSincronizzazioneCloud.Avvia("Sincronizzazione in corso, attendi", False)
            Catch exProcesso As Exception
                logger.ScriviLog("SincronizzaCloudAdesso avvio processo eccezione: " & exProcesso.Message)
            End Try

            Task.Run(New Action(AddressOf LavoroSincronizzazioneCloud))
        Catch ex As Exception
            logger.ScriviLog("SincronizzaCloudAdesso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub LavoroSincronizzazioneCloud()
        Try
            _esitoSincronizzazioneCloud = WGSincronizzazioneCloud.Sincronizza()
        Catch ex As Exception
            logger.ScriviLog("LavoroSincronizzazioneCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            _esitoSincronizzazioneCloud = Nothing
        Finally
            Try
                EseguiSuInterfacciaPrincipale(New MethodInvoker(AddressOf CompletaSincronizzazioneCloud))
            Catch exRientro As Exception
                logger.ScriviLog("LavoroSincronizzazioneCloud rientro eccezione: " & exRientro.Message)
            End Try
        End Try
    End Sub

    Private Sub CompletaSincronizzazioneCloud()
        Try
            Try
                If _processoSincronizzazioneCloud IsNot Nothing Then _processoSincronizzazioneCloud.Fine()
            Catch exFine As Exception
                logger.ScriviLog("CompletaSincronizzazioneCloud chiusura processo eccezione: " & exFine.Message)
            End Try
            _processoSincronizzazioneCloud = Nothing

            Dim esito As WGSincronizzazioneCloud.EsitoSincronizzazione = _esitoSincronizzazioneCloud
            If esito Is Nothing Then
                MostraAvvisoSincronizzazione("Sincronizzazione non riuscita.")
                Return
            End If

            Dim testo As New StringBuilder()
            testo.Append(If(String.IsNullOrWhiteSpace(esito.Messaggio), "Sincronizzazione terminata.", esito.Messaggio))
            For Each avviso As String In esito.Avvisi
                testo.Append(" " & avviso)
            Next
            MostraAvvisoSincronizzazione(testo.ToString())
        Catch ex As Exception
            logger.ScriviLog("CompletaSincronizzazioneCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InformazioniSincronizzazioneCloud()
        Dim contatti As CLSContatti = Nothing
        Try
            Dim testo As New StringBuilder()
            If WGSincronizzazioneCloud.Collegato Then
                testo.Append("Account Filen: " & WGSincronizzazioneCloud.Email & ". ")
                testo.Append("Sincronizzazione tra computer: " & If(WGSincronizzazioneCloud.SincronizzazioneAbilitata, "attivata", "disattivata") & ". ")
            Else
                testo.Append("Non è stato eseguito l'accesso a Filen, quindi la sincronizzazione tra computer è disattivata. ")
            End If

            Dim chiaveRubrica As String = WGSincronizzazioneCloud.RubricaSincronizzata
            If String.IsNullOrWhiteSpace(chiaveRubrica) Then
                testo.Append("Rubrica: nessuna. ")
            Else
                contatti = New CLSContatti()
                Dim nomeRubrica As String = contatti.NomeRubrica(chiaveRubrica)
                testo.Append("Rubrica: " & If(String.IsNullOrWhiteSpace(nomeRubrica), "non più presente", nomeRubrica) & ". ")
            End If

            Dim chiaveAgenda As String = WGSincronizzazioneCloud.AgendaSincronizzata
            If String.IsNullOrWhiteSpace(chiaveAgenda) Then
                testo.Append("Agenda: nessuna. ")
            Else
                Dim nomeAgenda As String = CLSAgenda.GetInstance().NomeAgenda(chiaveAgenda)
                testo.Append("Agenda: " & If(String.IsNullOrWhiteSpace(nomeAgenda), "non più presente", nomeAgenda) & ". ")
            End If

            testo.Append("Radio: " & If(WGSincronizzazioneCloud.RadioSincronizzata, "sincronizzate", "non sincronizzate") & ". ")
            testo.Append("Testate dei quotidiani: " & If(WGSincronizzazioneCloud.NotizieSincronizzate, "sincronizzate", "non sincronizzate") & ". ")
            testo.Append("Dati di WinGabriel: " & If(WGCloudDati.DatiAbilitati, "sincronizzati", "non sincronizzati") & ". ")
            If Not String.IsNullOrWhiteSpace(WGCloudDati.UltimoRiepilogo) Then testo.Append(WGCloudDati.UltimoRiepilogo & " ")
            testo.Append("Backup sul cloud: " & If(WGCloudDati.BackupSuCloud, "attivi", "non attivi") & ".")
            MostraAvvisoSincronizzazione(testo.ToString())
        Catch ex As Exception
            logger.ScriviLog("InformazioniSincronizzazioneCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        Finally
            Try
                If contatti IsNot Nothing Then contatti.Dispose()
            Catch exChiusura As Exception
                logger.ScriviLog("InformazioniSincronizzazioneCloud chiusura eccezione: " & exChiusura.Message)
            End Try
        End Try
    End Sub

    Private _messaggioCloud As String = ""
    Private _cartelleCloud As List(Of CartellaDatiCloud) = Nothing
    Private _backupCloud As List(Of InfoBackupCloud) = Nothing
    Private _percorsoBackupScaricato As String = ""
    Private _backupCloudDaScaricare As String = ""
    Private _percorsoBackupDaCaricare As String = ""

    Private Sub MostraMenuCloud()
        Try
            Dim m As New WGMenu()
            If Not WGSincronizzazioneCloud.Collegato Then
                m.aggiungi("Accedi con il tuo account Filen", AddressOf MostraCampiAccessoFilen)
                m.aggiungi("Crea un account Filen", AddressOf MostraCampiRegistrazioneFilen)
                m.aggiungi("Reinvia l'email di conferma dell'account", AddressOf MostraCampiConfermaFilen)
            Else
                m.aggiungi("Sincronizzazione tra computer", AddressOf MostraMenuSincronizzazioneCloud)
                m.aggiungi("Dati di WinGabriel", AddressOf MostraMenuDatiCloud)
                m.aggiungi("Backup sul cloud", AddressOf MostraMenuBackupCloud)
                m.aggiungi("Sincronizza adesso", AddressOf SincronizzaCloudAdesso)
            End If
            m.aggiungi("Backup e ripristino", AddressOf MostraMenuBackup)
            m.aggiungi("Spazio occupato", AddressOf MostraSpazioOccupatoCloud)
            m.aggiungi("Informazioni sul cloud", AddressOf InformazioniSincronizzazioneCloud)
            If WGSincronizzazioneCloud.Collegato Then m.aggiungi("Esci dall'account Filen", AddressOf EsciDaFilen)
            m.avvia("Cloud")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuDatiCloud()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Stato della sincronizzazione dei dati", AddressOf MenuAttivazioneDatiCloud)
            m.aggiungi("Cartelle da sincronizzare", AddressOf MostraCartelleDatiCloud)
            m.aggiungi("Informazioni sui dati sincronizzati", AddressOf InformazioniDatiCloud)
            m.avvia("Dati di WinGabriel")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuDatiCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MenuAttivazioneDatiCloud()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaDatiCloud)
            m.aggiungi("Disattivata", AddressOf DisattivaDatiCloud)
            m.posiziona(If(WGCloudDati.DatiAbilitati, 0, 1))
            m.avvia("Sincronizzazione dei dati di WinGabriel", True)
        Catch ex As Exception
            logger.ScriviLog("MenuAttivazioneDatiCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaDatiCloud()
        Try
            WGCloudDati.DatiAbilitati = True
            Dim testo As String = "Sincronizzazione attiva."
            If WGSincronizzazioneCloud.SincronizzazioneAbilitata Then
                WGSincronizzazioneCloud.RichiediSincronizzazione()
            Else
                testo &= " Attiva anche la sincronizzazione tra computer."
            End If
            MostraAvvisoSincronizzazione(testo)
        Catch ex As Exception
            logger.ScriviLog("AttivaDatiCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaDatiCloud()
        Try
            WGCloudDati.DatiAbilitati = False
            MostraAvvisoSincronizzazione("Sincronizzazione disattiva.")
        Catch ex As Exception
            logger.ScriviLog("DisattivaDatiCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraCartelleDatiCloud()
        Try
            EseguiOperazioneAccountFilen("Calcolo la dimensione delle cartelle, attendi", AddressOf LavoroCartelleDatiCloud, AddressOf CartelleDatiCloudCalcolate)
        Catch ex As Exception
            logger.ScriviLog("MostraCartelleDatiCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function LavoroCartelleDatiCloud() As Boolean
        Try
            _cartelleCloud = WGCloudDati.ElencoCartelle()
            Return True
        Catch ex As Exception
            logger.ScriviLog("LavoroCartelleDatiCloud eccezione: " & ex.Message)
            _cartelleCloud = Nothing
            Return False
        End Try
    End Function

    Private Function DescrizioneCartellaDatiCloud(cartella As CartellaDatiCloud) As String
        Try
            Return WGCloudDati.DescriviNomeCartella(cartella.Nome) & ", " & WGDb.WGBackup.DescriviDimensione(cartella.Dimensione) & ", " & If(cartella.Inclusa, "sincronizzata", "non sincronizzata")
        Catch ex As Exception
            logger.ScriviLog("DescrizioneCartellaDatiCloud eccezione: " & ex.Message)
            Return If(cartella Is Nothing, "", cartella.Nome)
        End Try
    End Function

    Private Sub CartelleDatiCloudCalcolate(riuscita As Boolean)
        Try
            If _cartelleCloud Is Nothing OrElse _cartelleCloud.Count = 0 Then
                MostraAvvisoSincronizzazione("Non ci sono cartelle da sincronizzare nella cartella dei dati.")
                Return
            End If
            Dim m As New WGMenu()
            For Each cartella As CartellaDatiCloud In _cartelleCloud
                Dim voce As New VoceNomeCloud(cartella.Nome)
                AddHandler voce.Scelto, AddressOf MenuCartellaDatiCloud
                m.aggiungi(DescrizioneCartellaDatiCloud(cartella), AddressOf voce.Scegli)
            Next
            m.avvia("Cartelle da sincronizzare")
        Catch ex As Exception
            logger.ScriviLog("CartelleDatiCloudCalcolate eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MenuCartellaDatiCloud(nome As String)
        Try
            Dim inclusa As Boolean = True
            If _cartelleCloud IsNot Nothing Then
                For Each cartella As CartellaDatiCloud In _cartelleCloud
                    If String.Equals(cartella.Nome, nome, StringComparison.OrdinalIgnoreCase) Then
                        inclusa = cartella.Inclusa
                        Exit For
                    End If
                Next
            End If
            Dim voce As New VoceMenuCloud(nome)
            AddHandler voce.Scelta, AddressOf CartellaDatiCloudScelta
            Dim m As New WGMenu()
            m.aggiungi("Sincronizzata", AddressOf voce.Si)
            m.aggiungi("Non sincronizzata", AddressOf voce.No)
            m.posiziona(If(inclusa, 0, 1))
            m.avvia(WGCloudDati.DescriviNomeCartella(nome), True)
        Catch ex As Exception
            logger.ScriviLog("MenuCartellaDatiCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub CartellaDatiCloudScelta(chiave As String, valore As Boolean)
        Try
            WGCloudDati.ImpostaCartellaSincronizzata(chiave, valore)
            If _cartelleCloud IsNot Nothing Then
                For Each cartella As CartellaDatiCloud In _cartelleCloud
                    If String.Equals(cartella.Nome, chiave, StringComparison.OrdinalIgnoreCase) Then cartella.Inclusa = valore
                Next
            End If
            If valore Then
                WGSincronizzazioneCloud.RichiediSincronizzazione()
                MostraAvvisoSincronizzazione(WGCloudDati.DescriviNomeCartella(chiave) & " verrà sincronizzata con gli altri computer.")
            Else
                MostraAvvisoSincronizzazione(WGCloudDati.DescriviNomeCartella(chiave) & " non verrà più sincronizzata. I file già sul cloud restano dove sono.")
            End If
        Catch ex As Exception
            logger.ScriviLog("CartellaDatiCloudScelta eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub InformazioniDatiCloud()
        Try
            MostraAvvisoSincronizzazione(If(WGCloudDati.DatiAbilitati, "Sincronizzazione attiva.", "Sincronizzazione disattiva."))
        Catch ex As Exception
            logger.ScriviLog("InformazioniDatiCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuBackupCloud()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Copia dei backup sul cloud", AddressOf MenuAttivazioneBackupCloud)
            m.aggiungi("Carica sul cloud l'ultimo backup", AddressOf CaricaUltimoBackupSulCloud)
            m.aggiungi("Backup presenti sul cloud", AddressOf MostraBackupSulCloud)
            m.avvia("Backup sul cloud")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuBackupCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MenuAttivazioneBackupCloud()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attivata", AddressOf AttivaBackupCloud)
            m.aggiungi("Disattivata", AddressOf DisattivaBackupCloud)
            m.posiziona(If(WGCloudDati.BackupSuCloud, 0, 1))
            m.avvia("Copia dei backup sul cloud", True)
        Catch ex As Exception
            logger.ScriviLog("MenuAttivazioneBackupCloud eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaBackupCloud()
        Try
            WGCloudDati.BackupSuCloud = True
            MostraAvvisoSincronizzazione("Da ora ogni backup completato viene copiato anche sul cloud. Sul cloud resta sempre un solo backup, l'ultimo: quelli precedenti vengono tolti per non occupare spazio.")
        Catch ex As Exception
            logger.ScriviLog("AttivaBackupCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DisattivaBackupCloud()
        Try
            WGCloudDati.BackupSuCloud = False
            MostraAvvisoSincronizzazione("I backup non vengono più copiati sul cloud. Quelli già caricati restano dove sono.")
        Catch ex As Exception
            logger.ScriviLog("DisattivaBackupCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub CaricaUltimoBackupSulCloud()
        Try
            _percorsoBackupDaCaricare = WGCloudDati.UltimoBackupLocale()
            If String.IsNullOrWhiteSpace(_percorsoBackupDaCaricare) Then
                MostraAvvisoSincronizzazione("Non c'è nessun backup su questo computer da caricare. Esegui prima un backup.")
                Return
            End If
            EseguiOperazioneAccountFilen("Copio il backup sul cloud, attendi", AddressOf LavoroCaricaBackupCloud, AddressOf BackupCloudCaricato)
        Catch ex As Exception
            logger.ScriviLog("CaricaUltimoBackupSulCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function LavoroCaricaBackupCloud() As Boolean
        Try
            Dim messaggio As String = ""
            Dim esito As Boolean = WGCloudDati.CaricaBackupSulCloud(_percorsoBackupDaCaricare, messaggio)
            _messaggioCloud = messaggio
            Return esito
        Catch ex As Exception
            logger.ScriviLog("LavoroCaricaBackupCloud eccezione: " & ex.Message)
            _messaggioCloud = "Non è stato possibile copiare il backup sul cloud."
            Return False
        End Try
    End Function

    Private Sub BackupCloudCaricato(riuscita As Boolean)
        Try
            MostraAvvisoSincronizzazione(If(String.IsNullOrWhiteSpace(_messaggioCloud), If(riuscita, "Backup copiato sul cloud.", "Non è stato possibile copiare il backup sul cloud."), _messaggioCloud))
        Catch ex As Exception
            logger.ScriviLog("BackupCloudCaricato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraBackupSulCloud()
        Try
            EseguiOperazioneAccountFilen("Leggo l'elenco dei backup sul cloud, attendi", AddressOf LavoroElencoBackupCloud, AddressOf ElencoBackupCloudLetto)
        Catch ex As Exception
            logger.ScriviLog("MostraBackupSulCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function LavoroElencoBackupCloud() As Boolean
        Try
            Dim messaggio As String = ""
            _backupCloud = WGCloudDati.ElencoBackupCloud(messaggio)
            _messaggioCloud = messaggio
            Return True
        Catch ex As Exception
            logger.ScriviLog("LavoroElencoBackupCloud eccezione: " & ex.Message)
            _backupCloud = Nothing
            _messaggioCloud = "Non è stato possibile leggere i backup sul cloud."
            Return False
        End Try
    End Function

    Private Sub ElencoBackupCloudLetto(riuscita As Boolean)
        Try
            If _backupCloud Is Nothing OrElse _backupCloud.Count = 0 Then
                MostraAvvisoSincronizzazione(If(String.IsNullOrWhiteSpace(_messaggioCloud), "Sul cloud non ci sono backup.", _messaggioCloud))
                Return
            End If
            Dim m As New WGMenu()
            For Each backup As InfoBackupCloud In _backupCloud
                Dim voce As New VoceNomeCloud(backup.Nome)
                AddHandler voce.Scelto, AddressOf MenuBackupCloudScelto
                m.aggiungi(backup.Descrizione, AddressOf voce.Scegli)
            Next
            m.avvia("Backup sul cloud da scaricare")
        Catch ex As Exception
            logger.ScriviLog("ElencoBackupCloudLetto eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MenuBackupCloudScelto(nome As String)
        Try
            Dim ripristina As New VoceNomeCloud(nome)
            AddHandler ripristina.Scelto, AddressOf ScaricaBackupCloudScelto
            Dim elimina As New VoceNomeCloud(nome)
            AddHandler elimina.Scelto, AddressOf ChiediConfermaEliminazioneBackupCloud
            Dim m As New WGMenu()
            m.aggiungi("Scarica e ripristina", AddressOf ripristina.Scegli)
            m.aggiungi("Elimina dal cloud", AddressOf elimina.Scegli)
            m.avvia("Backup sul cloud")
        Catch ex As Exception
            logger.ScriviLog("MenuBackupCloudScelto eccezione: " & ex.Message)
        End Try
    End Sub

    Private _domandaEliminaBackupCloud As WGDomanda
    Private _backupCloudDaEliminare As String = ""

    Private Sub ChiediConfermaEliminazioneBackupCloud(nome As String)
        Try
            _backupCloudDaEliminare = If(nome, "")
            _domandaEliminaBackupCloud = New WGDomanda()
            AddHandler _domandaEliminaBackupCloud.rispostadomanda, AddressOf RispostaEliminazioneBackupCloud
            _domandaEliminaBackupCloud.avvia("Vuoi davvero eliminare questo backup dal cloud? Non si può annullare.", True)
        Catch ex As Exception
            logger.ScriviLog("ChiediConfermaEliminazioneBackupCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RispostaEliminazioneBackupCloud(e As DialogResult)
        Try
            Try
                RemoveHandler _domandaEliminaBackupCloud.rispostadomanda, AddressOf RispostaEliminazioneBackupCloud
            Catch exRimozione As Exception
                logger.ScriviLog("RispostaEliminazioneBackupCloud rimozione gestore eccezione: " & exRimozione.Message)
            End Try
            If e <> DialogResult.Yes Then Return
            EseguiOperazioneAccountFilen("Elimino il backup dal cloud, attendi", AddressOf LavoroEliminaBackupCloud, AddressOf BackupCloudEliminato)
        Catch ex As Exception
            logger.ScriviLog("RispostaEliminazioneBackupCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function LavoroEliminaBackupCloud() As Boolean
        Try
            Dim messaggio As String = ""
            Dim esito As Boolean = WGCloudDati.EliminaBackupDalCloud(_backupCloudDaEliminare, messaggio)
            _messaggioCloud = messaggio
            Return esito
        Catch ex As Exception
            logger.ScriviLog("LavoroEliminaBackupCloud eccezione: " & ex.Message)
            _messaggioCloud = "Non è stato possibile eliminare il backup dal cloud."
            Return False
        End Try
    End Function

    Private Sub BackupCloudEliminato(riuscita As Boolean)
        Try
            MostraAvvisoSincronizzazione(If(String.IsNullOrWhiteSpace(_messaggioCloud), If(riuscita, "Il backup è stato eliminato dal cloud.", "Non è stato possibile eliminare il backup dal cloud."), _messaggioCloud))
        Catch ex As Exception
            logger.ScriviLog("BackupCloudEliminato eccezione: " & ex.Message)
        End Try
    End Sub

    Private _domandaEliminaBackup As WGDomanda
    Private _archivioDaEliminare As String = ""

    Private Sub ChiediConfermaEliminazioneBackup(percorso As String)
        Try
            _archivioDaEliminare = If(percorso, "")
            _domandaEliminaBackup = New WGDomanda()
            AddHandler _domandaEliminaBackup.rispostadomanda, AddressOf RispostaEliminazioneBackup
            _domandaEliminaBackup.avvia("Vuoi davvero eliminare questo backup? Non si può annullare.", True)
        Catch ex As Exception
            logger.ScriviLog("ChiediConfermaEliminazioneBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RispostaEliminazioneBackup(e As DialogResult)
        Try
            Try
                RemoveHandler _domandaEliminaBackup.rispostadomanda, AddressOf RispostaEliminazioneBackup
            Catch exRimozione As Exception
                logger.ScriviLog("RispostaEliminazioneBackup rimozione gestore eccezione: " & exRimozione.Message)
            End Try
            If e <> DialogResult.Yes Then Return
            Dim percorso As String = _archivioDaEliminare
            _archivioDaEliminare = ""
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                MostraAvvisoBackup("Il backup non è più disponibile.")
                Return
            End If
            Try
                File.Delete(percorso)
                MostraAvvisoBackup("Il backup è stato eliminato.")
            Catch exEliminazione As Exception
                logger.ScriviLog("RispostaEliminazioneBackup eliminazione eccezione: " & exEliminazione.Message)
                MostraAvvisoBackup("Non è stato possibile eliminare il backup.")
            End Try
        Catch ex As Exception
            logger.ScriviLog("RispostaEliminazioneBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScaricaBackupCloudScelto(nome As String)
        Try
            _backupCloudDaScaricare = If(nome, "")
            EseguiOperazioneAccountFilen("Scarico il backup dal cloud, attendi", AddressOf LavoroScaricaBackupCloud, AddressOf BackupCloudScaricato)
        Catch ex As Exception
            logger.ScriviLog("ScaricaBackupCloudScelto eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function LavoroScaricaBackupCloud() As Boolean
        Try
            Dim percorso As String = ""
            Dim messaggio As String = ""
            Dim esito As Boolean = WGCloudDati.ScaricaBackupDalCloud(_backupCloudDaScaricare, percorso, messaggio)
            _percorsoBackupScaricato = percorso
            _messaggioCloud = messaggio
            Return esito
        Catch ex As Exception
            logger.ScriviLog("LavoroScaricaBackupCloud eccezione: " & ex.Message)
            _percorsoBackupScaricato = ""
            _messaggioCloud = "Non è stato possibile scaricare il backup dal cloud."
            Return False
        End Try
    End Function

    Private Sub BackupCloudScaricato(riuscita As Boolean)
        Try
            If riuscita AndAlso Not String.IsNullOrWhiteSpace(_percorsoBackupScaricato) Then
                MenuContenutoArchivio(_percorsoBackupScaricato)
            Else
                MostraAvvisoSincronizzazione(If(String.IsNullOrWhiteSpace(_messaggioCloud), "Non è stato possibile scaricare il backup dal cloud.", _messaggioCloud))
            End If
        Catch ex As Exception
            logger.ScriviLog("BackupCloudScaricato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraSpazioOccupatoCloud()
        Try
            EseguiOperazioneAccountFilen("Calcolo lo spazio occupato, attendi", AddressOf LavoroSpazioOccupatoCloud, AddressOf SpazioOccupatoCloudCalcolato)
        Catch ex As Exception
            logger.ScriviLog("MostraSpazioOccupatoCloud eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function LavoroSpazioOccupatoCloud() As Boolean
        Try
            _messaggioCloud = WGCloudDati.RiepilogoSpazio()
            Return True
        Catch ex As Exception
            logger.ScriviLog("LavoroSpazioOccupatoCloud eccezione: " & ex.Message)
            _messaggioCloud = "Non è stato possibile calcolare lo spazio occupato."
            Return False
        End Try
    End Function

    Private Sub SpazioOccupatoCloudCalcolato(riuscita As Boolean)
        Try
            MostraAvvisoSincronizzazione(If(String.IsNullOrWhiteSpace(_messaggioCloud), "Non è stato possibile calcolare lo spazio occupato.", _messaggioCloud))
        Catch ex As Exception
            logger.ScriviLog("SpazioOccupatoCloudCalcolato eccezione: " & ex.Message)
        End Try
    End Sub

    Private _dialogoCartellaBackup As WGFileDialog
    Private _campiRitenzioneBackup As Campi
    Private _domandaRipristinoBackup As WGDomanda
    Private _archivioDaRipristinare As String = ""
    Private _voceDaRipristinare As String = ""
    Private _processoBackupManuale As WGProcessoInCorso = Nothing
    Private _destinazioneBackupManuale As String = ""
    Private _percorsoArchivioBackupManuale As String = ""
    Private _messaggioBackupManuale As String = ""
    Private _esitoBackupManuale As Boolean = False
    Private _processoRipristinoBackup As WGProcessoInCorso = Nothing
    Private _archivioRipristinoInCorso As String = ""
    Private _voceRipristinoInCorso As String = ""
    Private _messaggioRipristinoBackup As String = ""

    Private Sub MostraMenuBackup()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Esegui subito il backup", AddressOf EseguiBackupAdesso)
            m.aggiungi("Cartella dove salvare i backup", AddressOf ScegliCartellaBackup)
            m.aggiungi("Backup automatico", AddressOf MenuIntervalloBackup)
            m.aggiungi("Conservazione dei backup", AddressOf MenuRitenzioneBackup)
            m.aggiungi("Ripristina i dati da un backup", AddressOf MenuRipristinoBackup)
            m.aggiungi("Informazioni sui backup", AddressOf InformazioniBackup)
            m.avvia("Backup e ripristino")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraAvvisoBackup(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("MostraAvvisoBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub InformazioniBackup()
        Try
            Dim destinazione As String = WGDb.WGBackup.CartellaBackup
            Dim testo As New StringBuilder()

            If String.IsNullOrWhiteSpace(destinazione) Then
                testo.Append("Non è ancora stata scelta la cartella dei backup. ")
            Else
                testo.Append("Cartella dei backup: " & destinazione & ". ")
            End If

            testo.Append("Backup automatico: " & WGDb.WGBackup.DescriviIntervallo(WGDb.WGBackup.Intervallo) & ". ")
            testo.Append("Conservazione: " & WGDb.WGBackup.GiorniRitenzione.ToString() & " giorni, con almeno " & WGDb.WGBackup.CopieMinimeDaConservare.ToString() & " copie sempre conservate. ")

            Dim ultimo As DateTime = WGDb.WGBackup.UltimoBackup
            If ultimo = DateTime.MinValue Then
                testo.Append("Nessun backup ancora eseguito. ")
            Else
                testo.Append("Ultimo backup: " & ultimo.ToString("dd/MM/yyyy HH:mm") & ". ")
            End If

            Dim elenco As List(Of WGDb.InfoBackup) = WGDb.WGBackup.ElencoBackup(destinazione)
            testo.Append("Backup disponibili: " & elenco.Count.ToString() & ".")

            MostraAvvisoBackup(testo.ToString())
        Catch ex As Exception
            logger.ScriviLog("InformazioniBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiBackupAdesso()
        Try
            Dim destinazione As String = WGDb.WGBackup.CartellaBackup
            If String.IsNullOrWhiteSpace(destinazione) OrElse Not Directory.Exists(destinazione) Then
                MostraAvvisoBackup("Prima devi scegliere la cartella dove salvare i backup.")
                ScegliCartellaBackup()
                Return
            End If
            EseguiBackupSuCartella(destinazione)
        Catch ex As Exception
            logger.ScriviLog("EseguiBackupAdesso eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiBackupSuCartella(destinazione As String)
        Try
            If _processoBackupManuale IsNot Nothing Then
                MostraAvvisoBackup("C'è già un backup in corso, attendi che finisca.")
                Return
            End If

            Try
                If PreavvisoManager.IstanzaAttiva IsNot Nothing AndAlso PreavvisoManager.IstanzaAttiva.BackupInCorso Then
                    MostraAvvisoBackup("È in corso il backup automatico, riprova fra qualche minuto.")
                    Return
                End If
            Catch exStato As Exception
                logger.ScriviLog("EseguiBackupSuCartella stato backup automatico eccezione: " & exStato.Message)
            End Try

            _destinazioneBackupManuale = destinazione
            _percorsoArchivioBackupManuale = ""
            _messaggioBackupManuale = ""
            _esitoBackupManuale = False

            Try
                _processoBackupManuale = New WGProcessoInCorso()
                _processoBackupManuale.Avvia("Sto eseguendo il backup dei dati, attendi", False)
            Catch exProcesso As Exception
                logger.ScriviLog("EseguiBackupSuCartella avvio processo eccezione: " & exProcesso.Message)
            End Try

            Task.Run(New Action(AddressOf LavoroBackupManuale))
        Catch ex As Exception
            logger.ScriviLog("EseguiBackupSuCartella eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroBackupManuale()
        Try
            Dim percorsoArchivio As String = ""
            Dim messaggio As String = ""
            _esitoBackupManuale = WGDb.WGBackup.EseguiBackup(_destinazioneBackupManuale, percorsoArchivio, messaggio)
            _percorsoArchivioBackupManuale = percorsoArchivio
            _messaggioBackupManuale = messaggio
        Catch ex As Exception
            logger.ScriviLog("LavoroBackupManuale eccezione: " & ex.Message)
            _esitoBackupManuale = False
            _messaggioBackupManuale = "Backup non riuscito: " & ex.Message
        Finally
            Try
                EseguiSuInterfacciaPrincipale(New MethodInvoker(AddressOf CompletaBackupManuale))
            Catch exRientro As Exception
                logger.ScriviLog("LavoroBackupManuale rientro eccezione: " & exRientro.Message)
            End Try
        End Try
    End Sub

    Private Sub CompletaBackupManuale()
        Try
            Try
                If _processoBackupManuale IsNot Nothing Then
                    _processoBackupManuale.Fine()
                    _processoBackupManuale = Nothing
                End If
            Catch exFine As Exception
                logger.ScriviLog("CompletaBackupManuale chiusura processo eccezione: " & exFine.Message)
                _processoBackupManuale = Nothing
            End Try

            Dim testo As String = _messaggioBackupManuale
            If _esitoBackupManuale AndAlso Not String.IsNullOrWhiteSpace(_percorsoArchivioBackupManuale) Then
                testo = _messaggioBackupManuale & " Il file si trova in " & _percorsoArchivioBackupManuale & "."
            End If
            If String.IsNullOrWhiteSpace(testo) Then testo = "Backup non riuscito."
            MostraAvvisoBackup(testo)
        Catch ex As Exception
            logger.ScriviLog("CompletaBackupManuale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliCartellaBackup()
        Try
            _dialogoCartellaBackup = New WGFileDialog()
            AddHandler _dialogoCartellaBackup.singolaselezione, AddressOf CartellaBackupScelta
            _dialogoCartellaBackup.Avvia("")
        Catch ex As Exception
            logger.ScriviLog("ScegliCartellaBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub CartellaBackupScelta(elemento As String)
        Try
            Try
                RemoveHandler _dialogoCartellaBackup.singolaselezione, AddressOf CartellaBackupScelta
            Catch exRimozione As Exception
                logger.ScriviLog("CartellaBackupScelta rimozione gestore eccezione: " & exRimozione.Message)
            End Try

            Dim cartella As String = If(elemento, "").Trim()
            If String.IsNullOrWhiteSpace(cartella) Then Return

            If Not Directory.Exists(cartella) Then
                Try
                    cartella = Path.GetDirectoryName(cartella)
                Catch exCartella As Exception
                    logger.ScriviLog("CartellaBackupScelta risalita cartella eccezione: " & exCartella.Message)
                    cartella = ""
                End Try
            End If

            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then
                MostraAvvisoBackup("La cartella selezionata non è valida.")
                Return
            End If

            If Not WGDb.WGDatabase.VerificaCartellaScrivibile(cartella) Then
                MostraAvvisoBackup("La cartella selezionata non è scrivibile. Scegline un'altra.")
                Return
            End If

            WGDb.WGBackup.CartellaBackup = cartella
            RiavviaControlloBackupAutomatico()
            MostraAvvisoBackup("I backup verranno salvati in " & cartella & ".")
        Catch ex As Exception
            logger.ScriviLog("CartellaBackupScelta eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RiavviaControlloBackupAutomatico()
        Try
            If PreavvisoManager.IstanzaAttiva Is Nothing Then Return
            PreavvisoManager.IstanzaAttiva.RiallineaTimerBackup()
        Catch ex As Exception
            logger.ScriviLog("RiavviaControlloBackupAutomatico eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaIntervalloBackup(valore As WGDb.IntervalloBackup)
        Try
            WGDb.WGBackup.Intervallo = valore
            RiavviaControlloBackupAutomatico()
            If valore <> WGDb.IntervalloBackup.Mai AndAlso String.IsNullOrWhiteSpace(WGDb.WGBackup.CartellaBackup) Then
                MostraAvvisoBackup("Backup automatico impostato su: " & WGDb.WGBackup.DescriviIntervallo(valore) & ". Ora devi scegliere la cartella dove salvare i backup.")
                ScegliCartellaBackup()
                Return
            End If
            MostraAvvisoBackup("Backup automatico impostato su: " & WGDb.WGBackup.DescriviIntervallo(valore) & ".")
        Catch ex As Exception
            logger.ScriviLog("ImpostaIntervalloBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuIntervalloBackup()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nessun backup automatico", Sub() ImpostaIntervalloBackup(WGDb.IntervalloBackup.Mai))
            m.aggiungi("Ogni giorno", Sub() ImpostaIntervalloBackup(WGDb.IntervalloBackup.Quotidiano))
            m.aggiungi("Ogni settimana", Sub() ImpostaIntervalloBackup(WGDb.IntervalloBackup.Settimanale))
            m.aggiungi("Ogni mese", Sub() ImpostaIntervalloBackup(WGDb.IntervalloBackup.Mensile))
            Dim indice As Integer = 0
            Try
                indice = CInt(WGDb.WGBackup.Intervallo)
            Catch exIndice As Exception
                logger.ScriviLog("MenuIntervalloBackup indice eccezione: " & exIndice.Message)
                indice = 0
            End Try
            If indice < 0 OrElse indice > 3 Then indice = 0
            m.posiziona(indice)
            m.avvia("Backup automatico", True)
        Catch ex As Exception
            logger.ScriviLog("MenuIntervalloBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuRitenzioneBackup()
        Try
            _campiRitenzioneBackup = New Campi()
            _campiRitenzioneBackup.AggiungiNumerico("Per quanti giorni conservare i backup", "backup.giorni", True, False, WGDb.WGBackup.GiorniRitenzione.ToString())
            _campiRitenzioneBackup.AggiungiNumerico("Quante copie conservare sempre", "backup.copie", True, False, WGDb.WGBackup.CopieMinimeDaConservare.ToString())
            AddHandler _campiRitenzioneBackup.Fine, AddressOf ConfermaRitenzioneBackup
            _campiRitenzioneBackup.Avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuRitenzioneBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaRitenzioneBackup()
        Try
            Dim testoGiorni As String = ""
            Dim testoCopie As String = ""
            Try
                testoGiorni = _campiRitenzioneBackup.RecuperaValore("backup.giorni")
                testoCopie = _campiRitenzioneBackup.RecuperaValore("backup.copie")
            Catch exLettura As Exception
                logger.ScriviLog("ConfermaRitenzioneBackup lettura eccezione: " & exLettura.Message)
            End Try

            Try

            Catch exSmaltimento As Exception
                logger.ScriviLog("ConfermaRitenzioneBackup smaltimento eccezione: " & exSmaltimento.Message)
            End Try
            _campiRitenzioneBackup = Nothing

            Dim giorni As Integer = 0
            Dim copie As Integer = 0
            If Not Integer.TryParse(If(testoGiorni, "").Trim(), giorni) OrElse giorni <= 0 Then giorni = 30
            If Not Integer.TryParse(If(testoCopie, "").Trim(), copie) OrElse copie <= 0 Then copie = 3

            WGDb.WGBackup.GiorniRitenzione = giorni
            WGDb.WGBackup.CopieMinimeDaConservare = copie

            MostraAvvisoBackup("I backup verranno conservati per " & giorni.ToString() & " giorni, tenendo sempre almeno " & copie.ToString() & " copie.")
        Catch ex As Exception
            logger.ScriviLog("ConfermaRitenzioneBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuRipristinoBackup()
        Try
            Dim destinazione As String = WGDb.WGBackup.CartellaBackup
            If String.IsNullOrWhiteSpace(destinazione) OrElse Not Directory.Exists(destinazione) Then
                MostraAvvisoBackup("Prima devi scegliere la cartella dove si trovano i backup.")
                ScegliCartellaBackup()
                Return
            End If

            Dim elenco As List(Of WGDb.InfoBackup) = WGDb.WGBackup.ElencoBackup(destinazione)
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                MostraAvvisoBackup("Non è stato trovato nessun backup in " & destinazione & ".")
                Return
            End If

            Dim m As New WGMenu()
            For Each voce As WGDb.InfoBackup In elenco
                Dim percorsoVoce As String = voce.Percorso
                m.aggiungi(voce.Descrizione, Sub() MenuContenutoArchivio(percorsoVoce))
            Next
            m.avvia("Scegli il backup da ripristinare")
        Catch ex As Exception
            logger.ScriviLog("MenuRipristinoBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuContenutoArchivio(percorsoArchivio As String)
        Try
            If String.IsNullOrWhiteSpace(percorsoArchivio) OrElse Not File.Exists(percorsoArchivio) Then
                MostraAvvisoBackup("Il backup scelto non è più disponibile.")
                Return
            End If

            Dim m As New WGMenu()
            m.aggiungi("Ripristina tutto il backup", Sub() ChiediConfermaRipristino(percorsoArchivio, ""))

            Dim voceElimina As New VoceNomeCloud(percorsoArchivio)
            AddHandler voceElimina.Scelto, AddressOf ChiediConfermaEliminazioneBackup
            m.aggiungi("Elimina questo backup", AddressOf voceElimina.Scegli)

            Dim voci As List(Of String) = WGDb.WGBackup.ElencoContenutoArchivio(percorsoArchivio)
            If voci IsNot Nothing Then
                For Each nomeVoce As String In voci
                    Dim vocePresente As String = nomeVoce
                    m.aggiungi("Ripristina solo " & DescriviVoceArchivio(vocePresente), Sub() ChiediConfermaRipristino(percorsoArchivio, vocePresente))
                Next
            End If

            m.avvia("Cosa vuoi ripristinare")
        Catch ex As Exception
            logger.ScriviLog("MenuContenutoArchivio eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviVoceArchivio(nomeVoce As String) As String
        Try
            If String.IsNullOrWhiteSpace(nomeVoce) Then Return ""
            Dim parti() As String = nomeVoce.Split("/"c)
            If parti.Length <= 1 Then Return nomeVoce
            Dim nomeFile As String = parti(parti.Length - 1)
            If parti.Length >= 3 Then Return nomeFile & " della sezione " & parti(parti.Length - 2)
            Return nomeFile
        Catch ex As Exception
            logger.ScriviLog("DescriviVoceArchivio eccezione: " & ex.Message)
            Return If(nomeVoce, "")
        End Try
    End Function

    Private Sub ChiediConfermaRipristino(percorsoArchivio As String, nomeVoce As String)
        Try
            _archivioDaRipristinare = If(percorsoArchivio, "")
            _voceDaRipristinare = If(nomeVoce, "")

            Dim domanda As String = "Vuoi davvero ripristinare i dati da questo backup? I dati attuali verranno sostituiti, ma prima ne verrà salvata una copia di sicurezza."
            If Not String.IsNullOrWhiteSpace(_voceDaRipristinare) Then
                domanda = "Vuoi davvero ripristinare " & DescriviVoceArchivio(_voceDaRipristinare) & "? Il file attuale verrà sostituito, ma prima ne verrà salvata una copia di sicurezza."
            End If

            _domandaRipristinoBackup = New WGDomanda()
            AddHandler _domandaRipristinoBackup.rispostadomanda, AddressOf RispostaRipristinoBackup
            _domandaRipristinoBackup.avvia(domanda, True)
        Catch ex As Exception
            logger.ScriviLog("ChiediConfermaRipristino eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RispostaRipristinoBackup(e As DialogResult)
        Try
            Try
                RemoveHandler _domandaRipristinoBackup.rispostadomanda, AddressOf RispostaRipristinoBackup
            Catch exRimozione As Exception
                logger.ScriviLog("RispostaRipristinoBackup rimozione gestore eccezione: " & exRimozione.Message)
            End Try

            If e <> DialogResult.Yes Then Return

            _archivioRipristinoInCorso = _archivioDaRipristinare
            _voceRipristinoInCorso = _voceDaRipristinare
            _archivioDaRipristinare = ""
            _voceDaRipristinare = ""
            _messaggioRipristinoBackup = ""

            Try
                _processoRipristinoBackup = New WGProcessoInCorso()
                _processoRipristinoBackup.Avvia("Sto ripristinando i dati, attendi", False)
            Catch exProcesso As Exception
                logger.ScriviLog("RispostaRipristinoBackup avvio processo eccezione: " & exProcesso.Message)
            End Try

            Task.Run(New Action(AddressOf LavoroRipristinoBackup))
        Catch ex As Exception
            logger.ScriviLog("RispostaRipristinoBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroRipristinoBackup()
        Dim riuscito As Boolean = False
        Try
            Dim messaggio As String = ""
            riuscito = WGDb.WGBackup.Ripristina(_archivioRipristinoInCorso, _voceRipristinoInCorso, messaggio)
            _messaggioRipristinoBackup = messaggio
        Catch ex As Exception
            logger.ScriviLog("LavoroRipristinoBackup eccezione: " & ex.Message)
            _messaggioRipristinoBackup = "Ripristino non riuscito: " & ex.Message
            riuscito = False
        Finally
            Try
                If String.IsNullOrWhiteSpace(_messaggioRipristinoBackup) Then
                    _messaggioRipristinoBackup = If(riuscito, "Ripristino completato.", "Ripristino non riuscito.")
                End If
            Catch exTesto As Exception
                logger.ScriviLog("LavoroRipristinoBackup testo eccezione: " & exTesto.Message)
            End Try
            Try
                EseguiSuInterfacciaPrincipale(New MethodInvoker(AddressOf CompletaRipristinoBackup))
            Catch exRientro As Exception
                logger.ScriviLog("LavoroRipristinoBackup rientro eccezione: " & exRientro.Message)
            End Try
        End Try
    End Sub

    Private Sub CompletaRipristinoBackup()
        Try
            Try
                If _processoRipristinoBackup IsNot Nothing Then
                    _processoRipristinoBackup.Fine()
                    _processoRipristinoBackup = Nothing
                End If
            Catch exFine As Exception
                logger.ScriviLog("CompletaRipristinoBackup chiusura processo eccezione: " & exFine.Message)
                _processoRipristinoBackup = Nothing
            End Try

            _archivioRipristinoInCorso = ""
            _voceRipristinoInCorso = ""

            MostraAvvisoBackup(_messaggioRipristinoBackup)
        Catch ex As Exception
            logger.ScriviLog("CompletaRipristinoBackup eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuModalitaLetturaNumeri()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Numero intero", Sub() SalvaConfigurazioneGenerica("modalita_lettura_numeri", "intero", "I numeri verranno letti per intero, ad esempio quattro miliardi."))
            m.aggiungi("Cifra singola", Sub() SalvaConfigurazioneGenerica("modalita_lettura_numeri", "cifra", "I numeri verranno letti una cifra alla volta, ad esempio quattro zero zero zero."))
            m.aggiungi("A coppie", Sub() SalvaConfigurazioneGenerica("modalita_lettura_numeri", "coppie", "I numeri verranno letti a coppie di cifre."))
            m.aggiungi("A tre a tre", Sub() SalvaConfigurazioneGenerica("modalita_lettura_numeri", "triplette", "I numeri verranno letti a gruppi di tre cifre."))
            Dim impostazioni As New WGImpostazioniComuni()
            Dim indiceCorrente As Integer = 0
            Select Case impostazioni.ModalitaLetturaNumeri
                Case "cifra" : indiceCorrente = 1
                Case "coppie" : indiceCorrente = 2
                Case "triplette" : indiceCorrente = 3
                Case Else : indiceCorrente = 0
            End Select
            m.posiziona(indiceCorrente)
            m.avvia("Lettura dei numeri", True)
        Catch ex As Exception
            logger.ScriviLog("MenuModalitaLetturaNumeri eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function IndiceBinario(chiave As String, valorePrimaVoce As String, defaultVal As String) As Integer
        Try
            If String.Equals(LeggiConfigValore(chiave, defaultVal), valorePrimaVoce, StringComparison.OrdinalIgnoreCase) Then Return 0
            Return 1
        Catch ex As Exception
            logger.ScriviLog("IndiceBinario eccezione: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function IndiceMultiplo(chiave As String, valori As List(Of String), defaultVal As String) As Integer
        Try
            Dim attuale As String = LeggiConfigValore(chiave, defaultVal)
            Dim idx As Integer = valori.FindIndex(Function(v) String.Equals(v, attuale, StringComparison.OrdinalIgnoreCase))
            If idx < 0 Then Return 0
            Return idx
        Catch ex As Exception
            logger.ScriviLog("IndiceMultiplo eccezione: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub SalvaConfigurazioneGenerica(id As String, valore As String, Optional messaggioFeedback As String = "")
        SalvaConfig(id, valore)
        If Not String.IsNullOrEmpty(messaggioFeedback) Then

            Dim msg As New WGMessaggi()
            msg.Messaggio(messaggioFeedback)
            msg.ShowDialog()
        End If
    End Sub

    Private Sub ConfiguraOrdinamento()
        Dim fm As New WGFileManager.Recenti()

        fm.configurazione()
    End Sub

    Private Sub MostraMenuFile()
        Try
            Dim menuFile As New WGMenu()
            menuFile.aggiungi("Ordinamento file e cartelle", AddressOf ConfiguraOrdinamento)
            menuFile.aggiungi("Eliminazione dei file", AddressOf MostraMenuEliminazioneFile)
            menuFile.aggiungi("Apertura dei file di testo", AddressOf MostraMenuAperturaFileTesto)
            menuFile.aggiungi("Icona sul desktop", AddressOf MostraMenuIconaDesktop)
            menuFile.aggiungi("Cartella dati dell'applicazione", AddressOf ConfiguraCartellaDatiApplicazione)
            menuFile.avvia("Impostazioni File")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuFile eccezione: " & ex.Message)
        End Try
    End Sub

    Private _dialogoCartellaDati As WGFileDialog
    Private _domandaSpostamentoCartellaDati As WGDomanda
    Private _nuovaCartellaDatiSelezionata As String = ""
    Private _nuovaCartellaDatiDedicata As String = ""
    Private _processoSpostamentoDati As WGProcessoInCorso = Nothing
    Private _fileNonSpostatiDati As List(Of String) = Nothing
    Private _esitoSpostamentoDati As Boolean = False
    Private _vecchiaCartellaDatiAttuale As String = ""

    Private Sub ConfiguraCartellaDatiApplicazione()
        Try
            _vecchiaCartellaDatiAttuale = ""
            Try
                _vecchiaCartellaDatiAttuale = WGDb.WGDatabase.CartellaBaseDatiEffettiva()
                If String.IsNullOrWhiteSpace(_vecchiaCartellaDatiAttuale) Then
                    _vecchiaCartellaDatiAttuale = WGDb.WGDatabase.CartellaBaseDatiPredefinita()
                End If
            Catch exCartella As Exception
                logger.ScriviLog("ConfiguraCartellaDatiApplicazione lettura cartella attuale eccezione: " & exCartella.Message)
            End Try

            _dialogoCartellaDati = New WGFileDialog()
            AddHandler _dialogoCartellaDati.singolaselezione, AddressOf CartellaDatiScelta

            _dialogoCartellaDati.Avvia("")
        Catch ex As Exception
            logger.ScriviLog("ConfiguraCartellaDatiApplicazione eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                Dim msg As New WGMessaggi()
                msg.Messaggio("Si è verificato un errore durante l'apertura della configurazione della cartella dati: " & ex.Message)
                msg.ShowDialog()
            Catch
            End Try
        End Try
    End Sub

    Private Async Sub CartellaDatiScelta(elemento As String)
        Try
            Try
                RemoveHandler _dialogoCartellaDati.singolaselezione, AddressOf CartellaDatiScelta
            Catch
            End Try

            If String.IsNullOrWhiteSpace(elemento) OrElse Not Directory.Exists(elemento) Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("La cartella selezionata non è valida.")
                msg.ShowDialog()
                Return
            End If

            _nuovaCartellaDatiSelezionata = elemento

            ' Sottocartella WinGabriel oppure la cartella scelta com'è.
            Dim destinazioneDedicata As String = Await ConfigurazioneGuidata.ScegliSottocartellaDatiAsync(elemento)
            If destinazioneDedicata = "" Then Return
            _nuovaCartellaDatiSelezionata = destinazioneDedicata
            _nuovaCartellaDatiDedicata = destinazioneDedicata

            Dim stessaCartella As Boolean = False
            Try
                stessaCartella = String.Equals(
                            Path.GetFullPath(_nuovaCartellaDatiDedicata).TrimEnd(Path.DirectorySeparatorChar),
                            Path.GetFullPath(_vecchiaCartellaDatiAttuale).TrimEnd(Path.DirectorySeparatorChar),
                            StringComparison.OrdinalIgnoreCase)
            Catch exConfronto As Exception
                logger.ScriviLog("CartellaDatiScelta confronto percorsi eccezione: " & exConfronto.Message)
            End Try

            If stessaCartella Then
                Dim msgUguale As New WGMessaggi()
                msgUguale.Messaggio("La cartella selezionata è già quella in uso.")
                msgUguale.ShowDialog()
                Return
            End If

            _domandaSpostamentoCartellaDati = New WGDomanda()
            AddHandler _domandaSpostamentoCartellaDati.rispostadomanda, AddressOf RispostaSpostamentoCartellaDati
            _domandaSpostamentoCartellaDati.avvia("Vuoi spostare i file di dati esistenti nella nuova cartella scelta?", True)
        Catch ex As Exception
            logger.ScriviLog("CartellaDatiScelta eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                Dim msgErr As New WGMessaggi()
                msgErr.Messaggio("Si è verificato un errore durante la scelta della cartella dati: " & ex.Message)
                msgErr.ShowDialog()
            Catch
            End Try
        End Try
    End Sub

    Private Sub RispostaSpostamentoCartellaDati(e As DialogResult)
        Try
            Try
                RemoveHandler _domandaSpostamentoCartellaDati.rispostadomanda, AddressOf RispostaSpostamentoCartellaDati
            Catch
            End Try

            If e = DialogResult.Cancel Then Return

            Dim impostata As Boolean = False
            Try
                If WGDb.WGDatabase.VerificaCartellaScrivibile(_nuovaCartellaDatiSelezionata) Then
                    WGDb.WGDatabase.PreparaCartellaDati(_nuovaCartellaDatiSelezionata)
                    impostata = configManager.SalvaValore(WGDb.WGDatabase.IdConfigurazioneCartellaBaseDati, _nuovaCartellaDatiSelezionata)
                End If
            Catch exImposta As Exception
                logger.ScriviLog("RispostaSpostamentoCartellaDati impostazione cartella eccezione: " & exImposta.Message)
                impostata = False
            End Try

            Try
                WGDb.WGDatabase.AzzeraCacheCartellaBaseDati()
            Catch exCache As Exception
                logger.ScriviLog("RispostaSpostamentoCartellaDati azzeramento cache eccezione: " & exCache.Message)
            End Try

            If Not impostata Then
                Dim msgErrore As New WGMessaggi()
                msgErrore.Messaggio("Non è stato possibile impostare la nuova cartella dati. Verificare che il percorso sia scrivibile.")
                msgErrore.ShowDialog()
                Return
            End If

            If e = DialogResult.Yes Then
                AvviaSpostamentoCartellaDati()
            ElseIf e = DialogResult.No Then
                RiallineaModuliDatiConNuovaCartella()

                Dim msgNoSpostamento As New WGMessaggi()
                msgNoSpostamento.Messaggio("Cartella dati aggiornata in:" & Environment.NewLine & _nuovaCartellaDatiDedicata & Environment.NewLine & "I file esistenti sono rimasti nella cartella precedente.")
                msgNoSpostamento.ShowDialog()
            End If
        Catch ex As Exception
            logger.ScriviLog("RispostaSpostamentoCartellaDati eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                Dim msgErr As New WGMessaggi()
                msgErr.Messaggio("Si è verificato un errore durante l'aggiornamento della cartella dati: " & ex.Message)
                msgErr.ShowDialog()
            Catch
            End Try
        End Try
    End Sub

    Private Sub AvviaSpostamentoCartellaDati()
        Try
            _fileNonSpostatiDati = New List(Of String)
            _esitoSpostamentoDati = False

            Try
                _processoSpostamentoDati = New WGProcessoInCorso()
                _processoSpostamentoDati.Avvia("Sto spostando i file di dati nella nuova cartella, attendi", False)
            Catch exProcesso As Exception
                logger.ScriviLog("AvviaSpostamentoCartellaDati avvio WGProcessoInCorso eccezione: " & exProcesso.Message)
            End Try

            Task.Run(New Action(AddressOf LavoroSpostamentoCartellaDati))
        Catch ex As Exception
            logger.ScriviLog("AvviaSpostamentoCartellaDati eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroSpostamentoCartellaDati()
        Try
            Dim destinazioneSpostamento As String = _nuovaCartellaDatiDedicata
            If String.IsNullOrWhiteSpace(destinazioneSpostamento) Then destinazioneSpostamento = _nuovaCartellaDatiSelezionata

            Dim nonSpostati As List(Of String) = Nothing
            _esitoSpostamentoDati = WGDb.WGDatabase.SpostaCartellaDatiVersoNuovaPosizione(_vecchiaCartellaDatiAttuale, destinazioneSpostamento, nonSpostati, AddressOf AvanzamentoSpostamentoCartellaDati)
            _fileNonSpostatiDati = nonSpostati
        Catch ex As Exception
            logger.ScriviLog("LavoroSpostamentoCartellaDati eccezione: " & ex.Message)
            _esitoSpostamentoDati = False
        Finally
            Try
                EseguiSuInterfacciaPrincipale(New MethodInvoker(AddressOf CompletaSpostamentoCartellaDati))
            Catch exRientro As Exception
                logger.ScriviLog("LavoroSpostamentoCartellaDati rientro eccezione: " & exRientro.Message)
            End Try
        End Try
    End Sub

    Private Sub AvanzamentoSpostamentoCartellaDati(fatti As Integer, totale As Integer)
        Try
            If _processoSpostamentoDati Is Nothing OrElse totale <= 0 Then Return
            Dim testo As String = fatti.ToString() & " di " & totale.ToString()
            EseguiSuInterfacciaPrincipale(New MethodInvoker(Sub() AggiornaStatoSpostamento(testo)))
        Catch ex As Exception
            logger.ScriviLog("AvanzamentoSpostamentoCartellaDati eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaStatoSpostamento(testo As String)
        Try
            If _processoSpostamentoDati Is Nothing Then Return
            _processoSpostamentoDati.AggiornaStato(testo)
        Catch ex As Exception
            logger.ScriviLog("AggiornaStatoSpostamento eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub CompletaSpostamentoCartellaDati()
        Try
            Try
                If _processoSpostamentoDati IsNot Nothing Then
                    _processoSpostamentoDati.Fine()
                    _processoSpostamentoDati = Nothing
                End If
            Catch exFineProcesso As Exception
                logger.ScriviLog("CompletaSpostamentoCartellaDati chiusura WGProcessoInCorso eccezione: " & exFineProcesso.Message)
            End Try

            RiallineaModuliDatiConNuovaCartella()

            Dim msgEsito As New WGMessaggi()
            If _esitoSpostamentoDati Then
                msgEsito.Messaggio("Cartella dati aggiornata e file esistenti spostati correttamente in:" & Environment.NewLine & _nuovaCartellaDatiDedicata)
            Else
                Dim elenco As String = ""
                Try
                    If _fileNonSpostatiDati IsNot Nothing AndAlso _fileNonSpostatiDati.Count > 0 Then
                        elenco = Environment.NewLine & "Alcuni file non sono stati spostati:" & Environment.NewLine & String.Join(Environment.NewLine, _fileNonSpostatiDati)
                    End If
                Catch exElenco As Exception
                    logger.ScriviLog("CompletaSpostamentoCartellaDati elenco eccezione: " & exElenco.Message)
                End Try
                msgEsito.Messaggio("Cartella dati aggiornata in:" & Environment.NewLine & _nuovaCartellaDatiDedicata & elenco)
            End If
            msgEsito.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CompletaSpostamentoCartellaDati eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RiallineaModuliDatiConNuovaCartella()
        Try
            If PreavvisoManager.IstanzaAttiva IsNot Nothing Then
                PreavvisoManager.IstanzaAttiva.RiallineaCartellaDatiAgenda()
            Else
                CLSAgenda.ReimpostaIstanza()
            End If
        Catch ex As Exception
            logger.ScriviLog("RiallineaModuliDatiConNuovaCartella - CLSAgenda: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMenuEliminazioneFile()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Sposta i file eliminati nel cestino", Sub() SalvaConfigurazioneGenerica("eliminacestino", "True", "I file eliminati verranno spostati nel cestino."))
            m.aggiungi("Elimina i file in modo permanente", Sub() SalvaConfigurazioneGenerica("eliminacestino", "False", "I file eliminati verranno cancellati in modo permanente, senza passare dal cestino."))
            m.posiziona(IndiceBinario("eliminacestino", "True", "True"))
            m.avvia("Eliminazione dei file", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuEliminazioneFile eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuAperturaFileTesto()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Apri i file di testo in modifica", Sub() SalvaConfigurazioneGenerica("modalitaAperturaTesto", "Modifica", "I file di testo verranno aperti in modifica."))
            m.aggiungi("Apri i file di testo in sola lettura", Sub() SalvaConfigurazioneGenerica("modalitaAperturaTesto", "SolaLettura", "I file di testo verranno aperti in sola lettura."))
            m.posiziona(IndiceBinario("modalitaAperturaTesto", "Modifica", "Modifica"))
            m.avvia("Apertura dei file di testo", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuAperturaFileTesto eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuIconaDesktop()
        Dim menuIconaDesktop As New WGMenu()
        menuIconaDesktop.aggiungi("Attiva creazione automatica icona", AddressOf ImpostaCreazioneIconaAttivata)
        menuIconaDesktop.aggiungi("Disattiva creazione automatica icona", AddressOf ImpostaCreazioneIconaDisattivata)
        menuIconaDesktop.posiziona(IndiceBinario("crea_icona_desktop", "true", "true"))
        menuIconaDesktop.avvia("Icona sul desktop", True)
    End Sub

    Private Sub ImpostaCreazioneIconaAttivata()
        SalvaConfig("crea_icona_desktop", "true")
        GestisciCollegamentoDesktop(True)
        Dim messaggio As New WGMessaggi()
        messaggio.Messaggio("L'icona sul desktop è stata creata o aggiornata.")
        messaggio.ShowDialog()
    End Sub

    Private Sub ImpostaCreazioneIconaDisattivata()

        SalvaConfig("crea_icona_desktop", "false")
        GestisciCollegamentoDesktop(False)
        Dim messaggio As New WGMessaggi()
        messaggio.Messaggio("L'icona sul desktop è stata rimossa.")
        messaggio.ShowDialog()
    End Sub

    Private menuFontBloccoNote As WGMenuLista
    Private menuDimBloccoNote As WGMenuLista

    Private Sub MenuFontPredefinitoBloccoNote()
        Try
            menuFontBloccoNote = New WGMenuLista()
            menuFontBloccoNote.multiselezioneConsentita = False
            menuFontBloccoNote.impostaTitolo("Scegli il carattere predefinito per il blocco note")
            menuFontBloccoNote.aggiungiElemento("Arial", "Arial")
            menuFontBloccoNote.aggiungiElemento("Calibri", "Calibri")
            menuFontBloccoNote.aggiungiElemento("Times New Roman", "Times New Roman")
            menuFontBloccoNote.aggiungiElemento("Verdana", "Verdana")
            menuFontBloccoNote.aggiungiElemento("Segoe UI", "Segoe UI")
            menuFontBloccoNote.aggiungiElemento("Courier New", "Courier New")
            AddHandler menuFontBloccoNote.escPremuto, AddressOf ChiudiMenuFontBloccoNote
            AddHandler menuFontBloccoNote.ElementoSelezionato, AddressOf FontBloccoNoteSelezionato
            menuFontBloccoNote.posiziona(LeggiConfigValore("font_predefinito_campo", "Arial"))
            menuFontBloccoNote.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuFontPredefinitoBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuFontBloccoNote()
        Try
            menuFontBloccoNote.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuFontBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub FontBloccoNoteSelezionato()
        Try
            Dim ids As List(Of String) = menuFontBloccoNote.ottieniElementi()
            If ids.Count > 0 Then
                Dim fontScelto As String = ids(0).ToString()
                SalvaConfigurazioneGenerica("font_predefinito_campo", fontScelto, "Carattere predefinito del blocco note impostato su " & fontScelto & ".")
                menuFontBloccoNote.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog("FontBloccoNoteSelezionato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuDimensionePredefinitaBloccoNote()
        Try
            menuDimBloccoNote = New WGMenuLista()
            menuDimBloccoNote.multiselezioneConsentita = False
            menuDimBloccoNote.impostaTitolo("Scegli la dimensione predefinita del testo nel blocco note")
            Dim dimensioni() As Integer = {10, 12, 14, 16, 18, 20, 22, 24}
            For Each d In dimensioni
                menuDimBloccoNote.aggiungiElemento(d.ToString() & " punti", d.ToString())
            Next
            AddHandler menuDimBloccoNote.escPremuto, AddressOf ChiudiMenuDimBloccoNote
            AddHandler menuDimBloccoNote.ElementoSelezionato, AddressOf DimBloccoNoteSelezionata
            menuDimBloccoNote.posiziona(LeggiConfigValore("dimensione_predefinita_campo", "18"))
            menuDimBloccoNote.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuDimensionePredefinitaBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuDimBloccoNote()
        Try
            menuDimBloccoNote.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuDimBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DimBloccoNoteSelezionata()
        Try
            Dim ids As List(Of String) = menuDimBloccoNote.ottieniElementi()
            If ids.Count > 0 Then
                Dim dimScelta As String = ids(0).ToString()
                SalvaConfigurazioneGenerica("dimensione_predefinita_campo", dimScelta, "Dimensione predefinita del testo nel blocco note impostata su " & dimScelta & " punti.")
                menuDimBloccoNote.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog("DimBloccoNoteSelezionata eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLetturaPunteggiaturaBloccoNote()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nessuna", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "0", "La punteggiatura non verrà letta."))
            m.aggiungi("Poca", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "1", "Verranno letti solo i simboli tecnici, come chiocciola, cancelletto e percentuale."))
            m.aggiungi("Molta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "2", "Verranno letti simboli tecnici, parentesi, virgolette, trattini, punto e virgola e due punti."))
            m.aggiungi("Tutta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "3", "Verrà letta tutta la punteggiatura, compresi punti, virgole e punti interrogativi."))
            m.posiziona(IndiceLivelloPunteggiatura())
            m.avvia("Lettura punteggiatura", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLetturaPunteggiaturaBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLetturaPunteggiaturaScritturaBloccoNote()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nessuna", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "0", "Durante la scrittura non verranno letti simboli o punteggiatura."))
            m.aggiungi("Poca", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "1", "Durante la scrittura verranno letti solo i simboli tecnici, come chiocciola, cancelletto e percentuale."))
            m.aggiungi("Molta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "2", "Durante la scrittura verranno letti simboli tecnici, parentesi, virgolette, trattini, punto e virgola e due punti."))
            m.aggiungi("Tutta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "3", "Durante la scrittura verrà letta tutta la punteggiatura, compresi punti, virgole e punti interrogativi."))
            m.posiziona(IndiceLivelloPunteggiaturaScrittura())
            m.avvia("Lettura punteggiatura durante la scrittura", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLetturaPunteggiaturaScritturaBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPausaLetturaContinuaBloccoNote()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Breve (1 secondo)", Sub() SalvaConfigurazioneGenerica("intervallo_pausa_lettura_continua", "1000", "Pausa tra le righe impostata su 1 secondo."))
            m.aggiungi("Normale (2 secondi)", Sub() SalvaConfigurazioneGenerica("intervallo_pausa_lettura_continua", "2000", "Pausa tra le righe impostata su 2 secondi."))
            m.aggiungi("Lunga (3 secondi)", Sub() SalvaConfigurazioneGenerica("intervallo_pausa_lettura_continua", "3000", "Pausa tra le righe impostata su 3 secondi."))
            m.posiziona(IndiceMultiplo("intervallo_pausa_lettura_continua", New List(Of String) From {"1000", "2000", "3000"}, "2000"))
            m.avvia("Pausa tra le righe in lettura continua", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPausaLetturaContinuaBloccoNote eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuWordDocumento()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nome autore", AddressOf ImpostaNomeAutoreWord)
            m.aggiungi("Formato pagina", AddressOf MenuFormatoPaginaWord)
            m.aggiungi("Salvataggio automatico", AddressOf MenuSalvataggioAutomaticoWord)
            m.avvia("Documento e salvataggio")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuWordDocumento eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuWordCarattere()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Tipo di carattere", AddressOf MenuFontPredefinitoWord)
            m.aggiungi("Dimensione carattere", AddressOf MenuDimensionePredefinitaWord)
            m.aggiungi("Dimensione titoli", AddressOf MostraMenuDimensioniTitoloWord)
            m.aggiungi("Interlinea", AddressOf MenuPassoInterlineaWord)
            m.avvia("Carattere e paragrafo")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuWordCarattere eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuWordZoom()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Livello di zoom iniziale", AddressOf MenuZoomInizialeWord)
            m.aggiungi("Incremento zoom", AddressOf MenuPassoZoomWord)
            m.avvia("Zoom")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuWordZoom eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLetturaPunteggiaturaWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nessuna", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "0", "La punteggiatura non verrà letta."))
            m.aggiungi("Poca", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "1", "Verranno letti solo i simboli tecnici, come chiocciola, cancelletto e percentuale."))
            m.aggiungi("Molta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "2", "Verranno letti simboli tecnici, parentesi, virgolette, trattini, punto e virgola e due punti."))
            m.aggiungi("Tutta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_lettura", "3", "Verrà letta tutta la punteggiatura, compresi punti, virgole e punti interrogativi."))
            m.posiziona(IndiceLivelloPunteggiatura())
            m.avvia("Lettura punteggiatura", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLetturaPunteggiaturaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLetturaPunteggiaturaScritturaWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Nessuna", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "0", "Durante la scrittura non verranno letti simboli o punteggiatura."))
            m.aggiungi("Poca", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "1", "Durante la scrittura verranno letti solo i simboli tecnici, come chiocciola, cancelletto e percentuale."))
            m.aggiungi("Molta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "2", "Durante la scrittura verranno letti simboli tecnici, parentesi, virgolette, trattini, punto e virgola e due punti."))
            m.aggiungi("Tutta", Sub() SalvaConfigurazioneGenerica("livello_punteggiatura_scrittura", "3", "Durante la scrittura verrà letta tutta la punteggiatura, compresi punti, virgole e punti interrogativi."))
            m.posiziona(IndiceLivelloPunteggiaturaScrittura())
            m.avvia("Lettura punteggiatura durante la scrittura", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLetturaPunteggiaturaScritturaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuAnnuncioFormattazioneWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva annuncio formattazione", Sub() SalvaConfigurazioneGenerica("annuncia_formattazione", "1", "Annuncio dei cambi di formattazione attivato."))
            m.aggiungi("Disattiva annuncio formattazione", Sub() SalvaConfigurazioneGenerica("annuncia_formattazione", "0", "Annuncio dei cambi di formattazione disattivato."))
            m.posiziona(IndiceBinario("annuncia_formattazione", "1", "0"))
            m.avvia("Annuncio formattazione", True)
        Catch ex As Exception
            logger.ScriviLog("MenuAnnuncioFormattazioneWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaNomeAutoreWord()
        Try
            Dim nomeAttuale As String = LeggiConfigValore("NomeAutore", Environment.UserName)
            campiAutoreWord = New Campi()
            campiAutoreWord.Aggiungi("Nome autore da usare nei documenti Word e ODT", "nomeautore", False, False, nomeAttuale)
            AddHandler campiAutoreWord.Fine, AddressOf ConfermaNomeAutoreWord
            campiAutoreWord.Avvia()
        Catch ex As Exception
            logger.ScriviLog("ImpostaNomeAutoreWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ConfermaNomeAutoreWord()
        Try
            Dim nome As String = campiAutoreWord.RecuperaValore("nomeautore")
            campiAutoreWord = Nothing
            If String.IsNullOrWhiteSpace(nome) Then Return
            SalvaConfigurazioneGenerica("NomeAutore", nome.Trim(), "Nome autore dei documenti impostato su " & nome.Trim() & ".")
        Catch ex As Exception
            logger.ScriviLog("ConfermaNomeAutoreWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuLeggiIntestazioneCambioPagina()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Leggi intestazione e piè di pagina al cambio pagina", Sub() SalvaConfigurazioneGenerica("leggi_intestazione_cambio_pagina", "1", "Al cambio pagina verranno lette l'intestazione e il piè di pagina."))
            m.aggiungi("Non leggere intestazione e piè di pagina al cambio pagina", Sub() SalvaConfigurazioneGenerica("leggi_intestazione_cambio_pagina", "0", "Al cambio pagina non verranno più lette l'intestazione e il piè di pagina."))
            m.posiziona(IndiceBinario("leggi_intestazione_cambio_pagina", "1", "0"))
            m.avvia("Lettura intestazione al cambio pagina", True)
        Catch ex As Exception
            logger.ScriviLog("MenuLeggiIntestazioneCambioPagina eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuFruscioPaginaWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva effetto sonoro al cambio pagina", Sub() SalvaConfigurazioneGenerica("fruscio_pagina_attivo", "1", "Effetto sonoro al cambio pagina attivato."))
            m.aggiungi("Disattiva effetto sonoro al cambio pagina", Sub() SalvaConfigurazioneGenerica("fruscio_pagina_attivo", "0", "Effetto sonoro al cambio pagina disattivato."))
            m.posiziona(IndiceBinario("fruscio_pagina_attivo", "1", "1"))
            m.avvia("Effetto sonoro al cambio pagina", True)
        Catch ex As Exception
            logger.ScriviLog("MenuFruscioPaginaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuFormatoPaginaWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("A4", Sub() SalvaConfigurazioneGenerica("formato_pagina_predefinito", "A4", "Formato pagina predefinito impostato su A4."))
            m.aggiungi("Letter", Sub() SalvaConfigurazioneGenerica("formato_pagina_predefinito", "Letter", "Formato pagina predefinito impostato su Letter."))
            m.posiziona(IndiceBinario("formato_pagina_predefinito", "A4", "A4"))
            m.avvia("Formato pagina predefinito", True)
        Catch ex As Exception
            logger.ScriviLog("MenuFormatoPaginaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuFontPredefinitoWord()
        Try
            menuFontWord = New WGMenuLista()
            menuFontWord.multiselezioneConsentita = False
            menuFontWord.impostaTitolo("Scegli il carattere predefinito per i documenti Word")
            menuFontWord.aggiungiElemento("Arial", "Arial")
            menuFontWord.aggiungiElemento("Calibri", "Calibri")
            menuFontWord.aggiungiElemento("Times New Roman", "Times New Roman")
            menuFontWord.aggiungiElemento("Verdana", "Verdana")
            menuFontWord.aggiungiElemento("Segoe UI", "Segoe UI")
            menuFontWord.aggiungiElemento("Courier New", "Courier New")
            AddHandler menuFontWord.escPremuto, AddressOf ChiudiMenuFontWord
            AddHandler menuFontWord.ElementoSelezionato, AddressOf FontWordSelezionato
            menuFontWord.posiziona(LeggiConfigValore("font_predefinito", "Arial"))
            menuFontWord.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuFontPredefinitoWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuFontWord()
        Try
            menuFontWord.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuFontWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub FontWordSelezionato()
        Try
            Dim ids As List(Of String) = menuFontWord.ottieniElementi()
            If ids.Count > 0 Then
                Dim fontScelto As String = ids(0).ToString()
                SalvaConfigurazioneGenerica("font_predefinito", fontScelto, "Carattere predefinito dei documenti impostato su " & fontScelto & ".")
                menuFontWord.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog("FontWordSelezionato eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuDimensionePredefinitaWord()
        Try
            menuDimWord = New WGMenuLista()
            menuDimWord.multiselezioneConsentita = False
            menuDimWord.impostaTitolo("Scegli la dimensione predefinita del testo")
            Dim dimensioni() As Integer = {8, 9, 10, 11, 12, 14, 16, 18}
            For Each d In dimensioni
                menuDimWord.aggiungiElemento(d.ToString() & " punti", d.ToString())
            Next
            AddHandler menuDimWord.escPremuto, AddressOf ChiudiMenuDimWord
            AddHandler menuDimWord.ElementoSelezionato, AddressOf DimWordSelezionata
            menuDimWord.posiziona(LeggiConfigValore("dimensione_predefinita", "11"))
            menuDimWord.avvia()
        Catch ex As Exception
            logger.ScriviLog("MenuDimensionePredefinitaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ChiudiMenuDimWord()
        Try
            menuDimWord.abbandona()
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuDimWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub DimWordSelezionata()
        Try
            Dim ids As List(Of String) = menuDimWord.ottieniElementi()
            If ids.Count > 0 Then
                Dim dimScelta As String = ids(0).ToString()
                SalvaConfigurazioneGenerica("dimensione_predefinita", dimScelta, "Dimensione predefinita del testo impostata su " & dimScelta & " punti.")
                menuDimWord.abbandona()
            End If
        Catch ex As Exception
            logger.ScriviLog("DimWordSelezionata eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuDimensioniTitoloWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Titolo 1", Sub() MenuDimensioneTitoloWord(1))
            m.aggiungi("Titolo 2", Sub() MenuDimensioneTitoloWord(2))
            m.aggiungi("Titolo 3", Sub() MenuDimensioneTitoloWord(3))
            m.aggiungi("Titolo 4", Sub() MenuDimensioneTitoloWord(4))
            m.aggiungi("Titolo 5", Sub() MenuDimensioneTitoloWord(5))
            m.aggiungi("Titolo 6", Sub() MenuDimensioneTitoloWord(6))
            m.avvia("Dimensione titoli")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuDimensioniTitoloWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuDimensioneTitoloWord(livello As Integer)
        Try
            If livello < 1 OrElse livello > 6 Then Return
            Dim chiave As String = "dimensione_titolo_" & livello
            Dim defaults() As String = {"18", "16", "14", "13", "12", "11"}
            Dim defaultVal As String = defaults(livello - 1)
            Dim dimensioni() As Integer = {11, 12, 13, 14, 16, 18, 20, 22, 24, 28, 32}
            Dim valoriTesto As New List(Of String)
            Dim m As New WGMenu()
            For Each d In dimensioni
                Dim dCatturato As Integer = d
                valoriTesto.Add(dCatturato.ToString())
                m.aggiungi(dCatturato.ToString() & " punti", Sub() SalvaConfigurazioneGenerica(chiave, dCatturato.ToString(), "Dimensione del titolo " & livello & " impostata su " & dCatturato & " punti."))
            Next
            m.posiziona(IndiceMultiplo(chiave, valoriTesto, defaultVal))
            m.avvia("Dimensione titolo " & livello, True)
        Catch ex As Exception
            logger.ScriviLog("MenuDimensioneTitoloWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPassoZoomWord()
        Try
            Dim valori As New List(Of String) From {"0.05", "0.1", "0.15", "0.25"}
            Dim m As New WGMenu()
            For Each v In valori
                Dim vCatturato As String = v
                m.aggiungi(vCatturato, Sub() SalvaConfigurazioneGenerica("passo_zoom", vCatturato, "Passo dello zoom impostato su " & vCatturato & "."))
            Next
            m.posiziona(IndiceMultiplo("passo_zoom", valori, "0.1"))
            m.avvia("Incremento zoom", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPassoZoomWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuZoomInizialeWord()
        Try
            Dim m As New WGMenu()
            m.aggiungi("75%", Sub() SalvaConfigurazioneGenerica("zoom_iniziale", "0.75", "Zoom iniziale dei documenti impostato al 75%."))
            m.aggiungi("100%", Sub() SalvaConfigurazioneGenerica("zoom_iniziale", "1.0", "Zoom iniziale dei documenti impostato al 100%."))
            m.aggiungi("125%", Sub() SalvaConfigurazioneGenerica("zoom_iniziale", "1.25", "Zoom iniziale dei documenti impostato al 125%."))
            m.aggiungi("150%", Sub() SalvaConfigurazioneGenerica("zoom_iniziale", "1.5", "Zoom iniziale dei documenti impostato al 150%."))
            m.posiziona(IndiceMultiplo("zoom_iniziale", New List(Of String) From {"0.75", "1.0", "1.25", "1.5"}, "1.0"))
            m.avvia("Livello di zoom iniziale", True)
        Catch ex As Exception
            logger.ScriviLog("MenuZoomInizialeWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPassoInterlineaWord()
        Try
            Dim valori As New List(Of String) From {"0.25", "0.5", "1.0"}
            Dim m As New WGMenu()
            For Each v In valori
                Dim vCatturato As String = v
                m.aggiungi(vCatturato, Sub() SalvaConfigurazioneGenerica("passo_interlinea", vCatturato, "Passo dell'interlinea impostato su " & vCatturato & "."))
            Next
            m.posiziona(IndiceMultiplo("passo_interlinea", valori, "0.5"))
            m.avvia("Interlinea", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPassoInterlineaWord eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuPlayerMultimediale()
        Try

            Dim menuPlayer As New WGMenu()
            menuPlayer.aggiungi("Volume", AddressOf MenuPlayerVolume)
            menuPlayer.aggiungi("Navigazione e scorrimento", AddressOf MenuPlayerNavigazione)
            menuPlayer.aggiungi("Messaggi vocali e sintesi", AddressOf MenuPlayerFeedback)
            menuPlayer.aggiungi("Avvio e velocità di riproduzione", AddressOf MenuPlayerFlusso)
            menuPlayer.aggiungi("Apertura dei file multimediali", AddressOf MenuPlayerAperturaFile)

            menuPlayer.avvia("Impostazioni del lettore multimediale")
        Catch ex As Exception
            logger.ScriviLog("MostraMenuPlayerMultimediale eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuImpostazioniYoutube()
        Try
            Dim menuyoutube As New MenuYoutube()
            menuyoutube.MostraImpostazioni()
        Catch ex As Exception
            logger.ScriviLog("MenuImpostazioniYoutube eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuImpostazioniQuotidiani()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Avviso nuovi articoli", AddressOf MenuAvvisoNuoviArticoliQuotidiani)
            m.aggiungi("Intervallo di controllo", AddressOf MenuIntervalloControlloQuotidiani)
            m.aggiungi("Massimo articoli segnalati", AddressOf MenuMassimoArticoliSegnalatiQuotidiani)
            m.aggiungi("Motore di estrazione del testo", AddressOf MenuMotoreEstrazioneQuotidiani)
            m.avvia("Quotidiani")
        Catch ex As Exception
            logger.ScriviLog("MenuImpostazioniQuotidiani eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuAvvisoNuoviArticoliQuotidiani()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Attiva avviso nuovi articoli", Sub() SalvaConfigurazioneGenerica(ID_CONFIG_AVVISO_NUOVI_ARTICOLI_QUOTIDIANI, "true", "L'avviso di nuovi articoli nei quotidiani preferiti è attivo."))
            m.aggiungi("Disattiva avviso nuovi articoli", Sub() SalvaConfigurazioneGenerica(ID_CONFIG_AVVISO_NUOVI_ARTICOLI_QUOTIDIANI, "false", "L'avviso di nuovi articoli nei quotidiani preferiti è disattivato."))
            m.posiziona(IndiceBinario(ID_CONFIG_AVVISO_NUOVI_ARTICOLI_QUOTIDIANI, "true", "true"))
            m.avvia("Avviso nuovi articoli", True)
        Catch ex As Exception
            logger.ScriviLog("MenuAvvisoNuoviArticoliQuotidiani eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuIntervalloControlloQuotidiani()
        Try
            Dim m As New WGMenu()
            Dim valori As New List(Of String) From {"15", "30", "60", "120", "240"}
            Dim etichette As New List(Of String) From {"15 minuti", "30 minuti", "1 ora", "2 ore", "4 ore"}
            For indice As Integer = 0 To valori.Count - 1
                Dim valoreLocale As String = valori(indice)
                Dim etichettaLocale As String = etichette(indice)
                m.aggiungi("Ogni " & etichettaLocale, Sub() SalvaConfigurazioneGenerica(ID_CONFIG_INTERVALLO_CONTROLLO_QUOTIDIANI, valoreLocale, "Controllo nuovi articoli impostato ogni " & etichettaLocale & "."))
            Next
            m.posiziona(IndiceMultiplo(ID_CONFIG_INTERVALLO_CONTROLLO_QUOTIDIANI, valori, "30"))
            m.avvia("Intervallo di controllo", True)
        Catch ex As Exception
            logger.ScriviLog("MenuIntervalloControlloQuotidiani eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuMassimoArticoliSegnalatiQuotidiani()
        Try
            Dim m As New WGMenu()
            Dim valori As New List(Of String) From {"3", "5", "10", "20"}
            For Each v As String In valori
                Dim valoreLocale As String = v
                m.aggiungi("Massimo " & valoreLocale & " articoli", Sub() SalvaConfigurazioneGenerica(ID_CONFIG_MASSIMO_ARTICOLI_SEGNALATI_QUOTIDIANI, valoreLocale, "Verranno segnalati al massimo " & valoreLocale & " articoli nuovi per ogni controllo."))
            Next
            m.posiziona(IndiceMultiplo(ID_CONFIG_MASSIMO_ARTICOLI_SEGNALATI_QUOTIDIANI, valori, "5"))
            m.avvia("Massimo articoli segnalati", True)
        Catch ex As Exception
            logger.ScriviLog("MenuMassimoArticoliSegnalatiQuotidiani eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuMotoreEstrazioneQuotidiani()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Motore interno, qualità più bassa", AddressOf ScegliMotoreInternoQuotidiani)
            m.aggiungi("Motore Trafilatura, qualità alta", AddressOf ScegliMotoreTrafilaturaQuotidiani)
            m.posiziona(IndiceBinario(InstallatoreTrafilatura.ChiaveConfigMotore, InstallatoreTrafilatura.MotoreInterno, InstallatoreTrafilatura.MotoreInterno))
            m.avvia("Motore di estrazione del testo", True)
        Catch ex As Exception
            logger.ScriviLog("MenuMotoreEstrazioneQuotidiani eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliMotoreInternoQuotidiani()
        Try
            SalvaConfigurazioneGenerica(InstallatoreTrafilatura.ChiaveConfigMotore, InstallatoreTrafilatura.MotoreInterno, "Per i quotidiani è attivo il motore interno di estrazione del testo.")
        Catch ex As Exception
            logger.ScriviLog("ScegliMotoreInternoQuotidiani eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub ScegliMotoreTrafilaturaQuotidiani()
        Try
            If InstallatoreTrafilatura.Installato() Then
                SalvaConfigurazioneGenerica(InstallatoreTrafilatura.ChiaveConfigMotore, InstallatoreTrafilatura.MotoreTrafilatura, "Per i quotidiani è attivo il motore Trafilatura, qualità alta.")
                Return
            End If
            AvviaInstallazioneTrafilatura()
        Catch ex As Exception
            logger.ScriviLog("ScegliMotoreTrafilaturaQuotidiani eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaInstallazioneTrafilatura()
        Try
            If installazioneTrafilaturaInCorso Then
                Dim avviso As New WGMessaggi()
                avviso.Messaggio("L'installazione del motore Trafilatura è già in corso.")
                avviso.ShowDialog()
                Return
            End If

            installazioneTrafilaturaInCorso = True
            esitoTrafilatura = String.Empty
            If annullaTrafilatura IsNot Nothing Then
                annullaTrafilatura.Dispose()
                annullaTrafilatura = Nothing
            End If
            annullaTrafilatura = New CancellationTokenSource()
            contestoTrafilatura = SynchronizationContext.Current

            processoTrafilatura = New WGProcessoInCorso()
            AddHandler processoTrafilatura.cancella, AddressOf AnnullaInstallazioneTrafilatura

            Task.Run(New Action(AddressOf LavoroInstallazioneTrafilatura))
            processoTrafilatura.Avvia("Installazione del motore Trafilatura in corso, attendere", True)
        Catch ex As Exception
            installazioneTrafilaturaInCorso = False
            logger.ScriviLog("AvviaInstallazioneTrafilatura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub LavoroInstallazioneTrafilatura()
        Dim esito As String = "errore sconosciuto"
        Try
            Dim installatore As New InstallatoreTrafilatura()
            esito = installatore.Installa(annullaTrafilatura.Token, AddressOf AvanzamentoInstallazioneTrafilatura)
        Catch ex As Exception
            logger.ScriviLog("LavoroInstallazioneTrafilatura eccezione: " & ex.Message)
        Finally
            Try
                esitoTrafilatura = esito
                If contestoTrafilatura IsNot Nothing Then contestoTrafilatura.Post(AddressOf CompletaInstallazioneTrafilatura, Nothing)
            Catch exFine As Exception
                logger.ScriviLog("LavoroInstallazioneTrafilatura chiusura: " & exFine.Message)
            End Try
        End Try
    End Sub

    Private Sub AvanzamentoInstallazioneTrafilatura(testo As String)
        Try
            If contestoTrafilatura IsNot Nothing Then contestoTrafilatura.Post(AddressOf AggiornaStatoInstallazioneTrafilatura, testo)
        Catch ex As Exception
            logger.ScriviLog("AvanzamentoInstallazioneTrafilatura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaStatoInstallazioneTrafilatura(stato As Object)
        Try
            If processoTrafilatura Is Nothing Then Return
            Dim testo As String = TryCast(stato, String)
            If String.IsNullOrEmpty(testo) Then Return
            processoTrafilatura.AggiornaStato(testo)
        Catch ex As Exception
            logger.ScriviLog("AggiornaStatoInstallazioneTrafilatura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub AnnullaInstallazioneTrafilatura(sender As Object, e As EventArgs)
        Try
            If annullaTrafilatura IsNot Nothing Then annullaTrafilatura.Cancel()
            processoTrafilatura = Nothing
        Catch ex As Exception
            logger.ScriviLog("AnnullaInstallazioneTrafilatura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub CompletaInstallazioneTrafilatura(stato As Object)
        Try
            installazioneTrafilaturaInCorso = False
            If processoTrafilatura IsNot Nothing Then
                Try
                    processoTrafilatura.Fine()
                Catch exFine As Exception
                    logger.ScriviLog("CompletaInstallazioneTrafilatura chiusura processo: " & exFine.Message)
                End Try
                processoTrafilatura = Nothing
            End If

            If annullaTrafilatura IsNot Nothing AndAlso annullaTrafilatura.IsCancellationRequested Then Return

            If String.IsNullOrEmpty(esitoTrafilatura) Then
                SalvaConfigurazioneGenerica(InstallatoreTrafilatura.ChiaveConfigMotore, InstallatoreTrafilatura.MotoreTrafilatura, "Motore Trafilatura installato e attivo. Da adesso i testi dei quotidiani vengono estratti con qualità alta.")
                Return
            End If

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Installazione del motore Trafilatura non riuscita: " & esitoTrafilatura & ". Resta attivo il motore interno.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("CompletaInstallazioneTrafilatura eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPlayerAperturaFile()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Chiedi ogni volta", Sub() SalvaConfigurazioneGenerica("modalitaAperturaMedia", "", "Verrà chiesta la modalità di apertura di ogni file multimediale."))
            m.aggiungi("Ascolta sempre in sottofondo", Sub() SalvaConfigurazioneGenerica("modalitaAperturaMedia", "Sottofondo", "I file multimediali verranno sempre ascoltati in sottofondo."))
            m.aggiungi("Apri sempre il lettore multimediale", Sub() SalvaConfigurazioneGenerica("modalitaAperturaMedia", "Player", "I file multimediali apriranno sempre il lettore multimediale."))
            m.posiziona(IndiceMultiplo("modalitaAperturaMedia", New List(Of String) From {"", "Sottofondo", "Player"}, ""))
            m.avvia("Apertura dei file multimediali", True)
        Catch ex As Exception
            logger.ScriviLog("MenuPlayerAperturaFile eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub MenuPlayerVolume()
        Dim m As New WGMenu()
        m.aggiungi("Volume di partenza", AddressOf MenuPlayerVolumeDiPartenza)
        m.aggiungi("Memoria dell'ultimo volume", AddressOf MenuPlayerMemoriaVolume)
        m.aggiungi("Variazione volume ad ogni pressione", AddressOf MenuPlayerVariazioneVolume)
        m.avvia("Volume")
    End Sub

    Private Sub MenuPlayerVolumeDiPartenza()
        Dim m As New WGMenu()
        Dim valori As New List(Of String) From {"100", "75", "50", "25"}
        For Each v As String In valori
            Dim valoreLocale As String = v
            m.aggiungi("Volume di partenza: " & valoreLocale & "%", Sub() SalvaConfigurazioneGenerica("volumedipartenza", valoreLocale, "Volume di partenza impostato al " & valoreLocale & "%."))
        Next
        m.posiziona(IndiceMultiplo("volumedipartenza", valori, "100"))
        m.avvia("Volume di partenza", True)
    End Sub

    Private Sub MenuPlayerMemoriaVolume()
        Dim m As New WGMenu()
        m.aggiungi("Memorizza l'ultimo volume alla chiusura", Sub() SalvaConfigurazioneGenerica("memorizzavolume", "true", "Il lettore ricorderà l'ultimo volume utilizzato."))
        m.aggiungi("Non memorizzare il volume", Sub() SalvaConfigurazioneGenerica("memorizzavolume", "false", "Il lettore userà sempre il volume di partenza."))
        m.posiziona(IndiceBinario("memorizzavolume", "true", "true"))
        m.avvia("Memoria dell'ultimo volume", True)
    End Sub

    Private Sub MenuPlayerVariazioneVolume()
        Dim m As New WGMenu()
        Dim valori As New List(Of String) From {"1", "2", "5", "10", "15", "20"}
        For Each v As String In valori
            Dim valoreLocale As String = v
            m.aggiungi("Variazione volume: " & valoreLocale & "%", Sub() SalvaConfigurazioneGenerica("passovolume", valoreLocale, "Il volume varierà del " & valoreLocale & "% ad ogni pressione."))
        Next
        m.posiziona(IndiceMultiplo("passovolume", valori, "5"))
        m.avvia("Variazione volume ad ogni pressione", True)
    End Sub

    Private Sub MenuPlayerNavigazione()
        Dim m As New WGMenu()
        m.aggiungi("Salto breve", AddressOf MenuPlayerSaltoBreve)
        m.aggiungi("Salto lungo", AddressOf MenuPlayerSaltoLungo)
        m.aggiungi("Ripresa dall'ultima posizione", AddressOf MenuPlayerRipresaPosizione)
        m.avvia("Navigazione e scorrimento")
    End Sub

    Private Sub MenuPlayerSaltoBreve()
        Dim m As New WGMenu()
        Dim valori As New List(Of String) From {"5", "10", "15", "20", "30", "45", "60"}
        For Each v As String In valori
            Dim valoreLocale As String = v
            m.aggiungi("Salto breve: " & valoreLocale & " secondi", Sub() SalvaConfigurazioneGenerica("passoscorrimento", valoreLocale, "Salto breve impostato a " & valoreLocale & " secondi."))
        Next
        m.posiziona(IndiceMultiplo("passoscorrimento", valori, "10"))
        m.avvia("Salto breve", True)
    End Sub

    Private Sub MenuPlayerSaltoLungo()
        Dim m As New WGMenu()
        Dim valori As New List(Of String) From {"30", "60", "120", "180", "300", "600"}
        Dim etichette As New List(Of String) From {"30 secondi", "1 minuto", "2 minuti", "3 minuti", "5 minuti", "10 minuti"}
        For i As Integer = 0 To valori.Count - 1
            Dim valoreLocale As String = valori(i)
            Dim etichettaLocale As String = etichette(i)
            m.aggiungi("Salto lungo: " & etichettaLocale, Sub() SalvaConfigurazioneGenerica("passoscorrimentolungo", valoreLocale, "Salto lungo impostato a " & etichettaLocale & "."))
        Next
        m.posiziona(IndiceMultiplo("passoscorrimentolungo", valori, "60"))
        m.avvia("Salto lungo", True)
    End Sub

    Private Sub MenuPlayerRipresaPosizione()
        Dim m As New WGMenu()
        m.aggiungi("Riprendi dall'ultima posizione", Sub() SalvaConfigurazioneGenerica("ricordaposizione", "true", "Ripresa dall'ultima posizione attivata."))
        m.aggiungi("Riparti sempre dall'inizio", Sub() SalvaConfigurazioneGenerica("ricordaposizione", "false", "Ripresa dall'ultima posizione disattivata."))
        m.posiziona(IndiceBinario("ricordaposizione", "true", "true"))
        m.avvia("Ripresa dall'ultima posizione", True)
    End Sub

    Private Sub MenuPlayerFeedback()
        Dim m As New WGMenu()

        m.aggiungi("Attiva lettura messaggi di stato", Sub() SalvaConfigurazioneGenerica("leggistato", "true", "Lettura dei messaggi di stato attivata."))
        m.aggiungi("Disattiva lettura messaggi di stato", Sub() SalvaConfigurazioneGenerica("leggistato", "false", "Lettura dei messaggi di stato disattivata."))
        m.aggiungi("Attiva lettura del titolo", Sub() SalvaConfigurazioneGenerica("leggititolo", "true", "La lettura del titolo all'avvio è attivata."))
        m.aggiungi("Disattiva lettura del titolo", Sub() SalvaConfigurazioneGenerica("leggititolo", "false", "La lettura del titolo all'avvio è disattivata."))

        m.aggiungi("Attiva dettagli aggiuntivi del brano", Sub() SalvaConfigurazioneGenerica("leggidettaglibbrano", "true", "Dettagli aggiuntivi del brano attivati."))
        m.aggiungi("Disattiva dettagli aggiuntivi del brano", Sub() SalvaConfigurazioneGenerica("leggidettaglibbrano", "false", "Dettagli aggiuntivi del brano disattivati."))
        m.posiziona(IndiceBinario("leggistato", "true", "true"))
        m.avvia("Messaggi vocali e sintesi", True)
    End Sub

    Private Sub MenuPlayerFlusso()
        Dim m As New WGMenu()
        m.aggiungi("Avvio automatico", AddressOf MenuPlayerAvvioAutomatico)
        m.aggiungi("Variazione velocità", AddressOf MenuPlayerVariazioneVelocita)
        m.avvia("Avvio e velocità di riproduzione")
    End Sub

    Private Sub MenuPlayerAvvioAutomatico()
        Dim m As New WGMenu()
        m.aggiungi("Attiva avvio automatico", Sub() SalvaConfigurazioneGenerica("avvioautomatico", "true", "I brani partiranno automaticamente."))
        m.aggiungi("Disattiva avvio automatico", Sub() SalvaConfigurazioneGenerica("avvioautomatico", "false", "I brani non partiranno finché non premi Spazio."))
        m.posiziona(IndiceBinario("avvioautomatico", "true", "true"))
        m.avvia("Avvio automatico", True)
    End Sub

    Private Sub MenuPlayerVariazioneVelocita()
        Dim m As New WGMenu()
        Dim valori As New List(Of String) From {"0.05", "0.1", "0.25", "0.5", "1"}
        For Each v As String In valori
            Dim valoreLocale As String = v
            m.aggiungi("Variazione velocità: " & valoreLocale & "x", Sub() SalvaConfigurazioneGenerica("passovelocita", valoreLocale, "Variazione velocità impostata a " & valoreLocale & "x."))
        Next
        m.posiziona(IndiceMultiplo("passovelocita", valori, "0.25"))
        m.avvia("Variazione velocità", True)
    End Sub

    Private Sub MostraMenuImpostazioniWindows()
        Dim menuWin As New WGMenu()
        menuWin.aggiungi("Luminosità schermo", AddressOf ImpostaLuminosita)
        menuWin.aggiungi("Volume di sistema", AddressOf ImpostaVolumeSistema)
        menuWin.aggiungi("Risparmio energetico", AddressOf ImpostaRisparmioEnergetico)
        menuWin.aggiungi("Standby durante l'uso di WinGabriel", AddressOf MostraMenuStandbyDuranteUso)
        menuWin.aggiungi("Svuota cestino", AddressOf SvuotaCestinoSistema)
        menuWin.avvia("Impostazioni di Windows")
    End Sub

    Private Sub MostraMenuStandbyDuranteUso()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Disattivato (il computer non va in standby mentre WinGabriel è aperto)", AddressOf AttivaBloccoStandby)
            m.aggiungi("Consentito (Windows segue le sue normali impostazioni di risparmio energetico)", AddressOf DisattivaBloccoStandby)
            m.posiziona(IndiceBinario(ID_CONFIG_DISATTIVA_STANDBY_USO, "true", "true"))
            m.avvia("Standby del computer durante l'uso di WinGabriel", True)
        Catch ex As Exception
            logger.ScriviLog("MostraMenuStandbyDuranteUso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub AttivaBloccoStandby()
        Try
            SalvaConfig(ID_CONFIG_DISATTIVA_STANDBY_USO, "true")
            WGNoStandby.Avvia()

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Il computer non andrà più in standby automaticamente finché WinGabriel è aperto.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("AttivaBloccoStandby eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub DisattivaBloccoStandby()
        Try
            SalvaConfig(ID_CONFIG_DISATTIVA_STANDBY_USO, "false")
            WGNoStandby.Ferma()

            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Il computer potrà andare in standby normalmente, secondo le impostazioni di Windows.")
            messaggio.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("DisattivaBloccoStandby eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ImpostaLuminosita()
        menuLum = New WGMenuLista()
        menuLum.impostaTitolo("Scegli la luminosità (da 0 a 100)")
        menuLum.multiselezioneConsentita = False

        For i As Integer = 0 To 100 Step 10
            menuLum.aggiungiElemento(i.ToString() & "%", i.ToString())
        Next

        _luminositaOriginale = LeggiLuminosita()
        menuLum.aggiungiOpzione("Regolazione affinata", AddressOf OpzioneAffinataLuminosita)
        If _luminositaOriginale >= 0 AndAlso _luminositaOriginale <= 100 Then
            Try
                Dim percentualeArrotondata As Integer = CInt(Math.Round(_luminositaOriginale / 10.0)) * 10
                percentualeArrotondata = Math.Max(0, Math.Min(100, percentualeArrotondata))
                menuLum.posiziona(percentualeArrotondata.ToString())
            Catch ex As Exception
                logger.ScriviLog("ImpostaLuminosita posiziona eccezione: " & ex.Message)
            End Try
        End If

        AddHandler menuLum.escPremuto, AddressOf ChiudiMenuLuminosita
        AddHandler menuLum.ElementoSelezionato, AddressOf EseguiImpostaLuminosita
        AddHandler menuLum.VoceCambiata, AddressOf AnteprimaLuminosita
        menuLum.avvia()
    End Sub

    Private Sub OpzioneAffinataLuminosita(sender As Object, e As EventArgs)
        Try
            RicaricaAffinata(menuLum, _luminositaOriginale)
        Catch ex As Exception
            logger.ScriviLog("OpzioneAffinataLuminosita eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuLuminosita()
        Try
            If _luminositaOriginale >= 0 Then
                RichiediApplicazioneLuminosita(_luminositaOriginale)
                _luminositaOriginale = -1
            End If
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuLuminosita eccezione: " & ex.Message)
        End Try
        menuLum.abbandona()
    End Sub

    Private Sub AnteprimaLuminosita(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Return
            Dim valore As Integer = -1
            If Not Integer.TryParse(e.IdVoce, valore) Then Return
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            RichiediApplicazioneLuminosita(valore)
        Catch ex As Exception
            logger.ScriviLog("AnteprimaLuminosita eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub RichiediApplicazioneLuminosita(valore As Integer)
        Try
            Dim avviaTask As Boolean = False
            SyncLock _luminositaLock
                _luminositaValoreRichiesto = valore
                If Not _luminositaTaskAttivo Then
                    _luminositaTaskAttivo = True
                    avviaTask = True
                End If
            End SyncLock

            If avviaTask Then
                Task.Run(AddressOf EseguiApplicazioneLuminositaDifferita)
            End If
        Catch ex As Exception
            logger.ScriviLog("RichiediApplicazioneLuminosita eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiApplicazioneLuminositaDifferita()
        Try
            Do
                Dim valoreIniziale As Integer = -1
                SyncLock _luminositaLock
                    valoreIniziale = _luminositaValoreRichiesto
                End SyncLock

                Thread.Sleep(300)

                Dim valoreFinale As Integer = -1
                SyncLock _luminositaLock
                    valoreFinale = _luminositaValoreRichiesto
                End SyncLock

                If valoreFinale <> valoreIniziale Then
                    Continue Do
                End If

                ImpostaLuminositaDiretta(valoreFinale)

                SyncLock _luminositaLock
                    If _luminositaValoreRichiesto = valoreFinale Then
                        _luminositaTaskAttivo = False
                        Exit Do
                    End If
                End SyncLock
            Loop
        Catch ex As Exception
            logger.ScriviLog("EseguiApplicazioneLuminositaDifferita eccezione: " & ex.Message)
            Try
                SyncLock _luminositaLock
                    _luminositaTaskAttivo = False
                End SyncLock
            Catch exSblocco As Exception
                logger.ScriviLog("EseguiApplicazioneLuminositaDifferita sblocco eccezione: " & exSblocco.Message)
            End Try
        End Try
    End Sub

    Private Function LeggiLuminosita() As Integer
        Try
            Dim risultato As Integer = -1
            Using ricerca As New System.Management.ManagementObjectSearcher("root\WMI", "SELECT * FROM WmiMonitorBrightness")
                For Each oggetto As System.Management.ManagementObject In ricerca.Get()
                    Try
                        If oggetto("CurrentBrightness") IsNot Nothing Then
                            risultato = Convert.ToInt32(oggetto("CurrentBrightness"))
                            Exit For
                        End If
                    Catch exLettura As Exception
                        logger.ScriviLog("LeggiLuminosita lettura eccezione: " & exLettura.Message)
                    Finally
                        Try
                            oggetto.Dispose()
                        Catch exDisp As Exception
                            logger.ScriviLog("LeggiLuminosita dispose eccezione: " & exDisp.Message)
                        End Try
                    End Try
                Next
            End Using
            Return risultato
        Catch ex As Exception
            logger.ScriviLog("LeggiLuminosita eccezione: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Sub ImpostaLuminositaDiretta(valore As Integer)
        Try
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Using ricerca As New System.Management.ManagementObjectSearcher("root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods")
                For Each oggetto As System.Management.ManagementObject In ricerca.Get()
                    Try
                        oggetto.InvokeMethod("WmiSetBrightness", New Object() {CUInt(1), CByte(valore)})
                    Catch exInvoke As Exception
                        logger.ScriviLog("ImpostaLuminositaDiretta invoke eccezione: " & exInvoke.Message)
                    Finally
                        Try
                            oggetto.Dispose()
                        Catch exDisp As Exception
                            logger.ScriviLog("ImpostaLuminositaDiretta dispose eccezione: " & exDisp.Message)
                        End Try
                    End Try
                Next
            End Using
        Catch ex As Exception
            logger.ScriviLog("ImpostaLuminositaDiretta eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiImpostaLuminosita()
        Try
            Dim selezione As List(Of String) = menuLum.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then Return

            Dim valStr As String = selezione(0)
            Dim val As Integer
            If Not Integer.TryParse(valStr, val) Then Return
            If val < 0 Then val = 0
            If val > 100 Then val = 100

            RichiediApplicazioneLuminosita(val)
            _luminositaOriginale = -1

            Dim msg As New WGMessaggi()
            msg.Messaggio($"Luminosità impostata al {val}%.")
            msg.ShowDialog()
            menuLum.abbandona()
        Catch ex As Exception
            logger.ScriviLog("Errore luminosità: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaVolumeSistema()
        menuVolWin = New WGMenuLista()
        menuVolWin.impostaTitolo("Scegli il volume di sistema (da 0 a 100)")
        menuVolWin.multiselezioneConsentita = False

        For i As Integer = 0 To 100 Step 10
            menuVolWin.aggiungiElemento(i.ToString() & "%", i.ToString())
        Next

        _volumeSistemaOriginale = LeggiVolumeSistema()
        menuVolWin.aggiungiOpzione("Regolazione affinata", AddressOf OpzioneAffinataVolumeSistema)
        If _volumeSistemaOriginale >= 0 AndAlso _volumeSistemaOriginale <= 100 Then
            Try
                Dim percentualeArrotondata As Integer = CInt(Math.Round(_volumeSistemaOriginale / 10.0)) * 10
                percentualeArrotondata = Math.Max(0, Math.Min(100, percentualeArrotondata))
                menuVolWin.posiziona(percentualeArrotondata.ToString())
            Catch ex As Exception
                logger.ScriviLog("ImpostaVolumeSistema posiziona eccezione: " & ex.Message)
            End Try
        End If

        AddHandler menuVolWin.escPremuto, AddressOf ChiudiMenuVolumeSistema
        AddHandler menuVolWin.ElementoSelezionato, AddressOf EseguiImpostaVolumeSistema
        AddHandler menuVolWin.VoceCambiata, AddressOf AnteprimaVolumeSistema
        menuVolWin.avvia()
    End Sub

    Private Sub OpzioneAffinataVolumeSistema(sender As Object, e As EventArgs)
        Try
            RicaricaAffinata(menuVolWin, _volumeSistemaOriginale)
        Catch ex As Exception
            logger.ScriviLog("OpzioneAffinataVolumeSistema eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ChiudiMenuVolumeSistema()
        Try
            If _volumeSistemaOriginale >= 0 Then
                ImpostaVolumeSistemaDiretto(_volumeSistemaOriginale)
                _volumeSistemaOriginale = -1
            End If
        Catch ex As Exception
            logger.ScriviLog("ChiudiMenuVolumeSistema eccezione: " & ex.Message)
        End Try
        menuVolWin.abbandona()
    End Sub

    Private Sub AnteprimaVolumeSistema(sender As Object, e As VoceCambiataEventArgs)
        Try
            If e Is Nothing Then Return
            Dim valore As Integer = -1
            If Not Integer.TryParse(e.IdVoce, valore) Then Return
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            ImpostaVolumeSistemaDiretto(valore)
        Catch ex As Exception
            logger.ScriviLog("AnteprimaVolumeSistema eccezione: " & ex.Message)
        End Try
    End Sub

    Private Function LeggiVolumeSistema() As Integer
        Dim risultato As Integer = -1
        Try
            risultato = WGVolumeWindows.OttieniVolumeSistema()
        Catch ex As Exception
            logger.ScriviLog("LeggiVolumeSistema eccezione: " & ex.Message)
            risultato = -1
        End Try
        If risultato < 0 Then risultato = -1
        If risultato > 100 Then risultato = 100
        Return risultato
    End Function

    Private Sub ImpostaVolumeSistemaDiretto(valore As Integer)
        Try
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100

            If Not WGVolumeWindows.ImpostaVolumeSistema(valore) Then
            End If
        Catch ex As Exception
            logger.ScriviLog("ImpostaVolumeSistemaDiretto eccezione: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiImpostaVolumeSistema()
        Try
            Dim selezione As List(Of String) = menuVolWin.ottieniElementi()
            If selezione Is Nothing OrElse selezione.Count = 0 Then Return

            Dim valStr As String = selezione(0)
            Dim val As Integer
            If Not Integer.TryParse(valStr, val) Then Return
            If val < 0 Then val = 0
            If val > 100 Then val = 100

            ImpostaVolumeSistemaDiretto(val)
            _volumeSistemaOriginale = -1

            Dim msg As New WGMessaggi()
            msg.Messaggio($"Volume di sistema impostato al {val}%.")
            msg.ShowDialog()
            menuVolWin.abbandona()
        Catch ex As Exception
            logger.ScriviLog("Errore volume: " & ex.Message)
        End Try
    End Sub

    Private Sub SvuotaCestinoSistema()
        Try
            EseguiCmd("powershell", "-NoProfile -Command ""Clear-RecycleBin -Force""")
            Dim msg As New WGMessaggi()
            msg.Messaggio("Il cestino è stato svuotato.")
            msg.ShowDialog()
        Catch ex As Exception
            logger.ScriviLog("Errore svuotamento cestino: " & ex.Message)
        End Try
    End Sub

    Private Sub ImpostaRisparmioEnergetico()
        Dim processoincorso As WGProcessoInCorso = Nothing

        Try
            processoincorso = New WGProcessoInCorso()

            Dim syncContext As SynchronizationContext = SynchronizationContext.Current

            If syncContext Is Nothing Then
                processoincorso.Fine()
                Return
            End If

            Dim processoLocale As WGProcessoInCorso = processoincorso

            Task.Run(Sub()
                         Dim risposta As String = ""

                         Try
                             risposta = EseguiCmd("powercfg", "/list")
                         Catch exComando As Exception
                             logger.ScriviLog("Errore powercfg: " & exComando.Message)
                         End Try

                         syncContext.Post(Sub(state)
                                              Try
                                                  processoLocale.Fine()
                                                  MostraElencoPianiEnergetici(risposta)
                                              Catch exInterfaccia As Exception
                                                  logger.ScriviLog("ImpostaRisparmioEnergetico (interfaccia): " & exInterfaccia.Message & Environment.NewLine & exInterfaccia.StackTrace)
                                              End Try
                                          End Sub, Nothing)
                     End Sub)

            processoincorso.Avvia("Ricerca piani energetici, attendere")
        Catch ex As Exception
            logger.ScriviLog("ImpostaRisparmioEnergetico eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                If processoincorso IsNot Nothing Then processoincorso.Fine()
            Catch exFine As Exception
                logger.ScriviLog("ImpostaRisparmioEnergetico (chiusura processo): " & exFine.Message)
            End Try
        End Try
    End Sub

    Private Sub MostraElencoPianiEnergetici(risposta As String)
        Try
            Dim numeroPiani As Integer = 0

            menuEnergiaWin = New WGMenuLista()
            menuEnergiaWin.impostaTitolo("Piani di Risparmio Energetico")
            menuEnergiaWin.multiselezioneConsentita = False

            If Not String.IsNullOrEmpty(risposta) Then
                Dim righe() As String = risposta.Split(New Char() {Chr(13), Chr(10)}, StringSplitOptions.RemoveEmptyEntries)

                For Each riga In righe
                    If riga.Contains("GUID") Then
                        Dim startIndex As Integer = riga.IndexOf(":") + 1
                        Dim openParen As Integer = riga.IndexOf("(", startIndex)
                        Dim closeParen As Integer = If(openParen >= 0, riga.IndexOf(")", openParen), -1)

                        If startIndex > 0 AndAlso openParen > startIndex AndAlso closeParen > openParen Then
                            Dim guidStr As String = riga.Substring(startIndex, openParen - startIndex).Trim()
                            Dim nameStr As String = riga.Substring(openParen + 1, closeParen - openParen - 1).Trim()

                            If riga.EndsWith("*") Then
                                nameStr = "Attivo: " & nameStr
                            End If

                            menuEnergiaWin.aggiungiElemento(nameStr, guidStr)
                            numeroPiani += 1
                        End If
                    End If
                Next
            End If

            If numeroPiani = 0 Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("Nessun piano di risparmio energetico trovato.")
                msg.ShowDialog()
                Return
            End If

            AddHandler menuEnergiaWin.escPremuto, Sub() menuEnergiaWin.abbandona()
            AddHandler menuEnergiaWin.ElementoSelezionato, AddressOf ApplicaRisparmioEnergetico
            menuEnergiaWin.avvia()
        Catch ex As Exception
            logger.ScriviLog("MostraElencoPianiEnergetici eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ApplicaRisparmioEnergetico()
        Dim selezione As List(Of String) = menuEnergiaWin.ottieniElementi()
        If selezione.Count > 0 Then
            Dim guidScelto As String = selezione(0)
            Try
                EseguiCmd("powercfg", $"/setactive {guidScelto}")
                Dim msg As New WGMessaggi()
                msg.Messaggio("Piano di risparmio energetico applicato.")
                msg.ShowDialog()
                menuEnergiaWin.abbandona()
            Catch ex As Exception
                logger.ScriviLog("Errore applicazione powercfg: " & ex.Message)
            End Try
        End If
    End Sub

End Class
