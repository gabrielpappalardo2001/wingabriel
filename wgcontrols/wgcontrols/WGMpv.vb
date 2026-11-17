' ============================================================================================
' WGMpv - motore di riproduzione audio e video (libmpv)
' ============================================================================================
'
' A cosa serve
'   E' l'unico punto da cui WinGabriel comanda il lettore mpv: file locali, video di YouTube,
'   radio, canali TV in diretta (con profili automatici per RAI e Mediaset) e file OneDrive solo
'   online, che vengono scaricati prima di partire. La classe e' un singolo esemplare condiviso
'   da tutto il programma: si ottiene con OttieniIstanza(), non si crea con New.
'
' Come si usa
'   Dim lettore As WGMpv = WGMpv.OttieniIstanza()
'   If lettore Is Nothing Then Return                 ' libmpv non disponibile
'   AddHandler lettore.MediaCaricato, AddressOf QuandoParte
'   AddHandler lettore.ErroreMedia, AddressOf QuandoFallisce
'   lettore.AllegaAFinestra(gui.VideoHandle)          ' oppure ContinuaInSottofondo() per il solo audio
'   lettore.PreparaNuovoCaricamento()
'   lettore.CaricaFile("C:\musica\brano.mp3")
'   lettore.Volume = 80
'   lettore.AlternaRiproduzione()
'   lettore.FermaRiproduzione()
'
' Metodi pubblici principali
'   OttieniIstanza / GetInstance   restituiscono l'esemplare condiviso, Nothing se libmpv non si
'                     e' potuto avviare (la dll manca o la scheda video non collabora).
'   CaricaFile, RiproduciVideoYoutube, RiproduciStreamTv, RiproduciCanaleTv, RiproduciRai,
'   RiproduciMediaset, RiproduciPlaylistTv  avviano una riproduzione; non restituiscono nulla,
'                     l'esito arriva con gli eventi MediaCaricato / ErroreMedia / FineFile.
'   Riproduci, Pausa, AlternaRiproduzione, FermaRiproduzione, Scorri, ScorriAssoluto, Volume,
'   Velocita, AlternaMuto  comandi normali del lettore.
'   ContinuaInSottofondo   stacca il video dalla finestra e tiene solo l'audio.
'   Dispose           chiude mpv e ferma i thread interni; da chiamare solo alla chiusura del
'                     programma, perche' l'esemplare e' condiviso.
'
' Errori
'   Nessun metodo solleva eccezioni: ogni problema viene scritto nel log (dati\logs\log.txt) e
'   la chiamata non ha effetto. Quando la riproduzione fallisce davvero arriva l'evento
'   ErroreMedia (ErroreRadio per le radio); per i canali RAI, dopo troppi errori ravvicinati,
'   scatta CircuitoRaiSospeso e i tentativi vengono sospesi per qualche minuto.
'
' Attenzione
'   Tutti gli eventi vengono sollevati dal thread interno degli eventi di mpv: chi li gestisce e
'   deve toccare finestre o campi deve rientrare nel thread dell'interfaccia (Invoke) da solo.
'   libmpv-2.dll e cldapi.dll sono cercate nella cartella del programma. La riproduzione di un
'   file OneDrive solo online puo' richiedere parecchio tempo: si viene avvisati con l'evento
'   ProgressoDownloadOneDrive e si puo' annullare con AnnullaDownloadOneDrive.
' ============================================================================================
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Globalization
Imports System.Windows.Forms
Imports System.IO
Imports System.Net

Public Class WGMpv
    Implements IDisposable

    Private Shared _istanza As WGMpv
    Private Shared ReadOnly _blocco As New Object()
    Private _formSottofondo As Form
    Private _formOspite As Form

    Public Event MediaCaricato As EventHandler
    Public Event FineFile As EventHandler
    Public Event EndFile As EventHandler
    Public Event ErroreMedia As EventHandler
    Public Event Spegnimento As EventHandler
    Public Event MessaggioLog As EventHandler(Of LogMessageEventArgs)
    Public Event CambioProprieta As EventHandler(Of PropertyChangedEventArgs)
    Public Event CambioQualita As EventHandler(Of CambioQualitaEventArgs)
    Public Event AnalisiConnessioneCompletata As EventHandler(Of AnalisiConnessioneEventArgs)
    Public Event ProgressoDownloadOneDrive As EventHandler(Of ProgressoOneDriveEventArgs)
    Public Event RiconnessioneStream As EventHandler(Of RiconnessioneStreamEventArgs)
    Public Event StreamTvAvviato As EventHandler
    Public Event ErroreRadio As EventHandler(Of ErroreRadioEventArgs)
    Public Event CircuitoRaiSospeso As EventHandler(Of CircuitoRaiEventArgs)

    Private Shared log As New Logger()

    Private _inArresto As Boolean = False
    Private _handle As IntPtr = IntPtr.Zero
    Private _threadEventi As Thread

    Private _posizioneTemporale As Double = 0
    Private _durata As Double = 0
    Private _volume As Double = 100
    Private _velocita As Double = 1.0
    Private _inPausa As Boolean = False
    Private _durataBufferCorrente As Double = 0

    Private _urlYoutubeCorrente As String = String.Empty
    Private _qualitaCorrente As QualitaVideo = QualitaVideo.P480
    Private _qualitaAdattivaAbilitata As Boolean = True
    Private _posizioneRipristino As Double = -1
    Private _cambioQualitaInCorso As Boolean = False
    Private _ultimoCambioQualita As DateTime = DateTime.MinValue
    Private _contatoreSottoSoglia As Integer = 0
    Private _ultimaValutazione As DateTime = DateTime.MinValue
    Private _orarioAvvioRiproduzione As DateTime = DateTime.MinValue
    Private _bandaStimata As Double = 0
    Private _campioniBanda As Integer = 0
    Private _faseAvvioInCorso As Boolean = False
    Private _faseAttesaPreroll As Boolean = False
    Private _orarioInizioFaseAvvio As DateTime = DateTime.MinValue
    Private _tentativiVideoOndemand As Integer = 0
    Private _threadRetryVideoOndemand As Thread

    Private _threadDownloadOneDrive As Thread
    Private _downloadOneDriveInCorso As Boolean = False
    Private _annullaDownloadOneDrive As Boolean = False

    ' --- Modalità TV / IPTV live ---
    Private _modalitaLive As Boolean = False
    Private _urlStreamTv As String = String.Empty
    Private _urlOriginaleTv As String = String.Empty
    Private _userAgentTv As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    Private _refererTv As String = String.Empty
    Private _headersExtraTv As String = String.Empty
    Private _tentativiRiconnessione As Integer = 0
    Private _maxTentativiRiconnessione As Integer = 8
    Private _orarioUltimaRiconnessione As DateTime = DateTime.MinValue
    Private _ultimaPosizioneOsservata As Double = -1
    Private _orarioUltimoAvanzamento As DateTime = DateTime.MinValue
    Private _riconnessioneInCorso As Boolean = False
    Private _bassaLatenza As Boolean = False
    Private _threadRiconnessione As Thread
    Private _isRaiRelinker As Boolean = False
    Private _isMediaset As Boolean = False

    Private _radioCorrente As Boolean = False
    Private _urlRadioCorrente As String = String.Empty

    ' Profilo emittente rilevato automaticamente
    Private Structure ProfiloEmittente
        Public Nome As String
        Public UrlOttimizzato As String
        Public UserAgent As String
        Public Referer As String
        Public HeadersExtra As String
        Public IsRai As Boolean
        Public IsMediaset As Boolean
    End Structure

    ' User-Agent collaudati per emittenti italiane
    Private Const UA_RAI_HBBTV As String = "HbbTV/1.6.1 (+DRM; Eu; rv:34) AppleWebKit/537.36"
    Private Const UA_RAI_APPLETV As String = "AppleCoreMedia/1.0.0.19B92 (Apple TV; U; CPU OS 15_1 like Mac OS X; en_us)"
    Private Const UA_RAI_GENERIC As String = "Rai"
    Private Const UA_CHROME As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    Private Const UA_VLC As String = "VLC/3.0.20 LibVLC/3.0.20"
    Private Const UA_FIREFOX As String = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0"

    Private Shared ReadOnly RAI_FORCE_USERAGENT_VARIANTI As String() = {"raiplayappletv", "rainet/4.0.5", "hbbtv"}
    Private _raiIndiceVariante As Integer = 0
    Private _raiTentativiConsecutivi503 As Integer = 0
    Private Const SECONDI_COOLDOWN_RAI_503 As Integer = 6
    Private Const MAX_TENTATIVI_PRIMA_ROTAZIONE_RAI As Integer = 2

    Private Const RAI_SOGLIA_FALLIMENTI_CIRCUITO As Integer = 3
    Private Const RAI_FINESTRA_FALLIMENTI_SECONDI As Integer = 45
    Private Const RAI_COOLDOWN_MINIMO_SECONDI As Integer = 60
    Private Const RAI_COOLDOWN_MASSIMO_SECONDI As Integer = 900
    Private Shared ReadOnly _raiLockCircuito As New Object()
    Private Shared _raiFallimentiRecenti As Integer = 0
    Private Shared _raiPrimoFallimentoFinestra As DateTime = DateTime.MinValue
    Private Shared _raiCircuitoApertoDal As DateTime = DateTime.MinValue
    Private Shared _raiCooldownCorrenteSecondi As Integer = RAI_COOLDOWN_MINIMO_SECONDI
    Private Shared _raiInProvaSemiAperto As Boolean = False

    Private Const SOGLIA_BUFFER_DOWNGRADE As Double = 5.0
    Private Const TENTATIVI_DOWNGRADE As Integer = 4
    Private Const SECONDI_COOLDOWN_DOWNGRADE As Integer = 30
    Private Const SECONDI_TRA_VALUTAZIONI As Integer = 3
    Private Const SECONDI_GRAZIA_AVVIO As Integer = 8
    Private Const SECONDI_BUFFER_MISURA As Double = 60.0
    Private Const SECONDI_TIMEOUT_MISURA As Double = 45.0
    Private Const SECONDI_PREROLL_FINALE As Double = 10.0
    Private Const SECONDI_TIMEOUT_PREROLL As Double = 30.0
    Private Const CAMPIONI_BANDA_MINIMI As Integer = 4
    Private Const MARGINE_BANDA As Double = 2.0
    Private Const SOGLIA_BUFFER_PIENO As Double = 150.0
    Private Const INTERVALLO_PROGRESSO_MS As Integer = 500
    Private Const SECONDI_COOLDOWN_RICONNESSIONE As Integer = 3
    Private Const SECONDI_BACKOFF_BASE As Integer = 2
    Private Const SECONDI_STALLO_MASSIMO As Double = 15
    Private Const MAX_TENTATIVI_VIDEO_ONDEMAND As Integer = 2
    Private Const MILLISECONDI_RETRY_VIDEO_ONDEMAND As Integer = 900

    Private Shared ReadOnly BitrateStimatiBps As Double() = {18000000.0, 5000000.0, 2800000.0, 1400000.0, 800000.0}

    Private Const FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS As UInteger = &H400000UI
    Private Const FILE_ATTRIBUTE_RECALL_ON_OPEN As UInteger = &H40000UI
    Private Const FILE_ATTRIBUTE_OFFLINE As UInteger = &H1000UI

    Private Const CF_OPEN_FILE_FLAG_NONE As Integer = 0
    Private Const CF_HYDRATE_FLAG_NONE As Integer = 0
    Private Const CF_EOF As Long = -1L
    Private Const S_OK As Integer = 0

    Public Enum QualitaVideo
        K4 = 0
        P1080 = 1
        P720 = 2
        P480 = 3
        P360 = 4
    End Enum

    Private Const QUALITA_MASSIMA_ADATTIVA As QualitaVideo = QualitaVideo.P1080
    Private Const QUALITA_MINIMA As QualitaVideo = QualitaVideo.P360
    Private Const QUALITA_AVVIO As QualitaVideo = QualitaVideo.P480

    Private Shared ReadOnly FormatiAdattivi As String() = {
        "bestvideo[height<=2160][vcodec^=vp9]+bestaudio/bestvideo[height<=2160][vcodec!^=av01]+bestaudio/best",
        "bestvideo[height<=1080][vcodec^=vp9]+bestaudio/bestvideo[height<=1080]+bestaudio/best",
        "bestvideo[height<=720]+bestaudio/best",
        "bestvideo[height<=480]+bestaudio/best",
        "bestvideo[height<=360]+bestaudio/best[height<=360]/worst"
    }

    Private Shared ReadOnly NomiQualita As String() = {"4K", "1080p", "720p", "480p", "360p"}

    Private Const LIBMPV As String = "libmpv-2.dll"
    Private Const CFAPI As String = "cldapi.dll"

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function mpv_create() As IntPtr
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function mpv_initialize(ctx As IntPtr) As Integer
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub mpv_terminate_destroy(ctx As IntPtr)
    End Sub

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="mpv_set_option_string")>
    Private Shared Function mpv_set_option_string_raw(ctx As IntPtr, name As IntPtr, data As IntPtr) As Integer
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="mpv_set_property_string")>
    Private Shared Function mpv_set_property_string_raw(ctx As IntPtr, name As IntPtr, data As IntPtr) As Integer
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="mpv_get_property_string")>
    Private Shared Function mpv_get_property_string_raw(ctx As IntPtr, name As IntPtr) As IntPtr
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function mpv_command(ctx As IntPtr, args As IntPtr) As Integer
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function mpv_wait_event(ctx As IntPtr, timeout As Double) As IntPtr
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Sub mpv_free(data As IntPtr)
    End Sub

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl)>
    Private Shared Function mpv_error_string(errorCode As Integer) As IntPtr
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="mpv_request_log_messages")>
    Private Shared Function mpv_request_log_messages_raw(ctx As IntPtr, min_level As IntPtr) As Integer
    End Function

    <DllImport(LIBMPV, CallingConvention:=CallingConvention.Cdecl, EntryPoint:="mpv_observe_property")>
    Private Shared Function mpv_observe_property_raw(ctx As IntPtr, reply_userdata As UInt64, name As IntPtr, format As Integer) As Integer
    End Function

    Private Shared Function StringToUtf8Ptr(s As String) As IntPtr
        Try
            Dim testo As String = If(s, String.Empty)
            Dim bytes As Byte() = System.Text.Encoding.UTF8.GetBytes(testo & ChrW(0))
            Dim ptr As IntPtr = Marshal.AllocHGlobal(bytes.Length)
            Marshal.Copy(bytes, 0, ptr, bytes.Length)
            Return ptr
        Catch ex As Exception
            log.ScriviLog("Eccezione in StringToUtf8Ptr: " & ex.Message)
            Return IntPtr.Zero
        End Try
    End Function

    Private Shared Sub FreeUtf8Ptr(ptr As IntPtr)
        Try
            If ptr <> IntPtr.Zero Then Marshal.FreeHGlobal(ptr)
        Catch ex As Exception
            log.ScriviLog("Eccezione in FreeUtf8Ptr: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Utf8PtrToString(ptr As IntPtr) As String
        Try
            If ptr = IntPtr.Zero Then Return String.Empty
            Dim lunghezza As Integer = 0
            While Marshal.ReadByte(ptr, lunghezza) <> 0
                lunghezza += 1
            End While
            If lunghezza = 0 Then Return String.Empty
            Dim buffer As Byte() = New Byte(lunghezza - 1) {}
            Marshal.Copy(ptr, buffer, 0, lunghezza)
            Return System.Text.Encoding.UTF8.GetString(buffer)
        Catch ex As Exception
            log.ScriviLog("Eccezione in Utf8PtrToString: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Private Shared Function mpv_set_option_string(ctx As IntPtr, name As String, data As String) As Integer
        Dim pName As IntPtr = IntPtr.Zero, pData As IntPtr = IntPtr.Zero
        Try
            pName = StringToUtf8Ptr(name)
            pData = StringToUtf8Ptr(data)
            Return mpv_set_option_string_raw(ctx, pName, pData)
        Finally
            FreeUtf8Ptr(pName)
            FreeUtf8Ptr(pData)
        End Try
    End Function

    Private Shared Function mpv_set_property_string(ctx As IntPtr, name As String, data As String) As Integer
        Dim pName As IntPtr = IntPtr.Zero, pData As IntPtr = IntPtr.Zero
        Try
            pName = StringToUtf8Ptr(name)
            pData = StringToUtf8Ptr(data)
            Return mpv_set_property_string_raw(ctx, pName, pData)
        Finally
            FreeUtf8Ptr(pName)
            FreeUtf8Ptr(pData)
        End Try
    End Function

    Private Shared Function mpv_get_property_string(ctx As IntPtr, name As String) As IntPtr
        Dim pName As IntPtr = IntPtr.Zero
        Try
            pName = StringToUtf8Ptr(name)
            Return mpv_get_property_string_raw(ctx, pName)
        Finally
            FreeUtf8Ptr(pName)
        End Try
    End Function

    Private Shared Function mpv_request_log_messages(ctx As IntPtr, min_level As String) As Integer
        Dim pLevel As IntPtr = IntPtr.Zero
        Try
            pLevel = StringToUtf8Ptr(min_level)
            Return mpv_request_log_messages_raw(ctx, pLevel)
        Finally
            FreeUtf8Ptr(pLevel)
        End Try
    End Function

    Private Shared Function mpv_observe_property(ctx As IntPtr, reply_userdata As UInt64, name As String, format As Integer) As Integer
        Dim pName As IntPtr = IntPtr.Zero
        Try
            pName = StringToUtf8Ptr(name)
            Return mpv_observe_property_raw(ctx, reply_userdata, pName, format)
        Finally
            FreeUtf8Ptr(pName)
        End Try
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetFileAttributes(lpFileName As String) As UInteger
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetDriveTypeW(lpRootPathName As String) As UInteger
    End Function

    Private Const DRIVE_REMOTE As UInteger = 4UI

    Public Shared Function EPercorsoDiRete(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            Dim p As String = percorso.Trim()
            If p.StartsWith("\\?\UNC\", StringComparison.OrdinalIgnoreCase) Then Return True
            If p.StartsWith("\\?\", StringComparison.Ordinal) Then
                p = p.Substring(4)
            ElseIf p.StartsWith("\\", StringComparison.Ordinal) Then
                Return True
            End If
            If p.Length >= 2 AndAlso p(1) = ":"c AndAlso Char.IsLetter(p(0)) Then
                Return GetDriveTypeW(p.Substring(0, 2) & "\") = DRIVE_REMOTE
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("Eccezione in EPercorsoDiRete: " & ex.Message)
            Return False
        End Try
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibraryExW(lpLibFileName As String, hFile As IntPtr, dwFlags As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetModuleHandleW(lpModuleName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function GetFileSizeEx(hFile As IntPtr, ByRef lpFileSize As Long) As Boolean
    End Function

    <DllImport(CFAPI, CharSet:=CharSet.Unicode, CallingConvention:=CallingConvention.Winapi)>
    Private Shared Function CfOpenFileWithOplock(
        filePath As String,
        flags As Integer,
        ByRef protectedHandle As IntPtr) As Integer
    End Function

    <DllImport(CFAPI, CallingConvention:=CallingConvention.Winapi)>
    Private Shared Function CfHydratePlaceholder(
        fileHandle As IntPtr,
        startingOffset As Long,
        length As Long,
        hydrateFlags As Integer,
        overlapped As IntPtr) As Integer
    End Function

    <DllImport(CFAPI, CallingConvention:=CallingConvention.Winapi)>
    Private Shared Function CfGetWin32HandleFromProtectedHandle(protectedHandle As IntPtr) As IntPtr
    End Function

    <DllImport(CFAPI, CallingConvention:=CallingConvention.Winapi)>
    Private Shared Sub CfCloseHandle(protectedHandle As IntPtr)
    End Sub

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MpvEvent
        Public event_id As Integer
        Public [error] As Integer
        Public reply_userdata As UInt64
        Public data As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MpvEventProperty
        Public name As IntPtr
        Public format As Integer
        Public data As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MpvEventEndFile
        Public reason As Integer
        Public [error] As Integer
        Public playlist_entry_id As Int64
        Public playlist_insert_id As Int64
        Public playlist_insert_num_entries As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MpvEventLogMessage
        Public prefix As IntPtr
        Public level As IntPtr
        Public text As IntPtr
        Public log_level As Integer
    End Structure

    Private Const MPV_FORMAT_FLAG As Integer = 3
    Private Const MPV_FORMAT_DOUBLE As Integer = 5
    Private Const MPV_EVENT_SHUTDOWN As Integer = 1
    Private Const MPV_EVENT_LOG_MESSAGE As Integer = 2
    Private Const MPV_EVENT_FILE_LOADED As Integer = 8
    Private Const MPV_EVENT_END_FILE As Integer = 7
    Private Const MPV_EVENT_PROPERTY_CHANGE As Integer = 22

    #Region "Verifica e ripristino automatico di libmpv-2.dll"

    Private Const URL_LIBMPV_X64 As String = "https://wingabriel.net/wingabriel/x64/libmpv-2.dll"
    Private Const URL_LIBMPV_X86 As String = "https://wingabriel.net/wingabriel/x86/libmpv-2.dll"

    Private Const MACCHINA_PE_I386 As Integer = &H14C
    Private Const MACCHINA_PE_AMD64 As Integer = &H8664
    Private Const MACCHINA_PE_ARM64 As Integer = &HAA64
    Private Const LOAD_WITH_ALTERED_SEARCH_PATH As UInteger = &H8UI
    Private Const TIMEOUT_DOWNLOAD_LIBRERIA As Integer = 180000
    Private Const DIMENSIONE_MINIMA_LIBRERIA As Long = 1000000L
    Private Const SUFFISSO_LIBRERIA_NUOVA As String = ".nuova"
    Private Const SUFFISSO_LIBRERIA_VECCHIA As String = ".vecchia"

    Private Shared ReadOnly _bloccoLibreria As New Object()
    Private Shared _libreriaVerificata As Boolean = False
    Private Shared _libreriaPronta As Boolean = False
    Private Shared _ripristinoLibreriaInCorso As Boolean = False
    Private Shared _riavvioRichiesto As Boolean = False
    Private Shared _statoLibreria As String = String.Empty
    Private Shared _percentualeDownloadLibreria As Integer = 0

    ''' <summary>
    ''' Segnala l'avanzamento della verifica, del download e dell'installazione di libmpv-2.dll.
    ''' Viene sollevato anche prima che esista un esemplare di WGMpv, quindi e' condiviso.
    ''' </summary>
    Public Shared Event StatoLibreria As EventHandler(Of StatoLibreriaEventArgs)

    ''' <summary>
    ''' Vero quando libmpv-2.dll e' presente, dell'architettura giusta e caricata in memoria.
    ''' </summary>
    Public Shared ReadOnly Property LibreriaPronta As Boolean
        Get
            Try
                Return _libreriaPronta
            Catch
                Return False
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Vero mentre e' in corso il download o la sostituzione di libmpv-2.dll: chi mostra un
    ''' avviso di attesa non deve chiuderlo finche' questa proprieta' resta vera.
    ''' </summary>
    Public Shared ReadOnly Property RipristinoLibreriaInCorso As Boolean
        Get
            Try
                Return _ripristinoLibreriaInCorso
            Catch
                Return False
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Vero quando la libreria corretta e' stata scaricata ma non e' stato possibile sostituire
    ''' quella in uso: la sostituzione avverra' al prossimo avvio del programma.
    ''' </summary>
    Public Shared ReadOnly Property RiavvioRichiesto As Boolean
        Get
            Try
                Return _riavvioRichiesto
            Catch
                Return False
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Ultimo messaggio descrittivo sullo stato della libreria, adatto alla sintesi vocale.
    ''' </summary>
    Public Shared ReadOnly Property DescrizioneStatoLibreria As String
        Get
            Try
                If String.IsNullOrEmpty(_statoLibreria) Then Return String.Empty
                Return _statoLibreria
            Catch
                Return String.Empty
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Percentuale dell'eventuale download di libmpv-2.dll, da 0 a 100.
    ''' </summary>
    Public Shared ReadOnly Property PercentualeDownloadLibreria As Integer
        Get
            Try
                Return _percentualeDownloadLibreria
            Catch
                Return 0
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Vero quando esiste un esemplare di WGMpv con un contesto mpv valido.
    ''' </summary>
    Public Shared ReadOnly Property LettoreDisponibile As Boolean
        Get
            Try
                If _istanza Is Nothing Then Return False
                Return _istanza.MotoreAttivo
            Catch
                Return False
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Vero se questo esemplare ha un contesto mpv utilizzabile.
    ''' </summary>
    Public ReadOnly Property MotoreAttivo As Boolean
        Get
            Try
                Return _handle <> IntPtr.Zero
            Catch
                Return False
            End Try
        End Get
    End Property

    ''' <summary>
    ''' Controlla che libmpv-2.dll sia presente e compatibile e, se serve, la scarica dal sito
    ''' di WinGabriel scegliendo la versione a 32 o 64 bit in base al processo in esecuzione.
    ''' </summary>
    ''' <param name="forzaDownload">Vero per riscaricare la libreria anche se il controllo locale la ritiene valida.</param>
    ''' <returns>Vero se al termine la libreria e' caricabile.</returns>
    Public Shared Function AssicuraLibreriaMpv(Optional forzaDownload As Boolean = False) As Boolean
        SyncLock _bloccoLibreria
            Try
                If _libreriaVerificata AndAlso _libreriaPronta AndAlso Not forzaDownload Then Return True

                Dim percorso As String = PercorsoLibreriaLocale()
                If String.IsNullOrWhiteSpace(percorso) Then
                    _libreriaVerificata = True
                    _libreriaPronta = False
                    Return False
                End If

                ApplicaSostituzioneRimandata(percorso)

                Dim macchinaAttesa As Integer = MacchinaAttesaProcesso()
                Dim serveDownload As Boolean = forzaDownload

                If Not serveDownload Then
                    If Not File.Exists(percorso) Then
                        serveDownload = True
                        SegnalaStatoLibreria("mancante", "Il componente di riproduzione non e' presente, lo scarico.")
                    Else
                        Dim macchinaFile As Integer = LeggiMacchinaPe(percorso)
                        If macchinaFile = 0 Then
                            serveDownload = True
                            SegnalaStatoLibreria("danneggiata", "Il componente di riproduzione e' danneggiato, lo riscarico.")
                        ElseIf macchinaFile <> macchinaAttesa Then
                            serveDownload = True
                            SegnalaStatoLibreria("architettura", "Il componente di riproduzione non e' adatto a questo sistema, scarico quello corretto.")
                        Else
                            Try
                                Dim informazioni As New FileInfo(percorso)
                                If informazioni.Length < DIMENSIONE_MINIMA_LIBRERIA Then
                                    serveDownload = True
                                    SegnalaStatoLibreria("incompleta", "Il componente di riproduzione e' incompleto, lo riscarico.")
                                End If
                            Catch exDimensione As Exception
                                log.ScriviLog("AssicuraLibreriaMpv dimensione: " & exDimensione.Message)
                            End Try
                        End If
                    End If
                End If

                If serveDownload Then
                    _ripristinoLibreriaInCorso = True
                    Try
                        If Not ScaricaEInstallaLibreria(percorso, macchinaAttesa) Then
                            _libreriaVerificata = True
                            _libreriaPronta = False
                            Return False
                        End If
                    Finally
                        _ripristinoLibreriaInCorso = False
                    End Try
                End If

                Dim modulo As IntPtr = IntPtr.Zero
                Try
                    modulo = GetModuleHandleW(LIBMPV)
                    If modulo = IntPtr.Zero Then
                        modulo = LoadLibraryExW(percorso, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH)
                    End If
                Catch exCaricamento As Exception
                    log.ScriviLog("AssicuraLibreriaMpv caricamento: " & exCaricamento.Message)
                    modulo = IntPtr.Zero
                End Try

                _libreriaPronta = modulo <> IntPtr.Zero
                _libreriaVerificata = True

                If _libreriaPronta Then
                    SegnalaStatoLibreria("pronta", "Il componente di riproduzione e' pronto.")
                Else
                    SegnalaStatoLibreria("errore", "Non riesco a caricare il componente di riproduzione.")
                End If

                Return _libreriaPronta
            Catch ex As Exception
                log.ScriviLog("Eccezione in AssicuraLibreriaMpv: " & ex.ToString())
                _libreriaVerificata = True
                _libreriaPronta = False
                Return False
            End Try
        End SyncLock
    End Function

    Private Shared Function ScaricaEInstallaLibreria(percorsoFinale As String, macchinaAttesa As Integer) As Boolean
        Dim fileTemporaneo As String = String.Empty
        Try
            _percentualeDownloadLibreria = 0
            SegnalaStatoLibreria("download", "Sto scaricando il componente di riproduzione, attendi.")

            Try
                fileTemporaneo = Path.Combine(Path.GetTempPath(), "libmpv-2-" & Guid.NewGuid().ToString("N") & ".tmp")
            Catch exTemp As Exception
                log.ScriviLog("ScaricaEInstallaLibreria percorso temporaneo: " & exTemp.Message)
                Return False
            End Try

            ' Prima dal rilascio firmato (sito o, se il sito non risponde, GitHub), controllando l'impronta scritta nel manifest;
            ' se il manifest non si legge, dal sito e solo in https. Mai in http: la libreria si carica nel programma.
            Dim riuscito As Boolean = ScaricaLibreriaDalRilascio(fileTemporaneo)
            If Not riuscito Then
                riuscito = ScaricaFileLibreria(UrlLibreriaPrincipale(), fileTemporaneo)
            End If

            If Not riuscito Then
                SegnalaStatoLibreria("errore", "Non sono riuscito a scaricare il componente di riproduzione.")
                Return False
            End If

            Dim macchinaScaricata As Integer = LeggiMacchinaPe(fileTemporaneo)
            If macchinaScaricata <> macchinaAttesa Then
                SegnalaStatoLibreria("errore", "Il componente scaricato non e' adatto a questo sistema.")
                Return False
            End If

            SegnalaStatoLibreria("installazione", "Sto installando il componente di riproduzione.")

            If Not InstallaLibreria(fileTemporaneo, percorsoFinale) Then
                Return False
            End If

            SegnalaStatoLibreria("installata", "Componente di riproduzione aggiornato.")
            Return True
        Catch ex As Exception
            log.ScriviLog("Eccezione in ScaricaEInstallaLibreria: " & ex.ToString())
            Return False
        Finally
            Try
                If Not String.IsNullOrWhiteSpace(fileTemporaneo) AndAlso File.Exists(fileTemporaneo) Then
                    File.Delete(fileTemporaneo)
                End If
            Catch exPulizia As Exception
                log.ScriviLog("ScaricaEInstallaLibreria pulizia: " & exPulizia.Message)
            End Try
        End Try
    End Function

    Private Shared Function ScaricaFileLibreria(indirizzo As String, destinazione As String) As Boolean
        Dim risposta As HttpWebResponse = Nothing
        Try
            If String.IsNullOrWhiteSpace(indirizzo) OrElse String.IsNullOrWhiteSpace(destinazione) Then Return False

            Try
                ServicePointManager.Expect100Continue = False
                ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType) Or CType(12288, SecurityProtocolType)
            Catch
                Try
                    ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
                Catch exTls As Exception
                    log.ScriviLog("ScaricaFileLibreria tls: " & exTls.Message)
                End Try
            End Try

            Dim richiesta As HttpWebRequest = CType(WebRequest.Create(indirizzo), HttpWebRequest)
            richiesta.Method = "GET"
            richiesta.Timeout = TIMEOUT_DOWNLOAD_LIBRERIA
            richiesta.ReadWriteTimeout = TIMEOUT_DOWNLOAD_LIBRERIA
            richiesta.AllowAutoRedirect = True
            richiesta.KeepAlive = False
            richiesta.UserAgent = "WinGabriel"
            richiesta.Accept = "*/*"

            risposta = CType(richiesta.GetResponse(), HttpWebResponse)
            If CInt(risposta.StatusCode) <> 200 Then
                Return False
            End If

            Dim totale As Long = risposta.ContentLength
            Dim scritti As Long = 0
            Dim ultimaPercentuale As Integer = -1

            Using flussoRete As Stream = risposta.GetResponseStream()
                If flussoRete Is Nothing Then Return False
                Using flussoFile As New FileStream(destinazione, FileMode.Create, FileAccess.Write, FileShare.None)
                    Dim buffer(65535) As Byte
                    Dim letti As Integer = flussoRete.Read(buffer, 0, buffer.Length)
                    While letti > 0
                        flussoFile.Write(buffer, 0, letti)
                        scritti = scritti + letti

                        If totale > 0 Then
                            Dim percentuale As Integer = CInt(Math.Floor((scritti * 100.0R) / totale))
                            If percentuale > 100 Then percentuale = 100
                            If percentuale <> ultimaPercentuale AndAlso (percentuale Mod 5) = 0 Then
                                ultimaPercentuale = percentuale
                                _percentualeDownloadLibreria = percentuale
                                SegnalaStatoLibreria("download", "Scaricamento del componente di riproduzione al " & percentuale.ToString() & " per cento.")
                            End If
                        End If

                        letti = flussoRete.Read(buffer, 0, buffer.Length)
                    End While
                End Using
            End Using

            _percentualeDownloadLibreria = 100

            If scritti < DIMENSIONE_MINIMA_LIBRERIA Then
                Return False
            End If

            Return True
        Catch exWeb As WebException
            log.ScriviLog("ScaricaFileLibreria (" & indirizzo & "): " & exWeb.Message)
            Return False
        Catch ex As Exception
            log.ScriviLog("Eccezione in ScaricaFileLibreria: " & ex.ToString())
            Return False
        Finally
            Try
                If risposta IsNot Nothing Then risposta.Close()
            Catch exChiusura As Exception
                log.ScriviLog("ScaricaFileLibreria chiusura: " & exChiusura.Message)
            End Try
        End Try
    End Function

    Private Shared Function InstallaLibreria(origine As String, destinazione As String) As Boolean
        Try
            If Not File.Exists(destinazione) Then
                File.Copy(origine, destinazione, True)
                Return True
            End If

            Try
                File.Delete(destinazione)
                File.Copy(origine, destinazione, True)
                Return True
            Catch exElimina As Exception
                log.ScriviLog("InstallaLibreria eliminazione diretta non riuscita: " & exElimina.Message)
            End Try

            Try
                Dim vecchia As String = destinazione & SUFFISSO_LIBRERIA_VECCHIA & DateTime.Now.Ticks.ToString()
                File.Move(destinazione, vecchia)
                File.Copy(origine, destinazione, True)
                RimuoviLibrerieVecchie(destinazione)
                Return True
            Catch exRinomina As Exception
                log.ScriviLog("InstallaLibreria rinomina non riuscita: " & exRinomina.Message)
            End Try

            Try
                File.Copy(origine, destinazione & SUFFISSO_LIBRERIA_NUOVA, True)
                _riavvioRichiesto = True
                SegnalaStatoLibreria("riavvio", "Il componente di riproduzione e' pronto ma serve riavviare WinGabriel.")
                Return False
            Catch exRimandata As Exception
                log.ScriviLog("InstallaLibreria copia rimandata non riuscita: " & exRimandata.Message)
                Return False
            End Try
        Catch ex As Exception
            log.ScriviLog("Eccezione in InstallaLibreria: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Sub ApplicaSostituzioneRimandata(destinazione As String)
        Try
            Dim nuova As String = destinazione & SUFFISSO_LIBRERIA_NUOVA
            If Not File.Exists(nuova) Then Return

            Try
                If File.Exists(destinazione) Then File.Delete(destinazione)
            Catch exElimina As Exception
                log.ScriviLog("ApplicaSostituzioneRimandata eliminazione: " & exElimina.Message)
                Return
            End Try

            File.Move(nuova, destinazione)
        Catch ex As Exception
            log.ScriviLog("Eccezione in ApplicaSostituzioneRimandata: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub RimuoviLibrerieVecchie(destinazione As String)
        Try
            Dim cartella As String = Path.GetDirectoryName(destinazione)
            If String.IsNullOrWhiteSpace(cartella) OrElse Not Directory.Exists(cartella) Then Return

            Dim modello As String = Path.GetFileName(destinazione) & SUFFISSO_LIBRERIA_VECCHIA & "*"
            For Each f As String In Directory.GetFiles(cartella, modello)
                Try
                    File.Delete(f)
                Catch exFile As Exception
                    log.ScriviLog("RimuoviLibrerieVecchie: " & Path.GetFileName(f) & " - " & exFile.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("Eccezione in RimuoviLibrerieVecchie: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function PercorsoLibreriaLocale() As String
        Try
            Dim cartella As String = String.Empty

            Try
                cartella = AppDomain.CurrentDomain.BaseDirectory
            Catch exDominio As Exception
                log.ScriviLog("PercorsoLibreriaLocale dominio: " & exDominio.Message)
            End Try

            If String.IsNullOrWhiteSpace(cartella) Then
                Try
                    cartella = Application.StartupPath
                Catch exAvvio As Exception
                    log.ScriviLog("PercorsoLibreriaLocale avvio: " & exAvvio.Message)
                End Try
            End If

            If String.IsNullOrWhiteSpace(cartella) Then Return String.Empty
            Return Path.Combine(cartella, LIBMPV)
        Catch ex As Exception
            log.ScriviLog("Eccezione in PercorsoLibreriaLocale: " & ex.ToString())
            Return String.Empty
        End Try
    End Function

    Private Shared Function MacchinaAttesaProcesso() As Integer
        Try
            If IntPtr.Size = 8 Then
                If SistemaArm64() Then Return MACCHINA_PE_ARM64
                Return MACCHINA_PE_AMD64
            End If
            Return MACCHINA_PE_I386
        Catch ex As Exception
            log.ScriviLog("Eccezione in MacchinaAttesaProcesso: " & ex.ToString())
            If IntPtr.Size = 8 Then Return MACCHINA_PE_AMD64
            Return MACCHINA_PE_I386
        End Try
    End Function

    Private Shared Function SistemaArm64() As Boolean
        Try
            Dim architettura As String = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")
            If String.IsNullOrWhiteSpace(architettura) Then Return False
            Return architettura.IndexOf("ARM", StringComparison.OrdinalIgnoreCase) >= 0
        Catch ex As Exception
            log.ScriviLog("Eccezione in SistemaArm64: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function UrlLibreriaPrincipale() As String
        Try
            If IntPtr.Size = 8 Then Return URL_LIBMPV_X64
            Return URL_LIBMPV_X86
        Catch
            Return URL_LIBMPV_X86
        End Try
    End Function

    ''' <summary>
    ''' Scarica libmpv-2.dll dal rilascio firmato di WinGabriel (quello che usa anche l'aggiornatore) e controlla che l'impronta SHA-256
    ''' sia quella del manifest. Falso se il manifest non si legge, se manca la voce o se l'impronta non torna (il file si cancella).
    ''' </summary>
    Private Shared Function ScaricaLibreriaDalRilascio(destinazione As String) As Boolean
        Try
            Dim errore As String = ""
            Dim canale As String = RilascioFirmato.NormalizzaCanale(New WGConfig().LeggiValore("canale_aggiornamenti"))
            Dim manifest As RilascioFirmato.Manifest = RilascioFirmato.Leggi("wingabriel", canale, errore)
            If manifest Is Nothing Then
                log.ScriviLog("ScaricaLibreriaDalRilascio: manifest non disponibile (" & errore & ")")
                Return False
            End If
            Dim voce As RilascioFirmato.VoceManifest = manifest.Voci.FirstOrDefault(Function(v) v.Libmpv)
            If voce Is Nothing Then Return False
            Dim relativo As String = If(IntPtr.Size = 8, voce.UrlX64, voce.UrlX86)
            Dim attesa As String = If(IntPtr.Size = 8, voce.ImprontaX64, voce.ImprontaX86)
            If String.IsNullOrWhiteSpace(relativo) OrElse String.IsNullOrWhiteSpace(attesa) Then Return False
            If Not ScaricaFileLibreria(RilascioFirmato.UrlFile(manifest.UrlBaseFile, relativo), destinazione) Then Return False
            Dim impronta As String
            Using sha As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
                Using flusso As FileStream = File.OpenRead(destinazione)
                    impronta = BitConverter.ToString(sha.ComputeHash(flusso)).Replace("-", "").ToLowerInvariant()
                End Using
            End Using
            If Not String.Equals(impronta, attesa, StringComparison.OrdinalIgnoreCase) Then
                log.ScriviLog("ScaricaLibreriaDalRilascio: l'impronta del file scaricato non corrisponde al manifest firmato")
                File.Delete(destinazione)
                Return False
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("ScaricaLibreriaDalRilascio: " & ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Legge dall'intestazione PE l'architettura di un file eseguibile o di una libreria,
    ''' senza caricarlo in memoria. Restituisce zero se il file non e' un PE valido.
    ''' </summary>
    ''' <param name="percorso">Percorso completo del file da esaminare.</param>
    ''' <returns>Codice macchina PE, ad esempio 332 per x86 e 34404 per x64.</returns>
    Public Shared Function LeggiMacchinaPe(percorso As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then Return 0

            Using flusso As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                If flusso.Length < 64 Then Return 0

                Dim intestazione(1) As Byte
                flusso.Read(intestazione, 0, 2)
                If intestazione(0) <> &H4D OrElse intestazione(1) <> &H5A Then Return 0

                flusso.Seek(&H3C, SeekOrigin.Begin)
                Dim posizione(3) As Byte
                If flusso.Read(posizione, 0, 4) <> 4 Then Return 0

                Dim offsetPe As Integer = BitConverter.ToInt32(posizione, 0)
                If offsetPe <= 0 OrElse offsetPe + 6 > flusso.Length Then Return 0

                flusso.Seek(offsetPe, SeekOrigin.Begin)
                Dim firma(3) As Byte
                If flusso.Read(firma, 0, 4) <> 4 Then Return 0
                If firma(0) <> &H50 OrElse firma(1) <> &H45 OrElse firma(2) <> 0 OrElse firma(3) <> 0 Then Return 0

                Dim macchina(1) As Byte
                If flusso.Read(macchina, 0, 2) <> 2 Then Return 0

                Return CInt(BitConverter.ToUInt16(macchina, 0))
            End Using
        Catch ex As Exception
            log.ScriviLog("Eccezione in LeggiMacchinaPe (" & percorso & "): " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Sub SegnalaStatoLibreria(fase As String, messaggio As String)
        Try
            _statoLibreria = messaggio
            RaiseEvent StatoLibreria(Nothing, New StatoLibreriaEventArgs(fase, messaggio, _percentualeDownloadLibreria))
        Catch ex As Exception
            log.ScriviLog("Eccezione in SegnalaStatoLibreria: " & ex.Message)
        End Try
    End Sub

    Private Function CreaContestoMpv() As Boolean
        Try
            Try
                _handle = mpv_create()
            Catch exFormato As BadImageFormatException
                _handle = IntPtr.Zero
                log.ScriviLog("mpv_create: architettura di " & LIBMPV & " non compatibile, provo il ripristino. " & exFormato.Message)
            Catch exMancante As DllNotFoundException
                _handle = IntPtr.Zero
                log.ScriviLog("mpv_create: " & LIBMPV & " non trovata, provo il ripristino. " & exMancante.Message)
            End Try

            If _handle <> IntPtr.Zero Then Return True

            If Not AssicuraLibreriaMpv(True) Then Return False

            Try
                _handle = mpv_create()
            Catch exSecondo As Exception
                _handle = IntPtr.Zero
                log.ScriviLog("mpv_create dopo il ripristino: " & exSecondo.Message)
            End Try

            If _handle = IntPtr.Zero AndAlso Not _riavvioRichiesto Then
                _riavvioRichiesto = True
                SegnalaStatoLibreria("riavvio", "Il componente di riproduzione e' stato aggiornato, riavvia WinGabriel per usarlo.")
            End If

            Return _handle <> IntPtr.Zero
        Catch ex As Exception
            log.ScriviLog("Eccezione in CreaContestoMpv: " & ex.ToString())
            Return False
        End Try
    End Function

#End Region

    Private Sub New()
        Try
            AssicuraLibreriaMpv(False)

            If Not CreaContestoMpv() Then
                Return
            End If

            mpv_set_option_string(_handle, "idle", "yes")
            mpv_set_option_string(_handle, "keep-open", "yes")
            mpv_set_option_string(_handle, "terminal", "no")
            mpv_set_option_string(_handle, "ytdl", "yes")

            mpv_set_option_string(_handle, "vo", "gpu")
            mpv_set_option_string(_handle, "gpu-context", "d3d11")
            mpv_set_option_string(_handle, "hwdec", "auto")
            mpv_set_option_string(_handle, "hwdec-codecs", "h264,vc1,hevc,vp8,vp9")
            mpv_set_option_string(_handle, "vd-lavc-dr", "yes")
            mpv_set_option_string(_handle, "demuxer-thread", "yes")

            mpv_set_option_string(_handle, "cache", "yes")
            mpv_set_option_string(_handle, "demuxer-max-bytes", "1500M")
            mpv_set_option_string(_handle, "demuxer-max-back-bytes", "300M")
            mpv_set_option_string(_handle, "demuxer-seekable-cache", "yes")
            mpv_set_option_string(_handle, "cache-pause", "yes")
            mpv_set_option_string(_handle, "cache-pause-initial", "yes")
            mpv_set_option_string(_handle, "cache-pause-wait", "8")
            mpv_set_option_string(_handle, "cache-secs", "180")
            mpv_set_option_string(_handle, "cache-on-disk", "no")

            mpv_set_option_string(_handle, "stream-buffer-size", "1M")
            mpv_set_option_string(_handle, "force-seekable", "yes")
            mpv_set_option_string(_handle, "hr-seek", "no")
            mpv_set_option_string(_handle, "hr-seek-framedrop", "yes")
            mpv_set_option_string(_handle, "network-timeout", "30")
            mpv_set_option_string(_handle, "stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5,reconnect_on_network_error=1,reconnect_on_http_error=%7%4xx,5xx")
            ' yt-dlp ha bisogno di un motore JavaScript per YouTube: quello scaricato accanto al programma (deno.exe)
            Dim opzioniYtdl As String = "concurrent-fragments=6,fragment-retries=10,retries=10,extractor-args=%46%youtube:player_client=default,-android_sdkless"
            Try
                Dim deno As String = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deno.exe")
                Dim ytdlp As String = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe")
                Dim recente As Boolean = True
                ' --js-runtimes esiste dalla fine del 2025: con un yt-dlp più vecchio l'opzione farebbe fallire ogni riproduzione
                If System.IO.File.Exists(ytdlp) Then
                    Dim p As String() = If(FileVersionInfo.GetVersionInfo(ytdlp).FileVersion, "").Split("."c)
                    Dim a, m, g As Integer
                    If p.Length >= 3 AndAlso Integer.TryParse(p(0), a) AndAlso Integer.TryParse(p(1), m) AndAlso Integer.TryParse(p(2), g) Then recente = (a * 10000 + m * 100 + g) >= 20251112
                End If
                If recente AndAlso System.IO.File.Exists(deno) Then
                    Dim motore As String = "deno:" & deno
                    opzioniYtdl &= ",js-runtimes=%" & motore.Length.ToString() & "%" & motore
                End If
            Catch
            End Try
            mpv_set_option_string(_handle, "ytdl-raw-options", opzioniYtdl)
            ' Supporto protocolli streaming TV
            mpv_set_option_string(_handle, "hls-bitrate", "max")
            mpv_set_option_string(_handle, "demuxer-lavf-analyzeduration", "2")
            mpv_set_option_string(_handle, "demuxer-lavf-probesize", "1M")
            mpv_set_option_string(_handle, "demuxer-lavf-o", "fflags=+nobuffer+discardcorrupt")
            mpv_set_option_string(_handle, "vd-lavc-threads", "0")
            mpv_set_option_string(_handle, "ad-lavc-threads", "0")

            mpv_initialize(_handle)

            OsservaProprietaInterna("time-pos", MPV_FORMAT_DOUBLE)
            OsservaProprietaInterna("duration", MPV_FORMAT_DOUBLE)
            OsservaProprietaInterna("volume", MPV_FORMAT_DOUBLE)
            OsservaProprietaInterna("speed", MPV_FORMAT_DOUBLE)
            OsservaProprietaInterna("pause", MPV_FORMAT_FLAG)
            OsservaProprietaInterna("demuxer-cache-duration", MPV_FORMAT_DOUBLE)
            OsservaProprietaInterna("cache-speed", MPV_FORMAT_DOUBLE)

            ' Solo errori reali: evita di riempire il log di messaggi inutili
            mpv_request_log_messages(_handle, "error")

            _threadEventi = New Thread(AddressOf CicloEventi)
            _threadEventi.IsBackground = True
            _threadEventi.Start()
        Catch ex As Exception
            log.ScriviLog("Eccezione in costruttore WGMpv: " & ex.Message & " " & ex.StackTrace)
        End Try
    End Sub

    Public Sub SvuotaCache()
        Try
            Dim cartella As String = Path.Combine(Path.GetTempPath(), "WGMpvCache")
            If Directory.Exists(cartella) Then
                For Each f As String In Directory.GetFiles(cartella)
                    Try
                        File.Delete(f)
                    Catch exFile As Exception
                        log.ScriviLog("Impossibile eliminare file cache: " & f & " - " & exFile.Message)
                    End Try
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in SvuotaCache: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Restituisce l'esemplare condiviso del lettore, creandolo alla prima chiamata.
    ''' Se libmpv non e' utilizzabile restituisce Nothing senza memorizzare un esemplare
    ''' rotto, cosi' il tentativo successivo puo' riprovare dopo il ripristino della libreria.
    ''' </summary>
    ''' <returns>L'esemplare condiviso oppure Nothing se il motore non e' disponibile.</returns>
    Public Shared Function OttieniIstanza() As WGMpv
        Try
            If _istanza IsNot Nothing AndAlso _istanza.MotoreAttivo Then Return _istanza

            SyncLock _blocco
                If _istanza IsNot Nothing AndAlso _istanza.MotoreAttivo Then Return _istanza

                If _istanza IsNot Nothing Then
                    Try
                        _istanza.Dispose()
                    Catch exVecchia As Exception
                        log.ScriviLog("OttieniIstanza chiusura esemplare non valido: " & exVecchia.Message)
                    End Try
                    _istanza = Nothing
                End If

                Dim tentativo As WGMpv = New WGMpv()
                If tentativo.MotoreAttivo Then
                    _istanza = tentativo
                    Return _istanza
                End If

                Try
                    tentativo.Dispose()
                Catch exScarto As Exception
                    log.ScriviLog("OttieniIstanza scarto esemplare: " & exScarto.Message)
                End Try

                Return Nothing
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("Eccezione in OttieniIstanza: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>L'esemplare già creato, senza crearne uno (Nothing se il lettore non è mai partito).</summary>
    Public Shared ReadOnly Property IstanzaEsistente As WGMpv
        Get
            Dim esemplare As WGMpv = _istanza
            If esemplare IsNot Nothing AndAlso esemplare.MotoreAttivo Then Return esemplare
            Return Nothing
        End Get
    End Property

    Public Shared Function GetInstance() As WGMpv
        Try
            Return OttieniIstanza()
        Catch ex As Exception
            log.ScriviLog("Eccezione in GetInstance: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub CicloEventi()
        Try
            While Not _inArresto
                Try
                    Dim handleCorrente As IntPtr = _handle
                    If handleCorrente = IntPtr.Zero Then Exit While

                    ControllaFaseAvvio()
                    ControllaStalloLive()

                    Dim evPtr As IntPtr = mpv_wait_event(handleCorrente, 0.05)
                    If evPtr = IntPtr.Zero Then Continue While

                    Dim evt As MpvEvent = Marshal.PtrToStructure(Of MpvEvent)(evPtr)

                    Select Case evt.event_id
                        Case MPV_EVENT_FILE_LOADED
                            _tentativiVideoOndemand = 0
                            If _posizioneRipristino >= 0 Then
                                Dim pos As Double = _posizioneRipristino
                                _posizioneRipristino = -1
                                _cambioQualitaInCorso = False
                                EseguiComando("seek", pos.ToString(CultureInfo.InvariantCulture), "absolute+keyframes")
                            End If
                            If _modalitaLive Then
                                _tentativiRiconnessione = 0
                                _riconnessioneInCorso = False
                                _raiTentativiConsecutivi503 = 0
                                If _isRaiRelinker Then SegnalaEsitoRai(True)
                                _ultimaPosizioneOsservata = -1
                                _orarioUltimoAvanzamento = DateTime.Now
                                ' Forza audio attivo su stream live (alcuni HLS/ISM partono muti o in pausa)
                                Try
                                    ImpostaProprietaStringa("mute", "no")
                                    ImpostaProprietaStringa("volume", VolumeEffettivo().ToString(CultureInfo.InvariantCulture))
                                    ImpostaProprietaStringa("aid", "auto")
                                    ImpostaProprietaStringa("pause", "no")
                                Catch
                                End Try
                                RaiseEvent StreamTvAvviato(Me, EventArgs.Empty)
                            End If
                            RaiseEvent MediaCaricato(Me, EventArgs.Empty)

                        Case MPV_EVENT_END_FILE
                            If evt.data <> IntPtr.Zero Then
                                Dim endFileData As MpvEventEndFile = Marshal.PtrToStructure(Of MpvEventEndFile)(evt.data)
                                If endFileData.reason = 4 Then
                                    Dim errPtr As IntPtr = mpv_error_string(endFileData.error)
                                    Dim errStr As String = If(errPtr <> IntPtr.Zero, Utf8PtrToString(errPtr), "Errore sconosciuto")
                                    _cambioQualitaInCorso = False
                                    If _modalitaLive AndAlso Not String.IsNullOrEmpty(_urlStreamTv) Then
                                        ProgrammaRiconnessioneStream("errore: " & errStr)
                                    ElseIf Not _radioCorrente AndAlso Not String.IsNullOrEmpty(_urlYoutubeCorrente) AndAlso _tentativiVideoOndemand < MAX_TENTATIVI_VIDEO_ONDEMAND Then
                                        ProgrammaRetryVideoOndemand(errStr)
                                    Else
                                        _tentativiVideoOndemand = 0
                                        If _radioCorrente Then
                                            RaiseEvent ErroreRadio(Me, New ErroreRadioEventArgs(_urlRadioCorrente, errStr))
                                        End If
                                        RaiseEvent ErroreMedia(Me, EventArgs.Empty)
                                    End If
                                ElseIf endFileData.reason = 0 Then
                                    ' EOF normale: in live può indicare interruzione dello stream → riconnetti
                                    If _modalitaLive AndAlso Not String.IsNullOrEmpty(_urlStreamTv) Then
                                        ProgrammaRiconnessioneStream("fine stream inattesa")
                                    Else
                                        If _radioCorrente Then
                                            RaiseEvent ErroreRadio(Me, New ErroreRadioEventArgs(_urlRadioCorrente, "fine stream inattesa"))
                                        End If
                                        RaiseEvent FineFile(Me, EventArgs.Empty)
                                        RaiseEvent EndFile(Me, EventArgs.Empty)
                                    End If
                                ElseIf endFileData.reason = 2 Then
                                    ' Stop esplicito: non riconnettere, non loggare
                                End If
                            End If

                        Case MPV_EVENT_PROPERTY_CHANGE
                            If evt.data <> IntPtr.Zero Then
                                Dim prop As MpvEventProperty = Marshal.PtrToStructure(Of MpvEventProperty)(evt.data)
                                GestisciPropertyChange(prop)
                            End If

                        Case MPV_EVENT_LOG_MESSAGE
                            If evt.data <> IntPtr.Zero Then
                                Dim logMsg As MpvEventLogMessage = Marshal.PtrToStructure(Of MpvEventLogMessage)(evt.data)
                                Dim testo As String = If(logMsg.text <> IntPtr.Zero, Utf8PtrToString(logMsg.text), String.Empty)
                                Dim livello As String = If(logMsg.level <> IntPtr.Zero, Utf8PtrToString(logMsg.level), String.Empty)
                                ' Logghiamo solo errori gravi, filtrando rumore comune
                                If Not String.IsNullOrEmpty(testo) AndAlso
                                   (livello = "error" OrElse livello = "fatal") AndAlso
                                   Not testo.IndexOf("AVOption", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                                   Not testo.IndexOf("deprecated", StringComparison.OrdinalIgnoreCase) >= 0 Then
                                    RaiseEvent MessaggioLog(Me, New LogMessageEventArgs(testo, livello))
                                End If
                            End If

                        Case MPV_EVENT_SHUTDOWN
                            _inArresto = True
                            RaiseEvent Spegnimento(Me, EventArgs.Empty)
                    End Select
                Catch exInner As Exception
                    log.ScriviLog("Eccezione interna CicloEventi: " & exInner.Message)
                End Try
            End While
        Catch ex As Exception
            log.ScriviLog("Eccezione esterna CicloEventi: " & ex.Message & " " & ex.StackTrace)
        End Try
    End Sub

    Private Sub GestisciPropertyChange(prop As MpvEventProperty)
        Try
            If prop.name = IntPtr.Zero Then Return
            Dim nome As String = Utf8PtrToString(prop.name)
            If String.IsNullOrEmpty(nome) Then Return

            Dim valore As Object = Nothing
            If prop.format = MPV_FORMAT_DOUBLE AndAlso prop.data <> IntPtr.Zero Then
                valore = Marshal.PtrToStructure(Of Double)(prop.data)
            ElseIf prop.format = MPV_FORMAT_FLAG AndAlso prop.data <> IntPtr.Zero Then
                valore = Marshal.ReadByte(prop.data) <> 0
            End If

            Select Case nome
                Case "time-pos"
                    Dim nuovaPosizione As Double = If(valore IsNot Nothing, CDbl(valore), 0)
                    If _modalitaLive AndAlso Not _inPausa Then
                        If Math.Abs(nuovaPosizione - _ultimaPosizioneOsservata) > 0.05 Then
                            _ultimaPosizioneOsservata = nuovaPosizione
                            _orarioUltimoAvanzamento = DateTime.Now
                        End If
                    Else
                        _orarioUltimoAvanzamento = DateTime.Now
                    End If
                    _posizioneTemporale = nuovaPosizione
                Case "duration"
                    _durata = If(valore IsNot Nothing, CDbl(valore), 0)
                Case "volume"
                    ' Il volume dell'utente resta quello scelto anche mentre la musica è abbassata per la sintesi.
                    Dim volumeMpv As Double = If(valore IsNot Nothing, CDbl(valore), 100)
                    If _attenuazione > 0 AndAlso _attenuazione < 1 Then
                        If Math.Abs(volumeMpv - _volume * _attenuazione) > 0.5 Then _volume = Math.Min(100, volumeMpv / _attenuazione)
                    Else
                        _volume = volumeMpv
                    End If
                Case "speed"
                    _velocita = If(valore IsNot Nothing, CDbl(valore), 1.0)
                Case "pause"
                    _inPausa = If(valore IsNot Nothing, CBool(valore), False)
                Case "demuxer-cache-duration"
                    _durataBufferCorrente = If(valore IsNot Nothing, CDbl(valore), 0)
                    ValutaAdattamentoQualita()
                Case "cache-speed"
                    AggiornaStimaBanda(If(valore IsNot Nothing, CDbl(valore), 0))
            End Select

            RaiseEvent CambioProprieta(Me, New PropertyChangedEventArgs(nome, valore))
        Catch ex As Exception
            log.ScriviLog("Eccezione in GestisciPropertyChange: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiornaStimaBanda(velocitaBytesSec As Double)
        Try
            If String.IsNullOrEmpty(_urlYoutubeCorrente) Then Return
            If _cambioQualitaInCorso Then Return
            If velocitaBytesSec <= 0 Then Return
            If _durataBufferCorrente >= SOGLIA_BUFFER_PIENO Then Return

            Dim velocitaBps As Double = velocitaBytesSec * 8.0
            If _campioniBanda = 0 Then
                _bandaStimata = velocitaBps
            Else
                _bandaStimata = _bandaStimata * 0.7 + velocitaBps * 0.3
            End If
            _campioniBanda += 1
        Catch ex As Exception
            log.ScriviLog("Eccezione in AggiornaStimaBanda: " & ex.Message)
        End Try
    End Sub

    Private Function QualitaMassimaSostenibile() As QualitaVideo
        Try
            If _campioniBanda < CAMPIONI_BANDA_MINIMI OrElse _bandaStimata <= 0 Then
                Return _qualitaCorrente
            End If

            For q As Integer = CInt(QUALITA_MASSIMA_ADATTIVA) To CInt(QUALITA_MINIMA)
                If _bandaStimata >= BitrateStimatiBps(q) * MARGINE_BANDA Then
                    Return CType(q, QualitaVideo)
                End If
            Next
            Return QUALITA_MINIMA
        Catch ex As Exception
            log.ScriviLog("Eccezione in QualitaMassimaSostenibile: " & ex.Message)
            Return _qualitaCorrente
        End Try
    End Function

    Private Shared Function IndiceQualita(qualita As QualitaVideo) As Integer
        Try
            Dim indice As Integer = CInt(qualita)
            If indice < 0 Then indice = 0
            If indice > NomiQualita.Length - 1 Then indice = NomiQualita.Length - 1
            Return indice
        Catch ex As Exception
            log.ScriviLog("Eccezione in IndiceQualita: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function VideoInteramenteBufferizzato() As Boolean
        Try
            If _durata <= 0 Then Return False
            Return _durataBufferCorrente >= (_durata - _posizioneTemporale - 1.0)
        Catch ex As Exception
            log.ScriviLog("Eccezione in VideoInteramenteBufferizzato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ControllaFaseAvvio()
        Try
            If Not _faseAvvioInCorso Then Return
            If String.IsNullOrEmpty(_urlYoutubeCorrente) Then
                _faseAvvioInCorso = False
                _faseAttesaPreroll = False
                Return
            End If

            Dim trascorsi As Double = (DateTime.Now - _orarioInizioFaseAvvio).TotalSeconds

            If _faseAttesaPreroll Then
                If _durataBufferCorrente >= SECONDI_PREROLL_FINALE OrElse VideoInteramenteBufferizzato() OrElse trascorsi >= SECONDI_TIMEOUT_PREROLL Then
                    AvviaRiproduzioneDefinitiva()
                End If
                Return
            End If

            Dim bufferPieno As Boolean = _durataBufferCorrente >= SECONDI_BUFFER_MISURA
            Dim videoCompleto As Boolean = VideoInteramenteBufferizzato()
            Dim scaduto As Boolean = trascorsi >= SECONDI_TIMEOUT_MISURA

            If bufferPieno OrElse videoCompleto OrElse scaduto Then
                DecidiQualitaDefinitiva(trascorsi)
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in ControllaFaseAvvio: " & ex.Message)
        End Try
    End Sub

    Private Sub ControllaStalloLive()
        Try
            If Not _modalitaLive Then Return
            If _inPausa Then Return
            If String.IsNullOrEmpty(_urlStreamTv) Then Return
            If _faseAvvioInCorso Then Return
            If _riconnessioneInCorso Then Return
            If _orarioUltimoAvanzamento = DateTime.MinValue Then Return

            Dim secondiFermo As Double = (DateTime.Now - _orarioUltimoAvanzamento).TotalSeconds
            If secondiFermo >= SECONDI_STALLO_MASSIMO Then
                _orarioUltimoAvanzamento = DateTime.Now
                ProgrammaRiconnessioneStream("nessun avanzamento da " & secondiFermo.ToString("F0") & " secondi")
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in ControllaStalloLive: " & ex.Message)
        End Try
    End Sub

    Private Sub DecidiQualitaDefinitiva(secondiMisura As Double)
        Try
            Dim target As QualitaVideo = _qualitaCorrente
            If _qualitaAdattivaAbilitata Then
                target = QualitaMassimaSostenibile()
            End If

            RaiseEvent AnalisiConnessioneCompletata(Me, New AnalisiConnessioneEventArgs(_bandaStimata / 1000000.0, secondiMisura, NomiQualita(IndiceQualita(target))))

            If CInt(target) <> CInt(_qualitaCorrente) Then
                Dim vecchia As QualitaVideo = _qualitaCorrente
                _qualitaCorrente = target
                _faseAttesaPreroll = True
                _orarioInizioFaseAvvio = DateTime.Now
                ImpostaProprietaStringa("ytdl-format", FormatiAdattivi(IndiceQualita(target)))
                EseguiComando("loadfile", _urlYoutubeCorrente)
                RaiseEvent CambioQualita(Me, New CambioQualitaEventArgs(NomiQualita(IndiceQualita(vecchia)), NomiQualita(IndiceQualita(target)), _durataBufferCorrente))
            Else
                AvviaRiproduzioneDefinitiva()
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in DecidiQualitaDefinitiva: " & ex.Message)
            AvviaRiproduzioneDefinitiva()
        End Try
    End Sub

    Private Sub AvviaRiproduzioneDefinitiva()
        Try
            _faseAvvioInCorso = False
            _faseAttesaPreroll = False
            _orarioAvvioRiproduzione = DateTime.Now
            ImpostaProprietaStringa("pause", "no")
        Catch ex As Exception
            log.ScriviLog("Eccezione in AvviaRiproduzioneDefinitiva: " & ex.Message)
        End Try
    End Sub

    Private Sub ValutaAdattamentoQualita()
        Try
            If _modalitaLive Then Return
            If _faseAvvioInCorso Then Return
            If Not _qualitaAdattivaAbilitata Then Return
            If _cambioQualitaInCorso Then Return
            If String.IsNullOrEmpty(_urlYoutubeCorrente) Then Return

            If (DateTime.Now - _orarioAvvioRiproduzione).TotalSeconds < SECONDI_GRAZIA_AVVIO Then Return
            If (DateTime.Now - _ultimaValutazione).TotalSeconds < SECONDI_TRA_VALUTAZIONI Then Return
            _ultimaValutazione = DateTime.Now

            If _durataBufferCorrente < SOGLIA_BUFFER_DOWNGRADE AndAlso Not _inPausa AndAlso Not VideoInteramenteBufferizzato() Then
                Dim secondiDaUltimoCambio As Double = (DateTime.Now - _ultimoCambioQualita).TotalSeconds
                If secondiDaUltimoCambio < SECONDI_COOLDOWN_DOWNGRADE Then Return

                _contatoreSottoSoglia += 1
                If _contatoreSottoSoglia >= TENTATIVI_DOWNGRADE Then
                    _contatoreSottoSoglia = 0
                    AbbassaQualita()
                End If
            Else
                _contatoreSottoSoglia = 0
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in ValutaAdattamentoQualita: " & ex.Message)
        End Try
    End Sub

    Private Sub AbbassaQualita()
        Try
            If _qualitaCorrente >= QUALITA_MINIMA Then Return
            Dim vecchia As QualitaVideo = _qualitaCorrente
            _qualitaCorrente = CType(CInt(_qualitaCorrente) + 1, QualitaVideo)
            CambiaQualitaMantenendoPosizione(vecchia, _qualitaCorrente)
        Catch ex As Exception
            log.ScriviLog("Eccezione in AbbassaQualita: " & ex.Message)
        End Try
    End Sub

    Private Sub CambiaQualitaMantenendoPosizione(vecchia As QualitaVideo, nuova As QualitaVideo)
        Try
            _cambioQualitaInCorso = True
            _ultimoCambioQualita = DateTime.Now
            _orarioAvvioRiproduzione = DateTime.Now
            _posizioneRipristino = _posizioneTemporale
            _contatoreSottoSoglia = 0
            _bandaStimata = 0
            _campioniBanda = 0
            ImpostaProprietaStringa("ytdl-format", FormatiAdattivi(IndiceQualita(nuova)))
            EseguiComando("loadfile", _urlYoutubeCorrente)
            RaiseEvent CambioQualita(Me, New CambioQualitaEventArgs(NomiQualita(IndiceQualita(vecchia)), NomiQualita(IndiceQualita(nuova)), _durataBufferCorrente))
        Catch ex As Exception
            _cambioQualitaInCorso = False
            _posizioneRipristino = -1
            log.ScriviLog("Eccezione in CambiaQualitaMantenendoPosizione: " & ex.Message)
        End Try
    End Sub

    Private Sub OsservaProprietaInterna(nome As String, formato As Integer)
        Try
            If _handle = IntPtr.Zero Then Return
            mpv_observe_property(_handle, 0, nome, formato)
        Catch ex As Exception
            log.ScriviLog("Eccezione in OsservaProprietaInterna: " & ex.Message)
        End Try
    End Sub

    Private Sub EseguiComando(ParamArray args As String())
        Try
            If _handle = IntPtr.Zero Then Return
            If args Is Nothing OrElse args.Length = 0 Then Return

            Dim ptrs As IntPtr() = New IntPtr(args.Length - 1) {}
            For i As Integer = 0 To args.Length - 1
                ptrs(i) = StringToUtf8Ptr(If(args(i), String.Empty))
            Next

            Dim argArray As IntPtr = Marshal.AllocHGlobal((args.Length + 1) * IntPtr.Size)
            Try
                For i As Integer = 0 To args.Length - 1
                    Marshal.WriteIntPtr(argArray, i * IntPtr.Size, ptrs(i))
                Next
                Marshal.WriteIntPtr(argArray, args.Length * IntPtr.Size, IntPtr.Zero)
                mpv_command(_handle, argArray)
            Finally
                Marshal.FreeHGlobal(argArray)
                For Each p As IntPtr In ptrs
                    FreeUtf8Ptr(p)
                Next
            End Try
        Catch ex As Exception
            log.ScriviLog("Eccezione in EseguiComando: " & ex.Message)
        End Try
    End Sub

    Private Function AssicuraFormSottofondo() As Boolean
        Try
            If _formSottofondo IsNot Nothing AndAlso Not _formSottofondo.IsDisposed Then Return True

            Dim formPrincipale As Form = Nothing
            If Application.OpenForms.Count > 0 Then formPrincipale = Application.OpenForms(0)

            If formPrincipale IsNot Nothing AndAlso Not formPrincipale.IsDisposed AndAlso
               formPrincipale.IsHandleCreated AndAlso formPrincipale.InvokeRequired Then
                Dim risultato As Boolean = False
                formPrincipale.Invoke(New Action(Sub()
                                                     risultato = CreaFormSottofondo()
                                                 End Sub))
                Return risultato
            End If

            Return CreaFormSottofondo()
        Catch ex As Exception
            log.ScriviLog("Eccezione in AssicuraFormSottofondo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function CreaFormSottofondo() As Boolean
        Try
            If _formSottofondo IsNot Nothing AndAlso Not _formSottofondo.IsDisposed Then
                Try
                    _formSottofondo.Dispose()
                Catch exVecchia As Exception
                    log.ScriviLog("Eccezione chiusura vecchia form di sottofondo: " & exVecchia.Message)
                End Try
            End If
            _formSottofondo = New Form()
            _formSottofondo.WindowState = FormWindowState.Minimized
            _formSottofondo.ShowInTaskbar = False
            _formSottofondo.FormBorderStyle = FormBorderStyle.None
            _formSottofondo.Opacity = 0
            _formSottofondo.CreateControl()
            Return True
        Catch ex As Exception
            log.ScriviLog("Eccezione in CreaFormSottofondo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub ContinuaInSottofondo()
        Try
            If Not AssicuraFormSottofondo() Then
                Return
            End If
            If _formSottofondo Is Nothing OrElse _formSottofondo.IsDisposed Then
                Return
            End If
            ImpostaProprietaStringa("wid", _formSottofondo.Handle.ToInt64().ToString())
        Catch ex As Exception
            log.ScriviLog("Eccezione in ContinuaInSottofondo: " & ex.Message)
        End Try
    End Sub

    Public Sub AllegaAFinestra(handle As IntPtr)
        Try
            ScollegaFormOspiteCorrente()
            If handle = IntPtr.Zero Then
                ContinuaInSottofondo()
            Else
                ImpostaProprietaStringa("wid", handle.ToInt64().ToString())
            End If
            ImpostaProprietaStringa("vo", "gpu")
        Catch ex As Exception
            log.ScriviLog("Eccezione in AllegaAFinestra(IntPtr): " & ex.Message)
        End Try
    End Sub

    Public Sub AllegaAFinestra(formOspite As Form)
        Try
            If formOspite Is Nothing OrElse formOspite.IsDisposed Then Return
            ScollegaFormOspiteCorrente()
            If formOspite.IsDisposed Then Return
            _formOspite = formOspite
            AddHandler _formOspite.FormClosing, AddressOf FormOspiteChiude
            AddHandler _formOspite.Disposed, AddressOf FormOspiteDisposed
            ImpostaProprietaStringa("wid", _formOspite.Handle.ToInt64().ToString())
            ImpostaProprietaStringa("vo", "gpu")
        Catch ex As Exception
            log.ScriviLog("Eccezione in AllegaAFinestra(Form): " & ex.Message)
        End Try
    End Sub

    Private Sub FormOspiteChiude(sender As Object, e As FormClosingEventArgs)
        Try
            ContinuaInSottofondo()
        Catch ex As Exception
            log.ScriviLog("Eccezione in FormOspiteChiude: " & ex.Message)
        End Try
    End Sub

    Private Sub FormOspiteDisposed(sender As Object, e As EventArgs)
        Try
            ScollegaFormOspiteCorrente()
        Catch ex As Exception
            log.ScriviLog("Eccezione in FormOspiteDisposed: " & ex.Message)
        End Try
    End Sub

    Private Sub ScollegaFormOspiteCorrente()
        Try
            If _formOspite IsNot Nothing Then
                RemoveHandler _formOspite.FormClosing, AddressOf FormOspiteChiude
                RemoveHandler _formOspite.Disposed, AddressOf FormOspiteDisposed
                _formOspite = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in ScollegaFormOspiteCorrente: " & ex.Message)
        End Try
    End Sub

    Public Shared Function EFileOneDriveSoloOnline(percorso As String) As Boolean
        Try
            If String.IsNullOrEmpty(percorso) Then Return False
            If EPercorsoDiRete(percorso) Then Return False
            If Not File.Exists(percorso) Then Return False
            Dim attrs As UInteger = GetFileAttributes(percorso)
            If attrs = UInteger.MaxValue Then Return False
            If (attrs And FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) <> 0 Then Return True
            If (attrs And FILE_ATTRIBUTE_RECALL_ON_OPEN) <> 0 Then Return True
            If (attrs And FILE_ATTRIBUTE_OFFLINE) <> 0 Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("Eccezione in EFileOneDriveSoloOnline: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub IdrataOneDriveEAvvia(percorso As String)
        Dim handleProtetto As IntPtr = IntPtr.Zero
        Try
            RaiseEvent ProgressoDownloadOneDrive(Me, New ProgressoOneDriveEventArgs(percorso, 0, False, False))

            Dim hr As Integer = CfOpenFileWithOplock(percorso, CF_OPEN_FILE_FLAG_NONE, handleProtetto)
            If hr <> S_OK Then
                RaiseEvent ProgressoDownloadOneDrive(Me, New ProgressoOneDriveEventArgs(percorso, 0, True, False))
                Return
            End If

            Dim win32Handle As IntPtr = CfGetWin32HandleFromProtectedHandle(handleProtetto)
            Dim dimensioneTotale As Long = 0
            If win32Handle <> IntPtr.Zero Then
                GetFileSizeEx(win32Handle, dimensioneTotale)
            End If

            Dim threadIdratazione As New Thread(Sub()
                                                    Try
                                                        CfHydratePlaceholder(handleProtetto, 0L, CF_EOF, CF_HYDRATE_FLAG_NONE, IntPtr.Zero)
                                                    Catch exIdr As Exception
                                                        log.ScriviLog("Eccezione thread idratazione cfapi: " & exIdr.Message)
                                                    End Try
                                                End Sub)
            threadIdratazione.IsBackground = True
            threadIdratazione.Start()

            Do While threadIdratazione.IsAlive
                If _annullaDownloadOneDrive Then
                    Try
                        threadIdratazione.Abort()
                    Catch
                    End Try
                    threadIdratazione.Join(2000)
                    RaiseEvent ProgressoDownloadOneDrive(Me, New ProgressoOneDriveEventArgs(percorso, 0, True, False))
                    Return
                End If

                Dim percentuale As Integer = 0
                If dimensioneTotale > 0 AndAlso win32Handle <> IntPtr.Zero Then
                    Try
                        Dim info As New FileInfo(percorso)
                        Dim scaricati As Long = info.Length
                        If scaricati > 0 AndAlso scaricati <= dimensioneTotale Then
                            percentuale = CInt(scaricati * 100L \ dimensioneTotale)
                        End If
                    Catch
                    End Try
                End If

                RaiseEvent ProgressoDownloadOneDrive(Me, New ProgressoOneDriveEventArgs(percorso, percentuale, False, False))
                Thread.Sleep(INTERVALLO_PROGRESSO_MS)
            Loop

            RaiseEvent ProgressoDownloadOneDrive(Me, New ProgressoOneDriveEventArgs(percorso, 100, False, True))

            If _annullaDownloadOneDrive Then Return
            If _inArresto Then Return

            _downloadOneDriveInCorso = False
            EseguiComando("loadfile", percorso)
        Catch ex As Exception
            log.ScriviLog("Eccezione in IdrataOneDriveEAvvia: " & ex.Message)
            RaiseEvent ProgressoDownloadOneDrive(Me, New ProgressoOneDriveEventArgs(percorso, 0, True, False))
        Finally
            If handleProtetto <> IntPtr.Zero Then
                Try
                    CfCloseHandle(handleProtetto)
                Catch exClose As Exception
                    log.ScriviLog("Eccezione CfCloseHandle: " & exClose.Message)
                End Try
            End If
            _downloadOneDriveInCorso = False
        End Try
    End Sub

    Public Sub ImpostaRadioCorrente(url As String, attiva As Boolean)
        Try
            _radioCorrente = attiva
            If attiva Then
                _urlRadioCorrente = If(url, String.Empty)
            Else
                _urlRadioCorrente = String.Empty
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in ImpostaRadioCorrente: " & ex.Message)
        End Try
    End Sub

    Public Sub CaricaFile(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            ResetModalitaLive()
            _urlYoutubeCorrente = String.Empty
            _qualitaCorrente = QUALITA_AVVIO
            _contatoreSottoSoglia = 0
            _bandaStimata = 0
            _campioniBanda = 0
            _faseAvvioInCorso = False
            _faseAttesaPreroll = False
            ApplicaOpzioniVod()
            ImpostaProprietaStringa("pause", "no")

            If EFileOneDriveSoloOnline(percorso) Then
                If _downloadOneDriveInCorso Then
                    Return
                End If
                _downloadOneDriveInCorso = True
                _annullaDownloadOneDrive = False

                If _threadDownloadOneDrive IsNot Nothing AndAlso _threadDownloadOneDrive.IsAlive Then
                    Try
                        _threadDownloadOneDrive.Abort()
                    Catch
                    End Try
                    _threadDownloadOneDrive.Join(2000)
                End If

                _threadDownloadOneDrive = New Thread(Sub() IdrataOneDriveEAvvia(percorso))
                _threadDownloadOneDrive.IsBackground = True
                _threadDownloadOneDrive.Start()
            Else
                EseguiComando("loadfile", percorso, "replace")
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in CaricaFile: " & ex.Message)
        End Try
    End Sub

    Public Sub RiproduciVideoYoutube(url As String)
        Try
            If String.IsNullOrWhiteSpace(url) Then Return
            If Not url.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase) Then Return
            ResetModalitaLive()
            _urlYoutubeCorrente = url.Trim()
            _tentativiVideoOndemand = 0
            _qualitaCorrente = QUALITA_AVVIO
            _contatoreSottoSoglia = 0
            _ultimoCambioQualita = DateTime.MinValue
            _cambioQualitaInCorso = False
            _posizioneRipristino = -1
            _bandaStimata = 0
            _campioniBanda = 0
            _orarioAvvioRiproduzione = DateTime.Now
            _faseAvvioInCorso = True
            _faseAttesaPreroll = False
            _orarioInizioFaseAvvio = DateTime.Now
            ApplicaOpzioniVod()
            ImpostaProprietaStringa("pause", "yes")
            ImpostaProprietaStringa("ytdl-format", FormatiAdattivi(IndiceQualita(_qualitaCorrente)))
            EseguiComando("loadfile", _urlYoutubeCorrente)
        Catch ex As Exception
            log.ScriviLog("Eccezione in RiproduciVideoYoutube: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Riproduce uno stream TV / IPTV (HLS, MPEG-TS, HTTP, RTMP, RTSP, DASH, ISM, ecc.)
    ''' con opzioni ottimizzate per stabilità. Rileva automaticamente RAI e Mediaset.
    ''' </summary>
    Public Sub RiproduciStreamTv(url As String, Optional userAgent As String = Nothing, Optional referer As String = Nothing, Optional headersExtra As String = Nothing, Optional bassaLatenza As Boolean = False)
        Try
            If String.IsNullOrWhiteSpace(url) Then Return
            Dim urlPulito As String = url.Trim()

            _riconnessioneInCorso = False
            _tentativiRiconnessione = 0

            _modalitaLive = True
            _urlYoutubeCorrente = String.Empty
            _faseAvvioInCorso = False
            _faseAttesaPreroll = False
            _cambioQualitaInCorso = False
            _posizioneRipristino = -1
            _bassaLatenza = bassaLatenza
            _ultimaPosizioneOsservata = -1
            _orarioUltimoAvanzamento = DateTime.Now

            ' Auto-configurazione per emittenti italiane
            Dim profilo = RilevaProfiloEmittente(urlPulito)

            If profilo.IsRai AndAlso Not RaiDisponibile() Then
                Dim residui As Integer = RaiSecondiResiduiCooldown()
                RaiseEvent CircuitoRaiSospeso(Me, New CircuitoRaiEventArgs(residui, "circuito aperto"))
                Return
            End If

            urlPulito = profilo.UrlOttimizzato

            If Not String.IsNullOrWhiteSpace(userAgent) Then
                _userAgentTv = userAgent.Trim()
            Else
                _userAgentTv = profilo.UserAgent
            End If

            If Not String.IsNullOrWhiteSpace(referer) Then
                _refererTv = referer.Trim()
            Else
                _refererTv = profilo.Referer
            End If

            If Not String.IsNullOrWhiteSpace(headersExtra) Then
                _headersExtraTv = headersExtra.Trim()
            Else
                _headersExtraTv = profilo.HeadersExtra
            End If

            _urlStreamTv = urlPulito
            _urlOriginaleTv = url.Trim() ' per refresh relinker RAI

            ApplicaOpzioniLive(bassaLatenza)
            If profilo.IsRai OrElse profilo.IsMediaset Then
                ApplicaOpzioniEmittenteItaliana(profilo)
            End If
            ApplicaHeadersHttp()

            ImpostaProprietaStringa("mute", "no")
            ImpostaProprietaStringa("volume", VolumeEffettivo().ToString(CultureInfo.InvariantCulture))
            ImpostaProprietaStringa("aid", "auto")
            ImpostaProprietaStringa("pause", "no")
            ' "replace" interrompe il media corrente in modo sincrono rispetto a uno "stop" separato
            EseguiComando("loadfile", _urlStreamTv, "replace")
            _orarioAvvioRiproduzione = DateTime.Now
        Catch ex As Exception
            log.ScriviLog("Eccezione in RiproduciStreamTv: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Alias di RiproduciStreamTv.</summary>
    Public Sub RiproduciCanaleTv(url As String, Optional userAgent As String = Nothing, Optional referer As String = Nothing, Optional headersExtra As String = Nothing, Optional bassaLatenza As Boolean = False)
        Try
            RiproduciStreamTv(url, userAgent, referer, headersExtra, bassaLatenza)
        Catch ex As Exception
            log.ScriviLog("Eccezione in RiproduciCanaleTv: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Avvia un canale RAI noto per ID cont (relinker).
    ''' Esempi: Rai1=2606803, Rai2=308718, Rai3=308709, RaiNews=1, RaiSport=358025
    ''' </summary>
    Public Sub RiproduciRai(contId As String, Optional forzaHd As Boolean = True)
        Try
            If String.IsNullOrWhiteSpace(contId) Then Return
            Dim uaForce As String = If(forzaHd, RAI_FORCE_USERAGENT_VARIANTI(_raiIndiceVariante), "hbbtv")
            Dim url As String = "https://mediapolis.rai.it/relinker/relinkerServlet.htm?cont=" & contId.Trim() &
                                "&output=7&forceUserAgent=" & uaForce
            RiproduciStreamTv(url, bassaLatenza:=False)
        Catch ex As Exception
            log.ScriviLog("Eccezione in RiproduciRai: " & ex.Message)
        End Try
    End Sub

    Private Sub RuotaVarianteForceUserAgentRai()
        Try
            _raiIndiceVariante = (_raiIndiceVariante + 1) Mod RAI_FORCE_USERAGENT_VARIANTI.Length
            _raiTentativiConsecutivi503 = 0
        Catch ex As Exception
            log.ScriviLog("Eccezione in RuotaVarianteForceUserAgentRai: " & ex.Message)
        End Try
    End Sub

    Private Function SostituisciForceUserAgentRai(url As String, nuovaVariante As String) As String
        Try
            Dim u = url
            Dim idx As Integer = u.IndexOf("forceUserAgent=", StringComparison.OrdinalIgnoreCase)
            If idx < 0 Then Return u
            Dim fine As Integer = u.IndexOf("&"c, idx)
            Dim prima As String = u.Substring(0, idx)
            Dim dopo As String = If(fine >= 0, u.Substring(fine), String.Empty)
            Return prima & "forceUserAgent=" & nuovaVariante & dopo
        Catch ex As Exception
            log.ScriviLog("Eccezione in SostituisciForceUserAgentRai: " & ex.Message)
            Return url
        End Try
    End Function

    Public Shared Function RaiDisponibile() As Boolean
        Try
            SyncLock _raiLockCircuito
                If _raiCircuitoApertoDal = DateTime.MinValue Then Return True
                Dim trascorsi As Double = (DateTime.Now - _raiCircuitoApertoDal).TotalSeconds
                If trascorsi >= _raiCooldownCorrenteSecondi Then
                    If Not _raiInProvaSemiAperto Then
                        _raiInProvaSemiAperto = True
                        Return True
                    End If
                    Return False
                End If
                Return False
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("Eccezione in RaiDisponibile: " & ex.Message)
            Return True
        End Try
    End Function

    Public Shared Function RaiSecondiResiduiCooldown() As Integer
        Try
            SyncLock _raiLockCircuito
                If _raiCircuitoApertoDal = DateTime.MinValue Then Return 0
                Dim residui As Double = _raiCooldownCorrenteSecondi - (DateTime.Now - _raiCircuitoApertoDal).TotalSeconds
                Return CInt(Math.Max(0, Math.Ceiling(residui)))
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("Eccezione in RaiSecondiResiduiCooldown: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Shared Sub SegnalaEsitoRai(successo As Boolean, Optional motivo As String = "")
        Try
            SyncLock _raiLockCircuito
                If successo Then
                    _raiFallimentiRecenti = 0
                    _raiPrimoFallimentoFinestra = DateTime.MinValue
                    _raiCircuitoApertoDal = DateTime.MinValue
                    _raiCooldownCorrenteSecondi = RAI_COOLDOWN_MINIMO_SECONDI
                    _raiInProvaSemiAperto = False
                    Return
                End If

                If _raiInProvaSemiAperto Then
                    _raiInProvaSemiAperto = False
                    _raiCircuitoApertoDal = DateTime.Now
                    _raiCooldownCorrenteSecondi = Math.Min(RAI_COOLDOWN_MASSIMO_SECONDI, _raiCooldownCorrenteSecondi * 2)
                    Return
                End If

                If _raiPrimoFallimentoFinestra = DateTime.MinValue OrElse (DateTime.Now - _raiPrimoFallimentoFinestra).TotalSeconds > RAI_FINESTRA_FALLIMENTI_SECONDI Then
                    _raiPrimoFallimentoFinestra = DateTime.Now
                    _raiFallimentiRecenti = 1
                Else
                    _raiFallimentiRecenti += 1
                End If

                If _raiFallimentiRecenti >= RAI_SOGLIA_FALLIMENTI_CIRCUITO AndAlso _raiCircuitoApertoDal = DateTime.MinValue Then
                    _raiCircuitoApertoDal = DateTime.Now
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("Eccezione in SegnalaEsitoRai: " & ex.Message)
        End Try
    End Sub

    Public Sub RiproduciMediaset(channelCode As String)
        Try
            If String.IsNullOrWhiteSpace(channelCode) Then Return
            Dim code As String = channelCode.Trim().ToLowerInvariant()
            Dim url As String = "https://live02-seg.msf.cdn.mediaset.net/live/ch-" & code & "/" & code & "-clr.isml/index.m3u8"
            RiproduciStreamTv(url,
                userAgent:="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                referer:="https://www.mediasetplay.mediaset.it/")
        Catch ex As Exception
            log.ScriviLog("Eccezione in RiproduciMediaset: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Carica una playlist M3U/M3U8 di canali e avvia dal primo (o da indice specificato).
    ''' </summary>
    Public Sub RiproduciPlaylistTv(urlPlaylist As String, Optional indiceIniziale As Integer = 0, Optional userAgent As String = Nothing, Optional referer As String = Nothing)
        Try
            If String.IsNullOrWhiteSpace(urlPlaylist) Then Return
            _modalitaLive = True
            _urlYoutubeCorrente = String.Empty
            _faseAvvioInCorso = False
            _urlStreamTv = urlPlaylist.Trim()
            _ultimaPosizioneOsservata = -1
            _orarioUltimoAvanzamento = DateTime.Now
            If Not String.IsNullOrWhiteSpace(userAgent) Then _userAgentTv = userAgent.Trim()
            _refererTv = If(referer, String.Empty).Trim()

            ApplicaOpzioniLive(False)
            ApplicaHeadersHttp()
            ImpostaProprietaStringa("loop-playlist", "inf")
            ImpostaProprietaStringa("pause", "no")
            EseguiComando("loadlist", _urlStreamTv)
            If indiceIniziale > 0 Then
                EseguiComando("playlist-play-index", indiceIniziale.ToString(CultureInfo.InvariantCulture))
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in RiproduciPlaylistTv: " & ex.Message)
        End Try
    End Sub

    Public Sub PlaylistSuccessivo()
        Try
            EseguiComando("playlist-next")
        Catch ex As Exception
            log.ScriviLog("Eccezione in PlaylistSuccessivo: " & ex.Message)
        End Try
    End Sub

    Public Sub PlaylistPrecedente()
        Try
            EseguiComando("playlist-prev")
        Catch ex As Exception
            log.ScriviLog("Eccezione in PlaylistPrecedente: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaOpzioniLive(bassaLatenza As Boolean)
        Try
            ' IMPORTANTE per IPTV/live:
            ' - NON usare cache-on-disk (causa "Failed to create file cache" e puo mutare l'audio)
            ' - NON usare cache-pause-initial (blocca l'avvio e spesso lascia l'audio muto)
            ' - Forzare audio (aid/mute/volume)
            ImpostaProprietaStringa("cache-on-disk", "no")

            If bassaLatenza Then
                ImpostaProprietaStringa("cache", "no")
                ImpostaProprietaStringa("demuxer-max-bytes", "8M")
                ImpostaProprietaStringa("demuxer-max-back-bytes", "2M")
                ImpostaProprietaStringa("cache-secs", "3")
                ImpostaProprietaStringa("demuxer-lavf-analyzeduration", "0.5")
                ImpostaProprietaStringa("demuxer-lavf-probesize", "262144")
            Else
                ImpostaProprietaStringa("cache", "yes")
                ImpostaProprietaStringa("demuxer-max-bytes", "40M")
                ImpostaProprietaStringa("demuxer-max-back-bytes", "8M")
                ImpostaProprietaStringa("cache-secs", "12")
                ImpostaProprietaStringa("demuxer-lavf-analyzeduration", "1.5")
                ImpostaProprietaStringa("demuxer-lavf-probesize", "524288")
            End If

            ' Mai mettere in pausa in attesa del buffer sugli stream live
            ImpostaProprietaStringa("cache-pause", "no")
            ImpostaProprietaStringa("cache-pause-initial", "no")

            ' Audio: selezione automatica e volume attivo
            ImpostaProprietaStringa("aid", "auto")
            ImpostaProprietaStringa("mute", "no")
            ImpostaProprietaStringa("volume", VolumeEffettivo().ToString(CultureInfo.InvariantCulture))
            ImpostaProprietaStringa("audio-display", "no")
            ImpostaProprietaStringa("audio-channels", "auto")
            ImpostaProprietaStringa("audio-pitch-correction", "yes")
            ImpostaProprietaStringa("video-sync", "audio")
            ImpostaProprietaStringa("audio-file-auto", "no")
            ImpostaProprietaStringa("gapless-audio", "no")

            ImpostaProprietaStringa("force-seekable", "no")
            ImpostaProprietaStringa("hr-seek", "no")
            ImpostaProprietaStringa("keep-open", "yes")
            ImpostaProprietaStringa("idle", "yes")
            ImpostaProprietaStringa("network-timeout", "20")
            ImpostaProprietaStringa("stream-buffer-size", "1M")
            ImpostaProprietaStringa("hls-bitrate", "max")
            ImpostaProprietaStringa("ytdl", "no")
            ImpostaProprietaStringa("demuxer-thread", "yes")
            ImpostaProprietaStringa("correct-pts", "yes")
            _streamLavfOBase = "reconnect=1,reconnect_streamed=1,reconnect_delay_max=10," &
                "reconnect_on_network_error=1,reconnect_on_http_error=%7%4xx,5xx," &
                "rw_timeout=20000000,multiple_requests=1"
            ApplicaStreamLavfO()
            ImpostaProprietaStringa("demuxer-lavf-o", "fflags=+genpts+discardcorrupt,analyzeduration=1500000,probesize=500000")
            ImpostaProprietaStringa("tls-verify", "no")
        Catch ex As Exception
            log.ScriviLog("Eccezione in ApplicaOpzioniLive: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaOpzioniVod()
        Try
            ImpostaProprietaStringa("cache", "yes")
            ImpostaProprietaStringa("demuxer-max-bytes", "1500M")
            ImpostaProprietaStringa("demuxer-max-back-bytes", "300M")
            ImpostaProprietaStringa("cache-secs", "180")
            ImpostaProprietaStringa("cache-pause", "yes")
            ImpostaProprietaStringa("cache-pause-initial", "yes")
            ImpostaProprietaStringa("cache-pause-wait", "8")
            ImpostaProprietaStringa("force-seekable", "yes")
            ImpostaProprietaStringa("ytdl", "yes")
            ImpostaProprietaStringa("network-timeout", "30")
            _streamLavfOBase = "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5,reconnect_on_network_error=1,reconnect_on_http_error=%7%4xx,5xx"
            ApplicaStreamLavfO()
            ImpostaProprietaStringa("cache-on-disk", "no")
        Catch ex As Exception
            log.ScriviLog("Eccezione in ApplicaOpzioniVod: " & ex.Message)
        End Try
    End Sub

    Private _streamLavfOBase As String = String.Empty

    Private Shared Function CitaValoreLavf(valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return String.Empty
            Dim lunghezza As Integer = System.Text.Encoding.UTF8.GetByteCount(valore)
            Return "%" & lunghezza.ToString(CultureInfo.InvariantCulture) & "%" & valore
        Catch
            Return String.Empty
        End Try
    End Function

    Private Sub ApplicaStreamLavfO()
        Try
            Dim voci As New System.Collections.Generic.List(Of String)()

            If Not String.IsNullOrEmpty(_streamLavfOBase) Then
                voci.Add(_streamLavfOBase)
            End If

            If Not String.IsNullOrEmpty(_userAgentTv) Then
                voci.Add("user_agent=" & CitaValoreLavf(_userAgentTv))
            End If

            Dim intestazioni As New System.Text.StringBuilder()
            If Not String.IsNullOrEmpty(_refererTv) Then
                intestazioni.Append("Referer: ").Append(_refererTv).Append(vbCrLf)
            End If
            If Not String.IsNullOrEmpty(_headersExtraTv) Then
                For Each riga As String In _headersExtraTv.Split({vbCrLf, vbLf, vbCr, "|"c}, StringSplitOptions.RemoveEmptyEntries)
                    Dim t As String = riga.Trim()
                    If t.Length > 0 Then intestazioni.Append(t).Append(vbCrLf)
                Next
            End If

            If intestazioni.Length > 0 Then
                voci.Add("headers=" & CitaValoreLavf(intestazioni.ToString()))
            End If

            If voci.Count = 0 Then Return
            ImpostaProprietaStringa("stream-lavf-o", String.Join(",", voci.ToArray()))
        Catch ex As Exception
            log.ScriviLog("Eccezione in ApplicaStreamLavfO: " & ex.Message)
        End Try
    End Sub

    Private Sub ApplicaHeadersHttp()
        Try
            Dim parti As New System.Collections.Generic.List(Of String)()
            If Not String.IsNullOrEmpty(_userAgentTv) Then
                parti.Add("User-Agent: " & _userAgentTv)
            End If
            If Not String.IsNullOrEmpty(_refererTv) Then
                parti.Add("Referer: " & _refererTv)
            End If
            If Not String.IsNullOrEmpty(_headersExtraTv) Then
                ' Accetta sia "Origin: xxx" sia più righe
                For Each riga In _headersExtraTv.Split({vbCrLf, vbLf, vbCr, "|"c}, StringSplitOptions.RemoveEmptyEntries)
                    Dim t = riga.Trim()
                    If t.Length > 0 Then parti.Add(t)
                Next
            End If
            If parti.Count > 0 Then
                ' mpv vuole LF come separatore
                Dim httpHeader As String = String.Join(vbLf, parti)
                ImpostaProprietaStringa("http-header-fields", httpHeader)
            Else
                ImpostaProprietaStringa("http-header-fields", "")
            End If
            If Not String.IsNullOrEmpty(_userAgentTv) Then
                ImpostaProprietaStringa("user-agent", _userAgentTv)
            End If

            ApplicaStreamLavfO()
        Catch ex As Exception
            log.ScriviLog("Eccezione in ApplicaHeadersHttp: " & ex.Message)
        End Try
    End Sub

    Private Sub ResetModalitaLive()
        Try
            _modalitaLive = False
            _urlStreamTv = String.Empty
            _urlOriginaleTv = String.Empty
            _tentativiRiconnessione = 0
            _riconnessioneInCorso = False
            _bassaLatenza = False
            _isRaiRelinker = False
            _isMediaset = False
            _raiTentativiConsecutivi503 = 0
        Catch ex As Exception
            log.ScriviLog("Eccezione in ResetModalitaLive: " & ex.Message)
        End Try
    End Sub

    Private Function RilevaProfiloEmittente(url As String) As ProfiloEmittente
        Dim p As New ProfiloEmittente With {
            .Nome = "Generico",
            .UrlOttimizzato = url,
            .UserAgent = UA_CHROME,
            .Referer = String.Empty,
            .HeadersExtra = String.Empty,
            .IsRai = False,
            .IsMediaset = False
        }
        Try
            If String.IsNullOrEmpty(url) Then Return p
            Dim u = url.ToLowerInvariant()

            ' --- RAI (relinker mediapolis) ---
            If u.Contains("mediapolis.rai.it") OrElse u.Contains("mediapolisevent.rai.it") OrElse
               u.Contains("rai.it/relinker") OrElse u.Contains("raicdn.it") OrElse
               u.Contains("raiplay") OrElse u.Contains("rainews") OrElse u.Contains("akamaized.net/hls/live") AndAlso u.Contains("rai") Then

                p.Nome = "RAI"
                p.IsRai = True
                p.UserAgent = UA_CHROME
                p.Referer = "https://www.raiplay.it/"

                If u.Contains("relinkerservlet") OrElse u.Contains("relinker") Then
                    _isRaiRelinker = True
                    Dim urlNorm As String = url
                    If Not u.Contains("forceuseragent=") Then
                        If urlNorm.Contains("?") Then
                            urlNorm &= "&forceUserAgent=" & RAI_FORCE_USERAGENT_VARIANTI(_raiIndiceVariante)
                        Else
                            urlNorm &= "?forceUserAgent=" & RAI_FORCE_USERAGENT_VARIANTI(_raiIndiceVariante)
                        End If
                    End If
                    If Not u.Contains("output=") Then
                        urlNorm &= If(urlNorm.Contains("?"), "&", "?") & "output=7"
                    End If
                    If urlNorm.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then
                        urlNorm = "https://" & urlNorm.Substring(7)
                    End If
                    p.UrlOttimizzato = urlNorm
                    p.UserAgent = UA_CHROME
                Else
                    p.UserAgent = UA_CHROME
                End If
                Return p
            End If

            ' --- Mediaset ---
            If u.Contains("mediaset") OrElse u.Contains("msf.cdn.mediaset") OrElse
               u.Contains("msr.cdn.mediaset") OrElse u.Contains("live2.msf") OrElse
               u.Contains("live3-mediaset") OrElse u.Contains("mediasetplay") Then

                p.Nome = "Mediaset"
                p.IsMediaset = True
                _isMediaset = True
                p.UserAgent = UA_CHROME
                p.Referer = "https://www.mediasetplay.mediaset.it/"
                p.HeadersExtra = "Origin: https://www.mediasetplay.mediaset.it"
                Return p
            End If

            ' --- La7 ---
            If u.Contains("la7.it") OrElse u.Contains("la7cdn") Then
                p.Nome = "La7"
                p.UserAgent = UA_CHROME
                p.Referer = "https://www.la7.it/"
                Return p
            End If

            ' --- Generico HLS italiano / altro ---
            If u.Contains(".m3u8") OrElse u.Contains(".isml") OrElse u.Contains(".mpd") Then
                p.UserAgent = UA_CHROME
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in RilevaProfiloEmittente: " & ex.Message)
        End Try
        Return p
    End Function

    Private Sub ApplicaOpzioniEmittenteItaliana(profilo As ProfiloEmittente)
        Try
            If profilo.IsRai Then
                ' RAI: buffer più generoso, playlist refresh più frequente via reconnect
                ImpostaProprietaStringa("cache-secs", "25")
                ImpostaProprietaStringa("demuxer-max-bytes", "60M")
                ImpostaProprietaStringa("demuxer-lavf-analyzeduration", "3")
                ImpostaProprietaStringa("hls-bitrate", "max")
                ' Il relinker cambia token: al reconnect ricarichiamo l'URL originale
                _streamLavfOBase = "reconnect=1,reconnect_streamed=1,reconnect_delay_max=10," &
                    "reconnect_on_network_error=1,reconnect_on_http_error=%7%4xx,5xx," &
                    "rw_timeout=20000000,seekable=0"
                ApplicaStreamLavfO()
            ElseIf profilo.IsMediaset Then
                ' Mediaset ISM/HLS: header + audio track auto
                ImpostaProprietaStringa("cache-secs", "15")
                ImpostaProprietaStringa("demuxer-max-bytes", "40M")
                ImpostaProprietaStringa("hls-bitrate", "max")
                ImpostaProprietaStringa("aid", "auto")
                ImpostaProprietaStringa("mute", "no")
                ImpostaProprietaStringa("demuxer-lavf-o", "fflags=+genpts+discardcorrupt,analyzeduration=1500000,probesize=500000")
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in ApplicaOpzioniEmittenteItaliana: " & ex.Message)
        End Try
    End Sub

    Private Sub ProgrammaRiconnessioneStream(motivo As String)
        Try
            If _inArresto Then Return
            If Not _modalitaLive OrElse String.IsNullOrEmpty(_urlStreamTv) Then Return
            If _riconnessioneInCorso Then Return

            If _isRaiRelinker Then
                SegnalaEsitoRai(False, motivo)
                If Not RaiDisponibile() Then
                    Dim residui As Integer = RaiSecondiResiduiCooldown()
                    RaiseEvent CircuitoRaiSospeso(Me, New CircuitoRaiEventArgs(residui, motivo))
                    RaiseEvent ErroreMedia(Me, EventArgs.Empty)
                    Return
                End If
            End If

            Dim daUltima As Double = (DateTime.Now - _orarioUltimaRiconnessione).TotalSeconds
            If daUltima < SECONDI_COOLDOWN_RICONNESSIONE AndAlso _tentativiRiconnessione > 0 Then Return

            If _tentativiRiconnessione >= _maxTentativiRiconnessione Then
                RaiseEvent ErroreMedia(Me, EventArgs.Empty)
                Return
            End If

            _riconnessioneInCorso = True
            _tentativiRiconnessione += 1
            Dim delayMs As Integer
            If _isRaiRelinker Then
                _raiTentativiConsecutivi503 += 1
                delayMs = Math.Min(30000, SECONDI_COOLDOWN_RAI_503 * 1000 * _tentativiRiconnessione)
            ElseIf _tentativiRiconnessione = 1 Then
                delayMs = 400
            Else
                delayMs = Math.Min(30000, SECONDI_BACKOFF_BASE * 1000 * _tentativiRiconnessione)
            End If

            RaiseEvent RiconnessioneStream(Me, New RiconnessioneStreamEventArgs(_urlStreamTv, _tentativiRiconnessione, _maxTentativiRiconnessione, motivo))

            If _threadRiconnessione IsNot Nothing AndAlso _threadRiconnessione.IsAlive Then
                Try
                    _threadRiconnessione.Abort()
                Catch
                End Try
            End If

            Dim urlDaRicaricare As String = _urlStreamTv
            Dim urlOriginale As String = _urlOriginaleTv
            Dim eraRai As Boolean = _isRaiRelinker
            Dim eraMediaset As Boolean = _isMediaset
            _threadRiconnessione = New Thread(Sub()
                                                  Try
                                                      Thread.Sleep(delayMs)
                                                      If _inArresto OrElse Not _modalitaLive Then Return
                                                      _orarioUltimaRiconnessione = DateTime.Now

                                                      Dim urlFinale As String = urlDaRicaricare
                                                      If eraRai AndAlso Not String.IsNullOrEmpty(urlOriginale) Then
                                                          Dim urlBase As String = urlOriginale
                                                          If _raiTentativiConsecutivi503 >= MAX_TENTATIVI_PRIMA_ROTAZIONE_RAI Then
                                                              RuotaVarianteForceUserAgentRai()
                                                              urlBase = SostituisciForceUserAgentRai(urlOriginale, RAI_FORCE_USERAGENT_VARIANTI(_raiIndiceVariante))
                                                          End If
                                                          Dim profilo = RilevaProfiloEmittente(urlBase)
                                                          urlFinale = profilo.UrlOttimizzato
                                                          _urlStreamTv = urlFinale
                                                          _userAgentTv = profilo.UserAgent
                                                          _refererTv = profilo.Referer
                                                      End If

                                                      ApplicaOpzioniLive(_bassaLatenza)
                                                      If eraRai OrElse eraMediaset Then
                                                          Dim p2 = RilevaProfiloEmittente(urlFinale)
                                                          ApplicaOpzioniEmittenteItaliana(p2)
                                                      End If
                                                      ApplicaHeadersHttp()
                                                      ImpostaProprietaStringa("mute", "no")
                                                      ImpostaProprietaStringa("aid", "auto")
                                                      ImpostaProprietaStringa("pause", "no")
                                                      EseguiComando("loadfile", urlFinale)
                                                  Catch exThread As Exception
                                                      log.ScriviLog("Eccezione thread riconnessione: " & exThread.Message)
                                                  Finally
                                                      ' Senza questo sblocco un tentativo andato a vuoto impedirebbe per sempre i successivi.
                                                      _riconnessioneInCorso = False
                                                  End Try
                                              End Sub)
            _threadRiconnessione.IsBackground = True
            _threadRiconnessione.Start()
        Catch ex As Exception
            _riconnessioneInCorso = False
            log.ScriviLog("Eccezione in ProgrammaRiconnessioneStream: " & ex.Message)
        End Try
    End Sub

    Private Sub ProgrammaRetryVideoOndemand(motivo As String)
        Try
            If _inArresto Then Return
            Dim urlDaRicaricare As String = _urlYoutubeCorrente
            If String.IsNullOrEmpty(urlDaRicaricare) Then Return

            _tentativiVideoOndemand += 1
            Dim tentativoCorrente As Integer = _tentativiVideoOndemand

            If _threadRetryVideoOndemand IsNot Nothing AndAlso _threadRetryVideoOndemand.IsAlive Then
                Try
                    _threadRetryVideoOndemand.Abort()
                Catch
                End Try
            End If

            Dim qualitaDaUsare As QualitaVideo = _qualitaCorrente
            _threadRetryVideoOndemand = New Thread(Sub()
                                                        Try
                                                            Thread.Sleep(MILLISECONDI_RETRY_VIDEO_ONDEMAND * tentativoCorrente)
                                                            If _inArresto Then Return
                                                            If urlDaRicaricare <> _urlYoutubeCorrente Then Return
                                                            ImpostaProprietaStringa("ytdl-format", FormatiAdattivi(IndiceQualita(qualitaDaUsare)))
                                                            ImpostaProprietaStringa("pause", "no")
                                                            EseguiComando("loadfile", urlDaRicaricare, "replace")
                                                        Catch exThread As Exception
                                                            log.ScriviLog("Eccezione thread retry video on-demand: " & exThread.Message)
                                                        End Try
                                                    End Sub)
            _threadRetryVideoOndemand.IsBackground = True
            _threadRetryVideoOndemand.Start()
        Catch ex As Exception
            log.ScriviLog("Eccezione in ProgrammaRetryVideoOndemand: " & ex.Message)
        End Try
    End Sub

    Public Sub Riproduci()
        Try
            If _faseAvvioInCorso Then
                AvviaRiproduzioneDefinitiva()
                Return
            End If
            ImpostaProprietaStringa("pause", "no")
        Catch ex As Exception
            log.ScriviLog("Eccezione in Riproduci: " & ex.Message)
        End Try
    End Sub

    Public Sub Pausa()
        Try
            ImpostaProprietaStringa("pause", "yes")
        Catch ex As Exception
            log.ScriviLog("Eccezione in Pausa: " & ex.Message)
        End Try
    End Sub

    Public Sub FermaRiproduzione()
        Try
            _urlYoutubeCorrente = String.Empty
            _cambioQualitaInCorso = False
            _posizioneRipristino = -1
            _faseAvvioInCorso = False
            _faseAttesaPreroll = False
            _riconnessioneInCorso = False
            _tentativiRiconnessione = 0
            _tentativiVideoOndemand = 0
            ResetModalitaLive()
            EseguiComando("stop")
        Catch ex As Exception
            log.ScriviLog("Eccezione in FermaRiproduzione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Prepara un nuovo caricamento senza inviare "stop" (che in async può cancellare il loadfile successivo).
    ''' Usare prima di RiproduciStreamTv / CaricaFile quando si cambia media.
    ''' </summary>
    Public Sub PreparaNuovoCaricamento()
        Try
            _urlYoutubeCorrente = String.Empty
            _cambioQualitaInCorso = False
            _posizioneRipristino = -1
            _faseAvvioInCorso = False
            _faseAttesaPreroll = False
            _riconnessioneInCorso = False
            _tentativiRiconnessione = 0
            _tentativiVideoOndemand = 0
        Catch ex As Exception
            log.ScriviLog("Eccezione in PreparaNuovoCaricamento: " & ex.Message)
        End Try
    End Sub

    Public Sub StopPlayback()
        Try
            FermaRiproduzione()
        Catch ex As Exception
            log.ScriviLog("Eccezione in StopPlayback: " & ex.Message)
        End Try
    End Sub

    Public Sub AnnullaDownloadOneDrive()
        Try
            If Not _downloadOneDriveInCorso Then Return
            _annullaDownloadOneDrive = True
        Catch ex As Exception
            log.ScriviLog("Eccezione in AnnullaDownloadOneDrive: " & ex.Message)
        End Try
    End Sub

    Public Sub Scorri(secondi As Double)
        Try
            Dim target As Double = _posizioneTemporale + secondi
            target = LimitaEntroDurata(target)
            EseguiComando("seek", target.ToString(CultureInfo.InvariantCulture), "absolute+keyframes")
        Catch ex As Exception
            log.ScriviLog("Eccezione in Scorri: " & ex.Message)
        End Try
    End Sub

    Public Sub Seek(secondi As Double)
        Try
            Scorri(secondi)
        Catch ex As Exception
            log.ScriviLog("Eccezione in Seek: " & ex.Message)
        End Try
    End Sub

    Public Sub ScorriAssoluto(secondi As Double)
        Try
            Dim target As Double = LimitaEntroDurata(secondi)
            EseguiComando("seek", target.ToString(CultureInfo.InvariantCulture), "absolute+keyframes")
        Catch ex As Exception
            log.ScriviLog("Eccezione in ScorriAssoluto: " & ex.Message)
        End Try
    End Sub

    Private Function LimitaEntroDurata(secondi As Double) As Double
        Try
            Const margineSicurezza As Double = 0.5
            Dim risultato As Double = secondi
            If risultato < 0 Then risultato = 0

            If _durata > 0 Then
                Dim limiteMassimo As Double = Math.Max(0, _durata - margineSicurezza)
                If risultato > limiteMassimo Then risultato = limiteMassimo
            End If

            Return risultato
        Catch ex As Exception
            log.ScriviLog("Eccezione in LimitaEntroDurata: " & ex.Message)
            Return Math.Max(0, secondi)
        End Try
    End Function

    Public Sub ImpostaOpzioneStringa(nome As String, valore As String)
        Try
            If String.IsNullOrEmpty(nome) Then Return
            If _handle = IntPtr.Zero Then Return
            mpv_set_option_string(_handle, nome, If(valore, String.Empty))
        Catch ex As Exception
            log.ScriviLog("Eccezione in ImpostaOpzioneStringa: " & ex.Message)
        End Try
    End Sub

    Public Sub ImpostaProprietaStringa(nome As String, valore As String)
        Try
            If String.IsNullOrEmpty(nome) Then Return
            If _handle = IntPtr.Zero Then Return
            mpv_set_property_string(_handle, nome, If(valore, String.Empty))
        Catch ex As Exception
            log.ScriviLog("Eccezione in ImpostaProprietaStringa: " & ex.Message)
        End Try
    End Sub

    Public Sub AlternaRiproduzione()
        Try
            If _inPausa Then Riproduci() Else Pausa()
        Catch ex As Exception
            log.ScriviLog("Eccezione in AlternaRiproduzione: " & ex.Message)
        End Try
    End Sub

    Public Sub AlternaMuto()
        Try
            Dim muteAttuale As String = OttieniProprietaStringa("mute")
            ImpostaProprietaStringa("mute", If(muteAttuale = "yes", "no", "yes"))
        Catch ex As Exception
            log.ScriviLog("Eccezione in AlternaMuto: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniProprietaStringa(nome As String) As String
        Try
            If String.IsNullOrEmpty(nome) Then Return String.Empty
            If _handle = IntPtr.Zero Then Return String.Empty
            Dim ptr As IntPtr = mpv_get_property_string(_handle, nome)
            If ptr <> IntPtr.Zero Then
                Dim result As String = Utf8PtrToString(ptr)
                mpv_free(ptr)
                Return If(result, String.Empty)
            End If
            Return String.Empty
        Catch ex As Exception
            log.ScriviLog("Eccezione in OttieniProprietaStringa: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Public Sub ImpostaQualitaManuale(qualita As QualitaVideo)
        Try
            If String.IsNullOrEmpty(_urlYoutubeCorrente) Then Return
            qualita = CType(IndiceQualita(qualita), QualitaVideo)
            Dim vecchia As QualitaVideo = _qualitaCorrente
            _qualitaCorrente = qualita
            _qualitaAdattivaAbilitata = False
            If _faseAvvioInCorso Then
                _faseAvvioInCorso = False
                _faseAttesaPreroll = False
                ImpostaProprietaStringa("pause", "no")
            End If
            CambiaQualitaMantenendoPosizione(vecchia, qualita)
        Catch ex As Exception
            log.ScriviLog("Eccezione in ImpostaQualitaManuale: " & ex.Message)
        End Try
    End Sub

    ' Fattore da 0 a 1 applicato al volume mentre la sintesi vocale parla (1 = nessuna attenuazione).
    Private _attenuazione As Double = 1.0

    ''' <summary>Abbassa (fattore minore di 1) o riporta (1) il volume senza cambiare quello scelto dall'utente.</summary>
    Public Property Attenuazione As Double
        Get
            Return _attenuazione
        End Get
        Set(value As Double)
            Try
                Dim nuovo As Double = Math.Max(0, Math.Min(1, value))
                If Math.Abs(nuovo - _attenuazione) < 0.001 Then Return
                _attenuazione = nuovo
                ImpostaProprietaStringa("volume", VolumeEffettivo().ToString(CultureInfo.InvariantCulture))
            Catch ex As Exception
                log.ScriviLog("Eccezione in Attenuazione Set: " & ex.Message)
            End Try
        End Set
    End Property

    Private Function VolumeEffettivo() As Double
        Return Math.Round(_volume * _attenuazione, 1)
    End Function

    Public Property Volume As Double
        Get
            Return _volume
        End Get
        Set(value As Double)
            Try
                _volume = Math.Max(0, Math.Min(100, value))
                ImpostaProprietaStringa("volume", VolumeEffettivo().ToString(CultureInfo.InvariantCulture))
            Catch ex As Exception
                log.ScriviLog("Eccezione in Volume Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property Velocita As Double
        Get
            Return _velocita
        End Get
        Set(value As Double)
            Try
                _velocita = Math.Max(0.1, value)
                ImpostaProprietaStringa("speed", _velocita.ToString(CultureInfo.InvariantCulture))
            Catch ex As Exception
                log.ScriviLog("Eccezione in Velocita Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public ReadOnly Property PosizioneTemporale As TimeSpan
        Get
            Return SecondiInTempo(_posizioneTemporale)
        End Get
    End Property

    Public ReadOnly Property Durata As TimeSpan
        Get
            Return SecondiInTempo(_durata)
        End Get
    End Property

    Public ReadOnly Property InPausa As Boolean
        Get
            Return _inPausa
        End Get
    End Property

    Public ReadOnly Property IsPaused As Boolean
        Get
            Return _inPausa
        End Get
    End Property

    Public ReadOnly Property DurataBuffer As TimeSpan
        Get
            Return SecondiInTempo(_durataBufferCorrente)
        End Get
    End Property

    Private Shared Function SecondiInTempo(secondi As Double) As TimeSpan
        Try
            ' TimeSpan.FromSeconds solleva eccezione con valori non validi o fuori scala.
            If Double.IsNaN(secondi) OrElse Double.IsInfinity(secondi) OrElse secondi < 0 Then Return TimeSpan.Zero
            If secondi > 86400.0 * 365.0 Then Return TimeSpan.Zero
            Return TimeSpan.FromSeconds(secondi)
        Catch ex As Exception
            log.ScriviLog("Eccezione in SecondiInTempo: " & ex.Message)
            Return TimeSpan.Zero
        End Try
    End Function

    Public ReadOnly Property QualitaCorrente As QualitaVideo
        Get
            Return _qualitaCorrente
        End Get
    End Property

    Public ReadOnly Property BandaStimataMbps As Double
        Get
            Try
                Return _bandaStimata / 1000000.0
            Catch ex As Exception
                log.ScriviLog("Eccezione in BandaStimataMbps: " & ex.Message)
                Return 0
            End Try
        End Get
    End Property

    Public ReadOnly Property FaseAvvioInCorso As Boolean
        Get
            Return _faseAvvioInCorso
        End Get
    End Property

    Public ReadOnly Property NomeQualitaCorrente As String
        Get
            Try
                Return NomiQualita(IndiceQualita(_qualitaCorrente))
            Catch ex As Exception
                log.ScriviLog("Eccezione in NomeQualitaCorrente: " & ex.Message)
                Return String.Empty
            End Try
        End Get
    End Property

    Public Property QualitaAdattivaAbilitata As Boolean
        Get
            Return _qualitaAdattivaAbilitata
        End Get
        Set(value As Boolean)
            Try
                _qualitaAdattivaAbilitata = value
            Catch ex As Exception
                log.ScriviLog("Eccezione in QualitaAdattivaAbilitata Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public ReadOnly Property DownloadOneDriveInCorso As Boolean
        Get
            Return _downloadOneDriveInCorso
        End Get
    End Property

    Public ReadOnly Property ModalitaLive As Boolean
        Get
            Return _modalitaLive
        End Get
    End Property

    Public ReadOnly Property UrlStreamTv As String
        Get
            Return _urlStreamTv
        End Get
    End Property

    Public ReadOnly Property TentativiRiconnessione As Integer
        Get
            Return _tentativiRiconnessione
        End Get
    End Property

    Public Property MaxTentativiRiconnessione As Integer
        Get
            Return _maxTentativiRiconnessione
        End Get
        Set(value As Integer)
            Try
                _maxTentativiRiconnessione = Math.Max(1, value)
            Catch ex As Exception
                log.ScriviLog("Eccezione in MaxTentativiRiconnessione Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Property UserAgentTv As String
        Get
            Return _userAgentTv
        End Get
        Set(value As String)
            Try
                If Not String.IsNullOrWhiteSpace(value) Then _userAgentTv = value.Trim()
            Catch ex As Exception
                log.ScriviLog("Eccezione in UserAgentTv Set: " & ex.Message)
            End Try
        End Set
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            _inArresto = True
            _annullaDownloadOneDrive = True
            _riconnessioneInCorso = False
            ResetModalitaLive()
            If _threadRiconnessione IsNot Nothing AndAlso _threadRiconnessione.IsAlive Then
                Try
                    _threadRiconnessione.Abort()
                Catch
                End Try
                _threadRiconnessione.Join(1500)
            End If
            If _threadRetryVideoOndemand IsNot Nothing AndAlso _threadRetryVideoOndemand.IsAlive Then
                Try
                    _threadRetryVideoOndemand.Abort()
                Catch
                End Try
                _threadRetryVideoOndemand.Join(1500)
            End If
            If _threadDownloadOneDrive IsNot Nothing AndAlso _threadDownloadOneDrive.IsAlive Then
                _threadDownloadOneDrive.Join(3000)
                If _threadDownloadOneDrive.IsAlive Then
                    _threadDownloadOneDrive.Abort()
                End If
            End If
            ScollegaFormOspiteCorrente()
            If _formSottofondo IsNot Nothing AndAlso Not _formSottofondo.IsDisposed Then
                Try
                    _formSottofondo.Dispose()
                Catch exForm As Exception
                    log.ScriviLog("Eccezione chiusura form di sottofondo: " & exForm.Message)
                End Try
            End If
            ' Il ciclo eventi sta usando l'handle di mpv: va fermato prima di distruggerlo.
            If _threadEventi IsNot Nothing AndAlso _threadEventi.IsAlive Then
                _threadEventi.Join(2000)
            End If
            Dim handleDaChiudere As IntPtr = _handle
            _handle = IntPtr.Zero
            If handleDaChiudere <> IntPtr.Zero Then
                Try
                    mpv_terminate_destroy(handleDaChiudere)
                Catch exChiusura As Exception
                    log.ScriviLog("Eccezione mpv_terminate_destroy: " & exChiusura.Message)
                End Try
            End If
        Catch ex As Exception
            log.ScriviLog("Eccezione in Dispose: " & ex.Message)
        End Try
    End Sub

End Class

Public Class LogMessageEventArgs
    Inherits EventArgs
    Public ReadOnly Property Messaggio As String
    Public ReadOnly Property Livello As String
    Public Sub New(msg As String, lvl As String)
        Messaggio = msg
        Livello = lvl
    End Sub
End Class

Public Class PropertyChangedEventArgs
    Inherits EventArgs
    Public ReadOnly Property Nome As String
    Public ReadOnly Property Valore As Object
    Public Sub New(n As String, v As Object)
        Nome = n
        Valore = v
    End Sub
End Class

Public Class CambioQualitaEventArgs
    Inherits EventArgs
    Public ReadOnly Property QualitaVecchia As String
    Public ReadOnly Property QualitaNuova As String
    Public ReadOnly Property DurataBufferSecondi As Double
    Public Sub New(vecchia As String, nuova As String, buffer As Double)
        QualitaVecchia = vecchia
        QualitaNuova = nuova
        DurataBufferSecondi = buffer
    End Sub
End Class

Public Class AnalisiConnessioneEventArgs
    Inherits EventArgs
    Public ReadOnly Property BandaMbps As Double
    Public ReadOnly Property SecondiImpiegati As Double
    Public ReadOnly Property QualitaScelta As String
    Public Sub New(banda As Double, secondi As Double, qualita As String)
        BandaMbps = banda
        SecondiImpiegati = secondi
        QualitaScelta = qualita
    End Sub
End Class

Public Class ProgressoOneDriveEventArgs
    Inherits EventArgs
    Public ReadOnly Property Percorso As String
    Public ReadOnly Property Percentuale As Integer
    Public ReadOnly Property Timeout As Boolean
    Public ReadOnly Property Completato As Boolean
    Public Sub New(p As String, perc As Integer, tim As Boolean, comp As Boolean)
        Percorso = p
        Percentuale = perc
        Timeout = tim
        Completato = comp
    End Sub
End Class
Public Class RiconnessioneStreamEventArgs
    Inherits EventArgs
    Public ReadOnly Property Url As String
    Public ReadOnly Property Tentativo As Integer
    Public ReadOnly Property MaxTentativi As Integer
    Public ReadOnly Property Motivo As String
    Public Sub New(u As String, t As Integer, maxT As Integer, m As String)
        Url = u
        Tentativo = t
        MaxTentativi = maxT
        Motivo = m
    End Sub
End Class

Public Class ErroreRadioEventArgs
    Inherits EventArgs
    Public ReadOnly Property Url As String
    Public ReadOnly Property Motivo As String
    Public Sub New(u As String, m As String)
        Url = If(u, String.Empty)
        Motivo = If(m, String.Empty)
    End Sub
End Class

Public Class CircuitoRaiEventArgs
    Inherits EventArgs
    Public ReadOnly Property SecondiResidui As Integer
    Public ReadOnly Property Motivo As String
    Public Sub New(secondi As Integer, m As String)
        SecondiResidui = Math.Max(0, secondi)
        Motivo = If(m, String.Empty)
    End Sub
End Class

Public Class StatoLibreriaEventArgs
    Inherits EventArgs
    Public ReadOnly Property Fase As String
    Public ReadOnly Property Messaggio As String
    Public ReadOnly Property Percentuale As Integer
    Public Sub New(f As String, m As String, perc As Integer)
        Fase = If(f, String.Empty)
        Messaggio = If(m, String.Empty)
        Percentuale = Math.Max(0, Math.Min(100, perc))
    End Sub
End Class