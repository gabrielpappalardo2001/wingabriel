' ============================================================================================
' SessioneEditorVideo.Esporta - Alt sinistro: l'esportazione del video montato
' ============================================================================================
'
' Si scelgono la risoluzione (l'originale o una più bassa), il formato, la qualità, i fotogrammi al secondo e l'audio; poi la
' cartella (con il selettore di file di WinGabriel, che parte dalle unità del computer) e il nome. Durante il lavoro c'è la barra
' della percentuale e, ogni tanto, si legge a voce quanto manca, circa.
' ============================================================================================
Imports System
Imports wgcontrols
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Threading.Tasks

Partial Public NotInheritable Class SessioneEditorVideo

    Private Shared Function LarghezzaAltezzaDopo(p As ProgettoVideo, lato As Integer, ByRef larghezza As Integer, ByRef altezza As Integer) As Boolean
        Dim w, h As Integer
        p.DimensioniVisibili(w, h)
        larghezza = w
        altezza = h
        Dim corto As Integer = Math.Min(w, h)
        If lato <= 0 OrElse lato >= corto Then Return False
        Dim fattore As Double = lato / corto
        larghezza = CInt(Math.Round(w * fattore / 2.0)) * 2
        altezza = CInt(Math.Round(h * fattore / 2.0)) * 2
        Return True
    End Function

    Friend Sub AvviaEsportazione()
        Try
            If _esportando OrElse _chiusa Then Return
            If _registrandoVoce OrElse _inTaglio Then
                AiutoMusica.Messaggio("Prima termina il taglio o la registrazione del voiceover.")
                Return
            End If
            If _inAscolto Then FermaAscolto()
            If _p.PezziTenuti().Count = 0 Then
                AiutoMusica.Messaggio("Hai tagliato tutto il video: non c'è niente da esportare.")
                Return
            End If
            Dim imp As New ImpostazioniEsportazione()
            ScegliRisoluzione(imp)
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.AvviaEsportazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub ScegliRisoluzione(imp As ImpostazioniEsportazione)
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        Dim ow, oh As Integer
        _p.DimensioniVisibili(ow, oh)
        voci.Add(Voce("Originale, " & ow.ToString() & " per " & oh.ToString(), Sub()
                                                                                                    imp.LatoCorto = 0
                                                                                                    ScegliFormato(imp)
                                                                                                End Sub))
        For Each lato As Integer In New Integer() {2160, 1440, 1080, 720, 480, 360, 240}
            Dim l As Integer = lato
            Dim w, h As Integer
            If LarghezzaAltezzaDopo(_p, l, w, h) Then
                voci.Add(Voce(l.ToString() & " sul lato corto, " & w.ToString() & " per " & h.ToString(), Sub()
                                                                                                             imp.LatoCorto = l
                                                                                                             ScegliFormato(imp)
                                                                                                         End Sub))
            End If
        Next
        MostraScelte("Risoluzione del video", voci)
    End Sub

    Private Sub ScegliFormato(imp As ImpostazioniEsportazione)
        MostraScelte("Formato del video", New List(Of KeyValuePair(Of String, Action)) From {
                     Voce("Mp4 con H264, va bene dappertutto", Sub()
                                                                  imp.Formato = "mp4-h264"
                                                                  ScegliQualita(imp)
                                                              End Sub),
                     Voce("Mp4 con H265, file più piccolo", Sub()
                                                               imp.Formato = "mp4-h265"
                                                               ScegliQualita(imp)
                                                           End Sub),
                     Voce("Mkv con H264", Sub()
                                             imp.Formato = "mkv-h264"
                                             ScegliQualita(imp)
                                         End Sub)})
    End Sub

    Private Sub ScegliQualita(imp As ImpostazioniEsportazione)
        MostraScelte("Qualità del video", New List(Of KeyValuePair(Of String, Action)) From {
                     Voce("Media", Sub()
                                      imp.Qualita = 1
                                      ScegliFotogrammi(imp)
                                  End Sub),
                     Voce("Alta, file più grande", Sub()
                                                      imp.Qualita = 0
                                                      ScegliFotogrammi(imp)
                                                  End Sub),
                     Voce("Bassa, file più piccolo", Sub()
                                                        imp.Qualita = 2
                                                        ScegliFotogrammi(imp)
                                                    End Sub)})
    End Sub

    Private Sub ScegliFotogrammi(imp As ImpostazioniEsportazione)
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        voci.Add(Voce("Come l'originale, " & Math.Round(_p.Fps).ToString() & " al secondo", Sub()
                                                                                                imp.Fps = 0
                                                                                                ScegliAudio(imp)
                                                                                            End Sub))
        For Each f As Integer In New Integer() {60, 30, 25, 24, 15}
            Dim fps As Integer = f
            If fps < _p.Fps - 0.5 Then voci.Add(Voce(fps.ToString() & " fotogrammi al secondo", Sub()
                                                                                                    imp.Fps = fps
                                                                                                    ScegliAudio(imp)
                                                                                                End Sub))
        Next
        MostraScelte("Fotogrammi al secondo", voci)
    End Sub

    Private Sub ScegliAudio(imp As ImpostazioniEsportazione)
        Dim voci As New List(Of KeyValuePair(Of String, Action))()
        For Each kbit As Integer In New Integer() {192, 128, 256, 320}
            Dim k As Integer = kbit
            voci.Add(Voce(k.ToString() & " kilobit al secondo", Sub()
                                                                    imp.BitrateAudio = k
                                                                    ScegliCartella(imp)
                                                                End Sub))
        Next
        MostraScelte("Qualità dell'audio", voci)
    End Sub

    Private Sub ScegliCartella(imp As ImpostazioniEsportazione)
        _selettoreCartella = New SelettoreCartellaMusica()
        _selettoreCartella.Scegli(Sub(cartella As String) AiutoMusica.SuInterfaccia(Sub() ChiediNome(imp, cartella)))
    End Sub

    Private Sub ChiediNome(imp As ImpostazioniEsportazione, cartella As String)
        Dim predefinito As String = Path.GetFileNameWithoutExtension(_p.Sorgente) & " montato"
        AiutoRevisione.ChiediTesto("Nome del file", predefinito, Sub(testo As String)
                                                                     Dim nome As String = AiutoMusica.NomeFileSicuro(If(testo, "")).Trim()
                                                                     If nome = "" Then nome = AiutoMusica.NomeFileSicuro(predefinito)
                                                                     Dim percorso As String = Path.Combine(cartella, nome & imp.Estensione())
                                                                     Dim n As Integer = 2
                                                                     While File.Exists(percorso)
                                                                         percorso = Path.Combine(cartella, nome & " (" & n.ToString() & ")" & imp.Estensione())
                                                                         n += 1
                                                                     End While
                                                                     imp.Destinazione = percorso
                                                                     EseguiEsportazione(imp)
                                                                 End Sub)
    End Sub

    Private Async Sub EseguiEsportazione(imp As ImpostazioniEsportazione)
        Dim attesa As New WGProcessoInCorso()
        Try
            _esportando = True
            Dim cronometro As Stopwatch = Stopwatch.StartNew()
            Dim ultimaPercentuale As Integer = -1
            Dim ultimoAnnuncio As Long = -1
            attesa.Avvia("Sto esportando il video, attendere", False)
            Dim progetto As ProgettoVideo = _p
            Dim esito As String = Await Task.Run(Function() EsportazioneVideo.Esporta(progetto, imp,
                Sub(frazione As Double, mancano As Double)
                    Dim percentuale As Integer = CInt(Math.Floor(frazione * 100.0 / 5.0)) * 5
                    If percentuale <> ultimaPercentuale Then
                        ultimaPercentuale = percentuale
                        attesa.AggiornaPercentuale(percentuale)
                    End If
                    ' ogni mezzo minuto circa si dice quanto manca, appena c'è abbastanza lavoro fatto per stimarlo
                    If mancano >= 0.0 AndAlso (ultimoAnnuncio < 0 OrElse cronometro.ElapsedMilliseconds - ultimoAnnuncio >= 30000) Then
                        ultimoAnnuncio = cronometro.ElapsedMilliseconds
                        Dim voce As sintesivocale = sintesivocale.GetInstance()
                        If voce IsNot Nothing Then voce.LeggiTesto("Mancano " & ProgettoVideo.TestoTempoApprossimato(mancano), False, "Normale")
                    End If
                End Sub))
            Try
                attesa.Fine()
            Catch
            End Try
            _esportando = False
            If esito <> "" Then
                AiutoMusica.Messaggio(esito)
            Else
                Dim dimensione As Long = 0
                Try
                    dimensione = New FileInfo(imp.Destinazione).Length
                Catch
                End Try
                AiutoMusica.Messaggio("Video esportato in " & imp.Destinazione & ". Il file pesa " & TestoDimensione(dimensione) & ".")
            End If
        Catch ex As Exception
            _log.ScriviLog("SessioneEditorVideo.EseguiEsportazione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                attesa.Fine()
            Catch
            End Try
            _esportando = False
            AiutoMusica.Messaggio("Non è stato possibile esportare il video: " & ex.Message)
        Finally
            _esportando = False
            If _chiusa Then PulisciLavoro()
        End Try
    End Sub

    Private Shared Function TestoDimensione(byteTotali As Long) As String
        If byteTotali >= 1073741824L Then Return (byteTotali / 1073741824.0).ToString("0.0") & " gigabyte"
        If byteTotali >= 1048576L Then Return CInt(byteTotali / 1048576.0).ToString() & " megabyte"
        Return CInt(byteTotali / 1024.0).ToString() & " kilobyte"
    End Function

End Class
