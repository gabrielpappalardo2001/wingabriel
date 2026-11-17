' ============================================================================================
' SessioneEditorVideo.Menu - i menu di Invio: "Inizia taglio", "Esegui modifica" e tutte le modifiche
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading.Tasks

Partial Public NotInheritable Class SessioneEditorVideo

    Private Shared ReadOnly Cultura As CultureInfo = CultureInfo.InvariantCulture

    ''' <summary>Un menu di scelte: ogni voce ha il suo testo e quello che fa; il menu si chiude prima di eseguirla.</summary>
    Private Shared Sub MostraScelte(titolo As String, voci As List(Of KeyValuePair(Of String, Action)))
        Dim menu As New WGMenu()
        For Each voce As KeyValuePair(Of String, Action) In voci
            Dim azione As Action = voce.Value
            menu.aggiungi(voce.Key, Sub(s As Object, ev As EventArgs)
                                        ChiudiMenu(menu)
                                        azione()
                                    End Sub)
        Next
        menu.avvia(titolo)
    End Sub

    Private Shared Function Voce(testo As String, azione As Action) As KeyValuePair(Of String, Action)
        Return New KeyValuePair(Of String, Action)(testo, azione)
    End Function

    ' ---- Invio -------------------------------------------------------------------------------------------------

    Private Sub OnInvio(id As String)
        Try
            If _esportando OrElse _registrandoVoce Then Return
            If _inTaglio Then
                TerminaTaglio()
                Return
            End If
            If _inAscolto Then FermaAscolto()
            MostraScelte("Posizione " & ProgettoVideo.TestoTempo(PosizioneCorrente()), New List(Of KeyValuePair(Of String, Action)) From {
                         Voce("Inizia taglio", Sub() IniziaTaglio()),
                         Voce("Esegui modifica", Sub() MenuModifiche())})
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.OnInvio: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    ''' <summary>"Esegui modifica": tutte le modifiche che si possono fare nel punto in cui ci si trova.</summary>
    Private Sub MenuModifiche()
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        voci.Add(Voce("Salva il progetto", Sub() SalvaProgetto(Nothing)))
        If _p.Cartella <> "" Then voci.Add(Voce("Salva il progetto con un altro nome", Sub() ChiediNomeProgetto(True, Nothing)))
        voci.Add(Voce("Aggiungi effetto audio", Sub() ScegliEffetto(TipoEffetto.Sonoro)))
        voci.Add(Voce("Aggiungi effetto visivo", Sub() ScegliEffetto(TipoEffetto.Visivo)))
        voci.Add(Voce("Aggiungi testo scritto sul video", Sub() ChiediTestoSulVideo()))
        voci.Add(Voce("Aggiungi base musicale di sottofondo", Sub() ScegliBaseMusicale()))
        If _p.BaseMusicale <> "" Then
            voci.Add(Voce(If(_p.AbbassaBase, "La base musicale non si abbassa sotto l'audio del video", "La base musicale si abbassa da sola sotto l'audio del video"), Sub()
                                                                                                                                                                         _p.AbbassaBase = Not _p.AbbassaBase
                                                                                                                                                                         AudioCambiato()
                                                                                                                                                                         AiutoMusica.Annuncia(If(_p.AbbassaBase, "La base musicale si abbassa da sola quando c'è l'audio del video", "La base musicale resta sempre allo stesso volume"))
                                                                                                                                                                     End Sub))
        End If
        voci.Add(Voce("Aggiungi voiceover", Sub() MenuVoiceover()))
        voci.Add(Voce("Sostituisci l'audio del video", Sub() ScegliAudioSostitutivo()))
        voci.Add(Voce("Silenzia l'audio originale in un tratto", Sub() ChiediSilenzio()))
        voci.Add(Voce("Volume dell'audio originale: " & _p.VolumeAudioOriginale.ToString() & " per cento", Sub() ChiediVolumeOriginale()))
        voci.Add(Voce("Dissolvenza dell'audio all'inizio e alla fine", Sub() ChiediDissolvenzaAudio()))
        voci.Add(Voce(If(_p.NormalizzaAudio, "Non livellare il volume dell'audio", "Livella il volume dell'audio"), Sub()
                                                                                                                      _p.NormalizzaAudio = Not _p.NormalizzaAudio
                                                                                                                      AiutoMusica.Annuncia(If(_p.NormalizzaAudio, "Il volume dell'audio sarà livellato in esportazione", "Il volume dell'audio non sarà livellato"))
                                                                                                                  End Sub))
        voci.Add(Voce("Sistema inquadratura e colori", Sub() MenuCorrezione()))
        voci.Add(Voce("Ritaglia l'inquadratura in un formato", Sub() MenuFormatoRitaglio()))
        voci.Add(Voce("Ruota il video", Sub() MenuRotazione()))
        voci.Add(Voce("Velocità del video", Sub() MenuVelocita()))
        voci.Add(Voce("Taglia dall'inizio fino a qui", Sub() TagliaEstremo(True)))
        voci.Add(Voce("Taglia da qui fino alla fine", Sub() TagliaEstremo(False)))
        voci.Add(Voce("Segni", Sub() MenuSegni()))
        voci.Add(Voce("Vai a un punto preciso", Sub() ChiediPunto()))
        If _p.HaModifiche() Then voci.Add(Voce("Elimina una modifica", Sub() MenuElimina()))
        voci.Add(Voce("Riepilogo", Sub() AiutoMusica.Annuncia(_p.Riepilogo())))
        MostraScelte("Esegui modifica", voci)
    End Sub

    ' ---- effetti ---------------------------------------------------------------------------------------------

    Private Sub ScegliEffetto(tipo As TipoEffetto)
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        For Each def As DefinizioneEffetto In LibreriaEffetti.Elenco(tipo)
            Dim scelto As DefinizioneEffetto = def
            voci.Add(Voce(def.Nome, Sub() ApplicaEffetto(scelto)))
        Next
        MostraScelte(If(tipo = TipoEffetto.Sonoro, "Effetto audio", "Effetto visivo"), voci)
    End Sub

    Private Sub ApplicaEffetto(def As DefinizioneEffetto)
        Dim inizio As Double = PosizioneCorrente()
        If def.Tipo = TipoEffetto.Sonoro Then
            _p.EffettiSonori.Add(New EffettoApplicato() With {.Id = def.Id, .Inizio = inizio})
            AudioCambiato()
            AiutoMusica.Annuncia("Aggiunto l'effetto " & def.Nome & " a " & ProgettoVideo.TestoTempo(inizio) & ". Si sente insieme all'audio del video")
            Return
        End If
        If Not def.ConDurata Then
            _p.EffettiVisivi.Add(New EffettoApplicato() With {.Id = def.Id, .Inizio = inizio, .Fine = Math.Min(_p.Durata, inizio + def.DurataPredefinita)})
            AiutoMusica.Annuncia("Aggiunto l'effetto " & def.Nome & " a " & ProgettoVideo.TestoTempo(inizio))
            Return
        End If
        AiutoRevisione.ChiediTesto("Durata dell'effetto in secondi", def.DurataPredefinita.ToString("0.##", Cultura),
                                   Sub(testo As String)
                                       Dim durata As Double
                                       If Not Double.TryParse(If(testo, "").Trim().Replace(","c, "."c), NumberStyles.Float, Cultura, durata) OrElse durata <= 0.0 OrElse durata > 3600.0 Then
                                           AiutoMusica.Messaggio("La durata dev'essere un numero di secondi maggiore di zero.")
                                           Return
                                       End If
                                       Dim fine As Double = Math.Min(_p.Durata, inizio + durata)
                                       _p.EffettiVisivi.Add(New EffettoApplicato() With {.Id = def.Id, .Inizio = inizio, .Fine = fine})
                                       AiutoMusica.Annuncia("Aggiunto l'effetto " & def.Nome & " da " & ProgettoVideo.TestoTempo(inizio) & " a " & ProgettoVideo.TestoTempo(fine))
                                   End Sub)
    End Sub

    ' ---- testo, silenzio, dissolvenza, livello ---------------------------------------------------------------

    Private Sub ChiediTestoSulVideo()
        Dim inizio As Double = PosizioneCorrente()
        AiutoRevisione.ChiediTesto("Testo da scrivere sul video", "",
                                   Sub(testo As String)
                                       Dim pulito As String = LibreriaEffetti.TestoSicuro(testo)
                                       If pulito = "" Then
                                           AiutoMusica.Messaggio("Non hai scritto niente.")
                                           Return
                                       End If
                                       AiutoRevisione.ChiediTesto("Per quanti secondi resta scritto", "4",
                                                                  Sub(durataTesto As String)
                                                                      Dim durata As Double
                                                                      If Not Double.TryParse(If(durataTesto, "").Trim().Replace(","c, "."c), NumberStyles.Float, Cultura, durata) OrElse durata <= 0.0 OrElse durata > 3600.0 Then
                                                                          AiutoMusica.Messaggio("La durata dev'essere un numero di secondi maggiore di zero.")
                                                                          Return
                                                                      End If
                                                                      Dim fine As Double = Math.Min(_p.Durata, inizio + durata)
                                                                      _p.EffettiVisivi.Add(New EffettoApplicato() With {.Id = "testo", .Inizio = inizio, .Fine = fine, .Testo = pulito})
                                                                      AiutoMusica.Annuncia("Scritto sul video: " & pulito & ", da " & ProgettoVideo.TestoTempo(inizio) & " a " & ProgettoVideo.TestoTempo(fine))
                                                                  End Sub)
                                   End Sub)
    End Sub

    Private Sub ChiediSilenzio()
        Dim inizio As Double = PosizioneCorrente()
        AiutoRevisione.ChiediTesto("Per quanti secondi silenziare l'audio originale, a partire da qui", "3",
                                   Sub(testo As String)
                                       Dim durata As Double
                                       If Not Double.TryParse(If(testo, "").Trim().Replace(","c, "."c), NumberStyles.Float, Cultura, durata) OrElse durata <= 0.0 OrElse durata > 3600.0 Then
                                           AiutoMusica.Messaggio("La durata dev'essere un numero di secondi maggiore di zero.")
                                           Return
                                       End If
                                       Dim fine As Double = Math.Min(_p.Durata, inizio + durata)
                                       _p.Silenzi.Add(New Intervallo(inizio, fine))
                                       AudioCambiato()
                                       AiutoMusica.Annuncia("Audio originale silenziato da " & ProgettoVideo.TestoTempo(inizio) & " a " & ProgettoVideo.TestoTempo(fine) & ". Effetti, voiceover e base musicale si sentono")
                                   End Sub)
    End Sub

    Private Sub ChiediDissolvenzaAudio()
        AiutoRevisione.ChiediTesto("Secondi di dissolvenza dell'audio all'inizio e alla fine, da 0 a 10", _p.DissolvenzaAudio.ToString("0.##", Cultura),
                                   Sub(testo As String)
                                       Dim secondi As Double
                                       If Not Double.TryParse(If(testo, "").Trim().Replace(","c, "."c), NumberStyles.Float, Cultura, secondi) OrElse secondi < 0.0 OrElse secondi > 10.0 Then
                                           AiutoMusica.Messaggio("La dissolvenza va da 0 a 10 secondi.")
                                           Return
                                       End If
                                       _p.DissolvenzaAudio = secondi
                                       AiutoMusica.Annuncia(If(secondi = 0.0, "Nessuna dissolvenza dell'audio", "Dissolvenza dell'audio di " & secondi.ToString("0.##", CultureInfo.CurrentCulture) & " secondi, si sente nel video esportato"))
                                   End Sub)
    End Sub

    ' ---- formato, rotazione, velocità, tagli agli estremi, segni ---------------------------------------------

    Private Sub MenuFormatoRitaglio()
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        voci.Add(Voce("Nessun ritaglio", Sub() ImpostaFormato("")))
        For Each f As String In New String() {"9:16", "1:1", "4:5", "16:9", "4:3"}
            Dim formato As String = f
            voci.Add(Voce(f.Replace(":", " su ") & If(f = "9:16", ", verticale", If(f = "16:9", ", orizzontale", If(f = "1:1", ", quadrato", ""))), Sub() ImpostaFormato(formato)))
        Next
        MostraScelte("Ritaglia l'inquadratura", voci)
    End Sub

    Private Sub ImpostaFormato(formato As String)
        _p.FormatoRitaglio = formato
        Dim w, h As Integer
        _p.DimensioniVisibili(w, h)
        AiutoMusica.Annuncia(If(formato = "", "Nessun ritaglio", "Inquadratura ritagliata al centro: il video sarà " & w.ToString() & " per " & h.ToString()))
    End Sub

    Private Sub MenuRotazione()
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        For Each g As Integer In New Integer() {0, 90, 180, 270}
            Dim gradi As Integer = g
            voci.Add(Voce(If(g = 0, "Come l'originale", g.ToString() & " gradi in senso orario"), Sub()
                                                                                                     _p.Rotazione = gradi
                                                                                                     Dim w, h As Integer
                                                                                                     _p.DimensioniVisibili(w, h)
                                                                                                     AiutoMusica.Annuncia("Video ruotato di " & gradi.ToString() & " gradi: sarà " & w.ToString() & " per " & h.ToString())
                                                                                                 End Sub))
        Next
        MostraScelte("Ruota il video", voci)
    End Sub

    Private Sub MenuVelocita()
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        For Each v As Double In New Double() {1.0, 0.5, 0.75, 1.25, 1.5, 2.0, 3.0}
            Dim velocita As Double = v
            voci.Add(Voce(If(v = 1.0, "Normale", If(v < 1.0, "Più lento, ", "Più veloce, ") & v.ToString("0.##", CultureInfo.CurrentCulture) & " volte"), Sub()
                                                                                                                                                              _p.Velocita = velocita
                                                                                                                                                              AiutoMusica.Annuncia("Velocità " & velocita.ToString("0.##", CultureInfo.CurrentCulture) & " volte. Il video finale dura " & ProgettoVideo.TestoTempo(_p.DurataFinale()) & ". Si vede e si sente nel video esportato")
                                                                                                                                                          End Sub))
        Next
        MostraScelte("Velocità del video", voci)
    End Sub

    Private Sub TagliaEstremo(dall_inizio As Boolean)
        Dim pos As Double = PosizioneCorrente()
        If dall_inizio Then
            _p.AggiungiTaglio(0.0, pos)
        Else
            _p.AggiungiTaglio(pos, _p.Durata)
        End If
        AudioCambiato()
        AiutoMusica.Annuncia(If(dall_inizio, "Tagliato dall'inizio fino a ", "Tagliato da ") & ProgettoVideo.TestoTempo(pos) & If(dall_inizio, "", " fino alla fine") & ". Il video ora dura " & ProgettoVideo.TestoTempo(_p.DurataFinale()))
    End Sub

    Private Sub MenuSegni()
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        voci.Add(Voce("Segna questo punto", Sub()
                                                Dim pos As Double = PosizioneCorrente()
                                                _p.Segni.Add(pos)
                                                _p.Segni.Sort()
                                                AiutoMusica.Annuncia("Segno messo a " & ProgettoVideo.TestoTempo(pos) & ". Segni in tutto: " & _p.Segni.Count.ToString())
                                            End Sub))
        If _p.Segni.Count > 0 Then
            voci.Add(Voce("Vai al segno successivo", Sub() VaiAlSegno(True)))
            voci.Add(Voce("Vai al segno precedente", Sub() VaiAlSegno(False)))
        End If
        MostraScelte("Segni", voci)
    End Sub

    Private Sub VaiAlSegno(avanti As Boolean)
        Dim pos As Double = PosizioneCorrente()
        Dim trovato As Double = -1.0
        If avanti Then
            For Each s As Double In _p.Segni
                If s > pos + 0.001 Then
                    trovato = s
                    Exit For
                End If
            Next
        Else
            For i As Integer = _p.Segni.Count - 1 To 0 Step -1
                If _p.Segni(i) < pos - 0.001 Then
                    trovato = _p.Segni(i)
                    Exit For
                End If
            Next
        End If
        If trovato < 0.0 Then
            AiutoMusica.Messaggio(If(avanti, "Non ci sono segni più avanti.", "Non ci sono segni più indietro."))
            Return
        End If
        ImpostaPosizione(trovato)
        AggiornaRiga()
        AiutoMusica.Annuncia("Segno a " & ProgettoVideo.TestoTempo(trovato))
    End Sub

    ' ---- audio -------------------------------------------------------------------------------------------------

    Private Sub ScegliBaseMusicale()
        _selettoreFile.Scegli(SelettoreFile.EstensioniAudio,
                              Sub(percorso As String)
                                  AiutoRevisione.ChiediTesto("Volume della base musicale in percento, da 1 a 100", _p.VolumeBase.ToString(),
                                                             Sub(testo As String)
                                                                 Dim volume As Integer
                                                                 If Not Integer.TryParse(If(testo, "").Trim(), volume) OrElse volume < 1 OrElse volume > 100 Then
                                                                     AiutoMusica.Messaggio("Il volume va da 1 a 100 per cento.")
                                                                     Return
                                                                 End If
                                                                 _p.BaseMusicale = percorso
                                                                 _p.VolumeBase = volume
                                                                 AudioCambiato()
                                                                 AiutoMusica.Annuncia("Base musicale " & Path.GetFileNameWithoutExtension(percorso) & " aggiunta. Si ripete per tutto il video e si sente senza coprirlo")
                                                             End Sub)
                              End Sub)
    End Sub

    Private Sub ScegliAudioSostitutivo()
        _selettoreFile.Scegli(SelettoreFile.EstensioniAudio,
                              Sub(percorso As String)
                                  _p.AudioSostitutivo = percorso
                                  AudioCambiato()
                                  AiutoMusica.Annuncia("L'audio del video è sostituito con " & Path.GetFileNameWithoutExtension(percorso))
                              End Sub)
    End Sub

    Private Sub ChiediVolumeOriginale()
        AiutoRevisione.ChiediTesto("Volume dell'audio originale in percento, da 0 a 400", _p.VolumeAudioOriginale.ToString(),
                                   Sub(testo As String)
                                       Dim volume As Integer
                                       If Not Integer.TryParse(If(testo, "").Trim(), volume) OrElse volume < 0 OrElse volume > 400 Then
                                           AiutoMusica.Messaggio("Il volume va da 0 a 400 per cento.")
                                           Return
                                       End If
                                       _p.VolumeAudioOriginale = volume
                                       AudioCambiato()
                                       AiutoMusica.Annuncia("Volume dell'audio originale " & volume.ToString() & " per cento")
                                   End Sub)
    End Sub

    Private Sub MenuVoiceover()
        MostraScelte("Voiceover", New List(Of KeyValuePair(Of String, Action)) From {
                     Voce("Registra con il microfono dal punto corrente", Sub() IniziaRegistrazioneVoce()),
                     Voce("Usa un file audio dal punto corrente", Sub() ScegliFileVoce())})
    End Sub

    Private Sub ScegliFileVoce()
        Dim inizio As Double = PosizioneCorrente()
        _selettoreFile.Scegli(SelettoreFile.EstensioniAudio,
                              Sub(percorso As String)
                                  Dim durata As Double = 0.0
                                  Try
                                      Using lettore As New NAudio.Wave.AudioFileReader(percorso)
                                          durata = lettore.TotalTime.TotalSeconds
                                      End Using
                                  Catch ex As Exception
                                      AiutoMusica.Messaggio("Non è stato possibile aprire il file audio: " & ex.Message)
                                      Return
                                  End Try
                                  _p.Voci.Add(New VoceFuoricampo() With {.Percorso = percorso, .Inizio = inizio, .Durata = durata})
                                  AudioCambiato()
                                  AiutoMusica.Annuncia("Voiceover aggiunto a " & ProgettoVideo.TestoTempo(inizio) & ", dura " & ProgettoVideo.TestoTempo(durata))
                              End Sub)
    End Sub

    ' ---- inquadratura e colori ---------------------------------------------------------------------------------

    Private Sub MenuCorrezione()
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        voci.Add(Voce("Tutto in automatico", Sub() CalcolaCorrezione(True, True)))
        voci.Add(Voce("Solo i colori", Sub() CalcolaCorrezione(True, False)))
        voci.Add(Voce("Solo l'inquadratura", Sub() CalcolaCorrezione(False, True)))
        If _p.Correzione.Attiva Then voci.Add(Voce("Togli la correzione automatica", Sub()
                                                                                          _p.Correzione = New CorrezioneAutomatica()
                                                                                          AiutoMusica.Annuncia("Correzione automatica tolta")
                                                                                      End Sub))
        MostraScelte("Sistema inquadratura e colori", voci)
    End Sub

    Private Async Sub CalcolaCorrezione(colori As Boolean, inquadratura As Boolean)
        Try
            Dim c As CorrezioneAutomatica = Await WGProcessoInCorso.EseguiAsync("Sto analizzando il video, attendere", Function() AnalisiVideo.CalcolaAsync(_p, colori, inquadratura))
            If c Is Nothing OrElse Not c.Attiva Then
                AiutoMusica.Messaggio("Il video non ha bisogno di correzioni.")
                Return
            End If
            _p.Correzione = c
            Dim t As New System.Text.StringBuilder("Correzione calcolata ")
            t.Append(If(c.ConIntelligenzaArtificiale, "con l'intelligenza artificiale. ", "dal programma. "))
            If c.Ritaglio <> "" Then t.Append("Tolgo i bordi neri. ")
            If c.Stabilizza Then t.Append("Stabilizzo l'inquadratura. ")
            If c.Colori Then t.Append("Bilancio luminosità, contrasto, saturazione e colori. ")
            t.Append("Si vede nel video esportato.")
            AiutoMusica.Annuncia(t.ToString())
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.CalcolaCorrezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            AiutoMusica.Messaggio("Non è stato possibile analizzare il video: " & ex.Message)
        End Try
    End Sub

    ' ---- vai a, elimina --------------------------------------------------------------------------------------

    ''' <summary>Legge un tempo scritto come secondi (90 o 90,5) oppure come minuti:secondi (1:30) oppure ore:minuti:secondi.</summary>
    Private Shared Function LeggiTempo(testo As String, ByRef secondi As Double) As Boolean
        Dim parti() As String = If(testo, "").Trim().Replace(","c, "."c).Split(":"c)
        If parti.Length < 1 OrElse parti.Length > 3 Then Return False
        Dim totale As Double = 0.0
        For Each parte As String In parti
            Dim v As Double
            If Not Double.TryParse(parte.Trim(), NumberStyles.Float, Cultura, v) OrElse v < 0 Then Return False
            totale = totale * 60.0 + v
        Next
        secondi = totale
        Return True
    End Function

    Private Sub ChiediPunto()
        AiutoRevisione.ChiediTesto("Punto del video, in secondi oppure minuti e secondi come 1:30", "",
                                   Sub(testo As String)
                                       Dim s As Double
                                       If Not LeggiTempo(testo, s) OrElse s > _p.Durata + 0.001 Then
                                           AiutoMusica.Messaggio("Il punto dev'essere dentro il video, che dura " & ProgettoVideo.TestoTempo(_p.Durata) & ".")
                                           Return
                                       End If
                                       ImpostaPosizione(s)
                                       AggiornaRiga()
                                       AiutoMusica.Annuncia(ProgettoVideo.TestoTempo(PosizioneCorrente()))
                                   End Sub)
    End Sub

    Private Sub MenuElimina()
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        For Each taglio As Intervallo In _p.Tagli.ToList()
            Dim t As Intervallo = taglio
            voci.Add(Voce("Taglio da " & ProgettoVideo.TestoTempo(t.Inizio) & " a " & ProgettoVideo.TestoTempo(t.Fine), Sub()
                                                                                                                         _p.Tagli.Remove(t)
                                                                                                                         Eliminata("Taglio")
                                                                                                                     End Sub))
        Next
        For Each ev As EffettoApplicato In _p.EffettiVisivi.ToList()
            Dim e As EffettoApplicato = ev
            voci.Add(Voce(If(e.Id = "testo", "Testo " & e.Testo, "Effetto visivo " & e.Definizione.Nome) & " a " & ProgettoVideo.TestoTempo(e.Inizio), Sub()
                                                                                                                   _p.EffettiVisivi.Remove(e)
                                                                                                                   Eliminata("Effetto visivo")
                                                                                                               End Sub))
        Next
        For Each es As EffettoApplicato In _p.EffettiSonori.ToList()
            Dim e As EffettoApplicato = es
            voci.Add(Voce("Effetto audio " & e.Definizione.Nome & " a " & ProgettoVideo.TestoTempo(e.Inizio), Sub()
                                                                                                                  _p.EffettiSonori.Remove(e)
                                                                                                                  Eliminata("Effetto audio")
                                                                                                              End Sub))
        Next
        For Each vo As VoceFuoricampo In _p.Voci.ToList()
            Dim v As VoceFuoricampo = vo
            voci.Add(Voce("Voiceover a " & ProgettoVideo.TestoTempo(v.Inizio), Sub()
                                                                                    _p.Voci.Remove(v)
                                                                                    Eliminata("Voiceover")
                                                                                End Sub))
        Next
        For Each sl As Intervallo In _p.Silenzi.ToList()
            Dim s As Intervallo = sl
            voci.Add(Voce("Silenzio dell'audio originale da " & ProgettoVideo.TestoTempo(s.Inizio) & " a " & ProgettoVideo.TestoTempo(s.Fine), Sub()
                                                                                                                                                       _p.Silenzi.Remove(s)
                                                                                                                                                       Eliminata("Silenzio dell'audio originale")
                                                                                                                                                   End Sub))
        Next
        For Each sg As Double In _p.Segni.ToList()
            Dim segno As Double = sg
            voci.Add(Voce("Segno a " & ProgettoVideo.TestoTempo(segno), Sub()
                                                                            _p.Segni.Remove(segno)
                                                                            Eliminata("Segno")
                                                                        End Sub))
        Next
        If _p.Velocita <> 1.0 Then voci.Add(Voce("Velocità del video", Sub()
                                                                           _p.Velocita = 1.0
                                                                           Eliminata("Velocità del video")
                                                                       End Sub))
        If _p.Rotazione <> 0 Then voci.Add(Voce("Rotazione del video", Sub()
                                                                           _p.Rotazione = 0
                                                                           Eliminata("Rotazione del video")
                                                                       End Sub))
        If _p.FormatoRitaglio <> "" Then voci.Add(Voce("Ritaglio dell'inquadratura", Sub()
                                                                                         _p.FormatoRitaglio = ""
                                                                                         Eliminata("Ritaglio dell'inquadratura")
                                                                                     End Sub))
        If _p.DissolvenzaAudio > 0 Then voci.Add(Voce("Dissolvenza dell'audio", Sub()
                                                                                    _p.DissolvenzaAudio = 0
                                                                                    Eliminata("Dissolvenza dell'audio")
                                                                                End Sub))
        If _p.NormalizzaAudio Then voci.Add(Voce("Livellamento del volume", Sub()
                                                                                _p.NormalizzaAudio = False
                                                                                Eliminata("Livellamento del volume")
                                                                            End Sub))
        If _p.BaseMusicale <> "" Then voci.Add(Voce("Base musicale " & Path.GetFileNameWithoutExtension(_p.BaseMusicale), Sub()
                                                                                                                              _p.BaseMusicale = ""
                                                                                                                              Eliminata("Base musicale")
                                                                                                                          End Sub))
        If _p.AudioSostitutivo <> "" Then voci.Add(Voce("Audio sostitutivo " & Path.GetFileNameWithoutExtension(_p.AudioSostitutivo), Sub()
                                                                                                                                         _p.AudioSostitutivo = ""
                                                                                                                                         Eliminata("Audio sostitutivo")
                                                                                                                                     End Sub))
        If _p.Correzione.Attiva Then voci.Add(Voce("Correzione automatica", Sub()
                                                                                _p.Correzione = New CorrezioneAutomatica()
                                                                                Eliminata("Correzione automatica")
                                                                            End Sub))
        If voci.Count = 0 Then
            AiutoMusica.Messaggio("Non ci sono modifiche da eliminare.")
            Return
        End If
        MostraScelte("Elimina una modifica", voci)
    End Sub

    Private Sub Eliminata(cosa As String)
        AudioCambiato()
        AggiornaRiga()
        AiutoMusica.Annuncia("Eliminato: " & cosa)
    End Sub

End Class
