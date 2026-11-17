Imports System
Imports System.Collections.Generic
Imports NAudio.Wave

''' <summary>
''' Le impostazioni del registratore vocale: microfono, effetti sulla voce, volumi, ripetizione del sottofondo e sincronia. Si aprono
''' da Impostazioni, Applicazioni, Registratore vocale e dalle opzioni del registratore stesso: i valori sono sempre quelli salvati
''' (voce_microfono, voce_fx_*, voce_volume e gli altri).
''' </summary>
Public NotInheritable Class ConfigurazioneRegistratore

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Public Shared Sub Mostra()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Microfono", Sub() ScegliMicrofono())
            menu.aggiungi("Effetti", Sub() MostraEffettiVoce())
            menu.aggiungi("Ripeti sottofondo", Sub() ConfigurazioneMusica.ScegliSiNo("Ripeti il sottofondo", "voce_ripeti_sottofondo", False))
            menu.aggiungi("Sincronia", Sub() ConfigurazioneMusica.ScegliNumero("Regolazione fine della sincronia", "voce_compensazione_ms", 0, -200, 200, 5, " millisecondi"))
            menu.avvia("Impostazioni del registratore vocale")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneRegistratore.Mostra: " & ex.Message)
        End Try
    End Sub

    ' ---- il tempo massimo di registrazione ----------------------------------------------------------------------
    ' Si imposta qui (registratore vocale: voce_max_secondi, fotocamera: cam_max_secondi). 0 vuol dire nessun limite. Quando la registrazione
    ' arriva a quel tempo si ferma da sola, come se si fosse premuto lo stop. Il valore si legge all'inizio di ogni registrazione.

    Public Shared Function SecondiLimite(chiave As String) As Integer
        Return Math.Max(0, Math.Min(86400, ImpostazioniMusica.LeggiIntero(chiave, 0)))
    End Function

    Public Shared Function TestoLimite(chiave As String) As String
        Dim s As Integer = SecondiLimite(chiave)
        If s = 0 Then Return "nessun limite"
        If s < 60 Then Return s.ToString() & If(s = 1, " secondo", " secondi")
        Dim m As Integer = s \ 60
        Dim r As Integer = s Mod 60
        Dim t As String = m.ToString() & If(m = 1, " minuto", " minuti")
        If r > 0 Then t &= " e " & r.ToString() & If(r = 1, " secondo", " secondi")
        Return t
    End Function

    ''' <summary>Il menu "Volume" del menu Alt: Sottofondo e Microfono, ognuno con la sua regolazione.</summary>
    Public Shared Sub MostraVolume()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Sottofondo", Sub()
                                            menu.chiudi()
                                            ConfigurazioneMusica.ScegliNumero("Volume del sottofondo", "voce_volume_sottofondo", 60, 0, 100, 10, " per cento")
                                        End Sub)
            menu.aggiungi("Microfono", Sub()
                                           menu.chiudi()
                                           ConfigurazioneMusica.ScegliNumero("Volume del microfono", "voce_volume", 100, 0, 400, 10, " per cento")
                                       End Sub)
            menu.avvia("Volume")
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneRegistratore.MostraVolume: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub ScegliLimite(chiave As String)
        Try
            Dim valori() As Integer = {0, 10, 30, 60, 120, 300, 600, 900, 1800, 3600}
            Dim attuale As Integer = SecondiLimite(chiave)
            Dim menu As New WGMenu()
            Dim posizione As Integer = valori.Length
            For i As Integer = 0 To valori.Length - 1
                Dim v As Integer = valori(i)
                If v = attuale Then posizione = i
                Dim testo As String = If(v = 0, "Nessun limite", If(v < 60, v.ToString() & " secondi", If(v = 60, "1 minuto", If(v < 3600, (v \ 60).ToString() & " minuti", "1 ora"))))
                menu.aggiungi(testo, Sub()
                                         ImpostazioniMusica.Scrivi(chiave, v.ToString())
                                         menu.chiudi()
                                         AiutoMusica.Messaggio("Tempo massimo di registrazione: " & TestoLimite(chiave) & ".")
                                     End Sub)
            Next
            menu.aggiungi("Altro valore", Sub()
                                                                                                                    menu.chiudi()
                                                                                                                    AiutoRevisione.ChiediTesto("Tempo massimo in secondi, da 1 a 86400, oppure 0 per nessun limite", attuale.ToString(),
                                                                                                                                               Sub(testo As String)
                                                                                                                                                   Dim s As Integer
                                                                                                                                                   If Not Integer.TryParse(If(testo, "").Trim(), s) OrElse s < 0 OrElse s > 86400 Then
                                                                                                                                                       AiutoMusica.Messaggio("Il tempo va da 1 a 86400 secondi; 0 vuol dire nessun limite.")
                                                                                                                                                       Return
                                                                                                                                                   End If
                                                                                                                                                   ImpostazioniMusica.Scrivi(chiave, s.ToString())
                                                                                                                                                   AiutoMusica.Messaggio("Tempo massimo di registrazione: " & TestoLimite(chiave) & ".")
                                                                                                                                               End Sub)
                                                                                                                End Sub)
            menu.posiziona(posizione)
            menu.avvia("Tempo massimo di registrazione, ora " & TestoLimite(chiave), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneRegistratore.ScegliLimite: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' La scelta del microfono tra gli ingressi del computer. Vale per la registrazione audio e per quella video: il registratore
    ''' prende l'audio sempre da qui.
    ''' </summary>
    Public Shared Sub ScegliMicrofono()
        Try
            Dim quanti As Integer = WaveInEvent.DeviceCount
            If quanti = 0 Then
                AiutoMusica.Messaggio("Non c'è nessun microfono collegato al computer.")
                Return
            End If
            Dim attuale As String = ImpostazioniMusica.Leggi("voce_microfono", "")
            Dim posizione As Integer = 0
            Dim menu As New WGMenu()
            For i As Integer = 0 To quanti - 1
                Dim nome As String = WaveInEvent.GetCapabilities(i).ProductName
                If String.Equals(nome, attuale, StringComparison.OrdinalIgnoreCase) Then posizione = i
                menu.aggiungi(nome, Sub()
                                        ImpostazioniMusica.Scrivi("voce_microfono", nome)
                                        menu.chiudi()
                                        AiutoMusica.Messaggio("Microfono: " & nome & ".")
                                    End Sub)
            Next
            menu.posiziona(posizione)
            menu.avvia("Microfono", True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.ScegliMicrofono: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Stato(valore As Boolean) As String
        Return If(valore, "attivo", "non attivo")
    End Function

    ''' <summary>
    ''' Gli effetti che il registratore applica alla voce appena registrata, nella registrazione audio e in quella video. Il menu
    ''' si riapre dopo ogni scelta, con i valori aggiornati.
    ''' </summary>
    Public Shared Sub MostraEffettiVoce()
        Try
            Dim imp As ImpostazioniEffettiVoce = ImpostazioniEffettiVoce.Leggi()
            Dim menu As New WGMenu()
            Dim nomeIntonazione As String = If(imp.Intonazione = "autotune", "autotune", If(imp.Intonazione = "melodia", "correzione della melodia", "nessuna"))
            menu.aggiungi("Intonazione", Sub() Riapri(menu, AddressOf ScegliIntonazione))
            menu.aggiungi("Forza della melodia", Sub() Riapri(menu, Sub() ConfigurazioneMusica.ScegliNumero("Forza della melodia", "voce_fx_melodia_forza", 100, 0, 100, 10, " per cento")))
            menu.aggiungi("Vibrato della melodia", Sub() Riapri(menu, Sub() ConfigurazioneMusica.ScegliNumero("Vibrato della melodia", "voce_fx_melodia_vibrato", 100, 0, 100, 10, " per cento")))
            menu.aggiungi("Scala", Sub() Riapri(menu, AddressOf ScegliScala))
            menu.aggiungi("Nota", Sub() Riapri(menu, AddressOf ScegliTonica))
            menu.aggiungi("Tono",
                          Sub() Riapri(menu, Sub() ConfigurazioneMusica.ScegliNumero("Tono della voce", "voce_fx_semitoni", 0, -12, 12, 1, " semitoni")))
            menu.aggiungi("Rumore", Sub() Riapri(menu, AddressOf ScegliRumore))
            menu.aggiungi("Voce chiara", Sub() Riapri(menu, Sub() ConfigurazioneMusica.ScegliSiNo("Voce chiara", "voce_fx_chiarezza", False)))
            menu.aggiungi("Compressore", Sub() Riapri(menu, Sub() ConfigurazioneMusica.ScegliSiNo("Compressore", "voce_fx_compressore", False)))
            menu.aggiungi("Volume uniforme", Sub() Riapri(menu, Sub() ConfigurazioneMusica.ScegliSiNo("Volume uniforme", "voce_fx_livella", False)))
            menu.aggiungi("Riverbero", Sub() Riapri(menu, AddressOf ScegliRiverbero))
            menu.aggiungi("Eco", Sub() Riapri(menu, Sub() ConfigurazioneMusica.ScegliSiNo("Eco", "voce_fx_eco", False)))
            menu.aggiungi("Togli tutti", Sub()
                                                         ImpostazioniMusica.Scrivi("voce_fx_intonazione", "nessuna")
                                                         ImpostazioniMusica.Scrivi("voce_fx_semitoni", "0")
                                                         ImpostazioniMusica.ScriviBooleano("voce_fx_rumore", False)
                                                         ImpostazioniMusica.ScriviBooleano("voce_fx_chiarezza", False)
                                                         ImpostazioniMusica.ScriviBooleano("voce_fx_compressore", False)
                                                         ImpostazioniMusica.ScriviBooleano("voce_fx_livella", False)
                                                         ImpostazioniMusica.Scrivi("voce_fx_riverbero", "nessuno")
                                                         ImpostazioniMusica.ScriviBooleano("voce_fx_eco", False)
                                                         menu.chiudi()
                                                         AiutoMusica.Messaggio("Nessun effetto sulla voce.")
                                                     End Sub)
            menu.avvia("Effetti sulla voce. Attivi: " & imp.Descrizione())
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.MostraEffettiVoce: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Riapri(menu As WGMenu, scelta As Action)
        Try
            menu.chiudi()
        Catch
        End Try
        scelta()
    End Sub

    Private Shared Sub ScegliRumore()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Spento", Sub()
                                        ImpostazioniMusica.ScriviBooleano("voce_fx_rumore", False)
                                        menu.chiudi()
                                        AiutoMusica.Messaggio("Riduzione del rumore spenta.")
                                    End Sub)
            menu.aggiungi("Leggero", Sub() ImpostaRumore(menu, 10, "leggera"))
            menu.aggiungi("Medio", Sub() ImpostaRumore(menu, 18, "media"))
            menu.aggiungi("Forte", Sub() ImpostaRumore(menu, 28, "forte"))
            Dim acceso As Boolean = ImpostazioniMusica.LeggiBooleano("voce_fx_rumore", False)
            Dim db As Integer = ImpostazioniMusica.LeggiIntero("voce_fx_rumore_db", 18)
            menu.posiziona(If(Not acceso, 0, If(db <= 12, 1, If(db >= 24, 3, 2))))
            menu.avvia("Rumore, ora " & If(Not acceso, "spento", If(db <= 12, "leggero", If(db >= 24, "forte", "medio"))), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneRegistratore.ScegliRumore: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ImpostaRumore(menu As WGMenu, db As Integer, nome As String)
        ImpostazioniMusica.ScriviBooleano("voce_fx_rumore", True)
        ImpostazioniMusica.Scrivi("voce_fx_rumore_db", db.ToString())
        menu.chiudi()
        AiutoMusica.Messaggio("Riduzione del rumore " & nome & ".")
    End Sub

    Private Shared Sub ScegliIntonazione()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Nessuna", Sub() ImpostaTesto(menu, "voce_fx_intonazione", "nessuna", "Intonazione: nessuna correzione."))
            menu.aggiungi("Melodia", Sub() ImpostaTesto(menu, "voce_fx_intonazione", "melodia", "Intonazione: correzione della melodia."))
            menu.aggiungi("Autotune", Sub() ImpostaTesto(menu, "voce_fx_intonazione", "autotune", "Intonazione: autotune."))
            Dim attuale As String = ImpostazioniMusica.Leggi("voce_fx_intonazione", "nessuna")
            menu.posiziona(If(attuale = "melodia", 1, If(attuale = "autotune", 2, 0)))
            menu.avvia("Intonazione, ora " & If(attuale = "autotune", "autotune", If(attuale = "melodia", "correzione della melodia", "nessuna")), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.ScegliIntonazione: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliRiverbero()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Nessuno", Sub() ImpostaTesto(menu, "voce_fx_riverbero", "nessuno", "Riverbero spento."))
            menu.aggiungi("Stanza", Sub() ImpostaTesto(menu, "voce_fx_riverbero", "stanza", "Riverbero da stanza: una coda breve."))
            menu.aggiungi("Sala", Sub() ImpostaTesto(menu, "voce_fx_riverbero", "sala", "Riverbero da sala: una coda di circa due secondi."))
            menu.aggiungi("Grande", Sub() ImpostaTesto(menu, "voce_fx_riverbero", "grande", "Riverbero da ambiente grande: una coda lunga."))
            Dim attuale As String = ImpostazioniMusica.Leggi("voce_fx_riverbero", "nessuno")
            menu.posiziona(If(attuale = "stanza", 1, If(attuale = "sala", 2, If(attuale = "grande", 3, 0))))
            menu.avvia("Riverbero, ora " & If(attuale = "stanza" OrElse attuale = "sala" OrElse attuale = "grande", attuale, "nessuno"), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneRegistratore.ScegliRiverbero: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliScala()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Tutte", Sub() ImpostaTesto(menu, "voce_fx_scala", "cromatica", "Scala: tutte le note."))
            menu.aggiungi("Maggiore", Sub() ImpostaTesto(menu, "voce_fx_scala", "maggiore", "Scala: maggiore."))
            menu.aggiungi("Minore", Sub() ImpostaTesto(menu, "voce_fx_scala", "minore", "Scala: minore."))
            menu.aggiungi("Come il sottofondo", Sub() ImpostaTesto(menu, "voce_fx_scala", "sottofondo", "Scala: quella del sottofondo, se c'è; senza sottofondo valgono tutte le note."))
            Dim attuale As String = ImpostazioniMusica.Leggi("voce_fx_scala", "sottofondo")
            menu.posiziona(If(attuale = "maggiore", 1, If(attuale = "minore", 2, If(attuale = "sottofondo", 3, 0))))
            menu.avvia("Scala, ora " & If(attuale = "maggiore", "maggiore", If(attuale = "minore", "minore", If(attuale = "sottofondo", "come il sottofondo", "tutte le note"))), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.ScegliScala: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliTonica()
        Try
            Dim menu As New WGMenu()
            For i As Integer = 0 To 11
                Dim nota As Integer = i
                menu.aggiungi(ImpostazioniEffettiVoce.NomiNote(i), Sub() ImpostaTesto(menu, "voce_fx_tonica", nota.ToString(), "Nota di partenza: " & ImpostazioniEffettiVoce.NomiNote(nota) & "."))
            Next
            menu.posiziona(Math.Max(0, Math.Min(11, ImpostazioniMusica.LeggiIntero("voce_fx_tonica", 0))))
            menu.avvia("Nota, ora " & ImpostazioniEffettiVoce.NomiNote(Math.Max(0, Math.Min(11, ImpostazioniMusica.LeggiIntero("voce_fx_tonica", 0)))), True)
        Catch ex As Exception
            log.ScriviLog("ConfigurazioneMusica.ScegliTonica: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ImpostaTesto(menu As WGMenu, chiave As String, valore As String, messaggio As String)
        ImpostazioniMusica.Scrivi(chiave, valore)
        menu.chiudi()
        AiutoMusica.Messaggio(messaggio)
    End Sub

End Class
