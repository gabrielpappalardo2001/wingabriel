Imports System.IO
Imports System.Text
Imports System.Threading.Tasks
Imports WGLettoreSchermo

''' <summary>
''' Le impostazioni del lettore, in una normale finestra di Windows divisa in schede (si cambia scheda con Control e Tab). Ogni voce ha
''' la sua etichetta e il suo controllo, nell'ordine del tabulatore. Le scelte si scrivono nella configurazione condivisa con WinGabriel
''' (chiavi lettore_schermo_*). Il silenzio quando WinGabriel è in primo piano non è un'impostazione: è sempre così.
''' </summary>
Friend NotInheritable Class FinestraImpostazioni
    Inherits Form

    Private Enum TipoVoce
        Casella
        Scelta
        Numero
        Testo
        ElencoVoci
        ElencoEspeak
    End Enum

    Private NotInheritable Class Voce
        Public Chiave As String
        Public Etichetta As String
        Public Tipo As TipoVoce
        Public Predefinito As String
        Public SoloUnoZero As Boolean
        Public Valori As String()
        Public Nomi As String()
        Public Minimo As Integer
        Public Massimo As Integer
        Public Controllo As Control
        Public Scheda As String
    End Class

    Private ReadOnly _app As ApplicazioneLettore
    Private ReadOnly _voci As New List(Of Voce)
    Private ReadOnly _tabelle As New Dictionary(Of String, TableLayoutPanel)
    Private _schede As TabControl
    Private _avvioAutomatico As CheckBox
    Private _sospendi As Button
    Private _elencoProfili As ListBox
    Private _profiloProgramma As TextBox
    Private _profiloProlissita As ComboBox
    Private _profiloEcoCaratteri As CheckBox
    Private _profiloEcoParole As CheckBox

    Private Const SchedaGenerale As String = "Generale"
    Private Const SchedaVoce As String = "Voce"
    Private Const SchedaScrittura As String = "Scrittura"
    Private Const SchedaWeb As String = "Pagine web"
    Private Const SchedaAltro As String = "Altro"
    Private Const SchedaProfili As String = "Profili"
    Private Const SchedaSistema As String = "Sistema"
    Private Const PrefissoProfilo As String = "lettore_schermo_profilo_"

    Public Sub New(app As ApplicazioneLettore)
        _app = app
        Text = "Impostazioni del lettore di schermo"
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(720, 640)
        MinimumSize = New Size(560, 420)
        ShowInTaskbar = True

        DefinisciVoci()

        _schede = New TabControl()
        _schede.Dock = DockStyle.Fill
        _schede.AccessibleName = "Schede delle impostazioni"
        For Each nome As String In New String() {SchedaGenerale, SchedaVoce, SchedaScrittura, SchedaWeb, SchedaAltro, SchedaProfili, SchedaSistema}
            Dim pagina As New TabPage(nome)
            pagina.AutoScroll = True
            Dim tabella As New TableLayoutPanel()
            tabella.ColumnCount = 2
            tabella.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 55.0F))
            tabella.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 45.0F))
            tabella.AutoSize = True
            tabella.Dock = DockStyle.Top
            tabella.Padding = New Padding(10)
            pagina.Controls.Add(tabella)
            _schede.TabPages.Add(pagina)
            _tabelle(nome) = tabella
        Next

        For Each v As Voce In _voci
            Dim tabella As TableLayoutPanel = _tabelle(v.Scheda)
            Dim etichetta As New Label()
            etichetta.Text = v.Etichetta
            etichetta.AutoSize = True
            etichetta.Anchor = AnchorStyles.Left
            etichetta.Margin = New Padding(3, 6, 3, 3)
            Dim controllo As Control = CreaControllo(v)
            controllo.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            controllo.AccessibleName = v.Etichetta
            v.Controllo = controllo
            Dim riga As Integer = tabella.RowCount
            tabella.RowCount += 1
            tabella.Controls.Add(etichetta, 0, riga)
            tabella.Controls.Add(controllo, 1, riga)
        Next

        CostruisciSchedaProfili()
        CostruisciSchedaSistema()

        Dim pulsanti As New FlowLayoutPanel()
        pulsanti.Dock = DockStyle.Bottom
        pulsanti.FlowDirection = FlowDirection.RightToLeft
        pulsanti.Height = 46
        pulsanti.Padding = New Padding(8)
        Dim annulla As New Button()
        annulla.Text = "Annulla"
        annulla.DialogResult = DialogResult.Cancel
        Dim applica As New Button()
        applica.Text = "Applica"
        AddHandler applica.Click, Sub() Salva()
        Dim ok As New Button()
        ok.Text = "OK"
        AddHandler ok.Click, Sub()
                                 Salva()
                                 Close()
                             End Sub
        pulsanti.Controls.Add(annulla)
        pulsanti.Controls.Add(applica)
        pulsanti.Controls.Add(ok)

        Controls.Add(_schede)
        Controls.Add(pulsanti)
        AcceptButton = ok
        CancelButton = annulla
    End Sub

    Private Sub DefinisciVoci()
        Aggiungi(SchedaGenerale, "tasto", "Tasto dei comandi", TipoVoce.Scelta, "entrambi", New String() {"entrambi", "insert", "capslock"}, New String() {"Insert e Bloc maiuscole", "Solo Insert", "Solo Bloc maiuscole"})
        Aggiungi(SchedaGenerale, "layout", "Layout dei comandi", TipoVoce.Scelta, "portatile", New String() {"portatile", "desktop"}, New String() {"Portatile", "Da tavolo, con il tastierino"}, True)
        Aggiungi(SchedaGenerale, "prolissita", "Quantità di dettagli letti", TipoVoce.Scelta, "1", New String() {"0", "1", "2"}, New String() {"Pochi", "Normale", "Molti"}, True)
        Aggiungi(SchedaGenerale, "suoni", "Tutti i suoni del lettore", TipoVoce.Casella, "1", Nothing, Nothing, True)

        Aggiungi(SchedaVoce, "motore", "Sintesi vocale del lettore", TipoVoce.Scelta, "windows", New String() {"espeak", "windows"}, New String() {"eSpeak, consigliata", "Voci di Windows"})
        Aggiungi(SchedaVoce, "voce_espeak", "Voce di eSpeak", TipoVoce.ElencoEspeak, "it")
        Aggiungi(SchedaVoce, "voce_nome", "Voce di Windows", TipoVoce.ElencoVoci, "")
        Aggiungi(SchedaVoce, "voce_velocita", "Velocità della voce, da meno 10 a 10", TipoVoce.Numero, "3", Nothing, Nothing, False, -10, 10)
        Aggiungi(SchedaVoce, "voce_volume", "Volume della voce, da 0 a 100", TipoVoce.Numero, "100", Nothing, Nothing, False, 0, 100)
        Aggiungi(SchedaVoce, "punteggiatura", "Livello di punteggiatura letta", TipoVoce.Scelta, "-1", New String() {"-1", "0", "1", "2", "3"}, New String() {"Come in WinGabriel", "Livello 0, nessuna", "Livello 1", "Livello 2", "Livello 3, tutta"}, True)
        Aggiungi(SchedaVoce, "numeri", "Lettura dei numeri", TipoVoce.Scelta, "eredita", New String() {"eredita", "intero", "cifra", "coppie", "triplette"}, New String() {"Come in WinGabriel", "Numero intero", "Cifra per cifra", "A coppie", "A triplette"}, True)
        Aggiungi(SchedaVoce, "maiuscole", "Lettere maiuscole", TipoVoce.Scelta, "parla", New String() {"nessuna", "parla", "beep", "tutto"}, New String() {"Nessun segnale", "Dice maiuscolo", "Un suono", "Dice maiuscolo e suona"}, True)

        Aggiungi(SchedaScrittura, "eco_caratteri", "Ripeti ogni carattere scritto", TipoVoce.Casella, "true")
        Aggiungi(SchedaScrittura, "eco_parole", "Ripeti ogni parola scritta", TipoVoce.Casella, "true")
        Aggiungi(SchedaScrittura, "password", "Leggi i caratteri delle password", TipoVoce.Casella, "false")
        Aggiungi(SchedaScrittura, "rientri", "Annuncia i rientri", TipoVoce.Casella, "1", Nothing, Nothing, True)
        Aggiungi(SchedaScrittura, "annunciaFormato", "Annuncia il formato del testo", TipoVoce.Casella, "0", Nothing, Nothing, True)

        Aggiungi(SchedaWeb, "navigazione_web", "Navigazione nelle pagine web", TipoVoce.Casella, "true")
        Aggiungi(SchedaWeb, "regioni", "Annuncio delle regioni della pagina", TipoVoce.Scelta, "nuova", New String() {"mai", "nuova", "sempre", "separata"}, New String() {"Mai", "Quando cambiano", "Sempre", "Separate dal resto"})
        Aggiungi(SchedaWeb, "livello_titoli", "Di' il livello dei titoli", TipoVoce.Casella, "true")
        Aggiungi(SchedaWeb, "ciclica", "Navigazione ciclica: dopo l'ultimo si ricomincia", TipoVoce.Casella, "true")
        Aggiungi(SchedaWeb, "link_prima", "Di' collegamento prima del nome", TipoVoce.Casella, "true")
        Aggiungi(SchedaWeb, "colonne", "Leggi le colonne delle tabelle", TipoVoce.Casella, "false")
        Aggiungi(SchedaWeb, "avvisi", "Leggi gli avvisi delle altre applicazioni", TipoVoce.Casella, "true")
        Aggiungi(SchedaWeb, "posizione_elenchi", "Di' la posizione negli elenchi", TipoVoce.Casella, "true")

        Aggiungi(SchedaAltro, "ocr", "Riconoscimento ottico automatico", TipoVoce.Casella, "1", Nothing, Nothing, True)
        Aggiungi(SchedaAltro, "braille", "Uscita Braille", TipoVoce.Casella, "0", Nothing, Nothing, True)
        Aggiungi(SchedaAltro, "portaBraille", "Porta seriale del Braille", TipoVoce.Testo, "")
        Aggiungi(SchedaAltro, "tracciamentoMouse", "Leggi sotto il puntatore del mouse", TipoVoce.Casella, "0", Nothing, Nothing, True)
    End Sub

    Private Sub Aggiungi(scheda As String, chiave As String, etichetta As String, tipo As TipoVoce, predefinito As String, Optional valori As String() = Nothing, Optional nomi As String() = Nothing, Optional unoZero As Boolean = False, Optional minimo As Integer = 0, Optional massimo As Integer = 0)
        Dim v As New Voce()
        v.Scheda = scheda
        v.Chiave = "lettore_schermo_" & chiave
        v.Etichetta = etichetta
        v.Tipo = tipo
        v.Predefinito = predefinito
        v.SoloUnoZero = unoZero
        v.Valori = valori
        v.Nomi = nomi
        v.Minimo = minimo
        v.Massimo = massimo
        _voci.Add(v)
    End Sub

    Private Shared Function LeggiVero(testo As String, predefinito As String) As Boolean
        Dim t As String = If(testo, "").Trim()
        If t = "" Then t = predefinito
        Return t = "1" OrElse t.Equals("true", StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function CreaControllo(v As Voce) As Control
        Dim attuale As String = ConfigLettore.Leggi(v.Chiave, "").Trim()
        Select Case v.Tipo
            Case TipoVoce.Casella
                Dim c As New CheckBox()
                c.Text = "Attivo"
                c.AutoSize = True
                c.Checked = LeggiVero(attuale, v.Predefinito)
                Return c
            Case TipoVoce.Scelta
                Dim c As New ComboBox()
                c.DropDownStyle = ComboBoxStyle.DropDownList
                c.Items.AddRange(v.Nomi)
                Dim indice As Integer = Array.IndexOf(v.Valori, If(attuale = "", v.Predefinito, attuale).ToLowerInvariant())
                If indice < 0 Then indice = Array.IndexOf(v.Valori, v.Predefinito)
                c.SelectedIndex = Math.Max(indice, 0)
                Return c
            Case TipoVoce.Numero
                Dim c As New NumericUpDown()
                c.Minimum = v.Minimo
                c.Maximum = v.Massimo
                Dim valore As Integer
                If Not Integer.TryParse(If(attuale = "", v.Predefinito, attuale), valore) Then valore = CInt(v.Predefinito)
                c.Value = Math.Max(v.Minimo, Math.Min(v.Massimo, valore))
                Return c
            Case TipoVoce.ElencoEspeak
                Dim c As New ComboBox()
                c.DropDownStyle = ComboBoxStyle.DropDownList
                Dim voci As List(Of KeyValuePair(Of String, String)) = PrimoAvvio.VociEspeak()
                Dim valori(voci.Count - 1) As String
                Dim nomi(voci.Count - 1) As String
                For indice As Integer = 0 To voci.Count - 1
                    nomi(indice) = voci(indice).Key
                    valori(indice) = voci(indice).Value
                    c.Items.Add(voci(indice).Key)
                Next
                v.Valori = valori
                v.Nomi = nomi
                Dim posizione As Integer = Array.FindIndex(valori, Function(x) String.Equals(x, If(attuale = "", v.Predefinito, attuale), StringComparison.OrdinalIgnoreCase))
                c.SelectedIndex = Math.Max(0, posizione)
                Return c
            Case TipoVoce.ElencoVoci
                Dim c As New ComboBox()
                c.DropDownStyle = ComboBoxStyle.DropDownList
                c.Items.Add(VoceDiWindows)
                For Each nome As String In LeggiVociInstallate()
                    c.Items.Add(nome)
                Next
                Dim indice As Integer = If(attuale = "", 0, c.Items.IndexOf(attuale))
                If indice < 0 Then
                    ' La voce scelta una volta non è più installata: resta nell'elenco, così non si perde la scelta.
                    c.Items.Add(attuale)
                    indice = c.Items.Count - 1
                End If
                c.SelectedIndex = indice
                Return c
            Case Else
                Dim c As New TextBox()
                c.Text = If(attuale = "", v.Predefinito, attuale)
                Return c
        End Select
    End Function

    Private Const VoceDiWindows As String = "Voce predefinita di Windows"

    ''' <summary>
    ''' Le voci SAPI installate nel computer. Il motore vocale del lettore sceglie la voce cercando questo nome nella descrizione delle voci,
    ''' quindi il nome così com'è basta. Se l'elenco non si legge resta la sola voce predefinita di Windows.
    ''' </summary>
    Private Shared Function LeggiVociInstallate() As List(Of String)
        Dim voci As New List(Of String)
        Try
            Using sintetizzatore As New System.Speech.Synthesis.SpeechSynthesizer()
                For Each voce As System.Speech.Synthesis.InstalledVoice In sintetizzatore.GetInstalledVoices()
                    If voce.Enabled AndAlso Not voci.Contains(voce.VoiceInfo.Name) Then voci.Add(voce.VoiceInfo.Name)
                Next
            End Using
        Catch
        End Try
        Return voci
    End Function

    ' ------------------------------------------------------------------------------------------
    ' Scheda Profili: prolissità ed echi diversi per ogni programma
    ' ------------------------------------------------------------------------------------------

    Private Sub CostruisciSchedaProfili()
        Dim tabella As TableLayoutPanel = _tabelle(SchedaProfili)
        tabella.ColumnCount = 1
        tabella.ColumnStyles.Clear()
        tabella.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        Dim spiegazione As New Label()
        spiegazione.AutoSize = True
        spiegazione.MaximumSize = New Size(600, 0)
        spiegazione.Text = "Un profilo cambia la quantità di dettagli e gli echi quando il programma è in primo piano. Fuori da quel programma tornano le impostazioni generali. Si può anche salvare dal programma con Control e 0 dopo il tasto dei comandi."
        tabella.Controls.Add(spiegazione)

        Dim etichettaElenco As New Label()
        etichettaElenco.Text = "Programmi con un profilo"
        etichettaElenco.AutoSize = True
        tabella.Controls.Add(etichettaElenco)

        _elencoProfili = New ListBox()
        _elencoProfili.Height = 110
        _elencoProfili.Width = 560
        _elencoProfili.AccessibleName = "Programmi con un profilo"
        AddHandler _elencoProfili.SelectedIndexChanged, AddressOf AlCambioProfiloScelto
        tabella.Controls.Add(_elencoProfili)

        Dim etichettaNome As New Label()
        etichettaNome.Text = "Nome del programma, senza .exe"
        etichettaNome.AutoSize = True
        tabella.Controls.Add(etichettaNome)
        _profiloProgramma = New TextBox()
        _profiloProgramma.Width = 300
        _profiloProgramma.AccessibleName = "Nome del programma, senza .exe"
        tabella.Controls.Add(_profiloProgramma)

        Dim etichettaProlissita As New Label()
        etichettaProlissita.Text = "Quantità di dettagli letti"
        etichettaProlissita.AutoSize = True
        tabella.Controls.Add(etichettaProlissita)
        _profiloProlissita = New ComboBox()
        _profiloProlissita.DropDownStyle = ComboBoxStyle.DropDownList
        _profiloProlissita.Items.AddRange(New Object() {"Pochi", "Normale", "Molti"})
        _profiloProlissita.SelectedIndex = 1
        _profiloProlissita.AccessibleName = "Quantità di dettagli letti nel profilo"
        tabella.Controls.Add(_profiloProlissita)

        _profiloEcoCaratteri = New CheckBox()
        _profiloEcoCaratteri.Text = "Ripeti ogni carattere scritto"
        _profiloEcoCaratteri.AutoSize = True
        _profiloEcoCaratteri.Checked = True
        tabella.Controls.Add(_profiloEcoCaratteri)

        _profiloEcoParole = New CheckBox()
        _profiloEcoParole.Text = "Ripeti ogni parola scritta"
        _profiloEcoParole.AutoSize = True
        _profiloEcoParole.Checked = True
        tabella.Controls.Add(_profiloEcoParole)

        Dim salvaProfilo As New Button()
        salvaProfilo.AutoSize = True
        salvaProfilo.Text = "Salva il profilo"
        salvaProfilo.AccessibleName = salvaProfilo.Text
        AddHandler salvaProfilo.Click, Sub() SalvaProfiloDaFinestra()
        tabella.Controls.Add(salvaProfilo)

        Dim togliProfilo As New Button()
        togliProfilo.AutoSize = True
        togliProfilo.Text = "Togli il profilo scelto"
        togliProfilo.AccessibleName = togliProfilo.Text
        AddHandler togliProfilo.Click, Sub() TogliProfiloDaFinestra()
        tabella.Controls.Add(togliProfilo)

        RiempiElencoProfili()
    End Sub

    Private Sub RiempiElencoProfili()
        Try
            _elencoProfili.Items.Clear()
            For Each chiave As String In ConfigLettore.ChiaviConPrefisso(PrefissoProfilo)
                _elencoProfili.Items.Add(chiave.Substring(PrefissoProfilo.Length))
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraImpostazioni.RiempiElencoProfili", ex)
        End Try
    End Sub

    Private Sub AlCambioProfiloScelto(mittente As Object, e As EventArgs)
        Try
            If _elencoProfili.SelectedItem Is Nothing Then Return
            Dim nome As String = _elencoProfili.SelectedItem.ToString()
            _profiloProgramma.Text = nome
            For Each parte As String In ConfigLettore.Leggi(PrefissoProfilo & nome, "").Split(";"c)
                Dim pezzi As String() = parte.Split("="c)
                If pezzi.Length <> 2 Then Continue For
                Select Case pezzi(0).Trim()
                    Case "p"
                        Dim livello As Integer
                        If Integer.TryParse(pezzi(1), livello) Then _profiloProlissita.SelectedIndex = Math.Max(0, Math.Min(2, livello))
                    Case "c"
                        _profiloEcoCaratteri.Checked = pezzi(1).Trim() = "1"
                    Case "w"
                        _profiloEcoParole.Checked = pezzi(1).Trim() = "1"
                End Select
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraImpostazioni.AlCambioProfiloScelto", ex)
        End Try
    End Sub

    Private Sub SalvaProfiloDaFinestra()
        Try
            Dim nome As String = _profiloProgramma.Text.Trim()
            If nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then nome = nome.Substring(0, nome.Length - 4)
            nome = nome.ToLowerInvariant()
            If nome = "" Then
                MessageBox.Show(Me, "Scrivi il nome del programma, per esempio winword.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            Dim valore As String = "p=" & _profiloProlissita.SelectedIndex.ToString() & ";c=" & If(_profiloEcoCaratteri.Checked, "1", "0") & ";w=" & If(_profiloEcoParole.Checked, "1", "0")
            ConfigLettore.Scrivi(PrefissoProfilo & nome, valore)
            RiempiElencoProfili()
            _elencoProfili.SelectedItem = nome
            MessageBox.Show(Me, "Profilo salvato per " & nome & ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraImpostazioni.SalvaProfiloDaFinestra", ex)
            MessageBox.Show(Me, "Non è stato possibile salvare il profilo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub TogliProfiloDaFinestra()
        Try
            If _elencoProfili.SelectedItem Is Nothing Then
                MessageBox.Show(Me, "Scegli prima un programma dall'elenco.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            Dim nome As String = _elencoProfili.SelectedItem.ToString()
            ConfigLettore.Elimina(PrefissoProfilo & nome)
            RiempiElencoProfili()
            MessageBox.Show(Me, "Profilo di " & nome & " tolto.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraImpostazioni.TogliProfiloDaFinestra", ex)
            MessageBox.Show(Me, "Non è stato possibile togliere il profilo.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Scheda Sistema: avvio automatico, installazione per accesso e blocco, sospensione, chiusura
    ' ------------------------------------------------------------------------------------------

    Private Sub CostruisciSchedaSistema()
        Dim tabella As TableLayoutPanel = _tabelle(SchedaSistema)
        tabella.ColumnCount = 1
        tabella.ColumnStyles.Clear()
        tabella.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

        _avvioAutomatico = New CheckBox()
        _avvioAutomatico.Text = "Avvia il lettore quando accedi a Windows"
        _avvioAutomatico.AutoSize = True
        _avvioAutomatico.Checked = Installazione.AvvioAutomaticoAttivo()
        _avvioAutomatico.AccessibleName = _avvioAutomatico.Text
        tabella.Controls.Add(_avvioAutomatico)


        _sospendi = New Button()
        _sospendi.AutoSize = True
        AggiornaTestoSospendi()
        AddHandler _sospendi.Click, Sub()
                                        _app.AlternaSospensione()
                                        AggiornaTestoSospendi()
                                    End Sub
        tabella.Controls.Add(_sospendi)

        Dim icona As New Button()
        icona.AutoSize = True
        icona.Text = If(AvvioLettore.IconaPresente(), "Togli l'icona dal desktop", "Metti l'icona sul desktop")
        icona.AccessibleName = icona.Text
        AddHandler icona.Click, Sub()
                                    If AvvioLettore.AlternaIcona() Then
                                        icona.Text = If(AvvioLettore.IconaPresente(), "Togli l'icona dal desktop", "Metti l'icona sul desktop")
                                        icona.AccessibleName = icona.Text
                                    Else
                                        MessageBox.Show(Me, "Non è stato possibile cambiare l'icona sul desktop.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                                    End If
                                End Sub
        tabella.Controls.Add(icona)

        Dim aggiorna As New Button()
        aggiorna.AutoSize = True
        aggiorna.Text = "Controlla gli aggiornamenti e i file mancanti"
        aggiorna.AccessibleName = aggiorna.Text
        AddHandler aggiorna.Click, Async Sub()
                                       aggiorna.Enabled = False
                                       Try
                                           Dim esito As AvvioLettore.EsitoControllo = Await Task.Run(Function() AvvioLettore.Controlla())
                                           If IsDisposed Then Return
                                           If Not esito.Riuscito Then
                                               MessageBox.Show(Me, "Non è stato possibile collegarsi al sito degli aggiornamenti.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                                           ElseIf esito.Riparati = 0 AndAlso Not esito.AggiornamentoDisponibile Then
                                               MessageBox.Show(Me, "Il lettore di schermo è aggiornato e non manca nessun file.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
                                           Else
                                               AvvioLettore.Proponi(esito, Sub() _app.ChiudiTutto())
                                           End If
                                       Catch
                                       Finally
                                           If Not IsDisposed Then aggiorna.Enabled = True
                                       End Try
                                   End Sub
        tabella.Controls.Add(aggiorna)

        Dim chiudi As New Button()
        chiudi.AutoSize = True
        chiudi.Text = "Chiudi il lettore"
        chiudi.AccessibleName = chiudi.Text
        AddHandler chiudi.Click, Sub() _app.ChiediUscita()
        tabella.Controls.Add(chiudi)
    End Sub

    Private Sub AggiornaTestoSospendi()
        _sospendi.Text = If(_app.Sospeso, "Riprendi il lettore", "Sospendi il lettore")
        _sospendi.AccessibleName = _sospendi.Text
    End Sub

    Private Sub Salva()
        Try
            For Each v As Voce In _voci
                Dim testo As String
                Select Case v.Tipo
                    Case TipoVoce.Casella
                        Dim acceso As Boolean = DirectCast(v.Controllo, CheckBox).Checked
                        testo = If(v.SoloUnoZero, If(acceso, "1", "0"), If(acceso, "true", "false"))
                    Case TipoVoce.Scelta
                        Dim c As ComboBox = DirectCast(v.Controllo, ComboBox)
                        testo = v.Valori(Math.Max(c.SelectedIndex, 0))
                    Case TipoVoce.Numero
                        testo = CInt(DirectCast(v.Controllo, NumericUpDown).Value).ToString()
                    Case TipoVoce.ElencoEspeak
                        Dim c As ComboBox = DirectCast(v.Controllo, ComboBox)
                        testo = v.Valori(Math.Max(c.SelectedIndex, 0))
                    Case TipoVoce.ElencoVoci
                        Dim c As ComboBox = DirectCast(v.Controllo, ComboBox)
                        testo = If(c.SelectedIndex <= 0, "", c.SelectedItem.ToString())
                    Case Else
                        testo = DirectCast(v.Controllo, TextBox).Text.Trim()
                End Select
                ConfigLettore.Scrivi(v.Chiave, testo)
            Next
            If _avvioAutomatico.Checked <> Installazione.AvvioAutomaticoAttivo() Then Installazione.ImpostaAvvioAutomatico(_avvioAutomatico.Checked)
            If String.Equals(ConfigLettore.Leggi("lettore_schermo_motore", "windows").Trim(), "espeak", StringComparison.OrdinalIgnoreCase) AndAlso Not WGEspeakSintesi.WGEspeak.Installato() Then
                If Not PrimoAvvio.ScaricaEspeak(Me) Then
                    ConfigLettore.Scrivi("lettore_schermo_motore", "windows")
                    MessageBox.Show(Me, "Non sono riuscito a scaricare eSpeak. Resta la voce di Windows.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
            End If
            _app.Applica()
        Catch ex As Exception
            MessageBox.Show("Non è stato possibile salvare le impostazioni: " & ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

End Class
