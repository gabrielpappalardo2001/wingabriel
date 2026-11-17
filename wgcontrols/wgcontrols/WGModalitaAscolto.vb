Imports System
Imports System.Windows.Forms

' ============================================================================================
' WGModalitaAscolto - come si avvia l'ascolto di un brano, di una radio, di un canale o di un video
' ============================================================================================
'
' La scelta è quella delle impostazioni, "Apertura dei file multimediali" (modalitaAperturaMedia):
'   "Sottofondo"  ascolta sempre in sottofondo, con il centro di controllo;
'   "Player"      apre sempre il lettore multimediale a schermo;
'   vuoto         chiede ogni volta, con un menu.
' Chi avvia l'ascolto dà le due strade, e questa classe sceglie quale prendere:
'
'   WGModalitaAscolto.Avvia("Rai Radio 1", Sub() ...sottofondo..., Sub() ...lettore...)
' ============================================================================================
Public NotInheritable Class WGModalitaAscolto

    Public Const ChiaveConfigurazione As String = "modalitaAperturaMedia"
    Public Const Sottofondo As String = "Sottofondo"
    Public Const Lettore As String = "Player"

    Private Shared ReadOnly log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>La scelta fatta nelle impostazioni: Sottofondo, Player o vuoto (chiedi ogni volta).</summary>
    Public Shared Function Corrente() As String
        Try
            Dim config As New WGConfig()
            If Not config.EsisteId(ChiaveConfigurazione) Then Return ""
            Dim valore As String = If(config.LeggiValore(ChiaveConfigurazione), "").Replace(vbCr, "").Replace(vbLf, "").Trim()
            If String.Equals(valore, Sottofondo, StringComparison.OrdinalIgnoreCase) Then Return Sottofondo
            If String.Equals(valore, Lettore, StringComparison.OrdinalIgnoreCase) Then Return Lettore
            Return ""
        Catch ex As Exception
            log.ScriviLog("WGModalitaAscolto.Corrente: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>
    ''' Avvia l'ascolto nel modo scelto nelle impostazioni; se è "chiedi ogni volta" mostra il menu con le due possibilità.
    ''' Da chiamare dal thread dell'interfaccia.
    ''' </summary>
    Public Shared Sub Avvia(titolo As String, ascoltaInSottofondo As Action, apriLettore As Action)
        Try
            Select Case Corrente()
                Case Sottofondo
                    If ascoltaInSottofondo IsNot Nothing Then ascoltaInSottofondo()
                Case Lettore
                    If apriLettore IsNot Nothing Then apriLettore()
                Case Else
                    Dim menu As New WGMenu()
                    menu.aggiungi("Ascolto in background", Sub()
                                                               If ascoltaInSottofondo IsNot Nothing Then ascoltaInSottofondo()
                                                               ChiudiMenu(menu)
                                                           End Sub)
                    menu.aggiungi("Apri lettore multimediale", Sub()
                                                                   If apriLettore IsNot Nothing Then apriLettore()
                                                                   ChiudiMenu(menu)
                                                               End Sub)
                    menu.avvia(If(String.IsNullOrWhiteSpace(titolo), "Modalità di ascolto", "Modalità di ascolto, " & titolo), True)
            End Select
        Catch ex As Exception
            log.ScriviLog("WGModalitaAscolto.Avvia: " & ex.ToString())
        End Try
    End Sub

    Private Shared Sub ChiudiMenu(menu As WGMenu)
        Try
            menu.abbandona()
        Catch ex As Exception
            log.ScriviLog("WGModalitaAscolto.ChiudiMenu: " & ex.Message)
        End Try
    End Sub

End Class
