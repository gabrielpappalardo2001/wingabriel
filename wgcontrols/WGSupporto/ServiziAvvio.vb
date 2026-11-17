Imports System.IO
Imports System.Net
Imports System.Security.Policy
Imports System.Runtime.InteropServices
Imports System.Runtime.CompilerServices
Imports System.Text
Imports wgcontrols
Imports WGFileManager
Imports WGDati
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports System.Xml
Imports System.ComponentModel
Imports System.Security
Imports System.Diagnostics
Imports WGYoutube
Imports System.Linq
Imports System.Collections.Concurrent
Imports System.Security.Principal
Imports System.Management
Imports WGRadioTv
Imports WGSalute
Imports WGOpenStrictMap
Imports Newtonsoft.Json.Linq
Imports HtmlAgilityPack
Imports System.Text.RegularExpressions
Imports System.Data.SQLite
Imports WGLibriTesto
Imports WGInternetArchive
Imports WGMediaPlayer
Imports WGIA
Imports WGMail
Imports WGStampante
Imports WGContatti
Imports WGConnessioni

' Gestione dell'avvio, degli aggiornamenti automatici e dei collegamenti di sistema. Verifica i file binari e la configurazione
' iniziale.
Module ServiziAvvio

    Friend ReadOnly log As New wgcontrols.Logger()

    Friend Const APP_NOME_AGGIORNAMENTI As String = "wingabriel"

    Friend Const ID_CONFIG_CANALE_AGGIORNAMENTI As String = "canale_aggiornamenti"

    Friend ReadOnly cacheImpronteLock As New Object()

    Friend Const CHIAVE_REGISTRO_AVVIO As String = "Software\Microsoft\Windows\CurrentVersion\Run"

    Friend Const NOME_VALORE_AVVIO As String = "WinGabriel"

    Friend Const ID_CONFIG_VERIFICA_AGGIORNAMENTI As String = "verifica_aggiornamenti_avvio"

    Friend Const ID_CONFIG_AVVIO_AUTOMATICO As String = "avvio_automatico_computer"

    Friend Const ID_CONFIG_DISATTIVA_STANDBY_USO As String = "disattiva_standby_wingabriel"

    Friend Const ID_CONFIG_AVVISO_ULTIME_NOTIZIE As String = "avviso_ultime_notizie_avvio"

    Friend Const ID_CONFIG_INTERVALLO_CONTROLLO_NOTIZIE As String = "intervallo_controllo_notizie_minuti"

    Friend Const NOME_FILE_DATABASE_NOTIZIE As String = "wg-notizie.sqlite"

    Friend Const ID_CONFIG_AVVISO_NUOVI_ARTICOLI_QUOTIDIANI As String = "avviso_nuovi_articoli_quotidiani"

    Friend Const ID_CONFIG_INTERVALLO_CONTROLLO_QUOTIDIANI As String = "intervallo_controllo_quotidiani_minuti"

    Friend Const ID_CONFIG_MASSIMO_ARTICOLI_SEGNALATI_QUOTIDIANI As String = "massimo_articoli_segnalati_quotidiani"

    Friend Const ID_CONFIG_TOUCH_ATTIVO As String = "touch_attivo"

    Friend Const ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO As String = "touch_adatta_dispositivo"

    Friend Const ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO As String = "touch_orientamento_bloccato"







    Friend Declare Unicode Function DeleteFileW Lib "kernel32.dll" (ByVal lpFileName As String) As Boolean



    Friend domandaUscita As WGDomanda

    Friend Function OttieniDataPiuRecenteBinariLocali() As DateTime
        Dim dataPiuRecente As DateTime = DateTime.MinValue

        Try
            Dim cartellaBase As String = AppDomain.CurrentDomain.BaseDirectory
            Dim estensioni As String() = New String() {"*.dll", "*.exe"}

            For Each filtro As String In estensioni
                Try
                    For Each percorsoFile As String In Directory.GetFiles(cartellaBase, filtro, SearchOption.AllDirectories)
                        Try
                            Dim dataFile As DateTime = File.GetLastWriteTimeUtc(percorsoFile)
                            If dataFile > dataPiuRecente Then
                                dataPiuRecente = dataFile
                            End If
                        Catch exFile As Exception
                            ScriviLogAvvio("OttieniDataPiuRecenteBinariLocali (file " & percorsoFile & "): " & exFile.Message)
                        End Try
                    Next
                Catch exFiltro As Exception
                    ScriviLogAvvio("OttieniDataPiuRecenteBinariLocali (filtro " & filtro & "): " & exFiltro.Message & Environment.NewLine & exFiltro.StackTrace)
                End Try
            Next
        Catch ex As Exception
            ScriviLogAvvio("OttieniDataPiuRecenteBinariLocali eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

        Return dataPiuRecente
    End Function

    Friend cacheImpronteAvvio As Dictionary(Of String, String) = Nothing

    Friend cacheImpronteModificata As Boolean = False

    Friend Function PercorsoRelativoAllaBase(percorsoAssoluto As String) As String
        Try
            If String.IsNullOrEmpty(percorsoAssoluto) Then
                Return percorsoAssoluto
            End If

            Dim base As String = AppDomain.CurrentDomain.BaseDirectory

            If Not base.EndsWith(Path.DirectorySeparatorChar) AndAlso Not base.EndsWith(Path.AltDirectorySeparatorChar) Then
                base &= Path.DirectorySeparatorChar
            End If

            If percorsoAssoluto.StartsWith(base, StringComparison.OrdinalIgnoreCase) Then
                Return percorsoAssoluto.Substring(base.Length)
            End If

            Return percorsoAssoluto
        Catch ex As Exception
            ScriviLogAvvio("PercorsoRelativoAllaBase " & percorsoAssoluto & ": " & ex.Message)
            Return percorsoAssoluto
        End Try
    End Function

    Friend Function PercorsoAssolutoDaBase(percorsoRelativoOAssoluto As String) As String
        Try
            If String.IsNullOrEmpty(percorsoRelativoOAssoluto) Then
                Return percorsoRelativoOAssoluto
            End If

            If Path.IsPathRooted(percorsoRelativoOAssoluto) Then
                Return percorsoRelativoOAssoluto
            End If

            Return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, percorsoRelativoOAssoluto)
        Catch ex As Exception
            ScriviLogAvvio("PercorsoAssolutoDaBase " & percorsoRelativoOAssoluto & ": " & ex.Message)
            Return percorsoRelativoOAssoluto
        End Try
    End Function

    Friend Function PercorsoCacheImpronte() As String
        Return Path.Combine(WGDb.WGPercorsiApplicazione.CartellaCache(), "cacke.dat")
    End Function

    Friend Sub AssicuraCartellaCacheImpronte()
        Try
            Dim percorso As String = PercorsoCacheImpronte()
            Dim cartella As String = Path.GetDirectoryName(percorso)

            If Not String.IsNullOrEmpty(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If
        Catch ex As Exception
            ScriviLogAvvio("AssicuraCartellaCacheImpronte: " & ex.Message)
        End Try
    End Sub

    Friend Sub CaricaCacheImpronte()
        Try
            SyncLock cacheImpronteLock
                If cacheImpronteAvvio IsNot Nothing Then
                    Return
                End If

                cacheImpronteAvvio = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                cacheImpronteModificata = False

                Dim percorso As String = PercorsoCacheImpronte()

                If Not File.Exists(percorso) Then
                    Return
                End If

                For Each riga As String In File.ReadAllLines(percorso, Encoding.UTF8)
                    Try
                        If String.IsNullOrWhiteSpace(riga) Then
                            Continue For
                        End If

                        Dim separatore As Integer = riga.LastIndexOf("|"c)

                        If separatore <= 0 OrElse separatore >= riga.Length - 1 Then
                            Continue For
                        End If

                        cacheImpronteAvvio(riga.Substring(0, separatore)) = riga.Substring(separatore + 1)
                    Catch exRiga As Exception
                        ScriviLogAvvio("CaricaCacheImpronte (riga): " & exRiga.Message)
                    End Try
                Next
            End SyncLock
        Catch ex As Exception
            ScriviLogAvvio("CaricaCacheImpronte: " & ex.Message)
        End Try
    End Sub

    Friend Sub SalvaCacheImpronte()
        Try
            Dim istantanea As KeyValuePair(Of String, String)()

            SyncLock cacheImpronteLock
                If cacheImpronteAvvio Is Nothing OrElse Not cacheImpronteModificata Then
                    Return
                End If

                istantanea = cacheImpronteAvvio.ToArray()
                cacheImpronteModificata = False
            End SyncLock

            Dim contenuto As New StringBuilder()

            For Each coppia As KeyValuePair(Of String, String) In istantanea
                Try
                    Dim separatore As Integer = coppia.Key.IndexOf("|"c)

                    If separatore <= 0 Then
                        Continue For
                    End If

                    Dim percorsoVoce As String = coppia.Key.Substring(0, separatore)
                    Dim percorsoVoceAssoluto As String = PercorsoAssolutoDaBase(percorsoVoce)

                    If Not File.Exists(percorsoVoceAssoluto) Then
                        Continue For
                    End If

                    Dim informazioni As New FileInfo(percorsoVoceAssoluto)
                    Dim chiaveAttuale As String = percorsoVoce & "|" & informazioni.Length.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & informazioni.LastWriteTimeUtc.Ticks.ToString(Globalization.CultureInfo.InvariantCulture)

                    If Not String.Equals(chiaveAttuale, coppia.Key, StringComparison.OrdinalIgnoreCase) Then
                        Continue For
                    End If

                    contenuto.Append(coppia.Key)
                    contenuto.Append("|"c)
                    contenuto.Append(coppia.Value)
                    contenuto.Append(Environment.NewLine)
                Catch exVoce As Exception
                    ScriviLogAvvio("SalvaCacheImpronte (voce): " & exVoce.Message)
                End Try
            Next

            AssicuraCartellaCacheImpronte()

            File.WriteAllText(PercorsoCacheImpronte(), contenuto.ToString(), Encoding.UTF8)
        Catch ex As Exception
            ScriviLogAvvio("SalvaCacheImpronte: " & ex.Message)
        End Try
    End Sub

    Friend Function CalcolaImprontaFile(percorsoFile As String) As String
        Try
            If String.IsNullOrWhiteSpace(percorsoFile) OrElse Not File.Exists(percorsoFile) Then
                Return ""
            End If

            If cacheImpronteAvvio Is Nothing Then
                CaricaCacheImpronte()
            End If

            ' nella verifica completa si rilegge il file: la cache si fida di dimensione e data, che un file rovinato può avere uguali
            If verificaCompletaInCorso Then
                Dim ricalcolata As String = WGImprontaSha256.DaFile(percorsoFile)
                If Not String.IsNullOrEmpty(ricalcolata) Then
                    Dim chiaveCompleta As String = PercorsoRelativoAllaBase(percorsoFile) & "|" & New FileInfo(percorsoFile).Length.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & New FileInfo(percorsoFile).LastWriteTimeUtc.Ticks.ToString(Globalization.CultureInfo.InvariantCulture)
                    SyncLock cacheImpronteLock
                        cacheImpronteAvvio(chiaveCompleta) = ricalcolata
                        cacheImpronteModificata = True
                    End SyncLock
                End If
                Return ricalcolata
            End If

            Dim informazioni As New FileInfo(percorsoFile)
            Dim chiave As String = PercorsoRelativoAllaBase(percorsoFile) & "|" & informazioni.Length.ToString(Globalization.CultureInfo.InvariantCulture) & "|" & informazioni.LastWriteTimeUtc.Ticks.ToString(Globalization.CultureInfo.InvariantCulture)

            Dim memorizzata As String = Nothing

            SyncLock cacheImpronteLock
                If cacheImpronteAvvio.TryGetValue(chiave, memorizzata) AndAlso Not String.IsNullOrEmpty(memorizzata) Then
                    Return memorizzata
                End If
            End SyncLock

            Dim calcolata As String = WGImprontaSha256.DaFile(percorsoFile)

            If Not String.IsNullOrEmpty(calcolata) Then
                SyncLock cacheImpronteLock
                    cacheImpronteAvvio(chiave) = calcolata
                    cacheImpronteModificata = True
                End SyncLock
            End If

            Return calcolata
        Catch ex As Exception
            ScriviLogAvvio("CalcolaImprontaFile " & percorsoFile & ": " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Function VerificaAggiornamentiDisponibili() As Boolean
        Try
            Dim cartellaCache As String = WGDb.WGPercorsiApplicazione.CartellaCache()

            ' Il rilascio firmato del canale scelto (stabile o beta): se il sito lo ha e tutto torna (firma, impronta del manifest) l'elenco
            ' dei file viene da lì; se è già quello applicato non c'è niente da controllare. Senza rilascio valido non c'è niente da controllare.
            Dim testoRisposta As String = ""
            Dim puntatoreRilascio As RilascioFirmato.Puntatore = Nothing
            Dim manifestApplicabile As RilascioFirmato.Manifest = Nothing
            Try
                Dim erroreRilascio As String = ""
                puntatoreRilascio = RilascioFirmato.LeggiPuntatore(APP_NOME_AGGIORNAMENTI, CanaleAggiornamenti(), erroreRilascio)
                If puntatoreRilascio IsNot Nothing Then
                    RilascioFirmato.ContaUtente(cartellaCache, APP_NOME_AGGIORNAMENTI)
                    If RilascioFirmato.PiuVecchioDelloInstallato(cartellaCache, puntatoreRilascio) Then
                        ScriviLogAvvio("VerificaAggiornamentiDisponibili: il puntatore del rilascio è più vecchio di quello applicato, ignorato.")
                        Return False
                    End If
                    If Not verificaCompletaInCorso AndAlso RilascioFirmato.GiaApplicato(cartellaCache, puntatoreRilascio) AndAlso RilascioFirmato.FotografiaIntegra(cartellaCache, AppDomain.CurrentDomain.BaseDirectory, APP_NOME_AGGIORNAMENTI) Then Return False
                    manifestApplicabile = RilascioFirmato.LeggiManifest(puntatoreRilascio, erroreRilascio)
                    If manifestApplicabile IsNot Nothing Then
                        testoRisposta = RilascioFirmato.ComeElencoXml(manifestApplicabile)
                    Else
                        ScriviLogAvvio("VerificaAggiornamentiDisponibili (rilascio): " & erroreRilascio)
                        puntatoreRilascio = Nothing
                    End If
                ElseIf erroreRilascio <> "" Then
                    ScriviLogAvvio("VerificaAggiornamentiDisponibili (rilascio): " & erroreRilascio)
                End If
            Catch exRilascio As Exception
                ScriviLogAvvio("VerificaAggiornamentiDisponibili (rilascio): " & exRilascio.Message)
                puntatoreRilascio = Nothing
                testoRisposta = ""
            End Try

            If String.IsNullOrWhiteSpace(testoRisposta) Then
                Return False
            End If

            Dim documento As New XmlDocument()
            documento.LoadXml(testoRisposta)

            Dim nodiVoce As XmlNodeList = documento.SelectNodes("//Voce")

            If nodiVoce Is Nothing OrElse nodiVoce.Count = 0 Then
                Return False
            End If

            Dim cartellaBase As String = AppDomain.CurrentDomain.BaseDirectory
            Dim sistemaA64Bit As Boolean = Environment.Is64BitOperatingSystem
            Dim cartellaArchSbagliata As String = If(sistemaA64Bit, "x86/", "x64/")
            Dim numeroDaAggiornare As Integer = 0

            Dim opzioniParallelo As New ParallelOptions With {.MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount * 2)}

            Parallel.ForEach(nodiVoce.Cast(Of XmlNode)(), opzioniParallelo,
                        Sub(nodo As XmlNode)
                            Try
                                Dim attributoPercorso As XmlAttribute = nodo.Attributes("percorso")

                                If attributoPercorso Is Nothing OrElse String.IsNullOrWhiteSpace(attributoPercorso.Value) Then
                                    Return
                                End If

                                Dim percorsoRelativo As String = attributoPercorso.Value.Replace("\"c, "/"c)

                                If percorsoRelativo.Contains("..") Then
                                    Return
                                End If

                                If RilascioFirmato.FileDaNonScaricare(percorsoRelativo) Then
                                    Return
                                End If

                                Dim tipo As String = "normale"
                                Dim attributoTipo As XmlAttribute = nodo.Attributes("tipo")

                                If attributoTipo IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attributoTipo.Value) Then
                                    tipo = attributoTipo.Value
                                End If

                                Dim voceLibmpv As Boolean = tipo.Equals("libmpv", StringComparison.OrdinalIgnoreCase)

                                If Not voceLibmpv AndAlso percorsoRelativo.StartsWith(cartellaArchSbagliata, StringComparison.OrdinalIgnoreCase) Then
                                    Return
                                End If

                                Dim percorsoLocale As String

                                If voceLibmpv Then
                                    percorsoLocale = Path.Combine(cartellaBase, Path.GetFileName(percorsoRelativo))
                                Else
                                    percorsoLocale = Path.Combine(cartellaBase, percorsoRelativo.Replace("/"c, Path.DirectorySeparatorChar))
                                End If

                                If Not File.Exists(percorsoLocale) Then
                                    Interlocked.Increment(numeroDaAggiornare)
                                    Return
                                End If

                                Dim informazioniLocali As New FileInfo(percorsoLocale)

                                Dim dataModificaTrovata As Boolean = False
                                Dim dataRemotaAttuale As DateTime = DateTime.MinValue
                                Dim localeDataPariOPosticipata As Boolean = False

                                Try
                                    Dim attributoDataPrecoce As XmlAttribute = nodo.Attributes("dataModifica")
                                    If attributoDataPrecoce IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attributoDataPrecoce.Value) Then
                                        If DateTime.TryParse(attributoDataPrecoce.Value, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AdjustToUniversal Or Globalization.DateTimeStyles.AssumeUniversal, dataRemotaAttuale) Then
                                            dataModificaTrovata = True
                                            If informazioniLocali.LastWriteTimeUtc.AddSeconds(2) >= dataRemotaAttuale Then
                                                localeDataPariOPosticipata = True
                                            End If
                                        End If
                                    End If
                                Catch exDataPrecoce As Exception
                                    ScriviLogAvvio("VerificaAggiornamentiDisponibili (data locale " & percorsoRelativo & "): " & exDataPrecoce.Message)
                                End Try

                                If localeDataPariOPosticipata AndAlso Not verificaCompletaInCorso Then
                                    Return
                                End If

                                Dim nomeAttributoDimensione As String = "dimensione"

                                If voceLibmpv Then
                                    nomeAttributoDimensione = If(sistemaA64Bit, "dimensioneX64", "dimensioneX86")
                                End If

                                Dim dimensioneRemota As Long = -1
                                Dim attributoDimensione As XmlAttribute = nodo.Attributes(nomeAttributoDimensione)

                                If attributoDimensione IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attributoDimensione.Value) Then
                                    Dim valoreDimensione As Long = 0

                                    If Long.TryParse(attributoDimensione.Value, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, valoreDimensione) AndAlso valoreDimensione >= 0 Then
                                        dimensioneRemota = valoreDimensione
                                    End If
                                End If

                                If dimensioneRemota >= 0 AndAlso dimensioneRemota <> informazioniLocali.Length Then
                                    Interlocked.Increment(numeroDaAggiornare)
                                    Return
                                End If

                                Dim nomeAttributoImpronta As String = "hash"

                                If voceLibmpv Then
                                    nomeAttributoImpronta = If(sistemaA64Bit, "hashX64", "hashX86")
                                End If

                                Dim improntaRemota As String = ""
                                Dim attributoImpronta As XmlAttribute = nodo.Attributes(nomeAttributoImpronta)

                                If attributoImpronta IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attributoImpronta.Value) Then
                                    improntaRemota = attributoImpronta.Value.Trim()
                                End If

                                If improntaRemota.Length > 0 Then
                                    Dim improntaLocale As String = CalcolaImprontaFile(percorsoLocale)

                                    If improntaLocale.Length = 0 OrElse Not String.Equals(improntaLocale, improntaRemota, StringComparison.OrdinalIgnoreCase) Then
                                        Interlocked.Increment(numeroDaAggiornare)
                                    End If

                                    Return
                                End If

                                ' Senza impronta e con la stessa dimensione il file non si scarica: la data da sola
                                ' non basta a dire che il file del server è quello giusto per questa versione.
                            Catch exVoce As Exception
                                ScriviLogAvvio("VerificaAggiornamentiDisponibili (voce): " & exVoce.Message)
                            End Try
                        End Sub)

            Try
                Task.Run(Sub()
                             Try
                                 SalvaCacheImpronte()
                             Catch exCacheAsync As Exception
                                 ScriviLogAvvio("VerificaAggiornamentiDisponibili (salvataggio cache in background): " & exCacheAsync.Message)
                             End Try
                         End Sub)
            Catch exCache As Exception
                ScriviLogAvvio("VerificaAggiornamentiDisponibili (avvio salvataggio cache): " & exCache.Message)
            End Try

            If numeroDaAggiornare = 0 AndAlso puntatoreRilascio IsNot Nothing Then
                RilascioFirmato.SalvaStato(cartellaCache, puntatoreRilascio)
                If manifestApplicabile IsNot Nothing Then RilascioFirmato.SalvaFotografia(cartellaCache, AppDomain.CurrentDomain.BaseDirectory, manifestApplicabile)
            End If

            Return numeroDaAggiornare > 0
        Catch ex As Exception
            ScriviLogAvvio("VerificaAggiornamentiDisponibili eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function VerificaAggiornamentiAvvioAttiva() As Boolean
        Try
            Dim configVerifica As New WGConfig()

            If Not configVerifica.EsisteId(ID_CONFIG_VERIFICA_AGGIORNAMENTI) Then
                configVerifica.AggiungiValore(ID_CONFIG_VERIFICA_AGGIORNAMENTI, "true")
                Return True
            End If

            Dim valore As String = configVerifica.LeggiValore(ID_CONFIG_VERIFICA_AGGIORNAMENTI)

            If String.IsNullOrWhiteSpace(valore) Then
                Return True
            End If

            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogAvvio("VerificaAggiornamentiAvvioAttiva eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Public Function AdattamentoTouchAlDispositivoAttivo() As Boolean
        Try
            Dim configAdattamento As New WGConfig()

            If Not configAdattamento.EsisteId(ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO) Then
                configAdattamento.AggiungiValore(ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO, "true")
                Return True
            End If

            Dim valore As String = configAdattamento.LeggiValore(ID_CONFIG_TOUCH_ADATTA_DISPOSITIVO)

            If String.IsNullOrWhiteSpace(valore) Then
                Return True
            End If

            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogAvvio("AdattamentoTouchAlDispositivoAttivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Public Function OrientamentoTouchBloccatoAttivo() As Boolean
        Try
            Dim configOrientamento As New WGConfig()

            If Not configOrientamento.EsisteId(ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO) Then
                configOrientamento.AggiungiValore(ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO, "false")
                Return False
            End If

            Dim valore As String = configOrientamento.LeggiValore(ID_CONFIG_TOUCH_ORIENTAMENTO_BLOCCATO)

            If String.IsNullOrWhiteSpace(valore) Then
                Return False
            End If

            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogAvvio("OrientamentoTouchBloccatoAttivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Sub ApplicaAdattamentoTouchSalvato()
        Try
            Dim gestoreTouch As WGTouk = WGTouk.GetInstance()
            If gestoreTouch Is Nothing Then Exit Sub
            gestoreTouch.AdattaAlDispositivo = AdattamentoTouchAlDispositivoAttivo()
            gestoreTouch.OrientamentoBloccato = OrientamentoTouchBloccatoAttivo()
        Catch ex As Exception
            ScriviLogAvvio("ApplicaAdattamentoTouchSalvato eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Public Function AvvisoUltimeNotizieAttivo() As Boolean
        Try
            Dim configAvviso As New WGConfig()

            If Not configAvviso.EsisteId(ID_CONFIG_AVVISO_ULTIME_NOTIZIE) Then
                configAvviso.AggiungiValore(ID_CONFIG_AVVISO_ULTIME_NOTIZIE, "true")
                Return True
            End If

            Dim valore As String = configAvviso.LeggiValore(ID_CONFIG_AVVISO_ULTIME_NOTIZIE)

            If String.IsNullOrWhiteSpace(valore) Then
                Return True
            End If

            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogAvvio("AvvisoUltimeNotizieAttivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Public Function IntervalloControlloNotizieMinuti() As Integer
        Try
            Dim configIntervallo As New WGConfig()

            If Not configIntervallo.EsisteId(ID_CONFIG_INTERVALLO_CONTROLLO_NOTIZIE) Then
                configIntervallo.AggiungiValore(ID_CONFIG_INTERVALLO_CONTROLLO_NOTIZIE, "15")
                Return 15
            End If

            Dim valore As String = configIntervallo.LeggiValore(ID_CONFIG_INTERVALLO_CONTROLLO_NOTIZIE)
            Dim minuti As Integer
            If Integer.TryParse(valore, minuti) AndAlso minuti > 0 Then Return minuti
            Return 15
        Catch ex As Exception
            ScriviLogAvvio("IntervalloControlloNotizieMinuti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 15
        End Try
    End Function

    Public Function AvvisoNuoviArticoliQuotidianiAttivo() As Boolean
        Try
            Dim configAvviso As New WGConfig()

            If Not configAvviso.EsisteId(ID_CONFIG_AVVISO_NUOVI_ARTICOLI_QUOTIDIANI) Then
                configAvviso.AggiungiValore(ID_CONFIG_AVVISO_NUOVI_ARTICOLI_QUOTIDIANI, "true")
                Return True
            End If

            Dim valore As String = configAvviso.LeggiValore(ID_CONFIG_AVVISO_NUOVI_ARTICOLI_QUOTIDIANI)

            If String.IsNullOrWhiteSpace(valore) Then
                Return True
            End If

            Return String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogAvvio("AvvisoNuoviArticoliQuotidianiAttivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Public Function IntervalloControlloQuotidianiMinuti() As Integer
        Try
            Dim configIntervallo As New WGConfig()

            If Not configIntervallo.EsisteId(ID_CONFIG_INTERVALLO_CONTROLLO_QUOTIDIANI) Then
                configIntervallo.AggiungiValore(ID_CONFIG_INTERVALLO_CONTROLLO_QUOTIDIANI, "30")
                Return 30
            End If

            Dim valore As String = configIntervallo.LeggiValore(ID_CONFIG_INTERVALLO_CONTROLLO_QUOTIDIANI)
            Dim minuti As Integer
            If Integer.TryParse(valore, minuti) AndAlso minuti > 0 Then Return minuti
            Return 30
        Catch ex As Exception
            ScriviLogAvvio("IntervalloControlloQuotidianiMinuti eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 30
        End Try
    End Function

    Public Function MassimoArticoliSegnalatiQuotidiani() As Integer
        Try
            Dim configMassimo As New WGConfig()

            If Not configMassimo.EsisteId(ID_CONFIG_MASSIMO_ARTICOLI_SEGNALATI_QUOTIDIANI) Then
                configMassimo.AggiungiValore(ID_CONFIG_MASSIMO_ARTICOLI_SEGNALATI_QUOTIDIANI, "5")
                Return 5
            End If

            Dim valore As String = configMassimo.LeggiValore(ID_CONFIG_MASSIMO_ARTICOLI_SEGNALATI_QUOTIDIANI)
            Dim massimo As Integer
            If Integer.TryParse(valore, massimo) AndAlso massimo > 0 Then Return massimo
            Return 5
        Catch ex As Exception
            ScriviLogAvvio("MassimoArticoliSegnalatiQuotidiani eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 5
        End Try
    End Function

    Public Function RecuperaArticoliWinGabriel() As List(Of ArticoloNotiziaWinGabriel)
        Dim riuscito As Boolean = False
        Return RecuperaArticoliWinGabriel(riuscito)
    End Function

    ''' <summary>
    ''' Gli articoli del sito di WinGabriel (dal più recente). riuscito è falso se il sito non ha risposto o la risposta non era valida:
    ''' in quel caso l'elenco vuoto non vuol dire che non ci sono notizie, e chi chiama non deve segnare il controllo come fatto.
    ''' </summary>
    Public Function RecuperaArticoliWinGabriel(ByRef riuscito As Boolean) As List(Of ArticoloNotiziaWinGabriel)
        Dim risultato As New List(Of ArticoloNotiziaWinGabriel)
        riuscito = False
        Try
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12
            Dim pagina As Integer = 1
            Dim continua As Boolean = True
            Const PerPagina As Integer = 100

            While continua
                Dim url As String = "https://wingabriel.it/wp-json/wp/v2/posts?per_page=" & PerPagina.ToString() & "&page=" & pagina.ToString() & "&orderby=date&order=desc"
                Dim contenuto As String = Nothing
                Dim fineElenco As Boolean = False
                Const maxTentativi As Integer = 3

                For tentativo As Integer = 1 To maxTentativi
                    Dim codice As Integer = 0
                    Dim corpoErrore As String = ""
                    Dim stato As WebExceptionStatus = WebExceptionStatus.Success
                    Try
                        Using client As New WebClient()
                            client.Encoding = Encoding.UTF8
                            client.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36")
                            client.Headers.Add("Accept", "application/json")
                            contenuto = client.DownloadString(url)
                        End Using
                        Exit For
                    Catch exWeb As WebException
                        stato = exWeb.Status
                        ' Il codice e il corpo della risposta si leggono una volta sola, prima che la risposta venga chiusa.
                        Dim rispostaHttp As HttpWebResponse = TryCast(exWeb.Response, HttpWebResponse)
                        If rispostaHttp IsNot Nothing Then
                            Try
                                codice = CInt(rispostaHttp.StatusCode)
                                Using flusso As Stream = rispostaHttp.GetResponseStream()
                                    If flusso IsNot Nothing Then
                                        Using lettore As New StreamReader(flusso)
                                            corpoErrore = lettore.ReadToEnd()
                                        End Using
                                    End If
                                End Using
                            Catch exCorpo As Exception
                                log.ScriviLog("RecuperaArticoliWinGabriel (lettura della risposta, pagina " & pagina.ToString() & "): " & exCorpo.Message)
                            End Try
                        End If
                        If corpoErrore.Contains("rest_post_invalid_page_number") Then
                            ' Dopo l'ultima pagina WordPress risponde con un errore: l'elenco è finito.
                            fineElenco = True
                            Exit For
                        End If
                        Dim passeggero As Boolean = stato = WebExceptionStatus.Timeout OrElse stato = WebExceptionStatus.ConnectFailure OrElse stato = WebExceptionStatus.SendFailure OrElse
                            stato = WebExceptionStatus.NameResolutionFailure OrElse stato = WebExceptionStatus.ReceiveFailure OrElse stato = WebExceptionStatus.ConnectionClosed OrElse
                            codice = 429 OrElse (codice >= 500 AndAlso codice <= 599)
                        If passeggero AndAlso tentativo < maxTentativi Then
                            Thread.Sleep(1000 * tentativo)
                        Else
                            log.ScriviLog("RecuperaArticoliWinGabriel: " & url & " non riuscito (" & stato.ToString() & ", HTTP " & codice.ToString() & ", tentativo " & tentativo.ToString() & "): " & exWeb.Message)
                            Exit For
                        End If
                    Catch exGenerica As Exception
                        log.ScriviLog("RecuperaArticoliWinGabriel (tentativo " & tentativo.ToString() & "/" & maxTentativi.ToString() & ") per URL " & url & ": " & exGenerica.Message & Environment.NewLine & exGenerica.StackTrace)
                        If tentativo < maxTentativi Then Thread.Sleep(1000 * tentativo) Else Exit For
                    End Try
                Next

                If fineElenco Then
                    If pagina > 1 Then riuscito = True
                    Exit While
                End If
                If String.IsNullOrEmpty(contenuto) Then Exit While

                Dim arrayArticoli As JArray = Nothing
                Try
                    arrayArticoli = JArray.Parse(contenuto)
                Catch exParse As Exception
                    log.ScriviLog("RecuperaArticoliWinGabriel (risposta non valida, pagina " & pagina.ToString() & "): " & exParse.Message)
                    Exit While
                End Try
                riuscito = True
                If arrayArticoli.Count = 0 Then Exit While

                For Each elementoArticolo As JToken In arrayArticoli
                    Try
                        Dim articolo As New ArticoloNotiziaWinGabriel()
                        articolo.IdArticolo = elementoArticolo("id").ToString()
                        articolo.Titolo = PulisciTestoHtmlWinGabriel(elementoArticolo("title")("rendered").ToString())
                        articolo.Testo = PulisciTestoHtmlWinGabriel(elementoArticolo("content")("rendered").ToString())
                        articolo.Url = elementoArticolo("link").ToString()
                        If String.IsNullOrWhiteSpace(articolo.Titolo) Then articolo.Titolo = "Articolo senza titolo"
                        risultato.Add(articolo)
                    Catch exArticolo As Exception
                        log.ScriviLog("RecuperaArticoliWinGabriel (articolo, pagina " & pagina.ToString() & "): " & exArticolo.Message)
                    End Try
                Next

                If arrayArticoli.Count < PerPagina Then
                    Exit While
                End If
                pagina += 1
            End While
        Catch ex As Exception
            log.ScriviLog("RecuperaArticoliWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            riuscito = False
        End Try

        Return risultato
    End Function

    Public Function PulisciTestoHtmlWinGabriel(html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return ""

            ' I blocchi (paragrafi, titoli, elenchi) e gli a capo diventano righe: senza, il testo uscirebbe tutto attaccato.
            Dim conAcapo As String = Regex.Replace(html, "<br\s*/?>|</(p|div|h[1-6]|li|ul|ol|blockquote|tr|figure|pre)>", "$0" & vbLf, RegexOptions.IgnoreCase)

            Dim documento As New HtmlAgilityPack.HtmlDocument()
            documento.LoadHtml(conAcapo)

            Dim nodiDaRimuovere As HtmlAgilityPack.HtmlNodeCollection = documento.DocumentNode.SelectNodes("//script|//style")
            If nodiDaRimuovere IsNot Nothing Then
                For Each nodo As HtmlAgilityPack.HtmlNode In nodiDaRimuovere
                    nodo.Remove()
                Next
            End If

            Dim testoGrezzo As String = documento.DocumentNode.InnerText
            Dim testoDecodificato As String = WebUtility.HtmlDecode(testoGrezzo).Replace(ChrW(160), " ")

            testoDecodificato = Regex.Replace(testoDecodificato, "[ \t]+", " ")
            testoDecodificato = Regex.Replace(testoDecodificato, " ?(\r\n|\r|\n) ?", Environment.NewLine)
            testoDecodificato = Regex.Replace(testoDecodificato, "(\r\n|\r|\n){3,}", Environment.NewLine & Environment.NewLine)
            testoDecodificato = testoDecodificato.Trim()

            Return testoDecodificato
        Catch ex As Exception
            log.ScriviLog("PulisciTestoHtmlWinGabriel eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Friend verificaCompletaInCorso As Boolean

    Public Function VerificaERiparaAdesso() As Boolean
        Try
            verificaCompletaInCorso = True
            Return VerificaAggiornamentiDisponibili()
        Catch ex As Exception
            ScriviLogAvvio("VerificaERiparaAdesso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        Finally
            verificaCompletaInCorso = False
        End Try
    End Function

    Public Function VerificaAggiornamentiAdesso() As Boolean
        Try
            Return VerificaAggiornamentiDisponibili()
        Catch ex As Exception
            ScriviLogAvvio("VerificaAggiornamentiAdesso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function AggiornaAggiornatore() As Boolean
        Try
            Dim percorsoUpdater As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WGUpdater.exe")

            Dim testoRisposta As String = ""
            Dim urlFileAggiornatore As String = ""
            Try
                Dim erroreRilascio As String = ""
                Dim manifestRilascio As RilascioFirmato.Manifest = RilascioFirmato.Leggi(APP_NOME_AGGIORNAMENTI, CanaleAggiornamenti(), erroreRilascio)
                If manifestRilascio IsNot Nothing Then
                    testoRisposta = RilascioFirmato.ComeElencoXml(manifestRilascio)
                    urlFileAggiornatore = RilascioFirmato.UrlFile(manifestRilascio.UrlBaseFile, "WGUpdater.exe")
                ElseIf erroreRilascio <> "" Then
                    ScriviLogAvvio("AggiornaAggiornatore (rilascio): " & erroreRilascio)
                End If
            Catch exRilascio As Exception
                ScriviLogAvvio("AggiornaAggiornatore (rilascio): " & exRilascio.Message)
                testoRisposta = ""
            End Try

            If String.IsNullOrWhiteSpace(testoRisposta) Then
                Return False
            End If

            Dim documento As New XmlDocument()
            documento.LoadXml(testoRisposta)

            Dim nodoUpdater As XmlNode = Nothing
            Dim nodiVoceUpdater As XmlNodeList = documento.SelectNodes("//Voce")

            If nodiVoceUpdater Is Nothing OrElse nodiVoceUpdater.Count = 0 Then
                Return False
            End If

            For Each nodo As XmlNode In nodiVoceUpdater
                Try
                    Dim attributoPercorso As XmlAttribute = nodo.Attributes("percorso")

                    If attributoPercorso Is Nothing Then
                        Continue For
                    End If

                    If String.Equals(attributoPercorso.Value.Replace("\"c, "/"c), "WGUpdater.exe", StringComparison.OrdinalIgnoreCase) Then
                        nodoUpdater = nodo
                        Exit For
                    End If
                Catch exNodo As Exception
                    ScriviLogAvvio("AggiornaAggiornatore (nodo): " & exNodo.Message)
                End Try
            Next

            If nodoUpdater Is Nothing Then
                Return False
            End If

            Dim dimensioneRemota As Long = -1
            Dim attributoDimensione As XmlAttribute = nodoUpdater.Attributes("dimensione")

            If attributoDimensione IsNot Nothing Then
                Dim valoreDimensione As Long = 0

                If Long.TryParse(attributoDimensione.Value, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, valoreDimensione) Then
                    dimensioneRemota = valoreDimensione
                End If
            End If

            Dim improntaRemota As String = ""
            Dim attributoImpronta As XmlAttribute = nodoUpdater.Attributes("hash")

            If attributoImpronta IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(attributoImpronta.Value) Then
                improntaRemota = attributoImpronta.Value.Trim()
            End If

            If File.Exists(percorsoUpdater) Then
                Dim informazioniLocali As New FileInfo(percorsoUpdater)

                If dimensioneRemota >= 0 AndAlso dimensioneRemota = informazioniLocali.Length Then
                    If improntaRemota.Length = 0 Then
                        Return False
                    End If

                    If String.Equals(CalcolaImprontaFile(percorsoUpdater), improntaRemota, StringComparison.OrdinalIgnoreCase) Then
                        Return False
                    End If
                End If
            End If

            Dim percorsoTemporaneo As String = percorsoUpdater & ".nuovo"

            If File.Exists(percorsoTemporaneo) Then
                File.Delete(percorsoTemporaneo)
            End If

            Dim richiestaFile As HttpWebRequest = CType(WebRequest.Create(urlFileAggiornatore), HttpWebRequest)
            richiestaFile.Method = "GET"
            richiestaFile.UserAgent = "WinGabriel/1.0"
            richiestaFile.Timeout = 120000
            richiestaFile.ReadWriteTimeout = 120000

            Using rispostaFile As HttpWebResponse = CType(richiestaFile.GetResponse(), HttpWebResponse)
                Using flussoRemoto As Stream = rispostaFile.GetResponseStream()
                    If flussoRemoto Is Nothing Then Return False
                    Using flussoLocale As New FileStream(percorsoTemporaneo, FileMode.Create, FileAccess.Write, FileShare.None, 65536)
                        Dim buffer(65535) As Byte
                        Dim letti As Integer = flussoRemoto.Read(buffer, 0, buffer.Length)

                        While letti > 0
                            flussoLocale.Write(buffer, 0, letti)
                            letti = flussoRemoto.Read(buffer, 0, buffer.Length)
                        End While

                        flussoLocale.Flush(True)
                    End Using
                End Using
            End Using

            Dim informazioniScaricato As New FileInfo(percorsoTemporaneo)

            If informazioniScaricato.Length = 0 OrElse (dimensioneRemota >= 0 AndAlso informazioniScaricato.Length <> dimensioneRemota) Then
                File.Delete(percorsoTemporaneo)
                Return False
            End If

            If improntaRemota.Length > 0 Then
                If Not String.Equals(WGImprontaSha256.DaFile(percorsoTemporaneo), improntaRemota, StringComparison.OrdinalIgnoreCase) Then
                    File.Delete(percorsoTemporaneo)
                    Return False
                End If
            End If

            DeleteFileW(percorsoTemporaneo & ":Zone.Identifier")

            If File.Exists(percorsoUpdater) Then
                File.Replace(percorsoTemporaneo, percorsoUpdater, Nothing, True)
            Else
                File.Move(percorsoTemporaneo, percorsoUpdater)
            End If

            DeleteFileW(percorsoUpdater & ":Zone.Identifier")

            Return True
        Catch ex As Exception
            ScriviLogAvvio("AggiornaAggiornatore eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    ''' <summary>Il canale degli aggiornamenti scelto dall'utente: "stable" (predefinito) o "beta".</summary>
    Friend Function CanaleAggiornamenti() As String
        Try
            Return RilascioFirmato.NormalizzaCanale(LeggiValoreConfigurazione(ID_CONFIG_CANALE_AGGIORNAMENTI))
        Catch
            Return RilascioFirmato.CANALE_STABILE
        End Try
    End Function

    Friend Sub ImpostaAvvioAggiornatore(info As ProcessStartInfo)
        If IntegrazioneWindows.EInstallato() AndAlso Not AvvioAmministratore.Elevato() Then
            info.UseShellExecute = True
            info.Verb = "runas"
        Else
            info.UseShellExecute = False
        End If
    End Sub

    Public Function AvviaAggiornatore(ripristino As Boolean, Optional ripara As Boolean = False) As Boolean
        Try
            Dim percorsoUpdater As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WGUpdater.exe")

            Try
                AggiornaAggiornatore()
            Catch exAggiornaUpdater As Exception
                ScriviLogAvvio("AvviaAggiornatore (aggiornamento aggiornatore): " & exAggiornaUpdater.Message)
            End Try

            If Not File.Exists(percorsoUpdater) Then
                Return False
            End If

            Try
                WGUtils.RimuoviMarkOfTheWeb(percorsoUpdater)
            Catch exMarchio As Exception
                ScriviLogAvvio("AvviaAggiornatore rimozione marchio: " & exMarchio.Message)
            End Try

            Dim infoAvvio As New ProcessStartInfo()
            infoAvvio.FileName = percorsoUpdater
            infoAvvio.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            ImpostaAvvioAggiornatore(infoAvvio)

            If ripristino Then
                infoAvvio.Arguments = "/ripristina"
            ElseIf ripara Then
                infoAvvio.Arguments = "/ripara"
            End If

            Process.Start(infoAvvio)
            Return True
        Catch ex As Exception
            ScriviLogAvvio("AvviaAggiornatore eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function PercorsoCollegamentoAvvioAutomatico() As String
        Try
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WinGabriel.lnk")
        Catch ex As Exception
            ScriviLogAvvio("PercorsoCollegamentoAvvioAutomatico eccezione: " & ex.Message)
            Return String.Empty
        End Try
    End Function

    Public Function AvvioAutomaticoAttivo() As Boolean
        Try
            Try
                Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(CHIAVE_REGISTRO_AVVIO, False)
                    If chiave IsNot Nothing Then
                        Dim valore As Object = chiave.GetValue(NOME_VALORE_AVVIO)

                        If valore IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(valore.ToString()) Then
                            Return True
                        End If
                    End If
                End Using
            Catch exRegistro As Exception
                ScriviLogAvvio("AvvioAutomaticoAttivo registro: " & exRegistro.Message)
            End Try

            Dim percorsoCollegamento As String = PercorsoCollegamentoAvvioAutomatico()

            If Not String.IsNullOrEmpty(percorsoCollegamento) AndAlso File.Exists(percorsoCollegamento) Then
                Return True
            End If

            Return False
        Catch ex As Exception
            ScriviLogAvvio("AvvioAutomaticoAttivo eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function StandbyDisattivatoDuranteUso() As Boolean
        Try
            Dim percorsoConfig As String = AvvioAmministratore.PercorsoConfigurazione()
            If Not File.Exists(percorsoConfig) Then Return True

            Dim documentoConfig As New XmlDocument()
            documentoConfig.Load(percorsoConfig)

            Dim nodo As XmlNode = documentoConfig.SelectSingleNode("/Configurazioni/Configurazione[@id='" & ID_CONFIG_DISATTIVA_STANDBY_USO & "']")
            If nodo Is Nothing Then Return True

            Return String.Equals(nodo.InnerText.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogAvvio("StandbyDisattivatoDuranteUso eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return True
        End Try
    End Function

    Public Function ImpostaAvvioAutomatico(attivo As Boolean) As Boolean
        Dim esito As Boolean = False

        Try
            Dim percorsoEseguibile As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wingabriel.exe")

            If attivo AndAlso Not File.Exists(percorsoEseguibile) Then
                Return False
            End If

            Try
                Using chiave As Microsoft.Win32.RegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(CHIAVE_REGISTRO_AVVIO)
                    If chiave IsNot Nothing Then
                        If attivo Then
                            chiave.SetValue(NOME_VALORE_AVVIO, """" & percorsoEseguibile & """", Microsoft.Win32.RegistryValueKind.String)
                        Else
                            If chiave.GetValue(NOME_VALORE_AVVIO) IsNot Nothing Then
                                chiave.DeleteValue(NOME_VALORE_AVVIO, False)
                            End If
                        End If

                        esito = True
                    End If
                End Using
            Catch exRegistro As Exception
                ScriviLogAvvio("ImpostaAvvioAutomatico registro: " & exRegistro.Message)
            End Try

            Try
                Dim percorsoCollegamento As String = PercorsoCollegamentoAvvioAutomatico()

                If String.IsNullOrEmpty(percorsoCollegamento) Then
                    Return esito
                End If

                If attivo Then
                    If Not esito Then
                        Dim shell As Object = CreateObject("WScript.Shell")
                        Dim collegamento As Object = shell.CreateShortcut(percorsoCollegamento)
                        collegamento.TargetPath = percorsoEseguibile
                        collegamento.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                        collegamento.Description = "Avvio automatico di WinGabriel"
                        collegamento.Save()
                        Marshal.ReleaseComObject(collegamento)
                        Marshal.ReleaseComObject(shell)

                        esito = File.Exists(percorsoCollegamento)
                    End If
                Else
                    If File.Exists(percorsoCollegamento) Then
                        File.Delete(percorsoCollegamento)
                    End If
                End If
            Catch exCollegamento As Exception
                ScriviLogAvvio("ImpostaAvvioAutomatico collegamento: " & exCollegamento.Message)
            End Try
        Catch ex As Exception
            ScriviLogAvvio("ImpostaAvvioAutomatico eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try

        Return esito
    End Function



    Friend Const ID_CONFIG_LETTORE_SCHERMO As String = "screen_reader_preferito"

    Friend Const VALORE_LETTORE_WINGABRIEL As String = "WinGabriel"

    Friend Const ID_CONFIG_ECO_CARATTERI_LETTORE As String = "lettore_schermo_eco_caratteri"

    Friend Const ID_CONFIG_ECO_PAROLE_LETTORE As String = "lettore_schermo_eco_parole"

    Friend Const ID_CONFIG_TASTO_LETTORE As String = "lettore_schermo_tasto"

    Friend Const ID_CONFIG_PASSWORD_LETTORE As String = "lettore_schermo_password"

    Friend Const ID_CONFIG_NAVIGAZIONE_WEB_LETTORE As String = "lettore_schermo_navigazione_web"

    Friend Const ID_CONFIG_REGIONI_LETTORE As String = "lettore_schermo_regioni"

    Friend Const ID_CONFIG_LIVELLO_TITOLI_LETTORE As String = "lettore_schermo_livello_titoli"

    Friend Const ID_CONFIG_CICLICA_LETTORE As String = "lettore_schermo_ciclica"

    Friend Const ID_CONFIG_LINK_PRIMA_LETTORE As String = "lettore_schermo_link_prima"

    Friend Const ID_CONFIG_COLONNE_LETTORE As String = "lettore_schermo_colonne"

    Friend Const ID_CONFIG_AVVISI_LETTORE As String = "lettore_schermo_avvisi"

    Friend Const ID_CONFIG_POSIZIONE_ELENCHI_LETTORE As String = "lettore_schermo_posizione_elenchi"

    Friend Function LettoreSchermoWinGabrielPreferito() As Boolean
        Try
            Dim preferito As String = LeggiValoreConfigurazione(ID_CONFIG_LETTORE_SCHERMO).Trim()
            If String.Equals(preferito, VALORE_LETTORE_WINGABRIEL, StringComparison.OrdinalIgnoreCase) Then Return True
            If preferito = "" OrElse String.Equals(preferito, "Automatico", StringComparison.OrdinalIgnoreCase) Then
                Return RilevatoreLettoreSchermo.RilevaConCache() = RilevatoreLettoreSchermo.NOME_WINGABRIEL
            End If
            Return False
        Catch ex As Exception
            ScriviLogAvvio("LettoreSchermoWinGabrielPreferito: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Friend Sub AggiornaLettoreRilevato()
        Try
            Dim preferito As String = LeggiValoreConfigurazione(ID_CONFIG_LETTORE_SCHERMO).Trim()
            If preferito <> "" AndAlso Not String.Equals(preferito, "Automatico", StringComparison.OrdinalIgnoreCase) Then Return
            Dim percorso As String = ""
            Dim trovato As String = RilevatoreLettoreSchermo.Rileva(percorso)
            If LeggiValoreConfigurazione(RilevatoreLettoreSchermo.ID_CONFIG_RILEVATO) <> trovato Then
                ScriviValoreConfigurazione(RilevatoreLettoreSchermo.ID_CONFIG_RILEVATO, trovato)
            End If
        Catch ex As Exception
            ScriviLogAvvio("AggiornaLettoreRilevato: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Friend Sub ApplicaLettoreSchermoWinGabriel()
        Try
            AggiornaLettoreRilevato()
            ' Il lettore di schermo è un programma a parte (WGLettore.exe): qui lo si avvia, gli si fanno rileggere le impostazioni
            ' oppure lo si chiude. Le impostazioni sono già nel config.xml, che il lettore legge da solo.
            If LettoreSchermoWinGabrielPreferito() Then
                LettoreEsterno.AvviaOAggiorna()
            Else
                LettoreEsterno.Chiudi()
            End If
        Catch ex As Exception
            ScriviLogAvvio("ApplicaLettoreSchermoWinGabriel: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub









    Friend Sub RegistraOriginiBackup()
        Try
            Dim radice As String = ""
            Try
                radice = WGDb.WGBackup.CartellaRadiceDati()
            Catch exRadice As Exception
                ScriviLogAvvio("RegistraOriginiBackup radice: " & exRadice.Message)
                radice = ""
            End Try

            Dim cartellaApplicazione As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati")

            Try
                Dim luoghi As String = Path.Combine(cartellaApplicazione, "luoghi")
                If Directory.Exists(luoghi) Then WGDb.WGBackup.RegistraOrigine("luoghi", luoghi)
            Catch exLuoghi As Exception
                ScriviLogAvvio("RegistraOriginiBackup luoghi: " & exLuoghi.Message)
            End Try

            Try
                Dim ftpStorico As String = Path.Combine(cartellaApplicazione, "Ftp")
                If Directory.Exists(ftpStorico) Then WGDb.WGBackup.RegistraOrigine("ftp", ftpStorico)
            Catch exFtp As Exception
                ScriviLogAvvio("RegistraOriginiBackup ftp: " & exFtp.Message)
            End Try
        Catch ex As Exception
            ScriviLogAvvio("RegistraOriginiBackup: " & ex.Message)
        End Try
    End Sub








    Friend Sub EseguiSuInterfacciaPrincipale(azione As MethodInvoker)
        Try
            If azione Is Nothing Then Return
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                If contenitore.InvokeRequired Then
                    contenitore.BeginInvoke(azione)
                Else
                    azione.Invoke()
                End If
                Return
            End If
            azione.Invoke()
        Catch ex As Exception
            ScriviLogAvvio("EseguiSuInterfacciaPrincipale: " & ex.Message)
        End Try
    End Sub

    Friend Sub ProgrammaAvvioWGService()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza

            If contenitore IsNot Nothing AndAlso Not contenitore.IsDisposed AndAlso contenitore.IsHandleCreated Then
                Try
                    contenitore.BeginInvoke(New MethodInvoker(AddressOf AvviaWGServiceSeNecessario))
                    Return
                Catch exAccoda As Exception
                    ScriviLogAvvio("ProgrammaAvvioWGService (accodamento): " & exAccoda.Message & Environment.NewLine & exAccoda.StackTrace)
                End Try
            End If

            AvviaWGServiceSeNecessario()
        Catch ex As Exception
            ScriviLogAvvio("ProgrammaAvvioWGService: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Try
                AvviaWGServiceSeNecessario()
            Catch exRipiego As Exception
                ScriviLogAvvio("ProgrammaAvvioWGService (ripiego): " & exRipiego.Message & Environment.NewLine & exRipiego.StackTrace)
            End Try
        End Try
    End Sub

    Friend Sub AvviaWGServiceSeNecessario()
        Try
            Dim pathWGService As String = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WGService.exe")
            If Not File.Exists(pathWGService) Then Return
            If Process.GetProcessesByName("WGService").Length > 0 Then Return

            WGUtils.RimuoviMarkOfTheWeb(pathWGService)

            Dim infoAvvioService As New ProcessStartInfo()
            infoAvvioService.FileName = pathWGService
            infoAvvioService.UseShellExecute = False
            Process.Start(infoAvvioService)
        Catch ex As Exception
            ScriviLogAvvio("AvviaWGServiceSeNecessario: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub RegistraBrowserWinGabriel()
        Try
            WGImpostazioniComuni.GestoreBrowserWinGabriel = Sub(indirizzo As String) Global.WGBrowser.WGBrowser.ApriBrowser(indirizzo)
            WGImpostazioniComuni.GestoreEmailWinGabriel = Sub(destinatari As String, oggetto As String, corpo As String)
                                                              Try
                                                                  Dim posta As New MenuPosta()
                                                                  posta.InviaMessaggioConRichiestaDati(destinatari, oggetto, corpo)
                                                              Catch exPosta As Exception
                                                                  ScriviLogAvvio("GestoreEmailWinGabriel: " & exPosta.Message & Environment.NewLine & exPosta.StackTrace)
                                                              End Try
                                                          End Sub
        Catch ex As Exception
            ScriviLogAvvio("RegistraBrowserWinGabriel: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub








    Friend Sub SincronizzaCloudPrimaDiUscire()
        Try
            If Not WGSincronizzazioneCloud.Attiva Then Return
            WGSincronizzazioneCloud.SincronizzaPrimaDellaChiusura()
        Catch ex As Exception
            ScriviLogAvvio("SincronizzaCloudPrimaDiUscire: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Sub GestisciCollegamentoDesktopIniziale()
        ' Con WinGabriel installato, un'icona che non avvia l'attività pianificata (per esempio una versione fissa) si ripara da sola.
        Try
            IntegrazioneWindows.RiparaIconeSeInstallato()
        Catch exRipara As Exception
            log.ScriviLog("GestisciCollegamentoDesktopIniziale (ripara icone): " & exRipara.Message)
        End Try
        Try
            Dim configManager As New WGConfig()

            Dim creaIconaDesktop As Boolean = False
            If Not configManager.EsisteId("crea_icona_desktop") Then
                If configManager.AggiungiValore("crea_icona_desktop", "false") Then
                    creaIconaDesktop = False
                End If
            Else
                If Boolean.TryParse(configManager.LeggiValore("crea_icona_desktop"), creaIconaDesktop) Then

                Else
                    creaIconaDesktop = False
                    configManager.ModificaValore("crea_icona_desktop", "false")
                End If
            End If

            If creaIconaDesktop Then
                GestisciCollegamentoDesktop(True)
            End If
        Catch ex As Exception
            log.ScriviLog("GestisciCollegamentoDesktopIniziale eccezione: " & ex.Message)
        End Try
    End Sub

    Friend Function CollegamentoDesktopIntegro(percorsoCollegamento As String) As Boolean
        Dim shell As Object = Nothing
        Dim collegamento As Object = Nothing
        Try
            If String.IsNullOrWhiteSpace(percorsoCollegamento) Then Return False
            If Not System.IO.File.Exists(percorsoCollegamento) Then Return False
            If New FileInfo(percorsoCollegamento).Length <= 0 Then Return False

            shell = CreateObject("WScript.Shell")
            collegamento = shell.CreateShortcut(percorsoCollegamento)

            Dim destinazione As String = Convert.ToString(collegamento.TargetPath)
            If String.IsNullOrWhiteSpace(destinazione) Then Return False

            Dim destinazioneEspansa As String = Environment.ExpandEnvironmentVariables(destinazione)
            If Not System.IO.File.Exists(destinazioneEspansa) Then Return False

            Return True
        Catch ex As Exception
            Try
                log.ScriviLog($"Errore durante il controllo del collegamento desktop, verrà ricreato: {ex.Message}")
            Catch
            End Try
            Return False
        Finally
            Try
                If collegamento IsNot Nothing Then Marshal.ReleaseComObject(collegamento)
            Catch
            End Try
            Try
                If shell IsNot Nothing Then Marshal.ReleaseComObject(shell)
            Catch
            End Try
        End Try
    End Function

    Friend Sub GestisciCollegamentoDesktop(creaShortcut As Boolean)
        Dim startupDir As String = AppDomain.CurrentDomain.BaseDirectory

        Dim shortcutName As String = "WinGabriel.lnk"
        Dim desktopPath As String = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        Dim shortcutPath As String = Path.Combine(desktopPath, shortcutName)
        Dim targetPath As String = Path.Combine(startupDir, "wingabriel.exe")

        ' Una copia portatile, con WinGabriel installato nel sistema, non tocca mai l'icona sul desktop: è dell'installazione (avvia l'attività
        ' pianificata, con i privilegi senza conferma). Né per crearla, né per cambiarla, né per toglierla, qualunque sia l'impostazione.
        Try
            If Not IntegrazioneWindows.AttivitaPuntaQui() AndAlso IntegrazioneWindows.InstallatoNelSistema() Then Return
        Catch
        End Try

        If creaShortcut Then
            Try
                If Not File.Exists(targetPath) Then

                    Return
                End If
                If IntegrazioneWindows.AttivitaPuntaQui() Then
                    If Not CollegamentoDesktopIntegro(shortcutPath) Then
                        IntegrazioneWindows.CreaIcona(shortcutPath)
                    End If
                    Return
                End If
                ' Sul desktop c'è già l'icona dell'installazione (avvia l'attività pianificata, con i privilegi senza conferma): una copia
                ' portatile non la sostituisce.
                If IntegrazioneWindows.CollegamentoDellAttivita(shortcutPath) Then Return
                Dim shell As Object = CreateObject("WScript.Shell")
                Dim shortcut As Object = shell.CreateShortcut(shortcutPath)
                shortcut.TargetPath = targetPath
                ' Se il collegamento c'era già con altri argomenti o altra icona (per esempio quello dell'attività) non devono restare.
                shortcut.Arguments = ""
                shortcut.IconLocation = targetPath & ",0"
                shortcut.WindowStyle = 1

                shortcut.WorkingDirectory = startupDir
                shortcut.Description = "Collegamento a WinGabriel"
                shortcut.Save()
                Marshal.ReleaseComObject(shortcut)
                Marshal.ReleaseComObject(shell)
            Catch ex As Exception

                log.ScriviLog($"Errore generico durante la creazione del collegamento desktop: {ex.Message}")
            End Try
        Else
            If System.IO.File.Exists(shortcutPath) AndAlso (IntegrazioneWindows.AttivitaPuntaQui() OrElse Not IntegrazioneWindows.CollegamentoDellAttivita(shortcutPath)) Then
                Try
                    System.IO.File.Delete(shortcutPath)
                Catch ex As Exception
                    log.ScriviLog($"Errore generico durante l'eliminazione del collegamento desktop: {ex.Message}")
                End Try
            End If
        End If
    End Sub

End Module
