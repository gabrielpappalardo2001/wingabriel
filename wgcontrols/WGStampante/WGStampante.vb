Imports Microsoft.Win32
Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Net
Imports System.Net.Sockets
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms

' Gestione della stampa di documenti, tabelle e immagini. Include il rilevamento delle stampanti di sistema, la configurazione
' delle pagine e l'integrazione con la libreria PDFium.
Public Enum TipoStampante
    Locale = 0
    ReteDiretta = 1
    Condivisa = 2
End Enum

Public Class InfoStampante
    Public Property Nome As String = ""
    Public Property Predefinita As Boolean = False
    Public Property Rete As Boolean = False
    Public Property Server As String = ""
    Public Property Stato As String = ""
    Public Property Tipo As TipoStampante = TipoStampante.Locale
    Public Property Porta As String = ""
    Public Property Indirizzo As String = ""
    Public Property PortaTcp As Integer = 0
    Public Property NomeCondivisione As String = ""

    Public Sub New()
    End Sub

    Public Sub New(nomeStampante As String, ePredefinita As Boolean)
        Try
            Nome = If(nomeStampante, "")
            Predefinita = ePredefinita
        Catch
        End Try
    End Sub
End Class

Public Class WGBloccoStampa
    ''' <summary>Tipo di blocco: "testo", "tabella" oppure "immagine".</summary>
    Public Property Tipo As String = "testo"
    Public Property Testo As String = ""
    Public Property RigheTabella As List(Of String())

    ''' <summary>Percorso su disco dell'immagine da stampare (usato se ImmagineBytes non è impostato).</summary>
    Public Property PercorsoImmagine As String = ""
    ''' <summary>Contenuto binario dell'immagine da stampare (ha precedenza su PercorsoImmagine).</summary>
    Public Property ImmagineBytes As Byte()
    ''' <summary>Larghezza massima desiderata dell'immagine in millimetri. 0 = adatta automaticamente alla larghezza utile della pagina.</summary>
    Public Property LarghezzaImmagineMM As Integer = 0
    ''' <summary>Altezza massima desiderata dell'immagine in millimetri. 0 = calcolata mantenendo le proporzioni originali.</summary>
    Public Property AltezzaImmagineMM As Integer = 0
    Public Property IndiceFotogramma As Integer = 0

    Public Sub New()
        Try
            RigheTabella = New List(Of String())()
        Catch
        End Try
    End Sub
End Class

Public Class WGImpostazioniStampa
    Public Property MargineMillimetri As Integer = 15
    Public Property NomeCarattere As String = "Arial"
    Public Property DimensioneCarattere As Integer = 11
    Public Property Copie As Integer = 1
    Public Property Orientamento As String = "verticale"
    Public Property FronteRetro As String = "una_facciata"
    Public Property Colore As String = "colore"
    Public Property Qualita As String = "normale"
    Public Property MostraIntestazione As Boolean = True
    Public Property MostraNumeroPagina As Boolean = True

    Public Sub New()
    End Sub

    Public Function CopiaSicura() As WGImpostazioniStampa
        Dim copia As New WGImpostazioniStampa()
        Try
            copia.MargineMillimetri = MargineMillimetri
            copia.NomeCarattere = NomeCarattere
            copia.DimensioneCarattere = DimensioneCarattere
            copia.Copie = Copie
            copia.Orientamento = Orientamento
            copia.FronteRetro = FronteRetro
            copia.Colore = Colore
            copia.Qualita = Qualita
            copia.MostraIntestazione = MostraIntestazione
            copia.MostraNumeroPagina = MostraNumeroPagina
            copia.Normalizza()
        Catch
        End Try
        Return copia
    End Function

    Public Sub Normalizza()
        Try
            If MargineMillimetri < 0 Then MargineMillimetri = 0
            If MargineMillimetri > 50 Then MargineMillimetri = 50
            If DimensioneCarattere < 6 Then DimensioneCarattere = 6
            If DimensioneCarattere > 36 Then DimensioneCarattere = 36
            If Copie < 1 Then Copie = 1
            If Copie > 99 Then Copie = 99
            If String.IsNullOrWhiteSpace(NomeCarattere) Then NomeCarattere = "Arial"
            NomeCarattere = NomeCarattere.Trim()
            If String.IsNullOrWhiteSpace(Orientamento) Then Orientamento = "verticale"
            Orientamento = Orientamento.Trim().ToLowerInvariant()
            If Orientamento <> "orizzontale" Then Orientamento = "verticale"
            If String.IsNullOrWhiteSpace(FronteRetro) Then FronteRetro = "una_facciata"
            FronteRetro = FronteRetro.Trim().ToLowerInvariant()
            If FronteRetro <> "fronte_retro" Then FronteRetro = "una_facciata"
            If String.IsNullOrWhiteSpace(Colore) Then Colore = "colore"
            Colore = Colore.Trim().ToLowerInvariant()
            If Colore <> "bianco_nero" Then Colore = "colore"
            If String.IsNullOrWhiteSpace(Qualita) Then Qualita = "normale"
            Qualita = Qualita.Trim().ToLowerInvariant()
            If Qualita <> "bozza" AndAlso Qualita <> "alta" Then Qualita = "normale"
        Catch
        End Try
    End Sub
End Class

Public Class WGEsitoStampa
    Public Property Riuscita As Boolean = False
    Public Property Errore As String = ""
    Public Property Avviso As String = ""
    Public Property IdLavoro As Integer = 0
    Public Property PagineStampate As Integer = 0
    Public Property StatoFinale As String = ""

    Public Sub New()
    End Sub
End Class

Public Class CapacitaStampanteInfo
    Public Property SupportaFronteRetro As Boolean = False
    Public Property SupportaColore As Boolean = False
    Public Property ElencoFormati As New List(Of String)()

    Public Sub New()
    End Sub
End Class

Public Class WGStampante
    Private Shared ReadOnly _lockLog As New Object()
    Private Shared ReadOnly _lockStampa As New Object()
    Private Shared ReadOnly _lockElenco As New Object()
    Private Shared ReadOnly _lockErrore As New Object()
    Private Shared _ultimoErrore As String = ""
    Private Shared _ultimoAvviso As String = ""
    Private Shared ReadOnly _lockCapacita As New Object()
    Private Shared ReadOnly _lockFileCapacita As New Object()
    Private Shared ReadOnly _cacheCapacita As New Dictionary(Of String, CapacitaStampanteInfo)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _capacitaVerificate As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _capacitaInVerifica As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private Shared _cacheCapacitaCaricata As Boolean = False
    Private Const CAPACITA_TIMEOUT_MS As Integer = 20000
    Private Const SEPARATORE_FORMATI As Char = ChrW(31)
    Private Const ERROR_FILE_NOT_FOUND As Integer = 2
    Private Const ERROR_BAD_NETPATH As Integer = 53
    Private Const ERROR_BAD_NET_NAME As Integer = 67
    Private Const ERROR_LOGON_FAILURE As Integer = 1326
    Private Const ERROR_NOT_AUTHENTICATED As Integer = 1244
    Private Const ERROR_RPC_S_SERVER_UNAVAILABLE As Integer = 1722
    Private Const ERROR_UNKNOWN_PRINTER_DRIVER As Integer = 1797
    Private Const ERROR_INVALID_PRINTER_NAME As Integer = 1801
    Private Const ERROR_PRINTER_DRIVER_DOWNLOAD_NEEDED As Integer = 3019
    Private Const TIMEOUT_RETE_CONDIVISA_MS As Integer = 2500
    Private Const TIMEOUT_RETE_DIRETTA_MS As Integer = 1500
    Private Const TIMEOUT_STATI_ELENCO_MS As Integer = 5000
    Private Const VALIDITA_CACHE_RETE_MS As Integer = 5000
    Private Const CHIAVE_PORTE_TCPIP As String = "SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports\"
    Private Shared ReadOnly _lockRete As New Object()
    Private Shared ReadOnly _lockPdfium As New Object()
    Private Shared _pdfiumPronto As Boolean = False
    Private Shared _erroreDownloadPdfium As String = ""
    Private Shared _istanteErroreDownloadPdfium As DateTime = DateTime.MinValue
    Private Const NOME_DLL_PDFIUM As String = "pdfium.dll"
    Private Const ERROR_BAD_EXE_FORMAT As Integer = 193
    ' Sorgente dei binari PDFium per Windows: https://github.com/bblanchon/pdfium-binaries
    ' "latest" scarica sempre l'ultima versione. Per bloccare una versione precisa usare
    ' https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2FNNNN/  (NNNN = numero build)
    ' Una versione fissata, con le impronte dei pacchetti: "latest" cambierebbe senza che nessuno controlli cosa si carica nel programma.
    Private Const URL_BASE_PDFIUM As String = "https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F8165/"

    ''' <summary>L'impronta SHA-256 del pacchetto pdfium-win-architettura.tgz della versione fissata; "" se l'architettura non è prevista.</summary>
    Private Shared Function ImprontaPdfium(architettura As String) As String
        Select Case architettura
            Case "x64" : Return "ca7338ae7779b8b1b82f4580305c4d762d214f8df1d54b47647fe3cc75989ded"
            Case "x86" : Return "fc697d735516b2dd408072ecdd808a67e12c6402f3e762299015da1d81142986"
            Case "arm64" : Return "d65e405c59cffe6fe36c0935b90d40ddf3ce75ef284f3168a28dcf00d0388131"
            Case Else : Return ""
        End Select
    End Function
    Private Const TIMEOUT_DOWNLOAD_PDFIUM_MS As Integer = 60000
    Private Const PAUSA_DOPO_ERRORE_DOWNLOAD_PDFIUM_MS As Integer = 60000
    Private Const DIMENSIONE_MINIMA_PDFIUM As Long = 1024L * 1024L
    Private Const DIMENSIONE_MASSIMA_PDFIUM As Long = 100L * 1024L * 1024L
    Private Const MACCHINA_PE_X86 As Integer = &H14CI
    Private Const MACCHINA_PE_X64 As Integer = &H8664I
    Private Const MACCHINA_PE_ARM64 As Integer = &HAA64I
    Private Const FPDF_ERR_FILE As Integer = 2
    Private Const FPDF_ERR_FORMAT As Integer = 3
    Private Const FPDF_ERR_PASSWORD As Integer = 4
    Private Const FPDF_ERR_SECURITY As Integer = 5
    Private Const FPDF_ANNOT As Integer = &H1
    Private Const FPDF_PRINTING As Integer = &H800
    Private Const TOLLERANZA_FORMATO_FOGLIO As Double = 0.03
    Private Shared ReadOnly _cacheRete As New Dictionary(Of String, EsitoRaggiungibilita)(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _regexIPv4 As New Regex("(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])", RegexOptions.CultureInvariant)

    Private Const PRINTER_ENUM_LOCAL As UInteger = 2
    Private Const PRINTER_ENUM_CONNECTIONS As UInteger = 4
    Private Const PRINTER_ENUM_NAME As UInteger = &H8
    Private Const PRINTER_ENUM_REMOTE As UInteger = &H10
    Private Const PRINTER_ENUM_NETWORK As UInteger = &H40
    Private Const PRINTER_ENUM_CONTAINER As UInteger = &H8000
    Private Const PRINTER_ATTRIBUTE_NETWORK As UInteger = &H10
    Private Const PRINTER_ATTRIBUTE_LOCAL As UInteger = &H40
    Private Const RESOURCE_GLOBALNET As Integer = 2
    Private Const RESOURCETYPE_ANY As Integer = 0
    Private Const RESOURCETYPE_PRINT As Integer = 2
    Private Const RESOURCEUSAGE_CONTAINER As Integer = 2
    Private Const ERROR_NO_MORE_ITEMS As Integer = 259
    Private Const ERROR_MORE_DATA As Integer = 234
    Private Const PRINTER_STATUS_PAPER_JAM As UInteger = &H8
    Private Const PRINTER_STATUS_PAPER_OUT As UInteger = &H10
    Private Const PRINTER_STATUS_PAPER_PROBLEM As UInteger = &H40
    Private Const PRINTER_STATUS_OFFLINE As UInteger = &H80
    Private Const PRINTER_STATUS_OUTPUT_BIN_FULL As UInteger = &H800
    Private Const PRINTER_STATUS_PENDING_DELETION As UInteger = &H4
    Private Const PRINTER_STATUS_NOT_AVAILABLE As UInteger = &H1000
    Private Const PRINTER_STATUS_TONER_LOW As UInteger = &H20000
    Private Const PRINTER_STATUS_NO_TONER As UInteger = &H40000
    Private Const PRINTER_STATUS_USER_INTERVENTION As UInteger = &H100000
    Private Const PRINTER_STATUS_OUT_OF_MEMORY As UInteger = &H200000
    Private Const PRINTER_STATUS_DOOR_OPEN As UInteger = &H400000
    Private Const PRINTER_STATUS_ERROR As UInteger = &H2
    Private Const JOB_STATUS_PAUSED As UInteger = &H1
    Private Const JOB_STATUS_ERROR As UInteger = &H2
    Private Const JOB_STATUS_DELETING As UInteger = &H4
    Private Const JOB_STATUS_OFFLINE As UInteger = &H20
    Private Const JOB_STATUS_PAPEROUT As UInteger = &H40
    Private Const JOB_STATUS_PRINTING As UInteger = &H10
    Private Const JOB_STATUS_PRINTED As UInteger = &H80
    Private Const JOB_STATUS_DELETED As UInteger = &H100
    Private Const JOB_STATUS_USER_INTERVENTION As UInteger = &H400
    Private Const JOB_STATUS_COMPLETE As UInteger = &H1000
    Private Const DC_PAPERNAMES As Short = 16
    Private Const DC_DUPLEX As Short = 7
    Private Const DC_COLORDEVICE As Short = 32
    Private Const HORZRES As Integer = 8
    Private Const VERTRES As Integer = 10
    Private Const LOGPIXELSX As Integer = 88
    Private Const LOGPIXELSY As Integer = 90
    Private Const PHYSICALWIDTH As Integer = 110
    Private Const PHYSICALHEIGHT As Integer = 111
    Private Const PHYSICALOFFSETX As Integer = 112
    Private Const PHYSICALOFFSETY As Integer = 113
    Private Const DEFAULT_CHARSET As Integer = 1
    Private Const FW_NORMAL As Integer = 400
    Private Const OUT_TT_PRECIS As Integer = 4
    Private Const CLIP_DEFAULT_PRECIS As Integer = 0
    Private Const ANTIALIASED_QUALITY As Integer = 4
    Private Const DEFAULT_PITCH As Integer = 0
    Private Const DT_LEFT As UInteger = 0
    Private Const DT_WORDBREAK As UInteger = &H10
    Private Const DT_CALCRECT As UInteger = &H400
    Private Const DT_NOPREFIX As UInteger = &H800
    Private Const DT_EXPANDTABS As UInteger = &H40
    Private Const TRANSPARENT As Integer = 1
    Private Const PS_SOLID As Integer = 0
    Private Const DM_OUT_BUFFER As Integer = 2
    Private Const DM_IN_BUFFER As Integer = 8
    Private Const DM_ORIENTATION As Integer = &H1
    Private Const DM_COPIES As Integer = &H100
    Private Const DM_COLOR As Integer = &H800
    Private Const DM_DUPLEX As Integer = &H1000
    Private Const DM_PRINTQUALITY As Integer = &H400
    Private Const DMORIENT_PORTRAIT As Short = 1
    Private Const DMORIENT_LANDSCAPE As Short = 2
    Private Const DMDUP_SIMPLEX As Short = 1
    Private Const DMDUP_VERTICAL As Short = 2
    Private Const DMCOLOR_MONOCHROME As Short = 1
    Private Const DMCOLOR_COLOR As Short = 2
    Private Const DMRES_DRAFT As Short = -1
    Private Const DMRES_MEDIUM As Short = -3
    Private Const DMRES_HIGH As Short = -4
    Private Const TENTATIVI_MAX As Integer = 3
    Private Const PRINTER_CONTROL_PURGE As Integer = 3
    Private Const JOB_CONTROL_DELETE As Integer = 5
    Private Const JOB_CONTROL_CANCEL As Integer = 3
    Private Const PRINTER_ACCESS_USE As Integer = &H8
    Private Const PRINTER_ALL_ACCESS As Integer = &HF000C
    Private Const ERROR_ACCESS_DENIED As Integer = 5
    Private Const ERROR_INVALID_PARAMETER As Integer = 87
    Private Const ERROR_INSUFFICIENT_BUFFER As Integer = 122
    Private Const MASSIMO_LAVORI_CODA As UInteger = &HFFFFUI
    Private Const SVUOTA_CODA_TIMEOUT_MS As Integer = 30000
    Private Const SVUOTA_CODA_ATTESA_MS As Integer = 6000
    Private Const SVUOTA_CODA_INTERVALLO_MS As Integer = 150

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PRINTER_DEFAULTS
        Public pDatatype As IntPtr
        Public pDevMode As IntPtr
        Public DesiredAccess As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure PRINTER_INFO_4
        Public pPrinterName As String
        Public pServerName As String
        Public Attributes As UInteger
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure PRINTER_INFO_1
        Public Flags As UInteger
        Public pDescription As String
        Public pName As String
        Public pComment As String
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure PRINTER_INFO_6
        Public dwStatus As UInteger
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure SYSTEMTIME
        Public wYear As UShort
        Public wMonth As UShort
        Public wDayOfWeek As UShort
        Public wDay As UShort
        Public wHour As UShort
        Public wMinute As UShort
        Public wSecond As UShort
        Public wMilliseconds As UShort
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure JOB_INFO_1
        Public JobId As UInteger
        Public pPrinterName As String
        Public pMachineName As String
        Public pUserName As String
        Public pDocument As String
        Public pDatatype As String
        Public pStatus As String
        Public Status As UInteger
        Public Priority As UInteger
        Public Position As UInteger
        Public TotalPages As UInteger
        Public PagesPrinted As UInteger
        Public Submitted As SYSTEMTIME
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure DOCINFO
        Public cbSize As Integer
        Public lpszDocName As String
        Public lpszOutput As String
        Public lpszDatatype As String
        Public fwType As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure DEVMODE
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=32)>
        Public dmDeviceName As String
        Public dmSpecVersion As Short
        Public dmDriverVersion As Short
        Public dmSize As Short
        Public dmDriverExtra As Short
        Public dmFields As Integer
        Public dmOrientation As Short
        Public dmPaperSize As Short
        Public dmPaperLength As Short
        Public dmPaperWidth As Short
        Public dmScale As Short
        Public dmCopies As Short
        Public dmDefaultSource As Short
        Public dmPrintQuality As Short
        Public dmColor As Short
        Public dmDuplex As Short
        Public dmYResolution As Short
        Public dmTTOption As Short
        Public dmCollate As Short
        <MarshalAs(UnmanagedType.ByValTStr, SizeConst:=32)>
        Public dmFormName As String
        Public dmLogPixels As Short
        Public dmBitsPerPel As Integer
        Public dmPelsWidth As Integer
        Public dmPelsHeight As Integer
        Public dmDisplayFlags As Integer
        Public dmDisplayFrequency As Integer
        Public dmICMMethod As Integer
        Public dmICMIntent As Integer
        Public dmMediaType As Integer
        Public dmDitherType As Integer
        Public dmReserved1 As Integer
        Public dmReserved2 As Integer
        Public dmPanningWidth As Integer
        Public dmPanningHeight As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure PRINTER_INFO_5
        Public pPrinterName As String
        Public pPortName As String
        Public Attributes As UInteger
        Public DeviceNotSelectedTimeout As UInteger
        Public TransmissionRetryTimeout As UInteger
    End Structure

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function EnumPrinters(flags As UInteger, name As String, level As UInteger, pPrinterEnum As IntPtr, cbBuf As UInteger, ByRef pcbNeeded As UInteger, ByRef pcReturned As UInteger) As Boolean
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetDefaultPrinter(pszBuffer As StringBuilder, ByRef pcchBuffer As Integer) As Boolean
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function OpenPrinter(pPrinterName As String, ByRef phPrinter As IntPtr, pDefault As IntPtr) As Boolean
    End Function

    <DllImport("winspool.drv", EntryPoint:="OpenPrinterW", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function OpenPrinterConAccesso(pPrinterName As String, ByRef phPrinter As IntPtr, ByRef pDefault As PRINTER_DEFAULTS) As Boolean
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True, EntryPoint:="LoadLibraryW")>
    Private Shared Function LoadLibrary(lpFileName As String) As IntPtr
    End Function

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Sub FPDF_InitLibrary()
    End Sub

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall, CharSet:=CharSet.Ansi)>
    Private Shared Function FPDF_LoadMemDocument(dataBuf As IntPtr, size As Integer, password As String) As IntPtr
    End Function

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Function FPDF_GetLastError() As UInteger
    End Function

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Function FPDF_GetPageCount(documento As IntPtr) As Integer
    End Function

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Function FPDF_LoadPage(documento As IntPtr, indicePagina As Integer) As IntPtr
    End Function

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Function FPDF_GetPageWidth(pagina As IntPtr) As Double
    End Function

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Function FPDF_GetPageHeight(pagina As IntPtr) As Double
    End Function

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Sub FPDF_RenderPage(hdc As IntPtr, pagina As IntPtr, inizioX As Integer, inizioY As Integer, larghezza As Integer, altezza As Integer, rotazione As Integer, opzioni As Integer)
    End Sub

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Sub FPDF_ClosePage(pagina As IntPtr)
    End Sub

    <DllImport(NOME_DLL_PDFIUM, CallingConvention:=CallingConvention.StdCall)>
    Private Shared Sub FPDF_CloseDocument(documento As IntPtr)
    End Sub

    <DllImport("winspool.drv", SetLastError:=True)>
    Private Shared Function ClosePrinter(hPrinter As IntPtr) As Boolean
    End Function

    <DllImport("winspool.drv", SetLastError:=True)>
    Private Shared Function GetPrinter(hPrinter As IntPtr, level As Integer, pPrinter As IntPtr, cbBuf As Integer, ByRef pcbNeeded As Integer) As Boolean
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function AddPrinterConnection(pName As String) As Boolean
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function EnumJobs(hPrinter As IntPtr, FirstJob As UInteger, NoJobs As UInteger, Level As UInteger, pJob As IntPtr, cbBuf As UInteger, ByRef pcbNeeded As UInteger, ByRef pcReturned As UInteger) As Boolean
    End Function

    <DllImport("winspool.drv", SetLastError:=True)>
    Private Shared Function SetPrinter(hPrinter As IntPtr, Level As Integer, pPrinter As IntPtr, Command As Integer) As Boolean
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function SetJob(hPrinter As IntPtr, JobId As UInteger, Level As Integer, pJob As IntPtr, Command As Integer) As Boolean
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function DocumentProperties(hWnd As IntPtr, hPrinter As IntPtr, pDeviceName As String, pDevModeOutput As IntPtr, pDevModeInput As IntPtr, fMode As Integer) As Integer
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CreateDC(lpszDriver As String, lpszDevice As String, lpszOutput As String, lpInitData As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function CreateIC(lpszDriver As String, lpszDevice As String, lpszOutput As String, lpInitData As IntPtr) As IntPtr
    End Function

    <DllImport("winspool.drv", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function DeviceCapabilities(pDevice As String, pPort As String, fwCapability As Short, pOutput As IntPtr, pDevMode As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll", SetLastError:=True)>
    Private Shared Function ResetDC(hdc As IntPtr, lpInitData As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll", SetLastError:=True)>
    Private Shared Function DeleteDC(hdc As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode, EntryPoint:="StartDocW")>
    Private Shared Function StartDoc(hdc As IntPtr, ByRef lpdi As DOCINFO) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function StartPage(hdc As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function EndPage(hdc As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function EndDoc(hdc As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function AbortDoc(hdc As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function GetDeviceCaps(hdc As IntPtr, nIndex As Integer) As Integer
    End Function

    <DllImport("gdi32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function CreateFont(nHeight As Integer, nWidth As Integer, nEscapement As Integer, nOrientation As Integer, fnWeight As Integer, fdwItalic As UInteger, fdwUnderline As UInteger, fdwStrikeOut As UInteger, fdwCharSet As UInteger, fdwOutputPrecision As UInteger, fdwClipPrecision As UInteger, fdwQuality As UInteger, fdwPitchAndFamily As UInteger, lpszFace As String) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SelectObject(hdc As IntPtr, hgdiobj As IntPtr) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function DeleteObject(hObject As IntPtr) As Boolean
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetBkMode(hdc As IntPtr, iBkMode As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function SetTextColor(hdc As IntPtr, crColor As Integer) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function CreatePen(fnPenStyle As Integer, nWidth As Integer, crColor As Integer) As IntPtr
    End Function

    <DllImport("gdi32.dll")>
    Private Shared Function Rectangle(hdc As IntPtr, nLeftRect As Integer, nTopRect As Integer, nRightRect As Integer, nBottomRect As Integer) As Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function DrawText(hdc As IntPtr, lpchText As String, nCount As Integer, ByRef lpRect As RECT, uFormat As UInteger) As Integer
    End Function

    Private Class WGContestoPagina
        Public Hdc As IntPtr = IntPtr.Zero
        Public X As Integer = 0
        Public Margine As Integer = 0
        Public YContenutoInizio As Integer = 0
        Public LimiteBasso As Integer = 0
        Public LarghezzaUtile As Integer = 0
        Public LarghezzaPagina As Integer = 0
        Public AltezzaPagina As Integer = 0
        Public DpiX As Integer = 96
        Public DpiY As Integer = 96
        Public HFont As IntPtr = IntPtr.Zero
        Public HFontVecchio As IntPtr = IntPtr.Zero
        Public HFontIntestazione As IntPtr = IntPtr.Zero
        Public HPen As IntPtr = IntPtr.Zero
        Public HPenVecchio As IntPtr = IntPtr.Zero
        Public SoloMisura As Boolean = False
        Public TitoloDocumento As String = ""
        Public Settaggi As WGImpostazioniStampa
        Public TotalePagineConosciute As Integer = 0
        Public NumeroPagina As Integer = 0
        Public PaginaAperta As Boolean = False
        Public Y As Integer = 0

        Public Sub New()
        End Sub
    End Class

    Public Shared Function UltimoErrore() As String
        Try
            SyncLock _lockErrore
                Return If(_ultimoErrore, "")
            End SyncLock
        Catch
            Return ""
        End Try
    End Function

    Private Shared Sub ImpostaErrore(messaggio As String)
        Try
            SyncLock _lockErrore
                _ultimoErrore = If(messaggio, "")
            End SyncLock
            If Not String.IsNullOrWhiteSpace(messaggio) Then
                ScriviLog("Errore stampa: " & messaggio)
            End If
        Catch
        End Try
    End Sub

    Private Shared Sub AzzeraErrore()
        Try
            SyncLock _lockErrore
                _ultimoErrore = ""
                _ultimoAvviso = ""
            End SyncLock
        Catch
        End Try
    End Sub

    Public Shared Function UltimoAvviso() As String
        Try
            SyncLock _lockErrore
                Return If(_ultimoAvviso, "")
            End SyncLock
        Catch
            Return ""
        End Try
    End Function

    Private Shared Sub ImpostaAvviso(messaggio As String)
        Try
            SyncLock _lockErrore
                _ultimoAvviso = If(messaggio, "")
            End SyncLock
        Catch ex As Exception
            ScriviLog("ImpostaAvviso: " & ex.Message)
        End Try
    End Sub

    Private Shared Function CartellaBaseProgramma() As String
        Try
            Dim baseDir As String = ""
            Try
                baseDir = Application.StartupPath
            Catch
                baseDir = ""
            End Try
            If String.IsNullOrWhiteSpace(baseDir) Then
                Try
                    baseDir = AppDomain.CurrentDomain.BaseDirectory
                Catch
                    baseDir = ""
                End Try
            End If
            If String.IsNullOrWhiteSpace(baseDir) Then
                Try
                    Dim ingresso As Assembly = Assembly.GetEntryAssembly()
                    If ingresso IsNot Nothing Then baseDir = Path.GetDirectoryName(ingresso.Location)
                Catch
                    baseDir = ""
                End Try
            End If
            If String.IsNullOrWhiteSpace(baseDir) Then
                Try
                    baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                Catch
                    baseDir = ""
                End Try
            End If
            Return If(baseDir, "")
        Catch ex As Exception
            Return ""
        End Try
    End Function


    ' La cartella dei log: con il programma installato (Programmi\WinGabriel) %LocalAppData%\WinGabriel\logs, con quello portatile logs
    ' nella cartella del programma. Lo stesso criterio di WGPercorsiApplicazione, che qui non si può usare.
    Private Shared Function CartellaLogApplicazione() As String
        Try
            Dim programma As String = AppDomain.CurrentDomain.BaseDirectory.TrimEnd("\"c)
            For Each cartella As Environment.SpecialFolder In New Environment.SpecialFolder() {Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86}
                If String.Equals(programma, Path.Combine(Environment.GetFolderPath(cartella), "WinGabriel").TrimEnd("\"c), StringComparison.OrdinalIgnoreCase) Then
                    Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGabriel", "logs")
                End If
            Next
            Return Path.Combine(programma, "logs")
        Catch
            Return Path.Combine(Path.GetTempPath(), "WinGabriel")
        End Try
    End Function

    Private Shared Function PercorsoFileLog() As String
        Try
            Return Path.Combine(CartellaLogApplicazione(), "log.txt")
        Catch
            Try
                Return Path.Combine(Path.GetTempPath(), "log.txt")
            Catch
                Return "log.txt"
            End Try
        End Try
    End Function

    Private Shared Sub ScriviLog(messaggio As String)
        Try
            Dim percorso As String = PercorsoFileLog()
            Dim cartella As String = Path.GetDirectoryName(percorso)
            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If
            Dim riga As String = DateTime.Now.ToString() & ": WGStampante: " & If(messaggio, "") & Environment.NewLine
            SyncLock _lockLog
                Try
                    Using fs As New FileStream(percorso, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                        Dim bytes As Byte() = Encoding.UTF8.GetBytes(riga)
                        fs.Write(bytes, 0, bytes.Length)
                    End Using
                Catch
                    Try
                        Dim riserva As String = Path.Combine(Path.GetTempPath(), "log.txt")
                        Using fsRiserva As New FileStream(riserva, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                            Dim bytesRiserva As Byte() = Encoding.UTF8.GetBytes(riga)
                            fsRiserva.Write(bytesRiserva, 0, bytesRiserva.Length)
                        End Using
                    Catch
                    End Try
                End Try
            End SyncLock
        Catch
        End Try
    End Sub

    Private Shared Function ImpostaEsitoErrore(esito As WGEsitoStampa, messaggio As String) As WGEsitoStampa
        Try
            esito.Riuscita = False
            esito.Errore = If(messaggio, "")
            ImpostaErrore(messaggio)
        Catch
        End Try
        Return esito
    End Function

    Private Shared Function EsitoDaStatoCorrente(esito As WGEsitoStampa) As WGEsitoStampa
        Try
            esito.Riuscita = False
            esito.Errore = UltimoErrore()
        Catch
        End Try
        Return esito
    End Function

    Private Shared Function CodiceErrore() As Integer
        Try
            Return Marshal.GetLastWin32Error()
        Catch
            Return 0
        End Try
    End Function

    Private Shared Function NormalizzaNomeStampante(nome As String) As String
        Try
            If nome Is Nothing Then Return ""
            Return nome.Trim()
        Catch ex As Exception
            ScriviLog("NormalizzaNomeStampante: " & ex.Message)
            Return If(nome, "")
        End Try
    End Function

    Public Shared Function PercorsoReteValido(percorso As String) As Boolean
        Try
            Dim nome As String = NormalizzaNomeStampante(percorso)
            If nome.Length < 5 Then Return False
            If Not nome.StartsWith("\\", StringComparison.Ordinal) Then Return False
            Dim resto As String = nome.Substring(2)
            Dim parti As String() = resto.Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
            If parti.Length < 2 Then Return False
            If String.IsNullOrWhiteSpace(parti(0)) Then Return False
            If String.IsNullOrWhiteSpace(parti(1)) Then Return False
            Return True
        Catch ex As Exception
            ScriviLog("PercorsoReteValido: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function StampantePredefinitaSistema() As String
        Try
            Dim dimensione As Integer = 512
            Dim buffer As New StringBuilder(dimensione)
            If GetDefaultPrinter(buffer, dimensione) Then
                Dim nome As String = NormalizzaNomeStampante(buffer.ToString())
                Return nome
            End If
            If dimensione > 512 Then
                buffer = New StringBuilder(dimensione)
                If GetDefaultPrinter(buffer, dimensione) Then
                    Dim nomeLargo As String = NormalizzaNomeStampante(buffer.ToString())
                    Return nomeLargo
                End If
            End If
            Return ""
        Catch ex As Exception
            ScriviLog("StampantePredefinitaSistema: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function StampanteRaggiungibile(nomeStampante As String) As Boolean
        Dim hPrinter As IntPtr = IntPtr.Zero
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then Return False
            If Not OpenPrinter(nome, hPrinter, IntPtr.Zero) Then
                ScriviLog("StampanteRaggiungibile: OpenPrinter fallita per " & nome & " err=" & CodiceErrore().ToString())
                Return False
            End If
            Return True
        Catch ex As Exception
            ScriviLog("StampanteRaggiungibile: " & ex.Message)
            Return False
        Finally
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch
            End Try
        End Try
    End Function

    Public Shared Function AggiungiStampanteRete(percorso As String) As Boolean
        Try
            AzzeraErrore()
            Dim nome As String = NormalizzaNomeStampante(percorso)
            If Not PercorsoReteValido(nome) Then
                ImpostaErrore("Scrivere il percorso nella forma \\computer\stampante.")
                Return False
            End If

            Dim info As InfoStampante = InformazioniStampante(nome)
            Dim problemaRete As String = ""
            If Not VerificaRaggiungibilitaInfo(info, problemaRete) Then
                ImpostaErrore(problemaRete)
                Return False
            End If

            If StampanteInstallata(nome) Then Return True
            If AddPrinterConnection(nome) Then Return True

            Dim codice As Integer = CodiceErrore()
            If StampanteInstallata(nome) Then Return True
            ImpostaErrore(MessaggioErroreCollegamento(codice, info))
            Return False
        Catch ex As Exception
            ScriviLog("AggiungiStampanteRete: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ImpostaErrore("Impossibile collegare la stampante.")
            Return False
        End Try
    End Function

    Private Shared Function MessaggioErroreCollegamento(codice As Integer, info As InfoStampante) As String
        Try
            Dim computer As String = If(info Is Nothing, "", If(info.Server, ""))
            Dim condivisione As String = If(info Is Nothing, "", If(info.NomeCondivisione, ""))
            Select Case codice
                Case ERROR_ACCESS_DENIED, ERROR_PRINTER_DRIVER_DOWNLOAD_NEEDED
                    Return "Per installare il driver di questa stampante servono i permessi di amministratore: avvia WinGabriel come amministratore e riprova."
                Case ERROR_BAD_NET_NAME, ERROR_INVALID_PRINTER_NAME, ERROR_FILE_NOT_FOUND
                    Return "Sul computer " & computer & " non c'è una stampante condivisa con il nome " & condivisione & "."
                Case ERROR_BAD_NETPATH
                    Return "Il computer " & computer & " non si trova in rete."
                Case ERROR_RPC_S_SERVER_UNAVAILABLE
                    Return "Il servizio di stampa del computer " & computer & " non risponde."
                Case ERROR_UNKNOWN_PRINTER_DRIVER
                    Return "Il driver di questa stampante non è disponibile."
                Case ERROR_LOGON_FAILURE, ERROR_NOT_AUTHENTICATED
                    Return "Il computer " & computer & " ha rifiutato l'accesso con le credenziali in uso."
                Case Else
                    Return "Impossibile collegare la stampante, errore " & codice.ToString() & "."
            End Select
        Catch ex As Exception
            ScriviLog("MessaggioErroreCollegamento: " & ex.Message)
            Return "Impossibile collegare la stampante."
        End Try
    End Function

    Public Shared Function EtichettaStampante(info As InfoStampante, Optional nomeSelezionato As String = "") As String
        Try
            If info Is Nothing OrElse String.IsNullOrWhiteSpace(info.Nome) Then Return ""
            Dim etichetta As String
            If info.Tipo = TipoStampante.Condivisa Then
                etichetta = If(String.IsNullOrWhiteSpace(info.NomeCondivisione), info.Nome, info.NomeCondivisione)
                If Not String.IsNullOrWhiteSpace(info.Server) Then etichetta &= " condivisa dal computer " & info.Server
            ElseIf info.Tipo = TipoStampante.ReteDiretta Then
                etichetta = info.Nome & " di rete"
            Else
                etichetta = info.Nome
                If info.Rete Then etichetta &= " in rete"
            End If
            If info.Predefinita Then etichetta &= " predefinita di Windows"
            If Not String.IsNullOrWhiteSpace(nomeSelezionato) AndAlso String.Equals(info.Nome, nomeSelezionato, StringComparison.OrdinalIgnoreCase) Then
                etichetta &= " selezionata"
            End If
            If Not String.IsNullOrWhiteSpace(info.Stato) Then etichetta &= " " & info.Stato
            Return etichetta
        Catch ex As Exception
            ScriviLog("EtichettaStampante: " & ex.Message)
            If info Is Nothing Then Return ""
            Return If(info.Nome, "")
        End Try
    End Function

    Public Shared Function ElencoStampanti(Optional includiNome As String = "", Optional annullamento As CancellationToken = Nothing) As List(Of InfoStampante)
        Dim risultato As New List(Of InfoStampante)()
        Try
            If annullamento.IsCancellationRequested Then Return risultato
            Dim predefinita As String = StampantePredefinitaSistema()
            Dim visti As New Dictionary(Of String, InfoStampante)(StringComparer.OrdinalIgnoreCase)

            AggiungiStampantiEnumerate(visti, predefinita, PRINTER_ENUM_LOCAL Or PRINTER_ENUM_CONNECTIONS, 4)

            Dim extra As String = NormalizzaNomeStampante(includiNome)
            If Not String.IsNullOrWhiteSpace(extra) Then
                SyncLock _lockElenco
                    If Not visti.ContainsKey(extra) Then
                        Dim infoExtra As New InfoStampante(extra, String.Equals(extra, predefinita, StringComparison.OrdinalIgnoreCase))
                        infoExtra.Rete = PercorsoReteValido(extra)
                        visti(extra) = infoExtra
                    End If
                End SyncLock
            End If

            SyncLock _lockElenco
                For Each coppia As KeyValuePair(Of String, InfoStampante) In visti
                    risultato.Add(coppia.Value)
                Next
            End SyncLock
            risultato.Sort(New Comparison(Of InfoStampante)(AddressOf ConfrontaStampanti))

            For Each infoTipo As InfoStampante In risultato
                If annullamento.IsCancellationRequested Then Exit For
                Try
                    ClassificaStampante(infoTipo)
                Catch exTipo As Exception
                    ScriviLog("ElencoStampanti classificazione " & infoTipo.Nome & ": " & exTipo.Message)
                End Try
            Next

            If Not annullamento.IsCancellationRequested Then
                LeggiStatiInParallelo(risultato, annullamento)
            End If
        Catch ex As Exception
            ScriviLog("ElencoStampanti: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return risultato
    End Function

    Private Shared Function ConfrontaStampanti(a As InfoStampante, b As InfoStampante) As Integer
        Try
            Dim nomeA As String = If(a Is Nothing, "", If(a.Nome, ""))
            Dim nomeB As String = If(b Is Nothing, "", If(b.Nome, ""))
            Return String.Compare(nomeA, nomeB, StringComparison.CurrentCultureIgnoreCase)
        Catch ex As Exception
            ScriviLog("ConfrontaStampanti: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Class WGLavoroStato
        Public Info As InfoStampante = Nothing
        Public Stato As String = ""

        Public Sub Esegui()
            Try
                Stato = DescrizioneStatoDaInfo(Info)
            Catch ex As Exception
                ScriviLog("WGLavoroStato.Esegui: " & ex.Message & Environment.NewLine & ex.StackTrace)
                Stato = ""
            End Try
        End Sub
    End Class

    Private Shared Sub LeggiStatiInParallelo(elenco As List(Of InfoStampante), annullamento As CancellationToken)
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 Then Return
            Dim lavori As New List(Of WGLavoroStato)()
            Dim esecutori As New List(Of Thread)()

            For Each info As InfoStampante In elenco
                Try
                    Dim lavoro As New WGLavoroStato()
                    lavoro.Info = info
                    Dim esecutore As New Thread(AddressOf lavoro.Esegui)
                    esecutore.IsBackground = True
                    esecutore.Name = "WGStampante.Stato"
                    esecutore.Start()
                    lavori.Add(lavoro)
                    esecutori.Add(esecutore)
                Catch exAvvio As Exception
                    ScriviLog("LeggiStatiInParallelo avvio " & info.Nome & ": " & exAvvio.Message)
                End Try
            Next

            Dim cronometro As Stopwatch = Stopwatch.StartNew()
            For i As Integer = 0 To esecutori.Count - 1
                Try
                    Dim concluso As Boolean = False
                    Do
                        If esecutori(i).Join(100) Then
                            concluso = True
                            Exit Do
                        End If
                        If annullamento.IsCancellationRequested Then Exit Do
                        If cronometro.ElapsedMilliseconds >= TIMEOUT_STATI_ELENCO_MS Then Exit Do
                    Loop
                    If concluso Then
                        lavori(i).Info.Stato = If(lavori(i).Stato, "")
                    ElseIf Not annullamento.IsCancellationRequested Then
                        lavori(i).Info.Stato = "Lo stato non è disponibile."
                    End If
                Catch exAttesa As Exception
                    ScriviLog("LeggiStatiInParallelo attesa: " & exAttesa.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("LeggiStatiInParallelo: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Shared Function InformazioniStampante(nomeStampante As String) As InfoStampante
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            Dim info As New InfoStampante(nome, String.Equals(nome, StampantePredefinitaSistema(), StringComparison.OrdinalIgnoreCase))
            If String.IsNullOrWhiteSpace(nome) Then Return info
            ClassificaStampante(info)
            Return info
        Catch ex As Exception
            ScriviLog("InformazioniStampante: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New InfoStampante(If(nomeStampante, ""), False)
        End Try
    End Function

    Private Shared Sub ClassificaStampante(info As InfoStampante)
        Try
            If info Is Nothing Then Return
            Dim nome As String = NormalizzaNomeStampante(info.Nome)
            If String.IsNullOrWhiteSpace(nome) Then Return

            If nome.StartsWith("\\", StringComparison.Ordinal) Then
                info.Tipo = TipoStampante.Condivisa
                info.Rete = True
                Dim parti As String() = nome.Substring(2).Split(New Char() {"\"c}, StringSplitOptions.RemoveEmptyEntries)
                If parti.Length > 0 Then
                    info.Server = parti(0)
                    info.Indirizzo = parti(0)
                End If
                If parti.Length > 1 Then info.NomeCondivisione = parti(1)
                info.PortaTcp = 445
                Return
            End If

            info.Porta = LeggiPortaStampante(nome)
            AnalizzaPorta(info.Porta, info)
            info.Rete = info.Tipo <> TipoStampante.Locale
        Catch ex As Exception
            ScriviLog("ClassificaStampante: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function LeggiPortaStampante(nome As String) As String
        Dim hPrinter As IntPtr = IntPtr.Zero
        Try
            If Not OpenPrinter(nome, hPrinter, IntPtr.Zero) OrElse hPrinter = IntPtr.Zero Then
                hPrinter = IntPtr.Zero
                Return ""
            End If
            Dim needed As Integer = 0
            GetPrinter(hPrinter, 5, IntPtr.Zero, 0, needed)
            If needed <= 0 Then Return ""
            Dim buffer As IntPtr = Marshal.AllocHGlobal(needed)
            Try
                If Not GetPrinter(hPrinter, 5, buffer, needed, needed) Then
                    ScriviLog("LeggiPortaStampante: GetPrinter fallita per " & nome & " err=" & CodiceErrore().ToString())
                    Return ""
                End If
                Dim infoNativa As PRINTER_INFO_5 = DirectCast(Marshal.PtrToStructure(buffer, GetType(PRINTER_INFO_5)), PRINTER_INFO_5)
                Return If(infoNativa.pPortName, "").Trim()
            Finally
                Marshal.FreeHGlobal(buffer)
            End Try
        Catch ex As Exception
            ScriviLog("LeggiPortaStampante: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        Finally
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch exChiusura As Exception
                ScriviLog("LeggiPortaStampante chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Private Shared Sub AnalizzaPorta(porta As String, info As InfoStampante)
        Try
            info.Tipo = TipoStampante.Locale
            info.Indirizzo = ""
            info.PortaTcp = 0
            If String.IsNullOrWhiteSpace(porta) Then Return

            Dim prima As String = porta.Split(","c)(0).Trim()
            If prima.Length = 0 Then Return

            If prima.StartsWith("WSD", StringComparison.OrdinalIgnoreCase) OrElse prima.StartsWith("BJNP", StringComparison.OrdinalIgnoreCase) Then
                info.Tipo = TipoStampante.ReteDiretta
                Return
            End If

            If prima.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse prima.StartsWith("https://", StringComparison.OrdinalIgnoreCase) OrElse prima.StartsWith("ipp://", StringComparison.OrdinalIgnoreCase) OrElse prima.StartsWith("ipps://", StringComparison.OrdinalIgnoreCase) Then
                info.Tipo = TipoStampante.ReteDiretta
                Dim indirizzoUri As Uri = Nothing
                If Uri.TryCreate(prima, UriKind.Absolute, indirizzoUri) AndAlso indirizzoUri IsNot Nothing Then
                    info.Indirizzo = indirizzoUri.Host
                    info.PortaTcp = If(indirizzoUri.Port > 0, indirizzoUri.Port, 631)
                End If
                Return
            End If

            If LeggiPortaTcpDalRegistro(prima, info) Then Return

            Dim corrispondenza As Match = _regexIPv4.Match(prima)
            If corrispondenza.Success Then
                info.Tipo = TipoStampante.ReteDiretta
                info.Indirizzo = corrispondenza.Value
                info.PortaTcp = 9100
            End If
        Catch ex As Exception
            ScriviLog("AnalizzaPorta " & porta & ": " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function LeggiPortaTcpDalRegistro(porta As String, info As InfoStampante) As Boolean
        Try
            Using chiave As RegistryKey = Registry.LocalMachine.OpenSubKey(CHIAVE_PORTE_TCPIP & porta, False)
                If chiave Is Nothing Then Return False
                Dim host As String = Convert.ToString(chiave.GetValue("HostName", ""))
                If String.IsNullOrWhiteSpace(host) Then host = Convert.ToString(chiave.GetValue("IPAddress", ""))
                Dim protocollo As Integer = 1
                Dim numeroPorta As Integer = 9100
                Try
                    protocollo = Convert.ToInt32(chiave.GetValue("Protocol", 1))
                Catch exProtocollo As Exception
                    ScriviLog("LeggiPortaTcpDalRegistro protocollo " & porta & ": " & exProtocollo.Message)
                End Try
                Try
                    numeroPorta = Convert.ToInt32(chiave.GetValue("PortNumber", 9100))
                Catch exNumero As Exception
                    ScriviLog("LeggiPortaTcpDalRegistro numero porta " & porta & ": " & exNumero.Message)
                End Try
                info.Tipo = TipoStampante.ReteDiretta
                info.Indirizzo = If(host, "").Trim()
                If protocollo = 2 Then
                    info.PortaTcp = 515
                ElseIf numeroPorta > 0 Then
                    info.PortaTcp = numeroPorta
                Else
                    info.PortaTcp = 9100
                End If
                Return True
            End Using
        Catch ex As Exception
            ScriviLog("LeggiPortaTcpDalRegistro " & porta & ": " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Shared Function VerificaRaggiungibilita(nomeStampante As String, ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then Return True
            Return VerificaRaggiungibilitaInfo(InformazioniStampante(nome), messaggio)
        Catch ex As Exception
            ScriviLog("VerificaRaggiungibilita: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Private Shared Function VerificaRaggiungibilitaInfo(info As InfoStampante, ByRef messaggio As String) As Boolean
        messaggio = ""
        Try
            If info Is Nothing Then Return True
            If info.Tipo = TipoStampante.Locale Then Return True
            If String.IsNullOrWhiteSpace(info.Indirizzo) Then Return True

            Dim porte As New List(Of Integer)()
            Dim attesa As Integer
            If info.Tipo = TipoStampante.Condivisa Then
                porte.Add(445)
                porte.Add(139)
                attesa = TIMEOUT_RETE_CONDIVISA_MS
            Else
                For Each candidata As Integer In New Integer() {info.PortaTcp, 9100, 631, 80, 515}
                    If candidata > 0 AndAlso Not porte.Contains(candidata) Then porte.Add(candidata)
                Next
                attesa = TIMEOUT_RETE_DIRETTA_MS
            End If

            If HostRaggiungibileConCache(info.Indirizzo, porte, attesa) Then Return True

            If info.Tipo = TipoStampante.Condivisa Then
                messaggio = "Il computer " & info.Server & ", che condivide la stampante, non è raggiungibile. Verificare che sia acceso e collegato alla rete."
            Else
                messaggio = "La stampante " & info.Nome & " non risponde sulla rete. Verificare che sia accesa e collegata alla rete."
            End If
            Return False
        Catch ex As Exception
            ScriviLog("VerificaRaggiungibilitaInfo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            messaggio = ""
            Return True
        End Try
    End Function

    Private Class EsitoRaggiungibilita
        Public Istante As DateTime = DateTime.MinValue
        Public Raggiungibile As Boolean = False
    End Class

    Private Shared Function HostRaggiungibileConCache(host As String, porte As List(Of Integer), attesaMs As Integer) As Boolean
        Try
            Dim chiave As String = host.Trim().ToLowerInvariant()
            SyncLock _lockRete
                Dim memorizzato As EsitoRaggiungibilita = Nothing
                If _cacheRete.TryGetValue(chiave, memorizzato) AndAlso memorizzato IsNot Nothing Then
                    If (DateTime.UtcNow - memorizzato.Istante).TotalMilliseconds < VALIDITA_CACHE_RETE_MS Then Return memorizzato.Raggiungibile
                End If
            End SyncLock

            Dim raggiungibile As Boolean = HostRaggiungibile(host, porte, attesaMs)

            SyncLock _lockRete
                Dim nuovo As New EsitoRaggiungibilita()
                nuovo.Istante = DateTime.UtcNow
                nuovo.Raggiungibile = raggiungibile
                _cacheRete(chiave) = nuovo
            End SyncLock
            Return raggiungibile
        Catch ex As Exception
            ScriviLog("HostRaggiungibileConCache: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Private Shared Function HostRaggiungibile(host As String, porte As List(Of Integer), attesaMs As Integer) As Boolean
        Dim clienti As New List(Of TcpClient)()
        Dim tentativi As New List(Of IAsyncResult)()
        Dim conclusi As New List(Of Boolean)()
        Try
            If String.IsNullOrWhiteSpace(host) OrElse porte Is Nothing OrElse porte.Count = 0 Then Return False

            For Each porta As Integer In porte
                Dim cliente As TcpClient = Nothing
                Try
                    cliente = New TcpClient()
                    Dim tentativo As IAsyncResult = cliente.BeginConnect(host, porta, Nothing, Nothing)
                    clienti.Add(cliente)
                    tentativi.Add(tentativo)
                    conclusi.Add(False)
                Catch exSocket As SocketException
                    If cliente IsNot Nothing Then cliente.Close()
                    If exSocket.SocketErrorCode = SocketError.ConnectionRefused Then Return True
                Catch exAvvio As Exception
                    ScriviLog("HostRaggiungibile avvio " & host & ":" & porta.ToString() & ": " & exAvvio.Message)
                    If cliente IsNot Nothing Then cliente.Close()
                End Try
            Next

            If tentativi.Count = 0 Then Return False

            Dim cronometro As Stopwatch = Stopwatch.StartNew()
            Do
                Dim tuttiConclusi As Boolean = True
                For i As Integer = 0 To tentativi.Count - 1
                    If conclusi(i) Then Continue For
                    If Not tentativi(i).IsCompleted Then
                        tuttiConclusi = False
                        Continue For
                    End If
                    conclusi(i) = True
                    Try
                        clienti(i).EndConnect(tentativi(i))
                        Return True
                    Catch exSocket As SocketException
                        If exSocket.SocketErrorCode = SocketError.ConnectionRefused Then Return True
                    Catch exFine As Exception
                        ScriviLog("HostRaggiungibile esito " & host & ": " & exFine.Message)
                    End Try
                Next
                If tuttiConclusi Then Return False
                If cronometro.ElapsedMilliseconds >= attesaMs Then Return False
                Thread.Sleep(25)
            Loop
        Catch ex As Exception
            ScriviLog("HostRaggiungibile: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        Finally
            For Each cliente As TcpClient In clienti
                Try
                    cliente.Close()
                Catch exChiusura As Exception
                    ScriviLog("HostRaggiungibile chiusura: " & exChiusura.Message)
                End Try
            Next
        End Try
    End Function

    Private Shared Function StampanteInstallata(nome As String) As Boolean
        Try
            Dim visti As New Dictionary(Of String, InfoStampante)(StringComparer.OrdinalIgnoreCase)
            AggiungiStampantiEnumerate(visti, "", PRINTER_ENUM_LOCAL Or PRINTER_ENUM_CONNECTIONS, 4)
            SyncLock _lockElenco
                Return visti.ContainsKey(NormalizzaNomeStampante(nome))
            End SyncLock
        Catch ex As Exception
            ScriviLog("StampanteInstallata: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Shared Function SvuotaCodaStampa(nomeStampante As String) As Boolean
        Try
            AzzeraErrore()
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then
                ImpostaErrore("Nessuna stampante indicata.")
                Return False
            End If

            Dim lavoro As New WGLavoroSvuotaCoda()
            lavoro.Nome = nome

            Dim esecutore As New Thread(AddressOf lavoro.Esegui)
            esecutore.IsBackground = True
            esecutore.Name = "WGStampante.SvuotaCoda"
            esecutore.Start()

            If Not esecutore.Join(SVUOTA_CODA_TIMEOUT_MS) Then
                ImpostaErrore("La stampante " & nome & " non risponde entro " & (SVUOTA_CODA_TIMEOUT_MS \ 1000).ToString() & " secondi. Verificare che sia accesa e collegata, poi riprovare.")
                Return False
            End If

            If lavoro.Riuscito Then Return True

            Dim messaggio As String = If(lavoro.Errore, "")
            If String.IsNullOrWhiteSpace(messaggio) Then messaggio = "Impossibile svuotare la coda di stampa."
            ImpostaErrore(messaggio)
            Return False
        Catch ex As Exception
            ScriviLog("SvuotaCodaStampa: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ImpostaErrore("Impossibile svuotare la coda di stampa.")
            Return False
        End Try
    End Function

    Private Class WGLavoroSvuotaCoda
        Public Nome As String = ""
        Public Riuscito As Boolean = False
        Public Errore As String = ""

        Public Sub Esegui()
            Try
                Dim messaggio As String = ""
                Riuscito = SvuotaCodaInterna(Nome, messaggio)
                Errore = messaggio
            Catch ex As Exception
                ScriviLog("WGLavoroSvuotaCoda.Esegui: " & ex.Message & Environment.NewLine & ex.StackTrace)
                Riuscito = False
                Errore = "Impossibile svuotare la coda di stampa."
            End Try
        End Sub
    End Class

    Private Class LavoroCoda
        Public Id As UInteger = 0
        Public Utente As String = ""
    End Class

    Private Shared Function SvuotaCodaInterna(nome As String, ByRef errore As String) As Boolean
        Dim hPrinter As IntPtr = IntPtr.Zero
        errore = ""
        Try
            Dim info As InfoStampante = InformazioniStampante(nome)
            Dim condivisa As Boolean = info.Tipo = TipoStampante.Condivisa

            If condivisa Then
                Dim problemaRete As String = ""
                If Not VerificaRaggiungibilitaInfo(info, problemaRete) Then
                    errore = problemaRete
                    Return False
                End If
            End If

            Dim amministratore As Boolean = False
            If Not ApriStampantePerCoda(nome, hPrinter, amministratore, Not condivisa) Then
                errore = If(condivisa, "La coda di stampa del computer " & info.Server & " non è raggiungibile.", "La stampante " & nome & " non è raggiungibile.")
                Return False
            End If

            Dim negati As New HashSet(Of UInteger)()

            If amministratore Then
                If SetPrinter(hPrinter, 0, IntPtr.Zero, PRINTER_CONTROL_PURGE) Then
                    Dim dopoPurge As List(Of LavoroCoda) = AttendiCodaVuota(hPrinter, nome, negati, SVUOTA_CODA_ATTESA_MS)
                    If dopoPurge IsNot Nothing AndAlso dopoPurge.Count = 0 Then Return True
                Else
                    ScriviLog("SvuotaCodaInterna: SetPrinter PURGE fallita per " & nome & " err=" & CodiceErrore().ToString())
                End If
            End If

            Dim letturaRiuscita As Boolean = False
            Dim lavori As List(Of LavoroCoda) = ElencoLavoriCoda(hPrinter, nome, letturaRiuscita)
            If Not letturaRiuscita Then
                errore = "Impossibile leggere la coda di stampa di " & nome & "."
                Return False
            End If
            If lavori.Count = 0 Then Return True

            Dim altriUtenti As New HashSet(Of UInteger)()
            Dim daEliminare As New List(Of UInteger)()
            Dim utenteCorrente As String = Environment.UserName
            For Each lavoro As LavoroCoda In lavori
                If condivisa AndAlso Not String.IsNullOrWhiteSpace(lavoro.Utente) AndAlso Not String.Equals(lavoro.Utente, utenteCorrente, StringComparison.OrdinalIgnoreCase) Then
                    altriUtenti.Add(lavoro.Id)
                    negati.Add(lavoro.Id)
                Else
                    daEliminare.Add(lavoro.Id)
                End If
            Next

            Dim rimanenti As List(Of LavoroCoda) = lavori
            If daEliminare.Count > 0 Then
                EliminaLavoriCoda(hPrinter, nome, daEliminare, JOB_CONTROL_DELETE, negati)
                rimanenti = AttendiCodaVuota(hPrinter, nome, negati, SVUOTA_CODA_ATTESA_MS)
                If rimanenti Is Nothing Then
                    errore = "Impossibile verificare lo stato della coda di stampa di " & nome & "."
                    Return False
                End If
                If rimanenti.Count = 0 Then Return True

                Dim daRiprovare As New List(Of UInteger)()
                For Each lavoro As LavoroCoda In rimanenti
                    If Not negati.Contains(lavoro.Id) Then daRiprovare.Add(lavoro.Id)
                Next
                If daRiprovare.Count > 0 Then
                    EliminaLavoriCoda(hPrinter, nome, daRiprovare, JOB_CONTROL_CANCEL, negati)
                    rimanenti = AttendiCodaVuota(hPrinter, nome, negati, SVUOTA_CODA_ATTESA_MS \ 2)
                    If rimanenti Is Nothing Then
                        errore = "Impossibile verificare lo stato della coda di stampa di " & nome & "."
                        Return False
                    End If
                    If rimanenti.Count = 0 Then Return True
                End If
            End If

            Dim contaAltri As Integer = 0
            Dim contaNegati As Integer = 0
            Dim contaBloccati As Integer = 0
            For Each lavoro As LavoroCoda In rimanenti
                If altriUtenti.Contains(lavoro.Id) Then
                    contaAltri += 1
                ElseIf negati.Contains(lavoro.Id) Then
                    contaNegati += 1
                Else
                    contaBloccati += 1
                End If
            Next

            Dim testo As New StringBuilder()
            If contaAltri > 0 Then
                If daEliminare.Count > 0 AndAlso contaNegati = 0 AndAlso contaBloccati = 0 Then testo.Append("I tuoi documenti sono stati eliminati. ")
                If contaAltri = 1 Then
                    testo.Append("Resta in coda un documento di un altro utente, che solo chi gestisce il computer " & info.Server & " è in grado di eliminare.")
                Else
                    testo.Append("Restano in coda " & contaAltri.ToString() & " documenti di altri utenti, che solo chi gestisce il computer " & info.Server & " è in grado di eliminare.")
                End If
            End If
            If contaNegati > 0 Then
                If testo.Length > 0 Then testo.Append(" ")
                If contaNegati = 1 Then
                    testo.Append("Un documento non è stato eliminato perché appartiene a un altro utente o richiede i permessi di amministratore.")
                Else
                    testo.Append(contaNegati.ToString() & " documenti non sono stati eliminati perché appartengono ad altri utenti o richiedono i permessi di amministratore.")
                End If
            End If
            If contaBloccati > 0 Then
                If testo.Length > 0 Then testo.Append(" ")
                If contaBloccati = 1 Then
                    testo.Append("Un documento resta in coda perché la stampante non lo rilascia.")
                Else
                    testo.Append(contaBloccati.ToString() & " documenti restano in coda perché la stampante non li rilascia.")
                End If
                testo.Append(" Spegnere e riaccendere la stampante, poi riprovare.")
            End If

            errore = testo.ToString()
            If contaNegati > 0 OrElse contaBloccati > 0 Then
                ScriviLog("SvuotaCodaInterna: coda di " & nome & " non svuotata, negati=" & contaNegati.ToString() & " bloccati=" & contaBloccati.ToString())
            End If
            Return False
        Catch ex As Exception
            ScriviLog("SvuotaCodaInterna: " & ex.Message & Environment.NewLine & ex.StackTrace)
            errore = "Impossibile svuotare la coda di stampa."
            Return False
        Finally
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch exChiusura As Exception
                ScriviLog("SvuotaCodaInterna chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Private Shared Function ApriStampantePerCoda(nome As String, ByRef hPrinter As IntPtr, ByRef amministratore As Boolean, tentaAmministratore As Boolean) As Boolean
        hPrinter = IntPtr.Zero
        amministratore = False
        Try
            Dim predefiniti As New PRINTER_DEFAULTS()
            predefiniti.pDatatype = IntPtr.Zero
            predefiniti.pDevMode = IntPtr.Zero

            If tentaAmministratore Then
                predefiniti.DesiredAccess = PRINTER_ALL_ACCESS
                If OpenPrinterConAccesso(nome, hPrinter, predefiniti) AndAlso hPrinter <> IntPtr.Zero Then
                    amministratore = True
                    Return True
                End If
                hPrinter = IntPtr.Zero
            End If

            Dim ultimoCodice As Integer = 0
            For tentativo As Integer = 1 To 2
                predefiniti.DesiredAccess = PRINTER_ACCESS_USE
                If OpenPrinterConAccesso(nome, hPrinter, predefiniti) AndAlso hPrinter <> IntPtr.Zero Then
                    Return True
                End If
                ultimoCodice = CodiceErrore()
                hPrinter = IntPtr.Zero
                If tentativo = 1 Then Thread.Sleep(200)
            Next

            ScriviLog("ApriStampantePerCoda: impossibile aprire " & nome & " err=" & ultimoCodice.ToString())
            Return False
        Catch ex As Exception
            ScriviLog("ApriStampantePerCoda: " & ex.Message & Environment.NewLine & ex.StackTrace)
            hPrinter = IntPtr.Zero
            Return False
        End Try
    End Function

    Private Shared Function ElencoLavoriCoda(hPrinter As IntPtr, nome As String, ByRef riuscito As Boolean) As List(Of LavoroCoda)
        Dim risultato As New List(Of LavoroCoda)()
        riuscito = False
        Try
            If hPrinter = IntPtr.Zero Then Return risultato
            Dim dimensioneStruttura As Integer = Marshal.SizeOf(GetType(JOB_INFO_1))

            For tentativo As Integer = 1 To 5
                Dim needed As UInteger = 0
                Dim returned As UInteger = 0
                If EnumJobs(hPrinter, 0, MASSIMO_LAVORI_CODA, 1, IntPtr.Zero, 0, needed, returned) Then
                    riuscito = True
                    Return risultato
                End If
                Dim codicePrimo As Integer = CodiceErrore()
                If needed = 0 Then
                    If codicePrimo = 0 OrElse codicePrimo = ERROR_INSUFFICIENT_BUFFER Then
                        riuscito = True
                        Return risultato
                    End If
                    ScriviLog("ElencoLavoriCoda: EnumJobs fallita per " & nome & " err=" & codicePrimo.ToString())
                    Return risultato
                End If

                Dim dimensioneBuffer As UInteger = needed + CUInt(dimensioneStruttura * 4)
                Dim buffer As IntPtr = Marshal.AllocHGlobal(CInt(dimensioneBuffer))
                Try
                    If EnumJobs(hPrinter, 0, MASSIMO_LAVORI_CODA, 1, buffer, dimensioneBuffer, needed, returned) Then
                        For i As Integer = 0 To CInt(returned) - 1
                            Dim lavoro As JOB_INFO_1 = DirectCast(Marshal.PtrToStructure(IntPtr.Add(buffer, i * dimensioneStruttura), GetType(JOB_INFO_1)), JOB_INFO_1)
                            Dim voce As New LavoroCoda()
                            voce.Id = lavoro.JobId
                            voce.Utente = If(lavoro.pUserName, "")
                            risultato.Add(voce)
                        Next
                        riuscito = True
                        Return risultato
                    End If
                    Dim codiceSecondo As Integer = CodiceErrore()
                    If codiceSecondo <> ERROR_INSUFFICIENT_BUFFER Then
                        ScriviLog("ElencoLavoriCoda: EnumJobs fallita per " & nome & " err=" & codiceSecondo.ToString())
                        Return risultato
                    End If
                Finally
                    Marshal.FreeHGlobal(buffer)
                End Try
            Next

            ScriviLog("ElencoLavoriCoda: la coda di " & nome & " cambia di continuo, lettura non riuscita")
            Return risultato
        Catch ex As Exception
            ScriviLog("ElencoLavoriCoda: " & ex.Message & Environment.NewLine & ex.StackTrace)
            riuscito = False
            Return New List(Of LavoroCoda)()
        End Try
    End Function

    Private Shared Sub EliminaLavoriCoda(hPrinter As IntPtr, nome As String, lavori As List(Of UInteger), comando As Integer, negati As HashSet(Of UInteger))
        Try
            If hPrinter = IntPtr.Zero OrElse lavori Is Nothing Then Return
            For Each idLavoro As UInteger In lavori
                Try
                    If SetJob(hPrinter, idLavoro, 0, IntPtr.Zero, comando) Then
                        negati.Remove(idLavoro)
                        Continue For
                    End If
                    Dim codice As Integer = CodiceErrore()
                    If codice = ERROR_INVALID_PARAMETER Then Continue For
                    If codice = ERROR_ACCESS_DENIED Then
                        negati.Add(idLavoro)
                    End If
                    ScriviLog("EliminaLavoriCoda: SetJob fallita per il lavoro " & idLavoro.ToString() & " su " & nome & " comando=" & comando.ToString() & " err=" & codice.ToString())
                Catch exLavoro As Exception
                    ScriviLog("EliminaLavoriCoda (lavoro " & idLavoro.ToString() & "): " & exLavoro.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("EliminaLavoriCoda: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function AttendiCodaVuota(hPrinter As IntPtr, nome As String, negati As HashSet(Of UInteger), attesaMassimaMs As Integer) As List(Of LavoroCoda)
        Try
            Dim cronometro As Stopwatch = Stopwatch.StartNew()
            Dim ultimoElenco As List(Of LavoroCoda) = Nothing
            Do
                Dim letturaRiuscita As Boolean = False
                Dim elenco As List(Of LavoroCoda) = ElencoLavoriCoda(hPrinter, nome, letturaRiuscita)
                If letturaRiuscita Then
                    ultimoElenco = elenco
                    If elenco.Count = 0 Then Return elenco
                    Dim soloNegati As Boolean = True
                    For Each voce As LavoroCoda In elenco
                        If Not negati.Contains(voce.Id) Then
                            soloNegati = False
                            Exit For
                        End If
                    Next
                    If soloNegati Then Return elenco
                End If
                If cronometro.ElapsedMilliseconds >= attesaMassimaMs Then Exit Do
                Thread.Sleep(SVUOTA_CODA_INTERVALLO_MS)
            Loop
            Return ultimoElenco
        Catch ex As Exception
            ScriviLog("AttendiCodaVuota: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Private Shared Sub AggiungiStampantiEnumerate(visti As Dictionary(Of String, InfoStampante), predefinita As String, flags As UInteger, livello As UInteger, Optional nomeContenitore As String = Nothing)
        Try
            If visti Is Nothing Then Return
            Dim needed As UInteger = 0
            Dim returned As UInteger = 0
            EnumPrinters(flags, nomeContenitore, livello, IntPtr.Zero, 0, needed, returned)
            If needed = 0 Then Return

            Dim buffer As IntPtr = Marshal.AllocHGlobal(CInt(needed))
            Try
                If Not EnumPrinters(flags, nomeContenitore, livello, buffer, needed, needed, returned) Then
                    ScriviLog("AggiungiStampantiEnumerate: EnumPrinters fallita flags=" & flags.ToString() & " livello=" & livello.ToString() & " err=" & CodiceErrore().ToString())
                    Return
                End If
                If livello = 4 Then
                    Dim dimensioneStruttura As Integer = Marshal.SizeOf(GetType(PRINTER_INFO_4))
                    For i As Integer = 0 To CInt(returned) - 1
                        Dim infoNativa As PRINTER_INFO_4 = DirectCast(Marshal.PtrToStructure(IntPtr.Add(buffer, i * dimensioneStruttura), GetType(PRINTER_INFO_4)), PRINTER_INFO_4)
                        RegistraStampante(visti, infoNativa.pPrinterName, infoNativa.pServerName, infoNativa.Attributes, predefinita)
                    Next
                Else
                    Dim dimensioneStruttura As Integer = Marshal.SizeOf(GetType(PRINTER_INFO_1))
                    Dim contenitori As New List(Of String)()
                    For i As Integer = 0 To CInt(returned) - 1
                        Dim infoNativa As PRINTER_INFO_1 = DirectCast(Marshal.PtrToStructure(IntPtr.Add(buffer, i * dimensioneStruttura), GetType(PRINTER_INFO_1)), PRINTER_INFO_1)
                        Dim nome As String = NormalizzaNomeStampante(infoNativa.pName)
                        If String.IsNullOrWhiteSpace(nome) Then nome = NormalizzaNomeStampante(infoNativa.pDescription)
                        If (infoNativa.Flags And PRINTER_ENUM_CONTAINER) <> 0 Then
                            If Not String.IsNullOrWhiteSpace(nome) AndAlso Not String.Equals(nome, nomeContenitore, StringComparison.OrdinalIgnoreCase) Then
                                contenitori.Add(nome)
                            End If
                        Else
                            RegistraStampante(visti, nome, "", PRINTER_ATTRIBUTE_NETWORK, predefinita)
                        End If
                    Next
                    For Each contenitore As String In contenitori
                        AggiungiStampantiEnumerate(visti, predefinita, PRINTER_ENUM_NAME, 1, contenitore)
                    Next
                End If
            Finally
                Marshal.FreeHGlobal(buffer)
            End Try
        Catch ex As Exception
            ScriviLog("AggiungiStampantiEnumerate: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RegistraStampante(visti As Dictionary(Of String, InfoStampante), nomeGrezzo As String, serverGrezzo As String, attributi As UInteger, predefinita As String)
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeGrezzo)
            If String.IsNullOrWhiteSpace(nome) Then Return
            SyncLock _lockElenco
                If visti.ContainsKey(nome) Then
                    Dim esistente As InfoStampante = visti(nome)
                    If String.IsNullOrWhiteSpace(esistente.Server) Then esistente.Server = NormalizzaNomeStampante(serverGrezzo)
                    If (attributi And PRINTER_ATTRIBUTE_NETWORK) <> 0 Then esistente.Rete = True
                    Return
                End If
                Dim info As New InfoStampante(nome, String.Equals(nome, predefinita, StringComparison.OrdinalIgnoreCase))
                info.Server = NormalizzaNomeStampante(serverGrezzo)
                info.Rete = (attributi And PRINTER_ATTRIBUTE_NETWORK) <> 0 OrElse PercorsoReteValido(nome) OrElse Not String.IsNullOrWhiteSpace(info.Server)
                visti(nome) = info
            End SyncLock
        Catch ex As Exception
            ScriviLog("RegistraStampante: " & ex.Message)
        End Try
    End Sub

    Public Shared Function StampaTesto(testo As String, nomeStampante As String, Optional titoloDocumento As String = "WinGabriel", Optional impostazioni As WGImpostazioniStampa = Nothing) As Boolean
        Try
            Dim blocco As New WGBloccoStampa()
            blocco.Tipo = "testo"
            blocco.Testo = If(testo, "")
            Dim elenco As New List(Of WGBloccoStampa) From {blocco}
            Return StampaBlocchi(elenco, nomeStampante, titoloDocumento, impostazioni)
        Catch ex As Exception
            ScriviLog("StampaTesto: " & ex.Message)
            ImpostaErrore("La stampa non è riuscita.")
            Return False
        End Try
    End Function

    Public Shared Function StampaBlocchi(blocchi As List(Of WGBloccoStampa), nomeStampante As String, titoloDocumento As String, impostazioni As WGImpostazioniStampa, attendiCompletamento As Boolean, Optional timeoutCompletamentoSecondi As Integer = 60) As WGEsitoStampa
        Dim esito As WGEsitoStampa
        Try
            SyncLock _lockStampa
                esito = StampaBlocchiInterna(blocchi, nomeStampante, titoloDocumento, impostazioni)
            End SyncLock
        Catch ex As Exception
            ScriviLog("StampaBlocchi lock: " & ex.Message)
            esito = New WGEsitoStampa()
            ImpostaEsitoErrore(esito, "La stampa non è riuscita.")
        End Try
        Try
            If attendiCompletamento AndAlso esito IsNot Nothing AndAlso esito.Riuscita AndAlso esito.IdLavoro > 0 Then
                AttendiCompletamentoLavoro(NormalizzaNomeStampante(nomeStampante), esito.IdLavoro, timeoutCompletamentoSecondi, esito)
            End If
        Catch exAttesa As Exception
            ScriviLog("StampaBlocchi attesa completamento: " & exAttesa.Message)
        End Try
        Return esito
    End Function

    Public Shared Function StampaBlocchi(blocchi As List(Of WGBloccoStampa), nomeStampante As String, Optional titoloDocumento As String = "WinGabriel", Optional impostazioni As WGImpostazioniStampa = Nothing) As Boolean
        Try
            Dim esito As WGEsitoStampa = StampaBlocchi(blocchi, nomeStampante, titoloDocumento, impostazioni, False)
            Return esito IsNot Nothing AndAlso esito.Riuscita
        Catch ex As Exception
            ScriviLog("StampaBlocchi compat: " & ex.Message)
            ImpostaErrore("La stampa non è riuscita.")
            Return False
        End Try
    End Function

    Public Shared Function PdfiumDisponibile() As Boolean
        Try
            Dim errore As String = ""
            Return AssicuraPdfium(errore)
        Catch ex As Exception
            ScriviLog("PdfiumDisponibile: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Shared Function AssicuraPdfium(ByRef errore As String) As Boolean
        errore = ""
        Try
            SyncLock _lockPdfium
                If _pdfiumPronto Then Return True

                Dim architettura As String = ArchitetturaProcesso()
                Dim percorsoDll As String = TrovaPdfiumCompatibile(architettura)

                If String.IsNullOrEmpty(percorsoDll) Then
                    If Not String.IsNullOrEmpty(_erroreDownloadPdfium) AndAlso (DateTime.UtcNow - _istanteErroreDownloadPdfium).TotalMilliseconds < PAUSA_DOPO_ERRORE_DOWNLOAD_PDFIUM_MS Then
                        errore = _erroreDownloadPdfium
                        Return False
                    End If

                    Dim erroreDownload As String = ""
                    percorsoDll = ScaricaPdfium(architettura, erroreDownload)
                    If String.IsNullOrEmpty(percorsoDll) Then
                        _erroreDownloadPdfium = erroreDownload
                        _istanteErroreDownloadPdfium = DateTime.UtcNow
                        errore = erroreDownload
                        Return False
                    End If
                    _erroreDownloadPdfium = ""
                End If

                Dim modulo As IntPtr = LoadLibrary(percorsoDll)
                If modulo = IntPtr.Zero Then
                    Dim codice As Integer = CodiceErrore()
                    If codice = ERROR_BAD_EXE_FORMAT Then
                        errore = "Il componente " & NOME_DLL_PDFIUM & " non corrisponde all'architettura del programma (" & architettura & "). Eliminare il file " & percorsoDll & " e riprovare."
                    Else
                        errore = "Impossibile caricare il componente " & NOME_DLL_PDFIUM & ", errore " & codice.ToString() & "."
                    End If
                    ScriviLog("AssicuraPdfium: LoadLibrary fallita per " & percorsoDll & " err=" & codice.ToString())
                    Return False
                End If

                FPDF_InitLibrary()
                _pdfiumPronto = True
                Return True
            End SyncLock
        Catch ex As Exception
            ScriviLog("AssicuraPdfium: " & ex.Message & Environment.NewLine & ex.StackTrace)
            errore = "Impossibile caricare il componente " & NOME_DLL_PDFIUM & "."
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Architettura del PROCESSO in esecuzione ("x86", "x64" o "arm64"). È quella che deve avere pdfium.dll:
    ''' un'app a 32 bit su Windows a 64 bit richiede la DLL x86, anche se il sistema operativo è a 64 bit.
    ''' </summary>
    Private Shared Function ArchitetturaProcesso() As String
        Try
            If IntPtr.Size <> 8 Then Return "x86"
            Dim nativa As String = If(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"), "").Trim()
            If String.Equals(nativa, "ARM64", StringComparison.OrdinalIgnoreCase) Then Return "arm64"
            Return "x64"
        Catch ex As Exception
            ScriviLog("ArchitetturaProcesso: " & ex.Message)
            Return If(IntPtr.Size = 8, "x64", "x86")
        End Try
    End Function

    ''' <summary>Legge l'architettura dichiarata nell'header PE di una DLL ("x86", "x64", "arm64"), oppure "" se il file non è una DLL valida, o "altra" se è di un'architettura non prevista.</summary>
    Private Shared Function ArchitetturaDll(percorso As String) As String
        Try
            Using flusso As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                If flusso.Length < 128 Then Return ""
                Dim lettore As New BinaryReader(flusso)
                If lettore.ReadUInt16() <> &H5A4DUS Then Return ""
                flusso.Position = &H3C
                Dim offsetPe As Integer = lettore.ReadInt32()
                If offsetPe <= 0 OrElse offsetPe > flusso.Length - 6 Then Return ""
                flusso.Position = offsetPe
                If lettore.ReadUInt32() <> &H4550UI Then Return ""
                Dim macchina As Integer = lettore.ReadUInt16()
                Select Case macchina
                    Case MACCHINA_PE_X86
                        Return "x86"
                    Case MACCHINA_PE_X64
                        Return "x64"
                    Case MACCHINA_PE_ARM64
                        Return "arm64"
                    Case Else
                        Return "altra"
                End Select
            End Using
        Catch ex As Exception
            ScriviLog("ArchitetturaDll " & percorso & ": " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>Cartelle in cui cercare/installare pdfium.dll, in ordine di preferenza: cartella del programma, poi una cartella scrivibile dell'utente (utile se il programma è in Program Files).</summary>
    Private Shared Function CartellePdfium(architettura As String) As List(Of String)
        Dim elenco As New List(Of String)()
        Try
            Dim baseDir As String = CartellaBaseProgramma()
            If Not String.IsNullOrWhiteSpace(baseDir) Then elenco.Add(baseDir)
        Catch ex As Exception
            ScriviLog("CartellePdfium base: " & ex.Message)
        End Try
        Try
            ' Da amministratore la copia nei dati dell'utente non si usa: chiunque giri come utente potrebbe sostituirla.
            Dim datiLocali As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            If Not String.IsNullOrWhiteSpace(datiLocali) AndAlso Not WGDb.WGPercorsiApplicazione.Elevato() Then elenco.Add(Path.Combine(datiLocali, "WinGabriel", "pdfium", architettura))
        Catch ex As Exception
            ScriviLog("CartellePdfium dati locali: " & ex.Message)
        End Try
        Return elenco
    End Function

    ''' <summary>Restituisce il percorso di una pdfium.dll già presente con l'architettura giusta, oppure "" se manca o è di un'altra architettura.</summary>
    Private Shared Function TrovaPdfiumCompatibile(architettura As String) As String
        Try
            For Each cartella As String In CartellePdfium(architettura)
                Dim percorso As String = Path.Combine(cartella, NOME_DLL_PDFIUM)
                If Not File.Exists(percorso) Then Continue For
                Dim trovata As String = ArchitetturaDll(percorso)
                If String.Equals(trovata, architettura, StringComparison.OrdinalIgnoreCase) Then Return percorso
                ScriviLog("TrovaPdfiumCompatibile: " & percorso & " ha architettura '" & trovata & "' invece di '" & architettura & "'")
            Next
        Catch ex As Exception
            ScriviLog("TrovaPdfiumCompatibile: " & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>
    ''' Scarica da GitHub (bblanchon/pdfium-binaries) il pacchetto PDFium per l'architettura indicata, ne estrae bin/pdfium.dll
    ''' e la installa nella prima cartella scrivibile di CartellePdfium. Restituisce il percorso installato, oppure "" con il motivo in errore.
    ''' </summary>
    Private Shared Function ScaricaPdfium(architettura As String, ByRef errore As String) As String
        errore = ""
        Dim temporaneo As String = ""
        Try
            temporaneo = Path.Combine(WGDb.WGPercorsiApplicazione.CartellaTemporanea(), "pdfium_" & Guid.NewGuid().ToString("N") & ".dll")
            Dim url As String = URL_BASE_PDFIUM & "pdfium-win-" & architettura & ".tgz"
            ScriviLog("ScaricaPdfium: download di " & url)

            Try
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or CType(3072, SecurityProtocolType)
            Catch exTls As Exception
                ScriviLog("ScaricaPdfium TLS 1.2: " & exTls.Message)
            End Try

            Dim richiesta As HttpWebRequest = DirectCast(WebRequest.Create(url), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.UserAgent = "WinGabriel"
            richiesta.AllowAutoRedirect = True
            richiesta.Timeout = TIMEOUT_DOWNLOAD_PDFIUM_MS
            richiesta.ReadWriteTimeout = TIMEOUT_DOWNLOAD_PDFIUM_MS
            Try
                Dim proxy As IWebProxy = WebRequest.GetSystemWebProxy()
                If proxy IsNot Nothing Then
                    proxy.Credentials = CredentialCache.DefaultCredentials
                    richiesta.Proxy = proxy
                End If
            Catch exProxy As Exception
                ScriviLog("ScaricaPdfium proxy: " & exProxy.Message)
            End Try

            Dim estratta As Boolean = False
            Using risposta As WebResponse = richiesta.GetResponse()
                Using flusso As Stream = risposta.GetResponseStream()
                    ' Prima tutto il pacchetto in memoria (pochi MB), poi l'impronta, poi l'estrazione: la libreria si carica nel programma
                    ' e deve essere esattamente quella della versione fissata.
                    Using memoria As New MemoryStream()
                        flusso.CopyTo(memoria)
                        Dim attesa As String = ImprontaPdfium(architettura)
                        Dim trovata As String
                        Using sha As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
                            trovata = BitConverter.ToString(sha.ComputeHash(memoria.ToArray())).Replace("-", "").ToLowerInvariant()
                        End Using
                        If attesa = "" OrElse Not String.Equals(attesa, trovata, StringComparison.OrdinalIgnoreCase) Then
                            ScriviLog("ScaricaPdfium: impronta diversa da quella attesa per " & architettura)
                            errore = "Il componente " & NOME_DLL_PDFIUM & " scaricato non è quello previsto."
                            Return ""
                        End If
                        memoria.Position = 0
                        estratta = EstraiPdfiumDaTgz(memoria, temporaneo)
                    End Using
                End Using
            End Using

            If Not estratta Then
                errore = "Il pacchetto scaricato per il componente " & NOME_DLL_PDFIUM & " non è valido."
                Return ""
            End If

            Dim dimensione As Long = New FileInfo(temporaneo).Length
            Dim architetturaScaricata As String = ArchitetturaDll(temporaneo)
            If dimensione < DIMENSIONE_MINIMA_PDFIUM OrElse Not String.Equals(architetturaScaricata, architettura, StringComparison.OrdinalIgnoreCase) Then
                ScriviLog("ScaricaPdfium: file scaricato non valido, dimensione=" & dimensione.ToString() & " architettura='" & architetturaScaricata & "'")
                errore = "Il componente " & NOME_DLL_PDFIUM & " scaricato non è valido."
                Return ""
            End If

            Dim ultimoProblema As String = ""
            For Each cartella As String In CartellePdfium(architettura)
                Dim destinazione As String = Path.Combine(cartella, NOME_DLL_PDFIUM)
                Dim provvisorio As String = destinazione & ".new"
                Try
                    If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
                    File.Copy(temporaneo, provvisorio, True)
                    If File.Exists(destinazione) Then File.Delete(destinazione)
                    File.Move(provvisorio, destinazione)
                    ScriviLog("ScaricaPdfium: installato " & destinazione & " (" & architettura & ")")
                    Return destinazione
                Catch exCopia As Exception
                    ScriviLog("ScaricaPdfium: impossibile installare in " & cartella & ": " & exCopia.Message)
                    ultimoProblema = exCopia.Message
                    Try
                        If File.Exists(provvisorio) Then File.Delete(provvisorio)
                    Catch
                    End Try
                End Try
            Next

            errore = "Impossibile salvare il componente " & NOME_DLL_PDFIUM & " nella cartella del programma. " & ultimoProblema
            Return ""
        Catch exWeb As WebException
            ScriviLog("ScaricaPdfium rete: " & exWeb.Status.ToString() & " " & exWeb.Message)
            errore = "Impossibile scaricare il componente " & NOME_DLL_PDFIUM & " necessario per stampare i PDF. Verificare la connessione a Internet (github.com) e riprovare."
            Return ""
        Catch ex As Exception
            ScriviLog("ScaricaPdfium: " & ex.Message & Environment.NewLine & ex.StackTrace)
            errore = "Impossibile scaricare il componente " & NOME_DLL_PDFIUM & " necessario per stampare i PDF."
            Return ""
        Finally
            Try
                If Not String.IsNullOrEmpty(temporaneo) AndAlso File.Exists(temporaneo) Then File.Delete(temporaneo)
            Catch
            End Try
        End Try
    End Function

    ''' <summary>Legge al volo un archivio .tgz (gzip + tar) ed estrae il solo file bin/pdfium.dll in destinazione. Il .NET Framework non ha un lettore tar, quindi gli header a 512 byte vengono interpretati a mano.</summary>
    Private Shared Function EstraiPdfiumDaTgz(origine As Stream, destinazione As String) As Boolean
        Try
            Using gz As New GZipStream(origine, CompressionMode.Decompress)
                Dim intestazione(511) As Byte
                Dim nomeLungo As String = Nothing
                Do
                    If Not LeggiEsatti(gz, intestazione, 512) Then Return False

                    Dim blocco As Boolean = False
                    For i As Integer = 0 To 511
                        If intestazione(i) <> 0 Then
                            blocco = True
                            Exit For
                        End If
                    Next
                    If Not blocco Then Return False

                    Dim nome As String = LeggiTestoTar(intestazione, 0, 100)
                    Dim tipo As Char = ChrW(intestazione(156))
                    Dim dimensione As Long = LeggiOttaleTar(intestazione, 124, 12)
                    Dim conPadding As Long = ((dimensione + 511L) \ 512L) * 512L

                    If LeggiTestoTar(intestazione, 257, 5) = "ustar" Then
                        Dim prefisso As String = LeggiTestoTar(intestazione, 345, 155)
                        If prefisso.Length > 0 Then nome = prefisso & "/" & nome
                    End If

                    If tipo = "L"c Then
                        If dimensione <= 0 OrElse dimensione > 4096 Then Return False
                        Dim datiNome(CInt(dimensione) - 1) As Byte
                        If Not LeggiEsatti(gz, datiNome, CInt(dimensione)) Then Return False
                        Dim resto As Long = conPadding - dimensione
                        If resto > 0 AndAlso Not SaltaByte(gz, resto) Then Return False
                        Dim fine As Integer = Array.IndexOf(datiNome, CByte(0))
                        If fine < 0 Then fine = datiNome.Length
                        nomeLungo = Encoding.UTF8.GetString(datiNome, 0, fine)
                        Continue Do
                    End If

                    If nomeLungo IsNot Nothing Then
                        nome = nomeLungo
                        nomeLungo = Nothing
                    End If
                    nome = nome.Replace("\"c, "/"c)

                    Dim fileNormale As Boolean = (tipo = "0"c OrElse tipo = ChrW(0))
                    If fileNormale AndAlso nome.EndsWith("bin/pdfium.dll", StringComparison.OrdinalIgnoreCase) Then
                        If dimensione <= 0 OrElse dimensione > DIMENSIONE_MASSIMA_PDFIUM Then Return False
                        Using uscita As New FileStream(destinazione, FileMode.Create, FileAccess.Write, FileShare.None)
                            Dim buffer(65535) As Byte
                            Dim restanti As Long = dimensione
                            While restanti > 0
                                Dim letti As Integer = gz.Read(buffer, 0, CInt(Math.Min(CLng(buffer.Length), restanti)))
                                If letti <= 0 Then Return False
                                uscita.Write(buffer, 0, letti)
                                restanti -= letti
                            End While
                        End Using
                        Return True
                    End If

                    If conPadding > 0 AndAlso Not SaltaByte(gz, conPadding) Then Return False
                Loop
            End Using
        Catch ex As Exception
            ScriviLog("EstraiPdfiumDaTgz: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function LeggiEsatti(flusso As Stream, buffer As Byte(), quanti As Integer) As Boolean
        Dim letti As Integer = 0
        While letti < quanti
            Dim n As Integer = flusso.Read(buffer, letti, quanti - letti)
            If n <= 0 Then Return False
            letti += n
        End While
        Return True
    End Function

    Private Shared Function SaltaByte(flusso As Stream, quanti As Long) As Boolean
        Dim scarto(8191) As Byte
        While quanti > 0
            Dim n As Integer = flusso.Read(scarto, 0, CInt(Math.Min(CLng(scarto.Length), quanti)))
            If n <= 0 Then Return False
            quanti -= n
        End While
        Return True
    End Function

    Private Shared Function LeggiTestoTar(buffer As Byte(), inizio As Integer, lunghezza As Integer) As String
        Dim fine As Integer = inizio
        While fine < inizio + lunghezza AndAlso buffer(fine) <> 0
            fine += 1
        End While
        Return Encoding.UTF8.GetString(buffer, inizio, fine - inizio)
    End Function

    Private Shared Function LeggiOttaleTar(buffer As Byte(), inizio As Integer, lunghezza As Integer) As Long
        Dim valore As Long = 0
        Dim iniziato As Boolean = False
        For i As Integer = inizio To inizio + lunghezza - 1
            Dim b As Integer = buffer(i)
            If b >= 48 AndAlso b <= 55 Then
                valore = (valore * 8L) + CLng(b - 48)
                iniziato = True
            ElseIf iniziato Then
                Exit For
            End If
        Next
        Return valore
    End Function

    Private Shared Function ApriDocumentoPdf(percorso As String, ByRef memoria As IntPtr, ByRef errore As String) As IntPtr
        memoria = IntPtr.Zero
        errore = ""
        Try
            Dim dati As Byte()
            Using flusso As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                If flusso.Length <= 0 Then
                    errore = "Il file PDF è vuoto."
                    Return IntPtr.Zero
                End If
                If flusso.Length > Integer.MaxValue Then
                    errore = "Il file PDF è troppo grande per essere stampato."
                    Return IntPtr.Zero
                End If
                dati = New Byte(CInt(flusso.Length) - 1) {}
                Dim letti As Integer = 0
                While letti < dati.Length
                    Dim blocco As Integer = flusso.Read(dati, letti, dati.Length - letti)
                    If blocco <= 0 Then Exit While
                    letti += blocco
                End While
                If letti <> dati.Length Then
                    errore = "Impossibile leggere il file PDF per intero."
                    Return IntPtr.Zero
                End If
            End Using

            memoria = Marshal.AllocHGlobal(dati.Length)
            Marshal.Copy(dati, 0, memoria, dati.Length)

            Dim documento As IntPtr = FPDF_LoadMemDocument(memoria, dati.Length, Nothing)
            If documento = IntPtr.Zero Then
                Dim codice As Integer = CInt(FPDF_GetLastError())
                Select Case codice
                    Case FPDF_ERR_PASSWORD
                        errore = "Il PDF è protetto da password e non può essere stampato."
                    Case FPDF_ERR_FORMAT, FPDF_ERR_FILE
                        errore = "Il file non è un PDF valido oppure è danneggiato."
                    Case FPDF_ERR_SECURITY
                        errore = "Il PDF usa una protezione non supportata."
                    Case Else
                        errore = "Impossibile aprire il PDF."
                End Select
                ScriviLog("ApriDocumentoPdf: apertura fallita per " & percorso & " codice=" & codice.ToString())
                Marshal.FreeHGlobal(memoria)
                memoria = IntPtr.Zero
                Return IntPtr.Zero
            End If
            Return documento
        Catch ex As Exception
            ScriviLog("ApriDocumentoPdf: " & ex.Message & Environment.NewLine & ex.StackTrace)
            errore = "Impossibile aprire il PDF."
            Try
                If memoria <> IntPtr.Zero Then Marshal.FreeHGlobal(memoria)
            Catch exMemoria As Exception
                ScriviLog("ApriDocumentoPdf liberazione memoria: " & exMemoria.Message)
            End Try
            memoria = IntPtr.Zero
            Return IntPtr.Zero
        End Try
    End Function

    Private Shared Sub ChiudiDocumentoPdf(documento As IntPtr, memoria As IntPtr)
        Try
            If documento <> IntPtr.Zero Then FPDF_CloseDocument(documento)
        Catch ex As Exception
            ScriviLog("ChiudiDocumentoPdf documento: " & ex.Message)
        End Try
        Try
            If memoria <> IntPtr.Zero Then Marshal.FreeHGlobal(memoria)
        Catch exMemoria As Exception
            ScriviLog("ChiudiDocumentoPdf memoria: " & exMemoria.Message)
        End Try
    End Sub

    Public Shared Function ContaPaginePdf(percorsoPdf As String) As Integer
        Try
            AzzeraErrore()
            If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then
                ImpostaErrore("Il file PDF non esiste.")
                Return -1
            End If
            Dim errore As String = ""
            If Not AssicuraPdfium(errore) Then
                ImpostaErrore(errore)
                Return -1
            End If
            SyncLock _lockPdfium
                Dim memoria As IntPtr = IntPtr.Zero
                Dim documento As IntPtr = ApriDocumentoPdf(percorsoPdf, memoria, errore)
                If documento = IntPtr.Zero Then
                    ImpostaErrore(errore)
                    Return -1
                End If
                Try
                    Return FPDF_GetPageCount(documento)
                Finally
                    ChiudiDocumentoPdf(documento, memoria)
                End Try
            End SyncLock
        Catch ex As Exception
            ScriviLog("ContaPaginePdf: " & ex.Message & Environment.NewLine & ex.StackTrace)
            ImpostaErrore("Impossibile leggere il PDF.")
            Return -1
        End Try
    End Function

    Public Shared Function StampaPdf(percorsoPdf As String, nomeStampante As String, titoloDocumento As String, impostazioni As WGImpostazioniStampa, attendiCompletamento As Boolean, Optional timeoutCompletamentoSecondi As Integer = 60) As WGEsitoStampa
        Dim esito As WGEsitoStampa
        Try
            SyncLock _lockStampa
                esito = StampaPdfInterna(percorsoPdf, nomeStampante, titoloDocumento, impostazioni)
            End SyncLock
        Catch ex As Exception
            ScriviLog("StampaPdf lock: " & ex.Message & Environment.NewLine & ex.StackTrace)
            esito = New WGEsitoStampa()
            ImpostaEsitoErrore(esito, "La stampa non è riuscita.")
        End Try
        Try
            If attendiCompletamento AndAlso esito IsNot Nothing AndAlso esito.Riuscita AndAlso esito.IdLavoro > 0 Then
                AttendiCompletamentoLavoro(NormalizzaNomeStampante(nomeStampante), esito.IdLavoro, timeoutCompletamentoSecondi, esito)
            End If
        Catch exAttesa As Exception
            ScriviLog("StampaPdf attesa completamento: " & exAttesa.Message & Environment.NewLine & exAttesa.StackTrace)
        End Try
        Return esito
    End Function

    Private Shared Function StampaPdfInterna(percorsoPdf As String, nomeStampante As String, titoloDocumento As String, impostazioni As WGImpostazioniStampa) As WGEsitoStampa
        Dim esito As New WGEsitoStampa()
        Dim hdc As IntPtr = IntPtr.Zero
        Dim hPrinter As IntPtr = IntPtr.Zero
        Dim pDevMode As IntPtr = IntPtr.Zero
        Dim documento As IntPtr = IntPtr.Zero
        Dim memoria As IntPtr = IntPtr.Zero
        Dim documentoAvviato As Boolean = False
        Dim lockPdfiumPreso As Boolean = False
        Try
            AzzeraErrore()
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then Return ImpostaEsitoErrore(esito, "Nessuna stampante indicata.")
            If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then Return ImpostaEsitoErrore(esito, "Il file PDF non esiste.")

            Dim errorePdf As String = ""
            If Not AssicuraPdfium(errorePdf) Then Return ImpostaEsitoErrore(esito, errorePdf)

            Dim settaggi As WGImpostazioniStampa
            If impostazioni Is Nothing Then
                settaggi = New WGImpostazioniStampa()
            Else
                settaggi = impostazioni.CopiaSicura()
            End If
            settaggi.Normalizza()

            Dim problemaRete As String = ""
            If Not VerificaRaggiungibilita(nome, problemaRete) Then Return ImpostaEsitoErrore(esito, problemaRete)

            Monitor.Enter(_lockPdfium, lockPdfiumPreso)

            documento = ApriDocumentoPdf(percorsoPdf, memoria, errorePdf)
            If documento = IntPtr.Zero Then Return ImpostaEsitoErrore(esito, errorePdf)

            Dim numeroPagine As Integer = FPDF_GetPageCount(documento)
            If numeroPagine <= 0 Then Return ImpostaEsitoErrore(esito, "Il PDF non contiene pagine da stampare.")

            If Not ApriStampanteConRetry(nome, hPrinter) Then
                Dim problemaApertura As String = DescrizioneStatoStampante(nome)
                Return ImpostaEsitoErrore(esito, If(String.IsNullOrWhiteSpace(problemaApertura), "La stampante non è raggiungibile.", problemaApertura))
            End If
            If StampanteInErroreGrave(hPrinter, nome) Then Return EsitoDaStatoCorrente(esito)

            pDevMode = PreparaDevMode(hPrinter, nome, settaggi)
            hdc = CreaDcConRetry(nome, pDevMode)
            If hdc = IntPtr.Zero Then
                Dim problemaDc As String = DescrizioneStatoDaHandle(hPrinter)
                Return ImpostaEsitoErrore(esito, If(String.IsNullOrWhiteSpace(problemaDc), "Impossibile avviare la stampante.", problemaDc))
            End If
            If pDevMode <> IntPtr.Zero Then
                Try
                    ResetDC(hdc, pDevMode)
                Catch exReset As Exception
                    ScriviLog("StampaPdf ResetDC: " & exReset.Message)
                End Try
            End If

            Dim geometria As New GeometriaFoglio()
            geometria.DpiX = GetDeviceCaps(hdc, LOGPIXELSX)
            geometria.DpiY = GetDeviceCaps(hdc, LOGPIXELSY)
            If geometria.DpiX <= 0 Then geometria.DpiX = 300
            If geometria.DpiY <= 0 Then geometria.DpiY = 300
            geometria.AreaLarghezza = GetDeviceCaps(hdc, HORZRES)
            geometria.AreaAltezza = GetDeviceCaps(hdc, VERTRES)
            geometria.FisicaLarghezza = GetDeviceCaps(hdc, PHYSICALWIDTH)
            geometria.FisicaAltezza = GetDeviceCaps(hdc, PHYSICALHEIGHT)
            geometria.ScostamentoX = GetDeviceCaps(hdc, PHYSICALOFFSETX)
            geometria.ScostamentoY = GetDeviceCaps(hdc, PHYSICALOFFSETY)
            If geometria.AreaLarghezza <= 0 Then geometria.AreaLarghezza = CInt(geometria.DpiX * 8.0)
            If geometria.AreaAltezza <= 0 Then geometria.AreaAltezza = CInt(geometria.DpiY * 11.0)
            If geometria.FisicaLarghezza <= 0 Then geometria.FisicaLarghezza = geometria.AreaLarghezza
            If geometria.FisicaAltezza <= 0 Then geometria.FisicaAltezza = geometria.AreaAltezza
            If geometria.ScostamentoX < 0 Then geometria.ScostamentoX = 0
            If geometria.ScostamentoY < 0 Then geometria.ScostamentoY = 0

            Dim doc As New DOCINFO()
            doc.cbSize = Marshal.SizeOf(GetType(DOCINFO))
            doc.lpszDocName = If(String.IsNullOrWhiteSpace(titoloDocumento), Path.GetFileName(percorsoPdf), titoloDocumento.Trim())
            doc.lpszOutput = Nothing
            doc.lpszDatatype = Nothing
            doc.fwType = 0

            Dim idLavoro As Integer = 0
            If Not AvviaDocumentoConRetry(hdc, doc, idLavoro) Then
                Dim problemaDoc As String = DescrizioneStatoDaHandle(hPrinter)
                Return ImpostaEsitoErrore(esito, If(String.IsNullOrWhiteSpace(problemaDoc), "La stampante ha rifiutato il documento.", problemaDoc))
            End If
            documentoAvviato = True
            esito.IdLavoro = idLavoro

            Dim fronteRetro As Boolean = String.Equals(settaggi.FronteRetro, "fronte_retro", StringComparison.OrdinalIgnoreCase)
            Dim pagineInviate As Integer = 0
            For copia As Integer = 1 To settaggi.Copie
                If StampanteInErroreGrave(hPrinter, nome) Then Return EsitoDaStatoCorrente(esito)
                For indice As Integer = 0 To numeroPagine - 1
                    If Not StampaPaginaPdf(hdc, documento, indice, geometria) Then
                        Dim problemaPagina As String = DescrizioneStatoDaHandle(hPrinter)
                        Return ImpostaEsitoErrore(esito, If(String.IsNullOrWhiteSpace(problemaPagina), "La stampante non ha accettato la pagina " & (indice + 1).ToString() & ".", problemaPagina))
                    End If
                    pagineInviate += 1
                Next
                If fronteRetro AndAlso copia < settaggi.Copie AndAlso (numeroPagine Mod 2) = 1 Then
                    If StartPage(hdc) <= 0 OrElse EndPage(hdc) <= 0 Then
                        ScriviLog("StampaPdf: pagina bianca tra le copie non accettata err=" & CodiceErrore().ToString())
                    End If
                End If
            Next
            esito.PagineStampate = pagineInviate

            Dim esitoFine As Integer = EndDoc(hdc)
            documentoAvviato = False
            If esitoFine <= 0 Then
                ScriviLog("StampaPdf: EndDoc fallita err=" & CodiceErrore().ToString())
                Return ImpostaEsitoErrore(esito, "La stampante non ha accettato il documento.")
            End If
            Dim erroreDopo As String = ControllaErroriDopoStampa(hPrinter, nome, idLavoro)
            If Not String.IsNullOrWhiteSpace(erroreDopo) Then Return ImpostaEsitoErrore(esito, erroreDopo)
            Dim statoFinaleAvviso As String = DescrizioneStatoDaHandle(hPrinter)
            If Not String.IsNullOrWhiteSpace(statoFinaleAvviso) Then
                esito.Avviso = statoFinaleAvviso
                ImpostaAvviso(statoFinaleAvviso)
            End If
            esito.Riuscita = True
            esito.StatoFinale = "Stampa inviata"
            Return esito
        Catch ex As Exception
            ScriviLog("StampaPdfInterna: " & ex.Message & Environment.NewLine & ex.StackTrace)
            If String.IsNullOrWhiteSpace(esito.Errore) Then Return ImpostaEsitoErrore(esito, "La stampa del PDF non è riuscita.")
            esito.Riuscita = False
            Return esito
        Finally
            Try
                If documentoAvviato AndAlso hdc <> IntPtr.Zero Then AbortDoc(hdc)
            Catch exAnnulla As Exception
                ScriviLog("StampaPdfInterna annullamento documento: " & exAnnulla.Message)
            End Try
            Try
                If hdc <> IntPtr.Zero Then DeleteDC(hdc)
            Catch exDc As Exception
                ScriviLog("StampaPdfInterna DeleteDC: " & exDc.Message)
            End Try
            Try
                If pDevMode <> IntPtr.Zero Then Marshal.FreeHGlobal(pDevMode)
            Catch exDevMode As Exception
                ScriviLog("StampaPdfInterna DEVMODE: " & exDevMode.Message)
            End Try
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch exStampante As Exception
                ScriviLog("StampaPdfInterna ClosePrinter: " & exStampante.Message)
            End Try
            ChiudiDocumentoPdf(documento, memoria)
            Try
                If lockPdfiumPreso Then Monitor.Exit(_lockPdfium)
            Catch exLock As Exception
                ScriviLog("StampaPdfInterna rilascio lock: " & exLock.Message)
            End Try
        End Try
    End Function

    Private Class GeometriaFoglio
        Public DpiX As Integer = 300
        Public DpiY As Integer = 300
        Public AreaLarghezza As Integer = 0
        Public AreaAltezza As Integer = 0
        Public FisicaLarghezza As Integer = 0
        Public FisicaAltezza As Integer = 0
        Public ScostamentoX As Integer = 0
        Public ScostamentoY As Integer = 0
    End Class

    Private Shared Function StampaPaginaPdf(hdc As IntPtr, documento As IntPtr, indice As Integer, geometria As GeometriaFoglio) As Boolean
        Dim pagina As IntPtr = IntPtr.Zero
        Dim paginaAperta As Boolean = False
        Try
            pagina = FPDF_LoadPage(documento, indice)
            If pagina = IntPtr.Zero Then
                ScriviLog("StampaPaginaPdf: impossibile caricare la pagina " & (indice + 1).ToString())
                Return False
            End If

            Dim larghezzaPunti As Double = FPDF_GetPageWidth(pagina)
            Dim altezzaPunti As Double = FPDF_GetPageHeight(pagina)
            If larghezzaPunti <= 0 OrElse altezzaPunti <= 0 Then
                larghezzaPunti = 595.0
                altezzaPunti = 842.0
            End If

            Dim paginaOrizzontale As Boolean = larghezzaPunti > altezzaPunti
            Dim foglioOrizzontale As Boolean = geometria.FisicaLarghezza > geometria.FisicaAltezza
            Dim ruota As Boolean = paginaOrizzontale <> foglioOrizzontale AndAlso Math.Abs(larghezzaPunti - altezzaPunti) > 1.0

            Dim contenutoLarghezza As Double
            Dim contenutoAltezza As Double
            If ruota Then
                contenutoLarghezza = altezzaPunti / 72.0 * geometria.DpiX
                contenutoAltezza = larghezzaPunti / 72.0 * geometria.DpiY
            Else
                contenutoLarghezza = larghezzaPunti / 72.0 * geometria.DpiX
                contenutoAltezza = altezzaPunti / 72.0 * geometria.DpiY
            End If

            Dim x As Double
            Dim y As Double
            Dim larghezza As Double = contenutoLarghezza
            Dim altezza As Double = contenutoAltezza

            Dim stessoFormatoDelFoglio As Boolean = Math.Abs(contenutoLarghezza - geometria.FisicaLarghezza) <= geometria.FisicaLarghezza * TOLLERANZA_FORMATO_FOGLIO AndAlso Math.Abs(contenutoAltezza - geometria.FisicaAltezza) <= geometria.FisicaAltezza * TOLLERANZA_FORMATO_FOGLIO

            If stessoFormatoDelFoglio Then
                x = (geometria.FisicaLarghezza - larghezza) / 2.0 - geometria.ScostamentoX
                y = (geometria.FisicaAltezza - altezza) / 2.0 - geometria.ScostamentoY
            Else
                If larghezza > geometria.AreaLarghezza OrElse altezza > geometria.AreaAltezza Then
                    Dim scala As Double = Math.Min(geometria.AreaLarghezza / larghezza, geometria.AreaAltezza / altezza)
                    larghezza *= scala
                    altezza *= scala
                End If
                x = (geometria.AreaLarghezza - larghezza) / 2.0
                y = (geometria.AreaAltezza - altezza) / 2.0
            End If

            If StartPage(hdc) <= 0 Then
                ScriviLog("StampaPaginaPdf: StartPage fallita alla pagina " & (indice + 1).ToString() & " err=" & CodiceErrore().ToString())
                Return False
            End If
            paginaAperta = True

            FPDF_RenderPage(hdc, pagina, CInt(Math.Round(x)), CInt(Math.Round(y)), Math.Max(1, CInt(Math.Round(larghezza))), Math.Max(1, CInt(Math.Round(altezza))), If(ruota, 1, 0), FPDF_ANNOT Or FPDF_PRINTING)

            paginaAperta = False
            If EndPage(hdc) <= 0 Then
                ScriviLog("StampaPaginaPdf: EndPage fallita alla pagina " & (indice + 1).ToString() & " err=" & CodiceErrore().ToString())
                Return False
            End If
            Return True
        Catch ex As Exception
            ScriviLog("StampaPaginaPdf pagina " & (indice + 1).ToString() & ": " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        Finally
            Try
                If paginaAperta AndAlso hdc <> IntPtr.Zero Then EndPage(hdc)
            Catch exFinePagina As Exception
                ScriviLog("StampaPaginaPdf chiusura pagina: " & exFinePagina.Message)
            End Try
            Try
                If pagina <> IntPtr.Zero Then FPDF_ClosePage(pagina)
            Catch exPagina As Exception
                ScriviLog("StampaPaginaPdf ClosePage: " & exPagina.Message)
            End Try
        End Try
    End Function

    Public Shared Function ContaPagine(blocchi As List(Of WGBloccoStampa), nomeStampante As String, Optional impostazioni As WGImpostazioniStampa = Nothing) As Integer
        Dim hic As IntPtr = IntPtr.Zero
        Dim hPrinter As IntPtr = IntPtr.Zero
        Dim pDevMode As IntPtr = IntPtr.Zero
        Dim hFont As IntPtr = IntPtr.Zero
        Dim hFontIntestazione As IntPtr = IntPtr.Zero
        Dim hFontVecchio As IntPtr = IntPtr.Zero
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then
                ScriviLog("ContaPagine: nessuna stampante indicata")
                Return -1
            End If
            If blocchi Is Nothing OrElse blocchi.Count = 0 Then Return 0

            Dim settaggi As WGImpostazioniStampa
            If impostazioni Is Nothing Then
                settaggi = New WGImpostazioniStampa()
            Else
                settaggi = impostazioni.CopiaSicura()
            End If
            settaggi.Normalizza()

            Dim problemaRete As String = ""
            If Not VerificaRaggiungibilita(nome, problemaRete) Then
                ImpostaErrore(problemaRete)
                Return -1
            End If

            If Not OpenPrinter(nome, hPrinter, IntPtr.Zero) Then
                ScriviLog("ContaPagine: OpenPrinter fallita per " & nome & " err=" & CodiceErrore().ToString())
                Return -1
            End If

            pDevMode = PreparaDevMode(hPrinter, nome, settaggi)
            hic = CreateIC("WINSPOOL", nome, Nothing, pDevMode)
            If hic = IntPtr.Zero Then hic = CreateIC(Nothing, nome, Nothing, pDevMode)
            If hic = IntPtr.Zero Then
                ScriviLog("ContaPagine: CreateIC fallita err=" & CodiceErrore().ToString())
                Return -1
            End If

            Dim dpiY As Integer = GetDeviceCaps(hic, LOGPIXELSY)
            If dpiY <= 0 Then dpiY = 96
            Dim dpiX As Integer = GetDeviceCaps(hic, LOGPIXELSX)
            If dpiX <= 0 Then dpiX = 96
            Dim larghezza As Integer = GetDeviceCaps(hic, HORZRES)
            Dim altezza As Integer = GetDeviceCaps(hic, VERTRES)
            If larghezza <= 0 Then larghezza = GetDeviceCaps(hic, PHYSICALWIDTH)
            If altezza <= 0 Then altezza = GetDeviceCaps(hic, PHYSICALHEIGHT)
            If larghezza <= 0 Then larghezza = CInt(dpiX * 8.0)
            If altezza <= 0 Then altezza = CInt(dpiY * 11.0)

            Dim margine As Integer = CInt(Math.Round(settaggi.MargineMillimetri * dpiY / 25.4))
            If margine < 0 Then margine = 0
            Dim larghezzaUtile As Integer = Math.Max(80, larghezza - (margine * 2))

            hFont = CreaFontStampa(dpiY, settaggi)
            hFontIntestazione = CreaFontIntestazione(dpiY, settaggi)

            Dim altezzaIntestazione As Integer = AltezzaZonaIntestazionePiePagina(dpiY, settaggi, settaggi.MostraIntestazione)
            Dim altezzaPiePagina As Integer = AltezzaZonaIntestazionePiePagina(dpiY, settaggi, settaggi.MostraNumeroPagina)
            Dim yContenutoInizio As Integer = margine + altezzaIntestazione
            Dim limiteBasso As Integer = Math.Max(yContenutoInizio + 40, altezza - margine - altezzaPiePagina)

            Dim ctx As New WGContestoPagina()
            ctx.Hdc = hic
            ctx.X = margine
            ctx.Margine = margine
            ctx.YContenutoInizio = yContenutoInizio
            ctx.LimiteBasso = limiteBasso
            ctx.LarghezzaUtile = larghezzaUtile
            ctx.LarghezzaPagina = larghezza
            ctx.AltezzaPagina = altezza
            ctx.DpiX = dpiX
            ctx.DpiY = dpiY
            ctx.HFont = hFont
            ctx.HFontIntestazione = hFontIntestazione
            ctx.HPen = IntPtr.Zero
            ctx.TitoloDocumento = "WinGabriel"
            ctx.Settaggi = settaggi
            ctx.SoloMisura = True

            Try
                hFontVecchio = SelectObject(hic, hFont)
            Catch exSel As Exception
                ScriviLog("ContaPagine selezione font: " & exSel.Message)
            End Try

            For copia As Integer = 1 To settaggi.Copie
                If Not ImpaginaBlocchi(ctx, blocchi) Then
                    ScriviLog("ContaPagine: impaginazione fallita per " & nome)
                    Return -1
                End If
            Next

            Return ctx.NumeroPagina
        Catch ex As Exception
            ScriviLog("ContaPagine: " & ex.Message)
            Return -1
        Finally
            Try
                RipristinaOggetto(hic, hFontVecchio)
            Catch
            End Try
            EliminaOggetto(hFont)
            EliminaOggetto(hFontIntestazione)
            Try
                If hic <> IntPtr.Zero Then DeleteDC(hic)
            Catch
            End Try
            Try
                If pDevMode <> IntPtr.Zero Then Marshal.FreeHGlobal(pDevMode)
            Catch
            End Try
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch
            End Try
        End Try
    End Function

    Private Shared Sub AttendiCompletamentoLavoro(nome As String, idLavoro As Integer, timeoutSecondi As Integer, esito As WGEsitoStampa)
        Dim hPrinter As IntPtr = IntPtr.Zero
        Try
            If esito Is Nothing OrElse String.IsNullOrWhiteSpace(nome) OrElse idLavoro <= 0 Then Return
            Dim secondiEffettivi As Integer = timeoutSecondi
            If secondiEffettivi <= 0 Then secondiEffettivi = 60
            Dim scadenza As DateTime = DateTime.Now.AddSeconds(secondiEffettivi)
            If Not OpenPrinter(nome, hPrinter, IntPtr.Zero) Then
                ScriviLog("AttendiCompletamentoLavoro: OpenPrinter fallita per " & nome & " err=" & CodiceErrore().ToString())
                esito.StatoFinale = "Il lavoro è ancora in coda"
                Return
            End If
            Do
                Dim trovato As Boolean = False
                Dim statoLavoro As UInteger = 0
                Dim descrizioneStatoLavoro As String = ""
                Dim needed As UInteger = 0
                Dim returned As UInteger = 0
                EnumJobs(hPrinter, 0, 256, 1, IntPtr.Zero, 0, needed, returned)
                If needed > 0 Then
                    Dim buffer As IntPtr = Marshal.AllocHGlobal(CInt(needed))
                    Try
                        If EnumJobs(hPrinter, 0, 256, 1, buffer, needed, needed, returned) Then
                            Dim dimensioneStruttura As Integer = Marshal.SizeOf(GetType(JOB_INFO_1))
                            For i As Integer = 0 To CInt(returned) - 1
                                Dim lavoro As JOB_INFO_1 = DirectCast(Marshal.PtrToStructure(IntPtr.Add(buffer, i * dimensioneStruttura), GetType(JOB_INFO_1)), JOB_INFO_1)
                                If lavoro.JobId = CUInt(idLavoro) Then
                                    trovato = True
                                    statoLavoro = lavoro.Status
                                    descrizioneStatoLavoro = If(lavoro.pStatus, "")
                                    Exit For
                                End If
                            Next
                        End If
                    Finally
                        Marshal.FreeHGlobal(buffer)
                    End Try
                End If

                If Not trovato Then
                    esito.StatoFinale = "Stampa completata"
                    Return
                End If
                If (statoLavoro And JOB_STATUS_PRINTED) <> 0 OrElse (statoLavoro And JOB_STATUS_COMPLETE) <> 0 Then
                    esito.StatoFinale = "Stampa completata"
                    Return
                End If
                If (statoLavoro And JOB_STATUS_PAPEROUT) <> 0 Then
                    esito.Riuscita = False
                    esito.Errore = "Carta esaurita."
                    esito.StatoFinale = "Carta esaurita"
                    ImpostaErrore(esito.Errore)
                    Return
                End If
                If (statoLavoro And JOB_STATUS_DELETED) <> 0 OrElse (statoLavoro And JOB_STATUS_DELETING) <> 0 Then
                    esito.Riuscita = False
                    esito.Errore = "Il lavoro di stampa è stato eliminato."
                    esito.StatoFinale = "Lavoro eliminato"
                    ImpostaErrore(esito.Errore)
                    Return
                End If
                If (statoLavoro And JOB_STATUS_OFFLINE) <> 0 Then
                    esito.Riuscita = False
                    esito.Errore = "La stampante non è in linea."
                    esito.StatoFinale = "Stampante non in linea"
                    ImpostaErrore(esito.Errore)
                    Return
                End If
                If (statoLavoro And JOB_STATUS_ERROR) <> 0 OrElse (statoLavoro And JOB_STATUS_USER_INTERVENTION) <> 0 Then
                    Dim messaggioErrore As String = "Errore della stampante."
                    If Not String.IsNullOrWhiteSpace(descrizioneStatoLavoro) Then messaggioErrore = descrizioneStatoLavoro.Trim()
                    esito.Riuscita = False
                    esito.Errore = messaggioErrore
                    esito.StatoFinale = "Errore della stampante"
                    ImpostaErrore(esito.Errore)
                    Return
                End If

                Dim descrizioneStampante As String = DescrizioneStatoDaHandle(hPrinter)
                If Not String.IsNullOrWhiteSpace(descrizioneStampante) Then
                    If descrizioneStampante.IndexOf("carta è inceppata", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        esito.Riuscita = False
                        esito.Errore = descrizioneStampante
                        esito.StatoFinale = "Carta inceppata"
                        ImpostaErrore(esito.Errore)
                        Return
                    End If
                    If descrizioneStampante.IndexOf("manca la carta", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        esito.Riuscita = False
                        esito.Errore = descrizioneStampante
                        esito.StatoFinale = "Carta esaurita"
                        ImpostaErrore(esito.Errore)
                        Return
                    End If
                End If

                If DateTime.Now >= scadenza Then
                    esito.StatoFinale = "Il lavoro è ancora in coda"
                    Return
                End If
                Thread.Sleep(500)
            Loop
        Catch ex As Exception
            ScriviLog("AttendiCompletamentoLavoro: " & ex.Message)
        Finally
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch
            End Try
        End Try
    End Sub

    Private Shared Function StampaBlocchiInterna(blocchi As List(Of WGBloccoStampa), nomeStampante As String, titoloDocumento As String, impostazioni As WGImpostazioniStampa) As WGEsitoStampa
        Dim esito As New WGEsitoStampa()
        Dim hdc As IntPtr = IntPtr.Zero
        Dim hPrinter As IntPtr = IntPtr.Zero
        Dim pDevMode As IntPtr = IntPtr.Zero
        Dim hFont As IntPtr = IntPtr.Zero
        Dim hFontIntestazione As IntPtr = IntPtr.Zero
        Dim hPen As IntPtr = IntPtr.Zero
        Dim documentoAvviato As Boolean = False
        Dim ctx As WGContestoPagina = Nothing
        Try
            AzzeraErrore()
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then
                Return ImpostaEsitoErrore(esito, "Nessuna stampante indicata.")
            End If
            If blocchi Is Nothing OrElse blocchi.Count = 0 Then
                Return ImpostaEsitoErrore(esito, "Non c'è nulla da stampare.")
            End If
            Dim haContenuto As Boolean = False
            For Each bloccoControllo As WGBloccoStampa In blocchi
                If bloccoControllo Is Nothing Then Continue For
                If String.Equals(bloccoControllo.Tipo, "tabella", StringComparison.OrdinalIgnoreCase) Then
                    If bloccoControllo.RigheTabella IsNot Nothing AndAlso bloccoControllo.RigheTabella.Count > 0 Then haContenuto = True
                ElseIf String.Equals(bloccoControllo.Tipo, "immagine", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(bloccoControllo.Tipo, "immagine_pagina", StringComparison.OrdinalIgnoreCase) Then
                    If (bloccoControllo.ImmagineBytes IsNot Nothing AndAlso bloccoControllo.ImmagineBytes.Length > 0) OrElse Not String.IsNullOrWhiteSpace(bloccoControllo.PercorsoImmagine) Then haContenuto = True
                ElseIf Not String.IsNullOrEmpty(bloccoControllo.Testo) Then
                    haContenuto = True
                End If
            Next
            If Not haContenuto Then
                Return ImpostaEsitoErrore(esito, "Non c'è nulla da stampare.")
            End If

            Dim settaggi As WGImpostazioniStampa
            If impostazioni Is Nothing Then
                settaggi = New WGImpostazioniStampa()
            Else
                settaggi = impostazioni.CopiaSicura()
            End If
            settaggi.Normalizza()

            Dim problemaRete As String = ""
            If Not VerificaRaggiungibilita(nome, problemaRete) Then
                Return ImpostaEsitoErrore(esito, problemaRete)
            End If

            If Not ApriStampanteConRetry(nome, hPrinter) Then
                Dim problemaApertura As String = DescrizioneStatoStampante(nome)
                If String.IsNullOrWhiteSpace(problemaApertura) Then
                    Return ImpostaEsitoErrore(esito, "La stampante non è raggiungibile.")
                Else
                    Return ImpostaEsitoErrore(esito, problemaApertura)
                End If
            End If
            If StampanteInErroreGrave(hPrinter, nome) Then
                Return EsitoDaStatoCorrente(esito)
            End If

            pDevMode = PreparaDevMode(hPrinter, nome, settaggi)
            hdc = CreaDcConRetry(nome, pDevMode)
            If hdc = IntPtr.Zero Then
                Dim problemaDc As String = DescrizioneStatoDaHandle(hPrinter)
                If String.IsNullOrWhiteSpace(problemaDc) Then
                    Return ImpostaEsitoErrore(esito, "Impossibile avviare la stampante.")
                Else
                    Return ImpostaEsitoErrore(esito, problemaDc)
                End If
            End If
            If pDevMode <> IntPtr.Zero Then
                Try
                    ResetDC(hdc, pDevMode)
                Catch exReset As Exception
                    ScriviLog("StampaBlocchi ResetDC: " & exReset.Message)
                End Try
            End If

            Dim dpiY As Integer = GetDeviceCaps(hdc, LOGPIXELSY)
            If dpiY <= 0 Then dpiY = 96
            Dim dpiX As Integer = GetDeviceCaps(hdc, LOGPIXELSX)
            If dpiX <= 0 Then dpiX = 96
            Dim larghezza As Integer = GetDeviceCaps(hdc, HORZRES)
            Dim altezza As Integer = GetDeviceCaps(hdc, VERTRES)
            If larghezza <= 0 Then larghezza = GetDeviceCaps(hdc, PHYSICALWIDTH)
            If altezza <= 0 Then altezza = GetDeviceCaps(hdc, PHYSICALHEIGHT)
            If larghezza <= 0 Then larghezza = CInt(dpiX * 8.0)
            If altezza <= 0 Then altezza = CInt(dpiY * 11.0)

            Dim margine As Integer = CInt(Math.Round(settaggi.MargineMillimetri * dpiY / 25.4))
            If margine < 0 Then margine = 0
            Dim larghezzaUtile As Integer = Math.Max(80, larghezza - (margine * 2))

            hFont = CreaFontStampa(dpiY, settaggi)
            hFontIntestazione = CreaFontIntestazione(dpiY, settaggi)
            hPen = CreatePen(PS_SOLID, 1, 0)

            Dim altezzaIntestazione As Integer = AltezzaZonaIntestazionePiePagina(dpiY, settaggi, settaggi.MostraIntestazione)
            Dim altezzaPiePagina As Integer = AltezzaZonaIntestazionePiePagina(dpiY, settaggi, settaggi.MostraNumeroPagina)
            Dim yContenutoInizio As Integer = margine + altezzaIntestazione
            Dim limiteBasso As Integer = Math.Max(yContenutoInizio + 40, altezza - margine - altezzaPiePagina)

            ctx = New WGContestoPagina()
            ctx.Hdc = hdc
            ctx.X = margine
            ctx.Margine = margine
            ctx.YContenutoInizio = yContenutoInizio
            ctx.LimiteBasso = limiteBasso
            ctx.LarghezzaUtile = larghezzaUtile
            ctx.LarghezzaPagina = larghezza
            ctx.AltezzaPagina = altezza
            ctx.DpiX = dpiX
            ctx.DpiY = dpiY
            ctx.HFont = hFont
            ctx.HFontIntestazione = hFontIntestazione
            ctx.HPen = hPen
            ctx.TitoloDocumento = If(String.IsNullOrWhiteSpace(titoloDocumento), "WinGabriel", titoloDocumento.Trim())
            ctx.Settaggi = settaggi

            Try
                ctx.HFontVecchio = SelectObject(hdc, hFont)
                ctx.HPenVecchio = SelectObject(hdc, hPen)
                SetBkMode(hdc, TRANSPARENT)
                SetTextColor(hdc, 0)
            Catch exSelezione As Exception
                ScriviLog("StampaBlocchi selezione iniziale: " & exSelezione.Message)
            End Try

            If settaggi.MostraNumeroPagina Then
                ctx.SoloMisura = True
                For copiaMisura As Integer = 1 To settaggi.Copie
                    If Not ImpaginaBlocchi(ctx, blocchi) Then Exit For
                Next
                ctx.TotalePagineConosciute = ctx.NumeroPagina
                ctx.SoloMisura = False
                ctx.NumeroPagina = 0
                ctx.PaginaAperta = False
            End If

            Dim doc As New DOCINFO()
            doc.cbSize = Marshal.SizeOf(GetType(DOCINFO))
            doc.lpszDocName = ctx.TitoloDocumento
            doc.lpszOutput = Nothing
            doc.lpszDatatype = Nothing
            doc.fwType = 0

            Dim idLavoro As Integer = 0
            If Not AvviaDocumentoConRetry(hdc, doc, idLavoro) Then
                Dim problemaDoc As String = DescrizioneStatoDaHandle(hPrinter)
                If String.IsNullOrWhiteSpace(problemaDoc) Then
                    Return ImpostaEsitoErrore(esito, "La stampante ha rifiutato il documento.")
                Else
                    Return ImpostaEsitoErrore(esito, problemaDoc)
                End If
            End If
            documentoAvviato = True
            esito.IdLavoro = idLavoro

            For copia As Integer = 1 To settaggi.Copie
                If StampanteInErroreGrave(hPrinter, nome) Then
                    Return EsitoDaStatoCorrente(esito)
                End If
                If Not ImpaginaBlocchi(ctx, blocchi) Then
                    Dim problemaPagina As String = DescrizioneStatoDaHandle(hPrinter)
                    If String.IsNullOrWhiteSpace(problemaPagina) Then
                        Return ImpostaEsitoErrore(esito, "La stampante non ha accettato la pagina.")
                    Else
                        Return ImpostaEsitoErrore(esito, problemaPagina)
                    End If
                End If
            Next
            esito.PagineStampate = ctx.NumeroPagina

            Dim esitoFine As Integer = EndDoc(hdc)
            documentoAvviato = False
            If esitoFine <= 0 Then
                ScriviLog("StampaBlocchi: EndDoc fallita err=" & CodiceErrore().ToString())
                Return ImpostaEsitoErrore(esito, "La stampante non ha accettato il documento.")
            End If
            Dim erroreDopo As String = ControllaErroriDopoStampa(hPrinter, nome, idLavoro)
            If Not String.IsNullOrWhiteSpace(erroreDopo) Then
                Return ImpostaEsitoErrore(esito, erroreDopo)
            End If
            Dim statoFinaleAvviso As String = DescrizioneStatoDaHandle(hPrinter)
            If Not String.IsNullOrWhiteSpace(statoFinaleAvviso) Then
                esito.Avviso = statoFinaleAvviso
                ImpostaAvviso(statoFinaleAvviso)
            End If
            esito.Riuscita = True
            esito.StatoFinale = "Stampa inviata"
            Return esito
        Catch ex As Exception
            ScriviLog("StampaBlocchi: " & ex.Message)
            If String.IsNullOrWhiteSpace(esito.Errore) Then Return ImpostaEsitoErrore(esito, "La stampa non è riuscita.")
            esito.Riuscita = False
            Return esito
        Finally
            Try
                If ctx IsNot Nothing AndAlso ctx.PaginaAperta AndAlso hdc <> IntPtr.Zero Then EndPage(hdc)
            Catch exPagina As Exception
                ScriviLog("StampaBlocchi chiusura pagina: " & exPagina.Message)
            End Try
            Try
                If documentoAvviato AndAlso hdc <> IntPtr.Zero Then AbortDoc(hdc)
            Catch exAnnulla As Exception
                ScriviLog("StampaBlocchi annullamento documento: " & exAnnulla.Message)
            End Try
            If ctx IsNot Nothing Then
                RipristinaOggetto(hdc, ctx.HPenVecchio)
                RipristinaOggetto(hdc, ctx.HFontVecchio)
            End If
            EliminaOggetto(hPen)
            EliminaOggetto(hFont)
            EliminaOggetto(hFontIntestazione)
            Try
                If hdc <> IntPtr.Zero Then DeleteDC(hdc)
            Catch
            End Try
            Try
                If pDevMode <> IntPtr.Zero Then Marshal.FreeHGlobal(pDevMode)
            Catch
            End Try
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch
            End Try
        End Try
    End Function

    Private Shared Function ApriStampanteConRetry(nome As String, ByRef hPrinter As IntPtr) As Boolean
        Try
            hPrinter = IntPtr.Zero
            Dim ultimoErroreTentativo As Integer = 0
            For tentativo As Integer = 1 To TENTATIVI_MAX
                If OpenPrinter(nome, hPrinter, IntPtr.Zero) AndAlso hPrinter <> IntPtr.Zero Then
                    Return True
                End If
                ultimoErroreTentativo = CodiceErrore()
                hPrinter = IntPtr.Zero
                Thread.Sleep(250 * tentativo)
            Next
            ScriviLog("ApriStampanteConRetry: tutti i tentativi falliti per " & nome & " err=" & ultimoErroreTentativo.ToString())
            Return False
        Catch ex As Exception
            ScriviLog("ApriStampanteConRetry: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function DescrizioneStatoStampante(nomeStampante As String) As String
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Return DescrizioneStatoDaInfo(InformazioniStampante(nome))
        Catch ex As Exception
            ScriviLog("DescrizioneStatoStampante: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Private Shared Function DescrizioneStatoDaInfo(info As InfoStampante) As String
        Dim hPrinter As IntPtr = IntPtr.Zero
        Try
            If info Is Nothing OrElse String.IsNullOrWhiteSpace(info.Nome) Then Return ""
            Dim problemaRete As String = ""
            If Not VerificaRaggiungibilitaInfo(info, problemaRete) Then Return problemaRete
            If Not OpenPrinter(info.Nome, hPrinter, IntPtr.Zero) Then
                hPrinter = IntPtr.Zero
                Return "La stampante non è raggiungibile."
            End If
            Return DescrizioneStatoDaHandle(hPrinter)
        Catch ex As Exception
            ScriviLog("DescrizioneStatoDaInfo: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        Finally
            Try
                If hPrinter <> IntPtr.Zero Then ClosePrinter(hPrinter)
            Catch exChiusura As Exception
                ScriviLog("DescrizioneStatoDaInfo chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Private Shared Function DescrizioneStatoDaHandle(hPrinter As IntPtr) As String
        Try
            If hPrinter = IntPtr.Zero Then Return ""
            Dim needed As Integer = 0
            GetPrinter(hPrinter, 6, IntPtr.Zero, 0, needed)
            If needed <= 0 Then Return ""
            Dim buffer As IntPtr = Marshal.AllocHGlobal(needed)
            Try
                If Not GetPrinter(hPrinter, 6, buffer, needed, needed) Then Return ""
                Dim info As PRINTER_INFO_6 = DirectCast(Marshal.PtrToStructure(buffer, GetType(PRINTER_INFO_6)), PRINTER_INFO_6)
                Return DescrizioneStatoDaFlag(info.dwStatus)
            Finally
                Marshal.FreeHGlobal(buffer)
            End Try
        Catch ex As Exception
            ScriviLog("DescrizioneStatoDaHandle: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function DescrizioneStatoDaFlag(stato As UInteger) As String
        Try
            If stato = 0 Then Return ""
            If (stato And PRINTER_STATUS_PAPER_OUT) <> 0 Then Return "Manca la carta."
            If (stato And PRINTER_STATUS_PAPER_JAM) <> 0 Then Return "La carta è inceppata."
            If (stato And PRINTER_STATUS_PAPER_PROBLEM) <> 0 Then Return "C'è un problema con la carta."
            If (stato And PRINTER_STATUS_NO_TONER) <> 0 Then Return "Manca il toner."
            If (stato And PRINTER_STATUS_DOOR_OPEN) <> 0 Then Return "Lo sportello della stampante è aperto."
            If (stato And PRINTER_STATUS_NOT_AVAILABLE) <> 0 Then Return "La stampante non è disponibile."
            If (stato And PRINTER_STATUS_PENDING_DELETION) <> 0 Then Return "La stampante è in eliminazione."
            If (stato And PRINTER_STATUS_OUTPUT_BIN_FULL) <> 0 Then Return "Il vassoio di uscita è pieno."
            If (stato And PRINTER_STATUS_OUT_OF_MEMORY) <> 0 Then Return "La memoria della stampante è esaurita."
            If (stato And PRINTER_STATUS_USER_INTERVENTION) <> 0 Then Return "La stampante richiede un intervento."
            If (stato And PRINTER_STATUS_OFFLINE) <> 0 Then Return "La stampante non è in linea."
            If (stato And PRINTER_STATUS_TONER_LOW) <> 0 Then Return "Il toner è in esaurimento."
            If (stato And PRINTER_STATUS_ERROR) <> 0 Then Return "La stampante segnala un errore."
            Return ""
        Catch ex As Exception
            ScriviLog("DescrizioneStatoDaFlag: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function ErroreGraveDaDescrizione(descrizione As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(descrizione) Then Return False
            If descrizione.IndexOf("esaurimento", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
            If descrizione.IndexOf("non è in linea", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
            If descrizione.IndexOf("non è disponibile", StringComparison.OrdinalIgnoreCase) >= 0 Then Return False
            Return True
        Catch
            Return True
        End Try
    End Function

    Private Shared Function StampanteInErroreGrave(hPrinter As IntPtr, nome As String) As Boolean
        Try
            If hPrinter = IntPtr.Zero Then Return False
            Dim descrizione As String = DescrizioneStatoDaHandle(hPrinter)
            If String.IsNullOrWhiteSpace(descrizione) Then Return False
            If Not ErroreGraveDaDescrizione(descrizione) Then
                ImpostaAvviso(descrizione)
                Return False
            End If
            ScriviLog("StampanteInErroreGrave: " & nome & " " & descrizione)
            ImpostaErrore(descrizione)
            Return True
        Catch ex As Exception
            ScriviLog("StampanteInErroreGrave: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ControllaErroriDopoStampa(hPrinter As IntPtr, nome As String, idLavoro As Integer) As String
        Try
            Dim descrizione As String = DescrizioneStatoDaHandle(hPrinter)
            If ErroreGraveDaDescrizione(descrizione) Then Return descrizione
            Dim needed As UInteger = 0
            Dim returned As UInteger = 0
            EnumJobs(hPrinter, 0, 32, 1, IntPtr.Zero, 0, needed, returned)
            If needed = 0 Then Return ""
            Dim buffer As IntPtr = Marshal.AllocHGlobal(CInt(needed))
            Try
                If Not EnumJobs(hPrinter, 0, 32, 1, buffer, needed, needed, returned) Then Return If(descrizione, "")
                Dim dimensioneStruttura As Integer = Marshal.SizeOf(GetType(JOB_INFO_1))
                For i As Integer = 0 To CInt(returned) - 1
                    Dim lavoro As JOB_INFO_1 = DirectCast(Marshal.PtrToStructure(IntPtr.Add(buffer, i * dimensioneStruttura), GetType(JOB_INFO_1)), JOB_INFO_1)
                    If idLavoro > 0 AndAlso lavoro.JobId <> CUInt(idLavoro) Then Continue For
                    If (lavoro.Status And JOB_STATUS_PAPEROUT) <> 0 Then Return "Manca la carta."
                    If (lavoro.Status And JOB_STATUS_USER_INTERVENTION) <> 0 Then Return "La stampante richiede un intervento."
                    If (lavoro.Status And JOB_STATUS_ERROR) <> 0 Then
                        If Not String.IsNullOrWhiteSpace(lavoro.pStatus) Then Return lavoro.pStatus.Trim()
                        Return "La stampante segnala un errore."
                    End If
                Next
            Finally
                Marshal.FreeHGlobal(buffer)
            End Try
            Return ""
        Catch ex As Exception
            ScriviLog("ControllaErroriDopoStampa: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PreparaDevMode(hPrinter As IntPtr, nome As String, settaggi As WGImpostazioniStampa) As IntPtr
        Try
            If hPrinter = IntPtr.Zero OrElse String.IsNullOrWhiteSpace(nome) Then Return IntPtr.Zero
            Dim needed As Integer = DocumentProperties(IntPtr.Zero, hPrinter, nome, IntPtr.Zero, IntPtr.Zero, 0)
            If needed <= 0 Then
                ScriviLog("PreparaDevMode: dimensione non disponibile err=" & CodiceErrore().ToString())
                Return IntPtr.Zero
            End If
            Dim pDevMode As IntPtr = Marshal.AllocHGlobal(needed)
            Try
                If DocumentProperties(IntPtr.Zero, hPrinter, nome, pDevMode, IntPtr.Zero, DM_OUT_BUFFER) < 0 Then
                    ScriviLog("PreparaDevMode: lettura fallita err=" & CodiceErrore().ToString())
                    Marshal.FreeHGlobal(pDevMode)
                    Return IntPtr.Zero
                End If
                Dim modo As DEVMODE = DirectCast(Marshal.PtrToStructure(pDevMode, GetType(DEVMODE)), DEVMODE)
                If modo.dmSize <= 0 Then modo.dmSize = CShort(Marshal.SizeOf(GetType(DEVMODE)))
                Dim capacita As CapacitaStampanteInfo = CapacitaStampante(nome)
                modo.dmFields = modo.dmFields Or DM_ORIENTATION Or DM_COPIES Or DM_PRINTQUALITY
                If String.Equals(settaggi.Orientamento, "orizzontale", StringComparison.OrdinalIgnoreCase) Then
                    modo.dmOrientation = DMORIENT_LANDSCAPE
                Else
                    modo.dmOrientation = DMORIENT_PORTRAIT
                End If
                modo.dmCopies = 1
                If capacita.SupportaColore Then
                    modo.dmFields = modo.dmFields Or DM_COLOR
                    If String.Equals(settaggi.Colore, "bianco_nero", StringComparison.OrdinalIgnoreCase) Then
                        modo.dmColor = DMCOLOR_MONOCHROME
                    Else
                        modo.dmColor = DMCOLOR_COLOR
                    End If
                End If
                If capacita.SupportaFronteRetro Then
                    modo.dmFields = modo.dmFields Or DM_DUPLEX
                    If String.Equals(settaggi.FronteRetro, "fronte_retro", StringComparison.OrdinalIgnoreCase) Then
                        modo.dmDuplex = DMDUP_VERTICAL
                    Else
                        modo.dmDuplex = DMDUP_SIMPLEX
                    End If
                End If
                If String.Equals(settaggi.Qualita, "bozza", StringComparison.OrdinalIgnoreCase) Then
                    modo.dmPrintQuality = DMRES_DRAFT
                ElseIf String.Equals(settaggi.Qualita, "alta", StringComparison.OrdinalIgnoreCase) Then
                    modo.dmPrintQuality = DMRES_HIGH
                Else
                    modo.dmPrintQuality = DMRES_MEDIUM
                End If
                Marshal.StructureToPtr(modo, pDevMode, False)
                Dim esito As Integer = DocumentProperties(IntPtr.Zero, hPrinter, nome, pDevMode, pDevMode, DM_IN_BUFFER Or DM_OUT_BUFFER)
                If esito < 0 Then
                    ScriviLog("PreparaDevMode: applicazione parziale err=" & CodiceErrore().ToString())
                End If
                Return pDevMode
            Catch exInterno As Exception
                ScriviLog("PreparaDevMode interno: " & exInterno.Message)
                Try
                    Marshal.FreeHGlobal(pDevMode)
                Catch
                End Try
                Return IntPtr.Zero
            End Try
        Catch ex As Exception
            ScriviLog("PreparaDevMode: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Public Shared Function CapacitaStampante(nomeStampante As String) As CapacitaStampanteInfo
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then Return New CapacitaStampanteInfo()

            Dim memorizzata As CapacitaStampanteInfo = Nothing
            If CapacitaStampanteInCache(nome, memorizzata) Then Return memorizzata

            Dim lavoro As New WGLavoroCapacita()
            lavoro.Nome = nome
            lavoro.AggiornaCache = True

            Dim esecutore As New Thread(AddressOf lavoro.Esegui)
            esecutore.IsBackground = True
            esecutore.Name = "WGStampante.Capacita"
            esecutore.Start()

            If Not esecutore.Join(CAPACITA_TIMEOUT_MS) Then
                ScriviLog("CapacitaStampante: la stampante " & nome & " non ha risposto entro " & (CAPACITA_TIMEOUT_MS \ 1000).ToString() & " secondi")
                Return New CapacitaStampanteInfo()
            End If

            If lavoro.Risultato Is Nothing Then Return New CapacitaStampanteInfo()
            Return CopiaCapacita(lavoro.Risultato)
        Catch ex As Exception
            ScriviLog("CapacitaStampante: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New CapacitaStampanteInfo()
        End Try
    End Function

    Public Shared Function CapacitaStampanteInCache(nomeStampante As String, ByRef capacita As CapacitaStampanteInfo) As Boolean
        capacita = Nothing
        Try
            Dim nome As String = NormalizzaNomeStampante(nomeStampante)
            If String.IsNullOrWhiteSpace(nome) Then Return False

            CaricaCacheCapacita()

            Dim trovata As CapacitaStampanteInfo = Nothing
            Dim daVerificare As Boolean = False
            SyncLock _lockCapacita
                If Not _cacheCapacita.TryGetValue(nome, trovata) OrElse trovata Is Nothing Then Return False
                capacita = CopiaCapacita(trovata)
                If Not _capacitaVerificate.Contains(nome) AndAlso Not _capacitaInVerifica.Contains(nome) Then
                    _capacitaInVerifica.Add(nome)
                    daVerificare = True
                End If
            End SyncLock

            If daVerificare Then
                Try
                    Dim verifica As New WGLavoroCapacita()
                    verifica.Nome = nome
                    verifica.AggiornaCache = True
                    Dim esecutore As New Thread(AddressOf verifica.Esegui)
                    esecutore.IsBackground = True
                    esecutore.Priority = ThreadPriority.BelowNormal
                    esecutore.Name = "WGStampante.VerificaCapacita"
                    esecutore.Start()
                Catch exVerifica As Exception
                    ScriviLog("CapacitaStampanteInCache avvio verifica: " & exVerifica.Message)
                    SyncLock _lockCapacita
                        _capacitaInVerifica.Remove(nome)
                    End SyncLock
                End Try
            End If

            Return True
        Catch ex As Exception
            ScriviLog("CapacitaStampanteInCache: " & ex.Message & Environment.NewLine & ex.StackTrace)
            capacita = Nothing
            Return False
        End Try
    End Function

    Private Class WGLavoroCapacita
        Public Nome As String = ""
        Public AggiornaCache As Boolean = False
        Public Risultato As CapacitaStampanteInfo = Nothing
        Public Valida As Boolean = False

        Public Sub Esegui()
            Try
                Dim valida As Boolean = False
                Risultato = LeggiCapacitaDalDriver(Nome, valida)
                Valida = valida
                If AggiornaCache AndAlso Valida Then MemorizzaCapacita(Nome, Risultato)
            Catch ex As Exception
                ScriviLog("WGLavoroCapacita.Esegui: " & ex.Message & Environment.NewLine & ex.StackTrace)
                Risultato = New CapacitaStampanteInfo()
                Valida = False
            Finally
                If AggiornaCache Then
                    Try
                        SyncLock _lockCapacita
                            _capacitaInVerifica.Remove(Nome)
                            _capacitaVerificate.Add(Nome)
                        End SyncLock
                    Catch exFine As Exception
                        ScriviLog("WGLavoroCapacita.Esegui fine: " & exFine.Message)
                    End Try
                End If
            End Try
        End Sub
    End Class

    Private Shared Function LeggiCapacitaDalDriver(nome As String, ByRef valida As Boolean) As CapacitaStampanteInfo
        Dim risultato As New CapacitaStampanteInfo()
        valida = False
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return risultato
            If nome.StartsWith("\\", StringComparison.Ordinal) Then
                Dim problemaRete As String = ""
                If Not VerificaRaggiungibilita(nome, problemaRete) Then Return risultato
            End If
            Dim duplex As Integer = DeviceCapabilities(nome, Nothing, DC_DUPLEX, IntPtr.Zero, IntPtr.Zero)
            If duplex < 0 Then
                ScriviLog("LeggiCapacitaDalDriver: DeviceCapabilities fallita per " & nome & " err=" & CodiceErrore().ToString())
                Return risultato
            End If
            risultato.SupportaFronteRetro = duplex > 0
            Dim colore As Integer = DeviceCapabilities(nome, Nothing, DC_COLORDEVICE, IntPtr.Zero, IntPtr.Zero)
            risultato.SupportaColore = colore > 0
            Dim numFormati As Integer = DeviceCapabilities(nome, Nothing, DC_PAPERNAMES, IntPtr.Zero, IntPtr.Zero)
            If numFormati > 0 Then
                Dim dimensioneVoce As Integer = 64
                Dim bufferFormati As IntPtr = Marshal.AllocHGlobal(numFormati * dimensioneVoce * 2)
                Try
                    Dim esitoFormati As Integer = DeviceCapabilities(nome, Nothing, DC_PAPERNAMES, bufferFormati, IntPtr.Zero)
                    If esitoFormati > 0 Then
                        For i As Integer = 0 To Math.Min(esitoFormati, numFormati) - 1
                            Dim puntatore As IntPtr = IntPtr.Add(bufferFormati, i * dimensioneVoce * 2)
                            Dim formato As String = Marshal.PtrToStringUni(puntatore, dimensioneVoce)
                            If formato IsNot Nothing Then
                                Dim posizioneNull As Integer = formato.IndexOf(ControlChars.NullChar)
                                If posizioneNull >= 0 Then formato = formato.Substring(0, posizioneNull)
                                formato = formato.Trim()
                                If Not String.IsNullOrWhiteSpace(formato) Then risultato.ElencoFormati.Add(formato)
                            End If
                        Next
                    End If
                Finally
                    Marshal.FreeHGlobal(bufferFormati)
                End Try
            End If
            valida = True
        Catch ex As Exception
            ScriviLog("LeggiCapacitaDalDriver: " & ex.Message & Environment.NewLine & ex.StackTrace)
            valida = False
        End Try
        Return risultato
    End Function

    Private Shared Function CopiaCapacita(origine As CapacitaStampanteInfo) As CapacitaStampanteInfo
        Dim copia As New CapacitaStampanteInfo()
        Try
            If origine Is Nothing Then Return copia
            copia.SupportaFronteRetro = origine.SupportaFronteRetro
            copia.SupportaColore = origine.SupportaColore
            If origine.ElencoFormati IsNot Nothing Then copia.ElencoFormati.AddRange(origine.ElencoFormati)
        Catch ex As Exception
            ScriviLog("CopiaCapacita: " & ex.Message)
        End Try
        Return copia
    End Function

    Private Shared Sub MemorizzaCapacita(nome As String, capacita As CapacitaStampanteInfo)
        Try
            If String.IsNullOrWhiteSpace(nome) OrElse capacita Is Nothing Then Return
            CaricaCacheCapacita()
            SyncLock _lockCapacita
                _cacheCapacita(nome) = CopiaCapacita(capacita)
                _capacitaVerificate.Add(nome)
            End SyncLock
            SalvaCacheCapacita()
        Catch ex As Exception
            ScriviLog("MemorizzaCapacita: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function PercorsoCacheCapacita() As String
        Try
            Return Path.Combine(WGDb.WGPercorsiApplicazione.CartellaCache(), "capacita_stampanti.dat")
        Catch ex As Exception
            ScriviLog("PercorsoCacheCapacita: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub CaricaCacheCapacita()
        Try
            SyncLock _lockCapacita
                If _cacheCapacitaCaricata Then Return
                _cacheCapacitaCaricata = True

                Dim percorso As String = PercorsoCacheCapacita()
                If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return

                For Each riga As String In File.ReadAllLines(percorso, Encoding.UTF8)
                    Try
                        If String.IsNullOrWhiteSpace(riga) Then Continue For
                        Dim parti As String() = riga.Split(ControlChars.Tab)
                        If parti.Length < 3 Then Continue For
                        Dim nome As String = parti(0).Trim()
                        If nome.Length = 0 Then Continue For
                        Dim info As New CapacitaStampanteInfo()
                        info.SupportaFronteRetro = parti(1) = "1"
                        info.SupportaColore = parti(2) = "1"
                        If parti.Length > 3 AndAlso parti(3).Length > 0 Then
                            For Each formato As String In parti(3).Split(SEPARATORE_FORMATI)
                                If Not String.IsNullOrWhiteSpace(formato) Then info.ElencoFormati.Add(formato)
                            Next
                        End If
                        _cacheCapacita(nome) = info
                    Catch exRiga As Exception
                        ScriviLog("CaricaCacheCapacita (riga): " & exRiga.Message)
                    End Try
                Next
            End SyncLock
        Catch ex As Exception
            ScriviLog("CaricaCacheCapacita: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Sub SalvaCacheCapacita()
        Try
            Dim percorso As String = PercorsoCacheCapacita()
            If String.IsNullOrWhiteSpace(percorso) Then Return

            Dim contenuto As New StringBuilder()
            SyncLock _lockCapacita
                For Each coppia As KeyValuePair(Of String, CapacitaStampanteInfo) In _cacheCapacita
                    If coppia.Value Is Nothing Then Continue For
                    contenuto.Append(coppia.Key.Replace(ControlChars.Tab, " "c))
                    contenuto.Append(ControlChars.Tab)
                    contenuto.Append(If(coppia.Value.SupportaFronteRetro, "1", "0"))
                    contenuto.Append(ControlChars.Tab)
                    contenuto.Append(If(coppia.Value.SupportaColore, "1", "0"))
                    contenuto.Append(ControlChars.Tab)
                    If coppia.Value.ElencoFormati IsNot Nothing Then
                        contenuto.Append(String.Join(SEPARATORE_FORMATI.ToString(), coppia.Value.ElencoFormati).Replace(ControlChars.Tab, " "c))
                    End If
                    contenuto.Append(Environment.NewLine)
                Next
            End SyncLock

            SyncLock _lockFileCapacita
                Dim cartella As String = Path.GetDirectoryName(percorso)
                If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
                Dim temporaneo As String = percorso & ".tmp"
                File.WriteAllText(temporaneo, contenuto.ToString(), New UTF8Encoding(True))
                If File.Exists(percorso) Then File.Delete(percorso)
                File.Move(temporaneo, percorso)
            End SyncLock
        Catch ex As Exception
            ScriviLog("SalvaCacheCapacita: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Shared Function CreaDcConRetry(nome As String, pDevMode As IntPtr) As IntPtr
        Try
            Dim ultimoErroreTentativo As Integer = 0
            For tentativo As Integer = 1 To TENTATIVI_MAX
                Dim hdc As IntPtr = CreateDC("WINSPOOL", nome, Nothing, pDevMode)
                If hdc <> IntPtr.Zero Then Return hdc
                ultimoErroreTentativo = CodiceErrore()
                hdc = CreateDC(Nothing, nome, Nothing, pDevMode)
                If hdc <> IntPtr.Zero Then Return hdc
                ultimoErroreTentativo = CodiceErrore()
                Thread.Sleep(300 * tentativo)
            Next
            ScriviLog("CreaDcConRetry: tutti i tentativi falliti per " & nome & " err=" & ultimoErroreTentativo.ToString())
            Return IntPtr.Zero
        Catch ex As Exception
            ScriviLog("CreaDcConRetry: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Shared Function AvviaDocumentoConRetry(hdc As IntPtr, ByRef doc As DOCINFO, ByRef idLavoro As Integer) As Boolean
        Try
            idLavoro = 0
            Dim ultimoErroreTentativo As Integer = 0
            For tentativo As Integer = 1 To TENTATIVI_MAX
                Dim esito As Integer = StartDoc(hdc, doc)
                If esito > 0 Then
                    idLavoro = esito
                    Return True
                End If
                ultimoErroreTentativo = CodiceErrore()
                Thread.Sleep(250 * tentativo)
            Next
            ScriviLog("AvviaDocumentoConRetry: tutti i tentativi falliti err=" & ultimoErroreTentativo.ToString())
            Return False
        Catch ex As Exception
            ScriviLog("AvviaDocumentoConRetry: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function CreaFontStampa(dpiY As Integer, settaggi As WGImpostazioniStampa) As IntPtr
        Try
            Dim punti As Integer = settaggi.DimensioneCarattere
            If punti < 6 Then punti = 6
            Dim altezzaFont As Integer = -CInt(Math.Round(punti * dpiY / 72.0))
            If altezzaFont = 0 Then altezzaFont = -CInt(Math.Round(11.0 * dpiY / 72.0))
            Dim nomi As String() = {settaggi.NomeCarattere, "Arial", "Microsoft Sans Serif", "Tahoma"}
            For Each nomeFont As String In nomi
                If String.IsNullOrWhiteSpace(nomeFont) Then Continue For
                Dim hFont As IntPtr = CreateFont(altezzaFont, 0, 0, 0, FW_NORMAL, 0, 0, 0, CUInt(DEFAULT_CHARSET), CUInt(OUT_TT_PRECIS), CUInt(CLIP_DEFAULT_PRECIS), CUInt(ANTIALIASED_QUALITY), CUInt(DEFAULT_PITCH), nomeFont)
                If hFont <> IntPtr.Zero Then Return hFont
            Next
            ScriviLog("CreaFontStampa: CreateFont fallita")
            Return IntPtr.Zero
        Catch ex As Exception
            ScriviLog("CreaFontStampa: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Shared Function AvviaPagina(ctx As WGContestoPagina) As Boolean
        Try
            If ctx.PaginaAperta Then
                If Not ChiudiPagina(ctx) Then Return False
            End If
            If Not ctx.SoloMisura Then
                If StartPage(ctx.Hdc) <= 0 Then
                    ScriviLog("AvviaPagina: StartPage fallita err=" & CodiceErrore().ToString())
                    Return False
                End If
                PreparaPaginaGdi(ctx.Hdc, ctx.HFont, ctx.HFontVecchio, ctx.HPen, ctx.HPenVecchio)
            End If
            ctx.PaginaAperta = True
            ctx.NumeroPagina += 1
            ctx.Y = ctx.YContenutoInizio
            If Not ctx.SoloMisura Then DisegnaIntestazionePagina(ctx)
            Return True
        Catch ex As Exception
            ScriviLog("AvviaPagina: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ChiudiPagina(ctx As WGContestoPagina) As Boolean
        Try
            If Not ctx.PaginaAperta Then Return True
            If Not ctx.SoloMisura Then
                DisegnaPiePagina(ctx)
                If EndPage(ctx.Hdc) <= 0 Then
                    ScriviLog("ChiudiPagina: EndPage fallita err=" & CodiceErrore().ToString())
                    ctx.PaginaAperta = False
                    Return False
                End If
            End If
            ctx.PaginaAperta = False
            Return True
        Catch ex As Exception
            ScriviLog("ChiudiPagina: " & ex.Message)
            ctx.PaginaAperta = False
            Return False
        End Try
    End Function

    Private Shared Function ImpaginaBlocchi(ctx As WGContestoPagina, blocchi As List(Of WGBloccoStampa)) As Boolean
        Try
            If Not AvviaPagina(ctx) Then Return False
            For Each blocco As WGBloccoStampa In blocchi
                If blocco Is Nothing Then Continue For
                If String.Equals(blocco.Tipo, "tabella", StringComparison.OrdinalIgnoreCase) Then
                    If Not StampaTabella(ctx, blocco) Then Return False
                ElseIf String.Equals(blocco.Tipo, "immagine", StringComparison.OrdinalIgnoreCase) Then
                    If Not StampaImmagineBlocco(ctx, blocco) Then Return False
                ElseIf String.Equals(blocco.Tipo, "immagine_pagina", StringComparison.OrdinalIgnoreCase) Then
                    If Not StampaImmaginePagina(ctx, blocco) Then Return False
                Else
                    If Not StampaParagrafo(ctx, If(blocco.Testo, "")) Then Return False
                End If
            Next
            If Not ChiudiPagina(ctx) Then Return False
            Return True
        Catch ex As Exception
            ScriviLog("ImpaginaBlocchi: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function AltezzaZonaIntestazionePiePagina(dpiY As Integer, settaggi As WGImpostazioniStampa, attiva As Boolean) As Integer
        Try
            If Not attiva Then Return 0
            Dim punti As Integer = settaggi.DimensioneCarattere - 2
            If punti < 6 Then punti = 6
            Return CInt(Math.Round(punti * dpiY / 72.0 * 1.6)) + 8
        Catch ex As Exception
            ScriviLog("AltezzaZonaIntestazionePiePagina: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function CreaFontIntestazione(dpiY As Integer, settaggi As WGImpostazioniStampa) As IntPtr
        Try
            Dim punti As Integer = settaggi.DimensioneCarattere - 2
            If punti < 6 Then punti = 6
            Dim altezzaFont As Integer = -CInt(Math.Round(punti * dpiY / 72.0))
            If altezzaFont = 0 Then altezzaFont = -CInt(Math.Round(9.0 * dpiY / 72.0))
            Dim nomi As String() = {settaggi.NomeCarattere, "Arial", "Microsoft Sans Serif", "Tahoma"}
            For Each nomeFont As String In nomi
                If String.IsNullOrWhiteSpace(nomeFont) Then Continue For
                Dim hFont As IntPtr = CreateFont(altezzaFont, 0, 0, 0, FW_NORMAL, 0, 0, 0, CUInt(DEFAULT_CHARSET), CUInt(OUT_TT_PRECIS), CUInt(CLIP_DEFAULT_PRECIS), CUInt(ANTIALIASED_QUALITY), CUInt(DEFAULT_PITCH), nomeFont)
                If hFont <> IntPtr.Zero Then Return hFont
            Next
            ScriviLog("CreaFontIntestazione: CreateFont fallita")
            Return IntPtr.Zero
        Catch ex As Exception
            ScriviLog("CreaFontIntestazione: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Shared Sub DisegnaIntestazionePagina(ctx As WGContestoPagina)
        Try
            If ctx.Settaggi Is Nothing OrElse Not ctx.Settaggi.MostraIntestazione Then Return
            If String.IsNullOrWhiteSpace(ctx.TitoloDocumento) Then Return
            If ctx.HFontIntestazione = IntPtr.Zero Then Return
            Dim selezionatoPrecedente As IntPtr = SelectObject(ctx.Hdc, ctx.HFontIntestazione)
            Try
                Dim rect As New RECT With {.Left = ctx.X, .Top = ctx.Margine, .Right = ctx.X + ctx.LarghezzaUtile, .Bottom = ctx.YContenutoInizio}
                DrawText(ctx.Hdc, ctx.TitoloDocumento, ctx.TitoloDocumento.Length, rect, DT_LEFT Or DT_NOPREFIX)
            Finally
                If selezionatoPrecedente <> IntPtr.Zero Then SelectObject(ctx.Hdc, selezionatoPrecedente)
            End Try
        Catch ex As Exception
            ScriviLog("DisegnaIntestazionePagina: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub DisegnaPiePagina(ctx As WGContestoPagina)
        Try
            If ctx.Settaggi Is Nothing OrElse Not ctx.Settaggi.MostraNumeroPagina Then Return
            If ctx.HFontIntestazione = IntPtr.Zero Then Return
            Dim totale As Integer = Math.Max(ctx.NumeroPagina, ctx.TotalePagineConosciute)
            Dim testo As String = "Pagina " & ctx.NumeroPagina.ToString() & " di " & totale.ToString()
            Dim selezionatoPrecedente As IntPtr = SelectObject(ctx.Hdc, ctx.HFontIntestazione)
            Try
                Dim rect As New RECT With {.Left = ctx.X, .Top = ctx.LimiteBasso, .Right = ctx.X + ctx.LarghezzaUtile, .Bottom = ctx.AltezzaPagina - ctx.Margine}
                DrawText(ctx.Hdc, testo, testo.Length, rect, DT_LEFT Or DT_NOPREFIX)
            Finally
                If selezionatoPrecedente <> IntPtr.Zero Then SelectObject(ctx.Hdc, selezionatoPrecedente)
            End Try
        Catch ex As Exception
            ScriviLog("DisegnaPiePagina: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub PreparaPaginaGdi(hdc As IntPtr, hFont As IntPtr, ByRef hFontVecchio As IntPtr, hPen As IntPtr, ByRef hPenVecchio As IntPtr)
        Try
            If hFont <> IntPtr.Zero Then
                Dim selezionato As IntPtr = SelectObject(hdc, hFont)
                If hFontVecchio = IntPtr.Zero Then hFontVecchio = selezionato
            End If
            If hPen <> IntPtr.Zero Then
                Dim selezionatoPen As IntPtr = SelectObject(hdc, hPen)
                If hPenVecchio = IntPtr.Zero Then hPenVecchio = selezionatoPen
            End If
            SetBkMode(hdc, TRANSPARENT)
            SetTextColor(hdc, 0)
        Catch ex As Exception
            ScriviLog("PreparaPaginaGdi: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub RipristinaOggetto(hdc As IntPtr, precedente As IntPtr)
        Try
            If precedente <> IntPtr.Zero AndAlso hdc <> IntPtr.Zero Then SelectObject(hdc, precedente)
        Catch
        End Try
    End Sub

    Private Shared Sub EliminaOggetto(handle As IntPtr)
        Try
            If handle <> IntPtr.Zero Then DeleteObject(handle)
        Catch
        End Try
    End Sub

    Private Shared Function StampaParagrafo(ctx As WGContestoPagina, testo As String) As Boolean
        Try
            If String.IsNullOrEmpty(testo) Then
                ctx.Y += 8
                Return True
            End If
            Dim righe As String() = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(New Char() {ControlChars.Lf})
            For Each riga As String In righe
                Dim testoRiga As String = If(riga, "")
                Dim rectCalc As New RECT With {.Left = ctx.X, .Top = ctx.Y, .Right = ctx.X + ctx.LarghezzaUtile, .Bottom = ctx.Y + 8000}
                Dim formatoCalc As UInteger = DT_LEFT Or DT_WORDBREAK Or DT_NOPREFIX Or DT_EXPANDTABS Or DT_CALCRECT
                DrawText(ctx.Hdc, testoRiga, testoRiga.Length, rectCalc, formatoCalc)
                Dim altezzaRiga As Integer = Math.Max(16, rectCalc.Bottom - rectCalc.Top)
                If ctx.Y + altezzaRiga > ctx.LimiteBasso Then
                    If Not AvviaPagina(ctx) Then Return False
                End If
                If Not ctx.SoloMisura Then
                    Dim rectDraw As New RECT With {.Left = ctx.X, .Top = ctx.Y, .Right = ctx.X + ctx.LarghezzaUtile, .Bottom = ctx.Y + altezzaRiga}
                    DrawText(ctx.Hdc, testoRiga, testoRiga.Length, rectDraw, DT_LEFT Or DT_WORDBREAK Or DT_NOPREFIX Or DT_EXPANDTABS)
                End If
                ctx.Y += altezzaRiga
            Next
            ctx.Y += 8
            Return True
        Catch ex As Exception
            ScriviLog("StampaParagrafo: " & ex.Message)
            Return True
        End Try
    End Function

    Private Shared Function StampaTabella(ctx As WGContestoPagina, blocco As WGBloccoStampa) As Boolean
        Try
            If blocco Is Nothing OrElse blocco.RigheTabella Is Nothing OrElse blocco.RigheTabella.Count = 0 Then Return True

            Dim colonne As Integer = 1
            For Each riga As String() In blocco.RigheTabella
                If riga IsNot Nothing AndAlso riga.Length > colonne Then colonne = riga.Length
            Next
            If colonne < 1 Then colonne = 1
            Dim larghezzaCella As Integer = Math.Max(40, ctx.LarghezzaUtile \ colonne)
            Dim padding As Integer = 4

            If Not StampaParagrafo(ctx, "Tabella") Then Return False

            For Each riga As String() In blocco.RigheTabella
                Dim celle As String() = If(riga, New String() {})
                Dim altezzaRiga As Integer = 20
                For c As Integer = 0 To colonne - 1
                    Dim testoCella As String = ""
                    If c < celle.Length AndAlso celle(c) IsNot Nothing Then testoCella = celle(c)
                    Dim rectCalc As New RECT With {.Left = 0, .Top = 0, .Right = larghezzaCella - (padding * 2), .Bottom = 8000}
                    DrawText(ctx.Hdc, testoCella, testoCella.Length, rectCalc, DT_LEFT Or DT_WORDBREAK Or DT_NOPREFIX Or DT_CALCRECT)
                    Dim h As Integer = Math.Max(20, (rectCalc.Bottom - rectCalc.Top) + (padding * 2))
                    If h > altezzaRiga Then altezzaRiga = h
                Next

                If ctx.Y + altezzaRiga > ctx.LimiteBasso Then
                    If Not AvviaPagina(ctx) Then Return False
                End If

                If Not ctx.SoloMisura Then
                    For c As Integer = 0 To colonne - 1
                        Dim left As Integer = ctx.X + (c * larghezzaCella)
                        Dim right As Integer = left + larghezzaCella
                        Rectangle(ctx.Hdc, left, ctx.Y, right, ctx.Y + altezzaRiga)
                        Dim testoCella As String = ""
                        If c < celle.Length AndAlso celle(c) IsNot Nothing Then testoCella = celle(c)
                        Dim rectDraw As New RECT With {.Left = left + padding, .Top = ctx.Y + padding, .Right = right - padding, .Bottom = ctx.Y + altezzaRiga - padding}
                        DrawText(ctx.Hdc, testoCella, testoCella.Length, rectDraw, DT_LEFT Or DT_WORDBREAK Or DT_NOPREFIX)
                    Next
                End If
                ctx.Y += altezzaRiga
            Next
            ctx.Y += 12
            Return True
        Catch ex As Exception
            ScriviLog("StampaTabella: " & ex.Message)
            Return True
        End Try
    End Function

    Private Shared Function CaricaImmagineBlocco(blocco As WGBloccoStampa) As System.Drawing.Image
        Try
            If blocco Is Nothing Then Return Nothing
            Dim contenuto As Byte() = Nothing
            If blocco.ImmagineBytes IsNot Nothing AndAlso blocco.ImmagineBytes.Length > 0 Then
                contenuto = blocco.ImmagineBytes
            ElseIf Not String.IsNullOrWhiteSpace(blocco.PercorsoImmagine) AndAlso File.Exists(blocco.PercorsoImmagine) Then
                contenuto = File.ReadAllBytes(blocco.PercorsoImmagine)
            End If
            If contenuto Is Nothing OrElse contenuto.Length = 0 Then Return Nothing
            Dim fotogrammi As Integer = 0
            Return DecodificaImmagine(contenuto, blocco.IndiceFotogramma, fotogrammi)
        Catch ex As Exception
            ScriviLog("CaricaImmagineBlocco: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function DecodificaImmagine(contenuto As Byte(), indiceFotogramma As Integer, ByRef fotogrammi As Integer) As System.Drawing.Bitmap
        fotogrammi = 0
        Try
            If contenuto Is Nothing OrElse contenuto.Length = 0 Then Return Nothing
            Dim risultato As System.Drawing.Bitmap = DecodificaConGdi(contenuto, indiceFotogramma, fotogrammi)
            If risultato IsNot Nothing Then Return risultato
            Return DecodificaConWic(contenuto, indiceFotogramma, fotogrammi)
        Catch ex As Exception
            ScriviLog("DecodificaImmagine: " & ex.Message)
            fotogrammi = 0
            Return Nothing
        End Try
    End Function

    Private Shared Function DecodificaConGdi(contenuto As Byte(), indiceFotogramma As Integer, ByRef fotogrammi As Integer) As System.Drawing.Bitmap
        fotogrammi = 0
        Try
            Using flusso As New MemoryStream(contenuto)
                Using originale As System.Drawing.Image = System.Drawing.Image.FromStream(flusso, True, True)
                    Dim totale As Integer = 1
                    Dim conPagine As Boolean = False
                    Try
                        For Each dimensione As Guid In originale.FrameDimensionsList
                            If dimensione = System.Drawing.Imaging.FrameDimension.Page.Guid Then
                                conPagine = True
                                totale = Math.Max(1, originale.GetFrameCount(System.Drawing.Imaging.FrameDimension.Page))
                            End If
                        Next
                    Catch exFotogrammi As Exception
                        ScriviLog("DecodificaConGdi fotogrammi: " & exFotogrammi.Message)
                        conPagine = False
                        totale = 1
                    End Try
                    Dim indice As Integer = Math.Max(0, Math.Min(totale - 1, indiceFotogramma))
                    If conPagine AndAlso totale > 1 Then originale.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Page, indice)
                    Dim orientamento As Integer = OrientamentoExif(originale)
                    Dim copia As New System.Drawing.Bitmap(originale)
                    RuotaSecondoOrientamento(copia, orientamento)
                    fotogrammi = totale
                    Return copia
                End Using
            End Using
        Catch ex As Exception
            ScriviLog("DecodificaConGdi: " & ex.Message)
            fotogrammi = 0
            Return Nothing
        End Try
    End Function

    Private Shared Function DecodificaConWic(contenuto As Byte(), indiceFotogramma As Integer, ByRef fotogrammi As Integer) As System.Drawing.Bitmap
        fotogrammi = 0
        Try
            Using flusso As New MemoryStream(contenuto)
                Dim decodificatore As System.Windows.Media.Imaging.BitmapDecoder = System.Windows.Media.Imaging.BitmapDecoder.Create(flusso, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad)
                If decodificatore Is Nothing OrElse decodificatore.Frames Is Nothing OrElse decodificatore.Frames.Count = 0 Then Return Nothing
                Dim totale As Integer = decodificatore.Frames.Count
                Dim indice As Integer = Math.Max(0, Math.Min(totale - 1, indiceFotogramma))
                Dim fotogramma As System.Windows.Media.Imaging.BitmapFrame = decodificatore.Frames(indice)
                Dim sorgente As System.Windows.Media.Imaging.BitmapSource = fotogramma
                Dim pixelTotali As Double = CDbl(fotogramma.PixelWidth) * CDbl(fotogramma.PixelHeight)
                If pixelTotali > 40000000.0 Then
                    Dim fattore As Double = Math.Sqrt(40000000.0 / pixelTotali)
                    sorgente = New System.Windows.Media.Imaging.TransformedBitmap(fotogramma, New System.Windows.Media.ScaleTransform(fattore, fattore))
                End If
                Dim convertito As New System.Windows.Media.Imaging.FormatConvertedBitmap(sorgente, System.Windows.Media.PixelFormats.Bgra32, Nothing, 0)
                Dim larghezza As Integer = convertito.PixelWidth
                Dim altezza As Integer = convertito.PixelHeight
                If larghezza <= 0 OrElse altezza <= 0 Then Return Nothing
                Dim passo As Integer = larghezza * 4
                Dim pixel As Byte() = New Byte(passo * altezza - 1) {}
                convertito.CopyPixels(pixel, passo, 0)
                Dim risultato As New System.Drawing.Bitmap(larghezza, altezza, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                Dim dati As System.Drawing.Imaging.BitmapData = risultato.LockBits(New System.Drawing.Rectangle(0, 0, larghezza, altezza), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                Try
                    For riga As Integer = 0 To altezza - 1
                        Marshal.Copy(pixel, riga * passo, IntPtr.Add(dati.Scan0, riga * dati.Stride), passo)
                    Next
                Finally
                    risultato.UnlockBits(dati)
                End Try
                RuotaSecondoOrientamento(risultato, OrientamentoWic(fotogramma))
                fotogrammi = totale
                Return risultato
            End Using
        Catch ex As Exception
            ScriviLog("DecodificaConWic: " & ex.Message)
            fotogrammi = 0
            Return Nothing
        End Try
    End Function

    Private Shared Function OrientamentoExif(immagine As System.Drawing.Image) As Integer
        Try
            If immagine Is Nothing Then Return 1
            If Array.IndexOf(immagine.PropertyIdList, &H112) < 0 Then Return 1
            Dim voce As System.Drawing.Imaging.PropertyItem = immagine.GetPropertyItem(&H112)
            If voce Is Nothing OrElse voce.Value Is Nothing OrElse voce.Value.Length < 2 Then Return 1
            Dim valore As Integer = BitConverter.ToUInt16(voce.Value, 0)
            If valore < 1 OrElse valore > 8 Then Return 1
            Return valore
        Catch ex As Exception
            ScriviLog("OrientamentoExif: " & ex.Message)
            Return 1
        End Try
    End Function

    Private Shared Function OrientamentoWic(fotogramma As System.Windows.Media.Imaging.BitmapFrame) As Integer
        Try
            If fotogramma Is Nothing Then Return 1
            Dim metadati As System.Windows.Media.Imaging.BitmapMetadata = TryCast(fotogramma.Metadata, System.Windows.Media.Imaging.BitmapMetadata)
            If metadati Is Nothing Then Return 1
            Dim valore As Object = metadati.GetQuery("System.Photo.Orientation")
            If valore Is Nothing Then Return 1
            Dim numero As Integer = Convert.ToInt32(valore)
            If numero < 1 OrElse numero > 8 Then Return 1
            Return numero
        Catch ex As Exception
            ScriviLog("OrientamentoWic: " & ex.Message)
            Return 1
        End Try
    End Function

    Private Shared Sub RuotaSecondoOrientamento(immagine As System.Drawing.Bitmap, orientamento As Integer)
        Try
            If immagine Is Nothing Then Return
            Select Case orientamento
                Case 2
                    immagine.RotateFlip(System.Drawing.RotateFlipType.RotateNoneFlipX)
                Case 3
                    immagine.RotateFlip(System.Drawing.RotateFlipType.Rotate180FlipNone)
                Case 4
                    immagine.RotateFlip(System.Drawing.RotateFlipType.Rotate180FlipX)
                Case 5
                    immagine.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipX)
                Case 6
                    immagine.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone)
                Case 7
                    immagine.RotateFlip(System.Drawing.RotateFlipType.Rotate270FlipX)
                Case 8
                    immagine.RotateFlip(System.Drawing.RotateFlipType.Rotate270FlipNone)
            End Select
        Catch ex As Exception
            ScriviLog("RuotaSecondoOrientamento: " & ex.Message)
        End Try
    End Sub

    Private Shared Function StampaImmaginePagina(ctx As WGContestoPagina, blocco As WGBloccoStampa) As Boolean
        Dim immagine As System.Drawing.Image = Nothing
        Try
            If ctx.Y > ctx.YContenutoInizio Then
                If Not AvviaPagina(ctx) Then Return False
            End If
            Dim areaLarghezza As Integer = Math.Max(1, ctx.LarghezzaUtile)
            Dim areaAltezza As Integer = Math.Max(1, ctx.LimiteBasso - ctx.YContenutoInizio)
            If Not ctx.SoloMisura Then
                immagine = CaricaImmagineBlocco(blocco)
                If immagine Is Nothing OrElse immagine.Width <= 0 OrElse immagine.Height <= 0 Then
                    ScriviLog("StampaImmaginePagina: immagine non caricabile o non valida")
                Else
                    Dim larghezzaPollici As Double = areaLarghezza / CDbl(Math.Max(1, ctx.DpiX))
                    Dim altezzaPollici As Double = areaAltezza / CDbl(Math.Max(1, ctx.DpiY))
                    Dim scalaDiritta As Double = Math.Min(larghezzaPollici / immagine.Width, altezzaPollici / immagine.Height)
                    Dim scalaRuotata As Double = Math.Min(larghezzaPollici / immagine.Height, altezzaPollici / immagine.Width)
                    If scalaRuotata > scalaDiritta * 1.15 Then
                        Dim ruotata As New System.Drawing.Bitmap(immagine)
                        ruotata.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone)
                        immagine.Dispose()
                        immagine = ruotata
                    End If
                    Dim scala As Double = Math.Min(larghezzaPollici / immagine.Width, altezzaPollici / immagine.Height)
                    Dim larghezzaDestinazione As Integer = Math.Max(1, Math.Min(areaLarghezza, CInt(Math.Floor(immagine.Width * scala * ctx.DpiX))))
                    Dim altezzaDestinazione As Integer = Math.Max(1, Math.Min(areaAltezza, CInt(Math.Floor(immagine.Height * scala * ctx.DpiY))))
                    Dim sinistra As Integer = ctx.X + Math.Max(0, (areaLarghezza - larghezzaDestinazione) \ 2)
                    Dim alto As Integer = ctx.YContenutoInizio + Math.Max(0, (areaAltezza - altezzaDestinazione) \ 2)
                    Try
                        Using g As System.Drawing.Graphics = System.Drawing.Graphics.FromHdc(ctx.Hdc)
                            g.PageUnit = System.Drawing.GraphicsUnit.Pixel
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality
                            g.DrawImage(immagine, New System.Drawing.Rectangle(sinistra, alto, larghezzaDestinazione, altezzaDestinazione))
                        End Using
                    Catch exDisegno As Exception
                        ScriviLog("StampaImmaginePagina disegno: " & exDisegno.Message)
                    End Try
                End If
            End If
            ctx.Y = Math.Max(ctx.Y, ctx.LimiteBasso)
            Return True
        Catch ex As Exception
            ScriviLog("StampaImmaginePagina: " & ex.Message)
            Return True
        Finally
            Try
                If immagine IsNot Nothing Then immagine.Dispose()
            Catch exRilascio As Exception
                ScriviLog("StampaImmaginePagina rilascio: " & exRilascio.Message)
            End Try
        End Try
    End Function

    ''' <summary>Disegna un'immagine sull'HDC di stampa, adattandola alla larghezza/altezza utile della pagina e passando a pagina nuova se non c'entra.</summary>
    Private Shared Function StampaImmagineBlocco(ctx As WGContestoPagina, blocco As WGBloccoStampa) As Boolean
        Dim img As System.Drawing.Image = Nothing
        Try
            img = CaricaImmagineBlocco(blocco)
            If img Is Nothing OrElse img.Width <= 0 OrElse img.Height <= 0 Then
                ScriviLog("StampaImmagineBlocco: immagine non caricabile o non valida")
                Return True
            End If

            Dim rapporto As Double = img.Height / CDbl(img.Width)
            Dim targetW As Integer
            Dim targetH As Integer

            If blocco.LarghezzaImmagineMM > 0 Then
                targetW = CInt(Math.Round(blocco.LarghezzaImmagineMM * ctx.DpiX / 25.4))
            Else
                targetW = Math.Min(ctx.LarghezzaUtile, img.Width)
            End If
            If targetW <= 0 Then targetW = ctx.LarghezzaUtile

            If blocco.AltezzaImmagineMM > 0 Then
                targetH = CInt(Math.Round(blocco.AltezzaImmagineMM * ctx.DpiY / 25.4))
            Else
                targetH = CInt(Math.Round(targetW * rapporto))
            End If
            If targetH <= 0 Then targetH = targetW

            ' Se troppo larga, riduci mantenendo le proporzioni
            If targetW > ctx.LarghezzaUtile Then
                Dim fattoreW As Double = ctx.LarghezzaUtile / CDbl(targetW)
                targetW = ctx.LarghezzaUtile
                targetH = Math.Max(1, CInt(Math.Round(targetH * fattoreW)))
            End If

            ' Se più alta di una pagina intera, riduci mantenendo le proporzioni (non gestiamo lo spezzamento su più pagine)
            Dim altezzaMassimaPagina As Integer = Math.Max(40, ctx.LimiteBasso - ctx.YContenutoInizio)
            If targetH > altezzaMassimaPagina Then
                Dim fattoreH As Double = altezzaMassimaPagina / CDbl(targetH)
                targetH = altezzaMassimaPagina
                targetW = Math.Max(1, CInt(Math.Round(targetW * fattoreH)))
            End If

            If ctx.Y + targetH > ctx.LimiteBasso Then
                If Not AvviaPagina(ctx) Then Return False
            End If

            If Not ctx.SoloMisura Then
                Try
                    Using g As System.Drawing.Graphics = System.Drawing.Graphics.FromHdc(ctx.Hdc)
                        g.PageUnit = System.Drawing.GraphicsUnit.Pixel
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality
                        g.DrawImage(img, New System.Drawing.Rectangle(ctx.X, ctx.Y, targetW, targetH))
                    End Using
                Catch exDisegno As Exception
                    ScriviLog("StampaImmagineBlocco disegno: " & exDisegno.Message)
                End Try
            End If

            ctx.Y += targetH + 12
            Return True
        Catch ex As Exception
            ScriviLog("StampaImmagineBlocco: " & ex.Message)
            Return True
        Finally
            Try
                If img IsNot Nothing Then img.Dispose()
            Catch
            End Try
        End Try
    End Function

    Public Shared ReadOnly EstensioniImmagine As String() = {".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp"}
    Private Shared ReadOnly EstensioniTestoSemplice As String() = {".txt", ".log", ".csv", ".md", ".ini", ".xml", ".json", ".vb", ".cs", ".js", ".css"}

    Public Shared Function EImmagineStampabile(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            Dim estensione As String = Path.GetExtension(percorso).ToLowerInvariant()
            Return Array.IndexOf(EstensioniImmagine, estensione) >= 0
        Catch ex As Exception
            ScriviLog("EImmagineStampabile: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function ContaFotogrammiImmagine(percorso As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return 0
            Dim contenuto As Byte() = File.ReadAllBytes(percorso)
            Dim fotogrammi As Integer = 0
            Using prova As System.Drawing.Bitmap = DecodificaImmagine(contenuto, 0, fotogrammi)
                If prova Is Nothing Then Return 0
            End Using
            Return Math.Max(1, fotogrammi)
        Catch ex As Exception
            ScriviLog("ContaFotogrammiImmagine: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Shared Function BlocchiImmagine(percorso As String) As List(Of WGBloccoStampa)
        Dim blocchi As New List(Of WGBloccoStampa)()
        Try
            Dim fotogrammi As Integer = ContaFotogrammiImmagine(percorso)
            For indice As Integer = 0 To fotogrammi - 1
                Dim blocco As New WGBloccoStampa()
                blocco.Tipo = "immagine_pagina"
                blocco.PercorsoImmagine = percorso
                blocco.IndiceFotogramma = indice
                blocchi.Add(blocco)
            Next
        Catch ex As Exception
            ScriviLog("BlocchiImmagine: " & ex.Message)
        End Try
        Return blocchi
    End Function

    Private Shared Function ImpostazioniPerImmagini(impostazioni As WGImpostazioniStampa) As WGImpostazioniStampa
        Dim settaggi As WGImpostazioniStampa = New WGImpostazioniStampa()
        Try
            If impostazioni IsNot Nothing Then settaggi = impostazioni.CopiaSicura()
            settaggi.MostraIntestazione = False
            settaggi.MostraNumeroPagina = False
            settaggi.Normalizza()
        Catch ex As Exception
            ScriviLog("ImpostazioniPerImmagini: " & ex.Message)
        End Try
        Return settaggi
    End Function

    Public Shared Function ContaPagineImmagini(percorsi As List(Of String), Optional impostazioni As WGImpostazioniStampa = Nothing) As Integer
        Try
            If percorsi Is Nothing Then Return 0
            Dim settaggi As WGImpostazioniStampa = ImpostazioniPerImmagini(impostazioni)
            Dim totale As Integer = 0
            For Each percorso As String In percorsi
                totale += ContaFotogrammiImmagine(percorso)
            Next
            Return totale * Math.Max(1, settaggi.Copie)
        Catch ex As Exception
            ScriviLog("ContaPagineImmagini: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Shared Function StampaImmagini(percorsi As List(Of String), nomeStampante As String, titoloDocumento As String, impostazioni As WGImpostazioniStampa, attendiCompletamento As Boolean, Optional timeoutCompletamentoSecondi As Integer = 60) As WGEsitoStampa
        Dim esito As New WGEsitoStampa()
        Try
            AzzeraErrore()
            Dim blocchi As New List(Of WGBloccoStampa)()
            If percorsi IsNot Nothing Then
                For Each percorso As String In percorsi
                    If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Continue For
                    blocchi.AddRange(BlocchiImmagine(percorso))
                Next
            End If
            If blocchi.Count = 0 Then Return ImpostaEsitoErrore(esito, "L'immagine non si può leggere, quindi non si può stampare.")
            Return StampaBlocchi(blocchi, nomeStampante, titoloDocumento, ImpostazioniPerImmagini(impostazioni), attendiCompletamento, timeoutCompletamentoSecondi)
        Catch ex As Exception
            ScriviLog("StampaImmagini: " & ex.Message)
            Return ImpostaEsitoErrore(esito, "La stampa dell'immagine non è riuscita.")
        End Try
    End Function

    Public Shared Function StampaImmagine(percorsoImmagine As String, nomeStampante As String, Optional titoloDocumento As String = "WinGabriel", Optional impostazioni As WGImpostazioniStampa = Nothing) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorsoImmagine) OrElse Not File.Exists(percorsoImmagine) Then
                ImpostaErrore("Il file immagine non esiste.")
                Return False
            End If
            Dim esito As WGEsitoStampa = StampaImmagini(New List(Of String) From {percorsoImmagine}, nomeStampante, titoloDocumento, impostazioni, False)
            Return esito IsNot Nothing AndAlso esito.Riuscita
        Catch ex As Exception
            ScriviLog("StampaImmagine(percorso): " & ex.Message)
            ImpostaErrore("La stampa dell'immagine non è riuscita.")
            Return False
        End Try
    End Function

    Public Shared Function StampaImmagine(datiImmagine As Byte(), nomeStampante As String, Optional titoloDocumento As String = "WinGabriel", Optional impostazioni As WGImpostazioniStampa = Nothing) As Boolean
        Try
            If datiImmagine Is Nothing OrElse datiImmagine.Length = 0 Then
                ImpostaErrore("Non ci sono dati immagine da stampare.")
                Return False
            End If
            Dim fotogrammi As Integer = 0
            Using prova As System.Drawing.Bitmap = DecodificaImmagine(datiImmagine, 0, fotogrammi)
                If prova Is Nothing Then
                    ImpostaErrore("L'immagine non si può leggere, quindi non si può stampare.")
                    Return False
                End If
            End Using
            Dim elenco As New List(Of WGBloccoStampa)()
            For indice As Integer = 0 To Math.Max(1, fotogrammi) - 1
                Dim blocco As New WGBloccoStampa()
                blocco.Tipo = "immagine_pagina"
                blocco.ImmagineBytes = datiImmagine
                blocco.IndiceFotogramma = indice
                elenco.Add(blocco)
            Next
            Dim esito As WGEsitoStampa = StampaBlocchi(elenco, nomeStampante, titoloDocumento, ImpostazioniPerImmagini(impostazioni), False)
            Return esito IsNot Nothing AndAlso esito.Riuscita
        Catch ex As Exception
            ScriviLog("StampaImmagine(bytes): " & ex.Message)
            ImpostaErrore("La stampa dell'immagine non è riuscita.")
            Return False
        End Try
    End Function

    Public Shared Function StampaFile(percorsoFile As String, nomeStampante As String, titoloDocumento As String, impostazioni As WGImpostazioniStampa, attendiCompletamento As Boolean, Optional timeoutCompletamentoSecondi As Integer = 60) As WGEsitoStampa
        Dim esito As New WGEsitoStampa()
        Try
            AzzeraErrore()
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                Return ImpostaEsitoErrore(esito, "Il file da stampare non esiste.")
            End If

            Dim estensione As String = ""
            Try
                estensione = Path.GetExtension(percorsoFile).ToLowerInvariant()
            Catch exEstensione As Exception
                ScriviLog("StampaFile estensione: " & exEstensione.Message)
                estensione = ""
            End Try

            If estensione = ".pdf" Then
                Return StampaPdf(percorsoFile, nomeStampante, titoloDocumento, impostazioni, attendiCompletamento, timeoutCompletamentoSecondi)
            End If

            If EImmagineStampabile(percorsoFile) Then
                Return StampaImmagini(New List(Of String) From {percorsoFile}, nomeStampante, titoloDocumento, impostazioni, attendiCompletamento, timeoutCompletamentoSecondi)
            End If

            If Array.IndexOf(EstensioniTestoSemplice, estensione) >= 0 Then
                Try
                    Dim testo As String = File.ReadAllText(percorsoFile)
                    Dim bloccoTesto As New WGBloccoStampa()
                    bloccoTesto.Tipo = "testo"
                    bloccoTesto.Testo = testo
                    Dim elencoTesto As New List(Of WGBloccoStampa) From {bloccoTesto}
                    Return StampaBlocchi(elencoTesto, nomeStampante, titoloDocumento, impostazioni, attendiCompletamento, timeoutCompletamentoSecondi)
                Catch exLettura As Exception
                    ScriviLog("StampaFile lettura testo: " & exLettura.Message)
                    Return ImpostaEsitoErrore(esito, "Impossibile leggere il file di testo.")
                End Try
            End If

            Return ImpostaEsitoErrore(esito, "Questo tipo di file non si può stampare.")
        Catch ex As Exception
            ScriviLog("StampaFile: " & ex.Message)
            Return ImpostaEsitoErrore(esito, "La stampa del file non è riuscita.")
        End Try
    End Function

    Public Shared Function StampaFile(percorsoFile As String, nomeStampante As String, Optional titoloDocumento As String = "WinGabriel", Optional impostazioni As WGImpostazioniStampa = Nothing) As Boolean
        Try
            Dim esito As WGEsitoStampa = StampaFile(percorsoFile, nomeStampante, titoloDocumento, impostazioni, False)
            Return esito IsNot Nothing AndAlso esito.Riuscita
        Catch ex As Exception
            ScriviLog("StampaFile compat: " & ex.Message)
            ImpostaErrore("La stampa del file non è riuscita.")
            Return False
        End Try
    End Function

    Public Shared Function StampaPdf(percorsoPdf As String, nomeStampante As String, Optional impostazioni As WGImpostazioniStampa = Nothing) As Boolean
        Try
            AzzeraErrore()
            If String.IsNullOrWhiteSpace(percorsoPdf) OrElse Not File.Exists(percorsoPdf) Then
                ImpostaErrore("Il file PDF non esiste.")
                Return False
            End If
            Dim esito As WGEsitoStampa = StampaPdf(percorsoPdf, nomeStampante, Path.GetFileName(percorsoPdf), impostazioni, False)
            Return esito IsNot Nothing AndAlso esito.Riuscita
        Catch ex As Exception
            ScriviLog("StampaPdf: " & ex.Message)
            ImpostaErrore("La stampa del PDF non è riuscita.")
            Return False
        End Try
    End Function
End Class