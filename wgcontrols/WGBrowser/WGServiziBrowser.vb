Imports System.Collections.Generic
Imports System.Device.Location
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Win32
Imports Newtonsoft.Json.Linq
Imports WGBrowserManager
Imports wgcontrols

Public NotInheritable Class WGIdentitaBrowser
    Private Shared ReadOnly log As New Logger()
    Private Shared _versionePiattaforma As String = Nothing

    Private Const VersionePrincipaleRiserva As Integer = 152

    Private Sub New()
    End Sub

    Public Shared Function LingueAccettate() As String
        Try
            Return WGImpostazioniBrowser.LingueAccettate()
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.LingueAccettate: " & ex.Message)
            Return "it-IT,it;q=0.9,en-US;q=0.8,en;q=0.7"
        End Try
    End Function

    Public Shared Function VersioneCompletaMotore() As String
        Try
            Dim versione As String = WGMotoreBrowser.VersioneMotore()
            If Not String.IsNullOrWhiteSpace(versione) Then Return versione
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.VersioneCompletaMotore: " & ex.Message)
        End Try
        Return VersionePrincipaleRiserva.ToString() & ".0.0.0"
    End Function

    Public Shared Function VersionePrincipaleMotore() As Integer
        Try
            Dim versione As String = VersioneCompletaMotore()
            Dim punto As Integer = versione.IndexOf("."c)
            Dim numero As Integer
            If Integer.TryParse(If(punto > 0, versione.Substring(0, punto), versione), numero) AndAlso numero > 0 Then Return numero
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.VersionePrincipaleMotore: " & ex.Message)
        End Try
        Return VersionePrincipaleRiserva
    End Function

    Public Shared Function UserAgentChrome() As String
        Return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" & VersionePrincipaleMotore() & ".0.0.0 Safari/537.36"
    End Function

    Public Shared Function UserAgentFirefox() As String
        Try
            Dim giorni As Double = (DateTime.UtcNow - New DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc)).TotalDays
            Dim versione As Integer = 156 + Math.Max(0, CInt(Math.Floor(giorni / 28)))
            Return "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:" & versione & ".0) Gecko/20100101 Firefox/" & versione & ".0"
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.UserAgentFirefox: " & ex.Message)
            Return "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:156.0) Gecko/20100101 Firefox/156.0"
        End Try
    End Function

    Public Shared Function UserAgentEdge() As String
        Dim principale As String = VersionePrincipaleMotore().ToString()
        Return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/" & principale & ".0.0.0 Safari/537.36 Edg/" & principale & ".0.0.0"
    End Function

    Public Shared Function UserAgent(identita As String) As String
        Try
            Select Case If(identita, "")
                Case WGImpostazioniBrowser.IdentitaFirefox : Return UserAgentFirefox()
                Case WGImpostazioniBrowser.IdentitaEdge : Return UserAgentEdge()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.UserAgent: " & ex.Message)
        End Try
        Return UserAgentChrome()
    End Function

    Private Shared Function Marchio(nome As String, versione As String) As JObject
        Try
            Return New JObject(New JProperty("brand", If(nome, "")), New JProperty("version", If(versione, "")))
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.Marchio: " & ex.Message)
            Return New JObject()
        End Try
    End Function

    Public Shared Function MarchiBrowser(identita As String, principale As Integer, versione As String, versioneCompleta As Boolean) As JArray
        Dim risultato As New JArray()
        Try
            Dim seme As Integer = Math.Max(0, principale)
            Dim ordini As Integer()() = {New Integer() {0, 1, 2}, New Integer() {0, 2, 1}, New Integer() {1, 0, 2}, New Integer() {1, 2, 0}, New Integer() {2, 0, 1}, New Integer() {2, 1, 0}}
            Dim ordine As Integer() = ordini(seme Mod ordini.Length)
            Dim caratteri As String() = {" ", "(", ":", "-", ".", "/", ")", ";", "=", "?", "_"}
            Dim versioniFinte As String() = {"8", "99", "24"}
            Dim marchioFinto As String = "Not" & caratteri(seme Mod caratteri.Length) & "A" & caratteri((seme + 1) Mod caratteri.Length) & "Brand"
            Dim versioneFinta As String = versioniFinte(seme Mod versioniFinte.Length)
            If versioneCompleta Then versioneFinta &= ".0.0.0"
            Dim elenco(2) As JObject
            elenco(ordine(0)) = Marchio(marchioFinto, versioneFinta)
            elenco(ordine(1)) = Marchio("Chromium", versione)
            elenco(ordine(2)) = Marchio(If(identita = WGImpostazioniBrowser.IdentitaEdge, "Microsoft Edge", "Google Chrome"), versione)
            For Each elemento As JObject In elenco
                If elemento IsNot Nothing Then risultato.Add(elemento)
            Next
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.MarchiBrowser: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Shared Function IntestazioneMarchi(identita As String, principale As Integer) As String
        Try
            Dim parti As New List(Of String)()
            For Each voce As JToken In MarchiBrowser(identita, principale, principale.ToString(), False)
                Dim o As JObject = TryCast(voce, JObject)
                If o Is Nothing Then Continue For
                parti.Add("""" & If(o.Value(Of String)("brand"), "").Replace("""", "") & """;v=""" & If(o.Value(Of String)("version"), "").Replace("""", "") & """")
            Next
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.IntestazioneMarchi: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function VersionePiattaformaWindows() As String
        Try
            If _versionePiattaforma IsNot Nothing Then Return _versionePiattaforma
            Dim build As Integer = 0
            Using chiave As RegistryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\Microsoft\Windows NT\CurrentVersion")
                If chiave IsNot Nothing Then Integer.TryParse(Convert.ToString(chiave.GetValue("CurrentBuildNumber", "")), build)
            End Using
            Dim valore As String
            If build >= 26100 Then
                valore = "19.0.0"
            ElseIf build >= 22621 Then
                valore = "15.0.0"
            ElseIf build >= 22000 Then
                valore = "14.0.0"
            ElseIf build >= 19041 Then
                valore = "10.0.0"
            ElseIf build >= 18362 Then
                valore = "8.0.0"
            ElseIf build >= 17763 Then
                valore = "7.0.0"
            ElseIf build >= 17134 Then
                valore = "6.0.0"
            ElseIf build >= 16299 Then
                valore = "5.0.0"
            ElseIf build >= 15063 Then
                valore = "4.0.0"
            ElseIf build >= 14393 Then
                valore = "3.0.0"
            ElseIf build >= 10586 Then
                valore = "2.0.0"
            ElseIf build >= 10240 Then
                valore = "1.0.0"
            ElseIf build > 0 Then
                valore = "0.0.0"
            Else
                valore = "10.0.0"
            End If
            _versionePiattaforma = valore
            Return valore
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.VersionePiattaformaWindows: " & ex.Message)
            Return "10.0.0"
        End Try
    End Function

    Public Shared Function ParametriOverride(identita As String) As JObject
        Dim parametri As New JObject()
        Try
            parametri("userAgent") = UserAgent(identita)
            parametri("acceptLanguage") = LingueAccettate()
            parametri("platform") = "Win32"
            If identita <> WGImpostazioniBrowser.IdentitaFirefox Then
                Dim numeroPrincipale As Integer = VersionePrincipaleMotore()
                Dim principale As String = numeroPrincipale.ToString()
                Dim completa As String = VersioneCompletaMotore()
                Dim metadati As New JObject()
                metadati("brands") = MarchiBrowser(identita, numeroPrincipale, principale, False)
                metadati("fullVersionList") = MarchiBrowser(identita, numeroPrincipale, completa, True)
                metadati("fullVersion") = completa
                metadati("platform") = "Windows"
                metadati("platformVersion") = VersionePiattaformaWindows()
                metadati("architecture") = If(WGBrowser.ArchitetturaArm, "arm", "x86")
                metadati("model") = ""
                metadati("mobile") = False
                metadati("bitness") = If(Environment.Is64BitProcess, "64", "32")
                metadati("wow64") = Not Environment.Is64BitProcess AndAlso Environment.Is64BitOperatingSystem
                metadati("formFactors") = New JArray("Desktop")
                parametri("userAgentMetadata") = metadati
            End If
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.ParametriOverride: " & ex.Message)
        End Try
        Return parametri
    End Function

    Public Shared Function IdentitaPerIndirizzo(indirizzo As String) As String
        Try
            Dim host As String = WGArchivioBrowser.HostDi(indirizzo)
            If host = "" Then
                Dim predefinita As String = WGImpostazioniBrowser.IdentitaPredefinita
                Return If(WGImpostazioniBrowser.EIdentitaFissa(predefinita), predefinita, WGImpostazioniBrowser.IdentitaChrome)
            End If
            Return WGImpostazioniBrowser.IdentitaPerSito(host)
        Catch ex As Exception
            log.ScriviLog("WGIdentitaBrowser.IdentitaPerIndirizzo: " & ex.Message)
            Return WGImpostazioniBrowser.IdentitaChrome
        End Try
    End Function

    Public Const ScriptAntiRilevamento As String =
        "(function(){try{var ff=/Firefox\//.test(navigator.userAgent);var P=Navigator.prototype;" &
        "var def=function(n,v){try{Object.defineProperty(P,n,{get:function(){return v;},configurable:true,enumerable:true});}catch(e){}};" &
        "try{if(navigator.webdriver){def('webdriver',false);}}catch(e){}" &
        "if(ff){def('vendor','');def('productSub','20100101');def('oscpu','Windows NT 10.0; Win64; x64');def('buildID','20181001000000');" &
        "try{delete P.userAgentData;}catch(e){}try{delete window.chrome;}catch(e){}" &
        "try{if(window.chrome){Object.defineProperty(window,'chrome',{value:undefined,configurable:true,writable:true});}}catch(e){}}" &
        "else{try{var c=window.chrome;if(!c){c={runtime:{}};window.chrome=c;}" &
        "if(!c.app){c.app={isInstalled:false};}if(!c.loadTimes){c.loadTimes=function(){};}if(!c.csi){c.csi=function(){};}}catch(e){}}" &
        "}catch(e){}})();"

    Public Const ScriptNascondiPonte As String =
        "(function(){try{if(!window.__wgAgente||!window.__wgAgente.ponteCatturato)return;" &
        "var c=window.chrome;if(!c||!c.webview)return;" &
        "try{delete c.webview;}catch(e){}" &
        "if(c.webview){try{Object.defineProperty(c,'webview',{value:undefined,configurable:true,writable:true});}catch(e){}}" &
        "}catch(e){}})();"
End Class

Public NotInheritable Class WGCondivisioneSchermo
    Public Class Sorgente
        Public Property Id As String = ""
        Public Property Descrizione As String = ""
        Public Property Schermo As Boolean = False
        Public Property Finestra As IntPtr = IntPtr.Zero
    End Class

    Private Shared ReadOnly log As New Logger()

    Private Delegate Function ProceduraFinestre(hWnd As IntPtr, parametro As IntPtr) As Boolean

    <DllImport("user32.dll")>
    Private Shared Function EnumWindows(procedura As ProceduraFinestre, parametro As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsIconic(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetWindowTextLengthW(hWnd As IntPtr) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetWindowTextW(hWnd As IntPtr, testo As StringBuilder, massimo As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, SetLastError:=True, EntryPoint:="SendMessageTimeoutW")>
    Private Shared Function SendMessageTimeoutTesto(hWnd As IntPtr, messaggio As UInteger, wParam As IntPtr, lParam As String, opzioni As UInteger, attesa As UInteger, ByRef risultato As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="PostMessageW")>
    Private Shared Function PostMessage(hWnd As IntPtr, messaggio As UInteger, wParam As IntPtr, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindow(hWnd As IntPtr, comando As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindow(hWnd As IntPtr, comando As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowLongW")>
    Private Shared Function GetWindowLong(hWnd As IntPtr, indice As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef processo As UInteger) As UInteger
    End Function

    <DllImport("dwmapi.dll")>
    Private Shared Function DwmGetWindowAttribute(hWnd As IntPtr, attributo As Integer, ByRef valore As Integer, dimensione As Integer) As Integer
    End Function

    Private Const GW_OWNER As UInteger = 4
    Private Const GWL_EXSTYLE As Integer = -20
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WS_EX_APPWINDOW As Integer = &H40000
    Private Const DWMWA_CLOAKED As Integer = 14
    Private Const WM_SETTEXT As UInteger = &HC
    Private Const WM_CLOSE As UInteger = &H10
    Private Const SMTO_ABORTIFHUNG As UInteger = &H2
    Private Const SW_HIDE As Integer = 0
    Private Const AttesaMessaggio As UInteger = 1000

    Public Const IdSchermoIntero As String = "schermo"

    Private Sub New()
    End Sub

    Public Shared Function Sorgenti() As List(Of Sorgente)
        Dim risultato As New List(Of Sorgente)()
        Try
            Dim numeroSchermi As Integer = Screen.AllScreens.Length
            Dim descrizione As String = If(numeroSchermi <= 1, "Schermo intero", "Schermo intero, uno dei " & numeroSchermi & " schermi collegati, di solito il principale")
            risultato.Add(New Sorgente() With {.Id = IdSchermoIntero, .Descrizione = descrizione, .Schermo = True})
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.Sorgenti schermi: " & ex.Message)
            If risultato.Count = 0 Then risultato.Add(New Sorgente() With {.Id = IdSchermoIntero, .Descrizione = "Schermo intero", .Schermo = True})
        End Try
        Try
            If Not WGMotoreBrowser.FinestreCondivisibili Then Return risultato
            Dim processoCorrente As UInteger = CUInt(Process.GetCurrentProcess().Id)
            Dim finestre As New List(Of Sorgente)()
            Dim procedura As ProceduraFinestre = Function(hWnd As IntPtr, parametro As IntPtr) As Boolean
                                                     Try
                                                         Dim finestra As Sorgente = DescriviFinestra(hWnd, processoCorrente)
                                                         If finestra IsNot Nothing Then finestre.Add(finestra)
                                                     Catch exFinestra As Exception
                                                         log.ScriviLog("WGCondivisioneSchermo.Sorgenti finestra: " & exFinestra.Message)
                                                     End Try
                                                     Return True
                                                 End Function
            EnumWindows(procedura, IntPtr.Zero)
            GC.KeepAlive(procedura)
            risultato.AddRange(finestre)
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.Sorgenti finestre: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function DescriviFinestra(hWnd As IntPtr, processoCorrente As UInteger) As Sorgente
        Try
            If Not IsWindowVisible(hWnd) Then Return Nothing
            Dim stile As Integer = GetWindowLong(hWnd, GWL_EXSTYLE)
            Dim finestraApplicazione As Boolean = (stile And WS_EX_APPWINDOW) <> 0
            If Not finestraApplicazione AndAlso GetWindow(hWnd, GW_OWNER) <> IntPtr.Zero Then Return Nothing
            If (stile And WS_EX_TOOLWINDOW) <> 0 AndAlso Not finestraApplicazione Then Return Nothing
            Dim nascosta As Integer = 0
            If DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, nascosta, 4) = 0 AndAlso nascosta <> 0 Then Return Nothing
            Dim testoTitolo As String = Titolo(hWnd).Trim()
            If testoTitolo = "" OrElse testoTitolo = "Program Manager" Then Return Nothing
            If testoTitolo.Contains(WGMotoreBrowser.MarcatoreFinestraCondivisa) Then testoTitolo = testoTitolo.Replace(WGMotoreBrowser.MarcatoreFinestraCondivisa, "").Trim()
            Dim proprietario As UInteger = 0
            GetWindowThreadProcessId(hWnd, proprietario)
            Dim descrizione As String = If(proprietario = processoCorrente, "Finestra di WinGabriel, con la pagina del browser: " & testoTitolo, "Finestra " & testoTitolo)
            If IsIconic(hWnd) Then descrizione &= ", ridotta a icona: potrebbe apparire vuota"
            Return New Sorgente() With {.Id = "finestra:" & hWnd.ToInt64().ToString(), .Descrizione = descrizione, .Finestra = hWnd}
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.DescriviFinestra: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Function Titolo(hWnd As IntPtr) As String
        Try
            If hWnd = IntPtr.Zero OrElse Not IsWindow(hWnd) Then Return ""
            Dim lunghezza As Integer = GetWindowTextLengthW(hWnd)
            If lunghezza <= 0 Then Return ""
            Dim testo As New StringBuilder(lunghezza + 2)
            GetWindowTextW(hWnd, testo, testo.Capacity)
            Return testo.ToString()
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.Titolo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ImpostaTitolo(hWnd As IntPtr, testo As String) As Boolean
        Try
            If hWnd = IntPtr.Zero OrElse Not IsWindow(hWnd) Then Return False
            Dim risultato As IntPtr = IntPtr.Zero
            Dim esito As IntPtr = SendMessageTimeoutTesto(hWnd, WM_SETTEXT, IntPtr.Zero, If(testo, ""), SMTO_ABORTIFHUNG, AttesaMessaggio, risultato)
            If esito = IntPtr.Zero Then
                log.ScriviLog("WGCondivisioneSchermo.ImpostaTitolo: la finestra non ha accettato il nuovo titolo, errore " & Marshal.GetLastWin32Error())
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.ImpostaTitolo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function MarcaFinestra(hWnd As IntPtr) As Boolean
        Try
            Dim attuale As String = Titolo(hWnd)
            If attuale.Contains(WGMotoreBrowser.MarcatoreFinestraCondivisa) Then Return True
            If Not ImpostaTitolo(hWnd, attuale.TrimEnd() & " " & WGMotoreBrowser.MarcatoreFinestraCondivisa) Then Return False
            Return Titolo(hWnd).Contains(WGMotoreBrowser.MarcatoreFinestraCondivisa)
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.MarcaFinestra: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub TogliMarcatore(hWnd As IntPtr)
        Try
            Dim attuale As String = Titolo(hWnd)
            If Not attuale.Contains(WGMotoreBrowser.MarcatoreFinestraCondivisa) Then Return
            ImpostaTitolo(hWnd, attuale.Replace(" " & WGMotoreBrowser.MarcatoreFinestraCondivisa, "").Replace(WGMotoreBrowser.MarcatoreFinestraCondivisa, ""))
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.TogliMarcatore: " & ex.Message)
        End Try
    End Sub

    Public Shared Function FinestreDelProcesso(idProcesso As Integer) As HashSet(Of IntPtr)
        Dim risultato As New HashSet(Of IntPtr)()
        Try
            If idProcesso <= 0 Then Return risultato
            Dim procedura As ProceduraFinestre = Function(hWnd As IntPtr, parametro As IntPtr) As Boolean
                                                     Try
                                                         If Not IsWindowVisible(hWnd) Then Return True
                                                         Dim proprietario As UInteger = 0
                                                         GetWindowThreadProcessId(hWnd, proprietario)
                                                         If CLng(proprietario) = CLng(idProcesso) Then risultato.Add(hWnd)
                                                     Catch exFinestra As Exception
                                                         log.ScriviLog("WGCondivisioneSchermo.FinestreDelProcesso finestra: " & exFinestra.Message)
                                                     End Try
                                                     Return True
                                                 End Function
            EnumWindows(procedura, IntPtr.Zero)
            GC.KeepAlive(procedura)
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.FinestreDelProcesso: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Shared Function EBarraDiCondivisione(hWnd As IntPtr) As Boolean
        Try
            Dim testo As String = Titolo(hWnd).ToLowerInvariant()
            Return testo.Contains("sta condividendo") OrElse testo.Contains("stai condividendo") OrElse testo.Contains("is sharing") OrElse testo.Contains("are sharing")
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.EBarraDiCondivisione: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function ESceltaDiCondivisione(hWnd As IntPtr) As Boolean
        Try
            If EBarraDiCondivisione(hWnd) Then Return False
            Dim testo As String = Titolo(hWnd).ToLowerInvariant()
            Return testo.Contains("condivider") OrElse testo.Contains("what to share") OrElse testo.Contains("share with")
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.ESceltaDiCondivisione: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function EsisteVisibile(hWnd As IntPtr) As Boolean
        Try
            Return hWnd <> IntPtr.Zero AndAlso IsWindow(hWnd) AndAlso IsWindowVisible(hWnd)
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.EsisteVisibile: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Sub Nascondi(hWnd As IntPtr)
        Try
            If hWnd <> IntPtr.Zero AndAlso IsWindow(hWnd) Then ShowWindow(hWnd, SW_HIDE)
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.Nascondi: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub Chiudi(hWnd As IntPtr)
        Try
            If hWnd <> IntPtr.Zero AndAlso IsWindow(hWnd) Then PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)
        Catch ex As Exception
            log.ScriviLog("WGCondivisioneSchermo.Chiudi: " & ex.Message)
        End Try
    End Sub
End Class

Public NotInheritable Class WGPosizioneWindows
    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    Public Shared Function OttieniAsync(millisecondi As Integer) As Task(Of Tuple(Of Double, Double, Double))
        Return Task.Run(Function() Ottieni(millisecondi))
    End Function

    Private Shared Function Ottieni(millisecondi As Integer) As Tuple(Of Double, Double, Double)
        Try
            Using osservatore As New GeoCoordinateWatcher(GeoPositionAccuracy.High)
                If Not osservatore.TryStart(True, TimeSpan.FromMilliseconds(Math.Max(500, millisecondi))) Then Return Nothing
                Dim limite As DateTime = DateTime.Now.AddMilliseconds(Math.Max(500, millisecondi))
                While DateTime.Now < limite
                    Dim posizione As GeoPosition(Of GeoCoordinate) = osservatore.Position
                    If posizione IsNot Nothing AndAlso posizione.Location IsNot Nothing AndAlso Not posizione.Location.IsUnknown Then
                        Dim precisione As Double = posizione.Location.HorizontalAccuracy
                        If Double.IsNaN(precisione) OrElse precisione <= 0 Then precisione = 100
                        Return Tuple.Create(posizione.Location.Latitude, posizione.Location.Longitude, precisione)
                    End If
                    Thread.Sleep(200)
                End While
                osservatore.Stop()
            End Using
        Catch ex As Exception
            log.ScriviLog("WGPosizioneWindows.Ottieni: " & ex.Message)
        End Try
        Return Nothing
    End Function
End Class

Public Class WGRegistroDownload
    Implements IWGControlloDownload

    Friend Class StatoDownload
        Public Info As New DownloadAttivoBrowser()
        Public Operazione As IOperazioneDownload
        Public ChiaveArchivio As String = ""
        Public UltimaSoglia As Integer = 0
        Public Url As String = ""
        Public Mime As String = ""
        Public Esplicito As Boolean = False
        Public Concluso As Boolean = False
        Public Scheda As WGSchedaBrowser
    End Class

    Private Shared _contatoreId As Integer = 0

    Private ReadOnly log As New Logger()
    Private ReadOnly _lock As New Object()
    Private ReadOnly _stati As New Dictionary(Of Integer, StatoDownload)()
    Private ReadOnly _esplicite As New Dictionary(Of String, DateTime)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _contenitore As WGBrowser

    Public Sub New(contenitore As WGBrowser)
        _contenitore = contenitore
    End Sub

    Public Shared Function NuovoId() As Integer
        Return Interlocked.Increment(_contatoreId)
    End Function

    Public Sub SegnaEsplicito(url As String)
        Try
            If String.IsNullOrWhiteSpace(url) Then Return
            SyncLock _lock
                _esplicite(url) = DateTime.Now
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.SegnaEsplicito: " & ex.Message)
        End Try
    End Sub

    Private Function EraEsplicito(url As String, originale As String) As Boolean
        Try
            SyncLock _lock
                Dim istante As DateTime
                For Each candidato As String In New String() {If(url, ""), If(originale, "")}
                    If candidato <> "" AndAlso _esplicite.TryGetValue(candidato, istante) AndAlso (DateTime.Now - istante).TotalMinutes < 5 Then
                        _esplicite.Remove(candidato)
                        Return True
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.EraEsplicito: " & ex.Message)
        End Try
        Return False
    End Function

    Friend Function Stato(id As Integer) As StatoDownload
        Try
            SyncLock _lock
                Dim s As StatoDownload = Nothing
                If _stati.TryGetValue(id, s) Then Return s
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.Stato: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Friend Function Registra(elemento As ElementoDownload, percorso As String, scheda As WGSchedaBrowser, operazione As IOperazioneDownload) As StatoDownload
        Try
            If elemento Is Nothing Then Return Nothing
            Dim s As New StatoDownload()
            s.Info.Id = elemento.Id
            s.Info.Nome = Path.GetFileName(percorso)
            s.Info.Percorso = percorso
            s.Info.Totali = Math.Max(0L, elemento.Totali)
            s.Url = If(elemento.Indirizzo, "")
            s.Mime = If(elemento.TipoMime, "")
            s.Esplicito = EraEsplicito(elemento.Indirizzo, elemento.IndirizzoOriginale)
            s.Scheda = scheda
            s.Operazione = operazione
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivio IsNot Nothing Then s.ChiaveArchivio = archivio.RegistraDownload(s.Info.Nome, percorso, s.Url, "in corso", Math.Max(0L, elemento.Totali))
            SyncLock _lock
                _stati(elemento.Id) = s
            End SyncLock
            Return s
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.Registra: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Friend Sub Notifica(elemento As ElementoDownload)
        Try
            If elemento Is Nothing Then Return
            _contenitore.SuUI(Sub() Aggiorna(elemento))
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.Notifica: " & ex.Message)
        End Try
    End Sub

    Friend Sub Aggiorna(elemento As ElementoDownload)
        Try
            If elemento Is Nothing Then Return
            Dim s As StatoDownload = Stato(elemento.Id)
            If s Is Nothing OrElse s.Concluso Then Return
            s.Info.Percentuale = If(elemento.Totali > 0, elemento.Percentuale, -1)
            s.Info.Ricevuti = elemento.Ricevuti
            s.Info.Totali = Math.Max(0L, elemento.Totali)
            s.Info.Velocita = elemento.Velocita
            s.Info.InPausa = elemento.InPausa
            If Not String.IsNullOrWhiteSpace(elemento.TipoMime) Then s.Mime = elemento.TipoMime
            If Not String.IsNullOrWhiteSpace(elemento.PercorsoCompleto) Then
                s.Info.Percorso = elemento.PercorsoCompleto
                s.Info.Nome = Path.GetFileName(elemento.PercorsoCompleto)
            End If
            If elemento.Completato Then
                Concludi(s, "completato")
                Return
            End If
            If elemento.Annullato Then
                Concludi(s, "annullato")
                Return
            End If
            If elemento.Interrotto Then
                Concludi(s, "interrotto")
                Return
            End If
            If WGImpostazioniBrowser.AnnunciaAvanzamentoDownload AndAlso elemento.Totali > 2L * 1024L * 1024L AndAlso elemento.Percentuale > 0 Then
                Dim soglia As Integer = (elemento.Percentuale \ 25) * 25
                If soglia > s.UltimaSoglia AndAlso soglia < 100 Then
                    s.UltimaSoglia = soglia
                    _contenitore.Annuncia(s.Info.Nome & ": " & soglia & " percento scaricato")
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.Aggiorna: " & ex.Message)
        End Try
    End Sub

    Private Sub Concludi(s As StatoDownload, esito As String)
        Try
            s.Concluso = True
            s.Operazione = Nothing
            SyncLock _lock
                _stati.Remove(s.Info.Id)
            End SyncLock
            Dim archivio As WGArchivioBrowser = WGArchivioBrowser.Istanza
            If archivio IsNot Nothing AndAlso s.ChiaveArchivio <> "" Then archivio.AggiornaDownload(s.ChiaveArchivio, esito, s.Info.Percorso, Math.Max(s.Info.Ricevuti, 0))
            Select Case esito
                Case "completato"
                    Dim pdf As Boolean = s.Mime.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0 OrElse s.Info.Percorso.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                    If pdf AndAlso Not s.Esplicito AndAlso WGImpostazioniBrowser.ApriPdfNelLettore Then
                        _contenitore.ApriPdfNelLettore(s.Info.Percorso)
                    Else
                        _contenitore.Notifica("Scaricamento completato: " & s.Info.Nome & ", nella cartella " & Path.GetFileName(Path.GetDirectoryName(s.Info.Percorso)) & ". Control J per i download", AddressOf _contenitore.MostraDownloadDaNotifica)
                    End If
                Case "annullato"
                    _contenitore.Annuncia("Scaricamento di " & s.Info.Nome & " annullato")
                Case Else
                    _contenitore.Notifica("Scaricamento di " & s.Info.Nome & " non riuscito", AddressOf _contenitore.MostraDownloadDaNotifica)
            End Select
            WGAriaMonitor.Registra("Scaricamento " & esito & ": " & s.Info.Nome, "download", "")
            _contenitore.VerificaSchedeInAttesa()
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.Concludi: " & ex.Message)
        End Try
    End Sub

    Public Function DownloadInCorso() As List(Of DownloadAttivoBrowser) Implements IWGControlloDownload.DownloadInCorso
        Try
            SyncLock _lock
                Return _stati.Values.Where(Function(s) Not s.Concluso).Select(Function(s) s.Info).ToList()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.DownloadInCorso: " & ex.Message)
            Return New List(Of DownloadAttivoBrowser)()
        End Try
    End Function

    Public Function DownloadDellaScheda(scheda As WGSchedaBrowser) As Integer
        Try
            SyncLock _lock
                Return _stati.Values.Where(Function(s) Not s.Concluso AndAlso s.Scheda Is scheda).Count()
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.DownloadDellaScheda: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub EseguiSuOperazione(id As Integer, azione As Action(Of IOperazioneDownload))
        Try
            Dim s As StatoDownload = Stato(id)
            If s Is Nothing OrElse s.Concluso OrElse s.Operazione Is Nothing Then Return
            Dim operazione As IOperazioneDownload = s.Operazione
            _contenitore.SuUI(Sub()
                                  Try
                                      azione(operazione)
                                  Catch exOperazione As Exception
                                      log.ScriviLog("WGRegistroDownload.EseguiSuOperazione azione: " & exOperazione.Message)
                                  End Try
                              End Sub)
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.EseguiSuOperazione: " & ex.Message)
        End Try
    End Sub

    Public Sub PausaDownload(id As Integer) Implements IWGControlloDownload.PausaDownload
        EseguiSuOperazione(id, Sub(operazione) operazione.Pausa())
    End Sub

    Public Sub RiprendiDownload(id As Integer) Implements IWGControlloDownload.RiprendiDownload
        EseguiSuOperazione(id, Sub(operazione) operazione.Riprendi())
    End Sub

    Public Sub AnnullaDownload(id As Integer) Implements IWGControlloDownload.AnnullaDownload
        EseguiSuOperazione(id, Sub(operazione) operazione.Annulla())
    End Sub

    Public Sub AnnullaTutti()
        Try
            Dim ids As List(Of Integer)
            SyncLock _lock
                ids = _stati.Keys.ToList()
            End SyncLock
            For Each id As Integer In ids
                AnnullaDownload(id)
            Next
        Catch ex As Exception
            log.ScriviLog("WGRegistroDownload.AnnullaTutti: " & ex.Message)
        End Try
    End Sub
End Class

Public NotInheritable Class WGAccessiSiti
    Private Shared ReadOnly log As New Logger()
    Private Const NomeFile As String = "accessi-siti.dat"
    Private Const NomeSegno As String = "accessi-siti.txt"
    Private Shared _inCorso As Integer = 0
    Private Shared _ripristinoTentato As Integer = 0
    Private Shared _diNuovo As Integer = 0
    Private Shared _lavoroSalvataggio As Integer = 0
    Private Shared _ultimaImpronta As String = ""
    Private Shared _avvisoDaDare As String = ""
    Private Shared ReadOnly _lockAvviso As New Object()

    Private Sub New()
    End Sub

    Private Shared Function PercorsoFile() As String
        Try
            Dim cartella As String = WGImpostazioniBrowser.CartellaDatiBrowser()
            If String.IsNullOrWhiteSpace(cartella) Then Return ""
            Return Path.Combine(cartella, NomeFile)
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.PercorsoFile: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PercorsoSegno() As String
        Try
            Return Path.Combine(WGMotoreBrowser.CartellaProfilo, NomeSegno)
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.PercorsoSegno: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function Impronta(contenuto As String) As String
        Try
            Using algoritmo As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
                Return BitConverter.ToString(algoritmo.ComputeHash(Encoding.UTF8.GetBytes(If(contenuto, "")))).Replace("-", "")
            End Using
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.Impronta: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function LeggiSegno() As String
        Try
            Dim percorso As String = PercorsoSegno()
            If percorso = "" OrElse Not File.Exists(percorso) Then Return ""
            Return File.ReadAllText(percorso, Encoding.UTF8).Trim()
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.LeggiSegno: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub ScriviSegno(valore As String)
        Try
            Dim percorso As String = PercorsoSegno()
            If percorso = "" Then Return
            Dim cartella As String = Path.GetDirectoryName(percorso)
            If Not String.IsNullOrEmpty(cartella) AndAlso Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            File.WriteAllText(percorso, If(valore, ""), New UTF8Encoding(False))
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.ScriviSegno: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ChiaveCookie(dominio As String, percorso As String, nome As String) As String
        Try
            Return If(dominio, "").ToLowerInvariant() & "|" & If(percorso, "") & "|" & If(nome, "")
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.ChiaveCookie: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function AvvisoInSospeso() As String
        Try
            SyncLock _lockAvviso
                Dim testo As String = _avvisoDaDare
                _avvisoDaDare = ""
                Return testo
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.AvvisoInSospeso: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub ImpostaAvviso(testo As String)
        Try
            SyncLock _lockAvviso
                _avvisoDaDare = If(testo, "")
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.ImpostaAvviso: " & ex.Message)
        End Try
    End Sub

    Private Shared Function NomeSameSite(valore As CoreWebView2CookieSameSiteKind) As String
        Select Case valore
            Case CoreWebView2CookieSameSiteKind.None
                Return "none"
            Case CoreWebView2CookieSameSiteKind.Strict
                Return "strict"
            Case Else
                Return "lax"
        End Select
    End Function

    Private Shared Function SameSiteDa(valore As JToken) As CoreWebView2CookieSameSiteKind
        Try
            If valore Is Nothing Then Return CoreWebView2CookieSameSiteKind.Lax
            If valore.Type = JTokenType.Integer Then
                Select Case valore.Value(Of Integer)()
                    Case 1
                        Return CoreWebView2CookieSameSiteKind.None
                    Case 3
                        Return CoreWebView2CookieSameSiteKind.Strict
                    Case Else
                        Return CoreWebView2CookieSameSiteKind.Lax
                End Select
            End If
            Select Case If(valore.ToString(), "").Trim().ToLowerInvariant()
                Case "none", "norestriction"
                    Return CoreWebView2CookieSameSiteKind.None
                Case "strict"
                    Return CoreWebView2CookieSameSiteKind.Strict
                Case Else
                    Return CoreWebView2CookieSameSiteKind.Lax
            End Select
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.SameSiteDa: " & ex.Message)
            Return CoreWebView2CookieSameSiteKind.Lax
        End Try
    End Function

    Private Shared Async Function LeggiCookieAsync(browser As WGBrowser) As Task(Of JArray)
        Try
            If browser Is Nothing Then Return Nothing
            Dim core As CoreWebView2 = browser.CoreNormale()
            If core Is Nothing Then Return Nothing
            Dim gestore As CoreWebView2CookieManager = core.CookieManager
            If gestore Is Nothing Then Return Nothing
            Dim cookies As List(Of CoreWebView2Cookie) = Await gestore.GetCookiesAsync("")
            Dim elenco As New JArray()
            If cookies Is Nothing Then Return elenco
            Dim adesso As DateTime = DateTime.Now
            For Each c As CoreWebView2Cookie In cookies
                Try
                    If c Is Nothing OrElse String.IsNullOrEmpty(c.Domain) Then Continue For
                    Dim sessione As Boolean = c.IsSession
                    Dim scadenza As DateTime = DateTime.MinValue
                    If Not sessione Then
                        scadenza = c.Expires
                        If scadenza < adesso Then Continue For
                    End If
                    Dim o As New JObject()
                    o("n") = If(c.Name, "")
                    o("v") = If(c.Value, "")
                    o("d") = c.Domain
                    o("p") = If(String.IsNullOrEmpty(c.Path), "/", c.Path)
                    o("s") = c.IsSecure
                    o("h") = c.IsHttpOnly
                    If Not sessione Then o("e") = scadenza.ToUniversalTime().ToString("o", Globalization.CultureInfo.InvariantCulture)
                    o("ss") = NomeSameSite(c.SameSite)
                    elenco.Add(o)
                Catch exCookie As Exception
                    log.ScriviLog("WGAccessiSiti.LeggiCookieAsync cookie: " & exCookie.Message)
                End Try
            Next
            Return elenco
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.LeggiCookieAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Shared Sub Salva(millisecondi As Integer)
        Try
            Dim compito As Task(Of Boolean) = SalvaAsync()
            WGMotoreBrowser.AttendiCompito(compito, millisecondi)
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.Salva: " & ex.Message)
        End Try
    End Sub

    Public Shared Async Function SalvaAsync(Optional browser As WGBrowser = Nothing) As Task(Of Boolean)
        Dim acquisito As Boolean = False
        Try
            acquisito = Interlocked.Exchange(_inCorso, 1) = 0
            If Not acquisito Then Return False
            Dim percorso As String = PercorsoFile()
            If percorso = "" Then Return False
            Dim sorgente As WGBrowser = If(browser, WGBrowser.IstanzaAperta)
            If sorgente Is Nothing Then Return False
            Dim elenco As JArray = Await sorgente.EseguiSuInterfacciaAsync(Function() LeggiCookieAsync(sorgente))
            If elenco Is Nothing Then Return False
            Dim testo As String = elenco.ToString(Newtonsoft.Json.Formatting.None)
            Dim improntaTesto As String = Impronta(testo)
            If improntaTesto <> "" AndAlso String.Equals(improntaTesto, _ultimaImpronta, StringComparison.OrdinalIgnoreCase) AndAlso File.Exists(percorso) Then Return True
            Dim scritto As Boolean = Await Task.Run(Function()
                                                        Try
                                                            Dim contenuto As String = WGProtezioneDati.Proteggi(testo)
                                                            If contenuto = "" Then Return False
                                                            Dim temporaneo As String = percorso & ".nuovo"
                                                            File.WriteAllText(temporaneo, contenuto, New UTF8Encoding(False))
                                                            If File.Exists(percorso) Then File.Delete(percorso)
                                                            File.Move(temporaneo, percorso)
                                                            ScriviSegno(Impronta(contenuto))
                                                            Return True
                                                        Catch exScrittura As Exception
                                                            Dim registroScrittura As New Logger()
                                                            registroScrittura.ScriviLog("WGAccessiSiti.SalvaAsync scrittura: " & exScrittura.Message)
                                                            Return False
                                                        End Try
                                                    End Function)
            If scritto AndAlso improntaTesto <> "" Then _ultimaImpronta = improntaTesto
            Return scritto
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.SalvaAsync: " & ex.ToString())
            Return False
        Finally
            If acquisito Then Interlocked.Exchange(_inCorso, 0)
        End Try
    End Function

    ''' <summary>Riscrive il file degli accessi anche se i cookie non sono cambiati (per passare al formato portabile tra computer).</summary>
    Public Shared Sub ForzaRiscrittura()
        Try
            Dim percorso As String = PercorsoFile()
            If percorso <> "" AndAlso File.Exists(percorso) Then
                Dim contenuto As String = File.ReadAllText(percorso, Encoding.UTF8).Trim()
                If WGProtezioneDati.EPortabile(contenuto) Then Return
            End If
            _ultimaImpronta = ""
            RichiediSalvataggio()
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.ForzaRiscrittura: " & ex.Message)
        End Try
    End Sub

    Public Shared Sub RichiediSalvataggio()
        Try
            Interlocked.Exchange(_diNuovo, 1)
            If Interlocked.CompareExchange(_lavoroSalvataggio, 1, 0) = 0 Then
                Dim compito As Task = EseguiSalvataggiRichiestiAsync()
            End If
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.RichiediSalvataggio: " & ex.Message)
        End Try
    End Sub

    Private Shared Async Function EseguiSalvataggiRichiestiAsync() As Task
        Try
            Do
                If Interlocked.Exchange(_diNuovo, 0) = 0 Then Exit Do
                ' I cookie della stessa risposta entrano nel gestore un attimo dopo l'intestazione.
                Await Task.Delay(200)
                Interlocked.Exchange(_diNuovo, 0)
                Dim scritto As Boolean = Await SalvaAsync()
                If Not scritto AndAlso Interlocked.CompareExchange(_inCorso, 0, 0) = 1 Then
                    Interlocked.Exchange(_diNuovo, 1)
                    Await Task.Delay(300)
                End If
            Loop While Interlocked.CompareExchange(_diNuovo, 0, 0) = 1
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.EseguiSalvataggiRichiestiAsync: " & ex.Message)
        Finally
            Interlocked.Exchange(_lavoroSalvataggio, 0)
            If Interlocked.CompareExchange(_diNuovo, 0, 0) = 1 Then RichiediSalvataggio()
        End Try
    End Function

    Public Shared Sub NotificaRispostaRete(parametri As JObject)
        Try
            If parametri Is Nothing OrElse Not RispostaHaCookie(parametri) Then Return
            RichiediSalvataggio()
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.NotificaRispostaRete: " & ex.Message)
        End Try
    End Sub

    Private Shared Function RispostaHaCookie(parametri As JObject) As Boolean
        Try
            Dim righe As Integer = ContaSetCookie(parametri)
            If righe <= 0 Then Return False
            Dim bloccati As JArray = TryCast(parametri("blockedCookies"), JArray)
            Dim numeroBloccati As Integer = If(bloccati Is Nothing, 0, bloccati.Count)
            Return numeroBloccati < righe
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.RispostaHaCookie: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ContaSetCookie(parametri As JObject) As Integer
        Try
            Dim dalleIntestazioni As Integer = ContaSetCookieNelleIntestazioni(TryCast(parametri("headers"), JObject))
            If dalleIntestazioni > 0 Then Return dalleIntestazioni
            Dim testo As String = If(parametri.Value(Of String)("headersText"), "")
            If testo = "" Then Return 0
            Dim conteggio As Integer = 0
            For Each riga As String In testo.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim pulita As String = riga.Trim()
                If pulita.StartsWith("set-cookie:", StringComparison.OrdinalIgnoreCase) AndAlso pulita.Length > "set-cookie:".Length Then conteggio += 1
            Next
            Return conteggio
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.ContaSetCookie: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function ContaSetCookieNelleIntestazioni(intestazioni As JObject) As Integer
        Try
            If intestazioni Is Nothing Then Return 0
            For Each proprieta As JProperty In intestazioni.Properties()
                If Not String.Equals(proprieta.Name, "set-cookie", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim elenco As JArray = TryCast(proprieta.Value, JArray)
                If elenco IsNot Nothing Then
                    Dim voci As Integer = 0
                    For Each voce As JToken In elenco
                        If voce IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(voce.ToString()) Then voci += 1
                    Next
                    Return voci
                End If
                Dim testo As String = If(proprieta.Value Is Nothing, "", proprieta.Value.ToString())
                If testo = "" Then Return 0
                Dim righe As String() = testo.Split({vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)
                Dim conteggio As Integer = 0
                For Each riga As String In righe
                    If Not String.IsNullOrWhiteSpace(riga) Then conteggio += 1
                Next
                Return conteggio
            Next
            Return 0
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.ContaSetCookieNelleIntestazioni: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Shared Async Function RipristinaSeServeAsync(core As CoreWebView2) As Task
        Dim acquisito As Boolean = False
        Try
            If core Is Nothing Then Return
            acquisito = Interlocked.Exchange(_inCorso, 1) = 0
            If Not acquisito Then Return
            If Interlocked.Exchange(_ripristinoTentato, 1) = 1 Then Return
            Dim percorso As String = PercorsoFile()
            If percorso = "" OrElse Not File.Exists(percorso) Then Return
            Dim contenuto As String = Await Task.Run(Function()
                                                         Try
                                                             Return File.ReadAllText(percorso, Encoding.UTF8).Trim()
                                                         Catch exLettura As Exception
                                                             Dim registroLettura As New Logger()
                                                             registroLettura.ScriviLog("WGAccessiSiti.RipristinaSeServeAsync lettura: " & exLettura.Message)
                                                             Return ""
                                                         End Try
                                                     End Function)
            If String.IsNullOrEmpty(contenuto) Then Return
            Dim improntaFile As String = Impronta(contenuto)
            If String.Equals(improntaFile, LeggiSegno(), StringComparison.OrdinalIgnoreCase) Then Return
            Dim json As String = Await Task.Run(Function() WGProtezioneDati.Rivela(contenuto))
            If String.IsNullOrEmpty(json) Then
                ImpostaAvviso("Gli accessi ai siti salvati nel backup non si possono usare qui: bisogna accedere di nuovo ai siti")
                ScriviSegno(improntaFile)
                Return
            End If
            Dim elenco As JArray = JArray.Parse(json)
            Dim gestore As CoreWebView2CookieManager = core.CookieManager
            If gestore Is Nothing Then Return
            Dim esistenti As New HashSet(Of String)(StringComparer.Ordinal)
            Dim attuali As List(Of CoreWebView2Cookie) = Await gestore.GetCookiesAsync("")
            If attuali IsNot Nothing Then
                For Each c As CoreWebView2Cookie In attuali
                    Try
                        If c IsNot Nothing Then esistenti.Add(ChiaveCookie(c.Domain, c.Path, c.Name))
                    Catch exEsistente As Exception
                        log.ScriviLog("WGAccessiSiti.RipristinaSeServeAsync esistente: " & exEsistente.Message)
                    End Try
                Next
            End If
            Dim siti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each elemento As JToken In elenco
                Try
                    Dim o As JObject = TryCast(elemento, JObject)
                    If o Is Nothing Then Continue For
                    Dim dominio As String = If(o.Value(Of String)("d"), "").Trim()
                    If dominio = "" Then Continue For
                    Dim nome As String = If(o.Value(Of String)("n"), "")
                    Dim percorsoCookie As String = If(o.Value(Of String)("p"), "/")
                    If percorsoCookie = "" Then percorsoCookie = "/"
                    If esistenti.Contains(ChiaveCookie(dominio, percorsoCookie, nome)) Then Continue For
                    Dim scadenza As DateTime? = Nothing
                    Dim testoScadenza As String = If(o.Value(Of String)("e"), "")
                    Dim letta As DateTime
                    If testoScadenza <> "" AndAlso DateTime.TryParse(testoScadenza, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind, letta) Then
                        scadenza = letta.ToLocalTime()
                        If scadenza.Value < DateTime.Now Then Continue For
                    End If
                    Dim sicuro As Boolean = o.Value(Of Boolean?)("s").GetValueOrDefault()
                    Dim cookie As CoreWebView2Cookie = gestore.CreateCookie(nome, If(o.Value(Of String)("v"), ""), dominio, percorsoCookie)
                    If cookie Is Nothing Then Continue For
                    cookie.IsSecure = sicuro
                    cookie.IsHttpOnly = o.Value(Of Boolean?)("h").GetValueOrDefault()
                    Dim sameSite As CoreWebView2CookieSameSiteKind = SameSiteDa(o("ss"))
                    If sameSite = CoreWebView2CookieSameSiteKind.None AndAlso Not sicuro Then sameSite = CoreWebView2CookieSameSiteKind.Lax
                    cookie.SameSite = sameSite
                    If scadenza.HasValue Then cookie.Expires = scadenza.Value
                    gestore.AddOrUpdateCookie(cookie)
                    siti.Add(dominio.TrimStart("."c))
                Catch exCookie As Exception
                    log.ScriviLog("WGAccessiSiti.RipristinaSeServeAsync cookie: " & exCookie.Message)
                End Try
            Next
            ScriviSegno(improntaFile)
            If siti.Count > 0 Then ImpostaAvviso("Accessi ai siti ripristinati dal backup: " & siti.Count & If(siti.Count = 1, " sito", " siti"))
        Catch ex As Exception
            log.ScriviLog("WGAccessiSiti.RipristinaSeServeAsync: " & ex.ToString())
        Finally
            If acquisito Then Interlocked.Exchange(_inCorso, 0)
        End Try
    End Function
End Class
