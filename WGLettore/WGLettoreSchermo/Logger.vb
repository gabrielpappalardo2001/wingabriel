' Il lettore di schermo non scrive log. Questa classe, nello spazio dei nomi del progetto, ha la precedenza
' su wgcontrols.Logger: tutte le chiamate a ScriviLog restano nel codice ma non producono niente.
Friend NotInheritable Class Logger

    Public Sub ScriviLog(messaggio As String)
    End Sub

End Class
