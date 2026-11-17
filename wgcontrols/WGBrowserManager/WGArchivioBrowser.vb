Imports System.Collections.Concurrent
Imports System.Globalization
Imports WGDb
Imports wgcontrols

' Gestione del database locale del browser per preferiti, cronologia, download e credenziali.
Public NotInheritable Class WGArchivioBrowser
    Private Shared _istanza As WGArchivioBrowser
    Private Shared ReadOnly _lockIstanza As New Object()
    Private Shared ReadOnly _logCondiviso As New Logger()

    Private Const TabellaPreferiti As String = "Preferiti"
    Private Const TabellaCronologia As String = "Cronologia"
    Private Const TabellaDownload As String = "Download"
    Private Const TabellaPassword As String = "Password"
    Private Const TabellaPermessi As String = "PermessiSiti"
    Private Const TabellaImpostazioniSiti As String = "ImpostazioniSiti"
    Private Const TabellaDatiPersonali As String = "DatiPersonali"
    Private Const ColonnaChiave As String = "ChiaveWG"
    Private Const FormatoData As String = "yyyy-MM-dd HH:mm:ss"
    Private Const MassimoCronologia As Integer = 20000
    Private Const MassimoDownload As Integer = 1000

    Private ReadOnly _log As New Logger()
    Private ReadOnly _db As WGDatabase
    Private ReadOnly _lock As New Object()
    Private ReadOnly _impostazioniSiti As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _permessi As New ConcurrentDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private _inserimentiCronologia As Integer = 0

    Public Shared ReadOnly Property Istanza As WGArchivioBrowser
        Get
            Try
                If _istanza Is Nothing Then
                    SyncLock _lockIstanza
                        If _istanza Is Nothing Then _istanza = New WGArchivioBrowser()
                    End SyncLock
                End If
            Catch ex As Exception
                Dim registro As New Logger()
                registro.ScriviLog("WGArchivioBrowser.Istanza: " & ex.ToString())
            End Try
            Return _istanza
        End Get
    End Property

    Private Sub New()
        Try
            _db = New WGDatabase("browser", "browser.sqlite")
            If Not DatabasePronto() Then
                _log.ScriviLog("WGArchivioBrowser: impossibile aprire l'archivio del browser")
                Return
            End If
            _db.CreaTabellaSeNonEsiste(TabellaPreferiti, "Titolo TEXT, Indirizzo TEXT, Cartella TEXT, Ordine INTEGER")
            _db.CreaTabellaSeNonEsiste(TabellaCronologia, "Indirizzo TEXT, Titolo TEXT, Host TEXT, UltimaVisita TEXT, Visite INTEGER")
            _db.CreaTabellaSeNonEsiste(TabellaDownload, "Nome TEXT, Percorso TEXT, Indirizzo TEXT, Stato TEXT, Dimensione INTEGER, Data TEXT")
            _db.CreaTabellaSeNonEsiste(TabellaPassword, "Origine TEXT, Utente TEXT, Segreto TEXT, UltimoUso TEXT")
            _db.CreaTabellaSeNonEsiste(TabellaPermessi, "Origine TEXT, Tipo TEXT, Valore TEXT")
            _db.CreaTabellaSeNonEsiste(TabellaImpostazioniSiti, "Host TEXT, Chiave TEXT, Valore TEXT")
            _db.CreaTabellaSeNonEsiste(TabellaDatiPersonali, "Chiave TEXT, Valore TEXT")
            _db.EseguiNonQuery("CREATE INDEX IF NOT EXISTS IdxCronologiaIndirizzo ON Cronologia (Indirizzo);")
            _db.EseguiNonQuery("CREATE INDEX IF NOT EXISTS IdxCronologiaVisita ON Cronologia (UltimaVisita);")
            _db.EseguiNonQuery("CREATE INDEX IF NOT EXISTS IdxPreferitiIndirizzo ON Preferiti (Indirizzo);")
            _db.EseguiNonQuery("CREATE INDEX IF NOT EXISTS IdxPasswordOrigine ON Password (Origine);")
            CaricaCache()
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.New: " & ex.ToString())
        End Try
    End Sub

    Private Function DatabasePronto() As Boolean
        Try
            Return _db IsNot Nothing AndAlso _db.Valido
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.DatabasePronto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub CaricaCache()
        Try
            _impostazioniSiti.Clear()
            Dim tabella As DataTable = _db.OttieniTuttiIRecord(TabellaImpostazioniSiti)
            For Each riga As DataRow In tabella.Rows
                Dim host As String = Testo(riga, "Host")
                Dim chiave As String = Testo(riga, "Chiave")
                If host <> "" AndAlso chiave <> "" Then _impostazioniSiti(host & "|" & chiave) = Testo(riga, "Valore")
            Next
            _permessi.Clear()
            Dim permessi As DataTable = _db.OttieniTuttiIRecord(TabellaPermessi)
            For Each riga As DataRow In permessi.Rows
                Dim origine As String = Testo(riga, "Origine")
                Dim tipo As String = Testo(riga, "Tipo")
                If origine <> "" AndAlso tipo <> "" Then _permessi(origine & "|" & tipo) = Testo(riga, "Valore")
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.CaricaCache: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function Parametri(ParamArray coppie As Object()) As Dictionary(Of String, Object)
        Dim risultato As New Dictionary(Of String, Object)()
        Try
            For i As Integer = 0 To coppie.Length - 2 Step 2
                risultato(CStr(coppie(i))) = coppie(i + 1)
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGArchivioBrowser.Parametri: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function Testo(riga As DataRow, colonna As String) As String
        Try
            If riga Is Nothing OrElse Not riga.Table.Columns.Contains(colonna) Then Return ""
            Dim valore As Object = riga(colonna)
            If valore Is Nothing OrElse IsDBNull(valore) Then Return ""
            Return CStr(valore)
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGArchivioBrowser.Testo " & colonna & ": " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function Intero(riga As DataRow, colonna As String) As Long
        Try
            Dim valore As String = Testo(riga, colonna)
            Dim numero As Long
            If Long.TryParse(valore, NumberStyles.Integer, CultureInfo.InvariantCulture, numero) Then Return numero
            Return 0
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGArchivioBrowser.Intero " & colonna & ": " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function Data(riga As DataRow, colonna As String) As DateTime
        Try
            Dim valore As String = Testo(riga, colonna)
            Dim risultato As DateTime
            If DateTime.TryParseExact(valore, FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.None, risultato) Then Return risultato
            Return DateTime.MinValue
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGArchivioBrowser.Data " & colonna & ": " & ex.Message)
            Return DateTime.MinValue
        End Try
    End Function

    Private Shared Function Adesso() As String
        Return DateTime.Now.ToString(FormatoData, CultureInfo.InvariantCulture)
    End Function

    Public Shared Function HostDi(indirizzo As String) As String
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return ""
            Dim uri As Uri = Nothing
            If Uri.TryCreate(indirizzo, UriKind.Absolute, uri) Then Return uri.Host.ToLowerInvariant()
            Return ""
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGArchivioBrowser.HostDi: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function OrigineDi(indirizzo As String) As String
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return ""
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(indirizzo, UriKind.Absolute, uri) Then Return ""
            If uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps Then Return ""
            Return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGArchivioBrowser.OrigineDi: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function IndirizzoRegistrabile(indirizzo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return False
            Dim minuscolo As String = indirizzo.Trim().ToLowerInvariant()
            Return minuscolo.StartsWith("http://") OrElse minuscolo.StartsWith("https://") OrElse minuscolo.StartsWith("file:")
        Catch ex As Exception
            _logCondiviso.ScriviLog("WGArchivioBrowser.IndirizzoRegistrabile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function PreferitoDaRiga(riga As DataRow) As PreferitoBrowser
        Dim p As New PreferitoBrowser()
        Try
            p.Chiave = Testo(riga, ColonnaChiave)
            p.Titolo = Testo(riga, "Titolo")
            p.Indirizzo = Testo(riga, "Indirizzo")
            p.Cartella = Testo(riga, "Cartella")
            p.Ordine = CInt(Math.Min(Integer.MaxValue, Math.Max(Integer.MinValue, Intero(riga, "Ordine"))))
            p.DataAggiunta = Data(riga, WGDatabase.NomeColonnaDataCreazione)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.PreferitoDaRiga: " & ex.Message)
        End Try
        Return p
    End Function

    Public Function AggiungiPreferito(titolo As String, indirizzo As String, Optional cartella As String = "") As String
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(indirizzo) Then Return ""
            Dim titoloEffettivo As String = If(String.IsNullOrWhiteSpace(titolo), indirizzo, titolo.Trim())
            Dim ordine As Object = _db.EseguiScalare("SELECT IFNULL(MAX(Ordine), 0) + 1 FROM Preferiti;")
            Dim valoreOrdine As Long = 1
            If ordine IsNot Nothing AndAlso Not IsDBNull(ordine) Then Long.TryParse(Convert.ToString(ordine, CultureInfo.InvariantCulture), valoreOrdine)
            Return _db.InserisciRecord(TabellaPreferiti, Parametri("Titolo", titoloEffettivo, "Indirizzo", indirizzo.Trim(), "Cartella", If(cartella, "").Trim(), "Ordine", valoreOrdine))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.AggiungiPreferito: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function PreferitoPerIndirizzo(indirizzo As String) As PreferitoBrowser
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(indirizzo) Then Return Nothing
            Dim tabella As DataTable = _db.EseguiQuery("SELECT * FROM Preferiti WHERE Indirizzo = @i LIMIT 1;", Parametri("@i", indirizzo.Trim()))
            If tabella.Rows.Count = 0 Then Return Nothing
            Return PreferitoDaRiga(tabella.Rows(0))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.PreferitoPerIndirizzo: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniPreferito(chiave As String) As PreferitoBrowser
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            Dim riga As DataRow = _db.OttieniRecord(TabellaPreferiti, chiave)
            If riga Is Nothing Then Return Nothing
            Return PreferitoDaRiga(riga)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.OttieniPreferito: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function EliminaPreferito(chiave As String) As Boolean
        Try
            If Not DatabasePronto() Then Return False
            Return _db.EliminaRecord(TabellaPreferiti, chiave)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.EliminaPreferito: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function RinominaPreferito(chiave As String, titolo As String) As Boolean
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(titolo) Then Return False
            Return _db.AggiornaRecord(TabellaPreferiti, chiave, Parametri("Titolo", titolo.Trim()))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.RinominaPreferito: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function ModificaIndirizzoPreferito(chiave As String, indirizzo As String) As Boolean
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(indirizzo) Then Return False
            Return _db.AggiornaRecord(TabellaPreferiti, chiave, Parametri("Indirizzo", indirizzo.Trim()))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ModificaIndirizzoPreferito: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function SpostaPreferito(chiave As String, cartella As String) As Boolean
        Try
            If Not DatabasePronto() Then Return False
            Return _db.AggiornaRecord(TabellaPreferiti, chiave, Parametri("Cartella", If(cartella, "").Trim()))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.SpostaPreferito: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function ElencoPreferiti(Optional cartella As String = Nothing) As List(Of PreferitoBrowser)
        Dim risultato As New List(Of PreferitoBrowser)()
        Try
            If Not DatabasePronto() Then Return risultato
            Dim tabella As DataTable
            If cartella Is Nothing Then
                tabella = _db.EseguiQuery("SELECT * FROM Preferiti ORDER BY Ordine, Titolo COLLATE NOCASE;")
            Else
                tabella = _db.EseguiQuery("SELECT * FROM Preferiti WHERE IFNULL(Cartella, '') = @c ORDER BY Ordine, Titolo COLLATE NOCASE;", Parametri("@c", cartella))
            End If
            For Each riga As DataRow In tabella.Rows
                risultato.Add(PreferitoDaRiga(riga))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ElencoPreferiti: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function CartellePreferiti() As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            If Not DatabasePronto() Then Return risultato
            Dim tabella As DataTable = _db.EseguiQuery("SELECT DISTINCT Cartella FROM Preferiti WHERE IFNULL(Cartella, '') <> '' ORDER BY Cartella COLLATE NOCASE;")
            For Each riga As DataRow In tabella.Rows
                Dim nome As String = Testo(riga, "Cartella")
                If nome <> "" Then risultato.Add(nome)
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.CartellePreferiti: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function CercaPreferiti(testoCercato As String, Optional limite As Integer = 50) As List(Of PreferitoBrowser)
        Dim risultato As New List(Of PreferitoBrowser)()
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(testoCercato) Then Return risultato
            Dim tabella As DataTable = _db.EseguiQuery("SELECT * FROM Preferiti WHERE Titolo LIKE @t OR Indirizzo LIKE @t ORDER BY Titolo COLLATE NOCASE LIMIT " & Math.Max(1, limite).ToString(CultureInfo.InvariantCulture) & ";", Parametri("@t", "%" & testoCercato.Trim() & "%"))
            For Each riga As DataRow In tabella.Rows
                risultato.Add(PreferitoDaRiga(riga))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.CercaPreferiti: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function VisitaDaRiga(riga As DataRow) As VisitaBrowser
        Dim v As New VisitaBrowser()
        Try
            v.Chiave = Testo(riga, ColonnaChiave)
            v.Indirizzo = Testo(riga, "Indirizzo")
            v.Titolo = Testo(riga, "Titolo")
            v.Host = Testo(riga, "Host")
            v.UltimaVisita = Data(riga, "UltimaVisita")
            v.Visite = CInt(Math.Min(Integer.MaxValue, Intero(riga, "Visite")))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.VisitaDaRiga: " & ex.Message)
        End Try
        Return v
    End Function

    Public Sub RegistraVisita(indirizzo As String, titolo As String)
        Try
            If Not DatabasePronto() OrElse Not IndirizzoRegistrabile(indirizzo) Then Return
            SyncLock _lock
                Dim esistente As DataTable = _db.EseguiQuery("SELECT ChiaveWG, Visite FROM Cronologia WHERE Indirizzo = @i LIMIT 1;", Parametri("@i", indirizzo))
                If esistente.Rows.Count > 0 Then
                    Dim chiave As String = Testo(esistente.Rows(0), ColonnaChiave)
                    Dim valori As Dictionary(Of String, Object) = Parametri("UltimaVisita", Adesso(), "Visite", Intero(esistente.Rows(0), "Visite") + 1)
                    If Not String.IsNullOrWhiteSpace(titolo) Then valori("Titolo") = titolo.Trim()
                    _db.AggiornaRecord(TabellaCronologia, chiave, valori)
                Else
                    _db.InserisciRecord(TabellaCronologia, Parametri("Indirizzo", indirizzo, "Titolo", If(titolo, "").Trim(), "Host", HostDi(indirizzo), "UltimaVisita", Adesso(), "Visite", 1))
                    _inserimentiCronologia += 1
                    If _inserimentiCronologia Mod 250 = 0 Then PotaCronologia()
                End If
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.RegistraVisita: " & ex.ToString())
        End Try
    End Sub

    Public Sub AggiornaTitoloVisita(indirizzo As String, titolo As String)
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(titolo) OrElse Not IndirizzoRegistrabile(indirizzo) Then Return
            _db.EseguiNonQuery("UPDATE Cronologia SET Titolo = @t WHERE Indirizzo = @i;", Parametri("@t", titolo.Trim(), "@i", indirizzo))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.AggiornaTitoloVisita: " & ex.ToString())
        End Try
    End Sub

    Private Sub PotaCronologia()
        Try
            Dim totale As Object = _db.EseguiScalare("SELECT COUNT(*) FROM Cronologia;")
            Dim numero As Long = 0
            If totale IsNot Nothing AndAlso Not IsDBNull(totale) Then Long.TryParse(Convert.ToString(totale, CultureInfo.InvariantCulture), numero)
            If numero <= MassimoCronologia Then Return
            _db.EseguiNonQuery("DELETE FROM Cronologia WHERE ChiaveWG IN (SELECT ChiaveWG FROM Cronologia ORDER BY UltimaVisita ASC LIMIT " & (numero - MassimoCronologia).ToString(CultureInfo.InvariantCulture) & ");")
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.PotaCronologia: " & ex.ToString())
        End Try
    End Sub

    Public Function ElencoCronologia(Optional filtro As String = "", Optional limite As Integer = 500) As List(Of VisitaBrowser)
        Dim risultato As New List(Of VisitaBrowser)()
        Try
            If Not DatabasePronto() Then Return risultato
            Dim limiteSql As String = Math.Max(1, limite).ToString(CultureInfo.InvariantCulture)
            Dim tabella As DataTable
            If String.IsNullOrWhiteSpace(filtro) Then
                tabella = _db.EseguiQuery("SELECT * FROM Cronologia ORDER BY UltimaVisita DESC LIMIT " & limiteSql & ";")
            Else
                tabella = _db.EseguiQuery("SELECT * FROM Cronologia WHERE Titolo LIKE @f OR Indirizzo LIKE @f ORDER BY UltimaVisita DESC LIMIT " & limiteSql & ";", Parametri("@f", "%" & filtro.Trim() & "%"))
            End If
            For Each riga As DataRow In tabella.Rows
                risultato.Add(VisitaDaRiga(riga))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ElencoCronologia: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniVisita(chiave As String) As VisitaBrowser
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            Dim riga As DataRow = _db.OttieniRecord(TabellaCronologia, chiave)
            If riga Is Nothing Then Return Nothing
            Return VisitaDaRiga(riga)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.OttieniVisita: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function EliminaVisita(chiave As String) As Boolean
        Try
            If Not DatabasePronto() Then Return False
            Return _db.EliminaRecord(TabellaCronologia, chiave)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.EliminaVisita: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function SvuotaCronologia(Optional daData As DateTime? = Nothing) As Integer
        Try
            If Not DatabasePronto() Then Return 0
            If daData.HasValue Then
                Return _db.EseguiNonQuery("DELETE FROM Cronologia WHERE UltimaVisita >= @d;", Parametri("@d", daData.Value.ToString(FormatoData, CultureInfo.InvariantCulture)))
            End If
            Return _db.EseguiNonQuery("DELETE FROM Cronologia;")
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.SvuotaCronologia: " & ex.ToString())
            Return 0
        End Try
    End Function

    Public Function Suggerimenti(testoCercato As String, Optional limite As Integer = 15) As List(Of Tuple(Of String, String))
        Dim risultato As New List(Of Tuple(Of String, String))()
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(testoCercato) Then Return risultato
            Dim visti As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each p As PreferitoBrowser In CercaPreferiti(testoCercato, limite)
                If visti.Add(p.Indirizzo) Then risultato.Add(Tuple.Create(p.Titolo, p.Indirizzo))
            Next
            Dim tabella As DataTable = _db.EseguiQuery("SELECT * FROM Cronologia WHERE Titolo LIKE @f OR Indirizzo LIKE @f ORDER BY Visite DESC, UltimaVisita DESC LIMIT " & Math.Max(1, limite).ToString(CultureInfo.InvariantCulture) & ";", Parametri("@f", "%" & testoCercato.Trim() & "%"))
            For Each riga As DataRow In tabella.Rows
                If risultato.Count >= limite Then Exit For
                Dim v As VisitaBrowser = VisitaDaRiga(riga)
                If visti.Add(v.Indirizzo) Then risultato.Add(Tuple.Create(If(String.IsNullOrWhiteSpace(v.Titolo), v.Indirizzo, v.Titolo), v.Indirizzo))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.Suggerimenti: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Private Function DownloadDaRiga(riga As DataRow) As DownloadRegistrato
        Dim d As New DownloadRegistrato()
        Try
            d.Chiave = Testo(riga, ColonnaChiave)
            d.Nome = Testo(riga, "Nome")
            d.Percorso = Testo(riga, "Percorso")
            d.Indirizzo = Testo(riga, "Indirizzo")
            d.Stato = Testo(riga, "Stato")
            d.Dimensione = Intero(riga, "Dimensione")
            d.Data = Data(riga, "Data")
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.DownloadDaRiga: " & ex.Message)
        End Try
        Return d
    End Function

    Public Function RegistraDownload(nome As String, percorso As String, indirizzo As String, stato As String, dimensione As Long) As String
        Try
            If Not DatabasePronto() Then Return ""
            Dim chiave As String = _db.InserisciRecord(TabellaDownload, Parametri("Nome", If(nome, ""), "Percorso", If(percorso, ""), "Indirizzo", If(indirizzo, ""), "Stato", If(stato, ""), "Dimensione", dimensione, "Data", Adesso()))
            Dim totale As Object = _db.EseguiScalare("SELECT COUNT(*) FROM Download;")
            Dim numero As Long = 0
            If totale IsNot Nothing AndAlso Not IsDBNull(totale) Then Long.TryParse(Convert.ToString(totale, CultureInfo.InvariantCulture), numero)
            If numero > MassimoDownload Then
                _db.EseguiNonQuery("DELETE FROM Download WHERE ChiaveWG IN (SELECT ChiaveWG FROM Download ORDER BY Data ASC LIMIT " & (numero - MassimoDownload).ToString(CultureInfo.InvariantCulture) & ");")
            End If
            Return chiave
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.RegistraDownload: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Sub AggiornaDownload(chiave As String, stato As String, percorso As String, dimensione As Long)
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return
            Dim valori As Dictionary(Of String, Object) = Parametri("Stato", If(stato, ""), "Dimensione", dimensione)
            If Not String.IsNullOrWhiteSpace(percorso) Then
                valori("Percorso") = percorso
                valori("Nome") = IO.Path.GetFileName(percorso)
            End If
            _db.AggiornaRecord(TabellaDownload, chiave, valori)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.AggiornaDownload: " & ex.ToString())
        End Try
    End Sub

    Public Function ElencoDownload(Optional limite As Integer = 200) As List(Of DownloadRegistrato)
        Dim risultato As New List(Of DownloadRegistrato)()
        Try
            If Not DatabasePronto() Then Return risultato
            Dim tabella As DataTable = _db.EseguiQuery("SELECT * FROM Download ORDER BY Data DESC LIMIT " & Math.Max(1, limite).ToString(CultureInfo.InvariantCulture) & ";")
            For Each riga As DataRow In tabella.Rows
                risultato.Add(DownloadDaRiga(riga))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ElencoDownload: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function OttieniDownload(chiave As String) As DownloadRegistrato
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            Dim riga As DataRow = _db.OttieniRecord(TabellaDownload, chiave)
            If riga Is Nothing Then Return Nothing
            Return DownloadDaRiga(riga)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.OttieniDownload: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function EliminaDownload(chiave As String) As Boolean
        Try
            If Not DatabasePronto() Then Return False
            Return _db.EliminaRecord(TabellaDownload, chiave)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.EliminaDownload: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Sub SvuotaDownload()
        Try
            If Not DatabasePronto() Then Return
            _db.EseguiNonQuery("DELETE FROM Download;")
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.SvuotaDownload: " & ex.ToString())
        End Try
    End Sub

    Private Function CredenzialeDaRiga(riga As DataRow, conPassword As Boolean) As CredenzialeBrowser
        Dim c As New CredenzialeBrowser()
        Try
            c.Chiave = Testo(riga, ColonnaChiave)
            c.Origine = Testo(riga, "Origine")
            c.Utente = WGProtezioneDati.Rivela(Testo(riga, "Utente"))
            If conPassword Then c.Password = WGProtezioneDati.Rivela(Testo(riga, "Segreto"))
            c.UltimoUso = Data(riga, "UltimoUso")
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.CredenzialeDaRiga: " & ex.Message)
        End Try
        Return c
    End Function

    Public Function SalvaCredenziali(origine As String, utente As String, password As String) As Boolean
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(origine) OrElse String.IsNullOrEmpty(password) Then Return False
            Dim utenteNormale As String = If(utente, "").Trim()
            For Each esistente As CredenzialeBrowser In CredenzialiPer(origine)
                If String.Equals(esistente.Utente, utenteNormale, StringComparison.Ordinal) Then
                    Return _db.AggiornaRecord(TabellaPassword, esistente.Chiave, Parametri("Segreto", WGProtezioneDati.Proteggi(password), "UltimoUso", Adesso()))
                End If
            Next
            Return _db.InserisciRecord(TabellaPassword, Parametri("Origine", origine.ToLowerInvariant(), "Utente", WGProtezioneDati.Proteggi(utenteNormale), "Segreto", WGProtezioneDati.Proteggi(password), "UltimoUso", Adesso())) <> ""
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.SalvaCredenziali: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function CredenzialiPer(origine As String) As List(Of CredenzialeBrowser)
        Dim risultato As New List(Of CredenzialeBrowser)()
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(origine) Then Return risultato
            Dim tabella As DataTable = _db.EseguiQuery("SELECT * FROM Password WHERE Origine = @o ORDER BY UltimoUso DESC;", Parametri("@o", origine.ToLowerInvariant()))
            For Each riga As DataRow In tabella.Rows
                risultato.Add(CredenzialeDaRiga(riga, True))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.CredenzialiPer: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Function ElencoCredenziali() As List(Of CredenzialeBrowser)
        Dim risultato As New List(Of CredenzialeBrowser)()
        Try
            If Not DatabasePronto() Then Return risultato
            Dim tabella As DataTable = _db.EseguiQuery("SELECT * FROM Password ORDER BY Origine;")
            For Each riga As DataRow In tabella.Rows
                risultato.Add(CredenzialeDaRiga(riga, False))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ElencoCredenziali: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Function OttieniCredenziale(chiave As String) As CredenzialeBrowser
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return Nothing
            Dim riga As DataRow = _db.OttieniRecord(TabellaPassword, chiave)
            If riga Is Nothing Then Return Nothing
            Return CredenzialeDaRiga(riga, True)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.OttieniCredenziale: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Function ModificaPassword(chiave As String, password As String) As Boolean
        Try
            If Not DatabasePronto() OrElse String.IsNullOrEmpty(password) Then Return False
            Return _db.AggiornaRecord(TabellaPassword, chiave, Parametri("Segreto", WGProtezioneDati.Proteggi(password)))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ModificaPassword: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub SegnaUsoCredenziale(chiave As String)
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return
            _db.AggiornaRecord(TabellaPassword, chiave, Parametri("UltimoUso", Adesso()))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.SegnaUsoCredenziale: " & ex.Message)
        End Try
    End Sub

    Public Function EliminaCredenziale(chiave As String) As Boolean
        Try
            If Not DatabasePronto() Then Return False
            Return _db.EliminaRecord(TabellaPassword, chiave)
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.EliminaCredenziale: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function PermessoSito(origine As String, tipo As String) As String
        Try
            If String.IsNullOrWhiteSpace(origine) OrElse String.IsNullOrWhiteSpace(tipo) Then Return ""
            Dim valore As String = Nothing
            If _permessi.TryGetValue(origine.ToLowerInvariant() & "|" & tipo, valore) Then Return If(valore, "")
            Return ""
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.PermessoSito: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub ImpostaPermessoSito(origine As String, tipo As String, valore As String)
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(origine) OrElse String.IsNullOrWhiteSpace(tipo) Then Return
            Dim origineNormale As String = origine.ToLowerInvariant()
            SyncLock _lock
                _db.EseguiNonQuery("DELETE FROM PermessiSiti WHERE Origine = @o AND Tipo = @t;", Parametri("@o", origineNormale, "@t", tipo))
                If String.IsNullOrWhiteSpace(valore) Then
                    Dim rimosso As String = Nothing
                    _permessi.TryRemove(origineNormale & "|" & tipo, rimosso)
                Else
                    _db.InserisciRecord(TabellaPermessi, Parametri("Origine", origineNormale, "Tipo", tipo, "Valore", valore))
                    _permessi(origineNormale & "|" & tipo) = valore
                End If
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ImpostaPermessoSito: " & ex.ToString())
        End Try
    End Sub

    Public Function ElencoPermessi() As List(Of PermessoSitoBrowser)
        Dim risultato As New List(Of PermessoSitoBrowser)()
        Try
            If Not DatabasePronto() Then Return risultato
            Dim tabella As DataTable = _db.EseguiQuery("SELECT * FROM PermessiSiti ORDER BY Origine, Tipo;")
            For Each riga As DataRow In tabella.Rows
                risultato.Add(New PermessoSitoBrowser() With {.Chiave = Testo(riga, ColonnaChiave), .Origine = Testo(riga, "Origine"), .Tipo = Testo(riga, "Tipo"), .Valore = Testo(riga, "Valore")})
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ElencoPermessi: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function ValoreSito(host As String, chiave As String) As String
        Try
            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(chiave) Then Return ""
            Dim valore As String = Nothing
            If _impostazioniSiti.TryGetValue(host.ToLowerInvariant() & "|" & chiave, valore) Then Return If(valore, "")
            Return ""
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ValoreSito: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub ImpostaValoreSito(host As String, chiave As String, valore As String)
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(chiave) Then Return
            Dim hostNormale As String = host.ToLowerInvariant()
            SyncLock _lock
                _db.EseguiNonQuery("DELETE FROM ImpostazioniSiti WHERE Host = @h AND Chiave = @c;", Parametri("@h", hostNormale, "@c", chiave))
                If String.IsNullOrWhiteSpace(valore) Then
                    Dim rimosso As String = Nothing
                    _impostazioniSiti.TryRemove(hostNormale & "|" & chiave, rimosso)
                Else
                    _db.InserisciRecord(TabellaImpostazioniSiti, Parametri("Host", hostNormale, "Chiave", chiave, "Valore", valore))
                    _impostazioniSiti(hostNormale & "|" & chiave) = valore
                End If
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ImpostaValoreSito: " & ex.ToString())
        End Try
    End Sub

    Public Function ImpostazioniDeiSiti() As List(Of Tuple(Of String, String, String))
        Dim risultato As New List(Of Tuple(Of String, String, String))()
        Try
            For Each coppia As KeyValuePair(Of String, String) In _impostazioniSiti
                Dim parti As String() = coppia.Key.Split("|"c)
                If parti.Length = 2 Then risultato.Add(Tuple.Create(parti(0), parti(1), coppia.Value))
            Next
            risultato.Sort(Function(a, b) String.Compare(a.Item1 & "|" & a.Item2, b.Item1 & "|" & b.Item2, StringComparison.OrdinalIgnoreCase))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ImpostazioniDeiSiti: " & ex.ToString())
        End Try
        Return risultato
    End Function

    Public Function DatoPersonale(chiave As String) As String
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return ""
            Dim tabella As DataTable = _db.EseguiQuery("SELECT Valore FROM DatiPersonali WHERE Chiave = @c LIMIT 1;", Parametri("@c", chiave))
            If tabella.Rows.Count = 0 Then Return ""
            Return WGProtezioneDati.Rivela(Testo(tabella.Rows(0), "Valore"))
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.DatoPersonale: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub ImpostaDatoPersonale(chiave As String, valore As String)
        Try
            If Not DatabasePronto() OrElse String.IsNullOrWhiteSpace(chiave) Then Return
            SyncLock _lock
                _db.EseguiNonQuery("DELETE FROM DatiPersonali WHERE Chiave = @c;", Parametri("@c", chiave))
                If Not String.IsNullOrWhiteSpace(valore) Then
                    _db.InserisciRecord(TabellaDatiPersonali, Parametri("Chiave", chiave, "Valore", WGProtezioneDati.Proteggi(valore.Trim())))
                End If
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.ImpostaDatoPersonale: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Riscrive password, utenti e dati personali già salvati nel formato leggibile su ogni computer collegato a Filen.
    ''' Non fa nulla se non è stato fatto l'accesso a Filen o se è già tutto portabile.
    ''' </summary>
    Public Sub RendiPortabiliIDati()
        Try
            If Not DatabasePronto() OrElse Not WGProtezioneDati.PortabilePossibile Then Return
            SyncLock _lock
                Dim password As DataTable = _db.EseguiQuery("SELECT * FROM Password;")
                For Each riga As DataRow In password.Rows
                    Dim utente As String = Testo(riga, "Utente")
                    Dim segreto As String = Testo(riga, "Segreto")
                    If WGProtezioneDati.EPortabile(utente) AndAlso WGProtezioneDati.EPortabile(segreto) Then Continue For
                    Dim nuovoUtente As String = WGProtezioneDati.RendiPortabile(utente)
                    Dim nuovoSegreto As String = WGProtezioneDati.RendiPortabile(segreto)
                    If String.Equals(nuovoUtente, utente, StringComparison.Ordinal) AndAlso String.Equals(nuovoSegreto, segreto, StringComparison.Ordinal) Then Continue For
                    _db.AggiornaRecord(TabellaPassword, Testo(riga, ColonnaChiave), Parametri("Utente", nuovoUtente, "Segreto", nuovoSegreto))
                Next
                Dim personali As DataTable = _db.EseguiQuery("SELECT * FROM DatiPersonali;")
                For Each riga As DataRow In personali.Rows
                    Dim valore As String = Testo(riga, "Valore")
                    If WGProtezioneDati.EPortabile(valore) Then Continue For
                    Dim nuovo As String = WGProtezioneDati.RendiPortabile(valore)
                    If String.Equals(nuovo, valore, StringComparison.Ordinal) Then Continue For
                    _db.AggiornaRecord(TabellaDatiPersonali, Testo(riga, ColonnaChiave), Parametri("Valore", nuovo))
                Next
            End SyncLock
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.RendiPortabiliIDati: " & ex.ToString())
        End Try
    End Sub

    Public Function DatiPersonali() As Dictionary(Of String, String)
        Dim risultato As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            If Not DatabasePronto() Then Return risultato
            Dim tabella As DataTable = _db.EseguiQuery("SELECT Chiave, Valore FROM DatiPersonali;")
            For Each riga As DataRow In tabella.Rows
                Dim chiave As String = Testo(riga, "Chiave")
                If chiave <> "" Then risultato(chiave) = WGProtezioneDati.Rivela(Testo(riga, "Valore"))
            Next
        Catch ex As Exception
            _log.ScriviLog("WGArchivioBrowser.DatiPersonali: " & ex.Message)
        End Try
        Return risultato
    End Function
End Class
