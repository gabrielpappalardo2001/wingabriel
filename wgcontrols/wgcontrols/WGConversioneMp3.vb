Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports HtmlAgilityPack
Imports UglyToad.PdfPig
Imports WGStampante

Public Class WGConversioneMp3
    Private ReadOnly _log As New Logger()

    Private Const CHIAVE_CONFIG_MP3_VOCE As String = "mp3_voce"
    Private Const CHIAVE_CONFIG_MP3_VELOCITA As String = "mp3_velocita"
    Private Const CHIAVE_CONFIG_MP3_VOLUME As String = "mp3_volume"

    Private Const LUNGHEZZA_MASSIMA_PEZZO As Integer = 10000
    Private Const ATTESA_MASSIMA_SENZA_PROGRESSO_MS As Long = 180000
    Private Const ATTESA_MASSIMA_PRIMA_RISPOSTA_MS As Long = 15000
    Private Const TENTATIVI_MASSIMI As Integer = 3
    Private Const ATTESA_PRIMA_DI_RIPROVARE_MS As Integer = 3000
    Private Const ES_CONTINUOUS As UInteger = &H80000000UI
    Private Const ES_SYSTEM_REQUIRED As UInteger = &H1UI
    Private Const ES_AWAYMODE_REQUIRED As UInteger = &H40UI
    Private Const INTERVALLO_CONTROLLO_MS As Integer = 50
    Private Const INTERVALLO_SORVEGLIANZA_MS As Integer = 500

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function QueryUnbiasedInterruptTime(ByRef tempoNonPolarizzato As ULong) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetThreadExecutionState(stato As UInteger) As UInteger
    End Function

    Private ReadOnly _lockLavoro As New Object()
    Private _idLavoroCorrente As String = ""
    Private _percentualeCorrente As Integer = 0
    Private _lavoroConcluso As Boolean = False
    Private _lavoroRiuscito As Boolean = False
    Private _messaggioEsito As String = ""
    Private _tickUltimoSegnale As Long = 0
    Private _agganciatoAgliEventi As Boolean = False
    Private _primaRispostaRicevuta As Boolean = False
    Private _stalloRilevato As Boolean = False
    Private _motivoStallo As String = ""
    Private _sorveglianzaAttiva As Boolean = False

    Public ReadOnly Property PercentualeCorrente As Integer
        Get
            Try
                SyncLock _lockLavoro
                    Return _percentualeCorrente
                End SyncLock
            Catch ex As Exception
                _log.ScriviLog("WGConversioneMp3.PercentualeCorrente: " & ex.Message)
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property ConversioneInCorso As Boolean
        Get
            Try
                SyncLock _lockLavoro
                    Return Not String.IsNullOrEmpty(_idLavoroCorrente) AndAlso Not _lavoroConcluso
                End SyncLock
            Catch ex As Exception
                _log.ScriviLog("WGConversioneMp3.ConversioneInCorso: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Private Function LeggiConfigStringa(chiave As String, valoreDefault As String) As String
        Try
            Dim config As New WGConfig()
            If Not config.EsisteId(chiave) Then
                config.AggiungiValore(chiave, valoreDefault)
                Return valoreDefault
            End If
            Dim valore As String = config.LeggiValore(chiave)
            If String.IsNullOrWhiteSpace(valore) Then Return valoreDefault
            Return valore
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.LeggiConfigStringa (" & chiave & "): " & ex.Message)
            Return valoreDefault
        End Try
    End Function

    Private Function LeggiConfigIntero(chiave As String, valoreDefault As Integer) As Integer
        Try
            Dim valore As Integer = valoreDefault
            If Not Integer.TryParse(LeggiConfigStringa(chiave, valoreDefault.ToString()), valore) Then Return valoreDefault
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Return valore
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.LeggiConfigIntero (" & chiave & "): " & ex.Message)
            Return valoreDefault
        End Try
    End Function

    Private Function TickCorrenti() As Long
        Try
            Dim tempoNonPolarizzato As ULong = 0
            If QueryUnbiasedInterruptTime(tempoNonPolarizzato) Then
                Return CLng(tempoNonPolarizzato \ 10000UL)
            End If
            Return Environment.TickCount
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.TickCorrenti: " & ex.Message)
            Return Environment.TickCount
        End Try
    End Function

    Private Sub AgganciaEventiSintesi()
        Try
            If _agganciatoAgliEventi Then Return
            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi Is Nothing Then Return
            AddHandler sintesi.OnProgressoMp3, AddressOf GestisciProgressoMp3
            AddHandler sintesi.OnFineConversioneMp3, AddressOf GestisciFineConversioneMp3
            _agganciatoAgliEventi = True
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.AgganciaEventiSintesi: " & ex.Message)
        End Try
    End Sub

    Private Sub SganciaEventiSintesi()
        Try
            If Not _agganciatoAgliEventi Then Return
            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi IsNot Nothing Then
                RemoveHandler sintesi.OnProgressoMp3, AddressOf GestisciProgressoMp3
                RemoveHandler sintesi.OnFineConversioneMp3, AddressOf GestisciFineConversioneMp3
            End If
            _agganciatoAgliEventi = False
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.SganciaEventiSintesi: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciProgressoMp3(idLavoro As String, percentuale As Integer)
        Try
            SyncLock _lockLavoro
                If String.IsNullOrEmpty(_idLavoroCorrente) OrElse idLavoro <> _idLavoroCorrente Then Return
                Dim valore As Integer = percentuale
                If valore < 0 Then valore = 0
                If valore > 100 Then valore = 100
                _percentualeCorrente = valore
                _primaRispostaRicevuta = True
                _tickUltimoSegnale = TickCorrenti()
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.GestisciProgressoMp3: " & ex.Message)
        End Try
    End Sub

    Private Sub GestisciFineConversioneMp3(idLavoro As String, esito As Boolean, messaggio As String)
        Try
            SyncLock _lockLavoro
                If String.IsNullOrEmpty(_idLavoroCorrente) OrElse idLavoro <> _idLavoroCorrente Then Return
                _lavoroRiuscito = esito
                _messaggioEsito = messaggio
                _lavoroConcluso = True
                _primaRispostaRicevuta = True
                _tickUltimoSegnale = TickCorrenti()
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.GestisciFineConversioneMp3: " & ex.Message)
        End Try
    End Sub

    Public Function ConvertiTesto(testo As String, percorsoMp3 As String, Optional annulla As Func(Of Boolean) = Nothing, Optional progresso As Action(Of Integer, String) = Nothing) As Boolean
        Try
            SetThreadExecutionState(ES_CONTINUOUS Or ES_SYSTEM_REQUIRED Or ES_AWAYMODE_REQUIRED)
        Catch exStato As Exception
            _log.ScriviLog("WGConversioneMp3.ConvertiTesto (stato esecuzione): " & exStato.Message)
        End Try

        Try
            Dim tentativo As Integer = 1
            While tentativo <= TENTATIVI_MASSIMI
                Dim riprovabile As Boolean = False

                If EseguiConversione(testo, percorsoMp3, annulla, progresso, riprovabile) Then Return True
                If Not riprovabile Then Return False
                If annulla IsNot Nothing AndAlso annulla() Then Return False

                _log.ScriviLog("WGConversioneMp3.ConvertiTesto: tentativo " & tentativo.ToString() & " non riuscito, riavvio della conversione")

                Try
                    Dim sintesiRipristino As sintesivocale = sintesivocale.GetInstance()
                    If sintesiRipristino IsNot Nothing Then sintesiRipristino.AttendiCanalePronto(30000)
                Catch exCanale As Exception
                    _log.ScriviLog("WGConversioneMp3.ConvertiTesto (ripristino canale): " & exCanale.Message)
                End Try

                Thread.Sleep(ATTESA_PRIMA_DI_RIPROVARE_MS)
                tentativo += 1
            End While

            Return False
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.ConvertiTesto: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        Finally
            Try
                SetThreadExecutionState(ES_CONTINUOUS)
            Catch exStato As Exception
                _log.ScriviLog("WGConversioneMp3.ConvertiTesto (ripristino stato esecuzione): " & exStato.Message)
            End Try
        End Try
    End Function

    Private Sub AvviaSorveglianzaCanale()
        Try
            SyncLock _lockLavoro
                _stalloRilevato = False
                _motivoStallo = ""
                _sorveglianzaAttiva = True
            End SyncLock

            Dim thread As New Thread(AddressOf CicloSorveglianzaCanale)
            thread.IsBackground = True
            thread.Start()
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.AvviaSorveglianzaCanale: " & ex.Message)
        End Try
    End Sub

    Private Sub FermaSorveglianzaCanale()
        Try
            SyncLock _lockLavoro
                _sorveglianzaAttiva = False
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.FermaSorveglianzaCanale: " & ex.Message)
        End Try
    End Sub

    Private Sub CicloSorveglianzaCanale()
        Try
            While True
                Dim attiva As Boolean = False
                Dim concluso As Boolean = False
                Dim rispostaRicevuta As Boolean = False
                Dim tickSegnale As Long = 0

                SyncLock _lockLavoro
                    attiva = _sorveglianzaAttiva
                    concluso = _lavoroConcluso
                    rispostaRicevuta = _primaRispostaRicevuta
                    tickSegnale = _tickUltimoSegnale
                End SyncLock

                If Not attiva OrElse concluso Then Exit While

                Dim attesaTrascorsa As Long = TickCorrenti() - tickSegnale

                If Not rispostaRicevuta AndAlso attesaTrascorsa > ATTESA_MASSIMA_PRIMA_RISPOSTA_MS Then
                    SyncLock _lockLavoro
                        _motivoStallo = "il motore di sintesi non ha risposto al comando di avvio"
                        _stalloRilevato = True
                    End SyncLock
                    Exit While
                End If

                If rispostaRicevuta AndAlso attesaTrascorsa > ATTESA_MASSIMA_SENZA_PROGRESSO_MS Then
                    SyncLock _lockLavoro
                        _motivoStallo = "nessun segnale dal motore per " & (attesaTrascorsa \ 1000).ToString() & " secondi"
                        _stalloRilevato = True
                    End SyncLock
                    Exit While
                End If

                Thread.Sleep(INTERVALLO_SORVEGLIANZA_MS)
            End While
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.CicloSorveglianzaCanale: " & ex.Message)
        End Try
    End Sub

    Private Function EseguiConversione(testo As String, percorsoMp3 As String, annulla As Func(Of Boolean), progresso As Action(Of Integer, String), ByRef riprovabile As Boolean) As Boolean
        Dim idLavoro As String = ""
        riprovabile = False
        Try
            If String.IsNullOrWhiteSpace(percorsoMp3) Then
                _log.ScriviLog("WGConversioneMp3.EseguiConversione: percorso mp3 vuoto")
                Return False
            End If
            If String.IsNullOrWhiteSpace(testo) Then
                _log.ScriviLog("WGConversioneMp3.EseguiConversione: testo vuoto")
                Return False
            End If

            Dim percorsoFinale As String = percorsoMp3
            If Not percorsoFinale.ToLowerInvariant().EndsWith(".mp3") Then
                percorsoFinale &= ".mp3"
            End If

            Dim cartella As String = Path.GetDirectoryName(percorsoFinale)
            If Not String.IsNullOrEmpty(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If

            Dim pezzi As List(Of String) = SpezzaTesto(testo)
            If pezzi Is Nothing OrElse pezzi.Count = 0 Then
                _log.ScriviLog("WGConversioneMp3.EseguiConversione: nessun pezzo prodotto")
                Return False
            End If

            Dim voce As String = LeggiConfigStringa(CHIAVE_CONFIG_MP3_VOCE, "")
            Dim velocita As Integer = LeggiConfigIntero(CHIAVE_CONFIG_MP3_VELOCITA, 50)
            Dim volume As Integer = LeggiConfigIntero(CHIAVE_CONFIG_MP3_VOLUME, 80)

            idLavoro = "mp3" & Guid.NewGuid().ToString("N")

            AgganciaEventiSintesi()

            SyncLock _lockLavoro
                _idLavoroCorrente = idLavoro
                _percentualeCorrente = 0
                _lavoroConcluso = False
                _lavoroRiuscito = False
                _messaggioEsito = ""
                _primaRispostaRicevuta = False
                _tickUltimoSegnale = TickCorrenti()
            End SyncLock

            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi Is Nothing Then
                _log.ScriviLog("WGConversioneMp3.EseguiConversione: sintesi vocale non disponibile")
                Return False
            End If

            If Not sintesi.AvviaConversioneMp3(idLavoro, voce, velocita, volume, percorsoFinale, pezzi) Then
                riprovabile = True
                _log.ScriviLog("WGConversioneMp3.EseguiConversione: avvio conversione rifiutato")
                Return False
            End If

            AvviaSorveglianzaCanale()

            If progresso IsNot Nothing Then progresso(0, "Sintesi e conversione in mp3")

            Dim ultimaPercentualeNotificata As Integer = -1

            While True
                Dim concluso As Boolean = False
                Dim riuscito As Boolean = False
                Dim percentuale As Integer = 0

                SyncLock _lockLavoro
                    concluso = _lavoroConcluso
                    riuscito = _lavoroRiuscito
                    percentuale = _percentualeCorrente
                End SyncLock

                If progresso IsNot Nothing AndAlso percentuale <> ultimaPercentualeNotificata Then
                    ultimaPercentualeNotificata = percentuale
                    progresso(percentuale, "Sintesi e conversione in mp3")
                End If

                If concluso Then
                    If Not riuscito Then
                        Dim messaggio As String = ""
                        SyncLock _lockLavoro
                            messaggio = _messaggioEsito
                        End SyncLock
                        riprovabile = (messaggio <> "conversione annullata")
                        _log.ScriviLog("WGConversioneMp3.EseguiConversione: conversione fallita (" & messaggio & ")")
                        Return False
                    End If
                    Exit While
                End If

                If annulla IsNot Nothing AndAlso annulla() Then
                    sintesi.AnnullaConversioneMp3(idLavoro)
                    _log.ScriviLog("WGConversioneMp3.EseguiConversione: conversione annullata")
                    Return False
                End If

                Dim stallo As Boolean = False
                Dim motivo As String = ""
                SyncLock _lockLavoro
                    stallo = _stalloRilevato
                    motivo = _motivoStallo
                End SyncLock

                If stallo Then
                    sintesi.AnnullaConversioneMp3(idLavoro)
                    riprovabile = True
                    _log.ScriviLog("WGConversioneMp3.EseguiConversione: " & motivo)
                    Return False
                End If

                Thread.Sleep(INTERVALLO_CONTROLLO_MS)
            End While

            If Not File.Exists(percorsoFinale) Then
                _log.ScriviLog("WGConversioneMp3.EseguiConversione: file mp3 non trovato al termine")
                Return False
            End If

            If progresso IsNot Nothing Then progresso(100, "Completata")
            Return True
        Catch ex As Exception
            riprovabile = True
            _log.ScriviLog("WGConversioneMp3.EseguiConversione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                If Not String.IsNullOrEmpty(idLavoro) Then
                    Dim sintesiAnnulla As sintesivocale = sintesivocale.GetInstance()
                    If sintesiAnnulla IsNot Nothing Then sintesiAnnulla.AnnullaConversioneMp3(idLavoro)
                End If
            Catch exAnnulla As Exception
                _log.ScriviLog("WGConversioneMp3.EseguiConversione (annullamento): " & exAnnulla.Message)
            End Try
            Return False
        Finally
            FermaSorveglianzaCanale()
            Try
                SyncLock _lockLavoro
                    _idLavoroCorrente = ""
                End SyncLock
            Catch exPulizia As Exception
                _log.ScriviLog("WGConversioneMp3.EseguiConversione (pulizia): " & exPulizia.Message)
            End Try
        End Try
    End Function

    Public Function ConvertiFile(percorsoFile As String, percorsoMp3 As String, Optional annulla As Func(Of Boolean) = Nothing, Optional progresso As Action(Of Integer, String) = Nothing) As Boolean
        Try
            Dim testo As String = EstraiTestoDaFile(percorsoFile)
            If String.IsNullOrWhiteSpace(testo) Then
                _log.ScriviLog("WGConversioneMp3.ConvertiFile: nessun testo estratto")
                Return False
            End If
            Return ConvertiTesto(testo, percorsoMp3, annulla, progresso)
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.ConvertiFile: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub AnnullaConversione()
        Try
            Dim idLavoro As String = ""
            SyncLock _lockLavoro
                idLavoro = _idLavoroCorrente
            End SyncLock
            If String.IsNullOrEmpty(idLavoro) Then Return
            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi IsNot Nothing Then sintesi.AnnullaConversioneMp3(idLavoro)
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.AnnullaConversione: " & ex.Message)
        End Try
    End Sub

    Public Sub ChiudiConversione()
        Try
            AnnullaConversione()
            SganciaEventiSintesi()
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.ChiudiConversione: " & ex.Message)
        End Try
    End Sub

    Public Function EstraiTestoDaFile(percorsoFile As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                Return ""
            End If
            Dim estensione As String = Path.GetExtension(percorsoFile).ToLowerInvariant()
            If estensione = ".docx" Then
                Return WGWord.EstraiTestoCompleto(percorsoFile)
            End If
            If estensione = ".pdf" Then
                Return EstraiTestoPdf(percorsoFile)
            End If
            If estensione = ".html" OrElse estensione = ".htm" Then
                Return EstraiTestoHtml(percorsoFile)
            End If
            If estensione = ".rtf" Then
                Return EstraiTestoRtf(File.ReadAllText(percorsoFile))
            End If
            Return File.ReadAllText(percorsoFile)
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.EstraiTestoDaFile: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function EstraiBlocchiDaFile(percorsoFile As String) As List(Of WGBloccoStampa)
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                Return New List(Of WGBloccoStampa)()
            End If
            Dim estensione As String = Path.GetExtension(percorsoFile).ToLowerInvariant()
            If estensione = ".docx" Then
                Return WGWord.EstraiBlocchiStampaDaFile(percorsoFile)
            End If
            Dim testo As String = EstraiTestoDaFile(percorsoFile)
            Dim blocco As New WGBloccoStampa()
            blocco.Tipo = "testo"
            blocco.Testo = testo
            Return New List(Of WGBloccoStampa) From {blocco}
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.EstraiBlocchiDaFile: " & ex.Message)
            Return New List(Of WGBloccoStampa)()
        End Try
    End Function

    Public Function DescriviVoceConfigurata() As String
        Try
            Dim valoreConfigurato As String = LeggiConfigStringa(CHIAVE_CONFIG_MP3_VOCE, "")
            If String.IsNullOrWhiteSpace(valoreConfigurato) Then Return ""

            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi Is Nothing Then Return valoreConfigurato

            Dim elenco As List(Of Sintesi) = sintesi.OttieniSintesiSapi()
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return valoreConfigurato

            For Each voce As Sintesi In elenco
                Try
                    If String.Equals(voce.Id, valoreConfigurato, StringComparison.OrdinalIgnoreCase) Then Return voce.Nome
                Catch exVoce As Exception
                    _log.ScriviLog("WGConversioneMp3.DescriviVoceConfigurata (voce): " & exVoce.Message)
                End Try
            Next

            Dim indice As Integer = -1
            If Integer.TryParse(valoreConfigurato.Trim(), indice) Then
                If indice >= 0 AndAlso indice < elenco.Count Then Return elenco(indice).Nome
            End If

            Return valoreConfigurato
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.DescriviVoceConfigurata: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub AnteprimaVoceMp3(idVoceConfigurata As String, indiceVoceFallback As Integer, ratePercentuale As Integer, volume As Integer, Optional testo As String = "Questa è un'anteprima della voce.")
        Try
            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi Is Nothing Then Return

            Dim valoreVoce As String = idVoceConfigurata
            If String.IsNullOrWhiteSpace(valoreVoce) AndAlso indiceVoceFallback >= 0 Then
                valoreVoce = indiceVoceFallback.ToString()
            End If

            sintesi.AnteprimaVoceMp3(valoreVoce, ratePercentuale, volume, testo)
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.AnteprimaVoceMp3: " & ex.Message)
        End Try
    End Sub

    Public Sub FermaAnteprimaVoceMp3()
        Try
            Dim sintesi As sintesivocale = sintesivocale.GetInstance()
            If sintesi Is Nothing Then Return
            sintesi.FermaAnteprimaVoceMp3()
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.FermaAnteprimaVoceMp3: " & ex.Message)
        End Try
    End Sub

    Private Function SpezzaTesto(testo As String) As List(Of String)
        Dim pezzi As New List(Of String)()
        Try
            If String.IsNullOrEmpty(testo) Then Return pezzi
            Dim normalizzato As String = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            Dim paragrafi As String() = normalizzato.Split(New Char() {ControlChars.Lf}, StringSplitOptions.None)
            Dim accumulo As New StringBuilder()
            For Each paragrafo As String In paragrafi
                If accumulo.Length + paragrafo.Length > LUNGHEZZA_MASSIMA_PEZZO AndAlso accumulo.Length > 0 Then
                    pezzi.Add(accumulo.ToString())
                    accumulo.Clear()
                End If
                If accumulo.Length > 0 Then accumulo.Append(" ")
                accumulo.Append(paragrafo)
            Next
            If accumulo.Length > 0 Then pezzi.Add(accumulo.ToString())
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.SpezzaTesto: " & ex.Message)
            pezzi.Clear()
            pezzi.Add(testo)
        End Try
        Return pezzi
    End Function

    Private Function EstraiTestoPdf(percorso As String) As String
        Try
            Dim sb As New StringBuilder()
            Using documento = PdfDocument.Open(percorso)
                For Each pagina In documento.GetPages()
                    sb.AppendLine(pagina.Text)
                Next
            End Using
            Return sb.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.EstraiTestoPdf: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function EstraiTestoHtml(percorso As String) As String
        Try
            Dim doc As New HtmlAgilityPack.HtmlDocument()
            doc.Load(percorso)
            Return doc.DocumentNode.InnerText
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.EstraiTestoHtml: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function EstraiTestoRtf(contenuto As String) As String
        Try
            If String.IsNullOrEmpty(contenuto) Then Return ""
            Dim senzaGruppi As String = Regex.Replace(contenuto, "\\'[0-9a-fA-F]{2}", " ")
            senzaGruppi = Regex.Replace(senzaGruppi, "\\[a-zA-Z]+-?\d* ?", " ")
            senzaGruppi = senzaGruppi.Replace("{", " ").Replace("}", " ")
            Return Regex.Replace(senzaGruppi, "\s+", " ").Trim()
        Catch ex As Exception
            _log.ScriviLog("WGConversioneMp3.EstraiTestoRtf: " & ex.Message)
            Return contenuto
        End Try
    End Function
End Class