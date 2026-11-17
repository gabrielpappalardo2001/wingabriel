Imports System.Collections.Generic
Imports System.Globalization
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' Analisi del layout visivo e strutturale della pagina web e delle lingue dei testi.
Public Class InfoNodoLayout
    Public IndiceGlobale As Integer = -1
    Public IndicePadre As Integer = -1
    Public IdBackend As Integer = 0
    Public Tag As String = ""
    Public TipoNodo As Integer = 0
    Public Display As String = ""
    Public Cursore As String = ""
    Public Posizione As String = ""
    Public Galleggiante As String = ""
    Public HaLayout As Boolean = False
    Public Cliccabile As Boolean = False
    Public PuntatoreRadice As Boolean = False
    Public X As Double = 0
    Public Y As Double = 0
    Public Larghezza As Double = 0
    Public Altezza As Double = 0
    Public Lingua As String = ""
    Public Attributi As Dictionary(Of String, String) = Nothing

    Public Function Attributo(nome As String) As String
        If Attributi Is Nothing Then Return Nothing
        Dim valore As String = Nothing
        If Attributi.TryGetValue(nome, valore) Then Return valore
        Return Nothing
    End Function

    Public ReadOnly Property EBlocco As Boolean
        Get
            If Not HaLayout Then Return False
            If Posizione = "absolute" OrElse Posizione = "fixed" Then Return True
            If Galleggiante <> "" AndAlso Galleggiante <> "none" Then Return True
            Select Case Display
                Case "", "inline", "inline-block", "inline-flex", "inline-grid", "inline-table", "contents", "ruby", "ruby-text", "ruby-base", "none"
                    Return False
            End Select
            Return True
        End Get
    End Property
End Class

Public Class LayoutDocumento
    Public ReadOnly Nodi As New List(Of InfoNodoLayout)()
    Public ReadOnly PerIdBackend As New Dictionary(Of Integer, InfoNodoLayout)()
    Public LinguaDocumento As String = ""
    Public LarghezzaContenuto As Double = 0
    Public AltezzaContenuto As Double = 0

    Public Function Info(idBackend As Integer) As InfoNodoLayout
        If idBackend <= 0 Then Return Nothing
        Dim risultato As InfoNodoLayout = Nothing
        If PerIdBackend.TryGetValue(idBackend, risultato) Then Return risultato
        Return Nothing
    End Function

    Public Function Padre(info As InfoNodoLayout) As InfoNodoLayout
        If info Is Nothing OrElse info.IndicePadre < 0 OrElse info.IndicePadre >= Nodi.Count Then Return Nothing
        Return Nodi(info.IndicePadre)
    End Function

    Public Function EAntenato(idAntenato As Integer, idDiscendente As Integer) As Boolean
        Dim antenato As InfoNodoLayout = Info(idAntenato)
        Dim corrente As InfoNodoLayout = Info(idDiscendente)
        If antenato Is Nothing OrElse corrente Is Nothing Then Return False
        Dim giri As Integer = 0
        While corrente IsNot Nothing AndAlso giri < 2000
            giri += 1
            If corrente.IndiceGlobale = antenato.IndiceGlobale Then Return True
            corrente = Padre(corrente)
        End While
        Return False
    End Function

    Public Function OrdineDocumento(idBackend As Integer) As Integer
        Dim nodo As InfoNodoLayout = Info(idBackend)
        If nodo Is Nothing Then Return -1
        Return nodo.IndiceGlobale
    End Function

    Public Function LinguaDi(idBackend As Integer) As String
        Dim nodo As InfoNodoLayout = Info(idBackend)
        If nodo Is Nothing Then Return LinguaDocumento
        If nodo.Lingua <> "" Then Return nodo.Lingua
        Return LinguaDocumento
    End Function

    Public Function ElementoDi(idBackend As Integer) As InfoNodoLayout
        Dim nodo As InfoNodoLayout = Info(idBackend)
        Dim giri As Integer = 0
        While nodo IsNot Nothing AndAlso nodo.TipoNodo <> 1 AndAlso giri < 50
            giri += 1
            nodo = Padre(nodo)
        End While
        Return nodo
    End Function
End Class

Public NotInheritable Class WGLayoutPagina
    Private Shared ReadOnly log As New Logger()

    Private Shared ReadOnly AttributiUtili As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "onclick", "jsaction", "tabindex", "role", "href", "src", "alt", "colspan", "rowspan", "aria-colspan", "aria-rowspan",
        "id", "name", "lang", "title", "data-src", "srcset", "aria-hidden", "contenteditable", "type"
    }

    Private Sub New()
    End Sub

    Public Shared Async Function CatturaAsync(dev As WGDevTools, sessione As String) As Task(Of LayoutDocumento)
        Try
            If dev Is Nothing OrElse Not dev.ECollegato Then Return Nothing
            Await dev.AbilitaSnapshotAsync(sessione)
            Dim parametri As New JObject()
            parametri("computedStyles") = New JArray("display", "cursor", "position", "float")
            parametri("includeDOMRects") = False
            parametri("includePaintOrder") = False
            Dim risposta As JObject = Await dev.InviaAsync("DOMSnapshot.captureSnapshot", parametri, sessione, 12000)
            If risposta Is Nothing Then Return Nothing
            Return Await Task.Run(Function() Analizza(risposta))
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.CatturaAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Shared Function Interi(token As JToken) As Integer()
        Try
            Dim elenco As JArray = TryCast(token, JArray)
            If elenco Is Nothing Then Return New Integer() {}
            Dim risultato(elenco.Count - 1) As Integer
            For i As Integer = 0 To elenco.Count - 1
                risultato(i) = CInt(elenco(i))
            Next
            Return risultato
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.Interi: " & ex.Message)
            Return New Integer() {}
        End Try
    End Function

    Private Shared Function Testo(stringhe As String(), indice As Integer) As String
        If indice < 0 OrElse stringhe Is Nothing OrElse indice >= stringhe.Length Then Return ""
        Return If(stringhe(indice), "")
    End Function

    Private Shared Function Analizza(risposta As JObject) As LayoutDocumento
        Dim layout As New LayoutDocumento()
        Try
            Dim elencoStringhe As JArray = TryCast(risposta("strings"), JArray)
            Dim stringhe As String() = New String() {}
            If elencoStringhe IsNot Nothing Then
                ReDim stringhe(elencoStringhe.Count - 1)
                For i As Integer = 0 To elencoStringhe.Count - 1
                    stringhe(i) = elencoStringhe(i).ToString()
                Next
            End If
            Dim documenti As JArray = TryCast(risposta("documents"), JArray)
            If documenti Is Nothing Then Return layout
            Dim primo As Boolean = True
            For Each documento As JObject In documenti
                AnalizzaDocumento(layout, documento, stringhe, primo)
                primo = False
            Next
            CalcolaEreditati(layout)
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.Analizza: " & ex.ToString())
        End Try
        Return layout
    End Function

    Private Shared Sub AnalizzaDocumento(layout As LayoutDocumento, documento As JObject, stringhe As String(), principale As Boolean)
        Try
            Dim nodi As JObject = TryCast(documento("nodes"), JObject)
            If nodi Is Nothing Then Return
            Dim padri As Integer() = Interi(nodi("parentIndex"))
            Dim nomi As Integer() = Interi(nodi("nodeName"))
            Dim tipi As Integer() = Interi(nodi("nodeType"))
            Dim idBackend As Integer() = Interi(nodi("backendNodeId"))
            Dim attributi As JArray = TryCast(nodi("attributes"), JArray)
            Dim offset As Integer = layout.Nodi.Count
            Dim quanti As Integer = idBackend.Length
            For i As Integer = 0 To quanti - 1
                Dim info As New InfoNodoLayout()
                info.IndiceGlobale = offset + i
                info.IndicePadre = If(i < padri.Length AndAlso padri(i) >= 0, offset + padri(i), -1)
                info.IdBackend = idBackend(i)
                info.Tag = If(i < nomi.Length, Testo(stringhe, nomi(i)).ToLowerInvariant(), "")
                info.TipoNodo = If(i < tipi.Length, tipi(i), 0)
                If attributi IsNot Nothing AndAlso i < attributi.Count AndAlso info.TipoNodo = 1 Then
                    Dim coppie As JArray = TryCast(attributi(i), JArray)
                    If coppie IsNot Nothing AndAlso coppie.Count > 1 Then
                        For k As Integer = 0 To coppie.Count - 2 Step 2
                            Dim nome As String = Testo(stringhe, CInt(coppie(k))).ToLowerInvariant()
                            If AttributiUtili.Contains(nome) Then
                                If info.Attributi Is Nothing Then info.Attributi = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                                info.Attributi(nome) = Testo(stringhe, CInt(coppie(k + 1)))
                            End If
                        Next
                    End If
                End If
                layout.Nodi.Add(info)
                If info.IdBackend > 0 Then layout.PerIdBackend(info.IdBackend) = info
            Next
            Dim cliccabili As JObject = TryCast(nodi("isClickable"), JObject)
            If cliccabili IsNot Nothing Then
                For Each indice As Integer In Interi(cliccabili("index"))
                    If indice >= 0 AndAlso indice < quanti Then layout.Nodi(offset + indice).Cliccabile = True
                Next
            End If
            Dim impaginazione As JObject = TryCast(documento("layout"), JObject)
            If impaginazione IsNot Nothing Then
                Dim indiciNodo As Integer() = Interi(impaginazione("nodeIndex"))
                Dim stili As JArray = TryCast(impaginazione("styles"), JArray)
                Dim limiti As JArray = TryCast(impaginazione("bounds"), JArray)
                For j As Integer = 0 To indiciNodo.Length - 1
                    Dim indice As Integer = indiciNodo(j)
                    If indice < 0 OrElse indice >= quanti Then Continue For
                    Dim info As InfoNodoLayout = layout.Nodi(offset + indice)
                    If info.HaLayout Then Continue For
                    info.HaLayout = True
                    If stili IsNot Nothing AndAlso j < stili.Count Then
                        Dim valori As JArray = TryCast(stili(j), JArray)
                        If valori IsNot Nothing Then
                            If valori.Count > 0 Then info.Display = Testo(stringhe, CInt(valori(0))).ToLowerInvariant()
                            If valori.Count > 1 Then info.Cursore = Testo(stringhe, CInt(valori(1))).ToLowerInvariant()
                            If valori.Count > 2 Then info.Posizione = Testo(stringhe, CInt(valori(2))).ToLowerInvariant()
                            If valori.Count > 3 Then info.Galleggiante = Testo(stringhe, CInt(valori(3))).ToLowerInvariant()
                        End If
                    End If
                    If limiti IsNot Nothing AndAlso j < limiti.Count Then
                        Dim rettangolo As JArray = TryCast(limiti(j), JArray)
                        If rettangolo IsNot Nothing AndAlso rettangolo.Count >= 4 Then
                            info.X = CDbl(rettangolo(0))
                            info.Y = CDbl(rettangolo(1))
                            info.Larghezza = CDbl(rettangolo(2))
                            info.Altezza = CDbl(rettangolo(3))
                        End If
                    End If
                Next
            End If
            If principale Then
                layout.LinguaDocumento = NormalizzaLingua(Testo(stringhe, documento.Value(Of Integer?)("contentLanguage").GetValueOrDefault(-1)))
                layout.LarghezzaContenuto = documento.Value(Of Double?)("contentWidth").GetValueOrDefault()
                layout.AltezzaContenuto = documento.Value(Of Double?)("contentHeight").GetValueOrDefault()
            End If
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.AnalizzaDocumento: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function NormalizzaLingua(lingua As String) As String
        Try
            If String.IsNullOrWhiteSpace(lingua) Then Return ""
            Dim valore As String = lingua.Trim().ToLowerInvariant().Replace("_", "-")
            Dim virgola As Integer = valore.IndexOf(","c)
            If virgola > 0 Then valore = valore.Substring(0, virgola)
            Return valore
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.NormalizzaLingua: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Sub CalcolaEreditati(layout As LayoutDocumento)
        Try
            For Each info As InfoNodoLayout In layout.Nodi
                Dim padre As InfoNodoLayout = layout.Padre(info)
                Dim linguaPropria As String = NormalizzaLingua(info.Attributo("lang"))
                If linguaPropria <> "" Then
                    info.Lingua = linguaPropria
                ElseIf padre IsNot Nothing Then
                    info.Lingua = padre.Lingua
                End If
                If info.Tag = "html" AndAlso info.Lingua <> "" AndAlso layout.LinguaDocumento = "" Then layout.LinguaDocumento = info.Lingua
                If info.HaLayout AndAlso info.Cursore = "pointer" AndAlso info.TipoNodo = 1 Then
                    Dim antenato As InfoNodoLayout = padre
                    Dim giri As Integer = 0
                    While antenato IsNot Nothing AndAlso Not antenato.HaLayout AndAlso giri < 20
                        giri += 1
                        antenato = layout.Padre(antenato)
                    End While
                    info.PuntatoreRadice = antenato Is Nothing OrElse antenato.Cursore <> "pointer"
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.CalcolaEreditati: " & ex.ToString())
        End Try
    End Sub

    Public Shared Function NomeLingua(codice As String) As String
        Try
            Dim principale As String = If(codice, "").ToLowerInvariant()
            Dim trattino As Integer = principale.IndexOf("-"c)
            If trattino > 0 Then principale = principale.Substring(0, trattino)
            Select Case principale
                Case "it" : Return "italiano"
                Case "en" : Return "inglese"
                Case "fr" : Return "francese"
                Case "de" : Return "tedesco"
                Case "es" : Return "spagnolo"
                Case "pt" : Return "portoghese"
                Case "nl" : Return "olandese"
                Case "ru" : Return "russo"
                Case "uk" : Return "ucraino"
                Case "pl" : Return "polacco"
                Case "ro" : Return "rumeno"
                Case "el" : Return "greco"
                Case "tr" : Return "turco"
                Case "sv" : Return "svedese"
                Case "da" : Return "danese"
                Case "no", "nb", "nn" : Return "norvegese"
                Case "fi" : Return "finlandese"
                Case "cs" : Return "ceco"
                Case "sk" : Return "slovacco"
                Case "hu" : Return "ungherese"
                Case "hr" : Return "croato"
                Case "sl" : Return "sloveno"
                Case "sr" : Return "serbo"
                Case "bg" : Return "bulgaro"
                Case "sq" : Return "albanese"
                Case "ar" : Return "arabo"
                Case "he" : Return "ebraico"
                Case "zh" : Return "cinese"
                Case "ja" : Return "giapponese"
                Case "ko" : Return "coreano"
                Case "hi" : Return "hindi"
                Case "la" : Return "latino"
                Case "ca" : Return "catalano"
                Case "eu" : Return "basco"
                Case "gl" : Return "galiziano"
                Case "fur" : Return "friulano"
                Case "sc" : Return "sardo"
                Case "lld" : Return "ladino"
                Case "", "und" : Return ""
            End Select
            Try
                Return CultureInfo.GetCultureInfo(principale).DisplayName
            Catch exCultura As CultureNotFoundException
                log.ScriviLog("WGLayoutPagina.NomeLingua lingua sconosciuta " & principale & ": " & exCultura.Message)
                Return principale
            End Try
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.NomeLingua: " & ex.Message)
            Return If(codice, "")
        End Try
    End Function

    Public Shared Function StessaLingua(prima As String, seconda As String) As Boolean
        Try
            Dim a As String = If(prima, "").ToLowerInvariant()
            Dim b As String = If(seconda, "").ToLowerInvariant()
            If a = "" OrElse b = "" Then Return True
            Dim ta As Integer = a.IndexOf("-"c)
            Dim tb As Integer = b.IndexOf("-"c)
            If ta > 0 Then a = a.Substring(0, ta)
            If tb > 0 Then b = b.Substring(0, tb)
            Return a = b
        Catch ex As Exception
            log.ScriviLog("WGLayoutPagina.StessaLingua: " & ex.Message)
            Return True
        End Try
    End Function
End Class
