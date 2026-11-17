Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text

' Analisi e costruzione dell'indice della guida e dei punti di riferimento del documento.
Friend NotInheritable Class VoceIndiceAiuto
    Public Id As Integer
    Public Tipo As String = ""
    Public Livello As Integer
    Public Testo As String = ""
    Public InElenco As Boolean
    Public Foglia As Integer = -1
    Public Visitato As Boolean
    Public Scrivibile As Boolean
    Public Impaginazione As Boolean
    Public Focalizzabile As Boolean
    Public NomeRegione As String = ""
    Public Nome As String = ""
End Class

Friend NotInheritable Class RisultatoIndiceAiuto
    Public IdDocumento As Integer
    Public Troncato As Boolean
    Public ReadOnly Voci As New List(Of VoceIndiceAiuto)()
End Class

Friend NotInheritable Class IndiceAiuto

    Private Const RuoloTitoloIa2 As Integer = &H414
    Private Const RuoloOggettoIa2 As Integer = &H40A
    Private Const RuoloPulsanteAttivabileIa2 As Integer = &H42A
    Private Const StatoFuoriSchermo As Integer = &H10000
    Private Const StatoVisitato As Integer = &H800000
    Private Const StatoFocalizzabile As Integer = &H100000
    Private Const StatoModificabileIa2 As Integer = &H8
    Private Const MassimoTestoSotto As Integer = 2000

    Private Shared ReadOnly logShared As New Logger()

    Private NotInheritable Class Nodo
        Public Id As Integer
        Public Profondita As Integer
        Public Ruolo As Integer
        Public Stato As Integer
        Public RuoloIa2 As Integer
        Public StatiIa2 As Integer
        Public Livello As Integer
        Public NumeroFigli As Integer
        Public Nome As String = ""
        Public Valore As String = ""
        Public Aria As String = ""
        Public Tag As String = ""
        Public LivelloAttributo As Integer
        Public Impaginazione As Boolean
        Public Padre As Integer = -1
        Public Ultimo As Integer
        Public ReadOnly Figli As New List(Of Integer)()
        Public Tipo As String = ""
    End Class

    Private Sub New()
    End Sub

    Public Shared Function Analizza(risposta As String) As RisultatoIndiceAiuto
        Try
            If String.IsNullOrEmpty(risposta) Then Return Nothing
            Dim righe As String() = risposta.Split(ControlChars.Lf)
            If righe.Length = 0 Then Return Nothing
            Dim intestazione As String() = righe(0).Split(ControlChars.Tab)
            If intestazione.Length < 4 OrElse intestazione(0) <> "OK" Then Return Nothing
            Dim risultato As New RisultatoIndiceAiuto()
            risultato.IdDocumento = Intero(intestazione(1))
            risultato.Troncato = intestazione(3).Trim() = "1"
            Dim nodi As List(Of Nodo) = LeggiNodi(righe)
            If nodi.Count = 0 Then Return Nothing
            CollegaNodi(nodi)
            For indice As Integer = 0 To nodi.Count - 1
                nodi(indice).Tipo = Classifica(nodi, indice)
            Next
            CorreggiLinkTitolo(nodi)
            CostruisciVoci(nodi, risultato.Voci)
            Return risultato
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.Analizza: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Function Intero(testo As String) As Integer
        Try
            Dim valore As Long = 0L
            If Long.TryParse(If(testo, "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, valore) Then
                If valore > Integer.MaxValue OrElse valore < Integer.MinValue Then Return 0
                Return CInt(valore)
            End If
            Return 0
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.Intero: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Shared Function LeggiNodi(righe As String()) As List(Of Nodo)
        Dim nodi As New List(Of Nodo)()
        Try
            For indice As Integer = 1 To righe.Length - 1
                Dim riga As String = righe(indice)
                If riga.Length = 0 Then Continue For
                Dim campi As String() = riga.Split(ControlChars.Tab)
                If campi.Length < 11 Then Continue For
                Dim nodo As New Nodo()
                nodo.Id = Intero(campi(0))
                nodo.Profondita = Intero(campi(1))
                nodo.Ruolo = Intero(campi(2))
                nodo.Stato = Intero(campi(3))
                nodo.RuoloIa2 = Intero(campi(4))
                nodo.StatiIa2 = Intero(campi(5))
                nodo.Livello = Intero(campi(6))
                nodo.NumeroFigli = Intero(campi(7))
                nodo.Nome = campi(8).Trim()
                nodo.Valore = campi(9).Trim()
                LeggiAttributi(campi(10), nodo)
                If nodi.Count = 0 AndAlso nodo.Profondita <> 0 Then Continue For
                nodi.Add(nodo)
            Next
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.LeggiNodi: " & ex.Message)
        End Try
        Return nodi
    End Function

    Private Shared Sub LeggiAttributi(testo As String, nodo As Nodo)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            Dim nome As New StringBuilder()
            Dim valore As New StringBuilder()
            Dim inValore As Boolean = False
            Dim indice As Integer = 0
            While indice < testo.Length
                Dim carattere As Char = testo(indice)
                If carattere = "\"c AndAlso indice + 1 < testo.Length Then
                    If inValore Then valore.Append(testo(indice + 1)) Else nome.Append(testo(indice + 1))
                    indice += 2
                    Continue While
                End If
                If carattere = ":"c AndAlso Not inValore Then
                    inValore = True
                ElseIf carattere = ";"c Then
                    ApplicaAttributo(nome.ToString(), valore.ToString(), nodo)
                    nome.Clear()
                    valore.Clear()
                    inValore = False
                ElseIf inValore Then
                    valore.Append(carattere)
                Else
                    nome.Append(carattere)
                End If
                indice += 1
            End While
            If nome.Length > 0 Then ApplicaAttributo(nome.ToString(), valore.ToString(), nodo)
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.LeggiAttributi: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ApplicaAttributo(nome As String, valore As String, nodo As Nodo)
        Try
            Select Case nome.Trim().ToLowerInvariant()
                Case "xml-roles"
                    Dim primo As String = valore.Trim().ToLowerInvariant()
                    Dim spazio As Integer = primo.IndexOf(" "c)
                    If spazio > 0 Then primo = primo.Substring(0, spazio)
                    nodo.Aria = primo
                Case "tag"
                    nodo.Tag = valore.Trim().ToLowerInvariant()
                Case "level"
                    nodo.LivelloAttributo = Intero(valore)
                Case "layout-guess"
                    nodo.Impaginazione = String.Equals(valore.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            End Select
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.ApplicaAttributo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub CollegaNodi(nodi As List(Of Nodo))
        Try
            Dim pila As New List(Of Integer)()
            For indice As Integer = 0 To nodi.Count - 1
                Dim nodo As Nodo = nodi(indice)
                nodo.Ultimo = indice
                While pila.Count > 0 AndAlso nodi(pila(pila.Count - 1)).Profondita >= nodo.Profondita
                    pila.RemoveAt(pila.Count - 1)
                End While
                If pila.Count > 0 Then
                    nodo.Padre = pila(pila.Count - 1)
                    nodi(nodo.Padre).Figli.Add(indice)
                End If
                pila.Add(indice)
            Next
            For indice As Integer = nodi.Count - 1 To 1 Step -1
                Dim padre As Integer = nodi(indice).Padre
                If padre >= 0 AndAlso nodi(indice).Ultimo > nodi(padre).Ultimo Then nodi(padre).Ultimo = nodi(indice).Ultimo
            Next
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.CollegaNodi: " & ex.Message)
        End Try
    End Sub

    Private Shared Function LivelloDaTag(tag As String) As Integer
        If tag Is Nothing OrElse tag.Length <> 2 OrElse tag(0) <> "h"c Then Return 0
        Dim cifra As Integer = AscW(tag(1)) - AscW("0"c)
        If cifra >= 1 AndAlso cifra <= 6 Then Return cifra
        Return 0
    End Function

    Private Shared Function NomeRegioneAria(aria As String, nome As String) As String
        Select Case aria
            Case "main"
                Return "principale"
            Case "navigation"
                Return "navigazione"
            Case "search"
                Return "ricerca"
            Case "banner"
                Return "banner"
            Case "contentinfo"
                Return "informazioni"
            Case "complementary"
                Return "complementare"
            Case "form"
                Return If(nome = "", "", "modulo")
            Case "region"
                Return If(nome = "", "", "regione")
        End Select
        Return ""
    End Function

    Private Shared Function Modificabile(nodo As Nodo) As Boolean
        Return (nodo.StatiIa2 And StatoModificabileIa2) <> 0
    End Function

    Private Shared Function Classifica(nodi As List(Of Nodo), indice As Integer) As String
        Try
            If indice = 0 Then Return "documento"
            Dim nodo As Nodo = nodi(indice)
            Dim aria As String = nodo.Aria
            Dim ruolo As Integer = nodo.Ruolo
            If (aria = "menu" OrElse (aria = "" AndAlso ruolo = &HB)) AndAlso (nodo.Stato And StatoFuoriSchermo) <> 0 Then Return "ignora"
            If nodo.RuoloIa2 = RuoloTitoloIa2 OrElse aria = "heading" OrElse LivelloDaTag(nodo.Tag) > 0 Then Return "titolo"
            If Modificabile(nodo) AndAlso ruolo <> &H2A AndAlso ruolo <> &H2E AndAlso nodo.Padre >= 0 AndAlso Not Modificabile(nodi(nodo.Padre)) Then Return "modifica"
            If NomeRegioneAria(aria, nodo.Nome) <> "" AndAlso ruolo <> &H2B AndAlso ruolo <> &H1E AndAlso ruolo <> &H2A Then Return "regione"
            Select Case aria
                Case "embed", "object", "applet", "audio", "video", "embeddedobject", "plugin"
                    Return "oggetto"
            End Select
            Select Case nodo.Tag
                Case "embed", "object", "audio", "video"
                    Return "oggetto"
                Case "blockquote"
                    Return "citazione"
            End Select
            Select Case aria
                Case "link"
                    Return "collegamento"
                Case "button"
                    Return "pulsante"
                Case "checkbox", "switch"
                    Return "casella"
                Case "radio"
                    Return "opzione"
                Case "textbox", "searchbox", "slider", "spinbutton"
                    Return "modifica"
                Case "scrollbar", "tooltip"
                    Return "ignora"
                Case "combobox"
                    Return "combinata"
                Case "list", "listbox", "tree", "feed"
                    Return "elenco"
                Case "listitem", "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "treeitem", "row", "option"
                    Return "voce"
                Case "treegrid", "table", "grid"
                    Return "tabella"
                Case "separator"
                    Return "separatore"
                Case "blockquote"
                    Return "citazione"
                Case "cell", "gridcell"
                    Return "cella"
                Case "columnheader"
                    Return "intestazioneColonna"
                Case "rowheader"
                    Return "intestazioneRiga"
                Case "article"
                    Return "articolo"
                Case "img", "image"
                    Return "grafica"
            End Select
            If nodo.RuoloIa2 = RuoloOggettoIa2 Then Return "oggetto"
            If nodo.RuoloIa2 = RuoloPulsanteAttivabileIa2 Then Return "pulsante"
            Select Case ruolo
                Case &H1E
                    Return "collegamento"
                Case &H2B, &H38, &H39, &H3A, &H3E
                    Return "pulsante"
                Case &H2C
                    Return "casella"
                Case &H2D
                    Return "opzione"
                Case &H2A
                    Return If(Modificabile(nodo), "modifica", "testo")
                Case &H2E
                    Return "combinata"
                Case &H21, &H23
                    Return "elenco"
                Case &H33, &H34
                    Return "modifica"
                Case &H19
                    Return "intestazioneColonna"
                Case &H1A
                    Return "intestazioneRiga"
                Case &HD, &H1, &H2, &H3, &H7
                    Return "ignora"
                Case &H22, &H24, &H25, &HC, &H1C
                    Return "voce"
                Case &H28
                    Return "grafica"
                Case &H18
                    Return "tabella"
                Case &H29
                    Return "testo"
                Case &H1D
                    Return "cella"
                Case &H15
                    Return "separatore"
                Case &H9
                    Return "finestra"
                Case &HF
                    Return "riquadro"
            End Select
            If nodo.Figli.Count = 0 AndAlso nodo.Nome <> "" Then Return "testo"
            Return "altro"
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.Classifica: " & ex.Message)
            Return "altro"
        End Try
    End Function

    Private Shared Sub CorreggiLinkTitolo(nodi As List(Of Nodo))
        Try
            For indice As Integer = 1 To nodi.Count - 1
                Dim nodo As Nodo = nodi(indice)
                If nodo.Tipo <> "collegamento" Then Continue For
                For interno As Integer = indice + 1 To Math.Min(nodo.Ultimo, indice + 40)
                    If nodi(interno).Tipo = "titolo" Then
                        nodo.Tipo = "titolo"
                        nodo.LivelloAttributo = LivelloTitolo(nodi(interno))
                        Exit For
                    End If
                Next
            Next
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.CorreggiLinkTitolo: " & ex.Message)
        End Try
    End Sub

    Private Shared Function LivelloTitolo(nodo As Nodo) As Integer
        If nodo.LivelloAttributo > 0 Then Return nodo.LivelloAttributo
        Dim daTag As Integer = LivelloDaTag(nodo.Tag)
        If daTag > 0 Then Return daTag
        If nodo.Livello > 0 AndAlso nodo.Livello <= 9 Then Return nodo.Livello
        Return 0
    End Function

    Private Shared Function Interattivo(tipo As String) As Boolean
        Select Case tipo
            Case "collegamento", "pulsante", "modifica", "casella", "opzione", "combinata"
                Return True
        End Select
        Return False
    End Function

    Private Shared Function ContieneTitolo(nodi As List(Of Nodo), indice As Integer) As Boolean
        Dim nodo As Nodo = nodi(indice)
        For interno As Integer = indice + 1 To Math.Min(nodo.Ultimo, indice + 400)
            If nodi(interno).Tipo = "titolo" Then Return True
        Next
        Return False
    End Function

    Private Shared Function EFoglia(nodi As List(Of Nodo), indice As Integer) As Boolean
        Dim nodo As Nodo = nodi(indice)
        Dim tipo As String = nodo.Tipo
        Select Case tipo
            Case "ignora", "grafica", "collegamento", "pulsante", "modifica", "casella", "opzione", "combinata", "codice", "separatore"
                Return True
        End Select
        If tipo = "titolo" Then
            For interno As Integer = indice + 1 To Math.Min(nodo.Ultimo, indice + 200)
                If nodi(interno).Tipo = "collegamento" OrElse nodi(interno).Tipo = "pulsante" Then Return False
            Next
            Return True
        End If
        If tipo = "articolo" Then
            Return nodo.Figli.Count = 0
        End If
        If tipo = "testo" OrElse tipo = "voce" OrElse tipo = "cella" Then
            If nodo.Figli.Count = 0 Then Return True
            If ContieneTitolo(nodi, indice) Then Return False
            Dim limite As Integer = 1
            If tipo = "cella" OrElse (tipo = "voce" AndAlso nodo.Aria = "row") Then limite = 25
            Dim controllati As Integer = 0
            For Each figlio As Integer In nodo.Figli
                If controllati >= limite Then Exit For
                Select Case nodi(figlio).Tipo
                    Case "collegamento", "pulsante", "modifica", "titolo", "grafica", "casella", "opzione", "combinata", "elenco", "tabella", "regione", "citazione", "articolo"
                        Return False
                    Case "cella", "voce"
                        If tipo <> "testo" Then Return False
                End Select
                controllati += 1
            Next
            Return True
        End If
        Return False
    End Function

    Private Shared Function TestoSotto(nodi As List(Of Nodo), indice As Integer, massimo As Integer) As String
        Dim testo As New StringBuilder()
        Try
            Dim nodo As Nodo = nodi(indice)
            For interno As Integer = indice + 1 To nodo.Ultimo
                Dim figlio As Nodo = nodi(interno)
                If figlio.Figli.Count > 0 OrElse figlio.Nome = "" Then Continue For
                If testo.Length > 0 Then testo.Append(" "c)
                testo.Append(figlio.Nome)
                If testo.Length >= massimo Then Exit For
            Next
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.TestoSotto: " & ex.Message)
        End Try
        Dim risultato As String = testo.ToString().Trim()
        If risultato.Length > massimo Then risultato = risultato.Substring(0, massimo)
        Return risultato
    End Function

    Private Shared Function VoceComposita(tipo As String) As Boolean
        Return tipo = "voce" OrElse tipo = "cella" OrElse tipo = "intestazioneColonna" OrElse tipo = "intestazioneRiga"
    End Function

    Private Shared Function TestoDi(nodi As List(Of Nodo), indice As Integer, foglia As Boolean) As String
        Dim nodo As Nodo = nodi(indice)
        Dim tipo As String = nodo.Tipo
        If (tipo = "modifica" OrElse tipo = "combinata") AndAlso nodo.Valore <> "" Then Return nodo.Valore
        Dim nome As String = nodo.Nome
        If nome = "" AndAlso (tipo = "collegamento" OrElse tipo = "pulsante") Then nome = TestoSotto(nodi, indice, 300)
        If nome = "" AndAlso (tipo = "testo" OrElse tipo = "articolo" OrElse tipo = "regione") Then nome = TestoSotto(nodi, indice, MassimoTestoSotto)
        If nome = "" AndAlso VoceComposita(tipo) AndAlso foglia Then nome = TestoSotto(nodi, indice, MassimoTestoSotto)
        Return nome
    End Function

    Private Shared Function Utile(nodi As List(Of Nodo), indice As Integer, foglia As Boolean) As Boolean
        Dim nodo As Nodo = nodi(indice)
        Select Case nodo.Tipo
            Case "documento", "finestra", "ignora", "altro"
                Return False
            Case "testo"
                Return nodo.Nome <> "" OrElse nodo.Valore <> ""
            Case "grafica", "voce", "cella", "intestazioneColonna", "intestazioneRiga"
                If nodo.Nome <> "" OrElse nodo.Valore <> "" Then Return True
                If nodo.Tipo <> "grafica" AndAlso foglia Then Return TestoSotto(nodi, indice, 40) <> ""
                Return False
        End Select
        Return True
    End Function

    Private Shared Sub CostruisciVoci(nodi As List(Of Nodo), voci As List(Of VoceIndiceAiuto))
        Try
            Dim indice As Integer = 1
            While indice < nodi.Count
                Dim nodo As Nodo = nodi(indice)
                Dim foglia As Boolean = EFoglia(nodi, indice)
                If Utile(nodi, indice, foglia) Then AggiungiVoce(nodi, indice, foglia, voci)
                If foglia Then
                    indice = nodo.Ultimo + 1
                Else
                    indice += 1
                End If
            End While
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.CostruisciVoci: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub AggiungiVoce(nodi As List(Of Nodo), indice As Integer, foglia As Boolean, voci As List(Of VoceIndiceAiuto))
        Try
            Dim nodo As Nodo = nodi(indice)
            Dim tipo As String = nodo.Tipo
            Dim testo As String = TestoDi(nodi, indice, foglia)
            If tipo = "testo" AndAlso voci.Count > 0 Then
                Dim precedente As VoceIndiceAiuto = voci(voci.Count - 1)
                If precedente.Tipo = "titolo" AndAlso String.Equals(precedente.Testo, testo, StringComparison.CurrentCulture) Then Return
            End If
            Dim campoVuotoValido As Boolean = tipo = "modifica" OrElse tipo = "casella" OrElse tipo = "opzione" OrElse tipo = "combinata"
            Dim contenitoreValido As Boolean = tipo = "titolo" OrElse tipo = "regione" OrElse tipo = "tabella" OrElse tipo = "elenco" OrElse tipo = "citazione" OrElse tipo = "separatore" OrElse tipo = "riquadro" OrElse tipo = "oggetto"
            If testo = "" AndAlso Not campoVuotoValido AndAlso Not contenitoreValido Then Return
            Dim voce As New VoceIndiceAiuto()
            voce.Id = nodo.Id
            voce.Tipo = tipo
            voce.Testo = testo
            voce.Livello = If(tipo = "titolo", LivelloTitolo(nodo), 0)
            voce.Foglia = If(foglia, 1, 0)
            voce.Visitato = (nodo.Stato And StatoVisitato) <> 0
            voce.Focalizzabile = (nodo.Stato And StatoFocalizzabile) <> 0
            voce.Impaginazione = nodo.Impaginazione
            voce.Scrivibile = Modificabile(nodo)
            If tipo = "combinata" AndAlso Not voce.Scrivibile Then
                For Each figlio As Integer In nodo.Figli
                    If nodi(figlio).Tipo = "modifica" Then
                        voce.Scrivibile = True
                        Exit For
                    End If
                Next
            End If
            voce.Nome = nodo.Nome
            If tipo = "regione" Then voce.NomeRegione = NomeRegioneAria(nodo.Aria, nodo.Nome)
            If tipo = "articolo" AndAlso nodo.Padre >= 0 Then
                Dim padre As Nodo = nodi(nodo.Padre)
                voce.InElenco = padre.Aria = "list" OrElse padre.Aria = "feed" OrElse padre.Aria = "listbox" OrElse padre.Ruolo = &H21
            End If
            voci.Add(voce)
        Catch ex As Exception
            logShared.ScriviLog("IndiceAiuto.AggiungiVoce: " & ex.Message)
        End Try
    End Sub

End Class
