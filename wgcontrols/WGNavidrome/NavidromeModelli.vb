Imports System
Imports System.Collections.Generic
Imports Newtonsoft.Json.Linq

''' <summary>
''' Un errore restituito dal server Navidrome o dalla connessione. Il messaggio è già in italiano
''' e pronto per essere letto all'utente con un semplice WGMessaggi.
''' </summary>
Public Class NavidromeEccezione
    Inherits Exception

    Public ReadOnly Property Codice As Integer

    Public Sub New(messaggio As String, Optional codice As Integer = 0, Optional interna As Exception = Nothing)
        MyBase.New(messaggio, interna)
        Me.Codice = codice
    End Sub
End Class

''' <summary>Aiutanti per leggere i campi JSON delle risposte Subsonic senza sorprese sui valori mancanti.</summary>
Friend Module NavidromeJson

    Friend Function Testo(j As JToken, chiave As String) As String
        Try
            If j Is Nothing Then Return ""
            Dim v As JToken = j(chiave)
            If v Is Nothing OrElse v.Type = JTokenType.Null Then Return ""
            Return v.ToString()
        Catch
            Return ""
        End Try
    End Function

    Friend Function Intero(j As JToken, chiave As String) As Integer
        Try
            Dim t As String = Testo(j, chiave)
            Dim r As Integer
            If Integer.TryParse(t, r) Then Return r
            Return 0
        Catch
            Return 0
        End Try
    End Function

    Friend Function Lungo(j As JToken, chiave As String) As Long
        Try
            Dim t As String = Testo(j, chiave)
            Dim r As Long
            If Long.TryParse(t, r) Then Return r
            Return 0
        Catch
            Return 0
        End Try
    End Function

    Friend Function Booleano(j As JToken, chiave As String) As Boolean
        Return String.Equals(Testo(j, chiave), "true", StringComparison.OrdinalIgnoreCase)
    End Function

    Friend Function Presente(j As JToken, chiave As String) As Boolean
        Try
            Return j IsNot Nothing AndAlso j(chiave) IsNot Nothing AndAlso j(chiave).Type <> JTokenType.Null
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Gli elementi di una lista Subsonic: vuota se manca la chiave, anche con un solo oggetto.</summary>
    Friend Function Elenco(j As JToken, chiave As String) As List(Of JToken)
        Dim risultato As New List(Of JToken)
        Try
            If j Is Nothing Then Return risultato
            Dim v As JToken = j(chiave)
            If v Is Nothing Then Return risultato
            If v.Type = JTokenType.Array Then
                risultato.AddRange(v)
            ElseIf v.Type = JTokenType.Object Then
                risultato.Add(v)
            End If
        Catch
        End Try
        Return risultato
    End Function

    Friend Function Durata(secondi As Integer) As String
        If secondi <= 0 Then Return ""
        Dim t As TimeSpan = TimeSpan.FromSeconds(secondi)
        If t.TotalHours >= 1 Then
            Return CInt(Math.Floor(t.TotalHours)).ToString() & " ore " & t.Minutes.ToString() & " minuti"
        End If
        Return t.Minutes.ToString() & ":" & t.Seconds.ToString("00")
    End Function

End Module

Public Class NavidromeInfoServer
    Public Property Tipo As String = ""
    Public Property VersioneServer As String = ""
    Public Property VersioneApi As String = ""
End Class

Public Class NavidromeBrano
    Public Property Id As String = ""
    Public Property Titolo As String = ""
    Public Property Artista As String = ""
    Public Property ArtistaId As String = ""
    Public Property Album As String = ""
    Public Property AlbumId As String = ""
    Public Property Genere As String = ""
    Public Property Anno As Integer
    Public Property Traccia As Integer
    Public Property Disco As Integer
    Public Property DurataSecondi As Integer
    Public Property Formato As String = ""
    Public Property BitRate As Integer
    Public Property Dimensione As Long
    Public Property Preferito As Boolean
    Public Property Voto As Integer
    Public Property NumeroAscolti As Integer
    ''' <summary>Testo letto prima del titolo, per esempio chi sta ascoltando il brano.</summary>
    Public Property Prefisso As String = ""

    ''' <summary>Quello che si legge nell'elenco: titolo, artista, album e durata.</summary>
    Public ReadOnly Property Descrizione As String
        Get
            Dim s As String = Prefisso & Titolo
            If Not String.IsNullOrWhiteSpace(Artista) Then s &= ", " & Artista
            If Not String.IsNullOrWhiteSpace(Album) Then s &= ", " & Album
            Dim d As String = NavidromeJson.Durata(DurataSecondi)
            If d <> "" Then s &= ", " & d
            Return s
        End Get
    End Property

    Friend Shared Function Da(j As JToken) As NavidromeBrano
        Dim b As New NavidromeBrano()
        b.Id = NavidromeJson.Testo(j, "id")
        b.Titolo = NavidromeJson.Testo(j, "title")
        If b.Titolo = "" Then b.Titolo = NavidromeJson.Testo(j, "name")
        b.Artista = NavidromeJson.Testo(j, "artist")
        b.ArtistaId = NavidromeJson.Testo(j, "artistId")
        b.Album = NavidromeJson.Testo(j, "album")
        b.AlbumId = NavidromeJson.Testo(j, "albumId")
        b.Genere = NavidromeJson.Testo(j, "genre")
        b.Anno = NavidromeJson.Intero(j, "year")
        b.Traccia = NavidromeJson.Intero(j, "track")
        b.Disco = NavidromeJson.Intero(j, "discNumber")
        b.DurataSecondi = NavidromeJson.Intero(j, "duration")
        b.Formato = NavidromeJson.Testo(j, "suffix")
        b.BitRate = NavidromeJson.Intero(j, "bitRate")
        b.Dimensione = NavidromeJson.Lungo(j, "size")
        b.Preferito = NavidromeJson.Presente(j, "starred")
        b.Voto = NavidromeJson.Intero(j, "userRating")
        b.NumeroAscolti = NavidromeJson.Intero(j, "playCount")
        Return b
    End Function
End Class

Public Class NavidromeAlbum
    Public Property Id As String = ""
    Public Property Nome As String = ""
    Public Property Artista As String = ""
    Public Property ArtistaId As String = ""
    Public Property Anno As Integer
    Public Property Genere As String = ""
    Public Property NumeroBrani As Integer
    Public Property DurataSecondi As Integer
    Public Property Preferito As Boolean

    Public ReadOnly Property Descrizione As String
        Get
            Dim s As String = Nome
            If Not String.IsNullOrWhiteSpace(Artista) Then s &= ", " & Artista
            If Anno > 0 Then s &= ", " & Anno.ToString()
            If NumeroBrani > 0 Then s &= ", " & NumeroBrani.ToString() & If(NumeroBrani = 1, " brano", " brani")
            Return s
        End Get
    End Property

    Friend Shared Function Da(j As JToken) As NavidromeAlbum
        Dim a As New NavidromeAlbum()
        a.Id = NavidromeJson.Testo(j, "id")
        a.Nome = NavidromeJson.Testo(j, "name")
        If a.Nome = "" Then a.Nome = NavidromeJson.Testo(j, "title")
        a.Artista = NavidromeJson.Testo(j, "artist")
        a.ArtistaId = NavidromeJson.Testo(j, "artistId")
        a.Anno = NavidromeJson.Intero(j, "year")
        a.Genere = NavidromeJson.Testo(j, "genre")
        a.NumeroBrani = NavidromeJson.Intero(j, "songCount")
        a.DurataSecondi = NavidromeJson.Intero(j, "duration")
        a.Preferito = NavidromeJson.Presente(j, "starred")
        Return a
    End Function
End Class

Public Class NavidromeArtista
    Public Property Id As String = ""
    Public Property Nome As String = ""
    Public Property NumeroAlbum As Integer
    Public Property Preferito As Boolean

    Public ReadOnly Property Descrizione As String
        Get
            If NumeroAlbum > 0 Then Return Nome & ", " & NumeroAlbum.ToString() & " album"
            Return Nome
        End Get
    End Property

    Friend Shared Function Da(j As JToken) As NavidromeArtista
        Dim a As New NavidromeArtista()
        a.Id = NavidromeJson.Testo(j, "id")
        a.Nome = NavidromeJson.Testo(j, "name")
        a.NumeroAlbum = NavidromeJson.Intero(j, "albumCount")
        a.Preferito = NavidromeJson.Presente(j, "starred")
        Return a
    End Function
End Class

Public Class NavidromePlaylist
    Public Property Id As String = ""
    Public Property Nome As String = ""
    Public Property Proprietario As String = ""
    Public Property Pubblica As Boolean
    Public Property NumeroBrani As Integer
    Public Property DurataSecondi As Integer

    Public ReadOnly Property Descrizione As String
        Get
            Dim s As String = Nome & ", " & NumeroBrani.ToString() & If(NumeroBrani = 1, " brano", " brani")
            Dim d As String = NavidromeJson.Durata(DurataSecondi)
            If d <> "" Then s &= ", " & d
            Return s
        End Get
    End Property

    Friend Shared Function Da(j As JToken) As NavidromePlaylist
        Dim p As New NavidromePlaylist()
        p.Id = NavidromeJson.Testo(j, "id")
        p.Nome = NavidromeJson.Testo(j, "name")
        p.Proprietario = NavidromeJson.Testo(j, "owner")
        p.Pubblica = NavidromeJson.Booleano(j, "public")
        p.NumeroBrani = NavidromeJson.Intero(j, "songCount")
        p.DurataSecondi = NavidromeJson.Intero(j, "duration")
        Return p
    End Function
End Class

Public Class NavidromeGenere
    Public Property Nome As String = ""
    Public Property NumeroBrani As Integer
    Public Property NumeroAlbum As Integer

    Public ReadOnly Property Descrizione As String
        Get
            Return Nome & ", " & NumeroAlbum.ToString() & " album, " & NumeroBrani.ToString() & If(NumeroBrani = 1, " brano", " brani")
        End Get
    End Property

    Friend Shared Function Da(j As JToken) As NavidromeGenere
        Dim g As New NavidromeGenere()
        g.Nome = NavidromeJson.Testo(j, "value")
        g.NumeroBrani = NavidromeJson.Intero(j, "songCount")
        g.NumeroAlbum = NavidromeJson.Intero(j, "albumCount")
        Return g
    End Function
End Class

Public Class NavidromeRadio
    Public Property Id As String = ""
    Public Property Nome As String = ""
    Public Property IndirizzoStream As String = ""

    Friend Shared Function Da(j As JToken) As NavidromeRadio
        Dim r As New NavidromeRadio()
        r.Id = NavidromeJson.Testo(j, "id")
        r.Nome = NavidromeJson.Testo(j, "name")
        r.IndirizzoStream = NavidromeJson.Testo(j, "streamUrl")
        Return r
    End Function
End Class

Public Class NavidromeRisultatiRicerca
    Public Property Artisti As New List(Of NavidromeArtista)
    Public Property Album As New List(Of NavidromeAlbum)
    Public Property Brani As New List(Of NavidromeBrano)

    Public ReadOnly Property Vuoto As Boolean
        Get
            Return Artisti.Count = 0 AndAlso Album.Count = 0 AndAlso Brani.Count = 0
        End Get
    End Property
End Class

Public Class NavidromeStatoScansione
    Public Property InCorso As Boolean
    Public Property Conteggio As Long
End Class

''' <summary>Tipi di elenco di album che Navidrome sa fornire.</summary>
Public Enum NavidromeElencoAlbum
    PiuRecenti
    AscoltatiDiRecente
    PiuAscoltati
    Casuali
    OrdineAlfabetico
    OrdinePerArtista
    Preferiti
    PiuVotati
    PerGenere
End Enum
