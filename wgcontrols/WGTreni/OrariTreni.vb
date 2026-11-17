Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports wgcontrols

' ============================================================================================
' OrariTreni - gli orari programmati dei treni, letti dai file GTFS
' ============================================================================================
'
' Fonti
'   Trenitalia: orario nazionale (Frecce, Intercity, regionali) convertito dai dati NeTEx del
'               punto di accesso nazionale; licenza CC BY 4.0.
'   Trenord:    orario dei treni lombardi, dati aperti della Regione Lombardia.
'   Italo non pubblica un orario: i suoi treni si trovano solo in tempo reale (TempoRealeTreni).
'
' I file zip si scaricano nella cartella dati "treni" e si rinnovano dopo sette giorni. Se il
' download non riesce si usa la copia già scaricata. Gli orari restano in memoria finché
' WinGabriel è aperto.
' ============================================================================================

''' <summary>Una stazione degli orari: le fermate vicine con lo stesso nome sono unite.</summary>
Friend Class StazioneOrario
    Public Id As Integer
    Public Nome As String = ""
    Public Chiave As String = ""
    Public Lat As Double
    Public Lon As Double
    Public Passaggi As Integer
End Class

''' <summary>Un giorno di servizio: i giorni della settimana, il periodo e le eccezioni.</summary>
Friend Class ServizioOrario
    Public Giorni As Boolean() = Nothing
    Public Da As Integer
    Public A As Integer
    Public Aggiunte As HashSet(Of Integer) = Nothing
    Public Rimosse As HashSet(Of Integer) = Nothing

    ''' <summary>Vero se il servizio circola nel giorno dato (numero di giorni dal 1970; dow 0 = lunedì).</summary>
    Public Function Attivo(giorno As Integer, dow As Integer) As Boolean
        If Rimosse IsNot Nothing AndAlso Rimosse.Contains(giorno) Then Return False
        If Aggiunte IsNot Nothing AndAlso Aggiunte.Contains(giorno) Then Return True
        If Giorni Is Nothing Then Return False
        Return giorno >= Da AndAlso giorno <= A AndAlso Giorni(dow)
    End Function
End Class

''' <summary>Una corsa: un treno con le sue fermate e i suoi orari (secondi dalla mezzanotte, anche oltre le 24 ore).</summary>
Friend Class CorsaOrario
    Public Numero As String = ""
    Public Categoria As String = ""
    Public NomeCategoria As String = ""
    Public Vettore As String = ""
    Public Linea As String = ""
    Public Servizio As Integer
    Public Stazioni As Integer()
    Public Arrivi As Integer()
    Public Partenze As Integer()
    Public Sale As Boolean()
    Public Scende As Boolean()
    Public AltaVelocita As Boolean
    Public Veloce As Boolean

    ''' <summary>Per esempio "Frecciarossa 9432" oppure "Trenord S5 2226".</summary>
    Public Function Etichetta() As String
        If Vettore = "Trenord" Then
            Return "Trenord " & If(String.IsNullOrEmpty(Linea), "", Linea & " ") & Numero
        End If
        Return NomeCategoria & " " & Numero
    End Function
End Class

Friend NotInheritable Class OrariTreni

    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _blocco As New Object()
    Private Shared _corrente As OrariTreni
    Private Shared _client As HttpClient

    Private Const GiorniValiditaFile As Double = 7

    Private Class FonteOrari
        Public Nome As String
        Public NomeFile As String
        Public Url As String
        Public Trenord As Boolean
    End Class

    Private Shared ReadOnly Fonti As FonteOrari() = {
        New FonteOrari With {.Nome = "Trenitalia", .NomeFile = "trenitalia.zip", .Url = "https://github.com/deryclem/trenitalia-gtfs/raw/main/gtfs-trenitalia.zip", .Trenord = False},
        New FonteOrari With {.Nome = "Trenord", .NomeFile = "trenord.zip", .Url = "https://www.dati.lombardia.it/download/3z4k-mxz9/application%2Fzip", .Trenord = True}
    }

    Private Shared ReadOnly _epoca As New DateTime(1970, 1, 1)

    Public ReadOnly Stazioni As New List(Of StazioneOrario)()
    Public ReadOnly Corse As New List(Of CorsaOrario)()
    Public ReadOnly Servizi As New List(Of ServizioOrario)()
    Public ReadOnly Avvisi As New List(Of String)()
    Public PrimoGiorno As Integer = Integer.MaxValue
    Public UltimoGiorno As Integer = Integer.MinValue

    Private Sub New()
    End Sub

#Region "Giorni"

    Public Shared Function GiornoDa(data As DateTime) As Integer
        Return CInt((data.Date - _epoca).TotalDays)
    End Function

    Public Shared Function DataDa(giorno As Integer) As DateTime
        Return _epoca.AddDays(giorno)
    End Function

    ''' <summary>Giorno della settimana con lunedì = 0.</summary>
    Public Shared Function GiornoSettimana(data As DateTime) As Integer
        Return (CInt(data.DayOfWeek) + 6) Mod 7
    End Function

    Private Shared Function GiornoDaTesto(testo As String) As Integer
        ' aaaammgg
        If testo Is Nothing OrElse testo.Length < 8 Then Return 0
        Dim a As Integer = (AscW(testo(0)) - 48) * 1000 + (AscW(testo(1)) - 48) * 100 + (AscW(testo(2)) - 48) * 10 + (AscW(testo(3)) - 48)
        Dim m As Integer = (AscW(testo(4)) - 48) * 10 + (AscW(testo(5)) - 48)
        Dim g As Integer = (AscW(testo(6)) - 48) * 10 + (AscW(testo(7)) - 48)
        Return GiornoDa(New DateTime(a, m, g))
    End Function

    Private Shared Function SecondiDaTesto(testo As String) As Integer
        If String.IsNullOrWhiteSpace(testo) Then Return -1
        Dim p As String() = testo.Trim().Split(":"c)
        If p.Length < 2 Then Return -1
        Dim h As Integer
        Dim m As Integer
        Dim s As Integer = 0
        If Not Integer.TryParse(p(0), NumberStyles.Integer, CultureInfo.InvariantCulture, h) Then Return -1
        If Not Integer.TryParse(p(1), NumberStyles.Integer, CultureInfo.InvariantCulture, m) Then Return -1
        If p.Length > 2 Then Integer.TryParse(p(2), NumberStyles.Integer, CultureInfo.InvariantCulture, s)
        Return h * 3600 + m * 60 + s
    End Function

#End Region

#Region "Nomi"

    ''' <summary>Il nome in forma di confronto: minuscolo, senza accenti né punteggiatura, con le abbreviazioni comuni sciolte.</summary>
    Public Shared Function Normalizza(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim d As String = nome.Normalize(NormalizationForm.FormD)
            Dim sb As New StringBuilder(d.Length)
            For Each c As Char In d
                If CharUnicodeInfo.GetUnicodeCategory(c) = UnicodeCategory.NonSpacingMark Then Continue For
                If Char.IsLetterOrDigit(c) Then
                    sb.Append(Char.ToLowerInvariant(c))
                Else
                    sb.Append(" "c)
                End If
            Next
            Dim t As String = " " & Regex.Replace(sb.ToString(), "\s+", " ").Trim() & " "
            t = t.Replace(" c le ", " centrale ").Replace(" p ta ", " porta ").Replace(" p le ", " piazzale ")
            t = t.Replace(" c l ", " centrale ").Replace(" s lucia ", " santa lucia ").Replace(" s m novella ", " santa maria novella ")
            Return t.Trim()
        Catch ex As Exception
            _log.ScriviLog("OrariTreni.Normalizza: " & ex.Message)
            Return If(nome, "").ToLowerInvariant()
        End Try
    End Function

    ''' <summary>Il nome in forma leggibile: le maiuscole di Trenitalia diventano iniziali maiuscole.</summary>
    Private Shared Function NomeBello(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim s As String = nome.Trim()
            Dim lettere As Integer = 0
            Dim maiuscole As Integer = 0
            For Each c As Char In s
                If Char.IsLetter(c) Then
                    lettere += 1
                    If Char.IsUpper(c) Then maiuscole += 1
                End If
            Next
            If lettere = 0 OrElse maiuscole < lettere * 0.8 Then Return s
            s = s.ToUpperInvariant()
            s = s.Replace("C.LE", "CENTRALE").Replace("P.TA", "PORTA").Replace("P.LE", "PIAZZALE")
            s = Regex.Replace(s, "\.(?=\S)", ". ")
            Return CultureInfo.GetCultureInfo("it-IT").TextInfo.ToTitleCase(s.ToLowerInvariant())
        Catch ex As Exception
            _log.ScriviLog("OrariTreni.NomeBello: " & ex.Message)
            Return nome
        End Try
    End Function

    ''' <summary>Il nome esteso di una sigla di categoria, per esempio FR diventa Frecciarossa.</summary>
    Friend Shared Function NomeCategoriaPubblico(sigla As String) As String
        Return NomeCategoria(sigla, "")
    End Function

    Private Shared Function NomeCategoria(sigla As String, nomeFile As String) As String
        Select Case sigla
            Case "FR" : Return "Frecciarossa"
            Case "FA" : Return "Frecciargento"
            Case "FB" : Return "Frecciabianca"
            Case "IC" : Return "Intercity"
            Case "ICN" : Return "Intercity Notte"
            Case "EC" : Return "Eurocity"
            Case "EN" : Return "Euronight"
            Case "REG" : Return "Regionale"
            Case "RV" : Return "Regionale Veloce"
            Case "SFM" : Return "Suburbano"
            Case "MET" : Return "Metropolitano"
            Case "DIR" : Return "Diretto"
            Case "EXP" : Return "Espresso"
        End Select
        If Not String.IsNullOrWhiteSpace(nomeFile) Then Return nomeFile
        Return sigla
    End Function

#End Region

#Region "Download"

    Private Shared Function Client() As HttpClient
        If _client Is Nothing Then
            Net.ServicePointManager.SecurityProtocol = Net.ServicePointManager.SecurityProtocol Or Net.SecurityProtocolType.Tls12
            _client = New HttpClient()
            _client.Timeout = TimeSpan.FromMinutes(3)
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Treni/1.0")
        End If
        Return _client
    End Function

    ''' <summary>
    ''' I file degli orari si scaricano in dati\cache\treni: non fanno parte dei dati dell'utente (ricerche e corse salvate stanno in
    ''' dati\treni) e non vanno sincronizzati.
    ''' </summary>
    Private Shared Function CartellaCache() As String
        Dim cartella As String = Path.Combine(WGDb.WGDatabase.CartellaDati("cache"), "treni")
        If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
        ' I file scaricati nelle versioni precedenti stavano in dati\treni: si tolgono, perché non servono più.
        Try
            Dim vecchia As String = WGDb.WGDatabase.CartellaDati("treni")
            For Each nome As String In New String() {"trenitalia.zip", "trenord.zip", "trenitalia.zip.scarico", "trenord.zip.scarico"}
                Dim percorso As String = Path.Combine(vecchia, nome)
                If File.Exists(percorso) Then File.Delete(percorso)
            Next
        Catch ex As Exception
            _log.ScriviLog("OrariTreni.CartellaCache (pulizia): " & ex.Message)
        End Try
        Return cartella
    End Function

    ''' <summary>Scarica i file che mancano o sono vecchi. Restituisce le fonti che non si sono potute avere in nessun modo.</summary>
    Private Shared Async Function ScaricaSeServeAsync() As Task(Of List(Of String))
        Dim mancanti As New List(Of String)()
        Try
            Dim cartella As String = CartellaCache()
            For Each f As FonteOrari In Fonti
                Dim percorso As String = Path.Combine(cartella, f.NomeFile)
                Try
                    Dim serve As Boolean = Not File.Exists(percorso) OrElse (DateTime.UtcNow - File.GetLastWriteTimeUtc(percorso)).TotalDays > GiorniValiditaFile
                    If serve Then
                        Dim temporaneo As String = percorso & ".scarico"
                        Try
                            Using risposta As HttpResponseMessage = Await Client().GetAsync(f.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(False)
                                risposta.EnsureSuccessStatusCode()
                                Using sorgente As Stream = Await risposta.Content.ReadAsStreamAsync().ConfigureAwait(False)
                                    Using destinazione As New FileStream(temporaneo, FileMode.Create, FileAccess.Write)
                                        Await sorgente.CopyToAsync(destinazione).ConfigureAwait(False)
                                    End Using
                                End Using
                            End Using
                            ' Si controlla che sia davvero uno zip con le tabelle prima di sostituire il file buono.
                            Using z As ZipArchive = ZipFile.OpenRead(temporaneo)
                                If z.GetEntry("stop_times.txt") Is Nothing Then Throw New InvalidDataException("Il file non contiene gli orari")
                            End Using
                            If File.Exists(percorso) Then File.Delete(percorso)
                            File.Move(temporaneo, percorso)
                        Catch exScarico As Exception
                            _log.ScriviLog("OrariTreni.ScaricaSeServe " & f.Nome & ": " & exScarico.Message)
                            Try
                                If File.Exists(temporaneo) Then File.Delete(temporaneo)
                            Catch
                            End Try
                        End Try
                    End If
                Catch exFonte As Exception
                    _log.ScriviLog("OrariTreni.ScaricaSeServe fonte " & f.Nome & ": " & exFonte.Message)
                End Try
                If Not File.Exists(percorso) Then mancanti.Add(f.Nome)
            Next
        Catch ex As Exception
            _log.ScriviLog("OrariTreni.ScaricaSeServe: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
        Return mancanti
    End Function

#End Region

#Region "Lettura dei file"

    Private Shared Function Dividi(riga As String) As String()
        Dim campi As New List(Of String)(16)
        Dim i As Integer = 0
        Dim n As Integer = riga.Length
        While True
            If i < n AndAlso riga(i) = """"c Then
                Dim sb As New StringBuilder()
                i += 1
                While i < n
                    If riga(i) = """"c Then
                        If i + 1 < n AndAlso riga(i + 1) = """"c Then
                            sb.Append(""""c)
                            i += 2
                        Else
                            i += 1
                            Exit While
                        End If
                    Else
                        sb.Append(riga(i))
                        i += 1
                    End If
                End While
                campi.Add(sb.ToString())
                While i < n AndAlso riga(i) <> ","c
                    i += 1
                End While
            Else
                Dim j As Integer = riga.IndexOf(","c, i)
                If j < 0 Then j = n
                campi.Add(riga.Substring(i, j - i))
                i = j
            End If
            If i >= n Then Exit While
            i += 1
            If i = n Then
                campi.Add("")
                Exit While
            End If
        End While
        Return campi.ToArray()
    End Function

    ''' <summary>La tabella di un file GTFS: la prima riga dà i nomi delle colonne.</summary>
    Private Class Tabella
        Private ReadOnly _lettore As StreamReader
        Private ReadOnly _indici As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        Private _riga As String()

        Public Sub New(archivio As ZipArchive, nome As String)
            Dim voce As ZipArchiveEntry = archivio.GetEntry(nome)
            If voce Is Nothing Then Return
            _lettore = New StreamReader(voce.Open(), New UTF8Encoding(False), True, 65536)
            Dim intestazione As String = _lettore.ReadLine()
            If intestazione Is Nothing Then Return
            Dim colonne As String() = Dividi(intestazione)
            For i As Integer = 0 To colonne.Length - 1
                Dim c As String = colonne(i).Trim().Trim(ChrW(&HFEFF))
                If Not _indici.ContainsKey(c) Then _indici.Add(c, i)
            Next
        End Sub

        Public ReadOnly Property Esiste As Boolean
            Get
                Return _lettore IsNot Nothing
            End Get
        End Property

        Public Function Avanti() As Boolean
            If _lettore Is Nothing Then Return False
            Dim l As String = _lettore.ReadLine()
            While l IsNot Nothing AndAlso l.Length = 0
                l = _lettore.ReadLine()
            End While
            If l Is Nothing Then
                _lettore.Dispose()
                Return False
            End If
            _riga = Dividi(l)
            Return True
        End Function

        Public Function Campo(nome As String) As String
            Dim i As Integer
            If _riga Is Nothing OrElse Not _indici.TryGetValue(nome, i) OrElse i >= _riga.Length Then Return ""
            Return _riga(i)
        End Function
    End Class

    Private Class FermataTemporanea
        Public Sequenza As Integer
        Public Stazione As Integer
        Public Arrivo As Integer
        Public Partenza As Integer
        Public Sale As Boolean
        Public Scende As Boolean
    End Class

    Private Class CorsaTemporanea
        Public Corsa As CorsaOrario
        Public ServizioId As String
        Public Fermate As New List(Of FermataTemporanea)()
    End Class

    Private Class StazioneGrezza
        Public Nome As String
        Public Lat As Double
        Public Lon As Double
        Public Usata As Boolean
        Public Trenord As Boolean
    End Class

    Private Class Costruttore
        Public ReadOnly Grezze As New List(Of StazioneGrezza)()
        Public ReadOnly Corse As New List(Of CorsaTemporanea)()
    End Class

    Private Shared Function Decimale(testo As String) As Double
        Dim v As Double
        If Double.TryParse(testo, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return v
        Return 0
    End Function

    ''' <summary>Legge un file GTFS e aggiunge corse, fermate e servizi alle liste.</summary>
    Private Sub LeggiFonte(percorso As String, fonte As FonteOrari, costr As Costruttore, mappaServizi As Dictionary(Of String, Integer))
        Using archivio As ZipArchive = ZipFile.OpenRead(percorso)

            ' Linee: si scartano gli autobus.
            Dim sigleLinee As New Dictionary(Of String, String)()
            Dim nomiLinee As New Dictionary(Of String, String)()
            Dim lineeAmmesse As New HashSet(Of String)()
            Dim tr As New Tabella(archivio, "routes.txt")
            While tr.Avanti()
                Dim id As String = tr.Campo("route_id")
                Dim breve As String = tr.Campo("route_short_name")
                Dim lungo As String = tr.Campo("route_long_name")
                Dim tipo As String = tr.Campo("route_type")
                If fonte.Trenord Then
                    If id = "TN_Bus" Then Continue While
                Else
                    If breve.EndsWith("_BUS", StringComparison.OrdinalIgnoreCase) OrElse tipo = "3" Then Continue While
                End If
                lineeAmmesse.Add(id)
                sigleLinee(id) = If(fonte.Trenord, breve, If(breve.Contains("_"), breve.Substring(breve.LastIndexOf("_"c) + 1), breve))
                nomiLinee(id) = lungo
            End While

            ' Fermate.
            Dim fermate As New Dictionary(Of String, String())()
            Dim ts As New Tabella(archivio, "stops.txt")
            While ts.Avanti()
                fermate(ts.Campo("stop_id")) = New String() {ts.Campo("stop_name"), ts.Campo("stop_lat"), ts.Campo("stop_lon"), ts.Campo("parent_station")}
            End While

            ' Stazioni grezze: la fermata, o la sua stazione madre se c'è.
            Dim indiceGrezza As New Dictionary(Of String, Integer)()
            Dim DaFermata As Func(Of String, Integer) =
                Function(stopId As String) As Integer
                    Dim dati As String() = Nothing
                    If Not fermate.TryGetValue(stopId, dati) Then Return -1
                    Dim chiave As String = stopId
                    Dim nome As String = dati(0)
                    Dim lat As String = dati(1)
                    Dim lon As String = dati(2)
                    Dim madre As String = dati(3)
                    Dim datiMadre As String() = Nothing
                    If Not String.IsNullOrEmpty(madre) AndAlso fermate.TryGetValue(madre, datiMadre) Then
                        chiave = madre
                        nome = datiMadre(0)
                        lat = datiMadre(1)
                        lon = datiMadre(2)
                    End If
                    Dim idx As Integer
                    If Not indiceGrezza.TryGetValue(chiave, idx) Then
                        idx = costr.Grezze.Count
                        indiceGrezza(chiave) = idx
                        costr.Grezze.Add(New StazioneGrezza With {.Nome = nome, .Lat = Decimale(lat), .Lon = Decimale(lon), .Trenord = fonte.Trenord})
                    End If
                    Return idx
                End Function

            ' Corse.
            Dim perId As New Dictionary(Of String, CorsaTemporanea)()
            Dim tt As New Tabella(archivio, "trips.txt")
            While tt.Avanti()
                Dim linea As String = tt.Campo("route_id")
                If Not lineeAmmesse.Contains(linea) Then Continue While
                Dim c As New CorsaOrario()
                Dim breveCorsa As String = tt.Campo("trip_short_name")
                If fonte.Trenord Then
                    c.Vettore = "Trenord"
                    c.Categoria = "TN"
                    c.NomeCategoria = "Trenord"
                    c.Linea = sigleLinee(linea)
                    Dim p As Integer = breveCorsa.LastIndexOf(" - ", StringComparison.Ordinal)
                    c.Numero = If(p >= 0, breveCorsa.Substring(p + 3).Trim(), breveCorsa)
                    Dim sigla As String = c.Linea
                    c.Veloce = sigla.StartsWith("RE", StringComparison.OrdinalIgnoreCase)
                Else
                    c.Vettore = "Trenitalia"
                    c.Categoria = sigleLinee(linea)
                    c.NomeCategoria = NomeCategoria(c.Categoria, nomiLinee(linea))
                    c.Numero = breveCorsa
                    c.AltaVelocita = (c.Categoria = "FR" OrElse c.Categoria = "FA")
                    Select Case c.Categoria
                        Case "FR", "FA", "FB", "IC", "ICN", "EC", "EN", "EXP"
                            c.Veloce = True
                    End Select
                End If
                Dim t As New CorsaTemporanea()
                t.Corsa = c
                t.ServizioId = tt.Campo("service_id")
                perId(tt.Campo("trip_id")) = t
                costr.Corse.Add(t)
            End While

            ' Orari delle fermate.
            Dim tf As New Tabella(archivio, "stop_times.txt")
            While tf.Avanti()
                Dim t As CorsaTemporanea = Nothing
                If Not perId.TryGetValue(tf.Campo("trip_id"), t) Then Continue While
                Dim st As Integer = DaFermata(tf.Campo("stop_id"))
                If st < 0 Then Continue While
                Dim arrivo As Integer = SecondiDaTesto(tf.Campo("arrival_time"))
                Dim partenza As Integer = SecondiDaTesto(tf.Campo("departure_time"))
                If arrivo < 0 Then arrivo = partenza
                If partenza < 0 Then partenza = arrivo
                If arrivo < 0 Then Continue While
                Dim seq As Integer
                Integer.TryParse(tf.Campo("stop_sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, seq)
                Dim f As New FermataTemporanea()
                f.Sequenza = seq
                f.Stazione = st
                f.Arrivo = arrivo
                f.Partenza = partenza
                f.Sale = tf.Campo("pickup_type") <> "1"
                f.Scende = tf.Campo("drop_off_type") <> "1"
                t.Fermate.Add(f)
                costr.Grezze(st).Usata = True
            End While

            ' Servizi usati da queste corse.
            Dim usati As New HashSet(Of String)()
            For Each t As CorsaTemporanea In costr.Corse
                usati.Add(t.ServizioId)
            Next
            Dim locali As New Dictionary(Of String, ServizioOrario)()
            Dim DaId As Func(Of String, ServizioOrario) =
                Function(id As String) As ServizioOrario
                    Dim s As ServizioOrario = Nothing
                    If Not locali.TryGetValue(id, s) Then
                        s = New ServizioOrario()
                        locali(id) = s
                    End If
                    Return s
                End Function

            Dim tc As New Tabella(archivio, "calendar.txt")
            While tc.Avanti()
                Dim id As String = tc.Campo("service_id")
                If Not usati.Contains(id) Then Continue While
                Dim s As ServizioOrario = DaId(id)
                s.Giorni = New Boolean() {tc.Campo("monday") = "1", tc.Campo("tuesday") = "1", tc.Campo("wednesday") = "1", tc.Campo("thursday") = "1", tc.Campo("friday") = "1", tc.Campo("saturday") = "1", tc.Campo("sunday") = "1"}
                s.Da = GiornoDaTesto(tc.Campo("start_date"))
                s.A = GiornoDaTesto(tc.Campo("end_date"))
                If s.Da > 0 AndAlso s.Da < PrimoGiorno Then PrimoGiorno = s.Da
                If s.A > UltimoGiorno Then UltimoGiorno = s.A
            End While

            Dim td As New Tabella(archivio, "calendar_dates.txt")
            While td.Avanti()
                Dim id As String = td.Campo("service_id")
                If Not usati.Contains(id) Then Continue While
                Dim s As ServizioOrario = DaId(id)
                Dim giorno As Integer = GiornoDaTesto(td.Campo("date"))
                If giorno <= 0 Then Continue While
                If td.Campo("exception_type") = "1" Then
                    If s.Aggiunte Is Nothing Then s.Aggiunte = New HashSet(Of Integer)()
                    s.Aggiunte.Add(giorno)
                    If giorno < PrimoGiorno Then PrimoGiorno = giorno
                    If giorno > UltimoGiorno Then UltimoGiorno = giorno
                ElseIf td.Campo("exception_type") = "2" Then
                    If s.Rimosse Is Nothing Then s.Rimosse = New HashSet(Of Integer)()
                    s.Rimosse.Add(giorno)
                End If
            End While

            ' I servizi della fonte entrano nell'elenco generale, con un identificatore proprio.
            For Each coppia As KeyValuePair(Of String, ServizioOrario) In locali
                mappaServizi(fonte.Nome & "|" & coppia.Key) = Servizi.Count
                Servizi.Add(coppia.Value)
            Next
            For Each t As CorsaTemporanea In costr.Corse
                If t.Corsa.Vettore = fonte.Nome Then
                    Dim indice As Integer
                    If mappaServizi.TryGetValue(fonte.Nome & "|" & t.ServizioId, indice) Then
                        t.Corsa.Servizio = indice
                    Else
                        t.Corsa.Servizio = -1
                    End If
                End If
            Next
        End Using
    End Sub

#End Region

#Region "Unione delle stazioni"

    Private Shared Function Distanza(a As StazioneGrezza, b As StazioneGrezza) As Double
        Dim dLat As Double = (a.Lat - b.Lat) * 111.0
        Dim dLon As Double = (a.Lon - b.Lon) * 111.0 * Math.Cos(a.Lat * Math.PI / 180.0)
        Return Math.Sqrt(dLat * dLat + dLon * dLon)
    End Function

    Private Shared Function Radice(padre As Integer(), i As Integer) As Integer
        While padre(i) <> i
            padre(i) = padre(padre(i))
            i = padre(i)
        End While
        Return i
    End Function

    ''' <summary>Unisce le fermate della stessa stazione: stesso nome entro 3 km, oppure stessa città entro 250 metri.</summary>
    Private Sub UnisciStazioni(costr As Costruttore, ByRef cluster As Integer())
        Dim n As Integer = costr.Grezze.Count
        Dim padre(n - 1) As Integer
        For i As Integer = 0 To n - 1
            padre(i) = i
        Next
        Dim chiavi(n - 1) As String
        Dim perChiave As New Dictionary(Of String, List(Of Integer))()
        Dim perPrimo As New Dictionary(Of String, List(Of Integer))()
        For i As Integer = 0 To n - 1
            If Not costr.Grezze(i).Usata Then Continue For
            Dim k As String = Normalizza(costr.Grezze(i).Nome)
            chiavi(i) = k
            Dim l As List(Of Integer) = Nothing
            If Not perChiave.TryGetValue(k, l) Then
                l = New List(Of Integer)()
                perChiave(k) = l
            End If
            l.Add(i)
            Dim primo As String = If(k.Contains(" "), k.Substring(0, k.IndexOf(" "c)), k)
            Dim lp As List(Of Integer) = Nothing
            If Not perPrimo.TryGetValue(primo, lp) Then
                lp = New List(Of Integer)()
                perPrimo(primo) = lp
            End If
            lp.Add(i)
        Next
        For Each l As List(Of Integer) In perChiave.Values
            For a As Integer = 1 To l.Count - 1
                For b As Integer = 0 To a - 1
                    If Distanza(costr.Grezze(l(a)), costr.Grezze(l(b))) <= 3.0 Then
                        padre(Radice(padre, l(a))) = Radice(padre, l(b))
                        Exit For
                    End If
                Next
            Next
        Next
        For Each lp As List(Of Integer) In perPrimo.Values
            If lp.Count > 400 Then Continue For
            For a As Integer = 1 To lp.Count - 1
                For b As Integer = 0 To a - 1
                    If costr.Grezze(lp(a)).Trenord <> costr.Grezze(lp(b)).Trenord AndAlso Distanza(costr.Grezze(lp(a)), costr.Grezze(lp(b))) <= 0.25 Then
                        padre(Radice(padre, lp(a))) = Radice(padre, lp(b))
                    End If
                Next
            Next
        Next
        cluster = New Integer(n - 1) {}
        Dim assegnati As New Dictionary(Of Integer, Integer)()
        For i As Integer = 0 To n - 1
            cluster(i) = -1
            If Not costr.Grezze(i).Usata Then Continue For
            Dim r As Integer = Radice(padre, i)
            Dim idx As Integer
            If Not assegnati.TryGetValue(r, idx) Then
                idx = Stazioni.Count
                assegnati(r) = idx
                Dim s As New StazioneOrario()
                s.Id = idx
                s.Nome = NomeBello(costr.Grezze(i).Nome)
                s.Chiave = chiavi(i)
                s.Lat = costr.Grezze(i).Lat
                s.Lon = costr.Grezze(i).Lon
                Stazioni.Add(s)
            ElseIf Not costr.Grezze(i).Trenord Then
                ' Il nome di Trenitalia, scritto in modo uniforme, è quello che si mostra.
                Stazioni(idx).Nome = NomeBello(costr.Grezze(i).Nome)
                Stazioni(idx).Chiave = chiavi(i)
            End If
            cluster(i) = idx
        Next
    End Sub

#End Region

#Region "Costruzione"

    Private Shared Function Costruisci(mancanti As List(Of String)) As OrariTreni
        Dim orari As New OrariTreni()
        Dim cartella As String = CartellaCache()
        Dim costr As New Costruttore()
        Dim mappaServizi As New Dictionary(Of String, Integer)()
        For Each f As FonteOrari In Fonti
            Dim percorso As String = Path.Combine(cartella, f.NomeFile)
            If Not File.Exists(percorso) Then
                orari.Avvisi.Add("Gli orari di " & f.Nome & " non sono disponibili.")
                Continue For
            End If
            Try
                orari.LeggiFonte(percorso, f, costr, mappaServizi)
            Catch ex As Exception
                _log.ScriviLog("OrariTreni.Costruisci lettura " & f.Nome & ": " & ex.Message & Environment.NewLine & ex.StackTrace)
                orari.Avvisi.Add("Gli orari di " & f.Nome & " non si sono potuti leggere.")
            End Try
        Next
        If costr.Corse.Count = 0 Then Throw New InvalidOperationException("Nessun orario disponibile")

        Dim cluster As Integer() = Nothing
        orari.UnisciStazioni(costr, cluster)

        For Each t As CorsaTemporanea In costr.Corse
            If t.Corsa.Servizio < 0 OrElse t.Fermate.Count < 2 Then Continue For
            t.Fermate.Sort(Function(x, y) x.Sequenza.CompareTo(y.Sequenza))
            Dim n As Integer = t.Fermate.Count
            Dim c As CorsaOrario = t.Corsa
            c.Stazioni = New Integer(n - 1) {}
            c.Arrivi = New Integer(n - 1) {}
            c.Partenze = New Integer(n - 1) {}
            c.Sale = New Boolean(n - 1) {}
            c.Scende = New Boolean(n - 1) {}
            For i As Integer = 0 To n - 1
                Dim f As FermataTemporanea = t.Fermate(i)
                c.Stazioni(i) = cluster(f.Stazione)
                c.Arrivi(i) = f.Arrivo
                c.Partenze(i) = Math.Max(f.Partenza, f.Arrivo)
                c.Sale(i) = f.Sale AndAlso i < n - 1
                c.Scende(i) = f.Scende AndAlso i > 0
                If c.Stazioni(i) >= 0 Then orari.Stazioni(c.Stazioni(i)).Passaggi += 1
            Next
            orari.Corse.Add(c)
        Next
        Return orari
    End Function

    ''' <summary>
    ''' Gli orari pronti all'uso: scarica i file se servono e li legge. La prima volta ci vogliono alcuni secondi; poi restano in memoria.
    ''' </summary>
    Public Shared Async Function OttieniAsync() As Task(Of OrariTreni)
        Try
            Dim esistente As OrariTreni = _corrente
            If esistente IsNot Nothing Then Return esistente
            Dim mancanti As List(Of String) = Await ScaricaSeServeAsync().ConfigureAwait(False)
            Dim nuovo As OrariTreni = Await Task.Run(Function() Costruisci(mancanti)).ConfigureAwait(False)
            SyncLock _blocco
                _corrente = nuovo
            End SyncLock
            Return nuovo
        Catch ex As Exception
            _log.ScriviLog("OrariTreni.OttieniAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

#End Region

#Region "Ricerca delle stazioni"

    ''' <summary>Le stazioni più frequentate, per scegliere da un elenco quando non si è scritto niente.</summary>
    Public Function PiuFrequentate(massimo As Integer) As List(Of StazioneOrario)
        Dim elenco As New List(Of StazioneOrario)(Stazioni)
        elenco.Sort(Function(a, b) b.Passaggi.CompareTo(a.Passaggi))
        If elenco.Count > massimo Then elenco.RemoveRange(massimo, elenco.Count - massimo)
        Return elenco
    End Function

    ''' <summary>Le stazioni che corrispondono al testo, le più frequentate per prime.</summary>
    Public Function CercaStazioni(testo As String, Optional massimo As Integer = 12) As List(Of StazioneOrario)
        Try
            Dim q As String = Normalizza(testo)
            If q = "" Then Return New List(Of StazioneOrario)()
            Dim parole As String() = q.Split(" "c)
            Dim trovate As New List(Of KeyValuePair(Of Double, StazioneOrario))()
            For Each s As StazioneOrario In Stazioni
                Dim punteggio As Double = Corrispondenza(s.Chiave, q, parole)
                If punteggio <= 0 Then Continue For
                trovate.Add(New KeyValuePair(Of Double, StazioneOrario)(punteggio + Math.Log10(s.Passaggi + 1) * 2.0, s))
            Next
            trovate.Sort(Function(a, b) b.Key.CompareTo(a.Key))
            Dim risultato As New List(Of StazioneOrario)()
            For Each k As KeyValuePair(Of Double, StazioneOrario) In trovate
                risultato.Add(k.Value)
                If risultato.Count >= massimo Then Exit For
            Next
            Return risultato
        Catch ex As Exception
            _log.ScriviLog("OrariTreni.CercaStazioni: " & ex.Message)
            Return New List(Of StazioneOrario)()
        End Try
    End Function

    ''' <summary>Quanto il nome corrisponde alla ricerca: zero se non corrisponde. Ogni parola cercata deve cominciare una parola del nome.</summary>
    Friend Shared Function Corrispondenza(chiave As String, q As String, parole As String()) As Double
        If chiave = q Then Return 100
        Dim nome As String() = chiave.Split(" "c)
        For Each p As String In parole
            Dim trovata As Boolean = False
            For Each t As String In nome
                If t.StartsWith(p, StringComparison.Ordinal) OrElse (t = "s" AndAlso p.Length > 1 AndAlso p.StartsWith("s", StringComparison.Ordinal)) Then
                    trovata = True
                    Exit For
                End If
            Next
            If Not trovata Then Return 0
        Next
        If chiave.StartsWith(q, StringComparison.Ordinal) Then Return 60
        Return 20
    End Function

#End Region

End Class
