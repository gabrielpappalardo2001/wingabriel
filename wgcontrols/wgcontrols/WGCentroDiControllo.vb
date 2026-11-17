Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports System.Diagnostics
Imports Microsoft.VisualBasic.FileIO
Imports System.Collections.Generic
Imports System.Linq
Imports MediaDevices
Imports System.Net.Http
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports System.Data.SQLite
Imports System.Data
Imports System.Net
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Text.RegularExpressions
Imports System.Security.Cryptography
Imports System.Text
Imports System.Globalization
Imports WGFtp
Imports WGDb

Public Enum ElementType
    File
    WebRadio
    YouTube
    StreamTv
End Enum

Public Structure elemento
    Public path As String
    Public tipo As ElementType
    ''' <summary>Nome leggibile (canale TV, titolo custom). Se vuoto si deriva dal path.</summary>
    Public nome As String
    ''' <summary>User agent HTTP da usare per StreamTv, se noto. Vuoto = rilevamento automatico.</summary>
    Public userAgent As String
    ''' <summary>Referer HTTP da usare per StreamTv, se noto. Vuoto = rilevamento automatico.</summary>
    Public referer As String
    ''' <summary>Header HTTP extra (es. Origin) da usare per StreamTv, se noti.</summary>
    Public headersExtra As String
End Structure

Public Enum StatoOperazioneCorrente
    Nessuna
    File
    Ftp
    Video
    Archivio
    LibriVox
    Audio
    Bluetooth
    DownloadDipendenze
    Testi
    ArchivioInternet
    ConversioneMp3
    RiscritturaAi
    RiconoscimentoSpartiti
    ScaricamentoModelli
    ScaricamentoYoutube
End Enum

Public Enum TipoOperazioneFile
    Copia
    Sposta
    Elimina
    CopiaPortatile
    SpostaPortatile
    EliminaPortatile
End Enum

Public Enum TipoOperazioneFtp
    Upload
    Download
    CreaCartellaRemota
    SpostaRemoto
    CopiaRemoto
End Enum

Public Enum TipoOperazioneLibriVox
    Nessuna
    AggiornamentoCatalogo
    DownloadLibri
    EliminaLibri
End Enum

Public Enum DecisioneSovrascrittura
    Chiedi
    SovrascriviTutti
    SaltaTutti
End Enum

Public Enum ModalitaPreparazioneFile
    Copia
    Sposta
    Elimina
    DirectoryCopia
    DirectorySposta
    DirectoryElimina
    CopiaPortatile
    SpostaPortatile
    EliminaPortatile
End Enum

Public Class WGCentroDiControllo
    Private _descrizioneOperazioneCorrente As String
    Private _titoloLibroTestoCorrente As String
    Private Shared _istanza As WGCentroDiControllo
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _lockCoda As New Object()
    Private _log As New wgcontrols.Logger()
    Private _uiContext As SynchronizationContext

    Private Const WPD_PREFIX As String = "WPD|"
    Private Const URL_BASE_API_SEARCH As String = "https://librivox.org/api/feed/audiobooks?format=json"
    Private Const CARTELLA_DATI_LIBRI As String = "Dati\Libri"
    Private Const NOME_FILE_DB As String = "db.sqlite"
    Private Const NOME_FILE_DB_TESTI As String = "libritesto.db"
    Private Const URL_BASE_WIKISOURCE As String = "https://it.wikisource.org/w/api.php"
    Private Const CARTELLA_DATI_ARCHIVE As String = "Dati\InternetArchive"
    Private Const NOME_FILE_DB_ARCHIVE As String = "internetarchive.db"
    Private Const URL_SCRAPE_ARCHIVE As String = "https://archive.org/services/search/v1/scrape"
    Private Const URL_BASE_ARCHIVE As String = "https://archive.org"

    Public Event DownloadDipendenzeCompletato()

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function CopyFile(ByVal src As String, ByVal dst As String, ByVal failIfExists As Boolean) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function MoveFile(ByVal src As String, ByVal dst As String) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function DeleteFile(ByVal filename As String) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode)>
    Private Structure OperazioneFileShell64
        Public hwnd As IntPtr
        Public wFunc As UInteger
        Public pFrom As String
        Public pTo As String
        Public fFlags As UShort
        Public fAnyOperationsAborted As Integer
        Public hNameMappings As IntPtr
        Public lpszProgressTitle As String
    End Structure

    <StructLayout(LayoutKind.Sequential, CharSet:=CharSet.Unicode, Pack:=1)>
    Private Structure OperazioneFileShell32
        Public hwnd As IntPtr
        Public wFunc As UInteger
        Public pFrom As String
        Public pTo As String
        Public fFlags As UShort
        Public fAnyOperationsAborted As Integer
        Public hNameMappings As IntPtr
        Public lpszProgressTitle As String
    End Structure

    <DllImport("shell32.dll", EntryPoint:="SHFileOperationW", CharSet:=CharSet.Unicode)>
    Private Shared Function OperazioneFileShell64Esegui(ByRef operazione As OperazioneFileShell64) As Integer
    End Function

    <DllImport("shell32.dll", EntryPoint:="SHFileOperationW", CharSet:=CharSet.Unicode)>
    Private Shared Function OperazioneFileShell32Esegui(ByRef operazione As OperazioneFileShell32) As Integer
    End Function

    Private Const FO_DELETE As UInteger = 3UI
    Private Const FOF_SILENT As UShort = &H4US
    Private Const FOF_NOCONFIRMATION As UShort = &H10US
    Private Const FOF_ALLOWUNDO As UShort = &H40US
    Private Const FOF_NOERRORUI As UShort = &H400US

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Private Shared Function CreateDirectoryNativa(lpPathName As String, lpSecurityAttributes As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True, EntryPoint:="GetFileAttributesW")>
    Private Shared Function GetFileAttributesNativo(lpFileName As String) As UInteger
    End Function

    <DllImport("kernel32.dll", CharSet:=CharSet.Unicode, SetLastError:=True, EntryPoint:="GetCompressedFileSizeW")>
    Private Shared Function GetCompressedFileSizeNativo(lpFileName As String, ByRef lpFileSizeHigh As UInteger) As UInteger
    End Function

    <DllImport("cldapi.dll", CharSet:=CharSet.Unicode, CallingConvention:=CallingConvention.Winapi)>
    Private Shared Function CfOpenFileWithOplock(filePath As String, flags As Integer, ByRef protectedHandle As IntPtr) As Integer
    End Function

    <DllImport("cldapi.dll", CallingConvention:=CallingConvention.Winapi)>
    Private Shared Function CfHydratePlaceholder(fileHandle As IntPtr, startingOffset As Long, length As Long, hydrateFlags As Integer, overlapped As IntPtr) As Integer
    End Function

    <DllImport("cldapi.dll", CallingConvention:=CallingConvention.Winapi)>
    Private Shared Sub CfCloseHandle(protectedHandle As IntPtr)
    End Sub

    Private Const FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS As UInteger = &H400000UI
    Private Const FILE_ATTRIBUTE_RECALL_ON_OPEN As UInteger = &H40000UI
    Private Const FILE_ATTRIBUTE_OFFLINE As UInteger = &H1000UI
    Private Const CF_OPEN_FILE_FLAG_NONE As Integer = 0
    Private Const CF_HYDRATE_FLAG_NONE As Integer = 0
    Private Const CF_EOF As Long = -1L
    Private Const HR_S_OK As Integer = 0
    Private Const DIMENSIONE_FILE_NON_VALIDA As UInteger = &HFFFFFFFFUI

    Public Class LibroScaricato
        Public Property Id As String
        Public Property Titolo As String
        Public Property Autore As String
        Public Property Tipo As String
        Public Property PercorsoLocale As String
        Public Property Capitoli As List(Of CapitoloScaricato)

        Public Sub New()
            Tipo = "Audio"
            Capitoli = New List(Of CapitoloScaricato)()
        End Sub
    End Class

    Public Class CapitoloScaricato
        Public Property Id As Integer
        Public Property TitoloCapitolo As String
        Public Property PercorsoFile As String
    End Class

    Public Structure ElementoDaScaricareArchivioInternet
        Public Identificatore As String
        Public Titolo As String
        Public Autore As String
        Public EAudio As Boolean
    End Structure

    Private Class ProgressoSemplice
        Public Valore As Integer = 0
    End Class

    Private Class EsecutoreVoceEsterna
        Public Azione As Action
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Try
                If Azione Is Nothing Then Return
                Azione.Invoke()
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub
    End Class

    Private Class IdratatoreOneDrive
        Public Handle As IntPtr = IntPtr.Zero
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Try
                CfHydratePlaceholder(Handle, 0L, CF_EOF, CF_HYDRATE_FLAG_NONE, IntPtr.Zero)
            Catch ex As Exception
                _logger.ScriviLog("Thread download contenuto OneDrive: " & ex.ToString())
            End Try
        End Sub
    End Class

    Private Class TrasferimentoDispositivo
        Public Dispositivo As MediaDevice
        Public PercorsoLocale As String = ""
        Public PercorsoRemoto As String = ""
        Public EDownload As Boolean = True
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Try
                If Dispositivo Is Nothing Then Return
                If EDownload Then
                    Dispositivo.DownloadFile(PercorsoRemoto, PercorsoLocale)
                Else
                    Dispositivo.UploadFile(PercorsoLocale, PercorsoRemoto)
                End If
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub
    End Class

    Private Class PreparatoreLavoroFile
        Public Centro As WGCentroDiControllo
        Public Lavoro As LavoroFile
        Public Sorgenti As List(Of String)
        Public Destinazione As String = ""
        Public DispositivoDestinazione As String = ""
        Public PercorsoRemoto As String = ""
        Public Modalita As ModalitaPreparazioneFile = ModalitaPreparazioneFile.Copia
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Try
                If Centro Is Nothing Then Return
                Centro.PreparaLavoroFile(Me)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub
    End Class

    Private Class MonitoreProgressoFtp
        Public Lavoro As LavoroFtp
        Public Indice As Integer = 0
        Private _ultimoOrario As DateTime = DateTime.Now
        Private _ultimiBytes As Long = 0
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Aggiorna(bytesTrasferiti As Long, bytesTotali As Long)
            Try
                If Lavoro Is Nothing Then Return
                Dim adesso As DateTime = DateTime.Now
                Dim secondiTrascorsi As Double = (adesso - _ultimoOrario).TotalSeconds
                SyncLock Lavoro.BloccoStato
                    If Indice < Lavoro.StatiWorker.Count Then
                        If secondiTrascorsi > 0.2 AndAlso bytesTrasferiti > _ultimiBytes Then
                            Dim velocitaIstantanea As Double = (bytesTrasferiti - _ultimiBytes) / secondiTrascorsi
                            If Lavoro.StatiWorker(Indice).Velocita <= 0 Then
                                Lavoro.StatiWorker(Indice).Velocita = velocitaIstantanea
                            Else
                                Lavoro.StatiWorker(Indice).Velocita = Lavoro.StatiWorker(Indice).Velocita * 0.7 + velocitaIstantanea * 0.3
                            End If
                            _ultimoOrario = adesso
                            _ultimiBytes = bytesTrasferiti
                        End If
                        Lavoro.StatiWorker(Indice).Bytes = bytesTrasferiti
                    End If
                End SyncLock
            Catch ex As Exception
                _logger.ScriviLog("MonitoreProgressoFtp - Errore callback progresso: " & ex.ToString())
            End Try
        End Sub
    End Class

    Private Class WorkerFtp
        Public Centro As WGCentroDiControllo
        Public Lavoro As LavoroFtp
        Public Indice As Integer = 0
        Public Compiti As List(Of TaskFtp)
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Try
                If Centro Is Nothing Then Return
                Centro.EseguiWorkerFtp(Lavoro, Indice, Compiti)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub
    End Class

    Private Class PreparatoreLavoroFtp
        Public Centro As WGCentroDiControllo
        Public Lavoro As LavoroFtp
        Public Percorsi As List(Of String)
        Public Destinazione As String = ""
        Public Server As InfoServerFtp
        Public EUpload As Boolean = True
        Public EliminaSorgente As Boolean = False
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Try
                If Centro Is Nothing Then Return
                Centro.PreparaLavoroFtp(Me)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub
    End Class

    ''' <summary>
    ''' Prepara in background un lavoro di spostamento o copia fra due cartelle dello
    ''' stesso server FTP/SFTP: per ogni elemento selezionato determina solo se e' un file
    ''' o una cartella e la relativa dimensione totale, cosi' da creare un singolo TaskFtp
    ''' per elemento (la ricorsione dentro le cartelle e' gestita da CLSFtp).
    ''' </summary>
    Private Class PreparatoreSpostaCopiaFtp
        Public Centro As WGCentroDiControllo
        Public Lavoro As LavoroFtp
        Public Percorsi As List(Of String)
        Public Destinazione As String = ""
        Public Server As InfoServerFtp
        Public ESposta As Boolean = True
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Try
                If Centro Is Nothing Then Return
                Centro.PreparaSpostaCopiaFtpLavoro(Me)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub
    End Class

    Private MustInherit Class LavoroBase
        Public Identificativo As Integer = 0
        Public Descrizione As String = ""
        Public Tipo As StatoOperazioneCorrente = StatoOperazioneCorrente.Nessuna
        Public Risorse As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Public ThreadLavoro As Thread
        Public InPreparazione As Boolean = False
        Public InAttesa As Boolean = False
        Public MotivoAttesa As String = ""
        Public InEsecuzione As Boolean = False
        Public InPausa As Boolean = False
        Public Annulla As Boolean = False
        Public Blocco As New Object()
    End Class

    Private Class LavoroFile
        Inherits LavoroBase

        Public Coda As New List(Of TaskFile)()
        Public TotaleBytes As Long = 0
        Public BytesProcessati As Long = 0
        Public BytesFileCorrente As Long = 0
        Public NomeFileCorrente As String = ""
        Public VelocitaBps As Double = 0
        Public TempoInizio As DateTime = DateTime.Now
        Public TempoUltimaPausa As DateTime = DateTime.Now
        Public DurataTotalePausa As TimeSpan = TimeSpan.Zero
        Public MotivoInterruzione As String = ""
        Public Contesto As ContestoSovrascrittura = New ContestoSovrascrittura()
        Public DownloadOneDriveInCorso As Boolean = False
        Public DownloadOneDriveNomeFile As String = ""
        Public DownloadOneDriveBytesTotali As Long = 0
        Public DownloadOneDriveBytesScaricati As Long = 0
        Public DownloadOneDriveVelocitaBps As Double = 0
    End Class

    Private Class LavoroFtp
        Inherits LavoroBase

        Public Coda As New List(Of TaskFtp)()
        Public CartelleRemoteDaEliminare As New List(Of TaskFtp)()
        Public TotaleBytes As Long = 0
        Public BytesProcessati As Long = 0
        Public TempoInizio As DateTime = DateTime.Now
        Public MotivoInterruzione As String = ""
        Public Contesto As ContestoSovrascrittura = New ContestoSovrascrittura()
        Public StatiWorker As New List(Of StatoWorkerFtp)()
        Public IndiceProssimoTask As Integer = 0
        Public Annullamento As CancellationTokenSource
        Public BloccoStato As New Object()
        Public BloccoIndice As New Object()
    End Class

    Private Class LavoroArchivio
        Inherits LavoroBase

        Public Compito As TaskArchivio
        Public Processo As Process
        Public Progresso As Integer = 0
    End Class

    Private Class LavoroLibriVox
        Inherits LavoroBase

        Public TipoOperazione As TipoOperazioneLibriVox = TipoOperazioneLibriVox.Nessuna
        Public CodaDownload As New List(Of String)()
        Public CodaEliminazione As New List(Of String)()
        Public CartellaDownload As String = ""
        Public Progresso As Integer = 0
        Public Info As String = ""
        Public StatoDownload As StatoDownloadHttp
        Public ProgressoEstrazione As New ProgressoSemplice()
        Public LibriTotali As Integer = 0
        Public LibriCompletati As Integer = 0
    End Class

    Private Class LavoroTesti
        Inherits LavoroBase

        Public CodaDownload As New List(Of ElementoDaScaricareTesto)()
        Public CategorieDaAggiornare As New List(Of String)()
        Public EAggiornamentoCatalogo As Boolean = False
        Public Progresso As Integer = 0
        Public Info As String = ""
        Public LibriTotali As Integer = 0
        Public LibriCompletati As Integer = 0
        Public CapitoliTotali As Integer = 0
        Public CapitoliCompletati As Integer = 0
    End Class

    Private Class LavoroArchivioInternet
        Inherits LavoroBase

        Public CodaDownload As New List(Of ElementoDaScaricareArchivioInternet)()
        Public EAggiornamentoCatalogo As Boolean = False
        Public CartellaAudio As String = ""
        Public CartellaTesti As String = ""
        Public Progresso As Integer = 0
        Public Info As String = ""
        Public LibriTotali As Integer = 0
        Public LibriCompletati As Integer = 0
        Public BytesTotaliLibro As Long = 0
        Public BytesScaricatiLibro As Long = 0
        Public TracceTotaliLibro As Integer = 0

        Public Sub AggiungiBytes(quantita As Long)
            Interlocked.Add(BytesScaricatiLibro, quantita)
        End Sub

        Public Function BytesScaricati() As Long
            Return Interlocked.Read(BytesScaricatiLibro)
        End Function

        Public Sub AzzeraBytes()
            Interlocked.Exchange(BytesScaricatiLibro, 0)
        End Sub
    End Class

    Private Class LavoroVideo
        Inherits LavoroBase

        Public Coda As New List(Of String)()
        Public Processo As Process
        Public VideoTotali As Integer = 0
        Public VideoElaborati As Integer = 0
        Public ProgressoCorrente As Double = 0
        Public Info As String = ""
        Public Velocita As String = ""
        Public TempoResiduo As String = ""
    End Class

    Private Class LavoroBluetooth
        Inherits LavoroBase

        Public Files As New List(Of String)()
        Public NomeDispositivo As String = ""
        Public Progresso As Integer = 0
        Public Info As String = ""
    End Class

    Private Class ElementoRiscrittura
        Public Percorso As String = ""
        Public Testo As String = ""
        Public Codifica As Encoding
        Public UsaCrLf As Boolean = False
        Public UltimaModificaUtc As DateTime
        Public Blocchi As New List(Of String)()
    End Class

    Private Class LavoroRiscrittura
        Inherits LavoroBase

        Public Files As New List(Of String)()
        Public Istruzioni As String = ""
        Public Provider As WGAi.ProviderAi? = Nothing
        Public Modello As String = ""
        Public Progresso As Integer = 0
        Public Info As String = ""
        Public FileCompletati As Integer = 0
    End Class

    Private Class ControlloLavoro
        Private ReadOnly _centro As WGCentroDiControllo
        Private ReadOnly _lavoro As LavoroBase
        Private ReadOnly _logger As Logger

        Public Sub New(centro As WGCentroDiControllo, lavoro As LavoroBase, logger As Logger)
            _centro = centro
            _lavoro = lavoro
            _logger = logger
        End Sub

        Public Sub ApriMenu()
            Try
                _centro.ApriMenuLavoro(_lavoro, Me)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Public Sub ApriAzioni()
            Try
                _centro.ApriMenuAzioniLavoro(_lavoro, Me)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Public Sub MostraInformazioni()
            Try
                _centro.MostraInfoLavoro(_lavoro)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Public Sub MettiInPausa()
            Try
                _centro.MettiInPausaLavoro(_lavoro)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Public Sub Riprendi()
            Try
                _centro.RiprendiLavoro(_lavoro)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Public Sub Annulla()
            Try
                _centro.AnnullaLavoro(_lavoro)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub
    End Class

    Private Class ContestoSovrascrittura
        Public Decisione As DecisioneSovrascrittura = DecisioneSovrascrittura.Chiedi
    End Class

    Private Class StatoDownloadHttp
        Public NomeFile As String = ""
        Public BytesTotali As Long = 0
        Public BytesScaricati As Long = 0
        Public VelocitaBps As Double = 0
        Public Inizio As DateTime = DateTime.Now
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Azzera(nome As String)
            Try
                NomeFile = nome
                BytesTotali = 0
                BytesScaricati = 0
                VelocitaBps = 0
                Inizio = DateTime.Now
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Public Function Percentuale() As Double
            Try
                If BytesTotali <= 0 Then Return 0
                Dim valore As Double = (BytesScaricati / CDbl(BytesTotali)) * 100
                If valore < 0 Then valore = 0
                If valore > 100 Then valore = 100
                Return valore
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return 0
            End Try
        End Function

        Public Function SecondiResidui() As Double
            Try
                If BytesTotali <= 0 Then Return -1
                Dim velocita As Double = VelocitaBps
                If velocita <= 0 Then
                    Dim trascorsi As Double = (DateTime.Now - Inizio).TotalSeconds
                    If trascorsi <= 0 Then Return -1
                    velocita = BytesScaricati / trascorsi
                End If
                If velocita <= 0 Then Return -1
                Dim mancanti As Long = BytesTotali - BytesScaricati
                If mancanti < 0 Then mancanti = 0
                Return mancanti / velocita
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return -1
            End Try
        End Function
    End Class

    Private Class ScaricamentoFileArchivio
        Public Client As HttpClient
        Public Url As String
        Public PercorsoFile As String
        Public Md5Atteso As String
        Public Riuscito As Boolean = False
        Public Lavoro As LavoroArchivioInternet
        Private ReadOnly _logger As Logger

        Public Sub New(logger As Logger)
            _logger = logger
        End Sub

        Public Sub Esegui()
            Const numeroTentativiMassimo As Integer = 3
            Try
                For tentativo As Integer = 1 To numeroTentativiMassimo
                    Dim bytesSegnalati As Long = 0
                    Try
                        If AnnullamentoAttivo() Then Return

                        Using richiesta As New HttpRequestMessage(HttpMethod.Get, Url)
                            Using risposta As HttpResponseMessage = Client.SendAsync(richiesta, HttpCompletionOption.ResponseHeadersRead).Result
                                If Not risposta.IsSuccessStatusCode Then
                                    Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                    If tentativo < numeroTentativiMassimo Then
                                        Dim attesaMs As Integer = If(codiceStato = 429, 3000 * tentativo, 2000 * tentativo)
                                        If codiceStato = 429 AndAlso risposta.Headers.RetryAfter IsNot Nothing AndAlso risposta.Headers.RetryAfter.Delta.HasValue Then
                                            attesaMs = CInt(risposta.Headers.RetryAfter.Delta.Value.TotalMilliseconds)
                                        End If
                                        Thread.Sleep(attesaMs)
                                        Continue For
                                    End If
                                    Return
                                End If

                                Dim percorsoParziale As String = PercorsoFile & ".parziale"
                                Dim interrotto As Boolean = False
                                Dim md5Calcolato As String = ""

                                Using algoritmo As MD5 = MD5.Create()
                                    Using flussoRete As Stream = risposta.Content.ReadAsStreamAsync().Result
                                        Using flussoFile As New FileStream(percorsoParziale, FileMode.Create, FileAccess.Write, FileShare.None, 65536)
                                            Dim buffer(65535) As Byte
                                            Dim letti As Integer = flussoRete.Read(buffer, 0, buffer.Length)
                                            While letti > 0
                                                flussoFile.Write(buffer, 0, letti)
                                                algoritmo.TransformBlock(buffer, 0, letti, Nothing, 0)
                                                bytesSegnalati += letti
                                                SegnalaBytes(letti)
                                                If AnnullamentoAttivo() Then
                                                    interrotto = True
                                                    Exit While
                                                End If
                                                letti = flussoRete.Read(buffer, 0, buffer.Length)
                                            End While
                                        End Using
                                    End Using
                                    algoritmo.TransformFinalBlock(New Byte(-1) {}, 0, 0)
                                    md5Calcolato = ConvertiHashInTesto(algoritmo.Hash)
                                End Using

                                If interrotto Then
                                    EliminaFileParziale(percorsoParziale)
                                    SegnalaBytes(-bytesSegnalati)
                                    Return
                                End If

                                If Not String.IsNullOrWhiteSpace(Md5Atteso) AndAlso Not String.Equals(md5Calcolato, Md5Atteso, StringComparison.OrdinalIgnoreCase) Then
                                    EliminaFileParziale(percorsoParziale)
                                    SegnalaBytes(-bytesSegnalati)
                                    If tentativo < numeroTentativiMassimo Then
                                        Thread.Sleep(1000 * tentativo)
                                        Continue For
                                    End If
                                    Return
                                End If

                                EliminaFileParziale(PercorsoFile)
                                File.Move(percorsoParziale, PercorsoFile)
                                Riuscito = True
                                Return
                            End Using
                        End Using
                    Catch exTentativo As Exception
                        _logger.ScriviLog($"Errore download traccia (tentativo {tentativo} di {numeroTentativiMassimo}) {Url}: {exTentativo.Message}")
                        SegnalaBytes(-bytesSegnalati)
                        If tentativo < numeroTentativiMassimo Then
                            Thread.Sleep(1000 * tentativo)
                        End If
                    End Try
                Next
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Private Function AnnullamentoAttivo() As Boolean
            Try
                If Lavoro Is Nothing Then Return False
                Return Lavoro.Annulla
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return False
            End Try
        End Function

        Private Sub SegnalaBytes(quantita As Long)
            Try
                If quantita = 0 Then Return
                If Lavoro Is Nothing Then Return
                Lavoro.AggiungiBytes(quantita)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Private Sub EliminaFileParziale(percorso As String)
            Try
                If String.IsNullOrWhiteSpace(percorso) Then Return
                If File.Exists(percorso) Then File.Delete(percorso)
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
            End Try
        End Sub

        Private Function ConvertiHashInTesto(hash As Byte()) As String
            Try
                If hash Is Nothing Then Return ""
                Dim generatore As New Text.StringBuilder()
                For Each singoloByte As Byte In hash
                    generatore.Append(singoloByte.ToString("x2"))
                Next
                Return generatore.ToString()
            Catch ex As Exception
                _logger.ScriviLog(ex.ToString())
                Return ""
            End Try
        End Function
    End Class

    Public Class LibroTestoScaricato
        Public Property Id As String
        Public Property Titolo As String
        Public Property Autore As String
        Public Property Genere As String
    End Class

    Public Class CapitoloTestoScaricato
        Public Property Id As Integer
        Public Property TitoloCapitolo As String
        Public Property Testo As String
    End Class

    Public Structure ElementoDaScaricareTesto
        Public TitoloPaginaWikisource As String
        Public Titolo As String
        Public Autore As String
        Public Genere As String
    End Structure

    Private Class GitHubAsset
        Public Property name As String
        Public Property browser_download_url As String
    End Class

    Private Class GitHubRelease
        Public Property assets As List(Of GitHubAsset)
    End Class

    Private Class ErroreCopiaIrreversibile
        Inherits Exception
        Public Sub New(messaggio As String, interna As Exception)
            MyBase.New(messaggio, interna)
        End Sub
    End Class

    Private Const TENTATIVI_LETTURA_IO As Integer = 5
    Private Const ATTESA_BASE_RETRY_IO_MS As Integer = 400

    Private Structure TaskFile
        Public Tipo As TipoOperazioneFile
        Public Sorgente As String
        Public Destinazione As String
        Public Dimensione As Long
        Public WpdDeviceId As String
        Public WpdPath As String
    End Structure

    Private Structure TaskFtp
        Public Tipo As TipoOperazioneFtp
        Public PercorsoLocale As String
        Public PercorsoRemoto As String
        ''' <summary>
        ''' Usato solo da SpostaRemoto/CopiaRemoto: percorso remoto di destinazione, quando
        ''' origine e destinazione sono entrambe sullo stesso server (PercorsoRemoto e'
        ''' l'origine in quel caso).
        ''' </summary>
        Public PercorsoRemotoDestinazione As String
        ''' <summary>
        ''' Usato solo da SpostaRemoto/CopiaRemoto: True se l'elemento da spostare/copiare e'
        ''' una cartella (va gestita ricorsivamente) anziche' un singolo file.
        ''' </summary>
        Public EDirectory As Boolean
        Public Dimensione As Long
        Public Server As InfoServerFtp
        Public EliminaSorgenteDopo As Boolean
    End Structure

    Private Class StatoWorkerFtp
        Public NomeFile As String = ""
        Public Bytes As Long = 0
        Public Velocita As Double = 0
        Public Attivo As Boolean = False
    End Class

    Private Structure VoceEsterna
        Public Nome As String
        Public Azione As Action
        ' Testo che cambia (per esempio lo stato di una ricerca): se c'è, si mostra al posto del nome.
        Public Etichetta As Func(Of String)
    End Structure

    Private Structure TaskArchivio
        Public IsCompressione As Boolean
        Public Sorgenti As List(Of String)
        Public Destinazione As String
        Public Password As String
    End Structure

    Private Class LvbBook
        Public Property id As String
        Public Property title As String
        Public Property url_zip_file As String
        Public Property language As String
        Public Property authors As List(Of LvbAuthor)
        Public Property genres As List(Of LvbGenre)
    End Class

    Private Class LvbAuthor
        Public Property first_name As String
        Public Property last_name As String
    End Class

    Private Class LvbGenre
        Public Property name As String
    End Class

    Private Class LvbResponse
        Public Property books As List(Of LvbBook)
    End Class

    Private Class WsCategoryMember
        Public Property title As String
    End Class

    Private Class WsQuery
        Public Property categorymembers As List(Of WsCategoryMember)
    End Class

    Private Class WsContinue
        Public Property cmcontinue As String
    End Class

    Private Class WsCategoryResponse
        Public Property query As WsQuery
        Public Property [continue] As WsContinue
    End Class

    Private _lavori As New List(Of LavoroBase)()
    Private _lockLavori As New Object()
    Private _contatoreLavori As Integer = 0

    Private Const TENTATIVI_CURL As Integer = 3
    Private Const ATTESA_BASE_RETRY_CURL_MS As Integer = 800
    Private _ftp As New CLSFtp()
    Public Property GradoParallelismoFtp As Integer = 3

    Private _codaVideoInAttesa As New List(Of String)()
    Private _lockCodaVideo As New Object()
    Private _pathYtDlp As String = "yt-dlp.exe"

    Private _path7z As String = "7z.exe"

    Private WithEvents _mpv As WGMpv
    Public _inEsecuzioneAudio As Boolean = False
    Private _elementiAudio As List(Of elemento) = New List(Of elemento)()
    Private _indiceAudio As Integer = -1
    Private _ripetiTraccia As Boolean = False
    Private _descrizioneAudio As String = ""

    Private ReadOnly _categorieWikisourceTesti As String() = {"Romanzi", "Racconti", "Novelle", "Poesie", "Teatro", "Commedie", "Tragedie", "Fiabe", "Favole", "Saggi", "Epistolari", "Autobiografie", "Biografie", "Memorie", "Diari", "Aforismi"}
    Private _categorieDaCercareTesti As String()

    Private _menuCodaLista As WGMenuLista
    Private _vociEsterne As List(Of VoceEsterna) = New List(Of VoceEsterna)()

    Private ReadOnly _lockDomandaSovrascrittura As New Object()
    Private _eventoRispostaSovrascrittura As ManualResetEventSlim
    Private _rispostaSovrascrittura As Boolean = False
    Private Const TIMEOUT_DOMANDA_SOVRASCRITTURA_MS As Integer = 300000

    Private _inDownloadDipendenze As Boolean = False
    Private _progressoDownloadDipendenze As Integer = 0
    Private _statoDownloadDipendenze As String = ""

    Private _descrizioneConflitto As String = ""

    Private _contestoSovrascritturaAttivo As ContestoSovrascrittura
    Private _descrizioneSovrascritturaAttiva As String = ""
    Private _rispostaSovrascritturaRicevuta As Boolean = False
    Private _menuSovrascrittura As WGMenu

    Private _statoDownloadDipendenzeHttp As StatoDownloadHttp
    Private _baseProgressoDipendenze As Double = 0
    Private _pesoProgressoDipendenze As Double = 0

    Public Property UsaCestino As Boolean = True

    Private Sub New()
        Try
            _uiContext = SynchronizationContext.Current
            _statoDownloadDipendenzeHttp = New StatoDownloadHttp(_log)

            Dim pathDati As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_LIBRI)
            If Not Directory.Exists(pathDati) Then
                Directory.CreateDirectory(pathDati)
            End If

            Dim tDownload As New Thread(AddressOf ControllaEScaricaDipendenze)
            tDownload.IsBackground = True
            tDownload.Start()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Shared Function GetInstance() As WGCentroDiControllo
        Try
            If _istanza Is Nothing Then
                SyncLock _lock
                    If _istanza Is Nothing Then
                        _istanza = New WGCentroDiControllo()
                    End If
                End SyncLock
            End If
            Return _istanza
        Catch ex As Exception

            Dim _log As New Logger()
            _log.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    ' --- GESTIONE AUDIO / BACKGROUND ---

    Public Sub SincronizzaRiproduzioneBackground(elementiPlayer As List(Of elemento), indiceCorrente As Integer)
        Try
            _elementiAudio = elementiPlayer
            _indiceAudio = indiceCorrente
            _inEsecuzioneAudio = True

            _mpv = WGMpv.GetInstance()
            RemoveHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            AddHandler _mpv.EndFile, AddressOf GestisciFineTraccia

            AggiornaDescrizioneAudioCorrente()
            _descrizioneOperazioneCorrente = TestoOperazioneAudio()

            AggiornaAgganciamentoVideoAudio()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function TipoAudioCorrente() As ElementType
        Try
            If _indiceAudio >= 0 AndAlso _indiceAudio < _elementiAudio.Count Then
                Return _elementiAudio(_indiceAudio).tipo
            End If
            Return ElementType.File
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return ElementType.File
        End Try
    End Function

    Private Function EStreamLiveCorrente() As Boolean
        Try
            Dim t As ElementType = TipoAudioCorrente()
            Return t = ElementType.WebRadio OrElse t = ElementType.StreamTv
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub AggiornaDescrizioneAudioCorrente()
        Try
            If _indiceAudio >= 0 AndAlso _indiceAudio < _elementiAudio.Count Then
                Dim el As elemento = _elementiAudio(_indiceAudio)
                If Not String.IsNullOrWhiteSpace(el.nome) Then
                    _descrizioneAudio = el.nome
                ElseIf el.tipo = ElementType.YouTube Then
                    _descrizioneAudio = "Video da YouTube"
                Else
                    _descrizioneAudio = Path.GetFileName(el.path)
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function TestoOperazioneAudio() As String
        Try
            Select Case TipoAudioCorrente()
                Case ElementType.YouTube
                    Return "Visualizzazione video da YouTube"
                Case ElementType.StreamTv
                    Return "Riproduzione " & _descrizioneAudio
                Case ElementType.WebRadio
                    If String.IsNullOrWhiteSpace(_descrizioneAudio) Then Return "Ascolto Radio"
                    Return "Ascolto " & _descrizioneAudio
                Case Else
                    Return "Ascolto Brani"
            End Select
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "Riproduzione in corso"
        End Try
    End Function

    Private Sub AggiornaAgganciamentoVideoAudio()
        Try
            If _mpv Is Nothing Then Return
            Dim t As ElementType = TipoAudioCorrente()
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse contenitore.IsDisposed Then Return

            If t = ElementType.StreamTv OrElse t = ElementType.YouTube Then
                Dim handlePannello As IntPtr = contenitore.OttieniHandlePannelloVideo()
                If handlePannello <> IntPtr.Zero Then
                    contenitore.MostraPannelloVideo()
                    _mpv.AllegaAFinestra(handlePannello)
                End If
            Else
                contenitore.NascondiPannelloVideo()
                _mpv.AllegaAFinestra(IntPtr.Zero)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub GestisciFineTraccia(sender As Object, e As EventArgs)
        Try
            ' Se è uno stream live (radio o tv) non facciamo nulla: la riconnessione la gestisce mpv
            If EStreamLiveCorrente() Then Return

            ' mpv segnala EndFile solo quando il file è finito davvero (uno stop o la chiusura della finestra non lo segnalano):
            ' vale anche per i video di YouTube, che altrimenti restano "in riproduzione" per sempre e all'uscita
            ' il programma chiederebbe se interrompere un ascolto che è già finito.

            If _ripetiTraccia Then
                GestisciRiproduzione()
            Else
                If _indiceAudio < _elementiAudio.Count - 1 Then
                    _indiceAudio += 1
                    GestisciRiproduzione()
                Else
                    _inEsecuzioneAudio = False
                    _descrizioneOperazioneCorrente = ""
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub GestisciRiproduzione()
        Try
            If _indiceAudio < 0 OrElse _indiceAudio >= _elementiAudio.Count Then Return
            If _mpv Is Nothing Then Return

            Dim el As elemento = _elementiAudio(_indiceAudio)

            AggiornaDescrizioneAudioCorrente()
            _descrizioneOperazioneCorrente = TestoOperazioneAudio()
            AggiornaAgganciamentoVideoAudio()

            Select Case el.tipo
                Case ElementType.YouTube
                    _mpv.FermaRiproduzione()
                    _mpv.RiproduciVideoYoutube(el.path)
                Case ElementType.StreamTv
                    _mpv.PreparaNuovoCaricamento()
                    _mpv.RiproduciStreamTv(el.path, el.userAgent, el.referer, el.headersExtra)
                Case ElementType.WebRadio
                    _mpv.PreparaNuovoCaricamento()
                    _mpv.CaricaFile(el.path)
                    _mpv.Riproduci()
                Case Else
                    _mpv.FermaRiproduzione()
                    _mpv.CaricaFile(el.path)
                    _mpv.Riproduci()
            End Select
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub TerminaAscoltoAudio()
        Try
            If _mpv IsNot Nothing Then
                _mpv.StopPlayback()
                RemoveHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            End If
            _inEsecuzioneAudio = False
            _elementiAudio.Clear()
            _indiceAudio = -1
            _descrizioneAudio = ""
            _descrizioneOperazioneCorrente = ""

            Try
                Dim contenitore As WGContenitore = WGContenitore.Istanza
                If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                    contenitore.NascondiPannelloVideo()
                End If
            Catch exPannello As Exception
                _log.ScriviLog(exPannello.ToString())
            End Try
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    public Sub TracciaSuccessiva()
        Try
            If Not _inEsecuzioneAudio OrElse EStreamLiveCorrente() Then Return
            If _indiceAudio < _elementiAudio.Count - 1 Then
                _indiceAudio += 1
                GestisciRiproduzione()
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    public Sub TracciaPrecedente()
        Try
            If Not _inEsecuzioneAudio OrElse EStreamLiveCorrente() Then Return
            If _indiceAudio > 0 Then
                _indiceAudio -= 1
                GestisciRiproduzione()
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

#Region "Per il pannello del centro di controllo"

    <DllImport("user32.dll")>
    Private Shared Sub keybd_event(bVk As Byte, bScan As Byte, dwFlags As Integer, dwExtraInfo As IntPtr)
    End Sub

    ' Tasti multimediali: comandano i lettori di musica e video aperti fuori da WinGabriel.
    Private Shared Sub TastoMultimediale(tasto As Byte)
        Try
            keybd_event(tasto, 0, 0, IntPtr.Zero)
            keybd_event(tasto, 0, 2, IntPtr.Zero)
        Catch ex As Exception
            _logStatico.ScriviLog("TastoMultimediale: " & ex.Message)
        End Try
    End Sub

    Private Shared ReadOnly _logStatico As New wgcontrols.Logger()

    ''' <summary>C'è un ascolto del centro di controllo (brani o radio).</summary>
    Friend ReadOnly Property AscoltoInCorso As Boolean
        Get
            Return _inEsecuzioneAudio AndAlso _mpv IsNot Nothing
        End Get
    End Property

    Friend ReadOnly Property AscoltoInPausa As Boolean
        Get
            Try
                Return _mpv IsNot Nothing AndAlso _mpv.IsPaused
            Catch
                Return False
            End Try
        End Get
    End Property

    Friend Function DescrizioneAscolto() As String
        Try
            Return If(AscoltoInCorso, TestoOperazioneAudio(), "")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    ''' <summary>Brano precedente: dell'ascolto del centro di controllo, altrimenti del lettore di sistema.</summary>
    Friend Sub BranoPrecedente()
        If AscoltoInCorso Then TracciaPrecedente() Else TastoMultimediale(&HB1)
    End Sub

    Friend Sub BranoSuccessivo()
        If AscoltoInCorso Then TracciaSuccessiva() Else TastoMultimediale(&HB0)
    End Sub

    Friend Sub RiproduciPausa()
        If AscoltoInCorso Then PlayPausa() Else TastoMultimediale(&HB3)
    End Sub

    ''' <summary>
    ''' Le voci del centro di controllo (batteria, verifica della connessione, operazioni in corso, ascolto,
    ''' voci aggiunte dalle applicazioni), con il testo di adesso e l'azione: le usa il pannello nuovo.
    ''' </summary>
    Friend Function VociOperazioni() As List(Of Tuple(Of String, Action))
        Dim voci As New List(Of Tuple(Of String, Action))()
        Try
            ' Senza batteria (computer fisso) la voce non c'è.
            If WGStatoDispositivo.Batteria().Presente Then voci.Add(Tuple.Create(Of String, Action)(OttieniStatoBatteria(), AddressOf MostraInfoBatteria))
            voci.Add(Tuple.Create(Of String, Action)("Verifica connessione internet", AddressOf ControllaInternet))
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim controllo As New ControlloLavoro(Me, lavoro, _log)
                voci.Add(Tuple.Create(Of String, Action)(EtichettaLavoro(lavoro), AddressOf controllo.ApriMenu))
            Next
            If _inEsecuzioneAudio Then
                voci.Add(Tuple.Create(Of String, Action)(TestoOperazioneAudio(), If(EStreamLiveCorrente(), New Action(AddressOf ApriMenuAudioStream), New Action(AddressOf ApriMenuAudioFile))))
            End If
            If _inDownloadDipendenze Then
                voci.Add(Tuple.Create(Of String, Action)(EtichettaOperazione("Download dipendenze programma", PercentualeDipendenze()), AddressOf ApriMenuDownloadDipendenze))
            End If
            For Each voce As VoceEsterna In _vociEsterne.ToList()
                Dim esecutore As New EsecutoreVoceEsterna(_log)
                esecutore.Azione = voce.Azione
                Dim testoVoce As String = voce.Nome
                If voce.Etichetta IsNot Nothing Then
                    Try
                        testoVoce = voce.Etichetta()
                    Catch exEtichetta As Exception
                        _log.ScriviLog(exEtichetta.ToString())
                    End Try
                End If
                voci.Add(Tuple.Create(Of String, Action)(testoVoce, AddressOf esecutore.Esegui))
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return voci
    End Function

#End Region

    public Sub PlayPausa()
        Try
            If _mpv Is Nothing Then Return
            If _mpv.IsPaused Then
                If EStreamLiveCorrente() Then
                    GestisciRiproduzione()
                Else
                    _mpv.Riproduci()
                End If
            Else
                _mpv.Pausa()
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' --- FINE GESTIONE AUDIO ---

    Private ReadOnly Property ConnectionString As String
        Get
            Try
                Dim pathDb As String = ""
                Try
                    pathDb = WGDatabase.PercorsoFileDati("Libri", NOME_FILE_DB)
                Catch
                    pathDb = ""
                End Try
                If String.IsNullOrWhiteSpace(pathDb) Then
                    pathDb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_LIBRI, NOME_FILE_DB)
                End If
                Return $"Data Source={pathDb};Version=3;"
            Catch ex As Exception
                _log.ScriviLog(ex.ToString())
                Return ""
            End Try
        End Get
    End Property

    Private ReadOnly Property ConnectionStringTesti As String
        Get
            Try
                Dim pathDb As String = ""
                Try
                    pathDb = WGDatabase.PercorsoFileDati("Libri", NOME_FILE_DB_TESTI)
                Catch
                    pathDb = ""
                End Try
                If String.IsNullOrWhiteSpace(pathDb) Then
                    pathDb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_LIBRI, NOME_FILE_DB_TESTI)
                End If
                Return $"Data Source={pathDb};Version=3;"
            Catch ex As Exception
                _log.ScriviLog(ex.ToString())
                Return ""
            End Try
        End Get
    End Property

    Private ReadOnly Property ConnectionStringArchivioInternet As String
        Get
            Try
                Dim pathDb As String = ""
                Try
                    pathDb = WGDatabase.PercorsoFileDati("InternetArchive", NOME_FILE_DB_ARCHIVE)
                Catch
                    pathDb = ""
                End Try
                If String.IsNullOrWhiteSpace(pathDb) Then
                    pathDb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CARTELLA_DATI_ARCHIVE, NOME_FILE_DB_ARCHIVE)
                End If
                Return $"Data Source={pathDb};Version=3;"
            Catch ex As Exception
                _log.ScriviLog(ex.ToString())
                Return ""
            End Try
        End Get
    End Property

    ' Rete di sicurezza: se mpv non ha segnalato la fine ma l'elemento (non live) è arrivato in fondo ed è fermo, l'ascolto è finito:
    ' non conta come un'operazione in corso e non deve far chiedere niente all'uscita.
    Private Function AscoltoGiaFinito() As Boolean
        Try
            If Not _inEsecuzioneAudio OrElse _mpv Is Nothing Then Return False
            If EStreamLiveCorrente() Then Return False
            Dim durata As Double = _mpv.Durata.TotalSeconds
            If durata <= 1 Then Return False
            Dim posizione As Double = _mpv.PosizioneTemporale.TotalSeconds
            If durata - posizione > 1 Then Return False
            If _indiceAudio < _elementiAudio.Count - 1 Then Return False
            Return True
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Function OttieniOperazioneInCorso() As StatoOperazioneCorrente
        Try
            If CiSonoOperazioniFileInCorso() Then Return StatoOperazioneCorrente.File
            If CiSonoOperazioniFtpInCorso() Then Return StatoOperazioneCorrente.Ftp
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.Video) Then Return StatoOperazioneCorrente.Video
            If CiSonoOperazioniArchivioInCorso() Then Return StatoOperazioneCorrente.Archivio
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.LibriVox) Then Return StatoOperazioneCorrente.LibriVox
            If _inEsecuzioneAudio AndAlso Not AscoltoGiaFinito() Then Return StatoOperazioneCorrente.Audio
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.Bluetooth) Then Return StatoOperazioneCorrente.Bluetooth
            If _inDownloadDipendenze Then Return StatoOperazioneCorrente.DownloadDipendenze
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.Testi) Then Return StatoOperazioneCorrente.Testi
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.ArchivioInternet) Then Return StatoOperazioneCorrente.ArchivioInternet
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.ConversioneMp3) Then Return StatoOperazioneCorrente.ConversioneMp3
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.RiscritturaAi) Then Return StatoOperazioneCorrente.RiscritturaAi
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.RiconoscimentoSpartiti) Then Return StatoOperazioneCorrente.RiconoscimentoSpartiti
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.ScaricamentoModelli) Then Return StatoOperazioneCorrente.ScaricamentoModelli
            If CiSonoLavoriDiTipo(StatoOperazioneCorrente.ScaricamentoYoutube) Then Return StatoOperazioneCorrente.ScaricamentoYoutube
            Return StatoOperazioneCorrente.Nessuna
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return StatoOperazioneCorrente.Nessuna
        End Try
    End Function

    Public Function CiSonoDownloadDipendenzeInCorso() As Boolean
        Try
            Return _inDownloadDipendenze
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub ImpostaFaseDipendenze(descrizione As String, baseIniziale As Double, peso As Double)
        Try
            _statoDownloadDipendenze = descrizione
            _baseProgressoDipendenze = baseIniziale
            _pesoProgressoDipendenze = peso
            _statoDownloadDipendenzeHttp.Azzera(descrizione)
            PercentualeDipendenze()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub CompletaFaseDipendenze(baseFinale As Double)
        Try
            _baseProgressoDipendenze = baseFinale
            _pesoProgressoDipendenze = 0
            _statoDownloadDipendenzeHttp.Azzera("")
            _progressoDownloadDipendenze = CInt(Math.Round(baseFinale, 0))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ControllaEScaricaDipendenze()
        Try
            _inDownloadDipendenze = True
            _baseProgressoDipendenze = 0
            _pesoProgressoDipendenze = 0
            _progressoDownloadDipendenze = 0
            _statoDownloadDipendenze = "Avvio controllo dipendenze..."

            Dim basePath As String = AppDomain.CurrentDomain.BaseDirectory
            Dim downloadDir As String = Path.Combine(basePath, "DownloadTemp")

            If Directory.Exists(downloadDir) Then
                Directory.Delete(downloadDir, True)
            End If
            Directory.CreateDirectory(downloadDir)

            Dim client As New HttpClient()
            client.Timeout = TimeSpan.FromMinutes(20)

            Dim p7z As String = Path.Combine(basePath, _path7z)
            If Not File.Exists(p7z) Then
                Try
                    ImpostaFaseDipendenze("Download di 7z...", 0, 10)
                    Dim url7z As String = ""
                    If Environment.Is64BitOperatingSystem Then
                        url7z = "https://github.com/develar/7zip-bin/raw/master/win/x64/7za.exe"
                    Else
                        url7z = "https://github.com/develar/7zip-bin/raw/master/win/ia32/7za.exe"
                    End If
                    ScaricaFileHttpConProgresso(client, url7z, p7z, _statoDownloadDipendenzeHttp, Nothing)
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            End If
            CompletaFaseDipendenze(10)

            Dim pYtDlp As String = Path.Combine(basePath, _pathYtDlp)
            If Not File.Exists(pYtDlp) Then
                Try
                    ImpostaFaseDipendenze("Download di yt-dlp...", 10, 5)
                    ' l'impronta pubblicata insieme al file: se c'è si controlla, se non si riesce a leggerla si scarica comunque
                    Dim improntaYtDlp As String = ImprontaDaElenco(ScaricaTestoHttp(client, "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS"), "yt-dlp.exe")
                    ScaricaFileHttpConProgresso(client, "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", pYtDlp, _statoDownloadDipendenzeHttp, Nothing, improntaYtDlp)
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            End If
            CompletaFaseDipendenze(15)

            ' Deno, il motore JavaScript che yt-dlp usa per leggere YouTube: senza, mancano dei formati e prima o poi non funziona più
            Dim pDeno As String = Path.Combine(basePath, "deno.exe")
            If Not File.Exists(pDeno) AndAlso Environment.Is64BitOperatingSystem Then
                Try
                    ImpostaFaseDipendenze("Download del motore JavaScript per yt-dlp...", 15, 15)
                    Dim zipDeno As String = Path.Combine(downloadDir, "deno.zip")
                    Dim estraiDeno As String = Path.Combine(downloadDir, "deno_extracted")
                    Directory.CreateDirectory(estraiDeno)
                    ' il file .sha256sum di Deno è l'uscita di Get-FileHash ("Hash : impronta")
                    Dim improntaDeno As String = ""
                    Dim corrispondenzaDeno As System.Text.RegularExpressions.Match = System.Text.RegularExpressions.Regex.Match(ScaricaTestoHttp(client, "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip.sha256sum"), "Hash\s*:\s*([0-9A-Fa-f]{64})")
                    If corrispondenzaDeno.Success Then improntaDeno = corrispondenzaDeno.Groups(1).Value
                    If ScaricaFileHttpConProgresso(client, "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip", zipDeno, _statoDownloadDipendenzeHttp, Nothing, improntaDeno) Then
                        _statoDownloadDipendenze = "Estrazione del motore JavaScript..."
                        EstraiArchivio(zipDeno, estraiDeno, p7z)
                        Dim trovatoDeno As String = Directory.GetFiles(estraiDeno, "deno.exe", System.IO.SearchOption.AllDirectories).FirstOrDefault()
                        If Not String.IsNullOrEmpty(trovatoDeno) Then File.Move(trovatoDeno, pDeno)
                    End If
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            End If
            CompletaFaseDipendenze(30)

            Dim pathFfmpeg As String = Path.Combine(basePath, "ffmpeg.exe")
            If Not File.Exists(pathFfmpeg) Then
                Try
                    ImpostaFaseDipendenze("Download di FFmpeg...", 30, 35)
                    Dim urlFfmpeg As String = ""
                    If Environment.Is64BitOperatingSystem Then
                        urlFfmpeg = "https://github.com/kusaanko/FFmpeg-Auto-Build/releases/download/workflow-202501150548/win64_win64_20250115.zip"
                    Else
                        urlFfmpeg = "https://github.com/kusaanko/FFmpeg-Auto-Build/releases/download/workflow-202501150548/win32_win32_20250115.zip"
                    End If

                    Dim zipPath As String = Path.Combine(downloadDir, "ffmpeg.zip")
                    Dim extractPath As String = Path.Combine(downloadDir, "ffmpeg_extracted")
                    Directory.CreateDirectory(extractPath)

                    If ScaricaFileHttpConProgresso(client, urlFfmpeg, zipPath, _statoDownloadDipendenzeHttp, Nothing) Then
                        _statoDownloadDipendenze = "Estrazione di FFmpeg..."
                        EstraiArchivio(zipPath, extractPath, p7z)

                        Dim foundFile As String = Directory.GetFiles(extractPath, "ffmpeg.exe", System.IO.SearchOption.AllDirectories).FirstOrDefault()
                        If Not String.IsNullOrEmpty(foundFile) Then
                            File.Move(foundFile, pathFfmpeg)
                        End If
                    End If
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            End If
            CompletaFaseDipendenze(65)

            Dim pathNvdaDll As String = Path.Combine(basePath, "nvdaControllerClient.dll")
            If Not File.Exists(pathNvdaDll) Then
                Try
                    ImpostaFaseDipendenze("Download di NVDA Controller...", 65, 35)
                    Dim urlNvda As String = "https://download.nvaccess.org/releases/stable/nvda_2025.3.2_controllerClient.zip"

                    Dim zipPath As String = Path.Combine(downloadDir, "nvda.zip")
                    Dim extractPath As String = Path.Combine(downloadDir, "nvda_extracted")
                    Directory.CreateDirectory(extractPath)

                    If ScaricaFileHttpConProgresso(client, urlNvda, zipPath, _statoDownloadDipendenzeHttp, Nothing) Then
                        _statoDownloadDipendenze = "Estrazione di NVDA..."
                        EstraiArchivio(zipPath, extractPath, p7z)

                        Dim archFolder As String = If(Environment.Is64BitOperatingSystem, "x64", "x86")
                        Dim foundFile As String = Directory.GetFiles(extractPath, "nvdaControllerClient.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault(Function(f) f.Contains(Path.DirectorySeparatorChar & archFolder & Path.DirectorySeparatorChar))

                        If Not String.IsNullOrEmpty(foundFile) Then
                            File.Move(foundFile, pathNvdaDll)
                        End If
                    End If
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            End If
            CompletaFaseDipendenze(100)
            _statoDownloadDipendenze = "Completato"

            Try
                If Directory.Exists(downloadDir) Then
                    Directory.Delete(downloadDir, True)
                End If
            Catch ex As Exception
                _log.ScriviLog(ex.ToString())
            End Try

        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            _inDownloadDipendenze = False
            RaiseEvent DownloadDipendenzeCompletato()
        End Try
    End Sub

    Private Sub EstraiArchivio(src As String, dest As String, exe7z As String)
        Try
            Dim args As String = $"x ""{src}"" -o""{dest}"" -y"
            Dim psi As New ProcessStartInfo(exe7z, args) With {
                .WindowStyle = ProcessWindowStyle.Hidden,
                .CreateNoWindow = True,
                .UseShellExecute = False
            }
            Dim p As Process = Process.Start(psi)
            p.WaitForExit()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiVoceMenu(nomeVoce As String, azioneDaEseguire As Action)
        Try
            Dim voce As New VoceEsterna With {.Nome = nomeVoce, .Azione = azioneDaEseguire}
            If Not _vociEsterne.Any(Function(v) v.Nome = nomeVoce) Then
                _vociEsterne.Add(voce)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Voce che resta nel centro di controllo finché non la si toglie, con un testo ricalcolato ogni volta
    ''' che il centro si apre (per esempio "Ricerca file: 120 risultati, in corso"). chiave la identifica.
    ''' </summary>
    Public Sub AggiungiVoceMenuDinamica(chiave As String, etichetta As Func(Of String), azioneDaEseguire As Action)
        Try
            _vociEsterne.RemoveAll(Function(v) v.Nome = chiave)
            _vociEsterne.Add(New VoceEsterna With {.Nome = chiave, .Azione = azioneDaEseguire, .Etichetta = etichetta})
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub RimuoviVoceMenu(nomeVoce As String)
        Try
            Dim voce As VoceEsterna = _vociEsterne.FirstOrDefault(Function(v) v.Nome = nomeVoce)
            If Not String.IsNullOrEmpty(voce.Nome) Then
                _vociEsterne.Remove(voce)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Function CiSonoOperazioniInCorso() As Boolean
        Try
            Return CiSonoOperazioniFileInCorso() OrElse CiSonoOperazioniFtpInCorso() OrElse CiSonoOperazioniArchivioInCorso() OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.Video) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.LibriVox) OrElse _inDownloadDipendenze OrElse (_inEsecuzioneAudio AndAlso Not AscoltoGiaFinito()) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.Bluetooth) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.Testi) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.ArchivioInternet) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.ConversioneMp3) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.RiscritturaAi) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.RiconoscimentoSpartiti) OrElse CiSonoLavoriDiTipo(StatoOperazioneCorrente.ScaricamentoYoutube)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return True
        End Try
    End Function

    Public Function CiSonoOperazioniFileInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.File)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Function CiSonoOperazioniArchivioInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.Archivio)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Function CiSonoOperazioniLibriVoxInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.LibriVox)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return True
        End Try
    End Function

    Public Function CiSonoOperazioniTestiInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.Testi)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return True
        End Try
    End Function

    Public Function CiSonoOperazioniArchivioInternetInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.ArchivioInternet)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return True
        End Try
    End Function

    Public Function CiSonoOperazioniVideoInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.Video)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return True
        End Try
    End Function

    Public Function CiSonoOperazioniBluetoothInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.Bluetooth)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return True
        End Try
    End Function

    Private Function RisorsaOccupata(risorsa As String) As Boolean
        Try
            Dim valore As String = NormalizzaRisorsa(risorsa)
            If String.IsNullOrEmpty(valore) Then Return False
            For Each lavoro As LavoroBase In LavoriAttivi()
                If Not lavoro.InEsecuzione Then Continue For
                SyncLock lavoro.Blocco
                    If lavoro.Risorse.Contains(valore) Then Return True
                End SyncLock
            Next
            Return False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub GestisciConflittoOperazione(descrizioneCategoria As String)
        Try
            _descrizioneConflitto = descrizioneCategoria
            EseguiSuThreadInterfaccia(AddressOf MostraConflittoOperazione)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraConflittoOperazione()
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Un'altra operazione sta già usando questo elemento: " & _descrizioneConflitto & "." & vbCrLf & "Attendere il termine oppure gestirla dal Centro di Controllo.")
            messaggio.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function ChiediSovrascrittura(descrizioneElemento As String, contesto As ContestoSovrascrittura) As Boolean
        Try
            If contesto IsNot Nothing Then
                If contesto.Decisione = DecisioneSovrascrittura.SovrascriviTutti Then Return True
                If contesto.Decisione = DecisioneSovrascrittura.SaltaTutti Then Return False
            End If

            SyncLock _lockDomandaSovrascrittura
                If contesto IsNot Nothing Then
                    If contesto.Decisione = DecisioneSovrascrittura.SovrascriviTutti Then Return True
                    If contesto.Decisione = DecisioneSovrascrittura.SaltaTutti Then Return False
                End If

                _rispostaSovrascrittura = False
                _rispostaSovrascritturaRicevuta = False
                _contestoSovrascritturaAttivo = contesto
                _descrizioneSovrascritturaAttiva = descrizioneElemento

                Using evento As New ManualResetEventSlim(False)
                    _eventoRispostaSovrascrittura = evento
                    Try
                        EseguiSuThreadInterfaccia(AddressOf MostraMenuSovrascrittura)
                        If Not evento.Wait(TIMEOUT_DOMANDA_SOVRASCRITTURA_MS) Then
                            _rispostaSovrascrittura = False
                        End If
                    Finally
                        _eventoRispostaSovrascrittura = Nothing
                        _contestoSovrascritturaAttivo = Nothing
                        _descrizioneSovrascritturaAttiva = ""
                    End Try
                End Using
                Return _rispostaSovrascrittura
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub MostraMenuSovrascrittura()
        Try
            _menuSovrascrittura = New WGMenu()
            AddHandler _menuSovrascrittura.abbandonato, AddressOf SovrascritturaMenuAbbandonato
            _menuSovrascrittura.aggiungi("Sovrascrivi", AddressOf SovrascriviElementoCorrente)
            _menuSovrascrittura.aggiungi("Non sovrascrivere", AddressOf SaltaElementoCorrente)
            _menuSovrascrittura.aggiungi("Sovrascrivi tutti", AddressOf SovrascriviTuttiGliElementi)
            _menuSovrascrittura.aggiungi("Non sovrascrivere nessuno", AddressOf SaltaTuttiGliElementi)
            _menuSovrascrittura.avvia("L'elemento " & _descrizioneSovrascritturaAttiva & " esiste già, cosa vuoi fare?", True)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            RegistraRispostaSovrascrittura(False, DecisioneSovrascrittura.Chiedi)
        End Try
    End Sub

    Private Sub SovrascriviElementoCorrente()
        Try
            RegistraRispostaSovrascrittura(True, DecisioneSovrascrittura.Chiedi)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SaltaElementoCorrente()
        Try
            RegistraRispostaSovrascrittura(False, DecisioneSovrascrittura.Chiedi)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SovrascriviTuttiGliElementi()
        Try
            RegistraRispostaSovrascrittura(True, DecisioneSovrascrittura.SovrascriviTutti)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SaltaTuttiGliElementi()
        Try
            RegistraRispostaSovrascrittura(False, DecisioneSovrascrittura.SaltaTutti)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SovrascritturaMenuAbbandonato()
        Try
            If _rispostaSovrascritturaRicevuta Then Return
            RegistraRispostaSovrascrittura(False, DecisioneSovrascrittura.Chiedi)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RegistraRispostaSovrascrittura(sovrascrivi As Boolean, decisione As DecisioneSovrascrittura)
        Try
            If _rispostaSovrascritturaRicevuta Then Return
            _rispostaSovrascritturaRicevuta = True
            _rispostaSovrascrittura = sovrascrivi

            If decisione <> DecisioneSovrascrittura.Chiedi AndAlso _contestoSovrascritturaAttivo IsNot Nothing Then
                _contestoSovrascritturaAttivo.Decisione = decisione
            End If

            If _menuSovrascrittura IsNot Nothing Then
                RemoveHandler _menuSovrascrittura.abbandonato, AddressOf SovrascritturaMenuAbbandonato
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            _rispostaSovrascrittura = False
        Finally
            Try
                Dim evento As ManualResetEventSlim = _eventoRispostaSovrascrittura
                If evento IsNot Nothing Then evento.Set()
            Catch exSegnale As Exception
                _log.ScriviLog(exSegnale.ToString())
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' Verifica se il file di destinazione locale esiste gia' e, in tal caso, chiede
    ''' conferma. Restituisce True quando si puo' procedere.
    ''' </summary>
    Private Function ConsentitaScritturaLocale(percorsoDestinazione As String, contesto As ContestoSovrascrittura) As Boolean
        Try
            If String.IsNullOrEmpty(percorsoDestinazione) Then Return True
            If Not System.IO.File.Exists(percorsoDestinazione) Then Return True
            Return ChiediSovrascrittura(System.IO.Path.GetFileName(percorsoDestinazione), contesto)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Token di annullamento delle operazioni FTP: viene passato alla classe CLSFtp cosi'
    ''' che l'annullamento interrompa subito il trasferimento in corso, senza aspettare che
    ''' il file corrente finisca.
    ''' </summary>
    Private Function TokenAnnullamentoFtp(lavoro As LavoroFtp) As CancellationToken
        Try
            If lavoro Is Nothing Then Return CancellationToken.None
            Dim sorgente As CancellationTokenSource = lavoro.Annullamento
            If sorgente Is Nothing Then Return CancellationToken.None
            Return sorgente.Token
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return CancellationToken.None
        End Try
    End Function

    Private Sub RegistraLavoro(lavoro As LavoroBase, descrizione As String, tipo As StatoOperazioneCorrente)
        Try
            If lavoro Is Nothing Then Return
            lavoro.Descrizione = descrizione
            lavoro.Tipo = tipo
            lavoro.InPreparazione = True
            SyncLock _lockLavori
                _contatoreLavori += 1
                lavoro.Identificativo = _contatoreLavori
                _lavori.Add(lavoro)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RimuoviLavoro(lavoro As LavoroBase)
        Try
            If lavoro Is Nothing Then Return
            SyncLock _lockLavori
                _lavori.Remove(lavoro)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriAttivi() As List(Of LavoroBase)
        Try
            SyncLock _lockLavori
                Return New List(Of LavoroBase)(_lavori)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroBase)()
        End Try
    End Function

    Private Function LavoroInCorso(lavoro As LavoroBase) As Boolean
        Try
            If lavoro Is Nothing Then Return False
            Return lavoro.InPreparazione OrElse lavoro.InAttesa OrElse lavoro.InEsecuzione
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function CiSonoLavoriDiTipo(tipo As StatoOperazioneCorrente) As Boolean
        Try
            For Each lavoro As LavoroBase In LavoriAttivi()
                If lavoro.Tipo = tipo AndAlso LavoroInCorso(lavoro) Then Return True
            Next
            Return False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function NormalizzaRisorsa(valore As String) As String
        Try
            If String.IsNullOrWhiteSpace(valore) Then Return ""
            Dim risultato As String = valore.Trim()
            If risultato.StartsWith(WPD_PREFIX) OrElse risultato.Contains("://") Then
                Return risultato.TrimEnd("/"c)
            End If

            ' Converte forme tipo "/C:/..." o "/C:\..." (non supportate da Path.GetFullPath) in "C:\..."
            If risultato.Length >= 3 AndAlso risultato(0) = "/"c AndAlso Char.IsLetter(risultato(1)) AndAlso risultato(2) = ":"c Then
                risultato = risultato.Substring(1)
            End If
            risultato = risultato.Replace("/"c, "\"c)

            Try
                If (risultato.Contains(":") OrElse risultato.StartsWith("\"c)) AndAlso System.IO.Path.IsPathRooted(risultato) Then
                    ' Path.GetFullPath può generare NotSupportedException / ArgumentException
                    ' su percorsi speciali o malformati (es. $Recycle.Bin con formato errato)
                    risultato = System.IO.Path.GetFullPath(risultato)
                End If
            Catch
                ' Percorso non supportato o non valido: si lascia il valore già normalizzato, senza log
            End Try
            Return risultato.TrimEnd("\"c, "/"c)
        Catch
            Return ""
        End Try
    End Function

    Private Sub AggiungiRisorsa(lavoro As LavoroBase, risorsa As String)
        Try
            If lavoro Is Nothing Then Return
            Dim valore As String = NormalizzaRisorsa(risorsa)
            If String.IsNullOrEmpty(valore) Then Return
            SyncLock lavoro.Blocco
                lavoro.Risorse.Add(valore)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function CondividonoRisorse(primo As LavoroBase, secondo As LavoroBase) As Boolean
        Try
            If primo Is Nothing OrElse secondo Is Nothing Then Return False
            Dim risorsePrimo As List(Of String)
            Dim risorseSecondo As List(Of String)
            SyncLock primo.Blocco
                risorsePrimo = New List(Of String)(primo.Risorse)
            End SyncLock
            SyncLock secondo.Blocco
                risorseSecondo = New List(Of String)(secondo.Risorse)
            End SyncLock
            If risorsePrimo.Count = 0 OrElse risorseSecondo.Count = 0 Then Return False

            Dim insieme As New HashSet(Of String)(risorseSecondo, StringComparer.OrdinalIgnoreCase)
            For Each risorsa As String In risorsePrimo
                If insieme.Contains(risorsa) Then Return True
                For Each altra As String In risorseSecondo
                    If PercorsoContenutoIn(risorsa, altra) OrElse PercorsoContenutoIn(altra, risorsa) Then Return True
                Next
            Next
            Return False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return True
        End Try
    End Function

    Private Function PercorsoContenutoIn(interno As String, contenitore As String) As Boolean
        Try
            If String.IsNullOrEmpty(interno) OrElse String.IsNullOrEmpty(contenitore) Then Return False
            If interno.Length <= contenitore.Length Then Return False
            If Not interno.StartsWith(contenitore, StringComparison.OrdinalIgnoreCase) Then Return False
            Dim separatore As Char = interno(contenitore.Length)
            Return separatore = "\"c OrElse separatore = "/"c
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function TrovaLavoroInConflitto(lavoro As LavoroBase) As LavoroBase
        Try
            For Each altro As LavoroBase In _lavori
                If altro Is lavoro Then Continue For
                If Not altro.InEsecuzione Then Continue For
                If CondividonoRisorse(lavoro, altro) Then Return altro
            Next
            Return Nothing
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function AttendiRisorseLibere(lavoro As LavoroBase) As Boolean
        Try
            If lavoro Is Nothing Then Return False

            While True
                If lavoro.Annulla Then Return False

                Dim bloccante As LavoroBase = Nothing
                SyncLock _lockLavori
                    bloccante = TrovaLavoroInConflitto(lavoro)
                    If bloccante Is Nothing Then
                        lavoro.InAttesa = False
                        lavoro.MotivoAttesa = ""
                        lavoro.InEsecuzione = True
                        Return True
                    End If
                End SyncLock

                lavoro.InAttesa = True
                lavoro.MotivoAttesa = bloccante.Descrizione
                Thread.Sleep(300)
            End While

            Return False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function CreaLavoroFile(descrizione As String) As LavoroFile
        Try
            Dim lavoro As New LavoroFile()
            RegistraLavoro(lavoro, descrizione, StatoOperazioneCorrente.File)
            Return lavoro
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub RimuoviLavoroFile(lavoro As LavoroFile)
        Try
            RimuoviLavoro(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriFileAttivi() As List(Of LavoroFile)
        Try
            Dim risultato As New List(Of LavoroFile)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroFile As LavoroFile = TryCast(lavoro, LavoroFile)
                If lavoroFile IsNot Nothing Then risultato.Add(lavoroFile)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroFile)()
        End Try
    End Function

    Private Sub AvviaPreparazioneLavoroFile(modalita As ModalitaPreparazioneFile, descrizione As String, sorgenti As List(Of String), destinazione As String, dispositivo As String, percorsoRemoto As String)
        Try
            Dim lavoro As LavoroFile = CreaLavoroFile(descrizione)
            If lavoro Is Nothing Then Return

            Dim preparatore As New PreparatoreLavoroFile(_log)
            preparatore.Centro = Me
            preparatore.Lavoro = lavoro
            preparatore.Modalita = modalita
            preparatore.Sorgenti = sorgenti
            preparatore.Destinazione = destinazione
            preparatore.DispositivoDestinazione = dispositivo
            preparatore.PercorsoRemoto = percorsoRemoto

            Dim threadPreparazione As New Thread(AddressOf preparatore.Esegui)
            threadPreparazione.IsBackground = True
            threadPreparazione.Start()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub PreparaLavoroFile(preparatore As PreparatoreLavoroFile)
        Dim lavoro As LavoroFile = Nothing
        Try
            lavoro = preparatore.Lavoro
            Dim sorgenti As List(Of String) = preparatore.Sorgenti
            Dim destinazione As String = preparatore.Destinazione

            Select Case preparatore.Modalita
                Case ModalitaPreparazioneFile.DirectoryCopia, ModalitaPreparazioneFile.DirectorySposta, ModalitaPreparazioneFile.DirectoryElimina
                    For Each sorgente As String In sorgenti
                        If System.IO.Directory.Exists(sorgente) Then
                            If preparatore.Modalita = ModalitaPreparazioneFile.DirectoryElimina Then
                                AggiungiTask(lavoro, TipoOperazioneFile.Elimina, sorgente, "", 0)
                            Else
                                Dim infoDir As New System.IO.DirectoryInfo(sorgente)
                                For Each file As System.IO.FileInfo In infoDir.GetFiles("*", System.IO.SearchOption.AllDirectories)
                                    Dim relativo As String = file.FullName.Substring(sorgente.Length + 1)
                                    Dim destFinale As String = System.IO.Path.Combine(destinazione, relativo)
                                    Dim tipo As TipoOperazioneFile = If(preparatore.Modalita = ModalitaPreparazioneFile.DirectoryCopia, TipoOperazioneFile.Copia, TipoOperazioneFile.Sposta)
                                    AggiungiTask(lavoro, tipo, file.FullName, destFinale, file.Length)
                                Next
                            End If
                        ElseIf System.IO.File.Exists(sorgente) Then
                            If preparatore.Modalita = ModalitaPreparazioneFile.DirectoryElimina Then
                                AggiungiTask(lavoro, TipoOperazioneFile.Elimina, sorgente, "", 0)
                            Else
                                Dim nomeFile As String = System.IO.Path.GetFileName(sorgente)
                                Dim destFinale As String = System.IO.Path.Combine(destinazione, nomeFile)
                                Dim dimensione As Long = New System.IO.FileInfo(sorgente).Length
                                Dim tipo As TipoOperazioneFile = If(preparatore.Modalita = ModalitaPreparazioneFile.DirectoryCopia, TipoOperazioneFile.Copia, TipoOperazioneFile.Sposta)
                                AggiungiTask(lavoro, tipo, sorgente, destFinale, dimensione)
                            End If
                        End If
                    Next

                Case ModalitaPreparazioneFile.Copia, ModalitaPreparazioneFile.Sposta
                    Dim tipoElemento As TipoOperazioneFile = If(preparatore.Modalita = ModalitaPreparazioneFile.Copia, TipoOperazioneFile.Copia, TipoOperazioneFile.Sposta)
                    For Each sorgente As String In sorgenti
                        If System.IO.Directory.Exists(sorgente) Then
                            Dim infoDir As New System.IO.DirectoryInfo(sorgente)
                            Dim nomeCartella As String = infoDir.Name
                            Dim cartellaDestinazione As String = System.IO.Path.Combine(destinazione, nomeCartella)

                            If Not CreaCartellaLocaleSicura(cartellaDestinazione) Then
                                lavoro.MotivoInterruzione = "Permessi insufficienti per la cartella di destinazione: " & cartellaDestinazione
                                NotificaFineOperazione(lavoro.MotivoInterruzione)
                                Return
                            End If

                            For Each file As System.IO.FileInfo In infoDir.GetFiles("*", System.IO.SearchOption.AllDirectories)
                                Dim relativo As String = file.FullName.Substring(sorgente.Length)
                                If relativo.StartsWith("\") Then
                                    relativo = relativo.Substring(1)
                                End If
                                Dim destFinale As String = System.IO.Path.Combine(cartellaDestinazione, relativo)
                                AggiungiTask(lavoro, tipoElemento, file.FullName, destFinale, file.Length)
                            Next

                            If preparatore.Modalita = ModalitaPreparazioneFile.Sposta Then
                                AggiungiTask(lavoro, TipoOperazioneFile.Elimina, sorgente, "", 0)
                            End If
                        ElseIf System.IO.File.Exists(sorgente) Then
                            Dim dimensione As Long = New System.IO.FileInfo(sorgente).Length
                            Dim nomeFile As String = System.IO.Path.GetFileName(sorgente)
                            Dim destFinale As String = System.IO.Path.Combine(destinazione, nomeFile)
                            AggiungiTask(lavoro, tipoElemento, sorgente, destFinale, dimensione)
                        End If
                    Next

                Case ModalitaPreparazioneFile.Elimina
                    For Each sorgente As String In sorgenti
                        AggiungiTask(lavoro, TipoOperazioneFile.Elimina, sorgente, "", 0)
                    Next

                Case ModalitaPreparazioneFile.CopiaPortatile, ModalitaPreparazioneFile.SpostaPortatile
                    Dim eSpostamento As Boolean = (preparatore.Modalita = ModalitaPreparazioneFile.SpostaPortatile)
                    Dim dispositivoDestinazione As String = preparatore.DispositivoDestinazione
                    Dim percorsoRemoto As String = preparatore.PercorsoRemoto
                    Dim destDevice As MediaDevice = GetDevice(dispositivoDestinazione)
                    Dim daDispositivoADispositivo As Boolean = (destDevice IsNot Nothing)

                    For Each sorgente As String In sorgenti
                        If sorgente.StartsWith(WPD_PREFIX) Then
                            Dim parti As String() = sorgente.Split("|"c)
                            Dim identificativoDispositivo As String = parti(1)
                            Dim percorsoSorgenteWpd As String = parti(2)

                            If daDispositivoADispositivo Then
                                Dim nomeFile As String = System.IO.Path.GetFileName(percorsoSorgenteWpd)
                                Dim fileTemporaneo As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString() & "_" & nomeFile)
                                Dim dimensione As Long = OttieniDimensioneWPD(identificativoDispositivo, percorsoSorgenteWpd)

                                AggiungiTask(lavoro, TipoOperazioneFile.CopiaPortatile, sorgente, fileTemporaneo, dimensione)
                                Dim percorsoDestinazioneWpd As String = System.IO.Path.Combine(percorsoRemoto, nomeFile)
                                AggiungiTask(lavoro, TipoOperazioneFile.CopiaPortatile, fileTemporaneo, "", dimensione, dispositivoDestinazione, percorsoDestinazioneWpd)
                                If eSpostamento Then
                                    AggiungiTask(lavoro, TipoOperazioneFile.EliminaPortatile, "", "", 0, identificativoDispositivo, percorsoSorgenteWpd)
                                End If
                                AggiungiTask(lavoro, TipoOperazioneFile.Elimina, fileTemporaneo, "", 0)
                            Else
                                If IsWpdDirectory(identificativoDispositivo, percorsoSorgenteWpd) Then
                                    Dim nomeCartella As String = System.IO.Path.GetFileName(percorsoSorgenteWpd)
                                    Dim percorsoCompletoDestinazione As String = System.IO.Path.Combine(dispositivoDestinazione, nomeCartella)
                                    Dim tipoEsplorazione As TipoOperazioneFile = If(eSpostamento, TipoOperazioneFile.SpostaPortatile, TipoOperazioneFile.CopiaPortatile)
                                    EsploraEAccodaWPD(lavoro, identificativoDispositivo, percorsoSorgenteWpd, percorsoCompletoDestinazione, tipoEsplorazione)
                                    If eSpostamento Then
                                        AggiungiTask(lavoro, TipoOperazioneFile.EliminaPortatile, "", "", 0, identificativoDispositivo, percorsoSorgenteWpd)
                                    End If
                                Else
                                    Dim dimensione As Long = OttieniDimensioneWPD(identificativoDispositivo, percorsoSorgenteWpd)
                                    Dim tipoSingolo As TipoOperazioneFile = If(eSpostamento, TipoOperazioneFile.SpostaPortatile, TipoOperazioneFile.CopiaPortatile)
                                    AggiungiTask(lavoro, tipoSingolo, sorgente, dispositivoDestinazione, dimensione)
                                End If
                            End If
                        Else
                            If System.IO.Directory.Exists(sorgente) Then
                                Dim infoRadice As New System.IO.DirectoryInfo(sorgente)
                                Dim nomeRadice As String = infoRadice.Name
                                Dim elenchiFile As String() = System.IO.Directory.GetFiles(sorgente, "*", System.IO.SearchOption.AllDirectories)

                                For Each f As String In elenchiFile
                                    Dim dimensione As Long = New System.IO.FileInfo(f).Length
                                    Dim percorsoRelativo As String = f.Substring(sorgente.Length)
                                    If percorsoRelativo.StartsWith("\") Then
                                        percorsoRelativo = percorsoRelativo.Substring(1)
                                    End If
                                    Dim percorsoDestinazioneWpd As String = System.IO.Path.Combine(percorsoRemoto, nomeRadice, percorsoRelativo)
                                    Dim tipoFile As TipoOperazioneFile = If(eSpostamento, TipoOperazioneFile.SpostaPortatile, TipoOperazioneFile.CopiaPortatile)
                                    AggiungiTask(lavoro, tipoFile, f, "", dimensione, dispositivoDestinazione, percorsoDestinazioneWpd)
                                Next

                                If eSpostamento Then
                                    AggiungiTask(lavoro, TipoOperazioneFile.Elimina, sorgente, "", 0)
                                End If
                            ElseIf System.IO.File.Exists(sorgente) Then
                                Dim dimensione As Long = New System.IO.FileInfo(sorgente).Length
                                Dim nomeFile As String = System.IO.Path.GetFileName(sorgente)
                                Dim percorsoDestinazioneWpd As String = System.IO.Path.Combine(percorsoRemoto, nomeFile)
                                Dim tipoFile As TipoOperazioneFile = If(eSpostamento, TipoOperazioneFile.SpostaPortatile, TipoOperazioneFile.CopiaPortatile)
                                AggiungiTask(lavoro, tipoFile, sorgente, "", dimensione, dispositivoDestinazione, percorsoDestinazioneWpd)
                            End If
                        End If
                    Next

                Case ModalitaPreparazioneFile.EliminaPortatile
                    For Each percorsoRemotoSingolo As String In sorgenti
                        AggiungiTask(lavoro, TipoOperazioneFile.EliminaPortatile, "", "", 0, preparatore.DispositivoDestinazione, percorsoRemotoSingolo)
                    Next
            End Select
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InPreparazione = False
                    If lavoro.Coda.Count = 0 Then
                        RimuoviLavoroFile(lavoro)
                    Else
                        AvviaOperazioniFile(lavoro)
                    End If
                End If
            Catch exAvvio As Exception
                _log.ScriviLog(exAvvio.ToString())
            End Try
        End Try
    End Sub

    Public Sub AggiungiDirectoryOperazione(sorgente As String, destinazione As String, operazione As String)
        Try
            Dim modalita As ModalitaPreparazioneFile = ModalitaPreparazioneFile.DirectoryCopia
            If operazione = "ELIMINA" Then
                modalita = ModalitaPreparazioneFile.DirectoryElimina
            ElseIf operazione <> "COPIA" Then
                modalita = ModalitaPreparazioneFile.DirectorySposta
            End If
            AvviaPreparazioneLavoroFile(modalita, "Preparazione operazione directory...", New List(Of String) From {sorgente}, destinazione, "", "")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiCopia(sorgente As String, destinazione As String)
        Try
            AggiungiCopia(New List(Of String) From {sorgente}, destinazione)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiCopia(sorgenti As List(Of String), destinazione As String)
        Try
            AvviaPreparazioneLavoroFile(ModalitaPreparazioneFile.Copia, "Preparazione copia...", sorgenti, destinazione, "", "")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiSpostamento(sorgente As String, destinazione As String)
        Try
            AggiungiSpostamento(New List(Of String) From {sorgente}, destinazione)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiSpostamento(sorgenti As List(Of String), destinazione As String)
        Try
            AvviaPreparazioneLavoroFile(ModalitaPreparazioneFile.Sposta, "Preparazione spostamento...", sorgenti, destinazione, "", "")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiEliminazione(sorgente As String)
        Try
            AggiungiEliminazione(New List(Of String) From {sorgente})
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiEliminazione(sorgenti As List(Of String))
        Try
            AvviaPreparazioneLavoroFile(ModalitaPreparazioneFile.Elimina, "Preparazione eliminazione...", sorgenti, "", "", "")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Function CreaCartella(parentDir As String, nome As String) As Boolean
        Try
            If parentDir.StartsWith(WPD_PREFIX) Then

                Dim parts = parentDir.Split("|"c)
                Dim deviceId = parts(1)
                Dim parentPath = If(parts.Length > 2, parts(2), "\")
                Dim device As MediaDevice = GetDevice(deviceId)
                If device IsNot Nothing Then
                    device.Connect()
                    If Not device.IsConnected Then
                        Return False
                    End If
                    Try
                        Dim newPath = System.IO.Path.Combine(parentPath, nome)
                        device.CreateDirectory(newPath)
                        Return True
                    Catch ex As Exception
                        Dim messaggio As New WGMessaggi()
                        messaggio.messaggio("Non è stato possibile creare la cartella nel dispositivo portatile. Controllare i log per maggiori informazioni")
                        Messaggio.ShowDialog()
                        _log.ScriviLog(ex.ToString())
                        Return False
                    End Try
                End If
            Else

                Dim path As String = System.IO.Path.Combine(parentDir, nome)

                System.IO.Directory.CreateDirectory(path)
                If System.IO.Directory.Exists(path) Then
                    Return True
                Else
                    Dim p As New ProcessStartInfo("cmd.exe", $"/c mkdir ""{path}""")
                    p.WindowStyle = ProcessWindowStyle.Hidden
                    p.CreateNoWindow = True
                    p.UseShellExecute = False
                    Process.Start(p).WaitForExit()
                    If System.IO.Directory.Exists(path) Then
                        Return True
                    Else
                        Return False
                        Dim messaggio2 As New WGMessaggi()
                        messaggio2.Messaggio("Non è stato possibile creare la cartella nel percorso specificato")
                        messaggio2.Show()
                    End If
                End If
            End If

        Catch ex As Exception
                    Return False
            _log.ScriviLog(ex.Message & ex.StackTrace)
        End Try

    End Function

    ''' <summary>
    ''' Crea una cartella locale in modo sicuro. In caso di permessi insufficienti mostra un messaggio all'utente
    ''' e non scrive nel log, restituendo False.
    ''' </summary>
    Private Function CreaCartellaLocaleSicura(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            If System.IO.Directory.Exists(percorso) Then Return True
            System.IO.Directory.CreateDirectory(percorso)
            Return System.IO.Directory.Exists(percorso)
        Catch ex As UnauthorizedAccessException
            EseguiSuThreadInterfaccia(Sub()
                                          Dim msg As New WGMessaggi()
                                          msg.Messaggio("Permessi insufficienti." & vbCrLf &
                                                        "Impossibile creare o accedere alla cartella:" & vbCrLf &
                                                        percorso & vbCrLf &
                                                        "Scegliere un'altra destinazione o eseguire il programma con privilegi adeguati.")
                                          msg.ShowDialog()
                                      End Sub)
            Return False
        Catch ex As System.Security.SecurityException
            EseguiSuThreadInterfaccia(Sub()
                                          Dim msg As New WGMessaggi()
                                          msg.Messaggio("Permessi insufficienti." & vbCrLf &
                                                        "Impossibile creare o accedere alla cartella:" & vbCrLf &
                                                        percorso)
                                          msg.ShowDialog()
                                      End Sub)
            Return False
        Catch ex As System.IO.IOException
            ' Percorso non valido / disco pieno / altro: messaggio leggibile, nessun log
            EseguiSuThreadInterfaccia(Sub()
                                          Dim msg As New WGMessaggi()
                                          msg.Messaggio("Impossibile creare la cartella:" & vbCrLf &
                                                        percorso & vbCrLf &
                                                        ex.Message)
                                          msg.ShowDialog()
                                      End Sub)
            Return False
        Catch
            Return False
        End Try
    End Function

    Public Function RinominaElemento(currentDir As String, oldId As String, newName As String) As Boolean
        Try
            If oldId.StartsWith(WPD_PREFIX) Then
                Dim parts = oldId.Split("|"c)
                Dim deviceId = parts(1)
                Dim oldPath = parts(2)
                Dim device As MediaDevice = GetDevice(deviceId)
                If device IsNot Nothing Then
                    Try
                        device.Connect()
                        If Not device.IsConnected Then
                            Return False
                        End If
                        Dim parentDirWpd = System.IO.Path.GetDirectoryName(oldPath)
                        If device.FileExists(oldPath) Then
                            Dim infoFile As MediaFileInfo = device.GetFileInfo(oldPath)
                            infoFile.Rename(newName)
                            Return True
                        ElseIf device.DirectoryExists(oldPath) Then
                            Dim infoCartella As MediaDirectoryInfo = device.GetDirectoryInfo(oldPath)
                            infoCartella.Rename(newName)
                            Return True
                        Else
                            Return False
                        End If
                    Catch ex As Exception
                        _log.ScriviLog(ex.ToString())
                        Return False
                    Finally
                        Try
                            If device.IsConnected Then device.Disconnect()
                        Catch
                        End Try
                    End Try
                End If
                Return False
            Else
                Dim newPath As String = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(oldId), newName)

                If MoveFile(oldId, newPath) Then Return True
                Dim lastErr As Integer = Marshal.GetLastWin32Error()

                Try
                    If System.IO.Directory.Exists(oldId) Then
                        System.IO.Directory.Move(oldId, newPath)
                    Else
                        System.IO.File.Move(oldId, newPath)
                    End If
                    Return True
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try

                Try
                    Dim isDir As Boolean = System.IO.Directory.Exists(oldId)
                    Dim cmdArgs As String = $"/c move /Y ""{oldId}"" ""{newPath}"""
                    Dim p As New ProcessStartInfo("cmd.exe", cmdArgs)
                    p.WindowStyle = ProcessWindowStyle.Hidden
                    p.CreateNoWindow = True
                    p.UseShellExecute = False
                    Process.Start(p).WaitForExit()

                    If isDir Then
                        If System.IO.Directory.Exists(newPath) AndAlso Not System.IO.Directory.Exists(oldId) Then Return True
                    Else
                        If System.IO.File.Exists(newPath) AndAlso Not System.IO.File.Exists(oldId) Then Return True
                    End If
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try

                Return False
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Function ApriFile(percorso As String) As Boolean
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio("Questo tipo di file non è supportato dal programma.")
            messaggio.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return False
    End Function

    Public Function CiSonoOperazioniFtpInCorso() As Boolean
        Try
            Return CiSonoLavoriDiTipo(StatoOperazioneCorrente.Ftp)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function CreaLavoroFtp(descrizione As String) As LavoroFtp
        Try
            Dim lavoro As New LavoroFtp()
            RegistraLavoro(lavoro, descrizione, StatoOperazioneCorrente.Ftp)
            Return lavoro
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Sub RimuoviLavoroFtp(lavoro As LavoroFtp)
        Try
            RimuoviLavoro(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriFtpAttivi() As List(Of LavoroFtp)
        Try
            Dim risultato As New List(Of LavoroFtp)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroFtp As LavoroFtp = TryCast(lavoro, LavoroFtp)
                If lavoroFtp IsNot Nothing Then risultato.Add(lavoroFtp)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroFtp)()
        End Try
    End Function

    Private Sub AggiungiTaskFtp(lavoro As LavoroFtp, tipo As TipoOperazioneFtp, percorsoLocale As String, percorsoRemoto As String, dimensione As Long, server As InfoServerFtp, Optional eliminaSorgenteDopo As Boolean = False)
        Try
            If lavoro Is Nothing Then Return
            SyncLock lavoro.Blocco
                Dim t As New TaskFtp With {
                    .Tipo = tipo, .PercorsoLocale = percorsoLocale, .PercorsoRemoto = percorsoRemoto,
                    .Dimensione = dimensione, .Server = server, .EliminaSorgenteDopo = eliminaSorgenteDopo
                }
                lavoro.Coda.Add(t)
                lavoro.TotaleBytes += dimensione
            End SyncLock

            AggiungiRisorsa(lavoro, percorsoLocale)
            If Not String.IsNullOrEmpty(percorsoRemoto) AndAlso server IsNot Nothing Then
                AggiungiRisorsa(lavoro, "ftp://" & server.Host & "/" & percorsoRemoto.TrimStart("/"c))
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiUploadFtp(percorsoLocale As String, cartellaRemotaDestinazione As String, server As InfoServerFtp, Optional eliminaSorgenteLocale As Boolean = False)
        Try
            AggiungiUploadFtp(New List(Of String) From {percorsoLocale}, cartellaRemotaDestinazione, server, eliminaSorgenteLocale)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiUploadFtp(percorsiLocali As List(Of String), cartellaRemotaDestinazione As String, server As InfoServerFtp, Optional eliminaSorgenteLocale As Boolean = False)
        Try
            AvviaPreparazioneLavoroFtp(True, "Preparazione upload FTP...", percorsiLocali, cartellaRemotaDestinazione, server, eliminaSorgenteLocale)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiDownloadFtp(percorsoRemoto As String, cartellaLocaleDestinazione As String, server As InfoServerFtp, Optional eliminaSorgenteRemota As Boolean = False)
        Try
            AggiungiDownloadFtp(New List(Of String) From {percorsoRemoto}, cartellaLocaleDestinazione, server, eliminaSorgenteRemota)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiDownloadFtp(percorsiRemoti As List(Of String), cartellaLocaleDestinazione As String, server As InfoServerFtp, Optional eliminaSorgenteRemota As Boolean = False)
        Try
            AvviaPreparazioneLavoroFtp(False, "Preparazione download FTP...", percorsiRemoti, cartellaLocaleDestinazione, server, eliminaSorgenteRemota)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Sposta uno o piu' elementi (file e/o cartelle) da una cartella all'altra dello
    ''' stesso server FTP/SFTP, in background e in coda con le altre operazioni FTP, cosi'
    ''' da comparire nel Centro di Controllo con percentuale, velocita' e tempo residuo
    ''' come un upload o un download. Per ciascun elemento si prova prima la rinomina
    ''' diretta e si ripiega su copia+eliminazione solo se il server la rifiuta.
    ''' </summary>
    Public Sub AggiungiSpostaRemotoFtp(percorsiRemoti As List(Of String), cartellaRemotaDestinazione As String, server As InfoServerFtp)
        Try
            AvviaPreparazioneSpostaCopiaFtp(True, "Preparazione spostamento FTP...", percorsiRemoti, cartellaRemotaDestinazione, server)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Copia uno o piu' elementi (file e/o cartelle) da una cartella all'altra dello
    ''' stesso server FTP/SFTP, in background e in coda con le altre operazioni FTP.
    ''' Lascia sempre intatta l'origine.
    ''' </summary>
    Public Sub AggiungiCopiaRemotoFtp(percorsiRemoti As List(Of String), cartellaRemotaDestinazione As String, server As InfoServerFtp)
        Try
            AvviaPreparazioneSpostaCopiaFtp(False, "Preparazione copia FTP...", percorsiRemoti, cartellaRemotaDestinazione, server)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaPreparazioneSpostaCopiaFtp(eSposta As Boolean, descrizione As String, percorsi As List(Of String), destinazione As String, server As InfoServerFtp)
        Try
            If percorsi Is Nothing OrElse percorsi.Count = 0 Then Return
            Dim lavoro As LavoroFtp = CreaLavoroFtp(descrizione)
            If lavoro Is Nothing Then Return

            Dim preparatore As New PreparatoreSpostaCopiaFtp(_log)
            preparatore.Centro = Me
            preparatore.Lavoro = lavoro
            preparatore.Percorsi = percorsi
            preparatore.Destinazione = destinazione
            preparatore.Server = server
            preparatore.ESposta = eSposta

            Dim threadPreparazione As New Thread(AddressOf preparatore.Esegui)
            threadPreparazione.IsBackground = True
            threadPreparazione.Start()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub PreparaSpostaCopiaFtpLavoro(preparatore As PreparatoreSpostaCopiaFtp)
        Dim lavoro As LavoroFtp = Nothing
        Dim sessioneScansione As SessioneFtp = Nothing
        Try
            lavoro = preparatore.Lavoro
            Dim messaggioErrore As String = ""
            sessioneScansione = _ftp.ApriSessione(preparatore.Server, messaggioErrore)
            If sessioneScansione Is Nothing Then
                lavoro.MotivoInterruzione = "Impossibile connettersi al server: " & messaggioErrore
                Return
            End If

            For Each percorsoOrigine As String In preparatore.Percorsi
                Try
                    Dim eDirectory As Boolean = _ftp.ECartellaRemota(sessioneScansione, percorsoOrigine)
                    Dim dimensione As Long = 0
                    If eDirectory Then
                        Dim elementi As List(Of InfoElementoFtp) = _ftp.ElencaFileECartelleRicorsivo(sessioneScansione, percorsoOrigine)
                        For Each elemento As InfoElementoFtp In elementi
                            If Not elemento.EDirectory Then dimensione += Math.Max(0L, elemento.Dimensione)
                        Next
                    Else
                        dimensione = Math.Max(0L, _ftp.OttieniDimensioneFileFtp(sessioneScansione, percorsoOrigine))
                    End If

                    Dim tipo As TipoOperazioneFtp = If(preparatore.ESposta, TipoOperazioneFtp.SpostaRemoto, TipoOperazioneFtp.CopiaRemoto)
                    AggiungiTaskSpostaCopiaFtp(lavoro, tipo, percorsoOrigine, preparatore.Destinazione, dimensione, preparatore.Server, eDirectory)
                Catch exElemento As Exception
                    _log.ScriviLog(exElemento.ToString())
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            Try
                If sessioneScansione IsNot Nothing Then
                    _ftp.ChiudiSessione(sessioneScansione)
                End If
                If lavoro IsNot Nothing Then
                    lavoro.InPreparazione = False
                    If lavoro.Coda.Count = 0 Then
                        RimuoviLavoroFtp(lavoro)
                    Else
                        AvviaOperazioniFtp(lavoro)
                    End If
                End If
            Catch
            End Try
        End Try
    End Sub

    Private Sub AggiungiTaskSpostaCopiaFtp(lavoro As LavoroFtp, tipo As TipoOperazioneFtp, percorsoRemotoOrigine As String, cartellaRemotaDestinazione As String, dimensione As Long, server As InfoServerFtp, eDirectory As Boolean)
        Try
            If lavoro Is Nothing Then Return
            Dim nome As String = _ftp.NomeElementoRemoto(percorsoRemotoOrigine)
            Dim percorsoRemotoDestinazione As String = _ftp.CombinaPercorsoRemoto(cartellaRemotaDestinazione, nome)
            SyncLock lavoro.Blocco
                Dim t As New TaskFtp With {
                    .Tipo = tipo, .PercorsoRemoto = percorsoRemotoOrigine, .PercorsoRemotoDestinazione = percorsoRemotoDestinazione,
                    .Dimensione = dimensione, .Server = server, .EDirectory = eDirectory
                }
                lavoro.Coda.Add(t)
                lavoro.TotaleBytes += dimensione
            End SyncLock

            If server IsNot Nothing Then
                AggiungiRisorsa(lavoro, "ftp://" & server.Host & "/" & percorsoRemotoOrigine.TrimStart("/"c))
                AggiungiRisorsa(lavoro, "ftp://" & server.Host & "/" & percorsoRemotoDestinazione.TrimStart("/"c))
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaPreparazioneLavoroFtp(eUpload As Boolean, descrizione As String, percorsi As List(Of String), destinazione As String, server As InfoServerFtp, eliminaSorgente As Boolean)
        Try
            Dim lavoro As LavoroFtp = CreaLavoroFtp(descrizione)
            If lavoro Is Nothing Then Return

            Dim preparatore As New PreparatoreLavoroFtp(_log)
            preparatore.Centro = Me
            preparatore.Lavoro = lavoro
            preparatore.Percorsi = percorsi
            preparatore.Destinazione = destinazione
            preparatore.Server = server
            preparatore.EUpload = eUpload
            preparatore.EliminaSorgente = eliminaSorgente

            Dim threadPreparazione As New Thread(AddressOf preparatore.Esegui)
            threadPreparazione.IsBackground = True
            threadPreparazione.Start()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub PreparaLavoroFtp(preparatore As PreparatoreLavoroFtp)
        Dim lavoro As LavoroFtp = Nothing
        Dim sessioneScansione As SessioneFtp = Nothing
        Try
            lavoro = preparatore.Lavoro

            If preparatore.EUpload Then
                For Each sorgente As String In preparatore.Percorsi
                    If System.IO.Directory.Exists(sorgente) Then
                        Dim infoDir As New System.IO.DirectoryInfo(sorgente)
                        Dim cartellaRemotaBase As String = $"{preparatore.Destinazione.TrimEnd("/"c)}/{infoDir.Name}"

                        For Each dir As System.IO.DirectoryInfo In infoDir.GetDirectories("*", System.IO.SearchOption.AllDirectories)
                            Dim relativo As String = dir.FullName.Substring(sorgente.Length).Replace("\", "/").TrimStart("/"c)
                            Dim cartellaRemota As String = $"{cartellaRemotaBase}/{relativo}"
                            AggiungiTaskFtp(lavoro, TipoOperazioneFtp.CreaCartellaRemota, "", cartellaRemota, 0, preparatore.Server)
                        Next

                        For Each file As System.IO.FileInfo In infoDir.GetFiles("*", System.IO.SearchOption.AllDirectories)
                            Dim relativo As String = file.FullName.Substring(sorgente.Length).Replace("\", "/").TrimStart("/"c)
                            Dim destRemota As String = $"{cartellaRemotaBase}/{relativo}"
                            AggiungiTaskFtp(lavoro, TipoOperazioneFtp.Upload, file.FullName, destRemota, file.Length, preparatore.Server, preparatore.EliminaSorgente)
                        Next
                    ElseIf System.IO.File.Exists(sorgente) Then
                        Dim dimensione As Long = New System.IO.FileInfo(sorgente).Length
                        Dim nomeFile As String = System.IO.Path.GetFileName(sorgente)
                        Dim destRemota As String = $"{preparatore.Destinazione.TrimEnd("/"c)}/{nomeFile}"
                        AggiungiTaskFtp(lavoro, TipoOperazioneFtp.Upload, sorgente, destRemota, dimensione, preparatore.Server, preparatore.EliminaSorgente)
                    End If
                Next
            Else
                Dim messaggioErrore As String = ""
                sessioneScansione = _ftp.ApriSessione(preparatore.Server, messaggioErrore)
                If sessioneScansione Is Nothing Then
                    Return
                End If

                For Each percorsoRemoto As String In preparatore.Percorsi
                    Dim elementi As List(Of InfoElementoFtp) = _ftp.ElencaFileECartelle(sessioneScansione, System.IO.Path.GetDirectoryName(percorsoRemoto.Replace("/", "\")).Replace("\", "/"))
                    Dim nomeElemento As String = System.IO.Path.GetFileName(percorsoRemoto.TrimEnd("/"c))
                    Dim elementoCorrente As InfoElementoFtp = elementi.FirstOrDefault(Function(e) e.Nome = nomeElemento)

                    If elementoCorrente IsNot Nothing AndAlso elementoCorrente.EDirectory Then
                        Dim cartellaLocaleBase As String = System.IO.Path.Combine(preparatore.Destinazione, elementoCorrente.Nome)
                        If Not CreaCartellaLocaleSicura(cartellaLocaleBase) Then
                            lavoro.MotivoInterruzione = "Permessi insufficienti per creare la cartella locale: " & cartellaLocaleBase
                            NotificaFineOperazione(lavoro.MotivoInterruzione)
                            Continue For
                        End If

                        Dim tuttiElementi As List(Of InfoElementoFtp) = _ftp.ElencaFileECartelleRicorsivo(sessioneScansione, elementoCorrente.PercorsoCompleto)
                        Dim prefissoRemoto As String = elementoCorrente.PercorsoCompleto
                        For Each e As InfoElementoFtp In tuttiElementi
                            Dim relativo As String = e.PercorsoCompleto.Substring(prefissoRemoto.Length).TrimStart("/"c).Replace("/", "\")
                            Dim destLocale As String = System.IO.Path.Combine(cartellaLocaleBase, relativo)
                            If e.EDirectory Then
                                If Not CreaCartellaLocaleSicura(destLocale) Then
                                    ' Salta sottocartella non creabile; i file al suo interno non verranno scaricati
                                    Continue For
                                End If
                            Else
                                Dim dirPadre As String = System.IO.Path.GetDirectoryName(destLocale)
                                If Not String.IsNullOrEmpty(dirPadre) AndAlso Not CreaCartellaLocaleSicura(dirPadre) Then
                                    Continue For
                                End If
                                AggiungiTaskFtp(lavoro, TipoOperazioneFtp.Download, destLocale, e.PercorsoCompleto, e.Dimensione, preparatore.Server)
                            End If
                        Next
                        If preparatore.EliminaSorgente Then
                            lavoro.CartelleRemoteDaEliminare.Add(New TaskFtp With {.PercorsoRemoto = elementoCorrente.PercorsoCompleto, .Server = preparatore.Server})
                        End If
                    Else
                        Dim nomeFile As String = System.IO.Path.GetFileName(percorsoRemoto.TrimEnd("/"c))
                        Dim destLocale As String = System.IO.Path.Combine(preparatore.Destinazione, nomeFile)
                        Dim dirPadreFile As String = System.IO.Path.GetDirectoryName(destLocale)
                        If Not String.IsNullOrEmpty(dirPadreFile) AndAlso Not CreaCartellaLocaleSicura(dirPadreFile) Then
                            lavoro.MotivoInterruzione = "Permessi insufficienti per la cartella di destinazione: " & dirPadreFile
                            NotificaFineOperazione(lavoro.MotivoInterruzione)
                            Continue For
                        End If
                        Dim dimensione As Long = If(elementoCorrente IsNot Nothing, elementoCorrente.Dimensione, _ftp.OttieniDimensioneFileFtp(sessioneScansione, percorsoRemoto))
                        AggiungiTaskFtp(lavoro, TipoOperazioneFtp.Download, destLocale, percorsoRemoto, dimensione, preparatore.Server, preparatore.EliminaSorgente)
                    End If
                Next
            End If
        Catch ex As UnauthorizedAccessException
            If lavoro IsNot Nothing Then
                lavoro.MotivoInterruzione = "Permessi insufficienti durante la preparazione FTP."
            End If
            EseguiSuThreadInterfaccia(Sub()
                                          Dim msg As New WGMessaggi()
                                          msg.Messaggio("Permessi insufficienti." & vbCrLf &
                                                        "Impossibile preparare il trasferimento FTP nella destinazione scelta." & vbCrLf &
                                                        "Scegliere un'altra cartella o eseguire con privilegi adeguati.")
                                          msg.ShowDialog()
                                      End Sub)
        Catch ex As System.Security.SecurityException
            If lavoro IsNot Nothing Then
                lavoro.MotivoInterruzione = "Permessi insufficienti durante la preparazione FTP."
            End If
            EseguiSuThreadInterfaccia(Sub()
                                          Dim msg As New WGMessaggi()
                                          msg.Messaggio("Permessi insufficienti." & vbCrLf &
                                                        "Impossibile preparare il trasferimento FTP nella destinazione scelta.")
                                          msg.ShowDialog()
                                      End Sub)
        Catch ex As Exception
            ' Errori non di permesso: silenziati se già gestiti; solo casi inattesi restano loggati
            If TypeOf ex Is NotSupportedException OrElse TypeOf ex Is ArgumentException Then
                ' Percorso non supportato: nessun log
            Else
                _log.ScriviLog(ex.ToString())
            End If
        Finally
            Try
                If sessioneScansione IsNot Nothing Then
                    _ftp.ChiudiSessione(sessioneScansione)
                End If
                If lavoro IsNot Nothing Then
                    lavoro.InPreparazione = False
                    If lavoro.Coda.Count = 0 Then
                        RimuoviLavoroFtp(lavoro)
                    Else
                        AvviaOperazioniFtp(lavoro)
                    End If
                End If
            Catch
            End Try
        End Try
    End Sub

    Private Sub AvviaOperazioniFtp(lavoro As LavoroFtp)
        Try
            If lavoro Is Nothing Then Return
            SyncLock lavoro.Blocco
                If lavoro.ThreadLavoro IsNot Nothing OrElse lavoro.Coda.Count = 0 Then Return
                lavoro.InPausa = False
                lavoro.Annulla = False
                lavoro.Annullamento = New CancellationTokenSource()
                lavoro.TempoInizio = DateTime.Now
                lavoro.Contesto = New ContestoSovrascrittura()
                lavoro.Descrizione = "Operazione FTP in corso"
            End SyncLock

            lavoro.ThreadLavoro = New Thread(AddressOf EseguiOperazioniFtpLoop)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AvviaOperazioniFtp()
        Try
            For Each lavoro As LavoroFtp In LavoriFtpAttivi()
                If Not lavoro.InEsecuzione AndAlso Not lavoro.InPreparazione Then
                    AvviaOperazioniFtp(lavoro)
                End If
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiOperazioniFtpLoop(parametro As Object)
        Dim lavoro As LavoroFtp = TryCast(parametro, LavoroFtp)
        Dim sessionePrincipale As SessioneFtp = Nothing
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            lavoro.TempoInizio = DateTime.Now

            Dim compitiCartelle As List(Of TaskFtp)
            Dim compitiTrasferimento As List(Of TaskFtp)
            SyncLock lavoro.Blocco
                compitiCartelle = lavoro.Coda.Where(Function(c) c.Tipo = TipoOperazioneFtp.CreaCartellaRemota).ToList()
                compitiTrasferimento = lavoro.Coda.Where(Function(c) c.Tipo = TipoOperazioneFtp.Upload OrElse c.Tipo = TipoOperazioneFtp.Download).ToList()
            End SyncLock

            Dim serverBatch As InfoServerFtp = Nothing
            If compitiCartelle.Count > 0 Then
                serverBatch = compitiCartelle(0).Server
            ElseIf compitiTrasferimento.Count > 0 Then
                serverBatch = compitiTrasferimento(0).Server
            End If

            If compitiCartelle.Count > 0 AndAlso Not lavoro.Annulla Then
                Dim messaggioErrore As String = ""
                sessionePrincipale = _ftp.ApriSessione(serverBatch, messaggioErrore)
                If sessionePrincipale IsNot Nothing Then
                    For Each task As TaskFtp In compitiCartelle
                        If lavoro.Annulla Then Exit For
                        While lavoro.InPausa
                            Thread.Sleep(500)
                            If lavoro.Annulla Then Exit While
                        End While
                        If lavoro.Annulla Then Exit For
                        EseguiCreaCartellaRemotaFtp(sessionePrincipale, task)
                        lavoro.BytesProcessati += task.Dimensione
                    Next
                Else
                    lavoro.MotivoInterruzione = "Impossibile connettersi al server: " & messaggioErrore
                    lavoro.Annulla = True
                End If
            End If

            If Not lavoro.Annulla AndAlso compitiTrasferimento.Count > 0 Then
                lavoro.IndiceProssimoTask = 0
                SyncLock lavoro.BloccoStato
                    lavoro.StatiWorker = New List(Of StatoWorkerFtp)()
                    Dim numeroWorker As Integer = Math.Max(1, Math.Min(GradoParallelismoFtp, compitiTrasferimento.Count))
                    For w As Integer = 1 To numeroWorker
                        lavoro.StatiWorker.Add(New StatoWorkerFtp())
                    Next
                End SyncLock

                Dim threadsWorker As New List(Of Thread)()
                For w As Integer = 0 To lavoro.StatiWorker.Count - 1
                    Dim worker As New WorkerFtp(_log)
                    worker.Centro = Me
                    worker.Lavoro = lavoro
                    worker.Indice = w
                    worker.Compiti = compitiTrasferimento
                    Dim th As New Thread(AddressOf worker.Esegui)
                    th.IsBackground = True
                    threadsWorker.Add(th)
                Next
                For Each th As Thread In threadsWorker
                    th.Start()
                Next
                For Each th As Thread In threadsWorker
                    th.Join()
                Next
            End If

            If Not lavoro.Annulla Then
                For Each cartella As TaskFtp In lavoro.CartelleRemoteDaEliminare
                    Try
                        If sessionePrincipale Is Nothing OrElse Not sessionePrincipale.Connessa Then
                            If sessionePrincipale IsNot Nothing Then _ftp.ChiudiSessione(sessionePrincipale)
                            Dim messaggioErroreCleanup As String = ""
                            sessionePrincipale = _ftp.ApriSessione(cartella.Server, messaggioErroreCleanup)
                        End If
                        If sessionePrincipale IsNot Nothing Then
                            _ftp.EliminaCartellaFtpRicorsiva(sessionePrincipale, cartella.PercorsoRemoto)
                        End If
                    Catch ex As Exception
                        _log.ScriviLog(ex.ToString())
                    End Try
                Next
            End If

            If Not lavoro.Annulla Then
                NotificaFineOperazione("Operazioni FTP completate.")
            ElseIf Not String.IsNullOrEmpty(lavoro.MotivoInterruzione) Then
                NotificaFineOperazione(lavoro.MotivoInterruzione)
            Else
                NotificaFineOperazione("Operazioni FTP annullate o interrotte.")
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            Try
                If sessionePrincipale IsNot Nothing Then
                    _ftp.ChiudiSessione(sessionePrincipale)
                End If
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    SyncLock lavoro.Blocco
                        lavoro.Coda.Clear()
                    End SyncLock
                    lavoro.CartelleRemoteDaEliminare.Clear()
                    SyncLock lavoro.BloccoStato
                        lavoro.StatiWorker.Clear()
                    End SyncLock
                    If lavoro.Annullamento IsNot Nothing Then
                        lavoro.Annullamento.Dispose()
                        lavoro.Annullamento = Nothing
                    End If
                    RimuoviLavoroFtp(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Sub EseguiWorkerFtp(lavoro As LavoroFtp, idxWorker As Integer, compiti As List(Of TaskFtp))
        Dim sessione As SessioneFtp = Nothing
        Try
            If lavoro Is Nothing OrElse compiti Is Nothing OrElse compiti.Count = 0 Then Return
            Dim messaggioErroreIniziale As String = ""
            sessione = _ftp.ApriSessione(compiti(0).Server, messaggioErroreIniziale)
            If sessione Is Nothing Then
                lavoro.MotivoInterruzione = "Impossibile connettersi al server: " & messaggioErroreIniziale
                Return
            End If

            Do
                If lavoro.Annulla Then Exit Do

                Dim indiceCorrente As Integer = -1
                SyncLock lavoro.BloccoIndice
                    If lavoro.IndiceProssimoTask < compiti.Count Then
                        indiceCorrente = lavoro.IndiceProssimoTask
                        lavoro.IndiceProssimoTask += 1
                    End If
                End SyncLock
                If indiceCorrente < 0 Then Exit Do

                While lavoro.InPausa
                    Thread.Sleep(500)
                    If lavoro.Annulla Then Exit Do
                End While
                If lavoro.Annulla Then Exit Do

                Dim messaggioErroreRiconnessione As String = ""
                If Not _ftp.AssicuraConnessione(sessione, messaggioErroreRiconnessione) Then
                    Exit Do
                End If

                Dim task As TaskFtp = compiti(indiceCorrente)
                Try
                    Select Case task.Tipo
                        Case TipoOperazioneFtp.Upload : EseguiUploadFtp(lavoro, sessione, task, idxWorker)
                        Case TipoOperazioneFtp.Download : EseguiDownloadFtp(lavoro, sessione, task, idxWorker)
                        Case TipoOperazioneFtp.SpostaRemoto : EseguiSpostaRemotoFtp(lavoro, sessione, task, idxWorker)
                        Case TipoOperazioneFtp.CopiaRemoto : EseguiCopiaRemotoFtp(lavoro, sessione, task, idxWorker)
                    End Select
                    lavoro.BytesProcessati += task.Dimensione
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            Loop
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            Try
                If sessione IsNot Nothing Then
                    _ftp.ChiudiSessione(sessione)
                End If
                If lavoro IsNot Nothing Then
                    SyncLock lavoro.BloccoStato
                        If idxWorker < lavoro.StatiWorker.Count Then
                            lavoro.StatiWorker(idxWorker).Attivo = False
                        End If
                    End SyncLock
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Sub EseguiUploadFtp(lavoro As LavoroFtp, sessione As SessioneFtp, task As TaskFtp, idxWorker As Integer)
        Try
            If Not System.IO.File.Exists(task.PercorsoLocale) Then
                Return
            End If

            SyncLock lavoro.BloccoStato
                If idxWorker < lavoro.StatiWorker.Count Then
                    lavoro.StatiWorker(idxWorker).NomeFile = System.IO.Path.GetFileName(task.PercorsoLocale)
                    lavoro.StatiWorker(idxWorker).Bytes = 0
                    lavoro.StatiWorker(idxWorker).Velocita = 0
                    lavoro.StatiWorker(idxWorker).Attivo = True
                End If
            End SyncLock

            If _ftp.EsisteRemoto(sessione, task.PercorsoRemoto) Then
                If Not ChiediSovrascrittura(System.IO.Path.GetFileName(task.PercorsoLocale), lavoro.Contesto) Then
                    SyncLock lavoro.BloccoStato
                        If idxWorker < lavoro.StatiWorker.Count Then
                            lavoro.StatiWorker(idxWorker).Bytes = task.Dimensione
                            lavoro.StatiWorker(idxWorker).Velocita = 0
                        End If
                    End SyncLock
                    Return
                End If
            End If

            Dim standardError As String = ""
            Dim esito As Boolean = EseguiTrasferimentoConRetry(lavoro, sessione, task, idxWorker, True, standardError)
            If Not esito Then
                If lavoro.Annulla Then Return
                lavoro.MotivoInterruzione = $"Errore durante l'upload di {Chr(34)}{System.IO.Path.GetFileName(task.PercorsoLocale)}{Chr(34)}: {standardError}"
                Return
            End If

            If task.EliminaSorgenteDopo Then
                EseguiEliminaLocaleFtp(task)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiDownloadFtp(lavoro As LavoroFtp, sessione As SessioneFtp, task As TaskFtp, idxWorker As Integer)
        Try
            Dim dirDest As String = System.IO.Path.GetDirectoryName(task.PercorsoLocale)
            If Not String.IsNullOrEmpty(dirDest) AndAlso Not CreaCartellaLocaleSicura(dirDest) Then
                lavoro.MotivoInterruzione = "Permessi insufficienti per la cartella di destinazione: " & dirDest
                lavoro.Annulla = True
                Return
            End If

            SyncLock lavoro.BloccoStato
                If idxWorker < lavoro.StatiWorker.Count Then
                    lavoro.StatiWorker(idxWorker).NomeFile = System.IO.Path.GetFileName(task.PercorsoRemoto)
                    lavoro.StatiWorker(idxWorker).Bytes = 0
                    lavoro.StatiWorker(idxWorker).Velocita = 0
                    lavoro.StatiWorker(idxWorker).Attivo = True
                End If
            End SyncLock

            If System.IO.File.Exists(task.PercorsoLocale) Then
                If Not ChiediSovrascrittura(System.IO.Path.GetFileName(task.PercorsoLocale), lavoro.Contesto) Then
                    SyncLock lavoro.BloccoStato
                        If idxWorker < lavoro.StatiWorker.Count Then
                            lavoro.StatiWorker(idxWorker).Bytes = task.Dimensione
                            lavoro.StatiWorker(idxWorker).Velocita = 0
                        End If
                    End SyncLock
                    Return
                End If
                Try
                    System.IO.File.Delete(task.PercorsoLocale)
                Catch exRimozione As Exception
                    _log.ScriviLog(exRimozione.ToString())
                End Try
            End If

            Dim standardError As String = ""
            Dim esito As Boolean = EseguiTrasferimentoConRetry(lavoro, sessione, task, idxWorker, False, standardError)
            If Not esito Then
                If lavoro.Annulla Then Return
                lavoro.MotivoInterruzione = $"Errore durante il download di {Chr(34)}{task.PercorsoRemoto}{Chr(34)}: {standardError}"
                Try
                    If System.IO.File.Exists(task.PercorsoLocale) Then
                        System.IO.File.Delete(task.PercorsoLocale)
                    End If
                Catch exPulizia As Exception
                    _log.ScriviLog(exPulizia.ToString())
                End Try
                Return
            End If

            If task.EliminaSorgenteDopo Then
                EseguiEliminaRemotaFtp(sessione, task)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Esegue lo spostamento in coda di un elemento (file o cartella) fra due posizioni
    ''' dello stesso server: prova la rinomina diretta e ripiega su copia+eliminazione solo
    ''' se necessario (vedi CLSFtp.SpostaFileRemotoConFallback / SpostaCartellaRemota). Il
    ''' progresso viene riportato al Centro di Controllo tramite lo stesso monitor usato per
    ''' upload e download.
    ''' </summary>
    Private Sub EseguiSpostaRemotoFtp(lavoro As LavoroFtp, sessione As SessioneFtp, task As TaskFtp, idxWorker As Integer)
        Try
            SyncLock lavoro.BloccoStato
                If idxWorker < lavoro.StatiWorker.Count Then
                    lavoro.StatiWorker(idxWorker).NomeFile = _ftp.NomeElementoRemoto(task.PercorsoRemoto)
                    lavoro.StatiWorker(idxWorker).Bytes = 0
                    lavoro.StatiWorker(idxWorker).Velocita = 0
                    lavoro.StatiWorker(idxWorker).Attivo = True
                End If
            End SyncLock

            If _ftp.EsisteRemoto(sessione, task.PercorsoRemotoDestinazione) Then
                If Not ChiediSovrascrittura(_ftp.NomeElementoRemoto(task.PercorsoRemotoDestinazione), lavoro.Contesto) Then
                    SyncLock lavoro.BloccoStato
                        If idxWorker < lavoro.StatiWorker.Count Then
                            lavoro.StatiWorker(idxWorker).Bytes = task.Dimensione
                            lavoro.StatiWorker(idxWorker).Velocita = 0
                        End If
                    End SyncLock
                    Return
                End If
                If task.EDirectory Then
                    _ftp.EliminaCartellaFtpRicorsiva(sessione, task.PercorsoRemotoDestinazione)
                Else
                    _ftp.EliminaFileFtp(sessione, task.PercorsoRemotoDestinazione)
                End If
            End If

            Dim monitor As New MonitoreProgressoFtp(_log)
            monitor.Lavoro = lavoro
            monitor.Indice = idxWorker
            Dim callback As Action(Of Long, Long) = AddressOf monitor.Aggiorna
            Dim token As CancellationToken = TokenAnnullamentoFtp(lavoro)

            Dim errore As String = ""
            Dim esito As Boolean
            If task.EDirectory Then
                esito = _ftp.SpostaCartellaRemota(sessione, task.PercorsoRemoto, task.PercorsoRemotoDestinazione, errore, callback, token)
            Else
                esito = _ftp.SpostaFileRemotoConFallback(sessione, task.PercorsoRemoto, task.PercorsoRemotoDestinazione, errore, callback, token)
            End If

            SyncLock lavoro.BloccoStato
                If idxWorker < lavoro.StatiWorker.Count Then
                    If esito Then lavoro.StatiWorker(idxWorker).Bytes = task.Dimensione
                    lavoro.StatiWorker(idxWorker).Velocita = 0
                End If
            End SyncLock

            If Not esito Then
                If lavoro.Annulla Then Return
                lavoro.MotivoInterruzione = $"Errore durante lo spostamento di {Chr(34)}{_ftp.NomeElementoRemoto(task.PercorsoRemoto)}{Chr(34)}: {errore}"
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ''' <summary>
    ''' Esegue la copia in coda di un elemento (file o cartella) fra due posizioni dello
    ''' stesso server, lasciando intatta l'origine. Stesso schema di EseguiSpostaRemotoFtp.
    ''' </summary>
    Private Sub EseguiCopiaRemotoFtp(lavoro As LavoroFtp, sessione As SessioneFtp, task As TaskFtp, idxWorker As Integer)
        Try
            SyncLock lavoro.BloccoStato
                If idxWorker < lavoro.StatiWorker.Count Then
                    lavoro.StatiWorker(idxWorker).NomeFile = _ftp.NomeElementoRemoto(task.PercorsoRemoto)
                    lavoro.StatiWorker(idxWorker).Bytes = 0
                    lavoro.StatiWorker(idxWorker).Velocita = 0
                    lavoro.StatiWorker(idxWorker).Attivo = True
                End If
            End SyncLock

            If _ftp.EsisteRemoto(sessione, task.PercorsoRemotoDestinazione) Then
                If Not ChiediSovrascrittura(_ftp.NomeElementoRemoto(task.PercorsoRemotoDestinazione), lavoro.Contesto) Then
                    SyncLock lavoro.BloccoStato
                        If idxWorker < lavoro.StatiWorker.Count Then
                            lavoro.StatiWorker(idxWorker).Bytes = task.Dimensione
                            lavoro.StatiWorker(idxWorker).Velocita = 0
                        End If
                    End SyncLock
                    Return
                End If
                If task.EDirectory Then
                    _ftp.EliminaCartellaFtpRicorsiva(sessione, task.PercorsoRemotoDestinazione)
                Else
                    _ftp.EliminaFileFtp(sessione, task.PercorsoRemotoDestinazione)
                End If
            End If

            Dim monitor As New MonitoreProgressoFtp(_log)
            monitor.Lavoro = lavoro
            monitor.Indice = idxWorker
            Dim callback As Action(Of Long, Long) = AddressOf monitor.Aggiorna
            Dim token As CancellationToken = TokenAnnullamentoFtp(lavoro)

            Dim errore As String = ""
            Dim esito As Boolean
            If task.EDirectory Then
                esito = _ftp.CopiaCartellaRemota(sessione, task.PercorsoRemoto, task.PercorsoRemotoDestinazione, errore, callback, token)
            Else
                esito = _ftp.CopiaFileRemoto(sessione, task.PercorsoRemoto, task.PercorsoRemotoDestinazione, errore, callback, token)
            End If

            SyncLock lavoro.BloccoStato
                If idxWorker < lavoro.StatiWorker.Count Then
                    If esito Then lavoro.StatiWorker(idxWorker).Bytes = task.Dimensione
                    lavoro.StatiWorker(idxWorker).Velocita = 0
                End If
            End SyncLock

            If Not esito Then
                If lavoro.Annulla Then Return
                lavoro.MotivoInterruzione = $"Errore durante la copia di {Chr(34)}{_ftp.NomeElementoRemoto(task.PercorsoRemoto)}{Chr(34)}: {errore}"
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiCreaCartellaRemotaFtp(sessione As SessioneFtp, task As TaskFtp)
        Try
            _ftp.CreaCartellaFtp(sessione, task.PercorsoRemoto)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiEliminaLocaleFtp(task As TaskFtp)
        Try
            If System.IO.File.Exists(task.PercorsoLocale) Then
                System.IO.File.Delete(task.PercorsoLocale)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiEliminaRemotaFtp(sessione As SessioneFtp, task As TaskFtp)
        Try
            _ftp.EliminaFileFtp(sessione, task.PercorsoRemoto)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function EseguiTrasferimentoConRetry(lavoro As LavoroFtp, sessione As SessioneFtp, task As TaskFtp, idxWorker As Integer, eUpload As Boolean, ByRef standardError As String) As Boolean
        standardError = ""
        Try
            For tentativo As Integer = 1 To TENTATIVI_CURL
                While lavoro.InPausa
                    Thread.Sleep(500)
                    If lavoro.Annulla Then Return False
                End While
                If lavoro.Annulla Then Return False

                Dim monitor As New MonitoreProgressoFtp(_log)
                monitor.Lavoro = lavoro
                monitor.Indice = idxWorker

                Dim callback As Action(Of Long, Long) = AddressOf monitor.Aggiorna
                Dim token As CancellationToken = TokenAnnullamentoFtp(lavoro)
                Dim riprendi As Boolean = (tentativo > 1)

                Dim esito As Boolean
                If eUpload Then
                    esito = _ftp.CaricaFile(sessione, task.PercorsoLocale, task.PercorsoRemoto, standardError, callback, token, riprendi)
                Else
                    esito = _ftp.ScaricaFile(sessione, task.PercorsoRemoto, task.PercorsoLocale, standardError, callback, token, riprendi)
                End If

                SyncLock lavoro.BloccoStato
                    If idxWorker < lavoro.StatiWorker.Count Then
                        If esito Then lavoro.StatiWorker(idxWorker).Bytes = task.Dimensione
                        lavoro.StatiWorker(idxWorker).Velocita = 0
                    End If
                End SyncLock

                If esito Then Return True
                If lavoro.Annulla Then Return False
                If TokenAnnullamentoFtp(lavoro).IsCancellationRequested Then Return False


                If Not sessione.Connessa Then Exit For

                If tentativo < TENTATIVI_CURL Then
                    Thread.Sleep(ATTESA_BASE_RETRY_CURL_MS * tentativo)
                End If
            Next
            Return False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            standardError = ex.Message
            Return False
        End Try
    End Function

    Private Function VelocitaTotaleFtp(lavoro As LavoroFtp) As Double
        Try
            SyncLock lavoro.BloccoStato
                Return lavoro.StatiWorker.Where(Function(s) s.Attivo).Sum(Function(s) s.Velocita)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 0
        End Try
    End Function

    Private Function BytesFileCorrenteTotaleFtp(lavoro As LavoroFtp) As Long
        Try
            SyncLock lavoro.BloccoStato
                Return lavoro.StatiWorker.Where(Function(s) s.Attivo).Sum(Function(s) s.Bytes)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 0
        End Try
    End Function

    Private Function NomiFileFtpAttivi(lavoro As LavoroFtp) As String
        Try
            SyncLock lavoro.BloccoStato
                Dim nomi As List(Of String) = lavoro.StatiWorker.Where(Function(s) s.Attivo AndAlso Not String.IsNullOrEmpty(s.NomeFile)).Select(Function(s) s.NomeFile).ToList()
                Return String.Join(", ", nomi)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Public Sub AnnullaOperazioniFtp()
        Try
            For Each lavoro As LavoroFtp In LavoriFtpAttivi()
                AnnullaLavoro(lavoro)
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub MettiInPausaOperazioniFtp()
        Try
            For Each lavoro As LavoroFtp In LavoriFtpAttivi()
                MettiInPausaLavoro(lavoro)
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub RiprendiOperazioniFtp()
        Try
            For Each lavoro As LavoroFtp In LavoriFtpAttivi()
                RiprendiLavoro(lavoro)
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub copiaportatile(sorgente As String, destinazioneDevice As String, pathRemoto As String)
        Try
            copiaportatile(New List(Of String) From {sorgente}, destinazioneDevice, pathRemoto)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub copiaportatile(sorgenti As List(Of String), destinazioneDevice As String, pathRemoto As String)
        Try
            AvviaPreparazioneLavoroFile(ModalitaPreparazioneFile.CopiaPortatile, "Preparazione copia dispositivo...", sorgenti, "", destinazioneDevice, pathRemoto)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub spostaportatile(sorgente As String, destinazioneDevice As String, pathRemoto As String)
        Try
            spostaportatile(New List(Of String) From {sorgente}, destinazioneDevice, pathRemoto)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub spostaportatile(sorgenti As List(Of String), destinazioneDevice As String, pathRemoto As String)
        Try
            AvviaPreparazioneLavoroFile(ModalitaPreparazioneFile.SpostaPortatile, "Preparazione spostamento dispositivo...", sorgenti, "", destinazioneDevice, pathRemoto)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub eliminaportatile(deviceId As String, pathRemoto As String)
        Try
            eliminaportatile(deviceId, New List(Of String) From {pathRemoto})
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub eliminaportatile(deviceId As String, pathsRemoti As List(Of String))
        Try
            AvviaPreparazioneLavoroFile(ModalitaPreparazioneFile.EliminaPortatile, "Preparazione eliminazione dispositivo...", pathsRemoti, "", deviceId, "")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Function IsArchivioProtetto(percorsoArchivio As String) As Boolean
        Try
            If Not File.Exists(_path7z) Then
                Return False
            End If
            If Not File.Exists(percorsoArchivio) Then
                Return False
            End If

            Dim args As String = $"l -slt ""{percorsoArchivio}"""
            Dim psi As New ProcessStartInfo(_path7z, args)
            psi.UseShellExecute = False
            psi.RedirectStandardOutput = True
            psi.CreateNoWindow = True
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8

            Using proc As Process = Process.Start(psi)
                Dim output As String = proc.StandardOutput.ReadToEnd()
                proc.WaitForExit()
                Return output.Contains("Encrypted = +")
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Sub Comprimi(sorgenti As List(Of String), destinazione As String, Optional password As String = "")
        Try
            Dim compito As New TaskArchivio With {
                .IsCompressione = True,
                .Sorgenti = sorgenti,
                .Destinazione = destinazione,
                .Password = password
            }
            AvviaOperazioneArchivio(compito)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub Decomprimi(sorgente As String, destinazione As String, Optional password As String = "")
        Try
            Dim compito As New TaskArchivio With {
                .IsCompressione = False,
                .Sorgenti = New List(Of String) From {sorgente},
                .Destinazione = destinazione,
                .Password = password
            }
            AvviaOperazioneArchivio(compito)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriArchivioAttivi() As List(Of LavoroArchivio)
        Try
            Dim risultato As New List(Of LavoroArchivio)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroArchivio As LavoroArchivio = TryCast(lavoro, LavoroArchivio)
                If lavoroArchivio IsNot Nothing Then risultato.Add(lavoroArchivio)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroArchivio)()
        End Try
    End Function

    Private Sub RimuoviLavoroArchivio(lavoro As LavoroArchivio)
        Try
            RimuoviLavoro(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaOperazioneArchivio(compito As TaskArchivio)
        Try
            If Not File.Exists(_path7z) Then
                NotificaFineOperazione("7z.exe non trovato.")
                Return
            End If

            Dim lavoro As New LavoroArchivio()
            lavoro.Compito = compito
            lavoro.Annulla = False
            lavoro.Progresso = 0

            Dim estensione As String = ""
            If compito.IsCompressione Then
                estensione = Path.GetExtension(compito.Destinazione).Trim("."c).ToUpper()
                lavoro.Descrizione = $"Compressione archivio {estensione}"
            Else
                If compito.Sorgenti.Count > 0 Then
                    estensione = Path.GetExtension(compito.Sorgenti(0)).Trim("."c).ToUpper()
                End If
                lavoro.Descrizione = $"Decompressione archivio {estensione}"
            End If

            RegistraLavoro(lavoro, lavoro.Descrizione, StatoOperazioneCorrente.Archivio)
            lavoro.InPreparazione = False

            For Each sorgente As String In compito.Sorgenti
                AggiungiRisorsa(lavoro, sorgente)
            Next
            AggiungiRisorsa(lavoro, compito.Destinazione)

            lavoro.ThreadLavoro = New Thread(AddressOf EseguiArchivioLoop)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiArchivioLoop(parametro As Object)
        Dim lavoro As LavoroArchivio = TryCast(parametro, LavoroArchivio)
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return

            Dim argomenti As String = ""
            Dim passwordCmd As String = If(String.IsNullOrEmpty(lavoro.Compito.Password), "", $"-p{Chr(34)}{lavoro.Compito.Password}{Chr(34)}")

            If lavoro.Compito.IsCompressione Then
                Dim elencoSorgenti As String = String.Join(" ", lavoro.Compito.Sorgenti.Select(Function(s) $"{Chr(34)}{s}{Chr(34)}"))
                argomenti = $"a {Chr(34)}{lavoro.Compito.Destinazione}{Chr(34)} {elencoSorgenti} {passwordCmd} -bsp1 -y"
            Else
                argomenti = $"x {Chr(34)}{lavoro.Compito.Sorgenti(0)}{Chr(34)} -o{Chr(34)}{lavoro.Compito.Destinazione}{Chr(34)} {passwordCmd} -bsp1 -y"
            End If

            Dim psi As New ProcessStartInfo(_path7z, argomenti)
            psi.UseShellExecute = False
            psi.RedirectStandardOutput = True
            psi.CreateNoWindow = True
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8

            lavoro.Processo = Process.Start(psi)

            Dim lettore As StreamReader = lavoro.Processo.StandardOutput
            Dim buffer(1023) As Char
            Dim accumulatore As New System.Text.StringBuilder()

            While Not lavoro.Processo.HasExited
                If lavoro.Annulla Then
                    Try
                        lavoro.Processo.Kill()
                    Catch exKill As Exception
                        _log.ScriviLog(exKill.ToString())
                    End Try
                    Exit While
                End If

                Dim letti As Integer = lettore.Read(buffer, 0, buffer.Length)
                If letti > 0 Then
                    Dim testo As String = New String(buffer, 0, letti)
                    accumulatore.Append(testo)
                    If testo.Contains("%") Then
                        Dim righe As String() = accumulatore.ToString().Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                        If righe.Length > 0 Then
                            Dim ultimaRiga As String = righe(righe.Length - 1)
                            Dim parole As String() = ultimaRiga.Split(" "c)
                            For Each parola As String In parole
                                If parola.Contains("%") Then
                                    Dim valoreTesto As String = parola.Replace("%", "")
                                    Dim valore As Integer = 0
                                    If Integer.TryParse(valoreTesto, valore) Then
                                        If valore >= 0 AndAlso valore <= 100 Then lavoro.Progresso = valore
                                    End If
                                End If
                            Next
                        End If
                        If accumulatore.Length > 2000 Then
                            accumulatore.Clear()
                        End If
                    End If
                Else
                    Thread.Sleep(50)
                End If
            End While

            If Not lavoro.Annulla Then
                lavoro.Processo.WaitForExit()
                lavoro.Progresso = 100
                NotificaFineOperazione($"{lavoro.Descrizione}: operazione completata.")
            Else
                NotificaFineOperazione($"{lavoro.Descrizione}: operazione annullata.")
            End If

        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore durante operazione archivio.")
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    lavoro.Processo = Nothing
                    RimuoviLavoroArchivio(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Public Sub AggiornaCatalogoLibriVox()
        Try
            Dim lavoro As New LavoroLibriVox()
            lavoro.StatoDownload = New StatoDownloadHttp(_log)
            lavoro.TipoOperazione = TipoOperazioneLibriVox.AggiornamentoCatalogo
            lavoro.Info = "Avvio..."
            RegistraLavoro(lavoro, "Aggiornamento elenco libri LibriVox", StatoOperazioneCorrente.LibriVox)
            lavoro.InPreparazione = False
            AggiungiRisorsa(lavoro, "librivox:catalogo")
            AvviaLavoroLibriVox(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Function OttieniCategorieWikisourceDisponibili() As List(Of String)
        Return New List(Of String)(_categorieWikisourceTesti)
    End Function

    Public Sub AggiornaCatalogoTesti(Optional categorie As List(Of String) = Nothing)
        Try
            If categorie IsNot Nothing AndAlso categorie.Count > 0 Then
                _categorieDaCercareTesti = categorie.ToArray()
            Else
                _categorieDaCercareTesti = _categorieWikisourceTesti
            End If

            Dim lavoro As New LavoroTesti()
            lavoro.EAggiornamentoCatalogo = True
            lavoro.Info = "Avvio..."
            RegistraLavoro(lavoro, "Aggiornamento catalogo Biblioteca Testi", StatoOperazioneCorrente.Testi)
            lavoro.InPreparazione = False
            AggiungiRisorsa(lavoro, "testi:catalogo")

            lavoro.ThreadLavoro = New Thread(AddressOf ProcessaAggiornamentoCatalogoTesti)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriTestiAttivi() As List(Of LavoroTesti)
        Try
            Dim risultato As New List(Of LavoroTesti)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroTesti As LavoroTesti = TryCast(lavoro, LavoroTesti)
                If lavoroTesti IsNot Nothing Then risultato.Add(lavoroTesti)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroTesti)()
        End Try
    End Function

    Private Sub ProcessaAggiornamentoCatalogoTesti(parametro As Object)
        Dim lavoro As LavoroTesti = TryCast(parametro, LavoroTesti)
        Dim totaleWikisource As Integer = 0
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            Using conn As New SQLiteConnection(ConnectionStringTesti)
                conn.Open()
                Using cmdPragma As New SQLiteCommand(conn)
                    cmdPragma.CommandText = "PRAGMA synchronous = OFF;PRAGMA journal_mode = MEMORY;"
                    cmdPragma.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS LibriCatalogo (Id INTEGER PRIMARY KEY AUTOINCREMENT, Titolo TEXT, Autore TEXT, Genere TEXT, Fonte TEXT, IdGutendex INTEGER, UrlTestoPiano TEXT, TitoloPaginaWikisource TEXT, HaCapitoli INTEGER, Lingua TEXT, ChiaveUnivoca TEXT UNIQUE);" &
                                      "CREATE INDEX IF NOT EXISTS IdxAutore ON LibriCatalogo (Autore);" &
                                      "CREATE INDEX IF NOT EXISTS IdxGenere ON LibriCatalogo (Genere);"
                    cmd.ExecuteNonQuery()
                End Using
                Try
                    Using cmdMigrazione As New SQLiteCommand("ALTER TABLE LibriCatalogo ADD COLUMN HaCapitoli INTEGER", conn)
                        cmdMigrazione.ExecuteNonQuery()
                    End Using
                Catch
                End Try
                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "LibriCatalogo")
                Catch exChiave As Exception
                    _log.ScriviLog(exChiave.ToString())
                End Try
            End Using

            totaleWikisource = ProcessaCatalogoWikisource(lavoro)

            If lavoro.Annulla Then
                lavoro.Info = "Aggiornamento annullato."
                NotificaFineOperazione("Aggiornamento catalogo testi annullato.")
            Else
                lavoro.Progresso = 100
                lavoro.Info = $"Completato: {totaleWikisource} da Wikisource."
                NotificaFineOperazione($"Aggiornamento catalogo testi completato. {totaleWikisource} libri salvati da Wikisource.")
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore durante l'aggiornamento del catalogo testi.")
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Function ProcessaCatalogoWikisource(lavoro As LavoroTesti) As Integer
        Dim totale As Integer = 0
        Try
            Dim categorieDaProcessare As String() = If(_categorieDaCercareTesti IsNot Nothing AndAlso _categorieDaCercareTesti.Length > 0, _categorieDaCercareTesti, _categorieWikisourceTesti)

            Dim chiaviWGEsistentiCatalogo As New Dictionary(Of String, String)()
            Using connPulizia As New SQLiteConnection(ConnectionStringTesti)
                connPulizia.Open()
                Using cmdPulizia As New SQLiteCommand(connPulizia)
                    For Each nomeCategoriaPulizia As String In categorieDaProcessare
                        Try
                            cmdPulizia.CommandText = "SELECT ChiaveUnivoca, ChiaveWG FROM LibriCatalogo WHERE Fonte = 'Wikisource' AND Genere = @genere"
                            cmdPulizia.Parameters.Clear()
                            cmdPulizia.Parameters.AddWithValue("@genere", nomeCategoriaPulizia)
                            Using readerChiavi As SQLiteDataReader = cmdPulizia.ExecuteReader()
                                While readerChiavi.Read()
                                    Dim chiaveUnivocaEsistente As String = If(readerChiavi.IsDBNull(0), "", readerChiavi.GetString(0))
                                    Dim chiaveWGEsistente As String = If(readerChiavi.IsDBNull(1), "", readerChiavi.GetString(1))
                                    If Not String.IsNullOrWhiteSpace(chiaveUnivocaEsistente) AndAlso Not String.IsNullOrWhiteSpace(chiaveWGEsistente) AndAlso Not chiaviWGEsistentiCatalogo.ContainsKey(chiaveUnivocaEsistente) Then
                                        chiaviWGEsistentiCatalogo.Add(chiaveUnivocaEsistente, chiaveWGEsistente)
                                    End If
                                End While
                            End Using
                        Catch exLettura As Exception
                            _log.ScriviLog(exLettura.ToString())
                        End Try

                        cmdPulizia.CommandText = "DELETE FROM LibriCatalogo WHERE Fonte = 'Wikisource' AND Genere = @genere"
                        cmdPulizia.Parameters.Clear()
                        cmdPulizia.Parameters.AddWithValue("@genere", nomeCategoriaPulizia)
                        cmdPulizia.ExecuteNonQuery()
                    Next
                End Using
            End Using

            Dim client As New HttpClient()
            client.Timeout = TimeSpan.FromSeconds(60)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-BibliotecaTesti/1.0 (accessibilita; postmaster@wingabriel.net)")

            For Each nomeCategoria As String In categorieDaProcessare
                If lavoro.Annulla Then Exit For

                Dim titoliCategoria As New List(Of String)
                Dim continuazione As String = ""
                Dim contatorePagine As Integer = 0

                Do
                    If lavoro.Annulla Then Exit Do

                    lavoro.Info = $"Wikisource ({nomeCategoria}): {titoliCategoria.Count} pagine recuperate..."

                    Dim url As String = URL_BASE_WIKISOURCE & "?action=query&list=categorymembers&cmtitle=" & Uri.EscapeDataString("Categoria:" & nomeCategoria) & "&cmlimit=200&cmnamespace=0&format=json&formatversion=2"
                    If Not String.IsNullOrWhiteSpace(continuazione) Then
                        url &= "&cmcontinue=" & Uri.EscapeDataString(continuazione)
                    End If

                    Dim responseStr As String = EseguiRichiestaTestoConRetry(client, url)
                    If String.IsNullOrWhiteSpace(responseStr) Then Exit Do

                    Dim risposta As WsCategoryResponse = JsonConvert.DeserializeObject(Of WsCategoryResponse)(responseStr)
                    If risposta Is Nothing OrElse risposta.query Is Nothing OrElse risposta.query.categorymembers Is Nothing Then
                        Exit Do
                    End If

                    For Each membro As WsCategoryMember In risposta.query.categorymembers
                        If Not String.IsNullOrWhiteSpace(membro.title) Then
                            titoliCategoria.Add(membro.title)
                        End If
                    Next

                    continuazione = If(risposta.continue IsNot Nothing, risposta.continue.cmcontinue, "")
                    contatorePagine += 1
                    lavoro.Progresso = Math.Min(95, 45 + contatorePagine)
                Loop While Not String.IsNullOrWhiteSpace(continuazione) AndAlso contatorePagine < 50

                Dim opere As New Dictionary(Of String, Boolean)
                For Each titoloPagina As String In titoliCategoria
                    Try
                        Dim posizioneSlash As Integer = titoloPagina.IndexOf("/")
                        Dim radice As String = titoloPagina
                        Dim haSottopagine As Boolean = False
                        If posizioneSlash > 0 Then
                            radice = titoloPagina.Substring(0, posizioneSlash)
                            haSottopagine = True
                        End If

                        If opere.ContainsKey(radice) Then
                            If haSottopagine Then opere(radice) = True
                        Else
                            opere.Add(radice, haSottopagine)
                        End If
                    Catch exRadice As Exception
                        _log.ScriviLog(exRadice.ToString())
                    End Try
                Next

                Dim titoliDisambigua As HashSet(Of String) = OttieniPagineDisambiguaTesti(client, New List(Of String)(opere.Keys))
                For Each titoloDisambigua As String In titoliDisambigua
                    opere.Remove(titoloDisambigua)
                Next

                Using conn As New SQLiteConnection(ConnectionStringTesti)
                    conn.Open()
                    Using trans As SQLiteTransaction = conn.BeginTransaction()
                        Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO LibriCatalogo (Titolo, Autore, Genere, Fonte, IdGutendex, UrlTestoPiano, TitoloPaginaWikisource, HaCapitoli, Lingua, ChiaveUnivoca, ChiaveWG) VALUES (@titolo, 'Wikisource', @genere, 'Wikisource', 0, '', @paginawiki, @hacapitoli, 'it', @chiave, @chiaveWG)", conn, trans)
                            Dim pTitolo As SQLiteParameter = cmd.Parameters.Add("@titolo", DbType.String)
                            Dim pGenere As SQLiteParameter = cmd.Parameters.Add("@genere", DbType.String)
                            Dim pPagina As SQLiteParameter = cmd.Parameters.Add("@paginawiki", DbType.String)
                            Dim pHaCapitoli As SQLiteParameter = cmd.Parameters.Add("@hacapitoli", DbType.Int32)
                            Dim pChiave As SQLiteParameter = cmd.Parameters.Add("@chiave", DbType.String)
                            Dim pChiaveWG As SQLiteParameter = cmd.Parameters.Add("@chiaveWG", DbType.String)

                            For Each coppia As KeyValuePair(Of String, Boolean) In opere
                                Try
                                    Dim chiaveUnivocaRiga As String = "wikisource_" & coppia.Key
                                    Dim chiaveWGRiga As String = ""
                                    chiaviWGEsistentiCatalogo.TryGetValue(chiaveUnivocaRiga, chiaveWGRiga)
                                    If String.IsNullOrWhiteSpace(chiaveWGRiga) Then chiaveWGRiga = WGDatabase.GeneraChiave()

                                    pTitolo.Value = coppia.Key
                                    pGenere.Value = nomeCategoria
                                    pPagina.Value = coppia.Key
                                    pHaCapitoli.Value = If(coppia.Value, 1, 0)
                                    pChiave.Value = chiaveUnivocaRiga
                                    pChiaveWG.Value = chiaveWGRiga
                                    cmd.ExecuteNonQuery()
                                    totale += 1
                                Catch exOpera As Exception
                                    _log.ScriviLog(exOpera.ToString())
                                End Try
                            Next
                        End Using
                        trans.Commit()
                    End Using
                End Using
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return totale
    End Function

    Public Sub ScaricaLibriTesto(elementi As List(Of ElementoDaScaricareTesto))
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim lavoro As New LavoroTesti()
            lavoro.CodaDownload = New List(Of ElementoDaScaricareTesto)(elementi)
            lavoro.Info = "Inizializzazione..."
            RegistraLavoro(lavoro, "Download libri Wikisource", StatoOperazioneCorrente.Testi)
            lavoro.InPreparazione = False

            For Each elemento As ElementoDaScaricareTesto In lavoro.CodaDownload
                AggiungiRisorsa(lavoro, "testi:" & elemento.TitoloPaginaWikisource)
            Next

            lavoro.ThreadLavoro = New Thread(AddressOf ProcessaDownloadLibriTesto)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ProcessaDownloadLibriTesto(parametro As Object)
        Dim lavoro As LavoroTesti = TryCast(parametro, LavoroTesti)
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return

            Using conn As New SQLiteConnection(ConnectionStringTesti)
                conn.Open()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS LibriTestoScaricati (Chiave TEXT PRIMARY KEY, Titolo TEXT, Autore TEXT, Genere TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS CapitoliTestoScaricati (Id INTEGER PRIMARY KEY AUTOINCREMENT, LibroChiave TEXT, Ordine INTEGER, TitoloCapitolo TEXT, Testo TEXT);" &
                                      "CREATE INDEX IF NOT EXISTS IdxCapitoliTestoLibroChiave ON CapitoliTestoScaricati (LibroChiave);"
                    cmd.ExecuteNonQuery()
                End Using
                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "LibriTestoScaricati")
                    WGDatabase.AssicuraColonnaChiave(conn, "CapitoliTestoScaricati")
                Catch exChiave As Exception
                    _log.ScriviLog(exChiave.ToString())
                End Try
            End Using

            Dim client As New HttpClient()
            client.Timeout = TimeSpan.FromSeconds(60)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-BibliotecaTesti/1.0 (accessibilita; postmaster@wingabriel.net)")

            Dim totaleLibri As Integer = lavoro.CodaDownload.Count
            Dim completati As Integer = 0
            lavoro.LibriTotali = totaleLibri
            lavoro.LibriCompletati = 0
            lavoro.CapitoliTotali = 0
            lavoro.CapitoliCompletati = 0

            For Each libro As ElementoDaScaricareTesto In lavoro.CodaDownload
                If lavoro.Annulla Then Exit For

                _titoloLibroTestoCorrente = libro.Titolo
                lavoro.CapitoliTotali = 0
                lavoro.CapitoliCompletati = 0
                lavoro.Info = $"Download di {libro.Titolo}, libro {completati + 1} di {totaleLibri}"

                Try
                    ScaricaLibroTestoCompleto(lavoro, client, libro)
                Catch exLibro As Exception
                    _log.ScriviLog($"Errore download libro Wikisource {libro.TitoloPaginaWikisource}: {exLibro.Message}")
                End Try

                completati += 1
                lavoro.LibriCompletati = completati
                lavoro.CapitoliTotali = 0
                lavoro.CapitoliCompletati = 0
                lavoro.Progresso = CInt((completati / totaleLibri) * 100)
            Next

            If lavoro.Annulla Then
                NotificaFineOperazione("Download libri Wikisource annullato.")
            Else
                NotificaFineOperazione($"Download libri Wikisource completato. {completati} elementi elaborati su {totaleLibri}.")
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore durante il download dei libri Wikisource.")
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Sub ScaricaLibroTestoCompleto(lavoro As LavoroTesti, client As HttpClient, libro As ElementoDaScaricareTesto)
        Try
            Dim paginaCapitoli As New List(Of KeyValuePair(Of String, String))
            Dim continuazione As String = ""
            Dim contatorePagine As Integer = 0

            Do
                Dim urlElenco As String = URL_BASE_WIKISOURCE & "?action=query&generator=allpages&gapprefix=" & Uri.EscapeDataString(libro.TitoloPaginaWikisource & "/") & "&gapnamespace=0&gaplimit=500&prop=info&format=json&formatversion=2"
                If Not String.IsNullOrWhiteSpace(continuazione) Then
                    urlElenco &= "&gapcontinue=" & Uri.EscapeDataString(continuazione)
                End If

                Dim rispostaElenco As String = EseguiRichiestaTestoConRetry(client, urlElenco)
                If String.IsNullOrWhiteSpace(rispostaElenco) Then Exit Do

                Dim jsonElenco As JObject = JObject.Parse(rispostaElenco)
                Dim pagine As JArray = TryCast(jsonElenco("query")?("pages"), JArray)
                If pagine IsNot Nothing Then
                    For Each elemento As JObject In pagine
                        Dim titoloCompleto As String = elemento("title")?.ToString()
                        If String.IsNullOrWhiteSpace(titoloCompleto) Then Continue For

                        Dim lingua As String = elemento("pagelanguage")?.ToString()
                        If Not String.IsNullOrWhiteSpace(lingua) AndAlso Not lingua.StartsWith("it", StringComparison.OrdinalIgnoreCase) Then
                            Continue For
                        End If

                        Dim posizioneSlash As Integer = titoloCompleto.IndexOf("/")
                        Dim titoloCapitolo As String
                        If posizioneSlash >= 0 AndAlso posizioneSlash < titoloCompleto.Length - 1 Then
                            titoloCapitolo = titoloCompleto.Substring(posizioneSlash + 1)
                        Else
                            titoloCapitolo = titoloCompleto
                        End If
                        paginaCapitoli.Add(New KeyValuePair(Of String, String)(titoloCapitolo, titoloCompleto))
                    Next
                End If

                continuazione = jsonElenco("continue")?("gapcontinue")?.ToString()
                contatorePagine += 1
            Loop While Not String.IsNullOrWhiteSpace(continuazione) AndAlso contatorePagine < 20

            paginaCapitoli.Sort(AddressOf ConfrontaCapitoliTesto)

            Dim capitoliDaScaricare As New List(Of KeyValuePair(Of String, String))
            If paginaCapitoli.Count > 0 Then
                capitoliDaScaricare = paginaCapitoli
            Else
                capitoliDaScaricare.Add(New KeyValuePair(Of String, String)(libro.Titolo, libro.TitoloPaginaWikisource))
            End If

            Dim testiCapitoli As New List(Of KeyValuePair(Of String, String))
            lavoro.CapitoliTotali = capitoliDaScaricare.Count
            lavoro.CapitoliCompletati = 0

            For indiceCapitolo As Integer = 0 To capitoliDaScaricare.Count - 1
                If lavoro.Annulla Then Exit For
                Try
                    Dim voce As KeyValuePair(Of String, String) = capitoliDaScaricare(indiceCapitolo)
                    lavoro.Info = $"Download di {libro.Titolo}, capitolo {indiceCapitolo + 1} di {capitoliDaScaricare.Count}"
                    Dim testo As String = ScaricaTestoWikisourcePerPaginaLocale(client, voce.Value)
                    If Not String.IsNullOrWhiteSpace(testo) Then
                        testiCapitoli.Add(New KeyValuePair(Of String, String)(voce.Key, testo))
                    End If
                Catch exCap As Exception
                    _log.ScriviLog($"Errore download capitolo {capitoliDaScaricare(indiceCapitolo).Value}: {exCap.Message}")
                End Try

                lavoro.CapitoliCompletati = indiceCapitolo + 1

                If indiceCapitolo < capitoliDaScaricare.Count - 1 Then
                    Thread.Sleep(400)
                End If
            Next

            If testiCapitoli.Count = 0 Then Return

            Using conn As New SQLiteConnection(ConnectionStringTesti)
                conn.Open()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Dim chiaveWGLibro As String = ""
                    Using cmdChiaveLibro As New SQLiteCommand("SELECT ChiaveWG FROM LibriTestoScaricati WHERE Chiave = @chiave", conn, trans)
                        cmdChiaveLibro.Parameters.AddWithValue("@chiave", libro.TitoloPaginaWikisource)
                        Dim valoreChiaveLibro As Object = cmdChiaveLibro.ExecuteScalar()
                        If valoreChiaveLibro IsNot Nothing AndAlso Not IsDBNull(valoreChiaveLibro) Then chiaveWGLibro = Convert.ToString(valoreChiaveLibro)
                    End Using
                    If String.IsNullOrWhiteSpace(chiaveWGLibro) Then chiaveWGLibro = WGDatabase.GeneraChiave()

                    Using cmdLibro As New SQLiteCommand("INSERT OR REPLACE INTO LibriTestoScaricati (Chiave, Titolo, Autore, Genere, ChiaveWG) VALUES (@chiave, @titolo, @autore, @genere, @chiaveWG)", conn, trans)
                        cmdLibro.Parameters.AddWithValue("@chiave", libro.TitoloPaginaWikisource)
                        cmdLibro.Parameters.AddWithValue("@titolo", libro.Titolo)
                        cmdLibro.Parameters.AddWithValue("@autore", libro.Autore)
                        cmdLibro.Parameters.AddWithValue("@genere", libro.Genere)
                        cmdLibro.Parameters.AddWithValue("@chiaveWG", chiaveWGLibro)
                        cmdLibro.ExecuteNonQuery()
                    End Using

                    Dim chiaviWGEsistentiCapitoli As New Dictionary(Of Integer, String)()
                    Using cmdChiaviCap As New SQLiteCommand("SELECT Ordine, ChiaveWG FROM CapitoliTestoScaricati WHERE LibroChiave = @chiave", conn, trans)
                        cmdChiaviCap.Parameters.AddWithValue("@chiave", libro.TitoloPaginaWikisource)
                        Using readerCap As SQLiteDataReader = cmdChiaviCap.ExecuteReader()
                            While readerCap.Read()
                                If Not readerCap.IsDBNull(0) AndAlso Not readerCap.IsDBNull(1) Then
                                    Dim ordineEsistente As Integer = readerCap.GetInt32(0)
                                    Dim chiaveWGEsistente As String = readerCap.GetString(1)
                                    If Not String.IsNullOrWhiteSpace(chiaveWGEsistente) AndAlso Not chiaviWGEsistentiCapitoli.ContainsKey(ordineEsistente) Then
                                        chiaviWGEsistentiCapitoli.Add(ordineEsistente, chiaveWGEsistente)
                                    End If
                                End If
                            End While
                        End Using
                    End Using

                    Using cmdDelCap As New SQLiteCommand("DELETE FROM CapitoliTestoScaricati WHERE LibroChiave = @chiave", conn, trans)
                        cmdDelCap.Parameters.AddWithValue("@chiave", libro.TitoloPaginaWikisource)
                        cmdDelCap.ExecuteNonQuery()
                    End Using

                    Using cmdCap As New SQLiteCommand("INSERT INTO CapitoliTestoScaricati (LibroChiave, Ordine, TitoloCapitolo, Testo, ChiaveWG) VALUES (@chiave, @ordine, @tit, @testo, @chiaveWG)", conn, trans)
                        Dim pChiave As SQLiteParameter = cmdCap.Parameters.Add("@chiave", DbType.String)
                        Dim pOrdine As SQLiteParameter = cmdCap.Parameters.Add("@ordine", DbType.Int32)
                        Dim pTit As SQLiteParameter = cmdCap.Parameters.Add("@tit", DbType.String)
                        Dim pTesto As SQLiteParameter = cmdCap.Parameters.Add("@testo", DbType.String)
                        Dim pChiaveWG As SQLiteParameter = cmdCap.Parameters.Add("@chiaveWG", DbType.String)
                        pChiave.Value = libro.TitoloPaginaWikisource
                        For indice As Integer = 0 To testiCapitoli.Count - 1
                            Dim chiaveWGCapitolo As String = ""
                            chiaviWGEsistentiCapitoli.TryGetValue(indice, chiaveWGCapitolo)
                            If String.IsNullOrWhiteSpace(chiaveWGCapitolo) Then chiaveWGCapitolo = WGDatabase.GeneraChiave()
                            pOrdine.Value = indice
                            pTit.Value = testiCapitoli(indice).Key
                            pTesto.Value = testiCapitoli(indice).Value
                            pChiaveWG.Value = chiaveWGCapitolo
                            cmdCap.ExecuteNonQuery()
                        Next
                    End Using
                    trans.Commit()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function EseguiRichiestaTestoConRetry(client As HttpClient, url As String) As String
        Const numeroTentativiMassimo As Integer = 3
        Try
            For tentativo As Integer = 1 To numeroTentativiMassimo
                Try
                    Dim risposta As HttpResponseMessage = client.GetAsync(url).Result
                    If risposta.IsSuccessStatusCode Then
                        Return risposta.Content.ReadAsStringAsync().Result
                    End If

                    Dim codiceStato As Integer = CInt(risposta.StatusCode)

                    If tentativo < numeroTentativiMassimo Then
                        Thread.Sleep(CalcolaAttesaRetry(codiceStato, tentativo, risposta))
                    End If
                Catch ex As Exception
                    _log.ScriviLog($"Errore richiesta (tentativo {tentativo} di {numeroTentativiMassimo}) {url}: {ex.Message}")
                    If tentativo < numeroTentativiMassimo Then
                        Thread.Sleep(1000 * tentativo)
                    End If
                End Try
            Next
            Return ""
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EseguiRichiestaByteConRetry(client As HttpClient, url As String) As Byte()
        Const numeroTentativiMassimo As Integer = 3
        Try
            For tentativo As Integer = 1 To numeroTentativiMassimo
                Try
                    Dim risposta As HttpResponseMessage = client.GetAsync(url).Result
                    If risposta.IsSuccessStatusCode Then
                        Return risposta.Content.ReadAsByteArrayAsync().Result
                    End If

                    Dim codiceStato As Integer = CInt(risposta.StatusCode)

                    If tentativo < numeroTentativiMassimo Then
                        Thread.Sleep(CalcolaAttesaRetry(codiceStato, tentativo, risposta))
                    End If
                Catch ex As Exception
                    _log.ScriviLog($"Errore richiesta (tentativo {tentativo} di {numeroTentativiMassimo}) {url}: {ex.Message}")
                    If tentativo < numeroTentativiMassimo Then
                        Thread.Sleep(1000 * tentativo)
                    End If
                End Try
            Next
            Return Nothing
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function CalcolaAttesaRetry(codiceStato As Integer, tentativo As Integer, risposta As HttpResponseMessage) As Integer
        Try
            If codiceStato = 429 Then
                If risposta IsNot Nothing AndAlso risposta.Headers.RetryAfter IsNot Nothing AndAlso risposta.Headers.RetryAfter.Delta.HasValue Then
                    Return CInt(risposta.Headers.RetryAfter.Delta.Value.TotalMilliseconds)
                End If
                Return 3000 * tentativo
            End If
            If codiceStato >= 500 Then
                Return 2000 * tentativo
            End If
            Return 1000 * tentativo
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 2000
        End Try
    End Function

    Private Function OttieniPagineDisambiguaTesti(client As HttpClient, titoli As List(Of String)) As HashSet(Of String)
        Dim risultato As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Try
            If titoli Is Nothing OrElse titoli.Count = 0 Then Return risultato

            Dim indice As Integer = 0
            While indice < titoli.Count
                Dim quantitaLotto As Integer = Math.Min(50, titoli.Count - indice)
                Dim lotto As List(Of String) = titoli.GetRange(indice, quantitaLotto)
                Dim titoliUniti As String = String.Join("|", lotto)

                Dim url As String = URL_BASE_WIKISOURCE & "?action=query&prop=pageprops|info&ppprop=disambiguation&titles=" & Uri.EscapeDataString(titoliUniti) & "&format=json&formatversion=2"
                Dim risposta As String = EseguiRichiestaTestoConRetry(client, url)
                If Not String.IsNullOrWhiteSpace(risposta) Then
                    Dim json As JObject = JObject.Parse(risposta)
                    Dim pagine As JArray = TryCast(json("query")?("pages"), JArray)
                    If pagine IsNot Nothing Then
                        For Each pagina As JObject In pagine
                            Try
                                Dim titoloPagina As String = pagina("title")?.ToString()
                                If String.IsNullOrWhiteSpace(titoloPagina) Then Continue For

                                Dim eDaEscludere As Boolean = False

                                Dim props As JObject = TryCast(pagina("pageprops"), JObject)
                                If props IsNot Nothing AndAlso props("disambiguation") IsNot Nothing Then
                                    eDaEscludere = True
                                End If

                                Dim lingua As String = pagina("pagelanguage")?.ToString()
                                If Not String.IsNullOrWhiteSpace(lingua) AndAlso Not lingua.StartsWith("it", StringComparison.OrdinalIgnoreCase) Then
                                    eDaEscludere = True
                                End If

                                If eDaEscludere Then risultato.Add(titoloPagina)
                            Catch exPagina As Exception
                                _log.ScriviLog(exPagina.ToString())
                            End Try
                        Next
                    End If
                End If

                indice += quantitaLotto
            End While
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function ConfrontaCapitoliTesto(a As KeyValuePair(Of String, String), b As KeyValuePair(Of String, String)) As Integer
        Try
            Dim numeroA As Integer = EstraiPrimoNumeroTesto(a.Key)
            Dim numeroB As Integer = EstraiPrimoNumeroTesto(b.Key)
            If numeroA >= 0 AndAlso numeroB >= 0 AndAlso numeroA <> numeroB Then
                Return numeroA.CompareTo(numeroB)
            End If
            Return String.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 0
        End Try
    End Function

    Private Function EstraiPrimoNumeroTesto(testo As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return -1

            Dim corrispondenzaArabo As Match = Regex.Match(testo, "\d+")
            If corrispondenzaArabo.Success Then
                Dim numero As Integer
                If Integer.TryParse(corrispondenzaArabo.Value, numero) Then Return numero
            End If

            Dim testoMinuscolo As String = testo.ToLowerInvariant()
            Dim paroleContesto As String() = {"capitolo", "libro", "parte", "atto", "canto", "cap.", "cap ", "scena", "volume"}
            Dim haContesto As Boolean = False
            For Each parolaContesto As String In paroleContesto
                If testoMinuscolo.Contains(parolaContesto) Then
                    haContesto = True
                    Exit For
                End If
            Next
            If Not haContesto Then Return -1

            Dim parole As String() = testo.Trim().Split(New Char() {" "c, "-"c, "."c, ":"c}, StringSplitOptions.RemoveEmptyEntries)
            If parole.Length = 0 Then Return -1

            Dim ultimaParola As String = parole(parole.Length - 1)
            If Regex.IsMatch(ultimaParola, "^[MDCLXVImdclxvi]+$") Then
                Dim valoreRomano As Integer = ConvertiNumeroRomano(ultimaParola)
                If valoreRomano > 0 Then Return valoreRomano
            End If

            Return -1
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return -1
        End Try
    End Function

    Private Function ConvertiNumeroRomano(testo As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return -1

            Dim valori As New Dictionary(Of Char, Integer) From {
                {"I"c, 1}, {"V"c, 5}, {"X"c, 10}, {"L"c, 50}, {"C"c, 100}, {"D"c, 500}, {"M"c, 1000}
            }
            Dim testoMaiuscolo As String = testo.ToUpperInvariant()
            Dim totale As Integer = 0
            Dim precedente As Integer = 0

            For indice As Integer = testoMaiuscolo.Length - 1 To 0 Step -1
                Dim carattere As Char = testoMaiuscolo(indice)
                If Not valori.ContainsKey(carattere) Then Return -1
                Dim valoreCorrente As Integer = valori(carattere)
                If valoreCorrente < precedente Then
                    totale -= valoreCorrente
                Else
                    totale += valoreCorrente
                End If
                precedente = valoreCorrente
            Next

            If totale <= 0 Then Return -1
            Return totale
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return -1
        End Try
    End Function

    Private Function ScaricaTestoWikisourcePerPaginaLocale(client As HttpClient, titoloPagina As String) As String
        Try
            If String.IsNullOrWhiteSpace(titoloPagina) Then Return ""

            Dim url As String = URL_BASE_WIKISOURCE & "?action=parse&page=" & Uri.EscapeDataString(titoloPagina) & "&redirects=1&disabletoc=1&disableeditsection=1&prop=text&format=json&formatversion=2"
            Dim risposta As String = EseguiRichiestaTestoConRetry(client, url)
            If String.IsNullOrWhiteSpace(risposta) Then Return ""

            Dim json As JObject = JObject.Parse(risposta)

            Dim erroreApi As String = json("error")?("info")?.ToString()
            If Not String.IsNullOrWhiteSpace(erroreApi) Then Return ""

            Dim html As String = json("parse")?("text")?.ToString()
            Return ConvertiHtmlInTestoPianoLocale(html)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ConvertiHtmlInTestoPianoLocale(html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return ""

            Dim testo As String = html

            testo = Regex.Replace(testo, "<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<style[^>]*>.*?</style>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<span[^>]*class=""[^""]*mw-editsection[^""]*""[^>]*>.*?</span>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<sup[^>]*class=""[^""]*reference[^""]*""[^>]*>.*?</sup>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<ol[^>]*class=""[^""]*references[^""]*""[^>]*>.*?</ol>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<table[^>]*>.*?</table>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<div[^>]*class=""[^""]*(noprint|navigation-not-searchable|ws-noexport)[^""]*""[^>]*>.*?</div>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)

            testo = Regex.Replace(testo, "<br\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</p>", Environment.NewLine & Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</div>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</li>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "<li[^>]*>", "- ", RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</h[1-6]>", Environment.NewLine & Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "<h[1-6][^>]*>", Environment.NewLine, RegexOptions.IgnoreCase)

            testo = Regex.Replace(testo, "<[^>]+>", "")

            testo = System.Net.WebUtility.HtmlDecode(testo)

            testo = Regex.Replace(testo, "-?\s*\[\s*p\.\s*\d+\s*modifica\s*\]", "", RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "\[?\s*\d{1,4}\s*\]", "")

            testo = Regex.Replace(testo, "(?m)^[\s◄►\|·▶▷\-–—]*$", "")
            testo = Regex.Replace(testo, "[ \t]+", " ")
            testo = Regex.Replace(testo, "\n[ \t]+", Environment.NewLine)
            testo = Regex.Replace(testo, "\n{3,}", Environment.NewLine & Environment.NewLine)

            Return testo.Trim()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return html
        End Try
    End Function

    Public Function OttieniLibriTestoScaricati() As List(Of LibroTestoScaricato)
        Dim risultati As New List(Of LibroTestoScaricato)
        Try
            Using conn As New SQLiteConnection(ConnectionStringTesti)
                conn.Open()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS LibriTestoScaricati (Chiave TEXT PRIMARY KEY, Titolo TEXT, Autore TEXT, Genere TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS CapitoliTestoScaricati (Id INTEGER PRIMARY KEY AUTOINCREMENT, LibroChiave TEXT, Ordine INTEGER, TitoloCapitolo TEXT, Testo TEXT);"
                    cmd.ExecuteNonQuery()
                End Using
                Using cmd As New SQLiteCommand("SELECT Chiave, Titolo, Autore, Genere FROM LibriTestoScaricati ORDER BY Titolo COLLATE NOCASE", conn)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        While r.Read()
                            Dim libro As New LibroTestoScaricato()
                            libro.Id = r("Chiave").ToString()
                            libro.Titolo = r("Titolo").ToString()
                            libro.Autore = r("Autore").ToString()
                            libro.Genere = r("Genere").ToString()
                            risultati.Add(libro)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniCapitoliTestoScaricati(chiaveLibro As String) As List(Of CapitoloTestoScaricato)
        Dim risultati As New List(Of CapitoloTestoScaricato)
        Try
            If String.IsNullOrWhiteSpace(chiaveLibro) Then Return risultati
            Using conn As New SQLiteConnection(ConnectionStringTesti)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT Id, TitoloCapitolo, Testo FROM CapitoliTestoScaricati WHERE LibroChiave = @chiave ORDER BY Ordine", conn)
                    cmd.Parameters.AddWithValue("@chiave", chiaveLibro)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        While r.Read()
                            Dim capitolo As New CapitoloTestoScaricato()
                            capitolo.Id = Convert.ToInt32(r("Id"))
                            capitolo.TitoloCapitolo = r("TitoloCapitolo").ToString()
                            capitolo.Testo = r("Testo").ToString()
                            risultati.Add(capitolo)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function EliminaLibroTestoScaricato(chiaveLibro As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(chiaveLibro) Then Return False

            If RisorsaOccupata("testi:" & chiaveLibro) Then
                GestisciConflittoOperazione("Biblioteca testi")
                Return False
            End If

            Using conn As New SQLiteConnection(ConnectionStringTesti)
                conn.Open()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Using cmdCap As New SQLiteCommand("DELETE FROM CapitoliTestoScaricati WHERE LibroChiave = @chiave", conn, trans)
                        cmdCap.Parameters.AddWithValue("@chiave", chiaveLibro)
                        cmdCap.ExecuteNonQuery()
                    End Using
                    Using cmdLibro As New SQLiteCommand("DELETE FROM LibriTestoScaricati WHERE Chiave = @chiave", conn, trans)
                        cmdLibro.Parameters.AddWithValue("@chiave", chiaveLibro)
                        cmdLibro.ExecuteNonQuery()
                    End Using
                    trans.Commit()
                End Using
            End Using

            Return True
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Sub AggiornaCatalogoInternetArchive()
        Try
            Dim lavoro As New LavoroArchivioInternet()
            lavoro.EAggiornamentoCatalogo = True
            lavoro.Info = "Avvio..."
            RegistraLavoro(lavoro, "Aggiornamento catalogo Internet Archive", StatoOperazioneCorrente.ArchivioInternet)
            lavoro.InPreparazione = False
            AggiungiRisorsa(lavoro, "archivio:catalogo")

            lavoro.ThreadLavoro = New Thread(AddressOf ProcessaAggiornamentoCatalogoArchivioInternet)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriArchivioInternetAttivi() As List(Of LavoroArchivioInternet)
        Try
            Dim risultato As New List(Of LavoroArchivioInternet)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroArchivio As LavoroArchivioInternet = TryCast(lavoro, LavoroArchivioInternet)
                If lavoroArchivio IsNot Nothing Then risultato.Add(lavoroArchivio)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroArchivioInternet)()
        End Try
    End Function

    Private Sub ProcessaAggiornamentoCatalogoArchivioInternet(parametro As Object)
        Dim lavoro As LavoroArchivioInternet = TryCast(parametro, LavoroArchivioInternet)
        Dim totaleTesti As Integer = 0
        Dim totaleAudio As Integer = 0
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS ArchivioCatalogo (Id INTEGER PRIMARY KEY AUTOINCREMENT, Titolo TEXT, Autore TEXT, Genere TEXT, Identificatore TEXT, TipoSupporto TEXT, Lingua TEXT, ChiaveUnivoca TEXT UNIQUE);" &
                                      "CREATE INDEX IF NOT EXISTS IdxArchivioAutore ON ArchivioCatalogo (Autore);" &
                                      "CREATE INDEX IF NOT EXISTS IdxArchivioGenere ON ArchivioCatalogo (Genere);" &
                                      "CREATE INDEX IF NOT EXISTS IdxArchivioSupporto ON ArchivioCatalogo (TipoSupporto);" &
                                      "CREATE TABLE IF NOT EXISTS ImpostazioniCatalogoArchivio (Chiave TEXT PRIMARY KEY, Valore TEXT);"
                    cmd.ExecuteNonQuery()
                End Using

                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ArchivioCatalogo ADD COLUMN Descrizione TEXT", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ArchivioCatalogo ADD COLUMN Anno TEXT", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch
                End Try
                Try
                    Using cmdMigra As New SQLiteCommand("ALTER TABLE ArchivioCatalogo ADD COLUMN Downloads INTEGER", conn)
                        cmdMigra.ExecuteNonQuery()
                    End Using
                Catch
                End Try
                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "ArchivioCatalogo")
                Catch exChiave As Exception
                    _log.ScriviLog(exChiave.ToString())
                End Try
            End Using

            totaleTesti = ProcessaCatalogoArchivioPerSupporto(lavoro, "texts", "Testo")
            If Not lavoro.Annulla Then
                totaleAudio = ProcessaCatalogoArchivioPerSupporto(lavoro, "audio", "Audio")
            End If

            If lavoro.Annulla Then
                lavoro.Info = "Aggiornamento annullato."
                NotificaFineOperazione("Aggiornamento catalogo Internet Archive annullato.")
            Else
                lavoro.Progresso = 100
                lavoro.Info = $"Completato: {totaleTesti} testi, {totaleAudio} audio."
                NotificaFineOperazione($"Aggiornamento catalogo Internet Archive completato. {totaleTesti + totaleAudio} elementi nuovi o aggiornati (testi: {totaleTesti}, audio: {totaleAudio}).")
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore durante l'aggiornamento del catalogo Internet Archive.")
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Function LeggiImpostazioneCatalogoArchivio(chiave As String) As String
        Try
            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT Valore FROM ImpostazioniCatalogoArchivio WHERE Chiave = @chiave", conn)
                    cmd.Parameters.AddWithValue("@chiave", chiave)
                    Dim risultato As Object = cmd.ExecuteScalar()
                    If risultato IsNot Nothing AndAlso Not IsDBNull(risultato) Then Return risultato.ToString()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return ""
    End Function

    Private Sub ScriviImpostazioneCatalogoArchivio(chiave As String, valore As String)
        Try
            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO ImpostazioniCatalogoArchivio (Chiave, Valore) VALUES (@chiave, @valore)", conn)
                    cmd.Parameters.AddWithValue("@chiave", chiave)
                    cmd.Parameters.AddWithValue("@valore", valore)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function ProcessaCatalogoArchivioPerSupporto(lavoro As LavoroArchivioInternet, mediatype As String, etichettaSupporto As String) As Integer
        Dim totale As Integer = 0
        Const numeroMassimoPagine As Integer = 100
        Try
            Dim client As New HttpClient()
            client.Timeout = TimeSpan.FromSeconds(60)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-InternetArchive/1.0 (accessibilita; postmaster@wingabriel.net)")

            Dim clausolaSupporto As String
            If String.Equals(mediatype, "texts", StringComparison.OrdinalIgnoreCase) Then
                clausolaSupporto = "mediatype:(texts) AND (format:(DjVu) OR collection:(gutenberg))"
            ElseIf String.Equals(mediatype, "audio", StringComparison.OrdinalIgnoreCase) Then
                clausolaSupporto = "mediatype:(audio) AND collection:(librivoxaudio)"
            Else
                clausolaSupporto = "mediatype:(" & mediatype & ")"
            End If

            Dim chiaveImpostazione As String = "UltimoAggiornamento_" & etichettaSupporto
            Dim ultimoAggiornamento As String = LeggiImpostazioneCatalogoArchivio(chiaveImpostazione)
            Dim eAggiornamentoIncrementale As Boolean = Not String.IsNullOrWhiteSpace(ultimoAggiornamento)

            Dim query As String = "(language:(ita) OR language:(Italian) OR language:(it)) AND " & clausolaSupporto
            If eAggiornamentoIncrementale Then
                query &= " AND addeddate:[" & ultimoAggiornamento & " TO *]"
            End If

            Dim cursore As String = ""
            Dim contatorePagine As Integer = 0
            Dim chiaviGiaViste As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim dataInizioAggiornamento As String = DateTime.UtcNow.ToString("yyyy-MM-dd")

            Do
                If lavoro.Annulla Then Exit Do

                lavoro.Info = $"Internet Archive ({etichettaSupporto}): {totale} elementi recuperati..."

                Dim url As String = URL_SCRAPE_ARCHIVE & "?q=" & Uri.EscapeDataString(query) & "&fields=identifier,title,creator,subject,mediatype,description,date,downloads&sorts=downloads+desc&count=1000"
                If Not String.IsNullOrWhiteSpace(cursore) Then
                    url &= "&cursor=" & Uri.EscapeDataString(cursore)
                End If

                Dim responseStr As String = EseguiRichiestaTestoConRetry(client, url)
                If String.IsNullOrWhiteSpace(responseStr) Then Exit Do

                Dim json As JObject
                Try
                    json = JObject.Parse(responseStr)
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                    Exit Do
                End Try

                Dim elementi As JArray = TryCast(json("items"), JArray)
                If elementi Is Nothing OrElse elementi.Count = 0 Then Exit Do

                Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                    conn.Open()
                    Using trans As SQLiteTransaction = conn.BeginTransaction()
                        Using cmdLookupChiave As New SQLiteCommand("SELECT ChiaveWG FROM ArchivioCatalogo WHERE ChiaveUnivoca = @chiave", conn, trans),
                              cmd As New SQLiteCommand("INSERT OR REPLACE INTO ArchivioCatalogo (Titolo, Autore, Genere, Identificatore, TipoSupporto, Lingua, Descrizione, Anno, Downloads, ChiaveUnivoca, ChiaveWG) VALUES (@titolo, @autore, @genere, @identificatore, @tiposupporto, 'it', @descrizione, @anno, @downloads, @chiave, @chiaveWG)", conn, trans)
                            Dim pLookupChiave As SQLiteParameter = cmdLookupChiave.Parameters.Add("@chiave", DbType.String)
                            Dim pTitolo As SQLiteParameter = cmd.Parameters.Add("@titolo", DbType.String)
                            Dim pAutore As SQLiteParameter = cmd.Parameters.Add("@autore", DbType.String)
                            Dim pGenere As SQLiteParameter = cmd.Parameters.Add("@genere", DbType.String)
                            Dim pIdentificatore As SQLiteParameter = cmd.Parameters.Add("@identificatore", DbType.String)
                            Dim pTipoSupporto As SQLiteParameter = cmd.Parameters.Add("@tiposupporto", DbType.String)
                            Dim pDescrizione As SQLiteParameter = cmd.Parameters.Add("@descrizione", DbType.String)
                            Dim pAnno As SQLiteParameter = cmd.Parameters.Add("@anno", DbType.String)
                            Dim pDownloads As SQLiteParameter = cmd.Parameters.Add("@downloads", DbType.Int32)
                            Dim pChiave As SQLiteParameter = cmd.Parameters.Add("@chiave", DbType.String)
                            Dim pChiaveWG As SQLiteParameter = cmd.Parameters.Add("@chiaveWG", DbType.String)

                            For Each elemento As JObject In elementi
                                Try
                                    Dim identificatore As String = elemento("identifier")?.ToString()
                                    If String.IsNullOrWhiteSpace(identificatore) Then Continue For

                                    Dim titolo As String = If(elemento("title")?.ToString(), "Titolo sconosciuto")
                                    Dim autore As String = NormalizzaAutoreArchivio(elemento("creator"))
                                    Dim genere As String = ClassificaGenereArchivio(elemento("subject"))

                                    Dim chiaveDuplicato As String = titolo.Trim().ToLowerInvariant() & "|" & autore.Trim().ToLowerInvariant()
                                    If chiaviGiaViste.Contains(chiaveDuplicato) Then Continue For
                                    chiaviGiaViste.Add(chiaveDuplicato)

                                    Dim descrizione As String = EstraiPrimoValoreArchivio(elemento("description"))
                                    If descrizione.Length > 1000 Then descrizione = descrizione.Substring(0, 1000)
                                    Dim anno As String = EstraiPrimoValoreArchivio(elemento("date"))
                                    If anno.Length > 10 Then anno = anno.Substring(0, 10)
                                    Dim downloads As Integer = 0
                                    Integer.TryParse(elemento("downloads")?.ToString(), downloads)

                                    pTitolo.Value = titolo
                                    pAutore.Value = autore
                                    pGenere.Value = genere
                                    pIdentificatore.Value = identificatore
                                    pTipoSupporto.Value = etichettaSupporto
                                    pDescrizione.Value = descrizione
                                    pAnno.Value = anno
                                    pDownloads.Value = downloads
                                    Dim chiaveUnivocaRiga As String = "archive_" & identificatore
                                    pChiave.Value = chiaveUnivocaRiga

                                    pLookupChiave.Value = chiaveUnivocaRiga
                                    Dim chiaveWGRiga As String = ""
                                    Dim valoreChiaveWGRiga As Object = cmdLookupChiave.ExecuteScalar()
                                    If valoreChiaveWGRiga IsNot Nothing AndAlso Not IsDBNull(valoreChiaveWGRiga) Then chiaveWGRiga = Convert.ToString(valoreChiaveWGRiga)
                                    If String.IsNullOrWhiteSpace(chiaveWGRiga) Then chiaveWGRiga = WGDatabase.GeneraChiave()
                                    pChiaveWG.Value = chiaveWGRiga

                                    cmd.ExecuteNonQuery()
                                    totale += 1
                                Catch exElemento As Exception
                                    _log.ScriviLog(exElemento.ToString())
                                End Try
                            Next
                        End Using
                        trans.Commit()
                    End Using
                End Using

                cursore = json("cursor")?.ToString()
                contatorePagine += 1

                If etichettaSupporto = "Testo" Then
                    lavoro.Progresso = Math.Min(45, contatorePagine)
                Else
                    lavoro.Progresso = Math.Min(95, 45 + contatorePagine)
                End If
            Loop While Not String.IsNullOrWhiteSpace(cursore) AndAlso contatorePagine < numeroMassimoPagine

            If Not lavoro.Annulla Then
                ScriviImpostazioneCatalogoArchivio(chiaveImpostazione, dataInizioAggiornamento)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return totale
    End Function

    Private Function EstraiPrimoValoreArchivio(valore As JToken) As String
        Try
            If valore Is Nothing Then Return ""
            If valore.Type = JTokenType.Array Then
                Dim elenco As JArray = CType(valore, JArray)
                If elenco.Count = 0 Then Return ""
                Return elenco(0).ToString()
            End If
            Return valore.ToString()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EstraiElencoValoriArchivio(valore As JToken) As List(Of String)
        Dim risultato As New List(Of String)
        Try
            If valore Is Nothing Then Return risultato
            If valore.Type = JTokenType.Array Then
                For Each voce As JToken In CType(valore, JArray)
                    Dim testo As String = voce?.ToString()
                    If Not String.IsNullOrWhiteSpace(testo) Then risultato.Add(testo)
                Next
            Else
                Dim testo As String = valore.ToString()
                If Not String.IsNullOrWhiteSpace(testo) Then risultato.Add(testo)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function NormalizzaAutoreArchivio(valoreCreator As JToken) As String
        Try
            Dim autore As String = EstraiPrimoValoreArchivio(valoreCreator)
            autore = If(autore, "").Trim()

            If String.IsNullOrWhiteSpace(autore) Then Return "Autore sconosciuto"

            Dim autoriIgnoti As String() = {"unknown", "sconosciuto", "anonymous", "anonimo", "s.n.", "n.a.", "non identificato"}
            For Each valoreIgnoto As String In autoriIgnoti
                If String.Equals(autore, valoreIgnoto, StringComparison.OrdinalIgnoreCase) Then Return "Autore sconosciuto"
            Next

            Dim posizioneVirgola As Integer = autore.IndexOf(",")
            If posizioneVirgola > 0 AndAlso posizioneVirgola < autore.Length - 1 Then
                Dim cognome As String = autore.Substring(0, posizioneVirgola).Trim()
                Dim resto As String = autore.Substring(posizioneVirgola + 1).Trim()
                Dim posizioneVirgolaInterna As Integer = resto.IndexOf(",")
                If posizioneVirgolaInterna >= 0 Then resto = resto.Substring(0, posizioneVirgolaInterna).Trim()

                Dim contieneCifre As Boolean = False
                For Each carattere As Char In resto
                    If Char.IsDigit(carattere) Then
                        contieneCifre = True
                        Exit For
                    End If
                Next

                If Not String.IsNullOrWhiteSpace(cognome) AndAlso Not String.IsNullOrWhiteSpace(resto) AndAlso resto.Length < 60 AndAlso Not contieneCifre Then
                    Return resto & " " & cognome
                End If
            End If

            Return autore
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "Autore sconosciuto"
        End Try
    End Function

    Private Function ClassificaGenereArchivio(valoriSubject As JToken) As String
        Const generePredefinito As String = "Da classificare"
        Try
            Dim elenco As List(Of String) = EstraiElencoValoriArchivio(valoriSubject)
            If elenco.Count = 0 Then Return generePredefinito

            Dim mappaGeneri As New List(Of KeyValuePair(Of String, String)) From {
                New KeyValuePair(Of String, String)("romanz", "Romanzi"),
                New KeyValuePair(Of String, String)("novel", "Romanzi"),
                New KeyValuePair(Of String, String)("racconti", "Racconti"),
                New KeyValuePair(Of String, String)("novelle", "Racconti"),
                New KeyValuePair(Of String, String)("short stor", "Racconti"),
                New KeyValuePair(Of String, String)("fiaba", "Fiabe"),
                New KeyValuePair(Of String, String)("fiabe", "Fiabe"),
                New KeyValuePair(Of String, String)("fairy tale", "Fiabe"),
                New KeyValuePair(Of String, String)("poesia", "Poesia"),
                New KeyValuePair(Of String, String)("poesie", "Poesia"),
                New KeyValuePair(Of String, String)("poetry", "Poesia"),
                New KeyValuePair(Of String, String)("poem", "Poesia"),
                New KeyValuePair(Of String, String)("teatro", "Teatro"),
                New KeyValuePair(Of String, String)("drama", "Teatro"),
                New KeyValuePair(Of String, String)("play", "Teatro"),
                New KeyValuePair(Of String, String)("commedia", "Teatro"),
                New KeyValuePair(Of String, String)("biograf", "Biografie"),
                New KeyValuePair(Of String, String)("biography", "Biografie"),
                New KeyValuePair(Of String, String)("storia", "Storia"),
                New KeyValuePair(Of String, String)("history", "Storia"),
                New KeyValuePair(Of String, String)("filosofia", "Filosofia"),
                New KeyValuePair(Of String, String)("philosophy", "Filosofia"),
                New KeyValuePair(Of String, String)("saggi", "Saggistica"),
                New KeyValuePair(Of String, String)("saggistica", "Saggistica"),
                New KeyValuePair(Of String, String)("essay", "Saggistica"),
                New KeyValuePair(Of String, String)("religion", "Religione"),
                New KeyValuePair(Of String, String)("religione", "Religione"),
                New KeyValuePair(Of String, String)("scien", "Scienza"),
                New KeyValuePair(Of String, String)("art", "Arte"),
                New KeyValuePair(Of String, String)("arte", "Arte"),
                New KeyValuePair(Of String, String)("viagg", "Viaggi"),
                New KeyValuePair(Of String, String)("travel", "Viaggi"),
                New KeyValuePair(Of String, String)("music", "Musica"),
                New KeyValuePair(Of String, String)("musica", "Musica"),
                New KeyValuePair(Of String, String)("fantas", "Fantasy"),
                New KeyValuePair(Of String, String)("giallo", "Giallo"),
                New KeyValuePair(Of String, String)("detective", "Giallo"),
                New KeyValuePair(Of String, String)("avventur", "Avventura"),
                New KeyValuePair(Of String, String)("adventure", "Avventura"),
                New KeyValuePair(Of String, String)("politic", "Politica"),
                New KeyValuePair(Of String, String)("economia", "Economia"),
                New KeyValuePair(Of String, String)("economics", "Economia"),
                New KeyValuePair(Of String, String)("diritto", "Diritto"),
                New KeyValuePair(Of String, String)("law", "Diritto"),
                New KeyValuePair(Of String, String)("medicina", "Medicina"),
                New KeyValuePair(Of String, String)("medic", "Medicina")
            }

            For Each voce As String In elenco
                Dim voceMinuscola As String = voce.ToLowerInvariant()
                For Each coppia As KeyValuePair(Of String, String) In mappaGeneri
                    If voceMinuscola.Contains(coppia.Key) Then Return coppia.Value
                Next
            Next

            Return generePredefinito
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return generePredefinito
        End Try
    End Function

    Public Sub ScaricaLibriArchivioInternet(elementi As List(Of ElementoDaScaricareArchivioInternet), cartellaDestinazioneAudio As String, cartellaDestinazioneTesti As String)
        Try
            If elementi Is Nothing OrElse elementi.Count = 0 Then Return

            Dim lavoro As New LavoroArchivioInternet()
            lavoro.CodaDownload = New List(Of ElementoDaScaricareArchivioInternet)(elementi)
            lavoro.CartellaAudio = cartellaDestinazioneAudio
            lavoro.CartellaTesti = cartellaDestinazioneTesti
            lavoro.Info = "Inizializzazione..."
            RegistraLavoro(lavoro, "Download libri Internet Archive", StatoOperazioneCorrente.ArchivioInternet)
            lavoro.InPreparazione = False

            For Each elemento As ElementoDaScaricareArchivioInternet In lavoro.CodaDownload
                AggiungiRisorsa(lavoro, "archivio:" & elemento.Identificatore)
            Next

            lavoro.ThreadLavoro = New Thread(AddressOf ProcessaDownloadLibriArchivioInternet)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function NomeFileSicuroArchivioInternet(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return "senza_titolo"
            Dim risultato As String = nome
            For Each carattereNonValido As Char In Path.GetInvalidFileNameChars()
                risultato = risultato.Replace(carattereNonValido, "_"c)
            Next
            If risultato.Length > 120 Then risultato = risultato.Substring(0, 120)
            Return risultato.Trim()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "senza_titolo"
        End Try
    End Function

    Private Sub ProcessaDownloadLibriArchivioInternet(parametro As Object)
        Dim lavoro As LavoroArchivioInternet = TryCast(parametro, LavoroArchivioInternet)
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS LibriArchivioScaricati (Identificatore TEXT PRIMARY KEY, Titolo TEXT, Autore TEXT, Tipo TEXT, PercorsoLocale TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS CapitoliArchivioScaricati (Id INTEGER PRIMARY KEY AUTOINCREMENT, Identificatore TEXT, TitoloCapitolo TEXT, PercorsoFile TEXT);" &
                                      "CREATE INDEX IF NOT EXISTS IdxCapitoliArchivioIdentificatore ON CapitoliArchivioScaricati (Identificatore);"
                    cmd.ExecuteNonQuery()
                End Using
                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "LibriArchivioScaricati")
                    WGDatabase.AssicuraColonnaChiave(conn, "CapitoliArchivioScaricati")
                Catch exChiave As Exception
                    _log.ScriviLog(exChiave.ToString())
                End Try
            End Using

            Dim client As New HttpClient()
            client.Timeout = TimeSpan.FromSeconds(60)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-InternetArchive/1.0 (accessibilita; postmaster@wingabriel.net)")

            Dim totaleLibri As Integer = lavoro.CodaDownload.Count
            Dim completati As Integer = 0
            lavoro.LibriTotali = totaleLibri
            lavoro.LibriCompletati = 0
            lavoro.BytesTotaliLibro = 0
            lavoro.AzzeraBytes()

            For Each libro As ElementoDaScaricareArchivioInternet In lavoro.CodaDownload
                If lavoro.Annulla Then Exit For

                lavoro.BytesTotaliLibro = 0
                lavoro.TracceTotaliLibro = 0
                lavoro.AzzeraBytes()
                lavoro.Info = $"Download di {libro.Titolo}, elemento {completati + 1} di {totaleLibri}"

                Try
                    If libro.EAudio Then
                        ScaricaLibroAudioArchivioInternet(lavoro, client, libro)
                    Else
                        ScaricaLibroTestoArchivioInternet(lavoro, client, libro)
                    End If
                Catch exLibro As Exception
                    _log.ScriviLog($"Errore download libro Internet Archive {libro.Identificatore}: {exLibro.Message}")
                End Try

                completati += 1
                lavoro.LibriCompletati = completati
                lavoro.BytesTotaliLibro = 0
                lavoro.AzzeraBytes()
                lavoro.Progresso = CInt((completati / totaleLibri) * 100)
            Next

            If lavoro.Annulla Then
                NotificaFineOperazione("Download Internet Archive annullato.")
            Else
                NotificaFineOperazione($"Download Internet Archive completato. {completati} elementi elaborati su {totaleLibri}.")
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore durante il download da Internet Archive.")
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Sub ScaricaLibroAudioArchivioInternet(lavoro As LavoroArchivioInternet, client As HttpClient, libro As ElementoDaScaricareArchivioInternet)
        Const numeroMassimoParalleli As Integer = 4
        Try
            Dim urlMetadati As String = URL_BASE_ARCHIVE & "/metadata/" & libro.Identificatore
            Dim rispostaMetadati As String = EseguiRichiestaTestoConRetry(client, urlMetadati)
            If String.IsNullOrWhiteSpace(rispostaMetadati) Then Return

            Dim json As JObject = JObject.Parse(rispostaMetadati)
            Dim elencoFile As JArray = TryCast(json("files"), JArray)
            If elencoFile Is Nothing Then Return

            Dim nomiFile As New List(Of String)
            Dim md5PerFile As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim dimensioneComplessiva As Long = 0
            For Each elemento As JObject In elencoFile
                Dim nome As String = elemento("name")?.ToString()
                If Not String.IsNullOrWhiteSpace(nome) AndAlso nome.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) Then
                    nomiFile.Add(nome)
                    Dim md5File As String = elemento("md5")?.ToString()
                    If Not String.IsNullOrWhiteSpace(md5File) Then md5PerFile(nome) = md5File
                    Dim dimensioneTesto As String = elemento("size")?.ToString()
                    If Not String.IsNullOrWhiteSpace(dimensioneTesto) Then
                        Dim dimensioneFile As Long = 0
                        If Long.TryParse(dimensioneTesto, dimensioneFile) Then dimensioneComplessiva += dimensioneFile
                    End If
                End If
            Next
            nomiFile.Sort()

            If nomiFile.Count = 0 Then Return

            lavoro.AzzeraBytes()
            lavoro.BytesTotaliLibro = dimensioneComplessiva
            lavoro.TracceTotaliLibro = nomiFile.Count

            Dim cartellaLibro As String = Path.Combine(lavoro.CartellaAudio, NomeFileSicuroArchivioInternet(libro.Titolo))
            If Not Directory.Exists(cartellaLibro) Then Directory.CreateDirectory(cartellaLibro)

            Dim percorsiPerIndice As List(Of String) = nomiFile
            Dim indiceCorrente As Integer = 0

            While indiceCorrente < nomiFile.Count
                If lavoro.Annulla Then Exit While

                Dim statiLotto As New List(Of ScaricamentoFileArchivio)
                Dim compitiLotto As New List(Of Task)
                Dim fineLotto As Integer = Math.Min(indiceCorrente + numeroMassimoParalleli, nomiFile.Count)

                For indice As Integer = indiceCorrente To fineLotto - 1
                    Try
                        Dim nomeFileOriginale As String = nomiFile(indice)
                        Dim urlFile As String = URL_BASE_ARCHIVE & "/download/" & libro.Identificatore & "/" & nomeFileOriginale
                        Dim nomeFileFinale As String = (indice + 1).ToString("00") & " - " & NomeFileSicuroArchivioInternet(Path.GetFileNameWithoutExtension(nomeFileOriginale)) & ".mp3"
                        Dim percorsoFile As String = Path.Combine(cartellaLibro, nomeFileFinale)

                        Dim stato As New ScaricamentoFileArchivio(_log)
                        stato.Client = client
                        stato.Lavoro = lavoro
                        stato.Url = urlFile
                        stato.PercorsoFile = percorsoFile
                        lavoro.Info = $"Download di {libro.Titolo}, traccia {indice + 1} di {nomiFile.Count}"
                        If md5PerFile.ContainsKey(nomeFileOriginale) Then stato.Md5Atteso = md5PerFile(nomeFileOriginale)

                        statiLotto.Add(stato)
                        percorsiPerIndice(indice) = percorsoFile
                        compitiLotto.Add(Task.Run(AddressOf stato.Esegui))
                    Catch exAvvio As Exception
                        _log.ScriviLog($"Errore avvio download traccia {nomiFile(indice)} del libro {libro.Identificatore}: {exAvvio.Message}")
                    End Try
                Next

                Try
                    Task.WaitAll(compitiLotto.ToArray())
                Catch exAttesa As Exception
                    _log.ScriviLog(exAttesa.ToString())
                End Try

                indiceCorrente = fineLotto

                If indiceCorrente < nomiFile.Count Then
                    Thread.Sleep(500)
                End If
            End While

            Dim percorsiSalvati As New List(Of String)
            For Each percorso As String In percorsiPerIndice
                If Not String.IsNullOrWhiteSpace(percorso) AndAlso File.Exists(percorso) Then
                    percorsiSalvati.Add(percorso)
                End If
            Next

            If percorsiSalvati.Count = 0 Then Return

            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Dim chiaveWGLibro As String = ""
                    Using cmdChiaveLibro As New SQLiteCommand("SELECT ChiaveWG FROM LibriArchivioScaricati WHERE Identificatore = @id", conn, trans)
                        cmdChiaveLibro.Parameters.AddWithValue("@id", libro.Identificatore)
                        Dim valoreChiaveLibro As Object = cmdChiaveLibro.ExecuteScalar()
                        If valoreChiaveLibro IsNot Nothing AndAlso Not IsDBNull(valoreChiaveLibro) Then chiaveWGLibro = Convert.ToString(valoreChiaveLibro)
                    End Using
                    If String.IsNullOrWhiteSpace(chiaveWGLibro) Then chiaveWGLibro = WGDatabase.GeneraChiave()

                    Using cmdLibro As New SQLiteCommand("INSERT OR REPLACE INTO LibriArchivioScaricati (Identificatore, Titolo, Autore, Tipo, PercorsoLocale, ChiaveWG) VALUES (@id, @titolo, @autore, 'Audio', @path, @chiaveWG)", conn, trans)
                        cmdLibro.Parameters.AddWithValue("@id", libro.Identificatore)
                        cmdLibro.Parameters.AddWithValue("@titolo", libro.Titolo)
                        cmdLibro.Parameters.AddWithValue("@autore", libro.Autore)
                        cmdLibro.Parameters.AddWithValue("@path", cartellaLibro)
                        cmdLibro.Parameters.AddWithValue("@chiaveWG", chiaveWGLibro)
                        cmdLibro.ExecuteNonQuery()
                    End Using

                    Dim chiaviWGEsistentiCapitoli As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                    Using cmdChiaviCap As New SQLiteCommand("SELECT TitoloCapitolo, ChiaveWG FROM CapitoliArchivioScaricati WHERE Identificatore = @id", conn, trans)
                        cmdChiaviCap.Parameters.AddWithValue("@id", libro.Identificatore)
                        Using readerCap As SQLiteDataReader = cmdChiaviCap.ExecuteReader()
                            While readerCap.Read()
                                If Not readerCap.IsDBNull(0) AndAlso Not readerCap.IsDBNull(1) Then
                                    Dim titoloEsistente As String = readerCap.GetString(0)
                                    Dim chiaveWGEsistente As String = readerCap.GetString(1)
                                    If Not String.IsNullOrWhiteSpace(titoloEsistente) AndAlso Not String.IsNullOrWhiteSpace(chiaveWGEsistente) AndAlso Not chiaviWGEsistentiCapitoli.ContainsKey(titoloEsistente) Then
                                        chiaviWGEsistentiCapitoli.Add(titoloEsistente, chiaveWGEsistente)
                                    End If
                                End If
                            End While
                        End Using
                    End Using

                    Using cmdDelCaps As New SQLiteCommand("DELETE FROM CapitoliArchivioScaricati WHERE Identificatore = @id", conn, trans)
                        cmdDelCaps.Parameters.AddWithValue("@id", libro.Identificatore)
                        cmdDelCaps.ExecuteNonQuery()
                    End Using

                    Using cmdCaps As New SQLiteCommand("INSERT INTO CapitoliArchivioScaricati (Identificatore, TitoloCapitolo, PercorsoFile, ChiaveWG) VALUES (@id, @tit, @file, @chiaveWG)", conn, trans)
                        Dim pId As SQLiteParameter = cmdCaps.Parameters.Add("@id", DbType.String)
                        Dim pTit As SQLiteParameter = cmdCaps.Parameters.Add("@tit", DbType.String)
                        Dim pFile As SQLiteParameter = cmdCaps.Parameters.Add("@file", DbType.String)
                        Dim pChiaveWGCap As SQLiteParameter = cmdCaps.Parameters.Add("@chiaveWG", DbType.String)
                        pId.Value = libro.Identificatore
                        For Each percorso As String In percorsiSalvati
                            Dim titoloCapitolo As String = Path.GetFileNameWithoutExtension(percorso)
                            Dim chiaveWGCapitolo As String = ""
                            chiaviWGEsistentiCapitoli.TryGetValue(titoloCapitolo, chiaveWGCapitolo)
                            If String.IsNullOrWhiteSpace(chiaveWGCapitolo) Then chiaveWGCapitolo = WGDatabase.GeneraChiave()
                            pTit.Value = titoloCapitolo
                            pFile.Value = percorso
                            pChiaveWGCap.Value = chiaveWGCapitolo
                            cmdCaps.ExecuteNonQuery()
                        Next
                    End Using
                    trans.Commit()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ScaricaLibroTestoArchivioInternet(lavoro As LavoroArchivioInternet, client As HttpClient, libro As ElementoDaScaricareArchivioInternet)
        Try
            Dim urlMetadati As String = URL_BASE_ARCHIVE & "/metadata/" & libro.Identificatore
            Dim rispostaMetadati As String = EseguiRichiestaTestoConRetry(client, urlMetadati)
            If String.IsNullOrWhiteSpace(rispostaMetadati) Then Return

            Dim json As JObject = JObject.Parse(rispostaMetadati)
            Dim elencoFile As JArray = TryCast(json("files"), JArray)
            If elencoFile Is Nothing Then Return

            Dim nomeFileTesto As String = ""
            Dim md5Testo As String = ""

            For Each elemento As JObject In elencoFile
                Dim nome As String = elemento("name")?.ToString()
                If Not String.IsNullOrWhiteSpace(nome) AndAlso nome.EndsWith("_djvu.txt", StringComparison.OrdinalIgnoreCase) Then
                    nomeFileTesto = nome
                    md5Testo = elemento("md5")?.ToString()
                    Exit For
                End If
            Next

            If String.IsNullOrWhiteSpace(nomeFileTesto) Then
                For Each elemento As JObject In elencoFile
                    Dim nome As String = elemento("name")?.ToString()
                    If Not String.IsNullOrWhiteSpace(nome) AndAlso nome.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) Then
                        nomeFileTesto = nome
                        md5Testo = elemento("md5")?.ToString()
                        Exit For
                    End If
                Next
            End If

            If String.IsNullOrWhiteSpace(nomeFileTesto) Then Return

            Dim urlFile As String = URL_BASE_ARCHIVE & "/download/" & libro.Identificatore & "/" & nomeFileTesto
            Dim datiTesto As Byte() = EseguiRichiestaByteConRetry(client, urlFile)
            If datiTesto Is Nothing OrElse datiTesto.Length = 0 Then Return

            If Not String.IsNullOrWhiteSpace(md5Testo) Then
                Dim md5Calcolato As String = ""
                Try
                    Using algoritmo As MD5 = MD5.Create()
                        Dim hash As Byte() = algoritmo.ComputeHash(datiTesto)
                        Dim generatore As New Text.StringBuilder()
                        For Each singoloByte As Byte In hash
                            generatore.Append(singoloByte.ToString("x2"))
                        Next
                        md5Calcolato = generatore.ToString()
                    End Using
                Catch exMd5 As Exception
                    _log.ScriviLog(exMd5.ToString())
                End Try

                If Not String.IsNullOrWhiteSpace(md5Calcolato) AndAlso Not String.Equals(md5Calcolato, md5Testo, StringComparison.OrdinalIgnoreCase) Then
                    Return
                End If
            End If

            Dim testo As String = Text.Encoding.UTF8.GetString(datiTesto)
            If String.IsNullOrWhiteSpace(testo) Then Return

            If Not Directory.Exists(lavoro.CartellaTesti) Then
                Directory.CreateDirectory(lavoro.CartellaTesti)
            End If

            Dim percorsoFile As String = Path.Combine(lavoro.CartellaTesti, NomeFileSicuroArchivioInternet(libro.Titolo) & ".txt")
            File.WriteAllText(percorsoFile, testo)

            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Dim chiaveWGLibro As String = ""
                Using cmdChiaveLibro As New SQLiteCommand("SELECT ChiaveWG FROM LibriArchivioScaricati WHERE Identificatore = @id", conn)
                    cmdChiaveLibro.Parameters.AddWithValue("@id", libro.Identificatore)
                    Dim valoreChiaveLibro As Object = cmdChiaveLibro.ExecuteScalar()
                    If valoreChiaveLibro IsNot Nothing AndAlso Not IsDBNull(valoreChiaveLibro) Then chiaveWGLibro = Convert.ToString(valoreChiaveLibro)
                End Using
                If String.IsNullOrWhiteSpace(chiaveWGLibro) Then chiaveWGLibro = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, "LibriArchivioScaricati")
                Using cmdLibro As New SQLiteCommand("INSERT OR REPLACE INTO LibriArchivioScaricati (Identificatore, Titolo, Autore, Tipo, PercorsoLocale, ChiaveWG) VALUES (@id, @titolo, @autore, 'Testo', @path, @chiaveWG)", conn)
                    cmdLibro.Parameters.AddWithValue("@id", libro.Identificatore)
                    cmdLibro.Parameters.AddWithValue("@titolo", libro.Titolo)
                    cmdLibro.Parameters.AddWithValue("@autore", libro.Autore)
                    cmdLibro.Parameters.AddWithValue("@path", percorsoFile)
                    cmdLibro.Parameters.AddWithValue("@chiaveWG", chiaveWGLibro)
                    cmdLibro.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Function OttieniLibriScaricatiArchivioInternet(Optional filtroTipo As String = "Tutti") As List(Of LibroScaricato)
        Dim risultati As New List(Of LibroScaricato)
        Try
            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS LibriArchivioScaricati (Identificatore TEXT PRIMARY KEY, Titolo TEXT, Autore TEXT, Tipo TEXT, PercorsoLocale TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS CapitoliArchivioScaricati (Id INTEGER PRIMARY KEY AUTOINCREMENT, Identificatore TEXT, TitoloCapitolo TEXT, PercorsoFile TEXT);"
                    cmd.ExecuteNonQuery()
                End Using

                Dim sql As String = "SELECT Identificatore, Titolo, Autore, Tipo, PercorsoLocale FROM LibriArchivioScaricati"
                If String.Equals(filtroTipo, "Audio", StringComparison.OrdinalIgnoreCase) Then
                    sql &= " WHERE Tipo = 'Audio'"
                ElseIf String.Equals(filtroTipo, "Testo", StringComparison.OrdinalIgnoreCase) Then
                    sql &= " WHERE Tipo = 'Testo'"
                End If
                sql &= " ORDER BY Titolo COLLATE NOCASE"

                Using cmd As New SQLiteCommand(sql, conn)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        While r.Read()
                            Dim libro As New LibroScaricato()
                            libro.Id = r("Identificatore").ToString()
                            libro.Titolo = r("Titolo").ToString()
                            libro.Autore = r("Autore").ToString()
                            libro.Tipo = r("Tipo").ToString()
                            libro.PercorsoLocale = r("PercorsoLocale").ToString()
                            risultati.Add(libro)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniCapitoliLibroArchivioInternet(identificatore As String) As List(Of CapitoloScaricato)
        Dim risultati As New List(Of CapitoloScaricato)
        Try
            If String.IsNullOrWhiteSpace(identificatore) Then Return risultati
            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT Id, TitoloCapitolo, PercorsoFile FROM CapitoliArchivioScaricati WHERE Identificatore = @id ORDER BY TitoloCapitolo COLLATE NOCASE", conn)
                    cmd.Parameters.AddWithValue("@id", identificatore)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        While r.Read()
                            Dim capitolo As New CapitoloScaricato()
                            capitolo.Id = Convert.ToInt32(r("Id"))
                            capitolo.TitoloCapitolo = r("TitoloCapitolo").ToString()
                            capitolo.PercorsoFile = r("PercorsoFile").ToString()
                            risultati.Add(capitolo)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function EliminaLibroArchivioInternet(identificatore As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(identificatore) Then Return False

            If RisorsaOccupata("archivio:" & identificatore) Then
                GestisciConflittoOperazione("Internet Archive")
                Return False
            End If

            Dim percorsoLocale As String = ""
            Dim tipo As String = "Audio"
            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using cmd As New SQLiteCommand("SELECT PercorsoLocale, Tipo FROM LibriArchivioScaricati WHERE Identificatore = @id", conn)
                    cmd.Parameters.AddWithValue("@id", identificatore)
                    Using r As SQLiteDataReader = cmd.ExecuteReader()
                        If r.Read() Then
                            percorsoLocale = r("PercorsoLocale").ToString()
                            tipo = r("Tipo").ToString()
                        End If
                    End Using
                End Using
            End Using

            If String.Equals(tipo, "Testo", StringComparison.OrdinalIgnoreCase) Then
                If Not String.IsNullOrWhiteSpace(percorsoLocale) AndAlso File.Exists(percorsoLocale) Then
                    File.Delete(percorsoLocale)
                End If
            Else
                If Not String.IsNullOrWhiteSpace(percorsoLocale) AndAlso Directory.Exists(percorsoLocale) Then
                    Directory.Delete(percorsoLocale, True)
                End If
            End If

            Using conn As New SQLiteConnection(ConnectionStringArchivioInternet)
                conn.Open()
                Using trans As SQLiteTransaction = conn.BeginTransaction()
                    Using cmdCaps As New SQLiteCommand("DELETE FROM CapitoliArchivioScaricati WHERE Identificatore = @id", conn, trans)
                        cmdCaps.Parameters.AddWithValue("@id", identificatore)
                        cmdCaps.ExecuteNonQuery()
                    End Using
                    Using cmdLibro As New SQLiteCommand("DELETE FROM LibriArchivioScaricati WHERE Identificatore = @id", conn, trans)
                        cmdLibro.Parameters.AddWithValue("@id", identificatore)
                        cmdLibro.ExecuteNonQuery()
                    End Using
                    trans.Commit()
                End Using
            End Using

            Return True
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Public Sub ScaricaLibriLibriVox(idsLibri As List(Of String), cartellaDestinazione As String)
        Try
            If Not File.Exists(_path7z) Then
                NotificaFineOperazione("7z.exe non trovato, impossibile decomprimere i libri.")
                Return
            End If
            If idsLibri Is Nothing OrElse idsLibri.Count = 0 Then Return

            Dim lavoro As New LavoroLibriVox()
            lavoro.StatoDownload = New StatoDownloadHttp(_log)
            lavoro.TipoOperazione = TipoOperazioneLibriVox.DownloadLibri
            lavoro.CodaDownload = New List(Of String)(idsLibri)
            lavoro.CartellaDownload = cartellaDestinazione
            lavoro.Info = "Inizializzazione..."
            RegistraLavoro(lavoro, "Download libri LibriVox", StatoOperazioneCorrente.LibriVox)
            lavoro.InPreparazione = False

            For Each idLibro As String In lavoro.CodaDownload
                AggiungiRisorsa(lavoro, "librivox:" & idLibro)
            Next

            AvviaLavoroLibriVox(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub EliminaLibri(idsLibri As List(Of String))
        Try
            If idsLibri Is Nothing OrElse idsLibri.Count = 0 Then Return

            Dim lavoro As New LavoroLibriVox()
            lavoro.StatoDownload = New StatoDownloadHttp(_log)
            lavoro.TipoOperazione = TipoOperazioneLibriVox.EliminaLibri
            lavoro.CodaEliminazione = New List(Of String)(idsLibri)
            lavoro.Info = "Avvio eliminazione..."
            RegistraLavoro(lavoro, "Eliminazione libri scaricati", StatoOperazioneCorrente.LibriVox)
            lavoro.InPreparazione = False

            For Each idLibro As String In lavoro.CodaEliminazione
                AggiungiRisorsa(lavoro, "librivox:" & idLibro)
            Next

            AvviaLavoroLibriVox(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AvviaLavoroLibriVox(lavoro As LavoroLibriVox)
        Try
            If lavoro Is Nothing Then Return
            lavoro.ThreadLavoro = New Thread(AddressOf EseguiOperazioniLibriVoxLoop)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            RimuoviLavoro(lavoro)
        End Try
    End Sub

    Private Function LavoriLibriVoxAttivi() As List(Of LavoroLibriVox)
        Try
            Dim risultato As New List(Of LavoroLibriVox)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroLibriVox As LavoroLibriVox = TryCast(lavoro, LavoroLibriVox)
                If lavoroLibriVox IsNot Nothing Then risultato.Add(lavoroLibriVox)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroLibriVox)()
        End Try
    End Function

    Private Sub EseguiOperazioniLibriVoxLoop(parametro As Object)
        Dim lavoro As LavoroLibriVox = TryCast(parametro, LavoroLibriVox)
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return

            If lavoro.TipoOperazione = TipoOperazioneLibriVox.AggiornamentoCatalogo Then
                ProcessaAggiornamentoCatalogo(lavoro)
            ElseIf lavoro.TipoOperazione = TipoOperazioneLibriVox.DownloadLibri Then
                ProcessaDownloadLibri(lavoro)
            ElseIf lavoro.TipoOperazione = TipoOperazioneLibriVox.EliminaLibri Then
                ProcessaEliminazioneLibri(lavoro)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore generico operazione LibriVox.")
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Sub ProcessaAggiornamentoCatalogo(lavoro As LavoroLibriVox)
        Try
            Using conn As New SQLiteConnection(ConnectionString)
                conn.Open()
                Using cmdPragma As New SQLiteCommand(conn)
                    cmdPragma.CommandText = "PRAGMA synchronous = OFF;PRAGMA journal_mode = MEMORY;"
                    cmdPragma.ExecuteNonQuery()
                End Using

                Using cmd As New SQLiteCommand(conn)
                    cmd.CommandText = "CREATE TABLE IF NOT EXISTS Libri (id TEXT PRIMARY KEY, titolo TEXT, autore TEXT, lingua TEXT, zip TEXT, generi TEXT);" &
                                      "CREATE TABLE IF NOT EXISTS LibriScaricati (IdLibro TEXT PRIMARY KEY, Titolo TEXT, Autore TEXT, PercorsoLocale TEXT);" &
                                      "CREATE INDEX IF NOT EXISTS idx_LibriScaricati_Titolo ON LibriScaricati(Titolo);" &
                                      "CREATE INDEX IF NOT EXISTS idx_LibriScaricati_Autore ON LibriScaricati(Autore);" &
                                      "CREATE TABLE IF NOT EXISTS CapitoliScaricati (Id INTEGER PRIMARY KEY AUTOINCREMENT, IdLibro TEXT, TitoloCapitolo TEXT, PercorsoFile TEXT);" &
                                      "CREATE INDEX IF NOT EXISTS idx_CapitoliScaricati_IdLibro ON CapitoliScaricati(IdLibro);" &
                                      "CREATE TABLE IF NOT EXISTS UltimeAggiunte (IdLibro TEXT PRIMARY KEY, Titolo TEXT, Autore TEXT, Lingua TEXT, Zip TEXT, DataInserimento DATETIME DEFAULT CURRENT_TIMESTAMP);" &
                                      "CREATE INDEX IF NOT EXISTS idx_UltimeAggiunte_Lingua ON UltimeAggiunte(Lingua);"
                    cmd.ExecuteNonQuery()
                End Using

                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "Libri")
                    WGDatabase.AssicuraColonnaChiave(conn, "LibriScaricati")
                    WGDatabase.AssicuraColonnaChiave(conn, "CapitoliScaricati")
                    WGDatabase.AssicuraColonnaChiave(conn, "UltimeAggiunte")
                Catch exChiave As Exception
                    _log.ScriviLog(exChiave.ToString())
                End Try
            End Using

            Dim offset As Integer = 0
            Dim limit As Integer = 50
            Dim client As New HttpClient()
            client.Timeout = TimeSpan.FromSeconds(60)

            Dim hasMore As Boolean = True
            Dim totalEstimated As Integer = 20000
            Dim processed As Integer = 0

            While hasMore
                If lavoro.Annulla Then
                    Exit While
                End If

                Dim url As String = $"{URL_BASE_API_SEARCH}&limit={limit}&offset={offset}"
                Dim responseStr As String = ""

                Try
                    responseStr = client.GetStringAsync(url).Result
                Catch ex As Exception
                    If ex.Message.Contains("404") OrElse (ex.InnerException IsNot Nothing AndAlso ex.InnerException.Message.Contains("404")) Then
                        hasMore = False
                        Exit While
                    End If
                    _log.ScriviLog(ex.ToString())
                    hasMore = False
                    Exit While
                End Try

                Dim apiResponse As LvbResponse = JsonConvert.DeserializeObject(Of LvbResponse)(responseStr)

                If apiResponse Is Nothing OrElse apiResponse.books Is Nothing OrElse apiResponse.books.Count = 0 Then
                    hasMore = False
                    Exit While
                End If

                Using conn As New SQLiteConnection(ConnectionString)
                    conn.Open()
                    Using cmdPragma As New SQLiteCommand(conn)
                        cmdPragma.CommandText = "PRAGMA synchronous = OFF;PRAGMA journal_mode = MEMORY;"
                        cmdPragma.ExecuteNonQuery()
                    End Using

                    Using trans As SQLiteTransaction = conn.BeginTransaction()
                        Using cmd As New SQLiteCommand("INSERT OR REPLACE INTO Libri (id, titolo, autore, lingua, zip, generi, ChiaveWG) VALUES (@id, @titolo, @autore, @lingua, @zip, @generi, @chiaveWG)", conn)
                            Using cmdUltimi As New SQLiteCommand("INSERT OR REPLACE INTO UltimeAggiunte (IdLibro, Titolo, Autore, Lingua, Zip, ChiaveWG) VALUES (@id, @titolo, @autore, @lingua, @zip, @chiaveWG)", conn)
                                Using cmdLookupLibro As New SQLiteCommand("SELECT ChiaveWG FROM Libri WHERE id = @id", conn, trans)
                                    Using cmdLookupUltimi As New SQLiteCommand("SELECT ChiaveWG FROM UltimeAggiunte WHERE IdLibro = @id", conn, trans)
                                        Dim pId As SQLiteParameter = cmd.Parameters.Add("@id", DbType.String)
                                        Dim pTitolo As SQLiteParameter = cmd.Parameters.Add("@titolo", DbType.String)
                                        Dim pAutore As SQLiteParameter = cmd.Parameters.Add("@autore", DbType.String)
                                        Dim pLingua As SQLiteParameter = cmd.Parameters.Add("@lingua", DbType.String)
                                        Dim pZip As SQLiteParameter = cmd.Parameters.Add("@zip", DbType.String)
                                        Dim pGeneri As SQLiteParameter = cmd.Parameters.Add("@generi", DbType.String)
                                        Dim pChiaveWG As SQLiteParameter = cmd.Parameters.Add("@chiaveWG", DbType.String)

                                        Dim pIdUlt As SQLiteParameter = cmdUltimi.Parameters.Add("@id", DbType.String)
                                        Dim pTitoloUlt As SQLiteParameter = cmdUltimi.Parameters.Add("@titolo", DbType.String)
                                        Dim pAutoreUlt As SQLiteParameter = cmdUltimi.Parameters.Add("@autore", DbType.String)
                                        Dim pLinguaUlt As SQLiteParameter = cmdUltimi.Parameters.Add("@lingua", DbType.String)
                                        Dim pZipUlt As SQLiteParameter = cmdUltimi.Parameters.Add("@zip", DbType.String)
                                        Dim pChiaveWGUlt As SQLiteParameter = cmdUltimi.Parameters.Add("@chiaveWG", DbType.String)

                                        Dim pIdLookupLibro As SQLiteParameter = cmdLookupLibro.Parameters.Add("@id", DbType.String)
                                        Dim pIdLookupUltimi As SQLiteParameter = cmdLookupUltimi.Parameters.Add("@id", DbType.String)

                                        For Each b As LvbBook In apiResponse.books
                                            pId.Value = b.id
                                            pTitolo.Value = b.title

                                            Dim authors As String = ""
                                            If b.authors IsNot Nothing Then
                                                authors = String.Join(", ", b.authors.Select(Function(a) $"{a.first_name} {a.last_name}"))
                                            End If
                                            pAutore.Value = authors

                                            pLingua.Value = b.language
                                            pZip.Value = b.url_zip_file

                                            Dim genres As String = ""
                                            If b.genres IsNot Nothing Then
                                                genres = String.Join(", ", b.genres.Select(Function(g) g.name))
                                            End If
                                            pGeneri.Value = genres

                                            Dim chiaveWGLibro As String = ""
                                            Try
                                                pIdLookupLibro.Value = b.id
                                                Dim risultatoLookup As Object = cmdLookupLibro.ExecuteScalar()
                                                If risultatoLookup IsNot Nothing AndAlso Not IsDBNull(risultatoLookup) Then
                                                    chiaveWGLibro = risultatoLookup.ToString()
                                                End If
                                            Catch exLookup As Exception
                                                _log.ScriviLog(exLookup.ToString())
                                            End Try
                                            If String.IsNullOrWhiteSpace(chiaveWGLibro) Then chiaveWGLibro = WGDatabase.GeneraChiave()
                                            pChiaveWG.Value = chiaveWGLibro

                                            cmd.ExecuteNonQuery()

                                            Dim chiaveWGUltimi As String = ""
                                            Try
                                                pIdLookupUltimi.Value = b.id
                                                Dim risultatoLookupUlt As Object = cmdLookupUltimi.ExecuteScalar()
                                                If risultatoLookupUlt IsNot Nothing AndAlso Not IsDBNull(risultatoLookupUlt) Then
                                                    chiaveWGUltimi = risultatoLookupUlt.ToString()
                                                End If
                                            Catch exLookup As Exception
                                                _log.ScriviLog(exLookup.ToString())
                                            End Try
                                            If String.IsNullOrWhiteSpace(chiaveWGUltimi) Then chiaveWGUltimi = WGDatabase.GeneraChiave()

                                            pIdUlt.Value = b.id
                                            pTitoloUlt.Value = b.title
                                            pAutoreUlt.Value = authors
                                            pLinguaUlt.Value = b.language
                                            pZipUlt.Value = b.url_zip_file
                                            pChiaveWGUlt.Value = chiaveWGUltimi
                                            cmdUltimi.ExecuteNonQuery()
                                        Next
                                    End Using
                                End Using
                            End Using
                        End Using
                        trans.Commit()
                    End Using
                End Using

                processed += apiResponse.books.Count
                offset += limit

                Dim perc As Integer = Math.Min(100, CInt((processed / totalEstimated) * 100))
                lavoro.Progresso = perc
                lavoro.Info = $"Recuperati {processed} libri..."
            End While

            If lavoro.Annulla Then
                NotificaFineOperazione("Aggiornamento catalogo annullato.")
            Else
                lavoro.Progresso = 100
                NotificaFineOperazione($"Aggiornamento completato.{processed} libri salvati.")
            End If

        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore aggiornamento catalogo.")
        End Try
    End Sub

    Private Function NomeCartellaSicuroLibriVox(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return "senza_titolo"
            Dim risultato As String = nome
            For Each carattereNonValido As Char In Path.GetInvalidFileNameChars()
                risultato = risultato.Replace(carattereNonValido, "_"c)
            Next
            If risultato.Length > 120 Then risultato = risultato.Substring(0, 120)
            risultato = risultato.Trim()
            If String.IsNullOrWhiteSpace(risultato) Then Return "senza_titolo"
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "senza_titolo"
        End Try
    End Function

    Private Sub ProcessaDownloadLibri(lavoro As LavoroLibriVox)
        Try
            lavoro.LibriTotali = lavoro.CodaDownload.Count
            lavoro.LibriCompletati = 0
            lavoro.ProgressoEstrazione.Valore = 0
            lavoro.StatoDownload.Azzera("")
            lavoro.Progresso = 0

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromMinutes(30)

                For Each idLibro As String In lavoro.CodaDownload
                    If lavoro.Annulla Then Exit For

                    Dim urlZip As String = ""
                    Dim titolo As String = ""
                    Dim autore As String = ""

                    Using conn As New SQLiteConnection(ConnectionString)
                        conn.Open()
                        Using cmd As New SQLiteCommand("SELECT zip, titolo, autore FROM Libri WHERE id = @id", conn)
                            cmd.Parameters.AddWithValue("@id", idLibro)
                            Using r As SQLiteDataReader = cmd.ExecuteReader()
                                If r.Read() Then
                                    urlZip = r("zip").ToString()
                                    titolo = r("titolo").ToString()
                                    autore = r("autore").ToString()
                                End If
                            End Using
                        End Using
                    End Using

                    If String.IsNullOrEmpty(urlZip) Then
                        lavoro.LibriCompletati += 1
                        lavoro.Progresso = PercentualeLibriVox(lavoro)
                        Continue For
                    End If

                    lavoro.ProgressoEstrazione.Valore = 0
                    lavoro.StatoDownload.Azzera(titolo)
                    lavoro.Info = $"Download di {titolo}, libro {lavoro.LibriCompletati + 1} di {lavoro.LibriTotali}"

                    Dim tempFile As String = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip")
                    Dim cartellaFinale As String = Path.Combine(lavoro.CartellaDownload, NomeCartellaSicuroLibriVox(titolo))

                    Try
                        Dim scaricato As Boolean = ScaricaFileHttpConProgresso(client, urlZip, tempFile, lavoro.StatoDownload, lavoro)

                        If Not scaricato Then
                            EliminaFileSeEsiste(tempFile)
                            lavoro.LibriCompletati += 1
                            lavoro.Progresso = PercentualeLibriVox(lavoro)
                            Continue For
                        End If

                        If lavoro.Annulla Then
                            EliminaFileSeEsiste(tempFile)
                            Exit For
                        End If

                        If Directory.Exists(cartellaFinale) Then
                            Directory.Delete(cartellaFinale, True)
                        End If
                        Directory.CreateDirectory(cartellaFinale)

                        lavoro.Info = $"Estrazione di {titolo}, libro {lavoro.LibriCompletati + 1} di {lavoro.LibriTotali}"
                        EstraiArchivioConProgresso(tempFile, cartellaFinale, lavoro.ProgressoEstrazione, lavoro)

                        EliminaFileSeEsiste(tempFile)

                        If Not lavoro.Annulla Then
                            Using conn As New SQLiteConnection(ConnectionString)
                                conn.Open()
                                Using cmdPragma As New SQLiteCommand(conn)
                                    cmdPragma.CommandText = "PRAGMA synchronous = OFF;PRAGMA journal_mode = MEMORY;"
                                    cmdPragma.ExecuteNonQuery()
                                End Using

                                Using trans As SQLiteTransaction = conn.BeginTransaction()
                                    Dim chiaveWGLibroScaricato As String = ""
                                    Try
                                        Using cmdLookupLibro As New SQLiteCommand("SELECT ChiaveWG FROM LibriScaricati WHERE IdLibro = @id", conn, trans)
                                            cmdLookupLibro.Parameters.AddWithValue("@id", idLibro)
                                            Dim risultatoLookup As Object = cmdLookupLibro.ExecuteScalar()
                                            If risultatoLookup IsNot Nothing AndAlso Not IsDBNull(risultatoLookup) Then
                                                chiaveWGLibroScaricato = risultatoLookup.ToString()
                                            End If
                                        End Using
                                    Catch exLookup As Exception
                                        _log.ScriviLog(exLookup.ToString())
                                    End Try
                                    If String.IsNullOrWhiteSpace(chiaveWGLibroScaricato) Then chiaveWGLibroScaricato = WGDatabase.GeneraChiave()

                                    Using cmdLibro As New SQLiteCommand("INSERT OR REPLACE INTO LibriScaricati (IdLibro, Titolo, Autore, PercorsoLocale, ChiaveWG) VALUES (@id, @titolo, @autore, @path, @chiaveWG)", conn)
                                        cmdLibro.Parameters.AddWithValue("@id", idLibro)
                                        cmdLibro.Parameters.AddWithValue("@titolo", titolo)
                                        cmdLibro.Parameters.AddWithValue("@autore", autore)
                                        cmdLibro.Parameters.AddWithValue("@path", cartellaFinale)
                                        cmdLibro.Parameters.AddWithValue("@chiaveWG", chiaveWGLibroScaricato)
                                        cmdLibro.ExecuteNonQuery()
                                    End Using

                                    Dim chiaviWGEsistentiCapitoli As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                                    Try
                                        Using cmdLookupCaps As New SQLiteCommand("SELECT TitoloCapitolo, ChiaveWG FROM CapitoliScaricati WHERE IdLibro = @id", conn, trans)
                                            cmdLookupCaps.Parameters.AddWithValue("@id", idLibro)
                                            Using r As SQLiteDataReader = cmdLookupCaps.ExecuteReader()
                                                While r.Read()
                                                    Dim titoloCap As String = If(IsDBNull(r("TitoloCapitolo")), "", r("TitoloCapitolo").ToString())
                                                    Dim chiaveCap As String = If(IsDBNull(r("ChiaveWG")), "", r("ChiaveWG").ToString())
                                                    If Not String.IsNullOrWhiteSpace(titoloCap) AndAlso Not String.IsNullOrWhiteSpace(chiaveCap) AndAlso Not chiaviWGEsistentiCapitoli.ContainsKey(titoloCap) Then
                                                        chiaviWGEsistentiCapitoli.Add(titoloCap, chiaveCap)
                                                    End If
                                                End While
                                            End Using
                                        End Using
                                    Catch exLookup As Exception
                                        _log.ScriviLog(exLookup.ToString())
                                    End Try

                                    Using cmdDelCaps As New SQLiteCommand("DELETE FROM CapitoliScaricati WHERE IdLibro = @id", conn)
                                        cmdDelCaps.Parameters.AddWithValue("@id", idLibro)
                                        cmdDelCaps.ExecuteNonQuery()
                                    End Using

                                    Dim mp3Files As String() = Directory.GetFiles(cartellaFinale, "*.mp3")
                                    Array.Sort(mp3Files)
                                    Using cmdCaps As New SQLiteCommand("INSERT INTO CapitoliScaricati (IdLibro, TitoloCapitolo, PercorsoFile, ChiaveWG) VALUES (@id, @tit, @file, @chiaveWG)", conn)
                                        Dim pId As SQLiteParameter = cmdCaps.Parameters.Add("@id", DbType.String)
                                        Dim pTit As SQLiteParameter = cmdCaps.Parameters.Add("@tit", DbType.String)
                                        Dim pFile As SQLiteParameter = cmdCaps.Parameters.Add("@file", DbType.String)
                                        Dim pChiaveWGCap As SQLiteParameter = cmdCaps.Parameters.Add("@chiaveWG", DbType.String)

                                        pId.Value = idLibro
                                        For Each f As String In mp3Files
                                            Dim titoloCapitoloCorrente As String = Path.GetFileNameWithoutExtension(f)
                                            pTit.Value = titoloCapitoloCorrente
                                            pFile.Value = f
                                            Dim chiaveWGCapitolo As String = ""
                                            If chiaviWGEsistentiCapitoli.ContainsKey(titoloCapitoloCorrente) Then
                                                chiaveWGCapitolo = chiaviWGEsistentiCapitoli(titoloCapitoloCorrente)
                                            End If
                                            If String.IsNullOrWhiteSpace(chiaveWGCapitolo) Then chiaveWGCapitolo = WGDatabase.GeneraChiave()
                                            pChiaveWGCap.Value = chiaveWGCapitolo
                                            cmdCaps.ExecuteNonQuery()
                                        Next
                                    End Using
                                    trans.Commit()
                                End Using
                            End Using
                        End If

                    Catch ex As Exception
                        _log.ScriviLog($"Errore download o estrazione libro {idLibro}: {ex.Message}")
                        EliminaFileSeEsiste(tempFile)
                    End Try

                    lavoro.LibriCompletati += 1
                    lavoro.StatoDownload.Azzera("")
                    lavoro.ProgressoEstrazione.Valore = 0
                    lavoro.Progresso = PercentualeLibriVox(lavoro)
                Next
            End Using

            If lavoro.Annulla Then
                NotificaFineOperazione("Download LibriVox annullato.")
            Else
                lavoro.Progresso = 100
                NotificaFineOperazione($"Download LibriVox completato. {lavoro.LibriCompletati} libri elaborati su {lavoro.LibriTotali}.")
            End If

        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore download libri.")
        End Try
    End Sub

    Private Sub ProcessaEliminazioneLibri(lavoro As LavoroLibriVox)
        Try
            Dim totale As Integer = lavoro.CodaEliminazione.Count
            Dim processati As Integer = 0

            Using conn As New SQLiteConnection(ConnectionString)
                conn.Open()
                Using cmdPragma As New SQLiteCommand(conn)
                    cmdPragma.CommandText = "PRAGMA synchronous = OFF;PRAGMA journal_mode = MEMORY;"
                    cmdPragma.ExecuteNonQuery()
                End Using

                For Each idLibro As String In lavoro.CodaEliminazione
                    If lavoro.Annulla Then
                        Exit For
                    End If

                    Dim percorso As String = ""
                    Using cmdSel As New SQLiteCommand("SELECT PercorsoLocale FROM LibriScaricati WHERE IdLibro = @id", conn)
                        cmdSel.Parameters.AddWithValue("@id", idLibro)
                        Using r As SQLiteDataReader = cmdSel.ExecuteReader()
                            If r.Read() Then
                                percorso = r("PercorsoLocale").ToString()
                            End If
                        End Using
                    End Using

                    lavoro.Info = $"Eliminazione ID: {idLibro}"

                    Try
                        If Not String.IsNullOrEmpty(percorso) AndAlso Directory.Exists(percorso) Then
                            Directory.Delete(percorso, True)
                        End If
                    Catch ex As Exception
                        _log.ScriviLog($"Errore eliminazione cartella {percorso}: {ex.Message}")
                    End Try

                    Using trans As SQLiteTransaction = conn.BeginTransaction()
                        Using cmdDelCap As New SQLiteCommand("DELETE FROM CapitoliScaricati WHERE IdLibro = @id", conn)
                            cmdDelCap.Parameters.AddWithValue("@id", idLibro)
                            cmdDelCap.ExecuteNonQuery()
                        End Using
                        Using cmdDelLib As New SQLiteCommand("DELETE FROM LibriScaricati WHERE IdLibro = @id", conn)
                            cmdDelLib.Parameters.AddWithValue("@id", idLibro)
                            cmdDelLib.ExecuteNonQuery()
                        End Using
                        trans.Commit()
                    End Using

                    processati += 1
                    lavoro.Progresso = CInt((processati / totale) * 100)
                Next
            End Using

            If lavoro.Annulla Then
                NotificaFineOperazione("Eliminazione annullata.")
            Else
                NotificaFineOperazione("Eliminazione libri completata.")
            End If

        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore durante l'eliminazione.")
        End Try
    End Sub

    Private Sub AggiungiTask(lavoro As LavoroFile, tipo As TipoOperazioneFile, src As String, dest As String, size As Long, Optional wpdDevId As String = "", Optional wpdPath As String = "")
        Try
            If lavoro Is Nothing Then Return
            SyncLock lavoro.Blocco
                Dim t As New TaskFile With {
                    .Tipo = tipo, .Sorgente = src, .Destinazione = dest, .Dimensione = size,
                    .WpdDeviceId = wpdDevId, .WpdPath = wpdPath
                }
                lavoro.Coda.Add(t)
                lavoro.TotaleBytes += size
            End SyncLock

            AggiungiRisorsa(lavoro, src)
            AggiungiRisorsa(lavoro, dest)
            If Not String.IsNullOrEmpty(wpdPath) Then
                AggiungiRisorsa(lavoro, WPD_PREFIX & wpdDevId & "|" & wpdPath)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function GetDevice(deviceId As String) As MediaDevice
        Try
            Dim devices As IEnumerable(Of MediaDevice) = MediaDevice.GetDevices()
            Return devices.FirstOrDefault(Function(d) d.DeviceId = deviceId OrElse d.FriendlyName = deviceId)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function IsWpdDirectory(deviceId As String, path As String) As Boolean
        Dim device As MediaDevice = GetDevice(deviceId)
        If device IsNot Nothing Then
            Try
                device.Connect()
                Dim isDir As Boolean = device.DirectoryExists(path)
                device.Disconnect()
                Return isDir
            Catch ex As Exception
                _log.ScriviLog(ex.ToString())
            Finally
                If device.IsConnected Then
                    device.Disconnect()
                End If
            End Try
        End If
        Return False
    End Function

    Private Function OttieniDimensioneWPD(deviceId As String, path As String) As Long
        Dim device As MediaDevice = GetDevice(deviceId)
        If device IsNot Nothing Then
            Try
                device.Connect()
                Dim info As MediaFileInfo = device.GetFileInfo(path)
                Dim len As Long = info.Length
                device.Disconnect()
                Return len
            Catch ex As Exception
                _log.ScriviLog(ex.ToString())
            Finally
                If device.IsConnected Then
                    device.Disconnect()
                End If
            End Try
        End If
        Return 0
    End Function

    Private Sub EsploraEAccodaWPD(lavoro As LavoroFile, deviceId As String, sourcePathWpd As String, currentDestPathPc As String, tipoOp As TipoOperazioneFile)
        Dim device As MediaDevice = GetDevice(deviceId)
        If device Is Nothing Then
            Return
        End If

        Try
            device.Connect()

            Dim files As String() = device.GetFiles(sourcePathWpd)
            For Each f As String In files
                Dim wpdSrc As String = WPD_PREFIX & deviceId & "|" & f

                Dim size As Long = 0
                Try
                    Dim info As MediaFileInfo = device.GetFileInfo(f)
                    size = info.Length
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try

                AggiungiTask(lavoro, tipoOp, wpdSrc, currentDestPathPc, size)
            Next

            Dim dirs As String() = device.GetDirectories(sourcePathWpd)
            For Each d As String In dirs
                Dim dirName As String = System.IO.Path.GetFileName(d)
                Dim nextDestPc As String = System.IO.Path.Combine(currentDestPathPc, dirName)

                If Not System.IO.Directory.Exists(nextDestPc) Then
                    System.IO.Directory.CreateDirectory(nextDestPc)
                End If

                device.Disconnect()
                EsploraEAccodaWPD(lavoro, deviceId, d, nextDestPc, tipoOp)
                device.Connect()

                If tipoOp = TipoOperazioneFile.SpostaPortatile Then
                    AggiungiTask(lavoro, TipoOperazioneFile.EliminaPortatile, "", "", 0, deviceId, d)
                End If
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            If device.IsConnected Then
                device.Disconnect()
            End If
        End Try
    End Sub

    Private Sub AvviaOperazioniFile(lavoro As LavoroFile)
        Try
            If lavoro Is Nothing Then Return
            SyncLock lavoro.Blocco
                If lavoro.ThreadLavoro IsNot Nothing OrElse lavoro.Coda.Count = 0 Then Return
                lavoro.InPausa = False
                lavoro.Annulla = False
                lavoro.TempoInizio = DateTime.Now
                lavoro.DurataTotalePausa = TimeSpan.Zero
                lavoro.Contesto = New ContestoSovrascrittura()
                lavoro.Descrizione = DeterminaNomeOperazione(lavoro)
            End SyncLock

            lavoro.ThreadLavoro = New Thread(AddressOf EseguiOperazioniFileLoop)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AvviaOperazioniFile()
        Try
            For Each lavoro As LavoroFile In LavoriFileAttivi()
                If Not lavoro.InEsecuzione AndAlso Not lavoro.InPreparazione Then
                    AvviaOperazioniFile(lavoro)
                End If
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function DeterminaNomeOperazione(lavoro As LavoroFile) As String
        Try
            If lavoro Is Nothing OrElse lavoro.Coda.Count = 0 Then
                Return "Operazione file"
            End If
            Dim task As TaskFile = lavoro.Coda(0)
            Select Case task.Tipo
                Case TipoOperazioneFile.Copia
                    Return "Copia file in corso"
                Case TipoOperazioneFile.CopiaPortatile
                    If task.Sorgente.StartsWith(WPD_PREFIX) Then
                        Return "Copia da dispositivo portatile"
                    Else
                        Return "Copia su dispositivo portatile"
                    End If
                Case TipoOperazioneFile.Sposta
                    Return "Spostamento file in corso"
                Case TipoOperazioneFile.SpostaPortatile
                    If task.Sorgente.StartsWith(WPD_PREFIX) Then
                        Return "Spostamento da dispositivo portatile"
                    Else
                        Return "Spostamento su dispositivo portatile"
                    End If
                Case TipoOperazioneFile.Elimina
                    Return "Eliminazione file in corso"
                Case TipoOperazioneFile.EliminaPortatile
                    Return "Eliminazione file da dispositivo portatile"
                Case Else
                    Return "Operazione file"
            End Select
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "Operazione file"
        End Try
    End Function

    Private Function VerificaAccessibilitaPercorso(path As String, deviceCorrente As MediaDevice) As Boolean
        Try
            If path.StartsWith(WPD_PREFIX) Then
                Return deviceCorrente IsNot Nothing AndAlso deviceCorrente.IsConnected
            ElseIf Not String.IsNullOrEmpty(path) Then
                Dim root As String = System.IO.Path.GetPathRoot(path)
                Return System.IO.Directory.Exists(root)
            End If
            Return True
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub EseguiOperazioniFileLoop(parametro As Object)
        Dim lavoro As LavoroFile = TryCast(parametro, LavoroFile)
        Dim currentDevice As MediaDevice = Nothing
        Dim currentDeviceId As String = ""

        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return
            lavoro.TempoInizio = DateTime.Now

            Dim i As Integer = 0
            While i < lavoro.Coda.Count
                If lavoro.Annulla Then
                    Exit While
                End If

                While lavoro.InPausa
                    Thread.Sleep(500)
                    If lavoro.Annulla Then
                        Exit While
                    End If
                End While

                Dim task As TaskFile
                SyncLock lavoro.Blocco
                    If i < lavoro.Coda.Count Then
                        task = lavoro.Coda(i)
                    Else
                        Exit While
                    End If
                End SyncLock

                Dim needDevice As Boolean = (task.Sorgente.StartsWith(WPD_PREFIX) OrElse Not String.IsNullOrEmpty(task.WpdDeviceId))
                Dim taskDevId As String = ""

                If task.Sorgente.StartsWith(WPD_PREFIX) Then
                    taskDevId = task.Sorgente.Split("|"c)(1)
                ElseIf Not String.IsNullOrEmpty(task.WpdDeviceId) Then
                    taskDevId = task.WpdDeviceId
                End If

                If needDevice Then
                    If currentDevice Is Nothing OrElse currentDeviceId <> taskDevId Then
                        If currentDevice IsNot Nothing Then
                            currentDevice.Disconnect()
                            currentDevice.Dispose()
                            currentDevice = Nothing
                        End If
                        currentDevice = GetDevice(taskDevId)
                        If currentDevice IsNot Nothing Then
                            currentDevice.Connect()
                            currentDeviceId = taskDevId
                        End If
                    End If
                Else
                    If currentDevice IsNot Nothing Then
                        currentDevice.Disconnect()
                        currentDevice.Dispose()
                        currentDevice = Nothing
                        currentDeviceId = ""
                    End If
                End If

                If Not VerificaAccessibilitaPercorso(task.Sorgente, currentDevice) OrElse Not VerificaAccessibilitaPercorso(task.Destinazione, currentDevice) Then
                    lavoro.Annulla = True
                    NotificaFineOperazione("Operazione interrotta: il percorso di origine o destinazione non è più disponibile.")
                    Exit While
                End If

                Try
                    Select Case task.Tipo
                        Case TipoOperazioneFile.Copia : EseguiCopia(lavoro, task)
                        Case TipoOperazioneFile.Sposta : EseguiSposta(lavoro, task)
                        Case TipoOperazioneFile.Elimina : EseguiElimina(task)
                        Case TipoOperazioneFile.CopiaPortatile : EseguiCopiaPortatile(lavoro, task, currentDevice)
                        Case TipoOperazioneFile.SpostaPortatile
                            EseguiCopiaPortatile(lavoro, task, currentDevice)
                            If Not lavoro.Annulla Then
                                If task.Sorgente.StartsWith(WPD_PREFIX) Then
                                    Dim parts As String() = task.Sorgente.Split("|"c)
                                    If currentDevice IsNot Nothing AndAlso currentDevice.IsConnected Then
                                        Try
                                            currentDevice.DeleteFile(parts(2))
                                        Catch ex As Exception
                                            _log.ScriviLog(ex.ToString())
                                        End Try
                                    End If
                                Else
                                    If System.IO.File.Exists(task.Sorgente) Then
                                        System.IO.File.Delete(task.Sorgente)
                                    End If
                                End If
                            End If
                        Case TipoOperazioneFile.EliminaPortatile : EseguiEliminaPortatile(task, currentDevice)
                    End Select

                    lavoro.BytesProcessati += task.Dimensione

                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
                i += 1
            End While

            If Not lavoro.Annulla Then
                NotificaFineOperazione($"{lavoro.Descrizione}: operazione completata.")
            ElseIf Not String.IsNullOrEmpty(lavoro.MotivoInterruzione) Then
                NotificaFineOperazione(lavoro.MotivoInterruzione)
            Else
                NotificaFineOperazione($"{lavoro.Descrizione}: operazione annullata o interrotta.")
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            If currentDevice IsNot Nothing Then
                Try
                    currentDevice.Disconnect()
                    currentDevice.Dispose()
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            End If
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    SyncLock lavoro.Blocco
                        lavoro.Coda.Clear()
                    End SyncLock
                    RimuoviLavoroFile(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Public Shared Function EFileOneDriveSoloOnline(percorso As String) As Boolean
        Try
            If String.IsNullOrEmpty(percorso) Then Return False
            If percorso.StartsWith(WPD_PREFIX) Then Return False
            If Not System.IO.File.Exists(percorso) Then Return False
            Dim attrs As UInteger = GetFileAttributesNativo(percorso)
            If attrs = UInteger.MaxValue Then Return False
            If (attrs And FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) <> 0 Then Return True
            If (attrs And FILE_ATTRIBUTE_RECALL_ON_OPEN) <> 0 Then Return True
            If (attrs And FILE_ATTRIBUTE_OFFLINE) <> 0 Then Return True
            Return False
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Restituisce i byte effettivamente presenti su disco (utile per placeholder OneDrive).
    ''' Se GetCompressedFileSize fallisce prova FileInfo.Length.
    ''' </summary>
    Private Function OttieniBytesSuDisco(percorso As String) As Long
        Try
            Dim high As UInteger = 0
            Dim low As UInteger = GetCompressedFileSizeNativo(percorso, high)
            If low <> DIMENSIONE_FILE_NON_VALIDA OrElse Marshal.GetLastWin32Error() = 0 Then
                Dim dimensione As Long = (CLng(high) << 32) Or CLng(low)
                If dimensione >= 0 Then Return dimensione
            End If
        Catch
        End Try
        Try
            Dim info As New System.IO.FileInfo(percorso)
            If info.Exists Then Return Math.Max(0L, info.Length)
        Catch
        End Try
        Return -1
    End Function

    ''' <summary>
    ''' Stima la dimensione logica del file (per percentuale OneDrive), privilegiando dimensioneAttesa.
    ''' </summary>
    Private Function OttieniDimensioneLogicaOneDrive(percorso As String, dimensioneAttesa As Long) As Long
        Try
            If dimensioneAttesa > 0 Then Return dimensioneAttesa
            Dim info As New System.IO.FileInfo(percorso)
            If info.Exists AndAlso info.Length > 0 Then Return info.Length
            Dim suDisco As Long = OttieniBytesSuDisco(percorso)
            If suDisco > 0 Then Return suDisco
        Catch
        End Try
        Return 0
    End Function

    Private Function ScaricaContenutoOneDrive(lavoro As LavoroFile, percorso As String, dimensioneAttesa As Long) As Boolean
        Dim handleProtetto As IntPtr = IntPtr.Zero
        Dim threadDownloadContenuto As Thread = Nothing
        Dim annullamentoRichiesto As Boolean = False
        Try
            lavoro.DownloadOneDriveNomeFile = System.IO.Path.GetFileName(percorso)
            lavoro.DownloadOneDriveBytesTotali = OttieniDimensioneLogicaOneDrive(percorso, dimensioneAttesa)
            Dim bytesIniziali As Long = Math.Max(0, OttieniBytesSuDisco(percorso))
            ' Se già quasi completo non avviare hydrate
            If lavoro.DownloadOneDriveBytesTotali > 0 AndAlso bytesIniziali >= lavoro.DownloadOneDriveBytesTotali Then
                lavoro.DownloadOneDriveBytesScaricati = lavoro.DownloadOneDriveBytesTotali
                Return True
            End If

            lavoro.DownloadOneDriveBytesScaricati = bytesIniziali
            lavoro.DownloadOneDriveVelocitaBps = 0
            lavoro.DownloadOneDriveInCorso = True

            Dim hr As Integer = CfOpenFileWithOplock(percorso, CF_OPEN_FILE_FLAG_NONE, handleProtetto)
            If hr <> HR_S_OK Then
                ' Fallback: tentativo di lettura per forzare il download
                Try
                    Using fs As New System.IO.FileStream(percorso, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)
                        Dim buf(8191) As Byte
                        Dim letti As Integer = fs.Read(buf, 0, buf.Length)
                        While letti > 0 AndAlso Not lavoro.Annulla
                            Dim bytesOra As Long = Math.Max(0, OttieniBytesSuDisco(percorso))
                            lavoro.DownloadOneDriveBytesScaricati = bytesOra
                            If lavoro.DownloadOneDriveBytesTotali > 0 AndAlso bytesOra >= lavoro.DownloadOneDriveBytesTotali Then Exit While
                            If Not EFileOneDriveSoloOnline(percorso) Then Exit While
                            letti = fs.Read(buf, 0, buf.Length)
                        End While
                    End Using
                    lavoro.DownloadOneDriveBytesScaricati = Math.Max(lavoro.DownloadOneDriveBytesScaricati, OttieniBytesSuDisco(percorso))
                    If lavoro.DownloadOneDriveBytesTotali > 0 AndAlso lavoro.DownloadOneDriveBytesScaricati > lavoro.DownloadOneDriveBytesTotali Then
                        lavoro.DownloadOneDriveBytesScaricati = lavoro.DownloadOneDriveBytesTotali
                    End If
                    Return Not lavoro.Annulla
                Catch
                    Return False
                End Try
            End If

            Dim idratatore As New IdratatoreOneDrive(_log)
            idratatore.Handle = handleProtetto
            threadDownloadContenuto = New Thread(AddressOf idratatore.Esegui)
            threadDownloadContenuto.IsBackground = True
            threadDownloadContenuto.Start()

            Dim ultimoOrario As DateTime = DateTime.Now
            Dim ultimiBytes As Long = bytesIniziali
            Dim tentativiSenzaAvanzamento As Integer = 0
            Const maxTentativiSenzaAvanzamento As Integer = 40 ' ~20 secondi a 500ms

            While threadDownloadContenuto.IsAlive
                If lavoro.Annulla Then
                    annullamentoRichiesto = True
                    ' Non usare Thread.Abort (deprecato/pericoloso): si esce e si chiude l'handle
                    Exit While
                End If

                Thread.Sleep(400)

                Dim bytesOra As Long = OttieniBytesSuDisco(percorso)
                If bytesOra < 0 Then
                    ' Fallback a Length se GetCompressedFileSize non risponde
                    Try
                        Dim info As New System.IO.FileInfo(percorso)
                        If info.Exists Then bytesOra = Math.Max(0L, info.Length)
                    Catch
                        bytesOra = ultimiBytes
                    End Try
                End If

                If bytesOra >= 0 Then
                    If bytesOra > ultimiBytes Then
                        Dim adesso As DateTime = DateTime.Now
                        Dim secondiTrascorsi As Double = (adesso - ultimoOrario).TotalSeconds
                        If secondiTrascorsi > 0.15 Then
                            Dim velocitaIstantanea As Double = (bytesOra - ultimiBytes) / secondiTrascorsi
                            If lavoro.DownloadOneDriveVelocitaBps <= 0 Then
                                lavoro.DownloadOneDriveVelocitaBps = velocitaIstantanea
                            Else
                                lavoro.DownloadOneDriveVelocitaBps = lavoro.DownloadOneDriveVelocitaBps * 0.65 + velocitaIstantanea * 0.35
                            End If
                            ultimoOrario = adesso
                            ultimiBytes = bytesOra
                        End If
                        tentativiSenzaAvanzamento = 0
                    Else
                        tentativiSenzaAvanzamento += 1
                    End If

                    lavoro.DownloadOneDriveBytesScaricati = bytesOra

                    ' Percentuale completa: esci anche se il thread hydrate è ancora vivo
                    If lavoro.DownloadOneDriveBytesTotali > 0 AndAlso bytesOra >= lavoro.DownloadOneDriveBytesTotali Then
                        lavoro.DownloadOneDriveBytesScaricati = lavoro.DownloadOneDriveBytesTotali
                        Exit While
                    End If

                    ' Il file non è più solo online → contenuto disponibile
                    If Not EFileOneDriveSoloOnline(percorso) AndAlso bytesOra > bytesIniziali Then
                        If lavoro.DownloadOneDriveBytesTotali <= 0 Then
                            lavoro.DownloadOneDriveBytesTotali = bytesOra
                        End If
                        lavoro.DownloadOneDriveBytesScaricati = Math.Min(bytesOra, If(lavoro.DownloadOneDriveBytesTotali > 0, lavoro.DownloadOneDriveBytesTotali, bytesOra))
                        Exit While
                    End If
                End If

                ' Timeout soft se non c'è avanzamento e non è più online-only
                If tentativiSenzaAvanzamento >= maxTentativiSenzaAvanzamento AndAlso Not EFileOneDriveSoloOnline(percorso) Then
                    Exit While
                End If
            End While

            ' Aggiornamento finale percentuale
            Dim bytesFinali As Long = Math.Max(0, OttieniBytesSuDisco(percorso))
            If bytesFinali > lavoro.DownloadOneDriveBytesScaricati Then
                lavoro.DownloadOneDriveBytesScaricati = bytesFinali
            End If
            If lavoro.DownloadOneDriveBytesTotali > 0 AndAlso lavoro.DownloadOneDriveBytesScaricati > lavoro.DownloadOneDriveBytesTotali Then
                lavoro.DownloadOneDriveBytesScaricati = lavoro.DownloadOneDriveBytesTotali
            End If
            If lavoro.DownloadOneDriveBytesTotali <= 0 AndAlso bytesFinali > 0 Then
                lavoro.DownloadOneDriveBytesTotali = bytesFinali
                lavoro.DownloadOneDriveBytesScaricati = bytesFinali
            End If

            If annullamentoRichiesto OrElse lavoro.Annulla Then
                Return False
            End If
            Return True
        Catch
            Return False
        Finally
            If handleProtetto <> IntPtr.Zero Then
                Try
                    CfCloseHandle(handleProtetto)
                Catch
                End Try
            End If
            If threadDownloadContenuto IsNot Nothing AndAlso threadDownloadContenuto.IsAlive Then
                Try
                    threadDownloadContenuto.Join(1500)
                Catch
                End Try
            End If
            lavoro.DownloadOneDriveInCorso = False
            ' Mantiene l'ultima velocità per la UI fino al prossimo aggiornamento; azzera solo se annullato
            If lavoro.Annulla Then
                lavoro.DownloadOneDriveVelocitaBps = 0
            End If
        End Try
    End Function

    Private Sub ScaricaContenutoOneDriveSeNecessario(lavoro As LavoroFile, percorso As String, dimensioneAttesa As Long)
        Try
            If lavoro Is Nothing OrElse lavoro.Annulla Then Return
            If EFileOneDriveSoloOnline(percorso) Then
                ScaricaContenutoOneDrive(lavoro, percorso, dimensioneAttesa)
            End If
        Catch
            ' Nessun log: fallimento silenzioso, l'operazione file continuerà e gestirà l'errore a valle
        End Try
    End Sub

    Private Function LavoroAnnullato(lavoro As LavoroBase) As Boolean
        Try
            If lavoro Is Nothing Then Return False
            Return lavoro.Annulla
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub EliminaFileSeEsiste(percorso As String)
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return
            If System.IO.File.Exists(percorso) Then System.IO.File.Delete(percorso)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' Scarica un file in percorsoDestinazione & ".parziale" e lo mette a posto solo a scaricamento completo (e, se si conosce l'impronta SHA-256
    ' attesa, verificato). Se la connessione cade il tentativo dopo riprende da dove era arrivato (Range); un file più corto di quello che il
    ' server aveva annunciato non viene mai accettato.
    Private Function ScaricaFileHttpConProgresso(client As HttpClient, url As String, percorsoDestinazione As String, stato As StatoDownloadHttp, lavoro As LavoroBase, Optional improntaSha256Attesa As String = Nothing) As Boolean
        Const numeroTentativiMassimo As Integer = 4
        Dim percorsoParziale As String = percorsoDestinazione & ".parziale"
        Try
            If client Is Nothing OrElse String.IsNullOrWhiteSpace(url) Then Return False

            ' un parziale di una volta prima potrebbe essere di un'altra versione: si riprende solo dentro questo scaricamento
            EliminaFileSeEsiste(percorsoParziale)

            For tentativo As Integer = 1 To numeroTentativiMassimo
                Try
                    If LavoroAnnullato(lavoro) Then Return False

                    Dim gia As Long = 0
                    If System.IO.File.Exists(percorsoParziale) Then gia = New FileInfo(percorsoParziale).Length

                    Using richiesta As New HttpRequestMessage(HttpMethod.Get, url)
                        If gia > 0 Then richiesta.Headers.Range = New System.Net.Http.Headers.RangeHeaderValue(gia, Nothing)

                        Using risposta As HttpResponseMessage = client.SendAsync(richiesta, HttpCompletionOption.ResponseHeadersRead).Result
                            If CInt(risposta.StatusCode) = 416 Then
                                ' il parziale non coincide con il file del server: si ricomincia da capo
                                EliminaFileSeEsiste(percorsoParziale)
                                Thread.Sleep(500)
                                Continue For
                            End If

                            If Not risposta.IsSuccessStatusCode Then
                                Dim codiceStato As Integer = CInt(risposta.StatusCode)
                                If tentativo < numeroTentativiMassimo Then
                                    Thread.Sleep(CalcolaAttesaRetry(codiceStato, tentativo, risposta))
                                    Continue For
                                End If
                                Return False
                            End If

                            Dim riprende As Boolean = (CInt(risposta.StatusCode) = 206 AndAlso gia > 0)
                            If Not riprende Then gia = 0

                            Dim lunghezza As Long = 0
                            If risposta.Content.Headers.ContentLength.HasValue Then
                                lunghezza = risposta.Content.Headers.ContentLength.Value
                            End If
                            Dim totaleAtteso As Long = If(lunghezza > 0, gia + lunghezza, 0)

                            stato.BytesTotali = totaleAtteso
                            stato.BytesScaricati = gia
                            stato.VelocitaBps = 0
                            stato.Inizio = DateTime.Now

                            Dim ultimoOrario As DateTime = DateTime.Now
                            Dim ultimiBytes As Long = gia
                            Dim interrotto As Boolean = False
                            Dim scritti As Long = gia

                            Using flussoRete As Stream = risposta.Content.ReadAsStreamAsync().Result
                                Using flussoFile As New FileStream(percorsoParziale, If(riprende, FileMode.Append, FileMode.Create), FileAccess.Write, FileShare.None, 131072)
                                    Dim buffer(131071) As Byte
                                    Dim letti As Integer = flussoRete.Read(buffer, 0, buffer.Length)
                                    While letti > 0
                                        flussoFile.Write(buffer, 0, letti)
                                        scritti += letti
                                        stato.BytesScaricati += letti

                                        Dim adesso As DateTime = DateTime.Now
                                        Dim secondiTrascorsi As Double = (adesso - ultimoOrario).TotalSeconds
                                        If secondiTrascorsi >= 0.5 Then
                                            Dim velocitaIstantanea As Double = (stato.BytesScaricati - ultimiBytes) / secondiTrascorsi
                                            If stato.VelocitaBps <= 0 Then
                                                stato.VelocitaBps = velocitaIstantanea
                                            Else
                                                stato.VelocitaBps = stato.VelocitaBps * 0.7 + velocitaIstantanea * 0.3
                                            End If
                                            ultimoOrario = adesso
                                            ultimiBytes = stato.BytesScaricati
                                        End If

                                        If LavoroAnnullato(lavoro) Then
                                            interrotto = True
                                            Exit While
                                        End If

                                        letti = flussoRete.Read(buffer, 0, buffer.Length)
                                    End While
                                End Using
                            End Using

                            If interrotto Then
                                EliminaFileSeEsiste(percorsoParziale)
                                stato.VelocitaBps = 0
                                Return False
                            End If

                            ' meno byte di quelli annunciati: la connessione si è chiusa prima del tempo, si riprende dal punto raggiunto
                            If totaleAtteso > 0 AndAlso scritti < totaleAtteso Then
                                Throw New IOException($"scaricamento incompleto ({scritti} byte su {totaleAtteso})")
                            End If

                            If Not String.IsNullOrWhiteSpace(improntaSha256Attesa) Then
                                Dim calcolata As String = CalcolaSha256Minuscolo(percorsoParziale)
                                If Not String.Equals(calcolata, improntaSha256Attesa.Trim(), StringComparison.OrdinalIgnoreCase) Then
                                    EliminaFileSeEsiste(percorsoParziale)
                                    Throw New IOException("l'impronta SHA-256 del file scaricato non corrisponde a quella pubblicata")
                                End If
                            End If

                            If stato.BytesTotali <= 0 Then stato.BytesTotali = stato.BytesScaricati
                            EliminaFileSeEsiste(percorsoDestinazione)
                            System.IO.File.Move(percorsoParziale, percorsoDestinazione)
                            stato.VelocitaBps = 0
                            Return True
                        End Using
                    End Using
                Catch exTentativo As Exception
                    _log.ScriviLog($"ScaricaFileHttpConProgresso - tentativo {tentativo} di {numeroTentativiMassimo} per {url}: {exTentativo.Message}")
                    If tentativo < numeroTentativiMassimo Then
                        Thread.Sleep(1000 * tentativo)
                    End If
                End Try
            Next

            EliminaFileSeEsiste(percorsoParziale)
            Return False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function CalcolaSha256Minuscolo(percorso As String) As String
        Using flusso As New FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.Read, 131072)
            Using sha As System.Security.Cryptography.SHA256 = System.Security.Cryptography.SHA256.Create()
                Return BitConverter.ToString(sha.ComputeHash(flusso)).Replace("-", "").ToLowerInvariant()
            End Using
        End Using
    End Function

    ' Il testo di un piccolo file pubblicato (per esempio l'elenco delle impronte): vuoto se non si riesce a leggerlo.
    Private Function ScaricaTestoHttp(client As HttpClient, url As String) As String
        Try
            Return client.GetStringAsync(url).Result
        Catch ex As Exception
            _log.ScriviLog($"ScaricaTestoHttp {url}: {ex.Message}")
            Return ""
        End Try
    End Function

    ' Dalla riga "impronta  nomefile" (formato sha256sum) di un elenco, l'impronta del file indicato; vuota se non c'è.
    Private Function ImprontaDaElenco(elenco As String, nomeFile As String) As String
        Try
            For Each riga As String In elenco.Split({vbLf}, StringSplitOptions.RemoveEmptyEntries)
                Dim parti As String() = riga.Trim().Split({" "c, ChrW(9), "*"c}, StringSplitOptions.RemoveEmptyEntries)
                If parti.Length >= 2 AndAlso parti(0).Length = 64 AndAlso String.Equals(parti(parti.Length - 1), nomeFile, StringComparison.OrdinalIgnoreCase) Then Return parti(0)
            Next
        Catch
        End Try
        Return ""
    End Function

    Private Function EstraiArchivioConProgresso(percorsoArchivio As String, cartellaDestinazione As String, progresso As ProgressoSemplice, lavoro As LavoroBase) As Boolean
        Try
            If progresso IsNot Nothing Then progresso.Valore = 0

            Dim argomenti As String = $"x ""{percorsoArchivio}"" -o""{cartellaDestinazione}"" -bsp1 -y"
            Dim psi As New ProcessStartInfo(_path7z, argomenti)
            psi.UseShellExecute = False
            psi.RedirectStandardOutput = True
            psi.CreateNoWindow = True
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8

            Using processo As Process = Process.Start(psi)
                Dim lettore As StreamReader = processo.StandardOutput
                Dim buffer(1023) As Char
                Dim accumulatore As New System.Text.StringBuilder()

                While Not processo.HasExited
                    If LavoroAnnullato(lavoro) Then
                        Try
                            processo.Kill()
                        Catch exKill As Exception
                            _log.ScriviLog(exKill.ToString())
                        End Try
                        Return False
                    End If

                    Dim letti As Integer = lettore.Read(buffer, 0, buffer.Length)
                    If letti > 0 Then
                        Dim testo As String = New String(buffer, 0, letti)
                        accumulatore.Append(testo)
                        If testo.Contains("%") AndAlso progresso IsNot Nothing Then
                            Dim righe As String() = accumulatore.ToString().Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                            If righe.Length > 0 Then
                                Dim ultimaRiga As String = righe(righe.Length - 1)
                                Dim parole As String() = ultimaRiga.Split(" "c)
                                For Each parola As String In parole
                                    If parola.Contains("%") Then
                                        Dim valoreTesto As String = parola.Replace("%", "")
                                        Dim valore As Integer = 0
                                        If Integer.TryParse(valoreTesto, valore) Then
                                            If valore >= 0 AndAlso valore <= 100 Then progresso.Valore = valore
                                        End If
                                    End If
                                Next
                            End If
                            If accumulatore.Length > 2000 Then accumulatore.Clear()
                        End If
                    Else
                        Thread.Sleep(50)
                    End If
                End While

                processo.WaitForExit()
                If progresso IsNot Nothing Then progresso.Valore = 100
                Return processo.ExitCode = 0
            End Using
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Function TempoStimatoFineMp3(lavoro As LavoroConversioneMp3) As String
        Try
            If lavoro Is Nothing Then Return "in calcolo"
            Dim percentuale As Integer = lavoro.Progresso
            If percentuale < 0 Then percentuale = 0
            If percentuale > 100 Then percentuale = 100
            If percentuale >= 100 Then Return FormattaTempoDaSecondi(0)
            If percentuale < 3 Then Return "in calcolo"
            Dim trascorsi As Double = (DateTime.Now - lavoro.TempoInizio).TotalSeconds
            If trascorsi < 1 Then Return "in calcolo"
            Dim totaleStimato As Double = trascorsi * 100.0 / percentuale
            Dim residuo As Double = totaleStimato - trascorsi
            If residuo < 0 Then residuo = 0
            Return FormattaTempoDaSecondi(residuo)
        Catch ex As Exception
            _log.ScriviLog("WGCentroDiControllo.TempoStimatoFineMp3: " & ex.Message)
            Return "in calcolo"
        End Try
    End Function

    Private Function FormattaTempoDaSecondi(secondi As Double) As String
        Try
            If secondi < 0 Then Return "Calcolo in corso..."
            If secondi > 359999 Then Return "Sconosciuto"
            Dim intervallo As TimeSpan = TimeSpan.FromSeconds(secondi)
            Return $"{CInt(Math.Floor(intervallo.TotalHours))}h {intervallo.Minutes}m {intervallo.Seconds}s"
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "Sconosciuto"
        End Try
    End Function

    Private Function EtichettaOperazione(descrizione As String, percentuale As Integer) As String
        Try
            If String.IsNullOrWhiteSpace(descrizione) Then Return $"Operazione in corso, {percentuale} per cento"
            Return $"{descrizione}, {percentuale} per cento"
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return descrizione
        End Try
    End Function

    Private Function PercentualeFile(lavoro As LavoroFile) As Integer
        Try
            If lavoro Is Nothing OrElse lavoro.TotaleBytes <= 0 Then Return 0
            Dim elaborati As Long = lavoro.BytesProcessati + lavoro.BytesFileCorrente
            Dim valore As Double = (elaborati / CDbl(lavoro.TotaleBytes)) * 100
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Return CInt(Math.Round(valore, 0))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 0
        End Try
    End Function

    Private Function PercentualeFtp(lavoro As LavoroFtp) As Integer
        Try
            If lavoro Is Nothing OrElse lavoro.TotaleBytes <= 0 Then Return 0
            Dim elaborati As Long = lavoro.BytesProcessati + BytesFileCorrenteTotaleFtp(lavoro)
            Dim valore As Double = (elaborati / CDbl(lavoro.TotaleBytes)) * 100
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Return CInt(Math.Round(valore, 0))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 0
        End Try
    End Function

    Private Function PercentualeLibriVox(lavoro As LavoroLibriVox) As Integer
        Try
            If lavoro Is Nothing Then Return 0
            If lavoro.TipoOperazione <> TipoOperazioneLibriVox.DownloadLibri Then Return lavoro.Progresso
            If lavoro.LibriTotali <= 0 Then Return lavoro.Progresso

            Dim frazioneLibro As Double = ((lavoro.StatoDownload.Percentuale() * 0.85) + (lavoro.ProgressoEstrazione.Valore * 0.15)) / 100.0
            If frazioneLibro < 0 Then frazioneLibro = 0
            If frazioneLibro > 1 Then frazioneLibro = 1

            Dim valore As Double = ((lavoro.LibriCompletati + frazioneLibro) / CDbl(lavoro.LibriTotali)) * 100
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Return CInt(Math.Round(valore, 0))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return lavoro.Progresso
        End Try
    End Function

    Private Function PercentualeTesti(lavoro As LavoroTesti) As Integer
        Try
            If lavoro Is Nothing Then Return 0
            If lavoro.LibriTotali <= 0 Then Return lavoro.Progresso

            Dim frazioneLibro As Double = 0
            If lavoro.CapitoliTotali > 0 Then
                frazioneLibro = lavoro.CapitoliCompletati / CDbl(lavoro.CapitoliTotali)
            End If
            If frazioneLibro < 0 Then frazioneLibro = 0
            If frazioneLibro > 1 Then frazioneLibro = 1

            Dim valore As Double = ((lavoro.LibriCompletati + frazioneLibro) / CDbl(lavoro.LibriTotali)) * 100
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Return CInt(Math.Round(valore, 0))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return lavoro.Progresso
        End Try
    End Function

    Private Function PercentualeArchivioInternet(lavoro As LavoroArchivioInternet) As Integer
        Try
            If lavoro Is Nothing Then Return 0
            If lavoro.LibriTotali <= 0 Then Return lavoro.Progresso

            Dim frazioneLibro As Double = 0
            Dim totaliLibro As Long = lavoro.BytesTotaliLibro
            If totaliLibro > 0 Then
                frazioneLibro = lavoro.BytesScaricati() / CDbl(totaliLibro)
            End If
            If frazioneLibro < 0 Then frazioneLibro = 0
            If frazioneLibro > 1 Then frazioneLibro = 1

            Dim valore As Double = ((lavoro.LibriCompletati + frazioneLibro) / CDbl(lavoro.LibriTotali)) * 100
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Return CInt(Math.Round(valore, 0))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return lavoro.Progresso
        End Try
    End Function

    Private Function PercentualeVideo(lavoro As LavoroVideo) As Integer
        Try
            If lavoro Is Nothing OrElse lavoro.VideoTotali <= 0 Then Return 0
            Dim frazione As Double = lavoro.ProgressoCorrente / 100.0
            If frazione < 0 Then frazione = 0
            If frazione > 1 Then frazione = 1
            Dim valore As Double = ((lavoro.VideoElaborati + frazione) / CDbl(lavoro.VideoTotali)) * 100
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            Return CInt(Math.Round(valore, 0))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 0
        End Try
    End Function

    Private Function PercentualeDipendenze() As Integer
        Try
            Dim valore As Double = _baseProgressoDipendenze + (_pesoProgressoDipendenze * (_statoDownloadDipendenzeHttp.Percentuale() / 100.0))
            If valore < 0 Then valore = 0
            If valore > 100 Then valore = 100
            _progressoDownloadDipendenze = CInt(Math.Round(valore, 0))
            Return _progressoDownloadDipendenze
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return _progressoDownloadDipendenze
        End Try
    End Function

    Private Function FormattaVelocita(bytesPerSec As Double) As String
        Try
            If bytesPerSec <= 0 Then Return "calcolo in corso..."
            If bytesPerSec >= 1048576.0 Then
                Return (bytesPerSec / 1048576.0).ToString("F1") & " MB/s"
            ElseIf bytesPerSec >= 1024.0 Then
                Return (bytesPerSec / 1024.0).ToString("F0") & " KB/s"
            Else
                Return bytesPerSec.ToString("F0") & " B/s"
            End If
        Catch
            Return ""
        End Try
    End Function

    Private Function FormattaDimensioneBytes(bytes As Long) As String
        Try
            If bytes < 0 Then bytes = 0
            If bytes >= 1073741824L Then
                Return (bytes / 1073741824.0).ToString("F2") & " GB"
            ElseIf bytes >= 1048576L Then
                Return (bytes / 1048576.0).ToString("F1") & " MB"
            ElseIf bytes >= 1024L Then
                Return (bytes / 1024.0).ToString("F0") & " KB"
            Else
                Return bytes.ToString() & " B"
            End If
        Catch
            Return "0 B"
        End Try
    End Function

    Private Function FormattaTempoResiduoDownloadOneDrive(lavoro As LavoroFile) As String
        Try
            If lavoro Is Nothing OrElse lavoro.DownloadOneDriveBytesTotali <= 0 Then
                Return "calcolo in corso..."
            End If
            Dim rimanenti As Long = Math.Max(0, lavoro.DownloadOneDriveBytesTotali - lavoro.DownloadOneDriveBytesScaricati)
            If rimanenti <= 0 Then Return "quasi completato"
            Dim velocita As Double = lavoro.DownloadOneDriveVelocitaBps
            If velocita <= 0 Then Return "calcolo in corso..."
            Dim ts As TimeSpan = TimeSpan.FromSeconds(rimanenti / velocita)
            If ts.TotalHours >= 1 Then
                Return $"{CInt(Math.Floor(ts.TotalHours))}h {ts.Minutes}m {ts.Seconds}s"
            ElseIf ts.TotalMinutes >= 1 Then
                Return $"{ts.Minutes}m {ts.Seconds}s"
            Else
                Return $"{ts.Seconds}s"
            End If
        Catch
            Return "sconosciuto"
        End Try
    End Function

    Private Function FormattaTempoResiduoFtp(lavoro As LavoroFtp) As String
        Try
            If lavoro Is Nothing Then Return "sconosciuto"
            Dim velocitaTotale As Double = VelocitaTotaleFtp(lavoro)
            Dim bytesComplessivi As Long = lavoro.BytesProcessati + BytesFileCorrenteTotaleFtp(lavoro)
            If velocitaTotale <= 0 OrElse lavoro.TotaleBytes <= 0 Then
                Return "calcolo in corso..."
            End If
            Dim rimanenti As Long = Math.Max(0, lavoro.TotaleBytes - bytesComplessivi)
            Dim ts As TimeSpan = TimeSpan.FromSeconds(rimanenti / velocitaTotale)
            If ts.TotalHours >= 1 Then
                Return $"{CInt(Math.Floor(ts.TotalHours))}h {ts.Minutes}m {ts.Seconds}s"
            ElseIf ts.TotalMinutes >= 1 Then
                Return $"{ts.Minutes}m {ts.Seconds}s"
            Else
                Return $"{ts.Seconds}s"
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "sconosciuto"
        End Try
    End Function

    Private Sub EseguiCopia(lavoro As LavoroFile, task As TaskFile)
        Try
            Dim dirDest As String = System.IO.Path.GetDirectoryName(task.Destinazione)
            If Not String.IsNullOrEmpty(dirDest) AndAlso Not CreaCartellaLocaleSicura(dirDest) Then
                lavoro.MotivoInterruzione = "Permessi insufficienti per la cartella di destinazione: " & dirDest
                lavoro.Annulla = True
                Return
            End If
            If Not ConsentitaScritturaLocale(task.Destinazione, lavoro.Contesto) Then
                Return
            End If
            ScaricaContenutoOneDriveSeNecessario(lavoro, task.Sorgente, task.Dimensione)
            If lavoro.Annulla Then Return
            CopiaStream(lavoro, task.Sorgente, task.Destinazione)
        Catch ex As ErroreCopiaIrreversibile
            Return
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Try
                If Not CopyFile(task.Sorgente, task.Destinazione, False) Then
                    Throw New Exception("API CopyFile failed")
                End If
            Catch exApi As Exception
                _log.ScriviLog(exApi.ToString())
                CopiaDaRigaDiComando(task.Sorgente, task.Destinazione)
            End Try
        End Try
    End Sub

    Private Sub EseguiSposta(lavoro As LavoroFile, task As TaskFile)
        Try
            Dim dirDest As String = System.IO.Path.GetDirectoryName(task.Destinazione)
            If Not String.IsNullOrEmpty(dirDest) AndAlso Not CreaCartellaLocaleSicura(dirDest) Then
                lavoro.MotivoInterruzione = "Permessi insufficienti per la cartella di destinazione: " & dirDest
                lavoro.Annulla = True
                Return
            End If

            If Not ConsentitaScritturaLocale(task.Destinazione, lavoro.Contesto) Then
                Return
            End If

            ScaricaContenutoOneDriveSeNecessario(lavoro, task.Sorgente, task.Dimensione)
            If lavoro.Annulla Then Return

            Dim stessoVolume As Boolean = False
            Try
                Dim rootSorgente As String = System.IO.Path.GetPathRoot(NormalizzaRisorsa(task.Sorgente))
                Dim rootDest As String = System.IO.Path.GetPathRoot(NormalizzaRisorsa(task.Destinazione))
                stessoVolume = Not String.IsNullOrEmpty(rootSorgente) AndAlso
                               String.Equals(rootSorgente, rootDest, StringComparison.OrdinalIgnoreCase)
            Catch
                stessoVolume = False
            End Try

            If stessoVolume Then
                If System.IO.File.Exists(task.Destinazione) Then
                    System.IO.File.Delete(task.Destinazione)
                End If
                System.IO.File.Move(task.Sorgente, task.Destinazione)
            Else
                CopiaStream(lavoro, task.Sorgente, task.Destinazione)
                If System.IO.File.Exists(task.Sorgente) Then
                    System.IO.File.Delete(task.Sorgente)
                End If
            End If
        Catch ex As ErroreCopiaIrreversibile
            Return
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Try
                If Not MoveFile(task.Sorgente, task.Destinazione) Then
                    Throw New Exception("API MoveFile failed")
                End If
            Catch exApi As Exception
                _log.ScriviLog(exApi.ToString())
                SpostaDaRigaDiComando(task.Sorgente, task.Destinazione)
            End Try
        End Try
    End Sub

    ''' <summary>Manda file o cartella nel cestino senza finestre di Windows (anche di errore); vero se è sparito.</summary>
    Friend Function SpostaNelCestinoSenzaFinestre(percorso As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(percorso) Then Return False
            Dim elenco As String = percorso & ControlChars.NullChar & ControlChars.NullChar
            Dim opzioni As UShort = FOF_SILENT Or FOF_NOCONFIRMATION Or FOF_ALLOWUNDO Or FOF_NOERRORUI
            Dim risultato As Integer
            Dim interrotta As Boolean
            If IntPtr.Size = 8 Then
                Dim operazione As New OperazioneFileShell64()
                operazione.hwnd = IntPtr.Zero
                operazione.wFunc = FO_DELETE
                operazione.pFrom = elenco
                operazione.pTo = Nothing
                operazione.fFlags = opzioni
                risultato = OperazioneFileShell64Esegui(operazione)
                interrotta = operazione.fAnyOperationsAborted <> 0
            Else
                Dim operazione As New OperazioneFileShell32()
                operazione.hwnd = IntPtr.Zero
                operazione.wFunc = FO_DELETE
                operazione.pFrom = elenco
                operazione.pTo = Nothing
                operazione.fFlags = opzioni
                risultato = OperazioneFileShell32Esegui(operazione)
                interrotta = operazione.fAnyOperationsAborted <> 0
            End If
            If risultato <> 0 OrElse interrotta Then
                _log.ScriviLog("SpostaNelCestinoSenzaFinestre: codice " & risultato.ToString() & " per " & percorso)
                Return False
            End If
            Return Not (System.IO.File.Exists(percorso) OrElse System.IO.Directory.Exists(percorso))
        Catch ex As Exception
            _log.ScriviLog("SpostaNelCestinoSenzaFinestre: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Sub EseguiElimina(task As TaskFile)
        Try
            If System.IO.Directory.Exists(task.Sorgente) Then
                If UsaCestino Then
                    If Not SpostaNelCestinoSenzaFinestre(task.Sorgente) Then Throw New IOException("Spostamento nel cestino non riuscito: " & task.Sorgente)
                Else
                    System.IO.Directory.Delete(task.Sorgente, True)
                End If
            ElseIf System.IO.File.Exists(task.Sorgente) Then
                If UsaCestino Then
                    If Not SpostaNelCestinoSenzaFinestre(task.Sorgente) Then Throw New IOException("Spostamento nel cestino non riuscito: " & task.Sorgente)
                Else
                    System.IO.File.Delete(task.Sorgente)
                End If
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            If Not UsaCestino Then
                Try
                    If Not DeleteFile(task.Sorgente) Then
                        Throw New Exception("API DeleteFile failed")
                    End If
                Catch exApi As Exception
                    _log.ScriviLog(exApi.ToString())
                    EliminaDaRigaDiComando(task.Sorgente)
                End Try
            End If
        End Try
    End Sub

    Private Sub EseguiCopiaPortatile(lavoro As LavoroFile, task As TaskFile, device As MediaDevice)
        Try
            If device Is Nothing OrElse Not device.IsConnected Then
                Return
            End If

            If task.Sorgente.StartsWith(WPD_PREFIX) Then
                Dim parts As String() = task.Sorgente.Split("|"c)
                Dim pathRemoto As String = parts(2)
                Dim pathLocale As String = task.Destinazione

                Dim dirLocale As String = System.IO.Path.GetDirectoryName(pathLocale)
                If Not String.IsNullOrEmpty(dirLocale) AndAlso Not CreaCartellaLocaleSicura(dirLocale) Then
                    lavoro.MotivoInterruzione = "Permessi insufficienti per la cartella di destinazione: " & dirLocale
                    lavoro.Annulla = True
                    Return
                End If

                If Not ConsentitaScritturaLocale(pathLocale, lavoro.Contesto) Then
                    Return
                End If

                lavoro.NomeFileCorrente = System.IO.Path.GetFileName(pathRemoto)
                lavoro.BytesFileCorrente = 0
                lavoro.VelocitaBps = 0

                Dim trasferimento As New TrasferimentoDispositivo(_log)
                trasferimento.Dispositivo = device
                trasferimento.PercorsoRemoto = pathRemoto
                trasferimento.PercorsoLocale = pathLocale
                trasferimento.EDownload = True

                Dim threadDownload As New Thread(AddressOf trasferimento.Esegui)
                threadDownload.IsBackground = True
                threadDownload.Start()

                Dim ultimoOrario As DateTime = DateTime.Now
                Dim ultimiBytes As Long = 0
                While threadDownload.IsAlive
                    If lavoro.Annulla Then
                        Try
                            threadDownload.Abort()
                        Catch exAbort As Exception
                            _log.ScriviLog(exAbort.ToString())
                        End Try
                        threadDownload.Join(2000)
                        Exit While
                    End If
                    Thread.Sleep(500)
                    Try
                        If System.IO.File.Exists(pathLocale) Then
                            Dim bytesOra As Long = New System.IO.FileInfo(pathLocale).Length
                            Dim adesso As DateTime = DateTime.Now
                            Dim secondiTrascorsi As Double = (adesso - ultimoOrario).TotalSeconds
                            If secondiTrascorsi > 0 AndAlso bytesOra > ultimiBytes Then
                                Dim velocitaIstantanea As Double = (bytesOra - ultimiBytes) / secondiTrascorsi
                                If lavoro.VelocitaBps <= 0 Then
                                    lavoro.VelocitaBps = velocitaIstantanea
                                Else
                                    lavoro.VelocitaBps = lavoro.VelocitaBps * 0.7 + velocitaIstantanea * 0.3
                                End If
                                ultimoOrario = adesso
                                ultimiBytes = bytesOra
                            End If
                            lavoro.BytesFileCorrente = bytesOra
                        End If
                    Catch exPoll As Exception
                        _log.ScriviLog(exPoll.ToString())
                    End Try
                End While
                lavoro.VelocitaBps = 0
            Else
                Dim pathLocale As String = task.Sorgente
                Dim pathRemoto As String = task.WpdPath

                ScaricaContenutoOneDriveSeNecessario(lavoro, pathLocale, task.Dimensione)
                If lavoro.Annulla Then Return

                Dim dirRemota As String = System.IO.Path.GetDirectoryName(pathRemoto)
                If Not device.DirectoryExists(dirRemota) Then
                    device.CreateDirectory(dirRemota)
                End If

                Try
                    If device.FileExists(pathRemoto) Then
                        If Not ChiediSovrascrittura(System.IO.Path.GetFileName(pathRemoto), lavoro.Contesto) Then
                            Return
                        End If
                        device.DeleteFile(pathRemoto)
                    End If
                Catch exVerifica As Exception
                    _log.ScriviLog(exVerifica.ToString())
                End Try

                lavoro.NomeFileCorrente = System.IO.Path.GetFileName(pathLocale)
                lavoro.BytesFileCorrente = 0
                lavoro.VelocitaBps = 0

                Dim trasferimento As New TrasferimentoDispositivo(_log)
                trasferimento.Dispositivo = device
                trasferimento.PercorsoRemoto = pathRemoto
                trasferimento.PercorsoLocale = pathLocale
                trasferimento.EDownload = False

                Dim threadUpload As New Thread(AddressOf trasferimento.Esegui)
                threadUpload.IsBackground = True
                threadUpload.Start()

                Dim ultimoOrario As DateTime = DateTime.Now
                Dim ultimiBytes As Long = 0
                Dim contatorePoll As Integer = 0
                While threadUpload.IsAlive
                    If lavoro.Annulla Then
                        Try
                            threadUpload.Abort()
                        Catch exAbort As Exception
                            _log.ScriviLog(exAbort.ToString())
                        End Try
                        threadUpload.Join(2000)
                        Exit While
                    End If
                    Thread.Sleep(500)
                    contatorePoll += 1
                    If contatorePoll Mod 2 = 0 Then
                        Try
                            If device.FileExists(pathRemoto) Then
                                Dim infoRemoto As MediaFileInfo = device.GetFileInfo(pathRemoto)
                                Dim bytesOra As Long = infoRemoto.Length
                                Dim adesso As DateTime = DateTime.Now
                                Dim secondiTrascorsi As Double = (adesso - ultimoOrario).TotalSeconds
                                If secondiTrascorsi > 0 AndAlso bytesOra > ultimiBytes Then
                                    Dim velocitaIstantanea As Double = (bytesOra - ultimiBytes) / secondiTrascorsi
                                    If lavoro.VelocitaBps <= 0 Then
                                        lavoro.VelocitaBps = velocitaIstantanea
                                    Else
                                        lavoro.VelocitaBps = lavoro.VelocitaBps * 0.7 + velocitaIstantanea * 0.3
                                    End If
                                    ultimoOrario = adesso
                                    ultimiBytes = bytesOra
                                End If
                                lavoro.BytesFileCorrente = bytesOra
                            End If
                        Catch exPoll As Exception
                            _log.ScriviLog(exPoll.ToString())
                        End Try
                    End If
                End While
                lavoro.VelocitaBps = 0
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiEliminaPortatile(task As TaskFile, device As MediaDevice)
        Try
            If device Is Nothing OrElse Not device.IsConnected Then
                Return
            End If

            Dim pathRemoto As String = task.WpdPath
            If device.FileExists(pathRemoto) Then
                device.DeleteFile(pathRemoto)
            ElseIf device.DirectoryExists(pathRemoto) Then
                device.DeleteDirectory(pathRemoto, True)
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LeggiConRetry(fs As System.IO.FileStream, buffer() As Byte, percorso As String) As Integer
        Dim ultimoErrore As Exception = Nothing
        For tentativo As Integer = 1 To TENTATIVI_LETTURA_IO
            Try
                Return fs.Read(buffer, 0, buffer.Length)
            Catch ex As System.IO.IOException
                ultimoErrore = ex
                _log.ScriviLog($"CopiaStream (lettura, tentativo {tentativo}/{TENTATIVI_LETTURA_IO}) - {percorso} - {ex.Message}")
                If tentativo < TENTATIVI_LETTURA_IO Then
                    Thread.Sleep(ATTESA_BASE_RETRY_IO_MS * tentativo)
                End If
            End Try
        Next
        Throw New ErroreCopiaIrreversibile($"Lettura non riuscita dopo {TENTATIVI_LETTURA_IO} tentativi: {percorso}", ultimoErrore)
    End Function

    Private Sub ScriviConRetry(fs As System.IO.FileStream, buffer() As Byte, bytesRead As Integer, percorso As String)
        Dim ultimoErrore As Exception = Nothing
        For tentativo As Integer = 1 To TENTATIVI_LETTURA_IO
            Try
                fs.Write(buffer, 0, bytesRead)
                Return
            Catch ex As System.IO.IOException
                ultimoErrore = ex
                _log.ScriviLog($"CopiaStream (scrittura, tentativo {tentativo}/{TENTATIVI_LETTURA_IO}) - {percorso} - {ex.Message}")
                If tentativo < TENTATIVI_LETTURA_IO Then
                    Thread.Sleep(ATTESA_BASE_RETRY_IO_MS * tentativo)
                End If
            End Try
        Next
        Throw New ErroreCopiaIrreversibile($"Scrittura non riuscita dopo {TENTATIVI_LETTURA_IO} tentativi: {percorso}", ultimoErrore)
    End Sub

    Private Sub CopiaStream(lavoro As LavoroFile, src As String, dest As String)
        Dim fsIn As System.IO.FileStream = Nothing
        Dim fsOut As System.IO.FileStream = Nothing
        Try
            fsIn = New System.IO.FileStream(src, System.IO.FileMode.Open, System.IO.FileAccess.Read)
            fsOut = New System.IO.FileStream(dest, System.IO.FileMode.Create, System.IO.FileAccess.Write)
            Dim buffer(4 * 1024 * 1024 - 1) As Byte
            Dim bytesRead As Integer
            Dim bytesCopiatiFile As Long = 0
            lavoro.NomeFileCorrente = System.IO.Path.GetFileName(src)
            lavoro.BytesFileCorrente = 0
            lavoro.VelocitaBps = 0
            Dim ultimoOrarioVelocita As DateTime = DateTime.Now
            Dim ultimiBytesVelocita As Long = 0
            Do
                While lavoro.InPausa
                    Thread.Sleep(500)
                    If lavoro.Annulla Then
                        Exit Do
                    End If
                End While
                If lavoro.Annulla Then
                    Exit Do
                End If
                bytesRead = LeggiConRetry(fsIn, buffer, src)
                If bytesRead > 0 Then
                    ScriviConRetry(fsOut, buffer, bytesRead, dest)
                    bytesCopiatiFile += bytesRead
                    lavoro.BytesFileCorrente = bytesCopiatiFile
                    Dim adesso As DateTime = DateTime.Now
                    Dim secondiTrascorsi As Double = (adesso - ultimoOrarioVelocita).TotalSeconds
                    If secondiTrascorsi >= 0.3 Then
                        Dim velocitaIstantanea As Double = (bytesCopiatiFile - ultimiBytesVelocita) / secondiTrascorsi
                        If lavoro.VelocitaBps <= 0 Then
                            lavoro.VelocitaBps = velocitaIstantanea
                        Else
                            lavoro.VelocitaBps = lavoro.VelocitaBps * 0.7 + velocitaIstantanea * 0.3
                        End If
                        ultimoOrarioVelocita = adesso
                        ultimiBytesVelocita = bytesCopiatiFile
                    End If
                End If
            Loop While bytesRead > 0
        Catch ex As ErroreCopiaIrreversibile
            _log.ScriviLog($"CopiaStream - errore irreversibile, interrompo il lavoro corrente: {ex.Message}")
            lavoro.MotivoInterruzione = $"Operazione interrotta: impossibile leggere o scrivere {Chr(34)}{System.IO.Path.GetFileName(src)}{Chr(34)} dopo ripetuti tentativi. Il file potrebbe non essere raggiungibile (sincronizzazione cloud, disco locale o dispositivo esterno non disponibile)."
            lavoro.Annulla = True
            Throw
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Throw
        Finally
            If fsIn IsNot Nothing Then
                fsIn.Close()
            End If
            If fsOut IsNot Nothing Then
                fsOut.Close()
            End If
            If lavoro.Annulla AndAlso System.IO.File.Exists(dest) Then
                Try
                    System.IO.File.Delete(dest)
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
            End If
            lavoro.VelocitaBps = 0
        End Try
    End Sub

    Private Sub CopiaDaRigaDiComando(sorgente As String, destinazione As String)
        Try
            EseguiComandoShell(If(System.IO.Directory.Exists(sorgente), $"/C xcopy ""{sorgente}"" ""{destinazione}"" /E /I /Y", $"/C copy /Y ""{sorgente}"" ""{destinazione}"""))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SpostaDaRigaDiComando(sorgente As String, destinazione As String)
        Try
            EseguiComandoShell($"/C move /Y ""{sorgente}"" ""{destinazione}""")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EliminaDaRigaDiComando(sorgente As String)
        Try
            EseguiComandoShell(If(System.IO.Directory.Exists(sorgente), $"/C rmdir /S /Q ""{sorgente}""", $"/C del /F /Q ""{sorgente}"""))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiComandoShell(argomenti As String)
        Try
            Dim p As New ProcessStartInfo("cmd.exe", argomenti) With {.CreateNoWindow = True, .UseShellExecute = False}
            Process.Start(p).WaitForExit()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub RiscriviFileConAi(files As List(Of String), istruzioni As String, provider As WGAi.ProviderAi?, modello As String)
        Try
            If files Is Nothing OrElse files.Count = 0 Then Return
            If String.IsNullOrWhiteSpace(istruzioni) Then Return

            Dim lavoro As New LavoroRiscrittura()
            lavoro.Files = New List(Of String)(files)
            lavoro.Istruzioni = istruzioni.Trim()
            lavoro.Provider = provider
            lavoro.Modello = If(modello, "")
            lavoro.Info = "Inizializzazione..."
            Dim descrizione As String = If(files.Count = 1, "Riscrittura con AI di " & Path.GetFileName(files(0)), "Riscrittura con AI di " & files.Count.ToString() & " file")
            RegistraLavoro(lavoro, descrizione, StatoOperazioneCorrente.RiscritturaAi)
            lavoro.InPreparazione = False

            For Each percorso As String In lavoro.Files
                AggiungiRisorsa(lavoro, percorso)
            Next

            lavoro.ThreadLavoro = New Thread(AddressOf EseguiRiscritturaAi)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiRiscritturaAi(parametro As Object)
        Dim lavoro As LavoroRiscrittura = TryCast(parametro, LavoroRiscrittura)
        Dim modificati As Integer = 0
        Dim problemi As New List(Of String)()
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return

            Dim limiteBlocco As Integer = WGAi.CaratteriPerBloccoRiscrittura(lavoro.Provider)
            Dim elementi As New List(Of ElementoRiscrittura)()
            lavoro.Info = "Lettura dei file..."

            For Each percorso As String In lavoro.Files
                If lavoro.Annulla Then Exit For
                Dim nomeFile As String = Path.GetFileName(percorso)
                Try
                    If Not File.Exists(percorso) Then
                        problemi.Add(nomeFile & ": file non trovato.")
                        Continue For
                    End If
                    If (File.GetAttributes(percorso) And FileAttributes.ReadOnly) = FileAttributes.ReadOnly Then
                        problemi.Add(nomeFile & ": il file è in sola lettura.")
                        Continue For
                    End If
                    Dim elemento As New ElementoRiscrittura()
                    elemento.Percorso = percorso
                    elemento.UltimaModificaUtc = File.GetLastWriteTimeUtc(percorso)
                    Dim testo As String = WGAi.LeggiTestoFile(percorso, elemento.Codifica)
                    If String.IsNullOrWhiteSpace(testo) Then
                        problemi.Add(nomeFile & ": il file è vuoto.")
                        Continue For
                    End If
                    elemento.UsaCrLf = testo.Contains(vbCrLf)
                    elemento.Testo = testo.Replace(vbCrLf, vbLf)
                    elemento.Blocchi = WGAi.DividiPerRiscrittura(elemento.Testo, limiteBlocco)
                    elementi.Add(elemento)
                Catch exLettura As Exception
                    _log.ScriviLog(exLettura.ToString())
                    problemi.Add(nomeFile & ": lettura non riuscita.")
                End Try
            Next

            Dim bloccoTotali As Integer = elementi.Sum(Function(x) x.Blocchi.Count)
            Dim bloccoFatti As Integer = 0
            Dim indiceFile As Integer = 0

            For Each elemento As ElementoRiscrittura In elementi
                If lavoro.Annulla Then Exit For
                indiceFile += 1
                Dim nomeFile As String = Path.GetFileName(elemento.Percorso)
                Dim nuoviBlocchi As New List(Of String)()
                Dim motivoFallimento As String = ""

                For indiceBlocco As Integer = 0 To elemento.Blocchi.Count - 1
                    While lavoro.InPausa AndAlso Not lavoro.Annulla
                        Thread.Sleep(200)
                    End While
                    If lavoro.Annulla Then Exit For

                    lavoro.Info = "File " & indiceFile.ToString() & " di " & elementi.Count.ToString() & ": " & nomeFile & ", parte " & (indiceBlocco + 1).ToString() & " di " & elemento.Blocchi.Count.ToString()
                    Dim blocco As String = elemento.Blocchi(indiceBlocco)

                    If String.IsNullOrWhiteSpace(blocco) Then
                        nuoviBlocchi.Add(blocco)
                    Else
                        Dim attivita As Task(Of Tuple(Of String, String)) = WGAi.RiscriviTestoAsync(blocco, lavoro.Istruzioni, lavoro.Provider, lavoro.Modello)
                        While Not attivita.IsCompleted
                            If lavoro.Annulla Then Exit While
                            Thread.Sleep(150)
                        End While
                        If Not attivita.IsCompleted Then Exit For

                        Dim risposta As Tuple(Of String, String) = attivita.Result
                        If risposta Is Nothing OrElse String.IsNullOrWhiteSpace(risposta.Item1) Then
                            motivoFallimento = If(risposta IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(risposta.Item2), risposta.Item2, "nessuna risposta dall'intelligenza artificiale")
                            Exit For
                        End If

                        Dim inizio As String = blocco.Substring(0, blocco.Length - blocco.TrimStart().Length)
                        Dim fine As String = blocco.Substring(blocco.TrimEnd().Length)
                        nuoviBlocchi.Add(inizio & risposta.Item1.Replace(vbCrLf, vbLf).Trim() & fine)
                    End If

                    bloccoFatti += 1
                    If bloccoTotali > 0 Then lavoro.Progresso = Math.Min(99, CInt(bloccoFatti * 100.0 / bloccoTotali))
                Next

                If lavoro.Annulla Then Exit For

                If motivoFallimento <> "" Then
                    problemi.Add(nomeFile & ": " & motivoFallimento & ".")
                    bloccoFatti += elemento.Blocchi.Count - nuoviBlocchi.Count
                    Continue For
                End If

                Try
                    Dim risultato As String = String.Concat(nuoviBlocchi)
                    If String.IsNullOrWhiteSpace(risultato) Then
                        problemi.Add(nomeFile & ": risultato vuoto.")
                        Continue For
                    End If
                    If File.GetLastWriteTimeUtc(elemento.Percorso) <> elemento.UltimaModificaUtc Then
                        problemi.Add(nomeFile & ": il file è cambiato durante la riscrittura.")
                        Continue For
                    End If
                    If elemento.UsaCrLf Then risultato = risultato.Replace(vbLf, vbCrLf)
                    WGAi.ScriviTestoFile(elemento.Percorso, risultato, elemento.Codifica)
                    modificati += 1
                    lavoro.FileCompletati = modificati
                Catch exScrittura As Exception
                    _log.ScriviLog(exScrittura.ToString())
                    problemi.Add(nomeFile & ": scrittura non riuscita.")
                End Try
            Next

            lavoro.Progresso = 100

            If lavoro.Annulla Then
                NotificaFineOperazione("Riscrittura annullata. File modificati: " & modificati.ToString() & ".")
            ElseIf problemi.Count = 0 Then
                NotificaFineOperazione(If(modificati = 1, "Riscrittura completata: il file è stato modificato.", "Riscrittura completata: modificati " & modificati.ToString() & " file."))
            Else
                NotificaFineOperazione("Riscrittura terminata. File modificati: " & modificati.ToString() & ". Non modificati: " & String.Join(" ", problemi))
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore durante la riscrittura con AI.")
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Private Sub NotificaFineOperazione(testo As String)
        Try
            EseguiSuThreadInterfaccia(Sub() MostraMessaggioFineOperazione(testo))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraMessaggioFineOperazione(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiSuThreadInterfaccia(azione As Action)
        Try
            If azione Is Nothing Then Return

            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed Then
                If contenitore.InvokeRequired Then
                    contenitore.Invoke(New MethodInvoker(Sub() EseguiAzioneProtetta(azione)))
                Else
                    EseguiAzioneProtetta(azione)
                End If
                Return
            End If

            If _uiContext IsNot Nothing Then
                _uiContext.Post(Sub(state) EseguiAzioneProtetta(azione), Nothing)
                Return
            End If

            EseguiAzioneProtetta(azione)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiAzioneProtetta(azione As Action)
        Try
            azione()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub InviaFileBluetooth(files As List(Of String), nomeDispositivo As String)
        Try
            If files Is Nothing OrElse files.Count = 0 Then Return

            Dim lavoro As New LavoroBluetooth()
            lavoro.Files = New List(Of String)(files)
            lavoro.NomeDispositivo = nomeDispositivo
            lavoro.Info = "Inizializzazione..."
            RegistraLavoro(lavoro, "Invio Bluetooth", StatoOperazioneCorrente.Bluetooth)
            lavoro.InPreparazione = False

            For Each percorso As String In lavoro.Files
                AggiungiRisorsa(lavoro, percorso)
            Next
            AggiungiRisorsa(lavoro, "bluetooth:" & nomeDispositivo)

            lavoro.ThreadLavoro = New Thread(AddressOf EseguiInvioBluetooth)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriBluetoothAttivi() As List(Of LavoroBluetooth)
        Try
            Dim risultato As New List(Of LavoroBluetooth)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroBluetooth As LavoroBluetooth = TryCast(lavoro, LavoroBluetooth)
                If lavoroBluetooth IsNot Nothing Then risultato.Add(lavoroBluetooth)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroBluetooth)()
        End Try
    End Function

    Private Sub EseguiInvioBluetooth(parametro As Object)
        Dim lavoro As LavoroBluetooth = TryCast(parametro, LavoroBluetooth)
        Dim processo As Process = Nothing
        Dim scriptPath As String = ""
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return

            scriptPath = Path.Combine(Path.GetTempPath(), $"bt_send_{Guid.NewGuid()}.ps1")
            Dim scriptContent As String =
                "$files = @(" & String.Join(",", lavoro.Files.Select(Function(f) "'" & f.Replace("'", "''") & "'")) & ")" & vbCrLf &
                "$device = '" & lavoro.NomeDispositivo.Replace("'", "''") & "'" & vbCrLf &
                "foreach ($file in $files) {" & vbCrLf &
                "  Write-Host " & "STATO: Invio di $(Split-Path $file -Leaf)" & vbCrLf &
            "  for ($i = 0; $i -le 100; $i += 5) {" & vbCrLf &
                "    Write-Host " & "PROGRESSO:$i" & vbCrLf &
                "  Start-Sleep -Milliseconds 200" & vbCrLf &
                "  }" & vbCrLf &
                "}"
            File.WriteAllText(scriptPath, scriptContent)

            Dim psi As New ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File {Chr(34)}{scriptPath}{Chr(34)}")
            psi.UseShellExecute = False
            psi.RedirectStandardOutput = True
            psi.CreateNoWindow = True

            processo = Process.Start(psi)
            Dim lettore As StreamReader = processo.StandardOutput

            While Not processo.HasExited
                If lavoro.Annulla Then
                    Try
                        processo.Kill()
                    Catch exKill As Exception
                        _log.ScriviLog(exKill.ToString())
                    End Try
                    Exit While
                End If

                Dim riga As String = lettore.ReadLine()
                If Not String.IsNullOrEmpty(riga) Then
                    If riga.StartsWith("PROGRESSO:") Then
                        Integer.TryParse(riga.Substring(10), lavoro.Progresso)
                    ElseIf riga.StartsWith("STATO:") Then
                        lavoro.Info = riga.Substring(6)
                    End If
                End If
                Thread.Sleep(50)
            End While

            processo.WaitForExit()

            If Not lavoro.Annulla Then
                lavoro.Progresso = 100
                NotificaFineOperazione("Invio Bluetooth completato.")
            Else
                NotificaFineOperazione("Invio Bluetooth annullato.")
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            NotificaFineOperazione("Errore invio Bluetooth.")
        Finally
            Try
                If Not String.IsNullOrEmpty(scriptPath) AndAlso File.Exists(scriptPath) Then
                    File.Delete(scriptPath)
                End If
            Catch exFile As Exception
                _log.ScriviLog(exFile.ToString())
            End Try
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Public Sub AggiungiVideo(url As String)
        Try
            If String.IsNullOrWhiteSpace(url) Then Return
            SyncLock _lockCodaVideo
                _codaVideoInAttesa.Add(url)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AggiungiListaVideo(listaUrl As List(Of String))
        Try
            If listaUrl Is Nothing OrElse listaUrl.Count = 0 Then Return
            SyncLock _lockCodaVideo
                _codaVideoInAttesa.AddRange(listaUrl)
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub AvviaDownloadVideo()
        Try
            If Not System.IO.File.Exists(_pathYtDlp) Then
                NotificaFineOperazione("Errore: yt-dlp.exe non trovato.")
                Return
            End If

            Dim lavoro As New LavoroVideo()
            SyncLock _lockCodaVideo
                If _codaVideoInAttesa.Count = 0 Then Return
                lavoro.Coda = New List(Of String)(_codaVideoInAttesa)
                _codaVideoInAttesa.Clear()
            End SyncLock

            lavoro.VideoTotali = lavoro.Coda.Count
            lavoro.VideoElaborati = 0
            lavoro.Info = "Avvio download..."
            RegistraLavoro(lavoro, "Download Video in corso", StatoOperazioneCorrente.Video)
            lavoro.InPreparazione = False

            For Each url As String In lavoro.Coda
                AggiungiRisorsa(lavoro, "video:" & url)
            Next

            lavoro.ThreadLavoro = New Thread(AddressOf EseguiDownloadVideoLoop)
            lavoro.ThreadLavoro.IsBackground = True
            lavoro.ThreadLavoro.Start(lavoro)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function LavoriVideoAttivi() As List(Of LavoroVideo)
        Try
            Dim risultato As New List(Of LavoroVideo)()
            For Each lavoro As LavoroBase In LavoriAttivi()
                Dim lavoroVideo As LavoroVideo = TryCast(lavoro, LavoroVideo)
                If lavoroVideo IsNot Nothing Then risultato.Add(lavoroVideo)
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return New List(Of LavoroVideo)()
        End Try
    End Function

    Private Sub AnalizzaRigaProgressoVideo(lavoro As LavoroVideo, riga As String)
        Try
            If lavoro Is Nothing OrElse String.IsNullOrWhiteSpace(riga) Then Return

            If riga.IndexOf("[download]", StringComparison.OrdinalIgnoreCase) < 0 Then
                If riga.TrimStart().StartsWith("[") Then lavoro.Info = riga.Trim()
                Return
            End If

            Dim corrispondenzaPercentuale As Match = Regex.Match(riga, "(\d+(?:[\.,]\d+)?)\s*%")
            If corrispondenzaPercentuale.Success Then
                Dim valoreTesto As String = corrispondenzaPercentuale.Groups(1).Value.Replace(",", ".")
                Dim valore As Double = 0
                If Double.TryParse(valoreTesto, NumberStyles.Any, CultureInfo.InvariantCulture, valore) Then
                    If valore >= 0 AndAlso valore <= 100 Then lavoro.ProgressoCorrente = valore
                End If
            End If

            Dim corrispondenzaVelocita As Match = Regex.Match(riga, "at\s+(\S+/s)")
            If corrispondenzaVelocita.Success Then
                lavoro.Velocita = corrispondenzaVelocita.Groups(1).Value
            End If

            Dim corrispondenzaResiduo As Match = Regex.Match(riga, "ETA\s+([0-9:]+)")
            If corrispondenzaResiduo.Success Then
                lavoro.TempoResiduo = corrispondenzaResiduo.Groups(1).Value
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub EseguiDownloadVideoLoop(parametro As Object)
        Dim lavoro As LavoroVideo = TryCast(parametro, LavoroVideo)
        Try
            If lavoro Is Nothing Then Return
            If Not AttendiRisorseLibere(lavoro) Then Return

            Dim i As Integer = 0
            While i < lavoro.Coda.Count
                If lavoro.Annulla Then
                    Exit While
                End If
                While lavoro.InPausa
                    Thread.Sleep(500)
                    If lavoro.Annulla Then
                        Exit While
                    End If
                End While
                Try
                    lavoro.ProgressoCorrente = 0
                    lavoro.Velocita = ""
                    lavoro.TempoResiduo = ""
                    lavoro.Info = $"Download del video {i + 1} di {lavoro.VideoTotali}"

                    Dim argomenti As String = "--newline --no-color " & Chr(34) & lavoro.Coda(i) & Chr(34)
                    Dim procInfo As New ProcessStartInfo(_pathYtDlp, argomenti) With {
                        .UseShellExecute = False,
                        .CreateNoWindow = True,
                        .RedirectStandardOutput = True,
                        .RedirectStandardError = True,
                        .StandardOutputEncoding = System.Text.Encoding.UTF8
                    }

                    lavoro.Processo = Process.Start(procInfo)

                    Dim lettore As StreamReader = lavoro.Processo.StandardOutput
                    Dim riga As String = lettore.ReadLine()
                    While riga IsNot Nothing
                        AnalizzaRigaProgressoVideo(lavoro, riga)

                        If lavoro.Annulla Then
                            Try
                                If Not lavoro.Processo.HasExited Then lavoro.Processo.Kill()
                            Catch exKill As Exception
                                _log.ScriviLog(exKill.ToString())
                            End Try
                            Exit While
                        End If

                        riga = lettore.ReadLine()
                    End While

                    lavoro.Processo.WaitForExit()

                    If Not lavoro.Annulla Then
                        lavoro.ProgressoCorrente = 100
                        lavoro.VideoElaborati += 1
                    End If
                Catch ex As Exception
                    _log.ScriviLog(ex.ToString())
                End Try
                i += 1
            End While
            NotificaFineOperazione(If(Not lavoro.Annulla, "Download video completati.", "Download video annullati."))
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        Finally
            Try
                If lavoro IsNot Nothing Then
                    lavoro.InEsecuzione = False
                    lavoro.Processo = Nothing
                    lavoro.Coda.Clear()
                    RimuoviLavoro(lavoro)
                End If
            Catch exPulizia As Exception
                _log.ScriviLog(exPulizia.ToString())
            End Try
        End Try
    End Sub

    Public Sub RiproduciFileAudio(listaFiles As List(Of String), Optional nomi As List(Of String) = Nothing)
        Try
            If _inEsecuzioneAudio Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("È già in corso una riproduzione." & vbCrLf & "Terminare l'ascolto attuale per avviarne uno nuovo.")
                msg.ShowDialog()
                Return
            End If

            If listaFiles Is Nothing OrElse listaFiles.Count = 0 Then
                Return
            End If

            Dim nuovaLista As New List(Of elemento)
            For i As Integer = 0 To listaFiles.Count - 1
                Dim el As New elemento()
                el.path = listaFiles(i)
                If nomi IsNot Nothing AndAlso i < nomi.Count Then el.nome = nomi(i)
                If listaFiles(i).StartsWith("http", StringComparison.OrdinalIgnoreCase) Then
                    el.tipo = ElementType.YouTube
                Else
                    el.tipo = ElementType.File
                End If
                nuovaLista.Add(el)
            Next

            _elementiAudio = nuovaLista
            _indiceAudio = 0
            _inEsecuzioneAudio = True
            _ripetiTraccia = False

            _mpv = WGMpv.GetInstance()
            RemoveHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            AddHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            GestisciRiproduzione()

        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub RiproduciStreamRadio(url As String)
        Try
            If _inEsecuzioneAudio Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("È già in corso una riproduzione. Terminare l'ascolto attuale per avviarne uno nuovo.")
                msg.ShowDialog()
                Return
            End If

            Dim el As New elemento()
            el.path = url
            el.tipo = ElementType.WebRadio

            _elementiAudio = New List(Of elemento) From {el}
            _indiceAudio = 0
            _inEsecuzioneAudio = True
            _ripetiTraccia = False

            _mpv = WGMpv.GetInstance()
            RemoveHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            AddHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            GestisciRiproduzione()

        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ''' <summary>Ascolta in sottofondo una radio, con il suo nome come descrizione.</summary>
    Public Sub RiproduciStreamRadio(url As String, nome As String)
        Try
            If _inEsecuzioneAudio Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("È già in corso una riproduzione. Terminare l'ascolto attuale per avviarne uno nuovo.")
                msg.ShowDialog()
                Return
            End If

            Dim el As New elemento()
            el.path = url
            el.tipo = ElementType.WebRadio
            el.nome = nome

            _elementiAudio = New List(Of elemento) From {el}
            _indiceAudio = 0
            _inEsecuzioneAudio = True
            _ripetiTraccia = False

            _mpv = WGMpv.GetInstance()
            RemoveHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            AddHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            GestisciRiproduzione()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' Segue in sottofondo un canale televisivo, con l'user agent e il referer che richiede se noti.
    Public Sub RiproduciStreamTv(url As String, nome As String, userAgent As String, referer As String)
        Try
            If _inEsecuzioneAudio Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("È già in corso una riproduzione. Terminare l'ascolto attuale per avviarne uno nuovo.")
                msg.ShowDialog()
                Return
            End If

            Dim el As New elemento()
            el.path = url
            el.tipo = ElementType.StreamTv
            el.nome = nome
            el.userAgent = userAgent
            el.referer = referer

            _elementiAudio = New List(Of elemento) From {el}
            _indiceAudio = 0
            _inEsecuzioneAudio = True
            _ripetiTraccia = False

            _mpv = WGMpv.GetInstance()
            RemoveHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            AddHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            GestisciRiproduzione()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    ' Ascolta in sottofondo una serie di radio una dopo l'altra (per esempio tutte quelle di una regione).
    Public Sub RiproduciCodaRadio(indirizzi As List(Of String), nomi As List(Of String))
        Try
            If _inEsecuzioneAudio Then
                Dim msg As New WGMessaggi()
                msg.Messaggio("È già in corso una riproduzione. Terminare l'ascolto attuale per avviarne uno nuovo.")
                msg.ShowDialog()
                Return
            End If
            If indirizzi Is Nothing OrElse indirizzi.Count = 0 Then Return

            Dim nuovaLista As New List(Of elemento)
            For i As Integer = 0 To indirizzi.Count - 1
                Dim el As New elemento()
                el.path = indirizzi(i)
                el.tipo = ElementType.WebRadio
                If nomi IsNot Nothing AndAlso i < nomi.Count Then el.nome = nomi(i)
                nuovaLista.Add(el)
            Next

            _elementiAudio = nuovaLista
            _indiceAudio = 0
            _inEsecuzioneAudio = True
            _ripetiTraccia = False

            _mpv = WGMpv.GetInstance()
            RemoveHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            AddHandler _mpv.EndFile, AddressOf GestisciFineTraccia
            GestisciRiproduzione()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub VisualizzaControllo()
        Try
            ' Il centro di controllo di sempre, a menu: F6, Windows A e la voce del menu. Il pannello con
            ' interruttori e cursori si apre solo in modalità tablet con tre dita verso l'alto.
            Dim menu As New WGMenu()
            For Each voce As Tuple(Of String, Action) In VociOperazioni()
                Dim azione As Action = voce.Item2
                menu.aggiungi(voce.Item1, Sub(s, e) azione())
            Next
            menu.avvia("Centro di Controllo", True)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function PercentualeLavoro(lavoro As LavoroBase) As Integer
        Try
            Dim lavoroFile As LavoroFile = TryCast(lavoro, LavoroFile)
            If lavoroFile IsNot Nothing Then Return PercentualeFile(lavoroFile)

            Dim lavoroFtp As LavoroFtp = TryCast(lavoro, LavoroFtp)
            If lavoroFtp IsNot Nothing Then Return PercentualeFtp(lavoroFtp)

            Dim lavoroArchivio As LavoroArchivio = TryCast(lavoro, LavoroArchivio)
            If lavoroArchivio IsNot Nothing Then Return lavoroArchivio.Progresso

            Dim lavoroLibriVox As LavoroLibriVox = TryCast(lavoro, LavoroLibriVox)
            If lavoroLibriVox IsNot Nothing Then Return PercentualeLibriVox(lavoroLibriVox)

            Dim lavoroTesti As LavoroTesti = TryCast(lavoro, LavoroTesti)
            If lavoroTesti IsNot Nothing Then Return PercentualeTesti(lavoroTesti)

            Dim lavoroArchivioInternet As LavoroArchivioInternet = TryCast(lavoro, LavoroArchivioInternet)
            If lavoroArchivioInternet IsNot Nothing Then Return PercentualeArchivioInternet(lavoroArchivioInternet)

            Dim lavoroVideo As LavoroVideo = TryCast(lavoro, LavoroVideo)
            If lavoroVideo IsNot Nothing Then Return PercentualeVideo(lavoroVideo)

            Dim lavoroBluetooth As LavoroBluetooth = TryCast(lavoro, LavoroBluetooth)
            If lavoroBluetooth IsNot Nothing Then Return lavoroBluetooth.Progresso

            Dim lavoroRiscrittura As LavoroRiscrittura = TryCast(lavoro, LavoroRiscrittura)
            If lavoroRiscrittura IsNot Nothing Then Return lavoroRiscrittura.Progresso

            Dim lavoroMp3 As LavoroConversioneMp3 = TryCast(lavoro, LavoroConversioneMp3)
            If lavoroMp3 IsNot Nothing Then Return lavoroMp3.Progresso

            Dim lavoroSpartiti As LavoroRiconoscimentoSpartiti = TryCast(lavoro, LavoroRiconoscimentoSpartiti)
            If lavoroSpartiti IsNot Nothing Then Return lavoroSpartiti.Progresso

            Dim lavoroModelli As LavoroModelli = TryCast(lavoro, LavoroModelli)
            If lavoroModelli IsNot Nothing Then Return PercentualeModelli(lavoroModelli)

            Dim lavoroYoutube As LavoroYoutube = TryCast(lavoro, LavoroYoutube)
            If lavoroYoutube IsNot Nothing Then Return lavoroYoutube.Progresso

            Return 0
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return 0
        End Try
    End Function

    Private Function EtichettaLavoro(lavoro As LavoroBase) As String
        Try
            If lavoro Is Nothing Then Return "Operazione in corso"
            If lavoro.InPreparazione Then Return lavoro.Descrizione & ", preparazione"
            If lavoro.InAttesa Then Return lavoro.Descrizione & ", in attesa di " & lavoro.MotivoAttesa

            Dim lavoroMp3Etichetta As LavoroConversioneMp3 = TryCast(lavoro, LavoroConversioneMp3)
            If lavoroMp3Etichetta IsNot Nothing Then
                Dim percentualeMp3 As Integer = lavoroMp3Etichetta.Progresso
                If percentualeMp3 < 0 Then percentualeMp3 = 0
                If percentualeMp3 > 100 Then percentualeMp3 = 100
                Dim etichettaMp3 As String = lavoro.Descrizione & ", " & percentualeMp3.ToString() & " per cento, fine tra " & TempoStimatoFineMp3(lavoroMp3Etichetta)
                If lavoro.InPausa Then etichettaMp3 &= ", in pausa"
                Return etichettaMp3
            End If

            Dim lavoroSpartitiEtichetta As LavoroRiconoscimentoSpartiti = TryCast(lavoro, LavoroRiconoscimentoSpartiti)
            If lavoroSpartitiEtichetta IsNot Nothing Then Return EtichettaRiconoscimentoSpartiti(lavoroSpartitiEtichetta)

            Dim etichetta As String = EtichettaOperazione(lavoro.Descrizione, PercentualeLavoro(lavoro))
            If lavoro.InPausa Then etichetta &= ", in pausa"
            Return etichetta
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "Operazione in corso"
        End Try
    End Function

    Private Function LavoroSupportaPausa(lavoro As LavoroBase) As Boolean
        Try
            If TypeOf lavoro Is LavoroFile Then Return True
            If TypeOf lavoro Is LavoroFtp Then Return True
            If TypeOf lavoro Is LavoroVideo Then Return True
            If TypeOf lavoro Is LavoroRiscrittura Then Return True
            Return False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return False
        End Try
    End Function

    Private Sub ApriMenuLavoro(lavoro As LavoroBase, controllo As ControlloLavoro)
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Informazioni", AddressOf controllo.MostraInformazioni)
            If LavoroSupportaPausa(lavoro) Then
                menu.aggiungi("Azioni", AddressOf controllo.ApriAzioni)
            Else
                menu.aggiungi("Annulla Operazione", AddressOf controllo.Annulla)
            End If
            menu.avvia(EtichettaLavoro(lavoro), True)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriMenuAzioniLavoro(lavoro As LavoroBase, controllo As ControlloLavoro)
        Try
            Dim menu As New WGMenu()
            If lavoro.InPausa Then
                menu.aggiungi("Riprendi", AddressOf controllo.Riprendi)
            Else
                menu.aggiungi("Pausa", AddressOf controllo.MettiInPausa)
            End If
            menu.aggiungi("Annulla", AddressOf controllo.Annulla)
            menu.avvia("Azioni: " & lavoro.Descrizione, True)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MettiInPausaLavoro(lavoro As LavoroBase)
        Try
            If lavoro Is Nothing Then Return
            Dim lavoroFile As LavoroFile = TryCast(lavoro, LavoroFile)
            If lavoroFile IsNot Nothing Then
                lavoroFile.TempoUltimaPausa = DateTime.Now
            End If
            lavoro.InPausa = True
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub RiprendiLavoro(lavoro As LavoroBase)
        Try
            If lavoro Is Nothing Then Return
            Dim lavoroFile As LavoroFile = TryCast(lavoro, LavoroFile)
            If lavoroFile IsNot Nothing AndAlso lavoroFile.InPausa Then
                lavoroFile.DurataTotalePausa += (DateTime.Now - lavoroFile.TempoUltimaPausa)
            End If
            lavoro.InPausa = False
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub AnnullaLavoro(lavoro As LavoroBase)
        Try
            If lavoro Is Nothing Then Return
            lavoro.Annulla = True
            lavoro.InPausa = False

            Dim lavoroFtp As LavoroFtp = TryCast(lavoro, LavoroFtp)
            If lavoroFtp IsNot Nothing Then
                Try
                    Dim sorgente As CancellationTokenSource = lavoroFtp.Annullamento
                    If sorgente IsNot Nothing AndAlso Not sorgente.IsCancellationRequested Then
                        sorgente.Cancel()
                    End If
                Catch exToken As Exception
                    _log.ScriviLog(exToken.ToString())
                End Try
            End If

            Dim lavoroVideo As LavoroVideo = TryCast(lavoro, LavoroVideo)
            If lavoroVideo IsNot Nothing AndAlso lavoroVideo.Processo IsNot Nothing Then
                Try
                    If Not lavoroVideo.Processo.HasExited Then lavoroVideo.Processo.Kill()
                Catch exProcesso As Exception
                    _log.ScriviLog(exProcesso.ToString())
                End Try
            End If

            Dim lavoroArchivio As LavoroArchivio = TryCast(lavoro, LavoroArchivio)
            If lavoroArchivio IsNot Nothing AndAlso lavoroArchivio.Processo IsNot Nothing Then
                Try
                    If Not lavoroArchivio.Processo.HasExited Then lavoroArchivio.Processo.Kill()
                Catch exProcesso As Exception
                    _log.ScriviLog(exProcesso.ToString())
                End Try
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraInfoLavoro(lavoro As LavoroBase)
        Try
            If lavoro Is Nothing Then Return

            If lavoro.InAttesa Then
                Dim msgAttesa As New WGMessaggi()
                msgAttesa.Messaggio($"{lavoro.Descrizione}" & vbCrLf &
                                    "STATO: IN ATTESA" & vbCrLf &
                                    $"Sono in coda perché gli stessi file sono usati da: {lavoro.MotivoAttesa}." & vbCrLf &
                                    "L'operazione parte da sola appena l'altra ha finito.")
                msgAttesa.ShowDialog()
                Return
            End If

            Dim lavoroFile As LavoroFile = TryCast(lavoro, LavoroFile)
            If lavoroFile IsNot Nothing Then
                MostraInfoFile(lavoroFile)
                Return
            End If

            Dim lavoroFtp As LavoroFtp = TryCast(lavoro, LavoroFtp)
            If lavoroFtp IsNot Nothing Then
                MostraInfoFtp(lavoroFtp)
                Return
            End If

            Dim msg As New WGMessaggi()
            Dim righe As String = $"{lavoro.Descrizione}" & vbCrLf

            Dim lavoroArchivio As LavoroArchivio = TryCast(lavoro, LavoroArchivio)
            If lavoroArchivio IsNot Nothing Then
                righe &= $"Avanzamento: {lavoroArchivio.Progresso} %"
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroLibriVox As LavoroLibriVox = TryCast(lavoro, LavoroLibriVox)
            If lavoroLibriVox IsNot Nothing Then
                righe &= $"{lavoroLibriVox.Info}" & vbCrLf
                If lavoroLibriVox.TipoOperazione = TipoOperazioneLibriVox.DownloadLibri AndAlso lavoroLibriVox.LibriTotali > 0 Then
                    righe &= $"Libro {Math.Min(lavoroLibriVox.LibriCompletati + 1, lavoroLibriVox.LibriTotali)} di {lavoroLibriVox.LibriTotali}" & vbCrLf
                    If lavoroLibriVox.StatoDownload.BytesTotali > 0 Then
                        righe &= $"Download libro corrente: {CInt(Math.Round(lavoroLibriVox.StatoDownload.Percentuale(), 0))} %" & vbCrLf
                        righe &= $"Velocità: {FormattaVelocita(lavoroLibriVox.StatoDownload.VelocitaBps)}" & vbCrLf
                        righe &= $"Tempo stimato libro: {FormattaTempoDaSecondi(lavoroLibriVox.StatoDownload.SecondiResidui())}" & vbCrLf
                    End If
                    If lavoroLibriVox.ProgressoEstrazione.Valore > 0 Then
                        righe &= $"Estrazione libro corrente: {lavoroLibriVox.ProgressoEstrazione.Valore} %" & vbCrLf
                    End If
                End If
                righe &= $"Progresso totale: {PercentualeLibriVox(lavoroLibriVox)} %"
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroTesti As LavoroTesti = TryCast(lavoro, LavoroTesti)
            If lavoroTesti IsNot Nothing Then
                righe &= $"{lavoroTesti.Info}" & vbCrLf
                If lavoroTesti.LibriTotali > 0 Then
                    righe &= $"Libro {Math.Min(lavoroTesti.LibriCompletati + 1, lavoroTesti.LibriTotali)} di {lavoroTesti.LibriTotali}" & vbCrLf
                    If lavoroTesti.CapitoliTotali > 0 Then
                        righe &= $"Capitoli: {lavoroTesti.CapitoliCompletati} di {lavoroTesti.CapitoliTotali}" & vbCrLf
                    End If
                End If
                righe &= $"Progresso totale: {PercentualeTesti(lavoroTesti)} %"
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroArchivioInternet As LavoroArchivioInternet = TryCast(lavoro, LavoroArchivioInternet)
            If lavoroArchivioInternet IsNot Nothing Then
                righe &= $"{lavoroArchivioInternet.Info}" & vbCrLf
                If lavoroArchivioInternet.LibriTotali > 0 Then
                    righe &= $"Elemento {Math.Min(lavoroArchivioInternet.LibriCompletati + 1, lavoroArchivioInternet.LibriTotali)} di {lavoroArchivioInternet.LibriTotali}" & vbCrLf
                    If lavoroArchivioInternet.BytesTotaliLibro > 0 Then
                        Dim percentualeLibro As Double = (lavoroArchivioInternet.BytesScaricati() / CDbl(lavoroArchivioInternet.BytesTotaliLibro)) * 100
                        If percentualeLibro > 100 Then percentualeLibro = 100
                        righe &= $"Elemento corrente: {CInt(Math.Round(percentualeLibro, 0))} %" & vbCrLf
                        If lavoroArchivioInternet.TracceTotaliLibro > 0 Then
                            righe &= $"Tracce da scaricare: {lavoroArchivioInternet.TracceTotaliLibro}" & vbCrLf
                        End If
                    End If
                End If
                righe &= $"Progresso totale: {PercentualeArchivioInternet(lavoroArchivioInternet)} %"
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroVideo As LavoroVideo = TryCast(lavoro, LavoroVideo)
            If lavoroVideo IsNot Nothing Then
                If lavoroVideo.InPausa Then
                    righe &= "STATO: IN PAUSA" & vbCrLf
                End If
                righe &= $"{lavoroVideo.Info}" & vbCrLf
                righe &= $"Video scaricati: {lavoroVideo.VideoElaborati} su {lavoroVideo.VideoTotali}" & vbCrLf
                righe &= $"Video corrente: {CInt(Math.Round(lavoroVideo.ProgressoCorrente, 0))} %" & vbCrLf
                If Not String.IsNullOrWhiteSpace(lavoroVideo.Velocita) Then
                    righe &= $"Velocità: {lavoroVideo.Velocita}" & vbCrLf
                End If
                If Not String.IsNullOrWhiteSpace(lavoroVideo.TempoResiduo) Then
                    righe &= $"Tempo stimato: {lavoroVideo.TempoResiduo}" & vbCrLf
                End If
                righe &= $"Avanzamento totale: {PercentualeVideo(lavoroVideo)} %"
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroRiscrittura As LavoroRiscrittura = TryCast(lavoro, LavoroRiscrittura)
            If lavoroRiscrittura IsNot Nothing Then
                If lavoroRiscrittura.InPausa Then righe &= "STATO: IN PAUSA" & vbCrLf
                righe &= lavoroRiscrittura.Info & vbCrLf
                righe &= "File modificati: " & lavoroRiscrittura.FileCompletati.ToString() & " su " & lavoroRiscrittura.Files.Count.ToString() & vbCrLf
                righe &= "Avanzamento: " & lavoroRiscrittura.Progresso.ToString() & " %"
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroBluetooth As LavoroBluetooth = TryCast(lavoro, LavoroBluetooth)
            If lavoroBluetooth IsNot Nothing Then
                righe &= $"{lavoroBluetooth.Info}" & vbCrLf
                righe &= $"Dispositivo: {lavoroBluetooth.NomeDispositivo}" & vbCrLf
                righe &= $"Avanzamento: {lavoroBluetooth.Progresso} %"
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroMp3 As LavoroConversioneMp3 = TryCast(lavoro, LavoroConversioneMp3)
            If lavoroMp3 IsNot Nothing Then
                Dim percentualeMp3 As Integer = lavoroMp3.Progresso
                If percentualeMp3 < 0 Then percentualeMp3 = 0
                If percentualeMp3 > 100 Then percentualeMp3 = 100
                If Not String.IsNullOrWhiteSpace(lavoroMp3.Info) Then
                    righe &= lavoroMp3.Info & vbCrLf
                End If
                If Not String.IsNullOrWhiteSpace(lavoroMp3.PercorsoDestinazione) Then
                    righe &= "Destinazione: " & lavoroMp3.PercorsoDestinazione & vbCrLf
                End If
                righe &= "Avanzamento: " & percentualeMp3.ToString() & " per cento" & vbCrLf
                righe &= "Durata stimata alla fine: " & TempoStimatoFineMp3(lavoroMp3)
                msg.Messaggio(righe)
                msg.ShowDialog()
                Return
            End If

            Dim lavoroSpartiti As LavoroRiconoscimentoSpartiti = TryCast(lavoro, LavoroRiconoscimentoSpartiti)
            If lavoroSpartiti IsNot Nothing Then
                msg.Messaggio(righe & InfoRiconoscimentoSpartiti(lavoroSpartiti))
                msg.ShowDialog()
                Return
            End If

            Dim lavoroYoutubeInfo As LavoroYoutube = TryCast(lavoro, LavoroYoutube)
            If lavoroYoutubeInfo IsNot Nothing Then
                msg.Messaggio(InfoYoutube(lavoroYoutubeInfo))
                msg.ShowDialog()
                Return
            End If

            Dim lavoroModelliInfo As LavoroModelli = TryCast(lavoro, LavoroModelli)
            If lavoroModelliInfo IsNot Nothing Then
                msg.Messaggio(righe & InfoModelli(lavoroModelliInfo))
                msg.ShowDialog()
                Return
            End If

            righe &= $"Avanzamento: {PercentualeLavoro(lavoro)} %"
            msg.Messaggio(righe)
            msg.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraInfoFile(lavoro As LavoroFile)
        Try
            Dim msg As New WGMessaggi()
            If lavoro.InPausa Then
                msg.Messaggio($"{lavoro.Descrizione}" & vbCrLf & "STATO: IN PAUSA" & vbCrLf & "Il calcolo del tempo è sospeso.")
            ElseIf lavoro.InPreparazione Then
                msg.Messaggio($"{lavoro.Descrizione}" & vbCrLf & "STATO: PREPARAZIONE" & vbCrLf & "Scansione dei file in corso...")
            ElseIf lavoro.DownloadOneDriveInCorso Then
                Dim percDownload As Double = 0
                Dim rigaScaricato As String
                If lavoro.DownloadOneDriveBytesTotali > 0 Then
                    percDownload = (lavoro.DownloadOneDriveBytesScaricati / CDbl(lavoro.DownloadOneDriveBytesTotali)) * 100
                    If percDownload < 0 Then percDownload = 0
                    If percDownload > 100 Then percDownload = 100
                    rigaScaricato = $"Scaricato: {CInt(Math.Round(percDownload, 0))} % ({FormattaDimensioneBytes(lavoro.DownloadOneDriveBytesScaricati)} / {FormattaDimensioneBytes(lavoro.DownloadOneDriveBytesTotali)})"
                Else
                    rigaScaricato = $"Scaricato: {FormattaDimensioneBytes(lavoro.DownloadOneDriveBytesScaricati)} (dimensione totale in calcolo...)"
                End If
                msg.Messaggio("STATO: DOWNLOAD DA ONEDRIVE" & vbCrLf &
                              $"File: {lavoro.DownloadOneDriveNomeFile}" & vbCrLf &
                              rigaScaricato & vbCrLf &
                              $"Velocità: {FormattaVelocita(lavoro.DownloadOneDriveVelocitaBps)}" & vbCrLf &
                              $"Tempo residuo download: {FormattaTempoResiduoDownloadOneDrive(lavoro)}" & vbCrLf &
                              $"Completamento operazione totale: {PercentualeFile(lavoro)} %")
            Else
                Dim rigaFile As String = If(String.IsNullOrEmpty(lavoro.NomeFileCorrente), "", $"File: {lavoro.NomeFileCorrente}" & vbCrLf)
                msg.Messaggio($"{lavoro.Descrizione}" & vbCrLf &
                              rigaFile &
                              $"Completamento: {PercentualeFile(lavoro)} %" & vbCrLf &
                              $"Velocità: {FormattaVelocita(lavoro.VelocitaBps)}" & vbCrLf &
                              $"Tempo stimato: {CalcolaTempoResiduo(lavoro)}")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraInfoFtp(lavoro As LavoroFtp)
        Try
            Dim msg As New WGMessaggi()
            If lavoro.InPausa Then
                msg.Messaggio($"{lavoro.Descrizione}" & vbCrLf & "STATO: IN PAUSA" & vbCrLf & "Il calcolo del tempo è sospeso.")
            ElseIf lavoro.InPreparazione Then
                msg.Messaggio($"{lavoro.Descrizione}" & vbCrLf & "STATO: PREPARAZIONE" & vbCrLf & "Scansione dei file in corso...")
            Else
                Dim nomiFile As String = NomiFileFtpAttivi(lavoro)
                Dim rigaFile As String = If(String.IsNullOrEmpty(nomiFile), "", $"File in corso: {nomiFile}" & vbCrLf)
                msg.Messaggio("STATO: TRASFERIMENTO FTP/SFTP" & vbCrLf &
                              rigaFile &
                              $"Trasferimenti in parallelo: {GradoParallelismoFtp}" & vbCrLf &
                              $"Completamento: {PercentualeFtp(lavoro)} %" & vbCrLf &
                              $"Velocità: {FormattaVelocita(VelocitaTotaleFtp(lavoro))}" & vbCrLf &
                              $"Tempo stimato: {FormattaTempoResiduoFtp(lavoro)}")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriMenuDownloadDipendenze()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Informazioni", AddressOf MostraInfoDipendenze)
            menu.avvia(EtichettaOperazione("Dipendenze", PercentualeDipendenze()), True)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub MostraInfoDipendenze()
        Try
            Dim msg As New WGMessaggi()
            Dim righe As String = $"Stato: {_statoDownloadDipendenze}" & vbCrLf
            If _statoDownloadDipendenzeHttp.BytesTotali > 0 Then
                righe &= $"Componente in corso: {CInt(Math.Round(_statoDownloadDipendenzeHttp.Percentuale(), 0))} %" & vbCrLf
                righe &= $"Velocità: {FormattaVelocita(_statoDownloadDipendenzeHttp.VelocitaBps)}" & vbCrLf
                righe &= $"Tempo stimato: {FormattaTempoDaSecondi(_statoDownloadDipendenzeHttp.SecondiResidui())}" & vbCrLf
            End If
            righe &= $"Avanzamento totale: {PercentualeDipendenze()} %"
            msg.Messaggio(righe)
            msg.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriMenuAudioFile()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Info: " & _descrizioneAudio, Sub() Return)
            If _elementiAudio.Count > 1 Then
                menu.aggiungi("Traccia Precedente", Sub() TracciaPrecedente())
            End If

            If _mpv.IsPaused Then
                menu.aggiungi("Riproduci", Sub()
                                               _mpv.Riproduci()
                                           End Sub)
            Else
                menu.aggiungi("Pausa", Sub() _mpv.Pausa())
            End If

            If _elementiAudio.Count > 1 AndAlso _indiceAudio < _elementiAudio.Count - 1 Then
                menu.aggiungi("Traccia Successiva", Sub() TracciaSuccessiva())
            End If

            menu.aggiungi("Indietro 30 secondi", Sub() _mpv.Seek(-30))
            menu.aggiungi("Avanti 30 secondi", Sub() _mpv.Seek(30))

            Dim txtRipeti As String = If(_ripetiTraccia, "Disattiva Ripetizione", "Attiva Ripetizione")
            menu.aggiungi(txtRipeti, Sub() _ripetiTraccia = Not _ripetiTraccia)

            menu.aggiungi("Alza Volume", Sub() _mpv.Volume += 5)
            menu.aggiungi("Abbassa Volume", Sub() _mpv.Volume -= 5)

            menu.aggiungi("Coda", AddressOf ApriGestioneCoda)
            menu.aggiungi("Termina Ascolto", AddressOf TerminaAscoltoAudio)

            menu.avvia("Controllo Brani", True)
        Catch ex As Exception
             _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriGestioneCoda()
        Try
            _menuCodaLista = New WGMenuLista()
            _menuCodaLista.impostaTitolo("Coda di Riproduzione")
            _menuCodaLista.multiselezioneConsentita = True

            For i As Integer = 0 To _elementiAudio.Count - 1
                Dim el As elemento = _elementiAudio(i)
                Dim nomeVoce As String = el.nome
                If String.IsNullOrWhiteSpace(nomeVoce) Then nomeVoce = System.IO.Path.GetFileName(el.path)
                If i = _indiceAudio Then
                    nomeVoce = "[PLAY] " & nomeVoce
                End If
                _menuCodaLista.aggiungiElemento(nomeVoce, el.path)
             Next
            AddHandler _menuCodaLista.escPremuto, AddressOf chiudilista
            _menuCodaLista.opzioniPulisci()
            _menuCodaLista.aggiungiOpzione("Elimina", AddressOf Elimina)
            _menuCodaLista.aggiungiOpzione("Svuota coda", AddressOf SvuotaCoda)
            _menuCodaLista.avvia()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub chiudilista()
        Try
            _menuCodaLista.abbandona()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub
    
    Private Sub Elimina()
        Try
            Dim elementiDaEliminare As List(Of String) = _menuCodaLista.ottieniElementi()
            If elementiDaEliminare Is Nothing OrElse elementiDaEliminare.Count = 0 Then Return

            Dim rimossoCorrente As Boolean = False
            For Each pathToRemove As String In elementiDaEliminare
                Dim posizione As Integer = -1
                For i As Integer = 0 To _elementiAudio.Count - 1
                    If String.Equals(_elementiAudio(i).path, pathToRemove, StringComparison.OrdinalIgnoreCase) Then
                        posizione = i
                        Exit For
                    End If
                Next
                If posizione >= 0 Then
                    If posizione = _indiceAudio Then
                         rimossoCorrente = True
                    End If
                    _elementiAudio.RemoveAt(posizione)
                End If
            Next

            If _elementiAudio.Count = 0 Then
                TerminaAscoltoAudio()
                _menuCodaLista.pulisci()
                Dim msg As New WGMessaggi()
                msg.Messaggio("Coda vuota." & vbCrLf & "Riproduzione terminata.")
                msg.ShowDialog()
                _menuCodaLista.abbandona()
            Else
                If rimossoCorrente Then
                    If _indiceAudio >= _elementiAudio.Count Then
                         _indiceAudio = 0
                    End If
                    GestisciRiproduzione()
                End If
                ApriGestioneCoda()
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub SvuotaCoda()
        Try
            TerminaAscoltoAudio()
            _menuCodaLista.abbandona()
            Dim msg As New WGMessaggi()
             msg.Messaggio("Coda svuotata e riproduzione terminata.")
            msg.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Sub ApriMenuAudioStream()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Info: " & _descrizioneAudio, Sub() Return)

            If _mpv.IsPaused Then
                menu.aggiungi("Riproduci", Sub() PlayPausa())
            Else
                menu.aggiungi("Pausa", Sub() PlayPausa())
            End If

            menu.aggiungi("Alza Volume", Sub() _mpv.Volume += 5)
            menu.aggiungi("Abbassa Volume", Sub() _mpv.Volume -= 5)
            menu.aggiungi("Termina Ascolto", AddressOf TerminaAscoltoAudio)
            menu.avvia("Controllo Radio", True)
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Private Function CalcolaTempoResiduo(lavoro As LavoroFile) As String
        Try
            If lavoro Is Nothing Then Return "Sconosciuto"
            Dim bytesProcessatiTotali As Long = lavoro.BytesProcessati + lavoro.BytesFileCorrente
            If bytesProcessatiTotali = 0 Then
                Return "Calcolo in corso..."
            End If
            Dim bytesPerSec As Double = lavoro.VelocitaBps
            If bytesPerSec <= 0 Then
                Dim trascorsi As TimeSpan = (DateTime.Now - lavoro.TempoInizio) - lavoro.DurataTotalePausa
                If trascorsi.TotalSeconds <= 0 Then
                    Return "Calcolo in corso..."
                End If
                bytesPerSec = bytesProcessatiTotali / trascorsi.TotalSeconds
            End If
            If bytesPerSec <= 0 Then
                Return "Sconosciuto"
            End If
            Dim rimanente As TimeSpan = TimeSpan.FromSeconds(Math.Max(0, lavoro.TotaleBytes - bytesProcessatiTotali) / bytesPerSec)
            Return $"{CInt(Math.Floor(rimanente.TotalHours))}h {rimanente.Minutes}m {rimanente.Seconds}s"
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "Errore"
        End Try
    End Function

    Private Function OttieniStatoBatteria() As String
        Try
            Dim p As PowerStatus = SystemInformation.PowerStatus
             Return $"Batteria: {CInt(p.BatteryLifePercent * 100)} %" & If(p.PowerLineStatus = PowerLineStatus.Online, " In Carica", "")
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
            Return "Stato batteria non disponibile"
        End Try
    End Function

    Private Sub MostraInfoBatteria()
        Try
            ' Lo stesso messaggio delle barre di stato: percentuale, carica e tempo.
            WGStatoDispositivo.MostraBatteria()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

    Public Sub ControllaInternet()
        Try
             Dim msg As New WGMessaggi()
            msg.Messaggio("Connessione Internet: " & If(My.Computer.Network.IsAvailable, "ATTIVA", "ASSENTE"))
            msg.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
    End Sub

End Class