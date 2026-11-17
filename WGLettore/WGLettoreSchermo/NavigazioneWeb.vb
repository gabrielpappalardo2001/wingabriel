Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Text
Imports UIAutomationClient

' Motore principale per l'esplorazione, l'indicizzazione e la lettura dei contenuti nelle pagine web.
Friend NotInheritable Class ContestoNavigazione
    Public Radice As IUIAutomationElement
    Public Chiave As String = ""
    Public EWeb As Boolean
    Public DentroDocumento As Boolean
    Public EScrittura As Boolean
    Public EApplicazione As Boolean
    Public ERiquadro As Boolean
    Public Incerto As Boolean
    Public Finestra As IntPtr
End Class

Friend NotInheritable Class NavigazioneWeb

    Private Const MassimoVoci As Integer = 10000
    Private Const SchemaInvocazione As Integer = 10000
    Private Const SchemaSelezioneVoce As Integer = 10010
    Private Const SchemaEspansione As Integer = 10005
    Private Const SchemaAttivazione As Integer = 10015
    Private Const SchemaScorrimentoVoce As Integer = 10017
    Private Const SchemaAccessibilita As Integer = 10018
    Private Const SchemaGriglia As Integer = 10006
    Private Const SchemaCella As Integer = 10007
    Private Const SchemaVoceTabella As Integer = 10013
    Private Const VK_LEFT As Integer = &H25
    Private Const VK_UP As Integer = &H26
    Private Const VK_RIGHT As Integer = &H27
    Private Const VK_DOWN As Integer = &H28
    Private Const VK_PRIOR As Integer = &H21
    Private Const VK_NEXT As Integer = &H22
    Private Const VK_HOME As Integer = &H24
    Private Const VK_END As Integer = &H23
    Private Const VK_BACK As Integer = &H8
    Private Const VK_DELETE As Integer = &H2E

    Friend Shared ModoRegioni As Integer = 1
    Friend Shared Ciclica As Boolean = True

    Friend Shared NascondiMenuChiusi As Boolean = True

    Private Shared ReadOnly RuoliModalitaFocus As New HashSet(Of String)(New String() {
        "menuitem", "menuitemcheckbox", "menuitemradio", "option", "treeitem", "tab", "slider", "spinbutton",
        "gridcell", "row", "columnheader", "rowheader", "combobox",
        "listitem", "list", "cell", "grid", "treegrid", "tree", "listbox", "menu", "menubar", "tablist"})
    Private _catenaFocus As New HashSet(Of String)(StringComparer.Ordinal)

    Private _regioneCorrente As String = ""
    Private _riquadro As Boolean
    Private _sospesa As IUIAutomationElement
    Private _sospesaAvanti As Boolean
    Private ReadOnly log As New Logger()
    Private ReadOnly _automazione As IUIAutomation
    Private ReadOnly _descrittore As DescrittoreElementi
    Private ReadOnly _cache As IUIAutomationCacheRequest
    Private ReadOnly _finestreWeb As New Dictionary(Of IntPtr, KeyValuePair(Of Integer, Boolean))()
    Private _walker As IUIAutomationTreeWalker
    Private _walkerAntenati As IUIAutomationTreeWalker
    Private _voci As New List(Of VoceAlbero)()
    Private _contenitoriCorrenti As New List(Of String())()
    Private ReadOnly _vociElencoNote As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    Private _perIdentita As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    Private _cursoreIndice As IUIAutomationElement
    Private _indiceCompleto As Boolean
    Private _radice As IUIAutomationElement
    Private _corrente As IUIAutomationElement
    Private _raccoltaConsigliata As Integer
    Private _oltreLaFine As Boolean
    Private _rigaTesto As IUIAutomationTextRange
    Private _rigaChiaveRadice As String = ""
    Private _rigaChiaveCorrente As String = ""
    Private _ultimoARighe As Boolean
    Private _indiceTroncato As Boolean
    Private _testoRiga As String = ""
    Private _segmenti As New List(Of SegmentoRiga)()
    Private _indiceSegmento As Integer
    Private _racchiusoRiga As IUIAutomationElement
    Private _posRiga As Integer = -1
    Private Const AttesaVoceMs As Integer = 700
    Private _chiaveRadice As String = ""
    Private _finestra As IntPtr
    Private _web As Boolean
    Private _chromium As Boolean
    Private _indiceCompletoTick As Integer
    Private _vociNuove As List(Of VoceAlbero)
    Private _perIdentitaNuove As Dictionary(Of String, Integer)
    Private _pos As Integer = -1
    Private _verso As Integer
    Private _selezioneAttiva As Boolean
    Private _tutto As Boolean
    Private _ancora As IUIAutomationElement
    Private _ancoraPos As Integer
    Private _ancoraVerso As Integer
    Private _testoCorrente As String = ""
    Private _testoPronto As Boolean
    Private _ambito As IUIAutomationElement
    Private _rigaAperta As IUIAutomationElement
    Private _ultimaStruttura As Integer
    Private _bufferDaRicostruire As Boolean
    Private _paginaRadice As String = ""
    Private _urlMemoria As String = ""
    Private _paginaControlloTick As Integer
    Private ReadOnly _regioniNote As New Dictionary(Of String, String())(StringComparer.Ordinal)
    Private ReadOnly _titoloInLinkNoto As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    Private _usaVistaContenuto As Boolean
    Private ReadOnly _nascostoNoto As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
    Private ReadOnly _corpoNoto As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
    Private ReadOnly _contenutoNoto As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private ReadOnly _lunghezzaNota As New System.Collections.Concurrent.ConcurrentDictionary(Of String, MisuraNota)(StringComparer.Ordinal)
    Private ReadOnly _editabileNoto As New System.Collections.Concurrent.ConcurrentDictionary(Of String, MisuraNota)(StringComparer.Ordinal)
    Private Const ValiditaMisuraMs As Integer = 5000
    Private Const AttesaTabMs As Integer = 700
    Private Const AttesaTabRapidaMs As Integer = 150
    Private Const MassimoPosizioniRicordate As Integer = 24
    Private Const MassimoAntenatiContesto As Integer = 400
    Private Const TempoMassimoContestoMs As Integer = 400
    Private Const MassimoVociAiuto As Integer = 80000
    Private Const TempoAlberoAiutoMs As Integer = 2500
    Private Const AttesaAiutoMs As Long = 9000L
    Private Const IntervalloMinimoAiutoMs As Long = 1500L

    Private _aiutoAttivo As Boolean
    Private _aiutoFinestra As IntPtr
    Private _aiutoPrefisso As Integer()
    Private _aiutoSegno As Integer
    Private _aiutoErrori As Integer
    Private _aiutoFallimentiAggiornamento As Integer
    Private _aiutoSoloRicerca As Boolean
    Private _aiutoDaScartare As Boolean
    Private _aiutoRifiutato As Boolean
    Private _aiutoVerificato As Boolean
    Private _aiutoRichiesta As RichiestaAiuto
    Private _aiutoUltimaRichiesta As Integer = Integer.MinValue
    Private _aiutoUltimaDurata As Integer

    Private NotInheritable Class MisuraNota
        Public Valore As Integer
        Public Tick As Integer
    End Class

    Private NotInheritable Class PosizioneRicordata
        Public Cursore As IUIAutomationElement
        Public Carattere As Integer
        Public UltimoFuoco As IUIAutomationElement
        Public DaTasti As Boolean
        Public Tick As Integer
    End Class

    Private ReadOnly _posizioniRicordate As New Dictionary(Of String, PosizioneRicordata)(StringComparer.Ordinal)
    Private ReadOnly _titoloDentroNoto As New Dictionary(Of String, Boolean)(StringComparer.Ordinal)
    Private _mailSingolaTick As Integer = Integer.MinValue
    Private _mailSingolaChiave As String = ""
    Private _mailSingola As IUIAutomationElement

    Private NotInheritable Class VoceAlbero
        Private _elemento As IUIAutomationElement
        Private _risoluzioneTentata As Boolean
        Public Proprietario As NavigazioneWeb
        Public IdAiuto As Integer
        Public Chiave As String = ""
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
        Public NomeProprio As String = ""

        Public Property Elemento As IUIAutomationElement
            Get
                Try
                    If _elemento Is Nothing AndAlso IdAiuto <> 0 AndAlso Not _risoluzioneTentata AndAlso Proprietario IsNot Nothing Then
                        _risoluzioneTentata = True
                        _elemento = Proprietario.RisolviVoce(Me)
                    End If
                Catch ex As Exception
                    RegistroErrori.Scrivi("NavigazioneWeb.VoceAlbero.Elemento", ex)
                End Try
                Return _elemento
            End Get
            Set(value As IUIAutomationElement)
                _elemento = value
                _risoluzioneTentata = value IsNot Nothing
            End Set
        End Property

        Public Function ElementoRisolto() As IUIAutomationElement
            Return Elemento
        End Function
    End Class

    Private NotInheritable Class RichiestaAiuto
        Private ReadOnly _proprietario As NavigazioneWeb
        Public ReadOnly Finestra As IntPtr
        Public ReadOnly ChiaveRadice As String
        Public ReadOnly Prefisso As Integer()
        Public ReadOnly UltimoRadice As Integer
        Public ReadOnly Inizio As Integer
        Public Voci As List(Of VoceAlbero)
        Public Mappa As Dictionary(Of String, Integer)
        Public Segno As Integer
        Public Troncato As Boolean
        Public Fallita As Boolean
        Public Durata As Integer
        Private _pronta As Integer

        Public Sub New(proprietario As NavigazioneWeb, finestra As IntPtr, chiaveRadice As String, prefisso As Integer(), ultimoRadice As Integer)
            _proprietario = proprietario
            Me.Finestra = finestra
            Me.ChiaveRadice = chiaveRadice
            Me.Prefisso = prefisso
            Me.UltimoRadice = ultimoRadice
            Inizio = Environment.TickCount
        End Sub

        Public ReadOnly Property Pronta As Boolean
            Get
                Return System.Threading.Volatile.Read(_pronta) = 1
            End Get
        End Property

        Public Sub Esegui(stato As Object)
            Try
                Dim risposta As String = AiutoInProcesso.Istanza.RichiediAlbero(Finestra, -UltimoRadice, MassimoVociAiuto, TempoAlberoAiutoMs)
                Dim risultato As RisultatoIndiceAiuto = IndiceAiuto.Analizza(risposta)
                If risultato Is Nothing Then
                    Fallita = True
                ElseIf risultato.IdDocumento = -UltimoRadice Then
                    Segno = -1
                ElseIf risultato.IdDocumento = UltimoRadice Then
                    Segno = 1
                Else
                    Fallita = True
                End If
                If Not Fallita Then
                    Troncato = risultato.Troncato
                    _proprietario.PreparaVociAiuto(Me, risultato)
                End If
            Catch ex As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.RichiestaAiuto.Esegui", ex)
                Fallita = True
            Finally
                Durata = Math.Max(0, Environment.TickCount - Inizio)
                System.Threading.Volatile.Write(_pronta, 1)
            End Try
        End Sub
    End Class

    Public Sub New(automazione As IUIAutomation, descrittore As DescrittoreElementi, cache As IUIAutomationCacheRequest)
        Try
            _automazione = automazione
            _descrittore = descrittore
            _cache = cache
            If automazione IsNot Nothing Then
                _walker = automazione.ContentViewWalker
                _walkerAntenati = automazione.ControlViewWalker
                _usaVistaContenuto = True
            End If
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.New: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property IndiceDaCompletare As Boolean
        Get
            Return _radice IsNot Nothing AndAlso (Not _indiceCompleto OrElse _vociNuove IsNot Nothing OrElse _aiutoRichiesta IsNot Nothing OrElse _aiutoDaScartare)
        End Get
    End Property

    Public ReadOnly Property RaccoltaConsigliata As Boolean
        Get
            Return System.Threading.Volatile.Read(_raccoltaConsigliata) = 1
        End Get
    End Property

    Public Sub AzzeraRaccolta()
        Try
            System.Threading.Volatile.Write(_raccoltaConsigliata, 0)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AzzeraRaccolta: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property ChiaveRadice As String
        Get
            Return _chiaveRadice
        End Get
    End Property

    Public ReadOnly Property Finestra As IntPtr
        Get
            Return _finestra
        End Get
    End Property

    Public ReadOnly Property EWeb As Boolean
        Get
            Return _web
        End Get
    End Property

    Public ReadOnly Property NumeroVoci As Integer
        Get
            If _radice Is Nothing Then Return 0
            Return 1
        End Get
    End Property

    Private _indiceNoto As Integer = -1
    Private _testoNoto As String = ""

    Public Function RiancoraCursore() As Boolean
        Try
            If _radice Is Nothing OrElse _corrente Is Nothing Then Return False
            If ElementoPresente(_corrente) Then
                RicordaCursore()
                Return True
            End If
            Return RitrovaPosizione()
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RiancoraCursore: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub RicordaCursore()
        Try
            If _corrente Is Nothing OrElse _voci Is Nothing Then Return
            Dim indice As Integer = IndiceDi(_corrente)
            If indice < 0 OrElse indice >= _voci.Count Then Return
            _indiceNoto = indice
            _testoNoto = If(_voci(indice).Testo, "")
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RicordaCursore: " & ex.Message)
        End Try
    End Sub

    Public Function RitrovaPosizione() As Boolean
        Try
            If _radice Is Nothing OrElse _walker Is Nothing Then Return False
            If _indiceNoto < 0 AndAlso _testoNoto = "" Then Return False
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim nodo As IUIAutomationElement = _radice
            Dim ordine As Integer = -1
            Dim vicino As IUIAutomationElement = Nothing
            Dim uguale As IUIAutomationElement = Nothing
            Dim distanzaUguale As Integer = Integer.MaxValue
            For passo As Integer = 1 To 3000
                If orologio.ElapsedMilliseconds > 200 Then Exit For
                nodo = NodoSuccessivo(nodo)
                If nodo Is Nothing OrElse Stesso(nodo, _radice) Then Exit For
                Dim tipo As String = Classifica(nodo, _descrittore.TipoControllo(nodo))
                If Not VoceUtile(tipo, nodo) Then Continue For
                ordine += 1
                If ordine = _indiceNoto Then vicino = nodo
                If _testoNoto <> "" AndAlso String.Equals(TestoDi(nodo, tipo), _testoNoto, StringComparison.Ordinal) Then
                    Dim distanza As Integer = Math.Abs(ordine - _indiceNoto)
                    If distanza < distanzaUguale Then
                        uguale = nodo
                        distanzaUguale = distanza
                    End If
                End If
                If vicino IsNot Nothing AndAlso (uguale IsNot Nothing OrElse _testoNoto = "") AndAlso ordine >= _indiceNoto + Math.Min(distanzaUguale, 200) Then Exit For
            Next
            Dim scelto As IUIAutomationElement = If(uguale, vicino)
            If scelto Is Nothing Then Return False
            Sincronizza(scelto)
            Return True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RitrovaPosizione: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function RadiceValida() As Boolean
        Try
            If _radice Is Nothing Then Return False
            Return _radice.CurrentControlType <> 0
        Catch ex As TimeoutException
            RegistroErrori.Scrivi("NavigazioneWeb.RadiceValida", ex)
            Return True
        Catch ex As System.Runtime.InteropServices.COMException
            RegistroErrori.Scrivi("NavigazioneWeb.RadiceValida", ex)
            Return ex.HResult = &H80131505
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RadiceValida", ex)
            Return False
        End Try
    End Function

    Public Sub Pulisci()
        Try
            System.Threading.Volatile.Write(_raccoltaConsigliata, 1)
            _voci = New List(Of VoceAlbero)()
            _perIdentita = New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            _vociNuove = Nothing
            _perIdentitaNuove = Nothing
            _cursoreIndice = Nothing
            _indiceCompleto = False
            _bufferDaRicostruire = False
            _riquadro = False
            _radice = Nothing
            _corrente = Nothing
            _ambito = Nothing
            _rigaAperta = Nothing
            _chiaveRadice = ""
            _paginaRadice = ""
            _urlMemoria = ""
            _finestra = IntPtr.Zero
            _web = False
            AzzeraAiuto()
            AzzeraMemo()
            AzzeraCursore()
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.Pulisci: " & ex.Message)
        End Try
    End Sub

    Public Function Contesto(elemento As IUIAutomationElement, finestra As IntPtr) As ContestoNavigazione
        Dim esito As New ContestoNavigazione()
        Try
            esito.Finestra = finestra
            If elemento Is Nothing OrElse _walkerAntenati Is Nothing Then Return esito
            Dim web As Boolean = False
            Dim gecko As Boolean = False
            Dim documentiGecko As New List(Of IUIAutomationElement)()
            Dim corrente As IUIAutomationElement = elemento
            Dim documento As IUIAutomationElement = Nothing
            Dim completo As Boolean = False
            Dim radiceIncontrata As Boolean = False
            Dim radiceAttuale As IUIAutomationElement = _radice
            Dim tipoRadice As Integer = -1
            If radiceAttuale IsNot Nothing AndAlso finestra = _finestra Then
                tipoRadice = _descrittore.TipoControllo(radiceAttuale)
            Else
                radiceAttuale = Nothing
            End If
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Try
                For salto As Integer = 0 To MassimoAntenatiContesto
                    If corrente Is Nothing Then
                        completo = True
                        Exit For
                    End If
                    If orologio.ElapsedMilliseconds > TempoMassimoContestoMs Then Exit For
                    Dim tipo As Integer = _descrittore.TipoControllo(corrente)
                    If radiceAttuale IsNot Nothing AndAlso Not radiceIncontrata AndAlso tipo = tipoRadice AndAlso Stesso(corrente, radiceAttuale) Then radiceIncontrata = True
                    If tipo = DescrittoreElementi.ControlloDocumento Then
                        documento = corrente
                        documentiGecko.Add(corrente)
                    End If
                    If documento Is Nothing AndAlso _descrittore.RuoloAria(corrente) = "document" Then documento = corrente
                    Dim framework As String = _descrittore.Framework(corrente)
                    If DescrittoreElementi.FrameworkWeb(framework) Then web = True
                    If RegoleRuoli.NormalizzaFramework(framework) = "mozilla" Then gecko = True
                    If _descrittore.RuoloAria(corrente) = "application" Then esito.EApplicazione = True
                    If tipo = DescrittoreElementi.ControlloFinestra AndAlso documento IsNot Nothing Then
                        completo = True
                        Exit For
                    End If
                    corrente = _walkerAntenati.GetParentElementBuildCache(corrente, _cache)
                Next
            Catch exRisalita As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.Contesto (risalita)", exRisalita)
                completo = False
            End Try
            If Not completo Then
                If radiceIncontrata Then
                    documento = radiceAttuale
                    documentiGecko.Clear()
                    gecko = False
                    web = web OrElse _web
                Else
                    esito.Incerto = True
                End If
            End If
            If gecko Then
                documento = Nothing
                For Each candidato As IUIAutomationElement In documentiGecko
                    If DocumentoGeckoSolaLettura(candidato) Then
                        documento = candidato
                        Exit For
                    End If
                Next
                If documento Is Nothing Then
                    For Each candidato As IUIAutomationElement In documentiGecko
                        If _descrittore.SolaLettura(candidato) OrElse IndirizzoMessaggio(candidato) Then
                            documento = candidato
                            Exit For
                        End If
                    Next
                End If
            End If
            If documento IsNot Nothing Then
                esito.Radice = documento
                esito.Chiave = Chiave(documento)
                esito.DentroDocumento = True
                esito.EWeb = web
            End If
            AdattaRiquadroCollegato(elemento, esito)
            esito.EScrittura = RichiedeScrittura(elemento)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.Contesto", ex)
        End Try
        Return esito
    End Function

    Private Sub AdattaRiquadroCollegato(elemento As IUIAutomationElement, esito As ContestoNavigazione)
        Try
            If elemento Is Nothing OrElse esito Is Nothing OrElse _descrittore Is Nothing Then Return
            If Not esito.DentroDocumento Then Return
            If Not LetturaRiquadroCollegato.InRiquadro(elemento, _automazione, _descrittore, _walkerAntenati, _cache) Then Return
            Dim documento As IUIAutomationElement = LetturaRiquadroCollegato.Riquadro(elemento, _automazione, _descrittore, _walkerAntenati, _cache)
            If documento Is Nothing Then Return
            esito.Radice = documento
            esito.Chiave = Chiave(documento)
            esito.DentroDocumento = True
            esito.EWeb = True
            esito.EApplicazione = False
            esito.ERiquadro = True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AdattaRiquadroCollegato: " & ex.Message)
        End Try
    End Sub

    Public Function ContestoFinestra(finestra As IntPtr) As ContestoNavigazione
        Dim esito As New ContestoNavigazione()
        Try
            esito.Finestra = finestra
            If _automazione Is Nothing OrElse finestra = IntPtr.Zero Then Return esito
            Dim radice As IUIAutomationElement = _automazione.ElementFromHandleBuildCache(finestra, _cache)
            If radice Is Nothing Then Return esito
            Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloDocumento)
            Dim documento As IUIAutomationElement = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizione, _cache)
            If documento Is Nothing Then
                Dim condizioneRuolo As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaRuoloAria, "document")
                documento = radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizioneRuolo, _cache)
            End If
            If documento IsNot Nothing Then
                esito.Radice = documento
                esito.DentroDocumento = True
                esito.EWeb = DescrittoreElementi.FrameworkWeb(_descrittore.Framework(radice)) OrElse DescrittoreElementi.FrameworkWeb(_descrittore.Framework(documento))
            Else
                esito.Radice = radice
                esito.EWeb = DescrittoreElementi.FrameworkWeb(_descrittore.Framework(radice))
            End If
            esito.Chiave = Chiave(esito.Radice)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ContestoFinestra: " & ex.Message)
        End Try
        Return esito
    End Function

    Public Function Costruisci(radice As IUIAutomationElement, finestra As IntPtr, web As Boolean, Optional riquadro As Boolean = False) As Integer
        Try
            _riquadro = riquadro
            _contenitoriCorrenti = New List(Of String())()
            _vociElencoNote.Clear()
            _voci = New List(Of VoceAlbero)()
            _perIdentita = New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            _vociNuove = Nothing
            _perIdentitaNuove = Nothing
            _cursoreIndice = Nothing
            _indiceCompleto = False
            _bufferDaRicostruire = False
            _radice = radice
            _finestra = finestra
            _web = web
            _chiaveRadice = Chiave(radice)
            _paginaRadice = PaginaDi(radice)
            _urlMemoria = UrlMemoriaDi(radice)
            _ambito = Nothing
            _rigaAperta = Nothing
            AzzeraAiuto()
            AzzeraMemo()
            AzzeraCursore()
            _chromium = False
            If radice Is Nothing OrElse _automazione Is Nothing Then Return 0
            _chromium = web AndAlso _descrittore.Framework(radice) = "chrome"
            _walker = _automazione.ContentViewWalker
            _usaVistaContenuto = True
            _corrente = PrimaVoce(radice, 40)
            If _corrente Is Nothing AndAlso _walkerAntenati IsNot Nothing Then
                _walker = _walkerAntenati
                _usaVistaContenuto = False
                _corrente = PrimaVoce(radice, 40)
            End If
            If _corrente Is Nothing Then _corrente = radice
            If web Then AvviaAiuto()
            Return 1
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.Costruisci: " & ex.Message)
            Return 0
        End Try
    End Function

    Public Function Sincronizza(elemento As IUIAutomationElement) As Integer
        Try
            If elemento Is Nothing OrElse _radice Is Nothing Then Return -1
            _corrente = elemento
            _testoPronto = False
            _pos = -1
            _selezioneAttiva = False
            _tutto = False
            Return 0
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.Sincronizza: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Sub Indicizza(millisecondi As Integer)
        Try
            If _radice Is Nothing OrElse _walker Is Nothing Then Return
            If GestisciAiuto() Then Return
            If _bufferDaRicostruire AndAlso _indiceCompleto AndAlso _vociNuove Is Nothing Then RicominciaIndice()
            If _indiceCompleto AndAlso _vociNuove Is Nothing Then Return
            If millisecondi < 5 Then millisecondi = 5
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim passi As Integer = 0
            While passi < 400 AndAlso orologio.ElapsedMilliseconds < millisecondi AndAlso VociInLavoro() < MassimoVoci
                If _cursoreIndice Is Nothing Then _cursoreIndice = _radice
                _cursoreIndice = NodoSuccessivo(_cursoreIndice)
                passi += 1
                If _cursoreIndice Is Nothing OrElse Stesso(_cursoreIndice, _radice) Then
                    _indiceTroncato = False
                    ChiudiIndice()
                    Return
                End If
                Dim tipo As String = Classifica(_cursoreIndice, _descrittore.TipoControllo(_cursoreIndice))
                If VoceUtile(tipo, _cursoreIndice) Then AggiungiVoce(_cursoreIndice, tipo)
            End While
            If VociInLavoro() >= MassimoVoci Then
                _indiceTroncato = True
                ChiudiIndice()
            End If
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.Indicizza: " & ex.Message)
        End Try
    End Sub

    Private Function VociInLavoro() As Integer
        Try
            If _vociNuove IsNot Nothing Then Return _vociNuove.Count
            Return _voci.Count
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.VociInLavoro: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Sub ChiudiIndice()
        Try
            If _vociNuove IsNot Nothing Then
                _voci = _vociNuove
                _perIdentita = If(_perIdentitaNuove, New Dictionary(Of String, Integer)(StringComparer.Ordinal))
                _vociNuove = Nothing
                _perIdentitaNuove = Nothing
            End If
            _indiceCompleto = True
            _indiceCompletoTick = Environment.TickCount
            System.Threading.Volatile.Write(_raccoltaConsigliata, 1)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ChiudiIndice: " & ex.Message)
        End Try
    End Sub

    Private Function IndiceInAggiornamento() As Boolean
        Try
            Return _bufferDaRicostruire OrElse _vociNuove IsNot Nothing OrElse _aiutoRichiesta IsNot Nothing
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.IndiceInAggiornamento: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function Muovi(codice As Integer, ctrl As Boolean, maiuscolo As Boolean, alt As Boolean) As String
        Try
            If _radice Is Nothing Then Return "nessun contenuto"
            RicostruisciSePaginaCambiata()
            If _corrente Is Nothing Then _corrente = _radice
            If alt AndAlso ctrl Then Return MuoviCella(codice)
            Dim livello As Integer = 0
            Dim oltre As Boolean = _oltreLaFine
            _oltreLaFine = False
            Dim tipo As String = TipoDaTasto(codice, livello)
            If tipo <> "" AndAlso Not ctrl Then Return VaiAlTipo(tipo, livello, maiuscolo)
            _ultimoARighe = False
            If Not maiuscolo AndAlso (codice = VK_LEFT OrElse codice = VK_RIGHT OrElse ((codice = VK_HOME OrElse codice = VK_END) AndAlso Not ctrl)) AndAlso TestoARighe() AndAlso RigaAllineata() Then
                Dim nellaRiga As String = MuoviNellaRiga(codice, ctrl)
                If nellaRiga <> "" Then
                    _ultimoARighe = True
                    Return nellaRiga
                End If
            End If
            If Not maiuscolo AndAlso ctrl AndAlso (codice = VK_HOME OrElse codice = VK_END) AndAlso TestoARighe() Then
                Dim estrema As String = RigaEstrema(codice = VK_HOME)
                If estrema <> "" Then
                    _ultimoARighe = True
                    Return estrema
                End If
            End If
            If Not maiuscolo AndAlso Not ctrl AndAlso (codice = VK_UP OrElse codice = VK_DOWN) AndAlso TestoARighe() Then
                Dim rigaLetta As String = RigaDiTesto(codice = VK_DOWN, oltre AndAlso codice = VK_UP)
                If rigaLetta <> "" Then
                    _oltreLaFine = codice = VK_DOWN AndAlso rigaLetta = "fine"
                    _ultimoARighe = True
                    Return rigaLetta
                End If
            End If
            Select Case codice
                Case VK_UP
                    If oltre AndAlso Not maiuscolo AndAlso _corrente IsNot Nothing AndAlso Not Stesso(_corrente, _radice) Then Return Arriva(_corrente, False, False, False, "", False)
                    Return SpostaContenuto(False, maiuscolo, False)
                Case VK_DOWN
                    Dim esitoGiu As String = SpostaContenuto(True, maiuscolo, False)
                    _oltreLaFine = esitoGiu = "fine"
                    Return esitoGiu
                Case VK_LEFT
                    If ctrl Then Return SpostaParola(False, maiuscolo)
                    Return SpostaCarattere(-1, maiuscolo)
                Case VK_RIGHT
                    If ctrl Then Return SpostaParola(True, maiuscolo)
                    Return SpostaCarattere(1, maiuscolo)
                Case VK_PRIOR
                    Return SpostaPagina(False, maiuscolo)
                Case VK_NEXT
                    Dim esitoPagina As String = SpostaPagina(True, maiuscolo)
                    _oltreLaFine = esitoPagina = "fine"
                    Return esitoPagina
                Case VK_HOME
                    If ctrl Then Return VaiEstremo(True, maiuscolo)
                    Return VaiCarattereEstremo(True, maiuscolo)
                Case VK_END
                    If ctrl Then Return VaiEstremo(False, maiuscolo)
                    Return VaiCarattereEstremo(False, maiuscolo)
                Case VK_BACK
                    If ctrl Then Return SpostaContenuto(False, maiuscolo, False)
                    Return SpostaCarattere(-1, maiuscolo)
                Case VK_DELETE
                    If ctrl Then Return SpostaContenuto(True, maiuscolo, False)
                    Return SpostaCarattere(1, maiuscolo)
                Case &HBC
                    Return FineContenitore(maiuscolo)
            End Select
            Return ""
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.Muovi: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function RigaAttualeTesto() As String
        Try
            If Not TestoARighe() Then Return ""
            Dim riga As String = RigaDiTesto(False, True)
            If riga = "fine" OrElse riga = "inizio" Then Return ""
            Return riga
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RigaAttualeTesto", ex)
            Return ""
        End Try
    End Function

    Public ReadOnly Property UltimoMovimentoARighe As Boolean
        Get
            Return _ultimoARighe
        End Get
    End Property

    Private Function TestoARighe() As Boolean
        Try
            Dim radice As IUIAutomationElement = RadiceCursore()
            If radice Is Nothing OrElse _chromium Then Return False
            If RegoleRuoli.NormalizzaFramework(_descrittore.Framework(radice)) <> "mozilla" Then Return False
            If IndirizzoMessaggio(radice) Then Return True
            Dim indirizzo As String = _descrittore.LeggiStringa(radice, DescrittoreElementi.ProprietaValore).ToLowerInvariant()
            Return indirizzo.StartsWith("http:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("https:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("file:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("about:reader", StringComparison.Ordinal)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TestoARighe", ex)
            Return False
        End Try
    End Function

    Private Function IndirizzoMessaggio(documento As IUIAutomationElement) As Boolean
        Try
            If documento Is Nothing Then Return False
            Dim indirizzo As String = _descrittore.LeggiStringa(documento, DescrittoreElementi.ProprietaValore).ToLowerInvariant()
            If indirizzo.StartsWith("imap:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("mailbox:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("news:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("nntp:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("ews:", StringComparison.Ordinal) OrElse indirizzo.StartsWith("owl:", StringComparison.Ordinal) Then Return True
            Return indirizzo.StartsWith("file:", StringComparison.Ordinal) AndAlso indirizzo.EndsWith(".eml", StringComparison.Ordinal)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.IndirizzoMessaggio", ex)
            Return False
        End Try
    End Function

    Private Function RigaDiTesto(avanti As Boolean, rileggi As Boolean) As String
        Try
            Dim radice As IUIAutomationElement = RadiceCursore()
            If radice Is Nothing Then Return ""
            Dim pattern As IUIAutomationTextPattern = TryCast(radice.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then Return ""
            Dim chiaveRadice As String = Chiave(radice)
            Dim chiaveCorrente As String = If(_corrente Is Nothing, "", Chiave(_corrente))
            Dim ancora As IUIAutomationElement = _corrente
            Dim riancorato As Boolean = False
            If _rigaTesto Is Nothing OrElse _rigaChiaveRadice <> chiaveRadice OrElse chiaveCorrente <> _rigaChiaveCorrente Then
                riancorato = True
                _segmenti = New List(Of SegmentoRiga)()
                Dim partenza As IUIAutomationTextRange = Nothing
                If _corrente IsNot Nothing AndAlso Not Stesso(_corrente, radice) AndAlso Not Stesso(_corrente, _radice) Then
                    Try
                        partenza = pattern.RangeFromChild(_corrente)
                    Catch exFiglio As Exception
                        RegistroErrori.Scrivi("NavigazioneWeb.RigaDiTesto", exFiglio)
                        partenza = Nothing
                    End Try
                    If partenza Is Nothing Then Return ""
                End If
                If partenza Is Nothing Then
                    partenza = pattern.DocumentRange
                    If partenza Is Nothing Then Return ""
                    partenza.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, partenza, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
                    partenza.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
                    If avanti AndAlso (_corrente Is Nothing OrElse Stesso(_corrente, radice) OrElse Stesso(_corrente, _radice)) Then
                        _rigaTesto = partenza
                        _rigaChiaveRadice = chiaveRadice
                        Dim prima As String = LeggiRigaTesto(False)
                        If prima <> "" Then Return prima
                    End If
                Else
                    If avanti Then
                        partenza.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, partenza, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
                        partenza.Move(TextUnit.TextUnit_Character, -1)
                    Else
                        partenza.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, partenza, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
                    End If
                    partenza.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
                End If
                _rigaTesto = partenza
                _rigaChiaveRadice = chiaveRadice
            End If
            If rileggi Then
                If Not riancorato AndAlso _segmenti IsNot Nothing AndAlso _segmenti.Count > 0 Then Return ApplicaSegmento()
                Return LeggiRigaTesto(False)
            End If
            If riancorato AndAlso ancora IsNot Nothing AndAlso Not Stesso(ancora, radice) AndAlso Not Stesso(ancora, _radice) AndAlso _rigaTesto IsNot Nothing Then
                Dim primaLettura As String = LeggiRigaTesto(False)
                If primaLettura <> "" AndAlso _segmenti IsNot Nothing Then
                    For indiceAncora As Integer = 0 To _segmenti.Count - 1
                        If _segmenti(indiceAncora).Elemento IsNot Nothing AndAlso Stesso(_segmenti(indiceAncora).Elemento, ancora) Then
                            If avanti AndAlso indiceAncora < _segmenti.Count - 1 Then
                                _indiceSegmento = indiceAncora + 1
                                Return ApplicaSegmento()
                            End If
                            If Not avanti AndAlso indiceAncora > 0 Then
                                _indiceSegmento = indiceAncora - 1
                                Return ApplicaSegmento()
                            End If
                            Exit For
                        End If
                    Next
                End If
            End If
            If Not riancorato AndAlso _segmenti IsNot Nothing AndAlso _segmenti.Count > 0 Then
                If avanti AndAlso _indiceSegmento < _segmenti.Count - 1 Then
                    _indiceSegmento += 1
                    Return ApplicaSegmento()
                End If
                If Not avanti AndAlso _indiceSegmento > 0 Then
                    _indiceSegmento -= 1
                    Return ApplicaSegmento()
                End If
            End If
            For salto As Integer = 1 To 60
                Dim mosso As Integer = _rigaTesto.Move(TextUnit.TextUnit_Line, If(avanti, 1, -1))
                If mosso = 0 Then
                    If AltroTestoOltre(pattern, avanti) Then
                        _rigaTesto = Nothing
                        Return ""
                    End If
                    Return If(avanti, "fine", "inizio")
                End If
                _rigaTesto.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
                Dim letta As String = LeggiRigaTesto(Not avanti)
                If letta <> "" Then Return letta
            Next
            Return If(avanti, "fine", "inizio")
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RigaDiTesto", ex)
            _rigaTesto = Nothing
            Return ""
        End Try
    End Function

    Private Function RigaAllineata() As Boolean
        Try
            If _rigaTesto Is Nothing OrElse _testoRiga = "" Then Return False
            Dim radice As IUIAutomationElement = RadiceCursore()
            If radice Is Nothing OrElse _rigaChiaveRadice <> Chiave(radice) Then Return False
            Dim chiaveCorrente As String = If(_corrente Is Nothing, "", Chiave(_corrente))
            Return chiaveCorrente = _rigaChiaveCorrente
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RigaAllineata", ex)
            Return False
        End Try
    End Function

    Private Function MuoviNellaRiga(codice As Integer, ctrl As Boolean) As String
        Try
            Dim testo As String = _testoRiga
            If testo = "" Then Return ""
            Select Case codice
                Case VK_HOME
                    _posRiga = 0
                    Return _descrittore.NomeCarattere(testo.Substring(0, 1))
                Case VK_END
                    _posRiga = testo.Length - 1
                    Return _descrittore.NomeCarattere(testo.Substring(_posRiga, 1))
            End Select
            Dim avanti As Boolean = codice = VK_RIGHT
            If ctrl Then Return ParolaNellaRiga(avanti)
            If _posRiga < 0 Then
                _posRiga = If(avanti, 0, testo.Length - 1)
                Return _descrittore.NomeCarattere(testo.Substring(_posRiga, 1))
            End If
            Dim nuovo As Integer = _posRiga + If(avanti, 1, -1)
            If nuovo >= 0 AndAlso nuovo < testo.Length Then
                _posRiga = nuovo
                Return _descrittore.NomeCarattere(testo.Substring(nuovo, 1))
            End If
            Dim altra As String = RigaDiTesto(avanti, False)
            If altra = "" Then Return ""
            If altra = "fine" OrElse altra = "inizio" Then Return altra
            If _testoRiga = "" Then Return altra
            _posRiga = If(avanti, 0, _testoRiga.Length - 1)
            Return _descrittore.NomeCarattere(_testoRiga.Substring(_posRiga, 1))
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.MuoviNellaRiga", ex)
            Return ""
        End Try
    End Function

    Private Function ParolaNellaRiga(avanti As Boolean) As String
        Try
            Dim testo As String = _testoRiga
            Dim posizione As Integer = _posRiga
            If avanti Then
                If posizione < 0 Then
                    posizione = 0
                Else
                    While posizione < testo.Length AndAlso Not Char.IsWhiteSpace(testo(posizione))
                        posizione += 1
                    End While
                End If
                While posizione < testo.Length AndAlso Char.IsWhiteSpace(testo(posizione))
                    posizione += 1
                End While
                If posizione >= testo.Length Then
                    Dim altra As String = RigaDiTesto(True, False)
                    If altra = "" OrElse altra = "fine" OrElse _testoRiga = "" Then Return altra
                    _posRiga = 0
                    Return ParolaDa(_testoRiga, 0)
                End If
            Else
                If posizione < 0 Then posizione = testo.Length
                posizione -= 1
                While posizione >= 0 AndAlso Char.IsWhiteSpace(testo(posizione))
                    posizione -= 1
                End While
                If posizione < 0 Then
                    Dim altra As String = RigaDiTesto(False, False)
                    If altra = "" OrElse altra = "inizio" OrElse _testoRiga = "" Then Return altra
                    posizione = _testoRiga.Length - 1
                    testo = _testoRiga
                End If
                While posizione > 0 AndAlso Not Char.IsWhiteSpace(testo(posizione - 1))
                    posizione -= 1
                End While
            End If
            _posRiga = posizione
            Return ParolaDa(testo, posizione)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ParolaNellaRiga", ex)
            Return ""
        End Try
    End Function

    Private Function ParolaDa(testo As String, posizione As Integer) As String
        If String.IsNullOrEmpty(testo) OrElse posizione < 0 OrElse posizione >= testo.Length Then Return ""
        Dim fine As Integer = posizione
        While fine < testo.Length AndAlso Not Char.IsWhiteSpace(testo(fine))
            fine += 1
        End While
        Dim parola As String = testo.Substring(posizione, fine - posizione)
        If parola.Length = 1 Then Return _descrittore.NomeCarattere(parola)
        Return parola
    End Function

    Private Function RigaEstrema(inizio As Boolean) As String
        Try
            Dim radice As IUIAutomationElement = RadiceCursore()
            If radice Is Nothing Then Return ""
            Dim pattern As IUIAutomationTextPattern = TryCast(radice.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then Return ""
            Dim intervallo As IUIAutomationTextRange = pattern.DocumentRange
            If intervallo Is Nothing Then Return ""
            If inizio Then
                intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
            Else
                intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
                intervallo.Move(TextUnit.TextUnit_Character, -1)
            End If
            intervallo.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
            _rigaTesto = intervallo
            _rigaChiaveRadice = Chiave(radice)
            _oltreLaFine = False
            Dim letta As String = LeggiRigaTesto(Not inizio)
            For salto As Integer = 1 To 60
                If letta <> "" Then Exit For
                If _rigaTesto.Move(TextUnit.TextUnit_Line, If(inizio, 1, -1)) = 0 Then Exit For
                _rigaTesto.ExpandToEnclosingUnit(TextUnit.TextUnit_Line)
                letta = LeggiRigaTesto(Not inizio)
            Next
            If letta = "" Then
                _rigaTesto = Nothing
                Return ""
            End If
            Return letta & If(inizio, ", inizio", ", fine")
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RigaEstrema", ex)
            _rigaTesto = Nothing
            Return ""
        End Try
    End Function

    Private Function AltroTestoOltre(pattern As IUIAutomationTextPattern, avanti As Boolean) As Boolean
        Try
            Dim resto As IUIAutomationTextRange = pattern.DocumentRange
            If resto Is Nothing OrElse _rigaTesto Is Nothing Then Return False
            If avanti Then
                resto.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, _rigaTesto, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
            Else
                resto.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, _rigaTesto, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
            End If
            Dim testo As String = If(resto.GetText(400), "")
            Return testo.Replace(ChrW(&HFFFC), " ").Trim() <> ""
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.AltroTestoOltre", ex)
            Return False
        End Try
    End Function

    Private NotInheritable Class SegmentoRiga
        Public Testo As String = ""
        Public Annuncio As String = ""
        Public Elemento As IUIAutomationElement
    End Class

    Private Function LeggiRigaTesto(dallaFine As Boolean) As String
        Try
            If _rigaTesto Is Nothing Then Return ""
            Dim testo As String = If(_rigaTesto.GetText(2000), "")
            testo = testo.Replace(ChrW(&HFFFC), " ").Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If testo = "" Then Return ""
            Dim racchiuso As IUIAutomationElement = Nothing
            Try
                racchiuso = _rigaTesto.GetEnclosingElement()
            Catch exRacchiuso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.LeggiRigaTesto", exRacchiuso)
            End Try
            If racchiuso IsNot Nothing AndAlso (Stesso(racchiuso, _radice) OrElse Stesso(racchiuso, RadiceCursore()) OrElse Not DentroDi(racchiuso, RadiceCursore())) Then racchiuso = Nothing
            Dim figli As IUIAutomationElementArray = Nothing
            Try
                figli = _rigaTesto.GetChildren()
            Catch exFigli As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.LeggiRigaTesto", exFigli)
            End Try
            Dim segmenti As New List(Of SegmentoRiga)()
            Dim immagini As New List(Of SegmentoRiga)()
            Dim livelloTitolo As Integer = TitoloDellaRiga(racchiuso, figli)
            Dim cercaDa As Integer = 0
            If figli IsNot Nothing Then
                For indice As Integer = 0 To Math.Min(figli.Length, 20) - 1
                    Dim figlio As IUIAutomationElement = figli.GetElement(indice)
                    Dim tipoFiglio As String = Classifica(figlio, _descrittore.TipoControllo(figlio))
                    If tipoFiglio <> "collegamento" AndAlso tipoFiglio <> "pulsante" AndAlso tipoFiglio <> "grafica" Then Continue For
                    Dim nomeFiglio As String = _descrittore.Nome(figlio).Trim()
                    If nomeFiglio = "" Then Continue For
                    Dim posizione As Integer = testo.IndexOf(nomeFiglio, cercaDa, StringComparison.CurrentCulture)
                    If posizione < 0 Then
                        If tipoFiglio = "grafica" Then immagini.Add(New SegmentoRiga With {.Testo = nomeFiglio, .Annuncio = "grafica " & nomeFiglio, .Elemento = figlio})
                        Continue For
                    End If
                    Dim prima As String = testo.Substring(cercaDa, posizione - cercaDa).Trim()
                    If prima <> "" Then segmenti.Add(New SegmentoRiga With {.Testo = prima, .Annuncio = prima})
                    Dim ruolo As String = If(tipoFiglio = "collegamento", "link", If(tipoFiglio = "pulsante", "pulsante", "grafica"))
                    Dim annuncio As String = ruolo & " " & nomeFiglio
                    If tipoFiglio = "collegamento" Then annuncio = LinkStessaPagina(figlio, annuncio)
                    segmenti.Add(New SegmentoRiga With {.Testo = nomeFiglio, .Annuncio = annuncio, .Elemento = figlio})
                    cercaDa = posizione + nomeFiglio.Length
                Next
            End If
            Dim resto As String = If(cercaDa < testo.Length, testo.Substring(cercaDa).Trim(), "")
            If resto <> "" Then segmenti.Add(New SegmentoRiga With {.Testo = resto, .Annuncio = resto})
            segmenti.AddRange(immagini)
            If segmenti.Count = 0 Then segmenti.Add(New SegmentoRiga With {.Testo = testo, .Annuncio = testo})
            If segmenti.Count = 1 AndAlso segmenti(0).Elemento Is Nothing AndAlso racchiuso IsNot Nothing AndAlso Classifica(racchiuso, _descrittore.TipoControllo(racchiuso)) = "collegamento" Then
                segmenti(0).Annuncio = LinkStessaPagina(racchiuso, "link " & segmenti(0).Annuncio)
                segmenti(0).Elemento = racchiuso
            End If
            If livelloTitolo > 0 Then segmenti(0).Annuncio = _descrittore.FraseRuoloTitolo(livelloTitolo) & ", " & segmenti(0).Annuncio
            _segmenti = segmenti
            _racchiusoRiga = racchiuso
            _indiceSegmento = If(dallaFine, segmenti.Count - 1, 0)
            Return ApplicaSegmento()
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.LeggiRigaTesto", ex)
            Return ""
        End Try
    End Function

    Private Function ApplicaSegmento() As String
        Try
            If _segmenti Is Nothing OrElse _segmenti.Count = 0 Then Return ""
            If _indiceSegmento < 0 Then _indiceSegmento = 0
            If _indiceSegmento >= _segmenti.Count Then _indiceSegmento = _segmenti.Count - 1
            Dim segmento As SegmentoRiga = _segmenti(_indiceSegmento)
            _testoRiga = segmento.Testo
            _posRiga = -1
            Dim bersaglio As IUIAutomationElement = If(segmento.Elemento, _racchiusoRiga)
            If bersaglio IsNot Nothing Then
                _corrente = bersaglio
                _testoPronto = False
                _pos = -1
                _selezioneAttiva = False
                _tutto = False
            End If
            _rigaChiaveCorrente = If(_corrente Is Nothing, "", Chiave(_corrente))
            Return segmento.Annuncio
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ApplicaSegmento", ex)
            Return ""
        End Try
    End Function

    Private Function TitoloDellaRiga(racchiuso As IUIAutomationElement, figli As IUIAutomationElementArray) As Integer
        Try
            Dim corrente As IUIAutomationElement = racchiuso
            For salto As Integer = 0 To 3
                If corrente Is Nothing OrElse Stesso(corrente, RadiceCursore()) OrElse Stesso(corrente, _radice) Then Exit For
                If Classifica(corrente, _descrittore.TipoControllo(corrente)) = "titolo" Then
                    Dim livello As Integer = LivelloTitoloVoce(corrente)
                    Return If(livello > 0, livello, 2)
                End If
                corrente = GenitoreControllo(corrente)
            Next
            If figli IsNot Nothing Then
                For indice As Integer = 0 To Math.Min(figli.Length, 10) - 1
                    Dim figlio As IUIAutomationElement = figli.GetElement(indice)
                    If Classifica(figlio, _descrittore.TipoControllo(figlio)) = "titolo" Then
                        Dim livelloFiglio As Integer = LivelloTitoloVoce(figlio)
                        Return If(livelloFiglio > 0, livelloFiglio, 2)
                    End If
                Next
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TitoloDellaRiga", ex)
        End Try
        Return 0
    End Function

    Public Function TestoSelezione() As String
        Try
            If Not _selezioneAttiva OrElse _corrente Is Nothing Then Return ""
            If _tutto Then Return RaccogliTutto()
            If _ancora Is Nothing Then Return ""
            If Stesso(_ancora, _corrente) Then Return Porzione(TestoCorrente(), _ancoraPos, _pos)
            Dim inAvanti As Boolean = _verso >= _ancoraVerso
            Dim primo As IUIAutomationElement = If(inAvanti, _ancora, _corrente)
            Dim primoPos As Integer = If(inAvanti, _ancoraPos, _pos)
            Dim ultimo As IUIAutomationElement = If(inAvanti, _corrente, _ancora)
            Dim ultimoPos As Integer = If(inAvanti, _pos, _ancoraPos)
            Return Raccogli(primo, primoPos, ultimo, ultimoPos)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.TestoSelezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub SelezionaTutto()
        Try
            If _radice Is Nothing Then Return
            _selezioneAttiva = True
            _tutto = True
            _ancora = _corrente
            _ancoraPos = 0
            _ancoraVerso = _verso
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.SelezionaTutto: " & ex.Message)
        End Try
    End Sub

    Public Function ElementoCorrente() As IUIAutomationElement
        Try
            Return _corrente
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ElementoCorrente: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Function AnnuncioCorrente() As String
        Try
            If _corrente Is Nothing Then Return ""
            Return Annuncio(_corrente)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AnnuncioCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function TestoDalCorrente() As String
        Try
            If _corrente Is Nothing AndAlso _radice Is Nothing Then Return ""
            If _indiceCompleto Then
                Dim esatto As Boolean = False
                Dim indice As Integer = IndiceVicino(_corrente, esatto)
                If indice >= 0 AndAlso Not esatto Then indice += 1
                If indice >= 0 AndAlso indice < _voci.Count Then
                    Dim completo As New StringBuilder()
                    Dim primo As Integer = indice
                    If esatto AndAlso _pos > 0 Then
                        Dim proprio As String = TestoCorrente()
                        If _pos < proprio.Length Then AccodaPezzo(completo, proprio.Substring(_pos))
                        primo = indice + 1
                    End If
                    For posizione As Integer = primo To _voci.Count - 1
                        AccodaPezzo(completo, _voci(posizione).Testo)
                        If completo.Length > 50000 Then Exit For
                    Next
                    Return completo.ToString()
                End If
            End If
            Dim testo As New StringBuilder()
            Dim nodo As IUIAutomationElement = _corrente
            If nodo Is Nothing Then nodo = _radice
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim giri As Integer = 0
            Dim primoGiro As Boolean = True
            While nodo IsNot Nothing AndAlso giri < 400 AndAlso orologio.ElapsedMilliseconds < 1200 AndAlso testo.Length < 20000
                If Not Stesso(nodo, _radice) Then
                    Dim pezzo As String = TestoElemento(nodo)
                    If primoGiro AndAlso _pos > 0 AndAlso pezzo IsNot Nothing AndAlso _pos < pezzo.Length Then pezzo = pezzo.Substring(_pos)
                    AccodaPezzo(testo, pezzo)
                End If
                primoGiro = False
                Dim fine As Boolean = False
                nodo = ProssimaVoceTenace(nodo, True, 300, fine)
                If nodo Is Nothing Then Exit While
                giri += 1
            End While
            Return testo.ToString()
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.TestoDalCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function CarattereCorrente() As String
        Try
            If _corrente Is Nothing Then Return ""
            If _descrittore.Protetto(_corrente) Then Return "stella"
            If TestoARighe() AndAlso RigaAllineata() Then
                Dim posizioneRiga As Integer = Math.Max(0, Math.Min(_posRiga, _testoRiga.Length - 1))
                Return _descrittore.NomeCarattere(_testoRiga.Substring(posizioneRiga, 1))
            End If
            Dim testo As String = TestoCorrente()
            If testo.Length = 0 Then Return "vuoto"
            Dim posizione As Integer = _pos
            If posizione < 0 Then posizione = 0
            If posizione >= testo.Length Then posizione = testo.Length - 1
            Return _descrittore.NomeCarattere(testo.Substring(posizione, 1))
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CarattereCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function ParolaCorrente() As String
        Try
            If _corrente Is Nothing Then Return ""
            If _descrittore.Protetto(_corrente) Then Return "protetto"
            Dim testo As String = TestoCorrente()
            Dim posizioneTesto As Integer = _pos
            If TestoARighe() AndAlso RigaAllineata() Then
                testo = _testoRiga
                posizioneTesto = _posRiga
            End If
            If testo.Length = 0 Then Return "vuoto"
            Dim posizione As Integer = posizioneTesto
            If posizione < 0 Then posizione = 0
            If posizione >= testo.Length Then posizione = testo.Length - 1
            Dim inizio As Integer = posizione
            While inizio > 0 AndAlso Not Char.IsWhiteSpace(testo(inizio - 1))
                inizio -= 1
            End While
            Dim fine As Integer = posizione
            While fine < testo.Length AndAlso Not Char.IsWhiteSpace(testo(fine))
                fine += 1
            End While
            If fine <= inizio Then Return "vuoto"
            Dim parola As String = testo.Substring(inizio, fine - inizio)
            If parola.Length = 1 Then Return _descrittore.NomeCarattere(parola)
            Return parola
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ParolaCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function RichiedeScrittura(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 8
                If corrente Is Nothing Then Exit For
                If _descrittore.ECampoScrittura(corrente) Then Return True
                If salto = 0 AndAlso DocumentoEditabile(corrente) Then Return True
                If EditabileRicco(corrente, True) Then Return True
                If _radice IsNot Nothing AndAlso Stesso(corrente, _radice) Then Exit For
                corrente = GenitoreControllo(corrente)
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RichiedeScrittura: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function ElementoPerFuoco(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing Then Return Nothing
            Dim tipo As String = Classifica(elemento, _descrittore.TipoControllo(elemento))
            Select Case tipo
                Case "collegamento", "pulsante", "modifica", "casella", "opzione", "combinata"
                    Return elemento
                Case "titolo"
                    Return TrovaInterattivo(elemento, 0)
            End Select
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ElementoPerFuoco: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Public Sub ImpostaFocus(elemento As IUIAutomationElement)
        Try
            Dim catena As New HashSet(Of String)(StringComparer.Ordinal)
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 16
                If corrente Is Nothing Then Exit For
                Dim identita As String = Chiave(corrente)
                If identita <> "" Then catena.Add(identita)
                corrente = GenitoreControllo(corrente)
            Next
            _catenaFocus = catena
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ImpostaFocus: " & ex.Message)
        End Try
    End Sub

    Public Function RichiedeModalitaFocus(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If _descrittore.MessaggioTeams(elemento, _walkerAntenati, _cache) Then Return True
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            If RuoliModalitaFocus.Contains(ruolo) Then Return True
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloVoceMenu, DescrittoreElementi.ControlloMenu, DescrittoreElementi.ControlloBarraMenu,
                     DescrittoreElementi.ControlloCasellaCombinata, DescrittoreElementi.ControlloDispositivoScorrimento,
                     DescrittoreElementi.ControlloSelettoreNumerico, DescrittoreElementi.ControlloScheda,
                     DescrittoreElementi.ControlloElementoAlbero, DescrittoreElementi.ControlloElementoDati,
                     DescrittoreElementi.ControlloElementoElenco
                    Return True
            End Select
            If VoceGestitaDallApplicazione(elemento, ruolo) Then Return True
            If _descrittore.VoceDiComposito(elemento) OrElse _descrittore.ContenitoreComposito(elemento) Then Return True
            If LetturaAlberi.NelAlbero(elemento, _descrittore, _walkerAntenati, _cache) Then Return True
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 4
                If corrente Is Nothing Then Exit For
                Dim ruoloAntenato As String = _descrittore.RuoloAria(corrente)
                If salto <= 1 AndAlso (ruoloAntenato = "toolbar" OrElse ruoloAntenato = "menubar") Then Return True
                If ruoloAntenato = "menu" OrElse ruoloAntenato = "listbox" OrElse ruoloAntenato = "tree" OrElse ruoloAntenato = "tablist" Then Return True
                If ruoloAntenato = "grid" OrElse ruoloAntenato = "treegrid" Then Return True
                If ruoloAntenato = "radiogroup" AndAlso ruolo = "radio" Then Return True
                If _descrittore.TipoControllo(corrente) = DescrittoreElementi.ControlloDocumento Then Exit For
                If _radice IsNot Nothing AndAlso Stesso(corrente, _radice) Then Exit For
                corrente = GenitoreControllo(corrente)
            Next
            Return False
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RichiedeModalitaFocus: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function VoceGestitaDallApplicazione(elemento As IUIAutomationElement, ruolo As String) As Boolean
        Try
            If ruolo = "article" OrElse ruolo = "listitem" OrElse ruolo = "group" OrElse ruolo = "" Then
                Dim tipo As Integer = _descrittore.TipoControllo(elemento)
                Dim interattivo As Boolean = tipo = DescrittoreElementi.ControlloElementoElenco OrElse ruolo = "article" OrElse ruolo = "listitem"
                If Not interattivo AndAlso (tipo = DescrittoreElementi.ControlloGruppo OrElse tipo = DescrittoreElementi.ControlloPersonalizzato OrElse tipo = DescrittoreElementi.ControlloRiquadro) Then
                    Dim padre As IUIAutomationElement = GenitoreControllo(elemento)
                    For salto As Integer = 0 To 3
                        If padre Is Nothing Then Exit For
                        Dim ruoloPadre As String = _descrittore.RuoloAria(padre)
                        If ruoloPadre = "list" OrElse ruoloPadre = "feed" OrElse ruoloPadre = "listbox" OrElse ruoloPadre = "grid" OrElse ruoloPadre = "treegrid" OrElse ruoloPadre = "tree" Then interattivo = True : Exit For
                        If _descrittore.TipoControllo(padre) = DescrittoreElementi.ControlloElenco Then interattivo = True : Exit For
                        padre = GenitoreControllo(padre)
                    Next
                End If
                Return interattivo
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.VoceGestitaDallApplicazione", exAtteso)
        End Try
        Return False
    End Function

    Private Function ContenitoreChiuso(elemento As IUIAutomationElement, tipoControllo As Integer) As Boolean
        Try
            If Not NascondiMenuChiusi OrElse Not _web Then Return False
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            Dim eMenu As Boolean = ruolo = "menu" OrElse (ruolo = "" AndAlso tipoControllo = DescrittoreElementi.ControlloMenu)
            Dim eBarra As Boolean = ruolo = "menubar" OrElse ruolo = "toolbar" OrElse (ruolo = "" AndAlso tipoControllo = DescrittoreElementi.ControlloBarraStrumenti)
            If Not eMenu AndAlso Not eBarra Then Return False
            Dim identita As String = Chiave(elemento)
            If identita <> "" AndAlso _catenaFocus.Contains(identita) Then Return False
            If eMenu AndAlso _descrittore.FuoriSchermo(elemento) Then Return True
            Return DentroVoceElenco(elemento)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ContenitoreChiuso", exAtteso)
            Return False
        End Try
    End Function

    Private Function DentroVoceElenco(elemento As IUIAutomationElement) As Boolean
        Try
            Dim corrente As IUIAutomationElement = GenitoreControllo(elemento)
            For salto As Integer = 0 To 12
                If corrente Is Nothing Then Exit For
                Select Case _descrittore.RuoloAria(corrente)
                    Case "listitem", "treeitem", "row", "gridcell", "cell", "option", "article"
                        Return True
                End Select
                Select Case _descrittore.TipoControllo(corrente)
                    Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoAlbero, DescrittoreElementi.ControlloElementoDati
                        Return True
                End Select
                corrente = GenitoreControllo(corrente)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.DentroVoceElenco", exAtteso)
        End Try
        Return False
    End Function

    Public Function Attiva(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return "nessun elemento"
            Dim tipoElemento As String = Classifica(elemento, _descrittore.TipoControllo(elemento))
            If tipoElemento = "titolo" OrElse tipoElemento = "testo" OrElse tipoElemento = "cella" OrElse tipoElemento = "voce" OrElse tipoElemento = "grafica" OrElse tipoElemento = "altro" Then
                Dim interno As IUIAutomationElement = Nothing
                If tipoElemento = "titolo" OrElse tipoElemento = "testo" Then interno = TrovaInterattivo(elemento, 0)
                If interno Is Nothing Then interno = AntenatoInterattivo(elemento)
                If interno IsNot Nothing Then
                    elemento = interno
                    tipoElemento = Classifica(elemento, _descrittore.TipoControllo(elemento))
                End If
            End If
            Try
                Dim scorrimento As IUIAutomationScrollItemPattern = TryCast(elemento.GetCurrentPattern(SchemaScorrimentoVoce), IUIAutomationScrollItemPattern)
                If scorrimento IsNot Nothing Then scorrimento.ScrollIntoView()
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.Attiva", exAtteso)
            End Try
            If RichiedeScrittura(elemento) Then
                elemento.SetFocus()
                Return "scrittura"
            End If
            Dim interruttore As IUIAutomationTogglePattern = TryCast(elemento.GetCurrentPattern(SchemaAttivazione), IUIAutomationTogglePattern)
            If interruttore IsNot Nothing AndAlso (tipoElemento = "casella" OrElse tipoElemento = "pulsante") Then
                Dim prima As Integer = CInt(interruttore.CurrentToggleState)
                interruttore.Toggle()
                If tipoElemento = "pulsante" Then Return If(prima = 1, "non premuto", "premuto")
                Return If(prima = 1, "non selezionato", "selezionato")
            End If
            Dim voce As IUIAutomationSelectionItemPattern = TryCast(elemento.GetCurrentPattern(SchemaSelezioneVoce), IUIAutomationSelectionItemPattern)
            If voce IsNot Nothing AndAlso tipoElemento = "opzione" Then
                voce.Select()
                Return "selezionato"
            End If
            Dim invocazione As IUIAutomationInvokePattern = TryCast(elemento.GetCurrentPattern(SchemaInvocazione), IUIAutomationInvokePattern)
            If invocazione IsNot Nothing Then
                Try
                    invocazione.Invoke()
                    Return ""
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("NavigazioneWeb.Attiva", exAtteso2)
                End Try
            End If
            If interruttore IsNot Nothing Then
                Dim primaStato As Integer = CInt(interruttore.CurrentToggleState)
                interruttore.Toggle()
                Return If(primaStato = 1, "non selezionato", "selezionato")
            End If
            If voce IsNot Nothing Then
                voce.Select()
                Return "selezionato"
            End If
            Dim espansione As IUIAutomationExpandCollapsePattern = TryCast(elemento.GetCurrentPattern(SchemaEspansione), IUIAutomationExpandCollapsePattern)
            If espansione IsNot Nothing Then
                If CInt(espansione.CurrentExpandCollapseState) = 1 Then
                    espansione.Collapse()
                    Return "compresso"
                End If
                espansione.Expand()
                Return "espanso"
            End If
            Dim legacy As IUIAutomationLegacyIAccessiblePattern = TryCast(elemento.GetCurrentPattern(SchemaAccessibilita), IUIAutomationLegacyIAccessiblePattern)
            If legacy IsNot Nothing AndAlso Not String.IsNullOrEmpty(legacy.CurrentDefaultAction) Then
                legacy.DoDefaultAction()
                Return ""
            End If
            elemento.SetFocus()
            Return ""
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.Attiva: " & ex.Message)
            Return "non attivabile"
        End Try
    End Function

    Private Function AntenatoInterattivo(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            Dim corrente As IUIAutomationElement = GenitoreControllo(elemento)
            For salto As Integer = 0 To 3
                If corrente Is Nothing OrElse Stesso(corrente, _radice) Then Return Nothing
                Select Case Classifica(corrente, _descrittore.TipoControllo(corrente))
                    Case "collegamento", "pulsante"
                        Return corrente
                End Select
                corrente = GenitoreControllo(corrente)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.AntenatoInterattivo", exAtteso)
        End Try
        Return Nothing
    End Function

    Private Sub AzzeraCursore()
        _regioneCorrente = ""
        _sospesa = Nothing
        _pos = -1
        _verso = 0
        _selezioneAttiva = False
        _tutto = False
        _ancora = Nothing
        _ancoraPos = 0
        _ancoraVerso = 0
        _testoPronto = False
        _testoCorrente = ""
    End Sub

    Private Sub AzzeraMemo()
        _nascostoNoto.Clear()
        _corpoNoto.Clear()
        _contenutoNoto.Clear()
        _titoloDentroNoto.Clear()
        _regioniNote.Clear()
        _titoloInLinkNoto.Clear()
        _lunghezzaNota.Clear()
        _editabileNoto.Clear()
        _mailSingola = Nothing
        _mailSingolaChiave = ""
        _mailSingolaTick = Integer.MinValue
    End Sub

    Private Function MisuraValida(memoria As System.Collections.Concurrent.ConcurrentDictionary(Of String, MisuraNota), identita As String, ByRef valore As Integer) As Boolean
        valore = 0
        Try
            If identita = "" Then Return False
            Dim nota As MisuraNota = Nothing
            If Not memoria.TryGetValue(identita, nota) OrElse nota Is Nothing Then Return False
            Dim trascorso As Integer = Environment.TickCount - nota.Tick
            If trascorso < 0 OrElse trascorso > ValiditaMisuraMs Then Return False
            valore = nota.Valore
            Return True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.MisuraValida: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RicordaMisura(memoria As System.Collections.Concurrent.ConcurrentDictionary(Of String, MisuraNota), identita As String, valore As Integer)
        Try
            If identita = "" Then Return
            If memoria.Count > 4000 Then memoria.Clear()
            memoria(identita) = New MisuraNota With {.Valore = valore, .Tick = Environment.TickCount}
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RicordaMisura: " & ex.Message)
        End Try
    End Sub

    Private Function EditabileRicco(elemento As IUIAutomationElement, richiedeFuoco As Boolean) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If richiedeFuoco Then Return EditabileRiccoLetto(elemento, True)
            Dim identita As String = Chiave(elemento)
            Dim noto As Integer = 0
            If MisuraValida(_editabileNoto, identita, noto) Then Return noto = 1
            Dim esito As Boolean = EditabileRiccoLetto(elemento, False)
            RicordaMisura(_editabileNoto, identita, If(esito, 1, 0))
            Return esito
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.EditabileRicco: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function EditabileRiccoLetto(elemento As IUIAutomationElement, richiedeFuoco As Boolean) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloPersonalizzato, DescrittoreElementi.ControlloGruppo, DescrittoreElementi.ControlloRiquadro, 0
                Case Else
                    Return False
            End Select
            If Not _descrittore.FuocoDaTastiera(elemento) OrElse Not _descrittore.HaPatternTesto(elemento) Then Return False
            If _descrittore.SolaLettura(elemento) Then Return False
            If _radice IsNot Nothing AndAlso Stesso(elemento, _radice) Then Return False
            If _chromium Then
                If Not _descrittore.LeggiBooleano(elemento, DescrittoreElementi.ProprietaValoreDisponibile) Then Return False
                Return Not richiedeFuoco OrElse elemento.CurrentHasKeyboardFocus <> 0
            End If
            If richiedeFuoco AndAlso elemento.CurrentHasKeyboardFocus = 0 Then Return False
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then Return False
            Dim valore As Object = pattern.DocumentRange.GetAttributeValue(40015)
            Return TypeOf valore Is Boolean AndAlso Not CBool(valore)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.EditabileRiccoLetto", exAtteso)
            Return False
        End Try
    End Function

    Public Function ImpostaPuntoPartenzaFuoco(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Not _web Then Return False
            Dim contenitore As IUIAutomationElement = GenitoreControllo(elemento)
            For salto As Integer = 0 To 30
                If contenitore Is Nothing Then Exit For
                If _descrittore.HaPatternTesto(contenitore) Then
                    Try
                        Dim pattern As IUIAutomationTextPattern = TryCast(contenitore.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
                        If pattern IsNot Nothing Then
                            Dim intervallo As IUIAutomationTextRange = pattern.RangeFromChild(elemento)
                            If intervallo IsNot Nothing Then
                                intervallo.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
                                intervallo.Select()
                                Return True
                            End If
                        End If
                    Catch exAtteso As Exception
                        RegistroErrori.Scrivi("NavigazioneWeb.ImpostaPuntoPartenzaFuoco", exAtteso)
                    End Try
                End If
                If _radice IsNot Nothing AndAlso Stesso(contenitore, _radice) Then Exit For
                contenitore = GenitoreControllo(contenitore)
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ImpostaPuntoPartenzaFuoco: " & ex.Message)
        End Try
        Return False
    End Function

    Public Function EDocumentoORadice(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _radice Is Nothing Then Return False
            If _descrittore.TipoControllo(elemento) = DescrittoreElementi.ControlloDocumento AndAlso Not DocumentoEditabile(elemento) Then Return True
            Return Stesso(elemento, _radice)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.EDocumentoORadice", exAtteso)
            Return False
        End Try
    End Function

    Private Function DocumentoEditabile(elemento As IUIAutomationElement) As Boolean
        Try
            If _descrittore.TipoControllo(elemento) <> DescrittoreElementi.ControlloDocumento Then Return False
            If Chiave(elemento) = _chiaveRadice Then Return False
            If _descrittore.SolaLettura(elemento) Then Return False
            Return _descrittore.FuocoDaTastiera(elemento)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.DocumentoEditabile", exAtteso)
            Return False
        End Try
    End Function

    Private Function ContinuaDopoMessaggio(seleziona As Boolean, salto As Boolean) As String
        Try
            Dim lasciato As IUIAutomationElement = _corrente
            ChiudiMessaggio()
            Dim fine As Boolean = False
            Dim destinazione As IUIAutomationElement = ProssimaVoce(lasciato, True, 90, fine)
            If destinazione Is Nothing Then Return ""
            Return Arriva(destinazione, seleziona, True, False, "", salto)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ContinuaDopoMessaggio: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function SpostaContenuto(avanti As Boolean, seleziona As Boolean, salto As Boolean) As String
        Dim frase As String = ""
        Try
            For tentativo As Integer = 1 To 20
                frase = SpostaContenutoPasso(avanti, seleziona, salto)
                If frase <> "" Then Return frase
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.SpostaContenuto: " & ex.Message)
        End Try
        Return frase
    End Function

    ' Il campo di scrittura del documento (la casella dei messaggi di una chat, per esempio), che non sia già quello con il fuoco.
    Public Function CampoDiScrittura(fuocoAttuale As IUIAutomationElement) As IUIAutomationElement
        Try
            If _radice Is Nothing OrElse _automazione Is Nothing Then Return Nothing
            Dim focalizzabile As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaFuocoTastiera, True)
            Dim modifica As IUIAutomationCondition = _automazione.CreatePropertyCondition(DescrittoreElementi.ProprietaTipoControllo, DescrittoreElementi.ControlloModifica)
            Dim candidati As IUIAutomationElementArray = _radice.FindAllBuildCache(TreeScope.TreeScope_Descendants, _automazione.CreateAndCondition(focalizzabile, modifica), _cache)
            If candidati Is Nothing Then Return Nothing
            For indice As Integer = 0 To Math.Min(candidati.Length, 40) - 1
                Dim candidato As IUIAutomationElement = candidati.GetElement(indice)
                If fuocoAttuale IsNot Nothing AndAlso Stesso(candidato, fuocoAttuale) Then Continue For
                If _descrittore.CampoSolaLettura(candidato) Then Continue For
                Return candidato
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CampoDiScrittura: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Public Function FocalizzabileDalCursore(avanti As Boolean, rifiutato As IUIAutomationElement) As IUIAutomationElement
        Try
            If _corrente Is Nothing OrElse _radice Is Nothing Then Return Nothing
            Dim proprio As IUIAutomationElement = FocalizzabileDi(_corrente, Classifica(_corrente, _descrittore.TipoControllo(_corrente)))
            Dim esaurito As Boolean = False
            Dim ultimo As IUIAutomationElement = Nothing
            Dim trovato As IUIAutomationElement = FocalizzabileVivo(_corrente, avanti, proprio, rifiutato, AttesaTabRapidaMs, esaurito, ultimo)
            If trovato IsNot Nothing OrElse Not esaurito Then Return trovato
            If _indiceCompleto AndAlso Not IndiceInAggiornamento() Then
                Dim dallIndice As IUIAutomationElement = FocalizzabileDallIndice(avanti, proprio, rifiutato)
                If dallIndice IsNot Nothing Then Return dallIndice
            End If
            If ultimo IsNot Nothing Then
                trovato = FocalizzabileVivo(ultimo, avanti, proprio, rifiutato, AttesaTabMs - AttesaTabRapidaMs, esaurito, ultimo)
                If trovato IsNot Nothing OrElse Not esaurito Then Return trovato
            End If
            Return FocalizzabileDallIndice(avanti, proprio, rifiutato)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FocalizzabileDalCursore: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function FocalizzabileVivo(partenza As IUIAutomationElement, avanti As Boolean, proprio As IUIAutomationElement, rifiutato As IUIAutomationElement, budget As Integer, ByRef esaurito As Boolean, ByRef ultimo As IUIAutomationElement) As IUIAutomationElement
        esaurito = False
        ultimo = partenza
        Try
            If partenza Is Nothing Then Return Nothing
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim nodo As IUIAutomationElement = partenza
            Dim passi As Integer = 0
            While passi < 4000
                If orologio.ElapsedMilliseconds > budget Then
                    esaurito = True
                    Return Nothing
                End If
                nodo = If(avanti, NodoSuccessivo(nodo, True), NodoPrecedente(nodo, True))
                passi += 1
                If nodo Is Nothing Then
                    esaurito = (passi = 1 AndAlso Not ElementoPresente(partenza))
                    Return Nothing
                End If
                If Stesso(nodo, _radice) Then Return Nothing
                ultimo = nodo
                Dim candidato As IUIAutomationElement = FocalizzabileDi(nodo, Classifica(nodo, _descrittore.TipoControllo(nodo)))
                If candidato Is Nothing Then Continue While
                If Stesso(candidato, proprio) OrElse Stesso(candidato, rifiutato) Then Continue While
                Return candidato
            End While
            esaurito = True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FocalizzabileVivo: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function FocalizzabileDallIndice(avanti As Boolean, proprio As IUIAutomationElement, rifiutato As IUIAutomationElement) As IUIAutomationElement
        Try
            If Not _indiceCompleto OrElse _voci.Count = 0 Then Return Nothing
            Dim esatto As Boolean = False
            Dim inizio As Integer = IndiceVicino(_corrente, esatto)
            If inizio < 0 Then Return Nothing
            Dim controlliPadre As Integer = 0
            Return CercaFocalizzabile(inizio, esatto, avanti, proprio, rifiutato, controlliPadre)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FocalizzabileDallIndice: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function CercaFocalizzabile(inizio As Integer, esatto As Boolean, avanti As Boolean, proprio As IUIAutomationElement, rifiutato As IUIAutomationElement, ByRef controlliPadre As Integer) As IUIAutomationElement
        Try
            Dim totale As Integer = _voci.Count
            If totale = 0 Then Return Nothing
            Dim indice As Integer
            If avanti Then
                indice = inizio + 1
            Else
                indice = If(esatto, inizio - 1, inizio)
            End If
            While indice >= 0 AndAlso indice < totale
                Dim voce As VoceAlbero = _voci(indice)
                Dim daSaltare As Boolean = voce IsNot Nothing AndAlso voce.IdAiuto <> 0 AndAlso Not voce.Focalizzabile AndAlso voce.Tipo <> "titolo"
                If Not daSaltare AndAlso voce IsNot Nothing AndAlso voce.Elemento IsNot Nothing Then
                    Dim candidato As IUIAutomationElement = FocalizzabileDi(voce.Elemento, voce.Tipo)
                    If candidato Is Nothing AndAlso controlliPadre < 80 Then
                        controlliPadre += 1
                        candidato = EditabileContenitore(voce.Elemento)
                    End If
                    If candidato IsNot Nothing AndAlso Not Stesso(candidato, proprio) AndAlso Not Stesso(candidato, rifiutato) AndAlso ElementoPresente(candidato) Then Return candidato
                End If
                indice += If(avanti, 1, -1)
            End While
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CercaFocalizzabile: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function FocalizzabileDi(elemento As IUIAutomationElement, tipo As String) As IUIAutomationElement
        Try
            If elemento Is Nothing Then Return Nothing
            Select Case tipo
                Case "titolo"
                    Dim interno As IUIAutomationElement = TrovaInterattivo(elemento, 0)
                    If interno IsNot Nothing AndAlso _descrittore.FuocoDaTastiera(interno) Then Return interno
                    Return Nothing
                Case "regione", "documento", "riquadro", "finestra", "ignora", "separatore"
                    Return Nothing
            End Select
            If Not _descrittore.FuocoDaTastiera(elemento) Then Return Nothing
            If EDocumentoORadice(elemento) Then Return Nothing
            Return elemento
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FocalizzabileDi: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function EditabileContenitore(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing Then Return Nothing
            Dim padre As IUIAutomationElement = GenitoreControllo(elemento)
            If padre Is Nothing OrElse EDocumentoORadice(padre) Then Return Nothing
            If EditabileRicco(padre, False) Then Return padre
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.EditabileContenitore: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Public Function ERadice(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _radice Is Nothing Then Return False
            Return Stesso(elemento, _radice)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ERadice: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function CursoreDentro(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _corrente Is Nothing OrElse _radice Is Nothing Then Return False
            If Stesso(elemento, _radice) Then Return True
            Dim nodo As IUIAutomationElement = _corrente
            For salto As Integer = 0 To 25
                If nodo Is Nothing Then Return False
                If Stesso(nodo, elemento) Then Return True
                If Stesso(nodo, _radice) Then Return False
                nodo = GenitoreControllo(nodo)
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CursoreDentro: " & ex.Message)
        End Try
        Return False
    End Function

    Public Function CursoreSuElemento(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _corrente Is Nothing Then Return False
            Return Stesso(_corrente, elemento)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CursoreSuElemento: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function CursoreValido() As Boolean
        Try
            If _corrente Is Nothing OrElse _radice Is Nothing Then Return False
            Return ElementoPresente(_corrente)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CursoreValido: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub RicordaPosizione(ultimoFuoco As IUIAutomationElement, daTasti As Boolean)
        Try
            If _radice Is Nothing OrElse _corrente Is Nothing OrElse _chiaveRadice = "" Then Return
            If Not _posizioniRicordate.ContainsKey(ChiaveMemoria()) AndAlso _posizioniRicordate.Count >= MassimoPosizioniRicordate Then TogliPosizionePiuVecchia()
            Dim memoria As New PosizioneRicordata()
            memoria.Cursore = _corrente
            memoria.Carattere = _pos
            memoria.UltimoFuoco = ultimoFuoco
            memoria.DaTasti = daTasti
            memoria.Tick = Environment.TickCount
            _posizioniRicordate(ChiaveMemoria()) = memoria
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RicordaPosizione: " & ex.Message)
        End Try
    End Sub

    Private Sub TogliPosizionePiuVecchia()
        Try
            Dim chiaveVecchia As String = ""
            Dim etaMassima As Long = -1L
            For Each coppia As KeyValuePair(Of String, PosizioneRicordata) In _posizioniRicordate
                If coppia.Value Is Nothing Then
                    chiaveVecchia = coppia.Key
                    Exit For
                End If
                Dim eta As Long = CLng(Environment.TickCount) - CLng(coppia.Value.Tick)
                If eta < 0L Then eta += 4294967296L
                If eta > etaMassima Then
                    etaMassima = eta
                    chiaveVecchia = coppia.Key
                End If
            Next
            If chiaveVecchia <> "" Then _posizioniRicordate.Remove(chiaveVecchia)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.TogliPosizionePiuVecchia: " & ex.Message)
        End Try
    End Sub

    Public Function RipristinaPosizione(fuoco As IUIAutomationElement, ByRef daTasti As Boolean) As Boolean
        daTasti = False
        Try
            If _radice Is Nothing OrElse _chiaveRadice = "" Then Return False
            Dim memoria As PosizioneRicordata = Nothing
            If Not _posizioniRicordate.TryGetValue(ChiaveMemoria(), memoria) Then Return False
            _posizioniRicordate.Remove(ChiaveMemoria())
            If memoria Is Nothing OrElse memoria.Cursore Is Nothing Then Return False
            Dim stessoFuoco As Boolean = fuoco Is Nothing OrElse EDocumentoORadice(fuoco) OrElse Stesso(fuoco, memoria.UltimoFuoco)
            If Not stessoFuoco Then Return False
            If Not ElementoPresente(memoria.Cursore) OrElse Not DentroRadice(memoria.Cursore) Then Return False
            _corrente = memoria.Cursore
            _pos = memoria.Carattere
            _testoPronto = False
            _selezioneAttiva = False
            _tutto = False
            _sospesa = Nothing
            Dim uscite As String = ""
            Dim ingressi As String = ""
            FraseContenitori(_corrente, uscite, ingressi)
            AnnuncioRegione(_corrente)
            daTasti = memoria.DaTasti
            Return True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RipristinaPosizione: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function DentroDi(elemento As IUIAutomationElement, contenitore As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse contenitore Is Nothing Then Return False
            Dim corrente As IUIAutomationElement = GenitoreControllo(elemento)
            For salto As Integer = 0 To 60
                If corrente Is Nothing Then Return False
                If Stesso(corrente, contenitore) Then Return True
                corrente = GenitoreControllo(corrente)
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.DentroDi", ex)
        End Try
        Return False
    End Function

    Private Function DentroRadice(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _radice Is Nothing Then Return False
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 60
                If corrente Is Nothing Then Return False
                If Stesso(corrente, _radice) Then Return True
                corrente = GenitoreControllo(corrente)
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.DentroRadice: " & ex.Message)
        End Try
        Return False
    End Function

    Private Function ElementoPresente(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Return elemento.CurrentControlType <> 0
        Catch ex As TimeoutException
            RegistroErrori.Scrivi("NavigazioneWeb.ElementoPresente", ex)
            Return True
        Catch ex As System.Runtime.InteropServices.COMException
            If ex.HResult = &H80131505 Then Return True
            If Not ErroreElementoAssente(ex.HResult) Then log.ScriviLog("NavigazioneWeb.ElementoPresente: " & ex.Message)
            Return False
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ElementoPresente: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function ErroreElementoAssente(codice As Integer) As Boolean
        Select Case codice
            Case &H80040201, &H80010108, &H800706BA, &H800706BE, &H800401FD, &H80004005
                Return True
        End Select
        Return False
    End Function

    Private Function VocePresente(indice As Integer) As Boolean
        Try
            If indice < 0 OrElse indice >= _voci.Count Then Return False
            Dim voce As VoceAlbero = _voci(indice)
            If voce Is Nothing Then Return False
            Return ElementoPresente(voce.Elemento)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.VocePresente: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function PortaCursoreSu(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Return Arriva(elemento, False, True, False, "", True)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.PortaCursoreSu: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function FuocoDaSeguire(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse _radice Is Nothing Then Return True
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloDocumento, DescrittoreElementi.ControlloVoceMenu, DescrittoreElementi.ControlloMenu
                    Return True
            End Select
            If _descrittore.RuoloAria(elemento).StartsWith("menuitem", StringComparison.Ordinal) Then Return True
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 40
                If corrente Is Nothing Then Return True
                If Stesso(corrente, _radice) Then Return False
                If _descrittore.EDialogo(corrente) Then Return True
                corrente = GenitoreControllo(corrente)
            Next
            Return True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FuocoDaSeguire: " & ex.Message)
            Return True
        End Try
    End Function

    Public Function TestoVoceCorrente() As String
        Try
            Return TestoCorrente()
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.TestoVoceCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function BloccoCodice(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If _descrittore.RuoloAria(elemento) = "code" Then Return BloccoSuPiuRighe(elemento)
            Dim classe As String = _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaNomeClasse).ToLowerInvariant()
            If classe.Contains("language-") OrElse classe.Contains("code-block") OrElse classe.Contains("hljs") Then Return True
            Dim localizzato As String = _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaTipoControlloLocalizzato).ToLowerInvariant()
            If localizzato = "codice" OrElse localizzato = "code" Then Return BloccoSuPiuRighe(elemento)
            Return False
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.BloccoCodice: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function BloccoSuPiuRighe(elemento As IUIAutomationElement) As Boolean
        Try
            Dim padre As IUIAutomationElement = GenitoreControllo(elemento)
            If padre IsNot Nothing Then
                Dim classePadre As String = _descrittore.LeggiStringa(padre, DescrittoreElementi.ProprietaNomeClasse).ToLowerInvariant()
                If classePadre.Contains("code") OrElse classePadre.Contains("pre") Then Return True
            End If
            Dim testo As String = If(ContenutoVoce(elemento), "")
            Return testo.IndexOf(ChrW(10)) >= 0
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.BloccoSuPiuRighe: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TestoCodice(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim testo As String = TestoDalPattern(elemento, 20000)
            If String.IsNullOrWhiteSpace(testo) Then testo = ContenutoVoce(elemento)
            If testo Is Nothing Then Return ""
            Return testo.Replace(vbCrLf, vbLf).Trim(ChrW(10), ChrW(13), " "c)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.TestoCodice: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function AnnuncioCodice(elemento As IUIAutomationElement) As String
        Try
            Dim testo As String = TestoCodice(elemento)
            If testo = "" Then Return "codice"
            Dim righe As String() = testo.Split(ChrW(10))
            Dim prima As String = righe(0).Trim()
            If prima.Length > 200 Then prima = prima.Substring(0, 200)
            Dim quante As String = If(righe.Length = 1, "una riga", righe.Length.ToString() & " righe")
            Return If(prima = "", "codice, " & quante, "codice, " & quante & ", " & prima)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AnnuncioCodice: " & ex.Message)
            Return "codice"
        End Try
    End Function

    Private Function DocumentoGeckoSolaLettura(documento As IUIAutomationElement) As Boolean
        Try
            If documento Is Nothing Then Return False
            Dim valore As Object = documento.GetCurrentPropertyValue(30100)
            If TypeOf valore Is Integer Then Return (CInt(valore) And &H40) <> 0
            Return False
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.DocumentoGeckoSolaLettura: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LivelloTitoloInLink(elemento As IUIAutomationElement) As Integer
        Try
            If elemento Is Nothing OrElse _walkerAntenati Is Nothing Then Return 0
            Dim identita As String = Chiave(elemento)
            Dim noto As Integer = 0
            If identita <> "" AndAlso _titoloInLinkNoto.TryGetValue(identita, noto) Then Return noto
            Dim trovato As Integer = 0
            Dim visti As Integer = 0
            Dim figlio As IUIAutomationElement = _walkerAntenati.GetFirstChildElementBuildCache(elemento, _cache)
            While figlio IsNot Nothing AndAlso trovato = 0 AndAlso visti < 8
                visti += 1
                trovato = _descrittore.LivelloTitolo(figlio)
                If trovato = 0 Then
                    Dim nipote As IUIAutomationElement = _walkerAntenati.GetFirstChildElementBuildCache(figlio, _cache)
                    While nipote IsNot Nothing AndAlso trovato = 0 AndAlso visti < 8
                        visti += 1
                        trovato = _descrittore.LivelloTitolo(nipote)
                        If trovato = 0 Then nipote = _walkerAntenati.GetNextSiblingElementBuildCache(nipote, _cache)
                    End While
                End If
                If trovato = 0 Then figlio = _walkerAntenati.GetNextSiblingElementBuildCache(figlio, _cache)
            End While
            If identita <> "" Then
                If _titoloInLinkNoto.Count > 20000 Then _titoloInLinkNoto.Clear()
                _titoloInLinkNoto(identita) = trovato
            End If
            Return trovato
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.LivelloTitoloInLink: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function EsLinkTitolo(elemento As IUIAutomationElement, tipoControllo As Integer) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If tipoControllo <> DescrittoreElementi.ControlloCollegamento AndAlso _descrittore.RuoloAria(elemento) <> "link" Then Return False
            Return LivelloTitoloInLink(elemento) > 0
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.EsLinkTitolo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LivelloTitoloVoce(elemento As IUIAutomationElement) As Integer
        Try
            Dim livello As Integer = _descrittore.LivelloTitolo(elemento)
            If livello > 0 Then Return livello
            If EsLinkTitolo(elemento, _descrittore.TipoControllo(elemento)) Then Return LivelloTitoloInLink(elemento)
            Return 0
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.LivelloTitoloVoce: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function SpostaContenutoPasso(avanti As Boolean, seleziona As Boolean, salto As Boolean) As String
        If _sospesa IsNot Nothing Then
            Dim attesa As IUIAutomationElement = _sospesa
            _sospesa = Nothing
            If _sospesaAvanti = avanti Then Return Arriva(attesa, seleziona, avanti, False, "", salto)
            Return Arriva(_corrente, seleziona, avanti, False, "", salto)
        End If
        Dim giaLetto As IUIAutomationElement = ContenitoreGiaLetto(avanti)
        Dim dalBuffer As IUIAutomationElement = VoceDalBuffer(avanti, 1, giaLetto)
        If dalBuffer IsNot Nothing Then
            Dim fermataBuffer As String = FermataRegione(dalBuffer, avanti, seleziona)
            If fermataBuffer <> "" Then Return fermataBuffer
            Return Arriva(dalBuffer, seleziona, avanti, False, "", salto)
        End If
        Dim fine As Boolean = False
        Dim destinazione As IUIAutomationElement = ProssimaVoceTenace(_corrente, avanti, AttesaVoceMs, fine)
        Dim salti As Integer = 0
        While destinazione IsNot Nothing AndAlso salti < 30 AndAlso (SaltaConFrecce(destinazione, Classifica(destinazione, _descrittore.TipoControllo(destinazione))) OrElse Discende(destinazione, giaLetto))
            salti += 1
            destinazione = ProssimaVoceTenace(destinazione, avanti, AttesaVoceMs, fine)
        End While
        If destinazione Is Nothing Then
            If fine Then
                If _ambito IsNot Nothing AndAlso avanti Then
                    Dim seguente As String = ContinuaDopoMessaggio(seleziona, salto)
                    If seguente <> "" Then Return seguente
                    Return "fine"
                End If
                If _ambito IsNot Nothing Then Return EsciDalMessaggio(seleziona)
                Return If(avanti, "fine", "inizio")
            End If
            If _ambito IsNot Nothing AndAlso Not ElementoVivo(_corrente) Then Return EsciDalMessaggio(seleziona)
            Return "pagina in lettura"
        End If
        Dim fermata As String = FermataRegione(destinazione, avanti, seleziona)
        If fermata <> "" Then Return fermata
        Return Arriva(destinazione, seleziona, avanti, False, "", salto)
    End Function

    Private Function SaltaConFrecce(elemento As IUIAutomationElement, tipo As String) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Select Case tipo
                Case "regione", "elenco", "tabella", "citazione", "riquadro"
                    Return True
                Case "testo", "voce", "cella", "titolo"
                    Return Not Foglia(elemento, tipo)
            End Select
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.SaltaConFrecce: " & ex.Message)
        End Try
        Return False
    End Function

    Private Function LinkVisitato(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim valore As Object = elemento.GetCurrentPropertyValue(30100)
            If valore Is Nothing Then Return False
            Return (Convert.ToInt64(valore) And &H800000L) <> 0
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.LinkVisitato: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function TipoContenitore(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Select Case _descrittore.RuoloAria(elemento)
                Case "list"
                    Return "elenco"
                Case "table"
                    Return If(LetturaTabelle.TabellaDiImpaginazione(elemento, _descrittore), "", "tabella")
                Case "blockquote"
                    Return "citazione"
                Case "listbox", "grid", "treegrid", "tree", "menu", "menubar", "tablist"
                    Return ""
            End Select
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElenco
                    Return If(_web, "elenco", "")
                Case DescrittoreElementi.ControlloTabella
                    If Not _web OrElse LetturaTabelle.TabellaDiImpaginazione(elemento, _descrittore) Then Return ""
                    Return "tabella"
            End Select
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.TipoContenitore: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Sub FraseContenitori(elemento As IUIAutomationElement, ByRef uscite As String, ByRef ingressi As String)
        uscite = ""
        ingressi = ""
        Try
            If elemento Is Nothing OrElse _radice Is Nothing Then Return
            Dim catena As New List(Of String())()
            Dim elementi As New List(Of IUIAutomationElement)()
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 7
                If corrente Is Nothing OrElse Stesso(corrente, _radice) Then Exit For
                Dim tipo As String = TipoContenitore(corrente)
                If tipo <> "" Then
                    Dim chiaveContenitore As String = Chiave(corrente)
                    If chiaveContenitore <> "" Then
                        catena.Add(New String() {chiaveContenitore, tipo, If(salto = 0, "1", "0")})
                        elementi.Add(corrente)
                    End If
                End If
                corrente = GenitoreControllo(corrente)
            Next
            Dim nuove As New HashSet(Of String)(StringComparer.Ordinal)
            For Each voce As String() In catena
                nuove.Add(voce(0))
            Next
            Dim vecchie As New HashSet(Of String)(StringComparer.Ordinal)
            For Each voce As String() In _contenitoriCorrenti
                vecchie.Add(voce(0))
            Next
            Dim fuori As New List(Of String)()
            For Each voce As String() In _contenitoriCorrenti
                If Not nuove.Contains(voce(0)) Then fuori.Add(FraseUscita(voce(1)))
            Next
            Dim dentro As New List(Of String)()
            For indice As Integer = catena.Count - 1 To 0 Step -1
                Dim voce As String() = catena(indice)
                If vecchie.Contains(voce(0)) OrElse voce(2) = "1" Then Continue For
                Dim frase As String = FraseIngresso(voce(1), voce(0), elementi(indice))
                If frase <> "" Then dentro.Add(frase)
            Next
            _contenitoriCorrenti = catena
            uscite = String.Join(", ", fuori)
            ingressi = String.Join(", ", dentro)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FraseContenitori: " & ex.Message)
        End Try
    End Sub

    Private Shared Function FraseUscita(tipo As String) As String
        Select Case tipo
            Case "elenco"
                Return "fuori dall'elenco"
            Case "tabella"
                Return "fuori dalla tabella"
            Case "citazione"
                Return "fuori dalla citazione"
        End Select
        Return ""
    End Function

    Private Function FraseIngresso(tipo As String, chiaveContenitore As String, contenitore As IUIAutomationElement) As String
        Try
            Select Case tipo
                Case "elenco"
                    Dim numero As Integer = VociElenco(chiaveContenitore, contenitore)
                    If numero = 1 Then Return "elenco con 1 voce"
                    If numero > 1 Then Return "elenco con " & numero.ToString() & " voci"
                    Return "elenco"
                Case "tabella"
                    Dim sommario As String = LetturaTabelle.Sommario(contenitore, _descrittore)
                    Return If(sommario = "", "tabella", "tabella, " & sommario)
                Case "citazione"
                    Return "citazione"
            End Select
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FraseIngresso: " & ex.Message)
        End Try
        Return ""
    End Function

    Private Function VociElenco(chiaveContenitore As String, contenitore As IUIAutomationElement) As Integer
        Try
            Dim noto As Integer = 0
            If _vociElencoNote.TryGetValue(chiaveContenitore, noto) Then Return noto
            Dim numero As Integer = 0
            Dim figli As IUIAutomationElementArray = contenitore.FindAllBuildCache(TreeScope.TreeScope_Children, _automazione.CreateTrueCondition(), _cache)
            If figli IsNot Nothing Then
                For indice As Integer = 0 To figli.Length - 1
                    Dim figlio As IUIAutomationElement = figli.GetElement(indice)
                    If _descrittore.RuoloAria(figlio) = "listitem" OrElse _descrittore.TipoControllo(figlio) = DescrittoreElementi.ControlloElementoElenco Then numero += 1
                Next
            End If
            If _vociElencoNote.Count > 2000 Then _vociElencoNote.Clear()
            _vociElencoNote(chiaveContenitore) = numero
            Return numero
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.VociElenco: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function FineContenitore(indietro As Boolean) As String
        Try
            If _corrente Is Nothing OrElse _radice Is Nothing Then Return "nessun contenuto"
            Dim contenitore As IUIAutomationElement = Nothing
            Dim corrente As IUIAutomationElement = _corrente
            For salto As Integer = 0 To 10
                If corrente Is Nothing OrElse Stesso(corrente, _radice) Then Exit For
                If TipoContenitore(corrente) <> "" OrElse Classifica(corrente, _descrittore.TipoControllo(corrente)) = "regione" Then
                    contenitore = corrente
                    Exit For
                End If
                corrente = GenitoreControllo(corrente)
            Next
            If contenitore Is Nothing Then Return "non sei in un contenitore"
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            While Not _indiceCompleto AndAlso orologio.ElapsedMilliseconds < 1200
                Indicizza(120)
            End While
            Dim esatto As Boolean = False
            Dim inizio As Integer = IndiceVicino(_corrente, esatto)
            If inizio < 0 OrElse _voci.Count = 0 Then Return "pagina ancora in lettura"
            If indietro Then
                Dim indice As Integer = inizio
                While indice - 1 >= 0 AndAlso DentroContenitore(_voci(indice - 1).Elemento, contenitore)
                    indice -= 1
                End While
                Return Arriva(_voci(indice).Elemento, False, False, False, "", True)
            End If
            Dim prossimo As Integer = inizio + 1
            While prossimo < _voci.Count AndAlso DentroContenitore(_voci(prossimo).Elemento, contenitore)
                prossimo += 1
            End While
            If prossimo >= _voci.Count Then Return "fine"
            Return Arriva(_voci(prossimo).Elemento, False, True, False, "", True)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FineContenitore: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function DentroContenitore(elemento As IUIAutomationElement, contenitore As IUIAutomationElement) As Boolean
        If elemento Is Nothing OrElse contenitore Is Nothing Then Return False
        If Stesso(elemento, contenitore) Then Return True
        Return Discende(elemento, contenitore)
    End Function

    Private Function ContenitoreGiaLetto(avanti As Boolean) As IUIAutomationElement
        Try
            If Not avanti OrElse _corrente Is Nothing Then Return Nothing
            Dim tipo As String = Classifica(_corrente, _descrittore.TipoControllo(_corrente))
            Select Case tipo
                Case "regione", "elenco", "tabella", "citazione", "riquadro"
                    Return Nothing
            End Select
            If SaltaConFrecce(_corrente, tipo) Then Return _corrente
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ContenitoreGiaLetto: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function Discende(elemento As IUIAutomationElement, antenato As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse antenato Is Nothing Then Return False
            Dim corrente As IUIAutomationElement = GenitoreControllo(elemento)
            For salto As Integer = 0 To 8
                If corrente Is Nothing Then Return False
                If Stesso(corrente, antenato) Then Return True
                If _radice IsNot Nothing AndAlso Stesso(corrente, _radice) Then Return False
                corrente = GenitoreControllo(corrente)
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.Discende: " & ex.Message)
        End Try
        Return False
    End Function

    Private Function VoceDalBuffer(avanti As Boolean, passi As Integer, Optional giaLetto As IUIAutomationElement = Nothing) As IUIAutomationElement
        Try
            If _ambito IsNot Nothing OrElse Not _indiceCompleto OrElse _voci Is Nothing OrElse _voci.Count = 0 OrElse _corrente Is Nothing Then Return Nothing
            If _aiutoAttivo AndAlso _aiutoSoloRicerca Then Return Nothing
            Dim identita As String = Chiave(_corrente)
            Dim attuale As Integer = -1
            If identita = "" OrElse Not _perIdentita.TryGetValue(identita, attuale) Then Return Nothing
            Dim prossimo As Integer = attuale + If(avanti, passi, -passi)
            If prossimo < 0 OrElse prossimo >= _voci.Count Then
                If passi <= 1 Then Return Nothing
                prossimo = If(avanti, _voci.Count - 1, 0)
                If prossimo = attuale Then Return Nothing
            End If
            Dim guardia As Integer = 0
            While prossimo >= 0 AndAlso prossimo < _voci.Count AndAlso guardia < 200 AndAlso SaltaVoceIndice(_voci(prossimo), giaLetto)
                prossimo += If(avanti, 1, -1)
                guardia += 1
            End While
            If prossimo < 0 OrElse prossimo >= _voci.Count OrElse prossimo = attuale Then Return Nothing
            If Not VocePresente(prossimo) Then
                _bufferDaRicostruire = True
                Return Nothing
            End If
            Return _voci(prossimo).Elemento
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.VoceDalBuffer: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Function SpostaPagina(avanti As Boolean, seleziona As Boolean) As String
        Dim dalBuffer As IUIAutomationElement = VoceDalBuffer(avanti, 8)
        If dalBuffer IsNot Nothing Then Return Arriva(dalBuffer, seleziona, avanti, True, "", True)
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim attuale As IUIAutomationElement = _corrente
        Dim mosso As Boolean = False
        Dim allaFine As Boolean = False
        For passo As Integer = 1 To 8
            Dim restante As Integer = 110 - CInt(orologio.ElapsedMilliseconds)
            If restante < 8 AndAlso passo > 1 Then Exit For
            Dim fine As Boolean = False
            Dim prossimo As IUIAutomationElement
            If passo = 1 Then
                prossimo = ProssimaVoceTenace(attuale, avanti, AttesaVoceMs, fine)
            Else
                prossimo = ProssimaVoce(attuale, avanti, restante, fine)
            End If
            If fine Then allaFine = True
            If prossimo Is Nothing Then Exit For
            attuale = prossimo
            mosso = True
        Next
        If Not mosso OrElse attuale Is Nothing OrElse Stesso(attuale, _corrente) Then
            If Not allaFine Then Return "pagina in lettura"
            Return If(avanti, "fine", "inizio")
        End If
        Return Arriva(attuale, seleziona, avanti, True, "", True)
    End Function

    Private Function SpostaCarattere(delta As Integer, seleziona As Boolean) As String
        If _sospesa IsNot Nothing Then Return SpostaContenuto(delta > 0, seleziona, False)
        Dim testo As String = TestoCorrente()
        If testo.Length = 0 Then Return SpostaContenuto(delta > 0, seleziona, False)
        If _pos < 0 Then
            _pos = If(delta > 0, 0, testo.Length - 1)
            Return ApplicaCarattere(seleziona, _verso + delta)
        End If
        Dim nuovo As Integer = _pos + delta
        If nuovo >= 0 AndAlso nuovo < testo.Length Then
            _pos = nuovo
            Return ApplicaCarattere(seleziona, _verso + delta)
        End If
        Dim fine As Boolean = False
        Dim destinazione As IUIAutomationElement = ProssimaVoceTenace(_corrente, delta > 0, AttesaVoceMs, fine)
        If destinazione Is Nothing Then
            If fine Then
                If _ambito IsNot Nothing AndAlso delta > 0 Then
                    Dim seguente As String = ContinuaDopoMessaggio(seleziona, False)
                    If seguente <> "" Then Return seguente
                    Return "fine"
                End If
                If _ambito IsNot Nothing Then Return EsciDalMessaggio(seleziona)
                Return If(delta > 0, "fine", "inizio")
            End If
            If _ambito IsNot Nothing AndAlso Not ElementoVivo(_corrente) Then Return EsciDalMessaggio(seleziona)
            Return "pagina in lettura"
        End If
        Dim fermataCar As String = FermataRegione(destinazione, delta > 0, seleziona)
        If fermataCar <> "" Then Return fermataCar
        Dim nuovoVerso As Integer = _verso + delta
        Dim cresce As Boolean = True
        If seleziona Then
            cresce = Estende(nuovoVerso)
        Else
            _selezioneAttiva = False
            _tutto = False
        End If
        _verso = nuovoVerso
        _corrente = destinazione
        _testoPronto = False
        Dim arrivato As String = TestoCorrente()
        If arrivato.Length = 0 Then Return Annuncio(destinazione)
        _pos = If(delta > 0, 0, arrivato.Length - 1)
        Dim nome As String = NomeCarattereProtetto(arrivato, _pos)
        If Not seleziona Then Return nome
        Return nome & If(cresce, " selezionato", " deselezionato")
    End Function

    Private Function SpostaParola(avanti As Boolean, seleziona As Boolean) As String
        Try
            Dim delta As Integer = If(avanti, 1, -1)
            If _sospesa IsNot Nothing Then Return SpostaContenuto(avanti, seleziona, False)
            Dim testo As String = TestoCorrente()
            If testo.Length = 0 Then Return SpostaContenuto(avanti, seleziona, False)
            Dim posizione As Integer = _pos
            If avanti Then
                If posizione < 0 Then
                    posizione = 0
                Else
                    While posizione < testo.Length AndAlso Not Char.IsWhiteSpace(testo(posizione))
                        posizione += 1
                    End While
                    While posizione < testo.Length AndAlso Char.IsWhiteSpace(testo(posizione))
                        posizione += 1
                    End While
                End If
            Else
                If posizione < 0 Then posizione = testo.Length
                posizione -= 1
                While posizione >= 0 AndAlso Char.IsWhiteSpace(testo(posizione))
                    posizione -= 1
                End While
                If posizione >= 0 Then
                    While posizione > 0 AndAlso Not Char.IsWhiteSpace(testo(posizione - 1))
                        posizione -= 1
                    End While
                End If
            End If
            If posizione >= 0 AndAlso posizione < testo.Length Then
                _pos = posizione
                Return ApplicaParola(seleziona, _verso + delta, testo, posizione)
            End If
            Dim precedente As IUIAutomationElement = _corrente
            _pos = If(avanti, testo.Length - 1, 0)
            Dim esito As String = SpostaCarattere(delta, seleziona)
            If _corrente Is Nothing OrElse Stesso(_corrente, precedente) OrElse _pos < 0 Then Return esito
            Dim nuovoTesto As String = TestoCorrente()
            If nuovoTesto.Length = 0 Then Return esito
            If Not avanti Then
                Dim inizio As Integer = Math.Min(_pos, nuovoTesto.Length - 1)
                While inizio > 0 AndAlso Not Char.IsWhiteSpace(nuovoTesto(inizio - 1))
                    inizio -= 1
                End While
                _pos = inizio
            End If
            Dim parola As String = ParolaDa(nuovoTesto, _pos)
            If parola = "" Then Return esito
            If Not seleziona Then Return parola
            Return parola & If(esito.EndsWith("deselezionato", StringComparison.Ordinal), " deselezionato", " selezionato")
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.SpostaParola", ex)
            Return ""
        End Try
    End Function

    Private Function ApplicaParola(seleziona As Boolean, nuovoVerso As Integer, testo As String, posizione As Integer) As String
        Dim cresce As Boolean = True
        If seleziona Then
            cresce = Estende(nuovoVerso)
        Else
            _selezioneAttiva = False
            _tutto = False
        End If
        _verso = nuovoVerso
        Dim parola As String = If(_descrittore.Protetto(_corrente), "protetto", ParolaDa(testo, posizione))
        If parola = "" Then parola = "vuoto"
        If Not seleziona Then Return parola
        Return parola & If(cresce, " selezionato", " deselezionato")
    End Function

    Private Function ApplicaCarattere(seleziona As Boolean, nuovoVerso As Integer) As String
        Dim cresce As Boolean = True
        If seleziona Then
            cresce = Estende(nuovoVerso)
        Else
            _selezioneAttiva = False
            _tutto = False
        End If
        _verso = nuovoVerso
        Dim nome As String = NomeCarattereProtetto(TestoCorrente(), _pos)
        If Not seleziona Then Return nome
        Return nome & If(cresce, " selezionato", " deselezionato")
    End Function

    Private Function NomeCarattereProtetto(testo As String, posizione As Integer) As String
        If testo Is Nothing OrElse posizione < 0 OrElse posizione >= testo.Length Then Return "vuoto"
        If _descrittore.Protetto(_corrente) Then Return "stella"
        Return _descrittore.NomeCarattere(testo.Substring(posizione, 1))
    End Function

    Private Function VaiEstremo(inizio As Boolean, seleziona As Boolean) As String
        Dim destinazione As IUIAutomationElement = If(inizio, PrimaVoce(RadiceCursore(), AttesaVoceMs), UltimaVoce(AttesaVoceMs))
        If destinazione Is Nothing Then Return If(inizio, "inizio", "fine")
        If Stesso(destinazione, _corrente) AndAlso _pos < 0 Then Return Annuncio(destinazione) & If(inizio, ", inizio", ", fine")
        Return Arriva(destinazione, seleziona, Not inizio, True, "", True)
    End Function

    Private Function VaiCarattereEstremo(inizio As Boolean, seleziona As Boolean) As String
        Dim testo As String = TestoCorrente()
        If testo.Length = 0 Then Return Annuncio(_corrente)
        _pos = If(inizio, 0, testo.Length - 1)
        Return ApplicaCarattere(seleziona, _verso + If(inizio, -1, 1))
    End Function

    Private Sub RinfrescaSeVecchio()
        Try
            If Not _chromium OrElse Not _indiceCompleto OrElse _vociNuove IsNot Nothing Then Return
            Dim trascorso As Integer = Environment.TickCount - _indiceCompletoTick
            If trascorso >= 0 AndAlso trascorso < 30000 AndAlso Not _bufferDaRicostruire Then Return
            RicominciaIndice()
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RinfrescaSeVecchio: " & ex.Message)
        End Try
    End Sub

    Private Function VaiAlTipo(tipo As String, livello As Integer, indietro As Boolean) As String
        RinfrescaSeVecchio()
        If tipo = "ortografia" Then Return CercaErroreOrtografia(indietro)
        If tipo = "testo" Then
            Dim dopoLink As String = TestoDopoCollegamenti(indietro)
            If dopoLink <> "" Then Return dopoLink
        End If
        Dim esito As String = CercaTipo(tipo, livello, indietro)
        If esito <> "pagina ancora in lettura" Then Return esito
        If _indiceCompleto Then Return FraseAssente(tipo, livello)
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        While Not _indiceCompleto AndAlso orologio.ElapsedMilliseconds < 800
            Indicizza(80)
            esito = CercaTipo(tipo, livello, indietro)
            If esito <> "pagina ancora in lettura" Then Return esito
        End While
        If _indiceCompleto Then Return CercaTipo(tipo, livello, indietro)
        Return esito
    End Function

    Private Function TestoDopoCollegamenti(indietro As Boolean) As String
        Try
            If Not _indiceCompleto OrElse IndiceInAggiornamento() OrElse _voci Is Nothing OrElse _voci.Count = 0 Then Return ""
            Dim esatto As Boolean = False
            Dim indice As Integer = IndiceVicino(_corrente, esatto)
            If indice < 0 Then Return ""
            Dim passo As Integer = If(indietro, -1, 1)
            Dim totale As Integer = _voci.Count
            Dim posizione As Integer = indice
            If Not esatto AndAlso indietro Then posizione += 1
            Dim inBlocco As Boolean = esatto AndAlso ParteDiBloccoLink(_voci(indice).Tipo)
            Dim guardia As Integer = 0
            While guardia < totale
                guardia += 1
                posizione += passo
                If posizione < 0 OrElse posizione >= totale Then Return FraseAssente("testo", 0)
                Dim voce As VoceAlbero = _voci(posizione)
                If voce Is Nothing Then Continue While
                If ParteDiBloccoLink(voce.Tipo) Then
                    If voce.Tipo = "collegamento" Then inBlocco = True
                    Continue While
                End If
                If Not inBlocco Then Continue While
                If voce.Tipo <> "testo" AndAlso voce.Tipo <> "titolo" AndAlso voce.Tipo <> "articolo" AndAlso voce.Tipo <> "citazione" Then Continue While
                If Not VocePresente(posizione) Then
                    _bufferDaRicostruire = True
                    Return ""
                End If
                Return Arriva(voce.Elemento, False, Not indietro, True, "", True, False)
            End While
            Return FraseAssente("testo", 0)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TestoDopoCollegamenti", ex)
            Return ""
        End Try
    End Function

    Private Shared Function ParteDiBloccoLink(tipo As String) As Boolean
        Select Case tipo
            Case "collegamento", "elenco", "voce", "separatore", "grafica", "pulsante", "regione", "riquadro"
                Return True
        End Select
        Return False
    End Function

    Private Function LinkStessaPagina(elemento As IUIAutomationElement, testo As String) As String
        Try
            Dim indirizzo As String = _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaValore).Trim()
            If indirizzo = "" OrElse indirizzo.IndexOf("#"c) < 0 Then Return testo
            Dim stessa As Boolean = indirizzo.StartsWith("#", StringComparison.Ordinal)
            If Not stessa AndAlso _radice IsNot Nothing Then
                Dim pagina As String = _descrittore.LeggiStringa(_radice, DescrittoreElementi.ProprietaValore).Trim()
                Dim cancellettoPagina As Integer = pagina.IndexOf("#"c)
                If cancellettoPagina >= 0 Then pagina = pagina.Substring(0, cancellettoPagina)
                Dim base As String = indirizzo.Substring(0, indirizzo.IndexOf("#"c))
                stessa = pagina <> "" AndAlso String.Equals(base, pagina, StringComparison.OrdinalIgnoreCase)
            End If
            If Not stessa OrElse indirizzo.Length <= 1 Then Return testo
            Dim posizione As Integer = testo.IndexOf("link", StringComparison.Ordinal)
            If posizione < 0 Then Return testo & ", link alla stessa pagina"
            Return testo.Substring(0, posizione) & "link alla stessa pagina" & testo.Substring(posizione + 4)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.LinkStessaPagina", ex)
            Return testo
        End Try
    End Function

    Private Function CercaErroreOrtografia(indietro As Boolean) As String
        Try
            If _voci Is Nothing OrElse _voci.Count = 0 Then Return FraseAssente("ortografia", 0)
            Dim esatto As Boolean = False
            Dim inizio As Integer = IndiceVicino(_corrente, esatto)
            If inizio < 0 Then Return FraseAssente("ortografia", 0)
            If Not esatto AndAlso indietro Then inizio += 1
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim totale As Integer = _voci.Count
            For passo As Integer = 1 To totale
                If orologio.ElapsedMilliseconds > 2000L Then Exit For
                Dim indice As Integer = inizio + If(indietro, -passo, passo)
                If indice < 0 OrElse indice >= totale Then Exit For
                Dim voce As VoceAlbero = _voci(indice)
                If voce Is Nothing OrElse Not TipoConTesto(voce.Tipo) Then Continue For
                Dim errore As String = ""
                If ErroreNelTesto(voce.Elemento, errore) Then
                    Dim prefisso As String = "errore di ortografia, "
                    If errore <> "" Then prefisso &= errore & ", "
                    Return Arriva(voce.Elemento, False, Not indietro, True, prefisso, True, False)
                End If
            Next
            Return FraseAssente("ortografia", 0)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CercaErroreOrtografia: " & ex.Message)
            Return FraseAssente("ortografia", 0)
        End Try
    End Function

    Private Shared Function TipoConTesto(tipo As String) As Boolean
        Select Case tipo
            Case "testo", "modifica", "voce", "cella", "titolo", "articolo", "citazione", "collegamento"
                Return True
        End Select
        Return False
    End Function

    Private Function IntervalloDi(elemento As IUIAutomationElement) As IUIAutomationTextRange
        Try
            If elemento Is Nothing Then Return Nothing
            Dim proprio As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(10014), IUIAutomationTextPattern)
            If proprio IsNot Nothing Then Return proprio.DocumentRange
            If _radice Is Nothing Then Return Nothing
            Dim documento As IUIAutomationTextPattern = TryCast(_radice.GetCurrentPattern(10014), IUIAutomationTextPattern)
            If documento Is Nothing Then Return Nothing
            Return documento.RangeFromChild(elemento)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.IntervalloDi", ex)
            Return Nothing
        End Try
    End Function

    Private Function ErroreNelTesto(elemento As IUIAutomationElement, ByRef errore As String) As Boolean
        errore = ""
        Try
            Dim intervallo As IUIAutomationTextRange = IntervalloDi(elemento)
            If intervallo Is Nothing Then Return False
            Dim parte As IUIAutomationTextRange = intervallo.Clone()
            parte.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, parte, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
            Dim fine As IUIAutomationTextRange = parte.Clone()
            For giro As Integer = 1 To 200
                Dim ultima As Boolean = fine.Move(TextUnit.TextUnit_Format, 1) <= 0
                If ultima Then
                    parte.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
                Else
                    parte.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, fine, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
                    If parte.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) >= 0 Then
                        parte.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, intervallo, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
                        ultima = True
                    End If
                End If
                Dim trovato As IUIAutomationTextRange = ParteConErrore(parte)
                If trovato IsNot Nothing Then
                    errore = TestoBreve(trovato.GetText(80))
                    Return True
                End If
                If ultima Then Return False
                parte.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, parte, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
            Next
            Return False
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ErroreNelTesto", ex)
            Return False
        End Try
    End Function

    Private Function ParteConErrore(parte As IUIAutomationTextRange) As IUIAutomationTextRange
        Try
            Dim esito As Integer = ValoreErrore(parte.GetAttributeValue(40031))
            If esito = 1 Then Return parte
            If esito = 0 Then Return Nothing
            Dim parola As IUIAutomationTextRange = parte.Clone()
            parola.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, parola, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
            For giro As Integer = 1 To 100
                Dim prossima As IUIAutomationTextRange = parola.Clone()
                Dim ultima As Boolean = prossima.Move(TextUnit.TextUnit_Word, 1) <= 0
                If ultima OrElse prossima.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, parte, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) >= 0 Then
                    parola.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, parte, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
                    ultima = True
                Else
                    parola.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, prossima, TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)
                End If
                If ValoreErrore(parola.GetAttributeValue(40031)) = 1 Then Return parola
                If ultima Then Return Nothing
                parola.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, parola, TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)
            Next
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ParteConErrore", ex)
            Return Nothing
        End Try
    End Function

    Private Shared Function ValoreErrore(valore As Object) As Integer
        Try
            If valore Is Nothing Then Return 0
            If TypeOf valore Is Integer Then Return If(CodiceErrore(CInt(valore)), 1, 0)
            Dim lista As Array = TryCast(valore, Array)
            If lista IsNot Nothing Then
                For Each voce As Object In lista
                    If TypeOf voce Is Integer AndAlso CodiceErrore(CInt(voce)) Then Return 1
                Next
                Return 0
            End If
            Return 2
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ValoreErrore", ex)
            Return 0
        End Try
    End Function

    Private Shared Function CodiceErrore(codice As Integer) As Boolean
        Return codice = 60001 OrElse codice = 60002
    End Function

    Private Shared Function TestoBreve(testo As String) As String
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return ""
            Dim pulito As String = testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If pulito.Length > 60 Then pulito = pulito.Substring(0, 60).Trim()
            Return pulito
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TestoBreve", ex)
            Return ""
        End Try
    End Function

    Private Shared Function SembraParagrafo(testo As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return False
            Dim pulito As String = testo.Trim()
            If pulito.Length < 20 Then Return False
            Return System.Text.RegularExpressions.Regex.IsMatch(pulito, "[\.!\?" & ChrW(8230) & "][""'" & ChrW(8221) & ChrW(8217) & ChrW(187) & "\)]*(\s|$)")
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.SembraParagrafo", ex)
            Return False
        End Try
    End Function

    Private Function CercaTipo(tipo As String, livello As Integer, indietro As Boolean) As String
        Dim esatto As Boolean = False
        Dim inizio As Integer = IndiceVicino(_corrente, esatto)
        If Not esatto AndAlso inizio >= 0 AndAlso indietro Then inizio += 1
        If _indiceCompleto AndAlso inizio >= 0 AndAlso _voci.Count > 0 Then
            ' L'indice si usa sempre, anche se si sta aggiornando o ha qualche secondo: ogni voce trovata si controlla (VocePresente) e,
            ' se è sparita, l'indice si rifà in sottofondo. Il vecchio giro nell'albero a ogni tasto rendeva tutto lento.
            Dim partenza As Integer = inizio
            Dim assenti As Integer = 0
            For tentativo As Integer = 1 To 12
                Dim trovato As Integer = CercaElenco(partenza, tipo, livello, indietro, Ciclica AndAlso Not _riquadro)
                If trovato < 0 Then Exit For
                If VocePresente(trovato) Then
                    Dim prefisso As String = ""
                    If indietro AndAlso trovato >= inizio Then prefisso = "dalla fine, "
                    If Not indietro AndAlso trovato <= inizio Then prefisso = "dall'inizio, "
                    Return Arriva(_voci(trovato).Elemento, False, Not indietro, True, prefisso, True, False)
                End If
                assenti += 1
                partenza = trovato
            Next
            If assenti > 0 Then _bufferDaRicostruire = True
            If assenti > 0 OrElse IndiceInAggiornamento() OrElse _indiceTroncato Then
                Dim vivo As String = CercaVivo(tipo, livello, indietro, If(assenti > 0 OrElse _indiceTroncato, 1500, 300))
                If vivo <> "pagina ancora in lettura" Then Return vivo
                Return "pagina ancora in lettura"
            End If
            Return FraseAssente(tipo, livello)
        End If
        If Not _indiceCompleto AndAlso inizio >= 0 Then
            Dim avanti As Integer = CercaElenco(inizio, tipo, livello, indietro, False)
            If avanti >= 0 AndAlso VocePresente(avanti) Then Return Arriva(_voci(avanti).Elemento, False, Not indietro, False, "", True, False)
        End If
        Return CercaVivo(tipo, livello, indietro)
    End Function

    Private Function CercaElenco(inizio As Integer, tipo As String, livello As Integer, indietro As Boolean, avvolgi As Boolean) As Integer
        Dim totale As Integer = _voci.Count
        If totale = 0 Then Return -1
        Dim limite As Integer = If(avvolgi, totale, If(indietro, inizio + 1, totale - inizio))
        For passo As Integer = 1 To limite
            Dim indice As Integer = inizio + If(indietro, -passo, passo)
            If avvolgi Then
                Do While indice < 0
                    indice += totale
                Loop
                indice = indice Mod totale
                If indice = inizio Then Exit For
            ElseIf indice < 0 OrElse indice >= totale Then
                Exit For
            End If
            If Corrisponde(_voci(indice), tipo, livello) Then Return indice
        Next
        Return -1
    End Function

    Private Function CercaVivo(tipo As String, livello As Integer, indietro As Boolean, Optional budget As Integer = 130) As String
        Dim partenza As IUIAutomationElement = _corrente
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim nodo As IUIAutomationElement = partenza
        Dim avvolto As Boolean = False
        While orologio.ElapsedMilliseconds < budget
            Dim restante As Integer = budget - CInt(orologio.ElapsedMilliseconds)
            If restante < 8 Then Exit While
            Dim fine As Boolean = False
            Dim prossimo As IUIAutomationElement = ProssimaVoce(nodo, Not indietro, restante, fine)
            If prossimo Is Nothing Then
                If Not fine Then Return "pagina ancora in lettura"
                If Not Ciclica OrElse _riquadro Then Return FraseNessuno(tipo, livello, partenza)
                If avvolto Then Return FraseNessuno(tipo, livello, partenza)
                avvolto = True
                nodo = If(indietro, UltimaVoce(40), PrimaVoce(RadiceCursore(), 40))
                If nodo Is Nothing Then Return FraseNessuno(tipo, livello, partenza)
                If Stesso(nodo, partenza) Then Return FraseNessuno(tipo, livello, partenza)
                If CorrispondeNodo(nodo, tipo, livello) Then
                    Return Arriva(nodo, False, Not indietro, False, If(indietro, "dalla fine, ", "dall'inizio, "), True, False)
                End If
                Continue While
            End If
            nodo = prossimo
            If Stesso(nodo, partenza) Then Return FraseNessuno(tipo, livello, partenza)
            If CorrispondeNodo(nodo, tipo, livello) Then
                Dim prefisso As String = If(avvolto, If(indietro, "dalla fine, ", "dall'inizio, "), "")
                Return Arriva(nodo, False, Not indietro, False, prefisso, True, False)
            End If
        End While
        Return "pagina ancora in lettura"
    End Function

    Private Function IndiceVecchio(millisecondi As Integer) As Boolean
        Try
            Dim trascorso As Long = CLng(Environment.TickCount) - CLng(_indiceCompletoTick)
            If trascorso < 0L Then trascorso += 4294967296L
            Return trascorso >= CLng(millisecondi)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.IndiceVecchio: " & ex.Message)
            Return True
        End Try
    End Function

    Private Shared Function EsitoTrovato(esito As String) As Boolean
        If String.IsNullOrEmpty(esito) Then Return False
        If esito = "pagina ancora in lettura" OrElse esito = "non ci sono altri" Then Return False
        If esito.StartsWith("nessun", StringComparison.CurrentCultureIgnoreCase) Then Return False
        Return True
    End Function

    Private Sub ControllaVoceNellIndice()
        Try
            Dim corrente As IUIAutomationElement = _corrente
            If corrente Is Nothing OrElse _perIdentita Is Nothing Then Return
            Dim identita As String = Chiave(corrente)
            If identita = "" Then Return
            If Not _perIdentita.ContainsKey(identita) Then _bufferDaRicostruire = True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ControllaVoceNellIndice: " & ex.Message)
        End Try
    End Sub

    Private Function FraseNessuno(tipo As String, livello As Integer, partenza As IUIAutomationElement) As String
        If CorrispondeNodo(partenza, tipo, livello) Then Return "non ci sono altri"
        Return FraseAssente(tipo, livello)
    End Function

    Private Function MuoviCella(codice As Integer) As String
        Try
            Dim deltaRiga As Integer = 0
            Dim deltaColonna As Integer = 0
            Select Case codice
                Case VK_UP
                    deltaRiga = -1
                Case VK_DOWN
                    deltaRiga = 1
                Case VK_LEFT
                    deltaColonna = -1
                Case VK_RIGHT
                    deltaColonna = 1
                Case Else
                    Return ""
            End Select
            Dim errore As String = ""
            Dim destinazione As IUIAutomationElement = LetturaTabelle.CellaVicina(_corrente, deltaRiga, deltaColonna, _descrittore, _automazione, _cache, errore)
            If destinazione Is Nothing Then Return If(errore = "", "non sei in una tabella", errore)
            Return Arriva(destinazione, False, deltaRiga > 0 OrElse deltaColonna > 0, False, "", True)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.MuoviCella: " & ex.Message)
            Return "tabella non disponibile"
        End Try
    End Function

    Public Function LeggiRiga() As String
        Try
            Return LetturaTabelle.FraseRiga(_corrente, _descrittore, _automazione, _cache)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.LeggiRiga: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function LeggiColonna() As String
        Try
            Return LetturaTabelle.FraseColonna(_corrente, _descrittore, _automazione, _cache)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.LeggiColonna: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function CorpoDentro(elemento As IUIAutomationElement) As Boolean
        Return ContenitoreMessaggio(elemento) IsNot Nothing
    End Function

    Public Function EntraNelCorpo(elemento As IUIAutomationElement) As String
        Try
            Dim origine As IUIAutomationElement = ContenitoreMessaggio(elemento)
            If origine Is Nothing Then origine = elemento
            Dim corpo As IUIAutomationElement = TrovaCorpoDentro(origine)
            If corpo Is Nothing Then
                If Not RigaAperta(origine) Then Return ""
                corpo = origine
            End If
            _rigaAperta = origine
            _ambito = corpo
            Dim voce As IUIAutomationElement = PrimaVoceDiLettura(corpo)
            If voce Is Nothing Then voce = corpo
            If RigaAperta(origine) Then _ambito = origine
            Return Arriva(voce, False, True, False, "", True)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.EntraNelCorpo: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function EntraNellaMailSingola(fuoco As IUIAutomationElement) As String
        Try
            If fuoco Is Nothing OrElse _radice Is Nothing Then Return ""
            If RichiedeScrittura(fuoco) Then Return ""
            If ContenitoreMessaggio(fuoco) IsNot Nothing Then Return ""
            If VoceDiElenco(fuoco) Then Return ""
            Dim trovato As IUIAutomationElement = CercaMailSingola()
            If trovato Is Nothing Then Return ""
            If _ambito IsNot Nothing AndAlso (Stesso(trovato, _rigaAperta) OrElse Stesso(trovato, _ambito) OrElse Dentro(_ambito, trovato) OrElse Dentro(_rigaAperta, trovato)) Then Return ""
            Return EntraNelCorpo(trovato)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.EntraNellaMailSingola: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function RestaSullaMailSingola(fuoco As IUIAutomationElement) As Boolean
        Try
            If _ambito Is Nothing OrElse fuoco Is Nothing Then Return False
            If VoceDiElenco(fuoco) AndAlso Not RigaAperta(fuoco) AndAlso Not Dentro(fuoco, _rigaAperta) Then Return False
            Dim trovato As IUIAutomationElement = CercaMailSingola()
            If trovato Is Nothing Then Return False
            Return Stesso(trovato, _rigaAperta) OrElse Stesso(trovato, _ambito) OrElse Dentro(_ambito, trovato) OrElse Dentro(_rigaAperta, trovato)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RestaSullaMailSingola: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function ApriContenuto() As String
        Try
            If _radice Is Nothing Then Return ""
            Dim riga As IUIAutomationElement = _corrente
            Dim migliore As IUIAutomationElement = TrovaContenuto()
            If migliore Is Nothing Then Return ""
            _rigaAperta = riga
            _ambito = migliore
            Dim voce As IUIAutomationElement = PrimaVoceDiLettura(migliore)
            If voce Is Nothing Then voce = migliore
            Dim frase As String = Arriva(voce, False, True, False, "messaggio, ", True)
            Dim corpo As String = TestoDalPattern(migliore, 900)
            If corpo = "" Then corpo = Anteprima(voce, 700)
            If corpo <> "" Then
                Dim campione As String = corpo.Substring(0, Math.Min(48, corpo.Length))
                If frase.IndexOf(campione, StringComparison.CurrentCulture) < 0 Then frase &= ". " & corpo
            End If
            Return frase
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ApriContenuto: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Function RestaNelMessaggio(elemento As IUIAutomationElement) As Boolean
        Try
            If _ambito Is Nothing OrElse elemento Is Nothing Then Return False
            If Dentro(elemento, _ambito) Then Return True
            If _rigaAperta IsNot Nothing AndAlso (Stesso(elemento, _rigaAperta) OrElse Dentro(elemento, _rigaAperta)) Then Return True
            Return False
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RestaNelMessaggio: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub ChiudiMessaggio()
        _ambito = Nothing
        _rigaAperta = Nothing
    End Sub

    Private Function RadiceCursore() As IUIAutomationElement
        If _ambito IsNot Nothing Then Return _ambito
        Return _radice
    End Function

    Private Function EsciDalMessaggio(seleziona As Boolean) As String
        Dim lasciato As IUIAutomationElement = _ambito
        ChiudiMessaggio()
        Dim fine As Boolean = False
        Dim destinazione As IUIAutomationElement = Nothing
        If lasciato IsNot Nothing Then destinazione = ProssimaVoce(lasciato, False, 90, fine)
        If destinazione Is Nothing Then destinazione = PrimaVoce(_radice, 40)
        If destinazione Is Nothing Then Return "inizio"
        Return Arriva(destinazione, seleziona, False, False, "fuori dal messaggio, ", True)
    End Function

    Private Function ElementoVivo(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim id As Array = elemento.GetRuntimeId()
            Return id IsNot Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ElementoVivo", exAtteso)
            Return False
        End Try
    End Function

    Private Function TrovaContenuto() As IUIAutomationElement
        Dim migliore As IUIAutomationElement = Nothing
        Dim punteggioMigliore As Integer = 0
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim pila As New Stack(Of IUIAutomationElement)()
        pila.Push(_radice)
        Dim visitati As Integer = 0
        While pila.Count > 0 AndAlso visitati < 4000 AndAlso orologio.ElapsedMilliseconds < 700
            Dim nodo As IUIAutomationElement = pila.Pop()
            visitati += 1
            If nodo Is Nothing Then Continue While
            Dim fuori As Boolean = Not Stesso(nodo, _radice) AndAlso _descrittore.FuoriSchermo(nodo)
            Dim ruoloNodo As String = _descrittore.RuoloAria(nodo)
            If fuori AndAlso (ruoloNodo = "row" OrElse ruoloNodo = "listitem" OrElse ruoloNodo = "option" OrElse ruoloNodo = "menuitem" OrElse ruoloNodo = "menu" OrElse ruoloNodo = "toolbar") Then
                Dim eLaRiga As Boolean = _corrente IsNot Nothing AndAlso (Stesso(nodo, _corrente) OrElse Dentro(_corrente, nodo))
                If Not eLaRiga Then Continue While
            End If
            If Not fuori OrElse SembraCorpo(nodo) Then
                Dim punti As Integer = PunteggioContenuto(nodo)
                If fuori AndAlso punti > 500 Then punti -= 500
                If punti > punteggioMigliore Then
                    punteggioMigliore = punti
                    migliore = nodo
                End If
            End If
            Dim figlio As IUIAutomationElement = PrimoFiglio(nodo)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 80
                pila.Push(figlio)
                figlio = Fratello(figlio)
                giri += 1
            End While
        End While
        If punteggioMigliore < 1600 Then Return Nothing
        Return migliore
    End Function

    Private Function PunteggioContenuto(elemento As IUIAutomationElement) As Integer
        Try
            If elemento Is Nothing OrElse Stesso(elemento, _radice) Then Return 0
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            If ruolo = "row" OrElse ruolo = "gridcell" OrElse ruolo = "listitem" OrElse ruolo = "option" OrElse ruolo = "menu" OrElse ruolo = "menuitem" Then Return 0
            If _corrente IsNot Nothing AndAlso EVoceElenco(_corrente) AndAlso Dentro(_corrente, elemento) AndAlso Not SembraCorpo(elemento) Then Return 0
            If DentroElenco(elemento) AndAlso Not SembraCorpo(elemento) Then Return 0
            Dim nome As String = _descrittore.Nome(elemento)
            Dim lunghezza As Integer = If(nome Is Nothing, 0, nome.Length)
            Dim bonusNome As Integer = 0
            If NomeDaMessaggio(nome) Then bonusNome = 2500
            Select Case ruolo
                Case "article"
                    Return 8000 + bonusNome + Math.Min(lunghezza, 1500)
                Case "document"
                    Return 7200 + bonusNome + Math.Min(Math.Max(lunghezza, LunghezzaPattern(elemento)), 1500)
                Case "main"
                    Return 4200 + bonusNome + Math.Min(lunghezza, 400)
                Case "region"
                    If lunghezza >= 8 OrElse bonusNome > 0 Then Return 3400 + bonusNome + Math.Min(lunghezza, 800)
            End Select
            If lunghezza >= 40 Then Return 2200 + bonusNome + Math.Min(lunghezza, 1200)
            If bonusNome > 0 Then Return 2000 + Math.Min(lunghezza, 400)
            If ruolo = "" OrElse ruolo = "group" OrElse ruolo = "presentation" Then
                Select Case _descrittore.TipoControllo(elemento)
                    Case DescrittoreElementi.ControlloGruppo, DescrittoreElementi.ControlloRiquadro, DescrittoreElementi.ControlloDocumento, DescrittoreElementi.ControlloTesto, DescrittoreElementi.ControlloPersonalizzato
                        Dim dalPattern As Integer = LunghezzaPattern(elemento)
                        If dalPattern >= 80 Then Return 2600 + Math.Min(dalPattern, 1200)
                End Select
            End If
            Return 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.PunteggioContenuto", exAtteso)
            Return 0
        End Try
    End Function

    Private Function PrimaVoceDiLettura(contenitore As IUIAutomationElement) As IUIAutomationElement
        Dim nodo As IUIAutomationElement = PrimaVoce(contenitore, 80)
        Dim saltati As Integer = 0
        While nodo IsNot Nothing AndAlso saltati < 30
            Dim tipo As String = Classifica(nodo, _descrittore.TipoControllo(nodo))
            If tipo = "testo" OrElse tipo = "titolo" OrElse tipo = "articolo" Then
                If TestoElemento(nodo).Length >= 8 Then Return nodo
            End If
            If tipo <> "pulsante" AndAlso tipo <> "collegamento" AndAlso tipo <> "casella" AndAlso tipo <> "grafica" AndAlso tipo <> "voce" Then
                If TestoElemento(nodo).Length >= 24 Then Return nodo
            End If
            Dim fine As Boolean = False
            Dim prossimo As IUIAutomationElement = ProssimaVoce(nodo, True, 30, fine)
            If prossimo Is Nothing Then Exit While
            nodo = prossimo
            saltati += 1
        End While
        Return PrimaVoce(contenitore, 40)
    End Function

    Private Function EVoceElenco(elemento As IUIAutomationElement) As Boolean
        Select Case _descrittore.RuoloAria(elemento)
            Case "row", "gridcell", "listitem", "option", "treeitem", "link"
                Return True
        End Select
        Select Case _descrittore.TipoControllo(elemento)
            Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoDati, DescrittoreElementi.ControlloCollegamento
                Return True
        End Select
        Return False
    End Function

    Private Function DentroElenco(elemento As IUIAutomationElement) As Boolean
        Dim corrente As IUIAutomationElement = GenitoreControllo(elemento)
        For salto As Integer = 0 To 8
            If corrente Is Nothing Then Return False
            Select Case _descrittore.RuoloAria(corrente)
                Case "row", "listitem", "listbox", "list", "treegrid"
                    Return True
            End Select
            corrente = GenitoreControllo(corrente)
        Next
        Return False
    End Function

    Private Function Dentro(elemento As IUIAutomationElement, contenitore As IUIAutomationElement) As Boolean
        If elemento Is Nothing OrElse contenitore Is Nothing Then Return False
        Dim corrente As IUIAutomationElement = elemento
        For salto As Integer = 0 To 18
            If corrente Is Nothing Then Return False
            If Stesso(corrente, contenitore) Then Return True
            corrente = GenitoreControllo(corrente)
        Next
        Return False
    End Function

    Private Shared Function NomeDaMessaggio(nome As String) As Boolean
        If String.IsNullOrWhiteSpace(nome) Then Return False
        Dim testo As String = nome.ToLowerInvariant()
        Return testo.Contains("messaggio") OrElse testo.Contains("message") OrElse testo.Contains("conversazione") OrElse testo.Contains("conversation") OrElse testo.Contains("corpo del") OrElse testo.Contains("email")
    End Function

    Private Function LunghezzaPattern(elemento As IUIAutomationElement) As Integer
        Try
            If elemento Is Nothing Then Return 0
            Dim identita As String = Chiave(elemento)
            Dim nota As Integer = 0
            If MisuraValida(_lunghezzaNota, identita, nota) Then Return nota
            Dim testo As String = TestoDalPattern(elemento, 240)
            Dim lunghezza As Integer = If(testo Is Nothing, 0, testo.Length)
            RicordaMisura(_lunghezzaNota, identita, lunghezza)
            Return lunghezza
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.LunghezzaPattern: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function Anteprima(partenza As IUIAutomationElement, massimo As Integer) As String
        Dim testo As New StringBuilder()
        Dim nodo As IUIAutomationElement = partenza
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim giri As Integer = 0
        While nodo IsNot Nothing AndAlso giri < 40 AndAlso orologio.ElapsedMilliseconds < 180 AndAlso testo.Length < massimo
            If Not Stesso(nodo, _radice) Then AccodaPezzo(testo, TestoElemento(nodo))
            Dim fine As Boolean = False
            nodo = ProssimaVoce(nodo, True, 25, fine)
            If nodo Is Nothing Then Exit While
            giri += 1
        End While
        Dim pulito As String = testo.ToString().Replace(vbLf, " ").Trim()
        If pulito.Length > massimo Then pulito = pulito.Substring(0, massimo)
        Return pulito
    End Function

    Private Function Arriva(elemento As IUIAutomationElement, seleziona As Boolean, avanti As Boolean, conPosizione As Boolean, prefisso As String, salto As Boolean, Optional livelloPrima As Boolean = True) As String
        _sospesa = Nothing
        If avanti AndAlso Not seleziona Then
            Dim titoloInterno As IUIAutomationElement = TitoloUgualeDentro(elemento)
            If titoloInterno IsNot Nothing Then elemento = titoloInterno
        End If
        Dim nuovoVerso As Integer = _verso + If(salto, If(avanti, 1000, -1000), If(avanti, 1, -1))
        Dim cresce As Boolean = True
        If seleziona Then
            cresce = Estende(nuovoVerso)
        Else
            _selezioneAttiva = False
            _tutto = False
        End If
        _verso = nuovoVerso
        _corrente = elemento
        _testoPronto = False
        _pos = -1
        Dim uscite As String = ""
        Dim ingressi As String = ""
        FraseContenitori(elemento, uscite, ingressi)
        Dim regione As String = AnnuncioRegione(elemento)
        If regione <> "" AndAlso RegioneComeVoce(regione, elemento) Then regione = ""
        Dim frase As String = If(prefisso, "") & If(uscite <> "", uscite & ", ", "") & If(regione <> "", regione & ", ", "") & If(ingressi <> "", ingressi & ", ", "") & Annuncio(elemento, livelloPrima)
        If conPosizione Then frase &= Posizione(elemento)
        If seleziona Then frase &= If(cresce, " selezionato", " deselezionato")
        Return frase
    End Function

    Private Function TitoloUgualeDentro(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walker Is Nothing Then Return Nothing
            If Classifica(elemento, _descrittore.TipoControllo(elemento)) <> "regione" Then Return Nothing
            Dim nomeRegione As String = _descrittore.Nome(elemento).Trim()
            If nomeRegione = "" Then Return Nothing
            Dim nodo As IUIAutomationElement = elemento
            For salto As Integer = 0 To 3
                nodo = _walker.GetFirstChildElementBuildCache(nodo, _cache)
                If nodo Is Nothing Then Return Nothing
                If Classifica(nodo, _descrittore.TipoControllo(nodo)) = "titolo" Then
                    Dim nomeTitolo As String = _descrittore.Nome(nodo).Trim()
                    If nomeTitolo = "" Then nomeTitolo = If(ContenutoVoce(nodo), "").Trim()
                    If String.Equals(nomeTitolo, nomeRegione, StringComparison.CurrentCultureIgnoreCase) Then Return nodo
                    Return Nothing
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.TitoloUgualeDentro: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Private Function RegioneComeVoce(regione As String, elemento As IUIAutomationElement) As Boolean
        Try
            If String.IsNullOrWhiteSpace(regione) OrElse elemento Is Nothing Then Return False
            Dim nome As String = _descrittore.Nome(elemento)
            If nome = "" Then nome = ContenutoVoce(elemento)
            If String.IsNullOrWhiteSpace(nome) Then Return False
            nome = nome.Trim()
            Dim inizio As String = regione.Split(","c)(0).Trim()
            Return String.Equals(inizio, nome, StringComparison.CurrentCultureIgnoreCase) OrElse String.Equals(regione.Trim(), nome, StringComparison.CurrentCultureIgnoreCase)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RegioneComeVoce: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function Estende(nuovoVerso As Integer) As Boolean
        If Not _selezioneAttiva Then
            _selezioneAttiva = True
            _tutto = False
            _ancora = _corrente
            _ancoraPos = _pos
            _ancoraVerso = _verso
        End If
        Return Math.Abs(nuovoVerso - _ancoraVerso) >= Math.Abs(_verso - _ancoraVerso)
    End Function

    Private Function LivelloAntenatoTitolo(elemento As IUIAutomationElement) As Integer
        Try
            Dim corrente As IUIAutomationElement = GenitoreControllo(elemento)
            For salto As Integer = 0 To 2
                If corrente Is Nothing Then Exit For
                Dim livello As Integer = _descrittore.LivelloTitolo(corrente)
                If livello > 0 Then Return livello
                corrente = GenitoreControllo(corrente)
            Next
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.LivelloAntenatoTitolo", exAtteso)
        End Try
        Return 0
    End Function

    Private Sub RegioneDi(elemento As IUIAutomationElement, ByRef frase As String, ByRef chiaveOut As String, ByRef proprio As Boolean)
        frase = ""
        chiaveOut = ""
        proprio = False
        Dim visitati As New List(Of String)()
        Try
            Dim corrente As IUIAutomationElement = elemento
            For salto As Integer = 0 To 25
                If corrente Is Nothing Then Exit For
                Dim chiaveCorrente As String = Chiave(corrente)
                Dim noto() As String = Nothing
                If chiaveCorrente <> "" AndAlso _regioniNote.TryGetValue(chiaveCorrente, noto) Then
                    frase = noto(0)
                    chiaveOut = noto(1)
                    proprio = salto = 0 AndAlso chiaveOut <> "" AndAlso chiaveOut = chiaveCorrente
                    Exit For
                End If
                If chiaveCorrente <> "" Then visitati.Add(chiaveCorrente)
                Dim nomeRegione As String = _descrittore.NomeRegione(corrente)
                If nomeRegione <> "" Then
                    Dim nomeElemento As String = _descrittore.Nome(corrente)
                    frase = nomeRegione
                    If nomeElemento <> "" AndAlso Not String.Equals(nomeElemento, nomeRegione, StringComparison.CurrentCultureIgnoreCase) Then
                        frase = nomeElemento & ", " & nomeRegione
                    End If
                    chiaveOut = If(chiaveCorrente <> "", chiaveCorrente, Chiave(corrente))
                    proprio = (salto = 0)
                    Exit For
                End If
                corrente = GenitoreControllo(corrente)
            Next
            If _regioniNote.Count > 20000 Then _regioniNote.Clear()
            Dim valore() As String = {frase, chiaveOut}
            For Each visitato As String In visitati
                _regioniNote(visitato) = valore
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RegioneDi: " & ex.Message)
        End Try
    End Sub
    Private Function AnnuncioRegione(elemento As IUIAutomationElement) As String
        Try
            If ModoRegioni = 0 OrElse elemento Is Nothing Then Return ""
            Dim frase As String = ""
            Dim chiave As String = ""
            Dim proprio As Boolean = False
            RegioneDi(elemento, frase, chiave, proprio)
            Dim nuova As Boolean = chiave <> _regioneCorrente
            _regioneCorrente = chiave
            If ModoRegioni = 3 OrElse frase = "" OrElse proprio Then Return ""
            If ModoRegioni = 2 OrElse nuova Then Return frase
            Return ""
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AnnuncioRegione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function FermataRegione(destinazione As IUIAutomationElement, avanti As Boolean, seleziona As Boolean) As String
        Try
            If ModoRegioni <> 3 OrElse seleziona OrElse destinazione Is Nothing Then Return ""
            Dim frase As String = ""
            Dim chiave As String = ""
            Dim proprio As Boolean = False
            RegioneDi(destinazione, frase, chiave, proprio)
            If frase = "" OrElse proprio OrElse chiave = _regioneCorrente Then Return ""
            _regioneCorrente = chiave
            _sospesa = destinazione
            _sospesaAvanti = avanti
            Return frase
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FermataRegione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function Avvolgi(avanti As Boolean, seleziona As Boolean) As String
        If seleziona Then Return ""
        Dim origine As IUIAutomationElement = RadiceCursore()
        Dim destinazione As IUIAutomationElement = If(avanti, PrimaVoce(origine, 50), UltimaVoce(70))
        If destinazione Is Nothing OrElse Stesso(destinazione, _corrente) Then Return ""
        Return Arriva(destinazione, False, avanti, True, If(avanti, "dall'inizio, ", "dalla fine, "), True)
    End Function

    Private Function Posizione(elemento As IUIAutomationElement) As String
        If _web Then Return ""
        If Not _indiceCompleto OrElse _voci.Count = 0 Then Return ""
        Dim indice As Integer = IndiceDi(elemento)
        If indice < 0 Then Return ""
        Return ", " & (indice + 1).ToString() & " di " & _voci.Count.ToString()
    End Function

    Private Function ArricchisciVoce(elemento As IUIAutomationElement, testo As String, nome As String) As String
        Try
            If nome = "" Then
                If Foglia(elemento, Classifica(elemento, _descrittore.TipoControllo(elemento))) Then
                    Dim contenuto As String = ContenutoVoce(elemento, True)
                    If contenuto <> "" Then Return If(testo = "" OrElse testo = nome, contenuto, contenuto & ", " & testo)
                End If
                Return testo
            End If
            If _descrittore.RuoloAria(elemento) = "row" Then
                Dim sostituisce As Boolean = False
                Dim celle As String = LetturaRigheElenco.Descrivi(elemento, _descrittore, _walkerAntenati, _cache, True, sostituisce)
                If celle <> "" Then
                    If sostituisce Then
                        Dim posizione As Integer = testo.IndexOf(nome, StringComparison.Ordinal)
                        If posizione >= 0 Then Return testo.Substring(0, posizione) & celle & testo.Substring(posizione + nome.Length)
                    End If
                    Return testo & ", " & celle
                End If
            End If
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ArricchisciVoce", exAtteso)
        End Try
        Return testo
    End Function

    Private Function Annuncio(elemento As IUIAutomationElement, Optional livelloPrima As Boolean = True) As String
        If elemento Is Nothing Then Return "vuoto"
        Dim tipo As String = Classifica(elemento, _descrittore.TipoControllo(elemento))
        If tipo = "codice" Then Return AnnuncioCodice(elemento)
        Dim testo As String = _descrittore.Descrivi(elemento)
        Dim nome As String = _descrittore.Nome(elemento)
        If testo = "" Then testo = nome
        If testo = "" AndAlso (tipo = "separatore" OrElse tipo = "citazione" OrElse tipo = "riquadro") Then testo = NomeTipo(tipo, 0)
        If (tipo = "collegamento" OrElse tipo = "pulsante") AndAlso nome = "" Then
            Dim nomeInterno As String = NomeDaDiscendenti(elemento)
            If nomeInterno <> "" AndAlso tipo = "collegamento" Then
                Dim breve As String = DescrittoreElementi.NomeBreveIndirizzo(_descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaValore))
                If breve <> "" Then
                    testo = testo.Replace(", " & breve, "")
                    If testo.StartsWith(breve, StringComparison.Ordinal) Then testo = testo.Substring(breve.Length).TrimStart(","c, " "c)
                End If
            End If
            If nomeInterno <> "" Then
                If testo = "" Then
                    testo = nomeInterno
                ElseIf tipo = "collegamento" AndAlso DescrittoreElementi.LinkPrimaDelNome Then
                    testo = testo & ", " & nomeInterno
                Else
                    testo = nomeInterno & ", " & testo
                End If
            End If
        End If
        If tipo = "titolo" Then
            If livelloPrima AndAlso nome <> "" Then
                Dim ruolo As String = _descrittore.FraseRuoloTitolo(LivelloTitoloVoce(elemento))
                Dim marca As String = nome & ", " & ruolo
                If testo.StartsWith(marca, StringComparison.Ordinal) Then testo = ruolo & ", " & nome & testo.Substring(marca.Length)
            End If
            If EsLinkTitolo(elemento, _descrittore.TipoControllo(elemento)) Then
                Dim ruoloLink As String = _descrittore.FraseRuoloTitolo(LivelloTitoloVoce(elemento))
                If testo.IndexOf(ruoloLink, StringComparison.CurrentCultureIgnoreCase) < 0 Then testo = If(livelloPrima, ruoloLink & ", " & testo, testo & ", " & ruoloLink)
                If Not livelloPrima Then testo = LinkDopoNome(testo, nome)
            End If
            If TrovaInterattivo(elemento, 0) IsNot Nothing Then
                If livelloPrima AndAlso DescrittoreElementi.LinkPrimaDelNome Then
                    testo = "link, " & testo
                ElseIf nome <> "" AndAlso testo.StartsWith(nome, StringComparison.Ordinal) Then
                    testo = nome & ", link" & testo.Substring(nome.Length)
                Else
                    testo = testo & ", link"
                End If
            End If
        ElseIf DescrittoreElementi.DireLivelloTitoli Then
            Dim livelloAntenato As Integer = LivelloAntenatoTitolo(elemento)
            If livelloAntenato > 0 Then
                Dim ruoloAntenato As String = _descrittore.FraseRuoloTitolo(livelloAntenato)
                testo = If(livelloPrima, ruoloAntenato & ", " & testo, testo & ", " & ruoloAntenato)
            End If
        End If
        If EVoceComposita(tipo) Then testo = ArricchisciVoce(elemento, testo, nome)
        If testo = "" AndAlso Foglia(elemento, tipo) Then testo = ContenutoVoce(elemento)
        If testo = "" Then Return ""
        If tipo = "collegamento" Then testo = LinkStessaPagina(elemento, testo)
        testo = LetturaAlberi.Arricchisci(testo, elemento, _descrittore, _walkerAntenati, _cache)
        If tipo = "tabella" Then
            Dim sommario As String = LetturaTabelle.Sommario(elemento, _descrittore)
            If sommario <> "" AndAlso testo.IndexOf(sommario, StringComparison.CurrentCultureIgnoreCase) < 0 Then testo &= ", " & sommario
        End If
        Return testo
    End Function

    Private Function PrimaVoce(origine As IUIAutomationElement, millisecondi As Integer) As IUIAutomationElement
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim nodo As IUIAutomationElement = origine
        Dim passi As Integer = 0
        While nodo IsNot Nothing AndAlso passi < 3000 AndAlso orologio.ElapsedMilliseconds < millisecondi
            nodo = NodoSuccessivo(nodo)
            passi += 1
            If nodo Is Nothing OrElse Stesso(nodo, origine) Then Return Nothing
            Dim tipo As String = Classifica(nodo, _descrittore.TipoControllo(nodo))
            If VoceUtile(tipo, nodo) Then Return nodo
        End While
        Return Nothing
    End Function

    Private Function UltimaVoce(millisecondi As Integer) As IUIAutomationElement
        Dim origine As IUIAutomationElement = RadiceCursore()
        Dim nodo As IUIAutomationElement = UltimoDiscendente(origine)
        If nodo Is Nothing OrElse Stesso(nodo, origine) Then Return PrimaVoce(origine, millisecondi)
        Dim tipo As String = Classifica(nodo, _descrittore.TipoControllo(nodo))
        If VoceUtile(tipo, nodo) Then Return nodo
        Dim fine As Boolean = False
        Return ProssimaVoceTenace(nodo, False, Math.Max(millisecondi, AttesaVoceMs), fine)
    End Function

    Private Function ProssimaVoce(partenza As IUIAutomationElement, avanti As Boolean, millisecondi As Integer, ByRef fineAlbero As Boolean) As IUIAutomationElement
        Dim ultimo As IUIAutomationElement = Nothing
        Return CamminaVoce(partenza, avanti, millisecondi, fineAlbero, ultimo)
    End Function

    Private Function ProssimaVoceTenace(partenza As IUIAutomationElement, avanti As Boolean, millisecondi As Integer, ByRef fineAlbero As Boolean) As IUIAutomationElement
        fineAlbero = False
        Try
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim nodo As IUIAutomationElement = partenza
            For giro As Integer = 1 To 40
                Dim ultimo As IUIAutomationElement = Nothing
                Dim trovato As IUIAutomationElement = CamminaVoce(nodo, avanti, 90, fineAlbero, ultimo)
                If trovato IsNot Nothing OrElse fineAlbero Then Return trovato
                If ultimo Is Nothing OrElse Stesso(ultimo, nodo) Then Return Nothing
                If orologio.ElapsedMilliseconds >= millisecondi Then Return Nothing
                nodo = ultimo
            Next
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ProssimaVoceTenace", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function CamminaVoce(partenza As IUIAutomationElement, avanti As Boolean, millisecondi As Integer, ByRef fineAlbero As Boolean, ByRef ultimo As IUIAutomationElement) As IUIAutomationElement
        fineAlbero = False
        ultimo = Nothing
        Try
            If partenza Is Nothing Then
                fineAlbero = True
                Return Nothing
            End If
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim nodo As IUIAutomationElement = partenza
            Dim passi As Integer = 0
            While passi < 2500 AndAlso orologio.ElapsedMilliseconds < millisecondi
                nodo = If(avanti, NodoSuccessivo(nodo), NodoPrecedente(nodo))
                passi += 1
                If nodo Is Nothing OrElse Stesso(nodo, partenza) OrElse Stesso(nodo, RadiceCursore()) Then
                    fineAlbero = True
                    Return Nothing
                End If
                ultimo = nodo
                Dim tipo As String = Classifica(nodo, _descrittore.TipoControllo(nodo))
                If VoceUtile(tipo, nodo) Then Return nodo
            End While
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.CamminaVoce", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function NodoSuccessivo(nodo As IUIAutomationElement, Optional pagina As Boolean = False) As IUIAutomationElement
        Try
            If nodo Is Nothing Then Return Nothing
            Dim tipo As String = Classifica(nodo, _descrittore.TipoControllo(nodo))
            If Not Foglia(nodo, tipo) Then
                Dim figlio As IUIAutomationElement = PrimoFiglio(nodo)
                If figlio IsNot Nothing AndAlso Not Stesso(figlio, nodo) Then Return figlio
            End If
            Return SaltaAvanti(nodo, pagina)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.NodoSuccessivo", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function SaltaAvanti(nodo As IUIAutomationElement, Optional pagina As Boolean = False) As IUIAutomationElement
        Dim limite As IUIAutomationElement = If(pagina, _radice, RadiceCursore())
        Dim corrente As IUIAutomationElement = nodo
        Dim giri As Integer = 0
        While corrente IsNot Nothing AndAlso Not Stesso(corrente, limite) AndAlso giri < 80
            Dim successivo As IUIAutomationElement = Fratello(corrente)
            If successivo IsNot Nothing Then Return successivo
            corrente = GenitoreNavigazione(corrente)
            giri += 1
        End While
        Return Nothing
    End Function

    Private Function NodoPrecedente(nodo As IUIAutomationElement, Optional pagina As Boolean = False) As IUIAutomationElement
        Try
            If nodo Is Nothing OrElse Stesso(nodo, If(pagina, _radice, RadiceCursore())) Then Return Nothing
            Dim precedente As IUIAutomationElement = FratelloPrecedente(nodo)
            If precedente Is Nothing Then Return GenitoreNavigazione(nodo)
            Return UltimoDiscendente(precedente)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.NodoPrecedente", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function UltimoDiscendente(nodo As IUIAutomationElement) As IUIAutomationElement
        Dim corrente As IUIAutomationElement = nodo
        Dim giri As Integer = 0
        While corrente IsNot Nothing AndAlso giri < 60
            Dim tipo As String = Classifica(corrente, _descrittore.TipoControllo(corrente))
            If Foglia(corrente, tipo) Then Return corrente
            Dim ultimo As IUIAutomationElement = UltimoFiglio(corrente)
            If ultimo Is Nothing OrElse Stesso(ultimo, corrente) Then Return corrente
            corrente = ultimo
            giri += 1
        End While
        Return corrente
    End Function

    Private Function Foglia(elemento As IUIAutomationElement, tipo As String) As Boolean
        If _descrittore.MessaggioTeams(elemento, _walkerAntenati, _cache) Then Return True
        Select Case tipo
            Case "ignora", "grafica", "collegamento", "pulsante", "modifica", "casella", "opzione", "combinata", "codice", "separatore"
                Return True
        End Select
        If tipo = "titolo" AndAlso EsLinkTitolo(elemento, _descrittore.TipoControllo(elemento)) Then Return True
        If tipo = "titolo" Then Return TrovaInterattivo(elemento, 0) Is Nothing
        If tipo = "articolo" Then Return _descrittore.Nome(elemento).Length >= 40 AndAlso FigliSenzaContenuto(elemento) AndAlso Not ContieneTitolo(elemento)
        If (tipo = "voce" OrElse tipo = "cella") AndAlso HaCorpoEspanso(elemento) Then Return False
        If tipo = "testo" OrElse tipo = "voce" OrElse tipo = "cella" Then
            Dim figlio As IUIAutomationElement = PrimoFiglio(elemento)
            If figlio Is Nothing Then Return True
            If ContieneTitolo(elemento) Then Return False
            If tipo = "testo" AndAlso _descrittore.Nome(elemento).Length >= 40 AndAlso FigliSenzaContenuto(elemento) Then Return True
            Dim controllati As Integer = 0
            Dim limiteFigli As Integer = 1
            If tipo = "cella" OrElse (tipo = "voce" AndAlso _descrittore.RuoloAria(elemento) = "row") Then limiteFigli = 25
            While figlio IsNot Nothing AndAlso controllati < limiteFigli
                Dim tipoFiglio As String = Classifica(figlio, _descrittore.TipoControllo(figlio))
                Select Case tipoFiglio
                    Case "collegamento", "pulsante", "modifica", "titolo", "grafica", "casella", "opzione", "combinata", "elenco", "tabella", "regione", "citazione", "articolo"
                        Return False
                    Case "cella", "voce"
                        If tipo <> "testo" Then Return False
                End Select
                controllati += 1
                figlio = Fratello(figlio)
            End While
            Return True
        End If
        Return False
    End Function

    Private Function ContieneTitolo(elemento As IUIAutomationElement) As Boolean
        Try
            Dim identita As String = Chiave(elemento)
            Dim noto As Boolean = False
            If identita <> "" AndAlso _titoloDentroNoto.TryGetValue(identita, noto) Then Return noto
            Dim budget As Integer = 80
            Dim esito As Boolean = CercaTitoloDentro(elemento, 0, budget)
            If identita <> "" Then
                If _titoloDentroNoto.Count > 3000 Then _titoloDentroNoto.Clear()
                _titoloDentroNoto(identita) = esito
            End If
            Return esito
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ContieneTitolo", exAtteso)
            Return False
        End Try
    End Function

    Private Function CercaTitoloDentro(elemento As IUIAutomationElement, profondita As Integer, ByRef budget As Integer) As Boolean
        Try
            If elemento Is Nothing OrElse profondita > 4 Then Return False
            Dim figlio As IUIAutomationElement = PrimoFiglio(elemento)
            While figlio IsNot Nothing AndAlso budget > 0
                budget -= 1
                If _descrittore.LivelloTitolo(figlio) > 0 OrElse _descrittore.RuoloAria(figlio) = "heading" Then Return True
                If CercaTitoloDentro(figlio, profondita + 1, budget) Then Return True
                figlio = Fratello(figlio)
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.CercaTitoloDentro", exAtteso)
        End Try
        Return False
    End Function

    Private Function ArticoloInElenco(elemento As IUIAutomationElement) As Boolean
        Try
            If _descrittore.RuoloAria(elemento) <> "article" Then Return False
            Dim padre As IUIAutomationElement = GenitoreControllo(elemento)
            If padre Is Nothing Then Return False
            Select Case _descrittore.RuoloAria(padre)
                Case "list", "feed", "listbox"
                    Return True
            End Select
            Return _descrittore.TipoControllo(padre) = DescrittoreElementi.ControlloElenco
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ArticoloInElenco", exAtteso)
            Return False
        End Try
    End Function

    Private Function EVoceComposita(tipo As String) As Boolean
        Return tipo = "voce" OrElse tipo = "cella" OrElse tipo = "intestazioneColonna" OrElse tipo = "intestazioneRiga" OrElse tipo = "articolo"
    End Function

    Private Function ContenutoVoce(elemento As IUIAutomationElement, Optional conColonne As Boolean = False) As String
        Try
            If conColonne Then Return If(LetturaRigheElenco.Contenuto(elemento, _descrittore, _walkerAntenati, _cache, True), "")
            Dim identita As String = Chiave(elemento)
            Dim noto As String = Nothing
            If identita <> "" AndAlso _contenutoNoto.TryGetValue(identita, noto) Then Return noto
            Dim testo As String = LetturaRigheElenco.TestoVoce(elemento, _descrittore, _walkerAntenati, _cache, 40)
            If testo Is Nothing Then testo = ""
            If identita <> "" Then
                If _contenutoNoto.Count > 1500 Then _contenutoNoto.Clear()
                _contenutoNoto(identita) = testo
            End If
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ContenutoVoce", exAtteso)
            Return ""
        End Try
    End Function

    Private Function VoceUtile(tipo As String, elemento As IUIAutomationElement) As Boolean
        Select Case tipo
            Case "documento", "finestra", "ignora", "altro"
                Return False
        End Select
        If tipo = "testo" Then
            If _descrittore.Nome(elemento) <> "" OrElse _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaValore) <> "" Then Return True
            Return LunghezzaPattern(elemento) >= 24
        End If
        If tipo = "grafica" OrElse tipo = "voce" OrElse tipo = "cella" OrElse tipo = "intestazioneColonna" OrElse tipo = "intestazioneRiga" Then
            If _descrittore.Nome(elemento) <> "" OrElse _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaValore) <> "" Then Return True
            If tipo <> "grafica" AndAlso Foglia(elemento, tipo) Then Return ContenutoVoce(elemento) <> ""
            Return False
        End If
        Return True
    End Function

    Private Sub AggiungiVoce(elemento As IUIAutomationElement, tipo As String)
        Try
            Dim lista As List(Of VoceAlbero) = If(_vociNuove, _voci)
            Dim mappa As Dictionary(Of String, Integer) = If(_perIdentitaNuove, _perIdentita)
            Dim testo As String = TestoDi(elemento, tipo)
            If tipo = "testo" AndAlso lista.Count > 0 Then
                Dim precedente As VoceAlbero = lista(lista.Count - 1)
                If precedente.Tipo = "titolo" AndAlso String.Equals(precedente.Testo, testo, StringComparison.CurrentCulture) Then Return
            End If
            Dim campoVuotoValido As Boolean = tipo = "modifica" OrElse tipo = "casella" OrElse tipo = "opzione" OrElse tipo = "combinata"
            Dim contenitoreValido As Boolean = tipo = "titolo" OrElse tipo = "regione" OrElse tipo = "tabella" OrElse tipo = "elenco" OrElse tipo = "citazione" OrElse tipo = "separatore" OrElse tipo = "riquadro" OrElse tipo = "oggetto"
            If testo = "" AndAlso Not campoVuotoValido AndAlso Not contenitoreValido Then Return
            Dim identita As String = Chiave(elemento)
            If identita <> "" AndAlso mappa.ContainsKey(identita) Then Return
            Dim voce As New VoceAlbero()
            voce.Elemento = elemento
            voce.Tipo = tipo
            voce.Livello = If(tipo = "titolo", LivelloTitoloVoce(elemento), 0)
            voce.Testo = testo
            If tipo = "articolo" Then voce.InElenco = ArticoloInElenco(elemento)
            If identita <> "" Then mappa(identita) = lista.Count
            lista.Add(voce)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AggiungiVoce: " & ex.Message)
        End Try
    End Sub

    Private Function Classifica(elemento As IUIAutomationElement, tipoControllo As Integer) As String
        Try
            If ContenitoreChiuso(elemento, tipoControllo) Then Return "ignora"
            If _descrittore.MessaggioTeams(elemento, _walkerAntenati, _cache) Then Return "voce"
            If _descrittore.LivelloTitolo(elemento) > 0 OrElse _descrittore.RuoloAria(elemento) = "heading" Then Return "titolo"
            If EsLinkTitolo(elemento, tipoControllo) Then Return "titolo"
            If BloccoCodice(elemento) Then Return "codice"
            If EditabileRicco(elemento, False) Then Return "modifica"
            If _descrittore.NomeRegione(elemento) <> "" AndAlso tipoControllo <> DescrittoreElementi.ControlloPulsante AndAlso tipoControllo <> DescrittoreElementi.ControlloCollegamento AndAlso tipoControllo <> DescrittoreElementi.ControlloModifica Then
                Return "regione"
            End If
            Select Case _descrittore.RuoloAria(elemento)
                Case "embed", "object", "applet", "audio", "video", "embeddedobject", "plugin"
                    Return "oggetto"
            End Select
            Select Case _descrittore.RuoloAria(elemento)
                Case "link"
                    Return "collegamento"
                Case "button"
                    Return "pulsante"
                Case "checkbox"
                    Return "casella"
                Case "radio"
                    Return "opzione"
                Case "textbox", "searchbox"
                    Return "modifica"
                Case "combobox"
                    Return "combinata"
                Case "list"
                    Return "elenco"
                Case "listitem", "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "treeitem", "row", "option"
                    Return "voce"
                Case "switch"
                    Return "casella"
                Case "slider", "spinbutton", "scrollbar"
                    Return If(_descrittore.RuoloAria(elemento) = "scrollbar", "ignora", "modifica")
                Case "listbox", "tree", "feed"
                    Return "elenco"
                Case "treegrid"
                    Return "tabella"
                Case "tooltip"
                    Return "ignora"
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
                Case "table", "grid"
                    Return "tabella"
            End Select
            Select Case tipoControllo
                Case DescrittoreElementi.ControlloCollegamento
                    Return "collegamento"
                Case DescrittoreElementi.ControlloPulsante, DescrittoreElementi.ControlloPulsanteDiviso
                    Return "pulsante"
                Case DescrittoreElementi.ControlloCasellaControllo
                    Return "casella"
                Case DescrittoreElementi.ControlloPulsanteOpzione
                    Return "opzione"
                Case DescrittoreElementi.ControlloModifica
                    Return "modifica"
                Case DescrittoreElementi.ControlloCasellaCombinata
                    Return "combinata"
                Case DescrittoreElementi.ControlloElenco, DescrittoreElementi.ControlloAlbero
                    Return "elenco"
                Case DescrittoreElementi.ControlloDispositivoScorrimento, DescrittoreElementi.ControlloSelettoreNumerico
                    Return "modifica"
                Case DescrittoreElementi.ControlloIntestazione, DescrittoreElementi.ControlloElementoIntestazione
                    Return "intestazioneColonna"
                Case DescrittoreElementi.ControlloDescrizioneComando
                    Return "ignora"
                Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoAlbero, DescrittoreElementi.ControlloElementoDati, DescrittoreElementi.ControlloScheda, DescrittoreElementi.ControlloVoceMenu
                    Return "voce"
                Case DescrittoreElementi.ControlloImmagine
                    Return "grafica"
                Case DescrittoreElementi.ControlloTabella, DescrittoreElementi.ControlloGrigliaDati
                    Return "tabella"
                Case DescrittoreElementi.ControlloTesto
                    Return "testo"
                Case DescrittoreElementi.ControlloDocumento
                    If Not Stesso(elemento, _radice) AndAlso DocumentoEditabile(elemento) Then Return "modifica"
                    If Not Stesso(elemento, _radice) AndAlso FigliSenzaContenuto(elemento) Then
                        If _descrittore.Nome(elemento) <> "" OrElse LunghezzaPattern(elemento) >= 40 Then Return "testo"
                    End If
                    If Not Stesso(elemento, _radice) Then Return "riquadro"
                    Return "documento"
                Case DescrittoreElementi.ControlloFinestra
                    Return "finestra"
                Case DescrittoreElementi.ControlloSeparatore
                    Return "separatore"
                Case DescrittoreElementi.ControlloBarraTitolo, DescrittoreElementi.ControlloBarraMenu, DescrittoreElementi.ControlloBarraScorrimento, DescrittoreElementi.ControlloCursore
                    Return "ignora"
                Case DescrittoreElementi.ControlloPersonalizzato, DescrittoreElementi.ControlloGruppo, DescrittoreElementi.ControlloRiquadro
                    If _descrittore.RuoloAria(elemento) = "" AndAlso EditabileRicco(elemento, False) Then Return "modifica"
                    If FigliSenzaContenuto(elemento) Then
                        If _descrittore.Nome(elemento) <> "" OrElse LunghezzaPattern(elemento) >= 40 Then Return "testo"
                    End If
                    Return "altro"
                Case Else
                    Return "altro"
            End Select
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.Classifica", exAtteso)
            Return "altro"
        End Try
    End Function

    Private Function TrovaInterattivo(elemento As IUIAutomationElement, profondita As Integer) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse profondita > 3 Then Return Nothing
            Dim figlio As IUIAutomationElement = PrimoFiglio(elemento)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 12
                Dim tipo As String = Classifica(figlio, _descrittore.TipoControllo(figlio))
                If tipo = "collegamento" OrElse tipo = "pulsante" Then Return figlio
                Dim trovato As IUIAutomationElement = TrovaInterattivo(figlio, profondita + 1)
                If trovato IsNot Nothing Then Return trovato
                figlio = Fratello(figlio)
                giri += 1
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TrovaInterattivo", exAtteso)
        End Try
        Return Nothing
    End Function

    Private Function NomeDaDiscendenti(elemento As IUIAutomationElement) As String
        Try
            Dim parti As New List(Of String)()
            RaccogliNomi(elemento, parti, 0)
            Return String.Join(" ", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.NomeDaDiscendenti", exAtteso)
            Return ""
        End Try
    End Function

    Private Sub RaccogliNomi(elemento As IUIAutomationElement, parti As List(Of String), profondita As Integer)
        Try
            If profondita > 3 OrElse parti.Count >= 3 Then Return
            Dim figlio As IUIAutomationElement = PrimoFiglio(elemento)
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 12 AndAlso parti.Count < 3
                Dim nomeFiglio As String = _descrittore.Nome(figlio)
                If nomeFiglio <> "" Then
                    parti.Add(nomeFiglio)
                Else
                    RaccogliNomi(figlio, parti, profondita + 1)
                End If
                figlio = Fratello(figlio)
                giri += 1
            End While
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RaccogliNomi", exAtteso)
        End Try
    End Sub

    Private Function Corrisponde(voce As VoceAlbero, tipo As String, livello As Integer) As Boolean
        If tipo = "paragrafo" Then Return voce.Tipo = "testo" AndAlso SembraParagrafo(voce.Testo)
        If tipo = "ortografia" Then Return False
        If tipo = "visitato" Then Return voce.Tipo = "collegamento" AndAlso If(voce.IdAiuto <> 0, voce.Visitato, LinkVisitato(voce.Elemento))
        If tipo = "nonvisitato" Then Return voce.Tipo = "collegamento" AndAlso Not If(voce.IdAiuto <> 0, voce.Visitato, LinkVisitato(voce.Elemento))
        If tipo = "campo" Then
            Return voce.Tipo = "modifica" OrElse voce.Tipo = "casella" OrElse voce.Tipo = "opzione" OrElse voce.Tipo = "combinata"
        End If
        If tipo = "titolo" AndAlso livello > 0 Then Return voce.Tipo = "titolo" AndAlso voce.Livello = livello
        If tipo = "modifica" AndAlso voce.Tipo = "combinata" Then Return If(voce.IdAiuto <> 0, voce.Scrivibile, CombinataScrivibile(voce.Elemento))
        If tipo = "voce" AndAlso voce.Tipo = "articolo" AndAlso voce.InElenco Then Return True
        If tipo = "tabella" AndAlso voce.Tipo = "tabella" Then Return If(voce.IdAiuto <> 0, Not voce.Impaginazione, Not LetturaTabelle.TabellaDiImpaginazione(voce.Elemento, _descrittore))
        Return voce.Tipo = tipo
    End Function

    Private Function CombinataScrivibile(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If _descrittore.CampoSolaLettura(elemento) Then Return False
            Return _descrittore.HaPatternTesto(elemento)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CombinataScrivibile: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function LinkDopoNome(testo As String, nome As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return testo
            Dim prefissi As String() = {"link, ", "collegamento, "}
            Dim parola As String = ""
            For Each prefisso As String In prefissi
                If testo.StartsWith(prefisso, StringComparison.CurrentCultureIgnoreCase) Then
                    parola = testo.Substring(0, prefisso.Length - 2)
                    testo = testo.Substring(prefisso.Length)
                    Exit For
                End If
            Next
            If parola = "" Then Return testo
            If nome <> "" AndAlso testo.StartsWith(nome, StringComparison.Ordinal) Then Return nome & ", " & parola & testo.Substring(nome.Length)
            Return testo & ", " & parola
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.LinkDopoNome: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function CorrispondeNodo(elemento As IUIAutomationElement, tipo As String, livello As Integer) As Boolean
        If elemento Is Nothing Then Return False
        Dim tipoNodo As String = Classifica(elemento, _descrittore.TipoControllo(elemento))
        If tipo = "paragrafo" Then Return tipoNodo = "testo" AndAlso SembraParagrafo(TestoDi(elemento, "testo"))
        If tipo = "ortografia" Then Return False
        If tipo = "visitato" Then Return tipoNodo = "collegamento" AndAlso LinkVisitato(elemento)
        If tipo = "nonvisitato" Then Return tipoNodo = "collegamento" AndAlso Not LinkVisitato(elemento)
        If tipo = "campo" Then
            Return tipoNodo = "modifica" OrElse tipoNodo = "casella" OrElse tipoNodo = "opzione" OrElse tipoNodo = "combinata"
        End If
        If tipo = "titolo" AndAlso livello > 0 Then Return tipoNodo = "titolo" AndAlso LivelloTitoloVoce(elemento) = livello
        If tipo = "modifica" AndAlso tipoNodo = "combinata" Then Return CombinataScrivibile(elemento)
        If tipo = "voce" AndAlso tipoNodo = "articolo" AndAlso ArticoloInElenco(elemento) Then Return True
        If tipo = "tabella" AndAlso tipoNodo = "tabella" Then Return Not LetturaTabelle.TabellaDiImpaginazione(elemento, _descrittore)
        Return tipoNodo = tipo
    End Function

    Private Function TestoDi(elemento As IUIAutomationElement, tipo As String) As String
        Try
            If tipo = "modifica" OrElse tipo = "combinata" Then
                Dim valore As String = _descrittore.LeggiStringa(elemento, DescrittoreElementi.ProprietaValore)
                If valore <> "" Then Return valore
            End If
            If tipo = "codice" Then Return TestoCodice(elemento)
            Dim nome As String = _descrittore.Nome(elemento)
            If nome = "" AndAlso (tipo = "collegamento" OrElse tipo = "pulsante") Then nome = NomeDaDiscendenti(elemento)
            If nome = "" AndAlso (tipo = "testo" OrElse tipo = "articolo" OrElse tipo = "regione") Then nome = TestoDalPattern(elemento, 2000)
            If nome = "" AndAlso EVoceComposita(tipo) AndAlso Foglia(elemento, tipo) Then nome = ContenutoVoce(elemento)
            Return nome
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TestoDi", exAtteso)
            Return ""
        End Try
    End Function

    Private Function TestoCorrente() As String
        If _testoPronto Then Return _testoCorrente
        _testoCorrente = TestoElemento(_corrente)
        If _testoCorrente Is Nothing Then _testoCorrente = ""
        _testoPronto = True
        Return _testoCorrente
    End Function

    Private Function TestoElemento(elemento As IUIAutomationElement) As String
        If elemento Is Nothing Then Return ""
        Dim indice As Integer = -1
        Dim identita As String = Chiave(elemento)
        If identita <> "" AndAlso _perIdentita.TryGetValue(identita, indice) Then
            Dim salvato As String = _voci(indice).Testo
            If salvato <> "" Then Return salvato
        End If
        Return TestoDi(elemento, Classifica(elemento, _descrittore.TipoControllo(elemento)))
    End Function

    Private Function IndiceDi(elemento As IUIAutomationElement) As Integer
        Dim corrente As IUIAutomationElement = elemento
        For salto As Integer = 0 To 4
            If corrente Is Nothing Then Exit For
            Dim identita As String = Chiave(corrente)
            Dim indice As Integer = -1
            If identita <> "" AndAlso _perIdentita.TryGetValue(identita, indice) Then
                If salto = 0 Then Return indice
                If Foglia(corrente, Classifica(corrente, _descrittore.TipoControllo(corrente))) Then Return indice
                Return -1
            End If
            If Stesso(corrente, _radice) Then Exit For
            corrente = GenitoreControllo(corrente)
        Next
        Return -1
    End Function

    Private Function IndiceVicino(elemento As IUIAutomationElement, ByRef esatto As Boolean) As Integer
        esatto = False
        Try
            Dim diretto As Integer = IndiceDi(elemento)
            If diretto >= 0 Then
                esatto = True
                Return diretto
            End If
            If elemento Is Nothing OrElse _voci.Count = 0 Then Return -1
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim nodo As IUIAutomationElement = elemento
            For passo As Integer = 1 To 400
                If orologio.ElapsedMilliseconds > 60 Then Exit For
                nodo = NodoPrecedente(nodo)
                If nodo Is Nothing Then Exit For
                Dim identita As String = Chiave(nodo)
                Dim indice As Integer = -1
                If identita <> "" AndAlso _perIdentita.TryGetValue(identita, indice) Then Return indice
                If Stesso(nodo, _radice) Then Exit For
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.IndiceVicino: " & ex.Message)
        End Try
        Return -1
    End Function

    Private Function RaccogliTutto() As String
        If _indiceCompleto AndAlso _voci.Count > 0 Then
            Dim completo As New StringBuilder()
            For Each voce As VoceAlbero In _voci
                AccodaPezzo(completo, voce.Testo)
                If completo.Length > 100000 Then Exit For
            Next
            Return completo.ToString()
        End If
        Dim inizio As IUIAutomationElement = PrimaVoce(_radice, 40)
        If inizio Is Nothing Then Return Annuncio(_corrente)
        Return Raccogli(inizio, 0, Nothing, -1)
    End Function

    Private Function Raccogli(primo As IUIAutomationElement, primoPos As Integer, ultimo As IUIAutomationElement, ultimoPos As Integer) As String
        Dim testo As New StringBuilder()
        Dim nodo As IUIAutomationElement = primo
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim giri As Integer = 0
        While nodo IsNot Nothing AndAlso giri < 2000 AndAlso orologio.ElapsedMilliseconds < 1500 AndAlso testo.Length < 100000
            Dim contenuto As String = TestoElemento(nodo)
            Dim da As Integer = 0
            Dim a As Integer = If(contenuto Is Nothing, 0, contenuto.Length)
            If giri = 0 AndAlso primoPos > 0 Then da = Math.Min(primoPos, a)
            Dim eUltimo As Boolean = ultimo IsNot Nothing AndAlso Stesso(nodo, ultimo)
            If eUltimo AndAlso ultimoPos >= 0 Then a = Math.Min(ultimoPos + 1, If(contenuto Is Nothing, 0, contenuto.Length))
            If a > da AndAlso contenuto IsNot Nothing Then
                If testo.Length > 0 Then testo.Append(vbLf)
                testo.Append(contenuto, da, a - da)
            End If
            If eUltimo Then Exit While
            Dim fine As Boolean = False
            nodo = ProssimaVoceTenace(nodo, True, 300, fine)
            If nodo Is Nothing Then Exit While
            giri += 1
        End While
        Return testo.ToString()
    End Function

    Private Function Porzione(testo As String, da As Integer, a As Integer) As String
        If String.IsNullOrEmpty(testo) Then Return ""
        If da < 0 Then da = 0
        If a < 0 Then a = testo.Length - 1
        If da > a Then
            Dim scambio As Integer = da
            da = a
            a = scambio
        End If
        If da >= testo.Length Then Return ""
        If a >= testo.Length Then a = testo.Length - 1
        Return testo.Substring(da, a - da + 1)
    End Function

    Private Sub AccodaPezzo(testo As StringBuilder, pezzo As String)
        If String.IsNullOrWhiteSpace(pezzo) Then Return
        If testo.Length > 0 Then testo.Append(vbLf)
        testo.Append(pezzo)
    End Sub

    Private Function SembraCorpo(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing OrElse Stesso(elemento, _radice) Then Return False
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            Select Case ruolo
                Case "row", "gridcell", "listitem", "option", "menu", "menuitem", "toolbar", "list", "listbox", "treegrid"
                    Return False
            End Select
            If ruolo = "article" Then Return True
            If ruolo = "document" Then Return LunghezzaTesto(elemento) >= 20
            If ruolo = "region" OrElse ruolo = "main" Then
                If NomeDaMessaggio(_descrittore.Nome(elemento)) Then Return True
                Return LunghezzaTesto(elemento) >= 40
            End If
            Dim soglia As Integer = If(DentroElenco(elemento), 180, 60)
            Return LunghezzaTesto(elemento) >= soglia
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.SembraCorpo", exAtteso)
            Return False
        End Try
    End Function

    Private Function LunghezzaTesto(elemento As IUIAutomationElement) As Integer
        Dim nome As String = _descrittore.Nome(elemento)
        Dim n As Integer = If(nome Is Nothing, 0, nome.Length)
        If n >= 80 Then Return n
        Return Math.Max(n, LunghezzaPattern(elemento))
    End Function

    Private Function TestoProprio(nodo As IUIAutomationElement, contenitore As IUIAutomationElement) As Boolean
        Try
            Dim testo As String = Estratto(nodo)
            If testo.Length < 20 Then Return False
            Dim nome As String = _descrittore.Nome(contenitore)
            If nome <> "" AndAlso nome.IndexOf(testo, StringComparison.CurrentCultureIgnoreCase) >= 0 Then Return False
            If testo.Length >= 160 Then Return True
            Return nome = "" OrElse testo.Length > nome.Length + 30
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TestoProprio", exAtteso)
            Return False
        End Try
    End Function

    Private Function Estratto(elemento As IUIAutomationElement) As String
        Dim nome As String = _descrittore.Nome(elemento)
        If nome IsNot Nothing AndAlso nome.Length >= 20 Then
            If nome.Length > 180 Then Return nome.Substring(0, 180)
            Return nome
        End If
        Return TestoDalPattern(elemento, 180)
    End Function

    Private Function RigaAperta(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            If ruolo <> "row" AndAlso ruolo <> "listitem" AndAlso ruolo <> "article" Then Return False
            Dim stato As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaStatoEspansione, -1)
            Return stato = 1 OrElse stato = 2
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RigaAperta", exAtteso)
            Return False
        End Try
    End Function

    Private Function ContenitoreMessaggio(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing Then Return Nothing
            If RigaAperta(elemento) OrElse HaCorpoEspanso(elemento) Then Return elemento
            Dim corrente As IUIAutomationElement = GenitoreControllo(elemento)
            For salto As Integer = 0 To 8
                If corrente Is Nothing OrElse Stesso(corrente, _radice) Then Return Nothing
                If RigaAperta(corrente) Then Return corrente
                corrente = GenitoreControllo(corrente)
            Next
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ContenitoreMessaggio", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function VoceDiElenco(elemento As IUIAutomationElement) As Boolean
        Select Case _descrittore.RuoloAria(elemento)
            Case "row", "listitem", "option", "gridcell", "treeitem"
                Return True
        End Select
        Return False
    End Function

    Private Function HaCorpoEspanso(elemento As IUIAutomationElement) As Boolean
        Try
            If elemento Is Nothing Then Return False
            If RigaAperta(elemento) Then Return True
            If _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaStatoEspansione, -1) = 0 Then Return False
            Dim chiaveMemo As String = Chiave(elemento)
            Dim noto As Boolean
            If chiaveMemo <> "" AndAlso _corpoNoto.TryGetValue(chiaveMemo, noto) Then Return noto
            Dim esito As Boolean = CercaCorpo(elemento)
            If esito AndAlso chiaveMemo <> "" Then
                If _corpoNoto.Count > 400 Then _corpoNoto.Clear()
                _corpoNoto(chiaveMemo) = True
            End If
            Return esito
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.HaCorpoEspanso", exAtteso)
            Return False
        End Try
    End Function

    Private Function CercaCorpo(origine As IUIAutomationElement) As Boolean
        If origine Is Nothing OrElse _walkerAntenati Is Nothing Then Return False
        If _descrittore.LeggiIntero(origine, DescrittoreElementi.ProprietaStatoEspansione, -1) = 0 Then Return False
        Dim pila As New Stack(Of IUIAutomationElement)()
        pila.Push(origine)
        Dim visitati As Integer = 0
        While pila.Count > 0 AndAlso visitati < 48
            Dim nodo As IUIAutomationElement = pila.Pop()
            visitati += 1
            If nodo IsNot Nothing AndAlso Not Stesso(nodo, origine) Then
                If SembraCorpo(nodo) OrElse TestoProprio(nodo, origine) Then Return True
            End If
            If visitati > 40 OrElse _walkerAntenati Is Nothing Then Continue While
            Dim figlio As IUIAutomationElement = Nothing
            Try
                figlio = _walkerAntenati.GetFirstChildElementBuildCache(nodo, _cache)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.CercaCorpo", exAtteso)
                figlio = Nothing
            End Try
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 16
                pila.Push(figlio)
                Try
                    figlio = _walkerAntenati.GetNextSiblingElementBuildCache(figlio, _cache)
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("NavigazioneWeb.CercaCorpo", exAtteso2)
                    figlio = Nothing
                End Try
                giri += 1
            End While
        End While
        Return False
    End Function

    Private Function TrovaCorpoDentro(origine As IUIAutomationElement) As IUIAutomationElement
        If origine Is Nothing OrElse _walkerAntenati Is Nothing Then Return Nothing
        Dim migliore As IUIAutomationElement = Nothing
        Dim meglio As Integer = 0
        Dim pila As New Stack(Of IUIAutomationElement)()
        pila.Push(origine)
        Dim visitati As Integer = 0
        While pila.Count > 0 AndAlso visitati < 64
            Dim nodo As IUIAutomationElement = pila.Pop()
            visitati += 1
            If nodo IsNot Nothing AndAlso Not Stesso(nodo, origine) AndAlso Not Stesso(nodo, _radice) Then
                Dim punti As Integer = 0
                If SembraCorpo(nodo) Then punti = 1000 + Math.Min(LunghezzaTesto(nodo), 2000)
                If TestoProprio(nodo, origine) Then punti = Math.Max(punti, 600 + Math.Min(LunghezzaTesto(nodo), 2000))
                If punti > meglio Then
                    meglio = punti
                    migliore = nodo
                End If
            End If
            Dim figlio As IUIAutomationElement = Nothing
            Try
                figlio = _walkerAntenati.GetFirstChildElementBuildCache(nodo, _cache)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.TrovaCorpoDentro", exAtteso)
                figlio = Nothing
            End Try
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 16
                pila.Push(figlio)
                Try
                    figlio = _walkerAntenati.GetNextSiblingElementBuildCache(figlio, _cache)
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("NavigazioneWeb.TrovaCorpoDentro", exAtteso2)
                    figlio = Nothing
                End Try
                giri += 1
            End While
        End While
        If meglio < 600 Then Return Nothing
        Return migliore
    End Function

    Private Function CercaMailSingola() As IUIAutomationElement
        Dim adesso As Integer = Environment.TickCount
        If String.Equals(_mailSingolaChiave, _chiaveRadice, StringComparison.Ordinal) AndAlso adesso >= _mailSingolaTick AndAlso adesso - _mailSingolaTick < 700 Then
            Return _mailSingola
        End If
        Dim trovato As IUIAutomationElement = ScansionaMailSingola()
        _mailSingola = trovato
        _mailSingolaChiave = If(_chiaveRadice, "")
        _mailSingolaTick = adesso
        Return trovato
    End Function

    Private Function ScansionaMailSingola() As IUIAutomationElement
        If _radice Is Nothing OrElse _walkerAntenati Is Nothing Then Return Nothing
        Dim orologio As Stopwatch = Stopwatch.StartNew()
        Dim pila As New Stack(Of IUIAutomationElement)()
        pila.Push(_radice)
        Dim visitati As Integer = 0
        Dim chiuseVisibili As Integer = 0
        Dim aperta As IUIAutomationElement = Nothing
        Dim corpo As IUIAutomationElement = Nothing
        While pila.Count > 0 AndAlso visitati < 80 AndAlso orologio.ElapsedMilliseconds < 40
            Dim nodo As IUIAutomationElement = pila.Pop()
            visitati += 1
            If nodo Is Nothing Then Continue While
            If Not Stesso(nodo, _radice) Then
                Dim ruolo As String = _descrittore.RuoloAria(nodo)
                If ruolo = "row" OrElse ruolo = "listitem" Then
                    Dim stato As Integer = _descrittore.LeggiIntero(nodo, DescrittoreElementi.ProprietaStatoEspansione, -1)
                    If stato = 0 Then
                        If Not _descrittore.FuoriSchermo(nodo) Then
                            chiuseVisibili += 1
                            If chiuseVisibili > 2 Then Return Nothing
                        End If
                        Continue While
                    End If
                    If (stato = 1 OrElse stato = 2) AndAlso aperta Is Nothing Then aperta = nodo
                ElseIf corpo Is Nothing AndAlso Not _descrittore.FuoriSchermo(nodo) AndAlso NomeDaMessaggio(_descrittore.Nome(nodo)) AndAlso SembraCorpo(nodo) Then
                    corpo = nodo
                End If
            End If
            If visitati > 70 Then Continue While
            Dim figlio As IUIAutomationElement = Nothing
            Try
                figlio = _walkerAntenati.GetFirstChildElementBuildCache(nodo, _cache)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.ScansionaMailSingola", exAtteso)
                figlio = Nothing
            End Try
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 14
                pila.Push(figlio)
                Try
                    figlio = _walkerAntenati.GetNextSiblingElementBuildCache(figlio, _cache)
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("NavigazioneWeb.ScansionaMailSingola", exAtteso2)
                    figlio = Nothing
                End Try
                giri += 1
            End While
        End While
        If chiuseVisibili > 2 Then Return Nothing
        If aperta IsNot Nothing AndAlso chiuseVisibili <= 1 Then Return aperta
        If aperta Is Nothing AndAlso chiuseVisibili = 0 Then Return corpo
        Return Nothing
    End Function

    Private Function DaLeggereAncheSeNascosto(nodo As IUIAutomationElement) As Boolean
        Try
            If nodo Is Nothing OrElse Stesso(nodo, _radice) Then Return False
            Dim chiaveMemo As String = Chiave(nodo)
            Dim noto As Boolean
            If chiaveMemo <> "" AndAlso _nascostoNoto.TryGetValue(chiaveMemo, noto) Then Return noto
            Dim esito As Boolean = CalcolaNascosto(nodo)
            If chiaveMemo <> "" Then
                If _nascostoNoto.Count > 800 Then _nascostoNoto.Clear()
                _nascostoNoto(chiaveMemo) = esito
            End If
            Return esito
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.DaLeggereAncheSeNascosto", exAtteso)
            Return False
        End Try
    End Function

    Private Function CalcolaNascosto(nodo As IUIAutomationElement) As Boolean
        Dim tipo As Integer = _descrittore.TipoControllo(nodo)
        Select Case tipo
            Case DescrittoreElementi.ControlloGruppo, DescrittoreElementi.ControlloPersonalizzato, DescrittoreElementi.ControlloDocumento, DescrittoreElementi.ControlloRiquadro, DescrittoreElementi.ControlloTesto, DescrittoreElementi.ControlloModifica
            Case Else
                Return False
        End Select
        Dim ruolo As String = _descrittore.RuoloAria(nodo)
        Select Case ruolo
            Case "row", "gridcell", "listitem", "option", "menu", "menuitem", "toolbar", "menubar", "list", "listbox", "treegrid"
                Return False
        End Select
        If ruolo = "article" Then Return True
        If LunghezzaTesto(nodo) >= 40 Then Return True
        If _walker IsNot Nothing Then
            Dim visto As IUIAutomationElement = Nothing
            Try
                visto = _walker.GetFirstChildElementBuildCache(nodo, _cache)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.CalcolaNascosto", exAtteso)
                visto = Nothing
            End Try
            If visto IsNot Nothing Then Return False
        End If
        Return TestoNeiControlli(nodo)
    End Function

    Private Function TestoNeiControlli(origine As IUIAutomationElement) As Boolean
        If origine Is Nothing OrElse _walkerAntenati Is Nothing Then Return False
        Dim pila As New Stack(Of IUIAutomationElement)()
        Dim livelli As New Stack(Of Integer)()
        pila.Push(origine)
        livelli.Push(0)
        Dim visitati As Integer = 0
        While pila.Count > 0 AndAlso visitati < 8
            Dim nodo As IUIAutomationElement = pila.Pop()
            Dim livello As Integer = livelli.Pop()
            visitati += 1
            If livello > 0 AndAlso LunghezzaTesto(nodo) >= 24 Then Return True
            If livello >= 3 Then Continue While
            Dim figlio As IUIAutomationElement = Nothing
            Try
                figlio = _walkerAntenati.GetFirstChildElementBuildCache(nodo, _cache)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.TestoNeiControlli", exAtteso)
                figlio = Nothing
            End Try
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 6
                pila.Push(figlio)
                livelli.Push(livello + 1)
                Try
                    figlio = _walkerAntenati.GetNextSiblingElementBuildCache(figlio, _cache)
                Catch exAtteso2 As Exception
                    RegistroErrori.Scrivi("NavigazioneWeb.TestoNeiControlli", exAtteso2)
                    figlio = Nothing
                End Try
                giri += 1
            End While
        End While
        Return False
    End Function

    Private Function FigliSenzaContenuto(elemento As IUIAutomationElement) As Boolean
        Try
            Dim figlio As IUIAutomationElement = PrimoFiglio(elemento)
            If figlio Is Nothing Then Return True
            Dim giri As Integer = 0
            While figlio IsNot Nothing AndAlso giri < 6
                Dim ruolo As String = _descrittore.RuoloAria(figlio)
                Select Case ruolo
                    Case "link", "button", "textbox", "searchbox", "heading", "cell", "gridcell", "listitem", "checkbox", "radio", "combobox"
                        Return False
                End Select
                If _descrittore.Nome(figlio) <> "" AndAlso ruolo <> "presentation" AndAlso ruolo <> "none" Then Return False
                figlio = Fratello(figlio)
                giri += 1
            End While
            Return True
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.FigliSenzaContenuto", exAtteso)
            Return False
        End Try
    End Function

    Private Function ServeAlberoControllo(elemento As IUIAutomationElement) As Boolean
        Try
            If _walkerAntenati Is Nothing OrElse elemento Is Nothing Then Return False
            Select Case _descrittore.RuoloAria(elemento)
                Case "application", "document", "main", "region", "article", "group", "presentation"
                    Return True
            End Select
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloGruppo, DescrittoreElementi.ControlloPersonalizzato, DescrittoreElementi.ControlloDocumento, DescrittoreElementi.ControlloRiquadro, DescrittoreElementi.ControlloFinestra
                    Return True
            End Select
            Return False
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ServeAlberoControllo", exAtteso)
            Return False
        End Try
    End Function

    Private Function TestoDalPattern(elemento As IUIAutomationElement, massimo As Integer) As String
        Try
            If elemento Is Nothing OrElse massimo < 1 Then Return ""
            If _chromium Then Return ""
            Dim pattern As IUIAutomationTextPattern = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto), IUIAutomationTextPattern)
            If pattern Is Nothing Then pattern = TryCast(elemento.GetCurrentPattern(DescrittoreElementi.SchemaTesto2), IUIAutomationTextPattern)
            If pattern Is Nothing OrElse pattern.DocumentRange Is Nothing Then Return ""
            Dim testo As String = pattern.DocumentRange.GetText(massimo)
            If testo Is Nothing Then Return ""
            testo = testo.Replace(vbCr, " ").Replace(vbLf, " ").Trim()
            If testo.Length > massimo Then testo = testo.Substring(0, massimo)
            Return testo
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.TestoDalPattern", exAtteso)
            Return ""
        End Try
    End Function

    Private Function PrimoFiglio(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walker Is Nothing Then Return Nothing
            Dim figlio As IUIAutomationElement = _walker.GetFirstChildElementBuildCache(elemento, _cache)
            If Not _web OrElse Not _usaVistaContenuto OrElse _walkerAntenati Is Nothing Then
                If figlio IsNot Nothing Then Return figlio
                If Not ServeAlberoControllo(elemento) Then Return Nothing
                Return _walkerAntenati.GetFirstChildElementBuildCache(elemento, _cache)
            End If
            Dim controllo As IUIAutomationElement = _walkerAntenati.GetFirstChildElementBuildCache(elemento, _cache)
            If controllo IsNot Nothing AndAlso (figlio Is Nothing OrElse Not Stesso(controllo, figlio)) AndAlso DaLeggereAncheSeNascosto(controllo) Then Return controllo
            If figlio IsNot Nothing Then Return figlio
            If controllo IsNot Nothing AndAlso ServeAlberoControllo(elemento) Then Return controllo
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.PrimoFiglio", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function UltimoFiglio(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walker Is Nothing Then Return Nothing
            Dim figlio As IUIAutomationElement = _walker.GetLastChildElementBuildCache(elemento, _cache)
            If Not _web OrElse Not _usaVistaContenuto OrElse _walkerAntenati Is Nothing Then
                If figlio IsNot Nothing Then Return figlio
                If Not ServeAlberoControllo(elemento) Then Return Nothing
                Return _walkerAntenati.GetLastChildElementBuildCache(elemento, _cache)
            End If
            Dim controllo As IUIAutomationElement = _walkerAntenati.GetLastChildElementBuildCache(elemento, _cache)
            If controllo IsNot Nothing AndAlso (figlio Is Nothing OrElse Not Stesso(controllo, figlio)) AndAlso DaLeggereAncheSeNascosto(controllo) Then Return controllo
            If figlio IsNot Nothing Then Return figlio
            If controllo IsNot Nothing AndAlso ServeAlberoControllo(elemento) Then Return controllo
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.UltimoFiglio", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function Fratello(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walker Is Nothing Then Return Nothing
            Dim contenuto As IUIAutomationElement = Nothing
            Try
                contenuto = _walker.GetNextSiblingElementBuildCache(elemento, _cache)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.Fratello", exAtteso)
                contenuto = Nothing
            End Try
            If contenuto IsNot Nothing AndAlso Stesso(contenuto, elemento) Then contenuto = Nothing
            Dim nascosto As IUIAutomationElement = FratelloNascosto(elemento, contenuto, True)
            If nascosto IsNot Nothing Then Return nascosto
            If contenuto IsNot Nothing Then Return contenuto
            If _walkerAntenati Is Nothing OrElse _walkerAntenati Is _walker Then Return Nothing
            If Not ServeAlberoControllo(GenitoreControllo(elemento)) Then Return Nothing
            Dim prossimo As IUIAutomationElement = _walkerAntenati.GetNextSiblingElementBuildCache(elemento, _cache)
            If prossimo Is Nothing OrElse Stesso(prossimo, elemento) Then Return Nothing
            Return prossimo
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.Fratello", exAtteso2)
            Return Nothing
        End Try
    End Function

    Private Function FratelloPrecedente(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walker Is Nothing Then Return Nothing
            Dim contenuto As IUIAutomationElement = Nothing
            Try
                contenuto = _walker.GetPreviousSiblingElementBuildCache(elemento, _cache)
            Catch exAtteso As Exception
                RegistroErrori.Scrivi("NavigazioneWeb.FratelloPrecedente", exAtteso)
                contenuto = Nothing
            End Try
            If contenuto IsNot Nothing AndAlso Stesso(contenuto, elemento) Then contenuto = Nothing
            Dim nascosto As IUIAutomationElement = FratelloNascosto(elemento, contenuto, False)
            If nascosto IsNot Nothing Then Return nascosto
            If contenuto IsNot Nothing Then Return contenuto
            If _walkerAntenati Is Nothing OrElse _walkerAntenati Is _walker Then Return Nothing
            If Not ServeAlberoControllo(GenitoreControllo(elemento)) Then Return Nothing
            Dim precedente As IUIAutomationElement = _walkerAntenati.GetPreviousSiblingElementBuildCache(elemento, _cache)
            If precedente Is Nothing OrElse Stesso(precedente, elemento) Then Return Nothing
            Return precedente
        Catch exAtteso2 As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.FratelloPrecedente", exAtteso2)
            Return Nothing
        End Try
    End Function

    Private Function FratelloNascosto(elemento As IUIAutomationElement, contenuto As IUIAutomationElement, avanti As Boolean) As IUIAutomationElement
        Try
            If Not _web OrElse Not _usaVistaContenuto OrElse _walkerAntenati Is Nothing Then Return Nothing
            Dim genitore As IUIAutomationElement = GenitoreControllo(elemento)
            If Not ServeAlberoControllo(genitore) Then Return Nothing
            Dim controllo As IUIAutomationElement
            If avanti Then
                controllo = _walkerAntenati.GetNextSiblingElementBuildCache(elemento, _cache)
            Else
                controllo = _walkerAntenati.GetPreviousSiblingElementBuildCache(elemento, _cache)
            End If
            If controllo Is Nothing OrElse Stesso(controllo, elemento) Then Return Nothing
            If contenuto IsNot Nothing AndAlso Stesso(controllo, contenuto) Then Return Nothing
            If DaLeggereAncheSeNascosto(controllo) Then Return controllo
            Return Nothing
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.FratelloNascosto", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function GenitoreNavigazione(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walker Is Nothing OrElse Stesso(elemento, _radice) Then Return Nothing
            If _web AndAlso _usaVistaContenuto AndAlso _walkerAntenati IsNot Nothing AndAlso NodoNascosto(elemento) Then
                Return _walkerAntenati.GetParentElementBuildCache(elemento, _cache)
            End If
            Dim genitore As IUIAutomationElement = _walker.GetParentElementBuildCache(elemento, _cache)
            If genitore IsNot Nothing Then Return genitore
            If _walkerAntenati Is Nothing Then Return Nothing
            Return _walkerAntenati.GetParentElementBuildCache(elemento, _cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.GenitoreNavigazione", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function NodoNascosto(elemento As IUIAutomationElement) As Boolean
        Dim chiaveMemo As String = Chiave(elemento)
        Dim noto As Boolean = False
        Return chiaveMemo <> "" AndAlso _nascostoNoto.TryGetValue(chiaveMemo, noto) AndAlso noto
    End Function

    Private Function GenitoreControllo(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _walkerAntenati Is Nothing OrElse Stesso(elemento, _radice) Then Return Nothing
            Return _walkerAntenati.GetParentElementBuildCache(elemento, _cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.GenitoreControllo", exAtteso)
            Return Nothing
        End Try
    End Function

    Private Function ConCache(elemento As IUIAutomationElement) As IUIAutomationElement
        Try
            If elemento Is Nothing OrElse _cache Is Nothing Then Return elemento
            Return elemento.BuildUpdatedCache(_cache)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ConCache", exAtteso)
            Return elemento
        End Try
    End Function

    Private Function Stesso(primo As IUIAutomationElement, secondo As IUIAutomationElement) As Boolean
        Try
            If primo Is Nothing OrElse secondo Is Nothing OrElse _automazione Is Nothing Then Return False
            Return _automazione.CompareElements(primo, secondo) <> 0
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.Stesso", exAtteso)
            Return False
        End Try
    End Function

    ' Chrome e Firefox, aprendo un link nella stessa scheda, possono tenere lo stesso identificatore per il documento: da solo non dice
    ' che la pagina è un'altra. Si guarda l'indirizzo (senza parametri e senza #), così le applicazioni a pagina unica non fanno ricostruire.
    Private Shared Function PaginaDaUrl(url As String) As String
        If String.IsNullOrWhiteSpace(url) Then Return ""
        Dim indirizzo As Uri = Nothing
        If Uri.TryCreate(url.Trim(), UriKind.Absolute, indirizzo) Then Return indirizzo.Scheme & "://" & indirizzo.Host & indirizzo.AbsolutePath
        Dim fine As Integer = url.IndexOfAny(New Char() {"#"c, "?"c})
        If fine >= 0 Then url = url.Substring(0, fine)
        Return url.Trim()
    End Function

    ' La posizione ricordata vale per una pagina precisa: identificatore del documento più indirizzo completo (senza #).
    Private Function UrlMemoriaDi(radice As IUIAutomationElement) As String
        Try
            If radice Is Nothing OrElse _descrittore Is Nothing Then Return ""
            If _descrittore.TipoControllo(radice) <> DescrittoreElementi.ControlloDocumento Then Return ""
            Dim url As String = If(TryCast(radice.GetCurrentPropertyValue(DescrittoreElementi.ProprietaValore), String), "").Trim()
            Dim frammento As Integer = url.IndexOf("#"c)
            If frammento >= 0 Then url = url.Substring(0, frammento)
            Return url
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.UrlMemoriaDi", ex)
            Return ""
        End Try
    End Function

    Private Function ChiaveMemoria() As String
        If _chiaveRadice = "" Then Return ""
        Return _chiaveRadice & "|" & _urlMemoria
    End Function

    Private Function PaginaDi(radice As IUIAutomationElement) As String
        Try
            If radice Is Nothing OrElse _descrittore Is Nothing Then Return ""
            If _descrittore.TipoControllo(radice) <> DescrittoreElementi.ControlloDocumento Then Return ""
            Return PaginaDaUrl(TryCast(radice.GetCurrentPropertyValue(DescrittoreElementi.ProprietaValore), String))
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.PaginaDi", ex)
            Return ""
        End Try
    End Function

    Public Function PaginaCambiata(radice As IUIAutomationElement) As Boolean
        Try
            If _paginaRadice = "" Then Return False
            Dim pagina As String = PaginaDi(radice)
            Return pagina <> "" AndAlso Not String.Equals(pagina, _paginaRadice, StringComparison.Ordinal)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.PaginaCambiata", ex)
            Return False
        End Try
    End Function

    ' Chiamata prima di ogni spostamento: se nella stessa scheda è arrivata un'altra pagina, l'indice si rifà subito invece di aspettare.
    Public Function RicostruisciSePaginaCambiata() As Boolean
        Try
            If _radice Is Nothing OrElse Not _web OrElse _riquadro Then Return False
            Dim adesso As Integer = Environment.TickCount
            If adesso - _paginaControlloTick >= 0 AndAlso adesso - _paginaControlloTick < 250 Then Return False
            _paginaControlloTick = adesso
            If Not PaginaCambiata(_radice) Then Return False
            RicordaPosizione(Nothing, True)
            Costruisci(_radice, _finestra, True, _riquadro)
            Return True
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RicostruisciSePaginaCambiata", ex)
            Return False
        End Try
    End Function

    Private Function Chiave(elemento As IUIAutomationElement) As String
        Try
            If elemento Is Nothing Then Return ""
            Dim id As Array = elemento.GetRuntimeId()
            If id Is Nothing OrElse id.Length = 0 Then Return ""
            Dim parti(id.Length - 1) As String
            For indice As Integer = 0 To id.Length - 1
                parti(indice) = Convert.ToString(id.GetValue(indice))
            Next
            Return String.Join("-", parti)
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.Chiave", exAtteso)
            Return ""
        End Try
    End Function

    Public Function EFinestraWeb(finestra As IntPtr) As Boolean
        Try
            If finestra = IntPtr.Zero OrElse _automazione Is Nothing Then Return False
            Dim adesso As Integer = Environment.TickCount
            Dim noto As KeyValuePair(Of Integer, Boolean)
            SyncLock _finestreWeb
                If _finestreWeb.TryGetValue(finestra, noto) AndAlso adesso - noto.Key < 20000 Then Return noto.Value
            End SyncLock
            Dim contesto As ContestoNavigazione = ContestoFinestra(finestra)
            Dim web As Boolean = contesto IsNot Nothing AndAlso contesto.EWeb
            SyncLock _finestreWeb
                If _finestreWeb.Count > 60 Then _finestreWeb.Clear()
                _finestreWeb(finestra) = New KeyValuePair(Of Integer, Boolean)(adesso, web)
            End SyncLock
            Return web
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.EFinestraWeb: " & ex.Message)
            Return False
        End Try
    End Function

    Private Function FraseAssente(tipo As String, livello As Integer) As String
        If _indiceCompleto Then
            For Each voce As VoceAlbero In _voci
                If Corrisponde(voce, tipo, livello) Then Return "non ci sono altri"
            Next
        End If
        Dim nome As String = NomeTipo(tipo, livello)
        Select Case tipo
            Case "casella", "opzione", "tabella", "grafica", "regione", "voce", "combinata", "titolo", "citazione"
                Return "nessuna " & nome
            Case Else
                Return "nessun " & nome
        End Select
    End Function

    Private Function NomeTipo(tipo As String, livello As Integer) As String
        Select Case tipo
            Case "titolo"
                If livello > 0 Then Return "intestazione di livello " & livello.ToString()
                Return "intestazione"
            Case "collegamento"
                Return "link"
            Case "visitato"
                Return "link visitato"
            Case "nonvisitato"
                Return "link non visitato"
            Case "citazione"
                Return "citazione"
            Case "separatore"
                Return "separatore"
            Case "riquadro"
                Return "riquadro"
            Case "pulsante"
                Return "pulsante"
            Case "modifica"
                Return "campo di modifica"
            Case "campo"
                Return "campo"
            Case "casella"
                Return "casella"
            Case "opzione"
                Return "opzione"
            Case "combinata"
                Return "casella combinata"
            Case "elenco"
                Return "elenco"
            Case "voce"
                Return "voce"
            Case "tabella"
                Return "tabella"
            Case "cella"
                Return "cella"
            Case "articolo"
                Return "articolo"
            Case "intestazioneColonna"
                Return "intestazione di colonna"
            Case "intestazioneRiga"
                Return "intestazione di riga"
            Case "grafica"
                Return "immagine"
            Case "regione"
                Return "regione"
            Case "testo"
                Return "testo"
            Case "oggetto"
                Return "oggetto incorporato"
            Case "ortografia"
                Return "errore di ortografia"
            Case "paragrafo"
                Return "paragrafo"
            Case Else
                Return "elemento"
        End Select
    End Function

    Private Function TipoDaTasto(codice As Integer, ByRef livello As Integer) As String
        livello = 0
        Select Case codice
            Case &H48
                Return "titolo"
            Case &H55
                Return "nonvisitato"
            Case &H56
                Return "visitato"
            Case &H51
                Return "citazione"
            Case &H53
                Return "separatore"
            Case &H4D
                Return "riquadro"
            Case &H31 To &H36
                livello = codice - &H30
                Return "titolo"
            Case &H4B
                Return "collegamento"
            Case &H42
                Return "pulsante"
            Case &H45
                Return "modifica"
            Case &H46
                Return "campo"
            Case &H58
                Return "casella"
            Case &H52
                Return "opzione"
            Case &H43
                Return "combinata"
            Case &H4C
                Return "elenco"
            Case &H49
                Return "voce"
            Case &H54
                Return "tabella"
            Case &H47
                Return "grafica"
            Case &H44
                Return "regione"
            Case &H4E
                Return "testo"
            Case &H41
                Return "articolo"
            Case &H4F
                Return "oggetto"
            Case &H57
                Return "ortografia"
            Case &H50
                Return "paragrafo"
        End Select
        Return ""
    End Function

    Private Shared Function TrascorsoAiuto(tick As Integer) As Long
        Dim trascorso As Long = CLng(Environment.TickCount) - CLng(tick)
        If trascorso < 0L Then trascorso += 4294967296L
        Return trascorso
    End Function

    Private Sub AzzeraAiuto()
        Try
            _aiutoAttivo = False
            _aiutoFinestra = IntPtr.Zero
            _aiutoPrefisso = Nothing
            _aiutoSegno = 0
            _aiutoErrori = 0
            _aiutoFallimentiAggiornamento = 0
            _aiutoSoloRicerca = False
            _aiutoDaScartare = False
            _aiutoRifiutato = False
            _aiutoVerificato = False
            _aiutoRichiesta = Nothing
            _aiutoUltimaRichiesta = Integer.MinValue
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AzzeraAiuto: " & ex.Message)
        End Try
    End Sub

    Private Sub AvviaAiuto()
        Try
            If Not AiutoInProcesso.Abilitato OrElse Not _web OrElse _radice Is Nothing OrElse _finestra = IntPtr.Zero Then Return
            If _aiutoRifiutato OrElse _aiutoDaScartare OrElse _aiutoRichiesta IsNot Nothing Then Return
            ' Mentre la libreria costruisce l'albero il browser sta fermo: su pagine enormi e che cambiano di continuo si lascia un'attesa
            ' proporzionale al tempo che la richiesta precedente ha preso, perché il browser non resti bloccato a lungo.
            Dim attesaMinima As Long = Math.Min(15000L, Math.Max(IntervalloMinimoAiutoMs, 6L * _aiutoUltimaDurata))
            If TrascorsoAiuto(_aiutoUltimaRichiesta) < attesaMinima Then Return
            Dim identita As Array = _radice.GetRuntimeId()
            If identita Is Nothing OrElse identita.Length < 2 Then
                _aiutoRifiutato = True
                Return
            End If
            Dim prefisso(identita.Length - 2) As Integer
            For indice As Integer = 0 To identita.Length - 2
                prefisso(indice) = Convert.ToInt32(identita.GetValue(indice))
            Next
            Dim ultimo As Integer = Convert.ToInt32(identita.GetValue(identita.Length - 1))
            If ultimo = 0 OrElse ultimo = Integer.MinValue Then
                _aiutoRifiutato = True
                Return
            End If
            Dim richiesta As New RichiestaAiuto(Me, _finestra, _chiaveRadice, prefisso, ultimo)
            _aiutoRichiesta = richiesta
            _aiutoUltimaRichiesta = Environment.TickCount
            If Not System.Threading.ThreadPool.QueueUserWorkItem(AddressOf richiesta.Esegui) Then
                _aiutoRichiesta = Nothing
                _aiutoRifiutato = True
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.AvviaAiuto", ex)
            _aiutoRichiesta = Nothing
            _aiutoRifiutato = True
        End Try
    End Sub

    Private Function GestisciAiuto() As Boolean
        Try
            If _aiutoDaScartare Then ScartaAiuto()
            Dim richiesta As RichiestaAiuto = _aiutoRichiesta
            If richiesta Is Nothing Then Return False
            If Not richiesta.Pronta Then
                If TrascorsoAiuto(richiesta.Inizio) < AttesaAiutoMs Then Return True
                _aiutoRichiesta = Nothing
                FallimentoAiuto()
                Return False
            End If
            _aiutoRichiesta = Nothing
            _aiutoUltimaDurata = richiesta.Durata
            Dim stessaRadice As Boolean = String.Equals(richiesta.ChiaveRadice, _chiaveRadice, StringComparison.Ordinal)
            If richiesta.Fallita OrElse richiesta.Voci Is Nothing OrElse richiesta.Mappa Is Nothing OrElse Not stessaRadice Then
                If stessaRadice Then FallimentoAiuto()
                Return False
            End If
            InstallaAiuto(richiesta)
            Return True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.GestisciAiuto: " & ex.Message)
            _aiutoRichiesta = Nothing
            Return False
        End Try
    End Function

    Private Sub FallimentoAiuto()
        Try
            If _aiutoAttivo Then
                _aiutoFallimentiAggiornamento += 1
                _indiceCompletoTick = Environment.TickCount
                If _aiutoFallimentiAggiornamento >= 2 Then _aiutoDaScartare = True
            Else
                _aiutoRifiutato = True
            End If
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.FallimentoAiuto: " & ex.Message)
        End Try
    End Sub

    Private Sub InstallaAiuto(richiesta As RichiestaAiuto)
        Try
            _voci = richiesta.Voci
            _perIdentita = richiesta.Mappa
            _vociNuove = Nothing
            _perIdentitaNuove = Nothing
            _cursoreIndice = Nothing
            _bufferDaRicostruire = False
            _indiceTroncato = richiesta.Troncato
            _vociElencoNote.Clear()
            _aiutoAttivo = True
            _aiutoFinestra = richiesta.Finestra
            _aiutoPrefisso = richiesta.Prefisso
            _aiutoSegno = richiesta.Segno
            _aiutoFallimentiAggiornamento = 0
            ChiudiIndice()
            If Not _aiutoVerificato Then VerificaAiuto()
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.InstallaAiuto: " & ex.Message)
        End Try
    End Sub

    Private Sub VerificaAiuto()
        Try
            _aiutoVerificato = True
            Dim totale As Integer = _voci.Count
            If totale = 0 Then Return
            Dim provate As Integer = 0
            Dim riuscite As Integer = 0
            For passo As Integer = 0 To 2
                Dim indice As Integer = CInt((CLng(totale) * (passo * 2 + 1)) \ 6)
                If indice < 0 OrElse indice >= totale Then Continue For
                provate += 1
                If _voci(indice).Elemento IsNot Nothing Then riuscite += 1
            Next
            If provate > 0 AndAlso riuscite = 0 Then
                _aiutoDaScartare = True
                _bufferDaRicostruire = True
            End If
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.VerificaAiuto: " & ex.Message)
        End Try
    End Sub

    Private Sub ScartaAiuto()
        Try
            _aiutoDaScartare = False
            If Not _aiutoAttivo Then Return
            _aiutoAttivo = False
            _aiutoRifiutato = True
            _aiutoRichiesta = Nothing
            _voci = New List(Of VoceAlbero)()
            _perIdentita = New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            _vociNuove = Nothing
            _perIdentitaNuove = Nothing
            _cursoreIndice = Nothing
            _indiceCompleto = False
            _bufferDaRicostruire = False
            _vociElencoNote.Clear()
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ScartaAiuto: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ChiaveDaAiuto(prefisso As Integer(), segno As Integer, id As Integer) As String
        Try
            If prefisso Is Nothing Then Return ""
            Dim valore As Long = CLng(segno) * CLng(id)
            If valore > Integer.MaxValue OrElse valore < Integer.MinValue Then Return ""
            Dim parti(prefisso.Length) As String
            For indice As Integer = 0 To prefisso.Length - 1
                parti(indice) = Convert.ToString(prefisso(indice))
            Next
            parti(prefisso.Length) = Convert.ToString(CInt(valore))
            Return String.Join("-", parti)
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.ChiaveDaAiuto", ex)
            Return ""
        End Try
    End Function

    Private Sub PreparaVociAiuto(richiesta As RichiestaAiuto, risultato As RisultatoIndiceAiuto)
        Try
            Dim voci As New List(Of VoceAlbero)(risultato.Voci.Count)
            Dim mappa As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            For Each sorgente As VoceIndiceAiuto In risultato.Voci
                Dim chiaveVoce As String = ChiaveDaAiuto(richiesta.Prefisso, richiesta.Segno, sorgente.Id)
                If chiaveVoce = "" OrElse mappa.ContainsKey(chiaveVoce) Then Continue For
                Dim voce As New VoceAlbero()
                voce.Proprietario = Me
                voce.IdAiuto = sorgente.Id
                voce.Chiave = chiaveVoce
                voce.Tipo = sorgente.Tipo
                voce.Livello = sorgente.Livello
                voce.Testo = sorgente.Testo
                voce.InElenco = sorgente.InElenco
                voce.Foglia = sorgente.Foglia
                voce.Visitato = sorgente.Visitato
                voce.Scrivibile = sorgente.Scrivibile
                voce.Impaginazione = sorgente.Impaginazione
                voce.Focalizzabile = sorgente.Focalizzabile
                voce.NomeRegione = sorgente.NomeRegione
                voce.NomeProprio = sorgente.Nome
                mappa(chiaveVoce) = voci.Count
                voci.Add(voce)
            Next
            richiesta.Mappa = mappa
            richiesta.Voci = voci
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.PreparaVociAiuto: " & ex.Message)
            richiesta.Fallita = True
        End Try
    End Sub

    Private Function RisolviVoce(voce As VoceAlbero) As IUIAutomationElement
        Try
            If voce Is Nothing OrElse voce.IdAiuto = 0 OrElse Not _aiutoAttivo OrElse _automazione Is Nothing OrElse _radice Is Nothing Then Return Nothing
            Dim diverso As Boolean = False
            If Not _aiutoSoloRicerca Then
                Dim diretto As IUIAutomationElement = AiutoInProcesso.Istanza.ElementoDaId(_automazione, _cache, _aiutoFinestra, voce.IdAiuto)
                If diretto IsNot Nothing Then
                    If String.Equals(Chiave(diretto), voce.Chiave, StringComparison.Ordinal) Then
                        _aiutoErrori = 0
                        Return diretto
                    End If
                    diverso = True
                End If
            End If
            Dim cercato As IUIAutomationElement = CercaPerChiave(voce)
            If cercato IsNot Nothing Then
                _aiutoErrori = 0
                If diverso Then _aiutoSoloRicerca = True
                Return cercato
            End If
            _aiutoErrori += 1
            If _aiutoErrori >= 4 Then
                _aiutoDaScartare = True
                _bufferDaRicostruire = True
            End If
            Return Nothing
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.RisolviVoce", ex)
            Return Nothing
        End Try
    End Function

    Private Function CercaPerChiave(voce As VoceAlbero) As IUIAutomationElement
        Try
            If voce Is Nothing OrElse _aiutoPrefisso Is Nothing OrElse _radice Is Nothing OrElse _automazione Is Nothing Then Return Nothing
            Dim valore As Long = CLng(_aiutoSegno) * CLng(voce.IdAiuto)
            If valore > Integer.MaxValue OrElse valore < Integer.MinValue Then Return Nothing
            Dim identita(_aiutoPrefisso.Length) As Integer
            Array.Copy(_aiutoPrefisso, identita, _aiutoPrefisso.Length)
            identita(_aiutoPrefisso.Length) = CInt(valore)
            Dim condizione As IUIAutomationCondition = _automazione.CreatePropertyCondition(30000, identita)
            Dim trovato As IUIAutomationElement = _radice.FindFirstBuildCache(TreeScope.TreeScope_Descendants, condizione, _cache)
            If trovato Is Nothing Then Return Nothing
            If Not String.Equals(Chiave(trovato), voce.Chiave, StringComparison.Ordinal) Then Return Nothing
            Return trovato
        Catch ex As Exception
            RegistroErrori.Scrivi("NavigazioneWeb.CercaPerChiave", ex)
            Return Nothing
        End Try
    End Function

    Private Function SaltaVoceIndice(voce As VoceAlbero, giaLetto As IUIAutomationElement) As Boolean
        Try
            If voce Is Nothing Then Return False
            If voce.IdAiuto <> 0 AndAlso voce.Foglia >= 0 Then
                Select Case voce.Tipo
                    Case "regione", "elenco", "tabella", "citazione", "riquadro"
                        Return True
                    Case "testo", "voce", "cella", "titolo"
                        If voce.Foglia = 0 Then Return True
                End Select
                If giaLetto Is Nothing Then Return False
                Return Discende(voce.Elemento, giaLetto)
            End If
            Return SaltaConFrecce(voce.Elemento, voce.Tipo) OrElse Discende(voce.Elemento, giaLetto)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.SaltaVoceIndice: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub RicominciaIndice()
        Try
            If _aiutoAttivo AndAlso Not _aiutoDaScartare Then
                AvviaAiuto()
                ' Se la richiesta non è partita solo perché è troppo presto, l'aggiornamento resta da fare e si riprova al prossimo giro.
                _bufferDaRicostruire = _aiutoRichiesta Is Nothing AndAlso Not _aiutoRifiutato AndAlso Not _aiutoDaScartare
                Return
            End If
            _bufferDaRicostruire = False
            _cursoreIndice = _radice
            If _indiceCompleto AndAlso _voci.Count > 0 Then
                _vociNuove = New List(Of VoceAlbero)()
                _perIdentitaNuove = New Dictionary(Of String, Integer)(StringComparer.Ordinal)
                Return
            End If
            _vociNuove = Nothing
            _perIdentitaNuove = Nothing
            _voci = New List(Of VoceAlbero)()
            _perIdentita = New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            _indiceCompleto = False
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RicominciaIndice: " & ex.Message)
        End Try
    End Sub

    Public Sub AssicuraBuffer(millisecondi As Integer)
        Try
            If _radice Is Nothing OrElse _walker Is Nothing Then Return
            RicostruisciSePaginaCambiata()
            RinfrescaSeVecchio()
            If _bufferDaRicostruire AndAlso _indiceCompleto AndAlso _vociNuove Is Nothing Then RicominciaIndice()
            If millisecondi < 40 Then millisecondi = 40
            Dim inizio As Integer = Environment.TickCount
            While (Not _indiceCompleto OrElse _vociNuove IsNot Nothing) AndAlso TrascorsoAiuto(inizio) < CLng(millisecondi)
                Indicizza(80)
                If _aiutoRichiesta IsNot Nothing Then System.Threading.Thread.Sleep(20)
            End While
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AssicuraBuffer: " & ex.Message)
        End Try
    End Sub

    Public Sub SegnaStrutturaObsoleta()
        Try
            If _radice Is Nothing Then Return
            If Environment.TickCount - _ultimaStruttura < 900 AndAlso Environment.TickCount - _ultimaStruttura >= 0 Then Return
            _ultimaStruttura = Environment.TickCount
            _bufferDaRicostruire = True
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.SegnaStrutturaObsoleta: " & ex.Message)
        End Try
    End Sub

    Public Function CercaTesto(testo As String) As String
        Try
            If _radice Is Nothing OrElse String.IsNullOrWhiteSpace(testo) Then Return "nessun contenuto"
            AssicuraBuffer(1200)
            Dim ago As String = testo.Trim()
            If _corrente Is Nothing Then _corrente = _radice
            Dim esatto As Boolean = False
            Dim inizio As Integer = IndiceVicino(_corrente, esatto)
            If _voci.Count > 0 AndAlso inizio >= 0 Then
                For passo As Integer = 1 To _voci.Count
                    Dim indice As Integer = (inizio + passo) Mod _voci.Count
                    If _voci(indice).Testo.IndexOf(ago, StringComparison.CurrentCultureIgnoreCase) >= 0 AndAlso VocePresente(indice) Then
                        Dim prefisso As String = If(indice <= inizio, "dall'inizio, ", "")
                        Return Arriva(_voci(indice).Elemento, False, True, False, prefisso & "trovato, ", True, False)
                    End If
                Next
            End If
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            Dim nodo As IUIAutomationElement = _corrente
            While orologio.ElapsedMilliseconds < AttesaVoceMs
                Dim fine As Boolean = False
                Dim prossimo As IUIAutomationElement = ProssimaVoceTenace(nodo, True, 300, fine)
                If prossimo Is Nothing Then Exit While
                nodo = prossimo
                If Stesso(nodo, _corrente) Then Exit While
                Dim nome As String = _descrittore.NomeEsteso(nodo)
                If nome <> "" AndAlso nome.IndexOf(ago, StringComparison.CurrentCultureIgnoreCase) >= 0 Then
                    Return Arriva(nodo, False, True, False, "trovato, ", True, False)
                End If
            End While
            Return "non trovato, " & ago
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.CercaTesto: " & ex.Message)
            Return ""
        End Try
    End Function

    Friend Function ElementiPagina() As Dictionary(Of String, List(Of VoceElementoPagina))
        Dim gruppi As New Dictionary(Of String, List(Of VoceElementoPagina))(StringComparer.Ordinal)
        Try
            For Each nomeGruppo As String In FinestraElementi.Gruppi
                gruppi(nomeGruppo) = New List(Of VoceElementoPagina)()
            Next
            If _radice Is Nothing Then Return gruppi
            Dim orologio As Stopwatch = Stopwatch.StartNew()
            While Not _indiceCompleto AndAlso orologio.ElapsedMilliseconds < 2500
                Indicizza(150)
                If _aiutoRichiesta IsNot Nothing Then System.Threading.Thread.Sleep(20)
            End While
            Dim copia As New List(Of VoceAlbero)(_voci)
            For Each voce As VoceAlbero In copia
                If voce Is Nothing Then Continue For
                If voce.IdAiuto <> 0 Then
                    AggiungiElementoPaginaAiuto(voce, gruppi)
                    Continue For
                End If
                If voce.Elemento Is Nothing Then Continue For
                Dim gruppo As String = ""
                Dim testo As String = ""
                Select Case voce.Tipo
                    Case "collegamento"
                        gruppo = "Link"
                        testo = voce.Testo
                        If testo = "" Then testo = NomeDaDiscendenti(voce.Elemento)
                    Case "titolo"
                        gruppo = "Intestazioni"
                        testo = voce.Testo
                        If testo = "" Then testo = _descrittore.Nome(voce.Elemento)
                        If testo <> "" AndAlso voce.Livello > 0 Then testo &= ", livello " & voce.Livello.ToString()
                    Case "modifica", "casella", "opzione", "combinata"
                        gruppo = "Campi"
                        testo = Annuncio(voce.Elemento)
                        If testo = "" Then testo = voce.Testo
                    Case "pulsante"
                        gruppo = "Pulsanti"
                        testo = voce.Testo
                        If testo = "" Then testo = Annuncio(voce.Elemento)
                    Case "regione"
                        gruppo = "Regioni"
                        Dim nomeRegione As String = _descrittore.NomeRegione(voce.Elemento)
                        Dim nome As String = _descrittore.Nome(voce.Elemento)
                        If nome <> "" AndAlso Not String.Equals(nome, nomeRegione, StringComparison.CurrentCultureIgnoreCase) Then
                            testo = nome & ", " & nomeRegione
                        Else
                            testo = nomeRegione
                        End If
                End Select
                If gruppo = "" OrElse String.IsNullOrWhiteSpace(testo) Then Continue For
                Dim elemento As New VoceElementoPagina()
                elemento.Elemento = voce.Elemento
                elemento.Testo = testo.Trim()
                gruppi(gruppo).Add(elemento)
            Next
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.ElementiPagina: " & ex.Message)
        End Try
        Return gruppi
    End Function

    Private Sub AggiungiElementoPaginaAiuto(voce As VoceAlbero, gruppi As Dictionary(Of String, List(Of VoceElementoPagina)))
        Try
            Dim gruppo As String = ""
            Dim testo As String = voce.Testo
            Select Case voce.Tipo
                Case "collegamento"
                    gruppo = "Link"
                    If voce.Visitato AndAlso testo <> "" Then testo &= ", visitato"
                Case "titolo"
                    gruppo = "Intestazioni"
                    If testo <> "" AndAlso voce.Livello > 0 Then testo &= ", livello " & voce.Livello.ToString()
                Case "modifica", "casella", "opzione", "combinata"
                    gruppo = "Campi"
                    testo = If(testo = "", NomeTipo(voce.Tipo, 0), testo & ", " & NomeTipo(voce.Tipo, 0))
                Case "pulsante"
                    gruppo = "Pulsanti"
                Case "regione"
                    gruppo = "Regioni"
                    Dim nome As String = voce.NomeProprio
                    If nome <> "" AndAlso Not String.Equals(nome, voce.NomeRegione, StringComparison.CurrentCultureIgnoreCase) Then
                        testo = nome & ", " & voce.NomeRegione
                    Else
                        testo = voce.NomeRegione
                    End If
            End Select
            If gruppo = "" OrElse String.IsNullOrWhiteSpace(testo) Then Return
            Dim elemento As New VoceElementoPagina()
            elemento.Risolutore = AddressOf voce.ElementoRisolto
            elemento.Testo = testo.Trim()
            gruppi(gruppo).Add(elemento)
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.AggiungiElementoPaginaAiuto: " & ex.Message)
        End Try
    End Sub

    Public Function RiassuntoTipi() As String
        Try
            If _radice Is Nothing Then Return "nessun contenuto"
            If Not _indiceCompleto Then Indicizza(70)
            Dim conteggi As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
            For Each voce In _voci
                Dim tipo As String = voce.Tipo
                If tipo = "modifica" OrElse tipo = "casella" OrElse tipo = "opzione" OrElse tipo = "combinata" Then tipo = "campo"
                If tipo = "" OrElse tipo = "altro" OrElse tipo = "voce" OrElse tipo = "cella" Then Continue For
                Dim numero As Integer = 0
                conteggi.TryGetValue(tipo, numero)
                conteggi(tipo) = numero + 1
            Next
            If conteggi.Count = 0 Then
                If _indiceCompleto Then Return "nessun elemento indicizzato"
                Return "pagina ancora in lettura"
            End If
            Dim ordine() As String = {"titolo", "collegamento", "campo", "tabella", "elenco", "regione", "grafica", "pulsante", "articolo", "testo"}
            Dim parti As New List(Of String)()
            For Each tipo In ordine
                Dim numero As Integer = 0
                If conteggi.TryGetValue(tipo, numero) AndAlso numero > 0 Then parti.Add(numero.ToString() & " " & NomeTipoPlurale(tipo, numero))
            Next
            If parti.Count = 0 Then Return "nessun elemento indicizzato"
            Dim coda As String = If(_indiceCompleto, "", ", pagina ancora in lettura")
            Return String.Join(", ", parti) & coda
        Catch ex As Exception
            log.ScriviLog("NavigazioneWeb.RiassuntoTipi: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Function NomeTipoPlurale(tipo As String, numero As Integer) As String
        Dim uno As Boolean = numero = 1
        Select Case tipo
            Case "titolo" : Return If(uno, "titolo", "titoli")
            Case "collegamento" : Return If(uno, "link", "link")
            Case "campo" : Return If(uno, "campo", "campi")
            Case "tabella" : Return If(uno, "tabella", "tabelle")
            Case "elenco" : Return If(uno, "elenco", "elenchi")
            Case "regione" : Return If(uno, "regione", "regioni")
            Case "grafica" : Return If(uno, "immagine", "immagini")
            Case "pulsante" : Return If(uno, "pulsante", "pulsanti")
            Case "articolo" : Return If(uno, "articolo", "articoli")
            Case Else : Return tipo
        End Select
    End Function

End Class