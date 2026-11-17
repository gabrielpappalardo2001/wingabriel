Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Security.Principal
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports wgcontrols

' Pulizia del computer e manutenzione del sistema, con eliminazione di file temporanei, svuotamento del cestino e rimozione di
' duplicati.
Public Class WGPuliziaPC
    Private _log As New Logger()

    Private Const SHERB_NOCONFIRMATION As Integer = &H1
    Private Const SHERB_NOPROGRESSUI As Integer = &H2
    Private Const SHERB_NOSOUND As Integer = &H4
    Private Const ORE_TEMPORANEI As Double = 24
    Private Const ORE_REGISTRI As Double = 168
    Private Const MASCHERA_FILE_NON_LOCALI As Integer = &H441000
    Private Const TIMEOUT_SERVIZIO_MS As Integer = 45000
    Private Const TIMEOUT_DISM_MS As Integer = 1800000

    <DllImport("shell32.dll")>
    Private Shared Function SHEmptyRecycleBin(hwnd As IntPtr, pszRootPath As String, dwFlags As Integer) As Integer
    End Function

    <DllImport("shell32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SHQueryRecycleBin(pszRootPath As String, ByRef pSHQueryRBInfo As SHQUERYRBINFO) As Integer
    End Function

    <StructLayout(LayoutKind.Sequential, Pack:=4)>
    Public Structure SHQUERYRBINFO
        Public cbSize As Integer
        Public i64Size As Long
        Public i64NumItems As Long
    End Structure

    Public Class VocePulizia
        Public Property Nome As String = ""
        Public Property BytesLiberati As Long = 0
        Public Property ElementiEliminati As Integer = 0
        Public Property Saltata As Boolean = False
        Public Property Nota As String = ""
    End Class

    Public Class RisultatoPulizia
        Public ReadOnly Property Voci As New List(Of VocePulizia)()
        Public Property SonoAmministratore As Boolean = False
        Public Property DiscoSistema As String = ""
        Public Property SpazioLiberoPrima As Long = -1
        Public Property SpazioLiberoDopo As Long = -1

        Public ReadOnly Property TotaleLiberato As Long
            Get
                Dim totale As Long = 0
                For Each voce As VocePulizia In Voci
                    totale += voce.BytesLiberati
                Next
                Return totale
            End Get
        End Property

        Public ReadOnly Property CiSonoVociSaltate As Boolean
            Get
                For Each voce As VocePulizia In Voci
                    If voce.Saltata Then Return True
                Next
                Return False
            End Get
        End Property
    End Class

    Private Class Contatore
        Public Bytes As Long = 0
        Public Elementi As Integer = 0
        Public NonEliminati As Integer = 0
    End Class

    Private Class FileCandidato
        Public Percorso As String = ""
        Public Lunghezza As Long = 0
        Public Creazione As DateTime = DateTime.MinValue
    End Class

    Public Function Esegui(tipo As String) As RisultatoPulizia
        Dim risultato As New RisultatoPulizia()
        Try
            risultato.SonoAmministratore = SonoAmministratore()
            risultato.DiscoSistema = DiscoDiSistema()
            risultato.SpazioLiberoPrima = SpazioLibero(risultato.DiscoSistema)

            Select Case tipo
                Case "temp"
                    EseguiTemporanei(risultato)
                Case "cestino"
                    EseguiVoce(risultato, "Cestino", AddressOf SvuotaCestinoInterno)
                Case "duplicati"
                    EseguiVoce(risultato, "File duplicati", AddressOf EliminaDuplicatiInterno)
                Case "avanzata"
                    EseguiAvanzata(risultato)
                Case "componenti"
                    EseguiComponentiWindows(risultato)
                Case "completa"
                    EseguiTemporanei(risultato)
                    EseguiVoce(risultato, "Cestino", AddressOf SvuotaCestinoInterno)
                    EseguiAvanzata(risultato)
                Case Else
                    _log.ScriviLog("WGPuliziaPC.Esegui - Tipo di pulizia sconosciuto: " & tipo)
            End Select

            risultato.SpazioLiberoDopo = SpazioLibero(risultato.DiscoSistema)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.Esegui (" & tipo & ") - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Function CostruisciTestoBreve(risultato As RisultatoPulizia) As String
        Try
            If risultato Is Nothing Then Return "Pulizia terminata con errori. Controlla il log."
            Dim testo As New StringBuilder()
            Dim totale As Long = risultato.TotaleLiberato
            If totale > 0 Then
                testo.Append("Pulizia completata. Spazio liberato: " & FormatoDimensione(totale) & ".")
            Else
                testo.Append("Pulizia completata. Non è stato liberato spazio.")
            End If
            If risultato.SpazioLiberoDopo >= 0 AndAlso Not String.IsNullOrEmpty(risultato.DiscoSistema) Then
                testo.Append(" Spazio libero sul disco " & risultato.DiscoSistema.TrimEnd("\"c) & " ora: " & FormatoDimensione(risultato.SpazioLiberoDopo) & ".")
            End If
            If risultato.CiSonoVociSaltate Then
                testo.Append(" Alcune operazioni sono state saltate perché servono i diritti di amministratore.")
            End If
            Return testo.ToString()
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.CostruisciTestoBreve - Errore: " & ex.Message)
            Return "Pulizia terminata."
        End Try
    End Function

    Public Function CostruisciDettaglio(risultato As RisultatoPulizia) As List(Of String)
        Dim righe As New List(Of String)()
        Try
            If risultato Is Nothing Then Return righe
            righe.Add("Spazio totale liberato: " & FormatoDimensione(risultato.TotaleLiberato))
            For Each voce As VocePulizia In risultato.Voci
                If voce.Saltata Then
                    righe.Add(voce.Nome & ": saltata. " & voce.Nota)
                Else
                    Dim riga As String = voce.Nome & ": " & FormatoDimensione(voce.BytesLiberati)
                    If voce.ElementiEliminati > 0 Then riga &= " in " & voce.ElementiEliminati.ToString() & " elementi"
                    If Not String.IsNullOrEmpty(voce.Nota) Then riga &= ". " & voce.Nota
                    righe.Add(riga)
                End If
            Next
            If risultato.SpazioLiberoPrima >= 0 AndAlso risultato.SpazioLiberoDopo >= 0 Then
                righe.Add("Spazio libero sul disco " & risultato.DiscoSistema.TrimEnd("\"c) & " prima della pulizia: " & FormatoDimensione(risultato.SpazioLiberoPrima))
                righe.Add("Spazio libero sul disco " & risultato.DiscoSistema.TrimEnd("\"c) & " dopo la pulizia: " & FormatoDimensione(risultato.SpazioLiberoDopo))
            End If
            righe.Add(If(risultato.SonoAmministratore, "Programma avviato con i diritti di amministratore.", "Programma avviato senza i diritti di amministratore."))
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.CostruisciDettaglio - Errore: " & ex.Message)
        End Try
        Return righe
    End Function

    Public Function PulisciFileTemporanei() As Long
        Try
            Return Esegui("temp").TotaleLiberato
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciFileTemporanei - Errore: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function SvuotaCestino() As Long
        Try
            Return Esegui("cestino").TotaleLiberato
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.SvuotaCestino - Errore: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function EliminaDuplicatiSicuri() As Long
        Try
            Return Esegui("duplicati").TotaleLiberato
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.EliminaDuplicatiSicuri - Errore: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function SonoAmministratore() As Boolean
        Try
            Using identita As WindowsIdentity = WindowsIdentity.GetCurrent()
                Dim principale As New WindowsPrincipal(identita)
                Return principale.IsInRole(WindowsBuiltInRole.Administrator)
            End Using
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.SonoAmministratore - Errore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub EseguiVoce(risultato As RisultatoPulizia, nome As String, azione As Action(Of Contatore))
        Dim contatore As New Contatore()
        Dim voce As New VocePulizia() With {.Nome = nome}
        Try
            azione(contatore)
        Catch ex As Exception
            voce.Nota = "Errore durante la pulizia, controlla il log."
            _log.ScriviLog("WGPuliziaPC.EseguiVoce (" & nome & ") - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        End Try
        voce.BytesLiberati = contatore.Bytes
        voce.ElementiEliminati = contatore.Elementi
        If contatore.NonEliminati > 0 AndAlso String.IsNullOrEmpty(voce.Nota) Then
            voce.Nota = contatore.NonEliminati.ToString() & " elementi non eliminati perché in uso o protetti."
        End If
        risultato.Voci.Add(voce)
    End Sub

    Private Sub AggiungiVoceSaltata(risultato As RisultatoPulizia, nome As String, motivo As String)
        Try
            risultato.Voci.Add(New VocePulizia() With {.Nome = nome, .Saltata = True, .Nota = motivo})
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.AggiungiVoceSaltata - Errore: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiTemporanei(risultato As RisultatoPulizia)
        EseguiVoce(risultato, "File temporanei dell'utente", Sub(c) PulisciContenuto(Path.GetTempPath(), ORE_TEMPORANEI, c))
        EseguiVoce(risultato, "File temporanei di Windows", Sub(c) PulisciContenuto(Path.Combine(CartellaWindows(), "Temp"), ORE_TEMPORANEI, c))
    End Sub

    Private Sub EseguiAvanzata(risultato As RisultatoPulizia)
        If Not risultato.SonoAmministratore Then
            AggiungiVoceSaltata(risultato, "Pulizia avanzata di Windows", "Servono i diritti di amministratore.")
            Return
        End If
        EseguiVoce(risultato, "File temporanei di tutti gli utenti", AddressOf PulisciTemporaneiTuttiGliUtenti)
        EseguiVoce(risultato, "Cache di Windows Update", AddressOf PulisciCacheWindowsUpdate)
        EseguiVoce(risultato, "Cache di Ottimizzazione recapito", AddressOf PulisciCacheOttimizzazioneRecapito)
        EseguiVoce(risultato, "Rapporti degli errori di Windows", AddressOf PulisciRapportiErrori)
        EseguiVoce(risultato, "File di dump della memoria", AddressOf PulisciDump)
        EseguiVoce(risultato, "Registri di servizio di Windows", AddressOf PulisciRegistriServizio)
    End Sub

    Private Sub EseguiComponentiWindows(risultato As RisultatoPulizia)
        If Not risultato.SonoAmministratore Then
            AggiungiVoceSaltata(risultato, "Pulizia dei componenti di Windows", "Servono i diritti di amministratore.")
            Return
        End If
        EseguiVoce(risultato, "Pulizia dei componenti di Windows", AddressOf PulisciComponentiConDism)
    End Sub

    Private Function CartellaWindows() As String
        Try
            Return Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.CartellaWindows - Errore: " & ex.Message)
            Return "C:\Windows"
        End Try
    End Function

    Private Function DiscoDiSistema() As String
        Try
            Dim radice As String = Path.GetPathRoot(Environment.SystemDirectory)
            If String.IsNullOrEmpty(radice) Then Return "C:\"
            Return radice
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.DiscoDiSistema - Errore: " & ex.Message)
            Return "C:\"
        End Try
    End Function

    Private Function SpazioLibero(radice As String) As Long
        Try
            If String.IsNullOrEmpty(radice) Then Return -1
            Dim unita As New DriveInfo(radice)
            If Not unita.IsReady Then Return -1
            Return unita.AvailableFreeSpace
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.SpazioLibero (" & radice & ") - Errore: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Sub RegistraNonEliminato(percorso As String, ex As Exception, contatore As Contatore)
        Try
            contatore.NonEliminati += 1
            If TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException Then
                If contatore.NonEliminati <= 3 Then
                    _log.ScriviLog("WGPuliziaPC - Elemento non eliminabile (in uso o protetto): " & percorso & " - " & ex.Message)
                End If
            Else
                _log.ScriviLog("WGPuliziaPC - Errore su " & percorso & ": " & ex.GetType().FullName & " - " & ex.Message)
            End If
        Catch exLog As Exception
            System.Diagnostics.Debug.WriteLine("WGPuliziaPC LOG FAILURE: " & exLog.Message)
        End Try
    End Sub

    Private Sub PulisciContenuto(cartella As String, etaMinimaOre As Double, contatore As Contatore)
        Try
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then Return
            Dim limite As DateTime = DateTime.Now.AddHours(-etaMinimaOre)
            PulisciRicorsivo(New DirectoryInfo(cartella), limite, contatore, False)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciContenuto (" & cartella & ") - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        End Try
    End Sub

    Private Sub PulisciRicorsivo(cartella As DirectoryInfo, limite As DateTime, contatore As Contatore, eliminaCartella As Boolean)
        Dim ultimaModificaCartella As DateTime = DateTime.MaxValue
        Try
            ultimaModificaCartella = cartella.LastWriteTime
        Catch exData As Exception
            RegistraNonEliminato(cartella.FullName, exData, contatore)
        End Try

        Dim files As FileInfo() = New FileInfo() {}
        Try
            files = cartella.GetFiles()
        Catch ex As Exception
            RegistraNonEliminato(cartella.FullName, ex, contatore)
        End Try

        For Each f As FileInfo In files
            Try
                If f.LastWriteTime > limite Then Continue For
                Dim lunghezza As Long = f.Length
                If (f.Attributes And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                    f.Attributes = FileAttributes.Normal
                End If
                f.Delete()
                contatore.Bytes += lunghezza
                contatore.Elementi += 1
            Catch ex As Exception
                RegistraNonEliminato(f.FullName, ex, contatore)
            End Try
        Next

        Dim sottocartelle As DirectoryInfo() = New DirectoryInfo() {}
        Try
            sottocartelle = cartella.GetDirectories()
        Catch ex As Exception
            RegistraNonEliminato(cartella.FullName, ex, contatore)
        End Try

        For Each d As DirectoryInfo In sottocartelle
            Try
                If (d.Attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Continue For
                PulisciRicorsivo(d, limite, contatore, True)
            Catch ex As Exception
                RegistraNonEliminato(d.FullName, ex, contatore)
            End Try
        Next

        If eliminaCartella AndAlso ultimaModificaCartella <= limite Then
            Try
                If Not cartella.EnumerateFileSystemInfos().Any() Then
                    cartella.Delete()
                End If
            Catch ex As Exception
                RegistraNonEliminato(cartella.FullName, ex, contatore)
            End Try
        End If
    End Sub

    Private Sub EliminaFileSingolo(percorso As String, contatore As Contatore)
        Try
            If Not File.Exists(percorso) Then Return
            Dim info As New FileInfo(percorso)
            Dim lunghezza As Long = info.Length
            If (info.Attributes And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                info.Attributes = FileAttributes.Normal
            End If
            info.Delete()
            contatore.Bytes += lunghezza
            contatore.Elementi += 1
        Catch ex As Exception
            RegistraNonEliminato(percorso, ex, contatore)
        End Try
    End Sub

    Private Sub PulisciTemporaneiTuttiGliUtenti(contatore As Contatore)
        Try
            Dim profilo As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            Dim radiceUtenti As String = Path.GetDirectoryName(profilo)
            If String.IsNullOrEmpty(radiceUtenti) OrElse Not Directory.Exists(radiceUtenti) Then Return
            Dim esclusi As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {"Public", "Default", "Default User", "All Users", "desktop.ini"}
            For Each cartellaUtente As String In Directory.GetDirectories(radiceUtenti)
                Try
                    Dim nome As String = Path.GetFileName(cartellaUtente)
                    If esclusi.Contains(nome) Then Continue For
                    Dim attributi As FileAttributes = File.GetAttributes(cartellaUtente)
                    If (attributi And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Continue For
                    PulisciContenuto(Path.Combine(cartellaUtente, "AppData", "Local", "Temp"), ORE_TEMPORANEI, contatore)
                Catch exUtente As Exception
                    RegistraNonEliminato(cartellaUtente, exUtente, contatore)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciTemporaneiTuttiGliUtenti - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        End Try
    End Sub

    Private Sub PulisciCacheWindowsUpdate(contatore As Contatore)
        Dim servizi As New List(Of String)()
        Try
            servizi = FermaServizi(New String() {"wuauserv", "bits"})
            PulisciContenuto(Path.Combine(CartellaWindows(), "SoftwareDistribution", "Download"), 0, contatore)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciCacheWindowsUpdate - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        Finally
            AvviaServizi(servizi)
        End Try
    End Sub

    Private Sub PulisciCacheOttimizzazioneRecapito(contatore As Contatore)
        Dim servizi As New List(Of String)()
        Try
            servizi = FermaServizi(New String() {"dosvc"})
            PulisciContenuto(Path.Combine(CartellaWindows(), "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), 0, contatore)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciCacheOttimizzazioneRecapito - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        Finally
            AvviaServizi(servizi)
        End Try
    End Sub

    Private Sub PulisciRapportiErrori(contatore As Contatore)
        Try
            Dim datiProgramma As String = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            Dim baseWer As String = Path.Combine(datiProgramma, "Microsoft", "Windows", "WER")
            PulisciContenuto(Path.Combine(baseWer, "ReportArchive"), 0, contatore)
            PulisciContenuto(Path.Combine(baseWer, "ReportQueue"), 0, contatore)
            PulisciContenuto(Path.Combine(baseWer, "Temp"), 0, contatore)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciRapportiErrori - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        End Try
    End Sub

    Private Sub PulisciDump(contatore As Contatore)
        Try
            Dim windows As String = CartellaWindows()
            EliminaFileSingolo(Path.Combine(windows, "MEMORY.DMP"), contatore)
            PulisciContenuto(Path.Combine(windows, "Minidump"), 0, contatore)
            PulisciContenuto(Path.Combine(windows, "LiveKernelReports"), 0, contatore)
            PulisciContenuto(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"), 0, contatore)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciDump - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        End Try
    End Sub

    Private Sub PulisciRegistriServizio(contatore As Contatore)
        Try
            Dim registri As String = Path.Combine(CartellaWindows(), "Logs")
            PulisciContenuto(Path.Combine(registri, "CBS"), ORE_REGISTRI, contatore)
            PulisciContenuto(Path.Combine(registri, "DISM"), ORE_REGISTRI, contatore)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciRegistriServizio - Errore: " & ex.GetType().FullName & " - " & ex.Message)
        End Try
    End Sub

    Private Function EseguiProcesso(nomeFile As String, argomenti As String, timeoutMs As Integer, ByRef uscita As String) As Integer
        uscita = String.Empty
        Try
            Dim avvio As New System.Diagnostics.ProcessStartInfo(nomeFile, argomenti)
            avvio.UseShellExecute = False
            avvio.CreateNoWindow = True
            avvio.RedirectStandardOutput = True
            avvio.RedirectStandardError = True
            Using processo As System.Diagnostics.Process = System.Diagnostics.Process.Start(avvio)
                If processo Is Nothing Then Return -1
                Dim lettura As Task(Of String) = processo.StandardOutput.ReadToEndAsync()
                Dim letturaErrori As Task(Of String) = processo.StandardError.ReadToEndAsync()
                If Not processo.WaitForExit(timeoutMs) Then
                    _log.ScriviLog("WGPuliziaPC.EseguiProcesso - Timeout di " & timeoutMs.ToString() & " ms per: " & nomeFile & " " & argomenti)
                    Try
                        processo.Kill()
                    Catch exKill As Exception
                        _log.ScriviLog("WGPuliziaPC.EseguiProcesso - Errore chiusura processo: " & exKill.Message)
                    End Try
                    Return -2
                End If
                processo.WaitForExit()
                uscita = lettura.Result
                Return processo.ExitCode
            End Using
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.EseguiProcesso (" & nomeFile & ") - Errore: " & ex.GetType().FullName & " - " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function ServizioInEsecuzione(nome As String) As Boolean
        Try
            Dim uscita As String = String.Empty
            Dim codice As Integer = EseguiProcesso("sc.exe", "query " & nome, TIMEOUT_SERVIZIO_MS, uscita)
            If codice <> 0 Then Return False
            Return uscita.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.ServizioInEsecuzione (" & nome & ") - Errore: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FermaServizi(nomi As String()) As List(Of String)
        Dim fermati As New List(Of String)()
        For Each nome As String In nomi
            Try
                If Not ServizioInEsecuzione(nome) Then Continue For
                Dim uscita As String = String.Empty
                Dim codice As Integer = EseguiProcesso("net.exe", "stop " & nome & " /y", TIMEOUT_SERVIZIO_MS, uscita)
                If codice = 0 Then
                    fermati.Add(nome)
                Else
                    _log.ScriviLog("WGPuliziaPC.FermaServizi - net stop " & nome & " ha restituito " & codice.ToString())
                End If
            Catch ex As Exception
                _log.ScriviLog("WGPuliziaPC.FermaServizi (" & nome & ") - Errore: " & ex.Message)
            End Try
        Next
        Return fermati
    End Function

    Private Sub AvviaServizi(nomi As List(Of String))
        Try
            If nomi Is Nothing Then Return
            For indice As Integer = nomi.Count - 1 To 0 Step -1
                Try
                    Dim uscita As String = String.Empty
                    Dim codice As Integer = EseguiProcesso("net.exe", "start " & nomi(indice), TIMEOUT_SERVIZIO_MS, uscita)
                    If codice <> 0 Then
                        _log.ScriviLog("WGPuliziaPC.AvviaServizi - net start " & nomi(indice) & " ha restituito " & codice.ToString())
                    End If
                Catch exServizio As Exception
                    _log.ScriviLog("WGPuliziaPC.AvviaServizi (" & nomi(indice) & ") - Errore: " & exServizio.Message)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.AvviaServizi - Errore: " & ex.Message)
        End Try
    End Sub

    Private Function PercorsoDism() As String
        Try
            Dim windows As String = CartellaWindows()
            If Environment.Is64BitOperatingSystem AndAlso Not Environment.Is64BitProcess Then
                Return Path.Combine(windows, "Sysnative", "dism.exe")
            End If
            Return Path.Combine(windows, "System32", "dism.exe")
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PercorsoDism - Errore: " & ex.Message)
            Return "dism.exe"
        End Try
    End Function

    Private Sub PulisciComponentiConDism(contatore As Contatore)
        Try
            Dim disco As String = DiscoDiSistema()
            Dim prima As Long = SpazioLibero(disco)
            Dim uscita As String = String.Empty
            Dim codice As Integer = EseguiProcesso(PercorsoDism(), "/Online /Cleanup-Image /StartComponentCleanup", TIMEOUT_DISM_MS, uscita)
            Dim dopo As Long = SpazioLibero(disco)
            If codice <> 0 AndAlso codice <> 3010 Then
                _log.ScriviLog("WGPuliziaPC.PulisciComponentiConDism - DISM ha restituito il codice " & codice.ToString())
                Throw New InvalidOperationException("DISM ha restituito il codice " & codice.ToString())
            End If
            If prima >= 0 AndAlso dopo > prima Then
                contatore.Bytes = dopo - prima
                contatore.Elementi = 1
            End If
        Catch ex As InvalidOperationException
            Throw
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.PulisciComponentiConDism - Errore: " & ex.GetType().FullName & " - " & ex.Message)
            Throw
        End Try
    End Sub

    Private Sub SvuotaCestinoInterno(contatore As Contatore)
        Try
            Dim prima As New SHQUERYRBINFO()
            prima.cbSize = Marshal.SizeOf(prima)
            Dim esitoPrima As Integer = SHQueryRecycleBin(Nothing, prima)
            If esitoPrima <> 0 Then
                _log.ScriviLog("WGPuliziaPC.SvuotaCestino - SHQueryRecycleBin ha restituito " & esitoPrima.ToString())
            ElseIf prima.i64NumItems = 0 Then
                Return
            End If

            Dim esito As Integer = SHEmptyRecycleBin(IntPtr.Zero, Nothing, SHERB_NOCONFIRMATION Or SHERB_NOPROGRESSUI Or SHERB_NOSOUND)
            If esito <> 0 Then
                _log.ScriviLog("WGPuliziaPC.SvuotaCestino - SHEmptyRecycleBin ha restituito " & esito.ToString())
                Return
            End If

            If esitoPrima = 0 Then
                Dim dopo As New SHQUERYRBINFO()
                dopo.cbSize = Marshal.SizeOf(dopo)
                Dim esitoDopo As Integer = SHQueryRecycleBin(Nothing, dopo)
                Dim rimanente As Long = If(esitoDopo = 0, dopo.i64Size, 0)
                Dim rimanenteElementi As Long = If(esitoDopo = 0, dopo.i64NumItems, 0)
                contatore.Bytes = Math.Max(0, prima.i64Size - rimanente)
                contatore.Elementi = CInt(Math.Min(Math.Max(0, prima.i64NumItems - rimanenteElementi), CLng(Integer.MaxValue)))
            End If
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.SvuotaCestino - Errore: " & ex.GetType().FullName & " - " & ex.Message)
            Throw
        End Try
    End Sub

    Private Sub RaccogliFile(cartella As DirectoryInfo, destinazione As List(Of FileInfo))
        Try
            For Each f As FileInfo In cartella.GetFiles()
                destinazione.Add(f)
            Next
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.RaccogliFile - Errore lettura file in " & cartella.FullName & ": " & ex.Message)
        End Try

        Dim sottocartelle As DirectoryInfo() = New DirectoryInfo() {}
        Try
            sottocartelle = cartella.GetDirectories()
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.RaccogliFile - Errore lettura sottocartelle in " & cartella.FullName & ": " & ex.Message)
        End Try

        For Each d As DirectoryInfo In sottocartelle
            Try
                If (d.Attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then Continue For
                RaccogliFile(d, destinazione)
            Catch ex As Exception
                _log.ScriviLog("WGPuliziaPC.RaccogliFile - Errore su " & d.FullName & ": " & ex.Message)
            End Try
        Next
    End Sub

    Private Sub EliminaDuplicatiInterno(contatore As Contatore)
        Try
            Dim cartelleDaScansionare As New List(Of String)()
            cartelleDaScansionare.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
            cartelleDaScansionare.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))
            cartelleDaScansionare.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic))
            cartelleDaScansionare.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))
            cartelleDaScansionare.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"))

            Dim tuttiIFile As New List(Of FileInfo)()
            Dim cartelleVisitate As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each cartella As String In cartelleDaScansionare
                Try
                    If String.IsNullOrWhiteSpace(cartella) Then Continue For
                    If Not cartelleVisitate.Add(cartella) Then Continue For
                    If WGRete.IsRemoto(cartella) Then
                        _log.ScriviLog("WGPuliziaPC.EliminaDuplicati - Cartella su rete esclusa dalla ricerca dei duplicati: " & cartella)
                        Continue For
                    End If
                    If Directory.Exists(cartella) Then
                        RaccogliFile(New DirectoryInfo(cartella), tuttiIFile)
                    End If
                Catch ex As Exception
                    _log.ScriviLog("WGPuliziaPC.EliminaDuplicati - Errore scansione cartella " & cartella & ": " & ex.Message)
                End Try
            Next

            Dim percorsiVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim candidati As New List(Of FileCandidato)()
            For Each f As FileInfo In tuttiIFile
                Try
                    If Not percorsiVisti.Add(f.FullName) Then Continue For
                    If (f.Attributes And FileAttributes.System) = FileAttributes.System Then Continue For
                    If (CInt(f.Attributes) And MASCHERA_FILE_NON_LOCALI) <> 0 Then Continue For
                    Dim lunghezza As Long = f.Length
                    If lunghezza < 1024 Then Continue For
                    candidati.Add(New FileCandidato() With {.Percorso = f.FullName, .Lunghezza = lunghezza, .Creazione = f.CreationTime})
                Catch ex As Exception
                    _log.ScriviLog("WGPuliziaPC.EliminaDuplicati - Errore lettura file " & f.FullName & ": " & ex.Message)
                End Try
            Next

            Dim gruppi As List(Of List(Of FileCandidato)) = candidati.GroupBy(Function(c) c.Lunghezza).Where(Function(g) g.Count() > 1).Select(Function(g) g.OrderBy(Function(x) x.Creazione).ThenBy(Function(x) x.Percorso, StringComparer.OrdinalIgnoreCase).ToList()).ToList()

            Dim opzioni As New ParallelOptions()
            opzioni.MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount \ 2)

            Parallel.ForEach(gruppi, opzioni, Sub(gruppo) ProcessaGruppoDuplicati(gruppo, contatore))
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.EliminaDuplicati - Errore: " & ex.GetType().FullName & " - " & ex.Message)
            Throw
        End Try
    End Sub

    Private Sub ProcessaGruppoDuplicati(gruppo As List(Of FileCandidato), contatore As Contatore)
        Try
            Dim conservati As New Dictionary(Of String, FileCandidato)()
            For Each candidato As FileCandidato In gruppo
                Try
                    Dim hash As String = CalcolaHash(candidato.Percorso)
                    Dim originale As FileCandidato = Nothing
                    If conservati.TryGetValue(hash, originale) Then
                        If File.Exists(originale.Percorso) AndAlso ContenutoIdentico(originale.Percorso, candidato.Percorso) Then
                            Dim attributi As FileAttributes = File.GetAttributes(candidato.Percorso)
                            If (attributi And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                                File.SetAttributes(candidato.Percorso, FileAttributes.Normal)
                            End If
                            File.Delete(candidato.Percorso)
                            Interlocked.Add(contatore.Bytes, candidato.Lunghezza)
                            Interlocked.Increment(contatore.Elementi)
                        End If
                    Else
                        conservati.Add(hash, candidato)
                    End If
                Catch exFile As Exception
                    _log.ScriviLog("WGPuliziaPC.EliminaDuplicati - Errore su " & candidato.Percorso & ": " & exFile.GetType().FullName & " - " & exFile.Message)
                End Try
            Next
        Catch exGruppo As Exception
            _log.ScriviLog("WGPuliziaPC.EliminaDuplicati - Errore nel gruppo: " & exGruppo.Message)
        End Try
    End Sub

    Private Function CalcolaHash(percorsoFile As String) As String
        Using md5Hash As MD5 = MD5.Create()
            Using flusso As New FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim hash As Byte() = md5Hash.ComputeHash(flusso)
                Return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

    Private Function LeggiBlocco(flusso As Stream, buffer As Byte()) As Integer
        Dim letti As Integer = 0
        While letti < buffer.Length
            Dim parziale As Integer = flusso.Read(buffer, letti, buffer.Length - letti)
            If parziale <= 0 Then Exit While
            letti += parziale
        End While
        Return letti
    End Function

    Private Function ContenutoIdentico(primo As String, secondo As String) As Boolean
        Try
            Using a As New FileStream(primo, FileMode.Open, FileAccess.Read, FileShare.Read)
                Using b As New FileStream(secondo, FileMode.Open, FileAccess.Read, FileShare.Read)
                    If a.Length <> b.Length Then Return False
                    Dim bufferA(81919) As Byte
                    Dim bufferB(81919) As Byte
                    While True
                        Dim lettiA As Integer = LeggiBlocco(a, bufferA)
                        Dim lettiB As Integer = LeggiBlocco(b, bufferB)
                        If lettiA <> lettiB Then Return False
                        If lettiA = 0 Then Return True
                        For i As Integer = 0 To lettiA - 1
                            If bufferA(i) <> bufferB(i) Then Return False
                        Next
                    End While
                End Using
            End Using
            Return False
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.ContenutoIdentico - Errore: " & ex.GetType().FullName & " - " & ex.Message)
            Return False
        End Try
    End Function

    Public Function FormatoDimensione(bytes As Long) As String
        Try
            Dim nomiUnita() As String = {"B", "KB", "MB", "GB", "TB", "PB"}
            If bytes <= 0 Then Return "0 B"
            Dim indice As Integer = CInt(Math.Floor(Math.Log(bytes) / Math.Log(1024)))
            If indice < 0 Then indice = 0
            If indice > nomiUnita.Length - 1 Then indice = nomiUnita.Length - 1
            Dim leggibile As Double = Math.Round(bytes / Math.Pow(1024, indice), 2)
            Return leggibile.ToString("0.##") & " " & nomiUnita(indice)
        Catch ex As Exception
            _log.ScriviLog("WGPuliziaPC.FormatoDimensione - Errore: " & ex.Message)
            Return bytes.ToString() & " B"
        End Try
    End Function
End Class
