Imports System.Drawing
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports WGEspeakSintesi
Imports WGLettoreSchermo
Imports Lettore = WGLettoreSchermo.WGLettoreSchermo

''' <summary>Dove la libreria WGEspeak scrive le sue poche impostazioni: nella configurazione del lettore.</summary>
Friend NotInheritable Class ArchivioEspeakLettore
    Implements IArchivioEspeak

    Public Function Leggi(chiave As String) As String Implements IArchivioEspeak.Leggi
        Return ConfigLettore.Leggi("lettore_schermo_espeak_" & chiave, "")
    End Function

    Public Sub Scrivi(chiave As String, valore As String) Implements IArchivioEspeak.Scrivi
        ConfigLettore.Scrivi("lettore_schermo_espeak_" & chiave, valore)
    End Sub
End Class

''' <summary>
''' Il primo avvio: se chiedere come usare il lettore (installarlo o usarlo portatile), se farlo partire con Windows e sulla schermata
''' di accesso, e poi la voce e la velocità. Chi ha già una configurazione non vede niente e continua con le voci di Windows;
''' per chi comincia adesso la voce predefinita è eSpeak.
''' </summary>
Friend NotInheritable Class PrimoAvvio

    Private Const ChiaveFatto As String = "lettore_schermo_primo_avvio"
    Private Const ChiaveMotore As String = "lettore_schermo_motore"
    Private Const ChiaveVoceEspeak As String = "lettore_schermo_voce_espeak"
    Private Const ChiaveVelocita As String = "lettore_schermo_voce_velocita"

    Private Sub New()
    End Sub

    ''' <summary>Da chiamare appena si sa dove sta la configurazione e prima di scegliere la voce.</summary>
    Friend Shared Sub PreparaConfigurazione(sicuro As Boolean)
        Try
            WGEspeak.Archivio = New ArchivioEspeakLettore()
            CercaEspeakDiWinGabriel()
            If sicuro Then Return
            If ConfigLettore.Leggi(ChiaveFatto, "").Trim() <> "" Then Return
            If ConfigLettore.ChiaviConPrefisso("lettore_schermo_").Count > 0 Then
                ' Una configurazione c'è già: non si cambia niente, la voce resta quella di Windows.
                If ConfigLettore.Leggi(ChiaveMotore, "").Trim() = "" Then ConfigLettore.Scrivi(ChiaveMotore, "windows")
                ConfigLettore.Scrivi(ChiaveFatto, "configurazione-esistente")
            Else
                ConfigLettore.Scrivi(ChiaveMotore, "espeak")
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.PreparaConfigurazione", ex)
        End Try
    End Sub

    ' Se WinGabriel è installato ed ha già eSpeak, il lettore usa quello (WGEspeak guarda anche lì prima di scaricare): le cartelle accanto alla
    ' configurazione, quelle sopra di lei e quella di WinGabriel in Programmi.
    Private Shared Sub CercaEspeakDiWinGabriel()
        Try
            Dim configurazione As String = ConfigLettore.Percorso()
            If configurazione <> "" Then
                Dim cartella As String = System.IO.Path.GetDirectoryName(configurazione)
                For salto As Integer = 0 To 3
                    If String.IsNullOrEmpty(cartella) Then Exit For
                    WGEspeak.CercaAnche(System.IO.Path.Combine(cartella, "espeak"))
                    cartella = System.IO.Path.GetDirectoryName(cartella)
                Next
            End If
            For Each radice As Environment.SpecialFolder In New Environment.SpecialFolder() {Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86}
                Dim programmi As String = Environment.GetFolderPath(radice)
                If programmi <> "" Then WGEspeak.CercaAnche(System.IO.Path.Combine(programmi, "WinGabriel", "espeak"))
            Next
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.CercaEspeakDiWinGabriel", ex)
        End Try
    End Sub

    Private Shared _scaricoInSottofondo As Task

    ' Se la voce scelta è eSpeak e non c'è (né accanto al lettore né in WinGabriel), lo scarica in sottofondo con la libreria WGEspeak,
    ' dicendo "Download di eSpeak in corso". Finito, il lettore passa alla voce di eSpeak.
    Friend Shared Sub ScaricaEspeakSeManca(app As ApplicazioneLettore)
        Try
            If ConfigLettore.Leggi(ChiaveMotore, "").Trim() <> "espeak" Then Return
            If WGEspeak.Installato() Then Return
            If _scaricoInSottofondo IsNot Nothing Then Return
            _scaricoInSottofondo = Task.Run(Async Function()
                                                Try
                                                    ' Dopo la frase di avvio e la lettura della finestra, per non interromperle.
                                                    Await Task.Delay(3000)
                                                    Lettore.GetInstance().Annuncia("Download di eSpeak in corso")
                                                    Dim errore As String = Await WGEspeak.InstallaAsync(New Progress(Of Integer)(Sub(percentuale As Integer)
                                                                                                                              End Sub), CancellationToken.None)
                                                    If errore = "" AndAlso WGEspeak.Installato() Then
                                                        Lettore.GetInstance().Annuncia("Download di eSpeak completato")
                                                        app.RicaricaVoce()
                                                    Else
                                                        Lettore.GetInstance().Annuncia("Download di eSpeak non riuscito")
                                                    End If
                                                Catch ex As Exception
                                                    RegistroErrori.Scrivi("PrimoAvvio.ScaricaEspeakSeManca", ex)
                                                End Try
                                            End Function)
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.ScaricaEspeakSeManca (avvio)", ex)
        End Try
    End Sub

    ' Se lo scaricamento in sottofondo è in corso lo aspetta, così non se ne fanno due insieme.
    Private Shared Sub AttendiScaricoInSottofondo()
        Try
            Dim scarico As Task = _scaricoInSottofondo
            If scarico IsNot Nothing AndAlso Not scarico.IsCompleted Then scarico.Wait(TimeSpan.FromMinutes(5))
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.AttendiScaricoInSottofondo", ex)
        End Try
    End Sub

    Friend Shared Function DaFare() As Boolean
        Try
            Return ConfigLettore.Leggi(ChiaveFatto, "").Trim() = ""
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.DaFare", ex)
            Return False
        End Try
    End Function

    ''' <summary>Le domande del primo avvio, una dopo l'altra. Se si annulla la prima, si richiedono al prossimo avvio.</summary>
    Friend Shared Async Sub EseguiSeServe(app As ApplicazioneLettore)
        Try
            If Not DaFare() Then Return
            Dim scelta As String = InstallazioneGuidata.Chiedi(Nothing)
            If scelta = "" Then Return
            Await InstallaConScelta(scelta, Nothing)
            ChiediVoceEVelocita(app)
            ConfigLettore.Scrivi(ChiaveFatto, "fatto")
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.EseguiSeServe", ex)
        End Try
    End Sub

    ''' <summary>La voce di menu: se il lettore non è installato chiede come installarlo, altrimenti aggiorna l'installazione già fatta.</summary>
    Friend Shared Async Sub InstallaODaMenu()
        Try
            If Installazione.InstallatoInProgrammi() Then
                Dim conAvvio As Boolean = Installazione.ServizioInstallato()
                Dim codiceAggiornamento As Integer = Await Task.Run(Function() Installazione.InstallaServizio(conAvvio))
                MessageBox.Show(MessaggioInstallazione("sistema", codiceAggiornamento, conAvvio), "Installazione del lettore di schermo", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Dim scelta As String = InstallazioneGuidata.Chiedi(Nothing)
            If scelta = "" Then Return
            Await InstallaConScelta(scelta, Nothing)
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.InstallaODaMenu", ex)
        End Try
    End Sub

    Private Shared Async Function InstallaConScelta(scelta As String, proprietario As IWin32Window) As Task
        Dim conAvvio As Boolean = True
        If scelta = "sistema" Then
            Using domanda As New FinestraAvvioConWindows()
                Dim esito As DialogResult = If(proprietario Is Nothing, domanda.ShowDialog(), domanda.ShowDialog(proprietario))
                If esito = DialogResult.Cancel Then Return
                conAvvio = domanda.ConAvvio
            End Using
        End If
        Dim codice As Integer = Await Task.Run(Function() If(scelta = "portatile", Installazione.InstallaPortatile(), Installazione.InstallaServizio(conAvvio)))
        MessageBox.Show(MessaggioInstallazione(scelta, codice, conAvvio), "Installazione del lettore di schermo", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Function

    Private Shared Function MessaggioInstallazione(scelta As String, codice As Integer, conAvvio As Boolean) As String
        If codice = 0 AndAlso scelta = "portatile" Then Return "Fatto. Il lettore resta in questa cartella e parte quando accedi a Windows."
        If codice = 0 AndAlso conAvvio Then Return "Fatto. Il lettore è installato e partirà con Windows, sulla schermata di accesso, su quella di blocco e sul controllo account utente."
        If codice = 0 Then Return "Fatto. Il lettore è installato in Programmi. Non parte da solo."
        If codice = 1223 Then Return "La conferma di amministratore non è stata data: non è stato cambiato niente."
        Return "Non è stato possibile completare l'installazione. Codice " & codice.ToString() & "."
    End Function

    Private Shared Sub ChiediVoceEVelocita(app As ApplicazioneLettore)
        Using finestra As New FinestraScelteVoce(app)
            finestra.ShowDialog()
        End Using
    End Sub

    ''' <summary>Scarica eSpeak con la finestra che dice a che punto si è. Vero se alla fine c'è.</summary>
    Friend Shared Function ScaricaEspeak(proprietario As IWin32Window) As Boolean
        Try
            AttendiScaricoInSottofondo()
            If WGEspeak.Installato() Then Return True
            Using finestra As New FinestraScaricoEspeak()
                If proprietario Is Nothing Then finestra.ShowDialog() Else finestra.ShowDialog(proprietario)
                Return WGEspeak.Installato()
            End Using
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.ScaricaEspeak", ex)
            Return False
        End Try
    End Function

    ''' <summary>Le voci di eSpeak come coppie nome e identificatore.</summary>
    Friend Shared Function VociEspeak() As List(Of KeyValuePair(Of String, String))
        Dim elenco As New List(Of KeyValuePair(Of String, String))()
        Try
            If WGEspeak.Installato() Then
                Dim motore As WGMotoreEspeak = WGMotoreEspeak.Istanza()
                If motore IsNot Nothing Then
                    For Each voce As VoceEspeak In motore.Voci()
                        elenco.Add(New KeyValuePair(Of String, String)(voce.Nome, voce.Identificatore))
                    Next
                End If
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.VociEspeak", ex)
        End Try
        If elenco.Count = 0 Then
            elenco.Add(New KeyValuePair(Of String, String)("Italiano", "it"))
            elenco.Add(New KeyValuePair(Of String, String)("Italiano, maschile 2", "it+m2"))
            elenco.Add(New KeyValuePair(Of String, String)("Italiano, femminile 1", "it+f1"))
            elenco.Add(New KeyValuePair(Of String, String)("Italiano, femminile 2", "it+f2"))
        End If
        Return elenco
    End Function

    Friend Shared Function VociWindows() As List(Of String)
        Dim voci As New List(Of String)()
        Try
            Using sintetizzatore As New System.Speech.Synthesis.SpeechSynthesizer()
                For Each voce As System.Speech.Synthesis.InstalledVoice In sintetizzatore.GetInstalledVoices()
                    If voce.Enabled AndAlso Not voci.Contains(voce.VoiceInfo.Name) Then voci.Add(voce.VoiceInfo.Name)
                Next
            End Using
        Catch ex As Exception
            RegistroErrori.Scrivi("PrimoAvvio.VociWindows", ex)
        End Try
        Return voci
    End Function

End Class

''' <summary>Dopo aver scelto l'installazione nel sistema: partire con Windows e sulla schermata di accesso, oppure no.</summary>
Friend NotInheritable Class FinestraAvvioConWindows
    Inherits Form

    Friend ConAvvio As Boolean = True

    Public Sub New()
        Text = "Avvio del lettore di schermo"
        StartPosition = FormStartPosition.CenterScreen
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = True
        ClientSize = New Size(640, 250)
        AccessibleName = Text

        Dim domanda As New Label()
        domanda.AutoSize = False
        domanda.Dock = DockStyle.Top
        domanda.Height = 150
        domanda.Padding = New Padding(12)
        domanda.Text = "Vuoi che il lettore di schermo si avvii da solo quando Windows parte e sulla schermata di accesso dell'utente?" & Environment.NewLine & Environment.NewLine &
            "Se rispondi di sì, il lettore parla anche prima di entrare in Windows, sulla schermata di blocco e sul controllo account utente. Se rispondi di no, viene solo installato in Programmi e lo avvii tu quando serve."
        domanda.AccessibleName = domanda.Text
        Controls.Add(domanda)

        Dim si As New Button()
        si.Text = "Sì, avvia il lettore con Windows"
        si.Size = New Size(260, 36)
        si.Location = New Point(16, 170)
        si.AccessibleName = si.Text
        si.TabIndex = 0
        AddHandler si.Click, Sub()
                                 ConAvvio = True
                                 DialogResult = DialogResult.Yes
                                 Close()
                             End Sub
        Controls.Add(si)

        Dim no As New Button()
        no.Text = "No, installa soltanto"
        no.Size = New Size(200, 36)
        no.Location = New Point(si.Right + 12, 170)
        no.AccessibleName = no.Text
        no.TabIndex = 1
        AddHandler no.Click, Sub()
                                 ConAvvio = False
                                 DialogResult = DialogResult.No
                                 Close()
                             End Sub
        Controls.Add(no)

        Dim annulla As New Button()
        annulla.Text = "Annulla"
        annulla.DialogResult = DialogResult.Cancel
        annulla.Size = New Size(110, 36)
        annulla.Location = New Point(no.Right + 12, 170)
        annulla.AccessibleName = annulla.Text
        annulla.TabIndex = 2
        Controls.Add(annulla)

        AcceptButton = si
        CancelButton = annulla
    End Sub

End Class

''' <summary>La scelta della voce e della velocità, con la possibilità di ascoltarle subito.</summary>
Friend NotInheritable Class FinestraScelteVoce
    Inherits Form

    Private ReadOnly _app As ApplicazioneLettore
    Private ReadOnly _motore As ComboBox
    Private ReadOnly _voce As ComboBox
    Private ReadOnly _velocita As NumericUpDown
    Private _vociEspeak As List(Of KeyValuePair(Of String, String))
    Private _vociWindows As List(Of String)

    Public Sub New(app As ApplicazioneLettore)
        _app = app
        Text = "Voce del lettore di schermo"
        StartPosition = FormStartPosition.CenterScreen
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = True
        ClientSize = New Size(640, 330)
        AccessibleName = Text

        Dim introduzione As New Label()
        introduzione.AutoSize = False
        introduzione.SetBounds(12, 10, 616, 56)
        introduzione.Text = "Scegli la sintesi vocale, la voce e la velocità. Con il pulsante Prova senti subito come suona. Tutto si può cambiare più avanti dalle impostazioni."
        introduzione.AccessibleName = introduzione.Text
        Controls.Add(introduzione)

        Dim etichettaMotore As New Label()
        etichettaMotore.Text = "Sintesi vocale"
        etichettaMotore.AutoSize = True
        etichettaMotore.Location = New Point(12, 80)
        Controls.Add(etichettaMotore)
        _motore = New ComboBox()
        _motore.DropDownStyle = ComboBoxStyle.DropDownList
        _motore.Items.Add("eSpeak, consigliata")
        _motore.Items.Add("Voci di Windows")
        _motore.SetBounds(200, 76, 420, 28)
        _motore.AccessibleName = "Sintesi vocale"
        _motore.TabIndex = 0
        _motore.SelectedIndex = If(String.Equals(ConfigLettore.Leggi("lettore_schermo_motore", "espeak").Trim(), "windows", StringComparison.OrdinalIgnoreCase), 1, 0)
        Controls.Add(_motore)

        Dim etichettaVoce As New Label()
        etichettaVoce.Text = "Voce"
        etichettaVoce.AutoSize = True
        etichettaVoce.Location = New Point(12, 124)
        Controls.Add(etichettaVoce)
        _voce = New ComboBox()
        _voce.DropDownStyle = ComboBoxStyle.DropDownList
        _voce.SetBounds(200, 120, 420, 28)
        _voce.AccessibleName = "Voce"
        _voce.TabIndex = 1
        Controls.Add(_voce)

        Dim etichettaVelocita As New Label()
        etichettaVelocita.Text = "Velocità, da meno 10 a 10"
        etichettaVelocita.AutoSize = True
        etichettaVelocita.Location = New Point(12, 168)
        Controls.Add(etichettaVelocita)
        _velocita = New NumericUpDown()
        _velocita.Minimum = -10
        _velocita.Maximum = 10
        Dim attuale As Integer = 3
        Integer.TryParse(ConfigLettore.Leggi("lettore_schermo_voce_velocita", "").Trim(), attuale)
        _velocita.Value = Math.Max(-10, Math.Min(10, attuale))
        _velocita.SetBounds(200, 164, 100, 28)
        _velocita.AccessibleName = "Velocità, da meno 10 a 10"
        _velocita.TabIndex = 2
        Controls.Add(_velocita)

        Dim prova As New Button()
        prova.Text = "Prova la voce"
        prova.SetBounds(200, 216, 180, 36)
        prova.AccessibleName = prova.Text
        prova.TabIndex = 3
        AddHandler prova.Click, Sub() ProvaLaVoce()
        Controls.Add(prova)

        Dim continua As New Button()
        continua.Text = "Continua"
        continua.SetBounds(ClientSize.Width - 140, ClientSize.Height - 52, 120, 36)
        continua.AccessibleName = continua.Text
        continua.TabIndex = 4
        AddHandler continua.Click, Sub() Conferma()
        Controls.Add(continua)

        AcceptButton = continua
        _vociEspeak = PrimoAvvio.VociEspeak()
        _vociWindows = PrimoAvvio.VociWindows()
        RiempiVoci()
        AddHandler _motore.SelectedIndexChanged, Sub() RiempiVoci()
    End Sub

    Private Function MotoreScelto() As String
        Return If(_motore.SelectedIndex = 1, "windows", "espeak")
    End Function

    Private Sub RiempiVoci()
        _voce.Items.Clear()
        If MotoreScelto() = "espeak" Then
            If WGEspeak.Installato() Then _vociEspeak = PrimoAvvio.VociEspeak()
            For Each coppia As KeyValuePair(Of String, String) In _vociEspeak
                _voce.Items.Add(coppia.Key)
            Next
            Dim attuale As String = ConfigLettore.Leggi("lettore_schermo_voce_espeak", "it").Trim()
            Dim indice As Integer = _vociEspeak.FindIndex(Function(c) String.Equals(c.Value, attuale, StringComparison.OrdinalIgnoreCase))
            _voce.SelectedIndex = Math.Max(0, indice)
        Else
            _voce.Items.Add("Voce predefinita di Windows")
            For Each nome As String In _vociWindows
                _voce.Items.Add(nome)
            Next
            Dim attuale As String = ConfigLettore.Leggi("lettore_schermo_voce_nome", "").Trim()
            Dim indice As Integer = If(attuale = "", 0, _voce.Items.IndexOf(attuale))
            _voce.SelectedIndex = Math.Max(0, indice)
        End If
    End Sub

    ' Scrive le scelte nella configurazione e le fa applicare al lettore: la prova usa la voce vera.
    Private Function ApplicaScelte() As Boolean
        If MotoreScelto() = "espeak" AndAlso Not WGEspeak.Installato() Then
            If Not PrimoAvvio.ScaricaEspeak(Me) Then
                MessageBox.Show(Me, "Non sono riuscito a scaricare eSpeak. Resta la voce di Windows.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                _motore.SelectedIndex = 1
                Return False
            End If
            _vociEspeak = PrimoAvvio.VociEspeak()
            RiempiVoci()
        End If
        ConfigLettore.Scrivi("lettore_schermo_motore", MotoreScelto())
        If MotoreScelto() = "espeak" Then
            If _voce.SelectedIndex >= 0 AndAlso _voce.SelectedIndex < _vociEspeak.Count Then ConfigLettore.Scrivi("lettore_schermo_voce_espeak", _vociEspeak(_voce.SelectedIndex).Value)
        Else
            ConfigLettore.Scrivi("lettore_schermo_voce_nome", If(_voce.SelectedIndex <= 0, "", _voce.SelectedItem.ToString()))
        End If
        ConfigLettore.Scrivi("lettore_schermo_voce_velocita", CInt(_velocita.Value).ToString())
        _app.Applica()
        Return True
    End Function

    Private Sub ProvaLaVoce()
        Try
            If Not ApplicaScelte() Then Return
            Lettore.GetInstance().Annuncia("Questa è la voce del lettore di schermo.")
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraScelteVoce.ProvaLaVoce", ex)
        End Try
    End Sub

    Private Sub Conferma()
        Try
            ApplicaScelte()
            DialogResult = DialogResult.OK
            Close()
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraScelteVoce.Conferma", ex)
        End Try
    End Sub

End Class

''' <summary>Scarica eSpeak e dice la percentuale ogni dieci per cento.</summary>
Friend NotInheritable Class FinestraScaricoEspeak
    Inherits Form

    Private ReadOnly _barra As ProgressBar
    Private ReadOnly _testo As Label
    Private ReadOnly _annulla As New CancellationTokenSource()
    Private _ultimo As Integer = -1

    Public Sub New()
        Text = "Scarico di eSpeak"
        StartPosition = FormStartPosition.CenterScreen
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ControlBox = False
        ShowInTaskbar = True
        ClientSize = New Size(520, 150)
        AccessibleName = Text

        _testo = New Label()
        _testo.AutoSize = False
        _testo.SetBounds(12, 12, 496, 50)
        _testo.Text = "Download di eSpeak in corso. È la voce del lettore di schermo, sono circa dodici megabyte."
        _testo.AccessibleName = _testo.Text
        Controls.Add(_testo)

        _barra = New ProgressBar()
        _barra.SetBounds(12, 70, 496, 24)
        _barra.Minimum = 0
        _barra.Maximum = 100
        _barra.AccessibleName = "Avanzamento dello scaricamento"
        Controls.Add(_barra)

        Dim annulla As New Button()
        annulla.Text = "Annulla"
        annulla.SetBounds(ClientSize.Width - 130, 106, 110, 32)
        annulla.AccessibleName = annulla.Text
        AddHandler annulla.Click, Sub() _annulla.Cancel()
        Controls.Add(annulla)
        CancelButton = annulla

        AddHandler Shown, AddressOf AlLaVisualizzazione
    End Sub

    Private Sub Annuncia(testo As String)
        Try
            Lettore.GetInstance().Annuncia(testo)
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraScaricoEspeak.Annuncia", ex)
        End Try
    End Sub

    Private Async Sub AlLaVisualizzazione(mittente As Object, e As EventArgs)
        Try
            Annuncia(_testo.Text)
            Dim progresso As New Progress(Of Integer)(Sub(percentuale As Integer)
                                                          Dim valore As Integer = Math.Min(100, Math.Max(0, percentuale))
                                                          _barra.Value = valore
                                                          Dim passo As Integer = (valore \ 10) * 10
                                                          If passo > _ultimo Then
                                                              _ultimo = passo
                                                              If passo = 100 Then
                                                                  Annuncia("Scaricamento completato, installazione in corso")
                                                              ElseIf passo > 0 Then
                                                                  Annuncia(passo.ToString() & " per cento")
                                                              End If
                                                          End If
                                                      End Sub)
            Dim errore As String = Await WGEspeak.InstallaAsync(progresso, _annulla.Token)
            If errore <> "" AndAlso Not _annulla.IsCancellationRequested Then
                MessageBox.Show(Me, "Non sono riuscito a installare eSpeak: " & errore, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraScaricoEspeak.AlLaVisualizzazione", ex)
        Finally
            Close()
        End Try
    End Sub

End Class
