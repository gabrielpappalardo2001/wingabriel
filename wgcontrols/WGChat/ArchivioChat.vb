Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text
Imports Newtonsoft.Json.Linq
Imports WGDb

' ============================================================================================
' ArchivioChatDisco - dove la chat tiene i suoi dati sul computer, sempre cifrati con una password
' ============================================================================================
'
' Cartella dati\chat\<nome utente>: ogni nome utente ha il suo archivio; la chiave viene dalla password del nome utente dei giochi (la metà
' "locale" del derivato, che il server non vede mai), quindi non c'è una seconda password da ricordare.
' Dentro:
'   dispositivo\stato.wgc   lo stato di questo dispositivo (chiave d'identità, sessioni cifrate, coda): NON va sincronizzato, perché
'                           copiare le sessioni su un altro dispositivo le rovinerebbe;
'   dispositivo\sale.bin    il sale della chiave dello stato;
'   storico\sale.bin        il sale della chiave dello storico: uguale su tutti i dispositivi di una persona;
'   storico\<dispositivo>\<conversazione>.log   i messaggi, una riga cifrata per volta (solo aggiunte).
' Per avere la cronologia su più dispositivi basta sincronizzare la cartella "storico" con Filen: ogni dispositivo scrive soltanto
' nei propri file, quindi non ci sono mai conflitti, e quando legge unisce quelli di tutti. Serve la stessa password dappertutto.
' Le chiavi si ricavano dalla chiave locale con PBKDF2-SHA256 e i dati si cifrano con AES-256-GCM (libreria nativa di WinGabriel).
' ============================================================================================
Public NotInheritable Class ArchivioChatDisco
    Implements IArchivioChat

    Private Const Iterazioni As Integer = 1000      ' la chiave locale è già un derivato lento della password: qui basta separare gli usi
    Private Const AadStato As String = "WGChat-stato-v1"

    Private ReadOnly _base As String
    Private ReadOnly _chiaveStato As Byte()
    Private ReadOnly _chiaveStorico As Byte()
    Private ReadOnly _idDispositivo As String

    Public ReadOnly Property Stato As JObject

    Private Sub New(basePath As String, chiaveStato As Byte(), chiaveStorico As Byte(), id As String, stato As JObject)
        Try
            _base = basePath
            _chiaveStato = chiaveStato
            _chiaveStorico = chiaveStorico
            _idDispositivo = id
            Me.Stato = stato
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.New", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Cartella(utente As String) As String
        Try
            Dim nomeCartella As String = CriptoChat.ShaHex(Encoding.UTF8.GetBytes(utente.ToLowerInvariant())).Substring(0, 16)
            Dim c As String = Path.Combine(WGDatabase.CartellaDati("chat"), nomeCartella)
            Directory.CreateDirectory(c)
            Return c
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.Cartella", ex)
            Throw
        End Try
    End Function

    Public Shared Function Esiste(utente As String) As Boolean
        Try
            Return File.Exists(Path.Combine(Cartella(utente), "dispositivo", "stato.wgc"))
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.Esiste", ex)
            Throw
        End Try
    End Function

    Private Shared Function Sale(percorso As String) As Byte()
        Try
            If File.Exists(percorso) Then
                Dim s As Byte() = File.ReadAllBytes(percorso)
                If s.Length >= 16 Then Return s
            End If
            Directory.CreateDirectory(Path.GetDirectoryName(percorso))
            Dim nuovo As Byte() = CriptoChat.Casuali(16)
            File.WriteAllBytes(percorso, nuovo)
            Return nuovo
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.Sale", ex)
            Throw
        End Try
    End Function

    Private Shared Function Chiavi(master As Byte(), base2 As String, ByRef chiaveStorico As Byte()) As Byte()
        Try
            Dim saleStato As Byte() = Sale(Path.Combine(base2, "dispositivo", "sale.bin"))
            Dim saleStorico As Byte() = Sale(Path.Combine(base2, "storico", "sale.bin"))
            Dim segreto As String = CriptoChat.InHex(master)
            chiaveStorico = WGCifraturaNativa.DerivaPbkdf2("storico|" & segreto, saleStorico, Iterazioni, 32)
            Return WGCifraturaNativa.DerivaPbkdf2("stato|" & segreto, saleStato, Iterazioni, 32)
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.Chiavi", ex)
            Throw
        End Try
    End Function

    ''' <summary>Crea la cassaforte della chat di un nome utente; la chiave viene dalla sua password.</summary>
    Public Shared Function Crea(utente As String, master As Byte()) As ArchivioChatDisco
        Try
            If master Is Nothing OrElse master.Length < 16 Then Throw New ArgumentException("master")
            Dim b As String = Cartella(utente)
            Dim storico As Byte() = Nothing
            Dim chiave As Byte() = Chiavi(master, b, storico)
            Dim id As String = CriptoChat.InHex(CriptoChat.Casuali(6))
            File.WriteAllText(Path.Combine(b, "dispositivo", "id.txt"), id)
            Dim a As New ArchivioChatDisco(b, chiave, storico, id, MotoreChat.StatoVuoto(utente))
            a.SalvaStato(a.Stato)
            Return a
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.Crea", ex)
            Throw
        End Try
    End Function

    ''' <summary>Apre la cassaforte. Restituisce Nothing se la chiave non è quella giusta (per esempio password cambiata).</summary>
    Public Shared Function Apri(utente As String, master As Byte()) As ArchivioChatDisco
        Try
            Dim b As String = Cartella(utente)
            Dim file2 As String = Path.Combine(b, "dispositivo", "stato.wgc")
            If Not File.Exists(file2) Then Return Nothing
            Dim storico As Byte() = Nothing
            Dim chiave As Byte() = Chiavi(master, b, storico)
            Dim chiaro As Byte()
            Try
                chiaro = WGCifraturaNativa.DecifraConChiave(chiave, File.ReadAllBytes(file2), Encoding.UTF8.GetBytes(AadStato))
            Catch exLog As Exception
                RegistroErrori.Registra("ArchivioChatDisco.Apri", exLog)
                Return Nothing
            End Try
            If chiaro Is Nothing Then Return Nothing
            Dim idFile As String = Path.Combine(b, "dispositivo", "id.txt")
            Dim id As String = If(File.Exists(idFile), File.ReadAllText(idFile).Trim(), CriptoChat.InHex(CriptoChat.Casuali(6)))
            If Not File.Exists(idFile) Then File.WriteAllText(idFile, id)
            Return New ArchivioChatDisco(b, chiave, storico, id, JObject.Parse(Encoding.UTF8.GetString(chiaro)))
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.Apri", ex)
            Throw
        End Try
    End Function

    Public Sub SalvaStato(stato2 As JObject) Implements IArchivioChat.SalvaStato
        Try
            Dim cartellaDisp As String = Path.Combine(_base, "dispositivo")
            Directory.CreateDirectory(cartellaDisp)
            Dim cifrato As Byte() = WGCifraturaNativa.CifraConChiave(_chiaveStato, Encoding.UTF8.GetBytes(stato2.ToString(Newtonsoft.Json.Formatting.None)), Encoding.UTF8.GetBytes(AadStato))
            Dim finale As String = Path.Combine(cartellaDisp, "stato.wgc")
            Dim temp As String = finale & ".tmp"
            File.WriteAllBytes(temp, cifrato)
            If File.Exists(finale) Then
                File.Replace(temp, finale, Nothing)
            Else
                File.Move(temp, finale)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.SalvaStato", ex)
            Throw
        End Try
    End Sub

    Private Shared Function NomeFile(conversazione As String) As String
        Try
            Dim sb As New StringBuilder()
            For Each ch As Char In conversazione
                If Char.IsLetterOrDigit(ch) Then sb.Append(ch)
            Next
            Dim t As String = sb.ToString()
            If t.Length = 0 OrElse t.Length > 40 Then t = CriptoChat.ShaHex(Encoding.UTF8.GetBytes(conversazione)).Substring(0, 24)
            Return t
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.NomeFile", ex)
            Throw
        End Try
    End Function

    Private Function FileMio(conversazione As String) As String
        Try
            Return Path.Combine(_base, "storico", _idDispositivo, NomeFile(conversazione) & ".log")
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.FileMio", ex)
            Throw
        End Try
    End Function

    Public Sub SalvaMessaggio(conversazione As String, messaggio As JObject) Implements IArchivioChat.SalvaMessaggio
        Try
            Dim percorso As String = FileMio(conversazione)
            Directory.CreateDirectory(Path.GetDirectoryName(percorso))
            Dim cifrato As Byte() = WGCifraturaNativa.CifraConChiave(_chiaveStorico, Encoding.UTF8.GetBytes(messaggio.ToString(Newtonsoft.Json.Formatting.None)), Encoding.UTF8.GetBytes(NomeFile(conversazione)))
            File.AppendAllText(percorso, Convert.ToBase64String(cifrato) & vbLf, Encoding.ASCII)
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.SalvaMessaggio", ex)
            Throw
        End Try
    End Sub

    Private Shared Function Grado(st As String) As Integer
        Try
            Select Case st
                Case "consegnato" : Return 3
                Case "scaduto" : Return 2
                Case "inviato" : Return 1
            End Select
            Return 0
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.Grado", ex)
            Throw
        End Try
    End Function

    Public Function CaricaMessaggi(conversazione As String) As JArray Implements IArchivioChat.CaricaMessaggi
        Try
            Dim viste As New Dictionary(Of String, JObject)()
            Dim radice As String = Path.Combine(_base, "storico")
            If Not Directory.Exists(radice) Then Return New JArray()
            Dim nome As String = NomeFile(conversazione)
            For Each cartellaDisp As String In Directory.GetDirectories(radice)
                Dim percorso As String = Path.Combine(cartellaDisp, nome & ".log")
                If Not File.Exists(percorso) Then Continue For
                For Each riga As String In File.ReadAllLines(percorso, Encoding.ASCII)
                    If riga.Length = 0 Then Continue For
                    Try
                        Dim m As JObject = JObject.Parse(Encoding.UTF8.GetString(WGCifraturaNativa.DecifraConChiave(_chiaveStorico, Convert.FromBase64String(riga), Encoding.UTF8.GetBytes(nome))))
                        Dim id As String = CStr(m("id"))
                        Dim prec As JObject = Nothing
                        If Not viste.TryGetValue(id, prec) OrElse Grado(CStr(m("st"))) >= Grado(CStr(prec("st"))) Then viste(id) = m
                    Catch exLog As Exception
                        RegistroErrori.Registra("ArchivioChatDisco.CaricaMessaggi", exLog)
                        ' riga rovinata o scritta con un'altra password: si salta
                    End Try
                Next
            Next
            Return New JArray(viste.Values.OrderBy(Function(m) CLng(m("ts"))).Cast(Of Object)())
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.CaricaMessaggi", ex)
            Throw
        End Try
    End Function

    Public Sub EliminaConversazione(conversazione As String) Implements IArchivioChat.EliminaConversazione
        Try
            Dim radice As String = Path.Combine(_base, "storico")
            If Not Directory.Exists(radice) Then Return
            Dim nome As String = NomeFile(conversazione)
            For Each cartellaDisp As String In Directory.GetDirectories(radice)
                Try
                    Dim percorso As String = Path.Combine(cartellaDisp, nome & ".log")
                    If File.Exists(percorso) Then File.Delete(percorso)
                Catch exLog As Exception
                    RegistroErrori.Registra("ArchivioChatDisco.EliminaConversazione", exLog)
                End Try
            Next
        Catch ex As Exception
            RegistroErrori.Registra("ArchivioChatDisco.EliminaConversazione", ex)
            Throw
        End Try
    End Sub
End Class
