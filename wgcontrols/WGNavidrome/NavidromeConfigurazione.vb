Imports System
Imports System.Security.Cryptography
Imports System.Text
Imports WGDb
Imports wgcontrols

''' <summary>
''' I dati di collegamento al server Navidrome: indirizzo, nome utente e password.
''' Stanno nella configurazione di WinGabriel (WGConfig). La password non viene scritta in chiaro:
''' si cifra con la protezione dati di Windows legata all'utente (DPAPI). Se la configurazione viene
''' copiata su un altro computer o su un altro utente, la password non si decifra più e va riscritta.
''' </summary>
Public Class NavidromeConfigurazione

    Private Const CHIAVE_ATTIVO As String = "navidrome_attivo"
    Private Const CHIAVE_INDIRIZZO As String = "navidrome_indirizzo"
    Private Const CHIAVE_UTENTE As String = "navidrome_utente"
    Private Const CHIAVE_PASSWORD As String = "navidrome_password"
    Private Const MARCATORE As String = "dpapi1:"
    Private Shared ReadOnly Entropia As Byte() = Encoding.UTF8.GetBytes("WinGabriel-Navidrome-1")

    Private ReadOnly _config As New WGConfig()
    Private ReadOnly _log As New Logger()

    Public Property Indirizzo As String = ""
    Public Property Utente As String = ""
    Public Property Password As String = ""
    Public Property Attivo As Boolean

    ''' <summary>Vero se ci sono tutti i dati per provare a collegarsi.</summary>
    Public ReadOnly Property Completa As Boolean
        Get
            Return Indirizzo <> "" AndAlso Utente <> "" AndAlso Password <> ""
        End Get
    End Property

    ''' <summary>Vero se il collegamento è stato impostato e attivato dall'utente.</summary>
    Public ReadOnly Property Configurato As Boolean
        Get
            Return Attivo AndAlso Completa
        End Get
    End Property

    Public Shared Function Carica() As NavidromeConfigurazione
        Dim c As New NavidromeConfigurazione()
        c.Leggi()
        Return c
    End Function

    Public Sub Leggi()
        Try
            Attivo = LeggiTesto(CHIAVE_ATTIVO).Equals("true", StringComparison.OrdinalIgnoreCase)
            Indirizzo = LeggiTesto(CHIAVE_INDIRIZZO)
            Utente = LeggiTesto(CHIAVE_UTENTE)
            Password = Decifra(LeggiTesto(CHIAVE_PASSWORD))
        Catch ex As Exception
            _log.ScriviLog("NavidromeConfigurazione.Leggi: " & ex.ToString())
        End Try
    End Sub

    Public Sub Salva()
        Try
            _config.SalvaValore(CHIAVE_INDIRIZZO, NavidromeClient.NormalizzaIndirizzo(Indirizzo))
            _config.SalvaValore(CHIAVE_UTENTE, If(Utente, "").Trim())
            _config.SalvaValore(CHIAVE_PASSWORD, Cifra(Password))
            _config.SalvaValore(CHIAVE_ATTIVO, If(Attivo, "true", "false"))
        Catch ex As Exception
            _log.ScriviLog("NavidromeConfigurazione.Salva: " & ex.ToString())
            Throw
        End Try
    End Sub

    ''' <summary>Scollega il server: dimentica utente e password, tiene l'indirizzo per comodità.</summary>
    Public Sub Scollega()
        Try
            Attivo = False
            Utente = ""
            Password = ""
            _config.SalvaValore(CHIAVE_ATTIVO, "false")
            _config.SalvaValore(CHIAVE_UTENTE, "")
            _config.SalvaValore(CHIAVE_PASSWORD, "")
        Catch ex As Exception
            _log.ScriviLog("NavidromeConfigurazione.Scollega: " & ex.ToString())
        End Try
    End Sub

    Public Function CreaClient() As NavidromeClient
        Return New NavidromeClient(Indirizzo, Utente, Password)
    End Function

    Private Function LeggiTesto(chiave As String) As String
        If Not _config.EsisteId(chiave) Then Return ""
        Return If(_config.LeggiValore(chiave), "")
    End Function

    Private Shared Function Cifra(testo As String) As String
        Try
            If String.IsNullOrEmpty(testo) Then Return ""
            ' libreria nativa, senza password: la password resta leggibile anche con la configurazione importata su un altro computer
            Dim protetto As String = WGCifraturaNativa.ProteggiTesto("navidrome", testo)
            If protetto = "" Then Throw New InvalidOperationException("Cifratura non riuscita.")
            Return protetto
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog("NavidromeConfigurazione.Cifra: " & ex.ToString())
            Throw
        End Try
    End Function

    Private Shared Function Decifra(valore As String) As String
        Try
            If String.IsNullOrEmpty(valore) Then Return ""
            If WGCifraturaNativa.EProtetto(valore) Then Return WGCifraturaNativa.RivelaTesto("navidrome", valore)
            If Not valore.StartsWith(MARCATORE, StringComparison.Ordinal) Then Return ""
            ' formato precedente: DPAPI, valido solo per quell'utente di Windows su quel computer
            Dim dati As Byte() = Convert.FromBase64String(valore.Substring(MARCATORE.Length))
            Return Encoding.UTF8.GetString(ProtectedData.Unprotect(dati, Entropia, DataProtectionScope.CurrentUser))
        Catch ex As Exception
            Dim log As New Logger()
            log.ScriviLog("NavidromeConfigurazione.Decifra: password non decifrabile, verrà richiesta di nuovo. " & ex.Message)
            Return ""
        End Try
    End Function

End Class
