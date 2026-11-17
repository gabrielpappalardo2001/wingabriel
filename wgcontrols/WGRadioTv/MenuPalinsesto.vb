Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols

' ============================================================================================
' Le azioni su un programma (informazioni, promemoria, ascolto) e le viste che riguardano tutti i canali o tutte le radio insieme:
' "In onda adesso", "Cerca nei palinsesti" e "Programmi seguiti". Valgono per la TV e per la radio: cambia solo il tipo.
' ============================================================================================

''' <summary>Un canale o una radio con la sua programmazione, da adesso in poi.</summary>
Friend Class CanalePalinsesto
    Public Property Id As String = ""
    Public Property Nome As String = ""
    Public Property TvgId As String = ""
    Public Property Programmi As New List(Of TvProgrammazione.VoceProgramma)()
End Class

''' <summary>Quello che si può fare con un programma trovato nella programmazione di un canale o di una radio.</summary>
Friend NotInheritable Class AzioniProgramma

    Private Shared ReadOnly _logger As New Logger()

    Private Sub New()
    End Sub

    Friend Shared Sub MostraInformazioni(p As TvProgrammazione.VoceProgramma)
        Try
            Dim testo As String = p.Titolo & ", dalle " & p.Inizio.ToString("HH:mm") & " alle " & p.Fine.ToString("HH:mm") & "."
            If Not String.IsNullOrWhiteSpace(p.Descrizione) Then testo = testo & " " & p.Descrizione

            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("AzioniProgramma.MostraInformazioni: " & ex.ToString())
        End Try
    End Sub

    ' Il menu di un programma: informazioni e, se non è ancora iniziato, il promemoria; se è in onda, si può ascoltare subito.
    Friend Shared Sub MostraMenuProgramma(tipo As String, idCanale As String, nomeCanale As String, tvgId As String, p As TvProgrammazione.VoceProgramma)
        Try
            Dim eRadio As Boolean = String.Equals(tipo, TvPromemoria.TIPO_RADIO, StringComparison.OrdinalIgnoreCase)
            Dim menu As New WGMenu()
            menu.aggiungi("Informazioni", Sub() MostraInformazioni(p))

            If p.Inizio > DateTime.Now Then
                If TvPromemoria.Esiste(idCanale, p.Inizio, tipo) Then
                    menu.aggiungi("Rimuovi il promemoria", Sub() RimuoviPromemoria(tipo, idCanale, p))
                Else
                    menu.aggiungi("Imposta un promemoria", Sub() ImpostaPromemoria(tipo, idCanale, nomeCanale, tvgId, p))
                End If
            ElseIf eRadio Then
                menu.aggiungi("Ascolta la radio adesso", Sub() TvPromemoria.AvviaRadio(idCanale))
            Else
                menu.aggiungi("Guarda il canale adesso", Sub() TvPromemoria.AvviaCanale(idCanale))
            End If

            menu.avvia(p.Titolo)
        Catch ex As Exception
            _logger.ScriviLog("AzioniProgramma.MostraMenuProgramma: " & ex.ToString())
        End Try
    End Sub

    ' Aggiunge il promemoria e lo dice con un messaggio; poi, se il programma non è già tra quelli seguiti, chiede se seguirlo: in quel caso
    ' i promemoria si mettono da soli ogni volta che quel programma va in onda su quel canale.
    Friend Shared Sub ImpostaPromemoria(tipo As String, idCanale As String, nomeCanale As String, tvgId As String, p As TvProgrammazione.VoceProgramma)
        Try
            If Not TvPromemoria.Aggiungi(idCanale, nomeCanale, p.Titolo, p.Inizio, tipo, p.Fine) Then
                Dim errore As New WGMessaggi()
                errore.Messaggio("Non è stato possibile impostare il promemoria.")
                errore.ShowDialog()
                Return
            End If

            Dim quando As String = "alle " & p.Inizio.ToString("HH:mm")
            If p.Inizio.Date = DateTime.Today.AddDays(1) Then quando = "domani alle " & p.Inizio.ToString("HH:mm")
            Dim conferma As New WGMessaggi()
            conferma.Messaggio("Promemoria aggiunto per " & p.Titolo & " su " & nomeCanale & " " & quando & ".")
            conferma.ShowDialog()

            Dim titoloBase As String = TvPromemoria.TitoloBase(p.Titolo)
            If titoloBase.Length = 0 OrElse TvPromemoria.Segue(tipo, idCanale, titoloBase) Then Return

            WGDomanda.Chiedi("Vuoi seguire " & titoloBase & " su " & nomeCanale & "? Ti avviserò ogni volta che va in onda, anche nelle prossime settimane.",
                             Sub()
                                 If TvPromemoria.Segui(tipo, idCanale, nomeCanale, tvgId, titoloBase) Then
                                     TvPromemoria.ScansionaSeguitiSubito()
                                     Dim seguito As New WGMessaggi()
                                     seguito.Messaggio("Adesso segui " & titoloBase & " su " & nomeCanale & ". Trovi i programmi seguiti nel menu principale.")
                                     seguito.ShowDialog()
                                 Else
                                     Dim noSeguito As New WGMessaggi()
                                     noSeguito.Messaggio("Non è stato possibile seguire il programma.")
                                     noSeguito.ShowDialog()
                                 End If
                             End Sub)
        Catch ex As Exception
            _logger.ScriviLog("AzioniProgramma.ImpostaPromemoria: " & ex.ToString())
        End Try
    End Sub

    Friend Shared Sub RimuoviPromemoria(tipo As String, idCanale As String, p As TvProgrammazione.VoceProgramma)
        Try
            TvPromemoria.Rimuovi(idCanale, p.Inizio, tipo)
            Dim msg As New WGMessaggi()
            msg.Messaggio("Promemoria rimosso per " & p.Titolo & ".")
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("AzioniProgramma.RimuoviPromemoria: " & ex.ToString())
        End Try
    End Sub

    ' Dall'opzione "Avvisami quando inizia" di un elenco di programmi: aggiunge il promemoria per quella volta sola, senza chiedere se seguire il programma.
    Friend Shared Sub AvvisamiQuandoInizia(tipo As String, idCanale As String, nomeCanale As String, p As TvProgrammazione.VoceProgramma)
        Try
            Dim msg As New WGMessaggi()
            If p Is Nothing Then
                msg.Messaggio("Nessun programma selezionato.")
            ElseIf p.Fine <= DateTime.Now Then
                msg.Messaggio(p.Titolo & " è già finito.")
            ElseIf p.Inizio <= DateTime.Now Then
                msg.Messaggio(p.Titolo & " è già iniziato.")
            ElseIf TvPromemoria.Esiste(idCanale, p.Inizio, tipo) Then
                msg.Messaggio("Hai già un promemoria per " & p.Titolo & " su " & nomeCanale & ".")
            ElseIf TvPromemoria.Aggiungi(idCanale, nomeCanale, p.Titolo, p.Inizio, tipo, p.Fine) Then
                Dim quando As String = "alle " & p.Inizio.ToString("HH:mm")
                If p.Inizio.Date = DateTime.Today.AddDays(1) Then quando = "domani alle " & p.Inizio.ToString("HH:mm")
                msg.Messaggio("Promemoria aggiunto per " & p.Titolo & " su " & nomeCanale & " " & quando & ".")
            Else
                msg.Messaggio("Non è stato possibile impostare il promemoria.")
            End If
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("AzioniProgramma.AvvisamiQuandoInizia: " & ex.ToString())
        End Try
    End Sub

End Class

''' <summary>"In onda adesso", "Cerca nei palinsesti" e "Programmi seguiti" per i canali televisivi o per le radio.</summary>
Friend NotInheritable Class MenuPalinsesto

    Private Const MASSIMO_RISULTATI As Integer = 300
    Private Const DOWNLOAD_PARALLELI As Integer = 6

    Private ReadOnly _tipo As String
    Private ReadOnly _logger As New Logger()
    Private _campiRicerca As Campi

    Friend Sub New(tipo As String)
        _tipo = If(String.Equals(tipo, TvPromemoria.TIPO_RADIO, StringComparison.OrdinalIgnoreCase), TvPromemoria.TIPO_RADIO, TvPromemoria.TIPO_TV)
    End Sub

    Private ReadOnly Property ERadio As Boolean
        Get
            Return _tipo = TvPromemoria.TIPO_RADIO
        End Get
    End Property

    Private ReadOnly Property NomeCanali As String
        Get
            Return If(ERadio, "radio", "canali")
        End Get
    End Property

    Private Sub Avviso(testo As String)
        Dim msg As New WGMessaggi()
        msg.Messaggio(testo)
        msg.ShowDialog()
    End Sub

#Region "Caricamento della programmazione di tutti"

    ' Scarica (o prende dalla memoria) la programmazione di tutti i canali o di tutte le radio che hanno una fonte, con WGProcessoInCorso.
    ' Nothing se è successo un errore.
    Private Function CaricaCanali() As List(Of CanalePalinsesto)
        Dim risultato As List(Of CanalePalinsesto) = Nothing
        Dim proc As New WGProcessoInCorso()
        proc.Avvia("Sto scaricando la programmazione di tutte le " & If(ERadio, "radio", "reti") & ", attendi.", False)
        Try
            risultato = Task.Run(Function() CaricaCanaliAsync()).GetAwaiter().GetResult()
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.CaricaCanali: " & ex.ToString())
            risultato = Nothing
        Finally
            Try
                proc.Fine()
            Catch exFine As Exception
                _logger.ScriviLog("MenuPalinsesto.CaricaCanali fine: " & exFine.Message)
            End Try
        End Try
        Return risultato
    End Function

    Private Async Function CaricaCanaliAsync() As Task(Of List(Of CanalePalinsesto))
        Dim daCaricare As New List(Of CanalePalinsesto)()

        If ERadio Then
            Dim db As RadioDb = RadioDb.GetInstance()
            If Not db.IsCaricato Then db.Carica(False)
            Dim scelte As New Dictionary(Of String, RadioDb.StazioneRadio)(StringComparer.OrdinalIgnoreCase)
            For Each s As RadioDb.StazioneRadio In db.OttieniTutte()
                Dim chiave As String = RadioProgrammazione.ChiaveFonte(s.Nome)
                If chiave.Length = 0 Then Continue For
                Dim presente As RadioDb.StazioneRadio = Nothing
                If Not scelte.TryGetValue(chiave, presente) OrElse s.Voti > presente.Voti Then scelte(chiave) = s
            Next
            For Each s As RadioDb.StazioneRadio In scelte.Values
                daCaricare.Add(New CanalePalinsesto() With {.Id = s.Id, .Nome = s.Nome})
            Next
            daCaricare.Sort(Function(a As CanalePalinsesto, b As CanalePalinsesto) String.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase))
        Else
            ' L'elenco dei canali si ripulisce da solo di quelli che non rispondono, con una verifica in sottofondo che parte all'apertura della Web TV:
            ' si aspetta che finisca (al massimo un minuto e mezzo), così nella vista e nella ricerca ci sono solo i canali che restano nell'elenco.
            Dim verificaDb As TvCanaliDb = TvCanaliDb.GetInstance()
            Dim attesaMs As Integer = 0
            While verificaDb.VerificaInCorso AndAlso attesaMs < 90000
                Await Task.Delay(500).ConfigureAwait(False)
                attesaMs += 500
            End While

            For Each c As TvCanaliDb.CanaleTv In TvCanaliDb.GetInstance().OttieniTuttiICanali()
                daCaricare.Add(New CanalePalinsesto() With {.Id = c.Id, .Nome = c.Nome, .TvgId = c.TvgId})
            Next
        End If

        Dim limite As New SemaphoreSlim(DOWNLOAD_PARALLELI)
        Dim compiti As New List(Of Task)()
        For Each canale As CanalePalinsesto In daCaricare
            compiti.Add(CaricaUnoAsync(canale, limite))
        Next
        Await Task.WhenAll(compiti.ToArray()).ConfigureAwait(False)

        Dim risultato As New List(Of CanalePalinsesto)()
        For Each canale As CanalePalinsesto In daCaricare
            If canale.Programmi.Count > 0 Then risultato.Add(canale)
        Next
        Return risultato
    End Function

    Private Async Function CaricaUnoAsync(canale As CanalePalinsesto, limite As SemaphoreSlim) As Task
        Await limite.WaitAsync().ConfigureAwait(False)
        Try
            Dim programmi As List(Of TvProgrammazione.VoceProgramma)
            If ERadio Then
                programmi = Await RadioProgrammazione.OttieniAsync(canale.Nome).ConfigureAwait(False)
            Else
                programmi = Await TvProgrammazione.OttieniAsync(canale.TvgId, canale.Nome).ConfigureAwait(False)
            End If
            If programmi IsNot Nothing Then canale.Programmi = programmi
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.CaricaUnoAsync (" & canale.Nome & "): " & ex.Message)
        Finally
            limite.Release()
        End Try
    End Function

#End Region

#Region "In onda adesso"

    Friend Sub InOndaAdesso()
        Try
            Dim canali As List(Of CanalePalinsesto) = CaricaCanali()
            If canali Is Nothing Then
                Avviso("Non è stato possibile scaricare la programmazione. Controlla la connessione a internet e riprova.")
                Return
            End If
            If canali.Count = 0 Then
                Avviso("Non ho trovato programmazione per " & If(ERadio, "le radio", "i canali") & ".")
                Return
            End If

            Dim lista As New WGMenuLista()
            lista.ciclono = True
            lista.selezioneMenu = True
            lista.multiselezioneConsentita = False
            lista.BeginUpdate()
            For Each canale As CanalePalinsesto In canali
                lista.aggiungiElemento(EtichettaInOnda(canale), canale.Id)
            Next
            lista.EndUpdate()
            lista.impostaTitolo("In onda adesso, " & If(ERadio, "radio", "canali"))
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Try
                                                          For Each canale As CanalePalinsesto In canali
                                                              If String.Equals(canale.Id, id, StringComparison.OrdinalIgnoreCase) Then
                                                                  ApriProgrammiDelCanale(canale)
                                                                  Exit For
                                                              End If
                                                          Next
                                                      Catch exSel As Exception
                                                          _logger.ScriviLog("MenuPalinsesto.InOndaAdesso selezione: " & exSel.Message)
                                                      End Try
                                                  End Sub
            AddHandler lista.escPremuto, Sub(mittente As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.InOndaAdesso: " & ex.ToString())
        End Try
    End Sub

    Private Function EtichettaInOnda(canale As CanalePalinsesto) As String
        Try
            Dim primo As TvProgrammazione.VoceProgramma = canale.Programmi(0)
            Dim testo As String = canale.Nome & ": "
            If primo.Inizio <= DateTime.Now Then
                testo &= "in onda " & primo.Titolo & ", dalle " & primo.Inizio.ToString("HH:mm")
            Else
                testo &= "alle " & primo.Inizio.ToString("HH:mm") & " " & primo.Titolo
            End If
            If canale.Programmi.Count > 1 Then
                Dim dopo As TvProgrammazione.VoceProgramma = canale.Programmi(1)
                testo &= ". Poi alle " & dopo.Inizio.ToString("HH:mm") & " " & dopo.Titolo
            End If
            Return testo
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.EtichettaInOnda: " & ex.Message)
            Return canale.Nome
        End Try
    End Function

    ' La programmazione intera di un canale: Invio su un programma apre il suo menu.
    Private Sub ApriProgrammiDelCanale(canale As CanalePalinsesto)
        Try
            Dim lista As New WGMenuLista()
            lista.ciclono = True
            lista.selezioneMenu = True
            lista.multiselezioneConsentita = False
            lista.BeginUpdate()
            For i As Integer = 0 To canale.Programmi.Count - 1
                lista.aggiungiElemento(EtichettaProgramma(canale.Programmi(i), i = 0), i.ToString())
            Next
            lista.EndUpdate()
            lista.impostaTitolo("Programmazione di " & canale.Nome)
            lista.aggiungiOpzione("Avvisami quando inizia", Sub(mittente As Object, e As EventArgs)
                                                                Dim indiceOpzione As Integer
                                                                If Integer.TryParse(lista.ValoreSelezionato, indiceOpzione) AndAlso indiceOpzione >= 0 AndAlso indiceOpzione < canale.Programmi.Count Then
                                                                    AzioniProgramma.AvvisamiQuandoInizia(_tipo, canale.Id, canale.Nome, canale.Programmi(indiceOpzione))
                                                                End If
                                                            End Sub)
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim indice As Integer
                                                      If Integer.TryParse(id, indice) AndAlso indice >= 0 AndAlso indice < canale.Programmi.Count Then
                                                          AzioniProgramma.MostraMenuProgramma(_tipo, canale.Id, canale.Nome, canale.TvgId, canale.Programmi(indice))
                                                      End If
                                                  End Sub
            AddHandler lista.escPremuto, Sub(mittente As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.ApriProgrammiDelCanale: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function EtichettaProgramma(p As TvProgrammazione.VoceProgramma, inOnda As Boolean) As String
        Dim ora As String = p.Inizio.ToString("HH:mm")
        If inOnda AndAlso p.Inizio <= DateTime.Now Then Return "In onda ora, dalle " & ora & ", " & p.Titolo
        If p.Inizio.Date = DateTime.Today Then Return "Alle " & ora & ", " & p.Titolo
        If p.Inizio.Date = DateTime.Today.AddDays(1) Then Return "Domani alle " & ora & ", " & p.Titolo
        Return p.Inizio.ToString("dddd d MMMM") & " alle " & ora & ", " & p.Titolo
    End Function

#End Region

#Region "Ricerca nei palinsesti"

    Friend Sub Cerca()
        Try
            _campiRicerca = New Campi()
            _campiRicerca.Aggiungi("Cosa cerchi nei palinsesti, il titolo di un programma o un argomento", "pal.cerca", True, False, "")
            AddHandler _campiRicerca.Fine, AddressOf RicercaConfermata
            AddHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata
            _campiRicerca.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.Cerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub LiberaCampiRicerca()
        Try
            If _campiRicerca Is Nothing Then Return
            RemoveHandler _campiRicerca.Fine, AddressOf RicercaConfermata
            RemoveHandler _campiRicerca.Chiusura, AddressOf RicercaAnnullata
            _campiRicerca = Nothing
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.LiberaCampiRicerca: " & ex.Message)
            _campiRicerca = Nothing
        End Try
    End Sub

    Private Sub RicercaAnnullata(sender As Object, e As EventArgs)
        LiberaCampiRicerca()
    End Sub

    Private Sub RicercaConfermata(sender As Object, e As EventArgs)
        Try
            If _campiRicerca Is Nothing Then Return
            Dim testo As String = _campiRicerca.RecuperaValore("pal.cerca")
            LiberaCampiRicerca()
            If String.IsNullOrWhiteSpace(testo) Then Return
            EseguiRicerca(testo.Trim())
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.RicercaConfermata: " & ex.ToString())
        End Try
    End Sub

    Private Class Risultato
        Public Canale As CanalePalinsesto
        Public Programma As TvProgrammazione.VoceProgramma
    End Class

    Private Sub EseguiRicerca(testo As String)
        Try
            Dim canali As List(Of CanalePalinsesto) = CaricaCanali()
            If canali Is Nothing Then
                Avviso("Non è stato possibile scaricare la programmazione. Controlla la connessione a internet e riprova.")
                Return
            End If

            Dim parole As String() = Semplifica(testo).Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
            If parole.Length = 0 Then Return

            Dim trovati As New List(Of Risultato)()
            For Each canale As CanalePalinsesto In canali
                For Each p As TvProgrammazione.VoceProgramma In canale.Programmi
                    Dim pagliaio As String = " " & Semplifica(p.Titolo & " " & p.Descrizione) & " "
                    Dim tutte As Boolean = True
                    For Each parola As String In parole
                        If pagliaio.IndexOf(parola, StringComparison.Ordinal) < 0 Then
                            tutte = False
                            Exit For
                        End If
                    Next
                    If tutte Then trovati.Add(New Risultato() With {.Canale = canale, .Programma = p})
                Next
            Next

            If trovati.Count = 0 Then
                Avviso("Non ho trovato nessun programma per " & testo & ".")
                Return
            End If

            trovati.Sort(Function(a As Risultato, b As Risultato) a.Programma.Inizio.CompareTo(b.Programma.Inizio))
            If trovati.Count > MASSIMO_RISULTATI Then trovati.RemoveRange(MASSIMO_RISULTATI, trovati.Count - MASSIMO_RISULTATI)

            Dim lista As New WGMenuLista()
            lista.ciclono = True
            lista.selezioneMenu = True
            lista.multiselezioneConsentita = False
            lista.BeginUpdate()
            For i As Integer = 0 To trovati.Count - 1
                Dim r As Risultato = trovati(i)
                lista.aggiungiElemento(r.Canale.Nome & ", " & Quando(r.Programma) & ", " & r.Programma.Titolo, i.ToString())
            Next
            lista.EndUpdate()
            lista.impostaTitolo("Risultati per " & testo)
            lista.aggiungiOpzione("Avvisami quando inizia", Sub(mittente As Object, e As EventArgs)
                                                                Dim indiceOpzione As Integer
                                                                If Integer.TryParse(lista.ValoreSelezionato, indiceOpzione) AndAlso indiceOpzione >= 0 AndAlso indiceOpzione < trovati.Count Then
                                                                    Dim r As Risultato = trovati(indiceOpzione)
                                                                    AzioniProgramma.AvvisamiQuandoInizia(_tipo, r.Canale.Id, r.Canale.Nome, r.Programma)
                                                                End If
                                                            End Sub)
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim indice As Integer
                                                      If Integer.TryParse(id, indice) AndAlso indice >= 0 AndAlso indice < trovati.Count Then
                                                          Dim r As Risultato = trovati(indice)
                                                          AzioniProgramma.MostraMenuProgramma(_tipo, r.Canale.Id, r.Canale.Nome, r.Canale.TvgId, r.Programma)
                                                      End If
                                                  End Sub
            AddHandler lista.escPremuto, Sub(mittente As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.EseguiRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function Quando(p As TvProgrammazione.VoceProgramma) As String
        Dim ora As String = p.Inizio.ToString("HH:mm")
        If p.Inizio <= DateTime.Now Then Return "in onda dalle " & ora
        If p.Inizio.Date = DateTime.Today Then Return "oggi alle " & ora
        If p.Inizio.Date = DateTime.Today.AddDays(1) Then Return "domani alle " & ora
        Return p.Inizio.ToString("dddd d MMMM") & " alle " & ora
    End Function

    ' Minuscole, senza accenti e senza segni, con le parole separate da uno spazio: per confrontare quello che si scrive con i titoli.
    Private Shared Function Semplifica(testo As String) As String
        If String.IsNullOrWhiteSpace(testo) Then Return ""
        Dim scomposto As String = testo.Normalize(NormalizationForm.FormD)
        Dim sb As New StringBuilder()
        Dim ultimoSpazio As Boolean = True
        For Each c As Char In scomposto
            If CharUnicodeInfo.GetUnicodeCategory(c) = UnicodeCategory.NonSpacingMark Then Continue For
            If Char.IsLetterOrDigit(c) Then
                sb.Append(Char.ToLowerInvariant(c))
                ultimoSpazio = False
            ElseIf Not ultimoSpazio Then
                sb.Append(" "c)
                ultimoSpazio = True
            End If
        Next
        Return sb.ToString().Trim()
    End Function

#End Region

#Region "Programmi seguiti"

    Friend Sub Seguiti()
        Try
            Dim elenco As List(Of TvPromemoria.ProgrammaSeguito) = TvPromemoria.ElencoSeguiti(_tipo)
            If elenco.Count = 0 Then
                Avviso("Non segui ancora nessun programma. Quando imposti un promemoria, ti chiedo se vuoi seguire il programma.")
                Return
            End If

            Dim lista As New WGMenuLista()
            lista.ciclono = True
            lista.selezioneMenu = True
            lista.multiselezioneConsentita = False
            lista.BeginUpdate()
            For Each s As TvPromemoria.ProgrammaSeguito In elenco
                lista.aggiungiElemento(s.Titolo & ", su " & s.NomeCanale, s.Id.ToString())
            Next
            lista.EndUpdate()
            lista.impostaTitolo("Programmi seguiti, " & If(ERadio, "radio", "televisione"))

            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      For Each s As TvPromemoria.ProgrammaSeguito In elenco
                                                          If s.Id.ToString() = id Then
                                                              Avviso("Segui " & s.Titolo & " su " & s.NomeCanale & ": ti avviso ogni volta che va in onda. Per smettere, usa l'opzione Smetti di seguire.")
                                                              Exit For
                                                          End If
                                                      Next
                                                  End Sub
            lista.aggiungiOpzione("Smetti di seguire", Sub(mittente As Object, e As EventArgs)
                                                           Try
                                                               Dim id As String = lista.ValoreSelezionato
                                                               Dim numero As Long
                                                               If String.IsNullOrWhiteSpace(id) OrElse Not Long.TryParse(id, numero) Then Return
                                                               TvPromemoria.SmettiDiSeguire(numero)
                                                               lista.RimuoviElemento(id)
                                                               Avviso("Non segui più questo programma.")
                                                           Catch exSmetti As Exception
                                                               _logger.ScriviLog("MenuPalinsesto.Seguiti smetti: " & exSmetti.Message)
                                                           End Try
                                                       End Sub)
            AddHandler lista.escPremuto, Sub(mittente As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            _logger.ScriviLog("MenuPalinsesto.Seguiti: " & ex.ToString())
        End Try
    End Sub

#End Region

End Class
