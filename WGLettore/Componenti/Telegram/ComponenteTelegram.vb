Imports System.Collections.Generic
Imports WGLettoreSchermo

''' <summary>
''' Componente aggiuntivo per Telegram (l'app del Microsoft Store, già Unigram, e anche Telegram Desktop): voci della lista più pulite,
''' campo di scrittura riconosciuto, avvisi senza doppioni e tre comandi con il tasto dei comandi del lettore:
''' Control U va all'ultimo messaggio, Control E alla casella di scrittura, Control I rilegge per intero la voce con il fuoco.
''' </summary>
Public Class ComponenteTelegram
    Inherits ModuloEsternoLettore

    Private Const TastoU As Integer = &H55
    Private Const TastoE As Integer = &H45
    Private Const TastoI As Integer = &H49

    ' Parole che i controlli generici fanno dire e che in una chat non servono a niente.
    Private Shared ReadOnly _generiche As New HashSet(Of String)(StringComparer.CurrentCultureIgnoreCase) From {
        "riquadro", "personalizzato", "elemento", "pannello", "contenitore", "voce di elenco"}

    Private _servizi As ServiziComponente
    Private _ultimoAvviso As String = ""
    Private _ultimoAvvisoTick As Integer

    Public Overrides ReadOnly Property Programmi As String()
        Get
            Return New String() {"telegram", "unigram"}
        End Get
    End Property

    Public Overrides ReadOnly Property Nome As String
        Get
            Return "Telegram"
        End Get
    End Property

    Public Overrides Sub Avvia(servizi As ServiziComponente)
        _servizi = servizi
    End Sub

    Public Overrides Function AlFuocoElemento(elemento As ElementoUi, frase As String) As String
        Try
            If elemento Is Nothing Then Return frase
            Select Case elemento.Tipo
                Case "ListItem", "DataItem"
                    If EMessaggio(elemento) Then Return FraseDelMessaggio(elemento, frase)
                    Return FraseDellaVoce(elemento, frase)
                Case "Edit", "Document"
                    If elemento.Modificabile AndAlso (frase Is Nothing OrElse frase.IndexOf("modific", StringComparison.CurrentCultureIgnoreCase) < 0) Then
                        Return If(String.IsNullOrWhiteSpace(frase), "campo di modifica", frase & ", campo di modifica")
                    End If
            End Select
        Catch ex As Exception
            System.Diagnostics.Trace.WriteLine("ComponenteTelegram.AlFuocoElemento: " & ex.Message)
        End Try
        Return frase
    End Function

    ' Un messaggio sta nell'elenco con id "Messages".
    Private Shared Function EMessaggio(elemento As ElementoUi) As Boolean
        Try
            Dim padre As ElementoUi = elemento.Genitore()
            Return padre IsNot Nothing AndAlso String.Equals(padre.IdAutomazione, "Messages", StringComparison.Ordinal)
        Catch ex As Exception
            System.Diagnostics.Trace.WriteLine("ComponenteTelegram.EMessaggio: " & ex.Message)
            Return False
        End Try
    End Function

    ' Il nome che Telegram dà a un messaggio è un'unica stringa con a capo e punti sparsi, dove il titolo dell'autore si ripete e le
    ' emoji stanno dentro i nomi. Qui si ricompone dai pezzi (mittente, titolo, risposta, testo, ora), nell'ordine in cui si dicono.
    Private Shared Function FraseDelMessaggio(elemento As ElementoUi, frase As String) As String
        Try
            Dim mittente As String = ""
            Dim risposta As String = ""
            Dim testo As String = ""
            Dim ora As String = ""
            Dim titoli As New List(Of String)()
            For Each figlio As ElementoUi In elemento.Figli(80)
                Dim nome As String = Compatta(figlio.Nome)
                If nome = "" Then Continue For
                Select Case figlio.IdAutomazione
                    Case "HeaderLabel" : mittente = nome
                    Case "Reply" : risposta = nome
                    Case "TextBlock" : testo = nome
                    Case "Label"
                        If System.Text.RegularExpressions.Regex.IsMatch(nome, "^\d{1,2}:\d{2}$") Then
                            ora = nome
                        ElseIf Not Contiene(titoli, nome) Then
                            titoli.Add(nome)
                        End If
                End Select
            Next
            If mittente = "" AndAlso risposta = "" AndAlso testo = "" Then Return FraseDellaVoce(elemento, frase)
            Dim parti As New List(Of String)()
            If mittente <> "" Then parti.Add(mittente)
            For Each titolo As String In titoli
                If Not Contiene(parti, titolo) Then parti.Add(titolo)
            Next
            If risposta <> "" Then parti.Add("in risposta a " & risposta)
            If testo <> "" Then parti.Add(testo)
            If ora <> "" Then parti.Add("alle " & ora)
            For Each reazione As System.Text.RegularExpressions.Match In System.Text.RegularExpressions.Regex.Matches(Compatta(elemento.Nome), "([^.,]+?) ha reagito con (\S+)")
                Dim testoReazione As String = Compatta(reazione.Value)
                If testoReazione <> "" AndAlso Not Contiene(parti, testoReazione) Then parti.Add(testoReazione)
            Next
            Return String.Join(", ", parti)
        Catch ex As Exception
            System.Diagnostics.Trace.WriteLine("ComponenteTelegram.FraseDelMessaggio: " & ex.Message)
            Return frase
        End Try
    End Function

    ' A capo e spazi multipli diventano uno spazio solo; punti e virgole attaccati a una virgola si tolgono.
    Private Shared Function Compatta(testo As String) As String
        If String.IsNullOrWhiteSpace(testo) Then Return ""
        Dim pulito As String = System.Text.RegularExpressions.Regex.Replace(testo, "\s+", " ").Trim()
        pulito = System.Text.RegularExpressions.Regex.Replace(pulito, "\s+,", ",")
        pulito = System.Text.RegularExpressions.Regex.Replace(pulito, "\.\s*,", ",")
        Return pulito.Trim(","c, " "c)
    End Function

    ' Una voce della lista (chat o messaggio): senza ripetizioni e senza le parole dei controlli generici. Se il lettore non ha trovato
    ' altro che il nome, la frase si compone con i testi che la voce contiene: chi scrive, il messaggio, l'ora.
    Private Shared Function FraseDellaVoce(elemento As ElementoUi, frase As String) As String
        Dim risultato As String = FraseSenzaGruppo(elemento, frase)
        If EGruppo(elemento, frase) AndAlso risultato.IndexOf("gruppo", StringComparison.CurrentCultureIgnoreCase) < 0 Then risultato = "gruppo, " & risultato
        Return risultato
    End Function

    ' Un gruppo si riconosce dalle parole che Telegram mette nella sua voce (membri, partecipanti, iscritti) o dal nome del controllo.
    Private Shared Function EGruppo(elemento As ElementoUi, frase As String) As Boolean
        Try
            Dim testo As String = If(frase, "") & " " & String.Join(" ", elemento.TestiDiscendenti(14))
            For Each parola As String In New String() {"gruppo", "membri", "partecipanti", "iscritti", "supergruppo", "group", "members"}
                If testo.IndexOf(parola, StringComparison.CurrentCultureIgnoreCase) >= 0 Then Return True
            Next
        Catch ex As Exception
            System.Diagnostics.Trace.WriteLine("ComponenteTelegram.EGruppo: " & ex.Message)
        End Try
        Return False
    End Function

    Private Shared Function FraseSenzaGruppo(elemento As ElementoUi, frase As String) As String
        Dim parti As List(Of String) = Pulisci(frase)
        If parti.Count <= 1 Then
            Dim testi As List(Of String) = elemento.TestiDiscendenti(14)
            If testi.Count > 0 Then
                Dim composta As New List(Of String)(testi)
                For Each parte As String In parti
                    If Not Contiene(composta, parte) Then composta.Add(parte)
                Next
                Return String.Join(", ", composta)
            End If
        End If
        Return String.Join(", ", parti)
    End Function

    Private Shared Function Pulisci(frase As String) As List(Of String)
        Dim risultato As New List(Of String)()
        If String.IsNullOrWhiteSpace(frase) Then Return risultato
        frase = Compatta(frase)
        Dim tutte As New List(Of String)()
        For Each parte As String In frase.Split(New String() {", "}, StringSplitOptions.RemoveEmptyEntries)
            Dim pulita As String = parte.Trim()
            If pulita <> "" AndAlso Not Contiene(tutte, pulita) Then tutte.Add(pulita)
        Next
        For Each parte As String In tutte
            If tutte.Count > 1 AndAlso _generiche.Contains(parte) Then Continue For
            risultato.Add(parte)
        Next
        If risultato.Count = 0 AndAlso tutte.Count > 0 Then risultato.Add(tutte(0))
        Return risultato
    End Function

    Private Shared Function Contiene(elenco As List(Of String), testo As String) As Boolean
        For Each voce As String In elenco
            If String.Equals(voce, testo, StringComparison.CurrentCultureIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ' Lo stesso avviso ripetuto nel giro di pochi secondi (Telegram lo manda più volte) si dice una volta sola.
    Public Overrides Function AlEvento(evento As EventoComponente) As String
        Try
            If evento Is Nothing OrElse String.IsNullOrWhiteSpace(evento.Testo) Then Return Nothing
            Dim adesso As Integer = Environment.TickCount
            If String.Equals(evento.Testo, _ultimoAvviso, StringComparison.CurrentCulture) AndAlso adesso - _ultimoAvvisoTick >= 0 AndAlso adesso - _ultimoAvvisoTick < 4000 Then Return ""
            _ultimoAvviso = evento.Testo
            _ultimoAvvisoTick = adesso
        Catch ex As Exception
            System.Diagnostics.Trace.WriteLine("ComponenteTelegram.AlEvento: " & ex.Message)
        End Try
        Return Nothing
    End Function

    Public Overrides Function Comandi() As ComandoComponente()
        Return New ComandoComponente() {
            Nuovo("ultimoMessaggio", "va all'ultimo messaggio della conversazione", TastoU),
            Nuovo("scrivi", "porta il fuoco sulla casella di scrittura", TastoE),
            Nuovo("leggiVoce", "rilegge per intero la voce con il fuoco", TastoI)}
    End Function

    Private Shared Function Nuovo(id As String, descrizione As String, tasto As Integer) As ComandoComponente
        Dim comando As New ComandoComponente()
        comando.Id = id
        comando.Descrizione = descrizione
        comando.Tasto = tasto
        comando.Ctrl = True
        Return comando
    End Function

    Public Overrides Sub EseguiComando(id As String)
        Try
            Select Case id
                Case "ultimoMessaggio" : VaiAllUltimoMessaggio()
                Case "scrivi" : VaiAllaCasellaDiScrittura()
                Case "leggiVoce" : LeggiLaVoce()
            End Select
        Catch ex As Exception
            System.Diagnostics.Trace.WriteLine("ComponenteTelegram.EseguiComando: " & ex.Message)
        End Try
    End Sub

    Private Sub VaiAllUltimoMessaggio()
        Dim lista As ElementoUi = TrovaElenco(_servizi.ElementoConFuoco())
        If lista Is Nothing Then
            _servizi.AnnunciaSubito("Non trovo l'elenco dei messaggi")
            Return
        End If
        Dim figli As List(Of ElementoUi) = lista.Figli(5000)
        For indice As Integer = figli.Count - 1 To 0 Step -1
            If figli(indice).Tipo <> "ListItem" Then Continue For
            If Not figli(indice).DaiFuoco() Then
                Dim testi As List(Of String) = figli(indice).TestiDiscendenti(14)
                _servizi.AnnunciaSubito(If(testi.Count > 0, String.Join(", ", testi), figli(indice).Nome))
            End If
            Return
        Next
        _servizi.AnnunciaSubito("L'elenco è vuoto")
    End Sub

    Private Sub VaiAllaCasellaDiScrittura()
        Dim radice As ElementoUi = _servizi.ElementoConFuoco()
        Dim finestra As ElementoUi = Nothing
        Dim salti As Integer = 0
        While radice IsNot Nothing AndAlso salti < 30
            finestra = radice
            If radice.Tipo = "Window" Then Exit While
            radice = radice.Genitore()
            salti += 1
        End While
        If finestra Is Nothing Then
            _servizi.AnnunciaSubito("Non trovo la casella di scrittura")
            Return
        End If
        For Each candidato As ElementoUi In finestra.Discendenti(2500, 14)
            If (candidato.Tipo = "Edit" OrElse candidato.Tipo = "Document") AndAlso candidato.Modificabile Then
                If candidato.DaiFuoco() Then Return
            End If
        Next
        _servizi.AnnunciaSubito("Non trovo la casella di scrittura")
    End Sub

    Private Sub LeggiLaVoce()
        Dim fuoco As ElementoUi = _servizi.ElementoConFuoco()
        If fuoco Is Nothing Then
            _servizi.AnnunciaSubito("Nessun elemento con il fuoco")
            Return
        End If
        Dim testi As List(Of String) = fuoco.TestiDiscendenti(30)
        If testi.Count = 0 AndAlso fuoco.Nome.Trim() <> "" Then testi.Add(fuoco.Nome.Trim())
        _servizi.AnnunciaSubito(If(testi.Count > 0, String.Join(". ", testi), "Niente da leggere"))
    End Sub

    ' Dalla voce con il fuoco si sale fino all'elenco che la contiene.
    Private Shared Function TrovaElenco(partenza As ElementoUi) As ElementoUi
        Dim corrente As ElementoUi = partenza
        Dim salti As Integer = 0
        While corrente IsNot Nothing AndAlso salti < 12
            If corrente.Tipo = "List" Then Return corrente
            corrente = corrente.Genitore()
            salti += 1
        End While
        Return Nothing
    End Function

End Class
