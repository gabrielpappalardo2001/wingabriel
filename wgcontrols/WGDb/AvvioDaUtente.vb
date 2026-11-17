Option Strict Off
Imports System
Imports System.Diagnostics
Imports System.Runtime.InteropServices

''' <summary>
''' Apre link, documenti e programmi con i privilegi normali dell'utente anche quando WinGabriel gira come amministratore. Un browser
''' avviato da un programma amministratore sarebbe amministratore anche lui, con le pagine web che girano con quei privilegi. Per evitarlo
''' si chiede a Esplora risorse (che gira come utente) di aprire il file: il programma parte come se l'utente lo avesse aperto dal desktop.
''' Senza privilegi, o se Esplora risorse non risponde, si apre nel modo solito.
''' </summary>
Public NotInheritable Class AvvioDaUtente

    Private Sub New()
    End Sub

    <ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IServiceProvider
        Function QueryService(ByRef guidService As Guid, ByRef riid As Guid) As <MarshalAs(UnmanagedType.IUnknown)> Object
    End Interface

    ' Solo l'ordine dei metodi conta: quelli che non si usano hanno una firma qualsiasi, ma devono esserci tutti fino a quello usato.
    <ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IShellBrowser
        Sub GetWindow()
        Sub ContextSensitiveHelp()
        Sub InsertMenusSB()
        Sub SetMenuSB()
        Sub RemoveMenusSB()
        Sub SetStatusTextSB()
        Sub EnableModelessSB()
        Sub TranslateAcceleratorSB()
        Sub BrowseObject()
        Sub GetViewStateStream()
        Sub GetControlWindow()
        Sub SendControlMsg()
        Function QueryActiveShellView() As IShellView
    End Interface

    <ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IShellView
        Sub GetWindow()
        Sub ContextSensitiveHelp()
        Sub TranslateAccelerator()
        Sub EnableModeless()
        Sub UIActivate()
        Sub Refresh()
        Sub CreateViewWindow()
        Sub DestroyViewWindow()
        Sub GetCurrentInfo()
        Sub AddPropertySheetPages()
        Sub SaveViewState()
        Sub SelectItem()
        Function GetItemObject(uItem As UInteger, ByRef riid As Guid) As <MarshalAs(UnmanagedType.IDispatch)> Object
    End Interface

    Private Shared ReadOnly ClsidShellWindows As New Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")
    Private Shared ReadOnly SidTopLevelBrowser As New Guid("4C96BE40-915C-11CF-99D3-00AA004AE837")
    Private Shared ReadOnly IidShellBrowser As New Guid("000214E2-0000-0000-C000-000000000046")
    Private Shared ReadOnly IidDispatch As New Guid("00020400-0000-0000-C000-000000000046")
    Private Const CSIDL_DESKTOP As Integer = 0
    Private Const SWC_DESKTOP As Integer = 8
    Private Const SWFO_NEEDDISPATCH As Integer = 1
    Private Const SVGIO_BACKGROUND As UInteger = 0

    ''' <summary>
    ''' Apre il file, il link o il programma, con gli argomenti dati, come l'utente normale. Vero se è partito.
    ''' </summary>
    Public Shared Function Apri(percorso As String, Optional argomenti As String = "", Optional cartella As String = "") As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If WGPercorsiApplicazione.Elevato() AndAlso ApriTramiteEsplora(percorso, If(argomenti, ""), If(cartella, "")) Then Return True
            Dim info As New ProcessStartInfo(percorso, If(argomenti, "")) With {.UseShellExecute = True}
            If Not String.IsNullOrWhiteSpace(cartella) Then info.WorkingDirectory = cartella
            Using processo As Process = Process.Start(info)
            End Using
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("AvvioDaUtente.Apri", ex)
            Return False
        End Try
    End Function

    ' Il desktop di Esplora risorse, la sua vista e da lì l'oggetto Shell.Application del processo di Esplora: il suo ShellExecute
    ' fa partire il programma dentro Esplora, quindi con i privilegi dell'utente.
    Private Shared Function ApriTramiteEsplora(percorso As String, argomenti As String, cartella As String) As Boolean
        Dim finestre As Object = Nothing
        Dim desktop As Object = Nothing
        Dim vista As IShellView = Nothing
        Dim sfondo As Object = Nothing
        Dim shell As Object = Nothing
        Try
            Dim tipo As Type = Type.GetTypeFromCLSID(ClsidShellWindows)
            If tipo Is Nothing Then Return False
            finestre = Activator.CreateInstance(tipo)
            Dim posizione As Object = CSIDL_DESKTOP
            Dim radice As Object = Nothing
            Dim finestra As Integer = 0
            desktop = finestre.FindWindowSW(posizione, radice, SWC_DESKTOP, finestra, SWFO_NEEDDISPATCH)
            If desktop Is Nothing Then Return False
            Dim fornitore As IServiceProvider = TryCast(desktop, IServiceProvider)
            If fornitore Is Nothing Then Return False
            Dim browser As IShellBrowser = TryCast(fornitore.QueryService(SidTopLevelBrowser, IidShellBrowser), IShellBrowser)
            If browser Is Nothing Then Return False
            vista = browser.QueryActiveShellView()
            If vista Is Nothing Then Return False
            sfondo = vista.GetItemObject(SVGIO_BACKGROUND, IidDispatch)
            If sfondo Is Nothing Then Return False
            shell = sfondo.Application
            If shell Is Nothing Then Return False
            shell.ShellExecute(percorso, argomenti, cartella, "", 1)
            Return True
        Catch ex As Exception
            RegistroErrori.Registra("AvvioDaUtente.ApriTramiteEsplora", ex)
            Return False
        Finally
            For Each oggetto As Object In New Object() {shell, sfondo, vista, desktop, finestre}
                Try
                    If oggetto IsNot Nothing AndAlso Marshal.IsComObject(oggetto) Then Marshal.ReleaseComObject(oggetto)
                Catch
                End Try
            Next
        End Try
    End Function

End Class
