Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.Security.Principal
Imports System.Xml
Imports Microsoft.Win32

''' <summary>
''' WinGabriel chiede di partire con i privilegi di amministratore, così il suo lettore di schermo e le altre funzioni possono lavorare anche
''' con le applicazioni avviate come amministratore. Se la conferma di Windows non viene data, WinGabriel parte lo stesso, senza privilegi.
''' La scelta si cambia dal menu Avvio delle impostazioni.
''' </summary>
Public Module AvvioAmministratore

    Public Const ID_CONFIG As String = "avvio_amministratore"

    ''' <summary>
    ''' Vero se, quando il programma è partito, il file di configurazione non c'era ancora: è una installazione nuova. Chi aveva già
    ''' WinGabriel il file ce l'ha, quindi per lui certe cose, come l'installazione di eSpeak, non cambiano.
    ''' </summary>
    Public ProfiloNuovoAllAvvio As Boolean

    Public Sub RegistraProfiloNuovo()
        Try
            ProfiloNuovoAllAvvio = Not File.Exists(PercorsoConfigurazione())
        Catch
            ProfiloNuovoAllAvvio = False
        End Try
    End Sub

    ''' <summary>
    ''' Vero se l'avvio come amministratore è attivo (lo è finché non si disattiva). Legge il config.xml direttamente, senza usare
    ''' wgcontrols.dll, perché a questo punto dell'avvio la libreria può non essere ancora stata scaricata.
    ''' </summary>
    Public Function Richiesto() As Boolean
        Try
            Dim percorso As String = PercorsoConfigurazione()
            If Not File.Exists(percorso) Then Return True
            Dim doc As New XmlDocument()
            doc.XmlResolver = Nothing
            doc.Load(percorso)
            For Each nodo As XmlNode In doc.DocumentElement.ChildNodes
                Dim attributo As XmlAttribute = If(nodo.Attributes Is Nothing, Nothing, nodo.Attributes("id"))
                If attributo IsNot Nothing AndAlso attributo.Value = ID_CONFIG Then
                    Dim valore As XmlNode = nodo.SelectSingleNode("Valore[1]")
                    Dim testo As String = If(valore IsNot Nothing, valore.InnerText, nodo.InnerText)
                    Return Not String.Equals(testo.Trim(), "false", StringComparison.OrdinalIgnoreCase)
                End If
            Next
        Catch
        End Try
        Return True
    End Function

    Public Function PercorsoConfigurazione() As String
        Dim locale As String = WGDb.WGPercorsiApplicazione.PercorsoConfig()
        Try
            Dim cartella As String = AppDomain.CurrentDomain.BaseDirectory.TrimEnd("\"c)
            Dim installazione As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinGabriel").TrimEnd("\"c)
            Dim installato As Boolean = String.Equals(cartella, installazione, StringComparison.OrdinalIgnoreCase)
            If Not installato AndAlso File.Exists(locale) Then Return locale
            Using chiave As RegistryKey = Registry.CurrentUser.OpenSubKey("Software\WinGabriel")
                If chiave IsNot Nothing Then
                    Dim puntato As String = Convert.ToString(chiave.GetValue("PercorsoConfigurazione"))
                    If Not String.IsNullOrWhiteSpace(puntato) AndAlso File.Exists(puntato) Then Return puntato
                End If
            End Using
        Catch
        End Try
        Return locale
    End Function

    ''' <summary>
    ''' Un argomento tra virgolette secondo le regole di Windows: le barre rovesciate prima di una virgoletta (o della virgoletta di chiusura)
    ''' si raddoppiano e le virgolette interne si fanno precedere da una barra. Così "C:\cartella\" non si mangia l'argomento dopo.
    ''' </summary>
    Private Function ArgomentoTraVirgolette(valore As String) As String
        Try
            Dim testo As String = If(valore, "")
            Dim sb As New System.Text.StringBuilder("""")
            Dim barre As Integer = 0
            For Each c As Char In testo
                If c = "\"c Then
                    barre += 1
                ElseIf c = """"c Then
                    sb.Append("\"c, barre * 2 + 1).Append(""""c)
                    barre = 0
                Else
                    sb.Append("\"c, barre).Append(c)
                    barre = 0
                End If
            Next
            sb.Append("\"c, barre * 2).Append(""""c)
            Return sb.ToString()
        Catch
            Return """"""
        End Try
    End Function

    ''' <summary>
    ''' Vero se questo processo può avviare (o caricare) il file senza dare i suoi privilegi a chi non li ha. Senza privilegi di amministratore
    ''' è sempre vero. Con i privilegi, solo per i file in cartelle che un utente normale non può modificare (Programmi, Windows) o nella
    ''' cartella di WinGabriel stesso: un programma in %LOCALAPPDATA% o indicato in HKCU potrebbe averlo messo lì chiunque giri come utente,
    ''' e partirebbe come amministratore senza nessuna conferma.
    ''' </summary>
    Public Function EseguibileAffidabile(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If Not Elevato() Then Return True
            Dim completo As String = Path.GetFullPath(percorso)
            Dim radici As New List(Of String) From {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                AppDomain.CurrentDomain.BaseDirectory}
            For Each radice As String In radici
                If String.IsNullOrWhiteSpace(radice) Then Continue For
                Dim r As String = Path.GetFullPath(radice).TrimEnd("\"c) & "\"
                If completo.StartsWith(r, StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Catch
            Return False
        End Try
    End Function

    Public Function Elevato() As Boolean
        Try
            Using identita As WindowsIdentity = WindowsIdentity.GetCurrent()
                Return New WindowsPrincipal(identita).IsInRole(WindowsBuiltInRole.Administrator)
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Rilancia questo programma con gli argomenti dati, chiedendo i privilegi di amministratore, e aspetta. Restituisce il codice di uscita (1223 se la conferma è negata).</summary>
    Public Function EseguiComeAmministratore(argomenti As String) As Integer
        Try
            Dim info As New ProcessStartInfo(Application.ExecutablePath, argomenti)
            info.UseShellExecute = True
            info.Verb = "runas"
            Using processo As Process = Process.Start(info)
                processo.WaitForExit()
                Return processo.ExitCode
            End Using
        Catch ex As Win32Exception
            Return 1223
        Catch
            Return 1
        End Try
    End Function

    ''' <summary>
    ''' Rilancia WinGabriel con la richiesta di amministratore. Restituisce vero se il nuovo WinGabriel è partito: questo allora deve
    ''' uscire. Restituisce falso se non serve, se la conferma è stata negata o se non è riuscito: si prosegue senza privilegi.
    ''' </summary>
    Public Function RilanciaSeServe() As Boolean
        Try
            If Elevato() OrElse Not Richiesto() Then Return False
            Dim argomenti As String() = Environment.GetCommandLineArgs()
            ' Con /nonelevare (che il nuovo processo riceve) non si chiede più niente: evita ogni giro a vuoto.
            For i As Integer = 1 To argomenti.Length - 1
                If String.Equals(argomenti(i), "/nonelevare", StringComparison.OrdinalIgnoreCase) Then Return False
            Next
            ' Se WinGabriel è installato (attività pianificata che punta a questa copia) si avvia da lì: privilegi di amministratore senza
            ' alcuna richiesta di conferma, da qualunque via sia partito (icona, avvio automatico, riavvio da parte di WGService).
            If IntegrazioneWindows.AttivitaPuntaQui() AndAlso IntegrazioneWindows.AvviaTramiteAttivita() Then Return True
            Dim riga As New System.Text.StringBuilder("/nonelevare")
            For i As Integer = 1 To argomenti.Length - 1
                riga.Append(" "c).Append(ArgomentoTraVirgolette(argomenti(i)))
            Next
            Dim info As New ProcessStartInfo(Application.ExecutablePath, riga.ToString())
            info.UseShellExecute = True
            info.Verb = "runas"
            info.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            Process.Start(info)
            Return True
        Catch ex As Win32Exception
            Return False
        Catch
            Return False
        End Try
    End Function

End Module
