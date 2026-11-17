Imports System.IO
Imports System.Collections
Imports System.Net
Imports System.Threading
Imports NAudio.Wave
Imports SherpaOnnx
Imports wgcontrols

Public NotInheritable Class WGTrascrittoreLocale

    Private Shared log As New Logger()

    Public Delegate Sub AvanzamentoScaricamento(percentuale As Integer)

    Public Enum VarianteModello
        Standard = 0
        Ultra = 1
        Completo = 2
        Cohere = 3
    End Enum

    Public Const ESITO_ANNULLATO As String = "annullato"

    Private Const NOME_CARTELLA_MODELLI As String = "modelli"
    Private Const NOME_FILE_VARIANTE As String = "parakeet_variante.txt"
    Private Const FILE_TOKEN As String = "tokens.txt"
    Private Const FILE_LIBRERIA_SHERPA As String = "sherpa-onnx-c-api.dll"
    Private Const FILE_ONNXRUNTIME As String = "onnxruntime.dll"
    Private Const VERSIONE_MINIMA_ONNXRUNTIME_MAGGIORE As Integer = 1
    Private Const VERSIONE_MINIMA_ONNXRUNTIME_MINORE As Integer = 28
    Private Const FREQUENZA_CAMPIONAMENTO As Integer = 16000
    Private Const DIMENSIONE_CARATTERISTICHE As Integer = 128
    Private Const PROCESSORI_MINIMI As Integer = 4
    Private Const MEGABYTE As Long = 1048576L
    Private Const MEMORIA_MINIMA As Long = 6144L * MEGABYTE
    Private Const TOLLERANZA_MEMORIA As Long = 1024L * MEGABYTE
    Private Const SECONDI_BLOCCO_OBIETTIVO As Integer = 25
    Private Const SECONDI_BLOCCO_MINIMO As Integer = 18
    Private Const SECONDI_BLOCCO_MASSIMO As Integer = 30
    Private Const CAMPIONI_FINESTRA_TAGLIO As Integer = FREQUENZA_CAMPIONAMENTO \ 10
    Private Const MINUTI_RILASCIO_INATTIVITA As Integer = 10
    Private Const DIMENSIONE_BUFFER_SCARICAMENTO As Integer = 81920
    Private Const MILLISECONDI_TIMEOUT_RISPOSTA As Integer = 60000
    Private Const MILLISECONDI_TIMEOUT_LETTURA As Integer = 120000

    Private NotInheritable Class DatiVariante
        Public Property Nome As String
        Public Property Descrizione As String
        Public Property DescrizioneSpazio As String
        Public Property Cartella As String
        Public Property IndirizzoBase As String
        Public Property FileEncoder As String
        Public Property FileDecoder As String
        Public Property FileJoiner As String
        Public Property NomiFile As String()
        Public Property Motore As String = "transducer"
        Public Property IndirizzoArchivio As String = ""
        Public Property CartellaArchivio As String = ""
        Public Property Lingua As String = ""
        Public Property DimensioniAttese As Long()
        Public Property DimensioniMinime As Long()
        Public Property MemoriaMinima As Long
        Public Property MemoriaConsigliata As Long
        Public Property SpazioMinimo As Long
    End Class

    Private Shared ReadOnly _varianti As DatiVariante() = {
        New DatiVariante With {
            .Nome = "Parakeet standard",
            .Descrizione = "circa 670 megabyte",
            .DescrizioneSpazio = "1,5 gigabyte",
            .Cartella = "parakeet",
            .IndirizzoBase = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/",
            .FileEncoder = "encoder.int8.onnx",
            .FileDecoder = "decoder.int8.onnx",
            .FileJoiner = "joiner.int8.onnx",
            .NomiFile = {"encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", FILE_TOKEN},
            .DimensioniAttese = {652184281L, 11845275L, 6355277L, 93939L},
            .DimensioniMinime = {600L * MEGABYTE, 10L * MEGABYTE, 5L * MEGABYTE, 50000L},
            .MemoriaMinima = 6144L * MEGABYTE,
            .SpazioMinimo = 1536L * MEGABYTE},
        New DatiVariante With {
            .Nome = "Parakeet Ultra",
            .Descrizione = "circa 650 megabyte",
            .DescrizioneSpazio = "1,5 gigabyte",
            .Cartella = "parakeet-ultra",
            .IndirizzoBase = "https://huggingface.co/mldecode/parakeet-ultra-onnx-int8/resolve/main/",
            .FileEncoder = "encoder.int8.onnx",
            .FileDecoder = "decoder.int8.onnx",
            .FileJoiner = "joiner.int8.onnx",
            .NomiFile = {"encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", FILE_TOKEN},
            .DimensioniAttese = {650L * MEGABYTE, 12L * MEGABYTE, 6L * MEGABYTE, 94000L},
            .DimensioniMinime = {450L * MEGABYTE, 2L * MEGABYTE, 1L * MEGABYTE, 50000L},
            .MemoriaMinima = 6144L * MEGABYTE,
            .SpazioMinimo = 1536L * MEGABYTE},
        New DatiVariante With {
            .Nome = "Parakeet completo",
            .Descrizione = "circa 2,6 gigabyte",
            .DescrizioneSpazio = "4 gigabyte",
            .Cartella = "parakeet-completo",
            .IndirizzoBase = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3/resolve/main/",
            .FileEncoder = "encoder.onnx",
            .FileDecoder = "decoder.onnx",
            .FileJoiner = "joiner.onnx",
            .NomiFile = {"encoder.onnx", "encoder.weights", "decoder.onnx", "joiner.onnx", FILE_TOKEN},
            .DimensioniAttese = {5L * MEGABYTE, 2400L * MEGABYTE, 45L * MEGABYTE, 25L * MEGABYTE, 94000L},
            .DimensioniMinime = {100000L, 2000L * MEGABYTE, 5L * MEGABYTE, 2L * MEGABYTE, 50000L},
            .MemoriaMinima = 8192L * MEGABYTE,
            .MemoriaConsigliata = 12288L * MEGABYTE,
            .SpazioMinimo = 4096L * MEGABYTE},
        New DatiVariante With {
            .Nome = "Cohere Transcribe",
            .Descrizione = "circa 2,7 gigabyte",
            .DescrizioneSpazio = "6 gigabyte",
            .Cartella = "cohere-transcribe",
            .IndirizzoBase = "",
            .IndirizzoArchivio = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-cohere-transcribe-14-lang-int8-2026-04-01.tar.bz2",
            .CartellaArchivio = "sherpa-onnx-cohere-transcribe-14-lang-int8-2026-04-01",
            .Motore = "cohere",
            .Lingua = "it",
            .FileEncoder = "encoder.int8.onnx",
            .FileDecoder = "decoder.int8.onnx",
            .FileJoiner = "",
            .NomiFile = {"encoder.int8.onnx", "encoder.int8.onnx.data", "decoder.int8.onnx", FILE_TOKEN},
            .DimensioniAttese = {2400L * MEGABYTE},
            .DimensioniMinime = {1L * MEGABYTE, 2000L * MEGABYTE, 100L * MEGABYTE, 100000L},
            .MemoriaMinima = 8192L * MEGABYTE,
            .MemoriaConsigliata = 12288L * MEGABYTE,
            .SpazioMinimo = 6144L * MEGABYTE}
    }

    Private Const MINUTI_MASSIMI_ESTRAZIONE As Integer = 30

    Private Shared ReadOnly _bloccoVariante As New Object()
    Private Shared _varianteCaricata As Boolean = False
    Private Shared _variante As VarianteModello = VarianteModello.Standard

    Private Shared ReadOnly _bloccoRiconoscitore As New Object()
    Private Shared ReadOnly _bloccoLibrerie As New Object()
    Private Shared _librerieCaricate As Boolean = False
    Private Shared _mantieniInMemoria As Boolean = False

    Public Shared Property MantieniInMemoria As Boolean
        Get
            Return _mantieniInMemoria
        End Get
        Set(valore As Boolean)
            Try
                _mantieniInMemoria = valore
                If Not valore Then ProgrammaRilascio()
            Catch ex As Exception
                log.ScriviLog($"WGTrascrittoreLocale.MantieniInMemoria: {ex.Message}")
            End Try
        End Set
    End Property
    Private Shared _riconoscitore As OfflineRecognizer
    Private Shared _timerRilascio As Timer
    Private Shared _supportoVerificato As Boolean = False
    Private Shared _supportato As Boolean = False
    Private Shared _motivoNonSupportato As String = ""

    Private Const LOAD_WITH_ALTERED_SEARCH_PATH As UInteger = &H8UI

    <Runtime.InteropServices.DllImport("kernel32.dll", CharSet:=Runtime.InteropServices.CharSet.Unicode, SetLastError:=True)>
    Private Shared Function LoadLibraryEx(lpFileName As String, hFile As IntPtr, dwFlags As UInteger) As IntPtr
    End Function

    Private Sub New()
    End Sub

    Private Shared Function TrovaLibreriaNativa(nomeFile As String) As String
        Try
            Dim cartellaProgramma As String = AppDomain.CurrentDomain.BaseDirectory
            Dim cartelle As String() = {cartellaProgramma, Path.Combine(cartellaProgramma, "runtimes", "win-x64", "native"), Path.Combine(cartellaProgramma, "x64")}
            For Each cartella As String In cartelle
                Dim percorso As String = Path.Combine(cartella, nomeFile)
                If File.Exists(percorso) Then Return percorso
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.TrovaLibreriaNativa: {ex.Message}")
            Return ""
        End Try
    End Function

    Public Shared Function PrecaricaLibrerie() As Boolean
        Try
            SyncLock _bloccoLibrerie
                If _librerieCaricate Then Return True
                Dim percorsoOnnx As String = TrovaLibreriaNativa(FILE_ONNXRUNTIME)
                Dim percorsoSherpa As String = TrovaLibreriaNativa(FILE_LIBRERIA_SHERPA)
                If String.IsNullOrEmpty(percorsoOnnx) OrElse String.IsNullOrEmpty(percorsoSherpa) Then Return False
                If LoadLibraryEx(percorsoOnnx, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) = IntPtr.Zero Then Return False
                If LoadLibraryEx(percorsoSherpa, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) = IntPtr.Zero Then Return False
                _librerieCaricate = True
                Return True
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.PrecaricaLibrerie: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Private Shared Function Dati(variante As VarianteModello) As DatiVariante
        Try
            Dim indice As Integer = CInt(variante)
            If indice < 0 OrElse indice >= _varianti.Length Then indice = 0
            Return _varianti(indice)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.Dati: {ex.Message}")
            Return _varianti(0)
        End Try
    End Function

    Private Shared Function PercorsoFileVariante() As String
        Try
            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NOME_CARTELLA_MODELLI, NOME_FILE_VARIANTE)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.PercorsoFileVariante: {ex.Message}")
            Return NOME_FILE_VARIANTE
        End Try
    End Function

    Public Shared Property VarianteScelta As VarianteModello
        Get
            Try
                SyncLock _bloccoVariante
                    If Not _varianteCaricata Then
                        _varianteCaricata = True
                        Dim percorso As String = PercorsoFileVariante()
                        If File.Exists(percorso) Then
                            Dim valore As Integer
                            If Integer.TryParse(File.ReadAllText(percorso).Trim(), valore) AndAlso [Enum].IsDefined(GetType(VarianteModello), valore) Then
                                _variante = CType(valore, VarianteModello)
                            Else
                                log.ScriviLog($"WGTrascrittoreLocale.VarianteScelta: contenuto non valido in {percorso}, uso la versione standard")
                            End If
                        End If
                    End If
                    Return _variante
                End SyncLock
            Catch ex As Exception
                log.ScriviLog($"WGTrascrittoreLocale.VarianteScelta (lettura): {ex.Message}{Environment.NewLine}{ex.StackTrace}")
                Return VarianteModello.Standard
            End Try
        End Get
        Set(valore As VarianteModello)
            Try
                If Not [Enum].IsDefined(GetType(VarianteModello), valore) Then valore = VarianteModello.Standard
                Dim cambiata As Boolean
                SyncLock _bloccoVariante
                    cambiata = (Not _varianteCaricata) OrElse _variante <> valore
                    _variante = valore
                    _varianteCaricata = True
                    Dim percorso As String = PercorsoFileVariante()
                    Directory.CreateDirectory(Path.GetDirectoryName(percorso))
                    File.WriteAllText(percorso, CInt(valore).ToString(Globalization.CultureInfo.InvariantCulture))
                End SyncLock
                If cambiata Then Rilascia()
            Catch ex As Exception
                log.ScriviLog($"WGTrascrittoreLocale.VarianteScelta (scrittura): {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            End Try
        End Set
    End Property

    Public Shared Function NomeVariante(variante As VarianteModello) As String
        Try
            Return Dati(variante).Nome
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.NomeVariante: {ex.Message}")
            Return ""
        End Try
    End Function

    Public Shared Function DescrizioneVariante(variante As VarianteModello) As String
        Try
            Return Dati(variante).Descrizione
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.DescrizioneVariante: {ex.Message}")
            Return ""
        End Try
    End Function

    Public Shared ReadOnly Property DESCRIZIONE_DIMENSIONE As String
        Get
            Return DescrizioneVariante(VarianteScelta)
        End Get
    End Property

    Public Shared ReadOnly Property DescrizioneSpazioNecessario As String
        Get
            Try
                Return Dati(VarianteScelta).DescrizioneSpazio
            Catch ex As Exception
                log.ScriviLog($"WGTrascrittoreLocale.DescrizioneSpazioNecessario: {ex.Message}")
                Return ""
            End Try
        End Get
    End Property

    Public Shared Function MemoriaSufficiente(variante As VarianteModello) As Boolean
        Try
            Dim memoria As ULong = New Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory
            Return memoria >= CULng(Dati(variante).MemoriaMinima - TOLLERANZA_MEMORIA)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.MemoriaSufficiente: {ex.Message}")
            Return False
        End Try
    End Function

    Public Shared Function MemoriaConsigliataRaggiunta(variante As VarianteModello) As Boolean
        Try
            Dim consigliata As Long = Dati(variante).MemoriaConsigliata
            If consigliata <= 0 Then Return True
            Dim memoria As ULong = New Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory
            Return memoria >= CULng(consigliata - TOLLERANZA_MEMORIA)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.MemoriaConsigliataRaggiunta: {ex.Message}")
            Return True
        End Try
    End Function

    Public Shared Function DescrizioneMemoriaConsigliata(variante As VarianteModello) As String
        Try
            Dim consigliata As Long = Dati(variante).MemoriaConsigliata
            If consigliata <= 0 Then Return ""
            Return (consigliata \ (1024L * MEGABYTE)).ToString(Globalization.CultureInfo.InvariantCulture) & " gigabyte"
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.DescrizioneMemoriaConsigliata: {ex.Message}")
            Return ""
        End Try
    End Function

    Public Shared Function DescrizioneMemoriaMinima(variante As VarianteModello) As String
        Try
            Return (Dati(variante).MemoriaMinima \ (1024L * MEGABYTE)).ToString(Globalization.CultureInfo.InvariantCulture) & " gigabyte"
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.DescrizioneMemoriaMinima: {ex.Message}")
            Return ""
        End Try
    End Function

    Public Shared Function CartellaModello() As String
        Try
            Return CartellaModello(VarianteScelta)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.CartellaModello: {ex.Message}")
            Return Path.Combine(NOME_CARTELLA_MODELLI, _varianti(0).Cartella)
        End Try
    End Function

    Public Shared Function CartellaModello(variante As VarianteModello) As String
        Try
            Return Path.Combine(WGDb.WGPercorsiApplicazione.CartellaModelli(), Dati(variante).Cartella)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.CartellaModello (variante): {ex.Message}")
            Return Path.Combine(NOME_CARTELLA_MODELLI, _varianti(0).Cartella)
        End Try
    End Function

    Private Shared Function PercorsoFile(variante As VarianteModello, nomeFile As String) As String
        Try
            Return Path.Combine(CartellaModello(variante), nomeFile)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.PercorsoFile: {ex.Message}")
            Return nomeFile
        End Try
    End Function

    Private Shared Function FileCompleto(percorso As String, dimensioneMinima As Long) As Boolean
        Try
            Return File.Exists(percorso) AndAlso New FileInfo(percorso).Length >= dimensioneMinima
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.FileCompleto: {ex.Message}")
            Return False
        End Try
    End Function

    Public Shared Function ModelloPresente() As Boolean
        Try
            Return ModelloPresente(VarianteScelta)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ModelloPresente: {ex.Message}")
            Return False
        End Try
    End Function

    Public Shared Function ModelloPresente(variante As VarianteModello) As Boolean
        Try
            Dim datiVariante As DatiVariante = Dati(variante)
            For indice As Integer = 0 To datiVariante.NomiFile.Length - 1
                If Not FileCompleto(PercorsoFile(variante, datiVariante.NomiFile(indice)), datiVariante.DimensioniMinime(indice)) Then Return False
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ModelloPresente (variante): {ex.Message}")
            Return False
        End Try
    End Function

    Public Shared Function ModelloUtilizzabile() As Boolean
        Try
            Dim motivo As String = ""
            Return SistemaSupportato(motivo) AndAlso VariantiDaProvare().Count > 0
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ModelloUtilizzabile: {ex.Message}")
            Return False
        End Try
    End Function

    Public Shared Function SistemaSupportato(ByRef motivo As String) As Boolean
        motivo = ""
        Try
            If _supportoVerificato Then
                motivo = _motivoNonSupportato
                Return _supportato
            End If

            Dim esito As Boolean = VerificaSistema(motivo)
            _motivoNonSupportato = motivo
            _supportato = esito
            _supportoVerificato = True
            Return esito
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.SistemaSupportato: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            motivo = "non è stato possibile verificare le caratteristiche del computer"
            Return False
        End Try
    End Function

    Private Shared Function VerificaSistema(ByRef motivo As String) As Boolean
        motivo = ""
        Try
            If Not Environment.Is64BitProcess Then
                motivo = "il riconoscimento vocale locale richiede Windows a 64 bit"
                Return False
            End If

            If Environment.ProcessorCount < PROCESSORI_MINIMI Then
                motivo = "il processore deve avere almeno " & PROCESSORI_MINIMI.ToString(Globalization.CultureInfo.InvariantCulture) & " nuclei"
                Return False
            End If

            Dim memoria As ULong = New Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory
            If memoria < CULng(MEMORIA_MINIMA - 256L * MEGABYTE) Then
                motivo = "il computer deve avere almeno 6 gigabyte di memoria"
                Return False
            End If

            If String.IsNullOrEmpty(TrovaLibreriaNativa(FILE_LIBRERIA_SHERPA)) Then
                motivo = "manca la libreria " & FILE_LIBRERIA_SHERPA & " nella cartella del programma"
                Return False
            End If

            Dim percorsoOnnx As String = TrovaLibreriaNativa(FILE_ONNXRUNTIME)
            If String.IsNullOrEmpty(percorsoOnnx) Then
                motivo = "manca la libreria " & FILE_ONNXRUNTIME & " nella cartella del programma"
                Return False
            End If

            Dim informazioni As Diagnostics.FileVersionInfo = Diagnostics.FileVersionInfo.GetVersionInfo(percorsoOnnx)
            If informazioni.FileMajorPart < VERSIONE_MINIMA_ONNXRUNTIME_MAGGIORE OrElse (informazioni.FileMajorPart = VERSIONE_MINIMA_ONNXRUNTIME_MAGGIORE AndAlso informazioni.FileMinorPart < VERSIONE_MINIMA_ONNXRUNTIME_MINORE) Then
                motivo = "la libreria " & FILE_ONNXRUNTIME & " del programma è in versione " & informazioni.FileVersion & ", serve almeno la " & VERSIONE_MINIMA_ONNXRUNTIME_MAGGIORE.ToString(Globalization.CultureInfo.InvariantCulture) & "." & VERSIONE_MINIMA_ONNXRUNTIME_MINORE.ToString(Globalization.CultureInfo.InvariantCulture)
                Return False
            End If

            If Not PrecaricaLibrerie() Then
                motivo = "non è stato possibile caricare le librerie del riconoscimento vocale locale"
                Return False
            End If

            Return True
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.VerificaSistema: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            motivo = "non è stato possibile verificare le caratteristiche del computer"
            Return False
        End Try
    End Function

    Public Shared Function SpazioLiberoSufficiente() As Boolean
        Try
            Dim radice As String = Path.GetPathRoot(Path.GetFullPath(WGDb.WGPercorsiApplicazione.CartellaModelli()))
            If String.IsNullOrEmpty(radice) Then Return True
            Dim unita As New DriveInfo(radice)
            If Not unita.IsReady Then Return True
            Return unita.AvailableFreeSpace >= Dati(VarianteScelta).SpazioMinimo
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.SpazioLiberoSufficiente: {ex.Message}")
            Return True
        End Try
    End Function

    ''' <summary>
    ''' Avvia nel centro di controllo lo scaricamento del modello della versione scelta, con la percentuale e senza attese. Restituisce ""
    ''' se è partito (o se il modello c'è già), altrimenti il motivo per cui non può partire. Alla fine alTermine riceve "" se il modello
    ''' è pronto, "annullato" se si è annullato, altrimenti il motivo del fallimento.
    ''' </summary>
    Public Shared Function ScaricaModello(alTermine As Action(Of String)) As String
        Try
            Dim variante As VarianteModello = VarianteScelta
            Dim datiVariante As DatiVariante = Dati(variante)
            If Not MemoriaSufficiente(variante) Then Return "La versione " & datiVariante.Nome & " richiede almeno " & DescrizioneMemoriaMinima(variante) & " di memoria."
            If ModelloPresente(variante) Then
                If alTermine IsNot Nothing Then alTermine("")
                Return ""
            End If

            Dim cartella As String = CartellaModello(variante)
            Dim elementi As New List(Of ElementoModello)()
            Dim daArchivio As Boolean = Not String.IsNullOrEmpty(datiVariante.IndirizzoArchivio)
            If daArchivio Then
                If Not File.Exists(Path.Combine(Environment.SystemDirectory, "tar.exe")) Then Return "Per estrarre il modello " & datiVariante.Nome & " serve tar.exe, presente in Windows 10 dalla versione 1803 e in Windows 11, che su questo computer manca."
                elementi.Add(New ElementoModello() With {.Indirizzo = datiVariante.IndirizzoArchivio, .NomeFile = "modello.tar.bz2", .DimensioneMinima = 100L * MEGABYTE, .Estrazione = TipoEstrazioneModello.TarBz2})
            Else
                For indice As Integer = 0 To datiVariante.NomiFile.Length - 1
                    If FileCompleto(PercorsoFile(variante, datiVariante.NomiFile(indice)), datiVariante.DimensioniMinime(indice)) Then Continue For
                    elementi.Add(New ElementoModello() With {.Indirizzo = datiVariante.IndirizzoBase & datiVariante.NomiFile(indice) & "?download=true", .NomeFile = datiVariante.NomiFile(indice), .DimensioneMinima = datiVariante.DimensioniMinime(indice)})
                Next
            End If

            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Dim installa As Func(Of String, String) =
                Function(temporanea As String) As String
                    Return InstallaModello(variante, temporanea, daArchivio)
                End Function
            WGCentroDiControllo.GetInstance().AggiungiScaricamentoModelli("trascrittore-" & variante.ToString(), "modello vocale locale " & datiVariante.Nome, elementi, installa, alTermine)
            Return ""
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ScaricaModello: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Scaricamento del modello vocale locale non riuscito: " & ex.Message
        End Try
    End Function

    ' Mette al loro posto i file scaricati (o estratti) nella cartella temporanea. Restituisce "" se è riuscito.
    Private Shared Function InstallaModello(variante As VarianteModello, temporanea As String, daArchivio As Boolean) As String
        Try
            Dim datiVariante As DatiVariante = Dati(variante)
            Dim cartella As String = CartellaModello(variante)
            Directory.CreateDirectory(cartella)
            For Each nomeFile As String In datiVariante.NomiFile
                Dim origine As String = If(daArchivio, Path.Combine(temporanea, "modello", datiVariante.CartellaArchivio, nomeFile), Path.Combine(temporanea, nomeFile))
                Dim destinazione As String = Path.Combine(cartella, nomeFile)
                If Not File.Exists(origine) Then
                    If File.Exists(destinazione) Then Continue For
                    log.ScriviLog($"WGTrascrittoreLocale.InstallaModello: manca il file {nomeFile}")
                    Return "Nel modello " & datiVariante.Nome & " manca il file " & nomeFile & "."
                End If
                If File.Exists(destinazione) Then File.Delete(destinazione)
                File.Move(origine, destinazione)
            Next
            If Not ModelloPresente(variante) Then Return "Il modello vocale locale " & datiVariante.Nome & " non risulta scaricato correttamente."
            Return ""
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.InstallaModello: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return "Installazione del modello vocale locale non riuscita: " & ex.Message
        End Try
    End Function

    Public Shared Function EliminaModello() As Boolean
        Try
            Return EliminaModello(VarianteScelta)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.EliminaModello: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function EliminaModello(variante As VarianteModello) As Boolean
        Try
            If variante = VarianteScelta Then Rilascia()
            Dim datiVariante As DatiVariante = Dati(variante)
            Dim cartella As String = CartellaModello(variante)
            For Each nomeFile As String In datiVariante.NomiFile
                Dim percorso As String = Path.Combine(cartella, nomeFile)
                If File.Exists(percorso) Then File.Delete(percorso)
            Next
            Return Not ModelloPresente(variante)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.EliminaModello (variante): {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function EliminaVariantiNonInUso() As Boolean
        Try
            Dim tutteEliminate As Boolean = True
            Dim attiva As VarianteModello = VarianteScelta
            For Each variante As VarianteModello In [Enum].GetValues(GetType(VarianteModello))
                If variante = attiva Then Continue For
                If Not EliminaModello(variante) Then tutteEliminate = False
            Next
            Return tutteEliminate
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.EliminaVariantiNonInUso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

    Public Shared Function AltreVariantiPresenti() As Boolean
        Try
            Dim attiva As VarianteModello = VarianteScelta
            For Each variante As VarianteModello In [Enum].GetValues(GetType(VarianteModello))
                If variante <> attiva AndAlso ModelloPresente(variante) Then Return True
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.AltreVariantiPresenti: {ex.Message}")
            Return False
        End Try
    End Function

    Private Shared Function VariantiDaProvare() As List(Of VarianteModello)
        Dim elenco As New List(Of VarianteModello)
        Try
            Dim ordine As VarianteModello() = {VarianteScelta, VarianteModello.Standard, VarianteModello.Ultra, VarianteModello.Completo, VarianteModello.Cohere}
            For Each variante As VarianteModello In ordine
                If elenco.Contains(variante) Then Continue For
                If ModelloPresente(variante) AndAlso MemoriaSufficiente(variante) Then elenco.Add(variante)
            Next
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.VariantiDaProvare: {ex.Message}")
        End Try
        Return elenco
    End Function

    Private Shared Function ImpostaMembro(oggetto As Object, nomi As String(), valore As Object) As Boolean
        Try
            Dim tipo As Type = oggetto.GetType()
            For Each nome As String In nomi
                Dim campo As Reflection.FieldInfo = tipo.GetField(nome, Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance)
                If campo IsNot Nothing Then
                    campo.SetValue(oggetto, Convert.ChangeType(valore, campo.FieldType, Globalization.CultureInfo.InvariantCulture))
                    Return True
                End If
                Dim proprieta As Reflection.PropertyInfo = tipo.GetProperty(nome, Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance)
                If proprieta IsNot Nothing AndAlso proprieta.CanWrite Then
                    proprieta.SetValue(oggetto, Convert.ChangeType(valore, proprieta.PropertyType, Globalization.CultureInfo.InvariantCulture), Nothing)
                    Return True
                End If
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ImpostaMembro ({String.Join("/", nomi)}): {ex.Message}")
            Return False
        End Try
    End Function

    Private Shared Function LeggiMembro(oggetto As Object, nome As String) As Object
        Dim tipo As Type = oggetto.GetType()
        Dim campo As Reflection.FieldInfo = tipo.GetField(nome, Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance)
        If campo IsNot Nothing Then Return campo.GetValue(oggetto)
        Dim proprieta As Reflection.PropertyInfo = tipo.GetProperty(nome, Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance)
        If proprieta IsNot Nothing Then Return proprieta.GetValue(oggetto, Nothing)
        Return Nothing
    End Function

    Private Shared Function CreaRiconoscitoreCohere(variante As VarianteModello) As OfflineRecognizer
        Dim datiVariante As DatiVariante = Dati(variante)
        Dim configurazione As New OfflineRecognizerConfig()
        configurazione.FeatConfig.SampleRate = FREQUENZA_CAMPIONAMENTO
        configurazione.FeatConfig.FeatureDim = DIMENSIONE_CARATTERISTICHE
        configurazione.ModelConfig.Tokens = PercorsoFile(variante, FILE_TOKEN)
        configurazione.ModelConfig.NumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount \ 2))
        configurazione.ModelConfig.Debug = 0
        configurazione.ModelConfig.Provider = "cpu"
        configurazione.DecodingMethod = "greedy_search"

        Dim modello As Object = configurazione.ModelConfig
        Dim cohere As Object = LeggiMembro(modello, "CohereTranscribe")
        If cohere Is Nothing Then Throw New NotSupportedException("la libreria sherpa-onnx del programma non supporta Cohere Transcribe, serve una versione più recente")
        If Not ImpostaMembro(cohere, New String() {"Encoder"}, PercorsoFile(variante, datiVariante.FileEncoder)) Then Throw New NotSupportedException("impossibile impostare l'encoder di Cohere Transcribe")
        If Not ImpostaMembro(cohere, New String() {"Decoder"}, PercorsoFile(variante, datiVariante.FileDecoder)) Then Throw New NotSupportedException("impossibile impostare il decoder di Cohere Transcribe")
        If Not ImpostaMembro(cohere, New String() {"Language"}, datiVariante.Lingua) Then Throw New NotSupportedException("impossibile impostare la lingua di Cohere Transcribe")
        ImpostaMembro(cohere, New String() {"UsePunct", "UsePunctuation"}, 1)
        ImpostaMembro(cohere, New String() {"UseInverseTextNormalization", "UseItn"}, 1)

        Dim tipoModello As Type = modello.GetType()
        Dim campoCohere As Reflection.FieldInfo = tipoModello.GetField("CohereTranscribe", Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance)
        If campoCohere IsNot Nothing Then
            campoCohere.SetValue(modello, cohere)
        Else
            tipoModello.GetProperty("CohereTranscribe", Reflection.BindingFlags.Public Or Reflection.BindingFlags.Instance).SetValue(modello, cohere, Nothing)
        End If
        configurazione.ModelConfig = DirectCast(modello, OfflineModelConfig)
        Return New OfflineRecognizer(configurazione)
    End Function

    Private Shared Function CreaRiconoscitore(variante As VarianteModello) As OfflineRecognizer
        Dim datiVariante As DatiVariante = Dati(variante)
        If datiVariante.Motore = "cohere" Then Return CreaRiconoscitoreCohere(variante)
        Dim configurazione As New OfflineRecognizerConfig()
        configurazione.FeatConfig.SampleRate = FREQUENZA_CAMPIONAMENTO
        configurazione.FeatConfig.FeatureDim = DIMENSIONE_CARATTERISTICHE
        configurazione.ModelConfig.Transducer.Encoder = PercorsoFile(variante, datiVariante.FileEncoder)
        configurazione.ModelConfig.Transducer.Decoder = PercorsoFile(variante, datiVariante.FileDecoder)
        configurazione.ModelConfig.Transducer.Joiner = PercorsoFile(variante, datiVariante.FileJoiner)
        configurazione.ModelConfig.Tokens = PercorsoFile(variante, FILE_TOKEN)
        configurazione.ModelConfig.NumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount \ 2))
        configurazione.ModelConfig.Debug = 0
        configurazione.ModelConfig.Provider = "cpu"
        configurazione.ModelConfig.ModelType = "nemo_transducer"
        configurazione.DecodingMethod = "greedy_search"
        configurazione.MaxActivePaths = 4
        Return New OfflineRecognizer(configurazione)
    End Function

    Private Shared Function AssicuraRiconoscitore(ByRef errore As String) As Boolean
        errore = ""
        Try
            If _riconoscitore IsNot Nothing Then Return True

            Dim motivo As String = ""
            If Not SistemaSupportato(motivo) Then
                errore = "Il riconoscimento vocale locale non è disponibile: " & motivo & "."
                Return False
            End If

            Dim scelta As VarianteModello = VarianteScelta
            Dim varianti As List(Of VarianteModello) = VariantiDaProvare()
            If varianti.Count = 0 Then
                If Not ModelloPresente(scelta) Then
                    errore = "Il modello vocale locale " & Dati(scelta).Nome & " non è stato scaricato."
                Else
                    errore = "La versione " & Dati(scelta).Nome & " richiede almeno " & DescrizioneMemoriaMinima(scelta) & " di memoria."
                End If
                Return False
            End If

            If varianti(0) <> scelta Then
                log.ScriviLog($"WGTrascrittoreLocale.AssicuraRiconoscitore: la versione {Dati(scelta).Nome} non è scaricata o non entra in memoria, uso {Dati(varianti(0)).Nome}")
            End If

            For Each variante As VarianteModello In varianti
                Try
                    _riconoscitore = CreaRiconoscitore(variante)
                    If variante <> varianti(0) Then log.ScriviLog($"WGTrascrittoreLocale.AssicuraRiconoscitore: caricata la versione {Dati(variante).Nome} al posto di {Dati(varianti(0)).Nome}")
                    Return True
                Catch exVariante As Exception
                    log.ScriviLog($"WGTrascrittoreLocale.AssicuraRiconoscitore ({Dati(variante).Nome}): {exVariante.Message}{Environment.NewLine}{exVariante.StackTrace}")
                    errore = "Non è stato possibile caricare il modello vocale locale " & Dati(variante).Nome & ": " & exVariante.Message
                    _riconoscitore = Nothing
                End Try
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.AssicuraRiconoscitore: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            errore = "Non è stato possibile caricare il modello vocale locale: " & ex.Message
            _riconoscitore = Nothing
            Return False
        End Try
    End Function

    Public Shared Function Precarica(ByRef errore As String) As Boolean
        errore = ""
        Try
            SyncLock _bloccoRiconoscitore
                Dim esito As Boolean = AssicuraRiconoscitore(errore)
                If esito Then ProgrammaRilascio()
                Return esito
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.Precarica: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            errore = "Non è stato possibile caricare il modello vocale locale: " & ex.Message
            Return False
        End Try
    End Function

    Public Shared Function TrascriviPcm16(audio As Byte(), frequenza As Integer, ByRef errore As String) As String
        errore = ""
        Try
            If audio Is Nothing OrElse audio.Length < 2 Then Return ""
            Dim campioni((audio.Length \ 2) - 1) As Single
            For indice As Integer = 0 To campioni.Length - 1
                campioni(indice) = BitConverter.ToInt16(audio, indice * 2) / 32768.0F
            Next
            Return TrascriviCampioni(campioni, frequenza, errore)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.TrascriviPcm16: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            errore = "Errore del riconoscimento vocale locale: " & ex.Message
            Return ""
        End Try
    End Function

    Public Shared Function TrascriviPcm16ConTempi(audio As Byte(), frequenza As Integer, ByRef errore As String, ByRef parole As List(Of Tuple(Of String, Single))) As String
        errore = ""
        parole = New List(Of Tuple(Of String, Single))
        Try
            If audio Is Nothing OrElse audio.Length < 2 Then Return ""
            If frequenza <= 0 Then frequenza = FREQUENZA_CAMPIONAMENTO
            Dim campioni((audio.Length \ 2) - 1) As Single
            For indice As Integer = 0 To campioni.Length - 1
                campioni(indice) = BitConverter.ToInt16(audio, indice * 2) / 32768.0F
            Next
            If campioni.Length > SECONDI_BLOCCO_MASSIMO * frequenza Then Return TrascriviCampioni(campioni, frequenza, errore)

            SyncLock _bloccoRiconoscitore
                If Not AssicuraRiconoscitore(errore) Then Return ""
                ProgrammaRilascio()
                Using flusso As OfflineStream = _riconoscitore.CreateStream()
                    flusso.AcceptWaveform(frequenza, campioni)
                    _riconoscitore.Decode(flusso)
                    Dim risultato As OfflineRecognizerResult = flusso.Result
                    If risultato Is Nothing Then Return ""
                    parole = ParoleConTempi(risultato)
                    ProgrammaRilascio()
                    Return CorreggiTrascrizione(If(risultato.Text, "").Trim())
                End Using
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.TrascriviPcm16ConTempi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            errore = "Errore del riconoscimento vocale locale: " & ex.Message
            Return ""
        End Try
    End Function

    Private Shared Function ParoleConTempi(risultato As Object) As List(Of Tuple(Of String, Single))
        Dim parole As New List(Of Tuple(Of String, Single))
        Try
            Dim pezzi As IEnumerable = TryCast(LeggiMembro(risultato, "Tokens"), IEnumerable)
            Dim tempi As IEnumerable = TryCast(LeggiMembro(risultato, "Timestamps"), IEnumerable)
            If pezzi Is Nothing OrElse tempi Is Nothing Then Return parole
            Dim elencoPezzi As New List(Of String)
            For Each pezzo As Object In pezzi
                elencoPezzi.Add(Convert.ToString(pezzo, Globalization.CultureInfo.InvariantCulture))
            Next
            Dim elencoTempi As New List(Of Single)
            For Each tempo As Object In tempi
                elencoTempi.Add(Convert.ToSingle(tempo, Globalization.CultureInfo.InvariantCulture))
            Next
            If elencoPezzi.Count = 0 OrElse elencoPezzi.Count <> elencoTempi.Count Then Return parole

            Dim testoParola As New Text.StringBuilder()
            Dim inizioParola As Single = 0.0F
            For indice As Integer = 0 To elencoPezzi.Count - 1
                Dim pezzo As String = If(elencoPezzi(indice), "")
                Dim nuovaParola As Boolean = indice = 0 OrElse pezzo.StartsWith(" ", StringComparison.Ordinal) OrElse pezzo.StartsWith(ChrW(&H2581), StringComparison.Ordinal)
                Dim pulito As String = pezzo.Replace(ChrW(&H2581), " ").Trim()
                If nuovaParola AndAlso testoParola.Length > 0 Then
                    parole.Add(New Tuple(Of String, Single)(testoParola.ToString(), inizioParola))
                    testoParola.Clear()
                End If
                If testoParola.Length = 0 Then inizioParola = elencoTempi(indice)
                testoParola.Append(pulito)
            Next
            If testoParola.Length > 0 Then parole.Add(New Tuple(Of String, Single)(testoParola.ToString(), inizioParola))
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ParoleConTempi: {ex.Message}")
        End Try
        Return parole
    End Function

    Public Shared Function TrascriviFile(percorso As String, ByRef errore As String) As String
        errore = ""
        Try
            If String.IsNullOrWhiteSpace(percorso) OrElse Not File.Exists(percorso) Then
                errore = "File audio non trovato"
                Return ""
            End If

            Dim frequenza As Integer = FREQUENZA_CAMPIONAMENTO
            Dim campioni As Single() = LeggiCampioniMono(percorso, frequenza)
            If campioni Is Nothing OrElse campioni.Length = 0 Then
                errore = "Il file non contiene audio da trascrivere"
                Return ""
            End If
            Return TrascriviCampioni(campioni, frequenza, errore)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.TrascriviFile: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            errore = "Errore del riconoscimento vocale locale: " & ex.Message
            Return ""
        End Try
    End Function

    Private Shared Function LeggiCampioniMono(percorso As String, ByRef frequenza As Integer) As Single()
        Try
            Using lettore As New AudioFileReader(percorso)
                frequenza = lettore.WaveFormat.SampleRate
                Dim canali As Integer = Math.Max(1, lettore.WaveFormat.Channels)
                Dim risultato As New List(Of Single)
                Dim buffer(frequenza * canali - 1) As Single
                Dim letti As Integer = lettore.Read(buffer, 0, buffer.Length)
                While letti > 0
                    Dim posizione As Integer = 0
                    While posizione + canali <= letti
                        Dim somma As Single = 0.0F
                        For canale As Integer = 0 To canali - 1
                            somma += buffer(posizione + canale)
                        Next
                        risultato.Add(somma / canali)
                        posizione += canali
                    End While
                    letti = lettore.Read(buffer, 0, buffer.Length)
                End While
                Return risultato.ToArray()
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.LeggiCampioniMono: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New Single() {}
        End Try
    End Function

    Private Shared Function TrascriviCampioni(campioni As Single(), frequenza As Integer, ByRef errore As String) As String
        errore = ""
        Try
            If campioni Is Nothing OrElse campioni.Length = 0 Then Return ""
            If frequenza <= 0 Then frequenza = FREQUENZA_CAMPIONAMENTO

            SyncLock _bloccoRiconoscitore
                If Not AssicuraRiconoscitore(errore) Then Return ""
                ProgrammaRilascio()

                Dim testo As New Text.StringBuilder()
                For Each blocco As Single() In SuddividiCampioni(campioni, frequenza)
                    Dim parziale As String = DecodificaBlocco(blocco, frequenza)
                    If String.IsNullOrWhiteSpace(parziale) Then Continue For
                    If testo.Length > 0 Then testo.Append(" ")
                    testo.Append(parziale.Trim())
                Next

                ProgrammaRilascio()
                Return CorreggiTrascrizione(testo.ToString())
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.TrascriviCampioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            errore = "Errore del riconoscimento vocale locale: " & ex.Message
            Return ""
        End Try
    End Function

    Private Shared ReadOnly _lettereCompitazione As Dictionary(Of String, String) = CreaLettereCompitazione()
    Private Shared ReadOnly _coppieCompitazione As Dictionary(Of String, String) = CreaCoppieCompitazione()
    Private Shared ReadOnly _dominiCompitazione As New HashSet(Of String)(New String() {"gmail", "hotmail", "outlook", "libero", "yahoo", "icloud", "live", "alice", "tiscali", "virgilio", "fastwebnet", "pec", "com", "it", "net", "org", "eu", "info"}, StringComparer.Ordinal)
    Private Shared ReadOnly _paroleAmbigueCompitazione As New HashSet(Of String)(New String() {"e", "a", "i", "o", "ed", "ad"}, StringComparer.Ordinal)
    Private Shared ReadOnly _modelloCompitazione As New Text.RegularExpressions.Regex("\b(?:compit(?:azione|azioni|a|are)|competizione|compilazione|computazione|spelling|lettera\s+per\s+lettera)\b[\s,.:;]*", Text.RegularExpressions.RegexOptions.IgnoreCase Or Text.RegularExpressions.RegexOptions.CultureInvariant)
    Private Const LETTERE_MINIME_COMPITAZIONE_INCERTA As Integer = 3
    Private Shared ReadOnly _modelloParoleCompitazione As New Text.RegularExpressions.Regex("[\p{L}\p{N}]+|@|_", Text.RegularExpressions.RegexOptions.CultureInvariant)

    Private Shared Function CreaLettereCompitazione() As Dictionary(Of String, String)
        Dim lettere As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Try
            Dim coppie As String() = {
                "a=a", "bi=b", "ci=c", "di=d", "e=e", "effe=f", "gi=g", "acca=h", "i=i", "elle=l", "emme=m", "enne=n", "o=o", "pi=p", "cu=q", "erre=r", "esse=s", "ti=t", "u=u", "vu=v", "vi=v", "zeta=z", "cappa=k", "kappa=k", "ics=x", "ipsilon=y", "ypsilon=y", "gei=j",
                "ancona=a", "bologna=b", "bari=b", "como=c", "domodossola=d", "empoli=e", "firenze=f", "genova=g", "hotel=h", "imola=i", "jolly=j", "jersey=j", "kursaal=k", "livorno=l", "milano=m", "napoli=n", "otranto=o", "padova=p", "palermo=p", "quarto=q", "quadro=q", "roma=r", "savona=s", "sassari=s", "torino=t", "taranto=t", "udine=u", "venezia=v", "washington=w", "xeres=x", "xilofono=x", "yacht=y", "york=y", "yogurt=y", "zara=z",
                "alfa=a", "alpha=a", "bravo=b", "charlie=c", "delta=d", "echo=e", "eco=e", "foxtrot=f", "golf=g", "india=i", "juliett=j", "juliet=j", "kilo=k", "lima=l", "mike=m", "november=n", "oscar=o", "papa=p", "quebec=q", "romeo=r", "sierra=s", "tango=t", "uniform=u", "victor=v", "whiskey=w", "whisky=w", "xray=x", "yankee=y", "zulu=z",
                "zero=0", "uno=1", "una=1", "due=2", "tre=3", "quattro=4", "cinque=5", "sei=6", "sette=7", "otto=8", "nove=9",
                "chiocciola=@", "chiocciolina=@", "at=@", "punto=.", "trattino=-", "meno=-", "underscore=_", "sottolineato=_", "barra=/"}
            For Each coppia As String In coppie
                Dim separatore As Integer = coppia.IndexOf("="c)
                lettere(coppia.Substring(0, separatore)) = coppia.Substring(separatore + 1)
            Next
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.CreaLettereCompitazione: {ex.Message}")
        End Try
        Return lettere
    End Function

    Private Shared Function CreaCoppieCompitazione() As Dictionary(Of String, String)
        Dim coppie As New Dictionary(Of String, String)(StringComparer.Ordinal)
        Try
            coppie("doppia vu") = "w"
            coppie("doppio vu") = "w"
            coppie("vu doppia") = "w"
            coppie("i lunga") = "j"
            coppie("i greca") = "y"
            coppie("trattino basso") = "_"
            coppie("x ray") = "x"
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.CreaCoppieCompitazione: {ex.Message}")
        End Try
        Return coppie
    End Function

    Private Shared Function NormalizzaParolaCompitazione(parola As String) As String
        Try
            Dim scomposto As String = If(parola, "").ToLowerInvariant().Normalize(Text.NormalizationForm.FormD)
            Dim risultato As New Text.StringBuilder()
            For Each carattere As Char In scomposto
                If Globalization.CharUnicodeInfo.GetUnicodeCategory(carattere) <> Globalization.UnicodeCategory.NonSpacingMark Then risultato.Append(carattere)
            Next
            Return risultato.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.NormalizzaParolaCompitazione: {ex.Message}")
            Return If(parola, "").ToLowerInvariant()
        End Try
    End Function

    Private Shared Function LetteraCompitazione(parola As String) As String
        Try
            Dim normale As String = NormalizzaParolaCompitazione(parola)
            If normale = "" Then Return Nothing
            If _lettereCompitazione.ContainsKey(normale) Then Return _lettereCompitazione(normale)
            If normale.Length = 1 AndAlso normale(0) >= "a"c AndAlso normale(0) <= "z"c Then Return normale
            If normale = "@" OrElse normale = "_" Then Return normale
            If Text.RegularExpressions.Regex.IsMatch(normale, "^[0-9]+$") Then Return normale
            If _dominiCompitazione.Contains(normale) Then Return normale
            Return Nothing
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.LetteraCompitazione: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Public Shared Function ConvertiCompitazione(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return If(testo, "")
            Dim risultatoTesto As String = testo
            Dim posizione As Integer = 0
            Dim giri As Integer = 0
            While giri < 20
                giri += 1
                Dim inizio As Text.RegularExpressions.Match = _modelloCompitazione.Match(risultatoTesto, posizione)
                If Not inizio.Success Then Exit While

                Dim resto As String = risultatoTesto.Substring(inizio.Index + inizio.Length)
                Dim parole As Text.RegularExpressions.MatchCollection = _modelloParoleCompitazione.Matches(resto)
                Dim lettere As New List(Of String)
                Dim normaliConsumate As New List(Of String)
                Dim finiConsumate As New List(Of Integer)
                Dim fineCompitazione As Integer = 0
                Dim indice As Integer = 0

                While indice < parole.Count
                    Dim attuale As String = NormalizzaParolaCompitazione(parole(indice).Value)
                    Dim successiva As String = If(indice + 1 < parole.Count, NormalizzaParolaCompitazione(parole(indice + 1).Value), Nothing)

                    If attuale = "fine" Then
                        fineCompitazione = parole(indice).Index + parole(indice).Length
                        If successiva IsNot Nothing AndAlso (successiva = "compitazione" OrElse successiva = "parola") Then fineCompitazione = parole(indice + 1).Index + parole(indice + 1).Length
                        finiConsumate.Clear()
                        Exit While
                    End If

                    If successiva IsNot Nothing AndAlso _coppieCompitazione.ContainsKey(attuale & " " & successiva) Then
                        lettere.Add(_coppieCompitazione(attuale & " " & successiva))
                        normaliConsumate.Add(attuale & " " & successiva)
                        finiConsumate.Add(parole(indice + 1).Index + parole(indice + 1).Length)
                        fineCompitazione = finiConsumate(finiConsumate.Count - 1)
                        indice += 2
                        Continue While
                    End If

                    If (attuale = "doppia" OrElse attuale = "doppio") AndAlso successiva IsNot Nothing Then
                        Dim doppia As String = LetteraCompitazione(parole(indice + 1).Value)
                        If doppia IsNot Nothing AndAlso doppia.Length = 1 Then
                            lettere.Add(doppia & doppia)
                            normaliConsumate.Add(attuale & " " & successiva)
                            finiConsumate.Add(parole(indice + 1).Index + parole(indice + 1).Length)
                            fineCompitazione = finiConsumate(finiConsumate.Count - 1)
                            indice += 2
                            Continue While
                        End If
                    End If

                    If (attuale = "maiuscola" OrElse attuale = "maiuscolo") AndAlso successiva IsNot Nothing Then
                        Dim grande As String = LetteraCompitazione(parole(indice + 1).Value)
                        If grande IsNot Nothing Then
                            lettere.Add(grande.ToUpperInvariant())
                            normaliConsumate.Add(attuale & " " & successiva)
                            finiConsumate.Add(parole(indice + 1).Index + parole(indice + 1).Length)
                            fineCompitazione = finiConsumate(finiConsumate.Count - 1)
                            indice += 2
                            Continue While
                        End If
                    End If

                    Dim lettera As String = LetteraCompitazione(parole(indice).Value)
                    If lettera Is Nothing Then
                        While lettere.Count > 0 AndAlso normaliConsumate.Count > 0 AndAlso _paroleAmbigueCompitazione.Contains(normaliConsumate(normaliConsumate.Count - 1))
                            lettere.RemoveAt(lettere.Count - 1)
                            normaliConsumate.RemoveAt(normaliConsumate.Count - 1)
                            finiConsumate.RemoveAt(finiConsumate.Count - 1)
                        End While
                        fineCompitazione = If(finiConsumate.Count > 0, finiConsumate(finiConsumate.Count - 1), 0)
                        Exit While
                    End If

                    lettere.Add(lettera)
                    normaliConsumate.Add(attuale)
                    finiConsumate.Add(parole(indice).Index + parole(indice).Length)
                    fineCompitazione = finiConsumate(finiConsumate.Count - 1)
                    indice += 1
                End While

                Dim parola As String = String.Join("", lettere)
                Dim avvioSicuro As Boolean = NormalizzaParolaCompitazione(inizio.Value).StartsWith("compit", StringComparison.Ordinal)
                If parola = "" OrElse (Not avvioSicuro AndAlso lettere.Count < LETTERE_MINIME_COMPITAZIONE_INCERTA) Then
                    posizione = inizio.Index + inizio.Length
                    Continue While
                End If

                Dim prima As String = risultatoTesto.Substring(0, inizio.Index).TrimEnd()
                Dim dopo As String = resto.Substring(Math.Min(fineCompitazione, resto.Length)).TrimStart(" "c, ","c, "."c, ";"c, ":"c)
                risultatoTesto = If(prima = "", "", prima & " ") & parola & If(dopo = "", "", " " & dopo)
                posizione = Math.Min(risultatoTesto.Length, If(prima = "", 0, prima.Length + 1) + parola.Length)
            End While
            Return risultatoTesto
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ConvertiCompitazione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Public Shared Function CorreggiTrascrizione(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return If(testo, "")
            Dim corretto As String = Text.RegularExpressions.Regex.Replace(testo, "(?<![\p{L}\p{N}])([Uu])(?=[Ii][Nn])", Function(m As Text.RegularExpressions.Match) If(m.Value = "U", "W", "w"))
            Return ConvertiCompitazione(corretto)
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.CorreggiTrascrizione: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return testo
        End Try
    End Function

    Private Shared Function DecodificaBlocco(blocco As Single(), frequenza As Integer) As String
        Try
            Using flusso As OfflineStream = _riconoscitore.CreateStream()
                flusso.AcceptWaveform(frequenza, blocco)
                _riconoscitore.Decode(flusso)
                Dim risultato As OfflineRecognizerResult = flusso.Result
                If risultato Is Nothing Then Return ""
                Return If(risultato.Text, "")
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.DecodificaBlocco: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Shared Function SuddividiCampioni(campioni As Single(), frequenza As Integer) As List(Of Single())
        Dim blocchi As New List(Of Single())
        Try
            Dim massimo As Integer = SECONDI_BLOCCO_MASSIMO * frequenza
            If campioni.Length <= massimo Then
                blocchi.Add(campioni)
                Return blocchi
            End If

            Dim finestra As Integer = Math.Max(1, CAMPIONI_FINESTRA_TAGLIO * frequenza \ FREQUENZA_CAMPIONAMENTO)
            Dim inizio As Integer = 0
            While inizio < campioni.Length
                Dim restanti As Integer = campioni.Length - inizio
                If restanti <= massimo Then
                    blocchi.Add(CopiaIntervallo(campioni, inizio, restanti))
                    Exit While
                End If

                Dim primoTaglio As Integer = inizio + SECONDI_BLOCCO_MINIMO * frequenza
                Dim ultimoTaglio As Integer = inizio + massimo - finestra
                Dim taglio As Integer = inizio + SECONDI_BLOCCO_OBIETTIVO * frequenza
                Dim energiaMinima As Double = Double.MaxValue
                Dim posizione As Integer = primoTaglio
                While posizione <= ultimoTaglio
                    Dim energia As Double = 0
                    For indice As Integer = posizione To posizione + finestra - 1
                        energia += campioni(indice) * campioni(indice)
                    Next
                    If energia < energiaMinima Then
                        energiaMinima = energia
                        taglio = posizione + finestra \ 2
                    End If
                    posizione += finestra
                End While

                blocchi.Add(CopiaIntervallo(campioni, inizio, taglio - inizio))
                inizio = taglio
            End While
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.SuddividiCampioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            If blocchi.Count = 0 Then blocchi.Add(campioni)
        End Try
        Return blocchi
    End Function

    Private Shared Function CopiaIntervallo(campioni As Single(), inizio As Integer, lunghezza As Integer) As Single()
        Try
            Dim risultato(Math.Max(0, lunghezza) - 1) As Single
            If lunghezza > 0 Then Array.Copy(campioni, inizio, risultato, 0, lunghezza)
            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.CopiaIntervallo: {ex.Message}")
            Return New Single() {}
        End Try
    End Function

    Private Shared Sub ProgrammaRilascio()
        Try
            If _timerRilascio Is Nothing Then
                _timerRilascio = New Timer(AddressOf TimerRilascioScaduto, Nothing, MINUTI_RILASCIO_INATTIVITA * 60000, Timeout.Infinite)
            Else
                _timerRilascio.Change(MINUTI_RILASCIO_INATTIVITA * 60000, Timeout.Infinite)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.ProgrammaRilascio: {ex.Message}")
        End Try
    End Sub

    Private Shared Sub TimerRilascioScaduto(stato As Object)
        Try
            If _mantieniInMemoria Then
                ProgrammaRilascio()
                Exit Sub
            End If
            Rilascia()
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.TimerRilascioScaduto: {ex.Message}")
        End Try
    End Sub

    Public Shared Sub Rilascia()
        Try
            SyncLock _bloccoRiconoscitore
                If _timerRilascio IsNot Nothing Then
                    _timerRilascio.Dispose()
                    _timerRilascio = Nothing
                End If
                If _riconoscitore IsNot Nothing Then
                    _riconoscitore.Dispose()
                    _riconoscitore = Nothing
                End If
            End SyncLock
        Catch ex As Exception
            log.ScriviLog($"WGTrascrittoreLocale.Rilascia: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

End Class