Imports System.Net.Http
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

' Lettura, scrittura e aggiornamento dei tag musicali nei file MP3, con ricerca delle informazioni online.
Public Class CLSTagMp3
    Private _log As New wgcontrols.Logger()

    Private Const TENTATIVI_RICERCA As Integer = 3
    Private Const ATTESA_BASE_RETRY_MS As Integer = 500

    Private Shared ReadOnly _httpClient As New HttpClient() With {
        .Timeout = TimeSpan.FromSeconds(15)
    }

    Public Structure DatiTag
        Public Titolo As String
        Public Artista As String
        Public Album As String
        Public Anno As UInteger
        Public Genere As String
        Public Successo As Boolean
    End Structure

    Public Function LeggiTag(percorsoFile As String) As DatiTag
        Dim dati As New DatiTag()
        dati.Successo = False
        Try
            If WGRete.EsisteFile(percorsoFile, 8000) Then
                Using fileTag As TagLib.File = TagLib.File.Create(percorsoFile)
                    dati.Titolo = fileTag.Tag.Title
                    dati.Artista = String.Join(", ", fileTag.Tag.Performers)
                    dati.Album = fileTag.Tag.Album
                    dati.Anno = fileTag.Tag.Year
                    dati.Genere = String.Join(", ", fileTag.Tag.Genres)
                    dati.Successo = True
                End Using
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return dati
    End Function

    Public Function ScriviTag(percorsoFile As String, nuoviDati As DatiTag) As Boolean
        Try
            If WGRete.EsisteFile(percorsoFile, 8000) Then
                Using fileTag As TagLib.File = TagLib.File.Create(percorsoFile)
                    If Not String.IsNullOrEmpty(nuoviDati.Titolo) Then fileTag.Tag.Title = nuoviDati.Titolo

                    If Not String.IsNullOrEmpty(nuoviDati.Artista) Then
                        fileTag.Tag.Performers = nuoviDati.Artista.Split({", "}, StringSplitOptions.RemoveEmptyEntries)
                    End If

                    If Not String.IsNullOrEmpty(nuoviDati.Album) Then fileTag.Tag.Album = nuoviDati.Album
                    If nuoviDati.Anno > 0 Then fileTag.Tag.Year = nuoviDati.Anno

                    If Not String.IsNullOrEmpty(nuoviDati.Genere) Then
                        fileTag.Tag.Genres = nuoviDati.Genere.Split({", "}, StringSplitOptions.RemoveEmptyEntries)
                    End If

                    fileTag.Save()
                End Using
                Return True
            End If
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return False
    End Function

    Public Async Function CercaSuiTunes(query As String, Optional numeroRisultati As Integer = 5) As Task(Of List(Of DatiTag))
        Dim risultati As New List(Of DatiTag)()
        Try
            Dim url As String = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&media=music&entity=song&limit={numeroRisultati}"
            Dim jsonResult As String = Await EseguiRichiestaConRetry(url)
            If String.IsNullOrEmpty(jsonResult) Then Return risultati

            Dim radice As JObject = JObject.Parse(jsonResult)

            Dim conteggio As Integer = 0
            Dim tokenConteggio As JToken = radice("resultCount")
            If tokenConteggio IsNot Nothing Then
                Integer.TryParse(tokenConteggio.ToString(), conteggio)
            End If
            If conteggio <= 0 Then Return risultati

            Dim elementi As JArray = TryCast(radice("results"), JArray)
            If elementi Is Nothing Then Return risultati

            For Each elementoToken As JToken In elementi
                Try
                    Dim elemento As JObject = TryCast(elementoToken, JObject)
                    If elemento Is Nothing Then Continue For

                    Dim dati As New DatiTag()
                    dati.Titolo = ValoreStringaJson(elemento, "trackName")
                    dati.Artista = ValoreStringaJson(elemento, "artistName")
                    dati.Album = ValoreStringaJson(elemento, "collectionName")
                    dati.Genere = ValoreStringaJson(elemento, "primaryGenreName")

                    Dim releaseDate As String = ValoreStringaJson(elemento, "releaseDate")
                    If Not String.IsNullOrEmpty(releaseDate) AndAlso releaseDate.Length >= 4 Then
                        UInteger.TryParse(releaseDate.Substring(0, 4), dati.Anno)
                    End If

                    dati.Successo = True
                    risultati.Add(dati)
                Catch ex As Exception
                    _log.ScriviLog("CLSTagMp3.CercaSuiTunes - Errore elaborazione risultato: " & ex.Message)
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog(ex.ToString())
        End Try
        Return risultati
    End Function

    Private Function ValoreStringaJson(elemento As JObject, chiave As String) As String
        Try
            Dim token As JToken = elemento(chiave)
            If token Is Nothing OrElse token.Type = JTokenType.Null Then Return ""
            Return token.ToString()
        Catch ex As Exception
            _log.ScriviLog("CLSTagMp3.ValoreStringaJson - Errore: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Async Function EseguiRichiestaConRetry(url As String) As Task(Of String)
        For tentativo As Integer = 1 To TENTATIVI_RICERCA
            Try
                Dim response As HttpResponseMessage = Await _httpClient.GetAsync(url)
                If response.IsSuccessStatusCode Then
                    Return Await response.Content.ReadAsStringAsync()
                End If
            Catch ex As Exception
                _log.ScriviLog($"CLSTagMp3.EseguiRichiestaConRetry - tentativo {tentativo}/{TENTATIVI_RICERCA} fallito: {ex.Message}")
            End Try

            If tentativo < TENTATIVI_RICERCA Then
                Await Task.Delay(ATTESA_BASE_RETRY_MS * tentativo)
            End If
        Next
        Return ""
    End Function
End Class