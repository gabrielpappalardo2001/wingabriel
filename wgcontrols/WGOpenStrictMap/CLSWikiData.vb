Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq

''' <summary>
''' Recupera da Wikidata le informazioni enciclopediche sui luoghi di interesse:
''' descrizione, anno di costruzione, architetto, stile, collegamento a Wikipedia.
''' Utile per musei, monumenti, chiese e palazzi storici, dove il solo nome e
''' indirizzo dicono poco.
'''
''' Wikidata e' libero (licenza CC0), non richiede alcuna chiave ne' registrazione.
''' Le informazioni vengono chieste in italiano, con ripiego sull'inglese.
''' </summary>
Public Class CLSWikidata

#Region "Tipi pubblici"

    Public Class VoceWikidata
        Public Property Identificativo As String
        Public Property Titolo As String
        Public Property Descrizione As String
        Public Property Categoria As String
        Public Property AnnoInizio As String
        Public Property Architetto As String
        Public Property Stile As String
        Public Property PaginaWikipedia As String
        Public Property Latitudine As Double
        Public Property Longitudine As Double
    End Class

#End Region

#Region "Singleton e campi"

    Private Shared _istanza As CLSWikidata
    Private Shared ReadOnly _lockIstanza As New Object()
    Private Shared ReadOnly _lockLog As New Object()

    Private Const URL_SPARQL As String = "https://query.wikidata.org/sparql"

    Private ReadOnly _percorsoFileLog As String
    Private ReadOnly _cache As New Dictionary(Of String, List(Of VoceWikidata))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _lockCache As New Object()

    Private Shared ReadOnly _http As New HttpClient() With {
        .Timeout = TimeSpan.FromSeconds(70)
    }

    Shared Sub New()
        Try
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
            ' Wikidata chiede un identificativo riconoscibile: senza di esso rifiuta le richieste.
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel/1.0 (accessibilita; software libero per non vedenti)")
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json")
        Catch
        End Try
    End Sub

    Private Sub New()
        Try
            _percorsoFileLog = Path.Combine(wgcontrols.Logger.CartellaLogApplicazione(), "log.txt")
        Catch
        End Try
    End Sub

    Public Shared Function GetInstance() As CLSWikidata
        Try
            SyncLock _lockIstanza
                If _istanza Is Nothing Then _istanza = New CLSWikidata()
                Return _istanza
            End SyncLock
        Catch
            Return _istanza
        End Try
    End Function

#End Region

#Region "Ricerca"

    ''' <summary>
    ''' Cerca in Wikidata i luoghi con coordinate dentro un raggio da un punto.
    ''' Restituisce solo voci che hanno una descrizione o un collegamento a Wikipedia,
    ''' perche' le altre non aggiungerebbero niente a quello che gia' sappiamo.
    ''' </summary>
    Public Function CercaIntorno(latitudine As Double, longitudine As Double,
                                 Optional raggioKm As Double = 8,
                                 Optional massimo As Integer = 300) As List(Of VoceWikidata)
        Dim risultato As New List(Of VoceWikidata)
        Try
            If latitudine = 0 AndAlso longitudine = 0 Then Return risultato

            Dim chiave As String = latitudine.ToString("F3", CultureInfo.InvariantCulture) & "," &
                                   longitudine.ToString("F3", CultureInfo.InvariantCulture) & "," &
                                   raggioKm.ToString("F1", CultureInfo.InvariantCulture)

            SyncLock _lockCache
                If _cache.ContainsKey(chiave) Then Return _cache(chiave)
            End SyncLock

            Dim query As String = ComponiQuery(latitudine, longitudine, raggioKm, massimo)
            Dim json As String = InterrogaSparql(query)
            If String.IsNullOrWhiteSpace(json) Then Return risultato

            risultato = LeggiRisposta(json)

            SyncLock _lockCache
                _cache(chiave) = risultato
            End SyncLock
        Catch ex As Exception
            ScriviLog("CLSWikidata.CercaIntorno: " & ex.ToString())
        End Try
        Return risultato
    End Function

    ''' <summary>
    ''' Cerca fra le voci di una zona quella che corrisponde al nome indicato,
    ''' confrontando anche il titolo della pagina Wikipedia.
    ''' </summary>
    Public Function TrovaPerNome(voci As List(Of VoceWikidata), nome As String,
                                 latitudine As Double, longitudine As Double) As VoceWikidata
        Try
            If voci Is Nothing OrElse voci.Count = 0 OrElse String.IsNullOrWhiteSpace(nome) Then Return Nothing

            Dim cercato As String = Normalizza(nome)
            If cercato.Length < 4 Then Return Nothing

            Dim migliore As VoceWikidata = Nothing
            Dim distanzaMigliore As Double = Double.MaxValue

            For Each v In voci
                Try
                    Dim titolo As String = Normalizza(v.Titolo)
                    If titolo.Length < 4 Then Continue For

                    Dim compatibili As Boolean = (titolo = cercato)
                    If Not compatibili Then
                        compatibili = titolo.Contains(cercato) OrElse cercato.Contains(titolo)
                    End If
                    If Not compatibili Then Continue For

                    If latitudine <> 0 AndAlso longitudine <> 0 AndAlso v.Latitudine <> 0 AndAlso v.Longitudine <> 0 Then
                        Dim d As Double = DistanzaChilometri(latitudine, longitudine, v.Latitudine, v.Longitudine)
                        If d > 0.5 Then Continue For
                        If d < distanzaMigliore Then
                            distanzaMigliore = d
                            migliore = v
                        End If
                    ElseIf migliore Is Nothing Then
                        migliore = v
                    End If
                Catch
                End Try
            Next

            Return migliore
        Catch ex As Exception
            ScriviLog("CLSWikidata.TrovaPerNome: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Righe pronte da leggere con le informazioni enciclopediche di una voce.
    ''' </summary>
    Public Function InformazioniVoce(v As VoceWikidata) As List(Of String)
        Dim righe As New List(Of String)
        Try
            If v Is Nothing Then Return righe

            If Not String.IsNullOrWhiteSpace(v.Descrizione) Then
                righe.Add("Che cos'e': " & v.Descrizione)
            End If
            If Not String.IsNullOrWhiteSpace(v.Categoria) Then
                righe.Add("Tipo: " & v.Categoria)
            End If
            If Not String.IsNullOrWhiteSpace(v.AnnoInizio) Then
                righe.Add("Risale al: " & v.AnnoInizio)
            End If
            If Not String.IsNullOrWhiteSpace(v.Stile) Then
                righe.Add("Stile: " & v.Stile)
            End If
            If Not String.IsNullOrWhiteSpace(v.Architetto) Then
                righe.Add("Progettato da: " & v.Architetto)
            End If
            If Not String.IsNullOrWhiteSpace(v.PaginaWikipedia) Then
                righe.Add("Pagina di Wikipedia: " & v.PaginaWikipedia)
            End If

            If righe.Count > 0 Then
                righe.Add("Fonte enciclopedica: Wikidata e Wikipedia")
            End If
        Catch ex As Exception
            ScriviLog("CLSWikidata.InformazioniVoce: " & ex.Message)
        End Try
        Return righe
    End Function

#End Region

#Region "Interrogazione SPARQL"

    Private Function ComponiQuery(latitudine As Double, longitudine As Double, raggioKm As Double, massimo As Integer) As String
        Dim q As New StringBuilder()
        Try
            Dim lat As String = latitudine.ToString("R", CultureInfo.InvariantCulture)
            Dim lon As String = longitudine.ToString("R", CultureInfo.InvariantCulture)
            Dim raggio As String = raggioKm.ToString("R", CultureInfo.InvariantCulture)

            q.Append("SELECT ?voce ?voceLabel ?voceDescription ?tipoLabel ?inizio ?architettoLabel ?stileLabel ?coord ?articolo WHERE { ")
            q.Append("SERVICE wikibase:around { ?voce wdt:P625 ?coord . ")
            q.Append("bd:serviceParam wikibase:center ""Point(").Append(lon).Append(" ").Append(lat).Append(")""^^geo:wktLiteral . ")
            q.Append("bd:serviceParam wikibase:radius """).Append(raggio).Append(""" . } ")
            q.Append("OPTIONAL { ?voce wdt:P31 ?tipo . } ")
            q.Append("OPTIONAL { ?voce wdt:P571 ?inizio . } ")
            q.Append("OPTIONAL { ?voce wdt:P84 ?architetto . } ")
            q.Append("OPTIONAL { ?voce wdt:P149 ?stile . } ")
            q.Append("OPTIONAL { ?articolo schema:about ?voce ; schema:isPartOf <https://it.wikipedia.org/> . } ")
            q.Append("SERVICE wikibase:label { bd:serviceParam wikibase:language ""it,en"". } ")
            q.Append("} LIMIT ").Append(Math.Max(1, massimo).ToString(CultureInfo.InvariantCulture))
        Catch ex As Exception
            ScriviLog("CLSWikidata.ComponiQuery: " & ex.ToString())
            Return ""
        End Try
        Return q.ToString()
    End Function

    Private Function InterrogaSparql(query As String) As String
        Try
            If String.IsNullOrWhiteSpace(query) Then Return ""

            Dim url As String = URL_SPARQL & "?format=json&query=" & Uri.EscapeDataString(query)

            For tentativo As Integer = 1 To 2
                Try
                    Dim risposta = _http.GetAsync(url).GetAwaiter().GetResult()
                    If risposta.IsSuccessStatusCode Then
                        Dim testo As String = risposta.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        If Not String.IsNullOrWhiteSpace(testo) Then Return testo
                    Else
                        If CInt(risposta.StatusCode) = 429 Then
                            Try
                                Threading.Thread.Sleep(3000)
                            Catch
                            End Try
                        End If
                    End If
                Catch exTentativo As Exception
                    ScriviLog("CLSWikidata.InterrogaSparql tentativo " & tentativo.ToString() & ": " & exTentativo.Message)
                End Try
            Next
            Return ""
        Catch ex As Exception
            ScriviLog("CLSWikidata.InterrogaSparql: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function LeggiRisposta(json As String) As List(Of VoceWikidata)
        Dim elenco As New List(Of VoceWikidata)
        Try
            Dim radice As JObject = JObject.Parse(json)
            Dim risultati = radice("results")
            If risultati Is Nothing Then Return elenco
            Dim righe = risultati("bindings")
            If righe Is Nothing OrElse righe.Type <> JTokenType.Array Then Return elenco

            Dim giaVisti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each riga As JToken In righe.Children()
                Try
                    Dim v As New VoceWikidata()
                    v.Identificativo = UltimaParte(Valore(riga, "voce"))
                    v.Titolo = Valore(riga, "voceLabel")
                    v.Descrizione = Valore(riga, "voceDescription")
                    v.Categoria = Valore(riga, "tipoLabel")
                    v.AnnoInizio = SoloAnno(Valore(riga, "inizio"))
                    v.Architetto = Valore(riga, "architettoLabel")
                    v.Stile = Valore(riga, "stileLabel")
                    v.PaginaWikipedia = Valore(riga, "articolo")

                    If String.IsNullOrWhiteSpace(v.Titolo) Then Continue For
                    If v.Titolo.StartsWith("Q", StringComparison.OrdinalIgnoreCase) AndAlso v.Titolo.Length < 12 Then Continue For
                    If String.IsNullOrWhiteSpace(v.Descrizione) AndAlso String.IsNullOrWhiteSpace(v.PaginaWikipedia) Then Continue For

                    LeggiCoordinate(Valore(riga, "coord"), v)

                    Dim chiave As String = If(v.Identificativo.Length > 0, v.Identificativo, Normalizza(v.Titolo))
                    If giaVisti.Contains(chiave) Then Continue For
                    giaVisti.Add(chiave)

                    elenco.Add(v)
                Catch exRiga As Exception
                    ScriviLog("CLSWikidata.LeggiRisposta riga: " & exRiga.Message)
                End Try
            Next
        Catch ex As Exception
            ScriviLog("CLSWikidata.LeggiRisposta: " & ex.ToString())
        End Try
        Return elenco
    End Function

    Private Sub LeggiCoordinate(testo As String, v As VoceWikidata)
        Try
            If String.IsNullOrWhiteSpace(testo) OrElse v Is Nothing Then Return
            ' Formato atteso: Point(11.123456 43.654321)
            Dim inizio As Integer = testo.IndexOf("("c)
            Dim fine As Integer = testo.IndexOf(")"c)
            If inizio < 0 OrElse fine <= inizio Then Return

            Dim contenuto As String = testo.Substring(inizio + 1, fine - inizio - 1).Trim()
            Dim parti = contenuto.Split(" "c)
            If parti.Length < 2 Then Return

            Dim lon As Double = 0
            Dim lat As Double = 0
            If Double.TryParse(parti(0), NumberStyles.Any, CultureInfo.InvariantCulture, lon) AndAlso
               Double.TryParse(parti(1), NumberStyles.Any, CultureInfo.InvariantCulture, lat) Then
                v.Longitudine = lon
                v.Latitudine = lat
            End If
        Catch ex As Exception
            ScriviLog("CLSWikidata.LeggiCoordinate: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Utilita"

    Private Function Valore(riga As JToken, nome As String) As String
        Try
            If riga Is Nothing Then Return ""
            Dim campo = riga(nome)
            If campo Is Nothing Then Return ""
            Dim v = campo("value")
            If v Is Nothing OrElse v.Type = JTokenType.Null Then Return ""
            Return v.ToString().Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Function UltimaParte(url As String) As String
        Try
            If String.IsNullOrWhiteSpace(url) Then Return ""
            Dim posizione As Integer = url.LastIndexOf("/"c)
            If posizione < 0 Then Return url.Trim()
            Return url.Substring(posizione + 1).Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Function SoloAnno(data As String) As String
        Try
            If String.IsNullOrWhiteSpace(data) Then Return ""
            Dim d As DateTime
            If DateTime.TryParse(data, Nothing, DateTimeStyles.RoundtripKind, d) Then
                Return d.Year.ToString(CultureInfo.InvariantCulture)
            End If
            If data.Length >= 4 Then
                Dim anno As String = data.Substring(0, 4)
                Dim n As Integer = 0
                If Integer.TryParse(anno, n) AndAlso n > 0 Then Return anno
            End If
            Return ""
        Catch
            Return ""
        End Try
    End Function

    Private Function Normalizza(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim s As String = testo.Trim().ToUpperInvariant()
            s = s.Replace("À", "A").Replace("È", "E").Replace("É", "E").Replace("Ì", "I").Replace("Ò", "O").Replace("Ù", "U")
            s = s.Replace("'", " ").Replace("`", " ").Replace("-", " ").Replace(",", " ")
            While s.Contains("  ")
                s = s.Replace("  ", " ")
            End While
            Return s.Trim()
        Catch
            Return If(testo, "").ToUpperInvariant()
        End Try
    End Function

    Private Function DistanzaChilometri(lat1 As Double, lon1 As Double, lat2 As Double, lon2 As Double) As Double
        Try
            Dim raggio As Double = 6371.0
            Dim dLat As Double = (lat2 - lat1) * Math.PI / 180.0
            Dim dLon As Double = (lon2 - lon1) * Math.PI / 180.0
            Dim a As Double = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                              Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                              Math.Sin(dLon / 2) * Math.Sin(dLon / 2)
            Dim c As Double = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a))
            Return raggio * c
        Catch
            Return 0
        End Try
    End Function

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