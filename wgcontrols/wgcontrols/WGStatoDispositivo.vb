' ============================================================================================
' WGStatoDispositivo - batteria e Wi-Fi come nella barra in alto di un telefono
' ============================================================================================
'
' Batteria: percentuale, se è in carica o collegata alla corrente, se c'è davvero una batteria.
' Wi-Fi: rete collegata e tacche da 0 a 4, come sui telefoni (dal segnale in percentuale: 75 e
' oltre 4 tacche, 50 tre, 25 due, sotto una). Il Wi-Fi si legge in sottofondo ogni dieci secondi
' con LeggiWifi, che imposta il programma principale (WGConnessioni sta in un'altra libreria), così
' il disegno della barra non aspetta mai.
'
'   WGStatoDispositivo.LeggiWifi = Function() ...      ' ssid e segnale in percentuale
'   Dim b As StatoBatteria = WGStatoDispositivo.Batteria()
'   Dim w As StatoWifi = WGStatoDispositivo.Wifi()
' ============================================================================================
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class StatoBatteria
    Public Presente As Boolean
    Public Percentuale As Integer
    Public InCarica As Boolean
    Public AllaCorrente As Boolean
    ' Secondi di autonomia stimati da Windows mentre si va a batteria; 0 o meno se non lo sa ancora.
    Public SecondiRimasti As Integer

    Public ReadOnly Property Descrizione As String
        Get
            If Not Presente Then Return "Alimentazione di rete, senza batteria"
            Return "Batteria " & Percentuale & " per cento" & If(InCarica, ", in carica", If(AllaCorrente, ", carica e collegata alla corrente", "")) &
                   If(Not AllaCorrente AndAlso SecondiRimasti > 0, ", restano " & WGStatoDispositivo.Durata(SecondiRimasti), "")
        End Get
    End Property
End Class

Public Class StatoWifi
    Public Connesso As Boolean
    Public Rete As String = ""
    Public Tacche As Integer

    Public ReadOnly Property Descrizione As String
        Get
            If Not Connesso Then Return "Wi-Fi non connesso"
            Return "Wi-Fi " & Rete & ", " & If(Tacche = 1, "una tacca", Tacche & " tacche") & " su 4"
        End Get
    End Property
End Class

Public NotInheritable Class WGStatoDispositivo

    Private Shared ReadOnly log As New Logger()
    Private Shared _wifi As New StatoWifi()
    Private Shared _istanteWifi As DateTime = DateTime.MinValue
    Private Shared _lettura As Integer

    Private Sub New()
    End Sub

    ''' <summary>Rete Wi-Fi collegata e segnale in percentuale; Nothing se non collegati.</summary>
    Public Shared Property LeggiWifi As Func(Of Tuple(Of String, Integer))

    Public Shared Function Batteria() As StatoBatteria
        Dim stato As New StatoBatteria()
        Try
            Dim potenza As PowerStatus = SystemInformation.PowerStatus
            stato.Presente = (potenza.BatteryChargeStatus And BatteryChargeStatus.NoSystemBattery) <> BatteryChargeStatus.NoSystemBattery AndAlso
                             (potenza.BatteryChargeStatus And BatteryChargeStatus.Unknown) <> BatteryChargeStatus.Unknown
            stato.Percentuale = Math.Max(0, Math.Min(100, CInt(Math.Round(potenza.BatteryLifePercent * 100))))
            stato.AllaCorrente = potenza.PowerLineStatus = PowerLineStatus.Online
            stato.InCarica = (potenza.BatteryChargeStatus And BatteryChargeStatus.Charging) = BatteryChargeStatus.Charging OrElse
                             (stato.AllaCorrente AndAlso stato.Percentuale < 100)
            stato.SecondiRimasti = If(stato.AllaCorrente, 0, potenza.BatteryLifeRemaining)
        Catch ex As Exception
            log.ScriviLog("WGStatoDispositivo.Batteria: " & ex.Message)
        End Try
        Return stato
    End Function

    ''' <summary>Una durata a parole: "2 ore e 15 minuti", "un'ora", "40 minuti".</summary>
    Public Shared Function Durata(secondi As Integer) As String
        Dim minutiTotali As Integer = Math.Max(1, CInt(Math.Round(secondi / 60.0)))
        Dim ore As Integer = minutiTotali \ 60
        Dim minuti As Integer = minutiTotali Mod 60
        Dim testoOre As String = If(ore = 0, "", If(ore = 1, "un'ora", ore & " ore"))
        Dim testoMinuti As String = If(minuti = 0, "", If(minuti = 1, "un minuto", minuti & " minuti"))
        If testoOre = "" Then Return testoMinuti
        If testoMinuti = "" Then Return testoOre
        Return testoOre & " e " & testoMinuti
    End Function

    ' Minuti alla carica completa secondo il computer; 0 se non lo dice (molti computer non lo dicono).
    Private Shared Function MinutiAllaCaricaCompleta() As Integer
        Try
            Using ricerca As New System.Management.ManagementObjectSearcher("SELECT TimeToFullCharge FROM Win32_Battery")
                For Each batteria As System.Management.ManagementBaseObject In ricerca.Get()
                    Dim valore As Object = batteria("TimeToFullCharge")
                    If valore Is Nothing Then Continue For
                    Dim minuti As Long = Convert.ToInt64(valore)
                    ' Valori enormi vogliono dire "sconosciuto".
                    If minuti > 0 AndAlso minuti < 24 * 60 Then Return CInt(minuti)
                Next
            End Using
        Catch ex As Exception
            log.ScriviLog("WGStatoDispositivo.MinutiAllaCaricaCompleta: " & ex.Message)
        End Try
        Return 0
    End Function

    ''' <summary>
    ''' Invio sulla batteria (barre di stato, centro di controllo): un messaggio con la percentuale, lo stato
    ''' e il tempo, quello che resta a batteria oppure, se il computer lo dice, quello che manca alla carica.
    ''' </summary>
    Public Shared Async Sub MostraBatteria()
        Try
            Dim stato As StatoBatteria = Batteria()
            Dim testo As String
            If Not stato.Presente Then
                testo = "Questo computer è alimentato dalla rete e non ha una batteria."
            ElseIf stato.AllaCorrente Then
                testo = "Batteria " & stato.Percentuale & " per cento. "
                If stato.Percentuale >= 100 Then
                    testo &= "Carica completa, collegata alla corrente."
                Else
                    Dim minuti As Integer = Await WGProcessoInCorso.EseguiAsync("Lettura della batteria", Function() Task.Run(Function() MinutiAllaCaricaCompleta()))
                    testo &= If(stato.InCarica, "In carica.", "Collegata alla corrente.") &
                             If(minuti > 0, " Carica completa fra " & Durata(minuti * 60) & ".", "")
                End If
            Else
                ' Il tempo si dice solo quando Windows lo sa: se non c'è niente da dire non si dice niente.
                testo = "Batteria " & stato.Percentuale & " per cento." &
                        If(stato.SecondiRimasti > 0, " Restano " & Durata(stato.SecondiRimasti) & ".", "")
            End If
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGStatoDispositivo.MostraBatteria: " & ex.Message)
        End Try
    End Sub

    Public Shared Function TaccheDaSegnale(segnale As Integer) As Integer
        If segnale >= 75 Then Return 4
        If segnale >= 50 Then Return 3
        If segnale >= 25 Then Return 2
        If segnale > 0 Then Return 1
        Return 0
    End Function

    ''' <summary>Ultimo stato del Wi-Fi letto; se è vecchio si rilegge in sottofondo.</summary>
    Public Shared Function Wifi() As StatoWifi
        If (DateTime.Now - _istanteWifi).TotalSeconds > 10 Then AggiornaWifi()
        Return _wifi
    End Function

    Public Shared Sub AggiornaWifi()
        If LeggiWifi Is Nothing Then Return
        If Interlocked.Exchange(_lettura, 1) = 1 Then Return
        _istanteWifi = DateTime.Now
        Task.Run(Sub()
                     Try
                         Dim letto As Tuple(Of String, Integer) = LeggiWifi.Invoke()
                         Dim nuovo As New StatoWifi()
                         If letto IsNot Nothing AndAlso Not String.IsNullOrEmpty(letto.Item1) Then
                             nuovo.Connesso = True
                             nuovo.Rete = letto.Item1
                             nuovo.Tacche = Math.Max(1, TaccheDaSegnale(letto.Item2))
                         End If
                         _wifi = nuovo
                     Catch ex As Exception
                         log.ScriviLog("WGStatoDispositivo.AggiornaWifi: " & ex.Message)
                     Finally
                         Interlocked.Exchange(_lettura, 0)
                     End Try
                 End Sub)
    End Sub

End Class
