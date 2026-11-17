Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGContatti
Imports WGDati
Imports WGPlugin

' ============================================================================================
' Le protezioni dei servizi per i plugin:
'   - limiti (quante cose al minuto) e interruttore automatico: troppi errori o troppe richieste e il plugin si disattiva da solo;
'   - file propri del plugin, solo nella sua cartella dei dati;
'   - internet, solo https verso siti pubblici, con limiti;
'   - dati di WinGabriel (note, rubrica, agenda) solo con il permesso dell'utente, che si può revocare dalle impostazioni.
' Il plugin non ha altro modo di arrivare a questi dati: il suo codice è controllato prima del caricamento (VerificaCodicePlugin).
' ============================================================================================

Partial Friend NotInheritable Class ServiziPlugin

#Region "Limiti e interruttore automatico"

    Private Const FinestraSecondi As Integer = 60
    Private Const MassimoErroriInFinestra As Integer = 3
    Private Const MassimoInfrazioniInDueMinuti As Integer = 10
    Private Const SogliaAzioneLentaMs As Integer = 10000

    Private ReadOnly _bloccoLimiti As New Object()
    Private ReadOnly _erroriRecenti As New Queue(Of DateTime)()
    Private ReadOnly _infrazioniRecenti As New Queue(Of DateTime)()
    Private ReadOnly _eventiRecenti As New Dictionary(Of String, Queue(Of DateTime))(StringComparer.Ordinal)
    Private _fermato As Boolean

    Private Shared Sub Pulisci(coda As Queue(Of DateTime), secondi As Integer)
        Dim limite As DateTime = DateTime.UtcNow.AddSeconds(-secondi)
        While coda.Count > 0 AndAlso coda.Peek() < limite
            coda.Dequeue()
        End While
    End Sub

    ''' <summary>Un errore del codice del plugin: con troppi errori in poco tempo il plugin si disattiva da solo.</summary>
    Friend Sub ContaErrore()
        Try
            Dim fermare As Boolean = False
            SyncLock _bloccoLimiti
                Pulisci(_erroriRecenti, FinestraSecondi)
                _erroriRecenti.Enqueue(DateTime.UtcNow)
                If _erroriRecenti.Count >= MassimoErroriInFinestra AndAlso Not _fermato Then
                    _fermato = True
                    fermare = True
                End If
            End SyncLock
            If fermare Then WGGestorePlugin.DisattivaPerProblema(_id, TestiPlugin.DisattivatoPerErrori)
        Catch ex As Exception
            log.ScriviLog("ServiziPlugin.ContaErrore: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Un errore del plugin: nel registro e conteggiato per l'interruttore automatico.</summary>
    Friend Sub ErrorePlugin(origine As String, ex As Exception)
        ScriviErrore(origine, ex)
        ContaErrore()
    End Sub

    ''' <summary>
    ''' Vero se il plugin può fare un'altra cosa di quella categoria (al massimo "massimo" al minuto). Se supera il limite la richiesta
    ''' si scarta; e se continua a insistere (troppe infrazioni in poco tempo) il plugin si disattiva.
    ''' </summary>
    Friend Function Consenti(categoria As String, massimo As Integer) As Boolean
        Try
            Dim ammesso As Boolean = True
            Dim fermare As Boolean = False
            SyncLock _bloccoLimiti
                Dim coda As Queue(Of DateTime) = Nothing
                If Not _eventiRecenti.TryGetValue(categoria, coda) Then
                    coda = New Queue(Of DateTime)()
                    _eventiRecenti(categoria) = coda
                End If
                Pulisci(coda, FinestraSecondi)
                If coda.Count >= massimo Then
                    ammesso = False
                    Pulisci(_infrazioniRecenti, 120)
                    _infrazioniRecenti.Enqueue(DateTime.UtcNow)
                    If _infrazioniRecenti.Count >= MassimoInfrazioniInDueMinuti AndAlso Not _fermato Then
                        _fermato = True
                        fermare = True
                    End If
                Else
                    coda.Enqueue(DateTime.UtcNow)
                End If
            End SyncLock
            If fermare Then WGGestorePlugin.DisattivaPerProblema(_id, TestiPlugin.DisattivatoPerAbuso)
            Return ammesso
        Catch ex As Exception
            log.ScriviLog("ServiziPlugin.Consenti: " & ex.Message)
            Return False
        End Try
    End Function

#End Region

#Region "File propri del plugin"

    Private Const MassimoFileByte As Integer = 1024 * 1024
    Private Const MassimoCartellaByte As Long = 5L * 1024 * 1024
    Private Shared ReadOnly NomeFileValido As New Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)
    Private Shared ReadOnly NomiRiservati As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"}

    ''' <summary>Il percorso del file nella cartella dei dati, oppure vuoto se il nome non è semplice e sicuro.</summary>
    Private Function PercorsoFileDati(nome As String) As String
        If String.IsNullOrEmpty(nome) OrElse Not NomeFileValido.IsMatch(nome) OrElse nome.EndsWith(".", StringComparison.Ordinal) Then Return ""
        Dim senzaEstensione As String = nome
        Dim punto As Integer = nome.IndexOf("."c)
        If punto > 0 Then senzaEstensione = nome.Substring(0, punto)
        If NomiRiservati.Contains(senzaEstensione) Then Return ""
        Dim cartella As String = CartellaDati
        If cartella = "" Then Return ""
        Dim base As String = Path.GetFullPath(cartella).TrimEnd("\"c) & "\"
        Dim percorso As String = Path.GetFullPath(Path.Combine(cartella, nome))
        If Not percorso.StartsWith(base, StringComparison.OrdinalIgnoreCase) Then Return ""
        Return percorso
    End Function

    Public Function LeggiFileDati(nome As String) As Byte() Implements IWGServiziPlugin.LeggiFileDati
        Try
            If Not _attivo Then Return Nothing
            Dim percorso As String = PercorsoFileDati(nome)
            If percorso = "" OrElse Not System.IO.File.Exists(percorso) Then Return Nothing
            If New FileInfo(percorso).Length > MassimoFileByte Then Return Nothing
            Return System.IO.File.ReadAllBytes(percorso)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.LeggiFileDati", ex)
            Return Nothing
        End Try
    End Function

    Public Sub ScriviFileDati(nome As String, dati As Byte()) Implements IWGServiziPlugin.ScriviFileDati
        Try
            If Not _attivo OrElse dati Is Nothing OrElse dati.Length > MassimoFileByte Then Return
            Dim percorso As String = PercorsoFileDati(nome)
            If percorso = "" OrElse Not Consenti("file", 60) Then Return
            Dim totale As Long = 0
            For Each altro As String In Directory.GetFiles(CartellaDati)
                If Not String.Equals(altro, percorso, StringComparison.OrdinalIgnoreCase) Then totale += New FileInfo(altro).Length
            Next
            If totale + dati.Length > MassimoCartellaByte Then Return
            System.IO.File.WriteAllBytes(percorso, dati)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ScriviFileDati", ex)
        End Try
    End Sub

    Public Function ElencaFileDati() As IList(Of String) Implements IWGServiziPlugin.ElencaFileDati
        Dim nomi As New List(Of String)()
        Try
            If Not _attivo Then Return nomi
            Dim cartella As String = CartellaDati
            If cartella = "" Then Return nomi
            For Each percorso As String In Directory.GetFiles(cartella)
                Dim nome As String = Path.GetFileName(percorso)
                ' i file di servizio (le impostazioni, che iniziano con il trattino basso) non sono del plugin
                If NomeFileValido.IsMatch(nome) Then nomi.Add(nome)
            Next
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ElencaFileDati", ex)
        End Try
        Return nomi
    End Function

    Public Sub EliminaFileDati(nome As String) Implements IWGServiziPlugin.EliminaFileDati
        Try
            If Not _attivo Then Return
            Dim percorso As String = PercorsoFileDati(nome)
            If percorso <> "" AndAlso System.IO.File.Exists(percorso) Then System.IO.File.Delete(percorso)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.EliminaFileDati", ex)
        End Try
    End Sub

#End Region

#Region "Internet"

    Private Const MassimoRispostaByte As Integer = 2 * 1024 * 1024

    ''' <summary>Vero per gli indirizzi che non sono di un sito pubblico: il computer stesso, la rete di casa, gli indirizzi speciali.</summary>
    Private Shared Function IndirizzoNonPubblico(indirizzo As IPAddress) As Boolean
        If indirizzo Is Nothing Then Return True
        If indirizzo.IsIPv4MappedToIPv6 Then indirizzo = indirizzo.MapToIPv4()
        If IPAddress.IsLoopback(indirizzo) Then Return True
        Dim b As Byte() = indirizzo.GetAddressBytes()
        If indirizzo.AddressFamily = AddressFamily.InterNetwork Then
            If b(0) = 0 OrElse b(0) = 10 OrElse b(0) = 127 Then Return True
            If b(0) = 100 AndAlso b(1) >= 64 AndAlso b(1) <= 127 Then Return True
            If b(0) = 169 AndAlso b(1) = 254 Then Return True
            If b(0) = 172 AndAlso b(1) >= 16 AndAlso b(1) <= 31 Then Return True
            If b(0) = 192 AndAlso b(1) = 168 Then Return True
            If b(0) = 192 AndAlso b(1) = 0 AndAlso b(2) = 0 Then Return True
            If b(0) >= 224 Then Return True
            Return False
        End If
        If indirizzo.AddressFamily = AddressFamily.InterNetworkV6 Then
            If indirizzo.IsIPv6LinkLocal OrElse indirizzo.IsIPv6SiteLocal OrElse indirizzo.IsIPv6Multicast Then Return True
            If (b(0) And &HFE) = &HFC Then Return True
            Dim tuttiZero As Boolean = True
            For Each valore As Byte In b
                If valore <> 0 Then tuttiZero = False
            Next
            Return tuttiZero
        End If
        Return True
    End Function

    ''' <summary>Controlla che l'host non sia privato e restituisce anche l'indirizzo IP risolto ora: la richiesta vera
    ''' si fara' verso quello stesso indirizzo (con un proxy fisso, vedi ScaricaTestoAsync), non verso l'host risolto di
    ''' nuovo da HttpClient al momento della connessione. Altrimenti un host con un DNS ostile potrebbe rispondere con
    ''' un indirizzo pubblico a questo controllo e con uno privato alla connessione vera (DNS rebinding), aggirando il
    ''' filtro e facendo raggiungere a un plugin la rete interna.</summary>
    ''' <summary>Nothing se l'host non e' ammesso; altrimenti l'indirizzo IP, gia' controllato, da usare per la
    ''' connessione vera (i metodi async non possono avere parametri ByRef, da cui il risultato unico).</summary>
    Private Shared Async Function IndirizzoAmmesso(uri As Uri) As Task(Of IPAddress)
        If uri Is Nothing OrElse uri.Scheme <> Uri.UriSchemeHttps OrElse Not uri.IsDefaultPort OrElse uri.UserInfo <> "" Then Return Nothing
        Dim host As String = uri.DnsSafeHost
        Dim ip As IPAddress = Nothing
        If IPAddress.TryParse(host, ip) Then
            If IndirizzoNonPubblico(ip) Then Return Nothing
            Return ip
        End If
        If host.IndexOf("."c) < 0 Then Return Nothing
        Dim indirizzi As IPAddress() = Await Dns.GetHostAddressesAsync(host)
        If indirizzi Is Nothing OrElse indirizzi.Length = 0 Then Return Nothing
        For Each indirizzo As IPAddress In indirizzi
            If IndirizzoNonPubblico(indirizzo) Then Return Nothing
        Next
        Return indirizzi(0)
    End Function

    ''' <summary>Fa apparire a HttpClient un indirizzo IP fisso come se fosse il proxy da usare per una certa richiesta:
    ''' per https questo fa si' che la connessione TCP vada sempre e solo verso l'indirizzo gia' controllato (tramite
    ''' un tunnel CONNECT), mentre il nome del sito e il certificato restano quelli veri (SNI e verifica del nome non
    ''' cambiano, perche' avvengono dentro il tunnel). E' il modo per fissare l'indirizzo senza perdere la verifica
    ''' normale del certificato, che su un indirizzo IP nudo fallirebbe con quasi tutti i siti moderni.</summary>
    Private NotInheritable Class ProxySuIndirizzoFisso
        Implements IWebProxy
        Private ReadOnly _proxy As Uri
        Public Property Credentials As ICredentials Implements IWebProxy.Credentials
        Public Sub New(indirizzo As IPAddress, porta As Integer)
            Dim testo As String = If(indirizzo.AddressFamily = AddressFamily.InterNetworkV6, "[" & indirizzo.ToString() & "]", indirizzo.ToString())
            _proxy = New Uri("http://" & testo & ":" & porta.ToString())
        End Sub
        Public Function GetProxy(destinazione As Uri) As Uri Implements IWebProxy.GetProxy
            Return _proxy
        End Function
        Public Function IsBypassed(host As Uri) As Boolean Implements IWebProxy.IsBypassed
            Return False
        End Function
    End Class

    Public Async Function ScaricaTestoAsync(url As String) As Task(Of String) Implements IWGServiziPlugin.ScaricaTestoAsync
        Try
            If Not _attivo OrElse Not Consenti("rete", 10) Then Return Nothing
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return Nothing
            ServicePointManager.SecurityProtocol = CType(3072, SecurityProtocolType)
            ' i reindirizzamenti si seguono a mano (al massimo 3), controllando ogni volta dove vanno e fissando
            ' l'indirizzo IP appena controllato per quel passo (vedi IndirizzoAmmesso e ProxySuIndirizzoFisso).
            For passo As Integer = 0 To 3
                Dim indirizzoIp As IPAddress = Await IndirizzoAmmesso(uri)
                If indirizzoIp Is Nothing Then Return Nothing
                Using gestore As New HttpClientHandler()
                    gestore.AllowAutoRedirect = False
                    gestore.UseCookies = False
                    gestore.UseDefaultCredentials = False
                    gestore.UseProxy = True
                    gestore.Proxy = New ProxySuIndirizzoFisso(indirizzoIp, uri.Port)
                    Using cliente As New HttpClient(gestore)
                        cliente.Timeout = TimeSpan.FromSeconds(15)
                        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("WinGabriel-Plugin/1.0")
                        Using risposta As HttpResponseMessage = Await cliente.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                            Dim codice As Integer = CInt(risposta.StatusCode)
                            If codice >= 300 AndAlso codice < 400 AndAlso risposta.Headers.Location IsNot Nothing Then
                                uri = If(risposta.Headers.Location.IsAbsoluteUri, risposta.Headers.Location, New Uri(uri, risposta.Headers.Location))
                                Continue For
                            End If
                            If Not risposta.IsSuccessStatusCode Then Return Nothing
                            If risposta.Content.Headers.ContentLength.HasValue AndAlso risposta.Content.Headers.ContentLength.Value > MassimoRispostaByte Then Return Nothing
                            Using flusso As Stream = Await risposta.Content.ReadAsStreamAsync()
                                Using memoria As New MemoryStream()
                                    Dim buffer(16383) As Byte
                                    Dim letti As Integer = Await flusso.ReadAsync(buffer, 0, buffer.Length)
                                    While letti > 0
                                        If memoria.Length + letti > MassimoRispostaByte Then Return Nothing
                                        memoria.Write(buffer, 0, letti)
                                        letti = Await flusso.ReadAsync(buffer, 0, buffer.Length)
                                    End While
                                    Return Encoding.UTF8.GetString(memoria.ToArray())
                                End Using
                            End Using
                        End Using
                    End Using
                End Using
            Next
            Return Nothing
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ScaricaTestoAsync", ex)
            Return Nothing
        End Try
    End Function

#End Region

#Region "Permessi e dati di WinGabriel"

    Private ReadOnly _permessiRifiutati As New HashSet(Of String)(StringComparer.Ordinal)
    Private _richiestaInCorso As Boolean

    Public Function AccessoConcesso(permesso As String) As Boolean Implements IWGServiziPlugin.AccessoConcesso
        Try
            Return _attivo AndAlso PermessiPlugin.Concesso(_id, permesso)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AccessoConcesso", ex)
            Return False
        End Try
    End Function

    Public Async Function RichiediAccessoAsync(permesso As String, motivo As String) As Task(Of Boolean) Implements IWGServiziPlugin.RichiediAccessoAsync
        Try
            If Not _attivo OrElse Not PermessiPlugin.Valido(permesso) Then Return False
            If PermessiPlugin.Concesso(_id, permesso) Then Return True
            SyncLock _permessiRifiutati
                If _permessiRifiutati.Contains(permesso) OrElse _richiestaInCorso Then Return False
                _richiestaInCorso = True
            End SyncLock
            Try
                If Not Consenti("permessi", 3) Then Return False
                Dim cosa As String = PermessiPlugin.Cosa(permesso)
                Dim pulito As String = If(motivo, "").Replace(vbCr, " ").Replace(vbLf, " ").Trim()
                If pulito.Length > 120 Then pulito = pulito.Substring(0, 120)
                Dim domanda As String = If(pulito = "",
                    TestiPlugin.Con(TestiPlugin.DomandaPermesso.Replace("{cosa}", cosa), _nomeVisibile),
                    TestiPlugin.Con(TestiPlugin.DomandaPermessoMotivo.Replace("{cosa}", cosa).Replace("{motivo}", pulito), _nomeVisibile))
                Dim consentito As Boolean = Await ChiediAsync(domanda)
                If Not _attivo Then Return False
                If consentito Then
                    PermessiPlugin.Concedi(_id, permesso)
                    Messaggio(TestiPlugin.Con(TestiPlugin.PermessoConcesso.Replace("{cosa}", cosa), _nomeVisibile))
                Else
                    SyncLock _permessiRifiutati
                        _permessiRifiutati.Add(permesso)
                    End SyncLock
                    Messaggio(TestiPlugin.Con(TestiPlugin.PermessoRifiutato.Replace("{cosa}", cosa), _nomeVisibile))
                End If
                Return consentito
            Finally
                SyncLock _permessiRifiutati
                    _richiestaInCorso = False
                End SyncLock
            End Try
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.RichiediAccessoAsync", ex)
            Return False
        End Try
    End Function

    ''' <summary>Vero se il plugin è attivo, ha il permesso e non ha superato il limite di richieste.</summary>
    Private Function PuoLeggere(permesso As String) As Boolean
        Return _attivo AndAlso PermessiPlugin.Concesso(_id, permesso) AndAlso Consenti("dati", 30)
    End Function

    ''' <summary>Esegue sul thread dell'interfaccia e aspetta il risultato (i dati di WinGabriel si leggono da lì).</summary>
    Private Shared Function ConRisultato(Of T)(lavoro As Func(Of T)) As T
        Dim c As WGContenitore = WGContenitore.Istanza
        If c IsNot Nothing AndAlso Not c.IsDisposed AndAlso c.InvokeRequired Then
            Return CType(c.Invoke(lavoro), T)
        End If
        Return lavoro.Invoke()
    End Function

    Private Shared Function LeggiNoteInterno() As List(Of WGVoceElenco)
        Dim risultato As New List(Of WGVoceElenco)()
        Using note As New CLSNote()
            For Each voce As ElementoLista In note.RecuperaNotePaginata(1, 200, OrdinamentoNote.PiuRecenti)
                Dim numero As Integer
                If Integer.TryParse(voce.Id, numero) AndAlso Not note.NotaProtetta(numero) Then risultato.Add(New WGVoceElenco(voce.Id, voce.Testo))
            Next
        End Using
        Return risultato
    End Function

    Private Shared Function LeggiNotaInterno(id As String) As String
        Dim numero As Integer
        If Not Integer.TryParse(id, numero) Then Return Nothing
        Using note As New CLSNote()
            If note.NotaProtetta(numero) Then Return Nothing
            Dim nota As Nota = note.RecuperaNota(numero)
            If nota Is Nothing OrElse nota.Formato <> FormatoNota.Testo Then Return Nothing
            Dim testo As String = nota.Testo
            If testo IsNot Nothing AndAlso testo.Length > 200000 Then testo = testo.Substring(0, 200000)
            Return testo
        End Using
    End Function

    Private Shared Function Colonna(riga As DataRow, nome As String) As String
        If riga.Table.Columns.Contains(nome) AndAlso riga(nome) IsNot Nothing AndAlso Not Convert.IsDBNull(riga(nome)) Then Return Convert.ToString(riga(nome))
        Return ""
    End Function

    Private Shared Function CercaContattiInterno(testo As String) As List(Of WGContattoPlugin)
        Dim risultato As New List(Of WGContattoPlugin)()
        Using contatti As New CLSContatti()
            Dim rubriche As DataTable = contatti.OttieniRubriche()
            If rubriche Is Nothing Then Return risultato
            For Each rubrica As DataRow In rubriche.Rows
                Dim chiave As String = Colonna(rubrica, "ChiaveWG")
                If chiave = "" OrElse contatti.RubricaProtetta(chiave) Then Continue For
                Dim trovati As DataTable = contatti.Cerca(If(testo, ""), "", chiave)
                If trovati Is Nothing Then Continue For
                For Each riga As DataRow In trovati.Rows
                    Dim contatto As New WGContattoPlugin()
                    contatto.Nome = (Colonna(riga, "Nome") & " " & Colonna(riga, "Cognome")).Trim()
                    contatto.Telefono = If(Colonna(riga, "Cellulare") <> "", Colonna(riga, "Cellulare"), Colonna(riga, "Telefono"))
                    contatto.Email = Colonna(riga, "Email")
                    risultato.Add(contatto)
                    If risultato.Count >= 100 Then Return risultato
                Next
            Next
        End Using
        Return risultato
    End Function

    ''' <summary>Gli appuntamenti di UNA agenda (la scelta si cambia solo per il tempo della lettura e poi si rimette com'era).</summary>
    Private Shared Sub AppuntamentiDiAgenda(agenda As CLSAgenda, chiave As String, da As DateTime, a As DateTime, risultato As List(Of WGVoceElenco))
        Dim precedente As String = agenda.AgendaCorrente
        Try
            agenda.AgendaCorrente = chiave
            Dim elenco As List(Of CLSAgenda.AppuntamentoLista) = agenda.OttieniAppuntamentiPerPeriodoPaginati(
                da, a, 0, 200 - risultato.Count, CancellationToken.None, CLSAgenda.TipoOrdinamento.DataCrescente)
            If elenco Is Nothing Then Return
            For Each voce As CLSAgenda.AppuntamentoLista In elenco
                risultato.Add(New WGVoceElenco(voce.Id, (If(voce.Data, "") & ": " & If(voce.Oggetto, "")).Trim()))
            Next
        Finally
            agenda.AgendaCorrente = precedente
        End Try
    End Sub

    ''' <summary>
    ''' Gli appuntamenti delle sole agende NON protette: anche se l'utente ne ha aperta una protetta nella stessa sessione, qui non
    ''' entra (per quelle serve il permesso AgendaProtette e la password).
    ''' </summary>
    Private Shared Function LeggiAppuntamentiInterno(da As DateTime, a As DateTime) As List(Of WGVoceElenco)
        Dim risultato As New List(Of WGVoceElenco)()
        Dim agenda As CLSAgenda = CLSAgenda.GetInstance()
        For Each voce As CLSAgenda.DescrizioneAgenda In agenda.OttieniAgende()
            If voce.Protetta Then Continue For
            If risultato.Count >= 200 Then Exit For
            AppuntamentiDiAgenda(agenda, voce.Chiave, da, a, risultato)
        Next
        Return risultato
    End Function

    ''' <summary>Gli appuntamenti dell'agenda protetta che la password apre. Se era chiusa, torna chiusa.</summary>
    Private Shared Function LeggiAppuntamentiProtettiInterno(password As String, da As DateTime, a As DateTime) As List(Of WGVoceElenco)
        Dim risultato As New List(Of WGVoceElenco)()
        Dim agenda As CLSAgenda = CLSAgenda.GetInstance()
        Dim giaAperte As New HashSet(Of String)()
        For Each voce As CLSAgenda.DescrizioneAgenda In agenda.OttieniAgende()
            If voce.Protetta AndAlso agenda.AgendaSbloccata(voce.Chiave) Then giaAperte.Add(voce.Chiave)
        Next
        Dim chiave As String = agenda.TrovaAgendaPerPassword(password)
        If chiave = "" Then Return Nothing
        Try
            AppuntamentiDiAgenda(agenda, chiave, da, a, risultato)
        Finally
            If Not giaAperte.Contains(chiave) Then agenda.BloccaAgenda(chiave)
        End Try
        Return risultato
    End Function

    Private Shared Function ScriviNotaInterno(titolo As String, testo As String) As Boolean
        Using note As New CLSNote()
            Return note.AggiungiNota(titolo, testo)
        End Using
    End Function

    Private Shared Function ScriviContattoInterno(contatto As WGContattoPlugin) As Boolean
        Dim nomeCompleto As String = If(contatto.Nome, "").Trim()
        Dim spazio As Integer = nomeCompleto.IndexOf(" "c)
        Dim nome As String = If(spazio > 0, nomeCompleto.Substring(0, spazio), nomeCompleto)
        Dim cognome As String = If(spazio > 0, nomeCompleto.Substring(spazio + 1).Trim(), "")
        Dim email As New List(Of String)()
        If Not String.IsNullOrWhiteSpace(contatto.Email) Then email.Add(contatto.Email.Trim())
        Dim telefono As New List(Of String)()
        If Not String.IsNullOrWhiteSpace(contatto.Telefono) Then telefono.Add(contatto.Telefono.Trim())
        Using contatti As New CLSContatti()
            Return contatti.AggiungiContatto(nome, cognome, email, telefono) <> ""
        End Using
    End Function

    Private Shared Function ScriviAppuntamentoInterno(oggetto As String, data As DateTime, oraInizio As String, oraFine As String, note As String) As Boolean
        Return CLSAgenda.GetInstance().AggiungiAppuntamento(oggetto, "", If(oraInizio, ""), If(oraFine, ""), If(note, ""), 0,
            CLSAgenda.Frequenza.Nessuna, data.ToString("dd/MM/yyyy", Globalization.CultureInfo.InvariantCulture), "", "")
    End Function

    Private Shared Function NoteProtetteInterno() As List(Of WGVoceElenco)
        Dim risultato As New List(Of WGVoceElenco)()
        Using note As New CLSNote()
            For Each voce As ElementoLista In note.RecuperaNotePaginata(1, 200, OrdinamentoNote.PiuRecenti)
                Dim numero As Integer
                If Integer.TryParse(voce.Id, numero) AndAlso note.NotaProtetta(numero) Then risultato.Add(New WGVoceElenco(voce.Id, voce.Testo))
            Next
        End Using
        Return risultato
    End Function

    ''' <summary>Il testo di una nota protetta (Nothing se la password è sbagliata o la nota non è testo).</summary>
    Private Shared Function LeggiNotaProtettaInterno(id As String, password As String) As String
        Dim numero As Integer
        If Not Integer.TryParse(id, numero) Then Return Nothing
        Using note As New CLSNote()
            If Not note.NotaProtetta(numero) Then Return Nothing
            Dim nota As Nota = note.RecuperaNota(numero, password)
            If nota Is Nothing OrElse nota.Formato <> FormatoNota.Testo Then Return Nothing
            Dim testo As String = note.VisualizzaTestoNota(numero, password)
            If testo = "Password Errata" OrElse testo = "Contenuto Protetto" Then Return Nothing
            If testo IsNot Nothing AndAlso testo.Length > 200000 Then testo = testo.Substring(0, 200000)
            Return testo
        End Using
    End Function

    Private Shared Function ContattiProtettiInterno(testo As String, password As String) As List(Of WGContattoPlugin)
        Dim risultato As New List(Of WGContattoPlugin)()
        Using contatti As New CLSContatti()
            Dim chiave As String = contatti.TrovaRubricaPerPassword(password)
            If chiave = "" Then Return Nothing
            Dim trovati As DataTable = contatti.Cerca(If(testo, ""), "", chiave)
            If trovati Is Nothing Then Return risultato
            For Each riga As DataRow In trovati.Rows
                Dim contatto As New WGContattoPlugin()
                contatto.Nome = (Colonna(riga, "Nome") & " " & Colonna(riga, "Cognome")).Trim()
                contatto.Telefono = If(Colonna(riga, "Cellulare") <> "", Colonna(riga, "Cellulare"), Colonna(riga, "Telefono"))
                contatto.Email = Colonna(riga, "Email")
                risultato.Add(contatto)
                If risultato.Count >= 100 Then Exit For
            Next
        End Using
        Return risultato
    End Function

    Public Function ElencaNote() As IList(Of WGVoceElenco) Implements IWGServiziPlugin.ElencaNote
        Try
            If Not PuoLeggere(WGPermessi.Note) Then Return New List(Of WGVoceElenco)()
            Return ConRisultato(Of List(Of WGVoceElenco))(AddressOf LeggiNoteInterno)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ElencaNote", ex)
            Return New List(Of WGVoceElenco)()
        End Try
    End Function

    Public Function LeggiNota(id As String) As String Implements IWGServiziPlugin.LeggiNota
        Try
            If Not PuoLeggere(WGPermessi.Note) Then Return Nothing
            Return ConRisultato(Of String)(Function() LeggiNotaInterno(id))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.LeggiNota", ex)
            Return Nothing
        End Try
    End Function

    Public Function CercaContatti(testo As String) As IList(Of WGContattoPlugin) Implements IWGServiziPlugin.CercaContatti
        Try
            If Not PuoLeggere(WGPermessi.Rubrica) Then Return New List(Of WGContattoPlugin)()
            Return ConRisultato(Of List(Of WGContattoPlugin))(Function() CercaContattiInterno(testo))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.CercaContatti", ex)
            Return New List(Of WGContattoPlugin)()
        End Try
    End Function

    Public Function LeggiAppuntamenti(da As DateTime, a As DateTime) As IList(Of WGVoceElenco) Implements IWGServiziPlugin.LeggiAppuntamenti
        Try
            If Not PuoLeggere(WGPermessi.Agenda) Then Return New List(Of WGVoceElenco)()
            Return ConRisultato(Of List(Of WGVoceElenco))(Function() LeggiAppuntamentiInterno(da, a))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.LeggiAppuntamenti", ex)
            Return New List(Of WGVoceElenco)()
        End Try
    End Function

    ' ---- Scrittura: solo aggiunte, solo con il permesso ----

    Private Function PuoScrivere(permesso As String) As Boolean
        Return _attivo AndAlso PermessiPlugin.Concesso(_id, permesso) AndAlso Consenti("scrittura-dati", 10)
    End Function

    Public Function AggiungiNota(titolo As String, testo As String) As Boolean Implements IWGServiziPlugin.AggiungiNota
        Try
            If String.IsNullOrWhiteSpace(titolo) OrElse titolo.Length > 200 OrElse (testo IsNot Nothing AndAlso testo.Length > 200000) Then Return False
            If Not PuoScrivere(WGPermessi.NoteScrittura) Then Return False
            Return ConRisultato(Of Boolean)(Function() ScriviNotaInterno(titolo, If(testo, "")))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AggiungiNota", ex)
            Return False
        End Try
    End Function

    Public Function AggiungiContatto(contatto As WGContattoPlugin) As Boolean Implements IWGServiziPlugin.AggiungiContatto
        Try
            If contatto Is Nothing OrElse String.IsNullOrWhiteSpace(contatto.Nome) OrElse contatto.Nome.Length > 200 Then Return False
            If Not PuoScrivere(WGPermessi.RubricaScrittura) Then Return False
            Return ConRisultato(Of Boolean)(Function() ScriviContattoInterno(contatto))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AggiungiContatto", ex)
            Return False
        End Try
    End Function

    Public Function AggiungiAppuntamento(oggetto As String, data As DateTime, oraInizio As String, oraFine As String, note As String) As Boolean Implements IWGServiziPlugin.AggiungiAppuntamento
        Try
            If String.IsNullOrWhiteSpace(oggetto) OrElse oggetto.Length > 300 Then Return False
            If Not PuoScrivere(WGPermessi.AgendaScrittura) Then Return False
            Return ConRisultato(Of Boolean)(Function() ScriviAppuntamentoInterno(oggetto, data, oraInizio, oraFine, note))
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AggiungiAppuntamento", ex)
            Return False
        End Try
    End Function

    ' ---- Dati protetti da password: il permesso non basta, la password la scrive l'utente a ogni lettura ----

    ''' <summary>Apre il campo della password di WinGabriel e aspetta. Il plugin non vede mai quello che l'utente scrive.</summary>
    Private Function ChiediPasswordAsync() As Task(Of String)
        Dim esito As New TaskCompletionSource(Of String)()
        Try
            If Not Consenti("password", 3) Then
                esito.TrySetResult("")
                Return esito.Task
            End If
            SuInterfaccia(Sub()
                              Try
                                  Dim campi As New Campi()
                                  campi.SalvaSuEscape = False
                                  campi.AggiungiPassword(TestiPlugin.Con(TestiPlugin.TitoloPassword, _nomeVisibile), "pwd", True, False, "")
                                  AddHandler campi.Fine, Sub(mittente As Object, e As EventArgs) esito.TrySetResult(If(campi.RecuperaValore("pwd"), ""))
                                  AddHandler campi.Chiusura, Sub(mittente As Object, e As EventArgs) esito.TrySetResult("")
                                  campi.Avvia()
                              Catch ex As Exception
                                  ScriviErrore("ServiziPlugin.ChiediPasswordAsync", ex)
                                  esito.TrySetResult("")
                              End Try
                          End Sub)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ChiediPasswordAsync (esterno)", ex)
            esito.TrySetResult("")
        End Try
        Return esito.Task
    End Function

    Private Sub Avvisa(testo As String)
        SuInterfaccia(Sub()
                          Try
                              Dim m As New WGMessaggi()
                              m.Messaggio(testo)
                              m.ShowDialog()
                          Catch ex As Exception
                              ScriviErrore("ServiziPlugin.Avvisa", ex)
                          End Try
                      End Sub)
    End Sub

    Public Function ElencaNoteProtette() As IList(Of WGVoceElenco) Implements IWGServiziPlugin.ElencaNoteProtette
        Try
            If Not PuoLeggere(WGPermessi.NoteProtette) Then Return New List(Of WGVoceElenco)()
            Return ConRisultato(Of List(Of WGVoceElenco))(AddressOf NoteProtetteInterno)
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ElencaNoteProtette", ex)
            Return New List(Of WGVoceElenco)()
        End Try
    End Function

    Public Async Function LeggiNotaProtettaAsync(id As String) As Task(Of String) Implements IWGServiziPlugin.LeggiNotaProtettaAsync
        Try
            If Not PuoLeggere(WGPermessi.NoteProtette) Then Return Nothing
            Dim password As String = Await ChiediPasswordAsync()
            If Not _attivo Then Return Nothing
            If password = "" Then
                Avvisa(TestiPlugin.PasswordNonScritta)
                Return Nothing
            End If
            Dim testo As String = ConRisultato(Of String)(Function() LeggiNotaProtettaInterno(id, password))
            If testo Is Nothing Then Avvisa(TestiPlugin.PasswordErrata)
            Return testo
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.LeggiNotaProtettaAsync", ex)
            Return Nothing
        End Try
    End Function

    Public Async Function CercaContattiProtettiAsync(testo As String) As Task(Of IList(Of WGContattoPlugin)) Implements IWGServiziPlugin.CercaContattiProtettiAsync
        Try
            If Not PuoLeggere(WGPermessi.RubricaProtette) Then Return New List(Of WGContattoPlugin)()
            Dim password As String = Await ChiediPasswordAsync()
            If Not _attivo Then Return New List(Of WGContattoPlugin)()
            If password = "" Then
                Avvisa(TestiPlugin.PasswordNonScritta)
                Return New List(Of WGContattoPlugin)()
            End If
            Dim trovati As List(Of WGContattoPlugin) = ConRisultato(Of List(Of WGContattoPlugin))(Function() ContattiProtettiInterno(testo, password))
            If trovati Is Nothing Then
                Avvisa(TestiPlugin.PasswordErrata)
                Return New List(Of WGContattoPlugin)()
            End If
            Return trovati
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.CercaContattiProtettiAsync", ex)
            Return New List(Of WGContattoPlugin)()
        End Try
    End Function

    Public Async Function LeggiAppuntamentiProtettiAsync(da As DateTime, a As DateTime) As Task(Of IList(Of WGVoceElenco)) Implements IWGServiziPlugin.LeggiAppuntamentiProtettiAsync
        Try
            If Not PuoLeggere(WGPermessi.AgendaProtette) Then Return New List(Of WGVoceElenco)()
            Dim password As String = Await ChiediPasswordAsync()
            If Not _attivo Then Return New List(Of WGVoceElenco)()
            If password = "" Then
                Avvisa(TestiPlugin.PasswordNonScritta)
                Return New List(Of WGVoceElenco)()
            End If
            Dim trovati As List(Of WGVoceElenco) = ConRisultato(Of List(Of WGVoceElenco))(Function() LeggiAppuntamentiProtettiInterno(password, da, a))
            If trovati Is Nothing Then
                Avvisa(TestiPlugin.PasswordErrata)
                Return New List(Of WGVoceElenco)()
            End If
            Return trovati
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.LeggiAppuntamentiProtettiAsync", ex)
            Return New List(Of WGVoceElenco)()
        End Try
    End Function

#End Region

#Region "Spegnimento del computer"

    Public Async Function ProgrammaSpegnimentoAsync(durata As TimeSpan) As Task(Of Boolean) Implements IWGServiziPlugin.ProgrammaSpegnimentoAsync
        Try
            If Not _attivo OrElse durata <= TimeSpan.Zero OrElse durata > TimeSpan.FromHours(24) Then Return False
            If Not Await RichiediAccessoAsync(WGPermessi.Spegnimento, "serve per spegnere il computer quando finisce il tempo") Then Return False
            If Not _attivo OrElse Not PermessiPlugin.Concesso(_id, WGPermessi.Spegnimento) OrElse Not Consenti("spegnimento", 5) Then Return False
            WGAzioniProgrammate.ProgrammaSpegnimento(durata)
            Return True
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.ProgrammaSpegnimentoAsync", ex)
            Return False
        End Try
    End Function

    Public Sub AnnullaSpegnimento() Implements IWGServiziPlugin.AnnullaSpegnimento
        Try
            If Not _attivo OrElse Not PermessiPlugin.Concesso(_id, WGPermessi.Spegnimento) Then Return
            WGAzioniProgrammate.AnnullaSpegnimento()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.AnnullaSpegnimento", ex)
        End Try
    End Sub

    Public Function SpegnimentoRimanente() As TimeSpan Implements IWGServiziPlugin.SpegnimentoRimanente
        Try
            If Not _attivo Then Return TimeSpan.Zero
            Return WGAzioniProgrammate.Rimanente()
        Catch ex As Exception
            ScriviErrore("ServiziPlugin.SpegnimentoRimanente", ex)
            Return TimeSpan.Zero
        End Try
    End Function

#End Region

End Class
