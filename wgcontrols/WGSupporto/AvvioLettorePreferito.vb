Imports System.Diagnostics
Imports System.IO
Imports System.Threading.Tasks
Imports wgcontrols

' Avvio automatico del lettore di schermo preferito all'avvio del programma. Controlla lo stato dei processi in esecuzione.
Friend NotInheritable Class AvvioLettorePreferito

    Private Shared ReadOnly log As New Logger()
    Private Shared _registrato As Boolean

    Private Sub New()
    End Sub

    Friend Shared Sub Registra()
        Try
            If _registrato Then Return
            AddHandler WGScreenReader.PassatoInSecondoPiano, AddressOf QuandoInSecondoPiano
            _registrato = True
        Catch ex As Exception
            log.ScriviLog("AvvioLettorePreferito.Registra: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub QuandoInSecondoPiano(mittente As Object, e As EventArgs)
        Try
            Task.Run(AddressOf AvviaSeServe)
        Catch ex As Exception
            log.ScriviLog("AvvioLettorePreferito.QuandoInSecondoPiano: " & ex.Message)
        End Try
    End Sub

    Private Shared Function LeggiPreferito() As String
        Try
            Dim configurazione As New WGConfig()
            If Not configurazione.EsisteId(ServiziAvvio.ID_CONFIG_LETTORE_SCHERMO) Then Return ""
            Return If(configurazione.LeggiValore(ServiziAvvio.ID_CONFIG_LETTORE_SCHERMO), "").Trim()
        Catch ex As Exception
            log.ScriviLog("AvvioLettorePreferito.LeggiPreferito: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function NomeProcesso(nome As String) As String
        Select Case nome
            Case RilevatoreLettoreSchermo.NOME_NVDA : Return "nvda"
            Case RilevatoreLettoreSchermo.NOME_JAWS : Return "jfw"
            Case RilevatoreLettoreSchermo.NOME_KSR : Return "KSR"
            Case RilevatoreLettoreSchermo.NOME_ASSISTENTE_VOCALE : Return "Narrator"
            Case Else : Return ""
        End Select
    End Function

    Private Shared Sub AvviaSeServe()
        Try
            If WGScreenReader.ChiusuraAutomaticaSospesa Then Return
            If WGScreenReader.AvvioInCorso() Then
                log.ScriviLog("AvvioLettorePreferito: WinGabriel si sta avviando, il lettore non viene riavviato.")
                Return
            End If
            Dim preferito As String = LeggiPreferito()
            Dim nome As String = ""
            Dim percorso As String = ""

            If preferito = "" OrElse String.Equals(preferito, "Automatico", StringComparison.OrdinalIgnoreCase) Then
                nome = RilevatoreLettoreSchermo.Rileva(percorso)
            ElseIf String.Equals(preferito, RilevatoreLettoreSchermo.NOME_WINGABRIEL, StringComparison.OrdinalIgnoreCase) Then
                nome = RilevatoreLettoreSchermo.NOME_WINGABRIEL
                percorso = RilevatoreLettoreSchermo.TrovaLettoreWinGabriel()
            ElseIf String.Equals(preferito, RilevatoreLettoreSchermo.NOME_NVDA, StringComparison.OrdinalIgnoreCase) Then
                nome = RilevatoreLettoreSchermo.NOME_NVDA
                percorso = RilevatoreLettoreSchermo.TrovaNvda()
            ElseIf String.Equals(preferito, RilevatoreLettoreSchermo.NOME_JAWS, StringComparison.OrdinalIgnoreCase) Then
                nome = RilevatoreLettoreSchermo.NOME_JAWS
                percorso = RilevatoreLettoreSchermo.TrovaJaws()
            ElseIf String.Equals(preferito, RilevatoreLettoreSchermo.NOME_KSR, StringComparison.OrdinalIgnoreCase) Then
                nome = RilevatoreLettoreSchermo.NOME_KSR
                percorso = RilevatoreLettoreSchermo.TrovaKsr()
            ElseIf String.Equals(preferito, RilevatoreLettoreSchermo.NOME_ASSISTENTE_VOCALE, StringComparison.OrdinalIgnoreCase) Then
                nome = RilevatoreLettoreSchermo.NOME_ASSISTENTE_VOCALE
                percorso = RilevatoreLettoreSchermo.TrovaAssistenteVocale()
            End If

            If nome = "" OrElse percorso = "" Then Return

            If nome = RilevatoreLettoreSchermo.NOME_WINGABRIEL Then
                If Not LettoreEsterno.InEsecuzione() Then LettoreEsterno.AvviaOAggiorna()
                Return
            End If

            Dim processo As String = NomeProcesso(nome)
            If processo = "" OrElse RilevatoreLettoreSchermo.InEsecuzione(processo) Then Return

            log.ScriviLog("AvvioLettorePreferito: WinGabriel è in secondo piano, avvio " & nome & " (" & percorso & ").")
            Dim info As New ProcessStartInfo(percorso)
            info.UseShellExecute = True
            info.WorkingDirectory = Path.GetDirectoryName(percorso)
            Using avviato As Process = Process.Start(info)
            End Using
        Catch ex As Exception
            log.ScriviLog("AvvioLettorePreferito.AvviaSeServe: " & ex.Message)
        End Try
    End Sub

End Class
