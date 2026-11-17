Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Windows.Forms

''' <summary>
''' La guida del lettore di schermo. Il testo sta nella soluzione WGLettore, nel file Guida\lettore-schermo.txt,
''' incorporato nel programma. Si apre dall'icona accanto all'orologio e dalle impostazioni, scheda Sistema.
''' </summary>
Friend NotInheritable Class GuidaLettore

    Private Const NomeRisorsa As String = "WGLettore.Guida.lettore-schermo.txt"

    Private Sub New()
    End Sub

    ''' <summary>Il testo della guida, oppure una stringa vuota se la risorsa non c'è.</summary>
    Friend Shared Function Testo() As String
        Try
            Using flusso As Stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(NomeRisorsa)
                If flusso Is Nothing Then Return ""
                Using lettore As New StreamReader(flusso, Encoding.UTF8, True)
                    Return lettore.ReadToEnd()
                End Using
            End Using
        Catch ex As Exception
            Trace.WriteLine("GuidaLettore: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>Apre la guida in una finestra di sola lettura.</summary>
    Friend Shared Sub Mostra(proprietario As IWin32Window)
        Dim testo As String = GuidaLettore.Testo()
        If testo = "" Then
            MessageBox.Show(proprietario, "La guida del lettore di schermo non è stata trovata.", "Lettore di schermo", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Using finestra As New FinestraGuida(testo)
            If proprietario Is Nothing Then
                finestra.ShowDialog()
            Else
                finestra.ShowDialog(proprietario)
            End If
        End Using
    End Sub

End Class

''' <summary>Finestra di sola lettura con il testo della guida del lettore.</summary>
Friend NotInheritable Class FinestraGuida
    Inherits Form

    Public Sub New(testo As String)
        Text = "Guida del lettore di schermo"
        StartPosition = FormStartPosition.CenterScreen
        Size = New Size(760, 560)
        MinimumSize = New Size(480, 320)
        ShowInTaskbar = True
        AccessibleName = Text

        Dim pagine As New TextBox()
        pagine.Multiline = True
        pagine.ReadOnly = True
        pagine.ScrollBars = ScrollBars.Vertical
        pagine.Dock = DockStyle.Fill
        pagine.Text = testo
        pagine.AccessibleName = "Testo della guida"
        Controls.Add(pagine)

        Dim chiudi As New Button()
        chiudi.Text = "Chiudi"
        chiudi.Dock = DockStyle.Bottom
        chiudi.Height = 36
        chiudi.DialogResult = DialogResult.OK
        chiudi.AccessibleName = chiudi.Text
        Controls.Add(chiudi)
        AcceptButton = chiudi
        CancelButton = chiudi

        AddHandler Shown, Sub() pagine.Select(0, 0)
    End Sub

End Class
