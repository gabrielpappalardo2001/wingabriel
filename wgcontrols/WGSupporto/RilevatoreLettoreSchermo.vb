Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports Microsoft.Win32
Imports wgcontrols

' Rilevamento dei lettori di schermo installati nel sistema. Identifica software come NVDA, JAWS, KSR e l'assistente vocale.
Friend NotInheritable Class RilevatoreLettoreSchermo

    Friend Const NOME_WINGABRIEL As String = "WinGabriel"
    Friend Const NOME_NVDA As String = "NVDA"
    Friend Const NOME_JAWS As String = "JAWS"
    Friend Const NOME_KSR As String = "KSR"
    Friend Const NOME_ASSISTENTE_VOCALE As String = "Narrator"
    Friend Const ID_CONFIG_RILEVATO As String = "screen_reader_rilevato"

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _bloccoCache As New Object()
    Private Shared _ultimoRilevato As String = ""
    Private Shared _istanteRilevamento As DateTime = DateTime.MinValue

    Private Sub New()
    End Sub

    Private Shared Function CartelleProgrammi() As List(Of String)
        Dim elenco As New List(Of String)()
        Try
            Dim candidate As String() = {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)")
            }
            For Each cartella As String In candidate
                If String.IsNullOrWhiteSpace(cartella) Then Continue For
                Dim presente As Boolean = False
                For Each esistente As String In elenco
                    If String.Equals(esistente, cartella, StringComparison.OrdinalIgnoreCase) Then
                        presente = True
                        Exit For
                    End If
                Next
                If Not presente Then elenco.Add(cartella)
            Next
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.CartelleProgrammi: " & ex.Message)
        End Try
        Return elenco
    End Function

    Private Shared Function FileEsiste(percorso As String) As Boolean
        Try
            ' Da amministratore un lettore che sta nei dati dell'utente, o indicato nel suo registro, non si avvia: partirebbe come
            ' amministratore e chiunque giri come utente potrebbe averlo sostituito.
            Return Not String.IsNullOrWhiteSpace(percorso) AndAlso File.Exists(percorso) AndAlso WGDb.WGPercorsiApplicazione.AvvioSicuro(percorso)
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.FileEsiste: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function LeggiValoreRegistro(radice As RegistryHive, vista As RegistryView, sottochiave As String, nome As String) As String
        Try
            Using base As RegistryKey = RegistryKey.OpenBaseKey(radice, vista)
                Using chiave As RegistryKey = base.OpenSubKey(sottochiave)
                    If chiave Is Nothing Then Return ""
                    Dim valore As Object = chiave.GetValue(nome)
                    If valore Is Nothing Then Return ""
                    Return valore.ToString()
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.LeggiValoreRegistro: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PulisciPercorsoEseguibile(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim testo As String = valore.Trim()
            If testo.StartsWith("""") Then
                Dim fine As Integer = testo.IndexOf(""""c, 1)
                If fine > 1 Then testo = testo.Substring(1, fine - 1)
            Else
                Dim virgola As Integer = testo.IndexOf(","c)
                If virgola > 0 Then testo = testo.Substring(0, virgola)
            End If
            Return testo.Trim()
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.PulisciPercorsoEseguibile: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PercorsoDaProcesso(nomeProcesso As String) As String
        Try
            For Each processo As Process In Process.GetProcessesByName(nomeProcesso)
                Try
                    Dim percorso As String = processo.MainModule.FileName
                    If FileEsiste(percorso) Then Return percorso
                Catch exModulo As Exception
                    log.ScriviLog("RilevatoreLettoreSchermo.PercorsoDaProcesso (" & nomeProcesso & "): " & exModulo.Message)
                Finally
                    processo.Dispose()
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.PercorsoDaProcesso: " & ex.Message)
        End Try
        Return ""
    End Function

    Friend Shared Function InEsecuzione(nomeProcesso As String) As Boolean
        Try
            Dim processi As Process() = Process.GetProcessesByName(nomeProcesso)
            Dim trovato As Boolean = processi.Length > 0
            For Each processo As Process In processi
                processo.Dispose()
            Next
            Return trovato
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.InEsecuzione (" & nomeProcesso & "): " & ex.Message)
            Return False
        End Try
    End Function

    Friend Shared Function TrovaNvda() As String
        Try
            Dim viste As RegistryView() = {RegistryView.Registry64, RegistryView.Registry32}
            For Each vista As RegistryView In viste
                For Each nomeValore As String In New String() {"UninstallDirectory", "InstallLocation"}
                    Dim cartella As String = LeggiValoreRegistro(RegistryHive.LocalMachine, vista, "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NVDA", nomeValore)
                    If Not String.IsNullOrWhiteSpace(cartella) Then
                        Dim candidato As String = Path.Combine(cartella.Trim().Trim(""""c), "nvda.exe")
                        If FileEsiste(candidato) Then Return candidato
                    End If
                Next
                Dim icona As String = PulisciPercorsoEseguibile(LeggiValoreRegistro(RegistryHive.LocalMachine, vista, "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NVDA", "DisplayIcon"))
                If icona <> "" Then
                    Dim daIcona As String = Path.Combine(Path.GetDirectoryName(icona), "nvda.exe")
                    If FileEsiste(daIcona) Then Return daIcona
                End If
                Dim percorsoApp As String = PulisciPercorsoEseguibile(LeggiValoreRegistro(RegistryHive.LocalMachine, vista, "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\nvda.exe", ""))
                If FileEsiste(percorsoApp) Then Return percorsoApp
            Next

            For Each cartella As String In CartelleProgrammi()
                Dim candidato As String = Path.Combine(cartella, "NVDA", "nvda.exe")
                If FileEsiste(candidato) Then Return candidato
            Next

            Return PercorsoDaProcesso("nvda")
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.TrovaNvda: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ChiaveVersione(nomeCartella As String) As Long
        Try
            Dim numero As Long = 0
            Dim cifre As String = ""
            For Each carattere As Char In nomeCartella
                If Char.IsDigit(carattere) Then cifre &= carattere
            Next
            If cifre <> "" AndAlso Long.TryParse(cifre, numero) Then Return numero
            Return 0
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.ChiaveVersione: " & ex.Message)
            Return 0
        End Try
    End Function

    Friend Shared Function TrovaJaws() As String
        Try
            Dim candidati As New List(Of KeyValuePair(Of Long, String))()
            For Each cartellaProgrammi As String In CartelleProgrammi()
                Dim radiceJaws As String = Path.Combine(cartellaProgrammi, "Freedom Scientific", "JAWS")
                If Not Directory.Exists(radiceJaws) Then Continue For
                Try
                    For Each versione As String In Directory.GetDirectories(radiceJaws)
                        Dim jfw As String = Path.Combine(versione, "jfw.exe")
                        If FileEsiste(jfw) Then
                            candidati.Add(New KeyValuePair(Of Long, String)(ChiaveVersione(Path.GetFileName(versione)), jfw))
                        End If
                    Next
                Catch exCartella As Exception
                    log.ScriviLog("RilevatoreLettoreSchermo.TrovaJaws (" & radiceJaws & "): " & exCartella.Message)
                End Try
            Next
            If candidati.Count > 0 Then
                Dim migliore As KeyValuePair(Of Long, String) = candidati(0)
                For Each candidato As KeyValuePair(Of Long, String) In candidati
                    If candidato.Key > migliore.Key Then migliore = candidato
                Next
                Return migliore.Value
            End If

            Dim viste As RegistryView() = {RegistryView.Registry64, RegistryView.Registry32}
            For Each vista As RegistryView In viste
                Dim percorsoApp As String = PulisciPercorsoEseguibile(LeggiValoreRegistro(RegistryHive.LocalMachine, vista, "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\jfw.exe", ""))
                If FileEsiste(percorsoApp) Then Return percorsoApp
            Next

            Return PercorsoDaProcesso("jfw")
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.TrovaJaws: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>KSR ScreenReader: in Programmi, nei programmi dell'utente, tra i programmi registrati o nel processo in esecuzione.</summary>
    Friend Shared Function TrovaKsr() As String
        Try
            For Each cartella As String In CartelleProgrammi()
                Dim candidato As String = Path.Combine(cartella, "KSR ScreenReader", "KSR.exe")
                If FileEsiste(candidato) Then Return candidato
            Next
            Dim locale As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            If Not String.IsNullOrWhiteSpace(locale) Then
                Dim candidatoUtente As String = Path.Combine(locale, "Programs", "KSR ScreenReader", "KSR.exe")
                If FileEsiste(candidatoUtente) Then Return candidatoUtente
            End If
            For Each vista As RegistryView In New RegistryView() {RegistryView.Registry64, RegistryView.Registry32}
                For Each radice As RegistryHive In New RegistryHive() {RegistryHive.LocalMachine, RegistryHive.CurrentUser}
                    Dim percorsoApp As String = PulisciPercorsoEseguibile(LeggiValoreRegistro(radice, vista, "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\KSR.exe", ""))
                    If FileEsiste(percorsoApp) Then Return percorsoApp
                Next
            Next
            Return PercorsoDaProcesso("KSR")
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.TrovaKsr: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Shared Function TrovaAssistenteVocale() As String
        Try
            Dim cartellaSistema As String = Environment.GetFolderPath(Environment.SpecialFolder.System)
            Dim candidato As String = Path.Combine(cartellaSistema, "Narrator.exe")
            If FileEsiste(candidato) Then Return candidato
            Dim windows As String = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            candidato = Path.Combine(windows, "Sysnative", "Narrator.exe")
            If FileEsiste(candidato) Then Return candidato
            Return ""
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.TrovaAssistenteVocale: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Shared Function TrovaLettoreWinGabriel() As String
        Try
            Return LettoreEsterno.PercorsoEseguibile()
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.TrovaLettoreWinGabriel: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Shared Function RilevaConCache() As String
        Try
            SyncLock _bloccoCache
                If (DateTime.Now - _istanteRilevamento).TotalSeconds < 5 Then Return _ultimoRilevato
            End SyncLock
            Dim percorso As String = ""
            Dim trovato As String = Rileva(percorso)
            SyncLock _bloccoCache
                _ultimoRilevato = trovato
                _istanteRilevamento = DateTime.Now
            End SyncLock
            Return trovato
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.RilevaConCache: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Shared Function Rileva(ByRef percorso As String) As String
        percorso = ""
        Try
            percorso = TrovaLettoreWinGabriel()
            If percorso <> "" Then Return NOME_WINGABRIEL
            If InEsecuzione("nvda") Then
                percorso = TrovaNvda()
                Return NOME_NVDA
            End If
            If InEsecuzione("jfw") Then
                percorso = TrovaJaws()
                Return NOME_JAWS
            End If
            If InEsecuzione("KSR") Then
                percorso = TrovaKsr()
                Return NOME_KSR
            End If

            percorso = TrovaNvda()
            If percorso <> "" Then Return NOME_NVDA
            percorso = TrovaJaws()
            If percorso <> "" Then Return NOME_JAWS
            percorso = TrovaKsr()
            If percorso <> "" Then Return NOME_KSR
            percorso = TrovaAssistenteVocale()
            If percorso <> "" Then Return NOME_ASSISTENTE_VOCALE
        Catch ex As Exception
            log.ScriviLog("RilevatoreLettoreSchermo.Rileva: " & ex.Message)
        End Try
        percorso = ""
        Return ""
    End Function

    Friend Shared Function Descrivi(nome As String) As String
        Select Case nome
            Case NOME_WINGABRIEL : Return "il lettore di schermo sperimentale"
            Case NOME_NVDA : Return "NVDA"
            Case NOME_JAWS : Return "JAWS"
            Case NOME_KSR : Return "KSR"
            Case NOME_ASSISTENTE_VOCALE : Return "l'assistente vocale di Windows"
            Case Else : Return ""
        End Select
    End Function

End Class
