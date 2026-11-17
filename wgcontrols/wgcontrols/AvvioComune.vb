Imports System.Xml
Imports System.Runtime.CompilerServices
Imports System
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Diagnostics
Imports System.Windows.Forms
Imports System.Linq

Public Module AvvioComune

    Public Const ID_CONFIG_DISATTIVA_SUONI_VOLUME_AVVIO As String = "disattiva_suoni_volume_avvio"

    Public Const ID_CONFIG_VOLUME_PREFERITO_AVVIO As String = "volume_preferito_avvio"

    Public logAvvio As wgcontrols.Logger

    Public wgControlsAgganciato As Boolean = False

    Public Sub ScriviLogBootstrap(messaggio As String)
        Try
            Dim percorsoLog As String = Path.Combine(WGDb.WGPercorsiApplicazione.CartellaLog(), "logwingabriel.txt")
            Dim riga As String = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") & " - " & messaggio & Environment.NewLine
            File.AppendAllText(percorsoLog, riga, Encoding.UTF8)
        Catch
        End Try
    End Sub

    Public Sub ScriviLogAvvio(messaggio As String)
        Try
            If wgControlsAgganciato Then
                ScriviLogConLogger(messaggio)
            Else
                ScriviLogBootstrap(messaggio)
            End If
        Catch
            ScriviLogBootstrap(messaggio)
        End Try
    End Sub

    <MethodImpl(MethodImplOptions.NoInlining)>
    Public Sub ScriviLogConLogger(messaggio As String)
        Try
            If logAvvio Is Nothing Then
                ScriviLogBootstrap(messaggio)
                Return
            End If

        Catch
            ScriviLogBootstrap(messaggio)
        End Try
    End Sub

    Public Function SuoniVolumeAvvioDisattivati() As Boolean
        Try
            Dim percorsoConfig As String = AvvioAmministratore.PercorsoConfigurazione()
            If Not File.Exists(percorsoConfig) Then Return False

            Dim documentoConfig As New XmlDocument()
            documentoConfig.Load(percorsoConfig)

            Dim nodo As XmlNode = documentoConfig.SelectSingleNode("/Configurazioni/Configurazione[@id='" & ID_CONFIG_DISATTIVA_SUONI_VOLUME_AVVIO & "']")
            If nodo Is Nothing Then Return False

            Return String.Equals(nodo.InnerText.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        Catch ex As Exception
            ScriviLogAvvio("SuoniVolumeAvvioDisattivati eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return False
        End Try
    End Function

    Public Function VolumePreferitoAvvio() As Integer
        Try
            Dim percorsoConfig As String = AvvioAmministratore.PercorsoConfigurazione()
            If Not File.Exists(percorsoConfig) Then Return -1

            Dim documentoConfig As New XmlDocument()
            documentoConfig.Load(percorsoConfig)

            Dim nodo As XmlNode = documentoConfig.SelectSingleNode("/Configurazioni/Configurazione[@id='" & ID_CONFIG_VOLUME_PREFERITO_AVVIO & "']")
            If nodo Is Nothing Then Return -1

            Dim percentuale As Integer
            If Integer.TryParse(nodo.InnerText.Trim(), percentuale) AndAlso percentuale >= 0 AndAlso percentuale <= 100 Then
                Return percentuale
            End If
            Return -1
        Catch ex As Exception
            ScriviLogAvvio("VolumePreferitoAvvio eccezione: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return -1
        End Try
    End Function

    Public Function LeggiValoreConfigurazione(id As String) As String
        Try
            Dim cfg As New WGConfig()
            If Not cfg.EsisteId(id) Then Return ""
            Return If(cfg.LeggiValore(id), "")
        Catch ex As Exception
            ScriviLogAvvio("LeggiValoreConfigurazione (" & id & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
            Return ""
        End Try
    End Function

    Public Sub ScriviValoreConfigurazione(id As String, valore As String)
        Try
            Dim cfg As New WGConfig()
            If cfg.EsisteId(id) Then
                cfg.ModificaValore(id, valore)
            Else
                cfg.AggiungiValore(id, valore)
            End If
        Catch ex As Exception
            ScriviLogAvvio("ScriviValoreConfigurazione (" & id & "): " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

End Module
