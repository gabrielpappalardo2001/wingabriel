Imports System.Collections.Generic
Imports System.Text
Imports WGStampante

' Estrazione dei blocchi di stampa e generazione del testo ottimizzato per la sintesi vocale e la stampa.
Partial Public Class WGWord
    Public Function OttieniBlocchiStampa() As List(Of WGBloccoStampa)
        Dim risultato As New List(Of WGBloccoStampa)()
        Try
            SyncLock _elementsLock
                If _elements Is Nothing Then Return risultato
                For Each el In _elements
                    If TypeOf el Is DocParagraph Then
                        Dim blocco As New WGBloccoStampa()
                        blocco.Tipo = "testo"
                        blocco.Testo = DirectCast(el, DocParagraph).GetText()
                        risultato.Add(blocco)
                    ElseIf TypeOf el Is DocTable Then
                        risultato.Add(BloccoDaTabella(DirectCast(el, DocTable)))
                    End If
                Next
            End SyncLock
        Catch ex As Exception
            logger.ScriviLog("WGWord.OttieniBlocchiStampa: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Function OttieniTestoPerSintesi() As String
        Try
            Dim sb As New StringBuilder()
            SyncLock _elementsLock
                If _elements Is Nothing Then Return ""
                For Each el In _elements
                    If TypeOf el Is DocParagraph Then
                        sb.AppendLine(DirectCast(el, DocParagraph).GetText())
                    ElseIf TypeOf el Is DocTable Then
                        sb.AppendLine(TestoTabellaPerSintesi(DirectCast(el, DocTable)))
                    End If
                Next
            End SyncLock
            Return sb.ToString()
        Catch ex As Exception
            logger.ScriviLog("WGWord.OttieniTestoPerSintesi: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Property UltimoEsitoStampa As Global.WGStampante.WGEsitoStampa

    Public Function Stampa(nomeStampante As String, Optional titoloDocumento As String = "Documento") As Boolean
        Try
            UltimoEsitoStampa = Nothing
            Dim blocchi As List(Of WGBloccoStampa) = OttieniBlocchiStampa()
            If blocchi Is Nothing OrElse blocchi.Count = 0 Then
                logger.ScriviLog("WGWord.Stampa: nessun contenuto")
                Return False
            End If
            Dim impostazioniStampa As WGImpostazioniStampa = New WGImpostazioniComuni().CostruisciImpostazioniStampa()
            Dim esito As WGStampante.WGEsitoStampa = WGStampante.WGStampante.StampaBlocchi(blocchi, nomeStampante, titoloDocumento, impostazioniStampa, True)
            UltimoEsitoStampa = esito
            If esito Is Nothing Then Return False
            If Not esito.Riuscita AndAlso Not String.IsNullOrWhiteSpace(esito.Errore) Then
                logger.ScriviLog("WGWord.Stampa: " & esito.Errore)
            End If
            Return esito.Riuscita
        Catch ex As Exception
            logger.ScriviLog("WGWord.Stampa: " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function EstraiBlocchiStampaDaFile(percorso As String) As List(Of WGBloccoStampa)
        Dim risultato As New List(Of WGBloccoStampa)()
        Try
            Dim elements = WGWordUtils.CaricaDocumento(percorso, Function() False)
            If elements Is Nothing Then Return risultato
            For Each el In elements
                If TypeOf el Is DocParagraph Then
                    Dim blocco As New WGBloccoStampa()
                    blocco.Tipo = "testo"
                    blocco.Testo = DirectCast(el, DocParagraph).GetText()
                    risultato.Add(blocco)
                ElseIf TypeOf el Is DocTable Then
                    risultato.Add(BloccoDaTabella(DirectCast(el, DocTable)))
                End If
            Next
        Catch ex As Exception
            Dim logLocale As New Logger()
            logLocale.ScriviLog("WGWord.EstraiBlocchiStampaDaFile: " & ex.Message)
        End Try
        Return risultato
    End Function

    Public Shared Function EstraiTestoCompleto(percorso As String) As String
        Try
            Dim elements = WGWordUtils.CaricaDocumento(percorso, Function() False)
            Dim sb As New StringBuilder()
            If elements Is Nothing Then Return ""
            For Each el In elements
                If TypeOf el Is DocParagraph Then
                    sb.AppendLine(DirectCast(el, DocParagraph).GetText())
                ElseIf TypeOf el Is DocTable Then
                    sb.AppendLine(TestoTabellaPerSintesi(DirectCast(el, DocTable)))
                End If
            Next
            Return sb.ToString()
        Catch ex As Exception
            Dim logLocale As New Logger()
            logLocale.ScriviLog("WGWord.EstraiTestoCompleto: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Shared Function BloccoDaTabella(tbl As DocTable) As WGBloccoStampa
        Dim blocco As New WGBloccoStampa()
        Try
            blocco.Tipo = "tabella"
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return blocco
            For Each r In tbl.Rows
                Dim celle As New List(Of String)()
                If r IsNot Nothing AndAlso r.Cells IsNot Nothing Then
                    For Each c In r.Cells
                        Dim cellText As New StringBuilder()
                        If c IsNot Nothing AndAlso c.Paragraphs IsNot Nothing Then
                            For Each p In c.Paragraphs
                                cellText.Append(p.GetText() & " ")
                            Next
                        End If
                        celle.Add(cellText.ToString().Trim())
                    Next
                End If
                blocco.RigheTabella.Add(celle.ToArray())
            Next
        Catch ex As Exception
            Dim logLocale As New Logger()
            logLocale.ScriviLog("WGWord.BloccoDaTabella: " & ex.Message)
        End Try
        Return blocco
    End Function

    Private Shared Function TestoTabellaPerSintesi(tbl As DocTable) As String
        Try
            Dim sb As New StringBuilder()
            sb.AppendLine("Tabella.")
            If tbl Is Nothing OrElse tbl.Rows Is Nothing Then Return sb.ToString()
            Dim numeroRiga As Integer = 1
            For Each r In tbl.Rows
                Dim parti As New List(Of String)()
                If r IsNot Nothing AndAlso r.Cells IsNot Nothing Then
                    Dim numeroCella As Integer = 1
                    For Each c In r.Cells
                        Dim cellText As New StringBuilder()
                        If c IsNot Nothing AndAlso c.Paragraphs IsNot Nothing Then
                            For Each p In c.Paragraphs
                                cellText.Append(p.GetText() & " ")
                            Next
                        End If
                        parti.Add("colonna " & numeroCella.ToString() & " " & cellText.ToString().Trim())
                        numeroCella += 1
                    Next
                End If
                sb.AppendLine("Riga " & numeroRiga.ToString() & ": " & String.Join(", ", parti) & ".")
                numeroRiga += 1
            Next
            Return sb.ToString()
        Catch ex As Exception
            Dim logLocale As New Logger()
            logLocale.ScriviLog("WGWord.TestoTabellaPerSintesi: " & ex.Message)
            Return "Tabella."
        End Try
    End Function
End Class