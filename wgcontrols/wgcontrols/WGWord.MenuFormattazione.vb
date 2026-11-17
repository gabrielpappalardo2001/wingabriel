Imports System.Collections.Generic
Imports System.Windows.Forms

Partial Public Class WGWord

    Private Class VoceMenuOspite
        Public Property Nome As String = ""
        Public Property Evento As EventHandler = Nothing
    End Class

    Private _vociMenuOspite As New List(Of VoceMenuOspite)()
    Private _menuFormattazione As WGMenu
    Private _campiLinkMenu As Campi
    Private _gestoreLetturaTouch As WGWordGestoreMovimento

    Public Event RichiestaDettatura As EventHandler

    ''' <summary>
    ''' Attiva o disattiva la lettura continua a interruttore, con lo stesso comportamento del
    ''' tasto F8: se e' gia' in corso la ferma, altrimenti la avvia dalla posizione attuale del
    ''' cursore. Usa un'istanza dedicata di WGWordGestoreMovimento, separata da quella interna
    ''' che gestisce gia' la tastiera fisica, per non duplicarne l'ascolto: questa nuova istanza
    ''' viene subito scollegata dalla tastiera e serve solo per la lettura.
    ''' </summary>
    Public Sub AttivaDisattivaLetturaCompletaTouch()
        Try
            If _gestoreLetturaTouch Is Nothing Then
                _gestoreLetturaTouch = New WGWordGestoreMovimento(Me)
                _gestoreLetturaTouch.DisattivaAscoltoTastieraFisica()
            End If
            _gestoreLetturaTouch.AttivaDisattivaLetturaContinua()
        Catch ex As Exception
            logger.ScriviLog("WGWord.AttivaDisattivaLetturaCompletaTouch: " & ex.Message)
        End Try
    End Sub

    Public Sub AggiungiVoceMenuDocumento(testoVoce As String, evento As EventHandler)
        Try
            If String.IsNullOrWhiteSpace(testoVoce) OrElse evento Is Nothing Then Return
            Dim voce As New VoceMenuOspite()
            voce.Nome = testoVoce
            voce.Evento = evento
            _vociMenuOspite.Add(voce)
        Catch ex As Exception
            logger.ScriviLog("WGWord.AggiungiVoceMenuDocumento: " & ex.Message)
        End Try
    End Sub

    Public Sub SvuotaVociMenuDocumento()
        Try
            _vociMenuOspite.Clear()
        Catch ex As Exception
            logger.ScriviLog("WGWord.SvuotaVociMenuDocumento: " & ex.Message)
        End Try
    End Sub

    Public Sub MostraMenuFormattazione()
        If Me.InvokeRequired Then
            Try
                Me.BeginInvoke(New MethodInvoker(AddressOf MostraMenuFormattazione))
            Catch exRinvio As Exception
                logger.ScriviLog("WGWord.MostraMenuFormattazione (rinvio): " & exRinvio.Message)
            End Try
            Return
        End If
        Try
            If Me.IsDisposed OrElse Me.Disposing Then Return

            Dim m As New WGMenu()

            For Each voce As VoceMenuOspite In _vociMenuOspite
                If voce Is Nothing OrElse voce.Evento Is Nothing Then Continue For
                m.aggiungi(voce.Nome, voce.Evento)
            Next

            If Not SolaLettura Then
                m.aggiungi("Carattere", AddressOf VoceMenuCarattere)
                m.aggiungi("Paragrafo", AddressOf VoceMenuParagrafo)
                m.aggiungi("Elenchi e rientri", AddressOf VoceMenuElenchi)
            End If

            m.aggiungi("Tabelle", AddressOf VoceMenuTabelle)

            If Not SolaLettura Then
                m.aggiungi("Collegamenti e note", AddressOf VoceMenuCollegamenti)
                m.aggiungi("Trasforma il testo", AddressOf VoceMenuTrasformaTesto)
                m.aggiungi("Appunti", AddressOf VoceMenuAppunti)
                If WGDettatura.SistemaSupportato Then
                    m.aggiungi("Dettatura", AddressOf VoceMenuDettatura)
                End If
            End If

            m.aggiungi("Vai a", AddressOf VoceMenuVaiA)
            m.aggiungi("Informazioni sul documento", AddressOf VoceStatisticheDocumento)

            _menuFormattazione = m
            m.avvia("Documento", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.MostraMenuFormattazione: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuDettatura(sender As Object, e As EventArgs)
        Try
            RaiseEvent RichiestaDettatura(Me, EventArgs.Empty)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuDettatura: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuCarattere(sender As Object, e As EventArgs)
        Try
            Dim grassetto As Boolean = False
            Dim corsivo As Boolean = False
            Dim sottolineato As Boolean = False
            Dim barrato As Boolean = False
            LeggiStatoCarattereCorrente(grassetto, corsivo, sottolineato, barrato)

            Dim m As New WGMenu()
            m.aggiungi("Tipo di carattere", AddressOf VoceTipoCarattere)
            m.aggiungi("Dimensione del carattere", AddressOf VoceDimensioneCarattere)
            m.aggiungi("Colore del carattere", AddressOf VoceColoreCarattere)
            m.aggiungi("Colore evidenziatore", AddressOf VoceColoreEvidenziatore)
            m.aggiungi(If(grassetto, "Togli il grassetto", "Metti in grassetto"), AddressOf VoceGrassetto)
            m.aggiungi(If(corsivo, "Togli il corsivo", "Metti in corsivo"), AddressOf VoceCorsivo)
            m.aggiungi(If(sottolineato, "Togli la sottolineatura", "Sottolinea"), AddressOf VoceSottolineato)
            m.aggiungi(If(barrato, "Togli il barrato", "Barra il testo"), AddressOf VoceBarrato)
            m.aggiungi("Togli tutta la formattazione", AddressOf VoceRimuoviFormattazione)
            m.avvia("Carattere", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuCarattere: " & ex.Message)
        End Try
    End Sub

    Private Sub LeggiStatoCarattereCorrente(ByRef grassetto As Boolean, ByRef corsivo As Boolean, ByRef sottolineato As Boolean, ByRef barrato As Boolean)
        grassetto = False
        corsivo = False
        sottolineato = False
        barrato = False
        Try
            Dim para As DocParagraph = GetCurrentParagraph()
            If para Is Nothing OrElse para.Runs Is Nothing OrElse para.Runs.Count = 0 Then Return
            Dim conteggio As Integer = 0
            For Each r As DocRun In para.Runs
                If r Is Nothing OrElse r.IsImage Then Continue For
                Dim lunghezza As Integer = If(r.Text Is Nothing, 0, r.Text.Length)
                If _caretCharIndex >= conteggio AndAlso _caretCharIndex <= conteggio + lunghezza Then
                    grassetto = r.Bold
                    corsivo = r.Italic
                    sottolineato = r.Underline
                    barrato = r.Strikethrough
                    Return
                End If
                conteggio += lunghezza
            Next
        Catch ex As Exception
            logger.ScriviLog("WGWord.LeggiStatoCarattereCorrente: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceTipoCarattere(sender As Object, e As EventArgs)
        Try
            MostraMenuFont()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceTipoCarattere: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceDimensioneCarattere(sender As Object, e As EventArgs)
        Try
            MostraMenuDimensioni()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceDimensioneCarattere: " & ex.Message)
        End Try
    End Sub

    Private Shared ReadOnly NomiColori As String() = {"Nero", "Bianco", "Rosso", "Verde", "Blu", "Giallo", "Arancione", "Viola", "Grigio", "Marrone", "Celeste", "Rosa"}

    Private _coloreDestinatoEvidenziatore As Boolean = False

    Private Sub VoceColoreCarattere(sender As Object, e As EventArgs)
        Try
            _coloreDestinatoEvidenziatore = False
            MostraMenuColori("Colore del carattere", False)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceColoreCarattere: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceColoreEvidenziatore(sender As Object, e As EventArgs)
        Try
            _coloreDestinatoEvidenziatore = True
            MostraMenuColori("Colore evidenziatore", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceColoreEvidenziatore: " & ex.Message)
        End Try
    End Sub

    Private Class SceltaColore
        Private ReadOnly _padre As WGWord
        Private ReadOnly _nome As String
        Private ReadOnly _evidenziatore As Boolean
        Private ReadOnly _registro As Logger

        Public Sub New(padre As WGWord, nome As String, evidenziatore As Boolean)
            _padre = padre
            _nome = nome
            _evidenziatore = evidenziatore
            _registro = New Logger()
        End Sub

        Public ReadOnly Property Nome As String
            Get
                Return _nome
            End Get
        End Property

        Public Sub Applica(sender As Object, e As EventArgs)
            Try
                If _padre Is Nothing OrElse _padre.IsDisposed Then Return
                If _evidenziatore Then
                    _padre.ImpostaEvidenziatore(_nome)
                Else
                    _padre.ImpostaColoreFont(_nome)
                End If
            Catch ex As Exception
                _registro.ScriviLog("SceltaColore.Applica: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Sub MostraMenuColori(titolo As String, evidenziatore As Boolean)
        Try
            Dim m As New WGMenu()
            If evidenziatore Then
                Dim nessuno As New SceltaColore(Me, "Nessuno", True)
                m.aggiungi("Nessuna evidenziazione", AddressOf nessuno.Applica)
            End If
            For Each nome As String In NomiColori
                Dim scelta As New SceltaColore(Me, nome, evidenziatore)
                m.aggiungi(nome, AddressOf scelta.Applica)
            Next
            m.avvia(titolo, True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.MostraMenuColori: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceGrassetto(sender As Object, e As EventArgs)
        Try
            ApplicaGrassetto()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceGrassetto: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCorsivo(sender As Object, e As EventArgs)
        Try
            ApplicaCorsivo()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceCorsivo: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceSottolineato(sender As Object, e As EventArgs)
        Try
            ApplicaSottolineato()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceSottolineato: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceBarrato(sender As Object, e As EventArgs)
        Try
            ApplicaBarrato()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceBarrato: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRimuoviFormattazione(sender As Object, e As EventArgs)
        Try
            RimuoviFormattazione()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceRimuoviFormattazione: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuParagrafo(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Allinea a sinistra", AddressOf VoceAllineaSinistra)
            m.aggiungi("Allinea al centro", AddressOf VoceAllineaCentro)
            m.aggiungi("Allinea a destra", AddressOf VoceAllineaDestra)
            m.aggiungi("Giustifica", AddressOf VoceGiustifica)
            m.aggiungi("Aumenta l'interlinea", AddressOf VoceAumentaInterlinea)
            m.aggiungi("Riduci l'interlinea", AddressOf VoceRiduciInterlinea)
            m.avvia("Paragrafo", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuParagrafo: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAllineaSinistra(sender As Object, e As EventArgs)
        Try
            ImpostaAllineamento("Left")
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAllineaSinistra: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAllineaCentro(sender As Object, e As EventArgs)
        Try
            ImpostaAllineamento("Center")
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAllineaCentro: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAllineaDestra(sender As Object, e As EventArgs)
        Try
            ImpostaAllineamento("Right")
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAllineaDestra: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceGiustifica(sender As Object, e As EventArgs)
        Try
            ImpostaAllineamento("Justify")
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceGiustifica: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAumentaInterlinea(sender As Object, e As EventArgs)
        Try
            AumentaInterlinea()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAumentaInterlinea: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRiduciInterlinea(sender As Object, e As EventArgs)
        Try
            RiduciInterlinea()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceRiduciInterlinea: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuElenchi(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Elenco puntato", AddressOf VoceElencoPuntato)
            m.aggiungi("Elenco numerato", AddressOf VoceElencoNumerato)
            m.aggiungi("Aumenta il rientro", AddressOf VoceAumentaRientro)
            m.aggiungi("Riduci il rientro", AddressOf VoceRiduciRientro)
            m.aggiungi("Interruzione di pagina", AddressOf VoceInterruzionePagina)
            m.avvia("Elenchi e rientri", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuElenchi: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceElencoPuntato(sender As Object, e As EventArgs)
        Try
            InserisciElencoPuntato()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceElencoPuntato: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceElencoNumerato(sender As Object, e As EventArgs)
        Try
            InserisciElencoNumerato()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceElencoNumerato: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAumentaRientro(sender As Object, e As EventArgs)
        Try
            AumentaRientro()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAumentaRientro: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRiduciRientro(sender As Object, e As EventArgs)
        Try
            RiduciRientro()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceRiduciRientro: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceInterruzionePagina(sender As Object, e As EventArgs)
        Try
            InserisciInterruzionePagina()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceInterruzionePagina: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuTabelle(sender As Object, e As EventArgs)
        Try
            Dim inTabella As Boolean = False
            Dim stato As StatoCursore = OttieniStatoCursore()
            If stato IsNot Nothing Then inTabella = stato.InTabella

            Dim m As New WGMenu()

            If inTabella AndAlso Not SolaLettura Then
                m.aggiungi("Aggiungi una riga", AddressOf VoceAggiungiRiga)
                m.aggiungi("Elimina la riga", AddressOf VoceRimuoviRiga)
                m.aggiungi("Aggiungi una colonna", AddressOf VoceAggiungiColonna)
                m.aggiungi("Elimina la colonna", AddressOf VoceRimuoviColonna)
                m.aggiungi("Colore di sfondo della cella", AddressOf VoceSfondoCella)
            End If

            If Not SolaLettura Then
                m.aggiungi("Inserisci una tabella", AddressOf VoceInserisciTabella)
            End If

            m.aggiungi("Elenco delle tabelle", AddressOf VoceElencoTabelle)
            m.avvia("Tabelle", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuTabelle: " & ex.Message)
        End Try
    End Sub

    Private _campiTabellaMenu As Campi

    Private Sub VoceInserisciTabella(sender As Object, e As EventArgs)
        Try
            If SolaLettura Then Return
            RilasciaCampiTabellaMenu()
            _campiTabellaMenu = New Campi()
            _campiTabellaMenu.SalvaSuEscape = False
            _campiTabellaMenu.AggiungiNumerico("Numero di righe", "tab.righe", True, False, "")
            _campiTabellaMenu.AggiungiNumerico("Numero di colonne", "tab.colonne", True, False, "")
            AddHandler _campiTabellaMenu.Fine, AddressOf CampiTabellaMenuFine
            AddHandler _campiTabellaMenu.Chiusura, AddressOf CampiTabellaMenuChiusura
            _campiTabellaMenu.Avvia()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceInserisciTabella: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiTabellaMenuFine(sender As Object, e As EventArgs)
        Dim righe As Integer = 0
        Dim colonne As Integer = 0
        Try
            If _campiTabellaMenu Is Nothing Then Return
            Dim testoRighe As String = _campiTabellaMenu.RecuperaValore("tab.righe")
            Dim testoColonne As String = _campiTabellaMenu.RecuperaValore("tab.colonne")
            RilasciaCampiTabellaMenu()

            If Not Integer.TryParse(If(testoRighe, "").Trim(), righe) OrElse righe <= 0 Then righe = 0
            If Not Integer.TryParse(If(testoColonne, "").Trim(), colonne) OrElse colonne <= 0 Then colonne = 1
            If righe > 200 Then righe = 200
            If colonne > 50 Then colonne = 50

            If righe > 0 Then InserisciTabella(righe, colonne)
        Catch ex As Exception
            logger.ScriviLog("WGWord.CampiTabellaMenuFine: " & ex.Message)
        Finally
            Try
                RiprendiFocus(True)
            Catch exFocus As Exception
                logger.ScriviLog("WGWord.CampiTabellaMenuFine (focus): " & exFocus.Message)
            End Try
        End Try
    End Sub

    Private Sub CampiTabellaMenuChiusura(sender As Object, e As EventArgs)
        Try
            RilasciaCampiTabellaMenu()
        Catch ex As Exception
            logger.ScriviLog("WGWord.CampiTabellaMenuChiusura: " & ex.Message)
        Finally
            Try
                RiprendiFocus(True)
            Catch exFocus As Exception
                logger.ScriviLog("WGWord.CampiTabellaMenuChiusura (focus): " & exFocus.Message)
            End Try
        End Try
    End Sub

    Private Sub RilasciaCampiTabellaMenu()
        Try
            If _campiTabellaMenu Is Nothing Then Return
            Try
                RemoveHandler _campiTabellaMenu.Fine, AddressOf CampiTabellaMenuFine
            Catch exRimozione As Exception
                logger.ScriviLog("WGWord.RilasciaCampiTabellaMenu (fine): " & exRimozione.Message)
            End Try
            Try
                RemoveHandler _campiTabellaMenu.Chiusura, AddressOf CampiTabellaMenuChiusura
            Catch exRimozione As Exception
                logger.ScriviLog("WGWord.RilasciaCampiTabellaMenu (chiusura): " & exRimozione.Message)
            End Try
            _campiTabellaMenu = Nothing
            _campiTabellaMenu = Nothing
        Catch ex As Exception
            logger.ScriviLog("WGWord.RilasciaCampiTabellaMenu: " & ex.Message)
            _campiTabellaMenu = Nothing
        End Try
    End Sub

    Private Sub VoceAggiungiRiga(sender As Object, e As EventArgs)
        Try
            AggiungiRigaTabella()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAggiungiRiga: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRimuoviRiga(sender As Object, e As EventArgs)
        Try
            RimuoviRigaTabella()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceRimuoviRiga: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAggiungiColonna(sender As Object, e As EventArgs)
        Try
            AggiungiColonnaTabella()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAggiungiColonna: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRimuoviColonna(sender As Object, e As EventArgs)
        Try
            RimuoviColonnaTabella()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceRimuoviColonna: " & ex.Message)
        End Try
    End Sub

    Private Class SfondoCella
        Private ReadOnly _padre As WGWord
        Private ReadOnly _nome As String
        Private ReadOnly _esadecimale As String
        Private ReadOnly _registro As Logger

        Public Sub New(padre As WGWord, nome As String, esadecimale As String)
            _padre = padre
            _nome = nome
            _esadecimale = esadecimale
            _registro = New Logger()
        End Sub

        Public ReadOnly Property Nome As String
            Get
                Return _nome
            End Get
        End Property

        Public Sub Applica(sender As Object, e As EventArgs)
            Try
                If _padre Is Nothing OrElse _padre.IsDisposed Then Return
                _padre.ImpostaColoreSfondoCella(_esadecimale)
            Catch ex As Exception
                _registro.ScriviLog("SfondoCella.Applica: " & ex.Message)
            End Try
        End Sub
    End Class

    Private Sub VoceSfondoCella(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            Dim nessuno As New SfondoCella(Me, "Nessuno", "Nessuno")
            m.aggiungi("Nessuno sfondo", AddressOf nessuno.Applica)
            Dim grigio As New SfondoCella(Me, "Grigio chiaro", "F0F0F0")
            m.aggiungi("Grigio chiaro", AddressOf grigio.Applica)
            Dim giallo As New SfondoCella(Me, "Giallo", "FFFF00")
            m.aggiungi("Giallo", AddressOf giallo.Applica)
            Dim verde As New SfondoCella(Me, "Verde chiaro", "CCFFCC")
            m.aggiungi("Verde chiaro", AddressOf verde.Applica)
            Dim azzurro As New SfondoCella(Me, "Azzurro", "CCE5FF")
            m.aggiungi("Azzurro", AddressOf azzurro.Applica)
            Dim rosa As New SfondoCella(Me, "Rosa", "FFCCCC")
            m.aggiungi("Rosa", AddressOf rosa.Applica)
            m.avvia("Colore di sfondo della cella", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceSfondoCella: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceElencoTabelle(sender As Object, e As EventArgs)
        Try
            SopprimiAnnuncioRientro()
            MostraElencoTabelle()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceElencoTabelle: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuCollegamenti(sender As Object, e As EventArgs)
        Try
            Dim bookmarkNota As String = ""
            Try
                bookmarkNota = EBookmarkNotaAllaPosizioneCorrente()
            Catch exNota As Exception
                logger.ScriviLog("WGWord.VoceMenuCollegamenti (nota): " & exNota.Message)
            End Try

            Dim m As New WGMenu()
            m.aggiungi("Inserisci un collegamento", AddressOf VoceInserisciLink)
            m.aggiungi("Elimina il collegamento", AddressOf VoceEliminaLink)
            m.aggiungi("Elenco dei collegamenti", AddressOf VoceElencoLink)
            If String.IsNullOrEmpty(bookmarkNota) Then
                m.aggiungi("Inserisci una nota a piè di pagina", AddressOf VoceInserisciNotaPiePagina)
                m.aggiungi("Inserisci una nota di chiusura", AddressOf VoceInserisciNotaChiusura)
            Else
                m.aggiungi("Elimina la nota", AddressOf VoceEliminaNota)
            End If
            m.avvia("Collegamenti e note", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuCollegamenti: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceInserisciLink(sender As Object, e As EventArgs)
        Try
            RilasciaCampiLinkMenu()
            _campiLinkMenu = New Campi()
            _campiLinkMenu.SalvaSuEscape = False
            _campiLinkMenu.Aggiungi("Testo del collegamento", "link.testo", True, False, "")
            _campiLinkMenu.Aggiungi("Indirizzo", "link.url", True, False, "")
            AddHandler _campiLinkMenu.Fine, AddressOf CampiLinkMenuFine
            AddHandler _campiLinkMenu.Chiusura, AddressOf CampiLinkMenuChiusura
            _campiLinkMenu.Avvia()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceInserisciLink: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiLinkMenuFine(sender As Object, e As EventArgs)
        Try
            If _campiLinkMenu Is Nothing Then Return
            Dim testo As String = _campiLinkMenu.RecuperaValore("link.testo")
            Dim indirizzo As String = _campiLinkMenu.RecuperaValore("link.url")
            RilasciaCampiLinkMenu()
            If String.IsNullOrWhiteSpace(indirizzo) Then Return
            If String.IsNullOrWhiteSpace(testo) Then testo = indirizzo
            InserisciLink(testo.Trim(), indirizzo.Trim())
        Catch ex As Exception
            logger.ScriviLog("WGWord.CampiLinkMenuFine: " & ex.Message)
        Finally
            Try
                RiprendiFocus(True)
            Catch exFocus As Exception
                logger.ScriviLog("WGWord.CampiLinkMenuFine (focus): " & exFocus.Message)
            End Try
        End Try
    End Sub

    Private Sub CampiLinkMenuChiusura(sender As Object, e As EventArgs)
        Try
            RilasciaCampiLinkMenu()
        Catch ex As Exception
            logger.ScriviLog("WGWord.CampiLinkMenuChiusura: " & ex.Message)
        Finally
            Try
                RiprendiFocus(True)
            Catch exFocus As Exception
                logger.ScriviLog("WGWord.CampiLinkMenuChiusura (focus): " & exFocus.Message)
            End Try
        End Try
    End Sub

    Private Sub RilasciaCampiLinkMenu()
        Try
            If _campiLinkMenu Is Nothing Then Return
            Try
                RemoveHandler _campiLinkMenu.Fine, AddressOf CampiLinkMenuFine
            Catch exRimozione As Exception
                logger.ScriviLog("WGWord.RilasciaCampiLinkMenu (fine): " & exRimozione.Message)
            End Try
            Try
                RemoveHandler _campiLinkMenu.Chiusura, AddressOf CampiLinkMenuChiusura
            Catch exRimozione As Exception
                logger.ScriviLog("WGWord.RilasciaCampiLinkMenu (chiusura): " & exRimozione.Message)
            End Try
            _campiLinkMenu = Nothing
            _campiLinkMenu = Nothing
        Catch ex As Exception
            logger.ScriviLog("WGWord.RilasciaCampiLinkMenu: " & ex.Message)
            _campiLinkMenu = Nothing
        End Try
    End Sub

    Private Sub VoceEliminaLink(sender As Object, e As EventArgs)
        Try
            EliminaCollegamentoAllaPosizioneCorrente()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceEliminaLink: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceElencoLink(sender As Object, e As EventArgs)
        Try
            SopprimiAnnuncioRientro()
            MostraElencoLink()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceElencoLink: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceInserisciNotaPiePagina(sender As Object, e As EventArgs)
        Try
            InserisciNotaAPiePagina()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceInserisciNotaPiePagina: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceInserisciNotaChiusura(sender As Object, e As EventArgs)
        Try
            InserisciNotaDiChiusura()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceInserisciNotaChiusura: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceEliminaNota(sender As Object, e As EventArgs)
        Try
            EliminaNotaAllaPosizioneCorrente()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceEliminaNota: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuTrasformaTesto(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Tutto maiuscolo", AddressOf VoceMaiuscolo)
            m.aggiungi("Tutto minuscolo", AddressOf VoceMinuscolo)
            m.aggiungi("Iniziali maiuscole", AddressOf VoceInizialiMaiuscole)
            m.aggiungi("Togli gli spazi in piu'", AddressOf VoceRimuoviSpazi)
            m.aggiungi("Inserisci un'emoji", AddressOf VoceEmoji)
            m.aggiungi("Suggerimenti ortografici", AddressOf VoceSuggerimenti)
            m.avvia("Trasforma il testo", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuTrasformaTesto: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMaiuscolo(sender As Object, e As EventArgs)
        Try
            ConvertiSelezioneMaiuscolo()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMaiuscolo: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMinuscolo(sender As Object, e As EventArgs)
        Try
            ConvertiSelezioneMinuscolo()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMinuscolo: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceInizialiMaiuscole(sender As Object, e As EventArgs)
        Try
            ConvertiSelezioneInizialiMaiuscole()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceInizialiMaiuscole: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRimuoviSpazi(sender As Object, e As EventArgs)
        Try
            RimuoviSpaziVuotiExtra()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceRimuoviSpazi: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceEmoji(sender As Object, e As EventArgs)
        Try
            MostraMenuInserisciEmogi()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceEmoji: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceSuggerimenti(sender As Object, e As EventArgs)
        Try
            SopprimiAnnuncioRientro()
            MostraSuggerimentiOrtografici()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceSuggerimenti: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuAppunti(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Copia", AddressOf VoceCopia)
            m.aggiungi("Taglia", AddressOf VoceTaglia)
            m.aggiungi("Incolla", AddressOf VoceIncolla)
            m.aggiungi("Annulla", AddressOf VoceAnnulla)
            m.aggiungi("Ripristina", AddressOf VoceRipristina)
            m.avvia("Appunti", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuAppunti: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceCopia(sender As Object, e As EventArgs)
        Try
            CopiaFormattato()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceCopia: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceTaglia(sender As Object, e As EventArgs)
        Try
            TagliaFormattato()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceTaglia: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceIncolla(sender As Object, e As EventArgs)
        Try
            IncollaFormattato()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceIncolla: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceAnnulla(sender As Object, e As EventArgs)
        Try
            Undo()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceAnnulla: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceRipristina(sender As Object, e As EventArgs)
        Try
            Redo()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceRipristina: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceMenuVaiA(sender As Object, e As EventArgs)
        Try
            Dim m As New WGMenu()
            m.aggiungi("Vai a una riga", AddressOf VoceVaiARiga)
            m.aggiungi("Vai a una pagina", AddressOf VoceVaiAPagina)
            m.aggiungi("Elenco dei collegamenti", AddressOf VoceElencoLink)
            m.aggiungi("Elenco delle tabelle", AddressOf VoceElencoTabelle)
            m.avvia("Vai a", True)
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceMenuVaiA: " & ex.Message)
        End Try
    End Sub

    Private _campiVaiAMenu As Campi
    Private _vaiAPerPagina As Boolean = False

    Private Sub VoceVaiARiga(sender As Object, e As EventArgs)
        Try
            SopprimiAnnuncioRientro()
            _vaiAPerPagina = False
            ApriCampoVaiA("Numero della riga")
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceVaiARiga: " & ex.Message)
        End Try
    End Sub

    Private Sub VoceVaiAPagina(sender As Object, e As EventArgs)
        Try
            SopprimiAnnuncioRientro()
            _vaiAPerPagina = True
            ApriCampoVaiA("Numero della pagina")
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceVaiAPagina: " & ex.Message)
        End Try
    End Sub

    Private Sub ApriCampoVaiA(titolo As String)
        Try
            RilasciaCampiVaiAMenu()
            _campiVaiAMenu = New Campi()
            _campiVaiAMenu.SalvaSuEscape = False
            _campiVaiAMenu.AggiungiNumerico(titolo, "vaia.numero", True, False, "")
            AddHandler _campiVaiAMenu.Fine, AddressOf CampiVaiAMenuFine
            AddHandler _campiVaiAMenu.Chiusura, AddressOf CampiVaiAMenuChiusura
            _campiVaiAMenu.Avvia()
        Catch ex As Exception
            logger.ScriviLog("WGWord.ApriCampoVaiA: " & ex.Message)
        End Try
    End Sub

    Private Sub CampiVaiAMenuFine(sender As Object, e As EventArgs)
        Try
            If _campiVaiAMenu Is Nothing Then Return
            Dim testo As String = _campiVaiAMenu.RecuperaValore("vaia.numero")
            Dim perPagina As Boolean = _vaiAPerPagina
            RilasciaCampiVaiAMenu()
            Dim numero As Integer = 0
            If Not Integer.TryParse(If(testo, "").Trim(), numero) Then Return
            If numero <= 0 Then Return
            If perPagina Then
                VaiAPagina(numero)
            Else
                VaiARiga(numero)
            End If
        Catch ex As Exception
            logger.ScriviLog("WGWord.CampiVaiAMenuFine: " & ex.Message)
        Finally
            Try
                RiprendiFocus(False)
            Catch exFocus As Exception
                logger.ScriviLog("WGWord.CampiVaiAMenuFine (focus): " & exFocus.Message)
            End Try
        End Try
    End Sub

    Private Sub CampiVaiAMenuChiusura(sender As Object, e As EventArgs)
        Try
            RilasciaCampiVaiAMenu()
        Catch ex As Exception
            logger.ScriviLog("WGWord.CampiVaiAMenuChiusura: " & ex.Message)
        Finally
            Try
                RiprendiFocus(True)
            Catch exFocus As Exception
                logger.ScriviLog("WGWord.CampiVaiAMenuChiusura (focus): " & exFocus.Message)
            End Try
        End Try
    End Sub

    Private Sub RilasciaCampiVaiAMenu()
        Try
            If _campiVaiAMenu Is Nothing Then Return
            Try
                RemoveHandler _campiVaiAMenu.Fine, AddressOf CampiVaiAMenuFine
            Catch exRimozione As Exception
                logger.ScriviLog("WGWord.RilasciaCampiVaiAMenu (fine): " & exRimozione.Message)
            End Try
            Try
                RemoveHandler _campiVaiAMenu.Chiusura, AddressOf CampiVaiAMenuChiusura
            Catch exRimozione As Exception
                logger.ScriviLog("WGWord.RilasciaCampiVaiAMenu (chiusura): " & exRimozione.Message)
            End Try
            _campiVaiAMenu = Nothing
            _campiVaiAMenu = Nothing
        Catch ex As Exception
            logger.ScriviLog("WGWord.RilasciaCampiVaiAMenu: " & ex.Message)
            _campiVaiAMenu = Nothing
        End Try
    End Sub

    Private Sub VoceStatisticheDocumento(sender As Object, e As EventArgs)
        Try
            SopprimiAnnuncioRientro()
            StatisticheDocumento()
        Catch ex As Exception
            logger.ScriviLog("WGWord.VoceStatisticheDocumento: " & ex.Message)
        End Try
    End Sub

End Class