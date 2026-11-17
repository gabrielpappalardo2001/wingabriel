Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Linq
Imports System.Net
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json.Linq

' ============================================================================================
' MotoreChat - contatti, richieste, coda di invio con riprova e conferme di ricezione (WGChat)
' ============================================================================================
'
' Stesso comportamento di scripts\server\chat\sorgenti\motore.js, usato dal browser. Non conosce lo schermo né il disco: chi lo usa gli
' passa il relè (IReleChat) e l'archivio (IArchivioChat). Gira sul thread dell'interfaccia (le continuazioni tornano lì), quindi
' non servono blocchi.
' Nessun messaggio sta sul server: il relè inoltra soltanto, e solo a chi è collegato. Se l'altro non c'è il messaggio resta cifrato
' nella coda di questo dispositivo e si riprova finché non arriva la conferma (o per 14 giorni).
' ============================================================================================

Public Interface IReleChat
    ''' <summary>"ok" se il relè ha accettato il blocco, altrimenti "no" (il destinatario non è collegato, gettone sbagliato, coda piena...).</summary>
    Function InviaAsync(mb As String, tk As String, b64 As String) As Task(Of String)
    ''' <summary>Dice al relè su quali cassette si è collegati e aspetta fino a "attesa" secondi. Restituisce i blocchi: [{id, mb, b}].</summary>
    Function AscoltaAsync(cassette As JArray, ack As IList(Of String), attesa As Integer) As Task(Of JArray)
End Interface

Public Interface IArchivioChat
    Sub SalvaStato(stato As JObject)
    ''' <summary>Tutti i messaggi di una conversazione (anche quelli scritti da altri dispositivi dello stesso utente, se l'archivio è sincronizzato).</summary>
    Function CaricaMessaggi(conversazione As String) As JArray
    Sub SalvaMessaggio(conversazione As String, messaggio As JObject)
    Sub EliminaConversazione(conversazione As String)
End Interface

Public Class ContattoChat
    Public ReadOnly Dati As JObject
    Public Sub New(dati As JObject)
        Try
            Me.Dati = dati
        Catch ex As Exception
            RegistroErrori.Registra("ContattoChat.New", ex)
            Throw
        End Try
    End Sub
    Public ReadOnly Property Id As String
        Get
            Return CStr(Dati("id"))
        End Get
    End Property
    Public ReadOnly Property Nome As String
        Get
            Dim alias2 As String = CStr(Dati("alias"))
            Return If(String.IsNullOrEmpty(alias2), CStr(Dati("nome")), alias2)
        End Get
    End Property
    Public ReadOnly Property Stato As String
        Get
            Return CStr(Dati("stato"))
        End Get
    End Property
    Public ReadOnly Property NonLetti As Integer
        Get
            Return CInt(Dati("nonLetti"))
        End Get
    End Property
    Public ReadOnly Property Verificato As Boolean
        Get
            Return CBool(Dati("verificato"))
        End Get
    End Property
    ''' <summary>Il nome utente dei giochi, se il contatto è stato trovato per nome (il server dei giochi lo conosce).</summary>
    Public ReadOnly Property Utente As String
        Get
            Return If(Dati("utente") Is Nothing, "", CStr(Dati("utente")))
        End Get
    End Property
    Public ReadOnly Property Bloccato As Boolean
        Get
            Return Dati("bloccato") IsNot Nothing AndAlso Dati("bloccato").Type = JTokenType.Boolean AndAlso CBool(Dati("bloccato"))
        End Get
    End Property
End Class

Public Class MotoreChat

    Public Const ScadenzaCodaMs As Long = 14L * 86400000L
    Private Const MaxContatti As Integer = 200
    Private Const MaxVisti As Integer = 300
    Private Const RiprovaOkMs As Long = 30000

    Private ReadOnly _rele As IReleChat
    Private ReadOnly _archivio As IArchivioChat
    Private ReadOnly _messaggi As New Dictionary(Of String, JArray)()
    Private _ackPendenti As New List(Of String)()
    Private _invio As Boolean
    Private _attivo As Boolean
    Private _collegatoNotificato As Boolean?
    Private ReadOnly _identita As CoppiaChiavi

    Public ReadOnly Property Stato As JObject
    Public Event Evento(tipo As String, dati As Object)

    ''' <summary>Orologio in millisecondi (sostituibile nelle prove).</summary>
    Public Property Ora As Func(Of Long) = Function() DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    Public Shared Function StatoVuoto(nome As String) As JObject
        Try
            Dim id As CoppiaChiavi = CriptoChat.CreaIdentita()
            Return New JObject From {{"v", 1}, {"ident", New JObject From {{"priv", CriptoChat.InHex(id.Priv)}, {"pub", CriptoChat.InHex(id.Pub)}}},
                {"nome", PulisciNome(nome, "Io")}, {"inviti", New JArray()}, {"contatti", New JArray()}, {"coda", New JArray()}, {"bloccati", New JArray()}}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.StatoVuoto", ex)
            Throw
        End Try
    End Function

    Public Sub New(stato As JObject, rele As IReleChat, archivio As IArchivioChat)
        Try
            Me.Stato = stato
            _rele = rele
            _archivio = archivio
            _identita = New CoppiaChiavi With {.Priv = CriptoChat.DaHex(CStr(stato("ident")("priv"))), .Pub = CriptoChat.DaHex(CStr(stato("ident")("pub")))}
            If stato("bloccati") Is Nothing Then stato("bloccati") = New JArray()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.New", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Il nome nella chat è il nome utente dei giochi: uno solo per tutto.</summary>
    Public Sub ImpostaNomeUtente(nome As String)
        Try
            Dim n As String = PulisciNome(nome, NomeMio)
            If n <> NomeMio Then
                Stato("nome") = n
                Salva()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.ImpostaNomeUtente", ex)
            Throw
        End Try
    End Sub

    Private ReadOnly Property ElencoBloccatiGrezzo As JArray
        Get
            Return CType(Stato("bloccati"), JArray)
        End Get
    End Property

    Private Shared Function PulisciNome(n As String, Optional predefinito As String = "") As String
        Try
            Dim sb As New StringBuilder()
            For Each ch As Char In If(n, "")
                If Not Char.IsControl(ch) Then sb.Append(ch)
            Next
            Dim t As String = sb.ToString().Trim()
            If t.Length > 30 Then t = t.Substring(0, 30)
            Return If(t.Length = 0, predefinito, t)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.PulisciNome", ex)
            Throw
        End Try
    End Function

    Private Shared Function Esadecimale(s As String, n As Integer) As Boolean
        Try
            If s Is Nothing OrElse s.Length <> n Then Return False
            Return s.All(Function(c) Uri.IsHexDigit(c))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Esadecimale", ex)
            Throw
        End Try
    End Function

    Private Shared Function NuovoId() As String
        Try
            Return CriptoChat.InHex(CriptoChat.Casuali(8))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.NuovoId", ex)
            Throw
        End Try
    End Function

    Private Function Adesso() As Long
        Try
            Return Ora.Invoke()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Adesso", ex)
            Throw
        End Try
    End Function

    Private Sub Emetti(tipo As String, dati As Object)
        Try
            RaiseEvent Evento(tipo, dati)
        Catch exLog As Exception
            RegistroErrori.Registra("MotoreChat.Emetti", exLog)
            ' un ascoltatore che sbaglia non ferma il motore
        End Try
    End Sub

    Private Sub Salva()
        Try
            _archivio.SalvaStato(Stato)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Salva", ex)
            Throw
        End Try
    End Sub

    Public ReadOnly Property NomeMio As String
        Get
            Return CStr(Stato("nome"))
        End Get
    End Property

    Public ReadOnly Property Collegato As Boolean
        Get
            Return _collegatoNotificato.GetValueOrDefault(False)
        End Get
    End Property

    Public ReadOnly Property Inviti As JArray
        Get
            Return CType(Stato("inviti"), JArray)
        End Get
    End Property

    Private ReadOnly Property Coda As JArray
        Get
            Return CType(Stato("coda"), JArray)
        End Get
    End Property

    Public Function Contatti() As List(Of ContattoChat)
        Try
            Return CType(Stato("contatti"), JArray).Select(Function(k) New ContattoChat(CType(k, JObject))).ToList()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Contatti", ex)
            Throw
        End Try
    End Function

    Public Function Contatto(cid As String) As ContattoChat
        Try
            Dim k As JObject = TrovaContatto(cid)
            Return If(k Is Nothing, Nothing, New ContattoChat(k))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Contatto", ex)
            Throw
        End Try
    End Function

    Private Function TrovaContatto(cid As String) As JObject
        Try
            Return CType(Stato("contatti"), JArray).OfType(Of JObject)().FirstOrDefault(Function(k) CStr(k("id")) = cid)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.TrovaContatto", ex)
            Throw
        End Try
    End Function

    Public Sub CambiaNome(nome As String)
        Try
            Stato("nome") = PulisciNome(nome, NomeMio)
            Salva()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.CambiaNome", ex)
            Throw
        End Try
    End Sub

    ' ---------------------------------------------------------------- inviti e contatti
    Public Function NuovoInvito() As Invito
        Try
            Dim inv As Invito = CriptoChat.CreaInvito(_identita)
            Inviti.Add(New JObject From {{"codice", inv.Codice}, {"mb", inv.Mb}, {"rd", inv.Rd}, {"tk", inv.Tk}, {"creato", Adesso()}})
            While Inviti.Count > 5
                Inviti.RemoveAt(0)
            End While
            Salva()
            Return inv
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.NuovoInvito", ex)
            Throw
        End Try
    End Function

    Public Sub EliminaInvito(mb As String)
        Try
            For Each i As JToken In Inviti.Where(Function(x) CStr(x("mb")) = mb).ToList()
                i.Remove()
            Next
            Salva()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.EliminaInvito", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Aggiunge un contatto con il suo codice: parte la richiesta, che sarà consegnata appena l'altro è collegato.</summary>
    Public Function AggiungiContatto(codiceTesto As String, Optional utente As String = "") As ContattoChat
        Try
            If CType(Stato("contatti"), JArray).Count >= MaxContatti Then Throw New InvalidOperationException("Hai già troppi contatti.")
            Dim codice As CodiceLetto = CriptoChat.LeggiCodice(codiceTesto)
            If CriptoChat.InHex(codice.IkPub) = CriptoChat.InHex(_identita.Pub) Then Throw New InvalidOperationException("Questo è il tuo codice: devi usare quello di un'altra persona.")
            Dim ikHex As String = CriptoChat.InHex(codice.IkPub)
            If ElencoBloccatiGrezzo.OfType(Of JObject)().Any(Function(b) CStr(b("ik")) = ikHex) Then Throw New InvalidOperationException("Hai bloccato questa persona: prima sbloccala.")
            Dim esistente As JObject = CType(Stato("contatti"), JArray).OfType(Of JObject)().FirstOrDefault(Function(k) CStr(k("ikRemota")) = ikHex)
            If esistente IsNot Nothing Then Throw New InvalidOperationException("Hai già questo contatto: " & New ContattoChat(esistente).Nome & ".")
            Dim mia As New JObject From {{"mb", CriptoChat.InHex(CriptoChat.Casuali(16))}, {"rd", CriptoChat.InHex(CriptoChat.Casuali(32))}, {"tk", CriptoChat.InHex(CriptoChat.Casuali(32))}}
            Dim r As RispostaStretta = CriptoChat.IniziaStretta(_identita, codice, New JObject From {{"n", NomeMio}, {"mb", CStr(mia("mb"))}, {"tk", CStr(mia("tk"))}})
            Dim k2 As JObject = NuovoContatto(If(String.IsNullOrWhiteSpace(utente), "Nuovo contatto", PulisciNome(utente, "Nuovo contatto")), "invitato", ikHex, mia, New JObject From {{"mb", codice.Mb}, {"tk", codice.Tk}})
            k2("utente") = PulisciNome(utente)
            k2("stretta") = r.Stato
            CType(Stato("contatti"), JArray).Add(k2)
            AccodaBlocco(k2, "stretta", r.Blocco, Nothing)
            Salva()
            Dim c As New ContattoChat(k2)
            Emetti("contatto", c)
            Return c
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.AggiungiContatto", ex)
            Throw
        End Try
    End Function

    Private Function NuovoContatto(nome As String, stato2 As String, ikHex As String, mia As JObject, verso As JObject) As JObject
        Try
            Return New JObject From {{"id", NuovoId()}, {"nome", nome}, {"alias", ""}, {"stato", stato2}, {"ikRemota", ikHex}, {"mia", mia}, {"verso", verso},
                {"stretta", Nothing}, {"rstato", Nothing}, {"sessione", Nothing}, {"verificato", False}, {"visti", New JObject()}, {"nonLetti", 0}, {"creato", Adesso()}, {"conv", ""}, {"utente", ""}, {"bloccato", False}}
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.NuovoContatto", ex)
            Throw
        End Try
    End Function

    Public Sub Accetta(cid As String)
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k Is Nothing OrElse CStr(k("stato")) <> "richiesta" Then Throw New InvalidOperationException("Nessuna richiesta da accettare.")
            Dim mia As New JObject From {{"mb", CriptoChat.InHex(CriptoChat.Casuali(16))}, {"rd", CriptoChat.InHex(CriptoChat.Casuali(32))}, {"tk", CriptoChat.InHex(CriptoChat.Casuali(32))}}
            k("mia") = mia
            Dim r As RispostaStretta = CriptoChat.RispondiStretta(CType(k("rstato"), JObject), New JObject From {{"n", NomeMio}, {"mb", CStr(mia("mb"))}, {"tk", CStr(mia("tk"))}})
            k("rstato") = r.Stato
            k("stato") = "accettato"
            k("conv") = Conversazione(CStr(k("nome")))
            AccodaBlocco(k, "stretta2", r.Blocco, Nothing)
            Salva()
            Emetti("contatto", New ContattoChat(k))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Accetta", ex)
            Throw
        End Try
    End Sub

    Public Sub Rifiuta(cid As String)
        Try
            Elimina(cid)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Rifiuta", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Blocca una persona: non riceve più niente da lei, le sue richieste vengono ignorate e non le arriva alcun segnale (i suoi messaggi
    ''' restano "in attesa", come se non fossi collegato). Restituisce il nome utente, se si conosce, per bloccarlo anche nei giochi.
    ''' </summary>
    Public Function Blocca(cid As String) As String
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k Is Nothing Then Return ""
            Dim ik As String = CStr(k("ikRemota"))
            If Not ElencoBloccatiGrezzo.OfType(Of JObject)().Any(Function(b) CStr(b("ik")) = ik) Then
                ElencoBloccatiGrezzo.Add(New JObject From {{"ik", ik}, {"nome", New ContattoChat(k).Nome}, {"utente", New ContattoChat(k).Utente}})
            End If
            k("bloccato") = True
            For Each q As JToken In Coda.Where(Function(x) CStr(x("cid")) = cid).ToList()
                q.Remove()
            Next
            Salva()
            Emetti("contatto", New ContattoChat(k))
            Return New ContattoChat(k).Utente
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Blocca", ex)
            Throw
        End Try
    End Function

    ''' <summary>Sblocca per chiave d'identità (resta valido anche se il contatto era stato eliminato). Restituisce il nome utente, se si conosce.</summary>
    Public Function Sblocca(ik As String) As String
        Try
            Dim b As JObject = ElencoBloccatiGrezzo.OfType(Of JObject)().FirstOrDefault(Function(x) CStr(x("ik")) = ik)
            Dim utente As String = If(b Is Nothing, "", CStr(b("utente")))
            If b IsNot Nothing Then b.Remove()
            Dim k As JObject = CType(Stato("contatti"), JArray).OfType(Of JObject)().FirstOrDefault(Function(x) CStr(x("ikRemota")) = ik)
            If k IsNot Nothing Then k("bloccato") = False
            Salva()
            Emetti("contatto", If(k Is Nothing, Nothing, New ContattoChat(k)))
            Return utente
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Sblocca", ex)
            Throw
        End Try
    End Function

    Public Function ElencoBloccati() As List(Of JObject)
        Try
            Return ElencoBloccatiGrezzo.OfType(Of JObject)().ToList()
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.ElencoBloccati", ex)
            Throw
        End Try
    End Function

    Public Sub Elimina(cid As String)
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k Is Nothing Then Return
            Dim conv As String = CStr(k("conv"))
            k.Remove()
            For Each q As JToken In Coda.Where(Function(x) CStr(x("cid")) = cid).ToList()
                q.Remove()
            Next
            _messaggi.Remove(cid)
            Salva()
            If Not String.IsNullOrEmpty(conv) Then _archivio.EliminaConversazione(conv)
            Emetti("contatto", Nothing)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Elimina", ex)
            Throw
        End Try
    End Sub

    Public Sub SegnaVerificato(cid As String, Optional valore As Boolean = True)
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k Is Nothing Then Return
            k("verificato") = valore
            Salva()
            Emetti("contatto", New ContattoChat(k))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.SegnaVerificato", ex)
            Throw
        End Try
    End Sub

    Public Sub Rinomina(cid As String, alias2 As String)
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k Is Nothing Then Return
            k("alias") = PulisciNome(alias2)
            Salva()
            Emetti("contatto", New ContattoChat(k))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Rinomina", ex)
            Throw
        End Try
    End Sub

    Public Function NumeroDiSicurezza(cid As String) As String
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k Is Nothing Then Return ""
            Return CriptoChat.NumeroDiSicurezza(_identita.Pub, CriptoChat.DaHex(CStr(k("ikRemota"))))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.NumeroDiSicurezza", ex)
            Throw
        End Try
    End Function

    Public Sub SegnaLetti(cid As String)
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k IsNot Nothing AndAlso CInt(k("nonLetti")) > 0 Then
                k("nonLetti") = 0
                Salva()
                Emetti("contatto", New ContattoChat(k))
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.SegnaLetti", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Nome con cui si riconosce la stessa conversazione su più dispositivi (cartella sincronizzata): quello dichiarato dall'altro.</summary>
    Private Shared Function Conversazione(nome As String) As String
        Try
            Dim sb As New StringBuilder()
            For Each ch As Char In nome.ToLowerInvariant()
                If Char.IsLetterOrDigit(ch) Then sb.Append(ch)
            Next
            Dim t As String = sb.ToString()
            Return If(t.Length = 0, "contatto", t)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Conversazione", ex)
            Throw
        End Try
    End Function

    ' ---------------------------------------------------------------- messaggi
    Public Function ElencoMessaggi(cid As String) As JArray
        Try
            Dim lista As JArray = Nothing
            If _messaggi.TryGetValue(cid, lista) Then Return lista
            Dim k As JObject = TrovaContatto(cid)
            lista = New JArray()
            If k IsNot Nothing AndAlso Not String.IsNullOrEmpty(CStr(k("conv"))) Then
                Try
                    lista = _archivio.CaricaMessaggi(CStr(k("conv")))
                Catch exLog As Exception
                    RegistroErrori.Registra("MotoreChat.ElencoMessaggi", exLog)
                    lista = New JArray()
                End Try
            End If
            _messaggi(cid) = lista
            Return lista
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.ElencoMessaggi", ex)
            Throw
        End Try
    End Function

    Private Sub SalvaMessaggio(k As JObject, m As JObject)
        Try
            Dim conv As String = CStr(k("conv"))
            If Not String.IsNullOrEmpty(conv) Then _archivio.SalvaMessaggio(conv, m)
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.SalvaMessaggio", ex)
            Throw
        End Try
    End Sub

    Private Sub AccodaBlocco(k As JObject, tipo As String, blob As Byte(), mid As String)
        Try
            Coda.Add(New JObject From {{"cid", CStr(k("id"))}, {"tipo", tipo}, {"mid", mid}, {"b64", Convert.ToBase64String(blob)}, {"verso", k("verso").DeepClone()},
                {"tentativi", 0}, {"prossimo", 0L}, {"creato", Adesso()}})
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.AccodaBlocco", ex)
            Throw
        End Try
    End Sub

    Private Sub AccodaMessaggio(k As JObject, oggetto As JObject, tipo As String)
        Try
            Dim blob As Byte() = CriptoChat.Cifra(CType(k("sessione"), JObject), CriptoChat.TestoAByte(oggetto))
            AccodaBlocco(k, tipo, blob, If(oggetto("id") Is Nothing, Nothing, CStr(oggetto("id"))))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.AccodaMessaggio", ex)
            Throw
        End Try
    End Sub

    ''' <summary>Scrive un messaggio: si cifra subito e resta in coda finché l'altro non lo riceve.</summary>
    Public Function Scrivi(cid As String, testo As String) As JObject
        Try
            Dim k As JObject = TrovaContatto(cid)
            If k IsNot Nothing AndAlso New ContattoChat(k).Bloccato Then Throw New InvalidOperationException("Hai bloccato questa persona: sbloccala per scriverle.")
            If k Is Nothing OrElse k("sessione") Is Nothing OrElse k("sessione").Type = JTokenType.Null OrElse k("sessione")("cks") Is Nothing OrElse k("sessione")("cks").Type = JTokenType.Null Then
                Throw New InvalidOperationException("Questa conversazione non è ancora pronta: aspetta che l'altro si colleghi.")
            End If
            testo = (testo & "").Replace(vbCrLf, vbLf).Trim()
            If testo.Length = 0 Then Throw New InvalidOperationException("Scrivi qualcosa.")
            If testo.Length > CriptoChat.MassimoTesto Then Throw New InvalidOperationException("Il messaggio è troppo lungo: al massimo " & CriptoChat.MassimoTesto & " caratteri.")
            Dim id As String = NuovoId()
            AccodaMessaggio(k, New JObject From {{"t", "m"}, {"id", id}, {"x", testo}}, "msg")
            Dim m As New JObject From {{"id", id}, {"dir", "out"}, {"ts", Adesso()}, {"x", testo}, {"st", "attesa"}}
            ElencoMessaggi(cid).Add(m)
            Salva()
            SalvaMessaggio(k, m)
            Emetti("messaggio", New Tuple(Of String, JObject)(cid, m))
            Sollecita()
            Return m
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Scrivi", ex)
            Throw
        End Try
    End Function

    ''' <summary>Fa partire subito la consegna della coda (per esempio dopo aver scritto), senza aspettare il prossimo giro.</summary>
    Public Async Sub Sollecita()
        Try
            Await InviaCodaAsync()
        Catch exLog As Exception
            RegistroErrori.Registra("MotoreChat.Sollecita", exLog)
        End Try
    End Sub

    ''' <summary>Prova a consegnare quello che è in coda. Restituisce quanti blocchi sono stati accettati dal relè.</summary>
    Public Async Function InviaCodaAsync() As Task(Of Integer)
        Try
            If _invio Then Return 0
            _invio = True
            Try
                Return Await InviaCodaInternoAsync()
            Finally
                _invio = False
            End Try
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.InviaCodaAsync", ex)
            Throw
        End Try
    End Function

    Private Async Function InviaCodaInternoAsync() As Task(Of Integer)
        Try
            Dim adesso2 As Long = Adesso()
            Dim accettati As Integer = 0
            Dim cambiato As Boolean = False
            For Each q As JObject In Coda.OfType(Of JObject)().ToList()
                Dim k As JObject = TrovaContatto(CStr(q("cid")))
                If k Is Nothing OrElse New ContattoChat(k).Bloccato Then
                    q.Remove()
                    cambiato = True
                    Continue For
                End If
                If adesso2 - CLng(q("creato")) > ScadenzaCodaMs Then
                    q.Remove()
                    Dim m As JObject = ElencoMessaggi(CStr(q("cid"))).OfType(Of JObject)().FirstOrDefault(Function(x) q("mid") IsNot Nothing AndAlso CStr(x("id")) = CStr(q("mid")))
                    If m IsNot Nothing AndAlso CStr(m("st")) <> "consegnato" Then
                        m("st") = "scaduto"
                        SalvaMessaggio(k, m)
                        Emetti("messaggio", New Tuple(Of String, JObject)(CStr(q("cid")), m))
                    End If
                    cambiato = True
                    Continue For
                End If
                If CLng(q("prossimo")) > adesso2 Then Continue For
                Dim r As String
                Try
                    r = Await _rele.InviaAsync(CStr(q("verso")("mb")), CStr(q("verso")("tk")), CStr(q("b64")))
                Catch exLog As Exception
                    RegistroErrori.Registra("MotoreChat.InviaCodaInternoAsync", exLog)
                    r = "errore"
                End Try
                If r = "ok" Then
                    accettati += 1
                    If CStr(q("tipo")) = "ack" Then
                        q.Remove()
                    Else
                        q("prossimo") = adesso2 + RiprovaOkMs
                        q("tentativi") = 0
                        Dim m As JObject = ElencoMessaggi(CStr(q("cid"))).OfType(Of JObject)().FirstOrDefault(Function(x) q("mid") IsNot Nothing AndAlso CStr(x("id")) = CStr(q("mid")))
                        If m IsNot Nothing AndAlso CStr(m("st")) = "attesa" Then
                            m("st") = "inviato"
                            SalvaMessaggio(k, m)
                            Emetti("messaggio", New Tuple(Of String, JObject)(CStr(q("cid")), m))
                        End If
                    End If
                Else
                    q("tentativi") = CInt(q("tentativi")) + 1
                    q("prossimo") = adesso2 + CLng(Math.Min(60000, 5000 * Math.Pow(2, Math.Min(CInt(q("tentativi")), 4))))
                End If
                cambiato = True
            Next
            If cambiato Then Salva()
            Return accettati
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.InviaCodaInternoAsync", ex)
            Throw
        End Try
    End Function

    ' ---------------------------------------------------------------- ricezione
    Public Function Cassette() As JArray
        Try
            Dim r As New JArray()
            For Each i As JObject In Inviti.OfType(Of JObject)()
                r.Add(New JObject From {{"mb", CStr(i("mb"))}, {"rd", CStr(i("rd"))}, {"th", CriptoChat.ShaHex(CriptoChat.DaHex(CStr(i("tk"))))}})
            Next
            For Each k As JObject In CType(Stato("contatti"), JArray).OfType(Of JObject)()
                If k("mia") IsNot Nothing AndAlso k("mia").Type <> JTokenType.Null AndAlso CStr(k("stato")) <> "richiesta" AndAlso Not New ContattoChat(k).Bloccato Then
                    r.Add(New JObject From {{"mb", CStr(k("mia")("mb"))}, {"rd", CStr(k("mia")("rd"))}, {"th", CriptoChat.ShaHex(CriptoChat.DaHex(CStr(k("mia")("tk"))))}})
                End If
            Next
            Return r
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Cassette", ex)
            Throw
        End Try
    End Function

    ''' <summary>Una richiesta all'ascolto: chiede al relè se c'è qualcosa e lo elabora. Restituisce quanti blocchi sono arrivati.</summary>
    Public Async Function AscoltaAsync(attesa As Integer) As Task(Of Integer)
        Try
            Dim cassette2 As JArray = Cassette()
            If cassette2.Count = 0 Then Return 0
            Dim ack As List(Of String) = _ackPendenti
            _ackPendenti = New List(Of String)()
            Dim blocchi As JArray
            Try
                blocchi = Await _rele.AscoltaAsync(cassette2, ack, attesa)
            Catch exLog As Exception
                RegistroErrori.Registra("MotoreChat.AscoltaAsync", exLog)
                _ackPendenti.AddRange(ack)
                Throw
            End Try
            For Each b As JObject In blocchi.OfType(Of JObject)()
                Try
                    GestisciBlocco(b)
                Catch exLog As Exception
                    RegistroErrori.Registra("MotoreChat.AscoltaAsync", exLog)
                    ' un blocco rovinato non ferma gli altri
                End Try
                _ackPendenti.Add(CStr(b("id")))
            Next
            If blocchi.Count > 0 Then Salva()
            Return blocchi.Count
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.AscoltaAsync", ex)
            Throw
        End Try
    End Function

    Private Sub GestisciBlocco(b As JObject)
        Try
            Dim blob As Byte()
            Try
                blob = Convert.FromBase64String(CStr(b("b")))
            Catch exLog As Exception
                RegistroErrori.Registra("MotoreChat.GestisciBlocco", exLog)
                Return
            End Try
            Dim tipo As String = CriptoChat.TipoBlocco(blob)
            Dim mb As String = CStr(b("mb"))
            If Inviti.OfType(Of JObject)().Any(Function(i) CStr(i("mb")) = mb) Then
                If tipo = "stretta1" Then NuovaRichiesta(blob)
                Return
            End If
            Dim k As JObject = CType(Stato("contatti"), JArray).OfType(Of JObject)().FirstOrDefault(Function(x) x("mia") IsNot Nothing AndAlso x("mia").Type <> JTokenType.Null AndAlso CStr(x("mia")("mb")) = mb)
            If k Is Nothing OrElse New ContattoChat(k).Bloccato Then Return
            Dim st As String = CStr(k("stato"))
            If tipo = "stretta2" AndAlso st = "invitato" Then
                CompletaStretta(k, blob)
            ElseIf tipo = "messaggio" AndAlso (st = "attivo" OrElse st = "accettato") Then
                NuovoMessaggio(k, blob)
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.GestisciBlocco", ex)
            Throw
        End Try
    End Sub

    Private Sub NuovaRichiesta(blob As Byte())
        Try
            If CType(Stato("contatti"), JArray).Count >= MaxContatti Then Return
            Dim r As RichiestaRicevuta
            Try
                r = CriptoChat.RiceviStretta1(_identita, blob)
            Catch exLog As Exception
                RegistroErrori.Registra("MotoreChat.NuovaRichiesta", exLog)
                Return
            End Try
            Dim ikHex As String = CriptoChat.InHex(r.IkI)
            If ikHex = CriptoChat.InHex(_identita.Pub) Then Return
            If ElencoBloccatiGrezzo.OfType(Of JObject)().Any(Function(b) CStr(b("ik")) = ikHex) Then Return     ' una persona bloccata non riesce nemmeno a chiedere
            Dim mbR As String = CStr(r.Dati("mb")), tkR As String = CStr(r.Dati("tk"))
            If Not Esadecimale(mbR, 32) OrElse Not Esadecimale(tkR, 64) Then Return
            If CType(Stato("contatti"), JArray).OfType(Of JObject)().Any(Function(x) CStr(x("ikRemota")) = ikHex) Then Return
            Dim k As JObject = NuovoContatto(PulisciNome(CStr(r.Dati("n")), "Sconosciuto"), "richiesta", ikHex, Nothing, New JObject From {{"mb", mbR}, {"tk", tkR}})
            k("rstato") = r.Stato
            CType(Stato("contatti"), JArray).Add(k)
            Emetti("richiesta", New ContattoChat(k))
            Emetti("contatto", New ContattoChat(k))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.NuovaRichiesta", ex)
            Throw
        End Try
    End Sub

    Private Sub CompletaStretta(k As JObject, blob As Byte())
        Try
            Dim r As StrettaCompletata
            Try
                r = CriptoChat.FinisciStretta(_identita, CType(k("stretta"), JObject), blob)
            Catch exLog As Exception
                RegistroErrori.Registra("MotoreChat.CompletaStretta", exLog)
                Return
            End Try
            Dim mbR As String = CStr(r.Dati("mb")), tkR As String = CStr(r.Dati("tk"))
            If Not Esadecimale(mbR, 32) OrElse Not Esadecimale(tkR, 64) Then Return
            k("sessione") = r.Sessione
            k("verso") = New JObject From {{"mb", mbR}, {"tk", tkR}}
            k("nome") = PulisciNome(CStr(r.Dati("n")), CStr(k("nome")))
            k("conv") = Conversazione(CStr(k("nome")))
            k("stretta") = Nothing
            k("stato") = "attivo"
            For Each q As JToken In Coda.Where(Function(x) CStr(x("cid")) = CStr(k("id")) AndAlso CStr(x("tipo")) = "stretta").ToList()
                q.Remove()
            Next
            AccodaMessaggio(k, New JObject From {{"t", "k"}, {"id", NuovoId()}}, "k")
            Emetti("contatto", New ContattoChat(k))
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.CompletaStretta", ex)
            Throw
        End Try
    End Sub

    Private Sub NuovoMessaggio(k As JObject, blob As Byte())
        Try
            Dim h As String = CriptoChat.ShaHex(blob).Substring(0, 32)
            Dim testo As Byte()
            Try
                If k("sessione") Is Nothing OrElse k("sessione").Type = JTokenType.Null Then
                    Dim r As PrimoMessaggio = CriptoChat.AccettaPrimoMessaggio(CType(k("rstato"), JObject), blob)
                    k("sessione") = r.Sessione
                    k("rstato") = Nothing
                    k("stato") = "attivo"
                    For Each q As JToken In Coda.Where(Function(x) CStr(x("cid")) = CStr(k("id")) AndAlso CStr(x("tipo")) = "stretta2").ToList()
                        q.Remove()
                    Next
                    testo = r.Testo
                    Emetti("contatto", New ContattoChat(k))
                Else
                    testo = CriptoChat.Decifra(CType(k("sessione"), JObject), blob)
                End If
            Catch exLog As Exception
                RegistroErrori.Registra("MotoreChat.NuovoMessaggio", exLog)
                ' blocco già visto (rimandato perché la conferma non è arrivata): si conferma di nuovo
                Dim visti As JObject = CType(k("visti"), JObject)
                If visti(h) IsNot Nothing AndAlso Not String.IsNullOrEmpty(CStr(visti(h))) AndAlso k("sessione") IsNot Nothing AndAlso k("sessione").Type <> JTokenType.Null AndAlso k("sessione")("cks").Type <> JTokenType.Null Then
                    AccodaMessaggio(k, New JObject From {{"t", "a"}, {"id", CStr(visti(h))}}, "ack")
                End If
                Return
            End Try
            Dim o As JObject
            Try
                o = CriptoChat.ByteATesto(testo)
            Catch exLog As Exception
                RegistroErrori.Registra("MotoreChat.NuovoMessaggio", exLog)
                Return
            End Try
            Dim idMsg As String = If(o("id") Is Nothing, "", CStr(o("id")))
            Dim vistiMappa As JObject = CType(k("visti"), JObject)
            vistiMappa(h) = idMsg
            While vistiMappa.Count > MaxVisti
                CType(vistiMappa.Properties().First(), JProperty).Remove()
            End While
            Dim t As String = If(o("t") Is Nothing, "", CStr(o("t")))
            Dim cid As String = CStr(k("id"))
            If t = "m" AndAlso o("x") IsNot Nothing AndAlso Esadecimale(idMsg, 16) Then
                Dim lista As JArray = ElencoMessaggi(cid)
                If Not lista.OfType(Of JObject)().Any(Function(m) CStr(m("id")) = idMsg) Then
                    Dim x As String = CStr(o("x"))
                    If x.Length > CriptoChat.MassimoTesto Then x = x.Substring(0, CriptoChat.MassimoTesto)
                    Dim m As New JObject From {{"id", idMsg}, {"dir", "in"}, {"ts", Adesso()}, {"x", x}, {"st", "consegnato"}}
                    lista.Add(m)
                    k("nonLetti") = CInt(k("nonLetti")) + 1
                    SalvaMessaggio(k, m)
                    Emetti("messaggio", New Tuple(Of String, JObject)(cid, m))
                End If
                AccodaMessaggio(k, New JObject From {{"t", "a"}, {"id", idMsg}}, "ack")
            ElseIf t = "k" AndAlso Esadecimale(idMsg, 16) Then
                AccodaMessaggio(k, New JObject From {{"t", "a"}, {"id", idMsg}}, "ack")
            ElseIf t = "a" AndAlso Esadecimale(idMsg, 16) Then
                Dim m As JObject = ElencoMessaggi(cid).OfType(Of JObject)().FirstOrDefault(Function(x) CStr(x("id")) = idMsg)
                If m IsNot Nothing AndAlso CStr(m("st")) <> "consegnato" Then
                    m("st") = "consegnato"
                    SalvaMessaggio(k, m)
                    Emetti("messaggio", New Tuple(Of String, JObject)(cid, m))
                End If
                For Each q As JToken In Coda.Where(Function(x) CStr(x("cid")) = cid AndAlso x("mid") IsNot Nothing AndAlso CStr(x("mid")) = idMsg).ToList()
                    q.Remove()
                Next
            End If
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.NuovoMessaggio", ex)
            Throw
        End Try
    End Sub

    ' ---------------------------------------------------------------- ciclo
    ''' <summary>Resta collegato: ascolta e consegna finché non si chiama Ferma().</summary>
    Public Async Function AvviaAsync(Optional attesa As Integer = 15) As Task
        Try
            _attivo = True
            Dim errori As Integer = 0
            While _attivo
                Dim pausa As Integer = 0
                Try
                    Await InviaCodaAsync()
                    Dim prossima As Long = Long.MaxValue
                    For Each q As JObject In Coda.OfType(Of JObject)()
                        prossima = Math.Min(prossima, CLng(q("prossimo")))
                    Next
                    Dim secondi As Integer = attesa
                    If prossima <> Long.MaxValue Then secondi = CInt(Math.Max(1, Math.Min(attesa, Math.Ceiling((prossima - Adesso()) / 1000.0))))
                    Dim n As Integer = Await AscoltaAsync(secondi)
                    If Not _collegatoNotificato.GetValueOrDefault(False) Then
                        _collegatoNotificato = True
                        Emetti("collegamento", True)
                    End If
                    errori = 0
                    If n = 0 AndAlso Cassette().Count = 0 Then Await Task.Delay(1000)
                Catch ex As OperationCanceledException
                    Exit While
                Catch exLog As Exception
                    RegistroErrori.Registra("MotoreChat.AvviaAsync", exLog)
                    errori += 1
                    If _collegatoNotificato.GetValueOrDefault(True) Then
                        _collegatoNotificato = False
                        Emetti("collegamento", False)
                    End If
                    pausa = Math.Min(30000, 2000 * errori)
                End Try
                If pausa > 0 Then Await Task.Delay(pausa)
            End While
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.AvviaAsync", ex)
            Throw
        End Try
    End Function

    Public Sub Ferma()
        Try
            _attivo = False
        Catch ex As Exception
            RegistroErrori.Registra("MotoreChat.Ferma", ex)
            Throw
        End Try
    End Sub

End Class
