Imports System.Windows.Forms

' Finestra di dialogo per la ricerca testuale rapida con gestione della tastiera e della voce.
Friend NotInheritable Class FinestraCerca
    Inherits Form

    Private ReadOnly _campo As TextBox
    Private ReadOnly _primaDiChiudere As Action
    Private _esito As String = ""
    Private Shared _ultimo As String = ""

    Private Sub New(primaDiChiudere As Action)
        _primaDiChiudere = primaDiChiudere
        Text = "Cerca nella pagina"
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.CenterScreen
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        TopMost = True
        KeyPreview = True
        ClientSize = New Size(440, 92)
        Dim etichetta As New Label() With {
            .Text = "Cerca nella pagina",
            .Left = 12,
            .Top = 12,
            .Width = 416,
            .Height = 20
        }
        _campo = New TextBox() With {
            .Left = 12,
            .Top = 40,
            .Width = 416,
            .Text = _ultimo,
            .AccessibleName = "Cerca nella pagina"
        }
        Controls.Add(etichetta)
        Controls.Add(_campo)
        AddHandler _campo.KeyDown, AddressOf AlTasto
        AddHandler Shown, AddressOf AllApertura
    End Sub

    Public Shared Function Chiedi(primaDiChiudere As Action) As String
        Using finestra As New FinestraCerca(primaDiChiudere)
            If finestra.ShowDialog() = DialogResult.OK Then Return finestra._esito
            Return ""
        End Using
    End Function

    Private Sub AllApertura(sender As Object, e As EventArgs)
        _campo.Focus()
        If _campo.Text <> "" Then
            _campo.SelectAll()
            Parla("Cerca nella pagina, " & _campo.Text)
        Else
            Parla("Cerca nella pagina")
        End If
    End Sub

    Private Sub AlTasto(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            e.Handled = True
            e.SuppressKeyPress = True
            Dim scritto As String = _campo.Text.Trim()
            If scritto = "" Then
                Parla("ricerca annullata")
                DialogResult = DialogResult.Cancel
                Close()
                Return
            End If
            _esito = scritto
            _ultimo = _campo.Text
            If _primaDiChiudere IsNot Nothing Then _primaDiChiudere.Invoke()
            DialogResult = DialogResult.OK
            Close()
            Return
        End If
        If e.KeyCode = Keys.Escape Then
            e.Handled = True
            e.SuppressKeyPress = True
            Parla("ricerca annullata")
            DialogResult = DialogResult.Cancel
            Close()
            Return
        End If
        If e.KeyCode = Keys.Back AndAlso Not e.Control Then
            If _campo.SelectionLength > 0 Then
                Parla("selezione cancellata")
            ElseIf _campo.SelectionStart > 0 AndAlso _campo.Text.Length > 0 Then
                Parla(_campo.Text.Substring(_campo.SelectionStart - 1, 1))
            Else
                Parla("vuoto")
            End If
        End If
    End Sub

    Protected Overrides Sub OnKeyPress(e As KeyPressEventArgs)
        MyBase.OnKeyPress(e)
        If e.Handled OrElse Char.IsControl(e.KeyChar) Then Return
        Parla(e.KeyChar.ToString())
    End Sub

    Private Shared Sub Parla(testo As String)
        Try
            If testo = "" Then Return
            Dim voce As IVoceLettore = VoceLettore.Corrente()
            If voce Is Nothing Then Return
            voce.LeggiTesto(testo, priorita:="Alta")
        Catch exAtteso As Exception
            RegistroErrori.Scrivi("FinestraCerca.Parla", exAtteso)
        End Try
    End Sub

End Class
