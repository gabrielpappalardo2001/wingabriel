Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Text
Imports Accessibility
Imports UIAutomationClient

' Interazione con le API di accessibilità MSAA per descrivere eventi, finestre, ruoli e stati degli elementi sullo schermo.
Friend NotInheritable Class AccessibilitaMsaa

    Private Const OggettoClient As Integer = &HFFFFFFFC
    Private Const FiglioSeStesso As Integer = 0
    Private Const EventoMostra As UInteger = &H8002UI
    Private Const EventoNome As UInteger = &H800CUI
    Private Const EventoDescrizione As UInteger = &H800DUI
    Private Const SchemaAccessibilita As Integer = 10018
    Private Const VersoPrecedente As Integer = 6
    Private Const VersoSuccessivo As Integer = 5
    Private Const VersoContenitore As Integer = 1
    Private Const VersoContenuto As Integer = 7

    Private Shared ReadOnly IidAccessibile As Guid = GetType(Accessibility.IAccessible).GUID
    Private Shared ReadOnly log As New Logger()
    Private Shared _ultimoAccessibile As Accessibility.IAccessible
    Private Shared _ultimoFiglio As Object

    <DllImport("oleacc.dll")>
    Private Shared Function AccessibleObjectFromWindow(hwnd As IntPtr, oggetto As Integer, ByRef iid As Guid, <MarshalAs(UnmanagedType.IUnknown)> ByRef risultato As Object) As Integer
    End Function

    <DllImport("oleacc.dll")>
    Private Shared Function AccessibleObjectFromEvent(hwnd As IntPtr, oggetto As Integer, figlio As Integer, <MarshalAs(UnmanagedType.Interface)> ByRef accessibile As Accessibility.IAccessible, ByRef figlioRisultato As Object) As Integer
    End Function

    <DllImport("oleacc.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetRoleText(ruolo As Integer, testo As StringBuilder, capacita As Integer) As Integer
    End Function

    Private Sub New()
    End Sub

    Public Shared Function DescriviEvento(finestra As IntPtr, oggetto As Integer, figlio As Integer) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlioReale As Object = Nothing
            Dim esito As Integer = AccessibleObjectFromEvent(finestra, oggetto, figlio, accessibile, figlioReale)
            If esito < 0 OrElse accessibile Is Nothing Then Return DescriviFuoco(finestra)
            If figlioReale Is Nothing Then figlioReale = FiglioSeStesso
            Return DescriviAccessibile(accessibile, figlioReale)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviEvento", exAtteso)
            Return ""
        End Try
    End Function

    Public Shared Function StatoEvento(finestra As IntPtr, oggetto As Integer, figlio As Integer) As Long
        Try
            If finestra = IntPtr.Zero Then Return 0L
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlioReale As Object = Nothing
            If AccessibleObjectFromEvent(finestra, oggetto, figlio, accessibile, figlioReale) < 0 OrElse accessibile Is Nothing Then Return 0L
            If figlioReale Is Nothing Then figlioReale = FiglioSeStesso
            Dim valore As Object = accessibile.accState(figlioReale)
            If valore Is Nothing Then Return 0L
            Return Convert.ToInt64(valore)
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.StatoEvento: " & ex.Message)
            Return 0L
        End Try
    End Function

    Public Shared Function ValoreProgresso(finestra As IntPtr, oggetto As Integer, figlio As Integer, ByRef percentuale As Integer) As Boolean
        percentuale = -1
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlioReale As Object = Nothing
            If AccessibleObjectFromEvent(finestra, oggetto, figlio, accessibile, figlioReale) < 0 OrElse accessibile Is Nothing Then Return False
            If figlioReale Is Nothing Then figlioReale = FiglioSeStesso
            Dim ruolo As Integer = 0
            Dim valoreRuolo As Object = accessibile.accRole(figlioReale)
            If valoreRuolo Is Nothing OrElse Not Integer.TryParse(Convert.ToString(valoreRuolo), ruolo) OrElse ruolo <> 48 Then Return False
            Dim testo As String = accessibile.accValue(figlioReale)
            If String.IsNullOrEmpty(testo) Then Return False
            Dim cifre As New StringBuilder()
            For Each carattere As Char In testo
                If Char.IsDigit(carattere) Then
                    cifre.Append(carattere)
                ElseIf cifre.Length > 0 Then
                    Exit For
                End If
            Next
            If cifre.Length = 0 OrElse cifre.Length > 3 Then Return False
            Dim numero As Integer = Integer.Parse(cifre.ToString())
            If numero < 0 OrElse numero > 100 Then Return False
            percentuale = numero
            Return True
        Catch ex As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.ValoreProgresso", ex)
            Return False
        End Try
    End Function

    Public Shared Function InfoEvento(finestra As IntPtr, oggetto As Integer, figlio As Integer, ByRef ruolo As Integer, ByRef nome As String) As Boolean
        ruolo = 0
        nome = ""
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlioReale As Object = Nothing
            Dim esito As Integer = AccessibleObjectFromEvent(finestra, oggetto, figlio, accessibile, figlioReale)
            If esito < 0 OrElse accessibile Is Nothing Then Return False
            If figlioReale Is Nothing Then figlioReale = FiglioSeStesso
            Dim valoreRuolo As Object = Nothing
            Try
                valoreRuolo = accessibile.accRole(figlioReale)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("AccessibilitaMsaa.InfoEvento", exAtteso)
                valoreRuolo = Nothing
            End Try
            Dim numero As Integer = 0
            If valoreRuolo IsNot Nothing AndAlso Integer.TryParse(Convert.ToString(valoreRuolo), numero) Then ruolo = numero
            Dim testo As String = Nothing
            Try
                testo = accessibile.accName(figlioReale)
            Catch exAtteso2 As Exception
                RegistroErrori.Scrivi("AccessibilitaMsaa.InfoEvento", exAtteso2)
                testo = Nothing
            End Try
            nome = Pulisci(testo)
            Return True
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.InfoEvento: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function VocePopupGecko(finestra As IntPtr, oggetto As Integer, figlio As Integer, ByRef nome As String) As Boolean
        nome = ""
        Try
            If finestra = IntPtr.Zero Then Return False
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlioReale As Object = Nothing
            If AccessibleObjectFromEvent(finestra, oggetto, figlio, accessibile, figlioReale) < 0 OrElse accessibile Is Nothing Then Return False
            If figlioReale IsNot Nothing AndAlso Not (TypeOf figlioReale Is Integer AndAlso CInt(figlioReale) = FiglioSeStesso) Then Return False
            Dim info As AccessibilitaIA2.InfoIA2 = Nothing
            If Not AccessibilitaIA2.Leggi(accessibile, info) OrElse info.Attributi Is Nothing Then Return False
            Dim classi As String = ""
            If Not info.Attributi.TryGetValue("class", classi) OrElse classi Is Nothing Then Return False
            If (" " & classi & " ").IndexOf(" popup-menuitem ", StringComparison.OrdinalIgnoreCase) < 0 Then Return False
            Dim etichetta As String = ""
            If info.Attributi.TryGetValue("tag", etichetta) AndAlso Not String.IsNullOrEmpty(etichetta) AndAlso Not String.Equals(etichetta, "div", StringComparison.OrdinalIgnoreCase) Then Return False
            Dim proprio As String = Nothing
            Try
                proprio = accessibile.accName(FiglioSeStesso)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("AccessibilitaMsaa.VocePopupGecko", exAtteso)
                proprio = Nothing
            End Try
            nome = Pulisci(proprio)
            If nome = "" Then
                Dim primo As Object = Nothing
                Try
                    primo = accessibile.accChild(1)
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("AccessibilitaMsaa.VocePopupGecko", exAtteso2)
                    primo = Nothing
                End Try
                Dim delFiglio As String = Nothing
                Try
                    If TypeOf primo Is Accessibility.IAccessible Then
                        delFiglio = DirectCast(primo, Accessibility.IAccessible).accName(FiglioSeStesso)
                    Else
                        delFiglio = accessibile.accName(1)
                    End If
                Catch exAtteso3 As Exception
                    RegistroErrori.Scrivi("AccessibilitaMsaa.VocePopupGecko", exAtteso3)
                    delFiglio = Nothing
                End Try
                nome = Pulisci(delFiglio)
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.VocePopupGecko: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function TestoRegioneViva(finestra As IntPtr, oggetto As Integer, figlio As Integer, tipoEvento As UInteger, ByRef assertiva As Boolean) As String
        assertiva = False
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlioReale As Object = Nothing
            If AccessibleObjectFromEvent(finestra, oggetto, figlio, accessibile, figlioReale) < 0 OrElse accessibile Is Nothing Then Return ""
            If figlioReale IsNot Nothing AndAlso Not (TypeOf figlioReale Is Integer AndAlso CInt(figlioReale) = FiglioSeStesso) Then Return ""
            If (StatoProprio(accessibile) And &H8000L) <> 0L Then Return ""
            Dim info As AccessibilitaIA2.InfoIA2 = Nothing
            If Not AccessibilitaIA2.Leggi(accessibile, info) OrElse info.Attributi Is Nothing Then Return ""
            Dim vita As String = ValoreAttributo(info, "container-live")
            If vita <> "polite" AndAlso vita <> "assertive" AndAlso vita <> "rude" Then Return ""
            If (info.Stati And &H8) <> 0 Then Return ""
            If ValoreAttributo(info, "container-busy") = "true" Then Return ""
            Dim pertinente As String = ValoreAttributo(info, "container-relevant")
            Dim conTesto As Boolean = True
            Dim conAggiunte As Boolean = True
            If pertinente <> "" AndAlso pertinente <> "all" Then
                conTesto = pertinente.Contains("text")
                conAggiunte = pertinente.Contains("additions")
            End If
            If Not conTesto AndAlso Not conAggiunte Then Return ""
            If tipoEvento = EventoMostra AndAlso Not conAggiunte Then Return ""
            If (tipoEvento = EventoNome OrElse tipoEvento = EventoDescrizione) AndAlso Not conTesto Then Return ""
            Dim testo As String
            Dim atomico As Accessibility.IAccessible = RadiceAtomica(accessibile, info)
            If atomico IsNot Nothing Then
                testo = TestoNodo(atomico, 0)
            ElseIf tipoEvento = EventoNome Then
                testo = NomeProprio(accessibile)
            ElseIf tipoEvento = EventoDescrizione Then
                testo = DescrizionePropria(accessibile)
            Else
                testo = TestoNodo(accessibile, 0)
            End If
            testo = Pulisci(testo)
            If testo.Length > 600 Then testo = testo.Substring(0, 600)
            assertiva = vita <> "polite"
            Return testo
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.TestoRegioneViva: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function TestoAvvisoEvento(finestra As IntPtr, oggetto As Integer, figlio As Integer) As String
        Try
            If finestra = IntPtr.Zero Then Return ""
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlioReale As Object = Nothing
            If AccessibleObjectFromEvent(finestra, oggetto, figlio, accessibile, figlioReale) < 0 OrElse accessibile Is Nothing Then Return ""
            Dim testo As String
            If figlioReale IsNot Nothing AndAlso Not (TypeOf figlioReale Is Integer AndAlso CInt(figlioReale) = FiglioSeStesso) Then
                testo = Pulisci(accessibile.accName(figlioReale))
            Else
                If (StatoProprio(accessibile) And &H8000L) <> 0L Then Return ""
                testo = Pulisci(TestoNodo(accessibile, 0))
            End If
            If testo.Length > 600 Then testo = testo.Substring(0, 600)
            Return testo
        Catch ex As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.TestoAvvisoEvento", ex)
            Return ""
        End Try
    End Function

    Private Shared Function ValoreAttributo(info As AccessibilitaIA2.InfoIA2, chiave As String) As String
        Try
            Dim valore As String = Nothing
            If info.Attributi Is Nothing OrElse Not info.Attributi.TryGetValue(chiave, valore) OrElse valore Is Nothing Then Return ""
            Return valore.Trim().ToLowerInvariant()
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.ValoreAttributo: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function StatoProprio(accessibile As Accessibility.IAccessible) As Long
        Try
            Dim valore As Object = accessibile.accState(FiglioSeStesso)
            If valore Is Nothing Then Return 0L
            Return Convert.ToInt64(valore)
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.StatoProprio: " & ex.Message)
            Return 0L
        End Try
    End Function

    Private Shared Function NomeProprio(accessibile As Accessibility.IAccessible) As String
        Try
            Return Pulisci(accessibile.accName(FiglioSeStesso))
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.NomeProprio: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function DescrizionePropria(accessibile As Accessibility.IAccessible) As String
        Try
            Return Pulisci(accessibile.accDescription(FiglioSeStesso))
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.DescrizionePropria: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function RadiceAtomica(accessibile As Accessibility.IAccessible, info As AccessibilitaIA2.InfoIA2) As Accessibility.IAccessible
        Try
            If ValoreAttributo(info, "atomic") = "true" Then Return accessibile
            If ValoreAttributo(info, "container-atomic") <> "true" Then Return Nothing
            Dim corrente As Accessibility.IAccessible = accessibile
            For salto As Integer = 0 To 10
                Dim padre As Accessibility.IAccessible = TryCast(corrente.accParent, Accessibility.IAccessible)
                If padre Is Nothing Then Return Nothing
                Dim infoPadre As AccessibilitaIA2.InfoIA2 = Nothing
                If Not AccessibilitaIA2.Leggi(padre, infoPadre) OrElse infoPadre.Attributi Is Nothing Then Return Nothing
                If ValoreAttributo(infoPadre, "atomic") = "true" Then Return padre
                If ValoreAttributo(infoPadre, "container-atomic") <> "true" Then Return Nothing
                corrente = padre
            Next
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.RadiceAtomica: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Shared Function TestoNodo(accessibile As Accessibility.IAccessible, profondita As Integer) As String
        Try
            If accessibile Is Nothing Then Return ""
            Dim nome As String = NomeProprio(accessibile)
            If nome <> "" OrElse profondita >= 4 Then Return nome
            Dim quanti As Integer = 0
            Try
                quanti = accessibile.accChildCount
            Catch exConta As Exception
                log.ScriviLog("AccessibilitaMsaa.TestoNodo (figli): " & exConta.Message)
                quanti = 0
            End Try
            Dim parti As New List(Of String)()
            For indice As Integer = 1 To Math.Min(quanti, 30)
                Dim figlio As Object = Nothing
                Try
                    figlio = accessibile.accChild(indice)
                Catch exFiglio As Exception
                    log.ScriviLog("AccessibilitaMsaa.TestoNodo (figlio): " & exFiglio.Message)
                    figlio = Nothing
                End Try
                Dim parte As String = ""
                If TypeOf figlio Is Accessibility.IAccessible Then
                    parte = TestoNodo(DirectCast(figlio, Accessibility.IAccessible), profondita + 1)
                Else
                    Try
                        parte = Pulisci(accessibile.accName(indice))
                    Catch exNome As Exception
                        log.ScriviLog("AccessibilitaMsaa.TestoNodo (nome): " & exNome.Message)
                        parte = ""
                    End Try
                End If
                If parte <> "" Then parti.Add(parte)
            Next
            Return String.Join(" ", parti)
        Catch ex As Exception
            log.ScriviLog("AccessibilitaMsaa.TestoNodo: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function DescriviFuoco(finestra As IntPtr) As String
        Try
            Dim accessibile As Accessibility.IAccessible = Client(finestra)
            If accessibile Is Nothing Then Return ""
            Dim fuoco As Object = Nothing
            Try
                fuoco = accessibile.accFocus
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviFuoco", exAtteso)
                fuoco = Nothing
            End Try
            If TypeOf fuoco Is Accessibility.IAccessible Then Return DescriviAccessibile(DirectCast(fuoco, Accessibility.IAccessible), FiglioSeStesso)
            If fuoco IsNot Nothing AndAlso Not TypeOf fuoco Is String Then Return DescriviAccessibile(accessibile, fuoco)
            Return DescriviAccessibile(accessibile, FiglioSeStesso)
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviFuoco", exAtteso2)
            Return ""
        End Try
    End Function

    Public Shared Function DescriviElemento(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim dalPattern As String = DescriviDaPattern(elemento)
            If dalPattern <> "" Then Return dalPattern
            Dim hwnd As IntPtr = IntPtr.Zero
            Try
                hwnd = CType(elemento.CurrentNativeWindowHandle, IntPtr)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviElemento", exAtteso)
                hwnd = IntPtr.Zero
            End Try
            If hwnd = IntPtr.Zero Then Return ""
            Return DescriviFuoco(hwnd)
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviElemento", exAtteso2)
            Return ""
        End Try
    End Function

    Public Shared Function Naviga(elemento As IUIAutomationElement, versoContenitore As Boolean, versoContenuto As Boolean, avanti As Boolean) As String
        Try
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlio As Object = FiglioSeStesso
            If Not ProvaAccessibile(elemento, accessibile, figlio) Then
                accessibile = _ultimoAccessibile
                figlio = _ultimoFiglio
            End If
            If accessibile Is Nothing Then Return ""
            If figlio Is Nothing Then figlio = FiglioSeStesso
            Dim direzione As Integer = VersoSuccessivo
            If versoContenitore Then
                direzione = versoContenitore
            ElseIf versoContenuto Then
                direzione = versoContenuto
            ElseIf Not avanti Then
                direzione = VersoPrecedente
            End If
            Dim destinazione As Object = accessibile.accNavigate(direzione, figlio)
            If destinazione Is Nothing Then Return ""
            If TypeOf destinazione Is Accessibility.IAccessible Then
                _ultimoAccessibile = DirectCast(destinazione, Accessibility.IAccessible)
                _ultimoFiglio = FiglioSeStesso
                Return DescriviAccessibile(_ultimoAccessibile, FiglioSeStesso)
            End If
            _ultimoAccessibile = accessibile
            _ultimoFiglio = destinazione
            Return DescriviAccessibile(accessibile, destinazione)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.Naviga", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function DescriviDaPattern(elemento As IUIAutomationElement) As String
        Try
            Dim accessibile As Accessibility.IAccessible = Nothing
            Dim figlio As Object = FiglioSeStesso
            If ProvaAccessibile(elemento, accessibile, figlio) Then
                _ultimoAccessibile = accessibile
                _ultimoFiglio = figlio
                Dim fuoco As Object = Nothing
                Try
                    fuoco = accessibile.accFocus
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviDaPattern", exAtteso)
                    fuoco = Nothing
                End Try
                If TypeOf fuoco Is Accessibility.IAccessible AndAlso Not Object.ReferenceEquals(fuoco, accessibile) Then
                    Return DescriviAccessibile(DirectCast(fuoco, Accessibility.IAccessible), FiglioSeStesso)
                End If
                Return DescriviAccessibile(accessibile, figlio)
            End If
            Dim schema As IUIAutomationLegacyIAccessiblePattern = Pattern(elemento)
            If schema Is Nothing Then Return ""
            Return UnisciPattern(schema)
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviDaPattern", exAtteso2)
            Return ""
        End Try
    End Function

    Private Shared Function ProvaAccessibile(elemento As IUIAutomationElement, ByRef accessibile As Accessibility.IAccessible, ByRef figlio As Object) As Boolean
        accessibile = Nothing
        figlio = FiglioSeStesso
        Try
            If elemento Is Nothing Then Return False
            Dim schema As IUIAutomationLegacyIAccessiblePattern = Pattern(elemento)
            If schema Is Nothing Then Return False
            Try
                figlio = schema.CurrentChildId
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("AccessibilitaMsaa.ProvaAccessibile", exAtteso)
                figlio = FiglioSeStesso
            End Try
            If figlio Is Nothing Then figlio = FiglioSeStesso
            Dim hwnd As IntPtr = FinestraVicina(elemento)
            If hwnd = IntPtr.Zero Then Return False
            accessibile = Client(hwnd)
            Return accessibile IsNot Nothing
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.ProvaAccessibile", exAtteso2)
            Return False
        End Try
    End Function

    Private Shared _camminatore As IUIAutomationTreeWalker

    Private Shared Function Camminatore() As IUIAutomationTreeWalker
        If _camminatore Is Nothing Then
            Try
                _camminatore = New CUIAutomation().ControlViewWalker
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("AccessibilitaMsaa.Camminatore", exAtteso)
                _camminatore = Nothing
            End Try
        End If
        Return _camminatore
    End Function

    Private Shared Function FinestraVicina(elemento As IUIAutomationElement) As IntPtr
        Try
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 6
                If corrente Is Nothing Then Exit For
                Dim hwnd As IntPtr = IntPtr.Zero
                Try
                    hwnd = CType(corrente.CurrentNativeWindowHandle, IntPtr)
                Catch exAtteso As Exception
                    RegistroErrori.Scrivi("AccessibilitaMsaa.FinestraVicina", exAtteso)
                    hwnd = IntPtr.Zero
                End Try
                If hwnd <> IntPtr.Zero Then Return hwnd
                Dim risalita As IUIAutomationTreeWalker = Camminatore()
                If risalita Is Nothing Then Exit For
                corrente = risalita.GetParentElement(corrente)
            Next
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.FinestraVicina", exAtteso2)
        End Try
        Return IntPtr.Zero
    End Function

    Private Shared Function Pattern(elemento As IUIAutomationElement) As IUIAutomationLegacyIAccessiblePattern
        Try
            If elemento Is Nothing Then Return Nothing
            Return TryCast(elemento.GetCurrentPattern(SchemaAccessibilita), IUIAutomationLegacyIAccessiblePattern)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.Pattern", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function UnisciPattern(pattern As IUIAutomationLegacyIAccessiblePattern) As String
        Try
            Dim nome As String = Pulisci(pattern.CurrentName)
            Dim valore As String = Pulisci(pattern.CurrentValue)
            Dim descrizione As String = Pulisci(pattern.CurrentDescription)
            Dim ruolo As String = NomeRuolo(pattern.CurrentRole)
            Dim stato As String = NomeStato(pattern.CurrentState)
            Return Componi(nome, ruolo, valore, descrizione, stato)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.UnisciPattern", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function Pulisci(testo As String) As String
        If testo Is Nothing Then Return ""
        Return testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
    End Function

    Private Shared Function Componi(nome As String, ruolo As String, valore As String, descrizione As String, stato As String) As String
        If DescrittoreElementi.RuoloGenerico(ruolo) Then ruolo = ""
        If nome = "" AndAlso ruolo = "" AndAlso valore = "" AndAlso descrizione = "" Then Return ""
        If nome.Length > 300 Then nome = nome.Substring(0, 300)
        If valore.Length > 200 Then valore = valore.Substring(0, 200)
        If descrizione.Length > 160 Then descrizione = descrizione.Substring(0, 160)
        Dim parti As New List(Of String)()
        If nome <> "" Then parti.Add(nome)
        If ruolo <> "" AndAlso Not String.Equals(ruolo, nome, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(ruolo)
        If valore <> "" AndAlso Not String.Equals(valore, nome, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(valore)
        If descrizione <> "" AndAlso Not String.Equals(descrizione, nome, StringComparison.CurrentCultureIgnoreCase) AndAlso Not String.Equals(descrizione, valore, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(descrizione)
        If stato <> "" Then parti.Add(stato)
        Return String.Join(", ", parti)
    End Function

    Public Shared Function Valore(elemento As IUIAutomationElement) As String
        Try
            Dim frase As String = DescriviElemento(elemento)
            Return frase
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.Valore", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function Client(finestra As IntPtr) As Accessibility.IAccessible
        Try
            If finestra = IntPtr.Zero Then Return Nothing
            Dim oggetto As Object = Nothing
            Dim iid As Guid = IidAccessibile
            Dim esito As Integer = AccessibleObjectFromWindow(finestra, OggettoClient, iid, oggetto)
            If esito < 0 Then Return Nothing
            Return TryCast(oggetto, Accessibility.IAccessible)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.Client", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Shared Function DescriviAccessibile(accessibile As Accessibility.IAccessible, figlio As Object) As String
        Try
            If accessibile Is Nothing Then Return ""
            If figlio Is Nothing Then figlio = FiglioSeStesso
            Dim nome As String = LeggiStringa(accessibile, figlio, 1)
            Dim valore As String = LeggiStringa(accessibile, figlio, 2)
            Dim descrizione As String = LeggiStringa(accessibile, figlio, 3)
            Dim ruolo As String = NomeRuolo(LeggiOggetto(accessibile, figlio, 4))
            Dim stato As String = NomeStato(LeggiOggetto(accessibile, figlio, 5))
            If DescrittoreElementi.RuoloGenerico(ruolo) Then ruolo = ""
            If nome = "" AndAlso ruolo = "" AndAlso valore = "" AndAlso descrizione = "" Then Return ""
            If nome.Length > 300 Then nome = nome.Substring(0, 300)
            If valore.Length > 200 Then valore = valore.Substring(0, 200)
            If descrizione.Length > 160 Then descrizione = descrizione.Substring(0, 160)
            Dim parti As New List(Of String)()
            If nome <> "" Then parti.Add(nome)
            If ruolo <> "" AndAlso Not String.Equals(ruolo, nome, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(ruolo)
            If valore <> "" AndAlso Not String.Equals(valore, nome, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(valore)
            If descrizione <> "" AndAlso Not String.Equals(descrizione, nome, StringComparison.CurrentCultureIgnoreCase) AndAlso Not String.Equals(descrizione, valore, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(descrizione)
            If stato <> "" Then parti.Add(stato)
            If TypeOf figlio Is Integer AndAlso CInt(figlio) = FiglioSeStesso Then
                Dim info As AccessibilitaIA2.InfoIA2 = Nothing
                If AccessibilitaIA2.Leggi(accessibile, info) Then
                    Dim extra As String = AccessibilitaIA2.Frase(info)
                    If extra <> "" Then parti.Add(extra)
                End If
            End If
            Return String.Join(", ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.DescriviAccessibile", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function LeggiStringa(accessibile As Accessibility.IAccessible, figlio As Object, quale As Integer) As String
        Try
            Dim testo As String = TryCast(LeggiOggetto(accessibile, figlio, quale), String)
            If testo Is Nothing Then Return ""
            Return testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
        Catch ex As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.LeggiStringa", ex)
            Return ""
        End Try
    End Function

    Private Shared Function LeggiOggetto(accessibile As Accessibility.IAccessible, figlio As Object, quale As Integer) As Object
        Try
            If accessibile Is Nothing Then Return Nothing
            Select Case quale
                Case 1
                    Return accessibile.accName(figlio)
                Case 2
                    Return accessibile.accValue(figlio)
                Case 3
                    Return accessibile.accDescription(figlio)
                Case 4
                    Return accessibile.accRole(figlio)
                Case 5
                    Return accessibile.accState(figlio)
            End Select
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.LeggiOggetto", ex)
            Return Nothing
        End Try
    End Function

    Private Shared Function NomeRuolo(ruolo As Object) As String
        Try
            If ruolo Is Nothing Then Return ""
            If TypeOf ruolo Is String Then
                Dim testo As String = CStr(ruolo).Trim()
                If testo <> "" Then Return testo
            End If
            Dim numero As Integer
            If Not Integer.TryParse(Convert.ToString(ruolo), numero) OrElse numero <= 0 Then Return ""
            Select Case numero
                Case 2 : Return "barra dei menu"
                Case 3 : Return "barra di scorrimento"
                Case 9 : Return "finestra"
                Case 10 : Return ""
                Case 11 : Return "menu"
                Case 12 : Return "voce di menu"
                Case 15 : Return "documento"
                Case 16 : Return "riquadro"
                Case 17 : Return "grafico"
                Case 18 : Return "finestra di dialogo"
                Case 19 : Return "separatore"
                Case 20 : Return "gruppo"
                Case 21 : Return "separatore"
                Case 22 : Return "barra degli strumenti"
                Case 23 : Return "barra di stato"
                Case 24 : Return "tabella"
                Case 25 : Return "intestazione di colonna"
                Case 26 : Return "intestazione di riga"
                Case 27 : Return "colonna"
                Case 28 : Return "riga"
                Case 29 : Return "cella"
                Case 30 : Return "link"
                Case 33 : Return "elenco"
                Case 34 : Return "voce di elenco"
                Case 35 : Return "albero"
                Case 36 : Return "voce di albero"
                Case 37 : Return "scheda"
                Case 40 : Return "immagine"
                Case 41 : Return "testo"
                Case 42 : Return "campo di modifica"
                Case 43 : Return "pulsante"
                Case 44 : Return "casella di controllo"
                Case 45 : Return "pulsante di opzione"
                Case 46 : Return "casella combinata"
                Case 47 : Return "elenco a discesa"
                Case 48 : Return "barra di avanzamento"
                Case 49 : Return "quadrante"
                Case 50 : Return "tasto di scelta rapida"
                Case 51 : Return "dispositivo di scorrimento"
                Case 52 : Return "selettore numerico"
                Case 53 : Return "diagramma"
                Case 54 : Return "animazione"
                Case 55 : Return "equazione"
                Case 56 : Return "pulsante con elenco"
                Case 57 : Return "pulsante con menu"
                Case 58 : Return "griglia a discesa"
                Case 60 : Return "gruppo di schede"
                Case 61 : Return "orologio"
                Case 62 : Return "pulsante diviso"
                Case 63 : Return "indirizzo ip"
            End Select
            Dim nativo As New StringBuilder(80)
            If GetRoleText(numero, nativo, nativo.Capacity) > 0 Then Return nativo.ToString().Trim()
            Return ""
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.NomeRuolo", exAtteso)
            Return ""
        End Try
    End Function

    Private Shared Function NomeStato(stato As Object) As String
        Try
            If stato Is Nothing Then Return ""
            Dim maschera As Integer
            If Not Integer.TryParse(Convert.ToString(stato), maschera) Then Return ""
            Dim parti As New List(Of String)()
            If (maschera And &H1) <> 0 Then parti.Add("non disponibile")
            If (maschera And &H2) <> 0 Then parti.Add("selezionato")
            If (maschera And &H8) <> 0 Then parti.Add("premuto")
            If (maschera And &H10) <> 0 Then parti.Add("spuntato")
            If (maschera And &H20) <> 0 Then parti.Add("parziale")
            If (maschera And &H40) <> 0 Then parti.Add("sola lettura")
            If (maschera And &H80) <> 0 Then parti.Add("evidenziato")
            If (maschera And &H200) <> 0 Then parti.Add("espanso")
            If (maschera And &H400) <> 0 Then parti.Add("compresso")
            If (maschera And &H800) <> 0 Then parti.Add("occupato")
            If (maschera And &H20000) <> 0 Then parti.Add("fuori schermo")
            If (maschera And &H400000) <> 0 Then parti.Add("collegamento")
            If (maschera And &H800000) <> 0 Then parti.Add("già visitato")
            If (maschera And &H40000000) <> 0 Then parti.Add("con menu")
            If (maschera And &H20000000) <> 0 Then parti.Add("protetto")
            Return String.Join(", ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("AccessibilitaMsaa.NomeStato", exAtteso)
            Return ""
        End Try
    End Function

End Class
