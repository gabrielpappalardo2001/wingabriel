Imports System.ComponentModel
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Pipes
Imports System.Collections.Generic
Imports System.Text
Imports Microsoft.Win32
Imports wgcontrols

''' <summary>
''' Il lettore di schermo (WGLettore.exe) è un programma a sé. Questa classe lo avvia, lo ferma e gli passa i
''' comandi attraverso la sua pipe di controllo. Le impostazioni sono quelle di WinGabriel (stesso config.xml): WinGabriel indica
''' al lettore il suo file di configurazione, così funziona anche se i due programmi stanno in cartelle diverse.
''' Quando WinGabriel è in primo piano il lettore sta zitto; appena WinGabriel passa in secondo piano il lettore parla.
''' </summary>
Friend NotInheritable Class LettoreEsterno

    Private Const ChiaveAvvio As String = "Software\Microsoft\Windows\CurrentVersion\Run"
    Private Const NomeVoceAvvio As String = "WGLettore"
    Private Const NomeFileLettore As String = "WGLettore.exe"
    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Friend Shared Function PercorsoEseguibile() As String
        Try
            For Each candidato As String In CandidatiEseguibile()
                If EsisteFile(candidato) Then Return candidato
            Next
            Dim daAvvio As String = PercorsoDaAvvioAutomatico()
            If EsisteFile(daAvvio) Then Return daAvvio
            Dim daProcesso As String = PercorsoDaProcesso()
            If EsisteFile(daProcesso) Then Return daProcesso
        Catch ex As Exception
            log.ScriviLog("LettoreEsterno.PercorsoEseguibile: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Shared Function EsisteFile(percorso As String) As Boolean
        Try
            Return Not String.IsNullOrWhiteSpace(percorso) AndAlso File.Exists(percorso)
        Catch ex As Exception
            log.ScriviLog("LettoreEsterno.EsisteFile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function CandidatiEseguibile() As List(Of String)
        Dim elenco As New List(Of String)()
        Try
            Dim cartella As String = AppDomain.CurrentDomain.BaseDirectory.TrimEnd("\"c)
            elenco.Add(Path.Combine(cartella, NomeFileLettore))
            elenco.Add(Path.Combine(cartella, "WGLettore", NomeFileLettore))
            Dim superiore As String = Path.GetDirectoryName(cartella)
            If Not String.IsNullOrEmpty(superiore) Then
                elenco.Add(Path.Combine(superiore, "WGLettore", NomeFileLettore))
            End If
            For Each programmi As String In New String() {Environment.GetEnvironmentVariable("ProgramW6432"),
                                                          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                                          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}
                If String.IsNullOrEmpty(programmi) Then Continue For
                elenco.Add(Path.Combine(programmi, "WGLettore", NomeFileLettore))
                elenco.Add(Path.Combine(programmi, "WinGabriel", "WGLettore", NomeFileLettore))
            Next
            elenco.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WGLettore", NomeFileLettore))
            elenco.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "WGLettore", NomeFileLettore))
        Catch ex As Exception
            log.ScriviLog("LettoreEsterno.CandidatiEseguibile: " & ex.Message)
        End Try
        Return elenco
    End Function

    Private Shared Function PercorsoDaAvvioAutomatico() As String
        Try
            Using chiave As RegistryKey = Registry.CurrentUser.OpenSubKey(ChiaveAvvio)
                If chiave Is Nothing Then Return ""
                Dim valore As String = Convert.ToString(chiave.GetValue(NomeVoceAvvio))
                If String.IsNullOrWhiteSpace(valore) Then Return ""
                valore = valore.Trim()
                If valore.StartsWith("""") Then
                    Dim fine As Integer = valore.IndexOf(""""c, 1)
                    If fine > 1 Then Return valore.Substring(1, fine - 1)
                    Return ""
                End If
                Dim spazio As Integer = valore.IndexOf(" "c)
                Return If(spazio > 0, valore.Substring(0, spazio), valore)
            End Using
        Catch ex As Exception
            log.ScriviLog("LettoreEsterno.PercorsoDaAvvioAutomatico: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PercorsoDaProcesso() As String
        Try
            For Each processo As Process In Process.GetProcessesByName(Path.GetFileNameWithoutExtension(NomeFileLettore))
                Try
                    Return processo.MainModule.FileName
                Catch exModulo As Exception
                    log.ScriviLog("LettoreEsterno.PercorsoDaProcesso: " & exModulo.Message)
                Finally
                    processo.Dispose()
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("LettoreEsterno.PercorsoDaProcesso: " & ex.Message)
        End Try
        Return ""
    End Function

    Friend Shared Function PercorsoConfigurazione() As String
        Return AvvioAmministratore.PercorsoConfigurazione()
    End Function

    Private Shared Function NomePipe() As String
        Return "WGLettoreControllo_" & Process.GetCurrentProcess().SessionId.ToString() & "_Default"
    End Function

    ''' <summary>Manda un comando al lettore in esecuzione; Nothing se non c'è.</summary>
    Friend Shared Function Invia(comando As String) As String
        Try
            Using client As New NamedPipeClientStream(".", NomePipe(), PipeDirection.InOut)
                client.Connect(500)
                Dim scrittore As New StreamWriter(client, New UTF8Encoding(False))
                scrittore.AutoFlush = True
                scrittore.WriteLine(comando)
                Dim lettore As New StreamReader(client, Encoding.UTF8)
                Return lettore.ReadLine()
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    Friend Shared Function InEsecuzione() As Boolean
        Return Invia("STATO") IsNot Nothing
    End Function

    ''' <summary>Avvia il lettore se non c'è, altrimenti gli fa rileggere le impostazioni. Restituisce falso se il programma non si trova.</summary>
    Friend Shared Function AvviaOAggiorna(Optional conImpostazioni As Boolean = False) As Boolean
        Try
            If InEsecuzione() Then
                Invia("CONFIG " & PercorsoConfigurazione())
                If conImpostazioni Then Invia("IMPOSTAZIONI")
                Return True
            End If
            Dim eseguibile As String = PercorsoEseguibile()
            If eseguibile = "" Then Return False
            ' Con /avvio il lettore non si rilancia come amministratore: la richiesta di conferma comparirebbe sul desktop sicuro,
            ' dove WinGabriel non parla e il lettore non è ancora attivo, e a ogni passaggio in secondo piano se ne accumulerebbe una.
            Dim argomenti As String = "/avvio " & """/config:" & PercorsoConfigurazione() & """"
            If conImpostazioni Then argomenti &= " /impostazioni"
            Dim info As New ProcessStartInfo(eseguibile, argomenti)
            info.UseShellExecute = False
            info.WorkingDirectory = Path.GetDirectoryName(eseguibile)
            Process.Start(info)
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Fa installare o togliere il lettore per la schermata di accesso, di blocco e per il controllo account utente. Il lettore chiede la conferma di amministratore. Restituisce il codice di uscita: 0 fatto, 1223 conferma negata.</summary>
    Friend Shared Function InstallaOTogliServizio(installa As Boolean) As Integer
        Try
            Dim eseguibile As String = PercorsoEseguibile()
            If eseguibile = "" Then Return -1
            Dim info As New ProcessStartInfo(eseguibile, If(installa, "/installa", "/disinstalla"))
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

    Friend Shared Sub Ricarica()
        Invia("RICARICA")
    End Sub

    ''' <summary>Chiude il lettore di schermo se è attivo: si esce con un altro lettore (NVDA, JAWS, Assistente vocale) e due insieme non vanno.</summary>
    Friend Shared Sub ChiudiSeAttivo()
        Try
            If InEsecuzione() Then Chiudi()
        Catch
        End Try
    End Sub

    Friend Shared Sub Chiudi()
        Invia("ESCI")
    End Sub

    Friend Shared Function AvvioAutomaticoAttivo() As Boolean
        Try
            Using chiave As RegistryKey = Registry.CurrentUser.OpenSubKey(ChiaveAvvio)
                Return chiave IsNot Nothing AndAlso chiave.GetValue(NomeVoceAvvio) IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Fa partire il lettore quando l'utente accede a Windows. All'accesso non chiede i privilegi di amministratore.</summary>
    Friend Shared Function ImpostaAvvioAutomatico(attivo As Boolean) As Boolean
        Try
            Dim eseguibile As String = PercorsoEseguibile()
            If attivo AndAlso eseguibile = "" Then Return False
            Using chiave As RegistryKey = Registry.CurrentUser.CreateSubKey(ChiaveAvvio)
                If attivo Then
                    chiave.SetValue(NomeVoceAvvio, """" & eseguibile & """ /avvio ""/config:" & PercorsoConfigurazione() & """")
                Else
                    chiave.DeleteValue(NomeVoceAvvio, False)
                End If
            End Using
            Return True
        Catch
            Return False
        End Try
    End Function

End Class
