Public Class WGDomanda
    Private menu As New WGMenu
    Public Event rispostadomanda(e As DialogResult)

    ''' <summary>
    ''' Domanda sì o no. Conta solo la prima risposta: chiudendosi il menu solleva anche Annulla,
    ''' che qui viene ignorato se si è già risposto. Esc vale come no.
    ''' </summary>
    ''' <summary>La domanda, da aspettare con Await: vero se si risponde sì (Esc vale no).</summary>
    Public Shared Function ChiediAsync(domanda As String) As System.Threading.Tasks.Task(Of Boolean)
        Dim esito As New System.Threading.Tasks.TaskCompletionSource(Of Boolean)()
        Chiedi(domanda, Sub() esito.TrySetResult(True), Sub() esito.TrySetResult(False))
        Return esito.Task
    End Function

    Public Shared Sub Chiedi(domanda As String, seSi As Action, Optional seNo As Action = Nothing)
        Dim d As New WGDomanda()
        Dim risposto As Boolean = False
        AddHandler d.rispostadomanda, Sub(risposta As DialogResult)
                                          If risposto Then Return
                                          risposto = True
                                          If risposta = DialogResult.Yes Then
                                              If seSi IsNot Nothing Then seSi()
                                          ElseIf seNo IsNot Nothing Then
                                              seNo()
                                          End If
                                      End Sub
        d.avvia(domanda)
    End Sub
    Public Sub avvia(domanda As String, optional annullabilita as boolean = false)
        menu.aggiungi("si", AddressOf si)
        menu.aggiungi("no", AddressOf no)
        If annullabilita = True Then
            menu.aggiungi("Annulla", AddressOf annulla)
        End If

        AddHandler menu.Abbandonato, AddressOf chiudimenu
        menu.Avvia(domanda, true)
    End Sub
private sub si()
try
            RaiseEvent rispostadomanda(DialogResult.Yes)
        Catch ex as ObjectDisposedException
End try
end sub
private sub no()
try
            RaiseEvent rispostadomanda(DialogResult.No)
        Catch ex as ObjectDisposedException
End try
end sub

private sub annulla()                    
try
            RaiseEvent rispostadomanda(DialogResult.Cancel)
        Catch ex as ObjectDisposedException
End try

end sub
    Private Sub chiudimenu()
        Try
            RaiseEvent rispostadomanda(DialogResult.Cancel)
        Catch ex As ObjectDisposedException
        End Try

    End Sub
End Class