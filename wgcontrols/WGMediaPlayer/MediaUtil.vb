Imports System
Imports System.Collections.Generic
Imports wgcontrols

''' <summary>Piccoli aiutanti condivisi dalle schermate del Media player.</summary>
Friend Module MediaUtil

    Private ReadOnly _log As New Logger()

    Friend Sub Log(testo As String)
        Try
            _log.ScriviLog(testo)
        Catch
        End Try
    End Sub

    ''' <summary>Un messaggio letto dalla sintesi vocale. Serve anche per gli errori: niente domande sì o no.</summary>
    Friend Sub Messaggio(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            Log("MediaUtil.Messaggio: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Chiede una riga di testo; se l'utente conferma e il testo non è vuoto chiama l'azione.</summary>
    Friend Sub ChiediTesto(titolo As String, predefinito As String, quandoConfermato As Action(Of String))
        Try
            Dim campi As New Campi()
            campi.Aggiungi(titolo, "media.testo", True, False, If(predefinito, ""))
            AddHandler campi.Fine, Sub()
                                       Try
                                           Dim valore As String = campi.RecuperaValore("media.testo")
                                           If Not String.IsNullOrWhiteSpace(valore) Then quandoConfermato(valore.Trim())
                                       Catch ex As Exception
                                           Log("MediaUtil.ChiediTesto: " & ex.ToString())
                                       End Try
                                   End Sub
            campi.Avvia()
        Catch ex As Exception
            Log("MediaUtil.ChiediTesto: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Un piccolo menu di scelte: ogni voce chiude il menu e poi esegue la sua azione.</summary>
    Friend Sub Scegli(titolo As String, voci As IEnumerable(Of KeyValuePair(Of String, Action)), Optional posizione As Integer = -1)
        Try
            Dim menu As New WGMenu()
            For Each voce As KeyValuePair(Of String, Action) In voci
                Dim azione As Action = voce.Value
                menu.aggiungi(voce.Key, Sub()
                                            Try
                                                menu.abbandona()
                                                azione()
                                            Catch ex As Exception
                                                Log("MediaUtil.Scegli: " & ex.ToString())
                                            End Try
                                        End Sub)
            Next
            If posizione >= 0 Then menu.posiziona(posizione)
            menu.avvia(titolo, True)
        Catch ex As Exception
            Log("MediaUtil.Scegli: " & ex.ToString())
        End Try
    End Sub

    Friend Function Voce(testo As String, azione As Action) As KeyValuePair(Of String, Action)
        Return New KeyValuePair(Of String, Action)(testo, azione)
    End Function

End Module
