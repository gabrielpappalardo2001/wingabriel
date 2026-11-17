Imports System.Runtime.InteropServices
Imports System.Text

' Segnalazione dello stato di accessibilità al sistema operativo, attivazione dei client UIA e MSAA e risveglio delle finestre per
' il lettore.
Friend NotInheritable Class SegnaleAccessibilita

    Public Const WM_GETOBJECT As Integer = &H3D
    Public Const UiaRootObjectId As Integer = -25
    Public Const OBJID_CLIENT As Integer = -4
    Private Const SPI_GETSCREENREADER As UInteger = &H46UI
    Private Const SPI_SETSCREENREADER As UInteger = &H47UI
    Private Const SPIF_UPDATEINIFILE As UInteger = &H1UI
    Private Const SPIF_SENDCHANGE As UInteger = &H2UI
    Private Const SMTO_ABORTIFHUNG As UInteger = &H2UI

    Private ReadOnly log As New Logger()
    Private ReadOnly _bloccoFigli As New Object()
    Private _giaAttivo As Boolean
    Private _impostatoDaNoi As Boolean
    Private _ultimoAvviso As String = ""
    Private _figliWeb As List(Of IntPtr)
    Private _figliXaml As List(Of IntPtr)
    Private Shared ReadOnly _classiContenutoWeb As String() = {"Chrome_RenderWidgetHostHWND", "MozillaWindowClass", "Internet Explorer_Server"}
    Private Shared ReadOnly _classiProviderXaml As String() = {"Microsoft.UI.Content.DesktopChildSiteBridge", "DesktopWindowXamlSource", "Windows.UI.Composition.DesktopWindowContentBridge"}

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SystemParametersInfo(azione As UInteger, parametro As UInteger, ByRef valore As Boolean, flag As UInteger) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SystemParametersInfoPunta(azione As UInteger, parametro As UInteger, valore As IntPtr, flag As UInteger) As Boolean
    End Function

    <DllImport("UIAutomationCore.dll")>
    Private Shared Function UiaClientsAreListening() As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function EnumChildWindows(padre As IntPtr, procedura As NativoLettore.ProceduraFinestre, parametro As IntPtr) As Boolean
    End Function

    <DllImport("oleacc.dll")>
    Private Shared Function AccessibleObjectFromWindow(hwnd As IntPtr, oggetto As Integer, ByRef iid As Guid, <MarshalAs(UnmanagedType.IUnknown)> ByRef risultato As Object) As Integer
    End Function

    Private Shared ReadOnly IidAccessibile As New Guid("618736E0-3C3D-11CF-810C-00AA00389B71")

    Public ReadOnly Property ClientiInAscolto As Boolean
        Get
            Try
                Return UiaClientsAreListening()
            Catch ex As Exception
                log.ScriviLog("SegnaleAccessibilita.ClientiInAscolto: " & ex.Message)
                Return False
            End Try
        End Get
    End Property

    Public ReadOnly Property FlagLettoreAttivo As Boolean
        Get
            Try
                Dim valore As Boolean = False
                SystemParametersInfo(SPI_GETSCREENREADER, 0UI, valore, 0UI)
                Return valore
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("SegnaleAccessibilita.FlagLettoreAttivo", exAtteso)
                Return False
            End Try
        End Get
    End Property

    Public Sub Attiva()
        Try
            Dim attuale As Boolean = False
            SystemParametersInfo(SPI_GETSCREENREADER, 0UI, attuale, 0UI)
            _giaAttivo = attuale
            If attuale Then
                _impostatoDaNoi = False
                Return
            End If
            _impostatoDaNoi = SystemParametersInfoPunta(SPI_SETSCREENREADER, 1UI, IntPtr.Zero, SPIF_UPDATEINIFILE Or SPIF_SENDCHANGE)
        Catch ex As Exception
            log.ScriviLog("SegnaleAccessibilita.Attiva: " & ex.Message)
        End Try
    End Sub

    Public Sub Ripristina()
        Try
            If Not _impostatoDaNoi OrElse _giaAttivo Then Return
            SystemParametersInfoPunta(SPI_SETSCREENREADER, 0UI, IntPtr.Zero, SPIF_UPDATEINIFILE Or SPIF_SENDCHANGE)
            _impostatoDaNoi = False
        Catch ex As Exception
            log.ScriviLog("SegnaleAccessibilita.Ripristina: " & ex.Message)
        End Try
    End Sub

    Public Sub SvegliaFinestra(finestra As IntPtr)
        Try
            If finestra = IntPtr.Zero OrElse NativoLettore.IsHungAppWindow(finestra) Then Return
            SvegliaUia(finestra)
            SvegliaMsaa(finestra)
            Dim web As List(Of IntPtr)
            Dim xaml As List(Of IntPtr)
            SyncLock _bloccoFigli
                _figliWeb = New List(Of IntPtr)()
                _figliXaml = New List(Of IntPtr)()
                Dim procedura As New NativoLettore.ProceduraFinestre(AddressOf RaccogliFiglioWeb)
                EnumChildWindows(finestra, procedura, IntPtr.Zero)
                GC.KeepAlive(procedura)
                web = New List(Of IntPtr)(_figliWeb)
                xaml = New List(Of IntPtr)(_figliXaml)
                _figliWeb = Nothing
                _figliXaml = Nothing
            End SyncLock
            For Each figlio As IntPtr In web
                SvegliaMsaa(figlio)
            Next
            For Each figlio As IntPtr In xaml
                SvegliaUia(figlio)
                SvegliaMsaa(figlio)
            Next
        Catch ex As Exception
            log.ScriviLog("SegnaleAccessibilita.SvegliaFinestra: " & ex.Message)
        End Try
    End Sub

    Private Sub SvegliaUia(finestra As IntPtr)
        Try
            If finestra = IntPtr.Zero OrElse NativoLettore.IsHungAppWindow(finestra) Then Return
            NativoLettore.UiaHasServerSideProvider(finestra)
        Catch ex As Exception
            log.ScriviLog("SegnaleAccessibilita.SvegliaUia: " & ex.Message)
        End Try
    End Sub

    Private Sub SvegliaMsaa(finestra As IntPtr)
        Dim oggetto As Object = Nothing
        Try
            If finestra = IntPtr.Zero OrElse NativoLettore.IsHungAppWindow(finestra) Then Return
            Dim iid As Guid = IidAccessibile
            AccessibleObjectFromWindow(finestra, OBJID_CLIENT, iid, oggetto)
        Catch ex As Exception
            log.ScriviLog("SegnaleAccessibilita.SvegliaMsaa: " & ex.Message)
        Finally
            Try
                If oggetto IsNot Nothing AndAlso Marshal.IsComObject(oggetto) Then Marshal.ReleaseComObject(oggetto)
            Catch exRilascio As Exception
                log.ScriviLog("SegnaleAccessibilita.SvegliaMsaa (rilascio): " & exRilascio.Message)
            End Try
        End Try
    End Sub

    Private Function RaccogliFiglioWeb(finestra As IntPtr, parametro As IntPtr) As Boolean
        Try
            If _figliWeb Is Nothing Then Return False
            Dim nome As New StringBuilder(64)
            If NativoLettore.GetClassName(finestra, nome, nome.Capacity) > 0 Then
                Dim classe As String = nome.ToString()
                For Each candidata As String In _classiContenutoWeb
                    If String.Equals(classe, candidata, StringComparison.Ordinal) Then
                        _figliWeb.Add(finestra)
                        Exit For
                    End If
                Next
                For Each candidata As String In _classiProviderXaml
                    If String.Equals(classe, candidata, StringComparison.Ordinal) Then
                        If _figliXaml IsNot Nothing Then _figliXaml.Add(finestra)
                        Exit For
                    End If
                Next
            End If
            Return _figliWeb.Count < 4 AndAlso (_figliXaml Is Nothing OrElse _figliXaml.Count < 4)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("SegnaleAccessibilita.RaccogliFiglioWeb", exAtteso)
            Return False
        End Try
    End Function

    Public Function HaProviderUia(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero Then Return False
            If NativoLettore.IsHungAppWindow(finestra) Then Return False
            If NativoLettore.UiaHasServerSideProvider(finestra) Then Return True
            Dim xaml As List(Of IntPtr)
            SyncLock _bloccoFigli
                _figliWeb = New List(Of IntPtr)()
                _figliXaml = New List(Of IntPtr)()
                Dim procedura As New NativoLettore.ProceduraFinestre(AddressOf RaccogliFiglioWeb)
                EnumChildWindows(finestra, procedura, IntPtr.Zero)
                GC.KeepAlive(procedura)
                xaml = New List(Of IntPtr)(_figliXaml)
                _figliWeb = Nothing
                _figliXaml = Nothing
            End SyncLock
            For Each figlio As IntPtr In xaml
                If NativoLettore.IsHungAppWindow(figlio) Then Continue For
                If NativoLettore.UiaHasServerSideProvider(figlio) Then Return True
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("SegnaleAccessibilita.HaProviderUia: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function MessaggioSeManca() As String
        Try
            Dim ascolto As Boolean = False
            Try
                ascolto = UiaClientsAreListening()
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("SegnaleAccessibilita.MessaggioSeManca", exAtteso)
                ascolto = False
            End Try
            Dim flag As Boolean = FlagLettoreAttivo
            If ascolto AndAlso flag Then Return ""
            Dim testo As New StringBuilder()
            If Not flag Then testo.Append("Il flag del lettore di schermo non è attivo. ")
            If Not ascolto Then testo.Append("Nessun client UIA risulta in ascolto: Chrome e Edge possono tenere l'albero spento ancora per qualche secondo. ")
            Dim esito As String = testo.ToString().Trim()
            If esito = _ultimoAvviso Then Return ""
            _ultimoAvviso = esito
            Return esito
        Catch ex As Exception
            log.ScriviLog("SegnaleAccessibilita.MessaggioSeManca: " & ex.Message)
            Return ""
        End Try
    End Function

End Class
