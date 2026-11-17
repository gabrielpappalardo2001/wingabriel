Imports System
Imports System.Collections.Generic
Imports System.Data.SQLite
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Http
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Xml
Imports Newtonsoft.Json.Linq
Imports wgcontrols
Imports WGDb

Public Class LibroWinGabriel

    Public Id As Integer
    Public Titolo As String
    Public Autore As String
    Public Anno As String
    Public Lingua As String
    Public Genere As String
    Public Descrizione As String
    Public UrlScheda As String
    Public UrlDownload As String
    Public Wikidata As JToken
    Public AltriCampi As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

    Public Sub New()
        Titolo = ""
        Autore = ""
        Anno = ""
        Lingua = ""
        Genere = ""
        Descrizione = ""
        UrlScheda = ""
        UrlDownload = ""
    End Sub

    Public Function EtichettaElenco() As String
        Dim parti As New List(Of String)
        If Not String.IsNullOrWhiteSpace(Titolo) Then parti.Add(Titolo)
        If Not String.IsNullOrWhiteSpace(Autore) Then parti.Add(Autore)
        If Not String.IsNullOrWhiteSpace(Anno) Then parti.Add(Anno)
        If parti.Count = 0 Then Return "Libro " & Id.ToString()
        Return String.Join(" - ", parti)
    End Function

End Class

Public Class RisultatoRicercaWinGabriel

    Public Query As String
    Public Campo As String
    Public Totale As Integer
    Public Pagina As Integer
    Public Pagine As Integer
    Public PerPagina As Integer
    Public Libri As New List(Of LibroWinGabriel)
    Public Errore As String

    Public Sub New()
        Query = ""
        Campo = "tutto"
        Errore = ""
        Pagina = 1
        Pagine = 1
        PerPagina = 0
    End Sub

End Class

Public Class InfoBibliotecaWinGabriel

    Public Nome As String
    Public NumeroLibri As Integer
    Public Errore As String

    Public Sub New()
        Nome = "Biblioteca di WinGabriel"
        Errore = ""
    End Sub

End Class

Public Class CLSBibliotecaWinGabriel

    Public Const UrlBaseBiblioteca As String = "https://wingabriel.net/libri//"

    Private Const NomeSottocartellaDati As String = "BibliotecaWinGabriel"
    Private Const NomeFileDatabase As String = "libri_scaricati.sqlite"

    Private ReadOnly _logger As New Logger()
    Private ReadOnly _percorsoDatabase As String
    Private Shared ReadOnly _client As HttpClient

    Shared Sub New()
        Try
            _client = New HttpClient()
            _client.Timeout = TimeSpan.FromSeconds(60)
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Biblioteca/1.0 (accessibilita; postmaster@wingabriel.net)")
            _client.DefaultRequestHeaders.Add("Accept", "application/json, application/epub+zip, application/pdf, text/plain, text/html, */*")
        Catch
        End Try
    End Sub

    Public Sub New()
        Try
            Dim percorso As String = ""
            Try
                percorso = WGDatabase.PercorsoFileDati(NomeSottocartellaDati, NomeFileDatabase)
            Catch
                percorso = ""
            End Try
            If String.IsNullOrWhiteSpace(percorso) Then
                percorso = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dati", NomeSottocartellaDati, NomeFileDatabase)
            End If
            _percorsoDatabase = percorso
            CreaDatabaseSeNecessario()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.New: " & ex.ToString())
            _percorsoDatabase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NomeFileDatabase)
        End Try
    End Sub

    Public Function UrlApi() As String
        Return UrlBaseBiblioteca & "api.php"
    End Function

    Public Function UrlDownload(idLibro As Integer) As String
        Return UrlBaseBiblioteca & "scarica.php?id=" & idLibro.ToString()
    End Function

    Public Function Cerca(query As String, campo As String, pagina As Integer) As RisultatoRicercaWinGabriel
        Dim risultato As New RisultatoRicercaWinGabriel()
        Try
            If String.IsNullOrWhiteSpace(query) Then
                risultato.Errore = "Scrivi il testo da cercare."
                Return risultato
            End If

            Dim campoValido As String = NormalizzaCampo(campo)
            Dim paginaValida As Integer = Math.Max(1, pagina)
            risultato.Query = query.Trim()
            risultato.Campo = campoValido
            risultato.Pagina = paginaValida

            Dim url As String = UrlApi() & "?azione=cerca&q=" & Uri.EscapeDataString(risultato.Query) & "&campo=" & Uri.EscapeDataString(campoValido) & "&pagina=" & paginaValida.ToString()
            Dim json As JObject = ScaricaJson(url)
            If json Is Nothing Then
                risultato.Errore = "Non riesco a contattare la biblioteca di WinGabriel. Controlla la connessione e riprova."
                Return risultato
            End If

            Dim erroreApi As String = json("errore")?.ToString()
            If Not String.IsNullOrWhiteSpace(erroreApi) Then
                risultato.Errore = erroreApi
                Return risultato
            End If

            risultato.Totale = LeggiIntero(json("totale"))
            risultato.Pagina = Math.Max(1, LeggiIntero(json("pagina"), paginaValida))
            risultato.Pagine = Math.Max(1, LeggiIntero(json("pagine"), 1))
            risultato.PerPagina = LeggiIntero(json("per_pagina"))
            risultato.Campo = If(json("campo")?.ToString(), campoValido)
            risultato.Query = If(json("q")?.ToString(), risultato.Query)

            Dim elenco As JArray = TryCast(json("libri"), JArray)
            If elenco IsNot Nothing Then
                For Each elemento As JToken In elenco
                    Dim libro As LibroWinGabriel = CostruisciLibro(elemento)
                    If libro IsNot Nothing Then risultato.Libri.Add(libro)
                Next
            End If
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.Cerca: " & ex.ToString())
            risultato.Errore = "La ricerca non e' andata a buon fine."
        End Try
        Return risultato
    End Function

    Public Function OttieniLibro(idLibro As Integer) As LibroWinGabriel
        Try
            If idLibro <= 0 Then Return Nothing
            Dim url As String = UrlApi() & "?azione=libro&id=" & idLibro.ToString()
            Dim json As JObject = ScaricaJson(url)
            If json Is Nothing Then Return Nothing
            If Not String.IsNullOrWhiteSpace(json("errore")?.ToString()) Then Return Nothing
            Return CostruisciLibro(json)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniLibro: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniInfo() As InfoBibliotecaWinGabriel
        Dim info As New InfoBibliotecaWinGabriel()
        Try
            Dim json As JObject = ScaricaJson(UrlApi() & "?azione=info")
            If json Is Nothing Then
                info.Errore = "Non riesco a contattare la biblioteca di WinGabriel. Controlla la connessione e riprova."
                Return info
            End If
            If Not String.IsNullOrWhiteSpace(json("errore")?.ToString()) Then
                info.Errore = json("errore").ToString()
                Return info
            End If
            info.Nome = If(json("nome")?.ToString(), info.Nome)
            info.NumeroLibri = LeggiIntero(json("libri"))
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniInfo: " & ex.ToString())
            info.Errore = "Non riesco a leggere le informazioni della biblioteca."
        End Try
        Return info
    End Function

    Public Function ScaricaContenutoLibro(libro As LibroWinGabriel, ByRef formato As String, ByRef percorsoFile As String) As Byte()
        formato = ""
        percorsoFile = ""
        Try
            If libro Is Nothing OrElse libro.Id <= 0 Then Return Nothing
            Dim url As String = libro.UrlDownload
            If String.IsNullOrWhiteSpace(url) Then url = UrlDownload(libro.Id)
            If url.StartsWith("/") Then url = UrlBaseBiblioteca.TrimEnd("/"c) & url

            Dim risposta As HttpResponseMessage = EseguiGet(url)
            If risposta Is Nothing OrElse Not risposta.IsSuccessStatusCode Then Return Nothing

            Dim bytes As Byte() = risposta.Content.ReadAsByteArrayAsync().ConfigureAwait(False).GetAwaiter().GetResult()
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing

            formato = RilevaFormato(risposta, bytes)
            percorsoFile = SalvaFileOriginale(libro, bytes, formato)
            Return bytes
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.ScaricaContenutoLibro: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Public Function OttieniTestoLibro(libro As LibroWinGabriel, ByRef percorsoPdf As String) As String
        percorsoPdf = ""
        Try
            If libro Is Nothing Then Return ""

            Dim locale As LibroWinGabriel = OttieniLibroScaricato(libro.Id)
            If locale IsNot Nothing Then
                Dim testoLocale As String = OttieniTestoLibroScaricato(libro.Id)
                If Not String.IsNullOrWhiteSpace(testoLocale) Then Return testoLocale
                Dim percorsoLocale As String = OttieniPercorsoFileScaricato(libro.Id)
                If EPdf(percorsoLocale) Then
                    percorsoPdf = percorsoLocale
                    Return ""
                End If
            End If

            Dim formato As String = ""
            Dim percorsoFile As String = ""
            Dim bytes As Byte() = ScaricaContenutoLibro(libro, formato, percorsoFile)
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return ""

            If String.Equals(formato, "pdf", StringComparison.OrdinalIgnoreCase) Then
                percorsoPdf = percorsoFile
                Return ""
            End If

            Return ConvertiBytesInTesto(bytes, formato)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniTestoLibro: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function ScaricaESalva(libro As LibroWinGabriel) As Boolean
        Try
            If libro Is Nothing OrElse libro.Id <= 0 Then Return False

            Dim dettagli As LibroWinGabriel = OttieniLibro(libro.Id)
            If dettagli Is Nothing Then dettagli = libro

            Dim formato As String = ""
            Dim percorsoFile As String = ""
            Dim bytes As Byte() = ScaricaContenutoLibro(dettagli, formato, percorsoFile)
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return False

            Dim testo As String = ""
            If Not String.Equals(formato, "pdf", StringComparison.OrdinalIgnoreCase) Then
                testo = ConvertiBytesInTesto(bytes, formato)
            End If

            Return SalvaLibroScaricato(dettagli, percorsoFile, formato, testo)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.ScaricaESalva: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function ComponiScheda(libro As LibroWinGabriel) As String
        Try
            If libro Is Nothing Then Return ""
            Dim sb As New StringBuilder()
            AggiungiRigaScheda(sb, "Titolo", libro.Titolo)
            AggiungiRigaScheda(sb, "Autore", libro.Autore)
            AggiungiRigaScheda(sb, "Anno", libro.Anno)
            AggiungiRigaScheda(sb, "Lingua", libro.Lingua)
            AggiungiRigaScheda(sb, "Genere", libro.Genere)
            AggiungiRigaScheda(sb, "Descrizione", libro.Descrizione)
            If libro.AltriCampi IsNot Nothing Then
                For Each coppia As KeyValuePair(Of String, String) In libro.AltriCampi
                    AggiungiRigaScheda(sb, EtichettaCampo(coppia.Key), coppia.Value)
                Next
            End If
            If libro.Wikidata IsNot Nothing Then
                sb.AppendLine()
                sb.AppendLine("Scheda Wikidata")
                sb.Append(FormattaToken(libro.Wikidata, 0))
            End If
            If Not String.IsNullOrWhiteSpace(libro.UrlScheda) Then
                sb.AppendLine()
                AggiungiRigaScheda(sb, "Scheda sul sito", libro.UrlScheda)
            End If
            If Not String.IsNullOrWhiteSpace(libro.UrlDownload) Then
                AggiungiRigaScheda(sb, "Download", libro.UrlDownload)
            End If
            Return sb.ToString().Trim()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.ComponiScheda: " & ex.ToString())
            Return If(libro Is Nothing, "", libro.EtichettaElenco())
        End Try
    End Function

    Public Function OttieniLibriScaricati() As List(Of LibroWinGabriel)
        Dim risultati As New List(Of LibroWinGabriel)
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                If connessione Is Nothing Then Return risultati
                Using comando As New SQLiteCommand("SELECT Id, Titolo, Autore, Anno, Lingua, Genere, Descrizione, UrlScheda, UrlDownload, Formato FROM LibriScaricati ORDER BY Titolo COLLATE NOCASE, Autore COLLATE NOCASE", connessione)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        While lettore.Read()
                            Dim libro As New LibroWinGabriel()
                            libro.Id = Convert.ToInt32(lettore("Id"))
                            libro.Titolo = SeNull(lettore("Titolo"))
                            libro.Autore = SeNull(lettore("Autore"))
                            libro.Anno = SeNull(lettore("Anno"))
                            libro.Lingua = SeNull(lettore("Lingua"))
                            libro.Genere = SeNull(lettore("Genere"))
                            libro.Descrizione = SeNull(lettore("Descrizione"))
                            libro.UrlScheda = SeNull(lettore("UrlScheda"))
                            libro.UrlDownload = SeNull(lettore("UrlDownload"))
                            risultati.Add(libro)
                        End While
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniLibriScaricati: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Public Function OttieniLibroScaricato(idLibro As Integer) As LibroWinGabriel
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                If connessione Is Nothing Then Return Nothing
                Using comando As New SQLiteCommand("SELECT Id, Titolo, Autore, Anno, Lingua, Genere, Descrizione, UrlScheda, UrlDownload FROM LibriScaricati WHERE Id=@id", connessione)
                    comando.Parameters.AddWithValue("@id", idLibro)
                    Using lettore As SQLiteDataReader = comando.ExecuteReader()
                        If lettore.Read() Then
                            Dim libro As New LibroWinGabriel()
                            libro.Id = Convert.ToInt32(lettore("Id"))
                            libro.Titolo = SeNull(lettore("Titolo"))
                            libro.Autore = SeNull(lettore("Autore"))
                            libro.Anno = SeNull(lettore("Anno"))
                            libro.Lingua = SeNull(lettore("Lingua"))
                            libro.Genere = SeNull(lettore("Genere"))
                            libro.Descrizione = SeNull(lettore("Descrizione"))
                            libro.UrlScheda = SeNull(lettore("UrlScheda"))
                            libro.UrlDownload = SeNull(lettore("UrlDownload"))
                            Return libro
                        End If
                    End Using
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniLibroScaricato: " & ex.ToString())
        End Try
        Return Nothing
    End Function

    Public Function OttieniTestoLibroScaricato(idLibro As Integer) As String
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                If connessione Is Nothing Then Return ""
                Using comando As New SQLiteCommand("SELECT Testo FROM LibriScaricati WHERE Id=@id", connessione)
                    comando.Parameters.AddWithValue("@id", idLibro)
                    Dim valore As Object = comando.ExecuteScalar()
                    If valore Is Nothing OrElse valore Is DBNull.Value Then Return ""
                    Return Convert.ToString(valore)
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniTestoLibroScaricato: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function OttieniPercorsoFileScaricato(idLibro As Integer) As String
        Try
            Using connessione As SQLiteConnection = OttieniConnessione()
                If connessione Is Nothing Then Return ""
                Using comando As New SQLiteCommand("SELECT PercorsoFile FROM LibriScaricati WHERE Id=@id", connessione)
                    comando.Parameters.AddWithValue("@id", idLibro)
                    Dim valore As Object = comando.ExecuteScalar()
                    If valore Is Nothing OrElse valore Is DBNull.Value Then Return ""
                    Return Convert.ToString(valore)
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniPercorsoFileScaricato: " & ex.ToString())
            Return ""
        End Try
    End Function

    Public Function EliminaLibroScaricato(idLibro As Integer) As Boolean
        Try
            Dim percorsoFile As String = OttieniPercorsoFileScaricato(idLibro)
            Using connessione As SQLiteConnection = OttieniConnessione()
                If connessione Is Nothing Then Return False
                Using comando As New SQLiteCommand("DELETE FROM LibriScaricati WHERE Id=@id", connessione)
                    comando.Parameters.AddWithValue("@id", idLibro)
                    comando.ExecuteNonQuery()
                End Using
            End Using
            If Not String.IsNullOrWhiteSpace(percorsoFile) AndAlso File.Exists(percorsoFile) Then
                Try
                    File.Delete(percorsoFile)
                Catch exFile As Exception
                    _logger.ScriviLog("CLSBibliotecaWinGabriel.EliminaLibroScaricato file: " & exFile.ToString())
                End Try
            End If
            Return True
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.EliminaLibroScaricato: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Shared Function NormalizzaCampo(campo As String) As String
        If String.IsNullOrWhiteSpace(campo) Then Return "tutto"
        Dim valore As String = campo.Trim().ToLowerInvariant()
        If valore = "titolo" OrElse valore = "autore" OrElse valore = "tutto" Then Return valore
        Return "tutto"
    End Function

    Private Function SalvaLibroScaricato(libro As LibroWinGabriel, percorsoFile As String, formato As String, testo As String) As Boolean
        Try
            If libro Is Nothing Then Return False
            Using connessione As SQLiteConnection = OttieniConnessione()
                If connessione Is Nothing Then Return False
                Using comando As New SQLiteCommand("INSERT OR REPLACE INTO LibriScaricati (Id, Titolo, Autore, Anno, Lingua, Genere, Descrizione, UrlScheda, UrlDownload, Formato, PercorsoFile, Testo, DataDownload) VALUES (@id, @titolo, @autore, @anno, @lingua, @genere, @descrizione, @scheda, @download, @formato, @percorso, @testo, @data)", connessione)
                    comando.Parameters.AddWithValue("@id", libro.Id)
                    comando.Parameters.AddWithValue("@titolo", NullSeVuoto(libro.Titolo))
                    comando.Parameters.AddWithValue("@autore", NullSeVuoto(libro.Autore))
                    comando.Parameters.AddWithValue("@anno", NullSeVuoto(libro.Anno))
                    comando.Parameters.AddWithValue("@lingua", NullSeVuoto(libro.Lingua))
                    comando.Parameters.AddWithValue("@genere", NullSeVuoto(libro.Genere))
                    comando.Parameters.AddWithValue("@descrizione", NullSeVuoto(libro.Descrizione))
                    comando.Parameters.AddWithValue("@scheda", NullSeVuoto(libro.UrlScheda))
                    comando.Parameters.AddWithValue("@download", NullSeVuoto(libro.UrlDownload))
                    comando.Parameters.AddWithValue("@formato", NullSeVuoto(formato))
                    comando.Parameters.AddWithValue("@percorso", NullSeVuoto(percorsoFile))
                    comando.Parameters.AddWithValue("@testo", If(testo, ""))
                    comando.Parameters.AddWithValue("@data", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    comando.ExecuteNonQuery()
                End Using
            End Using
            Return True
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.SalvaLibroScaricato: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Function CostruisciLibro(token As JToken) As LibroWinGabriel
        Try
            Dim oggetto As JObject = TryCast(token, JObject)
            If oggetto Is Nothing Then Return Nothing

            Dim libro As New LibroWinGabriel()
            libro.Id = LeggiIntero(PrimoValore(oggetto, "id", "Id"))
            If libro.Id <= 0 Then Return Nothing

            libro.Titolo = TestoToken(PrimoValore(oggetto, "titolo", "title", "nome"))
            libro.Autore = TestoToken(PrimoValore(oggetto, "autore", "author", "autori", "authors", "creator"))
            libro.Anno = TestoToken(PrimoValore(oggetto, "anno", "year", "data", "date", "anno_pubblicazione"))
            libro.Lingua = TestoToken(PrimoValore(oggetto, "lingua", "language", "lang"))
            libro.Genere = TestoToken(PrimoValore(oggetto, "genere", "genre", "soggetto", "subject"))
            libro.Descrizione = TestoToken(PrimoValore(oggetto, "descrizione", "description", "sommario", "summary"))
            libro.UrlScheda = TestoToken(PrimoValore(oggetto, "scheda", "url_scheda", "url"))
            libro.UrlDownload = TestoToken(PrimoValore(oggetto, "download", "url_download"))
            libro.Wikidata = oggetto("wikidata")

            Dim chiaviNote As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "id", "titolo", "title", "nome", "autore", "author", "autori", "authors", "creator",
                "anno", "year", "data", "date", "anno_pubblicazione", "lingua", "language", "lang",
                "genere", "genre", "soggetto", "subject", "descrizione", "description", "sommario", "summary",
                "scheda", "url_scheda", "url", "download", "url_download", "wikidata"
            }
            For Each proprieta As JProperty In oggetto.Properties()
                If chiaviNote.Contains(proprieta.Name) Then Continue For
                If TypeOf proprieta.Value Is JObject OrElse TypeOf proprieta.Value Is JArray Then Continue For
                Dim valore As String = TestoToken(proprieta.Value)
                If String.IsNullOrWhiteSpace(valore) Then Continue For
                libro.AltriCampi(proprieta.Name) = valore
            Next
            Return libro
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.CostruisciLibro: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function ScaricaJson(url As String) As JObject
        Try
            Dim testo As String = ScaricaStringa(url)
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing
            Return JObject.Parse(testo)
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.ScaricaJson (" & url & "): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function ScaricaStringa(url As String) As String
        Try
            Dim risposta As HttpResponseMessage = EseguiGet(url)
            If risposta Is Nothing OrElse Not risposta.IsSuccessStatusCode Then Return ""
            Return risposta.Content.ReadAsStringAsync().ConfigureAwait(False).GetAwaiter().GetResult()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.ScaricaStringa (" & url & "): " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EseguiGet(url As String) As HttpResponseMessage
        Const tentativiMassimi As Integer = 3
        Try
            For tentativo As Integer = 1 To tentativiMassimi
                Try
                    Dim risposta As HttpResponseMessage = _client.GetAsync(url).ConfigureAwait(False).GetAwaiter().GetResult()
                    If risposta.IsSuccessStatusCode Then Return risposta
                    Dim codice As Integer = CInt(risposta.StatusCode)
                    If tentativo < tentativiMassimi Then
                        Dim attesa As Integer = If(codice = 429, 3000 * tentativo, 1000 * tentativo)
                        Threading.Thread.Sleep(attesa)
                    End If
                Catch exTentativo As Exception
                    _logger.ScriviLog("CLSBibliotecaWinGabriel.EseguiGet tentativo " & tentativo.ToString() & ": " & exTentativo.ToString())
                    If tentativo < tentativiMassimi Then Threading.Thread.Sleep(1000 * tentativo)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.EseguiGet (" & url & "): " & ex.ToString())
        End Try
        Return Nothing
    End Function

    Private Function RilevaFormato(risposta As HttpResponseMessage, bytes As Byte()) As String
        Try
            Dim tipo As String = ""
            If risposta IsNot Nothing AndAlso risposta.Content IsNot Nothing AndAlso risposta.Content.Headers.ContentType IsNot Nothing Then
                tipo = If(risposta.Content.Headers.ContentType.MediaType, "").ToLowerInvariant()
            End If
            Dim nomeFile As String = ""
            If risposta IsNot Nothing AndAlso risposta.Content IsNot Nothing AndAlso risposta.Content.Headers.ContentDisposition IsNot Nothing Then
                nomeFile = If(risposta.Content.Headers.ContentDisposition.FileName, "")
                nomeFile = nomeFile.Trim(""""c)
            End If
            Dim estensione As String = Path.GetExtension(nomeFile).ToLowerInvariant()

            If tipo.Contains("epub") OrElse estensione = ".epub" Then Return "epub"
            If tipo.Contains("pdf") OrElse estensione = ".pdf" Then Return "pdf"
            If tipo.Contains("html") OrElse estensione = ".html" OrElse estensione = ".htm" Then Return "html"
            If tipo.Contains("text/plain") OrElse estensione = ".txt" Then Return "txt"

            If bytes IsNot Nothing AndAlso bytes.Length >= 4 Then
                If bytes(0) = &H50 AndAlso bytes(1) = &H4B Then Return "epub"
                If bytes(0) = &H25 AndAlso bytes(1) = &H50 AndAlso bytes(2) = &H44 AndAlso bytes(3) = &H46 Then Return "pdf"
            End If

            Dim inizio As String = Encoding.UTF8.GetString(bytes, 0, Math.Min(200, bytes.Length)).TrimStart()
            If inizio.StartsWith("<", StringComparison.Ordinal) Then Return "html"
            Return "txt"
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.RilevaFormato: " & ex.ToString())
            Return "txt"
        End Try
    End Function

    Private Function SalvaFileOriginale(libro As LibroWinGabriel, bytes As Byte(), formato As String) As String
        Try
            Dim cartella As String = Path.GetDirectoryName(_percorsoDatabase)
            If String.IsNullOrWhiteSpace(cartella) Then cartella = AppDomain.CurrentDomain.BaseDirectory
            If Not Directory.Exists(cartella) Then Directory.CreateDirectory(cartella)
            Dim estensione As String = If(String.IsNullOrWhiteSpace(formato), "bin", formato)
            Dim nomeSicuro As String = "libro_" & libro.Id.ToString() & "." & estensione
            Dim percorso As String = Path.Combine(cartella, nomeSicuro)
            File.WriteAllBytes(percorso, bytes)
            Return percorso
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.SalvaFileOriginale: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function ConvertiBytesInTesto(bytes As Byte(), formato As String) As String
        Try
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return ""
            Select Case If(formato, "").ToLowerInvariant()
                Case "epub"
                    Return EstraiTestoEpub(bytes)
                Case "html", "htm"
                    Return ConvertiHtmlInTestoPiano(Encoding.UTF8.GetString(bytes))
                Case Else
                    Dim testo As String = Encoding.UTF8.GetString(bytes)
                    If testo.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 OrElse testo.IndexOf("<body", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Return ConvertiHtmlInTestoPiano(testo)
                    End If
                    Return testo.Trim()
            End Select
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.ConvertiBytesInTesto: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function EstraiTestoEpub(bytes As Byte()) As String
        Try
            Using ingresso As New MemoryStream(bytes)
                Using archivio As New ZipArchive(ingresso, ZipArchiveMode.Read)
                    Dim percorsiHtml As List(Of String) = PercorsiHtmlEpub(archivio)
                    Dim sb As New StringBuilder()
                    For Each percorsoRelativo As String In percorsiHtml
                        Dim voce As ZipArchiveEntry = archivio.GetEntry(percorsoRelativo)
                        If voce Is Nothing Then Continue For
                        Using streamVoce As Stream = voce.Open()
                            Using lettore As New StreamReader(streamVoce, Encoding.UTF8)
                                Dim html As String = lettore.ReadToEnd()
                                Dim testo As String = ConvertiHtmlInTestoPiano(html)
                                If Not String.IsNullOrWhiteSpace(testo) Then
                                    If sb.Length > 0 Then sb.AppendLine().AppendLine()
                                    sb.Append(testo)
                                End If
                            End Using
                        End Using
                    Next
                    Return sb.ToString().Trim()
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.EstraiTestoEpub: " & ex.ToString())
            Return ""
        End Try
    End Function

    Private Function PercorsiHtmlEpub(archivio As ZipArchive) As List(Of String)
        Dim risultati As New List(Of String)
        Try
            Dim container As ZipArchiveEntry = archivio.GetEntry("META-INF/container.xml")
            Dim opfRelativo As String = ""
            If container IsNot Nothing Then
                Using streamContainer As Stream = container.Open()
                    Dim xmlContainer As New XmlDocument()
                    xmlContainer.Load(streamContainer)
                    Dim nsmgr As New XmlNamespaceManager(xmlContainer.NameTable)
                    nsmgr.AddNamespace("c", "urn:oasis:names:tc:opendocument:xmlns:container")
                    Dim nodo As XmlNode = xmlContainer.SelectSingleNode("//c:rootfile[@full-path]", nsmgr)
                    If nodo Is Nothing Then nodo = xmlContainer.SelectSingleNode("//*[local-name()='rootfile']")
                    If nodo IsNot Nothing AndAlso nodo.Attributes IsNot Nothing AndAlso nodo.Attributes("full-path") IsNot Nothing Then
                        opfRelativo = nodo.Attributes("full-path").Value
                    End If
                End Using
            End If

            If Not String.IsNullOrWhiteSpace(opfRelativo) Then
                Dim voceOpf As ZipArchiveEntry = archivio.GetEntry(opfRelativo.Replace("\"c, "/"c))
                If voceOpf IsNot Nothing Then
                    Dim cartellaOpf As String = ""
                    Dim posizioneSlash As Integer = opfRelativo.LastIndexOf("/"c)
                    If posizioneSlash >= 0 Then cartellaOpf = opfRelativo.Substring(0, posizioneSlash + 1)
                    Using streamOpf As Stream = voceOpf.Open()
                        Dim xmlOpf As New XmlDocument()
                        xmlOpf.Load(streamOpf)
                        Dim idToHref As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                        For Each item As XmlNode In xmlOpf.SelectNodes("//*[local-name()='item']")
                            If item.Attributes Is Nothing Then Continue For
                            Dim idItem As String = If(item.Attributes("id")?.Value, "")
                            Dim href As String = If(item.Attributes("href")?.Value, "")
                            If idItem <> "" AndAlso href <> "" Then idToHref(idItem) = href
                        Next
                        For Each itemref As XmlNode In xmlOpf.SelectNodes("//*[local-name()='itemref']")
                            If itemref.Attributes Is Nothing Then Continue For
                            Dim idref As String = If(itemref.Attributes("idref")?.Value, "")
                            Dim href As String = ""
                            If idToHref.TryGetValue(idref, href) AndAlso Not String.IsNullOrWhiteSpace(href) Then
                                risultati.Add(NormalizzaPercorsoZip(cartellaOpf & href))
                            End If
                        Next
                    End Using
                End If
            End If

            If risultati.Count = 0 Then
                For Each voce As ZipArchiveEntry In archivio.Entries
                    Dim nome As String = If(voce.FullName, "").ToLowerInvariant()
                    If nome.EndsWith(".xhtml") OrElse nome.EndsWith(".html") OrElse nome.EndsWith(".htm") Then
                        risultati.Add(voce.FullName)
                    End If
                Next
            End If
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.PercorsiHtmlEpub: " & ex.ToString())
        End Try
        Return risultati
    End Function

    Private Function NormalizzaPercorsoZip(percorso As String) As String
        Try
            Dim valore As String = percorso.Replace("\"c, "/"c)
            While valore.Contains("/./")
                valore = valore.Replace("/./", "/")
            End While
            Dim parti As New List(Of String)
            For Each pezzo As String In valore.Split("/"c)
                If pezzo = ".." Then
                    If parti.Count > 0 Then parti.RemoveAt(parti.Count - 1)
                ElseIf pezzo <> "" AndAlso pezzo <> "." Then
                    parti.Add(pezzo)
                ElseIf pezzo = "" AndAlso parti.Count = 0 Then
                    parti.Add("")
                End If
            Next
            Return String.Join("/", parti).TrimStart("/"c)
        Catch
            Return percorso
        End Try
    End Function

    Private Function ConvertiHtmlInTestoPiano(html As String) As String
        Try
            If String.IsNullOrWhiteSpace(html) Then Return ""
            Dim testo As String = html
            testo = Regex.Replace(testo, "<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<style[^>]*>.*?</style>", "", RegexOptions.IgnoreCase Or RegexOptions.Singleline)
            testo = Regex.Replace(testo, "<br\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</p>", Environment.NewLine & Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</div>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</li>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "<li[^>]*>", "- ", RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "</h[1-6]>", Environment.NewLine & Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "<h[1-6][^>]*>", Environment.NewLine, RegexOptions.IgnoreCase)
            testo = Regex.Replace(testo, "<[^>]+>", "")
            testo = System.Net.WebUtility.HtmlDecode(testo)
            testo = Regex.Replace(testo, "[ \t]+", " ")
            testo = Regex.Replace(testo, "\n[ \t]+", Environment.NewLine)
            testo = Regex.Replace(testo, "\n{3,}", Environment.NewLine & Environment.NewLine)
            Return testo.Trim()
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.ConvertiHtmlInTestoPiano: " & ex.ToString())
            Return html
        End Try
    End Function

    Private Sub CreaDatabaseSeNecessario()
        Try
            Dim cartella As String = Path.GetDirectoryName(_percorsoDatabase)
            If Not String.IsNullOrWhiteSpace(cartella) AndAlso Not Directory.Exists(cartella) Then
                Directory.CreateDirectory(cartella)
            End If
            If Not File.Exists(_percorsoDatabase) Then
                SQLiteConnection.CreateFile(_percorsoDatabase)
            End If
            Using connessione As SQLiteConnection = OttieniConnessione()
                If connessione Is Nothing Then Return
                Using comando As New SQLiteCommand("CREATE TABLE IF NOT EXISTS LibriScaricati (Id INTEGER PRIMARY KEY, Titolo TEXT, Autore TEXT, Anno TEXT, Lingua TEXT, Genere TEXT, Descrizione TEXT, UrlScheda TEXT, UrlDownload TEXT, Formato TEXT, PercorsoFile TEXT, Testo TEXT, DataDownload TEXT)", connessione)
                    comando.ExecuteNonQuery()
                End Using
            End Using
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.CreaDatabaseSeNecessario: " & ex.ToString())
        End Try
    End Sub

    Private Function OttieniConnessione() As SQLiteConnection
        Try
            If String.IsNullOrWhiteSpace(_percorsoDatabase) Then Return Nothing
            Dim connessione As New SQLiteConnection("Data Source=" & _percorsoDatabase & ";Version=3;Journal Mode=WAL;Synchronous=Normal;Pooling=True;Default Timeout=30;")
            connessione.Open()
            Return connessione
        Catch ex As Exception
            _logger.ScriviLog("CLSBibliotecaWinGabriel.OttieniConnessione: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Function PrimoValore(oggetto As JObject, ParamArray nomi As String()) As JToken
        If oggetto Is Nothing Then Return Nothing
        For Each nome As String In nomi
            If oggetto(nome) IsNot Nothing AndAlso oggetto(nome).Type <> JTokenType.Null Then Return oggetto(nome)
        Next
        Return Nothing
    End Function

    Private Shared Function TestoToken(token As JToken) As String
        If token Is Nothing OrElse token.Type = JTokenType.Null Then Return ""
        If TypeOf token Is JArray Then
            Dim parti As New List(Of String)
            For Each elemento As JToken In DirectCast(token, JArray)
                Dim pezzo As String = TestoToken(elemento)
                If Not String.IsNullOrWhiteSpace(pezzo) Then parti.Add(pezzo)
            Next
            Return String.Join(", ", parti)
        End If
        If TypeOf token Is JObject Then
            Dim oggetto As JObject = DirectCast(token, JObject)
            Dim nome As JToken = PrimoValore(oggetto, "nome", "name", "label", "titolo", "title", "value")
            If nome IsNot Nothing Then Return TestoToken(nome)
        End If
        Return token.ToString().Trim()
    End Function

    Private Shared Function LeggiIntero(token As JToken, Optional predefinito As Integer = 0) As Integer
        Try
            If token Is Nothing Then Return predefinito
            Dim numero As Integer
            If Integer.TryParse(token.ToString(), numero) Then Return numero
        Catch
        End Try
        Return predefinito
    End Function

    Private Shared Sub AggiungiRigaScheda(sb As StringBuilder, etichetta As String, valore As String)
        If String.IsNullOrWhiteSpace(valore) Then Return
        sb.Append(etichetta)
        sb.Append(": ")
        sb.AppendLine(valore.Trim())
    End Sub

    Private Shared Function FormattaToken(token As JToken, livello As Integer) As String
        Dim sb As New StringBuilder()
        If token Is Nothing Then Return ""
        If TypeOf token Is JObject Then
            For Each proprieta As JProperty In DirectCast(token, JObject).Properties()
                If proprieta.Name = "completa" Then Continue For
                Dim valore As JToken = proprieta.Value
                If TypeOf valore Is JObject OrElse TypeOf valore Is JArray Then
                    sb.AppendLine(EtichettaCampo(proprieta.Name))
                    sb.Append(FormattaToken(valore, livello + 1))
                Else
                    Dim testo As String = TestoToken(valore)
                    If Not String.IsNullOrWhiteSpace(testo) Then
                        sb.Append(EtichettaCampo(proprieta.Name))
                        sb.Append(": ")
                        sb.AppendLine(testo)
                    End If
                End If
            Next
        ElseIf TypeOf token Is JArray Then
            For Each elemento As JToken In DirectCast(token, JArray)
                Dim testo As String = If(TypeOf elemento Is JValue, TestoToken(elemento), FormattaToken(elemento, livello + 1).Trim())
                If Not String.IsNullOrWhiteSpace(testo) Then
                    sb.Append("- ")
                    sb.AppendLine(testo)
                End If
            Next
        Else
            Dim semplice As String = TestoToken(token)
            If Not String.IsNullOrWhiteSpace(semplice) Then sb.AppendLine(semplice)
        End If
        Return sb.ToString()
    End Function

    Private Shared Function EtichettaCampo(chiave As String) As String
        If String.IsNullOrWhiteSpace(chiave) Then Return ""
        Select Case chiave.Trim().ToLowerInvariant()
            Case "title", "titolo" : Return "Titolo"
            Case "author", "autore", "autori", "authors", "creator" : Return "Autore"
            Case "year", "anno", "date", "data", "anno_pubblicazione", "publicationdate" : Return "Anno"
            Case "language", "lingua", "lang" : Return "Lingua"
            Case "genre", "genere", "soggetto", "subject" : Return "Genere"
            Case "description", "descrizione", "sommario", "summary" : Return "Descrizione"
            Case "publisher", "editore" : Return "Editore"
            Case "place", "luogo" : Return "Luogo"
            Case "isbn" : Return "ISBN"
            Case "pages", "pagine" : Return "Pagine"
            Case Else
                Dim testo As String = chiave.Replace("_"c, " "c)
                If testo.Length > 0 Then testo = Char.ToUpper(testo(0)) & testo.Substring(1)
                Return testo
        End Select
    End Function

    Private Shared Function SeNull(valore As Object) As String
        If valore Is Nothing OrElse valore Is DBNull.Value Then Return ""
        Return Convert.ToString(valore)
    End Function

    Private Shared Function NullSeVuoto(valore As String) As Object
        If String.IsNullOrWhiteSpace(valore) Then Return DBNull.Value
        Return valore
    End Function

    Private Shared Function EPdf(percorso As String) As Boolean
        If String.IsNullOrWhiteSpace(percorso) Then Return False
        Return String.Equals(Path.GetExtension(percorso), ".pdf", StringComparison.OrdinalIgnoreCase)
    End Function

End Class
