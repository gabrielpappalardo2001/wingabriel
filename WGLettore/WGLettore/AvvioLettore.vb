Imports System.Diagnostics
Imports System.IO
Imports System.Net
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Xml
Imports WGLettoreSchermo

''' <summary>
''' Quel che succede all'avvio, fuori dal desktop sicuro: chiude gli altri lettori, chiede l'icona sul desktop
''' e controlla aggiornamenti e file mancanti.
''' </summary>
Friend NotInheritable Class AvvioLettore

    Private Const ChiaveElenco As String = "Wg#7fA1-92Lm-Elenco-File-2026!"
    Private Const AppNome As String = "wglettore"
    Private Const UrlBase As String = "https://www.wingabriel.net/wglettore/"
    Private Const NomeAggiornatore As String = "WGLettoreAggiornatore.exe"
    Private Const IdIconaDesktop As String = "lettore_schermo_icona_desktop"
    Private Const NomeAttivitaLettore As String = "WGLettore"

    Private Sub New()
    End Sub

    Friend Shared Sub ChiudiAltriLettori()
        For Each nome As String In New String() {"nvda", "nvda_slave", "jfw", "Narrator", "Supernova", "SystemAccess", "ZoomText", "fsreader"}
            Try
                For Each processo As Process In Process.GetProcessesByName(nome)
                    Try
                        processo.Kill()
                        processo.WaitForExit(2000)
                    Catch exErrore1 As Exception
                        Trace.WriteLine("AvvioLettore: " & exErrore1.Message)
                    Finally
                        processo.Dispose()
                    End Try
                Next
            Catch exErrore2 As Exception
                Trace.WriteLine("AvvioLettore: " & exErrore2.Message)
            End Try
        Next
    End Sub

    Private Shared Function PercorsoIcona() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Lettore di schermo.lnk")
    End Function

    Friend Shared Function IconaPresente() As Boolean
        Return File.Exists(PercorsoIcona())
    End Function

    Friend Shared Function ImpostaIcona(presente As Boolean) As Boolean
        Try
            If Not presente Then
                If File.Exists(PercorsoIcona()) Then File.Delete(PercorsoIcona())
                Return True
            End If
            Dim shell As Object = CreateObject("WScript.Shell")
            Dim collegamento As Object = shell.CreateShortcut(PercorsoIcona())
            If Installazione.AttivitaPianificataEsiste() Then
                collegamento.TargetPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe")
                collegamento.Arguments = "/Run /TN " & NomeAttivitaLettore
                collegamento.WindowStyle = 7
            Else
                collegamento.TargetPath = Application.ExecutablePath
            End If
            collegamento.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath)
            collegamento.IconLocation = Application.ExecutablePath & ",0"
            collegamento.Description = "Lettore di schermo"
            collegamento.Save()
            System.Runtime.InteropServices.Marshal.ReleaseComObject(collegamento)
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell)
            Return True
        Catch exErrore3 As Exception
            Trace.WriteLine("AvvioLettore: " & exErrore3.Message)
            Return False
        End Try
    End Function

    Friend Shared Sub ChiediIconaDesktop()
        Try
            If ConfigLettore.Esiste(IdIconaDesktop) Then
                If ConfigLettore.Leggi(IdIconaDesktop, "") = "true" AndAlso Not IconaPresente() Then ImpostaIcona(True)
                Return
            End If
            Dim risposta As DialogResult = MessageBox.Show("Vuoi un'icona del lettore di schermo sul desktop?", "Lettore di schermo",
                                                           MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
            Dim si As Boolean = risposta = DialogResult.Yes
            ConfigLettore.Scrivi(IdIconaDesktop, If(si, "true", "false"))
            If si Then
                If Not ImpostaIcona(True) Then
                    MessageBox.Show("Non è stato possibile creare l'icona sul desktop.", "Lettore di schermo", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
            End If
        Catch exErrore4 As Exception
            Trace.WriteLine("AvvioLettore: " & exErrore4.Message)
        End Try
    End Sub

    Friend Shared Function AlternaIcona() As Boolean
        Dim voglio As Boolean = Not IconaPresente()
        If ImpostaIcona(voglio) Then
            ConfigLettore.Scrivi(IdIconaDesktop, If(voglio, "true", "false"))
            Return True
        End If
        Return False
    End Function

    Private NotInheritable Class VoceElenco
        Public Percorso As String
        Public Dimensione As Long = -1
        Public Impronta As String = ""
    End Class

    Friend NotInheritable Class EsitoControllo
        Public Riuscito As Boolean
        Public Riparati As Integer
        Public Falliti As Integer
        Public AggiornamentoDisponibile As Boolean
    End Class

    Private Shared Function LeggiElenco() As List(Of VoceElenco)
        ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
        Dim parametri As String = "chiave=" & Uri.EscapeDataString(ChiaveElenco) & "&app=" & Uri.EscapeDataString(AppNome)
        Dim dati As Byte() = Encoding.UTF8.GetBytes(parametri)
        Dim richiesta As HttpWebRequest = CType(WebRequest.Create(UrlBase & "DownloadFile.aspx"), HttpWebRequest)
        richiesta.Method = "POST"
        richiesta.ContentType = "application/x-www-form-urlencoded"
        richiesta.UserAgent = "WGLettore/1.0"
        richiesta.Timeout = 20000
        richiesta.ReadWriteTimeout = 20000
        richiesta.KeepAlive = False
        richiesta.ContentLength = dati.Length
        Using flusso As Stream = richiesta.GetRequestStream()
            flusso.Write(dati, 0, dati.Length)
        End Using
        Dim testo As String
        Using risposta As HttpWebResponse = CType(richiesta.GetResponse(), HttpWebResponse)
            Using lettore As New StreamReader(risposta.GetResponseStream(), Encoding.UTF8)
                testo = lettore.ReadToEnd()
            End Using
        End Using
        Dim elenco As New List(Of VoceElenco)()
        If String.IsNullOrWhiteSpace(testo) Then Return elenco
        Dim documento As New XmlDocument()
        documento.XmlResolver = Nothing
        documento.LoadXml(testo)
        For Each nodo As XmlNode In documento.SelectNodes("//Voce")
            Dim attributo As XmlAttribute = nodo.Attributes("percorso")
            If attributo Is Nothing OrElse String.IsNullOrWhiteSpace(attributo.Value) Then Continue For
            Dim voce As New VoceElenco()
            voce.Percorso = attributo.Value.Replace("/"c, "\"c).TrimStart("\"c)
            If voce.Percorso.Contains("..") OrElse Path.IsPathRooted(voce.Percorso) Then Continue For
            Dim d As Long
            If nodo.Attributes("dimensione") IsNot Nothing AndAlso Long.TryParse(nodo.Attributes("dimensione").Value, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, d) Then voce.Dimensione = d
            If nodo.Attributes("hash") IsNot Nothing Then voce.Impronta = nodo.Attributes("hash").Value.Trim()
            elenco.Add(voce)
        Next
        Return elenco
    End Function

    Private Shared Function ImprontaFile(percorso As String) As String
        Using sha As SHA256 = SHA256.Create()
            Using flusso As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Dim hash As Byte() = sha.ComputeHash(flusso)
                Dim sb As New StringBuilder()
                For Each b As Byte In hash
                    sb.Append(b.ToString("x2"))
                Next
                Return sb.ToString()
            End Using
        End Using
    End Function

    Private Shared Function Corrisponde(percorso As String, voce As VoceElenco) As Boolean
        Dim info As New FileInfo(percorso)
        If voce.Dimensione >= 0 AndAlso info.Length <> voce.Dimensione Then Return False
        If voce.Impronta <> "" AndAlso Not String.Equals(ImprontaFile(percorso), voce.Impronta, StringComparison.OrdinalIgnoreCase) Then Return False
        Return True
    End Function

    Private Shared Function Scarica(voce As VoceElenco, cartella As String) As Boolean
        Try
            Dim destinazione As String = Path.Combine(cartella, voce.Percorso)
            Directory.CreateDirectory(Path.GetDirectoryName(destinazione))
            Dim temporaneo As String = destinazione & ".nuovo"
            If File.Exists(temporaneo) Then File.Delete(temporaneo)
            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(UrlBase & voce.Percorso.Replace("\"c, "/"c)), HttpWebRequest)
            richiesta.UserAgent = "WGLettore/1.0"
            richiesta.Timeout = 60000
            richiesta.ReadWriteTimeout = 60000
            Using risposta As HttpWebResponse = CType(richiesta.GetResponse(), HttpWebResponse)
                Using remoto As Stream = risposta.GetResponseStream()
                    Using locale As New FileStream(temporaneo, FileMode.Create, FileAccess.Write, FileShare.None, 65536)
                        remoto.CopyTo(locale, 65536)
                        locale.Flush(True)
                    End Using
                End Using
            End Using
            If Not Corrisponde(temporaneo, voce) Then
                File.Delete(temporaneo)
                Return False
            End If
            If File.Exists(destinazione) Then
                File.Replace(temporaneo, destinazione, Nothing, True)
            Else
                File.Move(temporaneo, destinazione)
            End If
            Return True
        Catch exErrore5 As Exception
            Trace.WriteLine("AvvioLettore: " & exErrore5.Message)
            Return False
        End Try
    End Function

    Friend Shared Function Controlla() As EsitoControllo
        Dim esito As New EsitoControllo()
        Try
            Dim cartella As String = Path.GetDirectoryName(Application.ExecutablePath)
            Dim elenco As List(Of VoceElenco) = LeggiElenco()
            If elenco.Count = 0 Then Return esito
            esito.Riuscito = True
            For Each voce As VoceElenco In elenco
                Try
                    Dim percorso As String = Path.Combine(cartella, voce.Percorso)
                    If Not File.Exists(percorso) Then
                        If Scarica(voce, cartella) Then esito.Riparati += 1 Else esito.Falliti += 1
                    ElseIf Not Corrisponde(percorso, voce) Then
                        esito.AggiornamentoDisponibile = True
                    End If
                Catch exErrore6 As Exception
                    Trace.WriteLine("AvvioLettore: " & exErrore6.Message)
                    esito.Falliti += 1
                End Try
            Next
        Catch exErrore7 As Exception
            Trace.WriteLine("AvvioLettore: " & exErrore7.Message)
            esito.Riuscito = False
        End Try
        Return esito
    End Function

    Friend Shared Function AvviaAggiornatore() As Boolean
        Try
            Dim cartella As String = Path.GetDirectoryName(Application.ExecutablePath)
            Dim percorso As String = Path.Combine(cartella, NomeAggiornatore)
            If Not File.Exists(percorso) Then
                Dim voce As New VoceElenco()
                voce.Percorso = NomeAggiornatore
                If Not Scarica(voce, cartella) Then Return False
            End If
            Dim info As New ProcessStartInfo(percorso)
            info.WorkingDirectory = cartella
            info.UseShellExecute = False
            Process.Start(info)
            Return True
        Catch exErrore8 As Exception
            Trace.WriteLine("AvvioLettore: " & exErrore8.Message)
            Return False
        End Try
    End Function

    Friend Shared Sub ControllaInSottofondo(ui As Control, alzaChiusura As Action)
        Dim filo As New Thread(Sub()
                                   Try
                                       Thread.Sleep(8000)
                                       Dim esito As EsitoControllo = Controlla()
                                       If Not esito.Riuscito Then Return
                                       If esito.Riparati > 0 OrElse esito.AggiornamentoDisponibile Then
                                           ui.BeginInvoke(New Action(Sub() Proponi(esito, alzaChiusura)))
                                       End If
                                   Catch exErrore9 As Exception
                                       Trace.WriteLine("AvvioLettore: " & exErrore9.Message)
                                   End Try
                               End Sub)
        filo.IsBackground = True
        filo.Name = "WGLettoreAggiornamenti"
        filo.Start()
    End Sub

    Friend Shared Sub Proponi(esito As EsitoControllo, alzaChiusura As Action)
        Try
            If esito.Riparati > 0 Then
                MessageBox.Show("Sono stati recuperati " & esito.Riparati.ToString() & " file del lettore di schermo che mancavano. Alcuni saranno usati al prossimo avvio.", "Lettore di schermo", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
            If Not esito.AggiornamentoDisponibile Then Return
            Dim risposta As DialogResult = MessageBox.Show("È disponibile un aggiornamento del lettore di schermo. Vuoi installarlo adesso? Il lettore verrà chiuso e si riaprirà da solo.", "Lettore di schermo",
                                                           MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
            If risposta <> DialogResult.Yes Then Return
            If AvviaAggiornatore() Then
                alzaChiusura()
            Else
                MessageBox.Show("Non è stato possibile avviare il programma di aggiornamento.", "Lettore di schermo", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
        Catch exErrore10 As Exception
            Trace.WriteLine("AvvioLettore: " & exErrore10.Message)
        End Try
    End Sub

End Class
