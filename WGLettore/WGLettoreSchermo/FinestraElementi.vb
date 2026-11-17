Imports System.Collections.Generic
Imports System.Windows.Forms
Imports UIAutomationClient

' Elenco navigabile degli elementi della pagina con gruppi, selezione e lettura vocale.
Friend NotInheritable Class VoceElementoPagina
    Private Shared ReadOnly logVoce As New Logger()
    Private _elemento As IUIAutomationElement

    Public Property Risolutore As Func(Of IUIAutomationElement)

    Public Property Elemento As IUIAutomationElement
        Get
            Try
                If _elemento Is Nothing AndAlso Risolutore IsNot Nothing Then
                    Dim origine As Func(Of IUIAutomationElement) = Risolutore
                    Risolutore = Nothing
                    _elemento = origine.Invoke()
                End If
            Catch ex As Exception
                logVoce.ScriviLog("VoceElementoPagina.Elemento: " & ex.Message)
            End Try
            Return _elemento
        End Get
        Set(value As IUIAutomationElement)
            _elemento = value
        End Set
    End Property

    Public Property Testo As String = ""
End Class

Friend NotInheritable Class FinestraElementi
    Inherits Form

    Public Shared ReadOnly Gruppi As String() = {"Link", "Intestazioni", "Campi", "Pulsanti", "Regioni"}

    Private Shared _ultimoGruppo As Integer
    Private Shared ReadOnly log As New Logger()

    Private ReadOnly _voci As Dictionary(Of String, List(Of VoceElementoPagina))
    Private ReadOnly _primaDiChiudere As Action
    Private ReadOnly _etichetta As Label
    Private ReadOnly _elenco As ListBox
    Private _gruppo As Integer
    Private _scelta As VoceElementoPagina
    Private _attiva As Boolean
    Private _caricamento As Boolean

    Private Sub New(voci As Dictionary(Of String, List(Of VoceElementoPagina)), primaDiChiudere As Action)
        _voci = voci
        _primaDiChiudere = primaDiChiudere
        Text = "Elenco degli elementi"
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.CenterScreen
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        TopMost = True
        KeyPreview = True
        ClientSize = New Size(520, 360)
        _etichetta = New Label() With {
            .Left = 12,
            .Top = 12,
            .Width = 496,
            .Height = 20
        }
        _elenco = New ListBox() With {
            .Left = 12,
            .Top = 40,
            .Width = 496,
            .Height = 308,
            .IntegralHeight = False,
            .AccessibleName = "Elementi"
        }
        Controls.Add(_etichetta)
        Controls.Add(_elenco)
        AddHandler _elenco.SelectedIndexChanged, AddressOf AlCambioVoce
        AddHandler KeyDown, AddressOf AlTasto
        AddHandler Shown, AddressOf AllApertura
    End Sub

    Public Shared Function Scegli(voci As Dictionary(Of String, List(Of VoceElementoPagina)), primaDiChiudere As Action, ByRef attiva As Boolean) As VoceElementoPagina
        attiva = False
        Try
            If voci Is Nothing Then Return Nothing
            Using finestra As New FinestraElementi(voci, primaDiChiudere)
                If finestra.ShowDialog() <> DialogResult.OK Then Return Nothing
                attiva = finestra._attiva
                Return finestra._scelta
            End Using
        Catch ex As Exception
            log.ScriviLog("FinestraElementi.Scegli: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub AllApertura(sender As Object, e As EventArgs)
        Try
            _gruppo = _ultimoGruppo
            If _gruppo < 0 OrElse _gruppo >= Gruppi.Length Then _gruppo = 0
            If VociGruppo(_gruppo).Count = 0 Then
                For indice As Integer = 0 To Gruppi.Length - 1
                    If VociGruppo(indice).Count > 0 Then
                        _gruppo = indice
                        Exit For
                    End If
                Next
            End If
            _elenco.Focus()
            MostraGruppo("Elenco degli elementi, ")
        Catch ex As Exception
            log.ScriviLog("FinestraElementi.AllApertura: " & ex.Message)
        End Try
    End Sub

    Private Function VociGruppo(indice As Integer) As List(Of VoceElementoPagina)
        Dim elenco As List(Of VoceElementoPagina) = Nothing
        If indice >= 0 AndAlso indice < Gruppi.Length AndAlso _voci.TryGetValue(Gruppi(indice), elenco) AndAlso elenco IsNot Nothing Then Return elenco
        Return New List(Of VoceElementoPagina)()
    End Function

    Private Sub MostraGruppo(prefisso As String)
        Try
            _caricamento = True
            Dim elenco As List(Of VoceElementoPagina) = VociGruppo(_gruppo)
            _etichetta.Text = Gruppi(_gruppo) & ": frecce a destra e a sinistra cambiano tipo, Invio va all'elemento, Control Invio lo attiva"
            _elenco.BeginUpdate()
            _elenco.Items.Clear()
            For Each voce As VoceElementoPagina In elenco
                _elenco.Items.Add(voce.Testo)
            Next
            _elenco.EndUpdate()
            If elenco.Count > 0 Then _elenco.SelectedIndex = 0
            _caricamento = False
            Dim quanti As String = If(elenco.Count = 0, "nessun elemento", If(elenco.Count = 1, "1 elemento", elenco.Count.ToString() & " elementi"))
            Dim frase As String = prefisso & Gruppi(_gruppo) & ", " & quanti
            If elenco.Count > 0 Then frase &= ", " & FraseVoce(0)
            Parla(frase)
        Catch ex As Exception
            _caricamento = False
            log.ScriviLog("FinestraElementi.MostraGruppo: " & ex.Message)
        End Try
    End Sub

    Private Function FraseVoce(indice As Integer) As String
        Dim elenco As List(Of VoceElementoPagina) = VociGruppo(_gruppo)
        If indice < 0 OrElse indice >= elenco.Count Then Return ""
        Return elenco(indice).Testo & ", " & (indice + 1).ToString() & " di " & elenco.Count.ToString()
    End Function

    Private Sub AlCambioVoce(sender As Object, e As EventArgs)
        Try
            If _caricamento Then Return
            Parla(FraseVoce(_elenco.SelectedIndex))
        Catch ex As Exception
            log.ScriviLog("FinestraElementi.AlCambioVoce: " & ex.Message)
        End Try
    End Sub

    Private Sub AlTasto(sender As Object, e As KeyEventArgs)
        Try
            Select Case e.KeyCode
                Case Keys.Left, Keys.Right
                    e.Handled = True
                    e.SuppressKeyPress = True
                    Dim passo As Integer = If(e.KeyCode = Keys.Right, 1, -1)
                    _gruppo = (_gruppo + passo + Gruppi.Length) Mod Gruppi.Length
                    _ultimoGruppo = _gruppo
                    MostraGruppo("")
                Case Keys.Enter
                    e.Handled = True
                    e.SuppressKeyPress = True
                    Dim elenco As List(Of VoceElementoPagina) = VociGruppo(_gruppo)
                    Dim indice As Integer = _elenco.SelectedIndex
                    If indice < 0 OrElse indice >= elenco.Count Then
                        Parla("nessun elemento")
                        Return
                    End If
                    _scelta = elenco(indice)
                    _attiva = e.Control
                    _ultimoGruppo = _gruppo
                    If _primaDiChiudere IsNot Nothing Then _primaDiChiudere.Invoke()
                    DialogResult = DialogResult.OK
                    Close()
                Case Keys.Escape
                    e.Handled = True
                    e.SuppressKeyPress = True
                    _ultimoGruppo = _gruppo
                    Parla("chiuso")
                    DialogResult = DialogResult.Cancel
                    Close()
            End Select
        Catch ex As Exception
            log.ScriviLog("FinestraElementi.AlTasto: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Parla(testo As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            Dim voce As IVoceLettore = VoceLettore.Corrente()
            If voce Is Nothing Then Return
            voce.LeggiTesto(testo, priorita:="Alta")
        Catch ex As Exception
            log.ScriviLog("FinestraElementi.Parla: " & ex.Message)
        End Try
    End Sub

End Class