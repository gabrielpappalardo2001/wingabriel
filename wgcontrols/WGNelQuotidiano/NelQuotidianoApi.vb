Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>
''' Gli articoli di nelquotidiano.news raccolti sul sito (wingabriel.net/quotidiano.php) e letti con quotidianoapi.php: l'elenco, dal più
''' recente, e il testo di un articolo. L'interfaccia funziona anche senza password; se sul server ne è stata impostata una, va scritta
''' nella configurazione (chiave "nelquotidiano_password") e si manda nell'intestazione X-Password.
''' </summary>
Public NotInheritable Class NelQuotidianoApi

    Private Const URL_API As String = "https://www.wingabriel.net/quotidianoapi.php"
    Private Const CHIAVE_PASSWORD As String = "nelquotidiano_password"
    Private Const ARTICOLI_PER_PAGINA As Integer = 100
    Private Const PAGINE_MASSIME As Integer = 10
    Private Const ATTESA_MS As Integer = 30000

    Public NotInheritable Class VoceArticolo
        Public Property Id As Long
        Public Property Titolo As String = ""
        Public Property Url As String = ""
        Public Property Autore As String = ""
        ''' <summary>La data dell'articolo (aaaa-mm-gg) o, se non c'è, quella in cui è stato aggiunto.</summary>
        Public Property Data As DateTime = DateTime.MinValue
    End Class

    Public NotInheritable Class Articolo
        Public Property Id As Long
        Public Property Titolo As String = ""
        Public Property Url As String = ""
        Public Property Autore As String = ""
        Public Property Data As DateTime = DateTime.MinValue
        Public Property Testo As String = ""
    End Class

    Private Shared ReadOnly _logger As New Logger()

    Private Sub New()
    End Sub

    Private Shared Function Password() As String
        Try
            Return Convert.ToString(New WGConfig().LeggiValore(CHIAVE_PASSWORD)).Trim()
        Catch
            Return ""
        End Try
    End Function

    Private Shared Function Scarica(indirizzo As String) As JObject
        ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
        Dim richiesta As HttpWebRequest = CType(WebRequest.Create(indirizzo), HttpWebRequest)
        richiesta.Method = "GET"
        richiesta.UserAgent = "WinGabriel/1.0"
        richiesta.Accept = "application/json"
        richiesta.Timeout = ATTESA_MS
        richiesta.ReadWriteTimeout = ATTESA_MS
        richiesta.KeepAlive = False
        Dim parola As String = Password()
        If parola.Length > 0 Then richiesta.Headers.Add("X-Password", parola)
        Try
            Using risposta As HttpWebResponse = CType(richiesta.GetResponse(), HttpWebResponse)
                Return Leggi(risposta)
            End Using
        Catch ex As WebException
            If ex.Response IsNot Nothing Then
                Using risposta As HttpWebResponse = CType(ex.Response, HttpWebResponse)
                    Dim corpo As JObject = Nothing
                    Try
                        corpo = Leggi(risposta)
                    Catch
                    End Try
                    Dim motivo As String = If(corpo IsNot Nothing, Convert.ToString(corpo("errore")), "")
                    If risposta.StatusCode = HttpStatusCode.Unauthorized Then
                        Throw New InvalidOperationException("Il sito chiede una password per gli articoli e quella nella configurazione non è corretta.")
                    End If
                    Throw New InvalidOperationException(If(String.IsNullOrWhiteSpace(motivo), "Il sito ha risposto con l'errore " & CInt(risposta.StatusCode).ToString(CultureInfo.InvariantCulture) & ".", motivo))
                End Using
            End If
            Throw
        End Try
    End Function

    Private Shared Function Leggi(risposta As HttpWebResponse) As JObject
        Using flusso As Stream = risposta.GetResponseStream()
            Using lettore As New StreamReader(flusso, Encoding.UTF8)
                Dim testo As String = lettore.ReadToEnd()
                Dim token As JToken = JToken.Parse(testo)
                Dim oggetto As JObject = TryCast(token, JObject)
                If oggetto Is Nothing Then Throw New InvalidOperationException("Risposta del sito non valida.")
                Return oggetto
            End Using
        End Using
    End Function

    Private Shared Function LeggiData(valore As JToken, alternativa As JToken) As DateTime
        Dim d As DateTime
        For Each v As JToken In New JToken() {valore, alternativa}
            If v Is Nothing OrElse v.Type = JTokenType.Null Then Continue For
            If DateTime.TryParse(Convert.ToString(v), CultureInfo.InvariantCulture, DateTimeStyles.None, d) Then Return d
        Next
        Return DateTime.MinValue
    End Function

    ''' <summary>L'elenco degli articoli pronti, dal più recente (fino a mille). Solleva un errore se il sito non risponde.</summary>
    Public Shared Function ElencaAsync() As Task(Of List(Of VoceArticolo))
        Return Task.Run(Function() Elenca())
    End Function

    Private Shared Function Elenca() As List(Of VoceArticolo)
        Dim risultato As New List(Of VoceArticolo)()
        Dim dopo As Long = 0
        For pagina As Integer = 1 To PAGINE_MASSIME
            Dim indirizzo As String = URL_API & "?azione=elenco&limite=" & ARTICOLI_PER_PAGINA.ToString(CultureInfo.InvariantCulture)
            If dopo > 0 Then indirizzo &= "&dopo=" & dopo.ToString(CultureInfo.InvariantCulture)
            Dim dati As JObject = Scarica(indirizzo)
            Dim articoli As JArray = TryCast(dati("articoli"), JArray)
            If articoli Is Nothing Then Exit For
            For Each a As JToken In articoli
                Dim v As New VoceArticolo()
                v.Id = Convert.ToInt64(a("id"), CultureInfo.InvariantCulture)
                v.Titolo = Convert.ToString(a("titolo"))
                v.Url = Convert.ToString(a("url"))
                v.Autore = Convert.ToString(a("autore"))
                v.Data = LeggiData(a("data"), a("inserito"))
                risultato.Add(v)
            Next
            Dim prossimo As JToken = dati("prossimo")
            If prossimo Is Nothing OrElse prossimo.Type = JTokenType.Null Then Exit For
            dopo = Convert.ToInt64(prossimo, CultureInfo.InvariantCulture)
        Next
        Return risultato
    End Function

    ''' <summary>Titolo, autore, data e testo di un articolo. Nothing se non c'è più.</summary>
    Public Shared Function LeggiAsync(id As Long) As Task(Of Articolo)
        Return Task.Run(Function() LeggiArticolo(id))
    End Function

    Private Shared Function LeggiArticolo(id As Long) As Articolo
        Dim dati As JObject = Nothing
        Try
            dati = Scarica(URL_API & "?azione=articolo&id=" & id.ToString(CultureInfo.InvariantCulture))
        Catch ex As InvalidOperationException When ex.Message.IndexOf("non trovato", StringComparison.OrdinalIgnoreCase) >= 0
            Return Nothing
        End Try
        Dim a As New Articolo()
        a.Id = id
        a.Titolo = Convert.ToString(dati("titolo"))
        a.Url = Convert.ToString(dati("url"))
        a.Autore = Convert.ToString(dati("autore"))
        a.Data = LeggiData(dati("data"), dati("inserito"))
        a.Testo = Convert.ToString(dati("testo"))
        Return a
    End Function

End Class
