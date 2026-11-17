Imports System
Imports wgcontrols

''' <summary>
''' Le preferenze del Media player. Sono le stesse chiavi che usava Gestione file per l'ascolto dei
''' brani (modalità di apertura, ordine di preferiti e recenti): chi aveva già scelto qualcosa lo
''' ritrova senza dover rifare niente, e le scelte fatte qui valgono anche per la vecchia schermata.
''' </summary>
Friend Module MediaPlayerImpostazioni

    Friend Const CHIAVE_MODALITA_APERTURA As String = "modalitaAperturaMedia"
    Friend Const CHIAVE_ORDINE_PREFERITI As String = "ordinamento_brani_preferiti"
    Friend Const CHIAVE_ORDINE_RECENTI As String = "ordinamento_brani_recenti"

    Private ReadOnly _config As New WGConfig()

    Friend Function Leggi(chiave As String, Optional predefinito As String = "") As String
        Try
            If _config.EsisteId(chiave) Then Return If(_config.LeggiValore(chiave), predefinito)
        Catch ex As Exception
            MediaUtil.Log("MediaPlayerImpostazioni.Leggi: " & ex.ToString())
        End Try
        Return predefinito
    End Function

    Friend Sub Salva(chiave As String, valore As String)
        Try
            _config.SalvaValore(chiave, valore)
        Catch ex As Exception
            MediaUtil.Log("MediaPlayerImpostazioni.Salva: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>"" = chiedi ogni volta, "Sottofondo", "Player".</summary>
    Friend Function ModalitaApertura() As String
        Return Leggi(CHIAVE_MODALITA_APERTURA, "")
    End Function

End Module
