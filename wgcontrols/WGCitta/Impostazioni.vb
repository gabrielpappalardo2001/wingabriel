Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq
Imports NAudio.CoreAudioApi
Imports wgcontrols

''' <summary>
''' Le impostazioni di Porto Franco, salvate nella configurazione di WinGabriel (chiavi che cominciano con Citta):
''' volume dei suoni, volume della radio, audio 3D (solo in cuffia: automatico, sempre, mai), guida assistita (il navigatore svolta da solo),
''' quanti annunci (tutti o solo gli essenziali) e i tasti.
''' </summary>
Public NotInheritable Class ImpostazioniCitta
    Public Volume As Integer = 80
    Public VolumeRadio As Integer = 60
    ''' <summary>automatico, si, no.</summary>
    Public Audio3D As String = "automatico"
    Public GuidaAssistita As Boolean = True
    ''' <summary>Vero: solo gli annunci essenziali (niente nome della strada a ogni svolta, niente descrizioni dei quartieri ripetute).</summary>
    Public AnnunciEssenziali As Boolean
    ''' <summary>poco, normale, tanto.</summary>
    Public Traffico As String = "normale"
    ''' <summary>tranquilla, normale, severa.</summary>
    Public Polizia As String = "normale"
    ''' <summary>normale (un secondo è un minuto), lento (la metà), lentissimo (un quarto).</summary>
    Public Orologio As String = "normale"
    Public ReadOnly Tasti As New Dictionary(Of String, Keys)()

    ''' <summary>Le azioni a cui si può dare un tasto, con il nome a voce e il tasto di partenza.</summary>
    Public Shared ReadOnly Azioni As (Id As String, Nome As String, Tasto As Keys)() = {
        ("accelera", "Accelerare, camminare", Keys.Up),
        ("frena", "Frenare, tornare indietro a piedi", Keys.Down),
        ("sinistra", "Svoltare a sinistra", Keys.Left),
        ("destra", "Svoltare a destra", Keys.Right),
        ("sali", "Salire e scendere", Keys.Enter),
        ("entra", "Clacson, entrare in un luogo", Keys.Space),
        ("dove", "Dove sei", Keys.D),
        ("veicolo", "Il veicolo", Keys.V),
        ("stato", "Soldi e ricercato", Keys.S),
        ("missione", "La missione", Keys.M),
        ("navigatore", "L'indicazione del navigatore", Keys.G),
        ("meta", "Scegliere la meta", Keys.N),
        ("spegni", "Spegnere il navigatore", Keys.X),
        ("telefono", "Il telefono", Keys.T),
        ("luoghi", "I luoghi vicini", Keys.L),
        ("intorno", "Chi c'è intorno", Keys.Q),
        ("radio", "Accendere e cambiare la radio", Keys.R),
        ("lavoro", "Cominciare o finire un lavoro", Keys.J),
        ("ora", "L'ora e il tempo", Keys.O),
        ("radar", "Il radar dei veicoli intorno", Keys.A),
        ("rileggi", "Rileggere gli ultimi annunci, andando indietro", Keys.Z),
        ("frenoamano", "Inversione di marcia", Keys.B),
        ("garage", "Mettere il veicolo in garage, a casa o in un rifugio", Keys.P),
        ("banda", "La banda del quartiere", Keys.K),
        ("comandi", "L'elenco dei comandi", Keys.F1),
        ("pausa", "La pausa", Keys.Escape)
    }

    Public Shared Function Carica() As ImpostazioniCitta
        Try
            Dim i As New ImpostazioniCitta()
            For Each a In Azioni
                i.Tasti(a.Id) = a.Tasto
            Next
            Dim c As New WGConfig()
            Dim v As Integer
            If c.EsisteId("CittaVolume") AndAlso Integer.TryParse(c.LeggiValore("CittaVolume"), v) Then i.Volume = Math.Max(0, Math.Min(100, v))
            If c.EsisteId("CittaVolumeRadio") AndAlso Integer.TryParse(c.LeggiValore("CittaVolumeRadio"), v) Then i.VolumeRadio = Math.Max(0, Math.Min(100, v))
            If c.EsisteId("CittaAudio3D") Then i.Audio3D = If(c.LeggiValore("CittaAudio3D"), "automatico")
            If c.EsisteId("CittaGuidaAssistita") Then i.GuidaAssistita = String.Equals(c.LeggiValore("CittaGuidaAssistita"), "true", StringComparison.OrdinalIgnoreCase)
            If c.EsisteId("CittaAnnunci") Then i.AnnunciEssenziali = c.LeggiValore("CittaAnnunci") = "essenziali"
            If c.EsisteId("CittaTraffico") Then i.Traffico = If(c.LeggiValore("CittaTraffico"), "normale")
            If c.EsisteId("CittaPolizia") Then i.Polizia = If(c.LeggiValore("CittaPolizia"), "normale")
            If c.EsisteId("CittaOrologio") Then i.Orologio = If(c.LeggiValore("CittaOrologio"), "normale")
            If c.EsisteId("CittaTasti") Then
                Try
                    Dim j As JObject = JObject.Parse(c.LeggiValore("CittaTasti"))
                    For Each p As JProperty In j.Properties()
                        If i.Tasti.ContainsKey(p.Name) Then i.Tasti(p.Name) = CType(CInt(p.Value), Keys)
                    Next
                Catch ex As Exception
                    RegistroErrori.Registra("ImpostazioniCitta.Carica (tasti)", ex)
                End Try
            End If
            Return i
        Catch ex As Exception
            RegistroErrori.Registra("ImpostazioniCitta.Carica", ex)
            Throw
        End Try
    End Function

    Private Shared Sub Scrivi(c As WGConfig, id As String, valore As String)
        Try
            If c.EsisteId(id) Then c.ModificaValore(id, valore) Else c.AggiungiValore(id, valore)
        Catch ex As Exception
            RegistroErrori.Registra("ImpostazioniCitta.Scrivi", ex)
            Throw
        End Try
    End Sub

    Public Sub Salva()
        Try
            Dim c As New WGConfig()
            Scrivi(c, "CittaVolume", Volume.ToString())
            Scrivi(c, "CittaVolumeRadio", VolumeRadio.ToString())
            Scrivi(c, "CittaAudio3D", Audio3D)
            Scrivi(c, "CittaGuidaAssistita", If(GuidaAssistita, "true", "false"))
            Scrivi(c, "CittaAnnunci", If(AnnunciEssenziali, "essenziali", "tutti"))
            Scrivi(c, "CittaTraffico", Traffico)
            Scrivi(c, "CittaPolizia", Polizia)
            Scrivi(c, "CittaOrologio", Orologio)
            Dim j As New JObject()
            For Each kv As KeyValuePair(Of String, Keys) In Tasti
                j(kv.Key) = CInt(kv.Value)
            Next
            Scrivi(c, "CittaTasti", j.ToString(Newtonsoft.Json.Formatting.None))
        Catch ex As Exception
            RegistroErrori.Registra("ImpostazioniCitta.Salva", ex)
            Throw
        End Try
    End Sub

    ''' <summary>L'azione di un tasto ("" se il tasto non fa niente).</summary>
    Public Function AzioneDi(k As Keys) As String
        Try
            For Each kv As KeyValuePair(Of String, Keys) In Tasti
                If kv.Value = k Then Return kv.Key
            Next
            Return ""
        Catch ex As Exception
            RegistroErrori.Registra("ImpostazioniCitta.AzioneDi", ex)
            Throw
        End Try
    End Function

    ''' <summary>Il nome di un tasto a voce: "freccia su", "Invio", "F1", "D".</summary>
    Public Shared Function NomeTasto(k As Keys) As String
        Try
            Select Case k
                Case Keys.Up : Return "freccia su"
                Case Keys.Down : Return "freccia giù"
                Case Keys.Left : Return "freccia sinistra"
                Case Keys.Right : Return "freccia destra"
                Case Keys.Enter : Return "Invio"
                Case Keys.Space : Return "Spazio"
                Case Keys.Escape : Return "Esc"
                Case Keys.Back : Return "Backspace"
                Case Keys.Tab : Return "Tab"
                Case Keys.Oemplus : Return "più"
                Case Keys.OemMinus : Return "meno"
                Case Else : Return k.ToString()
            End Select
        Catch ex As Exception
            RegistroErrori.Registra("ImpostazioniCitta.NomeTasto", ex)
            Throw
        End Try
    End Function

    ''' <summary>
    ''' L'audio 3D si usa solo in cuffia: con "automatico" si guarda se l'uscita audio di Windows è una cuffia (il tipo del dispositivo o il nome);
    ''' "si" e "no" lo forzano.
    ''' </summary>
    Public Function Usa3D() As Boolean
        Try
            If Audio3D = "si" Then Return True
            If Audio3D = "no" Then Return False
            Return CuffieCollegate()
        Catch ex As Exception
            RegistroErrori.Registra("ImpostazioniCitta.Usa3D", ex)
            Throw
        End Try
    End Function

    Public Shared Function CuffieCollegate() As Boolean
        Try
            Using e As New MMDeviceEnumerator()
                Dim d As MMDevice = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                ' il tipo del dispositivo (PKEY_AudioEndpoint_FormFactor): 3 cuffie, 5 cuffie con microfono
                Dim chiave As New PropertyKey(New Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0)
                Try
                    Dim v As Object = d.Properties(chiave).Value
                    If v IsNot Nothing Then
                        Dim forma As Integer = Convert.ToInt32(v)
                        If forma = 3 OrElse forma = 5 Then Return True
                    End If
                Catch exForma As Exception
                    RegistroErrori.Registra("ImpostazioniCitta.CuffieCollegate (tipo)", exForma)
                End Try
                Dim nome As String = If(d.FriendlyName, "").ToLowerInvariant()
                Return {"cuffi", "auricolar", "headphone", "headset", "earphone", "airpods", "buds"}.Any(Function(x) nome.Contains(x))
            End Using
        Catch ex As Exception
            RegistroErrori.Registra("ImpostazioniCitta.CuffieCollegate", ex)
            Return False
        End Try
    End Function
End Class
