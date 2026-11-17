' ============================================================================================
' WGModalita - modalità classica o smart di WinGabriel
' ============================================================================================
'
' Classica: WinGabriel di sempre, un solo menu da cui si apre tutto. Delle novità restano solo
'   l'editor con più testi e il browser.
' Smart: tutto il resto delle novità: scrivania, applicazioni separate (Control frecce, F10,
'   Control F4, tasto Windows per il menu applicazioni), centro notifiche, musica più bassa mentre
'   parla la sintesi e un solo file alla volta, barra di stato della scrivania.
'
' La modalità si legge una volta all'avvio e resta quella per tutta la sessione: cambiarla vale
' dal prossimo avvio, perché applicazioni e finestre aperte sono organizzate secondo la modalità.
'
'   If WGModalita.Smart Then ...
'   WGModalita.ScegliAlProssimoAvvio(smart:=False)
' ============================================================================================
Public NotInheritable Class WGModalita

    Public Const IdConfigurazione As String = "modalita_wingabriel"
    Private Const ValoreSmart As String = "smart"
    Private Const ValoreClassica As String = "classica"

    Private Shared ReadOnly log As New Logger()
    Private Shared _smart As Integer = -1

    Private Sub New()
    End Sub

    ''' <summary>Vero in modalità smart (predefinita). Resta uguale per tutta la sessione.</summary>
    Public Shared ReadOnly Property Smart As Boolean
        Get
            If _smart < 0 Then _smart = If(Salvata() = ValoreClassica, 0, 1)
            Return _smart = 1
        End Get
    End Property

    ''' <summary>La modalità che partirà al prossimo avvio (può essere diversa da quella in uso).</summary>
    Public Shared ReadOnly Property SmartAlProssimoAvvio As Boolean
        Get
            Return Salvata() <> ValoreClassica
        End Get
    End Property

    Public Shared ReadOnly Property Nome As String
        Get
            Return If(Smart, "smart", "classica")
        End Get
    End Property

    Private Shared Function Salvata() As String
        Try
            Dim config As New WGConfig()
            If Not config.EsisteId(IdConfigurazione) Then Return ValoreSmart
            Return If(config.LeggiValore(IdConfigurazione), "").Trim().ToLowerInvariant()
        Catch ex As Exception
            log.ScriviLog("WGModalita.Salvata: " & ex.Message)
            Return ValoreSmart
        End Try
    End Function

    Public Shared Sub ScegliAlProssimoAvvio(smart As Boolean)
        Try
            Dim config As New WGConfig()
            config.AggiungiValore(IdConfigurazione, If(smart, ValoreSmart, ValoreClassica))
        Catch ex As Exception
            log.ScriviLog("WGModalita.ScegliAlProssimoAvvio: " & ex.Message)
        End Try
    End Sub

    Public Const DescrizioneSmart As String = "Modalità smart: scrivania, applicazioni separate con Control frecce, F10 e Control F4, tasto Windows per il menu applicazioni, centro notifiche, barra di stato con l'ora, il volume, la batteria e la connessione, musica più bassa mentre parla la sintesi e un solo file alla volta."
    Public Const DescrizioneClassica As String = "Modalità classica: il WinGabriel di sempre, con un solo menu da cui si apre tutto. Restano l'editor con più testi e il browser."

End Class
