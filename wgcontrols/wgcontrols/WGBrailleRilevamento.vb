' ============================================================================================
' WGBrailleRilevamento - dove cercare le barre braille
' ============================================================================================
'
' A cosa serve
'   Elenca i posti dove puo' esserci una barra braille: dispositivi HID braille (nessun driver), dispositivi
'   HIMS WinUSB, porte seriali Bluetooth (SPP) e porte seriali via cavo (USB con porta COM virtuale). Per le
'   porte si legge da Windows (WMI) il nome del dispositivo, cosi' si provano da sole solo quelle che sembrano
'   barre braille (Braille Sense, Braille Edge, Smart Beetle, FTDI) e non, per esempio, il GPS o un modem.
'
' Errori
'   Nessun metodo solleva eccezioni: elenchi vuoti se qualcosa non va, con il motivo nel log.
' ============================================================================================
Imports System.IO.Ports
Imports System.Linq
Imports System.Management
Imports System.Text.RegularExpressions

Friend NotInheritable Class WGBrailleRilevamento

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _nomiBarra As New Regex("braille|hims|beetle|sense|edge|selvas|polaris|qbraille|\bBS ?\d|\bBE\b", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private Shared ReadOnly _portaNelNome As New Regex("\((COM\d+)\)", RegexOptions.IgnoreCase Or RegexOptions.Compiled)
    Private Shared ReadOnly _indirizzoBt As New Regex("([0-9A-F]{12})_C[0-9A-F]+$", RegexOptions.IgnoreCase Or RegexOptions.Compiled)

    Private Sub New()
    End Sub

    ''' <summary>Tutti i candidati: HID, WinUSB e porte seriali.</summary>
    Public Shared Function Candidati() As List(Of CandidatoBraille)
        Dim r As New List(Of CandidatoBraille)()
        Try
            r.AddRange(WGBrailleHid.Elenca())
        Catch ex As Exception
            log.ScriviLog("WGBrailleRilevamento.Candidati (HID): " & ex.Message)
        End Try
        Try
            Dim percorso As String = CollegamentoWinUsb.TrovaPercorso()
            If Not String.IsNullOrEmpty(percorso) Then
                Dim c As New CandidatoBraille()
                c.Tipo = "WinUsb"
                c.Percorso = percorso
                c.Descrizione = "Barra HIMS (WinUSB)"
                c.Collegamento = "Cavo"
                c.Probabile = True
                r.Add(c)
            End If
        Catch ex As Exception
            log.ScriviLog("WGBrailleRilevamento.Candidati (WinUSB): " & ex.Message)
        End Try
        r.AddRange(CandidatiSeriali())
        Return r
    End Function

    ''' <summary>Le porte COM del computer, con il nome del dispositivo e un giudizio su quanto sembrano una barra braille.</summary>
    Public Shared Function CandidatiSeriali() As List(Of CandidatoBraille)
        Dim r As New List(Of CandidatoBraille)()
        Try
            Dim presenti As New List(Of String)(SerialPort.GetPortNames())
            If presenti.Count = 0 Then Return r

            Dim nomiBt As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim voci As New List(Of String()) ' porta, deviceId, nome
            Try
                Using cerca As New ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'BTHENUM%' OR Name LIKE '%(COM%'")
                    For Each o As ManagementObject In cerca.Get()
                        Try
                            Dim nome As String = Convert.ToString(o("Name"))
                            Dim id As String = Convert.ToString(o("DeviceID"))
                            If id Is Nothing Then Continue For
                            If id.StartsWith("BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) Then
                                nomiBt(id.Substring("BTHENUM\DEV_".Length, Math.Min(12, id.Length - "BTHENUM\DEV_".Length))) = nome
                                Continue For
                            End If
                            Dim m As Match = _portaNelNome.Match(If(nome, ""))
                            If m.Success Then voci.Add(New String() {m.Groups(1).Value, id, nome})
                        Catch exVoce As Exception
                            log.ScriviLog("WGBrailleRilevamento.CandidatiSeriali (voce): " & exVoce.Message)
                        End Try
                    Next
                End Using
            Catch exWmi As Exception
                log.ScriviLog("WGBrailleRilevamento.CandidatiSeriali (WMI): " & exWmi.Message)
            End Try

            Dim trovate As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each v As String() In voci
                Dim porta As String = v(0)
                If Not presenti.Contains(porta, StringComparer.OrdinalIgnoreCase) Then Continue For
                Dim id As String = v(1)
                Dim c As New CandidatoBraille()
                c.Tipo = "Seriale"
                c.Percorso = porta.ToUpperInvariant()
                c.Descrizione = v(2)
                If id.StartsWith("BTHENUM", StringComparison.OrdinalIgnoreCase) Then
                    c.Collegamento = "Bluetooth"
                    Dim m As Match = _indirizzoBt.Match(id)
                    Dim indirizzo As String = If(m.Success, m.Groups(1).Value, "")
                    ' Le porte "in ingresso" del Bluetooth hanno indirizzo a zero: non sono verso una barra.
                    If indirizzo = "" OrElse indirizzo.Trim("0"c) = "" Then
                        trovate.Add(c.Percorso)
                        Continue For
                    End If
                    Dim nomeBarra As String = Nothing
                    If nomiBt.TryGetValue(indirizzo, nomeBarra) Then
                        c.Descrizione = nomeBarra
                        c.Probabile = _nomiBarra.IsMatch(nomeBarra)
                    End If
                Else
                    c.Collegamento = "Cavo"
                    c.Probabile = id.IndexOf("VID_0403&PID_6001", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                  id.IndexOf("VID_1A86&PID_55D3", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                  _nomiBarra.IsMatch(If(v(2), ""))
                End If
                If trovate.Add(c.Percorso) Then r.Add(c)
            Next
            ' Le porte che Windows non ha descritto restano selezionabili a mano.
            For Each p As String In presenti
                If trovate.Add(p.ToUpperInvariant()) Then
                    Dim c As New CandidatoBraille()
                    c.Tipo = "Seriale"
                    c.Percorso = p.ToUpperInvariant()
                    c.Descrizione = "Porta seriale"
                    c.Collegamento = "Cavo"
                    r.Add(c)
                End If
            Next
        Catch ex As Exception
            log.ScriviLog("WGBrailleRilevamento.CandidatiSeriali: " & ex.Message)
        End Try
        Return r
    End Function

End Class
