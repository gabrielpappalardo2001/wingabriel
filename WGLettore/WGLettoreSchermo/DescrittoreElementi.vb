Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Globalization
Imports UIAutomationClient

' Estrazione e formattazione delle proprietà, dei ruoli e degli stati degli elementi dell'interfaccia utente.
Friend NotInheritable Class DescrittoreElementi

    Public Const ProprietaRettangolo As Integer = 30001
    Public Const ProprietaIdProcesso As Integer = 30002
    Public Const ProprietaTipoControllo As Integer = 30003
    Public Const ProprietaTipoControlloLocalizzato As Integer = 30004
    Public Const ProprietaNome As Integer = 30005
    Public Const ProprietaAbilitato As Integer = 30010
    Public Const ProprietaIdAutomazione As Integer = 30011
    Public Const ProprietaNomeClasse As Integer = 30012
    Public Const ProprietaPassword As Integer = 30019
    Public Const ProprietaFramework As Integer = 30024
    Public Const ProprietaEspansioneDisponibile As Integer = 30028
    Public Const ProprietaIntervalloDisponibile As Integer = 30033
    Public Const ProprietaSelezioneDisponibile As Integer = 30036
    Public Const ProprietaPatternSelezione As Integer = 30037
    Public Const ProprietaVoceFoglio As Integer = 30132
    Public Const ProprietaVoceGriglia As Integer = 30029
    Public Const ProprietaPatternGriglia As Integer = 30030
    Public Const ProprietaPatternTabella As Integer = 30038
    Public Const ProprietaVoceTabella As Integer = 30039
    Public Const ProprietaTestoDisponibile As Integer = 30040
    Public Const ProprietaAttivazioneDisponibile As Integer = 30041
    Public Const ProprietaValoreDisponibile As Integer = 30043
    Public Const ProprietaValore As Integer = 30045
    Public Const ProprietaValoreIntervallo As Integer = 30047
    Public Const ProprietaStatoEspansione As Integer = 30070
    Public Const ProprietaSelezionato As Integer = 30079
    Public Const ProprietaStatoAttivazione As Integer = 30086
    Public Const ProprietaPosizione As Integer = 30152
    Public Const ProprietaTotale As Integer = 30153
    Public Const ProprietaAiuto As Integer = 30013
    Public Const ProprietaEtichettatoDa As Integer = 30018
    Public Const ProprietaManiglia As Integer = 30020
    Public Const ProprietaDescrizioneProvider As Integer = 30107
    Public Const ProprietaTastoAccesso As Integer = 30007
    Public Const ProprietaScorciatoia As Integer = 30006
    Public Const ProprietaFuocoTastiera As Integer = 30009
    Public Const ProprietaSolaLettura As Integer = 30046
    Private Const AttributoSolaLetturaTesto As Integer = 40015
    Public Const ProprietaObbligatorio As Integer = 30025
    Public Const ProprietaDatoValido As Integer = 30103
    Public Const ProprietaRuoloAria As Integer = 30101
    Public Const ProprietaProprietaAria As Integer = 30102
    Public Const ProprietaLivello As Integer = 30154
    Public Const ProprietaTipoRegione As Integer = 30157
    Public Const ProprietaRegioneLocalizzata As Integer = 30158
    Public Const ProprietaDescrizioneCompleta As Integer = 30159
    Public Const ProprietaLivelloTitolo As Integer = 30173
    Public Const ProprietaFuoriSchermo As Integer = 30022
    Public Const ProprietaImpostazioneVita As Integer = 30135
    Public Const ProprietaTipoVoce As Integer = 30021

    Public Const SchemaTesto As Integer = 10014
    Public Const SchemaTesto2 As Integer = 10024

    Public Const ControlloPulsante As Integer = 50000
    Public Const ControlloCalendario As Integer = 50001
    Public Const ControlloCasellaControllo As Integer = 50002
    Public Const ControlloCasellaCombinata As Integer = 50003
    Public Const ControlloModifica As Integer = 50004
    Public Const ControlloCollegamento As Integer = 50005
    Public Const ControlloImmagine As Integer = 50006
    Public Const ControlloElementoElenco As Integer = 50007
    Public Const ControlloElenco As Integer = 50008
    Public Const ControlloMenu As Integer = 50009
    Public Const ControlloBarraMenu As Integer = 50010
    Public Const ControlloVoceMenu As Integer = 50011
    Public Const ControlloBarraAvanzamento As Integer = 50012
    Public Const ControlloPulsanteOpzione As Integer = 50013
    Public Const ControlloBarraScorrimento As Integer = 50014
    Public Const ControlloDispositivoScorrimento As Integer = 50015
    Public Const ControlloSelettoreNumerico As Integer = 50016
    Public Const ControlloBarraStato As Integer = 50017
    Public Const ControlloSchede As Integer = 50018
    Public Const ControlloScheda As Integer = 50019
    Public Const ControlloTesto As Integer = 50020
    Public Const ControlloBarraStrumenti As Integer = 50021
    Public Const ControlloDescrizioneComando As Integer = 50022
    Public Const ControlloAlbero As Integer = 50023
    Public Const ControlloElementoAlbero As Integer = 50024
    Public Const ControlloPersonalizzato As Integer = 50025
    Public Const ControlloGruppo As Integer = 50026
    Public Const ControlloCursore As Integer = 50027
    Public Const ControlloGrigliaDati As Integer = 50028
    Public Const ControlloElementoDati As Integer = 50029
    Public Const ControlloDocumento As Integer = 50030
    Public Const ControlloPulsanteDiviso As Integer = 50031
    Public Const ControlloFinestra As Integer = 50032
    Public Const ControlloRiquadro As Integer = 50033
    Public Const ControlloIntestazione As Integer = 50034
    Public Const ControlloElementoIntestazione As Integer = 50035
    Public Const ControlloTabella As Integer = 50036
    Public Const ControlloBarraTitolo As Integer = 50037
    Public Const ControlloSeparatore As Integer = 50038
    Public Const ControlloZoomSemantico As Integer = 50039
    Public Const ControlloBarraApplicazioni As Integer = 50040

    Public Shared ReadOnly ProprietaRichieste As Integer() = {
        ProprietaRettangolo,
        ProprietaIdProcesso,
        ProprietaTipoControllo,
        ProprietaTipoControlloLocalizzato,
        ProprietaNome,
        ProprietaAbilitato,
        ProprietaIdAutomazione,
        ProprietaNomeClasse,
        ProprietaPassword,
        ProprietaFramework,
        ProprietaEspansioneDisponibile,
        ProprietaIntervalloDisponibile,
        ProprietaSelezioneDisponibile,
        ProprietaTestoDisponibile,
        ProprietaAttivazioneDisponibile,
        ProprietaValoreDisponibile,
        ProprietaValore,
        ProprietaValoreIntervallo,
        ProprietaStatoEspansione,
        ProprietaSelezionato,
        ProprietaStatoAttivazione,
        ProprietaPosizione,
        ProprietaTotale,
        ProprietaAiuto,
        ProprietaEtichettatoDa,
        ProprietaManiglia,
        ProprietaDescrizioneProvider,
        ProprietaTastoAccesso,
        ProprietaScorciatoia,
        ProprietaFuocoTastiera,
        ProprietaSolaLettura,
        ProprietaObbligatorio,
        ProprietaRuoloAria,
        ProprietaProprietaAria,
        ProprietaLivello,
        ProprietaTipoRegione,
        ProprietaRegioneLocalizzata,
        ProprietaDescrizioneCompleta,
        ProprietaLivelloTitolo,
        ProprietaPatternSelezione,
        ProprietaVoceFoglio,
        ProprietaVoceGriglia,
        ProprietaPatternGriglia,
        ProprietaPatternTabella,
        ProprietaVoceTabella,
        ProprietaFuoriSchermo,
        ProprietaImpostazioneVita,
        ProprietaTipoVoce
    }

    Private Const LunghezzaMassimaValore As Integer = 500

    Public Shared Property DireLivelloTitoli As Boolean = True

    Public Shared Property LinkPrimaDelNome As Boolean = True

    Public Shared Property ProlissitaApplicata As Integer = 1

    Public Property RisolutoreNomi As System.Func(Of IUIAutomationElement, String)

    Private ReadOnly log As New Logger()
    Private ReadOnly _proprietaDisponibili As New HashSet(Of Integer)()
    Private ReadOnly _blocco As New Object()

    Public Sub RegistraProprieta(idProprieta As Integer)
        Try
            SyncLock _blocco
                _proprietaDisponibili.Add(idProprieta)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.RegistraProprieta: " & ex.Message)
        End Try
    End Sub

    Private Function ProprietaRegistrata(idProprieta As Integer) As Boolean
        Try
            SyncLock _blocco
                Return _proprietaDisponibili.Contains(idProprieta)
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ProprietaRegistrata: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function LeggiValore(elemento As IUIAutomationElement, idProprieta As Integer) As Object
        If elemento Is Nothing Then Return Nothing
        If Not ProprietaRegistrata(idProprieta) Then Return Nothing
        Try
            Return elemento.GetCachedPropertyValue(idProprieta)
        Catch exCache As ArgumentException
            RegistroErrori.Scrivi("DescrittoreElementi.LeggiValore", exCache)
        Catch exCache As System.Runtime.InteropServices.COMException
            RegistroErrori.Scrivi("DescrittoreElementi.LeggiValore", exCache)
        Catch exCache As InvalidOperationException
            RegistroErrori.Scrivi("DescrittoreElementi.LeggiValore", exCache)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LeggiValore (" & idProprieta.ToString() & "): " & ex.Message)
            Return Nothing
        End Try
        Try
            Return elemento.GetCurrentPropertyValue(idProprieta)
        Catch ex As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.LeggiValore", ex)
            Return Nothing
        End Try
    End Function

    Public Function LeggiStringa(elemento As IUIAutomationElement, idProprieta As Integer) As String
        Try
            Dim valore As Object = LeggiValore(elemento, idProprieta)
            If TypeOf valore Is String Then Return TogliControlliDirezione(CStr(valore)).Trim()
            Return ""
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LeggiStringa: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared ReadOnly ControlliDirezione As Char() = {ChrW(&H200E), ChrW(&H200F), ChrW(&H202A), ChrW(&H202B), ChrW(&H202C), ChrW(&H202D), ChrW(&H202E), ChrW(&H2066), ChrW(&H2067), ChrW(&H2068), ChrW(&H2069), ChrW(&HFEFF)}

    Public Shared Function TogliControlliDirezione(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) OrElse testo.IndexOfAny(ControlliDirezione) < 0 Then Return testo
            Dim pulito As New System.Text.StringBuilder(testo.Length)
            For Each carattere As Char In testo
                If Array.IndexOf(ControlliDirezione, carattere) < 0 Then pulito.Append(carattere)
            Next
            Return pulito.ToString()
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.TogliControlliDirezione", exAtteso)
            Return testo
        End Try
    End Function

    Public Function LeggiIntero(elemento As IUIAutomationElement, idProprieta As Integer, predefinito As Integer) As Integer
        Try
            Dim valore As Object = LeggiValore(elemento, idProprieta)
            If valore Is Nothing Then Return predefinito
            If TypeOf valore Is Boolean Then Return If(CBool(valore), 1, 0)
            If TypeOf valore Is Integer OrElse TypeOf valore Is Short OrElse TypeOf valore Is Long OrElse TypeOf valore Is Byte OrElse TypeOf valore Is Double OrElse TypeOf valore Is Decimal Then
                Return Convert.ToInt32(valore)
            End If
            Return predefinito
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LeggiIntero: " & ex.Message)
            Return predefinito
        End Try
    End Function

    Public Function LeggiBooleano(elemento As IUIAutomationElement, idProprieta As Integer) As Boolean
        Try
            Dim valore As Object = LeggiValore(elemento, idProprieta)
            If TypeOf valore Is Boolean Then Return CBool(valore)
            If TypeOf valore Is Integer OrElse TypeOf valore Is Short OrElse TypeOf valore Is Long Then Return Convert.ToInt64(valore) <> 0
            Return False
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LeggiBooleano: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function BooleanoFalso(elemento As IUIAutomationElement, idProprieta As Integer) As Boolean
        Try
            Dim valore As Object = LeggiValore(elemento, idProprieta)
            If TypeOf valore Is Boolean Then Return Not CBool(valore)
            Return False
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.BooleanoFalso: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LeggiDouble(elemento As IUIAutomationElement, idProprieta As Integer, ByRef risultato As Double) As Boolean
        Try
            Dim valore As Object = LeggiValore(elemento, idProprieta)
            If TypeOf valore Is Double Then
                risultato = CDbl(valore)
                Return True
            End If
            Return False
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LeggiDouble: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function Nome(elemento As IUIAutomationElement) As String
        Try
            Dim testo As String = LeggiStringa(elemento, ProprietaNome)
            If testo.IndexOf("&"c) >= 0 AndAlso ControlloConMnemonicoWx(elemento) Then testo = TogliMnemonico(testo)
            Return testo
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.Nome: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared ReadOnly _processiWx As New Dictionary(Of Integer, Boolean)()
    Private Shared ReadOnly _bloccoWx As New Object()

    Public Function ApplicazioneWx(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim pid As Integer = IdProcesso(elemento)
            If pid <= 0 Then Return False
            SyncLock _bloccoWx
                Dim noto As Boolean
                If _processiWx.TryGetValue(pid, noto) Then Return noto
            End SyncLock
            Dim valore As Object = LeggiValore(elemento, ProprietaManiglia)
            Dim maniglia As IntPtr = IntPtr.Zero
            If TypeOf valore Is Integer Then maniglia = New IntPtr(CInt(valore))
            If maniglia = IntPtr.Zero Then Return False
            Dim radice As IntPtr = NativoLettore.GetAncestor(maniglia, NativoLettore.GA_ROOT)
            If radice = IntPtr.Zero Then radice = maniglia
            Dim classe As New System.Text.StringBuilder(256)
            NativoLettore.GetClassName(radice, classe, classe.Capacity)
            Dim esito As Boolean = classe.ToString().StartsWith("wxWindow", StringComparison.Ordinal)
            SyncLock _bloccoWx
                If _processiWx.Count > 500 Then _processiWx.Clear()
                _processiWx(pid) = esito
            End SyncLock
            Return esito
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ApplicazioneWx: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function ControlloConMnemonicoWx(elemento As IUIAutomationElement) As Boolean
        Try
            Select Case TipoControllo(elemento)
                Case ControlloVoceMenu, ControlloMenu, ControlloBarraMenu
                    Return False
            End Select
            Dim classe As String = LeggiStringa(elemento, ProprietaNomeClasse)
            If classe <> "Button" AndAlso classe <> "Static" Then Return False
            Return ApplicazioneWx(elemento)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ControlloConMnemonicoWx: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function TogliMnemonico(testo As String) As String
        Try
            Dim segnaposto As String = ChrW(&HE000)
            Return testo.Replace("&&", segnaposto).Replace("&", "").Replace(segnaposto, "&")
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.TogliMnemonico", exAtteso)
            Return testo
        End Try
    End Function

    Public Function NomeEsteso(elemento As IUIAutomationElement) As String
        Try
            Dim letto As String = Nome(elemento)
            If letto <> "" Then Return letto
            Dim risolvi As System.Func(Of IUIAutomationElement, String) = RisolutoreNomi
            If risolvi Is Nothing Then Return ""
            Dim esteso As String = risolvi(elemento)
            If esteso Is Nothing Then Return ""
            Return esteso.Trim()
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.NomeEsteso: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function TipoControllo(elemento As IUIAutomationElement) As Integer
        Try
            Dim tipo As Integer = LeggiIntero(elemento, ProprietaTipoControllo, 0)
            If String.Equals(LeggiStringa(elemento, ProprietaIdAutomazione), "LineNumberBox", StringComparison.OrdinalIgnoreCase) Then Return ControlloModifica
            Return tipo
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.TipoControllo: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function IdProcesso(elemento As IUIAutomationElement) As Integer
        Try
            Return LeggiIntero(elemento, ProprietaIdProcesso, 0)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.IdProcesso: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function HaPatternTesto(elemento As IUIAutomationElement) As Boolean
        Try
            Return LeggiBooleano(elemento, ProprietaTestoDisponibile)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.HaPatternTesto: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function PatternTestoDiretto(elemento As IUIAutomationElement) As Object
        Try
            Dim pattern As Object = elemento.GetCurrentPattern(SchemaTesto)
            If pattern Is Nothing Then pattern = elemento.GetCurrentPattern(SchemaTesto2)
            Return pattern
        Catch ex As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.PatternTestoDiretto", ex)
            Return Nothing
        End Try
    End Function

    Public Function HaValore(elemento As IUIAutomationElement) As Boolean
        Try
            Return LeggiBooleano(elemento, ProprietaValoreDisponibile)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.HaValore: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function EditabileConTesto(elemento As IUIAutomationElement) As Boolean
        Try
            If HaPatternTesto(elemento) Then Return True
            If RegoleRuoli.NormalizzaFramework(Framework(elemento)) = "xaml" Then
                If PatternTestoDiretto(elemento) IsNot Nothing Then Return True
            End If
            Dim tipo As Integer = TipoControllo(elemento)
            Return tipo = ControlloModifica OrElse tipo = ControlloDocumento OrElse tipo = ControlloCasellaCombinata
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.EditabileConTesto: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function ECampoScrittura(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If Protetto(elemento) Then Return True
            If CampoSolaLettura(elemento) Then Return False
            Dim tipo As Integer = TipoControllo(elemento)
            If tipo = ControlloModifica OrElse tipo = ControlloCasellaCombinata OrElse tipo = ControlloSelettoreNumerico Then Return True
            Dim aria As String = RuoloAria(elemento)
            Return aria = "textbox" OrElse aria = "searchbox" OrElse aria = "combobox" OrElse aria = "spinbutton"
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ECampoScrittura: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function CampoSolaLettura(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If EditorVisualStudio(elemento) Then Return False
            If SolaLettura(elemento) Then Return True
            Return ValoreAria(elemento, "readonly") = "true"
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.CampoSolaLettura: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function EditorVisualStudio(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Return String.Equals(LeggiStringa(elemento, ProprietaNomeClasse), "WpfTextView", StringComparison.Ordinal)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.EditorVisualStudio: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function SolaLettura(elemento As IUIAutomationElement) As Boolean
        Try
            If Not LeggiBooleano(elemento, ProprietaSolaLettura) Then Return False
            Return Not ModificabileSecondoTesto(elemento)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.SolaLettura: " & ex.Message)
            Return False
        End Try
    End Function

    ' Alcuni campi (la casella dei messaggi di Unigram, le RichEditBox di XAML) dichiarano la sola lettura sul valore ma il testo, che è
    ' quello che conta, dice di essere modificabile: in quel caso il campo si scrive.
    Private Function ModificabileSecondoTesto(elemento As IUIAutomationElement) As Boolean
        Try
            If Not HaPatternTesto(elemento) Then Return False
            Dim tipo As Integer = TipoControllo(elemento)
            If tipo <> ControlloModifica AndAlso tipo <> ControlloDocumento Then Return False
            Dim modello As IUIAutomationTextPattern = TryCast(PatternTestoDiretto(elemento), IUIAutomationTextPattern)
            If modello Is Nothing Then Return False
            Dim valore As Object = modello.DocumentRange.GetAttributeValue(AttributoSolaLetturaTesto)
            Return TypeOf valore Is Boolean AndAlso Not CBool(valore)
        Catch ex As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.ModificabileSecondoTesto", ex)
            Return False
        End Try
    End Function

    Public Function FuocoDaTastiera(elemento As IUIAutomationElement) As Boolean
        Try
            Return LeggiBooleano(elemento, ProprietaFuocoTastiera)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.FuocoDaTastiera: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function NomeCorrente(elemento As IUIAutomationElement) As String
        Try
            Dim nomeCache As String = Nome(elemento)
            If nomeCache <> "" Then Return nomeCache
            If elemento Is Nothing Then Return ""
            Dim nomeVivo As String = elemento.CurrentName
            If nomeVivo Is Nothing Then Return ""
            Return nomeVivo.Trim()
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.NomeCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function RuoloAria(elemento As IUIAutomationElement) As String
        Try
            Return LeggiStringa(elemento, ProprietaRuoloAria).ToLowerInvariant()
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.RuoloAria: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function FuoriSchermo(elemento As IUIAutomationElement) As Boolean
        Try
            Return LeggiBooleano(elemento, ProprietaFuoriSchermo)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.FuoriSchermo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function ValoreAria(elemento As IUIAutomationElement, chiave As String) As String
        Try
            Dim proprieta As String = LeggiStringa(elemento, ProprietaProprietaAria)
            If proprieta = "" Then Return ""
            For Each parte As String In proprieta.Split(";"c)
                Dim voce As String = parte.Trim()
                If voce.StartsWith(chiave & "=", StringComparison.OrdinalIgnoreCase) Then
                    Return voce.Substring(chiave.Length + 1).Trim().ToLowerInvariant()
                End If
            Next
            Return ""
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ValoreAria: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function ImpostazioneVita(elemento As IUIAutomationElement) As Integer
        Try
            Dim valore As Integer = LeggiIntero(elemento, ProprietaImpostazioneVita, 0)
            If valore > 0 Then Return Math.Min(valore, 2)
            Dim aria As String = ValoreAria(elemento, "live")
            If aria = "assertive" Then Return 2
            If aria = "polite" Then Return 1
            Dim ruolo As String = RuoloAria(elemento)
            If ruolo = "alert" Then Return 2
            If ruolo = "status" OrElse ruolo = "log" Then Return 1
            Return 0
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ImpostazioneVita: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function Framework(elemento As IUIAutomationElement) As String
        Try
            Return LeggiStringa(elemento, ProprietaFramework).ToLowerInvariant()
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.Framework: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function FrameworkWeb(framework As String) As Boolean
        Select Case framework
            Case "chrome", "mozilla", "gecko", "firefox", "microsoftedge", "internetexplorer", "webview2", "edgehtml"
                Return True
        End Select
        Return False
    End Function

    Public Function VoceDiComposito(elemento As IUIAutomationElement) As Boolean
        Try
            Dim tipo As Integer = TipoControllo(elemento)
            If tipo = ControlloPulsanteOpzione OrElse tipo = ControlloCasellaControllo OrElse tipo = ControlloCollegamento OrElse tipo = ControlloPulsante Then Return False
            Return LeggiBooleano(elemento, ProprietaSelezioneDisponibile) OrElse LeggiBooleano(elemento, ProprietaVoceGriglia) OrElse LeggiBooleano(elemento, ProprietaVoceTabella)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.VoceDiComposito", exAtteso)
            Return False
        End Try
    End Function

    Public Function ContenitoreComposito(elemento As IUIAutomationElement) As Boolean
        Try
            Dim tipo As Integer = TipoControllo(elemento)
            If tipo = ControlloDocumento OrElse tipo = ControlloFinestra Then Return False
            Return LeggiBooleano(elemento, ProprietaPatternSelezione) OrElse LeggiBooleano(elemento, ProprietaPatternGriglia) OrElse LeggiBooleano(elemento, ProprietaPatternTabella)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.ContenitoreComposito", exAtteso)
            Return False
        End Try
    End Function

    Private Function ElencoOVoce(elemento As IUIAutomationElement) As Boolean
        Try
            Select Case TipoControllo(elemento)
                Case ControlloElenco, ControlloElementoElenco, ControlloAlbero, ControlloElementoAlbero, ControlloTabella, ControlloGrigliaDati, ControlloElementoDati
                    Return True
            End Select
            Select Case RuoloAria(elemento)
                Case "list", "listitem", "listbox", "option", "tree", "treeitem", "treegrid", "grid", "table", "row", "feed"
                    Return True
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.ElencoOVoce", exAtteso)
        End Try
        Return False
    End Function

    Private Shared _teamsTick As Integer
    Private Shared _teamsValore As Boolean
    Private Shared ReadOnly _processiTeams As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Boolean)()

    Public Shared Function PrimoPianoTeams() As Boolean
        Try
            Dim adesso As Integer = Environment.TickCount
            If _teamsTick <> 0 AndAlso adesso - _teamsTick >= 0 AndAlso adesso - _teamsTick < 400 Then Return _teamsValore
            Dim valore As Boolean = False
            Dim primo As IntPtr = NativoLettore.GetForegroundWindow()
            If primo <> IntPtr.Zero Then
                Dim pid As UInteger = 0UI
                NativoLettore.GetWindowThreadProcessId(primo, pid)
                If pid <> 0UI AndAlso Not _processiTeams.TryGetValue(pid, valore) Then
                    Dim nome As String = ""
                    Try
                        nome = Process.GetProcessById(CInt(pid)).ProcessName.ToLowerInvariant()
                    Catch exAtteso As Exception
                        RegistroErrori.Scrivi("DescrittoreElementi.PrimoPianoTeams", exAtteso)
                        nome = ""
                    End Try
                    valore = (nome = "ms-teams" OrElse nome = "teams" OrElse nome = "msteams")
                    If _processiTeams.Count > 300 Then _processiTeams.Clear()
                    _processiTeams(pid) = valore
                End If
            End If
            _teamsValore = valore
            _teamsTick = adesso
            Return valore
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.PrimoPianoTeams", exAtteso2)
            Return False
        End Try
    End Function

    Public Function MessaggioTeams(elemento As IUIAutomationElement, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As Boolean
        Try
            If elemento Is Nothing OrElse walker Is Nothing OrElse Not PrimoPianoTeams() Then Return False
            Dim ruolo As String = RuoloAria(elemento)
            Dim tipo As Integer = TipoControllo(elemento)
            Dim conRuolo As Boolean = (ruolo = "listitem" OrElse ruolo = "article")
            Dim generico As Boolean = (ruolo = "" AndAlso (tipo = ControlloElementoElenco OrElse tipo = ControlloGruppo OrElse tipo = ControlloPersonalizzato))
            If Not conRuolo AndAlso Not generico Then Return False
            Dim padre As IUIAutomationElement = If(cache Is Nothing, walker.GetParentElement(elemento), walker.GetParentElementBuildCache(elemento, cache))
            Dim salti As Integer = If(conRuolo, 2, 1)
            For salto As Integer = 1 To salti
                If padre Is Nothing Then Return False
                Dim ruoloPadre As String = RuoloAria(padre)
                Select Case ruoloPadre
                    Case "list", "feed", "listbox"
                        Return True
                End Select
                If TipoControllo(padre) = ControlloElenco Then Return True
                If ruoloPadre <> "" AndAlso ruoloPadre <> "presentation" AndAlso ruoloPadre <> "none" AndAlso ruoloPadre <> "generic" Then Return False
                If salto < salti Then padre = If(cache Is Nothing, walker.GetParentElement(padre), walker.GetParentElementBuildCache(padre, cache))
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.MessaggioTeams", exAtteso)
        End Try
        Return False
    End Function

    Public Function VoceElencoWeb(elemento As IUIAutomationElement) As Boolean
        Try
            Select Case RuoloAria(elemento)
                Case "listitem", "article"
                    Return True
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.VoceElencoWeb", exAtteso)
        End Try
        Return False
    End Function

    Public Function LivelloTitolo(elemento As IUIAutomationElement) As Integer
        Try
            If ElencoOVoce(elemento) Then Return 0
            Dim valore As Integer = LeggiIntero(elemento, ProprietaLivelloTitolo, 0)
            If valore >= 80051 AndAlso valore <= 80059 Then Return valore - 80050
            Dim aria As String = RuoloAria(elemento)
            If aria = "heading" Then
                Dim daAria As Integer = LivelloDaProprietaAria(elemento)
                If daAria > 0 Then Return daAria
                Dim daAlbero As Integer = LeggiIntero(elemento, ProprietaLivello, 0)
                If daAlbero >= 1 AndAlso daAlbero <= 9 Then Return daAlbero
                Return 1
            End If
            If FrameworkWeb(Framework(elemento)) Then Return 0
            Dim localizzato As String = LeggiStringa(elemento, ProprietaTipoControlloLocalizzato).ToLowerInvariant()
            If localizzato.Contains("barra del titolo") Then Return 0
            If localizzato.Contains("titolo") OrElse localizzato.Contains("heading") Then
                Dim daAria As Integer = LivelloDaProprietaAria(elemento)
                If daAria > 0 Then Return daAria
                Return 1
            End If
            Return 0
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LivelloTitolo: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function NomeRegione(elemento As IUIAutomationElement) As String
        Try
            Dim aria As String = RuoloAria(elemento)
            Dim tipoRegione As Integer = LeggiIntero(elemento, ProprietaTipoRegione, -1)
            Dim localizzato As String = LeggiStringa(elemento, ProprietaRegioneLocalizzata)
            Dim risultato As String = ""
            Dim generica As Boolean = False
            If localizzato <> "" Then
                risultato = localizzato
                Select Case localizzato.ToLowerInvariant()
                    Case "regione", "region", "modulo", "form"
                        generica = True
                End Select
            Else
                Select Case tipoRegione
                    Case 80000
                        risultato = "regione"
                        generica = True
                    Case 80001
                        risultato = "modulo"
                        generica = True
                    Case 80002
                        risultato = "principale"
                    Case 80003
                        risultato = "navigazione"
                    Case 80004
                        risultato = "ricerca"
                End Select
            End If
            If risultato = "" Then
                Select Case aria
                    Case "main"
                        risultato = "principale"
                    Case "navigation"
                        risultato = "navigazione"
                    Case "search"
                        risultato = "ricerca"
                    Case "banner"
                        risultato = "banner"
                    Case "contentinfo"
                        risultato = "informazioni"
                    Case "complementary"
                        risultato = "complementare"
                    Case "form"
                        risultato = "modulo"
                        generica = True
                    Case "region"
                        risultato = "regione"
                        generica = True
                End Select
            End If
            If risultato <> "" AndAlso (generica OrElse aria = "region" OrElse aria = "form") AndAlso Nome(elemento) = "" Then Return ""
            Return risultato
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.NomeRegione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function EDialogo(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim aria As String = RuoloAria(elemento)
            If aria = "dialog" OrElse aria = "alertdialog" Then Return True
            Select Case TipoControllo(elemento)
                Case ControlloFinestra, ControlloRiquadro, ControlloPersonalizzato, ControlloGruppo
                Case Else
                    Return False
            End Select
            Try
                Dim classe As String = elemento.CurrentClassName
                If classe IsNot Nothing AndAlso classe.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("DescrittoreElementi.EDialogo", exAtteso)
            End Try
            Try
                Dim valore As Object = elemento.GetCurrentPropertyValue(30174)
                If TypeOf valore Is Boolean Then Return CBool(valore)
            Catch exAtteso2 As Exception
                RegistroErrori.Scrivi("DescrittoreElementi.EDialogo", exAtteso2)
            End Try
            Return False
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.EDialogo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function TestoAiuto(elemento As IUIAutomationElement) As String
        Try
            Dim aiuto As String = LeggiStringa(elemento, ProprietaAiuto)
            If aiuto <> "" Then Return aiuto
            Return LeggiStringa(elemento, ProprietaDescrizioneCompleta)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.TestoAiuto: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function TastoAccesso(elemento As IUIAutomationElement) As String
        Try
            Dim accesso As String = LeggiStringa(elemento, ProprietaTastoAccesso)
            If accesso <> "" Then Return accesso
            Return LeggiStringa(elemento, ProprietaScorciatoia)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.TastoAccesso: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NonValidoAria(elemento As IUIAutomationElement) As Boolean
        Try
            Dim proprieta As String = LeggiStringa(elemento, ProprietaProprietaAria)
            If proprieta = "" Then Return False
            For Each parte As String In proprieta.Split(";"c)
                Dim voce As String = parte.Trim()
                If voce.StartsWith("invalid=", StringComparison.OrdinalIgnoreCase) Then
                    Dim valore As String = voce.Substring(8).Trim().ToLowerInvariant()
                    Return valore <> "" AndAlso valore <> "false"
                End If
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.NonValidoAria: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function DescriviAntenato(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim tipo As Integer = TipoControllo(elemento)
            Dim nomeElemento As String = Nome(elemento)
            Dim ruolo As String
            If tipo <> ControlloElenco Then
                Select Case RuoloAria(elemento)
                    Case "list", "feed", "listbox"
                        Return If(nomeElemento <> "", nomeElemento & ", elenco", "elenco")
                End Select
            End If
            Select Case tipo
                Case ControlloElenco
                    ruolo = "elenco"
                Case ControlloAlbero
                    ruolo = "albero"
                Case ControlloTabella
                    ruolo = "tabella"
                Case ControlloGrigliaDati
                    ruolo = "griglia dati"
                Case ControlloSchede
                    ruolo = "gruppo di schede"
                Case ControlloBarraStrumenti
                    ruolo = "barra degli strumenti"
                Case ControlloMenu
                    ruolo = "menu"
                Case ControlloBarraMenu
                    ruolo = "barra dei menu"
                Case ControlloBarraStato
                    ruolo = "barra di stato"
                Case ControlloBarraApplicazioni
                    ruolo = "barra dell'applicazione"
                Case ControlloGruppo
                    If nomeElemento = "" Then Return ""
                    ruolo = "gruppo"
                Case ControlloElementoAlbero
                    Return ""
                Case ControlloFinestra, ControlloDocumento, ControlloPulsante, ControlloCollegamento, ControlloModifica, ControlloTesto
                    Return ""
                Case Else
                    Dim regione As String = NomeRegione(elemento)
                    If regione = "" Then Return ""
                    If nomeElemento <> "" AndAlso Not String.Equals(nomeElemento, regione, StringComparison.CurrentCultureIgnoreCase) Then Return nomeElemento & ", " & regione
                    Return regione
            End Select
            If nomeElemento <> "" Then Return nomeElemento & ", " & ruolo
            Return ruolo
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.DescriviAntenato: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function LivelloDaProprietaAria(elemento As IUIAutomationElement) As Integer
        Try
            Dim proprieta As String = LeggiStringa(elemento, ProprietaProprietaAria)
            Dim indice As Integer = proprieta.IndexOf("level=", StringComparison.OrdinalIgnoreCase)
            If indice < 0 Then Return 0
            Dim numero As Integer = 0
            Dim trovato As Boolean = False
            For posizione As Integer = indice + 6 To proprieta.Length - 1
                Dim carattere As Char = proprieta(posizione)
                If carattere < "0"c OrElse carattere > "9"c Then Exit For
                trovato = True
                numero = numero * 10 + AscW(carattere) - AscW("0"c)
            Next
            If trovato AndAlso numero >= 1 AndAlso numero <= 9 Then Return numero
            Return 0
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LivelloDaProprietaAria: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function Protetto(elemento As IUIAutomationElement) As Boolean
        Try
            Return LeggiBooleano(elemento, ProprietaPassword)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.Protetto: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function EVoceAlbero(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If TipoControllo(elemento) = ControlloElementoAlbero Then Return True
            Dim ruolo As String = RuoloAria(elemento)
            Return ruolo = "treeitem" OrElse ruolo = "outlineitem"
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.EVoceAlbero: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function EAlbero(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If TipoControllo(elemento) = ControlloAlbero Then Return True
            Dim ruolo As String = RuoloAria(elemento)
            Return ruolo = "tree" OrElse ruolo = "treegrid" OrElse ruolo = "outline"
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.EAlbero: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function LivelloAlbero(elemento As IUIAutomationElement) As Integer
        Try
            If elemento Is Nothing OrElse Not EVoceAlbero(elemento) Then Return 0
            Dim valore As Integer = LeggiIntero(elemento, ProprietaLivello, 0)
            If valore >= 1 AndAlso valore <= 30 Then Return valore
            Dim daAria As Integer = LivelloDaProprietaAria(elemento)
            If daAria >= 1 AndAlso daAria <= 30 Then Return daAria
            Return 0
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.LivelloAlbero: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function FraseRuoloTitolo(livello As Integer) As String
        If DireLivelloTitoli AndAlso livello > 0 Then Return "intestazione di livello " & livello.ToString()
        Return "intestazione"
    End Function

    Public Function Descrivi(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim parti As New List(Of String)()
            Dim nomeElemento As String = NomeEsteso(elemento)
            Dim tipo As Integer = TipoControllo(elemento)
            Dim livello As Integer = LivelloTitolo(elemento)
            Dim ruolo As String
            If livello > 0 Then
                ruolo = If(DireLivelloTitoli, "intestazione di livello " & livello.ToString(), "intestazione")
            Else
                ruolo = NomeRuolo(elemento, tipo, nomeElemento)
            End If
            Dim regione As String = ""
            If livello = 0 Then regione = NomeRegione(elemento)
            Dim ruoloPrima As Boolean = LinkPrimaDelNome AndAlso ruolo = "link"
            If ruoloPrima Then parti.Add(ruolo)
            If nomeElemento <> "" Then parti.Add(nomeElemento)
            If EVoceAlbero(elemento) Then
                Dim profondita As Integer = LivelloAlbero(elemento)
                If profondita > 0 Then parti.Add("livello " & profondita.ToString())
            End If
            If ruolo <> "" AndAlso Not ruoloPrima Then parti.Add(ruolo)
            If regione <> "" AndAlso Not String.Equals(regione, ruolo, StringComparison.CurrentCultureIgnoreCase) Then parti.Add(regione)
            Dim variabile As String = ComponiParteVariabile(elemento, tipo, nomeElemento, True)
            If variabile <> "" Then parti.Add(variabile)
            Dim posizione As Integer = LeggiIntero(elemento, ProprietaPosizione, 0)
            Dim totale As Integer = LeggiIntero(elemento, ProprietaTotale, 0)
            If posizione > 0 AndAlso totale > 0 AndAlso (Not FrameworkWeb(Framework(elemento)) OrElse VoceDiComposito(elemento)) Then parti.Add(posizione.ToString() & " di " & totale.ToString())
            Dim cella As String = LetturaTabelle.DescriviCella(elemento, Me)
            If cella <> "" Then parti.Add(cella)
            If ProlissitaApplicata = 1 AndAlso tipo = ControlloVoceMenu Then
                Dim tastoMenu As String = TastoAccesso(elemento)
                If tastoMenu <> "" Then parti.Add(tastoMenu)
            End If
            If ProlissitaApplicata <= 0 Then
                Dim corto As New List(Of String)()
                If nomeElemento <> "" Then corto.Add(nomeElemento)
                If EVoceAlbero(elemento) Then
                    Dim profondita As Integer = LivelloAlbero(elemento)
                    If profondita > 0 Then corto.Add("livello " & profondita.ToString())
                End If
                If nomeElemento = "" AndAlso ruolo <> "" Then corto.Add(ruolo)
                If variabile <> "" AndAlso (variabile.IndexOf("selezion", StringComparison.CurrentCultureIgnoreCase) >= 0 OrElse variabile.IndexOf("premuto", StringComparison.CurrentCultureIgnoreCase) >= 0 OrElse variabile.IndexOf("non disponibile", StringComparison.CurrentCultureIgnoreCase) >= 0 OrElse variabile.IndexOf("chiuso", StringComparison.CurrentCultureIgnoreCase) >= 0 OrElse variabile.IndexOf("aperto", StringComparison.CurrentCultureIgnoreCase) >= 0) Then corto.Add(variabile)
                If corto.Count = 0 AndAlso ruolo <> "" Then corto.Add(ruolo)
                Return String.Join(", ", corto)
            End If
            If ProlissitaApplicata >= 2 Then
                Dim aiuto As String = TestoAiuto(elemento)
                If aiuto <> "" AndAlso Not String.Equals(aiuto, nomeElemento, StringComparison.CurrentCulture) Then parti.Add(aiuto)
                Dim scorciatoia As String = TastoAccesso(elemento)
                If scorciatoia <> "" Then parti.Add(scorciatoia)
            End If
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.Descrivi: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function ParteVariabile(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Return ComponiParteVariabile(elemento, TipoControllo(elemento), Nome(elemento), False)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ParteVariabile: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function ComponiParteVariabile(elemento As IUIAutomationElement, tipo As Integer, nomeElemento As String, includiTestoModificabile As Boolean) As String
        Try
            Dim parti As New List(Of String)()
            If BooleanoFalso(elemento, ProprietaAbilitato) Then parti.Add("non disponibile")
            If LeggiBooleano(elemento, ProprietaObbligatorio) Then parti.Add("obbligatorio")
            If NonValidoAria(elemento) Then parti.Add("non valido")
            AggiungiStatiAria(elemento, tipo, parti)
            AggiungiStatoAttivazione(elemento, tipo, parti)
            AggiungiStatoEspansione(elemento, tipo, parti)
            AggiungiPopup(elemento, tipo, parti)
            AggiungiStatoSelezione(elemento, tipo, parti)
            Dim protettoElemento As Boolean = Protetto(elemento)
            If protettoElemento Then parti.Add("protetto")
            Dim valoreAggiunto As Boolean = False
            If Not protettoElemento AndAlso tipo <> ControlloDocumento AndAlso LeggiBooleano(elemento, ProprietaValoreDisponibile) Then
                valoreAggiunto = AggiungiValore(elemento, tipo, nomeElemento, includiTestoModificabile, parti)
            End If
            If Not valoreAggiunto AndAlso tipo = ControlloCasellaCombinata Then valoreAggiunto = AggiungiSceltaCasella(elemento, nomeElemento, parti)
            If Not valoreAggiunto Then AggiungiValoreIntervallo(elemento, tipo, parti)
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.ComponiParteVariabile: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AggiungiStatoAttivazione(elemento As IUIAutomationElement, tipo As Integer, parti As List(Of String))
        Try
            If Not LeggiBooleano(elemento, ProprietaAttivazioneDisponibile) Then Return
            Dim stato As Integer = LeggiIntero(elemento, ProprietaStatoAttivazione, -1)
            Dim web As Boolean = FrameworkWeb(Framework(elemento))
            Dim ruolo As String = RuoloAria(elemento)
            Dim eCasella As Boolean = tipo = ControlloCasellaControllo OrElse tipo = ControlloElementoElenco OrElse tipo = ControlloElementoAlbero OrElse tipo = ControlloVoceMenu OrElse
                (web AndAlso (ruolo = "checkbox" OrElse ruolo = "menuitemcheckbox" OrElse ruolo = "switch"))
            If eCasella Then
                Select Case stato
                    Case 0
                        parti.Add("non selezionata")
                    Case 1
                        parti.Add("selezionata")
                    Case 2
                        parti.Add("parzialmente selezionata")
                End Select
                Return
            End If
            If web AndAlso (tipo = ControlloPulsante OrElse tipo = ControlloPulsanteDiviso OrElse ruolo = "button") Then
                Select Case stato
                    Case 1
                        parti.Add("premuto")
                    Case 2
                        parti.Add("parzialmente premuto")
                End Select
                Return
            End If
            Select Case stato
                Case 0
                    parti.Add("non premuto")
                Case 1
                    parti.Add("premuto")
                Case 2
                    parti.Add("parzialmente premuto")
            End Select
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiStatoAttivazione: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiStatoEspansione(elemento As IUIAutomationElement, tipo As Integer, parti As List(Of String))
        Try
            If Not LeggiBooleano(elemento, ProprietaEspansioneDisponibile) Then Return
            Dim stato As Integer = LeggiIntero(elemento, ProprietaStatoEspansione, -1)
            Dim voceAlbero As Boolean = tipo = ControlloElementoAlbero OrElse RuoloAria(elemento) = "treeitem"
            If stato = 0 AndAlso tipo = ControlloCasellaCombinata Then Return
            Select Case stato
                Case 0
                    If tipo = ControlloVoceMenu Then
                        parti.Add("sottomenu")
                    ElseIf voceAlbero Then
                        parti.Add("chiuso")
                    Else
                        parti.Add("compresso")
                    End If
                Case 1
                    parti.Add(If(voceAlbero, "aperto", "espanso"))
                Case 2
                    parti.Add(If(voceAlbero, "parzialmente aperto", "parzialmente espanso"))
            End Select
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiStatoEspansione: " & ex.Message)
        End Try
    End Sub

    Private Function AggiungiSceltaCasella(elemento As IUIAutomationElement, nomeElemento As String, parti As List(Of String)) As Boolean
        Try
            Dim scelta As String = SceltaCasella(elemento)
            If scelta = "" Then Return False
            If String.Equals(scelta, nomeElemento, StringComparison.CurrentCulture) Then Return False
            If scelta.Length > LunghezzaMassimaValore Then scelta = scelta.Substring(0, LunghezzaMassimaValore)
            parti.Add(scelta)
            Return True
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiSceltaCasella: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function SceltaCasella(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim selezione As IUIAutomationSelectionPattern = TryCast(elemento.GetCurrentPattern(10001), IUIAutomationSelectionPattern)
            If selezione Is Nothing Then Return ""
            Dim scelte As IUIAutomationElementArray = selezione.GetCurrentSelection()
            If scelte Is Nothing OrElse scelte.Length = 0 Then Return ""
            Dim prima As IUIAutomationElement = scelte.GetElement(0)
            If prima Is Nothing Then Return ""
            Return NomeCorrente(prima)
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.SceltaCasella: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AggiungiPopup(elemento As IUIAutomationElement, tipo As Integer, parti As List(Of String))
        Try
            If tipo = ControlloVoceMenu Then Return
            If Not FrameworkWeb(Framework(elemento)) Then Return
            Select Case ValoreAria(elemento, "haspopup")
                Case "true", "menu"
                    parti.Add("menu a comparsa")
                Case "listbox"
                    parti.Add("elenco a comparsa")
                Case "tree"
                    parti.Add("albero a comparsa")
                Case "grid"
                    parti.Add("griglia a comparsa")
                Case "dialog"
                    parti.Add("finestra di dialogo a comparsa")
            End Select
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiPopup: " & ex.Message)
        End Try
    End Sub

    Private Sub AggiungiStatoSelezione(elemento As IUIAutomationElement, tipo As Integer, parti As List(Of String))
        Try
            If Not LeggiBooleano(elemento, ProprietaSelezioneDisponibile) Then Return
            Dim valore As Object = LeggiValore(elemento, ProprietaSelezionato)
            If Not TypeOf valore Is Boolean Then Return
            Dim selezionato As Boolean = CBool(valore)
            If tipo = ControlloPulsanteOpzione Then
                If selezionato Then
                    parti.Add("selezionato")
                Else
                    parti.Add("non selezionato")
                End If
                Return
            End If
            If tipo = ControlloElementoElenco OrElse tipo = ControlloScheda OrElse tipo = ControlloElementoAlbero OrElse tipo = ControlloElementoDati Then
                If Not selezionato Then parti.Add("non selezionato")
            End If
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiStatoSelezione: " & ex.Message)
        End Try
    End Sub

    Private Function AggiungiValore(elemento As IUIAutomationElement, tipo As Integer, nomeElemento As String, includiTestoModificabile As Boolean, parti As List(Of String)) As Boolean
        Try
            Dim aria As String = RuoloAria(elemento)
            If tipo = ControlloCollegamento OrElse aria = "link" Then
                If nomeElemento <> "" Then Return False
                Dim breve As String = NomeBreveIndirizzo(LeggiStringa(elemento, ProprietaValore))
                If breve = "" Then Return False
                parti.Add(breve)
                Return True
            End If
            Dim modificabile As Boolean = (tipo = ControlloModifica) OrElse aria = "textbox" OrElse aria = "searchbox" OrElse (tipo = ControlloCasellaCombinata AndAlso HaPatternTesto(elemento))
            If modificabile AndAlso Not includiTestoModificabile Then Return False
            Dim valore As String = LeggiStringa(elemento, ProprietaValore)
            If valore = "" Then
                If modificabile Then
                    If Not PrimoPianoTeams() Then parti.Add("vuoto")
                    Return True
                End If
                Return False
            End If
            If String.Equals(valore, nomeElemento, StringComparison.CurrentCulture) Then Return False
            If valore.Length > LunghezzaMassimaValore Then valore = valore.Substring(0, LunghezzaMassimaValore)
            parti.Add(valore)
            Return True
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiValore: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function NomeBreveIndirizzo(indirizzo As String) As String
        Try
            Dim testo As String = If(indirizzo, "").Trim()
            If testo = "" Then Return ""
            If testo.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) OrElse testo = "#" Then Return ""
            If testo.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) Then Return testo.Substring(7).Split("?"c)(0)
            Dim taglio As Integer = testo.IndexOfAny(New Char() {"?"c, "#"c})
            If taglio >= 0 Then testo = testo.Substring(0, taglio)
            Dim protocollo As Integer = testo.IndexOf("://", StringComparison.Ordinal)
            If protocollo >= 0 Then testo = testo.Substring(protocollo + 3)
            Dim pezzi As String() = testo.Split(New Char() {"/"c}, StringSplitOptions.RemoveEmptyEntries)
            If pezzi.Length = 0 Then Return ""
            Dim ultimo As String = pezzi(pezzi.Length - 1)
            If pezzi.Length = 1 Then
                If ultimo.StartsWith("www.", StringComparison.OrdinalIgnoreCase) Then ultimo = ultimo.Substring(4)
                Return ultimo
            End If
            Dim punto As Integer = ultimo.LastIndexOf("."c)
            If punto > 0 Then ultimo = ultimo.Substring(0, punto)
            ultimo = Uri.UnescapeDataString(ultimo).Replace("-"c, " "c).Replace("_"c, " "c).Trim()
            If ultimo.Length > 60 Then ultimo = ultimo.Substring(0, 60)
            Return ultimo
        Catch ex As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.NomeBreveIndirizzo", ex)
            Return ""
        End Try
    End Function

    Private Sub AggiungiValoreIntervallo(elemento As IUIAutomationElement, tipo As Integer, parti As List(Of String))
        Try
            If Not LeggiBooleano(elemento, ProprietaIntervalloDisponibile) Then Return
            Dim valore As Double = 0
            If Not LeggiDouble(elemento, ProprietaValoreIntervallo, valore) Then Return
            If Double.IsNaN(valore) OrElse Double.IsInfinity(valore) Then Return
            Dim cultura As New CultureInfo("it-IT")
            If tipo = ControlloBarraAvanzamento Then
                parti.Add(Math.Round(valore).ToString("0", cultura) & " per cento")
            Else
                parti.Add(valore.ToString("0.##", cultura))
            End If
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiValoreIntervallo: " & ex.Message)
        End Try
    End Sub

    Public Shared Function RuoloGenerico(ruolo As String) As Boolean
        Try
            Dim testo As String = If(ruolo, "").Trim().ToLowerInvariant()
            If testo = "" Then Return False
            If testo.Contains("sconosciut") OrElse testo.Contains("unknown") Then Return True
            Select Case testo
                Case "riquadro", "pannello", "personalizzato", "elemento", "contenitore", "contenuto", "bordo", "griglia",
                     "riquadro di scorrimento", "riquadro diviso", "area di disegno", "client", "custom", "pane", "generic",
                     "generico", "section", "div", "grouping", "panel", "container", "border", "grid", "canvas"
                    Return True
            End Select
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.RuoloGenerico", exAtteso)
            Return False
        End Try
    End Function

    Private Function NomeRuolo(elemento As IUIAutomationElement, tipo As Integer, nomeElemento As String) As String
        Try
            If tipo = ControlloPersonalizzato OrElse tipo = ControlloRiquadro OrElse tipo = ControlloGruppo OrElse tipo = 0 Then
                Dim daRegola As String = RegoleRuoli.Istanza.Ruolo(Me, elemento)
                If daRegola <> "" Then Return If(RuoloGenerico(daRegola), "", daRegola)
            End If
            Select Case tipo
                Case ControlloPulsante
                    Return "pulsante"
                Case ControlloCalendario
                    Return "calendario"
                Case ControlloCasellaControllo
                    Return "casella di controllo"
                Case ControlloCasellaCombinata
                    Return "casella combinata"
                Case ControlloModifica
                    If CampoSolaLettura(elemento) Then Return "campo di sola lettura"
                    Return "campo di modifica"
                Case ControlloCollegamento
                    Return "link"
                Case ControlloImmagine
                    Return "immagine"
                Case ControlloElenco
                    Return "elenco"
                Case ControlloMenu
                    Return "menu"
                Case ControlloBarraMenu
                    Return "barra dei menu"
                Case ControlloBarraAvanzamento
                    Return "barra di avanzamento"
                Case ControlloPulsanteOpzione
                    Return "pulsante di opzione"
                Case ControlloBarraScorrimento
                    Return "barra di scorrimento"
                Case ControlloDispositivoScorrimento
                    Return "dispositivo di scorrimento"
                Case ControlloSelettoreNumerico
                    Return "selettore numerico"
                Case ControlloBarraStato
                    Return "barra di stato"
                Case ControlloSchede
                    Return "gruppo di schede"
                Case ControlloScheda
                    Return "scheda"
                Case ControlloBarraStrumenti
                    Return "barra degli strumenti"
                Case ControlloDescrizioneComando
                    Return "descrizione comando"
                Case ControlloAlbero
                    Return "albero"
                Case ControlloCursore
                    Return "cursore"
                Case ControlloGrigliaDati
                    Return "griglia dati"
                Case ControlloDocumento
                    Return "documento"
                Case ControlloPulsanteDiviso
                    Return "pulsante con menu"
                Case ControlloFinestra
                    Return "finestra"
                Case ControlloIntestazione
                    Return "intestazione"
                Case ControlloElementoIntestazione
                    Return "elemento intestazione"
                Case ControlloTabella
                    Return "tabella"
                Case ControlloBarraTitolo
                    Return "barra del titolo"
                Case ControlloSeparatore
                    Return "separatore"
                Case ControlloZoomSemantico
                    Return "zoom semantico"
                Case ControlloBarraApplicazioni
                    Return "barra dell'applicazione"
                Case ControlloElementoElenco, ControlloVoceMenu, ControlloTesto, ControlloRiquadro, ControlloElementoDati, ControlloElementoAlbero
                    Return ""
                Case ControlloGruppo
                    If nomeElemento = "" Then Return ""
                    Return "gruppo"
                Case Else
                    Dim localizzato As String = LeggiStringa(elemento, ProprietaTipoControlloLocalizzato)
                    If RuoloGenerico(localizzato) Then Return ""
                    Return localizzato
            End Select
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.NomeRuolo: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function DescriviColonne(elemento As IUIAutomationElement, walker As IUIAutomationTreeWalker, cache As IUIAutomationCacheRequest) As String
        Try
            If elemento Is Nothing OrElse walker Is Nothing Then Return ""
            Dim tipo As Integer = TipoControllo(elemento)
            If tipo <> ControlloElementoElenco AndAlso tipo <> ControlloElementoDati AndAlso tipo <> ControlloElementoAlbero Then Return ""
            Dim nomeRiga As String = Nome(elemento)
            Dim parti As New List(Of String)()
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim figlio As IUIAutomationElement = walker.GetFirstChildElementBuildCache(elemento, cache)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 20 AndAlso orologio.ElapsedMilliseconds < 200
                Dim valore As String = Nome(figlio)
                If valore = "" Then valore = LeggiStringa(figlio, ProprietaValore)
                If valore <> "" AndAlso Not String.Equals(valore, nomeRiga, StringComparison.CurrentCulture) Then
                    Dim intestazione As String = IntestazioneColonna(figlio)
                    If intestazione = "" OrElse String.Equals(intestazione, valore, StringComparison.CurrentCultureIgnoreCase) OrElse valore.StartsWith(intestazione, StringComparison.CurrentCultureIgnoreCase) Then
                        parti.Add(valore)
                    Else
                        parti.Add(intestazione & ": " & valore)
                    End If
                End If
                figlio = walker.GetNextSiblingElementBuildCache(figlio, cache)
                giri += 1
            End While
            If parti.Count > 0 Then Return String.Join(", ", parti)
            Try
                Dim legacy As IUIAutomationLegacyIAccessiblePattern = TryCast(elemento.GetCurrentPattern(10018), IUIAutomationLegacyIAccessiblePattern)
                If legacy IsNot Nothing Then
                    Dim descrizione As String = legacy.CurrentDescription
                    If Not String.IsNullOrWhiteSpace(descrizione) Then
                        descrizione = descrizione.Trim()
                        If Not String.Equals(descrizione, nomeRiga, StringComparison.CurrentCulture) Then
                            If descrizione.Length > 400 Then descrizione = descrizione.Substring(0, 400)
                            Return descrizione
                        End If
                    End If
                End If
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("DescrittoreElementi.DescriviColonne", exAtteso)
            End Try
            Return ""
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.DescriviColonne: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function IntestazioneColonna(cella As IUIAutomationElement) As String
        Try
            Dim voce As IUIAutomationTableItemPattern = TryCast(cella.GetCurrentPattern(10013), IUIAutomationTableItemPattern)
            If voce Is Nothing Then Return ""
            Dim intestazioni As IUIAutomationElementArray = voce.GetCurrentColumnHeaderItems()
            If intestazioni Is Nothing OrElse intestazioni.Length = 0 Then Return ""
            Return NomeCorrente(intestazioni.GetElement(0))
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("DescrittoreElementi.IntestazioneColonna", exAtteso)
            Return ""
        End Try
    End Function

    Public Function TestoRiga(testo As String) As String
        Try
            If testo Is Nothing Then Return "vuoto"
            Dim pulito As String = testo.TrimEnd(ChrW(13), ChrW(10), ChrW(11), ChrW(&H2029), ChrW(&H2028))
            If String.IsNullOrWhiteSpace(pulito) Then Return "vuoto"
            Return pulito
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.TestoRiga: " & ex.Message)
            Return ""
        End Try
    End Function

    Private _ultimoCarattere As String = ""

    Public ReadOnly Property UltimoCarattere As String
        Get
            Return _ultimoCarattere
        End Get
    End Property

    Public Function NomeFonetico(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Dim carattere As Char = testo(0)
            Dim parola As String = ""
            Select Case Char.ToLowerInvariant(carattere)
                Case "a"c : parola = "Ancona"
                Case "b"c : parola = "Bologna"
                Case "c"c : parola = "Como"
                Case "d"c : parola = "Domodossola"
                Case "e"c : parola = "Empoli"
                Case "f"c : parola = "Firenze"
                Case "g"c : parola = "Genova"
                Case "h"c : parola = "Hotel"
                Case "i"c : parola = "Imola"
                Case "j"c : parola = "Jolly"
                Case "k"c : parola = "Kappa"
                Case "l"c : parola = "Livorno"
                Case "m"c : parola = "Milano"
                Case "n"c : parola = "Napoli"
                Case "o"c : parola = "Otranto"
                Case "p"c : parola = "Padova"
                Case "q"c : parola = "Quarto"
                Case "r"c : parola = "Roma"
                Case "s"c : parola = "Savona"
                Case "t"c : parola = "Torino"
                Case "u"c : parola = "Udine"
                Case "v"c : parola = "Venezia"
                Case "w"c : parola = "Washington"
                Case "x"c : parola = "Xeres"
                Case "y"c : parola = "Yacht"
                Case "z"c : parola = "Zara"
            End Select
            If parola = "" Then Return NomeCarattere(testo.Substring(0, 1))
            Dim frase As String = Char.ToUpperInvariant(carattere).ToString() & " come " & parola
            If Char.IsUpper(carattere) Then frase &= ", maiuscola"
            Return frase
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.NomeFonetico: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AggiungiStatiAria(elemento As IUIAutomationElement, tipo As Integer, parti As List(Of String))
        Try
            Select Case ValoreAria(elemento, "current")
                Case "page"
                    parti.Add("pagina corrente")
                Case "step"
                    parti.Add("passaggio corrente")
                Case "location"
                    parti.Add("posizione corrente")
                Case "date"
                    parti.Add("data corrente")
                Case "time"
                    parti.Add("ora corrente")
                Case "true"
                    parti.Add("corrente")
            End Select
            Select Case ValoreAria(elemento, "sort")
                Case "ascending"
                    parti.Add("ordinato in modo crescente")
                Case "descending"
                    parti.Add("ordinato in modo decrescente")
                Case "other"
                    parti.Add("ordinato")
            End Select
            Dim ruolo As String = RuoloAria(elemento)
            If (tipo = ControlloModifica OrElse ruolo = "textbox" OrElse ruolo = "searchbox") AndAlso CampoSolaLettura(elemento) Then parti.Add("sola lettura")
            If tipo = ControlloCollegamento OrElse ruolo = "link" Then
                Dim stato As Object = elemento.GetCurrentPropertyValue(30100)
                If stato IsNot Nothing AndAlso (Convert.ToInt64(stato) And &H800000L) <> 0L Then parti.Add("visitato")
            End If
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.AggiungiStatiAria: " & ex.Message)
        End Try
    End Sub

    Public Function NomeCarattere(testo As String) As String
        Try
            If testo Is Nothing OrElse testo.Length = 0 Then Return "vuoto"
            Dim pulito As String = testo.TrimEnd(ChrW(13), ChrW(10), ChrW(11), ChrW(&H2029), ChrW(&H2028))
            If pulito.Length = 0 Then Return "a capo"
            If pulito.Length > 1 Then Return pulito
            _ultimoCarattere = pulito
            Dim carattere As Char = pulito(0)
            Select Case carattere
                Case " "c, ChrW(&HA0)
                    Return "spazio"
                Case ChrW(9)
                    Return "tabulazione"
                Case "."c
                    Return "punto"
                Case ","c
                    Return "virgola"
                Case ";"c
                    Return "punto e virgola"
                Case ":"c
                    Return "due punti"
                Case "!"c
                    Return "punto esclamativo"
                Case "?"c
                    Return "punto interrogativo"
                Case "'"c, ChrW(&H2019)
                    Return "apostrofo"
                Case """"c, ChrW(&H201C), ChrW(&H201D)
                    Return "virgolette"
                Case "("c
                    Return "aperta tonda"
                Case ")"c
                    Return "chiusa tonda"
                Case "["c
                    Return "aperta quadra"
                Case "]"c
                    Return "chiusa quadra"
                Case "{"c
                    Return "aperta graffa"
                Case "}"c
                    Return "chiusa graffa"
                Case "<"c
                    Return "minore"
                Case ">"c
                    Return "maggiore"
                Case "-"c
                    Return "trattino"
                Case "_"c
                    Return "trattino basso"
                Case "@"c
                    Return "chiocciola"
                Case "/"c
                    Return "barra"
                Case "\"c
                    Return "barra rovesciata"
                Case "|"c
                    Return "barra verticale"
                Case "*"c
                    Return "asterisco"
                Case "+"c
                    Return "più"
                Case "="c
                    Return "uguale"
                Case "#"c
                    Return "cancelletto"
                Case "&"c
                    Return "e commerciale"
                Case "%"c
                    Return "per cento"
                Case "€"c
                    Return "euro"
                Case "$"c
                    Return "dollaro"
                Case "^"c
                    Return "accento circonflesso"
                Case "~"c
                    Return "tilde"
                Case "°"c
                    Return "gradi"
                Case "§"c
                    Return "paragrafo"
            End Select
            If Char.IsUpper(carattere) Then Return pulito & " maiuscola"
            Return pulito
        Catch ex As Exception
            log.ScriviLog("DescrittoreElementi.NomeCarattere: " & ex.Message)
            Return ""
        End Try
    End Function

End Class