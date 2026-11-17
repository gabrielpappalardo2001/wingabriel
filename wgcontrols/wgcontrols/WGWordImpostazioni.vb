Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections.Generic
Imports System.Text
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports System.Linq

Partial Public Class WGWord

    Private Shared ReadOnly _defaultImpostazioni As New Dictionary(Of String, String) From {
        {"echo_digitazione", "3"},
        {"maiuscola_automatica", "1"},
        {"carica_immagini", "1"},
        {"lettura_punteggiatura_scrittura", "1"},
        {"lettura_punteggiatura_lettura", "1"},
        {"annuncia_formattazione", "1"},
        {"abilita_correttore", "1"},
        {"intervallo_salvataggio", "60000"},
        {"fruscio_pagina_attivo", "1"},
        {"leggi_intestazione_cambio_pagina", "0"},
        {"font_predefinito", "Arial"},
        {"dimensione_predefinita", "11"},
        {"dimensione_titolo_1", "18"},
        {"dimensione_titolo_2", "16"},
        {"dimensione_titolo_3", "14"},
        {"dimensione_titolo_4", "13"},
        {"dimensione_titolo_5", "12"},
        {"dimensione_titolo_6", "11"},
        {"passo_zoom", "0.1"},
        {"zoom_iniziale", "1.0"},
        {"passo_interlinea", "0.5"},
        {"formato_pagina_predefinito", "A4"},
        {"leggi_numero_nota_scorrimento", "1"}
    }

    Private Function ImpostazionePredefinita(chiave As String) As String
        Try
            If _defaultImpostazioni.ContainsKey(chiave) Then Return _defaultImpostazioni(chiave)
            Return ""
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ""
        End Try
    End Function

    Private Function LeggiImpostazioneStringa(chiave As String) As String
        Try
            If config Is Nothing Then Return ImpostazionePredefinita(chiave)
            Dim valore As String = config.LeggiValore(chiave)
            If String.IsNullOrEmpty(valore) Then Return ImpostazionePredefinita(chiave)
            Return valore
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return ImpostazionePredefinita(chiave)
        End Try
    End Function

    Private Function LeggiImpostazioneBooleana(chiave As String) As Boolean
        Try
            Return LeggiImpostazioneStringa(chiave) <> "0"
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return True
        End Try
    End Function

    Private Function LeggiImpostazioneIntera(chiave As String) As Integer
        Try
            Dim testo As String = LeggiImpostazioneStringa(chiave)
            Dim valore As Integer
            If Integer.TryParse(testo, valore) Then Return valore
            Dim testoDefault As String = ImpostazionePredefinita(chiave)
            If Integer.TryParse(testoDefault, valore) Then Return valore
            Return 0
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Private Function LeggiImpostazioneSingle(chiave As String) As Single
        Try
            Dim testo As String = LeggiImpostazioneStringa(chiave)
            Dim valore As Single
            If Single.TryParse(testo, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, valore) Then Return valore
            Dim testoDefault As String = ImpostazionePredefinita(chiave)
            If Single.TryParse(testoDefault, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, valore) Then Return valore
            Return 0
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return 0
        End Try
    End Function

    Public Sub ImpostaValore(chiave As String, valore As String)
        Try
            If config IsNot Nothing AndAlso Not String.IsNullOrEmpty(chiave) Then config.AggiungiValore(chiave, valore)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
        End Try
    End Sub

    Public ReadOnly Property EchoDigitazione As String
        Get
            Return LeggiImpostazioneStringa("echo_digitazione")
        End Get
    End Property

    Public ReadOnly Property MaiuscolaAutomaticaAttiva As Boolean
        Get
            Return LeggiImpostazioneBooleana("maiuscola_automatica")
        End Get
    End Property

    Public ReadOnly Property CaricaImmaginiAttivo As Boolean
        Get
            Return LeggiImpostazioneBooleana("carica_immagini")
        End Get
    End Property

    Public ReadOnly Property LetturaPunteggiaturaScrittura As Boolean
        Get
            Return LivelloPunteggiaturaScrittura > 0
        End Get
    End Property

    Public ReadOnly Property LivelloPunteggiaturaScrittura As Integer
        Get
            Try
                Dim valore As String = LeggiImpostazioneStringa("livello_punteggiatura_scrittura")
                Dim livelloParse As Integer
                If Integer.TryParse(valore, livelloParse) Then
                    If livelloParse < 0 Then livelloParse = 0
                    If livelloParse > 3 Then livelloParse = 3
                    Return livelloParse
                End If
                Return If(LeggiImpostazioneBooleana("lettura_punteggiatura_scrittura"), 3, 0)
            Catch ex As Exception
                logger.ScriviLog(ex.Message)
                Return 3
            End Try
        End Get
    End Property

    Public ReadOnly Property LetturaPunteggiaturaLettura As Boolean
        Get
            Return LeggiImpostazioneBooleana("lettura_punteggiatura_lettura")
        End Get
    End Property

    Public ReadOnly Property AnnunciaFormattazioneAttivo As Boolean
        Get
            Return LeggiImpostazioneBooleana("annuncia_formattazione")
        End Get
    End Property

    Public ReadOnly Property AbilitaCorrettoreAttivo As Boolean
        Get
            Return LeggiImpostazioneBooleana("abilita_correttore")
        End Get
    End Property

    Public ReadOnly Property IntervalloSalvataggioMs As Integer
        Get
            Dim valore As Integer = LeggiImpostazioneIntera("intervallo_salvataggio")
            If valore <= 0 Then Return 60000
            Return valore
        End Get
    End Property

    Public ReadOnly Property LeggiIntestazioneCambioPagina As Boolean
        Get
            Return LeggiImpostazioneBooleana("leggi_intestazione_cambio_pagina")
        End Get
    End Property

    Public ReadOnly Property FruscioPaginaAttivo As Boolean
        Get
            If Not SuoniGlobaliAttivi() Then Return False
            Return LeggiImpostazioneBooleana("fruscio_pagina_attivo")
        End Get
    End Property

    Private Function SuoniGlobaliAttivi() As Boolean
        Try
            If config Is Nothing Then Return True
            If config.EsisteId("SuoniAttivi") Then
                Dim valore As Boolean = True
                Boolean.TryParse(config.LeggiValore("SuoniAttivi"), valore)
                Return valore
            End If
            Return True
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return True
        End Try
    End Function

    Public ReadOnly Property FontPredefinito As String
        Get
            Dim valore As String = LeggiImpostazioneStringa("font_predefinito")
            If String.IsNullOrWhiteSpace(valore) Then Return "Arial"
            Return valore
        End Get
    End Property

    Public ReadOnly Property DimensionePredefinita As Single
        Get
            Dim valore As Single = LeggiImpostazioneSingle("dimensione_predefinita")
            If valore <= 0 Then Return 11.0F
            Return valore
        End Get
    End Property

    Public ReadOnly Property PassoZoom As Single
        Get
            Dim valore As Single = LeggiImpostazioneSingle("passo_zoom")
            If valore <= 0 Then Return 0.1F
            Return valore
        End Get
    End Property

    Public ReadOnly Property ZoomIniziale As Single
        Get
            Dim valore As Single = LeggiImpostazioneSingle("zoom_iniziale")
            If valore <= 0 Then Return 1.0F
            Return valore
        End Get
    End Property

    Public ReadOnly Property PassoInterlinea As Single
        Get
            Dim valore As Single = LeggiImpostazioneSingle("passo_interlinea")
            If valore <= 0 Then Return 0.5F
            Return valore
        End Get
    End Property

    Public ReadOnly Property FormatoPaginaPredefinito As String
        Get
            Dim valore As String = LeggiImpostazioneStringa("formato_pagina_predefinito")
            If String.IsNullOrWhiteSpace(valore) Then Return "A4"
            Return valore
        End Get
    End Property

    Public ReadOnly Property LeggiNumeroNotaInScorrimento As Boolean
        Get
            Return LeggiImpostazioneBooleana("leggi_numero_nota_scorrimento")
        End Get
    End Property

    Public Function DimensioneTitolo(livello As Integer) As Single
        Try
            If livello < 1 OrElse livello > 6 Then Return DimensionePredefinita
            Dim valore As Single = LeggiImpostazioneSingle("dimensione_titolo_" & livello)
            If valore <= 0 Then
                Dim fallback() As Single = {18.0F, 16.0F, 14.0F, 13.0F, 12.0F, 11.0F}
                Return fallback(livello - 1)
            End If
            Return valore
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return DimensionePredefinita
        End Try
    End Function

    Public Function OttieniDimensioniPaginaPredefinite() As Tuple(Of Integer, Integer)
        Try
            If FormatoPaginaPredefinito.Equals("Letter", StringComparison.OrdinalIgnoreCase) Then
                Return Tuple.Create(12240, 15840)
            End If
            Return Tuple.Create(11906, 16838)
        Catch ex As Exception
            logger.ScriviLog(ex.Message)
            Return Tuple.Create(11906, 16838)
        End Try
    End Function

End Class