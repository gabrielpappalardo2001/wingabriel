Imports System.Collections.Generic
Imports System.Globalization
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports wgcontrols

''' <summary>
''' Le segnalazioni anonime a Gabriel: un problema, un suggerimento o altro. L'utente può soltanto scriverne una nuova.
''' La segnalazione parte senza nome, senza account e senza indirizzo: solo tipo, testo e versione di WinGabriel. Il testo non può contenere
''' nomi: se contiene uno di quelli che il programma conosce (nome utente di Windows, nome del computer, nome completo dell'account, nome
''' utente dei giochi e della chat) la segnalazione si rifiuta, e il testo resta nel campo per toglierlo. Il server rifiuta in più
''' indirizzi di posta, indirizzi internet, numeri di telefono e le frasi con cui ci si presenta, e gira la segnalazione per mail a Gabriel.
''' </summary>
Public Class MenuSegnalazioni

    Private Const Indirizzo As String = "https://wingabriel.net/segnalazioni/segnalazioni.php?a=segnalazione"
    Private Const MinimoCaratteri As Integer = 10
    Private Const MassimoCaratteri As Integer = 3000
    Private Shared ReadOnly Http As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(30)}
    Private Shared ReadOnly Parole_Comuni As New HashSet(Of String)(New String() {"user", "utente", "admin", "administrator", "amministratore", "pc", "desktop", "laptop", "owner", "home", "windows", "guest", "ospite", "computer", "portatile", "default"})
    Private ReadOnly _log As New Logger()

    <DllImport("secur32.dll", CharSet:=CharSet.Unicode, SetLastError:=True)>
    Private Shared Function GetUserNameEx(formato As Integer, buffer As StringBuilder, ByRef dimensione As UInteger) As Boolean
    End Function

    ''' <summary>Il tipo di segnalazione e il testo, oppure Nothing se l'utente rinuncia.</summary>
    Private Class Bozza
        Public Tipo As String = "problema"
        Public Testo As String = ""
    End Class

    ''' <summary>Apre il modulo per una nuova segnalazione. Se non va bene (nome nel testo, testo troppo corto, invio non riuscito) lo riapre con il testo già scritto.</summary>
    Public Async Sub Nuova()
        Try
            Dim tipo As String = "problema"
            Dim testo As String = ""
            Do
                Dim bozza As Bozza = Await ChiediAsync(tipo, testo)
                If bozza Is Nothing Then Return
                tipo = bozza.Tipo
                testo = bozza.Testo

                Dim pulito As String = testo.Trim()
                If pulito.Length < MinimoCaratteri Then
                    Messaggio("La segnalazione è troppo breve.")
                    Continue Do
                End If
                If pulito.Length > MassimoCaratteri Then
                    Messaggio("La segnalazione è troppo lunga: il massimo è " & MassimoCaratteri.ToString(CultureInfo.InvariantCulture) & " caratteri.")
                    Continue Do
                End If
                Dim nome As String = TrovaNome(pulito)
                If nome <> "" Then
                    Messaggio("La segnalazione contiene un nome: " & nome & ". Non può contenere nomi: toglilo e riprova.")
                    Continue Do
                End If

                Dim errore As String = Nothing
                Dim inviata As Boolean = False
                Await WGProcessoInCorso.EseguiAsync("Invio della segnalazione in corso",
                    Async Function() As Task
                        Dim r As Tuple(Of Boolean, String) = Await InviaAsync(tipo, pulito)
                        inviata = r.Item1
                        errore = r.Item2
                    End Function)
                If inviata Then
                    Messaggio("La segnalazione è stata inviata.")
                    Return
                End If
                Messaggio(If(String.IsNullOrWhiteSpace(errore), "La segnalazione non è stata inviata. Controlla la connessione a internet e riprova.", errore))
            Loop
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.Nuova: " & ex.Message & Environment.NewLine & ex.StackTrace)
        End Try
    End Sub

    Private Function ChiediAsync(tipo As String, testo As String) As Task(Of Bozza)
        Try
            Dim esito As New TaskCompletionSource(Of Bozza)()
            Dim campi As New Campi()
            Dim voci As New List(Of KeyValuePair(Of String, String)) From {
                New KeyValuePair(Of String, String)("problema", "Problema"),
                New KeyValuePair(Of String, String)("suggerimento", "Suggerimento"),
                New KeyValuePair(Of String, String)("altro", "Altro")}
            campi.AggiungiScelta("Tipo di segnalazione", "segnalazione.tipo", voci, True, tipo)
            campi.AggiungiMultiriga("Segnalazione", "segnalazione.testo", True, False, testo)
            AddHandler campi.Fine, Sub()
                                       Try
                                           Dim b As New Bozza()
                                           Dim t As String = campi.RecuperaIdScelta("segnalazione.tipo")
                                           b.Tipo = If(String.IsNullOrEmpty(t), "altro", t)
                                           b.Testo = If(campi.RecuperaValore("segnalazione.testo"), "")
                                           esito.TrySetResult(b)
                                       Catch exLog As Exception
                                           _log.ScriviLog("MenuSegnalazioni.ChiediAsync: " & exLog.Message)
                                           esito.TrySetResult(Nothing)
                                       End Try
                                   End Sub
            AddHandler campi.Chiusura, Sub() Task.Delay(600).ContinueWith(Sub(t) esito.TrySetResult(Nothing))
            campi.Avvia()
            Return esito.Task
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.ChiediAsync: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
    End Function

    ''' <summary>Manda la segnalazione al server. Restituisce se è arrivata e, se no, il motivo da dire (vuoto: problema di rete).</summary>
    Private Async Function InviaAsync(tipo As String, testo As String) As Task(Of Tuple(Of Boolean, String))
        Try
            Dim corpo As New JObject()
            corpo("tipo") = tipo
            corpo("testo") = testo
            corpo("versione") = VersioneProgramma()
            Using contenuto As New StringContent(corpo.ToString(Formatting.None), Encoding.UTF8, "application/json")
                Using risposta As HttpResponseMessage = Await Http.PostAsync(Indirizzo, contenuto).ConfigureAwait(True)
                    If risposta.IsSuccessStatusCode Then Return Tuple.Create(True, "")
                    Dim motivo As String = ""
                    Try
                        Dim json As String = Await risposta.Content.ReadAsStringAsync().ConfigureAwait(True)
                        Dim o As JObject = JObject.Parse(json)
                        motivo = Convert.ToString(o("errore"))
                    Catch exLettura As Exception
                        motivo = ""
                    End Try
                    Return Tuple.Create(False, If(motivo, ""))
                End Using
            End Using
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.InviaAsync: " & ex.GetType().Name & ": " & ex.Message)
            Return Tuple.Create(False, "")
        End Try
    End Function

    Private Function VersioneProgramma() As String
        Try
            ' Il nome della versione installata, come lo ha scelto chi l'ha pubblicata.
            Dim v As String = Regex.Replace(RilascioFirmato.NomeInstallato(WGDb.WGPercorsiApplicazione.CartellaCache(), "wingabriel"), "[^\p{L}\p{N}._ -]", "").Trim()
            If v.Length > 40 Then v = v.Substring(0, 40).Trim()
            Return v
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.VersioneProgramma: " & ex.Message)
        End Try
        Return ""
    End Function

    ''' <summary>Il primo nome conosciuto che si trova nel testo come parola intera, oppure una stringa vuota.</summary>
    Friend Function TrovaNome(testo As String) As String
        Try
            Dim normalizzato As String = Normalizza(testo)
            For Each parola As String In NomiConosciuti()
                If Regex.IsMatch(normalizzato, "(?<![\p{L}\p{N}])" & Regex.Escape(parola) & "(?![\p{L}\p{N}])") Then Return parola
            Next
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.TrovaNome: " & ex.Message & Environment.NewLine & ex.StackTrace)
            Throw
        End Try
        Return ""
    End Function

    ''' <summary>Le parole che sono nomi di chi usa il computer: nome utente di Windows e nome completo dell'account, nome del computer, cartella del profilo, nome utente dei giochi e della chat.</summary>
    Private Function NomiConosciuti() As List(Of String)
        Dim sorgenti As New List(Of String)()
        Try
            sorgenti.Add(Environment.UserName)
            sorgenti.Add(Environment.MachineName)
            sorgenti.Add(Environment.UserDomainName)
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.NomiConosciuti ambiente: " & ex.Message)
        End Try
        Try
            Dim profilo As String = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            If Not String.IsNullOrEmpty(profilo) Then sorgenti.Add(System.IO.Path.GetFileName(profilo.TrimEnd("\"c)))
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.NomiConosciuti profilo: " & ex.Message)
        End Try
        Try
            Dim buffer As New StringBuilder(256)
            Dim dimensione As UInteger = 256
            If GetUserNameEx(3, buffer, dimensione) Then sorgenti.Add(buffer.ToString())
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.NomiConosciuti nome completo: " & ex.Message)
        End Try
        Try
            sorgenti.Add(GiochiNome())
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.NomiConosciuti giochi: " & ex.Message)
        End Try

        Dim parole As New List(Of String)()
        For Each s As String In sorgenti
            If String.IsNullOrWhiteSpace(s) Then Continue For
            Dim normalizzato As String = Normalizza(s)
            If normalizzato.Length >= 3 AndAlso Not parole.Contains(normalizzato) AndAlso Not normalizzato.Contains(" ") AndAlso Not Parole_Comuni.Contains(normalizzato) Then parole.Add(normalizzato)
            For Each p As String In Regex.Split(normalizzato, "[^\p{L}\p{N}]+")
                If p.Length >= 3 AndAlso Not Parole_Comuni.Contains(p) AndAlso Not parole.Contains(p) Then parole.Add(p)
            Next
        Next
        Return parole
    End Function

    Private Function GiochiNome() As String
        Return WGGiochi.GiochiClient.Condiviso.Nome
    End Function

    ''' <summary>Minuscolo e senza accenti, per confrontare "José" con "jose".</summary>
    Private Shared Function Normalizza(testo As String) As String
        Dim scomposto As String = testo.ToLowerInvariant().Normalize(NormalizationForm.FormD)
        Dim sb As New StringBuilder(scomposto.Length)
        For Each c As Char In scomposto
            If CharUnicodeInfo.GetUnicodeCategory(c) <> UnicodeCategory.NonSpacingMark Then sb.Append(c)
        Next
        Return sb.ToString().Normalize(NormalizationForm.FormC)
    End Function

    Private Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog("MenuSegnalazioni.Messaggio: " & ex.Message)
        End Try
    End Sub

End Class
