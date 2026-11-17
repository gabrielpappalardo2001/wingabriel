Imports System.Data
Imports System.Data.SQLite
Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports wgcontrols

Public Class WGPredizione

    Private Shared log As New Logger()
    Private Shared _istanza As WGPredizione

    Private Const NomeFileDizionario As String = "it-100k.txt"
    Private Const NomeFileDatabase As String = "predizione_utente.db"
    Private Const NumeroMassimoSuggerimenti As Integer = 3
    Private Const FrequenzaMinimaCorrezione As Long = 100

    Private ReadOnly _frequenze As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _paroleOrdinate As New List(Of String)
    Private ReadOnly _bigrammi As New Dictionary(Of String, Dictionary(Of String, Integer))(StringComparer.OrdinalIgnoreCase)

    Private ReadOnly _vicinanzaTasti As New Dictionary(Of Char, String)

    Private _connessione As SQLiteConnection
    Private _caricato As Boolean = False
    Private _bigrammiModificati As Boolean = False

    Private Sub New()
        Try
            PreparaVicinanzaTasti()
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.New: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Shared Function GetInstance() As WGPredizione
        Try
            If _istanza Is Nothing Then
                _istanza = New WGPredizione()
                _istanza.Carica()
            End If
            Return _istanza
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.GetInstance: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return _istanza
        End Try
    End Function

    Private Sub PreparaVicinanzaTasti()
        Try
            _vicinanzaTasti.Clear()
            _vicinanzaTasti("q"c) = "wa"
            _vicinanzaTasti("w"c) = "qeas"
            _vicinanzaTasti("e"c) = "wrsd"
            _vicinanzaTasti("r"c) = "etdf"
            _vicinanzaTasti("t"c) = "ryfg"
            _vicinanzaTasti("y"c) = "tugh"
            _vicinanzaTasti("u"c) = "yihj"
            _vicinanzaTasti("i"c) = "uojk"
            _vicinanzaTasti("o"c) = "ipkl"
            _vicinanzaTasti("p"c) = "ol"
            _vicinanzaTasti("a"c) = "qwsz"
            _vicinanzaTasti("s"c) = "awedxz"
            _vicinanzaTasti("d"c) = "serfcx"
            _vicinanzaTasti("f"c) = "drtgvc"
            _vicinanzaTasti("g"c) = "ftyhbv"
            _vicinanzaTasti("h"c) = "gyujnb"
            _vicinanzaTasti("j"c) = "huikmn"
            _vicinanzaTasti("k"c) = "jiolm"
            _vicinanzaTasti("l"c) = "kop"
            _vicinanzaTasti("z"c) = "asx"
            _vicinanzaTasti("x"c) = "zsdc"
            _vicinanzaTasti("c"c) = "xdfv"
            _vicinanzaTasti("v"c) = "cfgb"
            _vicinanzaTasti("b"c) = "vghn"
            _vicinanzaTasti("n"c) = "bhjm"
            _vicinanzaTasti("m"c) = "njk"
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.PreparaVicinanzaTasti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Const ChiaveConfigSuggerimenti As String = "suggerimenti_attivi"

    Public Shared Function SuggerimentiAttivi() As Boolean
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = cfg.LeggiValore(ChiaveConfigSuggerimenti)
            If String.IsNullOrWhiteSpace(valore) Then Return True
            Return String.Equals(valore.Trim(), "1", StringComparison.Ordinal)
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.SuggerimentiAttivi: {ex.Message}")
            Return True
        End Try
    End Function

    Public Shared Sub ImpostaSuggerimentiAttivi(attivi As Boolean)
        Try
            Dim cfg As New WGConfig()
            Dim valore As String = If(attivi, "1", "0")
            If Not cfg.EsisteId(ChiaveConfigSuggerimenti) Then
                cfg.AggiungiValore(ChiaveConfigSuggerimenti, valore)
            Else
                cfg.ModificaValore(ChiaveConfigSuggerimenti, valore)
            End If
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.ImpostaSuggerimentiAttivi: {ex.Message}")
        End Try
    End Sub

    Public Shared Function CartellaDizionario() As String
        Try
            Dim cartella As String = Path.Combine(Application.StartupPath, "dictionary")
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Return cartella
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.CartellaDizionario: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Application.StartupPath
        End Try
    End Function

    Public ReadOnly Property DizionarioDisponibile As Boolean
        Get
            Try
                Return _frequenze.Count > 0
            Catch ex As Exception
                log.ScriviLog($"WGPredizione.DizionarioDisponibile: {ex.Message}")
                Return False
            End Try
        End Get
    End Property

    Public Sub Carica()
        Try
            If _caricato Then Return
            _caricato = True
            CaricaDizionario()
            CaricaBigrammi()
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.Carica: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub CaricaDizionario()
        Try
            Dim percorso As String = Path.Combine(CartellaDizionario(), NomeFileDizionario)
            If Not File.Exists(percorso) Then
                log.ScriviLog($"WGPredizione.CaricaDizionario: dizionario non trovato in {percorso}")
                Return
            End If

            _frequenze.Clear()
            _paroleOrdinate.Clear()

            Using lettore As New StreamReader(percorso, Encoding.UTF8)
                Do
                    Dim riga As String = lettore.ReadLine()
                    If riga Is Nothing Then Exit Do
                    riga = riga.Trim()
                    If riga.Length = 0 Then Continue Do

                    Dim parti() As String = riga.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                    If parti.Length = 0 Then Continue Do

                    Dim parola As String = parti(0).Trim().ToLowerInvariant()
                    If parola.Length = 0 Then Continue Do

                    Dim frequenza As Long = 1
                    If parti.Length > 1 Then
                        Long.TryParse(parti(1).Trim(), frequenza)
                        If frequenza < 1 Then frequenza = 1
                    End If

                    If _frequenze.ContainsKey(parola) Then
                        If frequenza > _frequenze(parola) Then _frequenze(parola) = frequenza
                    Else
                        _frequenze(parola) = frequenza
                        _paroleOrdinate.Add(parola)
                    End If
                Loop
            End Using

            _paroleOrdinate.Sort(StringComparer.Ordinal)
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.CaricaDizionario: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Sub ApriDatabase()
        Try
            Dim percorso As String = Path.Combine(CartellaDizionario(), NomeFileDatabase)
            Dim daCreare As Boolean = Not File.Exists(percorso)

            _connessione = New SQLiteConnection("Data Source=" & percorso & ";Version=3;")
            _connessione.Open()

            If daCreare Then
                Using comando As New SQLiteCommand(_connessione)
                    comando.CommandText = "CREATE TABLE IF NOT EXISTS bigrammi (prima TEXT NOT NULL, seconda TEXT NOT NULL, conteggio INTEGER NOT NULL, PRIMARY KEY (prima, seconda))"
                    comando.ExecuteNonQuery()
                End Using
            End If

            Using comando As New SQLiteCommand(_connessione)
                comando.CommandText = "CREATE TABLE IF NOT EXISTS bigrammi (prima TEXT NOT NULL, seconda TEXT NOT NULL, conteggio INTEGER NOT NULL, PRIMARY KEY (prima, seconda))"
                comando.ExecuteNonQuery()
            End Using

            Using comando As New SQLiteCommand(_connessione)
                comando.CommandText = "CREATE INDEX IF NOT EXISTS idx_bigrammi_prima ON bigrammi (prima)"
                comando.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.ApriDatabase: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            _connessione = Nothing
        End Try
    End Sub

    Private Sub CaricaBigrammi()
        Try
            ApriDatabase()
            If _connessione Is Nothing Then Return

            _bigrammi.Clear()

            Using comando As New SQLiteCommand("SELECT prima, seconda, conteggio FROM bigrammi", _connessione)
                Using lettore As SQLiteDataReader = comando.ExecuteReader()
                    Do While lettore.Read()
                        Dim prima As String = lettore.GetString(0)
                        Dim seconda As String = lettore.GetString(1)
                        Dim conteggio As Integer = lettore.GetInt32(2)
                        If Not _bigrammi.ContainsKey(prima) Then
                            _bigrammi(prima) = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                        End If
                        _bigrammi(prima)(seconda) = conteggio
                    Loop
                End Using
            End Using
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.CaricaBigrammi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub SalvaBigrammi()
        Try
            If Not _bigrammiModificati Then Return
            If _connessione Is Nothing Then Return

            Using transazione As SQLiteTransaction = _connessione.BeginTransaction()
                Using comando As New SQLiteCommand(_connessione)
                    comando.Transaction = transazione
                    comando.CommandText = "INSERT OR REPLACE INTO bigrammi (prima, seconda, conteggio) VALUES (@prima, @seconda, @conteggio)"
                    comando.Parameters.Add("@prima", DbType.String)
                    comando.Parameters.Add("@seconda", DbType.String)
                    comando.Parameters.Add("@conteggio", DbType.Int32)

                    For Each coppiaPrima As KeyValuePair(Of String, Dictionary(Of String, Integer)) In _bigrammi
                        For Each coppiaSeconda As KeyValuePair(Of String, Integer) In coppiaPrima.Value
                            comando.Parameters("@prima").Value = coppiaPrima.Key
                            comando.Parameters("@seconda").Value = coppiaSeconda.Key
                            comando.Parameters("@conteggio").Value = coppiaSeconda.Value
                            comando.ExecuteNonQuery()
                        Next
                    Next
                End Using
                transazione.Commit()
            End Using

            _bigrammiModificati = False
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.SalvaBigrammi: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub ChiudiDatabase()
        Try
            SalvaBigrammi()
            If _connessione IsNot Nothing Then
                _connessione.Close()
                _connessione.Dispose()
                _connessione = Nothing
            End If
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.ChiudiDatabase: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Public Sub ImparaDaTesto(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return

            Dim parole() As String = EstraiParole(testo)
            If parole.Length < 2 Then Return

            For i As Integer = 0 To parole.Length - 2
                Dim prima As String = parole(i)
                Dim seconda As String = parole(i + 1)
                If prima.Length = 0 OrElse seconda.Length = 0 Then Continue For

                If Not _bigrammi.ContainsKey(prima) Then
                    _bigrammi(prima) = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                End If
                If _bigrammi(prima).ContainsKey(seconda) Then
                    _bigrammi(prima)(seconda) += 1
                Else
                    _bigrammi(prima)(seconda) = 1
                End If
            Next

            _bigrammiModificati = True
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.ImparaDaTesto: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    Private Function EstraiParole(testo As String) As String()
        Try
            Dim costruttore As New StringBuilder()
            For Each c As Char In testo
                If Char.IsLetter(c) OrElse c = "'"c Then
                    costruttore.Append(Char.ToLowerInvariant(c))
                Else
                    costruttore.Append(" "c)
                End If
            Next
            Return costruttore.ToString().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.EstraiParole: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New String() {}
        End Try
    End Function

    Public Function OttieniSuggerimenti(testoPrimaDelCursore As String) As List(Of String)
        Try
            Dim risultato As New List(Of String)
            If Not SuggerimentiAttivi() Then Return risultato
            If _frequenze.Count = 0 Then Return risultato

            Dim parolaParziale As String = EstraiParolaInCorso(testoPrimaDelCursore)

            If parolaParziale.Length = 0 Then
                Dim precedente As String = EstraiUltimaParolaCompleta(testoPrimaDelCursore)
                Return SuggerisciParolaSuccessiva(precedente)
            End If

            Return SuggerisciCompletamenti(parolaParziale)
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.OttieniSuggerimenti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of String)
        End Try
    End Function

    Public Function EstraiParolaInCorso(testoPrimaDelCursore As String) As String
        Try
            If String.IsNullOrEmpty(testoPrimaDelCursore) Then Return ""
            Dim costruttore As New StringBuilder()
            For i As Integer = testoPrimaDelCursore.Length - 1 To 0 Step -1
                Dim c As Char = testoPrimaDelCursore(i)
                If Char.IsLetter(c) OrElse c = "'"c Then
                    costruttore.Insert(0, Char.ToLowerInvariant(c))
                Else
                    Exit For
                End If
            Next
            Return costruttore.ToString()
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.EstraiParolaInCorso: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Function EstraiUltimaParolaCompleta(testoPrimaDelCursore As String) As String
        Try
            Dim parole() As String = EstraiParole(If(testoPrimaDelCursore, ""))
            If parole.Length = 0 Then Return ""
            Return parole(parole.Length - 1)
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.EstraiUltimaParolaCompleta: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return ""
        End Try
    End Function

    Private Function SuggerisciParolaSuccessiva(parolaPrecedente As String) As List(Of String)
        Try
            Dim risultato As New List(Of String)
            If parolaPrecedente.Length = 0 Then Return risultato
            If Not _bigrammi.ContainsKey(parolaPrecedente) Then Return risultato

            Dim candidati As New List(Of KeyValuePair(Of String, Integer))(_bigrammi(parolaPrecedente))
            candidati.Sort(Function(a, b) b.Value.CompareTo(a.Value))

            For Each candidato As KeyValuePair(Of String, Integer) In candidati
                risultato.Add(candidato.Key)
                If risultato.Count >= NumeroMassimoSuggerimenti Then Exit For
            Next

            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.SuggerisciParolaSuccessiva: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of String)
        End Try
    End Function

    Private Function SuggerisciCompletamenti(parolaParziale As String) As List(Of String)
        Try
            Dim candidati As New List(Of KeyValuePair(Of String, Long))

            Dim indice As Integer = _paroleOrdinate.BinarySearch(parolaParziale, StringComparer.Ordinal)
            If indice < 0 Then indice = Not indice

            Dim i As Integer = indice
            Do While i < _paroleOrdinate.Count
                Dim parola As String = _paroleOrdinate(i)
                If Not parola.StartsWith(parolaParziale, StringComparison.Ordinal) Then Exit Do
                If Not String.Equals(parola, parolaParziale, StringComparison.Ordinal) Then
                    candidati.Add(New KeyValuePair(Of String, Long)(parola, _frequenze(parola)))
                End If
                i += 1
            Loop

            candidati.Sort(Function(a, b) b.Value.CompareTo(a.Value))

            Dim risultato As New List(Of String)
            For Each candidato As KeyValuePair(Of String, Long) In candidati
                risultato.Add(candidato.Key)
                If risultato.Count >= NumeroMassimoSuggerimenti Then Exit For
            Next

            If risultato.Count < NumeroMassimoSuggerimenti Then
                For Each correzione As String In SuggerisciCorrezioni(parolaParziale)
                    If Not risultato.Contains(correzione) Then
                        risultato.Add(correzione)
                        If risultato.Count >= NumeroMassimoSuggerimenti Then Exit For
                    End If
                Next
            End If

            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.SuggerisciCompletamenti: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of String)
        End Try
    End Function

    Public Function SuggerisciCorrezioni(parolaSbagliata As String) As List(Of String)
        Try
            Dim risultato As New List(Of String)
            If parolaSbagliata.Length < 3 Then Return risultato
            If _frequenze.ContainsKey(parolaSbagliata) Then Return risultato

            Dim candidati As New List(Of KeyValuePair(Of String, Double))

            For Each coppia As KeyValuePair(Of String, Long) In _frequenze
                Dim parola As String = coppia.Key
                If coppia.Value < FrequenzaMinimaCorrezione Then Continue For
                If Math.Abs(parola.Length - parolaSbagliata.Length) > 1 Then Continue For

                Dim costo As Double = DistanzaPesata(parolaSbagliata, parola)
                If costo > 1.2 Then Continue For

                Dim punteggio As Double = costo - (Math.Log10(coppia.Value + 1) / 20.0)
                candidati.Add(New KeyValuePair(Of String, Double)(parola, punteggio))
            Next

            candidati.Sort(Function(a, b) a.Value.CompareTo(b.Value))

            For Each candidato As KeyValuePair(Of String, Double) In candidati
                risultato.Add(candidato.Key)
                If risultato.Count >= NumeroMassimoSuggerimenti Then Exit For
            Next

            Return risultato
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.SuggerisciCorrezioni: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return New List(Of String)
        End Try
    End Function

    Private Function DistanzaPesata(scritta As String, candidata As String) As Double
        Try
            Dim lunghezzaA As Integer = scritta.Length
            Dim lunghezzaB As Integer = candidata.Length

            Dim matrice(lunghezzaA, lunghezzaB) As Double

            For i As Integer = 0 To lunghezzaA
                matrice(i, 0) = i
            Next
            For j As Integer = 0 To lunghezzaB
                matrice(0, j) = j
            Next

            For i As Integer = 1 To lunghezzaA
                For j As Integer = 1 To lunghezzaB
                    Dim caratteroA As Char = scritta(i - 1)
                    Dim caratteroB As Char = candidata(j - 1)

                    Dim costoSostituzione As Double
                    If caratteroA = caratteroB Then
                        costoSostituzione = 0
                    ElseIf TastiVicini(caratteroA, caratteroB) Then
                        costoSostituzione = 0.4
                    Else
                        costoSostituzione = 1.0
                    End If

                    Dim valoreMinimo As Double = matrice(i - 1, j) + 1
                    If matrice(i, j - 1) + 1 < valoreMinimo Then valoreMinimo = matrice(i, j - 1) + 1
                    If matrice(i - 1, j - 1) + costoSostituzione < valoreMinimo Then valoreMinimo = matrice(i - 1, j - 1) + costoSostituzione

                    If i > 1 AndAlso j > 1 Then
                        If scritta(i - 1) = candidata(j - 2) AndAlso scritta(i - 2) = candidata(j - 1) Then
                            If matrice(i - 2, j - 2) + 0.5 < valoreMinimo Then valoreMinimo = matrice(i - 2, j - 2) + 0.5
                        End If
                    End If

                    matrice(i, j) = valoreMinimo
                Next
            Next

            Return matrice(lunghezzaA, lunghezzaB)
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.DistanzaPesata: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return Double.MaxValue
        End Try
    End Function

    Private Function TastiVicini(primo As Char, secondo As Char) As Boolean
        Try
            Dim a As Char = Char.ToLowerInvariant(primo)
            Dim b As Char = Char.ToLowerInvariant(secondo)
            If Not _vicinanzaTasti.ContainsKey(a) Then Return False
            Return _vicinanzaTasti(a).IndexOf(b) >= 0
        Catch ex As Exception
            log.ScriviLog($"WGPredizione.TastiVicini: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
            Return False
        End Try
    End Function

End Class