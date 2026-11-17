Imports System.ComponentModel
Imports System.IO
Imports System.Threading
Imports WGLettoreSchermo

''' <summary>
''' Le opzioni sulla riga di comando: /impostazioni, /esci, /avvio, /nonelevare, /config:percorso,
''' /installa, /installa:portatile, /installa:sistema, /disinstalla.
''' </summary>
Friend NotInheritable Class OpzioniAvvio

    Public Impostazioni As Boolean
    Public Esci As Boolean
    Public Avvio As Boolean
    Public Sicuro As Boolean
    Public Installa As Boolean
    Public InstallaPortatile As Boolean
    Public InstallaSistema As Boolean
    Public SenzaAvvio As Boolean
    Public SenzaComponenti As Boolean
    Public AttendiPid As Integer
    Public Disinstalla As Boolean
    Public AvvioAuto As String = ""
    Public NonElevare As Boolean
    Public PercorsoConfig As String = ""

    Public Sub New(argomenti As String())
        For Each argomento As String In argomenti
            Dim testo As String = If(argomento, "").Trim()
            Dim nome As String = testo.TrimStart("/"c, "-"c)
            Select Case nome.ToLowerInvariant()
                Case "impostazioni" : Impostazioni = True
                Case "esci" : Esci = True
                Case "avvio" : Avvio = True
                Case "sicuro" : Sicuro = True
                Case "installa" : Installa = True
                Case "installa:portatile" : Installa = True : InstallaPortatile = True
                Case "installa:sistema" : Installa = True : InstallaSistema = True
                Case "installa:sistema-senzaavvio" : Installa = True : InstallaSistema = True : SenzaAvvio = True
                Case "disinstalla" : Disinstalla = True
                Case "nonelevare" : NonElevare = True
                Case "senzacomponenti" : SenzaComponenti = True
                Case Else
                    If nome.StartsWith("config:", StringComparison.OrdinalIgnoreCase) Then PercorsoConfig = nome.Substring(7).Trim(""""c)
                    If nome.StartsWith("avvioauto:", StringComparison.OrdinalIgnoreCase) Then AvvioAuto = nome.Substring(10).Trim().ToLowerInvariant()
                    If nome.StartsWith("attendi:", StringComparison.OrdinalIgnoreCase) Then Integer.TryParse(nome.Substring(8).Trim(), AttendiPid)
            End Select
        Next
    End Sub

    ''' <summary>Gli argomenti da ripassare quando il programma si rilancia con i privilegi di amministratore.</summary>
    Public Function Argomenti() As String
        Dim testo As String = ""
        If Impostazioni Then testo &= " /impostazioni"
        If Sicuro Then testo &= " /sicuro"
        If Avvio Then testo &= " /avvio"
        If SenzaComponenti Then testo &= " /senzacomponenti"
        If PercorsoConfig <> "" Then testo &= " ""/config:" & PercorsoConfig & """"
        Return testo.Trim()
    End Function

End Class

''' <summary>Il punto di ingresso del lettore di schermo: legge la riga di comando e avvia il programma, l'installazione o la procedura guidata.</summary>
Friend Module Program

    <STAThread>
    Public Function Main(argomenti As String()) As Integer
        Dim opzioni As New OpzioniAvvio(argomenti)
        If opzioni.SenzaComponenti Then ComponentiAggiuntivi.SenzaComponenti = True
        ' Dopo un riavvio si aspetta che la copia di prima sia uscita, altrimenti questa la prenderebbe per un lettore già attivo.
        If opzioni.AttendiPid > 0 Then
            Try
                Using precedente As Process = Process.GetProcessById(opzioni.AttendiPid)
                    precedente.WaitForExit(15000)
                End Using
            Catch exAttesa As Exception
                Trace.WriteLine("Program: " & exAttesa.Message)
            End Try
        End If

        If opzioni.Installa Then
            If opzioni.InstallaPortatile Then Return Installazione.InstallaPortatile()
            If opzioni.InstallaSistema Then Return Installazione.InstallaServizio(Not opzioni.SenzaAvvio)
            Return InstallazioneGuidata.Esegui()
        End If
        If opzioni.Disinstalla Then Return Installazione.DisinstallaServizio()
        If opzioni.AvvioAuto = "on" OrElse opzioni.AvvioAuto = "off" Then Return Installazione.CambiaAvvioConAttivita(opzioni.AvvioAuto = "on")

        ' Il lettore è già attivo: gli si passa il comando e si esce.
        If ControlloLettore.InEsecuzione() Then
            If opzioni.Esci Then
                ControlloLettore.Invia("ESCI")
            Else
                If opzioni.PercorsoConfig <> "" Then ControlloLettore.Invia("CONFIG " & opzioni.PercorsoConfig)
                If opzioni.Impostazioni Then ControlloLettore.Invia("IMPOSTAZIONI")
            End If
            Return 0
        End If
        If opzioni.Esci Then Return 0

        ' Chiede i privilegi di amministratore, così il lettore può leggere anche le applicazioni avviate come
        ' amministratore. Se l'utente non conferma, il lettore parte lo stesso, con i privilegi normali.
        Dim senzaPrivilegi As Boolean = False
        ' All'accesso a Windows (/avvio) non si chiede niente: una finestra di conferma a quel punto non si potrebbe leggere.
        If Not NativoApp.Elevato() AndAlso Not opzioni.NonElevare AndAlso Not opzioni.Avvio AndAlso Not opzioni.Sicuro Then
            Try
                Dim info As New ProcessStartInfo(Application.ExecutablePath, opzioni.Argomenti() & " /nonelevare")
                info.UseShellExecute = True
                info.Verb = "runas"
                Process.Start(info)
                Return 0
            Catch ex As Win32Exception
                senzaPrivilegi = True
            Catch
                senzaPrivilegi = True
            End Try
        End If

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Dim contesto As ApplicazioneLettore = Nothing
        Try
            contesto = New ApplicazioneLettore(opzioni, senzaPrivilegi)
            contesto.Avvia()
            Application.Run(contesto)
        Finally
            If contesto IsNot Nothing Then contesto.Termina()
        End Try
        Return 0
    End Function

End Module
