Imports System.Collections.Generic
Imports System.Globalization

' Gestione delle preferenze e dei parametri di configurazione specifici per il movimento nei campi.
Partial Public Class WGCampoMovimento

    Private ReadOnly config As New WGConfig()

    Private Shared ReadOnly _defaultImpostazioniCampo As New Dictionary(Of String, String) From {
        {"lettura_punteggiatura_scrittura_campo", "1"},
        {"lettura_punteggiatura_lettura_campo", "1"},
        {"maiuscola_automatica_campo", "1"},
        {"font_predefinito_campo", "Arial"},
        {"dimensione_predefinita_campo", "18"},
        {"passo_dimensione_font_campo_min", "8"},
        {"passo_dimensione_font_campo_max", "72"},
        {"passo_dimensione_font_campo_step", "2"},
        {"intervallo_pausa_lettura_continua", "2000"},
        {"intervallo_debounce_correttore_campo", "500"},
        {"intervallo_maiuscola_automatica_campo", "2000"}
    }

    Private Function ImpostazionePredefinitaCampo(chiave As String) As String
        Try
            If _defaultImpostazioniCampo.ContainsKey(chiave) Then Return _defaultImpostazioniCampo(chiave)
            Return ""
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Private Function LeggiImpostazioneCampoStringa(chiave As String) As String
        Try
            If config Is Nothing Then Return ImpostazionePredefinitaCampo(chiave)
            Dim valore As String = config.LeggiValore(chiave)
            If String.IsNullOrEmpty(valore) Then Return ImpostazionePredefinitaCampo(chiave)
            Return valore
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return ImpostazionePredefinitaCampo(chiave)
        End Try
    End Function

    Private Function LeggiImpostazioneCampoBooleana(chiave As String) As Boolean
        Try
            Return LeggiImpostazioneCampoStringa(chiave) <> "0"
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return True
        End Try
    End Function

    Private Function LeggiImpostazioneCampoIntera(chiave As String) As Integer
        Try
            Dim testo As String = LeggiImpostazioneCampoStringa(chiave)
            Dim valore As Integer
            If Integer.TryParse(testo, valore) Then Return valore
            Dim testoDefault As String = ImpostazionePredefinitaCampo(chiave)
            If Integer.TryParse(testoDefault, valore) Then Return valore
            Return 0
        Catch ex As Exception
            log.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Public Sub ImpostaValoreCampo(chiave As String, valore As String)
        Try
            If config IsNot Nothing AndAlso Not String.IsNullOrEmpty(chiave) Then config.AggiungiValore(chiave, valore)
        Catch ex As Exception
            log.ScriviLog(ex.Message)
        End Try
    End Sub

    Public ReadOnly Property EchoDigitazioneCampo As String
        Get
            Return impostazioniComuni.EchoDigitazione
        End Get
    End Property

    
    Public ReadOnly Property MaiuscolaAutomaticaAttivaCampo As Boolean
        Get
            Return LeggiImpostazioneCampoBooleana("maiuscola_automatica_campo")
        End Get
    End Property

    Public ReadOnly Property FontPredefinitoCampo As String
        Get
            Dim valore As String = LeggiImpostazioneCampoStringa("font_predefinito_campo")
            If String.IsNullOrWhiteSpace(valore) Then Return "Arial"
            Return valore
        End Get
    End Property

    Public ReadOnly Property DimensionePredefinitaCampo As Single
        Get
            Dim valore As Integer = LeggiImpostazioneCampoIntera("dimensione_predefinita_campo")
            If valore <= 0 Then Return 18.0F
            Return CSng(valore)
        End Get
    End Property

    Public ReadOnly Property DimensioneMinimaMenuFontCampo As Integer
        Get
            Dim valore As Integer = LeggiImpostazioneCampoIntera("passo_dimensione_font_campo_min")
            If valore <= 0 Then Return 8
            Return valore
        End Get
    End Property

    Public ReadOnly Property DimensioneMassimaMenuFontCampo As Integer
        Get
            Dim valore As Integer = LeggiImpostazioneCampoIntera("passo_dimensione_font_campo_max")
            If valore <= 0 Then Return 72
            Return valore
        End Get
    End Property

    Public ReadOnly Property PassoMenuFontCampo As Integer
        Get
            Dim valore As Integer = LeggiImpostazioneCampoIntera("passo_dimensione_font_campo_step")
            If valore <= 0 Then Return 2
            Return valore
        End Get
    End Property

    Public ReadOnly Property IntervalloPausaLetturaContinuaCampo As Integer
        Get
            Dim valore As Integer = LeggiImpostazioneCampoIntera("intervallo_pausa_lettura_continua")
            If valore <= 0 Then Return 2000
            Return valore
        End Get
    End Property

    Public ReadOnly Property IntervalloDebounceCorrettoreCampo As Integer
        Get
            Dim valore As Integer = LeggiImpostazioneCampoIntera("intervallo_debounce_correttore_campo")
            If valore <= 0 Then Return 500
            Return valore
        End Get
    End Property

    Public ReadOnly Property IntervalloMaiuscolaAutomaticaCampo As Integer
        Get
            Dim valore As Integer = LeggiImpostazioneCampoIntera("intervallo_maiuscola_automatica_campo")
            If valore <= 0 Then Return 2000
            Return valore
        End Get
    End Property

End Class
