Imports wgcontrols
Imports System.IO

''' <summary>
''' L'ambiente beta dei giochi e della chat: i server di prova su wingabriel.net/beta, con un loro elenco di utenti e le loro partite.
''' Si accende SOLO a mano, scrivendo giochibeta = true nel file di configurazione (nessuna impostazione nei menu), e SOLO se WinGabriel
''' parte dalla cartella bin\Debug: da qualunque altra cartella (la versione installata) si usano sempre i giochi e la chat normali.
''' Nell'ambiente beta il nome utente e la sessione si salvano con chiavi diverse (con il suffisso "Beta"), così non si mescolano a quelli veri.
''' </summary>
Public NotInheritable Class AmbienteBeta

    Private Const CHIAVE As String = "giochibeta"
    Private Shared _attivo As Boolean? = Nothing

    Private Sub New()
    End Sub

    ''' <summary>True se si usano i server beta (giochibeta = true e programma avviato dalla cartella bin\Debug).</summary>
    Public Shared ReadOnly Property Attivo As Boolean
        Get
            If _attivo.HasValue Then Return _attivo.Value
            Dim valore As Boolean = False
            Try
                If AvviatoDaDebug() Then
                    Dim c As New WGConfig()
                    If c.EsisteId(CHIAVE) Then
                        Dim t As String = If(c.LeggiValore(CHIAVE), "").Trim()
                        valore = String.Equals(t, "true", StringComparison.OrdinalIgnoreCase)
                    End If
                End If
            Catch exLog As Exception
                RegistroErrori.Registra("AmbienteBeta.New", exLog)
                valore = False
            End Try
            _attivo = valore
            Return valore
        End Get
    End Property

    Private Shared Function AvviatoDaDebug() As Boolean
        Try
            Dim b As String = AppDomain.CurrentDomain.BaseDirectory.Replace("/"c, "\"c).TrimEnd("\"c) & "\"
            Return b.IndexOf("\bin\debug\", StringComparison.OrdinalIgnoreCase) >= 0
        Catch ex As Exception
            RegistroErrori.Registra("AmbienteBeta.AvviatoDaDebug", ex)
            Throw
        End Try
    End Function

    ''' <summary>Aggiunto alle chiavi di configurazione che ricordano nome utente, sessione e password: vuoto fuori dalla beta.</summary>
    Public Shared ReadOnly Property Suffisso As String
        Get
            Return If(Attivo, "Beta", "")
        End Get
    End Property

    Public Shared ReadOnly Property SitoGiochi As String
        Get
            Return If(Attivo, "https://wingabriel.net/beta/giochi/", "https://wingabriel.net/giochi/")
        End Get
    End Property

    Public Shared ReadOnly Property ServerGiochi As String
        Get
            Return SitoGiochi & "giochi.php"
        End Get
    End Property

    Public Shared ReadOnly Property ServerChat As String
        Get
            Return If(Attivo, "https://wingabriel.net/beta/chat/chat.php", "https://wingabriel.net/chat/chat.php")
        End Get
    End Property

End Class
