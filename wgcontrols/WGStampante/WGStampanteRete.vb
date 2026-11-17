Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Public Class WGStampanteTrovata
    Public Property Id As String = ""
    Public Property Nome As String = ""
    Public Property Associata As Boolean = False
    Public Property Associabile As Boolean = True
    Public Property Categorie As String = ""
    Public Property Protocollo As String = ""
    Public Property Indirizzo As String = ""

    Public ReadOnly Property NomeMostrato As String
        Get
            If String.IsNullOrWhiteSpace(Nome) Then Return "Stampante senza nome"
            Return Nome.Trim()
        End Get
    End Property

    Public ReadOnly Property Descrizione As String
        Get
            Dim testo As String = NomeMostrato
            If Not String.IsNullOrWhiteSpace(Indirizzo) Then testo &= ", indirizzo " & Indirizzo.Trim()
            If Associata Then testo &= ", già aggiunta a questo computer"
            Return testo
        End Get
    End Property
End Class

Partial Public Class WGStampante
    Private Const PROTOCOLLO_WSD As String = "{782232aa-a2f9-4993-971b-aedc551346b0}"
    Private Const PROTOCOLLO_DNSSD As String = "{4526e8c1-8aac-4153-9b16-55e86ada0e54}"
    Private Const ATTESA_RICERCA_RETE_MS As Integer = 120000
    Private Const ATTESA_AGGIUNTA_RETE_MS As Integer = 200000
    Private Const ATTESA_CODA_NUOVA_MS As Integer = 90000
    Private Shared ReadOnly ParoleStampante As String() = {"print", "stampant", "laserjet", "deskjet", "officejet", "envy", "smart tank", "pixma", "maxify", "ecotank", "workforce", "expression", "brother", "epson", "canon", "kyocera", "ricoh", "xerox", "lexmark", "samsung", "konica", "sharp", "oki", "mfc-", "dcp-", "hl-"}

    Private Shared Function PreparazioneWinRT() As String
        Dim righe As String() = {
            "  $ErrorActionPreference = 'Stop'",
            "  $ProgressPreference = 'SilentlyContinue'",
            "  Add-Type -AssemblyName System.Runtime.WindowsRuntime",
            "  $metodoAsTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1",
            "  function Attendi($operazione, [Type]$tipo, [int]$millisecondi) {",
            "    $compito = $metodoAsTask.MakeGenericMethod($tipo).Invoke($null, @($operazione))",
            "    if (-not $compito.Wait($millisecondi)) { throw 'tempo scaduto' }",
            "    return $compito.Result",
            "  }",
            "  function Codifica($valore) {",
            "    if ($null -eq $valore) { $valore = '' }",
            "    return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$valore))",
            "  }",
            "  $null = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType = WindowsRuntime]",
            "  $null = [Windows.Devices.Enumeration.DeviceInformationCollection, Windows.Devices.Enumeration, ContentType = WindowsRuntime]",
            "  $null = [Windows.Devices.Enumeration.DevicePairingResult, Windows.Devices.Enumeration, ContentType = WindowsRuntime]",
            "  $null = [Windows.Devices.Enumeration.DeviceInformationKind, Windows.Devices.Enumeration, ContentType = WindowsRuntime]"
        }
        Return String.Join(vbLf, righe)
    End Function

    Private Shared Function RigaErrorePowerShell() As String
        Return "  'ERRORE ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$_.Exception.Message))"
    End Function

    Private Shared Function ScriptRicercaStampanti() As String
        Dim righe As New List(Of String)()
        righe.Add("try {")
        righe.Add(PreparazioneWinRT())
        righe.Add("  $proprieta = New-Object 'System.Collections.Generic.List[string]'")
        righe.Add("  $proprieta.Add('System.Devices.Aep.Category')")
        righe.Add("  $proprieta.Add('System.Devices.Aep.DeviceAddress')")
        righe.Add("  $tipoAep = [Windows.Devices.Enumeration.DeviceInformationKind]::AssociationEndpoint")
        righe.Add("  foreach ($protocollo in @('" & PROTOCOLLO_WSD & "', '" & PROTOCOLLO_DNSSD & "')) {")
        righe.Add("    try {")
        righe.Add("      $selettore = 'System.Devices.Aep.ProtocolId:=""' + $protocollo + '""'")
        righe.Add("      $elenco = Attendi ([Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($selettore, $proprieta, $tipoAep)) ([Windows.Devices.Enumeration.DeviceInformationCollection]) 45000")
        righe.Add("      foreach ($dispositivo in $elenco) {")
        righe.Add("        $categorie = ''")
        righe.Add("        $indirizzo = ''")
        righe.Add("        try { $categorie = (@($dispositivo.Properties['System.Devices.Aep.Category']) | ForEach-Object { [string]$_ }) -join ';' } catch { $categorie = '' }")
        righe.Add("        try { $indirizzo = [string]$dispositivo.Properties['System.Devices.Aep.DeviceAddress'] } catch { $indirizzo = '' }")
        righe.Add("        'STAMPANTE ' + (Codifica $dispositivo.Id) + ' ' + (Codifica $dispositivo.Name) + ' ' + (Codifica $dispositivo.Pairing.IsPaired) + ' ' + (Codifica $dispositivo.Pairing.CanPair) + ' ' + (Codifica $categorie) + ' ' + (Codifica $protocollo) + ' ' + (Codifica $indirizzo)")
        righe.Add("      }")
        righe.Add("    } catch {")
        righe.Add("      'AVVISO ' + (Codifica $_.Exception.Message)")
        righe.Add("    }")
        righe.Add("  }")
        righe.Add("  'FINE'")
        righe.Add("} catch {")
        righe.Add(RigaErrorePowerShell())
        righe.Add("}")
        Return String.Join(vbLf, righe)
    End Function

    Private Shared Function ScriptAggiuntaStampante(id As String) As String
        Dim idCodificato As String = Convert.ToBase64String(Encoding.UTF8.GetBytes(If(id, "")))
        Dim righe As New List(Of String)()
        righe.Add("try {")
        righe.Add(PreparazioneWinRT())
        righe.Add("  $id = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" & idCodificato & "'))")
        righe.Add("  $proprieta = New-Object 'System.Collections.Generic.List[string]'")
        righe.Add("  $tipoAep = [Windows.Devices.Enumeration.DeviceInformationKind]::AssociationEndpoint")
        righe.Add("  $dispositivo = Attendi ([Windows.Devices.Enumeration.DeviceInformation]::CreateFromIdAsync($id, $proprieta, $tipoAep)) ([Windows.Devices.Enumeration.DeviceInformation]) 30000")
        righe.Add("  if ($dispositivo.Pairing.IsPaired) {")
        righe.Add("    'ESITO AlreadyPaired'")
        righe.Add("  } else {")
        righe.Add("    $risultato = Attendi ($dispositivo.Pairing.PairAsync()) ([Windows.Devices.Enumeration.DevicePairingResult]) 180000")
        righe.Add("    'ESITO ' + [string]$risultato.Status")
        righe.Add("  }")
        righe.Add("} catch {")
        righe.Add(RigaErrorePowerShell())
        righe.Add("}")
        Return String.Join(vbLf, righe)
    End Function

    Private Shared Function EseguiPowerShellNascosto(script As String, attesaMs As Integer, ByRef righe As List(Of String)) As Boolean
        righe = New List(Of String)()
        Dim processo As Process = Nothing
        Try
            Dim percorso As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            If Not File.Exists(percorso) Then percorso = "powershell.exe"
            Dim codificato As String = Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
            Dim avvio As New ProcessStartInfo(percorso, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " & codificato)
            avvio.UseShellExecute = False
            avvio.CreateNoWindow = True
            avvio.WindowStyle = ProcessWindowStyle.Hidden
            avvio.RedirectStandardOutput = True
            avvio.RedirectStandardError = True
            processo = Process.Start(avvio)
            If processo Is Nothing Then Return False
            Dim uscita As Task(Of String) = processo.StandardOutput.ReadToEndAsync()
            Dim errori As Task(Of String) = processo.StandardError.ReadToEndAsync()
            If Not processo.WaitForExit(attesaMs) Then
                Try
                    processo.Kill()
                Catch exChiusura As Exception
                    ScriviLog("EseguiPowerShellNascosto chiusura: " & exChiusura.Message)
                End Try
                ScriviLog("EseguiPowerShellNascosto: tempo scaduto")
                Return False
            End If
            processo.WaitForExit()
            Dim testo As String = ""
            If uscita.Wait(10000) Then testo = If(uscita.Result, "")
            For Each riga As String In testo.Split(New Char() {ChrW(13), ChrW(10)}, StringSplitOptions.RemoveEmptyEntries)
                righe.Add(riga)
            Next
            Dim testoErrori As String = ""
            If errori.Wait(5000) Then testoErrori = If(errori.Result, "")
            If Not String.IsNullOrWhiteSpace(testoErrori) Then ScriviLog("EseguiPowerShellNascosto errori: " & testoErrori.Trim())
            Return True
        Catch ex As Exception
            ScriviLog("EseguiPowerShellNascosto: " & ex.ToString())
            Return False
        Finally
            Try
                If processo IsNot Nothing Then processo.Dispose()
            Catch exRilascio As Exception
                ScriviLog("EseguiPowerShellNascosto rilascio: " & exRilascio.Message)
            End Try
        End Try
    End Function

    Private Shared Function DecodificaBase64(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return Encoding.UTF8.GetString(Convert.FromBase64String(testo))
        Catch ex As Exception
            ScriviLog("DecodificaBase64: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ParteRiga(parti As String(), indice As Integer) As String
        If parti Is Nothing OrElse indice < 0 OrElse indice >= parti.Length Then Return ""
        Return DecodificaBase64(parti(indice))
    End Function

    Private Shared Function SembraStampante(trovata As WGStampanteTrovata) As Boolean
        Try
            If trovata Is Nothing Then Return False
            Dim categorie As String = If(trovata.Categorie, "").ToLowerInvariant()
            If Not String.IsNullOrWhiteSpace(categorie) Then Return categorie.Contains("print")
            Dim nome As String = If(trovata.Nome, "").ToLowerInvariant()
            For Each parola As String In ParoleStampante
                If nome.Contains(parola) Then Return True
            Next
            Return False
        Catch ex As Exception
            ScriviLog("SembraStampante: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function CercaStampantiRete(ByRef errore As String) As List(Of WGStampanteTrovata)
        errore = ""
        Dim risultato As New List(Of WGStampanteTrovata)()
        Try
            Dim righe As List(Of String) = Nothing
            If Not EseguiPowerShellNascosto(ScriptRicercaStampanti(), ATTESA_RICERCA_RETE_MS, righe) Then
                errore = "La ricerca delle stampanti in rete non ha risposto in tempo."
                Return risultato
            End If
            Dim idVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim nomiVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim fineTrovata As Boolean = False
            For Each riga As String In righe
                Dim parti As String() = riga.Split(" "c)
                If parti.Length = 0 Then Continue For
                Select Case parti(0).Trim()
                    Case "STAMPANTE"
                        Dim trovata As New WGStampanteTrovata()
                        trovata.Id = ParteRiga(parti, 1)
                        trovata.Nome = ParteRiga(parti, 2).Trim()
                        trovata.Associata = String.Equals(ParteRiga(parti, 3), "True", StringComparison.OrdinalIgnoreCase)
                        trovata.Associabile = String.Equals(ParteRiga(parti, 4), "True", StringComparison.OrdinalIgnoreCase)
                        trovata.Categorie = ParteRiga(parti, 5)
                        trovata.Protocollo = ParteRiga(parti, 6)
                        trovata.Indirizzo = ParteRiga(parti, 7).Trim()
                        If String.IsNullOrWhiteSpace(trovata.Id) OrElse Not idVisti.Add(trovata.Id) Then Continue For
                        If Not SembraStampante(trovata) Then Continue For
                        If Not nomiVisti.Add(trovata.NomeMostrato) Then Continue For
                        risultato.Add(trovata)
                    Case "AVVISO"
                        ScriviLog("CercaStampantiRete avviso: " & ParteRiga(parti, 1))
                    Case "ERRORE"
                        ScriviLog("CercaStampantiRete errore: " & ParteRiga(parti, 1))
                        errore = "La ricerca delle stampanti in rete non è riuscita."
                    Case "FINE"
                        fineTrovata = True
                End Select
            Next
            If Not fineTrovata AndAlso String.IsNullOrEmpty(errore) AndAlso risultato.Count = 0 Then errore = "La ricerca delle stampanti in rete non è riuscita."
            risultato.Sort(Function(prima As WGStampanteTrovata, seconda As WGStampanteTrovata) String.Compare(prima.NomeMostrato, seconda.NomeMostrato, StringComparison.CurrentCultureIgnoreCase))
        Catch ex As Exception
            ScriviLog("CercaStampantiRete: " & ex.ToString())
            errore = "La ricerca delle stampanti in rete non è riuscita."
        End Try
        Return risultato
    End Function

    Private Shared Function NomiStampantiInstallate() As HashSet(Of String)
        Dim nomi As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim visti As New Dictionary(Of String, InfoStampante)(StringComparer.OrdinalIgnoreCase)
            AggiungiStampantiEnumerate(visti, "", PRINTER_ENUM_LOCAL Or PRINTER_ENUM_CONNECTIONS, 4)
            SyncLock _lockElenco
                For Each nome As String In visti.Keys
                    nomi.Add(nome)
                Next
            End SyncLock
        Catch ex As Exception
            ScriviLog("NomiStampantiInstallate: " & ex.Message)
        End Try
        Return nomi
    End Function

    Private Shared Function NormalizzaPerConfronto(testo As String) As String
        Try
            Dim risultato As New StringBuilder()
            For Each carattere As Char In If(testo, "").ToLowerInvariant()
                If Char.IsLetterOrDigit(carattere) Then risultato.Append(carattere)
            Next
            Return risultato.ToString()
        Catch ex As Exception
            ScriviLog("NormalizzaPerConfronto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function NomiSimili(primo As String, secondo As String) As Boolean
        Try
            Dim a As String = NormalizzaPerConfronto(primo)
            Dim b As String = NormalizzaPerConfronto(secondo)
            If a.Length < 4 OrElse b.Length < 4 Then Return False
            If a.Contains(b) OrElse b.Contains(a) Then Return True
            Dim lunghezza As Integer = Math.Min(12, Math.Min(a.Length, b.Length))
            Return a.Substring(0, lunghezza) = b.Substring(0, lunghezza)
        Catch ex As Exception
            ScriviLog("NomiSimili: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function AttendiNuovaStampante(prima As HashSet(Of String), nomeAtteso As String, attesaMs As Integer) As String
        Try
            Dim limite As DateTime = DateTime.UtcNow.AddMilliseconds(attesaMs)
            Do
                Dim attuali As HashSet(Of String) = NomiStampantiInstallate()
                Dim nuove As List(Of String) = attuali.Where(Function(nome As String) Not prima.Contains(nome)).ToList()
                If nuove.Count > 0 Then
                    Dim corrispondente As String = nuove.FirstOrDefault(Function(nome As String) NomiSimili(nome, nomeAtteso))
                    Return If(corrispondente, nuove(0))
                End If
                Dim esistente As String = attuali.FirstOrDefault(Function(nome As String) NomiSimili(nome, nomeAtteso))
                If Not String.IsNullOrEmpty(esistente) Then Return esistente
                Thread.Sleep(3000)
            Loop While DateTime.UtcNow < limite
        Catch ex As Exception
            ScriviLog("AttendiNuovaStampante: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Shared Function MessaggioEsitoAggiunta(esito As String) As String
        Select Case If(esito, "")
            Case "NotReadyToPair", "HardwareFailure", "ConnectionRejected"
                Return "La stampante non ha risposto. Controlla che sia accesa e collegata alla stessa rete del computer, poi riprova."
            Case "AccessDenied", "AuthenticationNotAllowed"
                Return "Questo account del computer non ha il permesso di aggiungere stampanti. Avvia WinGabriel come amministratore e riprova."
            Case "AuthenticationTimeout", "AuthenticationFailure", "RequiredHandlerNotRegistered", "RejectedByHandler", "ProtectionLevelCouldNotBeMet", "InvalidCeremonyData"
                Return "La stampante chiede una conferma o un codice che non si può dare da WinGabriel. Se sulla stampante c'è una richiesta di conferma, accettala e riprova."
            Case "OperationAlreadyInProgress"
                Return "L'aggiunta di questa stampante è già in corso. Aspetta qualche istante e controlla con Scegli stampante."
            Case "NoSupportedProfiles"
                Return "Questa stampante non si può aggiungere automaticamente."
            Case ""
                Return "L'aggiunta della stampante non è riuscita."
        End Select
        Return "L'aggiunta della stampante non è riuscita (" & esito & ")."
    End Function

    Public Shared Function AggiungiStampanteTrovata(stampante As WGStampanteTrovata, ByRef nomeInstallato As String, ByRef errore As String) As Boolean
        nomeInstallato = ""
        errore = ""
        Try
            If stampante Is Nothing OrElse String.IsNullOrWhiteSpace(stampante.Id) Then
                errore = "La stampante scelta non è valida."
                Return False
            End If
            Dim prima As HashSet(Of String) = NomiStampantiInstallate()
            Dim righe As List(Of String) = Nothing
            If Not EseguiPowerShellNascosto(ScriptAggiuntaStampante(stampante.Id), ATTESA_AGGIUNTA_RETE_MS, righe) Then
                errore = "L'aggiunta della stampante non ha risposto in tempo."
                Return False
            End If
            Dim esito As String = ""
            For Each riga As String In righe
                Dim parti As String() = riga.Split(" "c)
                If parti.Length = 0 Then Continue For
                Select Case parti(0).Trim()
                    Case "ESITO"
                        If parti.Length > 1 Then esito = parti(1).Trim()
                    Case "ERRORE"
                        ScriviLog("AggiungiStampanteTrovata errore: " & ParteRiga(parti, 1))
                End Select
            Next
            ScriviLog("AggiungiStampanteTrovata esito per " & stampante.NomeMostrato & ": " & esito)
            If Not String.Equals(esito, "Paired", StringComparison.OrdinalIgnoreCase) AndAlso Not String.Equals(esito, "AlreadyPaired", StringComparison.OrdinalIgnoreCase) Then
                errore = MessaggioEsitoAggiunta(esito)
                Return False
            End If
            nomeInstallato = AttendiNuovaStampante(prima, stampante.Nome, ATTESA_CODA_NUOVA_MS)
            Return True
        Catch ex As Exception
            ScriviLog("AggiungiStampanteTrovata: " & ex.ToString())
            errore = "L'aggiunta della stampante non è riuscita."
            Return False
        End Try
    End Function
End Class
