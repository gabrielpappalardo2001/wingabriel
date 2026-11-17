Imports System.Security.Cryptography
Imports System.Text
Imports GabrielSync.Filen
Imports WGDb
Imports wgcontrols

''' <summary>
''' Protegge password, utenti, dati personali e cookie del browser con la libreria nativa di cifratura (prefisso "WGX:"):
''' senza password dell'utente, con la chiave dello scopo "browser". I dati si leggono su qualunque computer e utente di Windows,
''' quindi viaggiano con la sincronizzazione e con le esportazioni dei dati.
''' Si leggono ancora i formati precedenti: "wg2:" (chiave derivata dall'account Filen) e DPAPI (solo quell'utente su quel computer).
''' </summary>
Public NotInheritable Class WGProtezioneDati
    Private Shared ReadOnly _log As New Logger()
    Private Shared ReadOnly _entropia As Byte() = Encoding.UTF8.GetBytes("WGBrowserManager.ProtezioneDati.2026")
    Private Shared _chiaveFilen As String = ""
    Private Const PrefissoFilen As String = "wg2:"
    Private Const Scopo As String = "browser"

    Private Sub New()
    End Sub

    Private Shared Function ChiaveFilen() As String
        Try
            Dim chiave As String = _chiaveFilen
            If chiave <> "" Then Return chiave
            chiave = WGSincronizzazioneCloud.ChiavePortabile()
            If chiave <> "" Then _chiaveFilen = chiave
            Return chiave
        Catch ex As Exception
            _log.ScriviLog("WGProtezioneDati.ChiaveFilen: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>Vero se il testo è già nel formato che si legge su ogni computer.</summary>
    Public Shared Function EPortabile(testoProtetto As String) As Boolean
        Return WGCifraturaNativa.EProtetto(testoProtetto)
    End Function

    ''' <summary>Vero se si possono scrivere dati portabili (la libreria di cifratura è caricata).</summary>
    Public Shared ReadOnly Property PortabilePossibile As Boolean
        Get
            Return WGCifraturaNativa.Disponibile
        End Get
    End Property

    Public Shared Function Proteggi(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            Return WGCifraturaNativa.ProteggiTesto(Scopo, testo)
        Catch ex As Exception
            _log.ScriviLog("WGProtezioneDati.Proteggi: " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function Rivela(testoProtetto As String) As String
        Try
            If String.IsNullOrEmpty(testoProtetto) Then Return ""
            If WGCifraturaNativa.EProtetto(testoProtetto) Then Return WGCifraturaNativa.RivelaTesto(Scopo, testoProtetto)
            If testoProtetto.StartsWith(PrefissoFilen, StringComparison.Ordinal) Then
                Dim chiave As String = ChiaveFilen()
                If chiave = "" Then Return ""
                Return FilenCrypto.DecifraMetadati(testoProtetto.Substring(PrefissoFilen.Length), chiave)
            End If
            Dim protetti As Byte() = Convert.FromBase64String(testoProtetto)
            Dim dati As Byte() = ProtectedData.Unprotect(protetti, _entropia, DataProtectionScope.CurrentUser)
            Return Encoding.UTF8.GetString(dati)
        Catch ex As Exception
            _log.ScriviLog("WGProtezioneDati.Rivela: " & ex.Message)
            Return ""
        End Try
    End Function

    ''' <summary>Riscrive nel formato portabile un testo protetto in un formato precedente. Se non si può, lo restituisce com'è.</summary>
    Public Shared Function RendiPortabile(testoProtetto As String) As String
        Try
            If String.IsNullOrEmpty(testoProtetto) OrElse EPortabile(testoProtetto) Then Return testoProtetto
            Dim chiaro As String = Rivela(testoProtetto)
            If chiaro = "" Then Return testoProtetto
            Dim nuovo As String = Proteggi(chiaro)
            Return If(EPortabile(nuovo), nuovo, testoProtetto)
        Catch ex As Exception
            _log.ScriviLog("WGProtezioneDati.RendiPortabile: " & ex.Message)
            Return testoProtetto
        End Try
    End Function
End Class
