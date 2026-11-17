Imports System.Drawing
Imports System.Windows.Forms
Imports WGLettoreSchermo

''' <summary>La schermata che si apre quando si sceglie Uscita: uscire, riavviare, oppure riavviare senza i componenti aggiuntivi.</summary>
Friend NotInheritable Class FinestraUscita
    Inherits Form

    Friend Scelta As String = "uscita"

    ' Chi ospita la schermata dà la funzione che dice le frasi; la schermata le dice da sé, appena si apre e a ogni pulsante raggiunto.
    Friend Annuncia As Action(Of String)
    Private ReadOnly _pulsanti As New List(Of Button)()
    Private _aperta As Boolean

    Public Sub New()
        Text = "Uscita dal lettore di schermo"
        StartPosition = FormStartPosition.CenterScreen
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = True
        ClientSize = New Size(660, 116)
        AccessibleName = Text

        Dim etichetta As New Label()
        etichetta.Text = "Che cosa vuoi fare?"
        etichetta.AutoSize = True
        etichetta.Location = New Point(16, 16)
        Controls.Add(etichetta)

        ' Pulsanti in fila: ci si sposta con le frecce destra e sinistra (e con Tab), si sceglie con Invio.
        Dim uscita As Button = NuovoPulsante("Uscita", "uscita", 16, 110, 0)
        NuovoPulsante("Riavvio", "riavvio", 134, 110, 1)
        NuovoPulsante("Riavvio senza componenti aggiuntivi", "riavvio-senza", 252, 270, 2)

        Dim annulla As New Button()
        annulla.Text = "Annulla"
        annulla.DialogResult = DialogResult.Cancel
        annulla.SetBounds(530, 56, 110, 36)
        annulla.AccessibleName = annulla.Text
        annulla.TabIndex = 3
        Controls.Add(annulla)

        Registra(annulla)
        CancelButton = annulla
        ActiveControl = uscita
    End Sub

    Private Function NuovoPulsante(testo As String, scelta As String, sinistra As Integer, larghezza As Integer, ordine As Integer) As Button
        Dim pulsante As New Button()
        pulsante.Text = testo
        pulsante.SetBounds(sinistra, 56, larghezza, 36)
        pulsante.AccessibleName = testo
        pulsante.TabIndex = ordine
        AddHandler pulsante.Click, Sub()
                                       Me.Scelta = scelta
                                       DialogResult = DialogResult.OK
                                       Close()
                                   End Sub
        Controls.Add(pulsante)
        Registra(pulsante)
        Return pulsante
    End Function

    Private Sub Registra(pulsante As Button)
        _pulsanti.Add(pulsante)
        AddHandler pulsante.GotFocus, Sub() DiciPulsante(pulsante)
    End Sub

    Private Sub DiciPulsante(pulsante As Button)
        Try
            If Not _aperta OrElse Annuncia Is Nothing Then Return
            Annuncia.Invoke(pulsante.Text & ", pulsante, " & (_pulsanti.IndexOf(pulsante) + 1).ToString() & " di " & _pulsanti.Count.ToString())
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraUscita.DiciPulsante", ex)
        End Try
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Try
            _aperta = True
            If Annuncia Is Nothing Then Return
            Annuncia.Invoke(Text & ", Che cosa vuoi fare?, " & ActiveControlTesto() & ", pulsante, 1 di " & _pulsanti.Count.ToString())
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraUscita.OnShown", ex)
        End Try
    End Sub

    Private Function ActiveControlTesto() As String
        If _pulsanti.Count > 0 Then Return _pulsanti(0).Text
        Return ""
    End Function

End Class

''' <summary>L'elenco dei componenti aggiuntivi installati, con la possibilità di disattivarne uno, riattivarlo o disinstallarlo.</summary>
Friend NotInheritable Class FinestraComponenti
    Inherits Form

    Private ReadOnly _elenco As ListBox
    Private ReadOnly _attiva As Button
    Private ReadOnly _disinstalla As Button
    Private _info As List(Of InfoComponente) = New List(Of InfoComponente)()

    Public Sub New()
        Text = "Componenti aggiuntivi"
        StartPosition = FormStartPosition.CenterScreen
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = True
        ClientSize = New Size(640, 380)
        AccessibleName = Text

        Dim etichetta As New Label()
        etichetta.Text = "Componenti aggiuntivi installati"
        etichetta.AutoSize = True
        etichetta.Location = New Point(12, 12)
        Controls.Add(etichetta)

        _elenco = New ListBox()
        _elenco.SetBounds(12, 36, 616, 230)
        _elenco.AccessibleName = "Componenti aggiuntivi installati"
        _elenco.TabIndex = 0
        AddHandler _elenco.SelectedIndexChanged, Sub() AggiornaPulsanti()
        Controls.Add(_elenco)

        _attiva = New Button()
        _attiva.SetBounds(12, 282, 180, 36)
        _attiva.Text = "Disattiva"
        _attiva.AccessibleName = _attiva.Text
        _attiva.TabIndex = 1
        AddHandler _attiva.Click, Sub() AlternaAttivo()
        Controls.Add(_attiva)

        _disinstalla = New Button()
        _disinstalla.SetBounds(204, 282, 180, 36)
        _disinstalla.Text = "Disinstalla"
        _disinstalla.AccessibleName = _disinstalla.Text
        _disinstalla.TabIndex = 2
        AddHandler _disinstalla.Click, Sub() Disinstalla()
        Controls.Add(_disinstalla)

        Dim chiudi As New Button()
        chiudi.Text = "Chiudi"
        chiudi.DialogResult = DialogResult.Cancel
        chiudi.SetBounds(ClientSize.Width - 130, ClientSize.Height - 52, 110, 36)
        chiudi.AccessibleName = chiudi.Text
        chiudi.TabIndex = 3
        Controls.Add(chiudi)
        CancelButton = chiudi
        AcceptButton = chiudi

        Riempi(Nothing)
    End Sub

    Private Function Selezionato() As InfoComponente
        If _elenco.SelectedIndex < 0 OrElse _elenco.SelectedIndex >= _info.Count Then Return Nothing
        Return _info(_elenco.SelectedIndex)
    End Function

    Private Sub Riempi(fileDaSelezionare As String)
        _info = ComponentiAggiuntivi.Elenco()
        _elenco.Items.Clear()
        For Each componente As InfoComponente In _info
            Dim riga As String = componente.Nome & ", " & If(componente.Attivo, "attivo", "disattivato")
            If componente.Programmi.Length > 0 Then riga &= ", per " & String.Join(", ", componente.Programmi)
            _elenco.Items.Add(riga)
        Next
        If _elenco.Items.Count > 0 Then
            Dim indice As Integer = If(fileDaSelezionare Is Nothing, -1, _info.FindIndex(Function(c) String.Equals(c.File, fileDaSelezionare, StringComparison.OrdinalIgnoreCase)))
            _elenco.SelectedIndex = Math.Max(0, indice)
        End If
        AggiornaPulsanti()
    End Sub

    Private Sub AggiornaPulsanti()
        Dim scelto As InfoComponente = Selezionato()
        _attiva.Enabled = scelto IsNot Nothing
        _disinstalla.Enabled = scelto IsNot Nothing
        _attiva.Text = If(scelto IsNot Nothing AndAlso Not scelto.Attivo, "Attiva", "Disattiva")
        _attiva.AccessibleName = _attiva.Text
    End Sub

    Private Sub AlternaAttivo()
        Try
            Dim scelto As InfoComponente = Selezionato()
            If scelto Is Nothing Then Return
            ComponentiAggiuntivi.ImpostaAttivo(scelto.File, Not scelto.Attivo)
            Riempi(scelto.File)
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraComponenti.AlternaAttivo", ex)
        End Try
    End Sub

    Private Sub Disinstalla()
        Try
            Dim scelto As InfoComponente = Selezionato()
            If scelto Is Nothing Then Return
            Dim risposta As DialogResult = MessageBox.Show(Me, "Vuoi disinstallare «" & scelto.Nome & "»? Il suo file viene cancellato e non si può recuperare.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2)
            If risposta <> DialogResult.Yes Then Return
            If ComponentiAggiuntivi.Disinstalla(scelto.File) Then
                Riempi(Nothing)
            Else
                MessageBox.Show(Me, "Non sono riuscito a cancellare il file del componente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End If
            If _info.Count = 0 Then Close()
        Catch ex As Exception
            RegistroErrori.Scrivi("FinestraComponenti.Disinstalla", ex)
        End Try
    End Sub

End Class
