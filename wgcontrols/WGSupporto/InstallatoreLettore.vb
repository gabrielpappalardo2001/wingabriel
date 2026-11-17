Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

''' <summary>
''' L'installazione del lettore di schermo: si scarica wglettore.zip da www.wingabriel.net oppure si usa la copia
''' accanto a WinGabriel. La procedura guidata chiede se usarlo in modo portatile o installarlo nel sistema.
''' Nel sistema WGLettore.exe /installa:sistema chiede la conferma di amministratore, copia il lettore in
''' Programmi\WGLettore e registra il servizio per la schermata di accesso, di blocco e del controllo account
''' utente. Se NVDA è registrato sul controllo account utente, viene tolto da lì. In uso portatile i file restano
''' nella loro cartella (se arrivano da internet, in una cartella dell'utente) e il lettore parte all'accesso.
''' La conferma di amministratore è quella di Windows, che nessun lettore legge: si dà con Invio, perché il pulsante Sì è
''' quello predefinito.
''' </summary>
Friend NotInheritable Class InstallatoreLettore

    Friend Const IndirizzoArchivio As String = "https://www.wingabriel.net/download/wglettore.zip"
    Private Const DimensioneMassima As Long = 200L * 1024L * 1024L

    Private Sub New()
    End Sub

    Friend Shared Sub MostraMenu()
        Try
            Dim m As New WGMenu()
            m.aggiungi("Scarica da internet e installa il lettore", Sub() ChiediModalita(AddressOf ScaricaEInstalla))
            m.aggiungi("Installa il lettore che è accanto a WinGabriel", Sub() ChiediModalita(AddressOf InstallaDaCartella))
            m.aggiungi("Togli il lettore dalla schermata di accesso e dal controllo account utente", Sub() Togli())
            m.avvia("Installazione del lettore di schermo")
        Catch ex As Exception
            Messaggio("Non è stato possibile aprire il menu dell'installazione.")
        End Try
    End Sub

    Private Shared Sub Messaggio(testo As String)
        Dim m As New WGMessaggi()
        m.Messaggio(testo)
        m.ShowDialog()
    End Sub

    Private Shared Function EsitoInTesto(codice As Integer, installa As Boolean, Optional portatile As Boolean = False) As String
        Select Case codice
            Case 0
                If Not installa Then Return "Fatto. Il lettore non parte più su quelle schermate."
                If portatile Then Return "Fatto. Il lettore resta nella sua cartella e parte quando accedi a Windows. Non va sul controllo account utente."
                Return "Fatto. Il lettore è installato: parte anche sulla schermata di accesso, su quella di blocco e sul controllo account utente. Se NVDA era registrato lì, è stato tolto."
            Case -5
                Return "Non è stata scelta una modalità: non è cambiato niente."
            Case 1223
                Return "La conferma di amministratore non è stata data: non è cambiato niente."
            Case -2
                Return "Non è stato possibile scaricare il lettore da www.wingabriel.net. Controlla la connessione e riprova."
            Case -3
                Return "Il file scaricato non contiene il lettore di schermo."
            Case -4
                Return "Il programma del lettore, WGLettore.exe, non si trova accanto a WinGabriel."
            Case Else
                Return "Non è stato possibile completare l'operazione. Codice " & codice.ToString() & "."
        End Select
    End Function

    Private Shared Sub ChiediModalita(azione As Action(Of String))
        Dim m As New WGMenu()
        m.aggiungi("Installazione nel sistema. Il lettore va in Programmi, parte all'accesso, sul blocco e sul controllo account utente. Se NVDA è lì, viene tolto.", Sub() azione("sistema"))
        m.aggiungi("Uso portatile. Il lettore resta nella sua cartella e parte quando accedi. Non va sul controllo account utente.", Sub() azione("portatile"))
        m.avvia("Come vuoi usare il lettore di schermo?")
    End Sub

    Private Shared Async Sub ScaricaEInstalla(modalita As String)
        Try
            Dim avviso As String = If(modalita = "portatile",
                                      "Scarico il lettore di schermo e lo preparo per l'uso portatile.",
                                      "Scarico il lettore di schermo e lo installo." & If(AvvioAmministratore.Elevato(), "", " Windows chiederà una conferma: premi Invio."))
            Dim codice As Integer = Await WGProcessoInCorso.EseguiAsync(avviso, Function() Task.Run(Function() ScaricaEInstallaInterno(modalita)))
            Messaggio(EsitoInTesto(codice, True, modalita = "portatile"))
        Catch ex As Exception
            Messaggio("Non è stato possibile installare il lettore.")
        End Try
    End Sub

    Private Shared Async Sub InstallaDaCartella(modalita As String)
        Try
            Dim eseguibile As String = LettoreEsterno.PercorsoEseguibile()
            If eseguibile = "" Then
                Messaggio(EsitoInTesto(-4, True, modalita = "portatile"))
                Return
            End If
            Dim portatile As Boolean = modalita = "portatile"
            Dim avviso As String = If(portatile, "Preparo il lettore per l'uso portatile.", "Installo il lettore di schermo." & If(AvvioAmministratore.Elevato(), "", " Windows chiederà una conferma: premi Invio."))
            Dim argomento As String = If(portatile, "/installa:portatile", "/installa:sistema")
            Dim codice As Integer = Await WGProcessoInCorso.EseguiAsync(avviso, Function() Task.Run(Function() Esegui(eseguibile, argomento)))
            Messaggio(EsitoInTesto(codice, True, portatile))
        Catch ex As Exception
            Messaggio("Non è stato possibile installare il lettore.")
        End Try
    End Sub

    Private Shared Async Sub Togli()
        Try
            Dim eseguibile As String = LettoreEsterno.PercorsoEseguibile()
            If eseguibile = "" Then
                Messaggio(EsitoInTesto(-4, False))
                Return
            End If
            Dim codice As Integer = Await WGProcessoInCorso.EseguiAsync("Tolgo il lettore dalla schermata di accesso e dal controllo account utente.",
                                                                        Function() Task.Run(Function() Esegui(eseguibile, "/disinstalla")))
            Messaggio(EsitoInTesto(codice, False))
        Catch ex As Exception
            Messaggio("Non è stato possibile togliere il lettore.")
        End Try
    End Sub

    Private Shared Function Esegui(eseguibile As String, argomenti As String) As Integer
        Try
            Dim info As New ProcessStartInfo(eseguibile, argomenti)
            info.UseShellExecute = False
            info.WorkingDirectory = Path.GetDirectoryName(eseguibile)
            Using processo As Process = Process.Start(info)
                processo.WaitForExit()
                Return processo.ExitCode
            End Using
        Catch
            Return -1
        End Try
    End Function

    ' Scarica e scompatta l'archivio, poi installa. Restituisce il codice di uscita dell'installazione oppure un codice negativo.
    ' In uso portatile i file estratti si copiano in una cartella dell'utente, perché la cartella di lavoro viene cancellata.
    Private Shared Function ScaricaEInstallaInterno(modalita As String) As Integer
        ' Non si estrae in %TEMP%: i criteri di controllo delle applicazioni di Windows possono impedire di eseguire lì un programma non firmato.
        Dim cartella As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "installazione-lettore", Guid.NewGuid().ToString("N"))
        Try
            Directory.CreateDirectory(cartella)
            Dim archivio As String = Path.Combine(cartella, "wglettore.zip")
            If Not Scarica(archivio) Then Return -2

            Dim destinazione As String = Path.Combine(cartella, "contenuto")
            Directory.CreateDirectory(destinazione)
            If Not Scompatta(archivio, destinazione) Then Return -3

            Dim eseguibile As String = Nothing
            For Each percorso As String In Directory.GetFiles(destinazione, "WGLettore.exe", SearchOption.AllDirectories)
                eseguibile = percorso
                Exit For
            Next
            If eseguibile Is Nothing Then Return -3
            If modalita = "portatile" Then
                Dim stabile As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WGLettore")
                CopiaCartella(Path.GetDirectoryName(eseguibile), stabile)
                eseguibile = Path.Combine(stabile, "WGLettore.exe")
                If Not File.Exists(eseguibile) Then Return -3
            End If
            Dim argomento As String = If(modalita = "portatile", "/installa:portatile", "/installa:sistema")
            Return Esegui(eseguibile, argomento)
        Catch
            Return -2
        Finally
            Try
                Directory.Delete(cartella, True)
            Catch
            End Try
        End Try
    End Function

    Private Shared Sub CopiaCartella(origine As String, destinazione As String)
        Directory.CreateDirectory(destinazione)
        For Each percorso As String In Directory.GetFiles(origine)
            Dim arrivo As String = Path.Combine(destinazione, Path.GetFileName(percorso))
            If Not String.Equals(Path.GetFullPath(percorso), Path.GetFullPath(arrivo), StringComparison.OrdinalIgnoreCase) Then
                File.Copy(percorso, arrivo, True)
            End If
        Next
        For Each sotto As String In Directory.GetDirectories(origine)
            CopiaCartella(sotto, Path.Combine(destinazione, Path.GetFileName(sotto)))
        Next
    End Sub

    Private Shared Function Scarica(percorsoFile As String) As Boolean
        Try
            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(5)
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-InstallatoreLettore/1.0")
                Using risposta As HttpResponseMessage = client.GetAsync(IndirizzoArchivio, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult()
                    If Not risposta.IsSuccessStatusCode Then Return False
                    Dim lunghezza As Long? = risposta.Content.Headers.ContentLength
                    If lunghezza.HasValue AndAlso lunghezza.Value > DimensioneMassima Then Return False
                    Using sorgente As Stream = risposta.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                        Using destinazione As FileStream = File.Create(percorsoFile)
                            Dim blocco(81919) As Byte
                            Dim totale As Long = 0
                            Dim letti As Integer = sorgente.Read(blocco, 0, blocco.Length)
                            While letti > 0
                                totale += letti
                                If totale > DimensioneMassima Then Return False
                                destinazione.Write(blocco, 0, letti)
                                letti = sorgente.Read(blocco, 0, blocco.Length)
                            End While
                        End Using
                    End Using
                End Using
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

    ' Scompatta con la protezione dai percorsi che escono dalla cartella di destinazione.
    Private Shared Function Scompatta(archivio As String, destinazione As String) As Boolean
        Try
            Dim radice As String = Path.GetFullPath(destinazione).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Using zip As ZipArchive = ZipFile.OpenRead(archivio)
                For Each voce As ZipArchiveEntry In zip.Entries
                    If voce.FullName.EndsWith("/", StringComparison.Ordinal) OrElse voce.Name = "" Then Continue For
                    Dim percorso As String = Path.GetFullPath(Path.Combine(destinazione, voce.FullName.Replace("/"c, Path.DirectorySeparatorChar)))
                    If Not percorso.StartsWith(radice, StringComparison.OrdinalIgnoreCase) Then Return False
                    Directory.CreateDirectory(Path.GetDirectoryName(percorso))
                    voce.ExtractToFile(percorso, True)
                Next
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

End Class
