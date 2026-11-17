Imports System
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq

''' <summary>
''' Accesso ai dati aperti Overture Maps, il catalogo di luoghi pubblicato dal consorzio
''' di Meta, Microsoft, Amazon e TomTom. Contiene molti piu' punti di interesse di
''' OpenStreetMap da solo e riporta telefono, sito, email e un punteggio di affidabilita'.
'''
''' Non usa alcun pacchetto NuGet: si appoggia all'eseguibile autonomo di DuckDB, che
''' viene scaricato una volta sola dal sito ufficiale alla prima ricerca e riusato sempre.
''' Essendo un processo separato funziona anche se il programma e' compilato a 32 bit.
''' I dati restano sul cloud: viene scaricata solo la porzione che serve al riquadro cercato.
''' Licenza dei dati: CDLA Permissive 2.0, che consente di salvarli e ridistribuirli.
''' </summary>
Public Class CLSOverture

#Region "Tipi pubblici"

    Public Delegate Sub Avanzamento(testo As String)

    Public Class LuogoOverture
        Public Property IdGers As String
        Public Property Nome As String
        Public Property Categoria As String
        Public Property Confidenza As Double
        Public Property Latitudine As Double
        Public Property Longitudine As Double
        Public Property Indirizzo As String
        Public Property Comune As String
        Public Property Cap As String
        Public Property Telefono As String
        Public Property Sito As String
        Public Property Email As String
        Public Property IdOsm As String
        Public Property Marca As String
    End Class

#End Region

#Region "Singleton e campi"

    Private Shared _istanza As CLSOverture
    Private Shared ReadOnly _lockIstanza As New Object()
    Private Shared ReadOnly _lockLog As New Object()

    Private Const URL_RELEASES As String = "https://labs.overturemaps.org/data/releases.json"
    Private Const RELEASE_DI_RISERVA As String = "2026-07-22.0"
    Private Const REGIONE_S3 As String = "us-west-2"

    Private Const URL_ULTIMA_DUCKDB As String = "https://api.github.com/repos/duckdb/duckdb/releases/latest"
    Private Const URL_DUCKDB_DI_RISERVA As String = "https://github.com/duckdb/duckdb/releases/download/v1.2.2/duckdb_cli-windows-amd64.zip"
    Private Const NOME_ARCHIVIO_DUCKDB As String = "duckdb_cli-windows-amd64.zip"

    Private ReadOnly _cartellaLavoro As String
    Private ReadOnly _cartellaProgramma As String
    Private ReadOnly _percorsoDuckDb As String
    Private ReadOnly _percorsoFileRelease As String
    Private ReadOnly _percorsoFileLog As String

    Private ReadOnly _lockRelease As New Object()
    Private ReadOnly _lockEseguibile As New Object()

    Private Shared ReadOnly _http As New HttpClient() With {
        .Timeout = TimeSpan.FromMinutes(5)
    }

    Private _releaseInUso As String = ""
    Private _releaseGiaRiprovata As Boolean = False
    Private _motoreNonDisponibile As Boolean = False

    Shared Sub New()
        Try
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0 (accessibilita)")
        Catch
        End Try
    End Sub

    Private Sub New()
        Try
            _cartellaLavoro = WGDb.WGDatabase.CartellaDati("luoghi")
            ' duckdb.exe è un programma: sta con il programma, non nei dati
            _cartellaProgramma = WGDb.WGPercorsiApplicazione.CartellaComponente("duckdb")
            _percorsoDuckDb = Path.Combine(_cartellaProgramma, "duckdb.exe")
            Try
                Dim vecchioDuckDb As String = Path.Combine(_cartellaLavoro, "duckdb.exe")
                If File.Exists(vecchioDuckDb) AndAlso Not File.Exists(_percorsoDuckDb) Then File.Move(vecchioDuckDb, _percorsoDuckDb)
            Catch
            End Try
            _percorsoFileRelease = Path.Combine(_cartellaLavoro, "release_overture.txt")
            _percorsoFileLog = Path.Combine(wgcontrols.Logger.CartellaLogApplicazione(), "log.txt")
        Catch
        End Try
    End Sub

    Public Shared Function GetInstance() As CLSOverture
        Try
            SyncLock _lockIstanza
                If _istanza Is Nothing Then _istanza = New CLSOverture()
                Return _istanza
            End SyncLock
        Catch
            Return _istanza
        End Try
    End Function

    ''' <summary>
    ''' Dice se il motore di interrogazione e' gia' presente sul computer. Se restituisce
    ''' falso, la prima ricerca comportera' uno scaricamento di alcune decine di megabyte.
    ''' </summary>
    Public Function MotorePronto() As Boolean
        Try
            Return Not String.IsNullOrWhiteSpace(_percorsoDuckDb) AndAlso File.Exists(_percorsoDuckDb)
        Catch
            Return False
        End Try
    End Function

#End Region

#Region "Eseguibile DuckDB"

    ''' <summary>
    ''' Assicura che l'eseguibile autonomo di DuckDB sia presente, scaricandolo dal sito
    ''' ufficiale la prima volta. Non richiede installazione ne' diritti di amministratore.
    ''' </summary>
    Private Function AssicuraEseguibile(avanzamento As Avanzamento) As Boolean
        Try
            SyncLock _lockEseguibile
                If MotorePronto() Then Return True

                Segnala(avanzamento, "Preparo il motore di ricerca dei luoghi, solo la prima volta")

                Dim url As String = TrovaUrlDuckDb()
                If url.Length = 0 Then url = URL_DUCKDB_DI_RISERVA

                Dim percorsoZip As String = Path.Combine(_cartellaProgramma, NOME_ARCHIVIO_DUCKDB)

                Try
                    If File.Exists(percorsoZip) Then File.Delete(percorsoZip)
                Catch
                End Try

                Segnala(avanzamento, "Scarico il motore di ricerca, attendi")
                If Not ScaricaFile(url, percorsoZip) Then
                    If url <> URL_DUCKDB_DI_RISERVA Then
                        If Not ScaricaFile(URL_DUCKDB_DI_RISERVA, percorsoZip) Then
                            Return False
                        End If
                    Else
                        Return False
                    End If
                End If

                Segnala(avanzamento, "Estraggo il motore di ricerca")
                If Not EstraiEseguibile(percorsoZip) Then
                    Return False
                End If

                Try
                    If File.Exists(percorsoZip) Then File.Delete(percorsoZip)
                Catch
                End Try

                Return MotorePronto()
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSOverture.AssicuraEseguibile: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function TrovaUrlDuckDb() As String
        Try
            Dim risposta = _http.GetAsync(URL_ULTIMA_DUCKDB).GetAwaiter().GetResult()
            If Not risposta.IsSuccessStatusCode Then Return ""

            Dim testo As String = risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            If String.IsNullOrWhiteSpace(testo) Then Return ""

            Dim radice As JObject = JObject.Parse(testo)
            Dim risorse = radice("assets")
            If risorse Is Nothing OrElse risorse.Type <> JTokenType.Array Then Return ""

            For Each risorsa As JToken In risorse.Children()
                Try
                    Dim nome As String = If(risorsa("name") Is Nothing, "", risorsa("name").ToString())
                    If String.Equals(nome, NOME_ARCHIVIO_DUCKDB, StringComparison.OrdinalIgnoreCase) Then
                        Dim indirizzo = risorsa("browser_download_url")
                        If indirizzo IsNot Nothing Then Return indirizzo.ToString()
                    End If
                Catch
                End Try
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSOverture.TrovaUrlDuckDb: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Estrae l'eseguibile dall'archivio zip leggendone direttamente la struttura.
    ''' Si evita di proposito la classe ZipFile, che richiederebbe di aggiungere al progetto
    ''' il riferimento System.IO.Compression.FileSystem: qui basta DeflateStream, che fa
    ''' parte di System.dll ed e' sempre disponibile.
    ''' </summary>
    Private Function EstraiEseguibile(percorsoZip As String) As Boolean
        Try
            If Not File.Exists(percorsoZip) Then Return False

            Using flusso As New FileStream(percorsoZip, FileMode.Open, FileAccess.Read)
                Using lettore As New BinaryReader(flusso)

                    Dim posizioneCoda As Long = TrovaCodaArchivio(flusso, lettore)
                    If posizioneCoda < 0 Then Return False

                    flusso.Seek(posizioneCoda + 10, SeekOrigin.Begin)
                    Dim numeroVoci As Integer = lettore.ReadUInt16()
                    lettore.ReadUInt32()
                    Dim inizioIndice As UInteger = lettore.ReadUInt32()

                    flusso.Seek(inizioIndice, SeekOrigin.Begin)

                    For i As Integer = 0 To numeroVoci - 1
                        Try
                            If lettore.ReadUInt32() <> &H2014B50UI Then Exit For

                            flusso.Seek(6, SeekOrigin.Current)
                            Dim metodo As Integer = lettore.ReadUInt16()
                            flusso.Seek(8, SeekOrigin.Current)
                            Dim dimensioneCompressa As UInteger = lettore.ReadUInt32()
                            Dim dimensioneOriginale As UInteger = lettore.ReadUInt32()
                            Dim lunghezzaNome As Integer = lettore.ReadUInt16()
                            Dim lunghezzaExtra As Integer = lettore.ReadUInt16()
                            Dim lunghezzaCommento As Integer = lettore.ReadUInt16()
                            flusso.Seek(8, SeekOrigin.Current)
                            Dim posizioneIntestazione As UInteger = lettore.ReadUInt32()

                            Dim byteNome As Byte() = lettore.ReadBytes(lunghezzaNome)
                            Dim nome As String = Encoding.UTF8.GetString(byteNome)

                            If lunghezzaExtra > 0 Then flusso.Seek(lunghezzaExtra, SeekOrigin.Current)
                            If lunghezzaCommento > 0 Then flusso.Seek(lunghezzaCommento, SeekOrigin.Current)

                            If Not nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then Continue For

                            Dim posizioneRipresa As Long = flusso.Position

                            If ScriviVoceEstratta(flusso, lettore, posizioneIntestazione, metodo, dimensioneCompressa, dimensioneOriginale) Then Return True

                            flusso.Seek(posizioneRipresa, SeekOrigin.Begin)
                        Catch exVoce As Exception
                            ScriviLog("CLSOverture.EstraiEseguibile voce: " & exVoce.Message)
                            Exit For
                        End Try
                    Next
                End Using
            End Using
            Return False
        Catch ex As Exception
            ScriviLog("CLSOverture.EstraiEseguibile: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function TrovaCodaArchivio(flusso As FileStream, lettore As BinaryReader) As Long
        Try
            Dim massimo As Integer = 66000
            Dim daLeggere As Integer = CInt(Math.Min(CLng(massimo), flusso.Length))
            flusso.Seek(-daLeggere, SeekOrigin.End)
            Dim coda As Byte() = lettore.ReadBytes(daLeggere)
            Dim inizio As Long = flusso.Length - daLeggere

            For i As Integer = coda.Length - 22 To 0 Step -1
                If coda(i) = &H50 AndAlso coda(i + 1) = &H4B AndAlso coda(i + 2) = &H5 AndAlso coda(i + 3) = &H6 Then
                    Return inizio + i
                End If
            Next
            Return -1
        Catch ex As Exception
            ScriviLog("CLSOverture.TrovaCodaArchivio: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function ScriviVoceEstratta(flusso As FileStream, lettore As BinaryReader,
                                        posizioneIntestazione As UInteger, metodo As Integer,
                                        dimensioneCompressa As UInteger, dimensioneOriginale As UInteger) As Boolean
        Try
            flusso.Seek(posizioneIntestazione, SeekOrigin.Begin)
            If lettore.ReadUInt32() <> &H4034B50UI Then Return False

            flusso.Seek(22, SeekOrigin.Current)
            Dim lunghezzaNome As Integer = lettore.ReadUInt16()
            Dim lunghezzaExtra As Integer = lettore.ReadUInt16()
            If lunghezzaNome > 0 Then flusso.Seek(lunghezzaNome, SeekOrigin.Current)
            If lunghezzaExtra > 0 Then flusso.Seek(lunghezzaExtra, SeekOrigin.Current)

            Dim dati As Byte() = lettore.ReadBytes(CInt(dimensioneCompressa))
            If dati Is Nothing OrElse dati.Length = 0 Then Return False

            If File.Exists(_percorsoDuckDb) Then File.Delete(_percorsoDuckDb)

            If metodo = 0 Then
                File.WriteAllBytes(_percorsoDuckDb, dati)
                Return True
            End If

            If metodo <> 8 Then
                Return False
            End If

            Using origine As New MemoryStream(dati)
                Using decompressore As New DeflateStream(origine, CompressionMode.Decompress)
                    Using destinazione As New FileStream(_percorsoDuckDb, FileMode.Create, FileAccess.Write)
                        Dim buffer(81919) As Byte
                        Dim letti As Integer = decompressore.Read(buffer, 0, buffer.Length)
                        While letti > 0
                            destinazione.Write(buffer, 0, letti)
                            letti = decompressore.Read(buffer, 0, buffer.Length)
                        End While
                    End Using
                End Using
            End Using

            Dim informazioni As New FileInfo(_percorsoDuckDb)
            If informazioni.Length < 1000000 Then
                Return False
            End If

            Return True
        Catch ex As Exception
            ScriviLog("CLSOverture.ScriviVoceEstratta: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function ScaricaFile(url As String, percorsoLocale As String) As Boolean
        Try
            Dim risposta = _http.GetAsync(url, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult()
            If Not risposta.IsSuccessStatusCode Then Return False

            Dim byteScaricati = risposta.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            If byteScaricati Is Nothing OrElse byteScaricati.Length < 10000 Then Return False

            File.WriteAllBytes(percorsoLocale, byteScaricati)
            Return True
        Catch ex As Exception
            ScriviLog("CLSOverture.ScaricaFile: " & ex.Message)
            Return False
        End Try
    End Function

#End Region

#Region "Release corrente"

    ''' <summary>
    ''' Overture rimuove le vecchie release dopo circa 60 giorni, quindi l'indirizzo dei dati
    ''' cambia periodicamente. Questa funzione lo ricava da sola dal file pubblico delle release,
    ''' cosi' non serve mai aggiornare un indirizzo a mano.
    ''' </summary>
    Public Function ReleaseCorrente() As String
        Try
            SyncLock _lockRelease
                If _releaseInUso.Length > 0 Then Return _releaseInUso

                Dim memorizzata As String = LeggiReleaseMemorizzata()
                If memorizzata.Length > 0 Then
                    _releaseInUso = memorizzata
                    Return _releaseInUso
                End If

                Dim scaricata As String = ScaricaReleasePiuRecente()
                If scaricata.Length > 0 Then
                    _releaseInUso = scaricata
                    SalvaReleaseMemorizzata(scaricata)
                    Return _releaseInUso
                End If

                _releaseInUso = RELEASE_DI_RISERVA
                Return _releaseInUso
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSOverture.ReleaseCorrente: " & ex.ToString())
            Return RELEASE_DI_RISERVA
        End Try
    End Function

    Private Sub DimenticaRelease()
        Try
            SyncLock _lockRelease
                _releaseInUso = ""
                Try
                    If Not String.IsNullOrWhiteSpace(_percorsoFileRelease) AndAlso File.Exists(_percorsoFileRelease) Then
                        File.Delete(_percorsoFileRelease)
                    End If
                Catch
                End Try
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSOverture.DimenticaRelease: " & ex.Message)
        End Try
    End Sub

    Private Function LeggiReleaseMemorizzata() As String
        Try
            If String.IsNullOrWhiteSpace(_percorsoFileRelease) OrElse Not File.Exists(_percorsoFileRelease) Then Return ""

            Dim righe = File.ReadAllLines(_percorsoFileRelease)
            If righe.Length < 2 Then Return ""

            Dim release As String = righe(0).Trim()
            Dim dataControllo As DateTime
            If Not DateTime.TryParse(righe(1).Trim(), dataControllo) Then Return ""

            ' Si ricontrolla ogni sette giorni: le release nuove escono circa una volta al mese.
            If (DateTime.Now - dataControllo).TotalDays > 7 Then Return ""
            Return release
        Catch ex As Exception
            ScriviLog("CLSOverture.LeggiReleaseMemorizzata: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub SalvaReleaseMemorizzata(release As String)
        Try
            If String.IsNullOrWhiteSpace(_percorsoFileRelease) Then Return
            File.WriteAllLines(_percorsoFileRelease, New String() {release, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")})
        Catch ex As Exception
            ScriviLog("CLSOverture.SalvaReleaseMemorizzata: " & ex.Message)
        End Try
    End Sub

    Private Function ScaricaReleasePiuRecente() As String
        Try
            Dim risposta = _http.GetAsync(URL_RELEASES).GetAwaiter().GetResult()
            If Not risposta.IsSuccessStatusCode Then Return ""

            Dim testo As String = risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            If String.IsNullOrWhiteSpace(testo) Then Return ""

            Dim radice As JObject = JObject.Parse(testo)
            Dim ultima = radice("latest")
            If ultima IsNot Nothing AndAlso ultima.Type = JTokenType.String Then
                Dim valore As String = ultima.ToString().Trim()
                If valore.Length > 0 Then Return valore
            End If

            Dim elenco = radice("releases")
            If elenco IsNot Nothing AndAlso elenco.Type = JTokenType.Array AndAlso elenco.HasValues Then
                Return elenco(0).ToString().Trim()
            End If

            Return ""
        Catch ex As Exception
            ScriviLog("CLSOverture.ScaricaReleasePiuRecente: " & ex.Message)
            Return ""
        End Try
    End Function

#End Region

#Region "Interrogazione"

    ''' <summary>
    ''' Cerca i luoghi dentro un riquadro geografico, filtrando per categorie Overture.
    ''' Passare un elenco vuoto di categorie per ottenere tutti i luoghi del riquadro.
    ''' </summary>
    Public Function CercaLuoghi(latitudineMinima As Double, latitudineMassima As Double,
                                longitudineMinima As Double, longitudineMassima As Double,
                                categorie As List(Of String),
                                Optional confidenzaMinima As Double = 0.4,
                                Optional massimo As Integer = 800,
                                Optional avanzamento As Avanzamento = Nothing) As List(Of LuogoOverture)
        Try
            ' Se il motore non e' disponibile non si riprova a ogni ricerca: sarebbe
            ' un tentativo di scaricamento lento e inutile ogni volta.
            If _motoreNonDisponibile Then Return New List(Of LuogoOverture)

            If Not AssicuraEseguibile(avanzamento) Then
                _motoreNonDisponibile = True
                Return New List(Of LuogoOverture)
            End If

            Dim risultato = EseguiRicerca(latitudineMinima, latitudineMassima, longitudineMinima, longitudineMassima, categorie, confidenzaMinima, massimo, "", False)

            ' Se non torna niente potrebbe essere una release scaduta, ma potrebbe anche
            ' essere che in quella zona non ci sia nulla di quella categoria. Si riprova
            ' una volta sola per sessione, non a ogni ricerca: altrimenti ogni ricerca
            ' senza risultati costerebbe il doppio del tempo.
            If (risultato Is Nothing OrElse risultato.Count = 0) AndAlso Not _releaseGiaRiprovata Then
                _releaseGiaRiprovata = True
                DimenticaRelease()
                risultato = EseguiRicerca(latitudineMinima, latitudineMassima, longitudineMinima, longitudineMassima, categorie, confidenzaMinima, massimo, "", False)
            End If

            Return If(risultato, New List(Of LuogoOverture))
        Catch ex As Exception
            ScriviLog("CLSOverture.CercaLuoghi: " & ex.ToString())
            Return New List(Of LuogoOverture)
        End Try
    End Function

    ''' <summary>
    ''' Cerca i luoghi dentro un riquadro il cui nome contiene il testo indicato.
    ''' </summary>
    Public Function CercaLuoghiPerNome(latitudineMinima As Double, latitudineMassima As Double,
                                       longitudineMinima As Double, longitudineMassima As Double,
                                       nome As String,
                                       Optional confidenzaMinima As Double = 0.3,
                                       Optional massimo As Integer = 400,
                                       Optional avanzamento As Avanzamento = Nothing) As List(Of LuogoOverture)
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return New List(Of LuogoOverture)
            If Not AssicuraEseguibile(avanzamento) Then
                Return New List(Of LuogoOverture)
            End If

            Dim risultato = EseguiRicerca(latitudineMinima, latitudineMassima, longitudineMinima, longitudineMassima, Nothing, confidenzaMinima, massimo, nome, True)
            Return If(risultato, New List(Of LuogoOverture))
        Catch ex As Exception
            ScriviLog("CLSOverture.CercaLuoghiPerNome: " & ex.ToString())
            Return New List(Of LuogoOverture)
        End Try
    End Function

    Private Function EseguiRicerca(latMin As Double, latMax As Double, lonMin As Double, lonMax As Double,
                                   categorie As List(Of String), confidenzaMinima As Double, massimo As Integer,
                                   nomeCercato As String, perNome As Boolean) As List(Of LuogoOverture)
        Dim elenco As New List(Of LuogoOverture)
        Try
            Dim release As String = ReleaseCorrente()
            If release.Length = 0 Then Return elenco

            Dim sql As String = ComponiQuery(release, latMin, latMax, lonMin, lonMax, categorie, confidenzaMinima, massimo, nomeCercato, perNome)
            If sql.Length = 0 Then Return elenco


            Dim risposta As String = EseguiDuckDb(sql)
            If String.IsNullOrWhiteSpace(risposta) Then
                Return elenco
            End If

            Dim inizioArray As Integer = risposta.IndexOf("["c)
            If inizioArray < 0 Then Return elenco
            Dim fineArray As Integer = risposta.LastIndexOf("]"c)
            If fineArray <= inizioArray Then Return elenco
            Dim json As String = risposta.Substring(inizioArray, fineArray - inizioArray + 1)

            Dim radice As JArray = JArray.Parse(json)
            For Each voce As JToken In radice.Children()
                Try
                    Dim l As New LuogoOverture()
                    l.IdGers = Testo(voce, "id")
                    l.Nome = Testo(voce, "nome")
                    l.Categoria = Testo(voce, "categoria")
                    l.Confidenza = Numero(voce, "affidabilita")
                    l.Longitudine = Numero(voce, "longitudine")
                    l.Latitudine = Numero(voce, "latitudine")
                    l.Indirizzo = Testo(voce, "indirizzo")
                    l.Comune = Testo(voce, "comune")
                    l.Cap = Testo(voce, "cap")
                    l.Telefono = Testo(voce, "telefono")
                    l.Sito = Testo(voce, "sito")
                    l.Email = Testo(voce, "email")
                    l.IdOsm = Testo(voce, "id_osm")
                    l.Marca = Testo(voce, "marca")

                    If l.Nome.Length > 0 Then elenco.Add(l)
                Catch exVoce As Exception
                    ScriviLog("CLSOverture.EseguiRicerca voce: " & exVoce.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSOverture.EseguiRicerca: " & ex.ToString())
        End Try
        Return elenco
    End Function

    ''' <summary>
    ''' Manda le istruzioni a DuckDB attraverso l'ingresso standard, cosi' non ci sono
    ''' problemi di virgolette o di lunghezza massima della riga di comando, e ne raccoglie
    ''' la risposta in formato JSON.
    ''' </summary>
    Private Function EseguiDuckDb(sql As String) As String
        Try
            If Not MotorePronto() Then Return ""

            Dim informazioni As New ProcessStartInfo()
            informazioni.FileName = _percorsoDuckDb
            informazioni.Arguments = "-json -batch"
            informazioni.WorkingDirectory = _cartellaLavoro
            informazioni.UseShellExecute = False
            informazioni.CreateNoWindow = True
            informazioni.RedirectStandardInput = True
            informazioni.RedirectStandardOutput = True
            informazioni.RedirectStandardError = True
            informazioni.StandardOutputEncoding = Encoding.UTF8

            Using processo As New Process()
                processo.StartInfo = informazioni
                If Not processo.Start() Then Return ""

                Try
                    processo.StandardInput.WriteLine("INSTALL httpfs;")
                    processo.StandardInput.WriteLine("LOAD httpfs;")
                    processo.StandardInput.WriteLine("SET s3_region='" & REGIONE_S3 & "';")
                    processo.StandardInput.WriteLine(sql & ";")
                    processo.StandardInput.Flush()
                    processo.StandardInput.Close()
                Catch exIngresso As Exception
                    ScriviLog("CLSOverture.EseguiDuckDb ingresso: " & exIngresso.ToString())
                End Try

                ' I due flussi vanno svuotati insieme: leggere prima tutta l'uscita e poi
                ' gli errori blocca DuckDB quando il buffer dell'altro flusso si riempie.
                Dim letturaUscita As System.Threading.Tasks.Task(Of String) = processo.StandardOutput.ReadToEndAsync()
                Dim letturaErrori As System.Threading.Tasks.Task(Of String) = processo.StandardError.ReadToEndAsync()

                If Not processo.WaitForExit(75000) Then
                    Try
                        processo.Kill()
                    Catch
                    End Try
                    Return ""
                End If

                Dim uscita As String = ""
                Dim errori As String = ""
                Try
                    If letturaUscita.Wait(10000) Then uscita = If(letturaUscita.Result, "")
                Catch exLettura As Exception
                    ScriviLog("CLSOverture.EseguiDuckDb lettura: " & exLettura.Message)
                End Try
                Try
                    If letturaErrori.Wait(2000) Then errori = If(letturaErrori.Result, "")
                Catch
                End Try
                If processo.HasExited AndAlso processo.ExitCode <> 0 Then
                    ScriviLog("CLSOverture.EseguiDuckDb: codice " & processo.ExitCode.ToString() & " " & errori)
                End If

                Return uscita
            End Using
        Catch ex As Exception
            ScriviLog("CLSOverture.EseguiDuckDb: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ComponiQuery(release As String, latMin As Double, latMax As Double, lonMin As Double, lonMax As Double,
                                  categorie As List(Of String), confidenzaMinima As Double, massimo As Integer,
                                  nomeCercato As String, perNome As Boolean) As String
        Dim sql As New StringBuilder()
        Try
            Dim percorso As String = "s3://overturemaps-" & REGIONE_S3 & "/release/" & release & "/theme=places/type=place/*"

            sql.Append("SELECT ")
            sql.Append("id, ")
            sql.Append("COALESCE(names.primary, '') AS nome, ")
            sql.Append("COALESCE(categories.primary, '') AS categoria, ")
            sql.Append("COALESCE(confidence, 0) AS affidabilita, ")
            sql.Append("bbox.xmin AS longitudine, ")
            sql.Append("bbox.ymin AS latitudine, ")
            sql.Append("COALESCE(TRY(addresses[1].freeform), '') AS indirizzo, ")
            sql.Append("COALESCE(TRY(addresses[1].locality), '') AS comune, ")
            sql.Append("COALESCE(TRY(addresses[1].postcode), '') AS cap, ")
            sql.Append("COALESCE(TRY(phones[1]), '') AS telefono, ")
            sql.Append("COALESCE(TRY(websites[1]), '') AS sito, ")
            sql.Append("COALESCE(TRY(emails[1]), '') AS email, ")
            sql.Append("COALESCE(TRY(list_filter(sources, x -> x.dataset ILIKE '%openstreetmap%')[1].record_id), '') AS id_osm, ")
            sql.Append("COALESCE(TRY(brand.names.primary), '') AS marca ")
            sql.Append("FROM read_parquet('").Append(percorso).Append("', hive_partitioning=1) ")

            sql.Append("WHERE bbox.xmin BETWEEN ").Append(Num(lonMin)).Append(" AND ").Append(Num(lonMax)).Append(" ")
            sql.Append("AND bbox.ymin BETWEEN ").Append(Num(latMin)).Append(" AND ").Append(Num(latMax)).Append(" ")
            sql.Append("AND COALESCE(confidence, 0) >= ").Append(Num(confidenzaMinima)).Append(" ")

            If perNome Then
                sql.Append("AND names.primary ILIKE '%").Append(PulisciTesto(nomeCercato)).Append("%' ")
            ElseIf categorie IsNot Nothing AndAlso categorie.Count > 0 Then
                Dim lista As New List(Of String)
                For Each c In categorie
                    Dim pulita As String = PulisciTesto(c)
                    If pulita.Length > 0 Then lista.Add("'" & pulita & "'")
                Next
                If lista.Count > 0 Then
                    ' Solo la categoria principale: le categorie alternative facevano comparire
                    ' luoghi fuori tema, per esempio una profumeria fra i ristoranti.
                    Dim insieme As String = String.Join(",", lista)
                    sql.Append("AND categories.primary IN (").Append(insieme).Append(") ")
                End If
            End If

            sql.Append("ORDER BY confidence DESC ")
            sql.Append("LIMIT ").Append(Math.Max(1, massimo).ToString(CultureInfo.InvariantCulture))
        Catch ex As Exception
            ScriviLog("CLSOverture.ComponiQuery: " & ex.ToString())
            Return ""
        End Try
        Return sql.ToString()
    End Function

#End Region

#Region "Utilita"

    Private Function Testo(voce As JToken, nome As String) As String
        Try
            If voce Is Nothing Then Return ""
            Dim v = voce(nome)
            If v Is Nothing OrElse v.Type = JTokenType.Null Then Return ""
            Return v.ToString().Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Function Numero(voce As JToken, nome As String) As Double
        Try
            If voce Is Nothing Then Return 0
            Dim v = voce(nome)
            If v Is Nothing OrElse v.Type = JTokenType.Null Then Return 0
            Dim r As Double = 0
            If Double.TryParse(v.ToString().Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, r) Then Return r
            Return 0
        Catch
            Return 0
        End Try
    End Function

    Private Function Num(valore As Double) As String
        Try
            Return valore.ToString("R", CultureInfo.InvariantCulture)
        Catch
            Return "0"
        End Try
    End Function

    Private Function PulisciTesto(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim s As String = testo.Trim()
            s = s.Replace("'", " ").Replace("""", " ").Replace(";", " ").Replace("\", " ")
            s = s.Replace("--", " ")
            Return s.Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Sub Segnala(avanzamento As Avanzamento, testo As String)
        Try
            If avanzamento IsNot Nothing Then avanzamento(testo)
        Catch
        End Try
    End Sub

    Private Sub ScriviLog(messaggio As String)
        Try
            SyncLock _lockLog
                Try
                    If String.IsNullOrWhiteSpace(_percorsoFileLog) Then Return
                    Dim cartella As String = Path.GetDirectoryName(_percorsoFileLog)
                    If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                        Directory.CreateDirectory(cartella)
                    End If
                    File.AppendAllText(_percorsoFileLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & messaggio & Environment.NewLine, Encoding.UTF8)
                Catch
                End Try
            End SyncLock
        Catch
        End Try
    End Sub

#End Region

End Class