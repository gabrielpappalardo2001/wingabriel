' Strutture dati per preferiti, visite, credenziali, permessi e download del browser.
Public Class PreferitoBrowser
    Public Property Chiave As String = ""
    Public Property Titolo As String = ""
    Public Property Indirizzo As String = ""
    Public Property Cartella As String = ""
    Public Property Ordine As Integer = 0
    Public Property DataAggiunta As DateTime = DateTime.MinValue
End Class

Public Class VisitaBrowser
    Public Property Chiave As String = ""
    Public Property Indirizzo As String = ""
    Public Property Titolo As String = ""
    Public Property Host As String = ""
    Public Property UltimaVisita As DateTime = DateTime.MinValue
    Public Property Visite As Integer = 0
End Class

Public Class DownloadRegistrato
    Public Property Chiave As String = ""
    Public Property Nome As String = ""
    Public Property Percorso As String = ""
    Public Property Indirizzo As String = ""
    Public Property Stato As String = ""
    Public Property Dimensione As Long = 0
    Public Property Data As DateTime = DateTime.MinValue
End Class

Public Class CredenzialeBrowser
    Public Property Chiave As String = ""
    Public Property Origine As String = ""
    Public Property Utente As String = ""
    Public Property Password As String = ""
    Public Property UltimoUso As DateTime = DateTime.MinValue
End Class

Public Class PermessoSitoBrowser
    Public Property Chiave As String = ""
    Public Property Origine As String = ""
    Public Property Tipo As String = ""
    Public Property Valore As String = ""
End Class

Public Class DownloadAttivoBrowser
    Public Property Id As Integer = 0
    Public Property Nome As String = ""
    Public Property Percorso As String = ""
    Public Property Percentuale As Integer = -1
    Public Property Ricevuti As Long = 0
    Public Property Totali As Long = 0
    Public Property Velocita As Long = 0
    Public Property InPausa As Boolean = False
End Class

Public Interface IWGControlloDownload
    Function DownloadInCorso() As List(Of DownloadAttivoBrowser)
    Sub PausaDownload(id As Integer)
    Sub RiprendiDownload(id As Integer)
    Sub AnnullaDownload(id As Integer)
End Interface
