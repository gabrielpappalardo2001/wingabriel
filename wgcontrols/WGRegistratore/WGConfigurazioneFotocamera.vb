' ============================================================================================
' WGConfigurazioneFotocamera - le impostazioni della fotocamera
' ============================================================================================
'
' Raccoglie tutto quello che si salva: modalità, videocamera, orientamento, risoluzione, fotogrammi al secondo, qualità, cartella, i
' controlli della videocamera (luminosità, contrasto, esposizione, messa a fuoco, zoom, bilanciamento del bianco...) e, in comune con il
' registratore vocale, microfono, effetti sulla voce, volumi, sottofondo e sincronia. Si apre da Impostazioni, Applicazioni, Fotocamera e
' dal menu con Alt della fotocamera: i valori sono sempre quelli salvati.
' ============================================================================================
Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks

Public NotInheritable Class ConfigurazioneFotocamera

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Public Shared Function ModalitaFoto() As Boolean
        Return ImpostazioniMusica.Leggi("cam_modalita", "video") = "foto"
    End Function

    Public Shared Function Verticale() As Boolean
        Return ImpostazioniMusica.LeggiBooleano("voce_video_verticale", False)
    End Function

    Public Shared Function NomeCamera() As String
        Return ImpostazioniMusica.Leggi("voce_camera", "")
    End Function

    Private Shared Function NomeSottofondo() As String
        Dim p As String = ImpostazioniMusica.Leggi("cam_sottofondo", "")
        If p <> "" AndAlso File.Exists(p) Then Return Path.GetFileNameWithoutExtension(p)
        Return ""
    End Function

    ''' <summary>Il testo della voce del sottofondo nei menu: scegliere se non c'è, cambiare se c'è.</summary>
    Public Shared Function TestoVoceSottofondo() As String
        Return If(NomeSottofondo() <> "", "Cambia sottofondo", "Sottofondo")
    End Function

    ''' <summary>
    ''' La sincronia della voce col video: si può misurare (calibrazione), regolare a mano in millisecondi o disattivare (0 millisecondi, la voce
    ''' resta dove è stata registrata). Il valore è lo stesso del registratore vocale (voce_compensazione_ms).
    ''' </summary>
    Public Shared Sub MostraSincronia()
        Try
            Dim valore As Integer = ImpostazioniMusica.LeggiIntero("voce_compensazione_ms", 0)
            Dim menu As New WGMenu()
            menu.aggiungi("Calibra", Sub()
                                                      menu.chiudi()
                                                      CalibratoreSincronia.Esegui()
                                                  End Sub)
            menu.aggiungi("Regola a mano", Sub()
                                                                                  menu.chiudi()
                                                                                  ConfigurazioneMusica.ScegliNumero("Regolazione fine della sincronia", "voce_compensazione_ms", 0, -200, 200, 5, " millisecondi")
                                                                              End Sub)
            If valore <> 0 Then
                menu.aggiungi("Azzera", Sub()
                                                            ImpostazioniMusica.Scrivi("voce_compensazione_ms", "0")
                                                            menu.chiudi()
                                                            AiutoMusica.Messaggio("Regolazione fine azzerata: la voce resta allineata con il ritardo misurato.")
                                                        End Sub)
            End If
            menu.avvia("Sincronia, regolazione fine ora " & valore.ToString() & " millisecondi")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.MostraSincronia: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Il sottofondo che si sente mentre si registra il video: senza, si sceglie; con, "Cambia sottofondo" apre scegli un nuovo sottofondo ed elimina il sottofondo.</summary>
    Public Shared Sub GestisciSottofondo()
        If NomeSottofondo() = "" Then
            ScegliNuovoSottofondo()
        Else
            AiutoSottofondo.MostraCambia(True, AddressOf ScegliNuovoSottofondo, AddressOf EliminaSottofondo)
        End If
    End Sub

    Private Shared Sub EliminaSottofondo()
        ImpostazioniMusica.Scrivi("cam_sottofondo", "")
        AiutoMusica.Messaggio("Sottofondo eliminato.")
    End Sub

    Private Shared _dialogoSottofondo As WGFileDialog

    Private Shared Sub ScegliNuovoSottofondo()
        Try
            _dialogoSottofondo = New WGFileDialog()
            _dialogoSottofondo.MultiselezioneConsentita = False
            For Each estensione As String In New String() {".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".aif", ".aiff", ".mp2"}
                _dialogoSottofondo.AggiungiEstensione(estensione)
            Next
            AddHandler _dialogoSottofondo.selezioneterminata, AddressOf OnSottofondoScelto
            _dialogoSottofondo.Avvia()
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliNuovoSottofondo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub OnSottofondoScelto(elementi As List(Of String))
        Try
            Try
                RemoveHandler _dialogoSottofondo.selezioneterminata, AddressOf OnSottofondoScelto
            Catch
            End Try
            If elementi Is Nothing OrElse elementi.Count = 0 OrElse Not File.Exists(elementi(0)) Then Return
            Try
                Using prova As New SottofondoAudio(elementi(0))
                End Using
            Catch ex As Exception
                AiutoMusica.Messaggio("Non è stato possibile aprire il file audio: " & ex.Message)
                Return
            End Try
            ImpostazioniMusica.Scrivi("cam_sottofondo", elementi(0))
            AiutoMusica.Messaggio("Sottofondo: " & Path.GetFileNameWithoutExtension(elementi(0)) & ".")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.OnSottofondoScelto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function TestoCamera() As String
        Dim n As String = NomeCamera()
        Return If(n <> "", n, "la prima trovata")
    End Function

    Private Shared Function TestoFormato() As String
        Dim r As String = ImpostazioniMusica.Leggi("cam_risoluzione", "")
        Return If(r <> "", r.Replace("x", " per "), "la migliore")
    End Function

    Private Shared Function TestoQualita() As String
        Dim q As Integer = ImpostazioniMusica.LeggiIntero("cam_qualita", 1)
        Return If(q = 0, "alta", If(q = 2, "bassa", "media"))
    End Function

    Public Shared Sub Mostra()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Modalità", Sub() ScegliModalita())
            menu.aggiungi("Videocamera", Sub() ScegliVideocamera())
            menu.aggiungi("Orientamento", Sub() ScegliOrientamentoVideo())
            menu.aggiungi("Risoluzione", Sub() ScegliRisoluzione())
            menu.aggiungi("Fotogrammi", Sub() ScegliFotogrammi())
            menu.aggiungi("Qualità", Sub() ScegliQualita())
            menu.aggiungi("Dove salvare", Sub() ScegliDoveSalvare())
            menu.aggiungi("Avanzate", Sub() MostraControlliAvanzati())
            menu.aggiungi("Microfono", Sub() ConfigurazioneRegistratore.ScegliMicrofono())
            menu.aggiungi("Effetti", Sub() ConfigurazioneRegistratore.MostraEffettiVoce())
            menu.aggiungi("Sincronia", Sub() MostraSincronia())
            menu.aggiungi("Ripeti sottofondo", Sub() ConfigurazioneMusica.ScegliSiNo("Ripeti il sottofondo", "voce_ripeti_sottofondo", False))
            menu.avvia("Impostazioni della fotocamera")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.Mostra: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ImpostaEChiudi(menu As WGMenu, chiave As String, valore As String, messaggio As String)
        ImpostazioniMusica.Scrivi(chiave, valore)
        menu.chiudi()
        AiutoMusica.Messaggio(messaggio)
    End Sub

    Public Shared Sub ScegliModalita()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Video", Sub() ImpostaEChiudi(menu, "cam_modalita", "video", "Modalità video: Invio avvia e ferma la registrazione."))
            menu.aggiungi("Foto", Sub() ImpostaEChiudi(menu, "cam_modalita", "foto", "Modalità foto: Invio scatta."))
            menu.posiziona(If(ModalitaFoto(), 1, 0))
            menu.avvia("Modalità, ora " & If(ModalitaFoto(), "foto", "video"), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliModalita: " & ex.Message)
        End Try
    End Sub

    ''' <summary>La scelta della videocamera (webcam) tra quelle collegate. Si cerca con un'attesa.</summary>
    Public Shared Async Sub ScegliVideocamera()
        Try
            If FfmpegUtil.Percorso() = "" Then
                AiutoMusica.Messaggio("Manca ffmpeg: il centro di controllo lo scarica da solo, riprova tra poco.")
                Return
            End If
            Dim camere As List(Of String) = Await WGProcessoInCorso.EseguiAsync("Cerco le videocamere", Function() Task.Run(Function() FfmpegUtil.Videocamere()))
            If camere Is Nothing OrElse camere.Count = 0 Then
                AiutoMusica.Messaggio("Non ho trovato nessuna videocamera.")
                Return
            End If
            Dim attuale As String = NomeCamera()
            Dim posizione As Integer = 0
            Dim menu As New WGMenu()
            For i As Integer = 0 To camere.Count - 1
                Dim nome As String = camere(i)
                If String.Equals(nome, attuale, StringComparison.OrdinalIgnoreCase) Then posizione = i
                menu.aggiungi(nome, Sub()
                                        ' cambiando videocamera le misure e i controlli della vecchia non valgono più
                                        ImpostazioniMusica.Scrivi("cam_risoluzione", "")
                                        ImpostaEChiudi(menu, "voce_camera", nome, "Videocamera: " & nome & ".")
                                    End Sub)
            Next
            menu.posiziona(posizione)
            menu.avvia("Videocamera, ora " & TestoCamera(), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliVideocamera: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Se il video si registra in orizzontale (come la videocamera) o in verticale (ritagliato al centro, come per i telefoni).</summary>
    Public Shared Sub ScegliOrientamentoVideo()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Orizzontale", Sub() ImpostaEChiudi(menu, "voce_video_verticale", "false", "Il video si registra in orizzontale."))
            menu.aggiungi("Verticale", Sub() ImpostaEChiudi(menu, "voce_video_verticale", "true", "Il video si registra in verticale: l'inquadratura si ritaglia al centro."))
            menu.posiziona(If(Verticale(), 1, 0))
            menu.avvia("Orientamento, ora " & If(Verticale(), "verticale", "orizzontale"), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliOrientamentoVideo: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Function FormatiDisponibiliAsync() As Task(Of List(Of FormatoCamera))
        Dim camera As String = NomeCamera()
        If camera = "" Then
            Dim camere As List(Of String) = Await WGProcessoInCorso.EseguiAsync("Cerco le videocamere", Function() Task.Run(Function() FfmpegUtil.Videocamere()))
            If camere IsNot Nothing AndAlso camere.Count > 0 Then camera = camere(0)
        End If
        If camera = "" Then Return New List(Of FormatoCamera)()
        Dim nome As String = camera
        Return Await WGProcessoInCorso.EseguiAsync("Leggo i formati della videocamera", Function() Task.Run(Function() ControlliFotocamera.Formati(nome)))
    End Function

    Public Shared Async Sub ScegliRisoluzione()
        Try
            Dim formati As List(Of FormatoCamera) = Await FormatiDisponibiliAsync()
            If formati Is Nothing OrElse formati.Count = 0 Then
                AiutoMusica.Messaggio("La videocamera non ha detto quali risoluzioni offre.")
                Return
            End If
            Dim attuale As String = ImpostazioniMusica.Leggi("cam_risoluzione", "")
            Dim menu As New WGMenu()
            Dim posizione As Integer = 0
            For i As Integer = 0 To formati.Count - 1
                Dim f As FormatoCamera = formati(i)
                Dim chiave As String = f.Larghezza.ToString() & "x" & f.Altezza.ToString()
                If chiave = attuale Then posizione = i
                menu.aggiungi(f.Testo(), Sub() ImpostaEChiudi(menu, "cam_risoluzione", chiave, "Risoluzione: " & f.Testo() & "."))
            Next
            menu.posiziona(posizione)
            menu.avvia("Risoluzione, ora " & TestoFormato(), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliRisoluzione: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ScegliFotogrammi()
        ConfigurazioneMusica.ScegliNumero("Fotogrammi al secondo", "cam_fps", 30, 10, 60, 5, " fotogrammi al secondo")
    End Sub

    Public Shared Sub ScegliQualita()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Alta", Sub() ImpostaEChiudi(menu, "cam_qualita", "0", "Qualità del video: alta."))
            menu.aggiungi("Media", Sub() ImpostaEChiudi(menu, "cam_qualita", "1", "Qualità del video: media."))
            menu.aggiungi("Bassa", Sub() ImpostaEChiudi(menu, "cam_qualita", "2", "Qualità del video: bassa."))
            menu.posiziona(Math.Max(0, Math.Min(2, ImpostazioniMusica.LeggiIntero("cam_qualita", 1))))
            menu.avvia("Qualità del video, ora " & TestoQualita(), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliQualita: " & ex.Message)
        End Try
    End Sub

    ' ---- dove si salvano foto e video -----------------------------------------------------------------------------
    ' Invio non chiede niente: il file resta in una posizione temporanea. Con "Salva" (menu delle opzioni) si sceglie il nome e, se serve, la
    ' cartella. Qui si decide se la cartella è quella dell'utente (Video per i video, Immagini per le foto) o se si sceglie ogni volta.

    Public Shared Function DoveSalvareUtente() As Boolean
        Return ImpostazioniMusica.Leggi("cam_dove", "scegli") = "utente"
    End Function

    ''' <summary>La cartella Video (o Immagini, per le foto) dell'utente corrente.</summary>
    Public Shared Function CartellaUtente(foto As Boolean) As String
        Dim c As String = Environment.GetFolderPath(If(foto, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos))
        If c = "" Then c = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        Return c
    End Function

    Public Shared Sub ScegliDoveSalvare()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Cartella utente", Sub() ImpostaEChiudi(menu, "cam_dove", "utente", "Salva nella cartella Video dell'utente corrente, e le foto in Immagini: Salva chiede solo il nome."))
            menu.aggiungi("Da scegliere", Sub() ImpostaEChiudi(menu, "cam_dove", "scegli", "Salva chiede la cartella e il nome ogni volta."))
            menu.posiziona(If(DoveSalvareUtente(), 0, 1))
            menu.avvia("Dove salvare foto e video, ora " & If(DoveSalvareUtente(), "la cartella dell'utente", "da scegliere ogni volta"), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliDoveSalvare: " & ex.Message)
        End Try
    End Sub

    ' ---- i controlli della videocamera -----------------------------------------------------------------------

    ''' <summary>
    ''' I controlli che la videocamera espone davvero (luminosità, contrasto, esposizione, messa a fuoco, zoom, bilanciamento del bianco e
    ''' gli altri): si scelgono dall'elenco; il valore resta salvato e si rimette ogni volta che si apre la fotocamera.
    ''' </summary>
    Public Shared Async Sub MostraControlliAvanzati()
        Try
            Dim camera As String = NomeCamera()
            If camera = "" Then
                Dim camere As List(Of String) = Await WGProcessoInCorso.EseguiAsync("Cerco le videocamere", Function() Task.Run(Function() FfmpegUtil.Videocamere()))
                If camere IsNot Nothing AndAlso camere.Count > 0 Then camera = camere(0)
            End If
            If camera = "" Then
                AiutoMusica.Messaggio("Non ho trovato nessuna videocamera.")
                Return
            End If
            Dim nomeSel As String = camera
            Dim elenco As List(Of ProprietaCamera) = Await WGProcessoInCorso.EseguiAsync("Leggo i controlli della videocamera", Function() Task.Run(Function() ControlliFotocamera.Elenco(nomeSel)))
            If elenco Is Nothing OrElse elenco.Count = 0 Then
                AiutoMusica.Messaggio("Questa videocamera non permette di regolare altro.")
                Return
            End If
            Dim menu As New WGMenu()
            For Each prop As ProprietaCamera In elenco
                Dim p As ProprietaCamera = prop
                menu.aggiungi(p.Nome, Sub() ScegliValoreControllo(nomeSel, p))
            Next
            menu.avvia("Avanzate")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.MostraControlliAvanzati: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliValoreControllo(camera As String, p As ProprietaCamera)
        Try
            Dim menu As New WGMenu()
            If p.AutomaticoPossibile Then
                menu.aggiungi("Automatico", Sub()
                                                menu.chiudi()
                                                ApplicaControllo(camera, p, p.Corrente, True)
                                            End Sub)
            End If
            If p.ManualePossibile Then
                menu.aggiungi("Valore", Sub()
                                                                                                                                                                       menu.chiudi()
                                                                                                                                                                       AiutoRevisione.ChiediTesto(p.Nome & ", da " & p.Minimo.ToString() & " a " & p.Massimo.ToString(), p.Corrente.ToString(),
                                                                                                                                                                                                  Sub(testo As String)
                                                                                                                                                                                                      Dim v As Integer
                                                                                                                                                                                                      If Not Integer.TryParse(If(testo, "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, v) OrElse v < p.Minimo OrElse v > p.Massimo Then
                                                                                                                                                                                                          AiutoMusica.Messaggio("Il valore va da " & p.Minimo.ToString() & " a " & p.Massimo.ToString() & ".")
                                                                                                                                                                                                          Return
                                                                                                                                                                                                      End If
                                                                                                                                                                                                      ApplicaControllo(camera, p, v, False)
                                                                                                                                                                                                  End Sub)
                                                                                                                                                                   End Sub)
            End If
            menu.aggiungi("Predefinito", Sub()
                                                                                                    menu.chiudi()
                                                                                                    ApplicaControllo(camera, p, p.Predefinito, False)
                                                                                                End Sub)
            menu.avvia(p.Descrizione() & ", da " & p.Minimo.ToString() & " a " & p.Massimo.ToString() & If(p.Passo > 1, ", a passi di " & p.Passo.ToString(), ""))
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneFotocamera.ScegliValoreControllo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ApplicaControllo(camera As String, p As ProprietaCamera, valore As Integer, automatico As Boolean)
        If ControlliFotocamera.Imposta(camera, p, valore, automatico) Then
            ImpostazioniMusica.Scrivi("cam_p_" & p.Id, valore.ToString(CultureInfo.InvariantCulture))
            ImpostazioniMusica.Scrivi("cam_a_" & p.Id, If(automatico, "true", "false"))
            p.Corrente = valore
            p.InAutomatico = automatico
            AiutoMusica.Annuncia(p.Nome & ": " & If(automatico, "automatico", valore.ToString()))
        Else
            AiutoMusica.Messaggio("La videocamera non ha accettato questo valore per " & p.Nome & ".")
        End If
    End Sub

End Class
