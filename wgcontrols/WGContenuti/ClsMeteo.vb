Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.Globalization
Imports System.Net.Http
Imports Newtonsoft.Json.Linq
Imports WGDb

' Gestione delle previsioni meteo e dei dati marini tramite database locale, con ricerca delle località e dei comuni.
Public Class CLSMeteo

    Private Shared ReadOnly logger As New wgcontrols.Logger()
    Private Shared ReadOnly _client As HttpClient
    Private ReadOnly _connectionString As String
    Private ReadOnly _percorsoDb As String
    Private Const TentativiRichiestaDefault As Integer = 3
    Private Const AttesaBaseRetryMs As Integer = 500

    Shared Sub New()
        Try
            _client = New HttpClient()
            _client.Timeout = TimeSpan.FromSeconds(20)
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Meteo/2.0 (accessibilita; postmaster@wingabriel.net)")
            _client.DefaultRequestHeaders.Add("Accept", "application/json")
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.SharedNew - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Friend Const OpenMeteoGeocodingUrlPublic As String = "https://geocoding-api.open-meteo.com/v1/search?name={0}&count=1&language=it&format=json"
    Private Const MetNoForecastUrl As String = "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,relative_humidity_2m,precipitation,weather_code,cloud_cover,surface_pressure,wind_speed_10m,wind_direction_10m&timezone=auto"
    Private Const OpenMeteoLegacyUrl As String = "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current_weather=true&timezone=auto"
    Private Const OpenMeteoDailyUrl As String = "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,wind_direction_10m_dominant&timezone=auto&forecast_days={2}"
    Private Const OpenMeteoHourlyUrl As String = "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&hourly=temperature_2m,apparent_temperature,relative_humidity_2m,dew_point_2m,precipitation,precipitation_probability,weather_code,pressure_msl,cloud_cover,visibility,wind_speed_10m,wind_direction_10m,wind_gusts_10m,uv_index&forecast_days={2}&timezone=auto"
    Private Const OpenMeteoMarineCurrentUrl As String = "https://marine-api.open-meteo.com/v1/marine?latitude={0}&longitude={1}&current=wave_height,wave_direction,wave_period,sea_surface_temperature,swell_wave_height&timezone=auto"
    Private Const OpenMeteoMarineHourlyUrl As String = "https://marine-api.open-meteo.com/v1/marine?latitude={0}&longitude={1}&hourly=wave_height,wave_direction,wave_period,sea_surface_temperature&forecast_days=1&timezone=auto"

    Public Sub New(Optional percorsoDb As String = "")
        Try
            If String.IsNullOrWhiteSpace(percorsoDb) Then
                Try
                    percorsoDb = WGDatabase.PercorsoFileDati("meteo", "comuni.db")
                Catch
                    percorsoDb = ""
                End Try
                If String.IsNullOrWhiteSpace(percorsoDb) Then
                    percorsoDb = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dati", "meteo", "comuni.db")
                End If
            End If
            Dim cartellaDb As String = IO.Path.GetDirectoryName(percorsoDb)
            If Not String.IsNullOrWhiteSpace(cartellaDb) AndAlso Not System.IO.Directory.Exists(cartellaDb) Then
                System.IO.Directory.CreateDirectory(cartellaDb)
            End If
            _connectionString = $"Data Source={percorsoDb};Version=3;"
            _percorsoDb = percorsoDb
            InizializzaDatabase()
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.New - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Sub InizializzaDatabase()
        Try
            Dim conn As SQLiteConnection = Nothing
            If Not String.IsNullOrWhiteSpace(_percorsoDb) Then
                conn = WGDatabase.ApriConnessione(_percorsoDb)
            End If
            If conn Is Nothing Then
                conn = New SQLiteConnection(_connectionString)
                conn.Open()
            End If
            Using conn
                Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS ComuniPreferiti (IdComune TEXT PRIMARY KEY, NomeComune TEXT NOT NULL, Lat REAL NOT NULL DEFAULT 0, Lon REAL NOT NULL DEFAULT 0, Paese TEXT NOT NULL DEFAULT '', Regione TEXT NOT NULL DEFAULT '', UltimoUso TEXT NOT NULL);", conn)
                    cmd.ExecuteNonQuery()
                End Using

                Using cmd As New SQLiteCommand("CREATE TABLE IF NOT EXISTS ComuniRecenti (IdComune TEXT PRIMARY KEY, NomeComune TEXT NOT NULL, Lat REAL NOT NULL DEFAULT 0, Lon REAL NOT NULL DEFAULT 0, Paese TEXT NOT NULL DEFAULT '', Regione TEXT NOT NULL DEFAULT '', UltimoUso TEXT NOT NULL);", conn)
                    cmd.ExecuteNonQuery()
                End Using

                Try
                    WGDatabase.AssicuraColonnaChiave(conn, "ComuniPreferiti")
                    WGDatabase.AssicuraColonnaChiave(conn, "ComuniRecenti")
                Catch exChiave As Exception
                    logger.ScriviLog("CLSMeteo.InizializzaDatabase chiave - " & exChiave.Message)
                End Try
            End Using
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.InizializzaDatabase - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Async Function GetStringConRetry(url As String, nomeOperazione As String, Optional tentativiMax As Integer = TentativiRichiestaDefault) As Task(Of String)
        Dim ultimoErrore As String = ""
        Try
            If tentativiMax < 1 Then tentativiMax = 1

            For tentativo As Integer = 1 To tentativiMax
                Try
                    Using risposta As HttpResponseMessage = Await _client.GetAsync(url)
                        Dim corpo As String = Await risposta.Content.ReadAsStringAsync()
                        If risposta.IsSuccessStatusCode Then
                            Return corpo
                        End If

                        ultimoErrore = $"HTTP {CInt(risposta.StatusCode)} - {corpo}"

                        Dim codiceStato As Integer = CInt(risposta.StatusCode)
                        If codiceStato < 500 AndAlso codiceStato <> 429 Then
                            Return Nothing
                        End If
                    End Using
                Catch ex As TaskCanceledException
                    ultimoErrore = "Timeout richiesta - " & ex.Message
                    logger.ScriviLog($"CLSMeteo.GetStringConRetry ({nomeOperazione}) tentativo {tentativo}/{tentativiMax} - {ultimoErrore} - URL: {url}")
                Catch ex As HttpRequestException
                    ultimoErrore = ex.Message
                    logger.ScriviLog($"CLSMeteo.GetStringConRetry ({nomeOperazione}) tentativo {tentativo}/{tentativiMax} - {ultimoErrore} - URL: {url}" & Environment.NewLine & ex.StackTrace)
                Catch ex As Exception
                    ultimoErrore = ex.Message
                    logger.ScriviLog($"CLSMeteo.GetStringConRetry ({nomeOperazione}) tentativo {tentativo}/{tentativiMax} - {ultimoErrore} - URL: {url}" & Environment.NewLine & ex.StackTrace)
                End Try

                If tentativo < tentativiMax Then
                    Await Task.Delay(AttesaBaseRetryMs * tentativo)
                End If
            Next

        Catch ex As Exception
            logger.ScriviLog($"CLSMeteo.GetStringConRetry ({nomeOperazione}) - errore imprevisto - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return Nothing
    End Function

    Public Structure CoordinateCitta
        Public IdComune As String
        Public Lat As Double
        Public Lon As Double
        Public NomeRisolto As String
        Public Paese As String
        Public Regione As String
    End Structure

    Public Class ComuneInfo
        Public Property IdComune As String = ""
        Public Property NomeComune As String = ""
        Public Property Lat As Double
        Public Property Lon As Double
        Public Property Paese As String = ""
        Public Property Regione As String = ""
        Public Property UltimoUso As DateTime = DateTime.MinValue
    End Class

    Public Class InfoMeteo
        Public Property Localita As String = ""
        Public Property Condizioni As String = ""
        Public Property Temperatura As String = ""
        Public Property Umidita As String = ""
        Public Property Vento As String = ""
        Public Property DirezioneVento As String = ""
        Public Property Pressione As String = ""
        Public Property Nuvolosita As String = ""
        Public Property Precipitazioni As String = ""
        Public Property Aggiornamento As String = ""
        Public Property Valido As Boolean = False
        Public Property Messaggio As String = ""
    End Class

    Public Class InfoMeteoGiorno
        Public Property DataIso As String = ""
        Public Property Giorno As String = ""
        Public Property DataCompleta As String = ""
        Public Property Condizioni As String = ""
        Public Property TempMin As String = ""
        Public Property TempMax As String = ""
        Public Property Precipitazioni As String = ""
        Public Property ProbabilitaPioggia As String = ""
        Public Property Vento As String = ""
        Public Property DirezioneVento As String = ""
    End Class

    Public Class InfoMeteoOra
        Public Property Id As String = ""
        Public Property Ora As String = ""
        Public Property Condizioni As String = ""
        Public Property Temperatura As String = ""
        Public Property TemperaturaPercepita As String = ""
        Public Property Umidita As String = ""
        Public Property PuntoDiRugiada As String = ""
        Public Property ProbabilitaPioggia As String = ""
        Public Property Precipitazioni As String = ""
        Public Property Pressione As String = ""
        Public Property Nuvolosita As String = ""
        Public Property Vento As String = ""
        Public Property DirezioneVento As String = ""
        Public Property RaffficheVento As String = ""
        Public Property IndiceUV As String = ""
        Public Property Visibilita As String = ""
    End Class

    Public Class PuntoMarino
        Public Property Valido As Boolean = False
        Public Property Lat As Double = 0
        Public Property Lon As Double = 0
        Public Property DistanzaKm As Double = 0
        Public Property Direzione As String = ""
        Public Property Messaggio As String = ""
    End Class

    Public Class InfoMeteoMarino
        Public Property Valido As Boolean = False
        Public Property DistanzaKm As Double = 0
        Public Property Direzione As String = ""
        Public Property AltezzaOnde As String = ""
        Public Property DirezioneOnde As String = ""
        Public Property PeriodoOnde As String = ""
        Public Property AltezzaMareggiata As String = ""
        Public Property TemperaturaMare As String = ""
        Public Property Aggiornamento As String = ""
        Public Property Messaggio As String = ""
    End Class

    Public Class InfoMeteoMarinoOra
        Public Property Id As String = ""
        Public Property Ora As String = ""
        Public Property AltezzaOnde As String = ""
        Public Property DirezioneOnde As String = ""
        Public Property PeriodoOnde As String = ""
        Public Property TemperaturaMare As String = ""
    End Class

    Public Async Function RisolviCoordinate(lat As Double, lon As Double, Optional nomeCitta As String = "") As Task(Of CoordinateCitta)
        Dim risultato As New CoordinateCitta With {
            .IdComune = "",
            .Lat = lat,
            .Lon = lon,
            .NomeRisolto = "",
            .Paese = "",
            .Regione = ""
        }

        If String.IsNullOrWhiteSpace(nomeCitta) Then
            Return risultato
        End If

        Try
            Dim url As String = String.Format(OpenMeteoGeocodingUrlPublic, Uri.EscapeDataString(nomeCitta.Trim()))
            Dim json As String = Await GetStringConRetry(url, "RisolviCoordinate")
            If json Is Nothing Then
                Return risultato
            End If

            Dim obj As JObject = JObject.Parse(json)
            Dim results As JArray = TryCast(obj("results"), JArray)

            If results Is Nothing OrElse results.Count = 0 Then
                Return risultato
            End If

            Dim primo As JToken = results(0)
            Dim latVal As Double? = JsonDouble(primo("latitude"))
            Dim lonVal As Double? = JsonDouble(primo("longitude"))

            If latVal.HasValue AndAlso lonVal.HasValue Then
                risultato.Lat = latVal.Value
                risultato.Lon = lonVal.Value
            End If

            risultato.IdComune = If(primo("id")?.ToString(), "")
            If String.IsNullOrWhiteSpace(risultato.IdComune) Then
                risultato.IdComune = CreaIdComuneFallback(nomeCitta)
            End If

            risultato.NomeRisolto = If(primo("name")?.ToString(), "")
            risultato.Paese = If(primo("country")?.ToString(), "")
            risultato.Regione = If(primo("admin1")?.ToString(), "")

        Catch ex As HttpRequestException
            logger.ScriviLog("CLSMeteo.RisolviCoordinate (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.RisolviCoordinate - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try

        Return risultato
    End Function

    Public Async Function OttieniMeteo(lat As Double, lon As Double, Optional nomeCitta As String = "") As Task(Of InfoMeteo)
        Dim info As New InfoMeteo()
        Try
            Dim coord As CoordinateCitta = Await RisolviCoordinate(lat, lon, nomeCitta)

            Dim localita As String
            If Not String.IsNullOrWhiteSpace(coord.NomeRisolto) Then
                localita = coord.NomeRisolto
                If Not String.IsNullOrWhiteSpace(coord.Regione) Then localita &= ", " & coord.Regione
                If Not String.IsNullOrWhiteSpace(coord.Paese) Then localita &= " (" & coord.Paese & ")"
            Else
                localita = coord.Lat.ToString("F4", CultureInfo.InvariantCulture) & ", " & coord.Lon.ToString("F4", CultureInfo.InvariantCulture)
            End If
            info.Localita = localita

            Dim latVal As Double = coord.Lat
            Dim lonVal As Double = coord.Lon
            If latVal < -90 OrElse latVal > 90 OrElse lonVal < -180 OrElse lonVal > 180 OrElse (latVal = 0 AndAlso lonVal = 0) Then
                info.Valido = False
                info.Messaggio = "Posizione non disponibile. Indicare una città valida."
                Return info
            End If

            Dim latStr As String = latVal.ToString("0.####", CultureInfo.InvariantCulture)
            Dim lonStr As String = lonVal.ToString("0.####", CultureInfo.InvariantCulture)

            Dim urlRicco As String = String.Format(MetNoForecastUrl, latStr, lonStr)
            Dim jsonRicco As String = Await GetStringConRetry(urlRicco, "OttieniMeteo (richiesta completa)")

            If jsonRicco IsNot Nothing Then
                Dim obj As JObject = JObject.Parse(jsonRicco)
                Dim cur As JToken = obj("current")
                If cur IsNot Nothing Then
                    Dim temp As Double? = JsonDouble(cur("temperature_2m"))
                    If temp.HasValue Then info.Temperatura = FormatNumeroIT(temp.Value, 1, "°C")

                    Dim umid As Double? = JsonDouble(cur("relative_humidity_2m"))
                    If umid.HasValue Then info.Umidita = FormatNumeroIT(umid.Value, 0, "%")

                    Dim vento As Double? = JsonDouble(cur("wind_speed_10m"))
                    If vento.HasValue Then info.Vento = FormatNumeroIT(vento.Value, 0, "km/h")

                    info.DirezioneVento = TraduzioneDirezVento(JsonDouble(cur("wind_direction_10m")))

                    Dim press As Double? = JsonDouble(cur("surface_pressure"))
                    If press.HasValue Then info.Pressione = FormatNumeroIT(press.Value, 0, "hPa")

                    Dim nuvole As Double? = JsonDouble(cur("cloud_cover"))
                    If nuvole.HasValue Then info.Nuvolosita = FormatNumeroIT(nuvole.Value, 0, "%")

                    Dim wCode As Double? = JsonDouble(cur("weather_code"))
                    info.Condizioni = DescriviCodiceMeteo(If(wCode.HasValue, CInt(wCode.Value), -1))

                    Dim prec As Double? = JsonDouble(cur("precipitation"))
                    info.Precipitazioni = FormatNumeroIT(If(prec.HasValue, prec.Value, 0), 1, "mm")

                    info.Aggiornamento = FormatAggiornamento(cur("time")?.ToString())
                    info.Valido = True
                    Return info
                End If
            End If

            Dim urlBase As String = String.Format(OpenMeteoLegacyUrl, latStr, lonStr)
            Dim corpoBase As String = Await GetStringConRetry(urlBase, "OttieniMeteo (fallback)")
            If corpoBase Is Nothing Then
                info.Valido = False
                info.Messaggio = "Dati meteo non disponibili al momento. Riprovare più tardi."
                Return info
            End If

            Dim objBase As JObject = JObject.Parse(corpoBase)
            Dim cw As JToken = objBase("current_weather")
            If cw Is Nothing Then
                info.Valido = False
                info.Messaggio = "Dati meteo non disponibili."
                Return info
            End If

            Dim tempBase As Double? = JsonDouble(cw("temperature"))
            If tempBase.HasValue Then info.Temperatura = FormatNumeroIT(tempBase.Value, 1, "°C")

            Dim ventoBase As Double? = JsonDouble(cw("windspeed"))
            If ventoBase.HasValue Then info.Vento = FormatNumeroIT(ventoBase.Value, 0, "km/h")

            info.DirezioneVento = TraduzioneDirezVento(JsonDouble(cw("winddirection")))

            Dim wCodeBase As Double? = JsonDouble(cw("weathercode"))
            info.Condizioni = DescriviCodiceMeteo(If(wCodeBase.HasValue, CInt(wCodeBase.Value), -1))

            info.Aggiornamento = FormatAggiornamento(cw("time")?.ToString())
            info.Valido = True

        Catch ex As HttpRequestException
            logger.ScriviLog("CLSMeteo.OttieniMeteo (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            info.Valido = False
            info.Messaggio = "Errore di connessione durante il recupero del meteo."
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniMeteo - " & ex.Message & Environment.NewLine & ex.StackTrace)
            info.Valido = False
            info.Messaggio = "Errore durante il recupero del meteo."
        End Try
        Return info
    End Function

    Public Async Function OttieniPrevisione(lat As Double, lon As Double, Optional nomeCitta As String = "", Optional giorni As Integer = 7) As Task(Of List(Of InfoMeteoGiorno))
        Dim lista As New List(Of InfoMeteoGiorno)()
        Try
            Dim coord As CoordinateCitta = Await RisolviCoordinate(lat, lon, nomeCitta)
            Dim latVal As Double = coord.Lat
            Dim lonVal As Double = coord.Lon
            If latVal < -90 OrElse latVal > 90 OrElse lonVal < -180 OrElse lonVal > 180 OrElse (latVal = 0 AndAlso lonVal = 0) Then
                Return lista
            End If

            If giorni < 1 Then giorni = 1
            If giorni > 16 Then giorni = 16

            Dim latStr As String = latVal.ToString("0.####", CultureInfo.InvariantCulture)
            Dim lonStr As String = lonVal.ToString("0.####", CultureInfo.InvariantCulture)
            Dim url As String = String.Format(OpenMeteoDailyUrl, latStr, lonStr, giorni)

            Dim json As String = Await GetStringConRetry(url, "OttieniPrevisione")
            If json Is Nothing Then Return lista

            Dim obj As JObject = JObject.Parse(json)
            Dim daily As JToken = obj("daily")
            If daily Is Nothing Then Return lista

            Dim tempi As JArray = TryCast(daily("time"), JArray)
            If tempi Is Nothing OrElse tempi.Count = 0 Then Return lista

            Dim codici As JArray = TryCast(daily("weather_code"), JArray)
            Dim tmax As JArray = TryCast(daily("temperature_2m_max"), JArray)
            Dim tmin As JArray = TryCast(daily("temperature_2m_min"), JArray)
            Dim precSum As JArray = TryCast(daily("precipitation_sum"), JArray)
            Dim precProb As JArray = TryCast(daily("precipitation_probability_max"), JArray)
            Dim ventoMax As JArray = TryCast(daily("wind_speed_10m_max"), JArray)
            Dim ventoDir As JArray = TryCast(daily("wind_direction_10m_dominant"), JArray)

            For i As Integer = 0 To tempi.Count - 1
                Dim g As New InfoMeteoGiorno()
                g.DataIso = tempi(i)?.ToString()
                g.Giorno = FormatGiornoBreve(tempi(i)?.ToString())
                g.DataCompleta = FormatDataCompleta(tempi(i)?.ToString())

                If codici IsNot Nothing AndAlso i < codici.Count Then
                    Dim wc As Double? = JsonDouble(codici(i))
                    g.Condizioni = DescriviCodiceMeteo(If(wc.HasValue, CInt(wc.Value), -1))
                End If
                If tmin IsNot Nothing AndAlso i < tmin.Count Then
                    Dim v As Double? = JsonDouble(tmin(i))
                    If v.HasValue Then g.TempMin = FormatNumeroIT(v.Value, 1, "°C")
                End If
                If tmax IsNot Nothing AndAlso i < tmax.Count Then
                    Dim v As Double? = JsonDouble(tmax(i))
                    If v.HasValue Then g.TempMax = FormatNumeroIT(v.Value, 1, "°C")
                End If
                If precSum IsNot Nothing AndAlso i < precSum.Count Then
                    Dim v As Double? = JsonDouble(precSum(i))
                    If v.HasValue Then g.Precipitazioni = FormatNumeroIT(v.Value, 1, "mm")
                End If
                If precProb IsNot Nothing AndAlso i < precProb.Count Then
                    Dim v As Double? = JsonDouble(precProb(i))
                    If v.HasValue Then g.ProbabilitaPioggia = FormatNumeroIT(v.Value, 0, "%")
                End If
                If ventoMax IsNot Nothing AndAlso i < ventoMax.Count Then
                    Dim v As Double? = JsonDouble(ventoMax(i))
                    If v.HasValue Then g.Vento = FormatNumeroIT(v.Value, 0, "km/h")
                End If
                If ventoDir IsNot Nothing AndAlso i < ventoDir.Count Then
                    g.DirezioneVento = TraduzioneDirezVento(JsonDouble(ventoDir(i)))
                End If

                lista.Add(g)
            Next
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSMeteo.OttieniPrevisione (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniPrevisione - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Public Async Function OttieniMeteoGiornaliero(lat As Double, lon As Double, Optional nomeCitta As String = "") As Task(Of InfoMeteoGiorno)
        Try
            Dim lista As List(Of InfoMeteoGiorno) = Await OttieniPrevisione(lat, lon, nomeCitta, 1)
            If lista IsNot Nothing AndAlso lista.Count > 0 Then
                Return lista(0)
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniMeteoGiornaliero - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return Nothing
    End Function

    Public Async Function OttieniMeteoOrario(lat As Double, lon As Double, Optional nomeCitta As String = "") As Task(Of List(Of InfoMeteoOra))
        Try
            Return Await OttieniMeteoOrarioData(lat, lon, "", nomeCitta)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniMeteoOrario - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of InfoMeteoOra)()
        End Try
    End Function

    Private Function CalcolaGiorniPrevisioneNecessari(dataRichiesta As String) As Integer
        Try
            If String.IsNullOrWhiteSpace(dataRichiesta) Then Return 1

            Dim dataObiettivo As DateTime
            If Not DateTime.TryParse(dataRichiesta, CultureInfo.InvariantCulture, DateTimeStyles.None, dataObiettivo) Then
                Return 1
            End If

            Dim differenza As Integer = CInt(Math.Ceiling((dataObiettivo.Date - DateTime.Now.Date).TotalDays)) + 1
            If differenza < 1 Then differenza = 1
            If differenza > 16 Then differenza = 16
            Return differenza
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.CalcolaGiorniPrevisioneNecessari - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return 1
        End Try
    End Function

    Public Async Function OttieniMeteoOrarioData(lat As Double, lon As Double, Optional dataRichiesta As String = "", Optional nomeCitta As String = "") As Task(Of List(Of InfoMeteoOra))
        Dim lista As New List(Of InfoMeteoOra)()
        Try
            Dim coord As CoordinateCitta = Await RisolviCoordinate(lat, lon, nomeCitta)
            Dim latVal As Double = coord.Lat
            Dim lonVal As Double = coord.Lon
            If latVal < -90 OrElse latVal > 90 OrElse lonVal < -180 OrElse lonVal > 180 OrElse (latVal = 0 AndAlso lonVal = 0) Then
                Return lista
            End If

            Dim giorniNecessari As Integer = CalcolaGiorniPrevisioneNecessari(dataRichiesta)

            Dim latStr As String = latVal.ToString("0.####", CultureInfo.InvariantCulture)
            Dim lonStr As String = lonVal.ToString("0.####", CultureInfo.InvariantCulture)
            Dim url As String = String.Format(OpenMeteoHourlyUrl, latStr, lonStr, giorniNecessari)

            Dim json As String = Await GetStringConRetry(url, "OttieniMeteoOrarioData")
            If json Is Nothing Then Return lista

            Dim obj As JObject = JObject.Parse(json)
            Dim hourly As JToken = obj("hourly")
            If hourly Is Nothing Then Return lista

            Dim tempi As JArray = TryCast(hourly("time"), JArray)
            If tempi Is Nothing OrElse tempi.Count = 0 Then Return lista

            Dim temps As JArray = TryCast(hourly("temperature_2m"), JArray)
            Dim tempsPercepite As JArray = TryCast(hourly("apparent_temperature"), JArray)
            Dim umidita As JArray = TryCast(hourly("relative_humidity_2m"), JArray)
            Dim puntoRugiada As JArray = TryCast(hourly("dew_point_2m"), JArray)
            Dim precipitazioni As JArray = TryCast(hourly("precipitation"), JArray)
            Dim codici As JArray = TryCast(hourly("weather_code"), JArray)
            Dim precProb As JArray = TryCast(hourly("precipitation_probability"), JArray)
            Dim pressione As JArray = TryCast(hourly("pressure_msl"), JArray)
            Dim nuvolosita As JArray = TryCast(hourly("cloud_cover"), JArray)
            Dim visibilita As JArray = TryCast(hourly("visibility"), JArray)
            Dim venti As JArray = TryCast(hourly("wind_speed_10m"), JArray)
            Dim direzioneVento As JArray = TryCast(hourly("wind_direction_10m"), JArray)
            Dim raffiche As JArray = TryCast(hourly("wind_gusts_10m"), JArray)
            Dim indiceUv As JArray = TryCast(hourly("uv_index"), JArray)

            For i As Integer = 0 To tempi.Count - 1
                Dim o As New InfoMeteoOra()
                Dim tempoRaw As String = tempi(i)?.ToString()
                o.Id = tempoRaw
                o.Ora = FormatOra(tempoRaw)

                If temps IsNot Nothing AndAlso i < temps.Count Then
                    Dim v As Double? = JsonDouble(temps(i))
                    If v.HasValue Then o.Temperatura = FormatNumeroIT(v.Value, 1, "°C")
                End If
                If tempsPercepite IsNot Nothing AndAlso i < tempsPercepite.Count Then
                    Dim v As Double? = JsonDouble(tempsPercepite(i))
                    If v.HasValue Then o.TemperaturaPercepita = FormatNumeroIT(v.Value, 1, "°C")
                End If
                If umidita IsNot Nothing AndAlso i < umidita.Count Then
                    Dim v As Double? = JsonDouble(umidita(i))
                    If v.HasValue Then o.Umidita = FormatNumeroIT(v.Value, 0, "%")
                End If
                If puntoRugiada IsNot Nothing AndAlso i < puntoRugiada.Count Then
                    Dim v As Double? = JsonDouble(puntoRugiada(i))
                    If v.HasValue Then o.PuntoDiRugiada = FormatNumeroIT(v.Value, 1, "°C")
                End If
                If precipitazioni IsNot Nothing AndAlso i < precipitazioni.Count Then
                    Dim v As Double? = JsonDouble(precipitazioni(i))
                    If v.HasValue Then o.Precipitazioni = FormatNumeroIT(v.Value, 1, "mm")
                End If
                If codici IsNot Nothing AndAlso i < codici.Count Then
                    Dim wc As Double? = JsonDouble(codici(i))
                    o.Condizioni = DescriviCodiceMeteo(If(wc.HasValue, CInt(wc.Value), -1))
                End If
                If precProb IsNot Nothing AndAlso i < precProb.Count Then
                    Dim v As Double? = JsonDouble(precProb(i))
                    If v.HasValue Then o.ProbabilitaPioggia = FormatNumeroIT(v.Value, 0, "%")
                End If
                If pressione IsNot Nothing AndAlso i < pressione.Count Then
                    Dim v As Double? = JsonDouble(pressione(i))
                    If v.HasValue Then o.Pressione = FormatNumeroIT(v.Value, 0, "hPa")
                End If
                If nuvolosita IsNot Nothing AndAlso i < nuvolosita.Count Then
                    Dim v As Double? = JsonDouble(nuvolosita(i))
                    If v.HasValue Then o.Nuvolosita = FormatNumeroIT(v.Value, 0, "%")
                End If
                If visibilita IsNot Nothing AndAlso i < visibilita.Count Then
                    Dim v As Double? = JsonDouble(visibilita(i))
                    If v.HasValue Then o.Visibilita = FormatNumeroIT(v.Value / 1000.0, 1, "km")
                End If
                If venti IsNot Nothing AndAlso i < venti.Count Then
                    Dim v As Double? = JsonDouble(venti(i))
                    If v.HasValue Then o.Vento = FormatNumeroIT(v.Value, 0, "km/h")
                End If
                If direzioneVento IsNot Nothing AndAlso i < direzioneVento.Count Then
                    Dim v As Double? = JsonDouble(direzioneVento(i))
                    o.DirezioneVento = TraduzioneDirezVento(v)
                End If
                If raffiche IsNot Nothing AndAlso i < raffiche.Count Then
                    Dim v As Double? = JsonDouble(raffiche(i))
                    If v.HasValue Then o.RaffficheVento = FormatNumeroIT(v.Value, 0, "km/h")
                End If
                If indiceUv IsNot Nothing AndAlso i < indiceUv.Count Then
                    Dim v As Double? = JsonDouble(indiceUv(i))
                    If v.HasValue Then o.IndiceUV = FormatNumeroIT(v.Value, 1, "")
                End If

                lista.Add(o)
            Next

            If Not String.IsNullOrWhiteSpace(dataRichiesta) Then
                Dim listaFiltrata As New List(Of InfoMeteoOra)()
                For Each o As InfoMeteoOra In lista
                    If Not String.IsNullOrWhiteSpace(o.Id) AndAlso o.Id.StartsWith(dataRichiesta, StringComparison.OrdinalIgnoreCase) Then
                        listaFiltrata.Add(o)
                    End If
                Next
                Return listaFiltrata
            End If
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSMeteo.OttieniMeteoOrarioData (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniMeteoOrarioData - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Private Function CalcolaDistanzaKm(lat1 As Double, lon1 As Double, lat2 As Double, lon2 As Double) As Double
        Try
            Const raggioTerraKm As Double = 6371.0
            Dim dLat As Double = (lat2 - lat1) * Math.PI / 180.0
            Dim dLon As Double = (lon2 - lon1) * Math.PI / 180.0
            Dim a As Double = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2)
            Dim c As Double = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a))
            Return raggioTerraKm * c
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.CalcolaDistanzaKm - " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function CalcolaDirezioneBussola(lat1 As Double, lon1 As Double, lat2 As Double, lon2 As Double) As String
        Try
            Dim lat1Rad As Double = lat1 * Math.PI / 180.0
            Dim lat2Rad As Double = lat2 * Math.PI / 180.0
            Dim dLonRad As Double = (lon2 - lon1) * Math.PI / 180.0
            Dim y As Double = Math.Sin(dLonRad) * Math.Cos(lat2Rad)
            Dim x As Double = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) - Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLonRad)
            Dim bearing As Double = Math.Atan2(y, x) * 180.0 / Math.PI
            bearing = (bearing + 360.0) Mod 360.0
            Return TraduzioneDirezVento(bearing)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.CalcolaDirezioneBussola - " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function EPuntoInMare(lat As Double, lon As Double) As Task(Of Boolean)
        Try
            Dim latStr As String = lat.ToString("0.####", CultureInfo.InvariantCulture)
            Dim lonStr As String = lon.ToString("0.####", CultureInfo.InvariantCulture)
            Dim url As String = String.Format(OpenMeteoMarineCurrentUrl, latStr, lonStr)

            Dim corpo As String = Await GetStringConRetry(url, "EPuntoInMare", 2)
            If corpo Is Nothing Then Return False

            Dim obj As JObject = JObject.Parse(corpo)
            Dim current As JToken = obj("current")
            If current Is Nothing Then Return False
            Dim altezzaOnde As Double? = JsonDouble(current("wave_height"))
            Return altezzaOnde.HasValue
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.EPuntoInMare - " & ex.Message)
            Return False
        End Try
    End Function

    Public Async Function TrovaPuntoMarinoVicino(lat As Double, lon As Double) As Task(Of PuntoMarino)
        Dim risultato As New PuntoMarino()
        Try
            If Await EPuntoInMare(lat, lon) Then
                risultato.Valido = True
                risultato.Lat = lat
                risultato.Lon = lon
                risultato.DistanzaKm = 0
                risultato.Direzione = ""
                Return risultato
            End If

            Dim raggiGradi As Double() = {0.15, 0.3, 0.6, 1.2, 2.5}
            Dim direzioniGradi As Double() = {0, 45, 90, 135, 180, 225, 270, 315}

            For Each raggio As Double In raggiGradi
                For Each direzioneGradi As Double In direzioniGradi
                    Dim radianti As Double = direzioneGradi * Math.PI / 180.0
                    Dim latCandidata As Double = lat + raggio * Math.Cos(radianti)
                    Dim lonCandidata As Double = lon + raggio * Math.Sin(radianti) / Math.Max(0.1, Math.Cos(lat * Math.PI / 180.0))

                    If latCandidata < -90 OrElse latCandidata > 90 OrElse lonCandidata < -180 OrElse lonCandidata > 180 Then Continue For

                    If Await EPuntoInMare(latCandidata, lonCandidata) Then
                        risultato.Valido = True
                        risultato.Lat = latCandidata
                        risultato.Lon = lonCandidata
                        risultato.DistanzaKm = CalcolaDistanzaKm(lat, lon, latCandidata, lonCandidata)
                        risultato.Direzione = CalcolaDirezioneBussola(lat, lon, latCandidata, lonCandidata)
                        Return risultato
                    End If
                Next
            Next

            risultato.Valido = False
            risultato.Messaggio = "Nessun tratto di mare trovato nel raggio di ricerca."
            Return risultato
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.TrovaPuntoMarinoVicino - " & ex.Message & Environment.NewLine & ex.StackTrace)
            risultato.Valido = False
            risultato.Messaggio = "Errore durante la ricerca del mare più vicino."
            Return risultato
        End Try
    End Function

    Public Async Function OttieniMeteoMarinoCorrente(puntoMarino As PuntoMarino) As Task(Of InfoMeteoMarino)
        Dim info As New InfoMeteoMarino()
        Try
            If puntoMarino Is Nothing OrElse Not puntoMarino.Valido Then
                info.Messaggio = "Questa città non ha un tratto di mare vicino disponibile."
                Return info
            End If

            info.DistanzaKm = puntoMarino.DistanzaKm
            info.Direzione = puntoMarino.Direzione

            Dim latStr As String = puntoMarino.Lat.ToString("0.####", CultureInfo.InvariantCulture)
            Dim lonStr As String = puntoMarino.Lon.ToString("0.####", CultureInfo.InvariantCulture)
            Dim url As String = String.Format(OpenMeteoMarineCurrentUrl, latStr, lonStr)

            Dim corpo As String = Await GetStringConRetry(url, "OttieniMeteoMarinoCorrente")
            If corpo Is Nothing Then
                info.Messaggio = "Dati marittimi non disponibili al momento."
                Return info
            End If

            Dim obj As JObject = JObject.Parse(corpo)
            Dim current As JToken = obj("current")
            If current Is Nothing Then
                info.Messaggio = "Dati marittimi non disponibili al momento."
                Return info
            End If

            Dim altezzaOnde As Double? = JsonDouble(current("wave_height"))
            If altezzaOnde.HasValue Then info.AltezzaOnde = FormatNumeroIT(altezzaOnde.Value, 1, "m")

            Dim direzioneOnde As Double? = JsonDouble(current("wave_direction"))
            info.DirezioneOnde = TraduzioneDirezVento(direzioneOnde)

            Dim periodoOnde As Double? = JsonDouble(current("wave_period"))
            If periodoOnde.HasValue Then info.PeriodoOnde = FormatNumeroIT(periodoOnde.Value, 1, "s")

            Dim mareggiata As Double? = JsonDouble(current("swell_wave_height"))
            If mareggiata.HasValue Then info.AltezzaMareggiata = FormatNumeroIT(mareggiata.Value, 1, "m")

            Dim tempMareCorr As Double? = JsonDouble(current("sea_surface_temperature"))
            If tempMareCorr.HasValue Then info.TemperaturaMare = FormatNumeroIT(tempMareCorr.Value, 1, "°C")

            Dim orarioAgg As String = current("time")?.ToString()
            info.Aggiornamento = FormatAggiornamento(orarioAgg)

            info.Valido = True
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSMeteo.OttieniMeteoMarinoCorrente (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            info.Messaggio = "Errore di rete nel recupero dei dati marittimi."
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniMeteoMarinoCorrente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            info.Messaggio = "Errore nel recupero dei dati marittimi."
        End Try
        Return info
    End Function

    Public Async Function OttieniMeteoMarinoOrario(puntoMarino As PuntoMarino) As Task(Of List(Of InfoMeteoMarinoOra))
        Dim lista As New List(Of InfoMeteoMarinoOra)()
        Try
            If puntoMarino Is Nothing OrElse Not puntoMarino.Valido Then Return lista

            Dim latStr As String = puntoMarino.Lat.ToString("0.####", CultureInfo.InvariantCulture)
            Dim lonStr As String = puntoMarino.Lon.ToString("0.####", CultureInfo.InvariantCulture)
            Dim url As String = String.Format(OpenMeteoMarineHourlyUrl, latStr, lonStr)

            Dim json As String = Await GetStringConRetry(url, "OttieniMeteoMarinoOrario")
            If json Is Nothing Then Return lista

            Dim obj As JObject = JObject.Parse(json)
            Dim hourly As JToken = obj("hourly")
            If hourly Is Nothing Then Return lista

            Dim tempi As JArray = TryCast(hourly("time"), JArray)
            If tempi Is Nothing OrElse tempi.Count = 0 Then Return lista

            Dim altezzeOnde As JArray = TryCast(hourly("wave_height"), JArray)
            Dim direzioniOnde As JArray = TryCast(hourly("wave_direction"), JArray)
            Dim periodiOnde As JArray = TryCast(hourly("wave_period"), JArray)
            Dim tempMareOra As JArray = TryCast(hourly("sea_surface_temperature"), JArray)

            For i As Integer = 0 To tempi.Count - 1
                Dim o As New InfoMeteoMarinoOra()
                Dim tempoRaw As String = tempi(i)?.ToString()
                o.Id = tempoRaw
                o.Ora = FormatOra(tempoRaw)

                If altezzeOnde IsNot Nothing AndAlso i < altezzeOnde.Count Then
                    Dim v As Double? = JsonDouble(altezzeOnde(i))
                    If v.HasValue Then o.AltezzaOnde = FormatNumeroIT(v.Value, 1, "m")
                End If
                If direzioniOnde IsNot Nothing AndAlso i < direzioniOnde.Count Then
                    Dim v As Double? = JsonDouble(direzioniOnde(i))
                    o.DirezioneOnde = TraduzioneDirezVento(v)
                End If
                If periodiOnde IsNot Nothing AndAlso i < periodiOnde.Count Then
                    Dim v As Double? = JsonDouble(periodiOnde(i))
                    If v.HasValue Then o.PeriodoOnde = FormatNumeroIT(v.Value, 1, "s")
                End If
                If tempMareOra IsNot Nothing AndAlso i < tempMareOra.Count Then
                    Dim v As Double? = JsonDouble(tempMareOra(i))
                    If v.HasValue Then o.TemperaturaMare = FormatNumeroIT(v.Value, 1, "°C")
                End If

                lista.Add(o)
            Next
        Catch ex As HttpRequestException
            logger.ScriviLog("CLSMeteo.OttieniMeteoMarinoOrario (HTTP) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniMeteoMarinoOrario - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Public Function AggiungiComunePreferito(comune As ComuneInfo) As Boolean
        Try
            Return SalvaComune("ComuniPreferiti", comune, False)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.AggiungiComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function AggiungiComunePreferito(idComune As String, nomeComune As String, lat As Double, lon As Double, Optional paese As String = "", Optional regione As String = "") As Boolean
        Try
            Dim comune As New ComuneInfo With {
            .IdComune = idComune,
            .NomeComune = nomeComune,
            .Lat = lat,
            .Lon = lon,
            .Paese = paese,
            .Regione = regione,
            .UltimoUso = DateTime.UtcNow
        }
            Return SalvaComune("ComuniPreferiti", comune, False)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.AggiungiComunePreferito (overload) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function OttieniComuniPreferiti() As List(Of ComuneInfo)
        Try
            Return LeggiComuni("ComuniPreferiti")
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniComuniPreferiti - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of ComuneInfo)()
        End Try
    End Function

    Public Function OttieniComunePreferito(idComune As String) As ComuneInfo
        Try
            Return LeggiComunePerId("ComuniPreferiti", idComune)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Public Function ModificaComunePreferito(idComune As String, nuovoNome As String) As Boolean
        Try
            Return AggiornaNomeComune("ComuniPreferiti", idComune, nuovoNome)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.ModificaComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function EliminaComunePreferito(idComune As String) As Boolean
        Try
            Return CancellaComune("ComuniPreferiti", idComune)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.EliminaComunePreferito - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function AggiungiComuneRecente(comune As ComuneInfo) As Boolean
        Try
            Return SalvaComune("ComuniRecenti", comune, True)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.AggiungiComuneRecente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function AggiungiComuneRecente(idComune As String, nomeComune As String, lat As Double, lon As Double, Optional paese As String = "", Optional regione As String = "") As Boolean
        Try
            Dim comune As New ComuneInfo With {
            .IdComune = idComune,
            .NomeComune = nomeComune,
            .Lat = lat,
            .Lon = lon,
            .Paese = paese,
            .Regione = regione,
            .UltimoUso = DateTime.UtcNow
        }
            Return SalvaComune("ComuniRecenti", comune, True)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.AggiungiComuneRecente (overload) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function OttieniComuniRecenti() As List(Of ComuneInfo)
        Try
            Return LeggiComuni("ComuniRecenti")
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniComuniRecenti - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New List(Of ComuneInfo)()
        End Try
    End Function

    Public Function OttieniComuneRecente(idComune As String) As ComuneInfo
        Try
            Return LeggiComunePerId("ComuniRecenti", idComune)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.OttieniComuneRecente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return Nothing
        End Try
    End Function

    Public Function ModificaComuneRecente(idComune As String, nuovoNome As String) As Boolean
        Try
            Return AggiornaNomeComune("ComuniRecenti", idComune, nuovoNome)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.ModificaComuneRecente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function EliminaComuneRecente(idComune As String) As Boolean
        Try
            Return CancellaComune("ComuniRecenti", idComune)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.EliminaComuneRecente - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function CreaComuneInfo(coord As CoordinateCitta, Optional nomeOverride As String = "") As ComuneInfo
        Try
            Dim nome As String = If(String.IsNullOrWhiteSpace(nomeOverride), coord.NomeRisolto, nomeOverride)
            If String.IsNullOrWhiteSpace(nome) Then
                nome = coord.IdComune
            End If

            Return New ComuneInfo With {
            .IdComune = If(String.IsNullOrWhiteSpace(coord.IdComune), CreaIdComuneFallback(nome), coord.IdComune),
            .NomeComune = nome,
            .Lat = coord.Lat,
            .Lon = coord.Lon,
            .Paese = coord.Paese,
            .Regione = coord.Regione,
            .UltimoUso = DateTime.UtcNow
        }
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.CreaComuneInfo - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New ComuneInfo()
        End Try
    End Function

    Private Function SalvaComune(nomeTabella As String, comune As ComuneInfo, aggiornaUltimoUso As Boolean) As Boolean
        Try
            If comune Is Nothing Then Return False
            If String.IsNullOrWhiteSpace(comune.IdComune) Then
                comune.IdComune = CreaIdComuneFallback(comune.NomeComune)
            End If
            If String.IsNullOrWhiteSpace(comune.IdComune) OrElse String.IsNullOrWhiteSpace(comune.NomeComune) Then
                Return False
            End If

            If aggiornaUltimoUso OrElse comune.UltimoUso = DateTime.MinValue Then
                comune.UltimoUso = DateTime.UtcNow
            End If

            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()

                Dim chiaveWGComune As String = ""
                Try
                    Using cmdLookup As SQLiteCommand = conn.CreateCommand()
                        cmdLookup.CommandText = If(nomeTabella = "ComuniRecenti", "SELECT ChiaveWG FROM ComuniRecenti WHERE IdComune = @IdComune;", "SELECT ChiaveWG FROM ComuniPreferiti WHERE IdComune = @IdComune;")
                        cmdLookup.Parameters.AddWithValue("@IdComune", comune.IdComune.Trim())
                        Dim risultatoLookup As Object = cmdLookup.ExecuteScalar()
                        If risultatoLookup IsNot Nothing AndAlso Not IsDBNull(risultatoLookup) Then
                            chiaveWGComune = risultatoLookup.ToString()
                        End If
                    End Using
                Catch exLookup As Exception
                    logger.ScriviLog($"CLSMeteo.SalvaComune({nomeTabella}) lookup - " & exLookup.Message)
                End Try
                If String.IsNullOrWhiteSpace(chiaveWGComune) Then
                    chiaveWGComune = WGDatabase.GeneraChiaveUnivocaPerTabella(conn, nomeTabella)
                End If

                Using cmd As SQLiteCommand = conn.CreateCommand()
                    If nomeTabella = "ComuniRecenti" Then
                        cmd.CommandText = "INSERT OR REPLACE INTO ComuniRecenti (IdComune, NomeComune, Lat, Lon, Paese, Regione, UltimoUso, ChiaveWG) VALUES (@IdComune, @NomeComune, @Lat, @Lon, @Paese, @Regione, @UltimoUso, @ChiaveWG);"
                    Else
                        cmd.CommandText = "INSERT OR REPLACE INTO ComuniPreferiti (IdComune, NomeComune, Lat, Lon, Paese, Regione, UltimoUso, ChiaveWG) VALUES (@IdComune, @NomeComune, @Lat, @Lon, @Paese, @Regione, @UltimoUso, @ChiaveWG);"
                    End If

                    cmd.Parameters.AddWithValue("@IdComune", comune.IdComune.Trim())
                    cmd.Parameters.AddWithValue("@NomeComune", comune.NomeComune.Trim())
                    cmd.Parameters.AddWithValue("@Lat", comune.Lat)
                    cmd.Parameters.AddWithValue("@Lon", comune.Lon)
                    cmd.Parameters.AddWithValue("@Paese", comune.Paese.Trim())
                    cmd.Parameters.AddWithValue("@Regione", comune.Regione.Trim())
                    cmd.Parameters.AddWithValue("@UltimoUso", comune.UltimoUso.ToString("o"))
                    cmd.Parameters.AddWithValue("@ChiaveWG", chiaveWGComune)
                    Return cmd.ExecuteNonQuery() > 0
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"CLSMeteo.SalvaComune({nomeTabella}) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function LeggiComuni(nomeTabella As String) As List(Of ComuneInfo)
        Dim lista As New List(Of ComuneInfo)()
        Try
            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As SQLiteCommand = conn.CreateCommand()
                    If nomeTabella = "ComuniRecenti" Then
                        cmd.CommandText = "SELECT IdComune, NomeComune, Lat, Lon, Paese, Regione, UltimoUso FROM ComuniRecenti ORDER BY UltimoUso DESC, NomeComune ASC;"
                    Else
                        cmd.CommandText = "SELECT IdComune, NomeComune, Lat, Lon, Paese, Regione, UltimoUso FROM ComuniPreferiti ORDER BY NomeComune ASC;"
                    End If

                    Using rdr As SQLiteDataReader = cmd.ExecuteReader()
                        While rdr.Read()
                            lista.Add(ComuneInfoDaReader(rdr))
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"CLSMeteo.LeggiComuni({nomeTabella}) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return lista
    End Function

    Private Function LeggiComunePerId(nomeTabella As String, idComune As String) As ComuneInfo
        Dim item As New ComuneInfo()
        Try
            If String.IsNullOrWhiteSpace(idComune) Then Return Nothing

            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As SQLiteCommand = conn.CreateCommand()
                    If nomeTabella = "ComuniRecenti" Then
                        cmd.CommandText = "SELECT IdComune, NomeComune, Lat, Lon, Paese, Regione, UltimoUso FROM ComuniRecenti WHERE IdComune = @IdComune LIMIT 1;"
                    Else
                        cmd.CommandText = "SELECT IdComune, NomeComune, Lat, Lon, Paese, Regione, UltimoUso FROM ComuniPreferiti WHERE IdComune = @IdComune LIMIT 1;"
                    End If
                    cmd.Parameters.AddWithValue("@IdComune", idComune.Trim())

                    Using rdr As SQLiteDataReader = cmd.ExecuteReader()
                        If rdr.Read() Then
                            item = ComuneInfoDaReader(rdr)
                            Return item
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"CLSMeteo.LeggiComunePerId({nomeTabella}) - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return Nothing
    End Function

    Private Function ComuneInfoDaReader(rdr As SQLiteDataReader) As ComuneInfo
        Try
            Dim item As New ComuneInfo()
            item.IdComune = If(IsDBNull(rdr("IdComune")), "", Convert.ToString(rdr("IdComune")))
            item.NomeComune = If(IsDBNull(rdr("NomeComune")), "", Convert.ToString(rdr("NomeComune")))
            item.Lat = If(IsDBNull(rdr("Lat")), 0R, Convert.ToDouble(rdr("Lat"), CultureInfo.InvariantCulture))
            item.Lon = If(IsDBNull(rdr("Lon")), 0R, Convert.ToDouble(rdr("Lon"), CultureInfo.InvariantCulture))
            item.Paese = If(IsDBNull(rdr("Paese")), "", Convert.ToString(rdr("Paese")))
            item.Regione = If(IsDBNull(rdr("Regione")), "", Convert.ToString(rdr("Regione")))
            If Not IsDBNull(rdr("UltimoUso")) Then
                Dim dt As DateTime
                If DateTime.TryParse(Convert.ToString(rdr("UltimoUso")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, dt) Then
                    item.UltimoUso = dt
                End If
            End If
            Return item
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.ComuneInfoDaReader - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return New ComuneInfo()
        End Try
    End Function

    Private Function AggiornaNomeComune(nomeTabella As String, idComune As String, nuovoNome As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(idComune) OrElse String.IsNullOrWhiteSpace(nuovoNome) Then
                Return False
            End If

            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As SQLiteCommand = conn.CreateCommand()
                    If nomeTabella = "ComuniRecenti" Then
                        cmd.CommandText = "UPDATE ComuniRecenti SET NomeComune = @NomeComune, UltimoUso = @UltimoUso WHERE IdComune = @IdComune;"
                        cmd.Parameters.AddWithValue("@UltimoUso", DateTime.UtcNow.ToString("o"))
                    Else
                        cmd.CommandText = "UPDATE ComuniPreferiti SET NomeComune = @NomeComune WHERE IdComune = @IdComune;"
                    End If

                    cmd.Parameters.AddWithValue("@IdComune", idComune.Trim())
                    cmd.Parameters.AddWithValue("@NomeComune", nuovoNome.Trim())
                    Return cmd.ExecuteNonQuery() > 0
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"CLSMeteo.AggiornaNomeComune({nomeTabella}) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function CancellaComune(nomeTabella As String, idComune As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(idComune) Then Return False

            Using conn As New SQLiteConnection(_connectionString)
                conn.Open()
                Using cmd As SQLiteCommand = conn.CreateCommand()
                    If nomeTabella = "ComuniRecenti" Then
                        cmd.CommandText = "DELETE FROM ComuniRecenti WHERE IdComune = @IdComune;"
                    Else
                        cmd.CommandText = "DELETE FROM ComuniPreferiti WHERE IdComune = @IdComune;"
                    End If

                    cmd.Parameters.AddWithValue("@IdComune", idComune.Trim())
                    Return cmd.ExecuteNonQuery() > 0
                End Using
            End Using
        Catch ex As Exception
            logger.ScriviLog($"CLSMeteo.CancellaComune({nomeTabella}) - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function CreaIdComuneFallback(nomeComune As String) As String
        Try
            If String.IsNullOrWhiteSpace(nomeComune) Then
                Return ""
            End If

            Dim sb As New System.Text.StringBuilder()
            For Each ch As Char In nomeComune.Trim().ToLowerInvariant()
                If Char.IsLetterOrDigit(ch) Then
                    sb.Append(ch)
                ElseIf Char.IsWhiteSpace(ch) OrElse ch = "-"c OrElse ch = "_"c Then
                    If sb.Length = 0 OrElse sb(sb.Length - 1) <> "-"c Then
                        sb.Append("-"c)
                    End If
                End If
            Next
            Return sb.ToString().Trim("-"c)
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.CreaIdComuneFallback - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return nomeComune
        End Try
    End Function

    Private Function JsonDouble(token As JToken) As Double?
        Try
            If token Is Nothing OrElse token.Type = JTokenType.Null OrElse token.Type = JTokenType.Undefined Then
                Return Nothing
            End If
            Return token.Value(Of Double)()
        Catch ex As Exception
            Try
                Dim s As String = token.ToString().Trim().Replace(","c, "."c)
                Dim d As Double = 0
                If Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, d) Then
                    Return d
                End If
            Catch exInterno As Exception
                logger.ScriviLog("CLSMeteo.JsonDouble (fallback) - " & exInterno.Message)
            End Try
            Return Nothing
        End Try
    End Function

    Private Function FormatNumeroIT(valore As Double, decimali As Integer, unita As String) As String
        Try
            Dim cul As New CultureInfo("it-IT")
            Dim fmt As String = "0"
            If decimali > 0 Then fmt = "0." & New String("0"c, decimali)
            Dim s As String = valore.ToString(fmt, cul)
            If String.IsNullOrWhiteSpace(unita) Then Return s
            Return s & " " & unita
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.FormatNumeroIT - " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return valore.ToString()
        End Try
    End Function

    Private Function FormatAggiornamento(orario As String) As String
        Try
            Dim dt As DateTime
            If Not String.IsNullOrWhiteSpace(orario) AndAlso DateTime.TryParse(orario, CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then
                Return dt.ToString("dddd d MMMM yyyy, HH:mm", New CultureInfo("it-IT"))
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.FormatAggiornamento - " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
        Return DateTime.Now.ToString("dddd d MMMM yyyy, HH:mm", New CultureInfo("it-IT"))
    End Function

    Private Function DescriviCodiceMeteo(code As Integer) As String
        Select Case code
            Case 0 : Return "Cielo sereno"
            Case 1 : Return "Prevalentemente sereno"
            Case 2 : Return "Parzialmente nuvoloso"
            Case 3 : Return "Coperto"
            Case 45 : Return "Nebbia"
            Case 48 : Return "Nebbia con brina"
            Case 51 : Return "Pioggerella leggera"
            Case 53 : Return "Pioggerella moderata"
            Case 55 : Return "Pioggerella intensa"
            Case 56 : Return "Pioggerella ghiacciata leggera"
            Case 57 : Return "Pioggerella ghiacciata intensa"
            Case 61 : Return "Pioggia leggera"
            Case 63 : Return "Pioggia moderata"
            Case 65 : Return "Pioggia intensa"
            Case 66 : Return "Pioggia gelata leggera"
            Case 67 : Return "Pioggia gelata intensa"
            Case 71 : Return "Nevicata leggera"
            Case 73 : Return "Nevicata moderata"
            Case 75 : Return "Nevicata intensa"
            Case 77 : Return "Granelli di neve"
            Case 80 : Return "Rovesci leggeri"
            Case 81 : Return "Rovesci moderati"
            Case 82 : Return "Rovesci violenti"
            Case 85 : Return "Rovesci di neve leggeri"
            Case 86 : Return "Rovesci di neve intensi"
            Case 95 : Return "Temporale"
            Case 96 : Return "Temporale con grandine leggera"
            Case 99 : Return "Temporale con grandine intensa"
            Case Else : Return "Non disponibile"
        End Select
    End Function

    Private Function TraduzioneDirezVento(gradi As Double?) As String
        If Not gradi.HasValue Then Return "N/D"
        Dim g As Double = gradi.Value
        Dim idx As Integer = CInt(Math.Floor((g + 22.5) / 45)) Mod 8
        If idx < 0 Then idx += 8
        Dim dirs As String() = {"Nord", "Nord-Est", "Est", "Sud-Est", "Sud", "Sud-Ovest", "Ovest", "Nord-Ovest"}
        Return dirs(idx)
    End Function

    Private Function FormatGiornoBreve(dataStr As String) As String
        Try
            Dim dt As DateTime
            If Not String.IsNullOrWhiteSpace(dataStr) AndAlso DateTime.TryParse(dataStr, CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then
                Return dt.ToString("dddd d", New CultureInfo("it-IT"))
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.FormatGiornoBreve - " & ex.Message)
        End Try
        Return If(dataStr, "")
    End Function

    Private Function FormatDataCompleta(dataStr As String) As String
        Try
            Dim dt As DateTime
            If Not String.IsNullOrWhiteSpace(dataStr) AndAlso DateTime.TryParse(dataStr, CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then
                Return dt.ToString("dddd d MMMM yyyy", New CultureInfo("it-IT"))
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.FormatDataCompleta - " & ex.Message)
        End Try
        Return If(dataStr, "")
    End Function

    Private Function FormatOra(orarioStr As String) As String
        Try
            Dim dt As DateTime
            If Not String.IsNullOrWhiteSpace(orarioStr) AndAlso DateTime.TryParse(orarioStr, CultureInfo.InvariantCulture, DateTimeStyles.None, dt) Then
                Return dt.ToString("HH:mm", New CultureInfo("it-IT"))
            End If
        Catch ex As Exception
            logger.ScriviLog("CLSMeteo.FormatOra - " & ex.Message)
        End Try
        Return If(orarioStr, "")
    End Function

End Class