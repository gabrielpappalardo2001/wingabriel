Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' Gli elenchi dei canali delle fonti dei palinsesti, scaricati dal sito e tenuti per qualche ora. Servono a trovare la programmazione di un canale o di una
' radio anche quando non è nelle tabelle fisse di TvProgrammazione e RadioProgrammazione: il nome del canale si confronta con i nomi delle fonti, e
' così ogni canale nuovo che compare negli elenchi delle fonti ha la sua programmazione senza toccare il programma.
'  - guida di Sky (apid.sky.it): i canali del satellite e del digitale terrestre (DTH e DTT);
'  - feed dei palinsesti di Mediaset: canali tv e radio del gruppo;
'  - RaiPlay (raiplay.it/dirette.json): i canali televisivi della Rai.
Friend NotInheritable Class GuidaPalinsesti

    Private Const ORE_VALIDITA As Integer = 12
    Private Const URL_SKY As String = "https://apid.sky.it/gtv/v1/channels?env="
    Private Const URL_MEDIASET As String = "https://feed.entertainment.tv.theplatform.eu/f/PR1GhC/mediaset-prod-all-stations-v2?form=cjson"
    Private Const URL_RAIPLAY As String = "https://www.raiplay.it/dirette.json"

    Private Shared ReadOnly _logger As New Logger()
    Private Shared ReadOnly _lock As New Object()

    Private Shared _sky As Dictionary(Of String, List(Of Integer))
    Private Shared _skyAlle As DateTime = DateTime.MinValue
    Private Shared _mediaset As Dictionary(Of String, KeyValuePair(Of String, String))
    Private Shared _mediasetAlle As DateTime = DateTime.MinValue
    Private Shared _raiTv As Dictionary(Of String, String)
    Private Shared _raiTvAlle As DateTime = DateTime.MinValue

    Private Sub New()
    End Sub

    ' Il nome senza maiuscole, accenti, spazi e segni, e senza le parole che le fonti aggiungono o tolgono a piacere (HD, DTT, FM, live, tv):
    ' "RADIONORBA TV", "Radio Norba" e "Radio Norba FM" diventano la stessa chiave. "radio" resta, perché "Rai Radio 1" non è "Rai 1".
    Friend Shared Function Chiave(nome As String) As String
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return ""
            Dim scomposto As String = nome.Normalize(NormalizationForm.FormD)
            Dim sb As New StringBuilder()
            For Each c As Char In scomposto
                If CharUnicodeInfo.GetUnicodeCategory(c) = UnicodeCategory.NonSpacingMark Then Continue For
                If Char.IsLetterOrDigit(c) Then sb.Append(Char.ToLowerInvariant(c))
            Next
            Dim r As String = Regex.Replace(sb.ToString(), "hd|dtt|fm|live|tv", "")
            If r.StartsWith("radiorai", StringComparison.Ordinal) Then r = "rairadio" & r.Substring(8)
            Return r
        Catch ex As Exception
            _logger.ScriviLog("GuidaPalinsesti.Chiave: " & ex.Message)
            Return ""
        End Try
    End Function

#Region "Sky"

    Private Shared Async Function DirectorySkyAsync() As Task(Of Dictionary(Of String, List(Of Integer)))
        Try
            SyncLock _lock
                If _sky IsNot Nothing AndAlso (DateTime.Now - _skyAlle).TotalHours < ORE_VALIDITA Then Return _sky
            End SyncLock

            Dim risultato As New Dictionary(Of String, List(Of Integer))(StringComparer.OrdinalIgnoreCase)
            Dim almenoUno As Boolean = False
            For Each ambiente As String In New String() {"DTH", "DTT"}
                Dim testo As String = Await TvProgrammazione.ScaricaTestoAsync(URL_SKY & ambiente).ConfigureAwait(False)
                If String.IsNullOrWhiteSpace(testo) Then Continue For
                Dim canali As JArray = TryCast(TvProgrammazione.AnalizzaJson(testo)("channels"), JArray)
                If canali Is Nothing Then Continue For
                almenoUno = True
                For Each c As JToken In canali
                    Dim chiave As String = GuidaPalinsesti.Chiave(TvProgrammazione.TestoDi(c("name")))
                    Dim id As Integer
                    If chiave.Length = 0 OrElse Not Integer.TryParse(TvProgrammazione.TestoDi(c("id")), id) Then Continue For
                    Dim elenco As List(Of Integer) = Nothing
                    If Not risultato.TryGetValue(chiave, elenco) Then
                        elenco = New List(Of Integer)()
                        risultato(chiave) = elenco
                    End If
                    If Not elenco.Contains(id) Then elenco.Add(id)
                Next
            Next
            If Not almenoUno Then Return Nothing

            SyncLock _lock
                _sky = risultato
                _skyAlle = DateTime.Now
            End SyncLock
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("GuidaPalinsesti.DirectorySkyAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ' I numeri dei canali Sky con quel nome (anche più di uno: lo stesso canale può comparire in più liste). Nothing se l'elenco non si scarica.
    Friend Shared Async Function IdSkyAsync(nome As String) As Task(Of List(Of Integer))
        Dim directory As Dictionary(Of String, List(Of Integer)) = Await DirectorySkyAsync().ConfigureAwait(False)
        If directory Is Nothing Then Return Nothing
        Dim elenco As List(Of Integer) = Nothing
        Dim chiave As String = GuidaPalinsesti.Chiave(nome)
        If chiave.Length > 0 AndAlso directory.TryGetValue(chiave, elenco) Then Return New List(Of Integer)(elenco)
        Return New List(Of Integer)()
    End Function

#End Region

#Region "Mediaset"

    Private Shared Async Function DirectoryMediasetAsync() As Task(Of Dictionary(Of String, KeyValuePair(Of String, String)))
        Try
            SyncLock _lock
                If _mediaset IsNot Nothing AndAlso (DateTime.Now - _mediasetAlle).TotalHours < ORE_VALIDITA Then Return _mediaset
            End SyncLock

            Dim testo As String = Await TvProgrammazione.ScaricaTestoAsync(URL_MEDIASET).ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing
            Dim voci As JArray = TryCast(TvProgrammazione.AnalizzaJson(testo)("entries"), JArray)
            If voci Is Nothing Then Return Nothing

            Dim risultato As New Dictionary(Of String, KeyValuePair(Of String, String))(StringComparer.OrdinalIgnoreCase)
            For Each v As JToken In voci
                Dim sigla As String = TvProgrammazione.TestoDi(v("callSign"))
                Dim titolo As String = TvProgrammazione.TestoDi(v("title"))
                Dim tipo As String = TvProgrammazione.TestoDi(v("mediasetstation$channelType")).ToUpperInvariant()
                If sigla.Length = 0 OrElse titolo.Length = 0 Then Continue For
                If tipo <> "TV" AndAlso tipo <> "RADIO" Then Continue For
                Dim chiave As String = tipo & "|" & GuidaPalinsesti.Chiave(titolo)
                If Not risultato.ContainsKey(chiave) Then risultato(chiave) = New KeyValuePair(Of String, String)(sigla, tipo)
            Next

            SyncLock _lock
                _mediaset = risultato
                _mediasetAlle = DateTime.Now
            End SyncLock
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("GuidaPalinsesti.DirectoryMediasetAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ' La sigla Mediaset del canale o della radio con quel nome; vuota se non c'è, Nothing se l'elenco non si scarica.
    Friend Shared Async Function SiglaMediasetAsync(nome As String, radio As Boolean) As Task(Of String)
        Dim directory As Dictionary(Of String, KeyValuePair(Of String, String)) = Await DirectoryMediasetAsync().ConfigureAwait(False)
        If directory Is Nothing Then Return Nothing
        Dim trovata As KeyValuePair(Of String, String)
        Dim chiave As String = If(radio, "RADIO", "TV") & "|" & GuidaPalinsesti.Chiave(nome)
        If directory.TryGetValue(chiave, trovata) Then Return trovata.Key
        Return ""
    End Function

#End Region

#Region "RaiPlay (televisione)"

    Private Shared Async Function DirectoryRaiTvAsync() As Task(Of Dictionary(Of String, String))
        Try
            SyncLock _lock
                If _raiTv IsNot Nothing AndAlso (DateTime.Now - _raiTvAlle).TotalHours < ORE_VALIDITA Then Return _raiTv
            End SyncLock

            Dim testo As String = Await TvProgrammazione.ScaricaTestoAsync(URL_RAIPLAY).ConfigureAwait(False)
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing
            Dim radice As JObject = TvProgrammazione.AnalizzaJson(testo)

            Dim risultato As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            For Each oggetto As JObject In radice.DescendantsAndSelf().OfType(Of JObject)()
                Dim nome As String = TvProgrammazione.TestoDi(oggetto("channel"))
                Dim percorso As String = TvProgrammazione.TestoDi(oggetto("path_id"))
                If nome.Length = 0 OrElse Not percorso.StartsWith("/dirette/", StringComparison.OrdinalIgnoreCase) Then Continue For
                If nome.IndexOf("radio", StringComparison.OrdinalIgnoreCase) >= 0 Then Continue For
                Dim chiave As String = GuidaPalinsesti.Chiave(nome)
                If chiave.Length = 0 OrElse risultato.ContainsKey(chiave) Then Continue For
                risultato(chiave) = SlugRai(nome)
            Next

            SyncLock _lock
                _raiTv = risultato
                _raiTvAlle = DateTime.Now
            End SyncLock
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("GuidaPalinsesti.DirectoryRaiTvAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ' L'indirizzo del palinsesto di RaiPlay è il nome in minuscolo con i trattini: "Rai News 24" diventa "rai-news-24".
    Private Shared Function SlugRai(nome As String) As String
        Dim scomposto As String = nome.Normalize(NormalizationForm.FormD)
        Dim sb As New StringBuilder()
        For Each c As Char In scomposto
            If CharUnicodeInfo.GetUnicodeCategory(c) = UnicodeCategory.NonSpacingMark Then Continue For
            If Char.IsLetterOrDigit(c) Then
                sb.Append(Char.ToLowerInvariant(c))
            ElseIf c = " "c OrElse c = "-"c Then
                sb.Append("-"c)
            End If
        Next
        Return Regex.Replace(sb.ToString(), "-+", "-").Trim("-"c)
    End Function

    ' L'indirizzo del palinsesto RaiPlay del canale televisivo con quel nome; vuoto se non è un canale Rai, Nothing se l'elenco non si scarica.
    Friend Shared Async Function SlugRaiTvAsync(nome As String) As Task(Of String)
        If GuidaPalinsesti.Chiave(nome).StartsWith("rai", StringComparison.Ordinal) = False Then Return ""
        Dim directory As Dictionary(Of String, String) = Await DirectoryRaiTvAsync().ConfigureAwait(False)
        If directory Is Nothing Then Return Nothing
        Dim slug As String = Nothing
        If directory.TryGetValue(GuidaPalinsesti.Chiave(nome), slug) Then Return slug
        Return ""
    End Function

    ' La programmazione di oggi e domani di un canale RaiPlay (televisione).
    Friend Shared Async Function ScaricaRaiTvAsync(slug As String) As Task(Of List(Of TvProgrammazione.VoceProgramma))
        Try
            Dim risultato As New List(Of TvProgrammazione.VoceProgramma)()
            For giorno As Integer = 0 To 1
                Dim data As DateTime = DateTime.Today.AddDays(giorno)
                Dim url As String = "https://www.raiplay.it/palinsesto/app/" & slug & "/" & data.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) & ".json"
                Dim testo As String = Await TvProgrammazione.ScaricaTestoAsync(url).ConfigureAwait(False)
                If String.IsNullOrWhiteSpace(testo) Then
                    If giorno = 0 Then Return Nothing
                    Continue For
                End If
                RadioProgrammazione.AggiungiGiornoRai(testo, risultato)
            Next
            Return risultato
        Catch ex As Exception
            _logger.ScriviLog("GuidaPalinsesti.ScaricaRaiTvAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

#End Region

    ' La programmazione di un canale o di una radio cercata per nome in tutte le fonti, nell'ordine: RaiPlay (canali Rai), Mediaset, Sky.
    ' Lista vuota se nessuna fonte ha quel nome, Nothing se una fonte non risponde e nessun'altra ha dato risultati.
    Friend Shared Async Function CercaPerNomeAsync(nome As String, radio As Boolean) As Task(Of List(Of TvProgrammazione.VoceProgramma))
        Try
            If String.IsNullOrWhiteSpace(nome) Then Return New List(Of TvProgrammazione.VoceProgramma)()
            Dim nonRisponde As Boolean = False

            If Not radio Then
                Dim slug As String = Await SlugRaiTvAsync(nome).ConfigureAwait(False)
                If slug Is Nothing Then
                    nonRisponde = True
                ElseIf slug.Length > 0 Then
                    Dim daRai As List(Of TvProgrammazione.VoceProgramma) = Await ScaricaRaiTvAsync(slug).ConfigureAwait(False)
                    If daRai IsNot Nothing AndAlso daRai.Count > 0 Then Return daRai
                End If
            End If

            Dim sigla As String = Await SiglaMediasetAsync(nome, radio).ConfigureAwait(False)
            If sigla Is Nothing Then
                nonRisponde = True
            ElseIf sigla.Length > 0 Then
                Dim daMediaset As List(Of TvProgrammazione.VoceProgramma) = Await TvProgrammazione.ScaricaMediasetAsync(sigla).ConfigureAwait(False)
                If daMediaset IsNot Nothing AndAlso daMediaset.Count > 0 Then Return daMediaset
            End If

            Dim idSky As List(Of Integer) = Await IdSkyAsync(nome).ConfigureAwait(False)
            If idSky Is Nothing Then
                nonRisponde = True
            Else
                For Each id As Integer In idSky
                    Dim daSky As List(Of TvProgrammazione.VoceProgramma) = Await TvProgrammazione.ScaricaSkyAsync(id).ConfigureAwait(False)
                    If daSky IsNot Nothing AndAlso daSky.Count > 0 Then Return daSky
                Next
            End If

            If nonRisponde Then Return Nothing
            Return New List(Of TvProgrammazione.VoceProgramma)()
        Catch ex As Exception
            _logger.ScriviLog("GuidaPalinsesti.CercaPerNomeAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

End Class
