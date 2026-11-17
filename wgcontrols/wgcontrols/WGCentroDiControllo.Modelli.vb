Imports System.IO
Imports System.Net.Http
Imports System.Threading

''' <summary>Come si estrae un file scaricato per un modello.</summary>
Public Enum TipoEstrazioneModello
    Nessuna
    Zip
    TarBz2
End Enum

''' <summary>Un file da scaricare per un modello. Si salva (ed eventualmente si estrae) in una cartella temporanea.</summary>
Public Class ElementoModello
    Public Property Indirizzo As String = ""
    ''' <summary>Il nome con cui il file si salva nella cartella temporanea.</summary>
    Public Property NomeFile As String = ""
    Public Property ImprontaSha256 As String = ""
    Public Property DimensioneMinima As Long = 0
    ''' <summary>Se il file è un archivio, si estrae in una sottocartella della cartella temporanea chiamata come il file senza estensione.</summary>
    Public Property Estrazione As TipoEstrazioneModello = TipoEstrazioneModello.Nessuna
End Class

Partial Public Class WGCentroDiControllo

    Private Class LavoroModelli
        Inherits LavoroBase

        Public Chiave As String = ""
        Public Elementi As New List(Of ElementoModello)()
        Public Installa As Func(Of String, String)
        Public AlTermine As Action(Of String)
        Public IndiceCorrente As Integer = 0
        Public Info As String = ""
        Public Estrazione As Boolean = False
        Public StatoDownload As StatoDownloadHttp
        Public TempoInizio As DateTime = DateTime.Now
    End Class

    ''' <summary>Mostra un avviso all'utente, da qualunque thread (il messaggio compare sul thread dell'interfaccia).</summary>
    Public Sub AvvisaUtente(testo As String)
        NotificaFineOperazione(testo)
    End Sub

    ''' <summary>Vero se è già in corso lo scaricamento di questi modelli.</summary>
    Public Function ScaricamentoModelliInCorso(chiave As String) As Boolean
        Try
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim modelli As LavoroModelli = TryCast(lavoro, LavoroModelli)
                If modelli IsNot Nothing AndAlso String.Equals(modelli.Chiave, chiave, StringComparison.OrdinalIgnoreCase) AndAlso LavoroInCorso(modelli) Then Return True
            Next
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.ScaricamentoModelliInCorso: " & ex.Message)
        End Try
        Return False
    End Function

    ''' <summary>
    ''' Scarica in sottofondo i file di un modello di intelligenza artificiale: il lavoro compare nel centro di controllo con la percentuale.
    ''' Riprende i download interrotti e controlla dimensione e impronta. Quando tutti i file sono pronti chiama installa, su un altro thread,
    ''' con la cartella temporanea che li contiene: installa li mette al loro posto e restituisce "" se è andato tutto bene, altrimenti il
    ''' motivo dell'errore. Alla fine alTermine riceve "" (riuscito) o il motivo del fallimento, sul thread dell'interfaccia. Un errore o un
    ''' annullamento vengono comunicati anche con un messaggio.
    ''' Restituisce falso se lo stesso scaricamento è già in corso.
    ''' </summary>
    Public Function AggiungiScaricamentoModelli(chiave As String, descrizione As String, elementi As List(Of ElementoModello), installa As Func(Of String, String), alTermine As Action(Of String)) As Boolean
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then
                If alTermine IsNot Nothing Then EseguiSuThreadInterfaccia(Sub() alTermine(""))
                Return True
            End If
            If ScaricamentoModelliInCorso(chiave) Then
                NotificaFineOperazione("Lo scaricamento di " & descrizione & " è già in corso: puoi seguirlo dal centro di controllo.")
                Return False
            End If
            Dim lavoro As New LavoroModelli()
            lavoro.Chiave = chiave
            lavoro.Elementi = elementi
            lavoro.Installa = installa
            lavoro.AlTermine = alTermine
            lavoro.Info = "preparazione"
            lavoro.StatoDownload = New StatoDownloadHttp(_log)
            lavoro.TempoInizio = DateTime.Now
            lavoro.Annulla = False
            RegistraLavoro(lavoro, "Scaricamento di " & descrizione, StatoOperazioneCorrente.ScaricamentoModelli)
            lavoro.InPreparazione = False
            lavoro.ThreadLavoro = New Thread(AddressOf EseguiScaricamentoModelli)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
            Return True
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.AggiungiScaricamentoModelli: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub EseguiScaricamentoModelli(parametro As Object)
        Dim lavoro As LavoroModelli = TryCast(parametro, LavoroModelli)
        Dim cartellaTemporanea As String = Path.Combine(Path.GetTempPath(), "wgmodelli_" & Guid.NewGuid().ToString("N"))
        Dim esito As String = ""
        Try
            If lavoro Is Nothing Then Return
            lavoro.TempoInizio = DateTime.Now
            lavoro.InEsecuzione = True
            Directory.CreateDirectory(cartellaTemporanea)

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(60)
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/2.0 (accessibilita; postmaster@wingabriel.net)")

                For indice As Integer = 0 To lavoro.Elementi.Count - 1
                    If lavoro.Annulla Then Exit For
                    Dim elemento As ElementoModello = lavoro.Elementi(indice)
                    lavoro.IndiceCorrente = indice
                    lavoro.Estrazione = False
                    lavoro.StatoDownload.Azzera(elemento.NomeFile)
                    lavoro.Info = "scarico " & elemento.NomeFile & ", file " & (indice + 1).ToString() & " di " & lavoro.Elementi.Count.ToString()

                    Dim destinazione As String = Path.Combine(cartellaTemporanea, elemento.NomeFile)
                    Directory.CreateDirectory(Path.GetDirectoryName(destinazione))
                    If Not ScaricaFileHttpConProgresso(client, elemento.Indirizzo, destinazione, lavoro.StatoDownload, lavoro, elemento.ImprontaSha256) Then
                        If lavoro.Annulla Then Exit For
                        esito = "Non sono riuscito a scaricare " & elemento.NomeFile & ". Controlla la connessione a internet e riprova."
                        Exit For
                    End If

                    If elemento.DimensioneMinima > 0 AndAlso New FileInfo(destinazione).Length < elemento.DimensioneMinima Then
                        esito = "Il file " & elemento.NomeFile & " scaricato non è completo."
                        Exit For
                    End If

                    If elemento.Estrazione <> TipoEstrazioneModello.Nessuna Then
                        lavoro.Estrazione = True
                        lavoro.Info = "estraggo " & elemento.NomeFile
                        esito = EstraiModello(destinazione, elemento.Estrazione, lavoro)
                        If esito <> "" Then Exit For
                        Try
                            File.Delete(destinazione)
                        Catch exCancella As Exception
                            _log.ScriviLog("WGCentroDiControllo.EseguiScaricamentoModelli (eliminazione archivio): " & exCancella.Message)
                        End Try
                    End If
                Next
            End Using

            If lavoro.Annulla Then
                esito = "annullato"
            ElseIf esito = "" AndAlso lavoro.Installa IsNot Nothing Then
                lavoro.Estrazione = False
                lavoro.Info = "installazione"
                lavoro.IndiceCorrente = lavoro.Elementi.Count
                esito = lavoro.Installa(cartellaTemporanea)
            End If
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.EseguiScaricamentoModelli: " & ex.Message & Environment.NewLine & ex.StackTrace)
            If esito = "" Then esito = "Lo scaricamento non è riuscito per un errore. Controlla il log."
        Finally
            Try
                lavoro.InEsecuzione = False
            Catch
            End Try
            Try
                If Directory.Exists(cartellaTemporanea) Then Directory.Delete(cartellaTemporanea, True)
            Catch exPulizia As Exception
                _log.ScriviLog("WGCentroDiControllo.EseguiScaricamentoModelli (pulizia): " & exPulizia.Message)
            End Try
            Try
                RimuoviLavoro(lavoro)
            Catch exRim As Exception
                _log.ScriviLog("WGCentroDiControllo.EseguiScaricamentoModelli (rimuovi): " & exRim.Message)
            End Try
        End Try

        Try
            Dim motivo As String = esito
            If motivo = "annullato" Then
                NotificaFineOperazione(lavoro.Descrizione & " annullato.")
            ElseIf motivo <> "" Then
                NotificaFineOperazione(motivo)
            End If
            If lavoro.AlTermine IsNot Nothing Then EseguiSuThreadInterfaccia(Sub() lavoro.AlTermine(If(motivo = "annullato", "annullato", motivo)))
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.EseguiScaricamentoModelli (esito): " & ex.Message)
        End Try
    End Sub

    ' Estrae un archivio scaricato in una cartella accanto, con il nome del file senza estensione. Restituisce "" se è riuscito.
    Private Function EstraiModello(archivio As String, tipo As TipoEstrazioneModello, lavoro As LavoroModelli) As String
        Try
            Dim cartella As String = Path.Combine(Path.GetDirectoryName(archivio), Path.GetFileNameWithoutExtension(archivio))
            If tipo = TipoEstrazioneModello.TarBz2 Then cartella = Path.Combine(Path.GetDirectoryName(archivio), Path.GetFileName(archivio).Split("."c)(0))
            If Directory.Exists(cartella) Then Directory.Delete(cartella, True)
            Directory.CreateDirectory(cartella)

            If tipo = TipoEstrazioneModello.Zip Then
                System.IO.Compression.ZipFile.ExtractToDirectory(archivio, cartella)
                Return ""
            End If

            Dim tar As String = Path.Combine(Environment.SystemDirectory, "tar.exe")
            If Not File.Exists(tar) Then Return "Per estrarre il modello serve tar.exe, presente in Windows 10 dalla versione 1803 e in Windows 11, che su questo computer manca."
            Dim avvio As New ProcessStartInfo(tar, "-xjf """ & archivio & """ -C """ & cartella & """")
            avvio.UseShellExecute = False
            avvio.CreateNoWindow = True
            avvio.RedirectStandardError = True
            avvio.RedirectStandardOutput = True
            Using processo As Process = Process.Start(avvio)
                Dim errori As System.Threading.Tasks.Task(Of String) = processo.StandardError.ReadToEndAsync()
                Dim uscita As System.Threading.Tasks.Task(Of String) = processo.StandardOutput.ReadToEndAsync()
                Dim secondi As Integer = 0
                While Not processo.WaitForExit(1000)
                    secondi += 1
                    If lavoro.Annulla Then
                        Try
                            processo.Kill()
                        Catch exKill As Exception
                            _log.ScriviLog("WGCentroDiControllo.EstraiModello (arresto di tar): " & exKill.Message)
                        End Try
                        Return ""
                    End If
                    If secondi > 30 * 60 Then
                        Try
                            processo.Kill()
                        Catch exKill As Exception
                            _log.ScriviLog("WGCentroDiControllo.EstraiModello (arresto di tar): " & exKill.Message)
                        End Try
                        Return "L'estrazione del modello non si è conclusa entro 30 minuti."
                    End If
                End While
                If processo.ExitCode <> 0 Then
                    _log.ScriviLog("WGCentroDiControllo.EstraiModello: tar è terminato con codice " & processo.ExitCode.ToString() & ": " & errori.Result)
                    Return "L'estrazione del modello non è riuscita."
                End If
            End Using
            Return ""
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.EstraiModello: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return "L'estrazione del modello non è riuscita: " & ex.Message
        End Try
    End Function

    Private Function PercentualeModelli(lavoro As LavoroModelli) As Integer
        Try
            Dim totale As Integer = Math.Max(1, lavoro.Elementi.Count)
            If lavoro.IndiceCorrente >= lavoro.Elementi.Count Then Return 99
            Dim parte As Double = 0
            If lavoro.Estrazione Then
                parte = 0.95
            ElseIf lavoro.StatoDownload IsNot Nothing AndAlso lavoro.StatoDownload.BytesTotali > 0 Then
                parte = Math.Min(0.95, lavoro.StatoDownload.BytesScaricati / CDbl(lavoro.StatoDownload.BytesTotali))
            End If
            Return CInt(Math.Max(0, Math.Min(99, (lavoro.IndiceCorrente + parte) * 100.0 / totale)))
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.PercentualeModelli: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function InfoModelli(lavoro As LavoroModelli) As String
        Dim righe As String = ""
        If lavoro.Info <> "" Then righe &= "Sto facendo: " & lavoro.Info & vbCrLf
        If lavoro.StatoDownload IsNot Nothing AndAlso lavoro.StatoDownload.BytesTotali > 0 AndAlso Not lavoro.Estrazione Then
            righe &= "File corrente: " & CInt(Math.Round(lavoro.StatoDownload.Percentuale(), 0)).ToString() & " per cento" & vbCrLf
            righe &= "Velocità: " & FormattaVelocita(lavoro.StatoDownload.VelocitaBps) & vbCrLf
        End If
        righe &= "Avanzamento totale: " & PercentualeModelli(lavoro).ToString() & " per cento"
        Return righe
    End Function
End Class
