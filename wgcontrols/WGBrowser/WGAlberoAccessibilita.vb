Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports WGBrowserManager
Imports wgcontrols

' Costruzione dell'albero di accessibilità della pagina per la lettura vocale e la navigazione.
Public Class WGAlberoAccessibilita

    Public Class Risultato
        Public Property Righe As New List(Of RigaVirtuale)()
        Public Property Titolo As String = ""
        Public Property IdModale As Integer = 0
        Public Property SessioneFocus As String = ""
        Public Property IdFocus As Integer = 0
        Public Property Pieno As Boolean = False
        Public Property Tabelle As New Dictionary(Of String, TabellaVirtuale)()
        Public Property Layout As LayoutDocumento = Nothing
        Public Property Lingua As String = ""
        Public Property AreeDisegnate As Boolean = False
    End Class

    Friend Class Documento
        Public Sessione As String = ""
        Public Nodi As New Dictionary(Of String, JObject)()
        Public Radice As JObject
        Public Layout As LayoutDocumento
    End Class

    Private ReadOnly log As New Logger()
    Private ReadOnly dev As WGDevTools

    Private Const MASSIMO_FRAME As Integer = 40

    Public Sub New(devTools As WGDevTools)
        dev = devTools
    End Sub

    Public Async Function CostruisciAsync() As Task(Of Risultato)
        Try
            If dev Is Nothing OrElse Not dev.ECollegato Then Return Nothing
            Dim compitoLayout As Task(Of LayoutDocumento) = WGLayoutPagina.CatturaAsync(dev, "")
            Dim compitoAlbero As Task(Of Documento) = AlberoAsync("", Nothing)
            Await Task.WhenAll(compitoLayout, compitoAlbero)
            Dim principale As Documento = compitoAlbero.Result
            If principale Is Nothing OrElse principale.Radice Is Nothing Then Return Nothing
            Dim layoutPrincipale As LayoutDocumento = compitoLayout.Result
            principale.Layout = layoutPrincipale
            Dim figli As New Dictionary(Of String, Documento)()
            Dim layoutSessioni As New Dictionary(Of String, LayoutDocumento)(StringComparer.Ordinal)
            layoutSessioni("") = layoutPrincipale
            Dim contatore As Integer() = {0}
            Await RaccogliFrameAsync("", figli, layoutSessioni, contatore, 0)
            Dim costruttore As New Costruttore(figli, WGImpostazioniBrowser.RiconosciCliccabili, WGImpostazioniBrowser.LeggiImmaginiSenzaDescrizione)
            Await Task.Run(Sub() costruttore.Esegui(principale))
            Dim risultato As New Risultato()
            risultato.Righe = costruttore.Righe
            risultato.Titolo = NomeDi(principale.Radice)
            risultato.IdModale = costruttore.IdModale
            risultato.SessioneFocus = costruttore.SessioneFocus
            risultato.IdFocus = costruttore.IdFocus
            risultato.Pieno = costruttore.Pieno
            risultato.Tabelle = costruttore.Tabelle
            risultato.Layout = layoutPrincipale
            risultato.Lingua = If(layoutPrincipale IsNot Nothing, layoutPrincipale.LinguaDocumento, "")
            risultato.AreeDisegnate = costruttore.AreeDisegnate
            Return risultato
        Catch ex As Exception
            log.ScriviLog("WGAlberoAccessibilita.CostruisciAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Async Function AlberoAsync(sessione As String, idFrame As String) As Task(Of Documento)
        Try
            Dim parametri As New JObject()
            If Not String.IsNullOrEmpty(idFrame) Then parametri("frameId") = idFrame
            Dim risposta As JObject = Await dev.InviaAsync("Accessibility.getFullAXTree", parametri, sessione, 15000)
            If risposta Is Nothing Then Return Nothing
            Dim nodi As JArray = TryCast(risposta("nodes"), JArray)
            If nodi Is Nothing OrElse nodi.Count = 0 Then Return Nothing
            Dim documento As New Documento() With {.Sessione = sessione}
            For Each nodo As JObject In nodi
                Dim id As String = nodo.Value(Of String)("nodeId")
                If id IsNot Nothing Then documento.Nodi(id) = nodo
            Next
            documento.Radice = CType(nodi(0), JObject)
            Return documento
        Catch ex As Exception
            log.ScriviLog("WGAlberoAccessibilita.AlberoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function Chiave(sessione As String, idNodo As Integer) As String
        Return If(sessione, "") & "|" & idNodo.ToString()
    End Function

    Private Async Function RaccogliFrameAsync(sessione As String, figli As Dictionary(Of String, Documento), layoutSessioni As Dictionary(Of String, LayoutDocumento), contatore As Integer(), profondita As Integer) As Task
        Try
            If profondita > 5 Then Return
            Dim alberoFrame As JObject = Await dev.InviaAsync("Page.getFrameTree", Nothing, sessione, 3000)
            Dim daVisitare As New List(Of JObject)()
            If alberoFrame IsNot Nothing AndAlso alberoFrame("frameTree") IsNot Nothing Then
                Dim radice As JObject = CType(alberoFrame("frameTree"), JObject)
                Dim pila As New Stack(Of JObject)()
                If radice("childFrames") IsNot Nothing Then
                    For Each f As JObject In radice("childFrames")
                        pila.Push(f)
                    Next
                End If
                While pila.Count > 0
                    Dim f As JObject = pila.Pop()
                    daVisitare.Add(f)
                    If f("childFrames") IsNot Nothing Then
                        For Each g As JObject In f("childFrames")
                            pila.Push(g)
                        Next
                    End If
                End While
            End If
            Dim sessioniFrame As List(Of WGDevTools.SessioneFrame) = dev.Frame.ToList()
            Dim layoutSessione As LayoutDocumento = Nothing
            layoutSessioni.TryGetValue(If(sessione, ""), layoutSessione)
            For Each f As JObject In daVisitare
                If contatore(0) >= MASSIMO_FRAME Then Return
                Dim idFrame As String = f("frame").Value(Of String)("id")
                If String.IsNullOrEmpty(idFrame) Then Continue For
                Dim remoto As WGDevTools.SessioneFrame = sessioniFrame.FirstOrDefault(Function(s) s.IdFrame = idFrame)
                If remoto IsNot Nothing Then Continue For
                contatore(0) += 1
                Dim parametri As New JObject()
                parametri("frameId") = idFrame
                Dim proprietario As JObject = Await dev.InviaAsync("DOM.getFrameOwner", parametri, sessione, 3000)
                If proprietario Is Nothing Then Continue For
                Dim idProprietario As Integer = proprietario.Value(Of Integer?)("backendNodeId").GetValueOrDefault()
                If idProprietario = 0 Then Continue For
                Dim documento As Documento = Await AlberoAsync(sessione, idFrame)
                If documento IsNot Nothing Then
                    documento.Layout = layoutSessione
                    figli(Chiave(sessione, idProprietario)) = documento
                End If
            Next
            For Each remoto As WGDevTools.SessioneFrame In sessioniFrame
                If contatore(0) >= MASSIMO_FRAME Then Return
                If remoto.SessionePadre <> sessione Then Continue For
                contatore(0) += 1
                Dim idProprietario As Integer = Await dev.ProprietarioAsync(remoto)
                If idProprietario = 0 Then Continue For
                Dim compitoAlbero As Task(Of Documento) = AlberoAsync(remoto.IdSessione, Nothing)
                Dim compitoLayout As Task(Of LayoutDocumento) = WGLayoutPagina.CatturaAsync(dev, remoto.IdSessione)
                Await Task.WhenAll(compitoAlbero, compitoLayout)
                Dim documento As Documento = compitoAlbero.Result
                layoutSessioni(remoto.IdSessione) = compitoLayout.Result
                If documento IsNot Nothing Then
                    documento.Layout = compitoLayout.Result
                    figli(Chiave(sessione, idProprietario)) = documento
                End If
                Await RaccogliFrameAsync(remoto.IdSessione, figli, layoutSessioni, contatore, profondita + 1)
            Next
        Catch ex As Exception
            log.ScriviLog("WGAlberoAccessibilita.RaccogliFrameAsync: " & ex.Message)
        End Try
    End Function

    Friend Shared Function RuoloDi(nodo As JObject) As String
        Dim r As JToken = nodo("role")
        If r Is Nothing Then Return ""
        Return If(r.Value(Of String)("value"), "")
    End Function

    Friend Shared Function NomeDi(nodo As JObject) As String
        Dim n As JToken = nodo("name")
        If n Is Nothing Then Return ""
        Return Normalizza(If(n("value")?.ToString(), ""))
    End Function

    Friend Shared Function ValoreDi(nodo As JObject) As String
        Dim v As JToken = nodo("value")
        If v Is Nothing Then Return ""
        Return If(v("value")?.ToString(), "")
    End Function

    Friend Shared Function DescrizioneDi(nodo As JObject) As String
        Dim d As JToken = nodo("description")
        If d Is Nothing Then Return ""
        Return Normalizza(If(d("value")?.ToString(), ""))
    End Function

    Friend Shared Function Proprieta(nodo As JObject, nome As String) As JToken
        Dim elenco As JArray = TryCast(nodo("properties"), JArray)
        If elenco Is Nothing Then Return Nothing
        For Each p As JObject In elenco
            If p.Value(Of String)("name") = nome Then
                Dim v As JToken = p("value")
                If v Is Nothing Then Return Nothing
                Return v("value")
            End If
        Next
        Return Nothing
    End Function

    Friend Shared Function ProprietaTesto(nodo As JObject, nome As String) As String
        Dim v As JToken = Proprieta(nodo, nome)
        If v Is Nothing Then Return ""
        Return v.ToString().ToLowerInvariant()
    End Function

    Friend Shared Function ProprietaVera(nodo As JObject, nome As String) As Boolean
        Return ProprietaTesto(nodo, nome) = "true"
    End Function

    Friend Shared Function IdDom(nodo As JObject) As Integer
        Return nodo.Value(Of Integer?)("backendDOMNodeId").GetValueOrDefault()
    End Function

    Friend Shared Function Ignorato(nodo As JObject) As Boolean
        Return nodo.Value(Of Boolean?)("ignored").GetValueOrDefault()
    End Function

    Friend Shared Function Normalizza(testo As String) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        Return Regex.Replace(testo, "[\s ​‎‏﻿]+", " ").Trim()
    End Function

    Friend Shared Function RuoloNormale(ruolo As String) As String
        Select Case ruolo
            Case "ComboBoxSelect", "ComboBoxMenuButton", "PopUpButton", "TextFieldWithComboBox", "combobox"
                Return "combobox"
            Case "DisclosureTriangle", "ToggleButton", "button"
                Return "button"
            Case "TextField", "textbox"
                Return "textbox"
            Case "image", "img", "Image", "graphics-symbol", "ImageMap", "SvgRoot"
                Return "img"
            Case "MenuListOption", "ListBoxOption", "option"
                Return "option"
            Case "Iframe", "IframePresentational"
                Return "frame"
            Case "Canvas", "canvas"
                Return "canvas"
            Case "Video", "video"
                Return "video"
            Case "Audio", "audio"
                Return "audio"
        End Select
        Return ruolo
    End Function

    Friend Shared ReadOnly RuoliWidget As New HashSet(Of String)(StringComparer.Ordinal) From {
        "link", "button", "checkbox", "radio", "switch", "textbox", "searchbox", "combobox", "slider", "spinbutton",
        "option", "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "treeitem", "progressbar", "meter", "listbox"
    }

    Private Shared ReadOnly RuoliInLinea As New HashSet(Of String)(StringComparer.Ordinal) From {
        "strong", "emphasis", "code", "mark", "time", "subscript", "superscript", "deletion", "insertion",
        "Abbr", "Ruby", "RubyAnnotation", "LabelText", "none", "presentation", "Label", "comment", "suggestion"
    }

    Private Shared ReadOnly RuoliGenerici As New HashSet(Of String)(StringComparer.Ordinal) From {
        "generic", "none", "presentation", "Section", "GenericContainer", "Div", "Span", "Pre", "group", "LayoutTable", "LayoutTableRow", "LayoutTableCell", "paragraph", ""
    }

    Private Shared ReadOnly RuoliStrutturali As New HashSet(Of String)(StringComparer.Ordinal) From {
        "list", "DescriptionList", "table", "grid", "treegrid", "row", "banner", "navigation", "main", "complementary", "contentinfo",
        "search", "form", "region", "dialog", "alertdialog", "tablist", "menu", "menubar", "tree", "toolbar", "radiogroup", "heading", "frame"
    }

    Private Shared ReadOnly DescrizioneRegione As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"banner", "intestazione della pagina"}, {"navigation", "navigazione"}, {"main", "contenuto principale"},
        {"complementary", "contenuto complementare"}, {"contentinfo", "informazioni sul contenuto"},
        {"search", "ricerca"}, {"form", "modulo"}, {"region", "regione"}, {"application", "applicazione"}
    }

    Private Shared ReadOnly DescrizioneGruppo As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"group", "gruppo"}, {"radiogroup", "gruppo di opzioni"}, {"toolbar", "barra degli strumenti"},
        {"tablist", "elenco schede"}, {"menu", "menu"}, {"menubar", "barra dei menu"}, {"tree", "struttura ad albero"},
        {"grid", "griglia"}, {"treegrid", "griglia ad albero"}
    }

    Friend Shared Function StatiDi(nodo As JObject, ruolo As String) As String
        Dim stati As New List(Of String)()
        Try
            Dim spuntato As String = ProprietaTesto(nodo, "checked")
            If ruolo = "switch" Then
                stati.Add(If(spuntato = "true", "attivato", "disattivato"))
            ElseIf ruolo = "checkbox" OrElse ruolo = "radio" OrElse ruolo = "menuitemcheckbox" OrElse ruolo = "menuitemradio" Then
                stati.Add(If(spuntato = "true", "selezionato", If(spuntato = "mixed", "parzialmente selezionato", "non selezionato")))
            End If
            Dim premuto As String = ProprietaTesto(nodo, "pressed")
            If premuto = "true" Then
                stati.Add("premuto")
            ElseIf premuto = "false" Then
                stati.Add("non premuto")
            ElseIf premuto = "mixed" Then
                stati.Add("parzialmente premuto")
            End If
            Dim espanso As JToken = Proprieta(nodo, "expanded")
            If espanso IsNot Nothing AndAlso ruolo <> "combobox" Then stati.Add(If(espanso.ToString().ToLowerInvariant() = "true", "espanso", "compresso"))
            If ruolo = "combobox" AndAlso ProprietaTesto(nodo, "editable") <> "" AndAlso espanso IsNot Nothing AndAlso espanso.ToString().ToLowerInvariant() = "true" Then stati.Add("suggerimenti aperti")
            If (ruolo = "tab" OrElse ruolo = "option" OrElse ruolo = "treeitem" OrElse ruolo = "row" OrElse ruolo = "gridcell") AndAlso ProprietaVera(nodo, "selected") Then stati.Add("selezionato")
            If ProprietaVera(nodo, "disabled") Then stati.Add("non disponibile")
            If ProprietaVera(nodo, "required") Then stati.Add("obbligatorio")
            Dim nonValido As String = ProprietaTesto(nodo, "invalid")
            If nonValido <> "" AndAlso nonValido <> "false" Then stati.Add("non valido")
            If (ruolo = "textbox" OrElse ruolo = "searchbox" OrElse ruolo = "combobox" OrElse ruolo = "spinbutton") AndAlso ProprietaVera(nodo, "readonly") Then stati.Add("sola lettura")
            Dim popup As String = ProprietaTesto(nodo, "hasPopup")
            If popup <> "" AndAlso popup <> "false" AndAlso ruolo <> "combobox" Then stati.Add(If(popup = "dialog", "apre una finestra", "apre un menu"))
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.StatiDi: " & ex.Message)
        End Try
        Return String.Join(", ", stati)
    End Function

    Friend Shared Function NomeDaIndirizzo(indirizzo As String) As String
        Try
            If String.IsNullOrWhiteSpace(indirizzo) Then Return ""
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(indirizzo, UriKind.Absolute, uri) Then
                Dim pulito As String = indirizzo.Split("?"c, "#"c)(0)
                Dim pezziRelativi As String() = pulito.Split(New Char() {"/"c}, StringSplitOptions.RemoveEmptyEntries)
                If pezziRelativi.Length = 0 Then Return ""
                Return PulisciNomeFile(Uri.UnescapeDataString(pezziRelativi(pezziRelativi.Length - 1)))
            End If
            If uri.Scheme = "javascript" OrElse uri.Scheme = "data" OrElse uri.Scheme = "blob" Then Return ""
            Dim pezzi As String() = uri.AbsolutePath.Split(New Char() {"/"c}, StringSplitOptions.RemoveEmptyEntries)
            If pezzi.Length = 0 Then Return uri.Host
            Return PulisciNomeFile(Uri.UnescapeDataString(pezzi(pezzi.Length - 1)))
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.NomeDaIndirizzo: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Shared Function SorgenteSicura(indirizzo As String) As String
        Try
            If String.IsNullOrEmpty(indirizzo) Then Return ""
            If indirizzo.StartsWith("data:", StringComparison.OrdinalIgnoreCase) OrElse indirizzo.Length > 2000 Then Return ""
            Return indirizzo
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.SorgenteSicura: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function PulisciNomeFile(nome As String) As String
        Try
            Dim risultato As String = Regex.Replace(nome, "\.[a-z0-9]{2,5}$", "", RegexOptions.IgnoreCase)
            risultato = Regex.Replace(risultato, "[-_+.]+", " ").Trim()
            If risultato.Length > 60 Then Return ""
            Dim lettere As Integer = risultato.Count(Function(c) Char.IsLetter(c))
            If lettere < 3 Then Return ""
            If Regex.IsMatch(risultato, "^[0-9a-f ]{12,}$", RegexOptions.IgnoreCase) Then Return ""
            Return risultato
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.PulisciNomeFile: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Shared Function RigaDaNodo(nodo As JObject, ruolo As String, sessione As String) As RigaVirtuale
        Dim r As New RigaVirtuale()
        Try
            r.DaAlbero = True
            r.Sessione = sessione
            r.IdElemento = IdDom(nodo)
            r.Ruolo = ruolo
            r.Testo = NomeDi(nodo)
            r.Stato = StatiDi(nodo, ruolo)
            Dim livello As JToken = Proprieta(nodo, "level")
            If livello IsNot Nothing Then
                Dim l As Integer
                If Integer.TryParse(livello.ToString(), l) Then r.Livello = l
            End If
            Dim modifica As String = ProprietaTesto(nodo, "editable")
            Dim valore As String = ValoreDi(nodo)
            If ruolo = "textbox" OrElse ruolo = "searchbox" OrElse (ruolo = "combobox" AndAlso modifica <> "") Then
                r.Modificabile = True
                r.Multiriga = ProprietaVera(nodo, "multiline") AndAlso ruolo = "textbox"
                r.SolaLettura = ProprietaVera(nodo, "readonly")
                If valore.Length > 0 AndAlso valore.Trim("•"c).Length = 0 Then
                    r.Password = True
                    r.LunghezzaPassword = valore.Length
                    valore = ""
                End If
            End If
            If ruolo = "slider" OrElse ruolo = "spinbutton" OrElse ruolo = "progressbar" OrElse ruolo = "meter" Then
                Dim testoValore As String = ProprietaTesto(nodo, "valuetext")
                If testoValore <> "" Then valore = Proprieta(nodo, "valuetext").ToString()
            End If
            If ruolo = "link" Then
                Dim indirizzo As JToken = Proprieta(nodo, "url")
                If indirizzo IsNot Nothing Then r.Indirizzo = indirizzo.ToString()
            End If
            If Normalizza(valore) <> r.Testo Then r.Valore = If(valore.Length > 2000, valore.Substring(0, 2000), valore)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.RigaDaNodo: " & ex.Message)
        End Try
        Return r
    End Function

    Public Class DescrizioneNodo
        Public Property Riga As RigaVirtuale = Nothing
        Public Property Contesti As New List(Of Tuple(Of String, String))()
        Public Property IdDiscendenteAttivo As Integer = 0
    End Class

    Private Shared ReadOnly DescrizioneContenitore As New Dictionary(Of String, String)(StringComparer.Ordinal) From {
        {"dialog", "finestra di dialogo"}, {"alertdialog", "finestra di avviso"}, {"listbox", "elenco"},
        {"menu", "menu"}, {"menubar", "barra dei menu"}, {"tablist", "elenco schede"}, {"tree", "struttura ad albero"},
        {"grid", "griglia"}, {"treegrid", "griglia ad albero"}, {"table", "tabella"}, {"radiogroup", "gruppo di opzioni"},
        {"toolbar", "barra degli strumenti"}, {"navigation", "navigazione"}, {"main", "contenuto principale"},
        {"banner", "intestazione della pagina"}, {"complementary", "contenuto complementare"},
        {"contentinfo", "informazioni sul contenuto"}, {"search", "ricerca"}, {"form", "modulo"}, {"region", "regione"},
        {"group", "gruppo"}, {"tabpanel", "scheda"}, {"application", "applicazione"}
    }

    Private Shared ReadOnly ContenitoriSoloConNome As New HashSet(Of String)(StringComparer.Ordinal) From {
        "form", "region", "group", "tabpanel", "application"
    }

    Private Const MASSIMO_FIGLI_PER_CONTESTO As Integer = 200

    Friend Shared Function NodoCorrelato(nodo As JObject, nome As String) As Integer
        Try
            Dim elenco As JArray = TryCast(nodo("properties"), JArray)
            If elenco Is Nothing Then Return 0
            For Each p As JObject In elenco
                If p.Value(Of String)("name") <> nome Then Continue For
                Dim valore As JObject = TryCast(p("value"), JObject)
                If valore Is Nothing Then Return 0
                Dim correlati As JArray = TryCast(valore("relatedNodes"), JArray)
                If correlati Is Nothing OrElse correlati.Count = 0 Then Return 0
                Return correlati(0).Value(Of Integer?)("backendDOMNodeId").GetValueOrDefault()
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.NodoCorrelato: " & ex.Message)
        End Try
        Return 0
    End Function

    Private Async Function NodiParzialiAsync(sessione As String, idNodo As Integer, parenti As Boolean) As Task(Of JArray)
        Try
            Dim parametri As New JObject()
            parametri("backendNodeId") = idNodo
            parametri("fetchRelatives") = parenti
            Dim risposta As JObject = Await dev.InviaAsync("Accessibility.getPartialAXTree", parametri, sessione, 2500)
            If risposta Is Nothing Then Return Nothing
            Return TryCast(risposta("nodes"), JArray)
        Catch ex As Exception
            log.ScriviLog("WGAlberoAccessibilita.NodiParzialiAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Async Function AttributiAsync(sessione As String, idNodo As Integer) As Task(Of Dictionary(Of String, String))
        Dim risultato As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim parametri As New JObject()
            parametri("backendNodeId") = idNodo
            Dim descritto As JObject = Await dev.InviaAsync("DOM.describeNode", parametri, sessione, 2000)
            If descritto Is Nothing OrElse descritto("node") Is Nothing Then Return risultato
            Dim attributi As JArray = TryCast(descritto("node")("attributes"), JArray)
            If attributi Is Nothing Then Return risultato
            For i As Integer = 0 To attributi.Count - 2 Step 2
                risultato(attributi(i).ToString()) = attributi(i + 1).ToString()
            Next
        Catch ex As Exception
            log.ScriviLog("WGAlberoAccessibilita.AttributiAsync: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Shared Function GenitoreDi(nodo As JObject, perId As Dictionary(Of String, JObject)) As JObject
        Try
            Dim idGenitore As String = nodo.Value(Of String)("parentId")
            Dim genitore As JObject = Nothing
            If Not String.IsNullOrEmpty(idGenitore) AndAlso perId.TryGetValue(idGenitore, genitore) Then Return genitore
            Dim idProprio As String = nodo.Value(Of String)("nodeId")
            If String.IsNullOrEmpty(idProprio) Then Return Nothing
            For Each candidato As JObject In perId.Values
                Dim figli As JArray = TryCast(candidato("childIds"), JArray)
                If figli Is Nothing Then Continue For
                For Each f As JToken In figli
                    If f.ToString() = idProprio Then Return candidato
                Next
            Next
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.GenitoreDi: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Shared Function TestoDeiDiscendenti(nodo As JObject, perId As Dictionary(Of String, JObject)) As String
        Dim testo As New StringBuilder()
        Try
            Dim pila As New Stack(Of Tuple(Of JObject, Integer))()
            pila.Push(Tuple.Create(nodo, 0))
            Dim giri As Integer = 0
            While pila.Count > 0 AndAlso giri < 400 AndAlso testo.Length < 600
                giri += 1
                Dim elemento As Tuple(Of JObject, Integer) = pila.Pop()
                Dim n As JObject = elemento.Item1
                If elemento.Item2 > 0 Then
                    If RuoloDi(n) = "StaticText" Then
                        testo.Append(" ").Append(If(n("name")?("value")?.ToString(), ""))
                        Continue While
                    End If
                    If Not Ignorato(n) AndAlso RuoloNormale(RuoloDi(n)) = "img" Then
                        testo.Append(" ").Append(NomeDi(n))
                        Continue While
                    End If
                End If
                If elemento.Item2 >= 6 Then Continue While
                Dim figli As JArray = TryCast(n("childIds"), JArray)
                If figli Is Nothing Then Continue While
                For i As Integer = figli.Count - 1 To 0 Step -1
                    Dim figlio As JObject = Nothing
                    If perId.TryGetValue(figli(i).ToString(), figlio) Then pila.Push(Tuple.Create(figlio, elemento.Item2 + 1))
                Next
            End While
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.TestoDeiDiscendenti: " & ex.Message)
        End Try
        Return Normalizza(testo.ToString())
    End Function

    Private Shared Function ContestiDi(nodo As JObject, perId As Dictionary(Of String, JObject), sessione As String) As List(Of Tuple(Of String, String))
        Dim contesti As New List(Of Tuple(Of String, String))()
        Try
            Dim corrente As JObject = GenitoreDi(nodo, perId)
            Dim giri As Integer = 0
            While corrente IsNot Nothing AndAlso giri < 200
                giri += 1
                If Not Ignorato(corrente) Then
                    Dim ruolo As String = RuoloNormale(RuoloDi(corrente))
                    Dim descrizione As String = Nothing
                    If DescrizioneContenitore.TryGetValue(ruolo, descrizione) Then
                        Dim nome As String = NomeDi(corrente)
                        If nome.Length > 120 Then nome = nome.Substring(0, 120)
                        If Not (ContenitoriSoloConNome.Contains(ruolo) AndAlso nome = "") Then
                            Dim testoContesto As String = descrizione & If(nome <> "", " " & nome, "")
                            If ruolo = "dialog" OrElse ruolo = "alertdialog" Then
                                Dim spiegazione As String = DescrizioneDi(corrente)
                                If spiegazione.Length > 300 Then spiegazione = spiegazione.Substring(0, 300)
                                If spiegazione <> "" AndAlso Not String.Equals(spiegazione, nome, StringComparison.OrdinalIgnoreCase) Then testoContesto &= ". " & spiegazione
                            End If
                            contesti.Add(Tuple.Create(If(sessione, "") & "|" & IdDom(corrente).ToString() & "|" & ruolo & "|" & nome, testoContesto))
                        End If
                    End If
                End If
                corrente = GenitoreDi(corrente, perId)
            End While
            contesti.Reverse()
            If contesti.Count > 6 Then contesti = contesti.Skip(contesti.Count - 6).ToList()
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGAlberoAccessibilita.ContestiDi: " & ex.Message)
        End Try
        Return contesti
    End Function

    Public Async Function DescriviNodoAsync(sessione As String, idNodo As Integer, conContesto As Boolean) As Task(Of DescrizioneNodo)
        Try
            If dev Is Nothing OrElse Not dev.ECollegato OrElse idNodo <= 0 Then Return Nothing
            Dim nodi As JArray = Await NodiParzialiAsync(sessione, idNodo, False)
            If nodi Is Nothing OrElse nodi.Count = 0 Then Return Nothing
            Dim ispezionato As JObject = nodi.OfType(Of JObject)().FirstOrDefault(Function(n) IdDom(n) = idNodo)
            If ispezionato Is Nothing Then ispezionato = TryCast(nodi(0), JObject)
            If ispezionato Is Nothing OrElse Ignorato(ispezionato) Then Return Nothing
            Dim perId As New Dictionary(Of String, JObject)(StringComparer.Ordinal)
            Dim figliDiretti As JArray = TryCast(ispezionato("childIds"), JArray)
            Dim numeroFigli As Integer = If(figliDiretti Is Nothing, 0, figliDiretti.Count)
            Dim servonoParenti As Boolean = (conContesto OrElse NomeDi(ispezionato) = "") AndAlso numeroFigli <= MASSIMO_FIGLI_PER_CONTESTO
            If servonoParenti Then
                Dim parenti As JArray = Await NodiParzialiAsync(sessione, idNodo, True)
                If parenti IsNot Nothing Then
                    For Each elemento As JToken In parenti
                        Dim n As JObject = TryCast(elemento, JObject)
                        If n Is Nothing Then Continue For
                        Dim chiaveNodo As String = n.Value(Of String)("nodeId")
                        If chiaveNodo IsNot Nothing Then perId(chiaveNodo) = n
                        If IdDom(n) = idNodo AndAlso Not Ignorato(n) Then ispezionato = n
                    Next
                End If
            End If
            Dim idAx As String = ispezionato.Value(Of String)("nodeId")
            If Not String.IsNullOrEmpty(idAx) AndAlso Not perId.ContainsKey(idAx) Then perId(idAx) = ispezionato
            Dim ruoloChrome As String = RuoloDi(ispezionato)
            Dim ruolo As String = RuoloNormale(ruoloChrome)
            Dim riga As RigaVirtuale = RigaDaNodo(ispezionato, ruolo, sessione)
            If ruolo = "heading" AndAlso riga.Livello <= 0 Then riga.Livello = 2
            Dim modificabile As String = ProprietaTesto(ispezionato, "editable")
            If Not riga.Modificabile AndAlso modificabile <> "" AndAlso (ProprietaVera(ispezionato, "focusable") OrElse ProprietaVera(ispezionato, "focused")) AndAlso Not RuoliWidget.Contains(ruolo) Then
                riga.Modificabile = True
                riga.Multiriga = ProprietaVera(ispezionato, "multiline") OrElse modificabile = "richtext"
                riga.SolaLettura = ProprietaVera(ispezionato, "readonly")
            End If
            If riga.Testo = "" AndAlso Not riga.Modificabile Then
                Dim testoFigli As String = TestoDeiDiscendenti(ispezionato, perId)
                If testoFigli.Length > 400 Then testoFigli = testoFigli.Substring(0, 400)
                If RuoliGenerici.Contains(ruoloChrome) AndAlso ProprietaVera(ispezionato, "focusable") Then
                    riga.Ruolo = "cliccabile"
                    riga.Stato = StatiDi(ispezionato, "cliccabile")
                End If
                If testoFigli <> "" Then riga.Testo = testoFigli
                If riga.Testo = "" AndAlso ruolo = "link" Then riga.Testo = NomeDaIndirizzo(riga.Indirizzo)
            End If
            If riga.Modificabile Then
                Dim attributi As Dictionary(Of String, String) = Await AttributiAsync(sessione, idNodo)
                Dim tipo As String = Nothing
                If attributi.TryGetValue("type", tipo) AndAlso String.Equals(tipo, "password", StringComparison.OrdinalIgnoreCase) AndAlso Not riga.Password Then
                    riga.Password = True
                    riga.LunghezzaPassword = riga.Valore.Length
                    riga.Valore = ""
                End If
                If riga.Testo = "" Then
                    Dim nomeCampo As String = ""
                    If Not attributi.TryGetValue("name", nomeCampo) OrElse String.IsNullOrWhiteSpace(nomeCampo) Then
                        If Not attributi.TryGetValue("id", nomeCampo) Then nomeCampo = ""
                    End If
                    nomeCampo = Normalizza(Regex.Replace(If(nomeCampo, ""), "[-_.\[\]]+", " "))
                    If nomeCampo.Length > 40 OrElse Regex.IsMatch(nomeCampo, "^[0-9a-f ]{8,}$", RegexOptions.IgnoreCase) Then nomeCampo = ""
                    If nomeCampo = "" AndAlso riga.Password Then nomeCampo = "password"
                    riga.Testo = nomeCampo
                End If
            End If
            Dim descrizioneNodo As String = DescrizioneDi(ispezionato)
            If descrizioneNodo <> "" AndAlso Not String.Equals(descrizioneNodo, riga.Testo, StringComparison.OrdinalIgnoreCase) Then riga.Descrizione = descrizioneNodo
            RigaVirtuale.Completa(riga)
            Dim risultato As New DescrizioneNodo()
            risultato.Riga = riga
            risultato.IdDiscendenteAttivo = NodoCorrelato(ispezionato, "activedescendant")
            If conContesto AndAlso perId.Count > 1 Then risultato.Contesti = ContestiDi(ispezionato, perId, sessione)
            Return risultato
        Catch ex As Exception
            log.ScriviLog("WGAlberoAccessibilita.DescriviNodoAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Class AnalisiSottoalbero
        Public Widget As Integer = 0
        Public Intestazioni As Integer = 0
        Public Strutture As Integer = 0
        Public Nodi As Integer = 0
        Public ReadOnly Testo As New StringBuilder()
        Public NomeImmagine As String = ""
    End Class

    Private Class ContestoCella
        Public Tabella As TabellaVirtuale
        Public Cella As CellaVirtuale
        Public Prima As Boolean = True
    End Class

    Private Class Costruttore
        Public ReadOnly Righe As New List(Of RigaVirtuale)()
        Public ReadOnly Tabelle As New Dictionary(Of String, TabellaVirtuale)(StringComparer.Ordinal)
        Public IdModale As Integer = 0
        Public SessioneFocus As String = ""
        Public IdFocus As Integer = 0
        Public Pieno As Boolean = False
        Public AreeDisegnate As Boolean = False

        Private ReadOnly log As New Logger()
        Private ReadOnly documentiDeiFrame As Dictionary(Of String, Documento)
        Private ReadOnly riconosciCliccabili As Boolean
        Private ReadOnly immaginiSenzaNome As Boolean
        Private documento As Documento
        Private ReadOnly linea As New StringBuilder()
        Private lineaId As Integer = 0
        Private link As Integer = 0
        Private linkRuolo As String = ""
        Private linkIndirizzo As String = ""
        Private dialogo As Integer = 0
        Private visitati As Integer = 0
        Private dentroCliccabile As Integer = 0
        Private ReadOnly celle As New Stack(Of ContestoCella)()

        Private attesaRegione As String = ""
        Private attesaLista As Integer = 0
        Private attesaElemento As Boolean = False
        Private attesaTabella As String = ""
        Private attesaArticolo As Boolean = False
        Private attesaGruppo As String = ""
        Private attesaCitazione As Boolean = False
        Private attesaParagrafo As Boolean = False

        Private Const MASSIMO_RIGHE As Integer = 25000
        Private Const MASSIMO_NODI As Integer = 400000
        Private Const LUNGHEZZA_MASSIMA_RIGA As Integer = 600

        Public Sub New(documentiFigli As Dictionary(Of String, Documento), cliccabili As Boolean, immagini As Boolean)
            documentiDeiFrame = documentiFigli
            riconosciCliccabili = cliccabili
            immaginiSenzaNome = immagini
        End Sub

        Public Sub Esegui(principale As Documento)
            Try
                documento = principale
                Dim radice As JObject = principale.Radice
                Dim modale As JObject = TrovaModale(principale)
                If modale IsNot Nothing Then
                    IdModale = IdDom(modale)
                    attesaRegione = "finestra di dialogo" & If(NomeDi(modale) <> "", " " & NomeDi(modale), "")
                    Visita(modale, 0)
                Else
                    Visita(radice, 0)
                End If
                ChiudiLinea()
                AggiungiAreeNonEsposte(principale, modale)
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Costruttore.Esegui: " & ex.ToString())
            End Try
        End Sub

        Private Sub AggiungiAreeNonEsposte(principale As Documento, modale As JObject)
            Try
                Dim layout As LayoutDocumento = principale.Layout
                If layout Is Nothing OrElse Pieno Then Return
                Dim presenti As New HashSet(Of Integer)()
                For Each r As RigaVirtuale In Righe
                    If r.Sessione = "" AndAlso r.IdElemento > 0 Then presenti.Add(r.IdElemento)
                Next
                Dim idModale As Integer = If(modale IsNot Nothing, IdDom(modale), 0)
                For Each info As InfoNodoLayout In layout.Nodi
                    If info.Tag <> "canvas" OrElse Not info.HaLayout OrElse info.IdBackend <= 0 Then Continue For
                    If info.Larghezza < 150 OrElse info.Altezza < 100 Then Continue For
                    If presenti.Contains(info.IdBackend) Then Continue For
                    If idModale > 0 AndAlso Not layout.EAntenato(idModale, info.IdBackend) Then Continue For
                    If String.Equals(info.Attributo("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) Then Continue For
                    Dim posizione As Integer = Righe.Count
                    For i As Integer = 0 To Righe.Count - 1
                        Dim ordine As Integer = Righe(i).OrdineDocumento
                        If Righe(i).Sessione = "" AndAlso ordine >= 0 AndAlso ordine > info.IndiceGlobale Then
                            posizione = i
                            Exit For
                        End If
                    Next
                    Dim nome As String = Normalizza(If(info.Attributo("title"), ""))
                    Dim riga As New RigaVirtuale() With {
                        .DaAlbero = True, .Sessione = "", .Ruolo = "canvas", .IdElemento = info.IdBackend,
                        .Testo = If(nome <> "", nome, "contenuto grafico senza testo"), .OrdineDocumento = info.IndiceGlobale,
                        .Lingua = layout.LinguaDi(info.IdBackend)
                    }
                    RigaVirtuale.Completa(riga)
                    Righe.Insert(posizione, riga)
                    presenti.Add(info.IdBackend)
                    AreeDisegnate = True
                    For Each tabella As TabellaVirtuale In Tabelle.Values
                        For Each cella As CellaVirtuale In tabella.Celle
                            If cella.IndicePrimaRiga >= posizione Then cella.IndicePrimaRiga += 1
                        Next
                    Next
                Next
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.AggiungiAreeNonEsposte: " & ex.Message)
            End Try
        End Sub

        Private Function TrovaModale(doc As Documento) As JObject
            Dim trovato As JObject = Nothing
            Try
                For Each nodo As JObject In doc.Nodi.Values
                    If Ignorato(nodo) Then Continue For
                    Dim ruolo As String = RuoloDi(nodo)
                    If (ruolo = "dialog" OrElse ruolo = "alertdialog") AndAlso ProprietaVera(nodo, "modal") Then trovato = nodo
                Next
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.TrovaModale: " & ex.Message)
            End Try
            Return trovato
        End Function

        Private Function Figli(nodo As JObject) As List(Of JObject)
            Dim risultato As New List(Of JObject)()
            Dim elenco As JArray = TryCast(nodo("childIds"), JArray)
            If elenco Is Nothing Then Return risultato
            For Each id As JToken In elenco
                Dim figlio As JObject = Nothing
                If documento.Nodi.TryGetValue(id.ToString(), figlio) Then risultato.Add(figlio)
            Next
            Return risultato
        End Function

        Private Function InfoDi(nodo As JObject) As InfoNodoLayout
            If documento.Layout Is Nothing Then Return Nothing
            Return documento.Layout.Info(IdDom(nodo))
        End Function

        Private Sub VisitaFigli(nodo As JObject, elementoPadre As Integer)
            For Each figlio As JObject In Figli(nodo)
                If Pieno Then Return
                Visita(figlio, elementoPadre)
            Next
        End Sub

        Private Sub Emetti(r As RigaVirtuale)
            Try
                If Righe.Count >= MASSIMO_RIGHE Then
                    Pieno = True
                    Return
                End If
                r.DaAlbero = True
                r.Sessione = documento.Sessione
                If attesaRegione <> "" Then r.Regione = attesaRegione
                If attesaLista > 0 Then r.ElementiLista = attesaLista
                If attesaElemento Then r.InizioElementoLista = True
                If attesaTabella <> "" Then r.Tabella = attesaTabella
                If attesaArticolo Then r.InizioArticolo = True
                If attesaGruppo <> "" Then r.Gruppo = attesaGruppo
                If attesaCitazione Then r.InizioCitazione = True
                If attesaParagrafo Then r.InizioParagrafo = True
                attesaRegione = "" : attesaLista = 0 : attesaElemento = False : attesaTabella = ""
                attesaArticolo = False : attesaGruppo = "" : attesaCitazione = False : attesaParagrafo = False
                If dialogo <> 0 Then r.IdDialogo = dialogo
                If link <> 0 AndAlso r.IdAttivazione = 0 AndAlso r.IdElemento <> link Then
                    r.IdAttivazione = link
                    r.RuoloAttivazione = linkRuolo
                    If r.Indirizzo = "" Then r.Indirizzo = linkIndirizzo
                End If
                If documento.Layout IsNot Nothing Then
                    r.Lingua = documento.Layout.LinguaDi(r.IdElemento)
                    If documento.Sessione = "" Then r.OrdineDocumento = documento.Layout.OrdineDocumento(r.IdElemento)
                End If
                If celle.Count > 0 Then
                    Dim contesto As ContestoCella = celle.Peek()
                    r.IdTabella = contesto.Tabella.Id
                    r.RigaTabella = contesto.Cella.Riga
                    r.ColonnaTabella = contesto.Cella.Colonna
                    If contesto.Prima Then
                        r.InizioCella = True
                        contesto.Prima = False
                        contesto.Cella.IndicePrimaRiga = Righe.Count
                    End If
                    Dim parte As String = If(r.Ruolo = "text", r.Testo, r.CostruisciAnnuncio(False))
                    If r.Ruolo <> "text" Then
                        RigaVirtuale.Completa(r)
                        parte = r.CostruisciAnnuncio(False)
                    End If
                    If Not String.IsNullOrWhiteSpace(parte) Then contesto.Cella.Testo = (contesto.Cella.Testo & " " & parte).Trim()
                End If
                RigaVirtuale.Completa(r)
                Righe.Add(r)
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Emetti: " & ex.Message)
            End Try
        End Sub

        Private Sub ChiudiLinea()
            Try
                If linea.Length = 0 Then Return
                Dim testo As String = Normalizza(linea.ToString())
                linea.Clear()
                If testo = "" Then Return
                For Each pezzo As String In Spezza(testo)
                    Dim r As New RigaVirtuale() With {.Ruolo = "text", .Testo = pezzo, .IdElemento = lineaId}
                    Emetti(r)
                Next
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.ChiudiLinea: " & ex.Message)
            End Try
        End Sub

        Private Shared Function Spezza(testo As String) As List(Of String)
            Dim pezzi As New List(Of String)()
            Dim resto As String = testo
            While resto.Length > LUNGHEZZA_MASSIMA_RIGA
                Dim finestra As String = resto.Substring(0, LUNGHEZZA_MASSIMA_RIGA)
                Dim taglio As Integer = -1
                For i As Integer = finestra.Length - 2 To LUNGHEZZA_MASSIMA_RIGA \ 3 Step -1
                    If ".!?;:".IndexOf(finestra(i)) >= 0 AndAlso finestra(i + 1) = " "c Then
                        taglio = i + 1
                        Exit For
                    End If
                Next
                If taglio < 0 Then taglio = finestra.LastIndexOf(" "c)
                If taglio < LUNGHEZZA_MASSIMA_RIGA \ 3 Then taglio = LUNGHEZZA_MASSIMA_RIGA
                pezzi.Add(resto.Substring(0, taglio).Trim())
                resto = resto.Substring(taglio).Trim()
            End While
            If resto <> "" Then pezzi.Add(resto)
            Return pezzi
        End Function

        Private Sub AggiungiTesto(testo As String, elementoPadre As Integer)
            If String.IsNullOrEmpty(testo) Then Return
            If linea.Length = 0 Then
                If testo.Trim().Length = 0 Then Return
                lineaId = elementoPadre
            End If
            linea.Append(testo)
        End Sub

        Private Sub Visita(nodo As JObject, elementoPadre As Integer)
            Try
                visitati += 1
                If visitati > MASSIMO_NODI Then
                    Pieno = True
                    Return
                End If
                Dim idNodo As Integer = IdDom(nodo)
                Dim ruoloChrome As String = RuoloDi(nodo)
                If ProprietaVera(nodo, "focused") AndAlso ruoloChrome <> "RootWebArea" Then
                    SessioneFocus = documento.Sessione
                    IdFocus = idNodo
                End If
                Dim ruolo As String = RuoloNormale(ruoloChrome)
                Dim padreFigli As Integer = If(idNodo > 0 AndAlso ruoloChrome <> "StaticText", idNodo, elementoPadre)
                Dim info As InfoNodoLayout = InfoDi(nodo)

                If Ignorato(nodo) Then
                    If GestisciCliccabile(nodo, info, ruolo, padreFigli) Then Return
                    Dim bloccoIgnorato As Boolean = info IsNot Nothing AndAlso info.EBlocco
                    If bloccoIgnorato Then ChiudiLinea()
                    VisitaFigli(nodo, padreFigli)
                    If bloccoIgnorato Then ChiudiLinea()
                    Return
                End If

                Select Case ruoloChrome
                    Case "InlineTextBox", "ListMarker", "MenuListPopup"
                        Return
                    Case "StaticText"
                        Dim testoStatico As String = If(nodo("name")?("value")?.ToString(), "")
                        If idNodo = 0 AndAlso testoStatico.Trim().Trim("•"c, "◦"c, "▪"c, "▫"c, "‣"c, "⁃"c, "·"c, "-"c, "*"c).Length = 0 Then Return
                        AggiungiTesto(testoStatico, elementoPadre)
                        Return
                    Case "LineBreak"
                        ChiudiLinea()
                        Return
                    Case "RootWebArea", "WebArea"
                        VisitaFigli(nodo, padreFigli)
                        Return
                End Select

                If ruolo = "frame" Then
                    ChiudiLinea()
                    Frame(nodo)
                    Return
                End If
                If ruoloChrome = "math" OrElse ruoloChrome = "MathMLMath" Then
                    ' Una formula è una riga sola: il cursore la sostituisce con il parlato ricavato dal MathML.
                    ChiudiLinea()
                    Dim parlatoFormula As String = NomeDi(nodo)
                    If parlatoFormula = "" Then parlatoFormula = Normalizza(TestoFormula(nodo, 0))
                    Emetti(New RigaVirtuale() With {.Ruolo = "math", .Testo = parlatoFormula, .IdElemento = idNodo})
                    ChiudiLinea()
                    Return
                End If
                If ruolo = "img" Then
                    Immagine(nodo, info)
                    Return
                End If
                If ruolo = "canvas" Then
                    AreaDisegnata(nodo, info, padreFigli)
                    Return
                End If
                If ruolo = "video" OrElse ruolo = "audio" Then
                    ChiudiLinea()
                    Dim nomeMedia As String = NomeDi(nodo)
                    Dim sorgenteMedia As String = SorgenteSicura(If(info IsNot Nothing, If(info.Attributo("src"), ""), ""))
                    Emetti(New RigaVirtuale() With {.Ruolo = ruolo, .Testo = If(nomeMedia <> "", nomeMedia, NomeDaIndirizzo(sorgenteMedia)), .IdElemento = idNodo, .Sorgente = sorgenteMedia})
                    VisitaFigli(nodo, padreFigli)
                    ChiudiLinea()
                    Return
                End If
                If ruolo = "separator" OrElse ruolo = "scrollbar" Then
                    ChiudiLinea()
                    Return
                End If
                If RuoliWidget.Contains(ruolo) Then
                    ChiudiLinea()
                    Widget(nodo, ruolo, padreFigli, info)
                    Return
                End If
                If ruolo = "heading" Then
                    ChiudiLinea()
                    Intestazione(nodo)
                    Return
                End If
                If RuoliInLinea.Contains(ruoloChrome) Then
                    VisitaFigli(nodo, elementoPadre)
                    Return
                End If
                If GestisciCliccabile(nodo, info, ruolo, padreFigli) Then Return

                Dim blocco As Boolean = True
                If info IsNot Nothing AndAlso info.HaLayout AndAlso Not info.EBlocco AndAlso RuoliGenerici.Contains(ruoloChrome) AndAlso ruoloChrome <> "paragraph" Then blocco = False
                If blocco Then ChiudiLinea()
                Dim impostati As New List(Of String)()
                Dim nome As String = NomeDi(nodo)
                If DescrizioneRegione.ContainsKey(ruolo) Then
                    attesaRegione = DescrizioneRegione(ruolo) & If(nome <> "", " " & nome, "")
                    impostati.Add("rg")
                ElseIf ruolo = "dialog" OrElse ruolo = "alertdialog" Then
                    If IdModale <> idNodo Then
                        attesaRegione = "finestra di dialogo" & If(nome <> "", " " & nome, "")
                        impostati.Add("rg")
                    End If
                ElseIf ruolo = "list" OrElse ruolo = "DescriptionList" Then
                    Dim elementi As Integer = ContaDiscendenti(nodo, New String() {"listitem", "DescriptionListTerm", "term"}, 2)
                    If elementi > 0 Then
                        attesaLista = elementi
                        impostati.Add("ls")
                    End If
                ElseIf ruolo = "listitem" OrElse ruolo = "DescriptionListTerm" OrElse ruolo = "term" Then
                    attesaElemento = True
                    impostati.Add("li")
                ElseIf ruolo = "table" OrElse ruolo = "grid" OrElse ruolo = "treegrid" Then
                    Dim tabella As TabellaVirtuale = CostruisciTabella(nodo, nome)
                    If tabella IsNot Nothing Then
                        attesaTabella = If(nome <> "", nome & ", ", "") & tabella.Righe & If(tabella.Righe = 1, " riga, ", " righe, ") & tabella.Colonne & If(tabella.Colonne = 1, " colonna", " colonne")
                    Else
                        attesaTabella = If(nome <> "", nome, "senza righe")
                    End If
                    impostati.Add("tb")
                ElseIf ruolo = "article" Then
                    attesaArticolo = True
                    impostati.Add("ar")
                ElseIf ruolo = "blockquote" Then
                    attesaCitazione = True
                    impostati.Add("bq")
                ElseIf ruolo = "paragraph" Then
                    attesaParagrafo = True
                    impostati.Add("pp")
                ElseIf DescrizioneGruppo.ContainsKey(ruolo) Then
                    If nome <> "" OrElse ruolo <> "group" Then
                        attesaGruppo = DescrizioneGruppo(ruolo) & If(nome <> "", " " & nome, "")
                        impostati.Add("gr")
                    End If
                End If
                Dim contestoCella As ContestoCella = ContestoPerCella(idNodo, ruolo)
                If contestoCella IsNot Nothing Then celle.Push(contestoCella)
                Dim righePrima As Integer = Righe.Count
                Dim dialogoPrima As Integer = dialogo
                If ruolo = "dialog" OrElse ruolo = "alertdialog" Then dialogo = idNodo
                VisitaFigli(nodo, padreFigli)
                If blocco OrElse contestoCella IsNot Nothing Then ChiudiLinea()
                dialogo = dialogoPrima
                If contestoCella IsNot Nothing Then celle.Pop()
                If Righe.Count = righePrima Then
                    For Each chiaveAttesa As String In impostati
                        Select Case chiaveAttesa
                            Case "rg" : attesaRegione = ""
                            Case "ls" : attesaLista = 0
                            Case "li" : attesaElemento = False
                            Case "tb" : attesaTabella = ""
                            Case "ar" : attesaArticolo = False
                            Case "bq" : attesaCitazione = False
                            Case "gr" : attesaGruppo = ""
                            Case "pp" : attesaParagrafo = False
                        End Select
                    Next
                End If
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Visita: " & ex.Message)
            End Try
        End Sub

        Private Function ContestoPerCella(idNodo As Integer, ruolo As String) As ContestoCella
            If idNodo <= 0 Then Return Nothing
            If ruolo <> "cell" AndAlso ruolo <> "gridcell" AndAlso ruolo <> "columnheader" AndAlso ruolo <> "rowheader" Then Return Nothing
            For Each tabella As TabellaVirtuale In Tabelle.Values
                If Not tabella.Id.StartsWith(documento.Sessione & "|", StringComparison.Ordinal) Then Continue For
                Dim cella As CellaVirtuale = Nothing
                If tabella.PerNodo.TryGetValue(idNodo, cella) Then Return New ContestoCella() With {.Tabella = tabella, .Cella = cella}
            Next
            Return Nothing
        End Function

        Private Function AttributoIntero(info As InfoNodoLayout, nomi As String(), predefinito As Integer) As Integer
            Try
                If info Is Nothing Then Return predefinito
                For Each nome As String In nomi
                    Dim valore As String = info.Attributo(nome)
                    Dim numero As Integer
                    If Not String.IsNullOrWhiteSpace(valore) AndAlso Integer.TryParse(valore.Trim(), numero) Then Return numero
                Next
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.AttributoIntero: " & ex.Message)
            End Try
            Return predefinito
        End Function

        Private Sub RaccogliRighe(nodo As JObject, righe As List(Of JObject), radice As Boolean, profondita As Integer)
            If profondita > 12 Then Return
            For Each figlio As JObject In Figli(nodo)
                Dim r As String = RuoloDi(figlio)
                If r = "row" AndAlso Not Ignorato(figlio) Then
                    righe.Add(figlio)
                    Continue For
                End If
                If Not Ignorato(figlio) AndAlso (r = "table" OrElse r = "grid" OrElse r = "treegrid") Then Continue For
                RaccogliRighe(figlio, righe, False, profondita + 1)
            Next
        End Sub

        Private Sub RaccogliCelle(nodo As JObject, risultato As List(Of JObject), profondita As Integer)
            If profondita > 8 Then Return
            For Each figlio As JObject In Figli(nodo)
                Dim r As String = RuoloDi(figlio)
                If Not Ignorato(figlio) AndAlso (r = "cell" OrElse r = "gridcell" OrElse r = "columnheader" OrElse r = "rowheader") Then
                    risultato.Add(figlio)
                    Continue For
                End If
                If Not Ignorato(figlio) AndAlso (r = "row" OrElse r = "table" OrElse r = "grid" OrElse r = "treegrid") Then Continue For
                RaccogliCelle(figlio, risultato, profondita + 1)
            Next
        End Sub

        Private Function CostruisciTabella(nodo As JObject, nome As String) As TabellaVirtuale
            Try
                Dim idTabella As Integer = IdDom(nodo)
                If idTabella <= 0 Then Return Nothing
                Dim righe As New List(Of JObject)()
                RaccogliRighe(nodo, righe, True, 0)
                If righe.Count = 0 Then Return Nothing
                Dim tabella As New TabellaVirtuale() With {.Id = documento.Sessione & "|" & idTabella.ToString(), .Nome = nome, .Righe = righe.Count}
                Dim occupate As New HashSet(Of Long)()
                Dim massimoColonne As Integer = 0
                For indiceRiga As Integer = 1 To righe.Count
                    Dim celleRiga As New List(Of JObject)()
                    RaccogliCelle(righe(indiceRiga - 1), celleRiga, 0)
                    Dim colonna As Integer = 1
                    For Each nodoCella As JObject In celleRiga
                        While occupate.Contains(CLng(indiceRiga) * 100000L + colonna)
                            colonna += 1
                        End While
                        Dim infoCella As InfoNodoLayout = InfoDi(nodoCella)
                        Dim spanColonne As Integer = Math.Max(1, Math.Min(100, AttributoIntero(infoCella, New String() {"colspan", "aria-colspan"}, 1)))
                        Dim spanRighe As Integer = AttributoIntero(infoCella, New String() {"rowspan", "aria-rowspan"}, 1)
                        If spanRighe <= 0 Then spanRighe = righe.Count - indiceRiga + 1
                        spanRighe = Math.Max(1, Math.Min(righe.Count - indiceRiga + 1, spanRighe))
                        Dim ruoloCella As String = RuoloDi(nodoCella)
                        Dim cella As New CellaVirtuale() With {
                            .Riga = indiceRiga, .Colonna = colonna, .SpanRighe = spanRighe, .SpanColonne = spanColonne,
                            .IdNodo = IdDom(nodoCella), .Intestazione = ruoloCella = "columnheader", .IntestazioneRiga = ruoloCella = "rowheader"
                        }
                        tabella.Celle.Add(cella)
                        If cella.IdNodo > 0 Then tabella.PerNodo(cella.IdNodo) = cella
                        For rr As Integer = indiceRiga To indiceRiga + spanRighe - 1
                            For cc As Integer = colonna To colonna + spanColonne - 1
                                occupate.Add(CLng(rr) * 100000L + cc)
                            Next
                        Next
                        colonna += spanColonne
                        If colonna - 1 > massimoColonne Then massimoColonne = colonna - 1
                    Next
                Next
                tabella.Colonne = massimoColonne
                Tabelle(tabella.Id) = tabella
                Return tabella
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.CostruisciTabella: " & ex.Message)
                Return Nothing
            End Try
        End Function

        Private Function ContaDiscendenti(nodo As JObject, ruoli As String(), profonditaMassima As Integer) As Integer
            Dim conta As Integer = 0
            Try
                Dim pila As New Stack(Of Tuple(Of JObject, Integer))()
                For Each f As JObject In Figli(nodo)
                    pila.Push(Tuple.Create(f, 1))
                Next
                Dim giri As Integer = 0
                While pila.Count > 0 AndAlso giri < 20000
                    giri += 1
                    Dim elemento As Tuple(Of JObject, Integer) = pila.Pop()
                    Dim r As String = RuoloDi(elemento.Item1)
                    If Not Ignorato(elemento.Item1) AndAlso ruoli.Contains(r) Then
                        conta += 1
                        Continue While
                    End If
                    If elemento.Item2 >= profonditaMassima AndAlso Not Ignorato(elemento.Item1) Then Continue While
                    For Each f As JObject In Figli(elemento.Item1)
                        pila.Push(Tuple.Create(f, elemento.Item2 + If(Ignorato(elemento.Item1), 0, 1)))
                    Next
                End While
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.ContaDiscendenti: " & ex.Message)
            End Try
            Return conta
        End Function

        Private Function ContieneIntestazione(nodo As JObject) As Boolean
            Try
                Dim pila As New Stack(Of JObject)()
                For Each f As JObject In Figli(nodo)
                    pila.Push(f)
                Next
                Dim giri As Integer = 0
                While pila.Count > 0 AndAlso giri < 500
                    giri += 1
                    Dim n As JObject = pila.Pop()
                    If RuoloDi(n) = "heading" AndAlso Not Ignorato(n) Then Return True
                    For Each f As JObject In Figli(n)
                        pila.Push(f)
                    Next
                End While
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.ContieneIntestazione: " & ex.Message)
            End Try
            Return False
        End Function

        Private Function Analizza(nodo As JObject) As AnalisiSottoalbero
            Dim analisi As New AnalisiSottoalbero()
            Try
                Dim pila As New Stack(Of JObject)()
                Dim elenco As List(Of JObject) = Figli(nodo)
                For i As Integer = elenco.Count - 1 To 0 Step -1
                    pila.Push(elenco(i))
                Next
                While pila.Count > 0 AndAlso analisi.Nodi < 2000
                    analisi.Nodi += 1
                    Dim n As JObject = pila.Pop()
                    Dim ruoloChrome As String = RuoloDi(n)
                    Dim r As String = RuoloNormale(ruoloChrome)
                    If Not Ignorato(n) Then
                        If RuoliWidget.Contains(r) Then
                            analisi.Widget += 1
                            Continue While
                        End If
                        If r = "heading" Then analisi.Intestazioni += 1
                        If RuoliStrutturali.Contains(r) Then analisi.Strutture += 1
                        If ruoloChrome = "StaticText" Then
                            analisi.Testo.Append(" ").Append(If(n("name")?("value")?.ToString(), ""))
                            Continue While
                        End If
                        If r = "img" Then
                            Dim nomeImmagine As String = NomeDi(n)
                            If nomeImmagine <> "" Then
                                analisi.Testo.Append(" ").Append(nomeImmagine)
                            ElseIf analisi.NomeImmagine = "" Then
                                Dim infoImmagine As InfoNodoLayout = InfoDi(n)
                                If infoImmagine IsNot Nothing Then analisi.NomeImmagine = NomeDaIndirizzo(If(infoImmagine.Attributo("src"), ""))
                            End If
                            Continue While
                        End If
                    End If
                    Dim figliNodo As List(Of JObject) = Figli(n)
                    For i As Integer = figliNodo.Count - 1 To 0 Step -1
                        pila.Push(figliNodo(i))
                    Next
                End While
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Analizza: " & ex.Message)
            End Try
            Return analisi
        End Function

        Private Function CandidatoCliccabile(nodo As JObject, info As InfoNodoLayout, ruolo As String) As Boolean
            Try
                If Not riconosciCliccabili OrElse info Is Nothing OrElse info.TipoNodo <> 1 Then Return False
                If link <> 0 OrElse dentroCliccabile > 0 Then Return False
                Select Case info.Tag
                    Case "html", "body", "label", "main", "form", "nav", "header", "footer", "option", "select", "input", "textarea", "button", "a", "summary", "iframe", "frame", "svg", "path", "g", "img", "video", "audio", "canvas", "table", "tbody", "thead", "tr", "td", "th", "ul", "ol", "dl"
                        Return False
                End Select
                If RuoliWidget.Contains(ruolo) OrElse RuoliStrutturali.Contains(ruolo) Then Return False
                If ruolo = "LabelText" OrElse ruolo = "Label" Then Return False
                If info.HaLayout Then
                    If info.Larghezza < 2 OrElse info.Altezza < 2 Then Return False
                    Dim larghezzaPagina As Double = If(documento.Layout IsNot Nothing, documento.Layout.LarghezzaContenuto, 0)
                    If larghezzaPagina > 0 AndAlso info.Larghezza >= larghezzaPagina * 0.9 AndAlso info.Altezza >= 500 Then Return False
                End If
                If info.Cliccabile Then Return True
                If info.PuntatoreRadice Then Return True
                If info.Attributo("onclick") IsNot Nothing Then Return True
                Dim azioni As String = info.Attributo("jsaction")
                If azioni IsNot Nothing AndAlso (azioni.IndexOf("click", StringComparison.OrdinalIgnoreCase) >= 0 OrElse (azioni.Trim() <> "" AndAlso azioni.IndexOf(":"c) < 0)) Then Return True
                Dim tabindex As String = info.Attributo("tabindex")
                Dim valoreTab As Integer
                If tabindex IsNot Nothing AndAlso Integer.TryParse(tabindex.Trim(), valoreTab) AndAlso valoreTab >= 0 AndAlso RuoliGenerici.Contains(RuoloDi(nodo)) Then Return True
                Return False
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.CandidatoCliccabile: " & ex.Message)
                Return False
            End Try
        End Function

        Private Function GestisciCliccabile(nodo As JObject, info As InfoNodoLayout, ruolo As String, padreFigli As Integer) As Boolean
            Try
                If Not CandidatoCliccabile(nodo, info, ruolo) Then Return False
                Dim analisi As AnalisiSottoalbero = Analizza(nodo)
                If analisi.Widget > 0 OrElse analisi.Nodi >= 2000 Then Return False
                Dim testo As String = Normalizza(analisi.Testo.ToString())
                If testo.Length > 3000 Then Return False
                Dim idNodo As Integer = IdDom(nodo)
                If analisi.Intestazioni = 0 AndAlso analisi.Strutture = 0 AndAlso testo.Length <= 400 Then
                    ChiudiLinea()
                    Dim nome As String = NomeDi(nodo)
                    If nome = "" Then nome = testo
                    If nome = "" Then nome = analisi.NomeImmagine
                    If nome = "" AndAlso info IsNot Nothing Then nome = Normalizza(If(info.Attributo("title"), ""))
                    If nome = "" Then nome = "senza nome"
                    If ruolo = "listitem" Then attesaElemento = True
                    If ruolo = "paragraph" Then attesaParagrafo = True
                    Dim r As New RigaVirtuale() With {.Ruolo = "cliccabile", .Testo = nome, .IdElemento = idNodo, .Stato = StatiDi(nodo, "cliccabile")}
                    Emetti(r)
                    Return True
                End If
                If RuoliStrutturali.Contains(ruolo) OrElse ruolo = "listitem" OrElse ruolo = "article" Then Return False
                ChiudiLinea()
                Dim vecchioLink As Integer = link
                Dim vecchioRuolo As String = linkRuolo
                Dim vecchioIndirizzo As String = linkIndirizzo
                link = idNodo
                linkRuolo = "cliccabile"
                linkIndirizzo = ""
                dentroCliccabile += 1
                VisitaFigli(nodo, padreFigli)
                ChiudiLinea()
                dentroCliccabile -= 1
                link = vecchioLink
                linkRuolo = vecchioRuolo
                linkIndirizzo = vecchioIndirizzo
                Return True
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.GestisciCliccabile: " & ex.Message)
                Return False
            End Try
        End Function

        Private Function TestoFormula(nodo As JObject, profondita As Integer) As String
            If profondita > 40 Then Return ""
            If RuoloDi(nodo) = "StaticText" Then Return If(nodo("name")?("value")?.ToString(), "")
            Dim parti As New List(Of String)()
            For Each figlio As JObject In Figli(nodo)
                Dim t As String = TestoFormula(figlio, profondita + 1)
                If t <> "" Then parti.Add(t)
            Next
            Return String.Join(" ", parti)
        End Function

        Private Sub Immagine(nodo As JObject, info As InfoNodoLayout)
            Try
                Dim idNodo As Integer = IdDom(nodo)
                Dim nomeImmagine As String = NomeDi(nodo)
                If nomeImmagine <> "" Then
                    If link = 0 Then
                        ChiudiLinea()
                        Emetti(New RigaVirtuale() With {.Ruolo = "img", .Testo = nomeImmagine, .IdElemento = idNodo, .Sorgente = SorgenteSicura(If(info IsNot Nothing, If(info.Attributo("src"), ""), ""))})
                    Else
                        AggiungiTesto(" " & nomeImmagine & " ", idNodo)
                    End If
                    Return
                End If
                If Not immaginiSenzaNome OrElse link <> 0 OrElse info Is Nothing Then Return
                If Not info.HaLayout OrElse info.Larghezza < 30 OrElse info.Altezza < 30 Then Return
                If info.Tag <> "img" AndAlso info.Tag <> "svg" AndAlso info.Tag <> "picture" AndAlso info.Tag <> "input" Then Return
                Dim sorgente As String = SorgenteSicura(If(info.Attributo("src"), If(info.Attributo("data-src"), "")))
                ChiudiLinea()
                Emetti(New RigaVirtuale() With {.Ruolo = "img", .Testo = NomeDaIndirizzo(sorgente), .IdElemento = idNodo, .SenzaNome = True, .Sorgente = sorgente})
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Immagine: " & ex.Message)
            End Try
        End Sub

        Private Sub AreaDisegnata(nodo As JObject, info As InfoNodoLayout, padreFigli As Integer)
            Try
                Dim righePrima As Integer = Righe.Count
                ChiudiLinea()
                VisitaFigli(nodo, padreFigli)
                ChiudiLinea()
                If Righe.Count > righePrima Then Return
                If info Is Nothing OrElse Not info.HaLayout OrElse info.Larghezza < 150 OrElse info.Altezza < 100 Then Return
                AreeDisegnate = True
                Dim nome As String = NomeDi(nodo)
                Emetti(New RigaVirtuale() With {.Ruolo = "canvas", .Testo = If(nome <> "", nome, "contenuto grafico senza testo"), .IdElemento = IdDom(nodo)})
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.AreaDisegnata: " & ex.Message)
            End Try
        End Sub

        Private Function NomeDiRiserva(nodo As JObject, ruolo As String, info As InfoNodoLayout, riga As RigaVirtuale) As String
            Try
                Dim analisi As AnalisiSottoalbero = Analizza(nodo)
                Dim testo As String = Normalizza(analisi.Testo.ToString())
                If testo <> "" Then Return testo
                If analisi.NomeImmagine <> "" Then Return analisi.NomeImmagine
                If info IsNot Nothing Then
                    Dim titolo As String = info.Attributo("title")
                    If Not String.IsNullOrWhiteSpace(titolo) Then Return Normalizza(titolo)
                End If
                If ruolo = "link" Then
                    Dim indirizzo As String = riga.Indirizzo
                    If indirizzo = "" AndAlso info IsNot Nothing Then indirizzo = If(info.Attributo("href"), "")
                    Dim dalIndirizzo As String = NomeDaIndirizzo(indirizzo)
                    If dalIndirizzo <> "" Then Return dalIndirizzo
                End If
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.NomeDiRiserva: " & ex.Message)
            End Try
            Return ""
        End Function

        Private Sub Widget(nodo As JObject, ruolo As String, padreFigli As Integer, info As InfoNodoLayout)
            Try
                Dim idNodo As Integer = IdDom(nodo)
                If (ruolo = "link" OrElse ruolo = "button") AndAlso ContieneIntestazione(nodo) Then
                    Dim vecchioLink As Integer = link
                    Dim vecchioRuolo As String = linkRuolo
                    Dim vecchioIndirizzo As String = linkIndirizzo
                    link = idNodo
                    linkRuolo = ruolo
                    linkIndirizzo = ""
                    If ruolo = "link" Then
                        Dim indirizzo As JToken = Proprieta(nodo, "url")
                        If indirizzo IsNot Nothing Then linkIndirizzo = indirizzo.ToString()
                    End If
                    VisitaFigli(nodo, padreFigli)
                    ChiudiLinea()
                    link = vecchioLink
                    linkRuolo = vecchioRuolo
                    linkIndirizzo = vecchioIndirizzo
                    Return
                End If
                If ruolo = "listbox" Then
                    Dim nomeElenco As String = NomeDi(nodo)
                    attesaGruppo = "elenco" & If(nomeElenco <> "", " " & nomeElenco, "")
                    Dim righePrima As Integer = Righe.Count
                    VisitaFigli(nodo, padreFigli)
                    ChiudiLinea()
                    If Righe.Count = righePrima Then
                        attesaGruppo = ""
                        Emetti(RigaDaNodo(nodo, ruolo, documento.Sessione))
                    End If
                    Return
                End If
                Dim riga As RigaVirtuale = RigaDaNodo(nodo, ruolo, documento.Sessione)
                If ruolo = "link" AndAlso riga.Indirizzo = "" AndAlso info IsNot Nothing Then riga.Indirizzo = If(info.Attributo("href"), "")
                If riga.Modificabile AndAlso info IsNot Nothing Then
                    If String.Equals(info.Attributo("type"), "password", StringComparison.OrdinalIgnoreCase) AndAlso Not riga.Password Then
                        riga.Password = True
                        riga.LunghezzaPassword = riga.Valore.Length
                        riga.Valore = ""
                    End If
                    If riga.Testo = "" Then
                        Dim nomeCampo As String = If(info.Attributo("name"), If(info.Attributo("id"), ""))
                        nomeCampo = Normalizza(Regex.Replace(nomeCampo, "[-_.\[\]]+", " "))
                        If nomeCampo.Length > 40 OrElse Regex.IsMatch(nomeCampo, "^[0-9a-f ]{8,}$", RegexOptions.IgnoreCase) Then nomeCampo = ""
                        If nomeCampo = "" AndAlso riga.Password Then nomeCampo = "password"
                        riga.Testo = nomeCampo
                    End If
                End If
                If ruolo = "treeitem" Then
                    If riga.Testo <> "" Then Emetti(riga)
                    For Each f As JObject In Figli(nodo)
                        Dim rf As String = RuoloDi(f)
                        If rf = "group" OrElse rf = "tree" OrElse Ignorato(f) Then Visita(f, padreFigli)
                    Next
                    ChiudiLinea()
                    Return
                End If
                If riga.Testo = "" AndAlso (ruolo = "link" OrElse ruolo = "button" OrElse ruolo = "menuitem" OrElse ruolo = "tab" OrElse ruolo = "option") Then
                    Dim riserva As String = NomeDiRiserva(nodo, ruolo, info, riga)
                    If riserva <> "" Then
                        riga.Testo = riserva
                    ElseIf ruolo = "link" OrElse ruolo = "button" Then
                        riga.Testo = "senza nome"
                    End If
                End If
                If riga.Testo = "" AndAlso riga.Valore = "" AndAlso Not riga.Modificabile AndAlso ruolo <> "checkbox" AndAlso ruolo <> "radio" AndAlso ruolo <> "switch" AndAlso ruolo <> "combobox" AndAlso ruolo <> "slider" Then Return
                Emetti(riga)
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Widget: " & ex.Message)
            End Try
        End Sub

        Private Sub Intestazione(nodo As JObject)
            Try
                Dim riga As RigaVirtuale = RigaDaNodo(nodo, "heading", documento.Sessione)
                If riga.Testo = "" Then Return
                If riga.Livello <= 0 Then riga.Livello = 2
                If link = 0 Then
                    Dim interno As JObject = WidgetUnico(nodo)
                    If interno IsNot Nothing Then
                        Dim ruoloInterno As String = RuoloNormale(RuoloDi(interno))
                        riga.IdAttivazione = IdDom(interno)
                        riga.RuoloAttivazione = ruoloInterno
                        If ruoloInterno = "link" Then
                            Dim indirizzo As JToken = Proprieta(interno, "url")
                            If indirizzo IsNot Nothing Then riga.Indirizzo = indirizzo.ToString()
                        End If
                        Dim stati As String = StatiDi(interno, ruoloInterno)
                        If stati <> "" Then riga.Stato = stati
                    End If
                End If
                Emetti(riga)
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Intestazione: " & ex.Message)
            End Try
        End Sub

        Private Function WidgetUnico(nodo As JObject) As JObject
            Dim trovati As New List(Of JObject)()
            Try
                Dim pila As New Stack(Of JObject)()
                For Each f As JObject In Figli(nodo)
                    pila.Push(f)
                Next
                While pila.Count > 0 AndAlso trovati.Count < 2
                    Dim n As JObject = pila.Pop()
                    Dim r As String = RuoloNormale(RuoloDi(n))
                    If Not Ignorato(n) AndAlso (r = "link" OrElse r = "button") Then
                        trovati.Add(n)
                        Continue While
                    End If
                    For Each f As JObject In Figli(n)
                        pila.Push(f)
                    Next
                End While
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.WidgetUnico: " & ex.Message)
            End Try
            Return If(trovati.Count = 1, trovati(0), Nothing)
        End Function

        Private Sub Frame(nodo As JObject)
            Try
                Dim nome As String = NomeDi(nodo)
                Dim figlio As Documento = Nothing
                If documentiDeiFrame.TryGetValue(Chiave(documento.Sessione, IdDom(nodo)), figlio) AndAlso figlio.Radice IsNot Nothing Then
                    Dim vecchio As Documento = documento
                    documento = figlio
                    Dim righePrima As Integer = Righe.Count
                    attesaRegione = "frame" & If(nome <> "", " " & nome, "")
                    Visita(figlio.Radice, 0)
                    ChiudiLinea()
                    If Righe.Count = righePrima Then attesaRegione = ""
                    documento = vecchio
                    Return
                End If
                If nome <> "" Then Emetti(New RigaVirtuale() With {.Ruolo = "frame", .Testo = nome, .IdElemento = IdDom(nodo)})
            Catch ex As Exception
                log.ScriviLog("WGAlberoAccessibilita.Frame: " & ex.Message)
            End Try
        End Sub
    End Class
End Class
