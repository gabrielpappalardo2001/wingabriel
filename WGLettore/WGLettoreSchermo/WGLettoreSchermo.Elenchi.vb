Imports System.Collections.Generic
Imports System.Threading
Imports UIAutomationClient

' Garantisce "3 di 12" in coda a ogni voce di elenco o scheda, in qualunque programma (desktop, Esplora risorse, finestre di
' Windows): prima UIA, poi il. Sul desktop e nelle liste di Windows la posizione non arriva da UIA: la si prende dalla lista
' stessa (Questo PC, 7 di 13).
Partial Public NotInheritable Class WGLettoreSchermo

    Private Shared ReadOnly _regexPosizione As New System.Text.RegularExpressions.Regex("(^|[^0-9])[0-9]+ di [0-9]+($|[^0-9])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)

    ' Garantisce "3 di 12" in coda a ogni voce di elenco o scheda, in qualunque programma (desktop, Esplora risorse, finestre di Windows): prima UIA, poi il controllo nativo, poi i fratelli.
    Private Function AggiungiPosizioneFinale(elemento As IUIAutomationElement, testo As String) As String
        Try
            If Not AnnunciaPosizioneElenchi OrElse elemento Is Nothing OrElse _descrittore Is Nothing OrElse String.IsNullOrEmpty(testo) Then Return testo
            Select Case _descrittore.TipoControllo(elemento)
                Case DescrittoreElementi.ControlloElementoElenco, DescrittoreElementi.ControlloElementoDati, DescrittoreElementi.ControlloScheda
                Case Else
                    Return testo
            End Select
            If _regexPosizione.IsMatch(testo) Then Return testo
            If ElementoWeb(elemento) Then Return testo
            Dim posizione As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaPosizione, 0)
            Dim totale As Integer = _descrittore.LeggiIntero(elemento, DescrittoreElementi.ProprietaTotale, 0)
            If posizione <= 0 OrElse totale <= 0 OrElse posizione > totale Then
                posizione = 0
                totale = 0
                Dim lista As IntPtr = ControlliNativi.TrovaControllo(elemento, _walkerControllo, _richiestaCache, True)
                If lista <> IntPtr.Zero Then
                    Dim riga As ControlliNativi.RigaNativa = Nothing
                    If ControlliNativi.LeggiRigaLista(lista, riga) AndAlso riga.Totale > 0 AndAlso riga.Indice > 0 Then
                        posizione = riga.Indice
                        totale = riga.Totale
                    End If
                End If
            End If
            If posizione <= 0 OrElse totale <= 0 Then PosizioneTraFratelli(elemento, posizione, totale)
            If posizione <= 0 OrElse totale <= 0 OrElse posizione > totale Then Return testo
            Return testo & ", " & posizione.ToString() & " di " & totale.ToString()
        Catch ex As Exception
            RegistroErrori.Scrivi("WGLettoreSchermo.AggiungiPosizioneFinale", ex)
            Return testo
        End Try
    End Function

    Private Function AggiungiColonneRiga(elemento As IUIAutomationElement, testo As String) As String
        Try
            Dim web As Boolean = ElementoWeb(elemento)
            Dim soloRigheAria As Boolean = web AndAlso Not _descrittore.VoceDiComposito(elemento)
            If Not web Then
                Dim lista As IntPtr = ControlliNativi.TrovaControllo(elemento, _walkerControllo, _richiestaCache, True)
                If lista <> IntPtr.Zero Then
                    Dim riga As ControlliNativi.RigaNativa = Nothing
                    If ControlliNativi.LeggiRigaLista(lista, riga) Then
                        Dim colonne As String = ControlliNativi.SoloColonne(riga)
                        If colonne <> "" Then testo = testo & ", " & colonne
                        ' Sul desktop e nelle liste di Windows la posizione non arriva da UIA: la si prende dalla lista stessa (Questo PC, 7 di 13).
                        If AnnunciaPosizioneElenchi AndAlso riga.Totale > 1 AndAlso riga.Indice > 0 AndAlso testo.IndexOf(" di " & riga.Totale.ToString(), StringComparison.Ordinal) < 0 Then
                            testo = testo & ", " & riga.Indice.ToString() & " di " & riga.Totale.ToString()
                        End If
                        Return testo
                    End If
                End If
            End If
            Dim sostituisce As Boolean = False
            Dim dalleCelle As String = LetturaRigheElenco.Descrivi(elemento, _descrittore, _walkerControllo, _richiestaCache, soloRigheAria, sostituisce)
            If dalleCelle <> "" Then
                Dim nome As String = _descrittore.Nome(elemento)
                If sostituisce AndAlso nome <> "" Then
                    Dim posizione As Integer = testo.IndexOf(nome, StringComparison.Ordinal)
                    If posizione >= 0 Then Return testo.Substring(0, posizione) & dalleCelle & testo.Substring(posizione + nome.Length)
                End If
                Return testo & ", " & dalleCelle
            End If
            If Not web Then
                Dim vecchie As String = _descrittore.DescriviColonne(elemento, _walkerControllo, _richiestaCache)
                If vecchie <> "" Then Return testo & ", " & vecchie
            End If
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.AggiungiColonneRiga: " & ex.Message)
        End Try
        Return testo
    End Function

    Private Function VoceElencoSenzaNome(elemento As IUIAutomationElement, testo As String) As String
        Try
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return testo
            If _descrittore.Nome(elemento) <> "" Then Return testo
            Dim ruolo As String = _descrittore.RuoloAria(elemento)
            Dim tipo As Integer = _descrittore.TipoControllo(elemento)
            Dim eVoce As Boolean = tipo = DescrittoreElementi.ControlloElementoElenco OrElse tipo = DescrittoreElementi.ControlloElementoAlbero OrElse
                                   tipo = DescrittoreElementi.ControlloElementoDati OrElse tipo = DescrittoreElementi.ControlloElementoIntestazione
            Select Case ruolo
                Case "listitem", "article", "row", "treeitem", "cell", "gridcell", "columnheader", "rowheader", "option"
                    eVoce = True
            End Select
            If Not eVoce AndAlso _descrittore.VoceDiComposito(elemento) Then eVoce = True
            If Not eVoce Then Return testo
            Dim contenuto As String = LetturaRigheElenco.Contenuto(elemento, _descrittore, _walkerControllo, _richiestaCache, ElementoWeb(elemento))
            If contenuto = "" Then Return testo
            Return If(testo = "", contenuto, contenuto & ", " & testo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.VoceElencoSenzaNome: " & ex.Message)
            Return testo
        End Try
    End Function

    Private Function TestoAggiornatoNuovo(evento As EventoLettore) As Boolean
        Try
            Dim elemento As IUIAutomationElement = evento.Elemento
            If elemento Is Nothing OrElse _descrittore Is Nothing Then Return False
            If Not LetturaTestoAggiornato.ESuperficieDiTesto(elemento, _descrittore) Then Return False
            Dim nuovo As String = LetturaTestoAggiornato.NuovoTesto(elemento)
            If nuovo = "" Then Return True
            If TastoRecente(350) AndAlso nuovo.Length <= 3 Then Return True
            If FinestraTerminale(NativoLettore.GetForegroundWindow()) Then
                ParlaRigheTerminale(nuovo)
            Else
                Parla(nuovo, False)
            End If
            Return True
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.TestoAggiornatoNuovo: " & ex.Message)
            Return False
        End Try
    End Function

    Private Sub ParlaRigheTerminale(testo As String)
        Try
            If String.IsNullOrEmpty(testo) Then Return
            Dim righe As String() = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ChrW(10))
            Dim utili As New List(Of String)()
            For Each riga As String In righe
                If Not String.IsNullOrWhiteSpace(riga) Then utili.Add(riga.TrimEnd())
            Next
            Volatile.Write(_origineUtente, 1)
            Try
                For Each riga As String In utili
                    Parla(riga, False)
                Next
            Finally
                Volatile.Write(_origineUtente, 0)
            End Try
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.ParlaRigheTerminale: " & ex.Message)
        End Try
    End Sub

    Private Function DescriviAlberoNativo(elemento As IUIAutomationElement, testo As String) As String
        Try
            If Not _descrittore.EAlbero(elemento) Then Return testo
            Dim albero As IntPtr = ControlliNativi.TrovaControllo(elemento, _walkerControllo, _richiestaCache, False)
            If albero = IntPtr.Zero Then Return testo
            Dim nodo As ControlliNativi.NodoNativo = Nothing
            If Not ControlliNativi.LeggiNodoSelezionato(albero, nodo) OrElse String.IsNullOrEmpty(nodo.Nome) Then Return testo
            Return ControlliNativi.FraseNodo(nodo)
        Catch ex As Exception
            log.ScriviLog("WGLettoreSchermo.DescriviAlberoNativo: " & ex.Message)
            Return testo
        End Try
    End Function

End Class
