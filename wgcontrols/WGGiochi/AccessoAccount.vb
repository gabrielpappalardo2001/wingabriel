Imports System
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports wgcontrols

''' <summary>
''' L'accesso all'account di WinGabriel, uno solo per i giochi e per la chat.
''' Senza password non si entra. La password non parte mai dal computer: al server va un derivato
''' (vedi GiochiClient.Deriva); l'altra metà resta in memoria (GiochiClient.Master) e protegge la chat sul computer.
''' Sul computer la password si salva solo se si risponde sì, e solo cifrata: all'accesso successivo si entra con quell'utente.
''' Se un nome utente vecchio non ha ancora una password, scriverla gliela assegna.
''' </summary>
Public NotInheritable Class AccessoAccount

    Private Shared _avvisoChatDato As Boolean

    Private Sub New()
    End Sub

    ''' <summary>L'unico avviso sulla chat, una volta sola: prima del primo ingresso oppure quando si gioca con altri.</summary>
    Public Shared Sub AvvisaChat()
        Try
            If _avvisoChatDato Then Return
            _avvisoChatDato = True
            MenuGiochi.Messaggio("Servono nome utente e password. Se non hai un account puoi crearne uno.")
        Catch ex As Exception
            RegistroErrori.Registra("AccessoAccount.AvvisaChat", ex)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Si assicura di essere entrati. False se non si riesce o si annulla.
    ''' Se la password cifrata di questo computer c'è, si entra con quell'utente senza chiederla di nuovo.
    ''' Con richiediMaster serve anche la chiave locale della chat, ricavata dalla stessa password.
    ''' </summary>
    Public Shared Async Function AccediAsync(client As GiochiClient, richiediMaster As Boolean) As Task(Of Boolean)
        Try
            If client.ELocale Then
                ' giochi senza internet: nessun account da scrivere, entra da solo nel motore locale
                Try
                    Await AttesaGiochi.EseguiAsync("Avvio i giochi sul computer", Function() client.AccessoLocaleAsync())
                    Return True
                Catch ex As GiochiErrore
                    MenuGiochi.Messaggio(ex.Message)
                    Return False
                End Try
            End If
            Try
                Await RipristinaMasterAsync(client)
            Catch ex As Exception
                RegistroErrori.Registra("AccessoAccount.AccediAsync", ex)
                client.Master = Nothing
            End Try
            If Not richiediMaster OrElse client.Master IsNot Nothing Then
                Try
                    If Await client.SessioneValidaAsync() AndAlso (Not richiediMaster OrElse client.Master IsNot Nothing) Then Return True
                Catch ex As GiochiErrore
                    MenuGiochi.Messaggio(ex.Message)
                    Return False
                End Try
            End If
            If client.Nome.Length > 0 Then
                Dim salvata As String = client.PasswordSalvata()
                If salvata.Length > 0 Then
                    Dim esitoSalvata As Integer = Await ProvaPasswordAsync(client, client.Nome, salvata, False, False)
                    If esitoSalvata > 0 Then Return True
                    If esitoSalvata < 0 Then Return False
                    client.DimenticaPassword()
                    client.Master = Nothing
                End If
            End If
            Return Await EntraConPasswordAsync(client)
        Catch ex As Exception
            RegistroErrori.Registra("AccessoAccount.AccediAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Dalla password cifrata ricostruisce la chiave locale, senza chiederla di nuovo.</summary>
    Private Shared Async Function RipristinaMasterAsync(client As GiochiClient) As Task
        Try
            If client.Master IsNot Nothing OrElse client.Nome.Length = 0 Then Return
            Dim salvata As String = client.PasswordSalvata()
            If salvata.Length = 0 Then Return
            Dim nome As String = client.Nome
            client.Master = Await Task.Run(Function() As Byte()
                                                Dim master As Byte() = Nothing
                                                GiochiClient.Deriva(nome, salvata, master)
                                                Return master
                                            End Function)
        Catch ex As Exception
            RegistroErrori.Registra("AccessoAccount.RipristinaMasterAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>Nome utente e password. Se il nome non ha ancora una password, quella scritta gli viene assegnata.</summary>
    Private Shared Async Function EntraConPasswordAsync(client As GiochiClient) As Task(Of Boolean)
        Try
            Do
                Dim voci As New List(Of String)()
                If client.Nome.Length > 0 Then
                    voci.Add("Accedi come " & client.Nome)
                    voci.Add("Accedi a un altro account")
                Else
                    voci.Add("Accedi all'account")
                End If
                voci.Add("Crea un account")
                Dim scelta As Integer = Await MenuGiochi.ScegliAsync("Accedi all'account o crea un account", voci)
                If scelta < 0 Then Return False
                Dim conNomeSalvato As Boolean = client.Nome.Length > 0
                Dim crea As Boolean = scelta = voci.Count - 1
                Dim nome As String = ""
                If conNomeSalvato AndAlso scelta = 0 Then
                    nome = client.Nome
                Else
                    nome = Await MenuGiochi.ChiediTestoAsync(If(crea, "Nome utente del nuovo account, da 2 a 20 caratteri", "Nome utente dell'account"), "")
                    If String.IsNullOrWhiteSpace(nome) Then Continue Do
                    nome = nome.Trim()
                End If
                Dim password As String = Await MenuGiochi.ChiediPasswordAsync(If(crea, "Password del nuovo account, almeno 8 caratteri", "Password di " & nome))
                If String.IsNullOrEmpty(password) Then Continue Do
                If crea Then
                    If password.Length < 8 Then
                        MenuGiochi.Messaggio("La password è troppo corta: servono almeno 8 caratteri.")
                        Continue Do
                    End If
                    Dim ripetuta As String = Await MenuGiochi.ChiediPasswordAsync("Ripeti la password")
                    If ripetuta Is Nothing Then Continue Do
                    If ripetuta <> password Then
                        MenuGiochi.Messaggio("Le due password sono diverse. Riprova.")
                        Continue Do
                    End If
                End If
                Dim esito As Integer = Await ProvaPasswordAsync(client, nome, password, crea)
                If esito < 0 Then Return False
                If esito > 0 Then Return True
            Loop
        Catch ex As Exception
            RegistroErrori.Registra("AccessoAccount.EntraConPasswordAsync", ex)
            Throw
        End Try
    End Function

    ''' <summary>1 entrato, 0 riprova, -1 errore di collegamento: si esce. avvisa falso: niente messaggio se la password salvata non vale più, e non si chiede di salvarla.</summary>
    Private Shared Async Function ProvaPasswordAsync(client As GiochiClient, nome As String, password As String, crea As Boolean, Optional avvisa As Boolean = True) As Task(Of Integer)
        Try
            Dim connessione As Task = If(crea, client.RegistraAsync(nome, password), client.EntraAsync(nome, password))
            Await AttesaGiochi.EseguiAsync(If(crea, "Creo l'account", "Accedo all'account"), Function() connessione)
            ' avvisa è falso solo quando si entra con la password già salvata: in quel caso non si chiede di nuovo.
            If avvisa Then Await ChiediSalvataggioPasswordAsync(client, password)
            Return 1
        Catch ex As GiochiErrore
            If avvisa OrElse ex.Codice = 0 OrElse ex.Codice >= 500 Then MenuGiochi.Messaggio(ex.Message)
            If ex.Codice = 0 OrElse ex.Codice >= 500 Then Return -1
            Return 0
        End Try
    End Function

    ''' <summary>Dopo un ingresso riuscito con la password appena scritta: si salva solo se si risponde sì. Esc vale no e cancella quella già presente.</summary>
    Private Shared Async Function ChiediSalvataggioPasswordAsync(client As GiochiClient, password As String) As Task
        Try
            If Await WGDomanda.ChiediAsync("Vuoi salvare la password su questo computer? Resta cifrata e la prossima volta entri senza scriverla.") Then
                client.SalvaPassword(password)
            Else
                client.DimenticaPassword()
            End If
        Catch ex As Exception
            RegistroErrori.Registra("AccessoAccount.ChiediSalvataggioPasswordAsync", ex)
            Throw
        End Try
    End Function
End Class
