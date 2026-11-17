Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq
Imports wgcontrols

' Navigazione rapida tra elementi specifici della pagina come intestazioni, link, pulsanti e moduli.
Public Class WGNavigatoreDOM
    Private ReadOnly log As New Logger()
    Private ReadOnly paginaWeb As WGPaginaWeb
    Private ReadOnly cursore As WGCursorVirtuale
    Private ReadOnly cdpInterazione As WGCdpInterazione
    Private ReadOnly lettura As sintesivocale = sintesivocale.GetInstance()

    Public Sub New(pagina As WGPaginaWeb, cursorVirtuale As WGCursorVirtuale, interazione As WGCdpInterazione)
        paginaWeb = pagina
        cursore = cursorVirtuale
        cdpInterazione = interazione
    End Sub

    Private Shared Function EIntestazione(r As RigaVirtuale, livello As Integer) As Boolean
        If r.Ruolo <> "heading" Then Return False
        Return livello <= 0 OrElse r.Livello = livello
    End Function

    Public Shared Function ELink(r As RigaVirtuale) As Boolean
        Return r.Ruolo = "link" OrElse (r.IdAttivazione > 0 AndAlso r.RuoloAttivazione = "link")
    End Function

    Public Shared Function EPulsante(r As RigaVirtuale) As Boolean
        If r.Ruolo = "button" OrElse r.Ruolo = "cliccabile" Then Return True
        Return r.IdAttivazione > 0 AndAlso (r.RuoloAttivazione = "button" OrElse r.RuoloAttivazione = "cliccabile")
    End Function

    Public Shared Function ECliccabile(r As RigaVirtuale) As Boolean
        Return r.Ruolo = "cliccabile" OrElse (r.IdAttivazione > 0 AndAlso r.RuoloAttivazione = "cliccabile")
    End Function

    Public Shared Function ECampoModulo(r As RigaVirtuale) As Boolean
        If r.IdAttivazione > 0 Then Return False
        If r.Modificabile Then Return True
        Select Case r.Ruolo
            Case "textbox", "searchbox", "combobox", "listbox", "checkbox", "radio", "switch", "slider", "spinbutton"
                Return True
        End Select
        Return False
    End Function

    Public Shared Function EMultimediale(r As RigaVirtuale) As Boolean
        Return r.Ruolo = "video" OrElse r.Ruolo = "audio" OrElse r.Ruolo = "canvas"
    End Function

    Private Async Function SaltaAsync(condizione As Func(Of RigaVirtuale, Boolean), indietro As Boolean, nessuno As String, Optional distinguiAttivazione As Boolean = False, Optional contesto As ContestoLettura = ContestoLettura.Contenitori, Optional ciclico As Boolean = False) As Task
        Try
            Await cursore.AssicuraBufferAsync()
            Dim indice As Integer = cursore.TrovaRiga(condizione, indietro, distinguiAttivazione, contesto <> ContestoLettura.SoloIngresso)
            If indice < 0 AndAlso ciclico Then
                Dim daCapo As Integer = cursore.TrovaRiga(condizione, indietro, distinguiAttivazione, contesto <> ContestoLettura.SoloIngresso, True)
                If daCapo >= 0 Then
                    If daCapo <> cursore.IndiceCorrente Then lettura.LeggiTesto(If(indietro, "Ricomincio dalla fine", "Ricomincio dall'inizio"))
                    cursore.SpostaAConContesto(daCapo, contesto)
                    Return
                End If
            End If
            If indice < 0 Then
                lettura.LeggiTesto(nessuno)
                Return
            End If
            cursore.SpostaAConContesto(indice, contesto)
        Catch ex As Exception
            log.ScriviLog("WGNavigatoreDOM.SaltaAsync: " & ex.Message)
        End Try
    End Function

    Private Shared Function Direzione(indietro As Boolean, femminile As Boolean) As String
        If indietro Then Return " precedente"
        Return If(femminile, " successiva", " successivo")
    End Function

    Public Function SaltaAIntestazioneSuccessiva(livello As String, indietro As Boolean) As Task
        Try
            Dim numero As Integer = 0
            If Not String.Equals(livello, "qualsiasi", StringComparison.OrdinalIgnoreCase) Then
                Integer.TryParse(livello.ToLowerInvariant().Replace("h", ""), numero)
            End If
            Dim messaggio As String = If(numero > 0, "Nessuna intestazione di livello " & numero & Direzione(indietro, True), "Nessuna intestazione" & Direzione(indietro, True))
            Return SaltaAsync(Function(r) EIntestazione(r, numero), indietro, messaggio, False, ContestoLettura.Nessuno, True)
        Catch ex As Exception
            log.ScriviLog("WGNavigatoreDOM.SaltaAIntestazioneSuccessiva: " & ex.Message)
            Return Task.CompletedTask
        End Try
    End Function

    Public Function SaltaALinkSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(AddressOf ELink, indietro, "Nessun link" & Direzione(indietro, False), True)
    End Function

    Public Function SaltaAPulsanteSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(AddressOf EPulsante, indietro, "Nessun pulsante" & Direzione(indietro, False), True)
    End Function

    Public Function SaltaACliccabileSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(AddressOf ECliccabile, indietro, "Nessun elemento cliccabile" & Direzione(indietro, False), True)
    End Function

    Public Function SaltaACampoSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(AddressOf ECampoModulo, indietro, "Nessun campo" & Direzione(indietro, False))
    End Function

    Public Function SaltaACampoModificaSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Modificabile AndAlso r.IdAttivazione = 0, indietro, "Nessun campo di modifica" & Direzione(indietro, False))
    End Function

    Public Function SaltaACasellaCombinataSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "combobox" OrElse r.Ruolo = "listbox", indietro, "Nessuna casella combinata" & Direzione(indietro, True))
    End Function

    Public Function SaltaACasellaControlloSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "checkbox" OrElse r.Ruolo = "switch", indietro, "Nessuna casella di controllo" & Direzione(indietro, True))
    End Function

    Public Function SaltaAPulsanteOpzioneSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "radio", indietro, "Nessun pulsante di opzione" & Direzione(indietro, False))
    End Function

    Public Function SaltaAImmagineSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "img", indietro, "Nessuna immagine" & Direzione(indietro, True))
    End Function

    Public Function SaltaAContenutoMultimedialeSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(AddressOf EMultimediale, indietro, "Nessun video, audio o area disegnata" & Direzione(indietro, False))
    End Function

    Public Function SaltaARegioneLandmarkSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) Not String.IsNullOrEmpty(r.Regione), indietro, "Nessuna regione" & Direzione(indietro, True), False, ContestoLettura.SoloIngresso)
    End Function

    Public Function SaltaAListaSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.ElementiLista > 0, indietro, "Nessun elenco" & Direzione(indietro, False))
    End Function

    Public Function SaltaAElementoListaSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.InizioElementoLista, indietro, "Nessun elemento di elenco" & Direzione(indietro, False))
    End Function

    Public Function SaltaATabellaSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) Not String.IsNullOrEmpty(r.Tabella), indietro, "Nessuna tabella" & Direzione(indietro, True))
    End Function

    Public Function SaltaAVoceDiMenuSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "menuitem" OrElse r.Ruolo = "menuitemcheckbox" OrElse r.Ruolo = "menuitemradio", indietro, "Nessuna voce di menu" & Direzione(indietro, True))
    End Function

    Public Function SaltaASchedaSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "tab", indietro, "Nessuna scheda" & Direzione(indietro, True))
    End Function

    Public Function SaltaAdArticoloSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.InizioArticolo, indietro, "Nessun articolo" & Direzione(indietro, False), False, ContestoLettura.SoloIngresso)
    End Function

    Public Function SaltaACitazioneSuccessiva(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.InizioCitazione, indietro, "Nessuna citazione" & Direzione(indietro, True))
    End Function

    Public Function SaltaAParagrafoSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.InizioParagrafo, indietro, "Nessun paragrafo" & Direzione(indietro, False))
    End Function

    Public Function SaltaAFrameSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "frame" OrElse (r.Regione IsNot Nothing AndAlso r.Regione.StartsWith("frame", StringComparison.Ordinal)), indietro, "Nessun frame" & Direzione(indietro, False), False, ContestoLettura.SoloIngresso)
    End Function

    Public Function SaltaATestoNonLinkSuccessivo(indietro As Boolean) As Task
        Return SaltaAsync(Function(r) r.Ruolo = "text" AndAlso r.IdAttivazione = 0, indietro, "Nessun testo" & Direzione(indietro, False))
    End Function

    Public Function LeggiStatoCampoAsync() As Task(Of JObject)
        Try
            Return cdpInterazione.StatoCampoAsync()
        Catch ex As Exception
            log.ScriviLog("WGNavigatoreDOM.LeggiStatoCampoAsync: " & ex.Message)
            Return Task.FromResult(Of JObject)(Nothing)
        End Try
    End Function

    Public Shared Function RigaAlCursore(valore As String, posizione As Integer) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            Dim p As Integer = Math.Max(0, Math.Min(posizione, valore.Length))
            Dim inizio As Integer = p
            While inizio > 0 AndAlso valore(inizio - 1) <> vbLf
                inizio -= 1
            End While
            Dim fine As Integer = p
            While fine < valore.Length AndAlso valore(fine) <> vbLf
                fine += 1
            End While
            Return valore.Substring(inizio, fine - inizio).Replace(vbCr, "")
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGNavigatoreDOM.RigaAlCursore: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function ParolaAlCursore(valore As String, posizione As Integer) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            Dim p As Integer = Math.Max(0, Math.Min(posizione, valore.Length))
            Dim inizio As Integer = p
            While inizio > 0 AndAlso Not Char.IsWhiteSpace(valore(inizio - 1))
                inizio -= 1
            End While
            Dim fine As Integer = p
            While fine < valore.Length AndAlso Not Char.IsWhiteSpace(valore(fine))
                fine += 1
            End While
            Return valore.Substring(inizio, fine - inizio)
        Catch ex As Exception
            Dim registro As New Logger()
            registro.ScriviLog("WGNavigatoreDOM.ParolaAlCursore: " & ex.Message)
            Return ""
        End Try
    End Function
End Class