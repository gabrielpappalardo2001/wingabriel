Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' L'installazione guidata del lettore di schermo. Chiede se usarlo in modo portatile oppure installarlo nel sistema.
''' L'installazione nel sistema copia il lettore in Programmi e lo registra sulla schermata di accesso, su quella di blocco
''' e sul controllo account utente. In quel passaggio, se NVDA è registrato sul controllo account utente, viene tolto da lì.
''' L'uso portatile lascia i file dove sono e fa partire il lettore all'accesso dell'utente, senza il controllo account utente.
''' </summary>
Friend NotInheritable Class InstallazioneGuidata

    Private Sub New()
    End Sub

    ''' <summary>Chiede la modalità. Restituisce "portatile", "sistema" oppure una stringa vuota se si annulla.</summary>
    Friend Shared Function Chiedi(proprietario As IWin32Window) As String
        Using finestra As New FinestraSceltaInstallazione()
            Dim esito As DialogResult = If(proprietario Is Nothing, finestra.ShowDialog(), finestra.ShowDialog(proprietario))
            If esito <> DialogResult.OK Then Return ""
            Return finestra.Scelta
        End Using
    End Function

    ''' <summary>Mostra la domanda e applica la scelta. -5 se si annulla, altrimenti il codice dell'installazione.</summary>
    Friend Shared Function Esegui() As Integer
        Application.EnableVisualStyles()
        Dim scelta As String = Chiedi(Nothing)
        Return Applica(scelta)
    End Function

    ''' <summary>Applica una scelta già fatta. -5 se la scelta è vuota.</summary>
    Friend Shared Function Applica(scelta As String) As Integer
        If scelta = "portatile" Then Return Installazione.InstallaPortatile()
        If scelta = "sistema" Then Return Installazione.InstallaServizio(True)
        Return -5
    End Function

End Class

''' <summary>
''' La domanda della procedura guidata: uso portatile oppure installazione nel sistema.
''' </summary>
Friend NotInheritable Class FinestraSceltaInstallazione
    Inherits Form

    Friend Scelta As String = ""

    Public Sub New()
        Text = "Installazione del lettore di schermo"
        StartPosition = FormStartPosition.CenterScreen
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = True
        ClientSize = New Size(640, 380)
        AccessibleName = Text
        AccessibleDescription = "Scegli se usare il lettore in modo portatile oppure installarlo nel sistema."

        Dim spiegazione As New Label()
        spiegazione.AutoSize = False
        spiegazione.Dock = DockStyle.Top
        spiegazione.Height = 180
        spiegazione.Padding = New Padding(12)
        spiegazione.Text = "Scegli come usare il lettore di schermo." & Environment.NewLine & Environment.NewLine &
            "Installazione nel sistema: il lettore viene copiato in Programmi. Parte all'accesso, sulla schermata di blocco e sul controllo account utente. Se NVDA è registrato sul controllo account utente, viene tolto da lì." & Environment.NewLine & Environment.NewLine &
            "Uso portatile: il lettore resta nella cartella da cui lo hai avviato e parte quando accedi a Windows. Non va sulla schermata di accesso né sul controllo account utente."
        spiegazione.AccessibleName = spiegazione.Text
        Controls.Add(spiegazione)

        Dim sistema As New RadioButton()
        sistema.Text = "Installazione nel sistema"
        sistema.Checked = True
        sistema.AutoSize = True
        sistema.Location = New Point(16, 196)
        sistema.AccessibleName = sistema.Text
        sistema.AccessibleDescription = "Copia il lettore in Programmi e lo mette anche sul controllo account utente. Se c'è NVDA, lo toglie da lì."
        sistema.TabIndex = 0
        Controls.Add(sistema)

        Dim portatile As New RadioButton()
        portatile.Text = "Uso portatile"
        portatile.AutoSize = True
        portatile.Location = New Point(16, 228)
        portatile.AccessibleName = portatile.Text
        portatile.AccessibleDescription = "Lascia il lettore in questa cartella. Parte all'accesso dell'utente e non va sul controllo account utente."
        portatile.TabIndex = 1
        Controls.Add(portatile)

        Dim annulla As New Button()
        annulla.Text = "Annulla"
        annulla.DialogResult = DialogResult.Cancel
        annulla.Size = New Size(120, 34)
        annulla.Location = New Point(ClientSize.Width - 136, ClientSize.Height - 50)
        annulla.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        annulla.AccessibleName = annulla.Text
        annulla.TabIndex = 3
        Controls.Add(annulla)

        Dim continua As New Button()
        continua.Text = "Continua"
        continua.Size = New Size(120, 34)
        continua.Location = New Point(annulla.Left - 132, annulla.Top)
        continua.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        continua.AccessibleName = continua.Text
        continua.TabIndex = 2
        AddHandler continua.Click, Sub()
                                        Scelta = If(portatile.Checked, "portatile", "sistema")
                                        DialogResult = DialogResult.OK
                                        Close()
                                    End Sub
        Controls.Add(continua)

        AcceptButton = continua
        CancelButton = annulla
    End Sub

End Class
