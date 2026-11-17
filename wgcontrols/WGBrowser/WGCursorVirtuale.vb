Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports WGBrowserManager
Imports wgcontrols

' Cursore virtuale per la lettura riga per riga e la traduzione dei contenuti accessibili.
Public Enum ContestoLettura
    Tutto
    Contenitori
    Nessuno
    SoloIngresso
End Enum

Public Class RigaVirtuale
    Public Property TestoAnnuncio As String = ""
    Public Property TipoElemento As String = ""
    Public Property IsInterattivo As Boolean = False
    Public Property IdElemento As Integer = 0
    Public Property Stato As String = ""

    Public Property Ruolo As String = "text"
    Public Property Testo As String = ""
    Public Property Livello As Integer = 0
    Public Property Valore As String = ""
    Public Property Modificabile As Boolean = False
    Public Property Multiriga As Boolean = False
    Public Property Password As Boolean = False
    Public Property LunghezzaPassword As Integer = 0
    Public Property SolaLettura As Boolean = False
    Public Property IdAttivazione As Integer = 0
    Public Property RuoloAttivazione As String = ""
    Public Property Regione As String = ""
    Public Property ElementiLista As Integer = 0
    Public Property InizioElementoLista As Boolean = False
    Public Property Tabella As String = ""
    Public Property InizioArticolo As Boolean = False
    Public Property Gruppo As String = ""
    Public Property InizioCitazione As Boolean = False
    Public Property Descrizione As String = ""
    Public Property IdDialogo As Integer = 0
    Public Property DaAlbero As Boolean = False
    Public Property Sessione As String = ""

    Public Property Indirizzo As String = ""
    Public Property Lingua As String = ""
    Public Property SenzaNome As Boolean = False
    Public Property Sorgente As String = ""
    Public Property IdTabella As String = ""
    Public Property RigaTabella As Integer = 0
    Public Property ColonnaTabella As Integer = 0
    Public Property InizioCella As Boolean = False
    Public Property OrdineDocumento As Integer = -1
    Public Property InizioParagrafo As Boolean = False
    ' Testo prima della traduzione della pagina; vuoto se la riga non è tradotta.
    Public Property TestoOriginale As String = ""

    Public Function Copia() As RigaVirtuale
        Return CType(Me.MemberwiseClone(), RigaVirtuale)
    End Function

    Public Function StessoElemento(sessioneAltro As String, idAltro As Integer) As Boolean
        Return idAltro > 0 AndAlso IdElemento = idAltro AndAlso String.Equals(If(Sessione, ""), If(sessioneAltro, ""), StringComparison.Ordinal)
    End Function

    Public Function StessoDaAttivare(sessioneAltro As String, idAltro As Integer) As Boolean
        Return idAltro > 0 AndAlso IdDaAttivare = idAltro AndAlso String.Equals(If(Sessione, ""), If(sessioneAltro, ""), StringComparison.Ordinal)
    End Function

    Public ReadOnly Property ChiaveElemento As String
        Get
            Return If(Sessione, "") & "|" & IdElemento.ToString()
        End Get
    End Property

    Public ReadOnly Property ChiaveContenuto As String
        Get
            Return If(Sessione, "") & "|" & IdElemento.ToString() & "|" & Ruolo & "|" & Testo & "|" & Valore
        End Get
    End Property

    Private Shared ReadOnly _spaziImpronta As New Regex("\s+", RegexOptions.Compiled)
    Private Shared ReadOnly _cifreImpronta As New Regex("\d+", RegexOptions.Compiled)

    ' Impronta di una riga indipendente dagli id del DOM, per ritrovarla quando il sito ridisegna la pagina.
    ' Livello 1: ruolo, testo, indirizzo e regione. Livello 2: come 1 ma senza regione e con le cifre mascherate.
    ' Livello 3: solo ruolo e testo con le cifre mascherate.
    ' Il testo normalizzato si calcola una volta sola per riga (finché il testo non cambia): la ricerca dell'equivalente
    ' confronta migliaia di righe a ogni aggiornamento e le espressioni regolari erano la parte più lenta.
    Private _testoImpronta As String = Nothing
    Private _normaleImpronta As String = ""
    Private _mascheratoImpronta As String = ""

    Private Sub PreparaImpronta()
        Dim testoAttuale As String = If(Testo, "")
        If _testoImpronta IsNot Nothing AndAlso String.Equals(_testoImpronta, testoAttuale, StringComparison.Ordinal) Then Return
        _normaleImpronta = _spaziImpronta.Replace(testoAttuale.Trim(), " ").ToLowerInvariant()
        _mascheratoImpronta = _cifreImpronta.Replace(_normaleImpronta, "#")
        _testoImpronta = testoAttuale
    End Sub

    Public Function Impronta(livello As Integer) As String
        PreparaImpronta()
        Select Case livello
            Case 1
                Return If(Ruolo, "") & "|" & _normaleImpronta & "|" & If(Indirizzo, "") & "|" & If(Regione, "")
            Case 2
                Return If(Ruolo, "") & "|" & _mascheratoImpronta & "|" & If(Indirizzo, "")
            Case Else
                Return If(Ruolo, "") & "|" & _mascheratoImpronta
        End Select
    End Function

    Public Shared Sub Completa(r As RigaVirtuale)
        Try
            r.TipoElemento = DescriviTipo(r)
            r.IsInterattivo = (r.Ruolo <> "text" AndAlso r.Ruolo <> "heading" AndAlso r.Ruolo <> "img" AndAlso r.Ruolo <> "math") OrElse r.IdAttivazione > 0
            r.TestoAnnuncio = r.CostruisciAnnuncio(False)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("RigaVirtuale.Completa: " & ex.Message)
        End Try
    End Sub

    Public ReadOnly Property IdDaAttivare As Integer
        Get
            Return If(IdAttivazione > 0, IdAttivazione, IdElemento)
        End Get
    End Property

    Public ReadOnly Property RuoloDaAttivare As String
        Get
            Return If(IdAttivazione > 0 AndAlso Not String.IsNullOrEmpty(RuoloAttivazione), RuoloAttivazione, Ruolo)
        End Get
    End Property

    Public ReadOnly Property IsCampo As Boolean
        Get
            If IdAttivazione > 0 Then Return False
            If Modificabile Then Return True
            Select Case Ruolo
                Case "combobox", "listbox", "slider", "spinbutton", "textbox", "searchbox"
                    Return True
            End Select
            Return False
        End Get
    End Property

    Public ReadOnly Property ELink As Boolean
        Get
            Return Ruolo = "link" OrElse (IdAttivazione > 0 AndAlso RuoloAttivazione = "link")
        End Get
    End Property

    Public Shared Function DaJson(o As JObject) As RigaVirtuale
        If o Is Nothing Then Return Nothing
        Dim r As New RigaVirtuale()
        Try
            r.IdElemento = o.Value(Of Integer?)("i").GetValueOrDefault()
            r.Ruolo = If(o.Value(Of String)("r"), "text")
            r.Testo = If(o.Value(Of String)("t"), "")
            r.Livello = o.Value(Of Integer?)("l").GetValueOrDefault()
            r.Stato = If(o.Value(Of String)("s"), "")
            r.Valore = If(o.Value(Of String)("v"), "")
            r.Modificabile = o.Value(Of Integer?)("ed").GetValueOrDefault() = 1
            r.Multiriga = o.Value(Of Integer?)("ml").GetValueOrDefault() = 1
            r.Password = o.Value(Of Integer?)("pw").GetValueOrDefault() = 1
            r.LunghezzaPassword = o.Value(Of Integer?)("pl").GetValueOrDefault()
            r.SolaLettura = o.Value(Of Integer?)("ro").GetValueOrDefault() = 1
            r.IdAttivazione = o.Value(Of Integer?)("lk").GetValueOrDefault()
            r.RuoloAttivazione = If(o.Value(Of String)("lr"), "")
            r.Regione = If(o.Value(Of String)("rg"), "")
            r.ElementiLista = o.Value(Of Integer?)("ls").GetValueOrDefault()
            r.InizioElementoLista = o.Value(Of Integer?)("li").GetValueOrDefault() = 1
            r.Tabella = If(o.Value(Of String)("tb"), "")
            r.InizioArticolo = o.Value(Of Integer?)("ar").GetValueOrDefault() = 1
            r.Gruppo = If(o.Value(Of String)("gr"), "")
            r.InizioCitazione = o.Value(Of Integer?)("bq").GetValueOrDefault() = 1
            r.Descrizione = If(o.Value(Of String)("dd"), "")
            r.IdDialogo = o.Value(Of Integer?)("dg").GetValueOrDefault()
            r.Indirizzo = If(o.Value(Of String)("u"), "")
            r.Lingua = If(o.Value(Of String)("lg"), "")
            r.SenzaNome = o.Value(Of Integer?)("sn").GetValueOrDefault() = 1
            r.Sorgente = If(o.Value(Of String)("src"), "")
            r.InizioParagrafo = o.Value(Of Integer?)("pp").GetValueOrDefault() = 1
            Completa(r)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("RigaVirtuale.DaJson: " & ex.Message)
        End Try
        Return r
    End Function

    Public Shared Function DescriviTipo(r As RigaVirtuale) As String
        Select Case r.Ruolo
            Case "heading" : Return "Intestazione " & Math.Max(1, r.Livello).ToString()
            Case "link" : Return "Link"
            Case "button" : Return "Pulsante"
            Case "cliccabile" : Return "Cliccabile"
            Case "checkbox" : Return "Casella di controllo"
            Case "radio" : Return "Pulsante di opzione"
            Case "switch" : Return "Interruttore"
            Case "combobox" : Return If(r.Modificabile, "Casella combinata modificabile", "Casella combinata")
            Case "listbox" : Return "Elenco"
            Case "searchbox" : Return "Campo di ricerca"
            Case "textbox"
                If r.Password Then Return "Campo password"
                Return If(r.Multiriga, "Area di testo", "Campo di testo")
            Case "slider" : Return "Dispositivo di scorrimento"
            Case "spinbutton" : Return "Casella numerica"
            Case "img" : Return "Immagine"
            Case "option" : Return "Opzione"
            Case "menuitem", "menuitemcheckbox", "menuitemradio" : Return "Voce di menu"
            Case "tab" : Return "Scheda"
            Case "treeitem" : Return "Elemento struttura"
            Case "progressbar" : Return "Barra di avanzamento"
            Case "meter" : Return "Misuratore"
            Case "frame" : Return "Frame"
            Case "canvas" : Return "Area disegnata"
            Case "math" : Return "Formula"
            Case "video" : Return "Video"
            Case "audio" : Return "Audio"
            Case "dialog", "alertdialog" : Return "Finestra di dialogo"
            Case "text"
                If r.IdAttivazione > 0 Then
                    Select Case r.RuoloAttivazione
                        Case "button" : Return "Pulsante"
                        Case "cliccabile" : Return "Cliccabile"
                    End Select
                    Return "Link"
                End If
                Return ""
        End Select
        If r.Modificabile Then Return If(r.Multiriga, "Area di testo", "Campo di testo")
        Return ""
    End Function

    Public ReadOnly Property HaContestoIngresso As Boolean
        Get
            Return Not String.IsNullOrEmpty(Regione) OrElse Not String.IsNullOrEmpty(Gruppo) OrElse InizioArticolo
        End Get
    End Property

    Public Function TestoContestoIngresso() As String
        Dim parti As New List(Of String)()
        Try
            If Not String.IsNullOrEmpty(Regione) Then parti.Add("Regione " & Regione)
            If Not String.IsNullOrEmpty(Gruppo) Then parti.Add(Gruppo)
            If InizioArticolo Then parti.Add("Articolo")
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("RigaVirtuale.TestoContestoIngresso: " & ex.Message)
        End Try
        Return String.Join(", ", parti)
    End Function

    Public Function CostruisciAnnuncio(conContesto As Boolean) As String
        Return CostruisciAnnuncioParti(conContesto, conContesto)
    End Function

    Public Function CostruisciAnnuncioParti(ingresso As Boolean, contenitori As Boolean) As String
        Dim parti As New List(Of String)()
        Try
            If ingresso AndAlso Not String.IsNullOrEmpty(Regione) Then parti.Add("Regione " & Regione)
            If contenitori AndAlso Not String.IsNullOrEmpty(Tabella) Then parti.Add("Tabella " & Tabella)
            If ingresso AndAlso Not String.IsNullOrEmpty(Gruppo) Then parti.Add(Gruppo)
            If ingresso AndAlso InizioArticolo Then parti.Add("Articolo")
            If contenitori AndAlso InizioCitazione Then parti.Add("Citazione")
            If contenitori AndAlso ElementiLista > 0 Then parti.Add("Elenco di " & ElementiLista & If(ElementiLista = 1, " elemento", " elementi"))
            Dim tipo As String = TipoElemento
            If Ruolo = "heading" AndAlso IdAttivazione > 0 Then
                tipo &= If(RuoloAttivazione = "button", ", pulsante", If(RuoloAttivazione = "cliccabile", ", cliccabile", ", link"))
            ElseIf Ruolo <> "text" AndAlso Ruolo <> "link" AndAlso IdAttivazione > 0 Then
                tipo &= If(RuoloAttivazione = "button", ", pulsante", If(RuoloAttivazione = "cliccabile", ", cliccabile", ", link"))
            End If
            Dim nome As String = Testo
            If Ruolo = "img" AndAlso SenzaNome Then nome = "senza descrizione" & If(String.IsNullOrWhiteSpace(Testo), "", ", " & Testo)
            Dim principale As String
            If String.IsNullOrEmpty(nome) Then
                principale = If(tipo, "")
            ElseIf String.IsNullOrEmpty(tipo) Then
                principale = nome
            ElseIf Ruolo = "heading" Then
                ' Come JAWS: prima il testo, poi il livello ("Contenuti, intestazione livello 2").
                principale = nome & ", intestazione livello " & Math.Max(1, Livello).ToString() & tipo.Substring(tipo.IndexOf(" "c) + 1).TrimStart("0123456789".ToCharArray())
            ElseIf RuoloDaAttivare = "button" Then
                principale = nome & " " & Char.ToLowerInvariant(tipo(0)) & tipo.Substring(1)
            Else
                principale = tipo & " " & nome
            End If
            If Password Then
                principale &= If(LunghezzaPassword > 0, ", " & LunghezzaPassword & " caratteri", ", vuoto")
            ElseIf Not String.IsNullOrEmpty(Valore) Then
                principale &= ", " & Valore.Replace(vbLf, " ")
            ElseIf Modificabile Then
                principale &= ", vuoto"
            End If
            If Ruolo = "treeitem" AndAlso Livello > 0 Then principale &= ", livello " & Livello
            If Not String.IsNullOrEmpty(Stato) Then principale &= " (" & Stato & ")"
            If Not String.IsNullOrEmpty(Descrizione) Then principale &= ". " & Descrizione
            parti.Add(principale.Trim())
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("RigaVirtuale.CostruisciAnnuncio: " & ex.Message)
        End Try
        Return String.Join(", ", parti)
    End Function
End Class

Public Class CellaVirtuale
    Public Riga As Integer = 1
    Public Colonna As Integer = 1
    Public SpanRighe As Integer = 1
    Public SpanColonne As Integer = 1
    Public IdNodo As Integer = 0
    Public Intestazione As Boolean = False
    Public IntestazioneRiga As Boolean = False
    Public Testo As String = ""
    Public IndicePrimaRiga As Integer = -1
End Class

Public Class TabellaVirtuale
    Public Id As String = ""
    Public Nome As String = ""
    Public Righe As Integer = 0
    Public Colonne As Integer = 0
    Public ReadOnly Celle As New List(Of CellaVirtuale)()
    Public ReadOnly PerNodo As New Dictionary(Of Integer, CellaVirtuale)()

    Public Function CellaIn(riga As Integer, colonna As Integer) As CellaVirtuale
        For Each c As CellaVirtuale In Celle
            If riga >= c.Riga AndAlso riga < c.Riga + c.SpanRighe AndAlso colonna >= c.Colonna AndAlso colonna < c.Colonna + c.SpanColonne Then Return c
        Next
        Return Nothing
    End Function

    Public Function IntestazioneColonna(colonna As Integer, esclusa As CellaVirtuale) As String
        Dim parti As New List(Of String)()
        For Each c As CellaVirtuale In Celle
            If Not c.Intestazione OrElse c Is esclusa Then Continue For
            If colonna >= c.Colonna AndAlso colonna < c.Colonna + c.SpanColonne AndAlso c.Testo <> "" Then parti.Add(c.Testo)
        Next
        Return String.Join(" ", parti)
    End Function

    Public Function IntestazioneRiga(riga As Integer, esclusa As CellaVirtuale) As String
        Dim parti As New List(Of String)()
        For Each c As CellaVirtuale In Celle
            If Not c.IntestazioneRiga OrElse c Is esclusa Then Continue For
            If riga >= c.Riga AndAlso riga < c.Riga + c.SpanRighe AndAlso c.Testo <> "" Then parti.Add(c.Testo)
        Next
        Return String.Join(" ", parti)
    End Function
End Class

Public Class WGCursorVirtuale
    Private log As New Logger()
    Private lettura As sintesivocale = sintesivocale.GetInstance()
    Private paginaWeb As WGPaginaWeb
    Private formPadre As System.Windows.Forms.Control
    Private cdpInterazione As WGCdpInterazione

    Private righeVirtuali As New List(Of RigaVirtuale)()
    Private indiceRigaVirtuale As Integer = -1
    Private posizioneCarattere As Integer = -1
    Private letturaContinuaAttiva As Boolean = False
    Private threadLetturaContinua As Thread = Nothing
    Private aggiornaInCorso As Task(Of Boolean) = Nothing
    Private ultimoAggiornamento As DateTime = DateTime.MinValue
    Private _bufferSporco As Boolean = False
    Private _istanteUltimaAzione As DateTime = DateTime.MinValue
    Private _istanteUltimoSpostamento As DateTime = DateTime.MinValue
    Private idModaleCorrente As Integer = 0
    Private idPrimaDelModale As Integer = 0
    Private rigaPrimaDelModale As RigaVirtuale = Nothing
    Private sessionePrimaDelModale As String = ""
    Private testoPrimaDelModale As String = ""
    Private albero As WGAlberoAccessibilita
    Private _ultimaLinguaLetta As String = ""
    Private _annunciaLingua As Boolean = True
    Private _cellaCorrente As Tuple(Of String, Integer, Integer) = Nothing
    Private _suContestoIngresso As Boolean = False
    Private _bordoRaggiunto As Integer = 0
    Private _indiceBordo As Integer = -1
    Private _carattereBordo As Integer = -1
    Private _selAncoraRiga As Integer = -1
    Private _selAncoraCarattere As Integer = 0
    Private _selFuocoRiga As Integer = -1
    Private _selFuocoCarattere As Integer = 0

    ' Traduzione della pagina: il testo tradotto prende il posto di quello originale nelle righe, che restano
    ' collegate agli stessi elementi; l'originale resta in TestoOriginale. Le traduzioni sono ricordate per testo,
    ' così a ogni ricostruzione della pagina si riapplicano subito e si chiedono al traduttore solo le righe nuove.
    Private ReadOnly _traduzioni As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Private _traduzioneInCorso As Boolean = False
    Private _versioneTraduzione As Integer = 0
    Private _istanteErroreTraduzione As DateTime = DateTime.MinValue
    ' Si traduce una finestra di righe intorno al cursore, che si sposta con la lettura: sulle pagine molto lunghe
    ' (commenti, risultati infiniti) non si manda tutta la pagina al traduttore.
    Private Const RigheTraduzioneIndietro As Integer = 200
    Private Const RigheTraduzioneAvanti As Integer = 1500
    Public Property TraduzioneAttiva As Boolean = False
    Public Property LinguaOrigineTraduzione As String = ""
    Public Property LinguaDestinazioneTraduzione As String = ""
    Public Property LinguaRilevataTraduzione As String = ""

    Public Shared ReadOnly DescrizioniImmagini As New System.Collections.Concurrent.ConcurrentDictionary(Of String, String)(StringComparer.Ordinal)

    Private Shared _scriptAgente As String = Nothing
    Private Shared ReadOnly _lockScript As New Object()
    Private Shared ReadOnly _modelloTempo As New Regex("^(\d{1,2}[:.]\d{2}([:.]\d{2})?|\d+\s*(s|sec|secondi|min|minuti|h|ore|giorni|g|m)\b.*|adesso|ora|now|\d+\s*(seconds?|minutes?|hours?)\s+ago)$", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    ' Impostato direttamente dopo un'azione dell'utente (clic, compilazione, uscita da un campo): le frecce successive
    ' attendono il nuovo contenuto. I cambiamenti spontanei del sito passano da SegnaSporcoDalSito e non fermano la lettura.
    Public Property BufferSporco As Boolean
        Get
            Return _bufferSporco
        End Get
        Set(value As Boolean)
            _bufferSporco = value
            If value Then _istanteUltimaAzione = DateTime.Now
        End Set
    End Property

    Public Sub SegnaSporcoDalSito()
        _bufferSporco = True
    End Sub
    Public Property Titolo As String = ""
    Public Property Indirizzo As String = ""
    Public Property IndiceFocusPagina As Integer = -1
    Public Property UsaAlbero As Boolean = False
    Public Property LinguaPagina As String = ""
    Public Property LayoutPrincipale As LayoutDocumento = Nothing
    Public Property Tabelle As New Dictionary(Of String, TabellaVirtuale)()
    Public Property UltimeRigheNuove As New List(Of RigaVirtuale)()
    Public Property ContieneAreeDisegnate As Boolean = False
    Public Property ContieneSemanticaFlutter As Boolean = False

    Public ReadOnly Property IndiceCorrente As Integer
        Get
            Return indiceRigaVirtuale
        End Get
    End Property

    Public ReadOnly Property IdModale As Integer
        Get
            Return idModaleCorrente
        End Get
    End Property

    Public ReadOnly Property Righe As List(Of RigaVirtuale)
        Get
            Return righeVirtuali
        End Get
    End Property

    Public ReadOnly Property IstanteUltimoAggiornamento As DateTime
        Get
            Return ultimoAggiornamento
        End Get
    End Property

    Public ReadOnly Property AggiornamentoInCorso As Boolean
        Get
            Return aggiornaInCorso IsNot Nothing AndAlso Not aggiornaInCorso.IsCompleted
        End Get
    End Property

    Public ReadOnly Property RigaCorrente As RigaVirtuale
        Get
            Try
                If indiceRigaVirtuale >= 0 AndAlso indiceRigaVirtuale < righeVirtuali.Count Then Return righeVirtuali(indiceRigaVirtuale)
                Return Nothing
            Catch ex As Exception
                log.ScriviLog("WGCursorVirtuale.RigaCorrente: " & ex.Message)
                Return Nothing
            End Try
        End Get
    End Property

    Public ReadOnly Property IsLetturaContinuaAttiva As Boolean
        Get
            Return letturaContinuaAttiva
        End Get
    End Property

    Public ReadOnly Property SuContestoIngresso As Boolean
        Get
            Return _suContestoIngresso
        End Get
    End Property

    Private ReadOnly Property RegioniAParte As Boolean
        Get
            Try
                Return WGImpostazioniBrowser.RegioniAParte
            Catch ex As Exception
                log.ScriviLog("WGCursorVirtuale.RegioniAParte: " & ex.Message)
                Return True
            End Try
        End Get
    End Property

    Public Sub New(pagina As WGPaginaWeb, controllo As System.Windows.Forms.Control, interazione As WGCdpInterazione, alberoAccessibilita As WGAlberoAccessibilita)
        Try
            paginaWeb = pagina
            formPadre = controllo
            cdpInterazione = interazione
            albero = alberoAccessibilita
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.New: " & ex.ToString())
        End Try
    End Sub

    Public Shared ReadOnly Property ScriptAgente As String
        Get
            SyncLock _lockScript
                If _scriptAgente Is Nothing Then
                    Try
                        Dim assembly As Assembly = GetType(WGCursorVirtuale).Assembly
                        Dim nomeRisorsa As String = Nothing
                        For Each nome As String In assembly.GetManifestResourceNames()
                            If nome.EndsWith("WGBrowserAgente.js", StringComparison.OrdinalIgnoreCase) Then
                                nomeRisorsa = nome
                                Exit For
                            End If
                        Next
                        If nomeRisorsa IsNot Nothing Then
                            Using flusso As Stream = assembly.GetManifestResourceStream(nomeRisorsa)
                                Using lettore As New StreamReader(flusso, Encoding.UTF8)
                                    _scriptAgente = lettore.ReadToEnd()
                                End Using
                            End Using
                        Else
                            _scriptAgente = ""
                        End If
                    Catch ex As Exception
                        Dim registro As New Logger()
                        registro.ScriviLog("WGCursorVirtuale.ScriptAgente: " & ex.Message)
                        _scriptAgente = ""
                    End Try
                End If
                Return _scriptAgente
            End SyncLock
        End Get
    End Property

    Public Function AggiornaAsync(nuovaPagina As Boolean) As Task(Of Boolean)
        Try
            If aggiornaInCorso IsNot Nothing AndAlso Not aggiornaInCorso.IsCompleted Then Return aggiornaInCorso
            aggiornaInCorso = EseguiAggiornamentoAsync(nuovaPagina)
            Return aggiornaInCorso
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.AggiornaAsync: " & ex.Message)
            Return Task.FromResult(False)
        End Try
    End Function

    Private Async Function EseguiAggiornamentoAsync(nuovaPagina As Boolean) As Task(Of Boolean)
        Try
            If paginaWeb Is Nothing OrElse paginaWeb.IsDisposed Then Return False
            Dim nuove As List(Of RigaVirtuale) = Nothing
            Dim titoloNuovo As String = ""
            Dim idModale As Integer = 0
            Dim sessioneFocus As String = ""
            Dim idFocus As Integer = 0
            Dim nuoveTabelle As New Dictionary(Of String, TabellaVirtuale)()
            Dim nuovoLayout As LayoutDocumento = Nothing
            Dim linguaNuova As String = ""
            Dim areeDisegnate As Boolean = False
            _annunciaLingua = WGImpostazioniBrowser.AnnunciaLingua

            Dim dalAlbero As WGAlberoAccessibilita.Risultato = Nothing
            If albero IsNot Nothing Then dalAlbero = Await albero.CostruisciAsync()
            If dalAlbero IsNot Nothing AndAlso dalAlbero.Righe.Count > 0 Then
                nuove = dalAlbero.Righe
                titoloNuovo = dalAlbero.Titolo
                idModale = dalAlbero.IdModale
                sessioneFocus = dalAlbero.SessioneFocus
                idFocus = dalAlbero.IdFocus
                nuoveTabelle = dalAlbero.Tabelle
                nuovoLayout = dalAlbero.Layout
                linguaNuova = dalAlbero.Lingua
                areeDisegnate = dalAlbero.AreeDisegnate
                UsaAlbero = True
            Else
                Dim json As String = Await cdpInterazione.ValutaAsync("window.__wgAgente ? window.__wgAgente.costruisci() : null", 8000)
                If String.IsNullOrEmpty(json) OrElse json = "null" Then
                    InserisciAgente()
                    Await Task.Delay(100)
                    json = Await cdpInterazione.ValutaAsync("window.__wgAgente ? window.__wgAgente.costruisci() : null", 8000)
                End If
                If String.IsNullOrEmpty(json) OrElse json = "null" Then Return False
                Dim dati As JObject = JObject.Parse(json)
                nuove = New List(Of RigaVirtuale)()
                Dim elenco As JArray = TryCast(dati("righe"), JArray)
                If elenco IsNot Nothing Then
                    Dim immaginiSenzaNome As Boolean = WGImpostazioniBrowser.LeggiImmaginiSenzaDescrizione
                    Dim cliccabili As Boolean = WGImpostazioniBrowser.RiconosciCliccabili
                    For Each elemento As JToken In elenco
                        Dim riga As RigaVirtuale = RigaVirtuale.DaJson(TryCast(elemento, JObject))
                        If riga Is Nothing Then Continue For
                        If riga.SenzaNome AndAlso Not immaginiSenzaNome Then Continue For
                        If Not cliccabili Then
                            If riga.Ruolo = "cliccabile" Then riga.Ruolo = "text"
                            If riga.RuoloAttivazione = "cliccabile" Then
                                riga.IdAttivazione = 0
                                riga.RuoloAttivazione = ""
                            End If
                            RigaVirtuale.Completa(riga)
                        End If
                        If Not String.IsNullOrWhiteSpace(riga.Testo) OrElse riga.IsInterattivo OrElse riga.SenzaNome Then nuove.Add(riga)
                    Next
                End If
                titoloNuovo = If(dati.Value(Of String)("titolo"), "")
                idModale = dati.Value(Of Integer?)("modale").GetValueOrDefault()
                idFocus = dati.Value(Of Integer?)("focus").GetValueOrDefault()
                linguaNuova = WGLayoutPagina.NormalizzaLingua(If(dati.Value(Of String)("lingua"), ""))
                areeDisegnate = dati.Value(Of Integer?)("canvas").GetValueOrDefault() = 1
                UsaAlbero = False
            End If

            ApplicaDescrizioniMemorizzate(nuove)
            ApplicaFormule(nuove)
            If TraduzioneAttiva Then ApplicaTraduzioni(nuove)
            Dim vecchie As List(Of RigaVirtuale) = righeVirtuali
            Dim vecchia As RigaVirtuale = RigaCorrente
            Dim vecchioIndice As Integer = indiceRigaVirtuale
            Dim eraSuContesto As Boolean = _suContestoIngresso
            Dim rigaAncora As RigaVirtuale = Nothing
            Dim rigaFuoco As RigaVirtuale = Nothing
            Dim indiceAncora As Integer = _selAncoraRiga
            Dim indiceFuoco As Integer = _selFuocoRiga
            Try
                If SelezioneAttiva AndAlso vecchie IsNot Nothing Then
                    If _selAncoraRiga >= 0 AndAlso _selAncoraRiga < vecchie.Count Then rigaAncora = vecchie(_selAncoraRiga)
                    If _selFuocoRiga >= 0 AndAlso _selFuocoRiga < vecchie.Count Then rigaFuoco = vecchie(_selFuocoRiga)
                End If
            Catch exSelezione As Exception
                log.ScriviLog("WGCursorVirtuale.EseguiAggiornamentoAsync selezione: " & exSelezione.Message)
                rigaAncora = Nothing
                rigaFuoco = Nothing
            End Try
            If Not nuovaPagina Then CalcolaRigheNuove(vecchie, nuove) Else UltimeRigheNuove = New List(Of RigaVirtuale)()
            Titolo = titoloNuovo
            righeVirtuali = nuove
            Tabelle = nuoveTabelle
            LayoutPrincipale = nuovoLayout
            LinguaPagina = linguaNuova
            ContieneAreeDisegnate = areeDisegnate
            BufferSporco = False
            ultimoAggiornamento = DateTime.Now
            posizioneCarattere = -1
            IndiceFocusPagina = IndiceDiNodo(sessioneFocus, idFocus)

            _suContestoIngresso = False
            If nuovaPagina Then
                indiceRigaVirtuale = -1
                idModaleCorrente = idModale
                idPrimaDelModale = 0
                _cellaCorrente = Nothing
                _ultimaLinguaLetta = LinguaPagina
            ElseIf idModale <> idModaleCorrente Then
                If idModale <> 0 Then
                    If vecchia IsNot Nothing AndAlso idModaleCorrente = 0 Then
                        idPrimaDelModale = vecchia.IdElemento
                        sessionePrimaDelModale = vecchia.Sessione
                        testoPrimaDelModale = vecchia.Testo
                        rigaPrimaDelModale = vecchia
                    End If
                    indiceRigaVirtuale = If(righeVirtuali.Count > 0, 0, -1)
                    idModaleCorrente = idModale
                    If righeVirtuali.Count > 0 Then lettura.LeggiTesto(righeVirtuali(0).CostruisciAnnuncio(True))
                Else
                    idModaleCorrente = 0
                    indiceRigaVirtuale = TrovaIndice(sessionePrimaDelModale, idPrimaDelModale, testoPrimaDelModale, -1, rigaPrimaDelModale, Nothing)
                    idPrimaDelModale = 0
                    rigaPrimaDelModale = Nothing
                    If indiceRigaVirtuale < 0 AndAlso righeVirtuali.Count > 0 Then indiceRigaVirtuale = 0
                    Dim annuncio As String = "Finestra di dialogo chiusa"
                    If RigaCorrente IsNot Nothing Then annuncio &= ". " & RigaCorrente.CostruisciAnnuncio(True)
                    lettura.LeggiTesto(annuncio)
                End If
            ElseIf vecchia IsNot Nothing Then
                indiceRigaVirtuale = TrovaIndice(vecchia.Sessione, vecchia.IdElemento, vecchia.Testo, vecchioIndice, vecchia, vecchie)
                _suContestoIngresso = eraSuContesto AndAlso indiceRigaVirtuale >= 0 AndAlso indiceRigaVirtuale < righeVirtuali.Count AndAlso righeVirtuali(indiceRigaVirtuale).HaContestoIngresso
            End If
            RipristinaSelezione(nuovaPagina, rigaAncora, indiceAncora, rigaFuoco, indiceFuoco)
            ProseguiTraduzione()
            PreparaFormule()
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EseguiAggiornamentoAsync: " & ex.ToString())
            Return False
        End Try
    End Function

    ' Formule matematiche: il MathML di ogni formula viene letto dalla pagina una volta sola, convertito in
    ' parlato e ricordato per elemento, così a ogni ricostruzione la riga ha subito il testo giusto.
    Private ReadOnly _formule As New Dictionary(Of String, WGFormula)(StringComparer.Ordinal)
    Private _formuleInCorso As Boolean = False
    Private Const FunzioneMathML As String = "function(){var m=this;if(m.localName!=='math'){m=(m.querySelector&&m.querySelector('math'))||(m.closest&&m.closest('math'));}" &
        "if(!m)return '';try{return new XMLSerializer().serializeToString(m);}catch(e){return '';}}"

    Private Shared Function ChiaveFormula(r As RigaVirtuale) As String
        Return If(r.Sessione, "") & "|" & r.IdElemento.ToString()
    End Function

    Private Sub ApplicaFormule(righe As List(Of RigaVirtuale))
        Try
            If righe Is Nothing OrElse _formule.Count = 0 Then Return
            For Each r As RigaVirtuale In righe
                If r.Ruolo <> "math" Then Continue For
                Dim formula As WGFormula = Nothing
                If _formule.TryGetValue(ChiaveFormula(r), formula) AndAlso formula IsNot Nothing Then r.Testo = formula.Parlato
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.ApplicaFormule: " & ex.Message)
        End Try
    End Sub

    Public Function FormulaDi(riga As RigaVirtuale) As WGFormula
        If riga Is Nothing OrElse riga.Ruolo <> "math" Then Return Nothing
        Dim formula As WGFormula = Nothing
        _formule.TryGetValue(ChiaveFormula(riga), formula)
        Return formula
    End Function

    ''' <summary>Formula della riga, letta subito dalla pagina se non è ancora stata preparata.</summary>
    Public Async Function FormulaDiAsync(riga As RigaVirtuale) As Task(Of WGFormula)
        Try
            Dim formula As WGFormula = FormulaDi(riga)
            If formula IsNot Nothing OrElse riga Is Nothing OrElse riga.Ruolo <> "math" OrElse riga.IdElemento <= 0 Then Return formula
            Dim dev As WGDevTools = If(cdpInterazione IsNot Nothing, cdpInterazione.DevTools, Nothing)
            If dev Is Nothing Then Return Nothing
            Dim valore As JToken = Await dev.ChiamaSuNodoAsync(riga.Sessione, riga.IdElemento, FunzioneMathML, 2000)
            formula = WGFormula.DaMathML(If(valore IsNot Nothing, valore.ToString(), ""))
            If formula IsNot Nothing Then _formule(ChiaveFormula(riga)) = formula
            Return formula
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.FormulaDiAsync: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Async Sub PreparaFormule()
        If _formuleInCorso Then Return
        _formuleInCorso = True
        Try
            Dim dev As WGDevTools = If(cdpInterazione IsNot Nothing, cdpInterazione.DevTools, Nothing)
            If dev Is Nothing Then Return
            For Each r As RigaVirtuale In righeVirtuali.ToList()
                If r.Ruolo <> "math" OrElse r.IdElemento <= 0 OrElse _formule.ContainsKey(ChiaveFormula(r)) Then Continue For
                Dim valore As JToken = Await dev.ChiamaSuNodoAsync(r.Sessione, r.IdElemento, FunzioneMathML, 2000)
                Dim formula As WGFormula = WGFormula.DaMathML(If(valore IsNot Nothing, valore.ToString(), ""))
                ' Anche un esito vuoto si ricorda: la formula resta con il testo dato dalla pagina.
                If _formule.Count > 2000 Then _formule.Clear()
                _formule(ChiaveFormula(r)) = formula
                If formula IsNot Nothing Then
                    For Each attuale As RigaVirtuale In righeVirtuali
                        If attuale.Ruolo = "math" AndAlso ChiaveFormula(attuale) = ChiaveFormula(r) Then attuale.Testo = formula.Parlato
                    Next
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.PreparaFormule: " & ex.Message)
        Finally
            _formuleInCorso = False
        End Try
    End Sub

    Private Shared Function RigaDaTradurre(r As RigaVirtuale) As Boolean
        If r Is Nothing OrElse r.Modificabile OrElse r.Password OrElse r.TestoOriginale <> "" OrElse r.Ruolo = "math" Then Return False
        If String.IsNullOrWhiteSpace(r.Testo) Then Return False
        For Each c As Char In r.Testo
            If Char.IsLetter(c) Then Return True
        Next
        Return False
    End Function

    Private Sub ApplicaTraduzioni(righe As List(Of RigaVirtuale))
        Try
            If righe Is Nothing OrElse _traduzioni.Count = 0 Then Return
            For Each r As RigaVirtuale In righe
                If Not RigaDaTradurre(r) Then Continue For
                Dim tradotto As String = Nothing
                If Not _traduzioni.TryGetValue(r.Testo, tradotto) Then Continue For
                r.TestoOriginale = r.Testo
                r.Testo = tradotto
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.ApplicaTraduzioni: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Attiva la traduzione e traduce subito la parte di pagina intorno al cursore; il resto prosegue in sottofondo.
    ''' Restituisce "" se la prima parte è tradotta, altrimenti il motivo dell'errore.
    ''' </summary>
    Public Async Function AvviaTraduzioneAsync(origine As String, destinazione As String) As Task(Of String)
        Try
            If TraduzioneAttiva Then FermaTraduzione()
            If Not WGTraduttore.StessaLingua(destinazione, LinguaDestinazioneTraduzione) OrElse If(origine, "") <> LinguaOrigineTraduzione Then _traduzioni.Clear()
            LinguaOrigineTraduzione = If(origine, "")
            LinguaDestinazioneTraduzione = destinazione
            LinguaRilevataTraduzione = ""
            TraduzioneAttiva = True
            _versioneTraduzione += 1
            ApplicaTraduzioni(righeVirtuali)
            ' Un giro di traduzione della richiesta precedente si ferma al prossimo controllo della versione.
            Dim attesa As Integer = 0
            While _traduzioneInCorso AndAlso attesa < 100
                Await Task.Delay(50)
                attesa += 1
            End While
            _istanteErroreTraduzione = DateTime.MinValue
            Dim errore As String = Await TraduciMancantiAsync(1)
            If errore <> "" Then
                FermaTraduzione()
                Return errore
            End If
            Dim resto As Task(Of String) = TraduciMancantiAsync(Integer.MaxValue)
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.AvviaTraduzioneAsync: " & ex.ToString())
            Return ex.Message
        End Try
    End Function

    ''' <summary>Torna al testo originale; le traduzioni restano ricordate per riattivarle subito.</summary>
    Public Sub FermaTraduzione()
        Try
            TraduzioneAttiva = False
            _versioneTraduzione += 1
            For Each r As RigaVirtuale In righeVirtuali
                If r.TestoOriginale = "" Then Continue For
                r.Testo = r.TestoOriginale
                r.TestoOriginale = ""
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.FermaTraduzione: " & ex.Message)
        End Try
    End Sub

    ' Riprende la traduzione delle righe vicine al cursore se ne mancano (righe nuove, cursore spostato lontano).
    ' Dopo un errore aspetta mezzo minuto, per non insistere con un servizio che ha rifiutato le richieste.
    Private Sub ProseguiTraduzione()
        Try
            If Not TraduzioneAttiva OrElse _traduzioneInCorso Then Return
            If (DateTime.Now - _istanteErroreTraduzione).TotalSeconds < 30 Then Return
            Dim righe As List(Of RigaVirtuale) = righeVirtuali
            Dim inizio As Integer = Math.Max(0, indiceRigaVirtuale - 10)
            Dim fine As Integer = Math.Min(righe.Count - 1, Math.Max(indiceRigaVirtuale, 0) + 150)
            For i As Integer = inizio To fine
                Dim r As RigaVirtuale = righe(i)
                If RigaDaTradurre(r) AndAlso Not _traduzioni.ContainsKey(r.Testo) Then
                    Dim compito As Task(Of String) = TraduciMancantiAsync(Integer.MaxValue)
                    Return
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.ProseguiTraduzione: " & ex.Message)
        End Try
    End Sub

    ' Traduce le righe non ancora tradotte a blocchi, partendo da poco prima della riga corrente.
    ' giri indica quanti blocchi tradurre prima di fermarsi (1 per la prima parte della pagina).
    Private Async Function TraduciMancantiAsync(giri As Integer) As Task(Of String)
        If _traduzioneInCorso Then Return ""
        _traduzioneInCorso = True
        Dim versione As Integer = _versioneTraduzione
        Try
            Dim fatti As Integer = 0
            While fatti < giri
                If Not TraduzioneAttiva OrElse versione <> _versioneTraduzione Then Return ""
                Dim righe As List(Of RigaVirtuale) = righeVirtuali
                Dim totale As Integer = righe.Count
                If totale = 0 Then Return ""
                Dim limite As Integer = If(fatti = 0, 80, 250)
                Dim mancanti As New List(Of String)()
                Dim visti As New HashSet(Of String)(StringComparer.Ordinal)
                Dim centro As Integer = Math.Max(0, Math.Min(totale - 1, indiceRigaVirtuale))
                Dim primaRiga As Integer = Math.Max(0, centro - RigheTraduzioneIndietro)
                Dim ultimaRiga As Integer = Math.Min(totale - 1, centro + RigheTraduzioneAvanti)
                ' Prima le righe dal cursore in avanti, poi quelle prima del cursore.
                Dim partenza As Integer = Math.Max(primaRiga, centro - 5)
                Dim ordine As IEnumerable(Of Integer) = Enumerable.Range(partenza, ultimaRiga - partenza + 1).Concat(Enumerable.Range(primaRiga, partenza - primaRiga))
                For Each indice As Integer In ordine
                    Dim r As RigaVirtuale = righe(indice)
                    If Not RigaDaTradurre(r) OrElse _traduzioni.ContainsKey(r.Testo) Then Continue For
                    If visti.Add(r.Testo) Then mancanti.Add(r.Testo)
                    If mancanti.Count >= limite Then Exit For
                Next
                If mancanti.Count = 0 Then
                    ApplicaTraduzioni(righeVirtuali)
                    Return ""
                End If
                Dim esito As WGTraduttore.RisultatoElenco = Await WGTraduttore.TraduciElencoAsync(mancanti, LinguaOrigineTraduzione, LinguaDestinazioneTraduzione)
                If Not TraduzioneAttiva OrElse versione <> _versioneTraduzione Then Return ""
                If Not esito.Riuscito Then
                    log.ScriviLog("WGCursorVirtuale.TraduciMancantiAsync: " & esito.Errore)
                    _istanteErroreTraduzione = DateTime.Now
                    Return esito.Errore
                End If
                If LinguaRilevataTraduzione = "" AndAlso esito.LinguaOrigine <> "" Then LinguaRilevataTraduzione = esito.LinguaOrigine
                For i As Integer = 0 To mancanti.Count - 1
                    _traduzioni(mancanti(i)) = esito.Testi(i)
                Next
                ApplicaTraduzioni(righeVirtuali)
                fatti += 1
            End While
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TraduciMancantiAsync: " & ex.ToString())
            Return ex.Message
        Finally
            _traduzioneInCorso = False
        End Try
    End Function

    Private Sub ApplicaDescrizioniMemorizzate(nuove As List(Of RigaVirtuale))
        Try
            If nuove Is Nothing OrElse DescrizioniImmagini.IsEmpty Then Return
            For Each r As RigaVirtuale In nuove
                If Not r.SenzaNome OrElse String.IsNullOrEmpty(r.Sorgente) Then Continue For
                Dim descrizione As String = Nothing
                If DescrizioniImmagini.TryGetValue(r.Sorgente, descrizione) AndAlso Not String.IsNullOrWhiteSpace(descrizione) Then
                    r.Testo = descrizione
                    r.SenzaNome = False
                    RigaVirtuale.Completa(r)
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.ApplicaDescrizioniMemorizzate: " & ex.Message)
        End Try
    End Sub

    Private Sub CalcolaRigheNuove(vecchie As List(Of RigaVirtuale), nuove As List(Of RigaVirtuale))
        Try
            Dim risultato As New List(Of RigaVirtuale)()
            If vecchie Is Nothing OrElse vecchie.Count = 0 OrElse nuove Is Nothing Then
                UltimeRigheNuove = risultato
                Return
            End If
            Dim contenuti As New HashSet(Of String)(StringComparer.Ordinal)
            Dim elementi As New HashSet(Of String)(StringComparer.Ordinal)
            Dim testi As New HashSet(Of String)(StringComparer.Ordinal)
            For Each r As RigaVirtuale In vecchie
                contenuti.Add(r.ChiaveContenuto)
                elementi.Add(r.ChiaveElemento)
                testi.Add(r.Testo)
            Next
            For Each r As RigaVirtuale In nuove
                If r.IsCampo OrElse r.Modificabile Then Continue For
                If String.IsNullOrWhiteSpace(r.Testo) Then Continue For
                If contenuti.Contains(r.ChiaveContenuto) Then Continue For
                If testi.Contains(r.Testo) Then Continue For
                Dim testoPulito As String = r.Testo.Trim()
                If elementi.Contains(r.ChiaveElemento) AndAlso testoPulito.Length < 25 Then Continue For
                If _modelloTempo.IsMatch(testoPulito) Then Continue For
                risultato.Add(r)
            Next
            UltimeRigheNuove = risultato
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.CalcolaRigheNuove: " & ex.Message)
            UltimeRigheNuove = New List(Of RigaVirtuale)()
        End Try
    End Sub

    Private Sub RipristinaSelezione(nuovaPagina As Boolean, rigaAncora As RigaVirtuale, indiceAncora As Integer, rigaFuoco As RigaVirtuale, indiceFuoco As Integer)
        Try
            If nuovaPagina OrElse rigaAncora Is Nothing OrElse rigaFuoco Is Nothing Then
                AnnullaSelezione()
                Return
            End If
            Dim nuovaAncora As Integer = TrovaRigaUguale(rigaAncora, indiceAncora)
            Dim nuovoFuoco As Integer = TrovaRigaUguale(rigaFuoco, indiceFuoco)
            If nuovaAncora < 0 OrElse nuovoFuoco < 0 Then
                AnnullaSelezione()
                Return
            End If
            _selAncoraRiga = nuovaAncora
            _selFuocoRiga = nuovoFuoco
            _selAncoraCarattere = Math.Max(0, Math.Min(_selAncoraCarattere, TestoSelezionabile(nuovaAncora).Length))
            _selFuocoCarattere = Math.Max(0, Math.Min(_selFuocoCarattere, TestoSelezionabile(nuovoFuoco).Length))
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.RipristinaSelezione: " & ex.Message)
            AnnullaSelezione()
        End Try
    End Sub

    Private Shared Function TrovaEquivalente(elenco As List(Of RigaVirtuale), riferimento As RigaVirtuale, indiceRiferimento As Integer, vecchie As List(Of RigaVirtuale)) As Integer
        Try
            If elenco Is Nothing OrElse elenco.Count = 0 OrElse riferimento Is Nothing Then Return -1
            If String.IsNullOrWhiteSpace(riferimento.Testo) AndAlso String.IsNullOrEmpty(riferimento.Indirizzo) Then Return -1
            For livello As Integer = 1 To 3
                Dim chiave As String = riferimento.Impronta(livello)
                Dim candidati As New List(Of Integer)()
                For i As Integer = 0 To elenco.Count - 1
                    If String.Equals(elenco(i).Impronta(livello), chiave, StringComparison.Ordinal) Then candidati.Add(i)
                Next
                If candidati.Count = 0 Then Continue For
                If candidati.Count = 1 Then Return candidati(0)
                If vecchie IsNot Nothing AndAlso indiceRiferimento >= 0 AndAlso indiceRiferimento < vecchie.Count Then
                    Dim ordinale As Integer = 0
                    Dim totaleVecchie As Integer = 0
                    For j As Integer = 0 To vecchie.Count - 1
                        If Not String.Equals(vecchie(j).Impronta(livello), chiave, StringComparison.Ordinal) Then Continue For
                        If j < indiceRiferimento Then ordinale += 1
                        totaleVecchie += 1
                    Next
                    If totaleVecchie = candidati.Count Then Return candidati(Math.Min(ordinale, candidati.Count - 1))
                End If
                Dim migliore As Integer = candidati(0)
                If indiceRiferimento >= 0 Then
                    For Each c As Integer In candidati
                        If Math.Abs(c - indiceRiferimento) < Math.Abs(migliore - indiceRiferimento) Then migliore = c
                    Next
                End If
                Return migliore
            Next
            Return -1
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGCursorVirtuale.TrovaEquivalente: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function TrovaRigaUguale(riga As RigaVirtuale, indiceVecchio As Integer) As Integer
        Try
            If riga Is Nothing OrElse righeVirtuali.Count = 0 Then Return -1
            If riga.IdElemento > 0 Then
                For i As Integer = 0 To righeVirtuali.Count - 1
                    If righeVirtuali(i).StessoElemento(riga.Sessione, riga.IdElemento) AndAlso String.Equals(righeVirtuali(i).Testo, riga.Testo, StringComparison.Ordinal) Then Return i
                Next
                Dim equivalente As Integer = TrovaEquivalente(righeVirtuali, riga, indiceVecchio, Nothing)
                If equivalente >= 0 AndAlso String.Equals(righeVirtuali(equivalente).Testo, riga.Testo, StringComparison.Ordinal) Then Return equivalente
                Return -1
            End If
            Dim migliore As Integer = -1
            For i As Integer = 0 To righeVirtuali.Count - 1
                If Not String.Equals(righeVirtuali(i).Testo, riga.Testo, StringComparison.Ordinal) Then Continue For
                If migliore < 0 OrElse Math.Abs(i - indiceVecchio) < Math.Abs(migliore - indiceVecchio) Then migliore = i
            Next
            Return migliore
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TrovaRigaUguale: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Function TrovaIndice(sessione As String, idElemento As Integer, testo As String, indiceDiRiserva As Integer, Optional riferimento As RigaVirtuale = Nothing, Optional vecchie As List(Of RigaVirtuale) = Nothing) As Integer
        Try
            If righeVirtuali.Count = 0 Then Return -1
            If idElemento > 0 Then
                Dim primoStessoId As Integer = -1
                For i As Integer = 0 To righeVirtuali.Count - 1
                    If righeVirtuali(i).StessoElemento(sessione, idElemento) Then
                        If primoStessoId < 0 Then primoStessoId = i
                        If String.Equals(righeVirtuali(i).Testo, testo, StringComparison.Ordinal) Then Return i
                    End If
                Next
                If primoStessoId >= 0 Then Return primoStessoId
            End If
            If riferimento IsNot Nothing Then
                Dim equivalente As Integer = TrovaEquivalente(righeVirtuali, riferimento, indiceDiRiserva, vecchie)
                If equivalente >= 0 Then Return equivalente
            End If
            If indiceDiRiserva < 0 Then Return -1
            Return Math.Min(indiceDiRiserva, righeVirtuali.Count - 1)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TrovaIndice: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Sub InserisciAgente()
        Try
            If paginaWeb Is Nothing OrElse paginaWeb.IsDisposed OrElse Not paginaWeb.Pronta Then Return
            Dim script As String = ScriptAgente
            If String.IsNullOrEmpty(script) Then Return
            paginaWeb.EseguiScript(script)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.InserisciAgente: " & ex.Message)
        End Try
    End Sub

    ' Attende il contenuto aggiornato solo quando serve: subito dopo un'azione dell'utente o vicino alla fine della pagina,
    ' dove i siti caricano altri elementi. Sui siti che cambiano di continuo (video, commenti) le frecce non si fermano:
    ' l'aggiornamento parte comunque e il cursore si riallinea appena è pronto.
    Private Function AttesaNecessaria() As Boolean
        If (DateTime.Now - _istanteUltimaAzione).TotalMilliseconds < 3000 Then Return True
        Return indiceRigaVirtuale < 0 OrElse indiceRigaVirtuale >= righeVirtuali.Count - 3
    End Function

    ' Vero mentre l'utente preme i tasti di spostamento in rapida successione (H, K, frecce tenute premute):
    ' in quei momenti la pagina non si ricostruisce e i salti usano le righe già pronte.
    Public ReadOnly Property NavigazioneRapida As Boolean
        Get
            Return (DateTime.Now - _istanteUltimoSpostamento).TotalMilliseconds < 450
        End Get
    End Property

    Public Async Function AssicuraBufferAsync() As Task
        Try
            ProseguiTraduzione()
            Dim rapida As Boolean = NavigazioneRapida
            _istanteUltimoSpostamento = DateTime.Now
            If righeVirtuali.Count = 0 Then
                Await AggiornaAsync(False)
            ElseIf rapida AndAlso (DateTime.Now - _istanteUltimaAzione).TotalMilliseconds >= 3000 Then
                Return
            ElseIf BufferSporco AndAlso (DateTime.Now - ultimoAggiornamento).TotalMilliseconds > 1200 Then
                Dim compito As Task(Of Boolean) = AggiornaAsync(False)
                If AttesaNecessaria() Then Await Task.WhenAny(compito, Task.Delay(700))
            ElseIf AggiornamentoInCorso AndAlso AttesaNecessaria() Then
                Await Task.WhenAny(aggiornaInCorso, Task.Delay(400))
            End If
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.AssicuraBufferAsync: " & ex.Message)
        End Try
    End Function

    Public Sub Reset()
        Try
            righeVirtuali = New List(Of RigaVirtuale)()
            indiceRigaVirtuale = -1
            _suContestoIngresso = False
            AnnullaSelezione()
            posizioneCarattere = -1
            idModaleCorrente = 0
            idPrimaDelModale = 0
            _cellaCorrente = Nothing
            Tabelle = New Dictionary(Of String, TabellaVirtuale)()
            LayoutPrincipale = Nothing
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.Reset: " & ex.ToString())
        End Try
    End Sub

    Private Function TestoConLingua(riga As RigaVirtuale, testo As String) As String
        Try
            If Not _annunciaLingua OrElse riga Is Nothing Then Return testo
            Dim linguaRiga As String = If(riga.TestoOriginale <> "", LinguaDestinazioneTraduzione, If(riga.Lingua <> "", riga.Lingua, LinguaPagina))
            If linguaRiga = "" Then Return testo
            If _ultimaLinguaLetta = "" Then
                _ultimaLinguaLetta = linguaRiga
                Return testo
            End If
            If WGLayoutPagina.StessaLingua(linguaRiga, _ultimaLinguaLetta) Then Return testo
            _ultimaLinguaLetta = linguaRiga
            Dim nome As String = WGLayoutPagina.NomeLingua(linguaRiga)
            If nome = "" Then Return testo
            Return "In " & nome & ": " & testo
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TestoConLingua: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Sub SegnaBordo(direzione As Integer)
        Try
            _bordoRaggiunto = direzione
            _indiceBordo = indiceRigaVirtuale
            _carattereBordo = posizioneCarattere
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SegnaBordo: " & ex.Message)
        End Try
    End Sub

    Private Function TornaDalBordo(direzione As Integer) As Boolean
        Try
            Dim bordo As Integer = _bordoRaggiunto
            _bordoRaggiunto = 0
            If bordo = 0 OrElse bordo <> direzione OrElse _suContestoIngresso Then Return False
            If indiceRigaVirtuale <> _indiceBordo OrElse posizioneCarattere <> _carattereBordo Then Return False
            If bordo > 0 AndAlso indiceRigaVirtuale <> righeVirtuali.Count - 1 Then Return False
            If bordo < 0 AndAlso indiceRigaVirtuale <> 0 Then Return False
            LeggiCorrente(True)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TornaDalBordo: " & ex.Message)
            Return False
        End Try
    End Function

    Public Async Function LeggiSuccessivaAsync() As Task
        Try
            Await AssicuraBufferAsync()
            If righeVirtuali.Count = 0 Then
                lettura.LeggiTesto("Pagina vuota")
                Return
            End If
            If TornaDalBordo(-1) Then Return
            If _suContestoIngresso AndAlso indiceRigaVirtuale >= 0 AndAlso indiceRigaVirtuale < righeVirtuali.Count Then
                _suContestoIngresso = False
                LeggiCorrente(True)
                Return
            End If
            If indiceRigaVirtuale < righeVirtuali.Count - 1 Then
                SpostaLeggendoIngresso(indiceRigaVirtuale + 1)
            Else
                SegnaBordo(1)
                lettura.LeggiTesto("Fine pagina")
            End If
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.LeggiSuccessivaAsync: " & ex.ToString())
        End Try
    End Function

    Public Async Function LeggiPrecedenteAsync() As Task
        Try
            Await AssicuraBufferAsync()
            If righeVirtuali.Count = 0 Then
                lettura.LeggiTesto("Pagina vuota")
                Return
            End If
            If TornaDalBordo(1) Then Return
            If Not _suContestoIngresso AndAlso RegioniAParte AndAlso indiceRigaVirtuale >= 0 AndAlso indiceRigaVirtuale < righeVirtuali.Count AndAlso righeVirtuali(indiceRigaVirtuale).HaContestoIngresso Then
                SpostaSuContesto(indiceRigaVirtuale)
                Return
            End If
            If indiceRigaVirtuale > 0 Then
                SpostaA(indiceRigaVirtuale - 1, True)
            ElseIf indiceRigaVirtuale < 0 Then
                SpostaLeggendoIngresso(0)
            Else
                SegnaBordo(-1)
                lettura.LeggiTesto("Inizio pagina")
            End If
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.LeggiPrecedenteAsync: " & ex.ToString())
        End Try
    End Function

    Public Async Function SaltaRigheAsync(quante As Integer) As Task
        Try
            Await AssicuraBufferAsync()
            If righeVirtuali.Count = 0 Then Return
            Dim nuovo As Integer = Math.Max(0, Math.Min(righeVirtuali.Count - 1, Math.Max(indiceRigaVirtuale, 0) + quante))
            If nuovo = indiceRigaVirtuale Then
                SegnaBordo(If(quante > 0, 1, -1))
                lettura.LeggiTesto(If(quante > 0, "Fine pagina", "Inizio pagina"))
                Return
            End If
            SpostaLeggendoIngresso(nuovo)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SaltaRigheAsync: " & ex.ToString())
        End Try
    End Function

    Public Async Function VaiAllInizioAsync() As Task
        Try
            Await AssicuraBufferAsync()
            If righeVirtuali.Count = 0 Then Return
            SpostaLeggendoIngresso(0)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.VaiAllInizioAsync: " & ex.ToString())
        End Try
    End Function

    Public Async Function VaiAllaFineAsync() As Task
        Try
            Await AssicuraBufferAsync()
            If righeVirtuali.Count = 0 Then Return
            SpostaA(righeVirtuali.Count - 1, True)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.VaiAllaFineAsync: " & ex.ToString())
        End Try
    End Function

    Public Sub SpostaA(indice As Integer, leggi As Boolean)
        Try
            If indice < 0 OrElse indice >= righeVirtuali.Count Then Return
            AnnullaSelezione()
            indiceRigaVirtuale = indice
            posizioneCarattere = -1
            _cellaCorrente = Nothing
            _suContestoIngresso = False
            _bordoRaggiunto = 0
            Dim riga As RigaVirtuale = righeVirtuali(indice)
            If cdpInterazione IsNot Nothing Then cdpInterazione.MostraRiga(riga)
            If leggi Then LeggiCorrente(True)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SpostaA: " & ex.ToString())
        End Try
    End Sub

    Public Sub SpostaLeggendoIngresso(indice As Integer)
        Try
            If indice < 0 OrElse indice >= righeVirtuali.Count Then Return
            If RegioniAParte AndAlso righeVirtuali(indice).HaContestoIngresso Then
                SpostaSuContesto(indice)
            Else
                SpostaA(indice, True)
            End If
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SpostaLeggendoIngresso: " & ex.ToString())
        End Try
    End Sub

    Public Sub SpostaSuContesto(indice As Integer)
        Try
            If indice < 0 OrElse indice >= righeVirtuali.Count Then Return
            AnnullaSelezione()
            indiceRigaVirtuale = indice
            posizioneCarattere = -1
            _cellaCorrente = Nothing
            _bordoRaggiunto = 0
            Dim riga As RigaVirtuale = righeVirtuali(indice)
            _suContestoIngresso = riga.HaContestoIngresso
            If cdpInterazione IsNot Nothing Then cdpInterazione.MostraRiga(riga)
            LeggiCorrente(True)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SpostaSuContesto: " & ex.ToString())
        End Try
    End Sub

    Public Sub SpostaAConContesto(indice As Integer, contesto As ContestoLettura)
        Try
            If indice < 0 OrElse indice >= righeVirtuali.Count Then Return
            If Not RegioniAParte Then
                SpostaA(indice, True)
                Return
            End If
            Dim riga As RigaVirtuale = righeVirtuali(indice)
            If contesto = ContestoLettura.SoloIngresso AndAlso riga.HaContestoIngresso Then
                SpostaSuContesto(indice)
                Return
            End If
            SpostaA(indice, False)
            Dim testo As String
            Select Case contesto
                Case ContestoLettura.Tutto
                    testo = riga.CostruisciAnnuncio(True)
                Case ContestoLettura.Contenitori, ContestoLettura.SoloIngresso
                    testo = riga.CostruisciAnnuncioParti(False, True)
                Case Else
                    testo = riga.CostruisciAnnuncioParti(False, False)
            End Select
            If testo.Length > 1500 Then testo = testo.Substring(0, 1500)
            lettura.LeggiTesto(TestoConLingua(riga, testo))
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SpostaAConContesto: " & ex.ToString())
        End Try
    End Sub

    Public Sub EsciDalContesto(leggi As Boolean)
        Try
            If Not _suContestoIngresso Then Return
            _suContestoIngresso = False
            If leggi Then LeggiCorrente(True)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EsciDalContesto: " & ex.Message)
        End Try
    End Sub

    Public Sub LeggiCorrente(Optional conContesto As Boolean = False)
        Try
            If indiceRigaVirtuale < 0 OrElse indiceRigaVirtuale >= righeVirtuali.Count Then Return
            Dim riga As RigaVirtuale = righeVirtuali(indiceRigaVirtuale)
            Dim testo As String = ""
            If _suContestoIngresso Then testo = riga.TestoContestoIngresso()
            If testo = "" Then
                If conContesto AndAlso RegioniAParte Then
                    testo = riga.CostruisciAnnuncioParti(False, True)
                Else
                    testo = riga.CostruisciAnnuncio(conContesto)
                End If
            End If
            If testo.Length > 1500 Then testo = testo.Substring(0, 1500)
            lettura.LeggiTesto(TestoConLingua(riga, testo))
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.LeggiCorrente: " & ex.ToString())
        End Try
    End Sub

    Public Function TrovaRiga(condizione As Func(Of RigaVirtuale, Boolean), indietro As Boolean, Optional distinguiAttivazione As Boolean = False, Optional includiRigaDelContesto As Boolean = True, Optional dalEstremo As Boolean = False) As Integer
        Try
            If righeVirtuali.Count = 0 Then Return -1
            Dim corrente As RigaVirtuale = RigaCorrente
            Dim idCorrente As Integer = If(corrente IsNot Nothing, corrente.IdDaAttivare, 0)
            If dalEstremo Then
                If indietro Then
                    For i As Integer = righeVirtuali.Count - 1 To 0 Step -1
                        If condizione(righeVirtuali(i)) Then Return i
                    Next
                Else
                    For i As Integer = 0 To righeVirtuali.Count - 1
                        If condizione(righeVirtuali(i)) Then Return i
                    Next
                End If
                Return -1
            End If
            If _suContestoIngresso AndAlso includiRigaDelContesto AndAlso Not indietro AndAlso indiceRigaVirtuale >= 0 AndAlso indiceRigaVirtuale < righeVirtuali.Count AndAlso condizione(righeVirtuali(indiceRigaVirtuale)) Then
                Return indiceRigaVirtuale
            End If
            If indietro Then
                Dim inizio As Integer = If(indiceRigaVirtuale < 0, -1, indiceRigaVirtuale - 1)
                For i As Integer = inizio To 0 Step -1
                    Dim r As RigaVirtuale = righeVirtuali(i)
                    If Not condizione(r) Then Continue For
                    If distinguiAttivazione AndAlso r.IdDaAttivare = idCorrente Then Continue For
                    If distinguiAttivazione Then
                        While i > 0 AndAlso righeVirtuali(i - 1).IdDaAttivare = r.IdDaAttivare AndAlso condizione(righeVirtuali(i - 1))
                            i -= 1
                        End While
                    End If
                    Return i
                Next
            Else
                For i As Integer = indiceRigaVirtuale + 1 To righeVirtuali.Count - 1
                    Dim r As RigaVirtuale = righeVirtuali(i)
                    If Not condizione(r) Then Continue For
                    If distinguiAttivazione AndAlso r.IdDaAttivare = idCorrente Then Continue For
                    Return i
                Next
            End If
            Return -1
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TrovaRiga: " & ex.ToString())
            Return -1
        End Try
    End Function

    Public Async Function AttivaCorrenteAsync() As Task(Of RigaVirtuale)
        Try
            Dim riga As RigaVirtuale = RigaCorrente
            If riga Is Nothing Then Return Nothing
            If riga.IsCampo Then Return riga
            _istanteUltimaAzione = DateTime.Now
            Dim riuscito As Boolean = Await cdpInterazione.AttivaRigaAsync(riga)
            If Not riuscito Then riuscito = Await RiprovaSuRigaEquivalenteAsync(riga)
            If Not riuscito Then
                lettura.LeggiTesto("Elemento non più presente nella pagina")
                BufferSporco = True
            End If
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.AttivaCorrenteAsync: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ' Se l'attivazione non è avvenuta (nodo scaduto dopo che il sito ha ridisegnato la pagina), ricostruisce l'albero,
    ' ritrova lo stesso elemento dall'impronta e riprova una sola volta. Non ripete mai un clic già eseguito.
    Private Async Function RiprovaSuRigaEquivalenteAsync(riga As RigaVirtuale) As Task(Of Boolean)
        Try
            Dim vecchie As List(Of RigaVirtuale) = righeVirtuali
            Dim indiceVecchio As Integer = indiceRigaVirtuale
            Dim compito As Task(Of Boolean) = AggiornaAsync(False)
            Dim terminato As Task = Await Task.WhenAny(compito, Task.Delay(2500))
            If Not Object.ReferenceEquals(terminato, compito) OrElse Not compito.IsCompleted OrElse compito.IsFaulted OrElse Not compito.Result Then Return False
            Dim indice As Integer = TrovaEquivalente(righeVirtuali, riga, indiceVecchio, vecchie)
            If indice < 0 Then Return False
            indiceRigaVirtuale = indice
            posizioneCarattere = -1
            Return Await cdpInterazione.AttivaRigaAsync(righeVirtuali(indice))
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.RiprovaSuRigaEquivalenteAsync: " & ex.Message)
            Return False
        End Try
    End Function

    Public Function SincronizzaConNodo(sessione As String, idElemento As Integer) As Boolean
        Try
            Dim indice As Integer = IndiceDiNodo(sessione, idElemento)
            If indice < 0 Then
                For i As Integer = 0 To righeVirtuali.Count - 1
                    If righeVirtuali(i).StessoDaAttivare(sessione, idElemento) Then
                        indice = i
                        Exit For
                    End If
                Next
            End If
            If indice < 0 Then Return False
            AnnullaSelezione()
            indiceRigaVirtuale = indice
            posizioneCarattere = -1
            _cellaCorrente = Nothing
            _suContestoIngresso = False
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SincronizzaConNodo: " & ex.ToString())
            Return False
        End Try
    End Function

    Public Function IndiceDiNodo(sessione As String, idElemento As Integer) As Integer
        Try
            If idElemento <= 0 Then Return -1
            For i As Integer = 0 To righeVirtuali.Count - 1
                If righeVirtuali(i).StessoElemento(sessione, idElemento) Then Return i
            Next
            Return -1
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.IndiceDiNodo: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Function IndiceDiNodoOAttivazione(sessione As String, idElemento As Integer) As Integer
        Try
            Dim indice As Integer = IndiceDiNodo(sessione, idElemento)
            If indice >= 0 OrElse idElemento <= 0 Then Return indice
            For i As Integer = 0 To righeVirtuali.Count - 1
                If righeVirtuali(i).StessoDaAttivare(sessione, idElemento) Then Return i
            Next
            Return -1
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.IndiceDiNodoOAttivazione: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Function CellaDiNodo(sessione As String, idNodo As Integer) As Tuple(Of TabellaVirtuale, CellaVirtuale)
        Try
            If idNodo <= 0 Then Return Nothing
            Dim prefisso As String = If(sessione, "") & "|"
            For Each tabella As TabellaVirtuale In Tabelle.Values
                If Not tabella.Id.StartsWith(prefisso, StringComparison.Ordinal) Then Continue For
                Dim cella As CellaVirtuale = Nothing
                If tabella.PerNodo.TryGetValue(idNodo, cella) Then Return Tuple.Create(tabella, cella)
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.CellaDiNodo: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Public Function IndiceDestinazione(idNodo As Integer) As Integer
        Try
            If idNodo <= 0 Then Return -1
            Dim layout As LayoutDocumento = LayoutPrincipale
            For i As Integer = 0 To righeVirtuali.Count - 1
                Dim r As RigaVirtuale = righeVirtuali(i)
                If r.Sessione <> "" Then Continue For
                If r.IdElemento = idNodo OrElse r.IdAttivazione = idNodo Then Return i
                If layout IsNot Nothing AndAlso layout.EAntenato(idNodo, r.IdElemento) Then Return i
            Next
            If layout Is Nothing Then Return -1
            Dim ordine As Integer = layout.OrdineDocumento(idNodo)
            If ordine < 0 Then Return -1
            For i As Integer = 0 To righeVirtuali.Count - 1
                Dim r As RigaVirtuale = righeVirtuali(i)
                If r.Sessione = "" AndAlso r.OrdineDocumento >= ordine Then Return i
            Next
            Return -1
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.IndiceDestinazione: " & ex.Message)
            Return -1
        End Try
    End Function

    Public Function ContieneRiga(riga As RigaVirtuale) As Boolean
        Try
            If riga Is Nothing Then Return False
            Return IndiceDiNodo(riga.Sessione, riga.IdElemento) >= 0 OrElse (riga.IdAttivazione > 0 AndAlso IndiceDiNodo(riga.Sessione, riga.IdAttivazione) >= 0)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.ContieneRiga: " & ex.Message)
            Return False
        End Try
    End Function

    Public Sub LeggiCarattere(avanti As Boolean)
        Try
            _suContestoIngresso = False
            AnnullaSelezione()
            Dim riga As RigaVirtuale = RigaCorrente
            If riga Is Nothing Then Return
            Dim testo As String = riga.Testo
            If String.IsNullOrEmpty(testo) Then Return
            If avanti Then
                If posizioneCarattere >= testo.Length - 1 Then
                    lettura.LeggiTesto("Fine riga")
                    posizioneCarattere = testo.Length - 1
                    Return
                End If
                posizioneCarattere += 1
            Else
                If posizioneCarattere <= 0 Then
                    lettura.LeggiTesto("Inizio riga")
                    posizioneCarattere = 0
                    Return
                End If
                posizioneCarattere -= 1
            End If
            lettura.LeggiTesto(WGCaratteri.NomeCarattere(testo(posizioneCarattere), True))
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.LeggiCarattere: " & ex.ToString())
        End Try
    End Sub

    Public Sub LeggiParola(avanti As Boolean)
        Try
            _suContestoIngresso = False
            AnnullaSelezione()
            Dim riga As RigaVirtuale = RigaCorrente
            If riga Is Nothing Then Return
            Dim testo As String = riga.Testo
            If String.IsNullOrEmpty(testo) Then Return
            Dim p As Integer = Math.Max(0, posizioneCarattere)
            If avanti Then
                If posizioneCarattere < 0 Then
                    p = 0
                Else
                    While p < testo.Length AndAlso Not Char.IsWhiteSpace(testo(p))
                        p += 1
                    End While
                    While p < testo.Length AndAlso Char.IsWhiteSpace(testo(p))
                        p += 1
                    End While
                End If
                If p >= testo.Length Then
                    lettura.LeggiTesto("Fine riga")
                    Return
                End If
            Else
                If p <= 0 Then
                    lettura.LeggiTesto("Inizio riga")
                    Return
                End If
                p -= 1
                While p > 0 AndAlso Char.IsWhiteSpace(testo(p))
                    p -= 1
                End While
                While p > 0 AndAlso Not Char.IsWhiteSpace(testo(p - 1))
                    p -= 1
                End While
            End If
            posizioneCarattere = p
            Dim fine As Integer = p
            While fine < testo.Length AndAlso Not Char.IsWhiteSpace(testo(fine))
                fine += 1
            End While
            lettura.LeggiTesto(testo.Substring(p, fine - p))
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.LeggiParola: " & ex.ToString())
        End Try
    End Sub

    Public Function Cerca(testoCercato As String, Optional indietro As Boolean = False) As Integer
        Try
            If String.IsNullOrWhiteSpace(testoCercato) OrElse righeVirtuali.Count = 0 Then Return -1
            Dim n As Integer = righeVirtuali.Count
            Dim partenza As Integer = indiceRigaVirtuale
            If partenza < 0 Then partenza = If(indietro, n, -1)
            For passo As Integer = 1 To n
                Dim i As Integer = If(indietro, partenza - passo, partenza + passo) Mod n
                If i < 0 Then i += n
                Dim r As RigaVirtuale = righeVirtuali(i)
                If r.Testo.IndexOf(testoCercato, StringComparison.CurrentCultureIgnoreCase) >= 0 OrElse r.Valore.IndexOf(testoCercato, StringComparison.CurrentCultureIgnoreCase) >= 0 Then Return i
            Next
            Return -1
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.Cerca: " & ex.ToString())
            Return -1
        End Try
    End Function

    ' Ricerca per occorrenza: una riga lunga può contenere il testo più volte, e la ricerca successiva
    ' riparte dal punto trovato prima nella stessa riga invece di saltare alla riga dopo.
    Private _ricercaRiga As Integer = -1
    Private _ricercaCarattere As Integer = -1

    Private Shared Function PosizioneIn(testo As String, cercato As String, inizio As Integer) As Integer
        If String.IsNullOrEmpty(testo) OrElse inizio >= testo.Length Then Return -1
        Return System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(testo, cercato, Math.Max(0, inizio), System.Globalization.CompareOptions.IgnoreCase)
    End Function

    Private Shared Function UltimaPosizioneIn(testo As String, cercato As String, primaDi As Integer) As Integer
        Dim ultima As Integer = -1
        Dim p As Integer = PosizioneIn(testo, cercato, 0)
        While p >= 0 AndAlso p < primaDi
            ultima = p
            p = PosizioneIn(testo, cercato, p + 1)
        End While
        Return ultima
    End Function

    Private Shared Function OccorrenzeIn(testo As String, cercato As String) As Integer
        Dim n As Integer = 0
        Dim p As Integer = PosizioneIn(testo, cercato, 0)
        While p >= 0
            n += 1
            p = PosizioneIn(testo, cercato, p + cercato.Length)
        End While
        Return n
    End Function

    ''' <summary>
    ''' Occorrenza successiva o precedente: riga e posizione del carattere nel testo della riga
    ''' (-1 se il testo è stato trovato nel valore di un campo). Nothing se non c'è.
    ''' </summary>
    Public Function CercaOccorrenza(testoCercato As String, indietro As Boolean) As Tuple(Of Integer, Integer)
        Try
            Dim n As Integer = righeVirtuali.Count
            If String.IsNullOrWhiteSpace(testoCercato) OrElse n = 0 Then Return Nothing
            Dim rigaPartenza As Integer = indiceRigaVirtuale
            If rigaPartenza >= 0 AndAlso rigaPartenza < n AndAlso rigaPartenza = _ricercaRiga AndAlso _ricercaCarattere >= 0 Then
                Dim testo As String = righeVirtuali(rigaPartenza).Testo
                Dim stessa As Integer = If(indietro, UltimaPosizioneIn(testo, testoCercato, _ricercaCarattere), PosizioneIn(testo, testoCercato, _ricercaCarattere + 1))
                If stessa >= 0 Then Return RicordaRicerca(rigaPartenza, stessa)
            End If
            If rigaPartenza < 0 Then rigaPartenza = If(indietro, n, -1)
            For passo As Integer = 1 To n
                Dim i As Integer = If(indietro, rigaPartenza - passo, rigaPartenza + passo) Mod n
                If i < 0 Then i += n
                Dim r As RigaVirtuale = righeVirtuali(i)
                Dim posizione As Integer = If(indietro, UltimaPosizioneIn(r.Testo, testoCercato, Integer.MaxValue), PosizioneIn(r.Testo, testoCercato, 0))
                If posizione >= 0 Then Return RicordaRicerca(i, posizione)
                If PosizioneIn(r.Valore, testoCercato, 0) >= 0 Then Return RicordaRicerca(i, -1)
            Next
            Return Nothing
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.CercaOccorrenza: " & ex.ToString())
            Return Nothing
        End Try
    End Function

    Private Function RicordaRicerca(riga As Integer, carattere As Integer) As Tuple(Of Integer, Integer)
        _ricercaRiga = riga
        _ricercaCarattere = carattere
        Return Tuple.Create(riga, carattere)
    End Function

    ''' <summary>Quante volte compare il testo nella pagina e quale è l'occorrenza indicata (1 = la prima).</summary>
    Public Function ContaOccorrenze(testoCercato As String, riga As Integer, carattere As Integer) As Tuple(Of Integer, Integer)
        Dim totale As Integer = 0
        Dim ordinale As Integer = 0
        Try
            For i As Integer = 0 To righeVirtuali.Count - 1
                Dim r As RigaVirtuale = righeVirtuali(i)
                Dim nelTesto As Integer = OccorrenzeIn(r.Testo, testoCercato)
                Dim contate As Integer = If(nelTesto > 0, nelTesto, If(PosizioneIn(r.Valore, testoCercato, 0) >= 0, 1, 0))
                If i < riga Then
                    ordinale += contate
                ElseIf i = riga Then
                    ordinale += If(carattere >= 0, OccorrenzeIn(r.Testo.Substring(0, Math.Min(carattere, r.Testo.Length)), testoCercato), 0) + 1
                End If
                totale += contate
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.ContaOccorrenze: " & ex.Message)
        End Try
        Return Tuple.Create(totale, Math.Min(ordinale, totale))
    End Function

    ''' <summary>Porta il cursore dei caratteri appena prima della posizione: la freccia destra legge da lì.</summary>
    Public Sub PosizionaCarattere(posizione As Integer)
        posizioneCarattere = Math.Max(-1, posizione - 1)
    End Sub

    Public Function TestoPagina(Optional daIndice As Integer = 0) As String
        Dim testo As New StringBuilder()
        Try
            For i As Integer = Math.Max(0, daIndice) To righeVirtuali.Count - 1
                testo.AppendLine(righeVirtuali(i).CostruisciAnnuncio(False))
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TestoPagina: " & ex.Message)
        End Try
        Return testo.ToString()
    End Function

    Private Function TestoSelezionabile(indice As Integer) As String
        Try
            If indice < 0 OrElse indice >= righeVirtuali.Count Then Return ""
            Dim r As RigaVirtuale = righeVirtuali(indice)
            If Not String.IsNullOrEmpty(r.Testo) Then Return r.Testo
            If r.Password Then Return ""
            Return If(r.Valore, "").Replace(vbCr, "").Replace(vbLf, " ")
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TestoSelezionabile: " & ex.Message)
            Return ""
        End Try
    End Function

    Public ReadOnly Property SelezioneAttiva As Boolean
        Get
            Return _selAncoraRiga >= 0 AndAlso _selFuocoRiga >= 0 AndAlso (_selAncoraRiga <> _selFuocoRiga OrElse _selAncoraCarattere <> _selFuocoCarattere)
        End Get
    End Property

    Public Sub AnnullaSelezione()
        Try
            _selAncoraRiga = -1
            _selAncoraCarattere = 0
            _selFuocoRiga = -1
            _selFuocoCarattere = 0
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.AnnullaSelezione: " & ex.Message)
        End Try
    End Sub

    Private Shared Function ConfrontaPosizioni(riga1 As Integer, carattere1 As Integer, riga2 As Integer, carattere2 As Integer) As Integer
        Try
            If riga1 <> riga2 Then Return If(riga1 < riga2, -1, 1)
            If carattere1 = carattere2 Then Return 0
            Return If(carattere1 < carattere2, -1, 1)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGCursorVirtuale.ConfrontaPosizioni: " & ex.Message)
            Return 0
        End Try
    End Function

    Private Function TestoTraPosizioni(rigaInizio As Integer, carattereInizio As Integer, rigaFine As Integer, carattereFine As Integer) As String
        Dim risultato As New StringBuilder()
        Try
            For r As Integer = Math.Max(0, rigaInizio) To Math.Min(rigaFine, righeVirtuali.Count - 1)
                Dim t As String = TestoSelezionabile(r)
                Dim da As Integer = If(r = rigaInizio, Math.Max(0, Math.Min(carattereInizio, t.Length)), 0)
                Dim a As Integer = If(r = rigaFine, Math.Max(0, Math.Min(carattereFine, t.Length)), t.Length)
                If a > da Then risultato.Append(t.Substring(da, a - da))
                If r < rigaFine Then risultato.Append(vbCrLf)
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TestoTraPosizioni: " & ex.Message)
        End Try
        Return risultato.ToString()
    End Function

    Public Function TestoSelezione() As String
        Try
            If Not SelezioneAttiva Then Return ""
            If ConfrontaPosizioni(_selAncoraRiga, _selAncoraCarattere, _selFuocoRiga, _selFuocoCarattere) <= 0 Then
                Return TestoTraPosizioni(_selAncoraRiga, _selAncoraCarattere, _selFuocoRiga, _selFuocoCarattere)
            End If
            Return TestoTraPosizioni(_selFuocoRiga, _selFuocoCarattere, _selAncoraRiga, _selAncoraCarattere)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TestoSelezione: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Sub AssicuraAncoraSelezione()
        Try
            If _selAncoraRiga >= 0 AndAlso _selFuocoRiga >= 0 Then Return
            Dim riga As Integer = Math.Max(0, Math.Min(indiceRigaVirtuale, righeVirtuali.Count - 1))
            Dim carattere As Integer = Math.Max(0, Math.Min(posizioneCarattere, TestoSelezionabile(riga).Length))
            _selAncoraRiga = riga
            _selAncoraCarattere = carattere
            _selFuocoRiga = riga
            _selFuocoCarattere = carattere
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.AssicuraAncoraSelezione: " & ex.Message)
        End Try
    End Sub

    Private Sub SpostaFuocoSelezione(nuovaRiga As Integer, nuovoCarattere As Integer)
        Try
            If righeVirtuali.Count = 0 Then Return
            nuovaRiga = Math.Max(0, Math.Min(nuovaRiga, righeVirtuali.Count - 1))
            nuovoCarattere = Math.Max(0, Math.Min(nuovoCarattere, TestoSelezionabile(nuovaRiga).Length))
            Dim vecchiaRiga As Integer = _selFuocoRiga
            Dim vecchioCarattere As Integer = _selFuocoCarattere
            Dim confronto As Integer = ConfrontaPosizioni(vecchiaRiga, vecchioCarattere, nuovaRiga, nuovoCarattere)
            If confronto = 0 Then Return
            Dim attraversato As String = If(confronto < 0, TestoTraPosizioni(vecchiaRiga, vecchioCarattere, nuovaRiga, nuovoCarattere), TestoTraPosizioni(nuovaRiga, nuovoCarattere, vecchiaRiga, vecchioCarattere))
            Dim vecchioDallAncora As Integer = ConfrontaPosizioni(_selAncoraRiga, _selAncoraCarattere, vecchiaRiga, vecchioCarattere)
            Dim nuovoDallAncora As Integer = ConfrontaPosizioni(_selAncoraRiga, _selAncoraCarattere, nuovaRiga, nuovoCarattere)
            _selFuocoRiga = nuovaRiga
            _selFuocoCarattere = nuovoCarattere
            indiceRigaVirtuale = nuovaRiga
            _suContestoIngresso = False
            _cellaCorrente = Nothing
            Dim lunghezza As Integer = TestoSelezionabile(nuovaRiga).Length
            posizioneCarattere = If(lunghezza = 0, -1, Math.Min(nuovoCarattere, lunghezza - 1))
            If cdpInterazione IsNot Nothing Then cdpInterazione.MostraRiga(righeVirtuali(nuovaRiga))
            Dim annuncio As String
            If Not SelezioneAttiva Then
                annuncio = "Nessuna selezione"
            ElseIf vecchioDallAncora <> 0 AndAlso nuovoDallAncora <> 0 AndAlso vecchioDallAncora <> nuovoDallAncora Then
                annuncio = DescriviTestoSelezionato(TestoSelezione()) & " selezionato"
            Else
                Dim allontana As Boolean = (confronto < 0 AndAlso nuovoDallAncora < 0) OrElse (confronto > 0 AndAlso nuovoDallAncora > 0)
                annuncio = DescriviTestoSelezionato(attraversato) & If(allontana, " selezionato", " deselezionato")
            End If
            lettura.LeggiTesto(annuncio)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SpostaFuocoSelezione: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function DescriviTestoSelezionato(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return "niente"
            If testo = vbCrLf OrElse testo = vbLf Then Return "a capo"
            If testo.Length = 1 Then Return WGCaratteri.NomeCarattere(testo(0), True)
            Dim pulito As String = testo
            If pulito.EndsWith(vbCrLf, StringComparison.Ordinal) Then pulito = pulito.Substring(0, pulito.Length - 2)
            pulito = pulito.Replace(vbCrLf, " a capo ").Trim()
            If pulito = "" Then Return "spazi"
            If pulito.Length > 600 Then pulito = pulito.Substring(0, 600) & ", eccetera"
            Return pulito
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGCursorVirtuale.DescriviTestoSelezionato: " & ex.Message)
            Return If(testo, "")
        End Try
    End Function

    Public Sub EstendiSelezioneCarattere(avanti As Boolean)
        Try
            If righeVirtuali.Count = 0 Then Return
            AssicuraAncoraSelezione()
            Dim r As Integer = _selFuocoRiga
            Dim c As Integer = _selFuocoCarattere
            Dim lunghezza As Integer = TestoSelezionabile(r).Length
            If avanti Then
                If c < lunghezza Then
                    c += 1
                ElseIf r < righeVirtuali.Count - 1 Then
                    r += 1
                    c = 0
                Else
                    lettura.LeggiTesto("Fine pagina")
                    Return
                End If
            Else
                If c > 0 Then
                    c -= 1
                ElseIf r > 0 Then
                    r -= 1
                    c = TestoSelezionabile(r).Length
                Else
                    lettura.LeggiTesto("Inizio pagina")
                    Return
                End If
            End If
            SpostaFuocoSelezione(r, c)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EstendiSelezioneCarattere: " & ex.ToString())
        End Try
    End Sub

    Public Sub EstendiSelezioneParola(avanti As Boolean)
        Try
            If righeVirtuali.Count = 0 Then Return
            AssicuraAncoraSelezione()
            Dim r As Integer = _selFuocoRiga
            Dim c As Integer = _selFuocoCarattere
            Dim testo As String = TestoSelezionabile(r)
            If avanti Then
                If c >= testo.Length Then
                    If r >= righeVirtuali.Count - 1 Then
                        lettura.LeggiTesto("Fine pagina")
                        Return
                    End If
                    r += 1
                    c = 0
                Else
                    While c < testo.Length AndAlso Not Char.IsWhiteSpace(testo(c))
                        c += 1
                    End While
                    While c < testo.Length AndAlso Char.IsWhiteSpace(testo(c))
                        c += 1
                    End While
                End If
            Else
                If c <= 0 Then
                    If r <= 0 Then
                        lettura.LeggiTesto("Inizio pagina")
                        Return
                    End If
                    r -= 1
                    c = TestoSelezionabile(r).Length
                Else
                    While c > 0 AndAlso Char.IsWhiteSpace(testo(c - 1))
                        c -= 1
                    End While
                    While c > 0 AndAlso Not Char.IsWhiteSpace(testo(c - 1))
                        c -= 1
                    End While
                End If
            End If
            SpostaFuocoSelezione(r, c)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EstendiSelezioneParola: " & ex.ToString())
        End Try
    End Sub

    Public Sub EstendiSelezioneRiga(avanti As Boolean)
        Try
            If righeVirtuali.Count = 0 Then Return
            AssicuraAncoraSelezione()
            Dim r As Integer = _selFuocoRiga
            Dim c As Integer = _selFuocoCarattere
            If avanti Then
                If r < righeVirtuali.Count - 1 Then
                    SpostaFuocoSelezione(r + 1, 0)
                ElseIf c < TestoSelezionabile(r).Length Then
                    SpostaFuocoSelezione(r, TestoSelezionabile(r).Length)
                Else
                    lettura.LeggiTesto("Fine pagina")
                End If
            Else
                If c > 0 AndAlso r = 0 Then
                    SpostaFuocoSelezione(0, 0)
                ElseIf r > 0 Then
                    SpostaFuocoSelezione(r - 1, 0)
                Else
                    lettura.LeggiTesto("Inizio pagina")
                End If
            End If
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EstendiSelezioneRiga: " & ex.ToString())
        End Try
    End Sub

    Public Sub EstendiSelezioneAllaRiga(fine As Boolean)
        Try
            If righeVirtuali.Count = 0 Then Return
            AssicuraAncoraSelezione()
            Dim r As Integer = _selFuocoRiga
            Dim destinazione As Integer = If(fine, TestoSelezionabile(r).Length, 0)
            If destinazione = _selFuocoCarattere Then
                lettura.LeggiTesto(If(fine, "Fine riga", "Inizio riga"))
                Return
            End If
            SpostaFuocoSelezione(r, destinazione)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EstendiSelezioneAllaRiga: " & ex.ToString())
        End Try
    End Sub

    Public Sub EstendiSelezioneAllaPagina(fine As Boolean)
        Try
            If righeVirtuali.Count = 0 Then Return
            AssicuraAncoraSelezione()
            Dim r As Integer = If(fine, righeVirtuali.Count - 1, 0)
            Dim c As Integer = If(fine, TestoSelezionabile(r).Length, 0)
            If r = _selFuocoRiga AndAlso c = _selFuocoCarattere Then
                lettura.LeggiTesto(If(fine, "Fine pagina", "Inizio pagina"))
                Return
            End If
            _selFuocoRiga = r
            _selFuocoCarattere = c
            indiceRigaVirtuale = r
            _suContestoIngresso = False
            posizioneCarattere = -1
            If cdpInterazione IsNot Nothing Then cdpInterazione.MostraRiga(righeVirtuali(r))
            lettura.LeggiTesto(If(SelezioneAttiva, If(fine, "Selezionato fino alla fine della pagina", "Selezionato fino all'inizio della pagina"), "Nessuna selezione"))
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EstendiSelezioneAllaPagina: " & ex.ToString())
        End Try
    End Sub

    Public Sub SelezionaTutto()
        Try
            If righeVirtuali.Count = 0 Then
                lettura.LeggiTesto("Pagina vuota")
                Return
            End If
            _selAncoraRiga = 0
            _selAncoraCarattere = 0
            _selFuocoRiga = righeVirtuali.Count - 1
            _selFuocoCarattere = TestoSelezionabile(_selFuocoRiga).Length
            indiceRigaVirtuale = _selFuocoRiga
            _suContestoIngresso = False
            posizioneCarattere = -1
            lettura.LeggiTesto("Tutta la pagina selezionata")
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SelezionaTutto: " & ex.ToString())
        End Try
    End Sub

    Public Function TestoSemplice() As String
        Dim testo As New StringBuilder()
        Try
            For Each r As RigaVirtuale In righeVirtuali
                If String.IsNullOrWhiteSpace(r.Testo) AndAlso String.IsNullOrWhiteSpace(r.Valore) Then Continue For
                If r.Ruolo = "heading" Then testo.AppendLine()
                testo.AppendLine(If(String.IsNullOrWhiteSpace(r.Testo), r.Valore, r.Testo))
            Next
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.TestoSemplice: " & ex.Message)
        End Try
        Return testo.ToString()
    End Function

    Public Function SpostaInTabella(dRiga As Integer, dColonna As Integer) As String
        Try
            Dim riga As RigaVirtuale = RigaCorrente
            Dim tabella As TabellaVirtuale = Nothing
            Dim r As Integer = 0
            Dim c As Integer = 0
            If _cellaCorrente IsNot Nothing AndAlso Tabelle.TryGetValue(_cellaCorrente.Item1, tabella) AndAlso (riga Is Nothing OrElse riga.IdTabella = "" OrElse riga.IdTabella = _cellaCorrente.Item1) Then
                r = _cellaCorrente.Item2
                c = _cellaCorrente.Item3
            ElseIf riga IsNot Nothing AndAlso riga.IdTabella <> "" AndAlso Tabelle.TryGetValue(riga.IdTabella, tabella) Then
                r = riga.RigaTabella
                c = riga.ColonnaTabella
            Else
                Return "Non si è in una tabella"
            End If
            Dim cella As CellaVirtuale = tabella.CellaIn(r, c)
            If cella Is Nothing Then Return "Cella non trovata"
            Dim nuovaRiga As Integer = r
            Dim nuovaColonna As Integer = c
            If dRiga > 0 Then nuovaRiga = cella.Riga + cella.SpanRighe
            If dRiga < 0 Then nuovaRiga = cella.Riga - 1
            If dColonna > 0 Then nuovaColonna = cella.Colonna + cella.SpanColonne
            If dColonna < 0 Then nuovaColonna = cella.Colonna - 1
            Dim destinazione As CellaVirtuale = tabella.CellaIn(nuovaRiga, nuovaColonna)
            If destinazione Is Nothing Then
                If dRiga > 0 Then Return "Ultima riga della tabella"
                If dRiga < 0 Then Return "Prima riga della tabella"
                If dColonna > 0 Then Return "Ultima colonna della tabella"
                Return "Prima colonna della tabella"
            End If
            _cellaCorrente = Tuple.Create(tabella.Id, nuovaRiga, nuovaColonna)
            If destinazione.IndicePrimaRiga >= 0 AndAlso destinazione.IndicePrimaRiga < righeVirtuali.Count Then
                indiceRigaVirtuale = destinazione.IndicePrimaRiga
                posizioneCarattere = -1
                If cdpInterazione IsNot Nothing Then cdpInterazione.MostraRiga(righeVirtuali(indiceRigaVirtuale))
            End If
            Dim contenuto As String = If(String.IsNullOrWhiteSpace(destinazione.Testo), "vuota", destinazione.Testo)
            Dim parti As New List(Of String)()
            If dColonna <> 0 Then
                Dim intestazione As String = tabella.IntestazioneColonna(nuovaColonna, destinazione)
                If intestazione <> "" Then parti.Add(intestazione)
                parti.Add("colonna " & nuovaColonna)
            Else
                Dim intestazione As String = tabella.IntestazioneRiga(nuovaRiga, destinazione)
                If intestazione <> "" Then parti.Add(intestazione)
                parti.Add("riga " & nuovaRiga)
            End If
            Return String.Join(", ", parti) & ": " & contenuto
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.SpostaInTabella: " & ex.ToString())
            Return "Errore nella tabella"
        End Try
    End Function

    Public Function DescriviCellaCorrente() As String
        Try
            Dim riga As RigaVirtuale = RigaCorrente
            If riga Is Nothing OrElse riga.IdTabella = "" Then Return "Non si è in una tabella"
            Dim tabella As TabellaVirtuale = Nothing
            If Not Tabelle.TryGetValue(riga.IdTabella, tabella) Then Return "Non si è in una tabella"
            Dim cella As CellaVirtuale = tabella.CellaIn(riga.RigaTabella, riga.ColonnaTabella)
            Dim parti As New List(Of String)()
            parti.Add("Riga " & riga.RigaTabella & " di " & tabella.Righe & ", colonna " & riga.ColonnaTabella & " di " & tabella.Colonne)
            Dim intestazioneColonna As String = tabella.IntestazioneColonna(riga.ColonnaTabella, cella)
            If intestazioneColonna <> "" Then parti.Add("intestazione di colonna " & intestazioneColonna)
            Dim intestazioneRiga As String = tabella.IntestazioneRiga(riga.RigaTabella, cella)
            If intestazioneRiga <> "" Then parti.Add("intestazione di riga " & intestazioneRiga)
            Return String.Join(", ", parti)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.DescriviCellaCorrente: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Sub AvviaLetturaContinua()
        Try
            FermaLetturaContinua()
            If righeVirtuali.Count = 0 Then Return
            If indiceRigaVirtuale < 0 Then indiceRigaVirtuale = 0
            letturaContinuaAttiva = True
            threadLetturaContinua = New Thread(AddressOf EseguiLetturaContinua)
            threadLetturaContinua.IsBackground = True
            threadLetturaContinua.Start()
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.AvviaLetturaContinua: " & ex.ToString())
        End Try
    End Sub

    Private Sub EseguiLetturaContinua()
        Try
            Dim primo As Boolean = True
            While letturaContinuaAttiva
                If formPadre Is Nothing OrElse formPadre.IsDisposed OrElse Not formPadre.IsHandleCreated Then Return
                Dim testo As String = ""
                formPadre.Invoke(New System.Windows.Forms.MethodInvoker(Sub() testo = PreparaRigaLetturaContinua(primo)))
                primo = False
                If Not letturaContinuaAttiva OrElse String.IsNullOrEmpty(testo) Then Exit While
                lettura.LeggiTesto(testo)
                Dim limite As Integer = Math.Max(1500, testo.Length * 90 + 1000)
                Dim trascorsi As Integer = 0
                Thread.Sleep(250)
                trascorsi = 250
                While letturaContinuaAttiva AndAlso trascorsi < limite AndAlso lettura.StaParlando
                    Thread.Sleep(50)
                    trascorsi += 50
                End While
            End While
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.EseguiLetturaContinua: " & ex.ToString())
        Finally
            letturaContinuaAttiva = False
        End Try
    End Sub

    Private Function PreparaRigaLetturaContinua(primo As Boolean) As String
        Try
            If Not letturaContinuaAttiva Then Return ""
            If Not primo Then indiceRigaVirtuale += 1
            If indiceRigaVirtuale >= righeVirtuali.Count Then
                indiceRigaVirtuale = righeVirtuali.Count - 1
                letturaContinuaAttiva = False
                lettura.LeggiTesto("Fine pagina")
                Return ""
            End If
            Dim riga As RigaVirtuale = righeVirtuali(indiceRigaVirtuale)
            If cdpInterazione IsNot Nothing Then cdpInterazione.MostraRiga(riga)
            Dim testo As String = riga.CostruisciAnnuncio(True)
            If testo.Length > 1500 Then testo = testo.Substring(0, 1500)
            Return TestoConLingua(riga, testo)
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.PreparaRigaLetturaContinua: " & ex.ToString())
            letturaContinuaAttiva = False
            Return ""
        End Try
    End Function

    Public Sub FermaLetturaContinua()
        Try
            Dim eraAttiva As Boolean = letturaContinuaAttiva
            letturaContinuaAttiva = False
            If eraAttiva Then lettura.FermaVoce()
            If threadLetturaContinua IsNot Nothing AndAlso threadLetturaContinua.IsAlive AndAlso Thread.CurrentThread IsNot threadLetturaContinua Then
                threadLetturaContinua.Join(300)
            End If
            threadLetturaContinua = Nothing
        Catch ex As Exception
            log.ScriviLog("WGCursorVirtuale.FermaLetturaContinua: " & ex.ToString())
        End Try
    End Sub
End Class