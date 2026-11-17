Imports System.Collections.Concurrent
Imports System.ComponentModel
Imports System.Drawing.Text
Imports System.Net.Security
Imports System.Security.AccessControl
Imports Microsoft.SqlServer.Server
Imports wgcontrols
Imports WGDati
Imports System.Security.Cryptography
Imports System.IO
Imports System.Windows.Forms
Imports System.Text
Imports System.Threading
Imports System.Linq

' Archivio sicuro protetto da password per dati sensibili. Gestisce note riservate, carte di pagamento, dati di siti internet e
' documenti di identità.
Public Class MenuRiservato
    Private singolodato As Dato
    Private Class modificadato
        Public nome As String
        Public campo As Integer
    End Class
    Private nomi As List(Of modificadato)
    Private nome As String
    Private indice As Integer
    Private indice2 As Integer
    Private menulista As WGMenuLista

    Private password As String
    Private menu As WGMenu
    Private datiriservati As New CLSDati
    Private campi As Campi
    Private camponota As WGCampo
    Private menulistavisualizza As WGMenuLista
    Private ReadOnly _logger As Logger

    Private testoRicerca As String = ""
    Private domanda As WGDomanda
    Private tempNomeNota As String

    Private _uiContext As SynchronizationContext
    Private attesa As WGProcessoInCorso
    Private _tempDatiElenco As List(Of elencodato)
    Private _statoCorrente As StatoLista

    Private _idDaEliminare As New List(Of Integer)

    Private Enum StatoLista
        Consultazione
        Ricerca
    End Enum

    Public Sub New()
        Try
            _uiContext = SynchronizationContext.Current
            _logger = New Logger()
            Dim risultato As Boolean = datiriservati.PasswordImpostata()
            If risultato = True Then
                campi = New Campi()
                campi.AggiungiPassword("Inserisci la password per entrare nell'area riservata", "c.datipassword", True)
                AddHandler campi.Fine, AddressOf campifinepassword
                campi.Avvia()
            Else
                InizializzaNuovaPassword()
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia all'avvio dell'area riservata: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in MenuRiservato.New: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As IOException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di I/O all'avvio dell'area riservata: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("IOException in MenuRiservato.New: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso all'avvio dell'area riservata: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in MenuRiservato.New: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim msg As New WGMessaggi()
            msg.Messaggio(testo)
            msg.ShowDialog()
        Catch ex As Exception
            _logger.ScriviLog("Exception in MostraMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub InizializzaNuovaPassword()
        Try
            campi = New Campi()
            campi.AggiungiPassword("Inserisci la nuova password", "p1", True)
            campi.AggiungiPassword("Conferma la password", "p2", True)
            AddHandler campi.Fine, AddressOf impostapasswordcampi
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in InizializzaNuovaPassword: " & ex.Message)
        End Try
    End Sub

    Private Sub campifinepassword()
        Try
            Dim pw As String
            pw = campi.RecuperaValore("c.datipassword")
            If String.IsNullOrEmpty(pw) Then
                MostraMessaggio("Password non inserita.")
                campi = Nothing
            Else
                Dim risultato As Boolean
                risultato = datiriservati.VerificaPasswordCorretta(pw)
                If risultato = True Then
                    password = pw
                    AvviaMenuPrincipale()
                Else
                    MostraMessaggio("La password non risulta corretta.")
                End If
            End If

        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante la verifica della password: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in campifinepassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la verifica della password: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in campifinepassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub AvviaMenuPrincipale()
        Try
            menu = New WGMenu
            menu.aggiungi("Inserisci nuovo dato", AddressOf nuovodato)
            menu.aggiungi("Consulta dati", AddressOf consultadati)
            menu.aggiungi("Cerca dati", AddressOf cercadati)
            menu.aggiungi("Cambia password", AddressOf cambiapassword)
            menu.avvia("menu dati riservati")
        Catch ex As Exception
            _logger.ScriviLog("Exception in AvviaMenuPrincipale: " & ex.Message)
        End Try
    End Sub

    Private Sub cambiapassword()
        Try
            campi = New Campi()
            campi.AggiungiPassword("Vecchia Password", "old", True)
            campi.AggiungiPassword("Nuova Password", "new1", True)
            campi.AggiungiPassword("Conferma Password", "new2", True)
            AddHandler campi.Fine, AddressOf eseguiCambioPassword
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in cambiapassword: " & ex.Message)
        End Try
    End Sub

    Private Sub eseguiCambioPassword()
        Try
            Dim pOld As String = campi.RecuperaValore("old")
            Dim pNew1 As String = campi.RecuperaValore("new1")
            Dim pNew2 As String = campi.RecuperaValore("new2")
            campi = Nothing

            If pNew1 <> pNew2 Then
                MostraMessaggio("Le nuove password non corrispondono. Riprova.")
                cambiapassword()
                Return
            End If

            If datiriservati.ModificaPassword(pOld, pNew1) Then
                MostraMessaggio("Password modificata.")
                password = pNew1
            Else
                MostraMessaggio("Errore: Vecchia password errata o nuova password non sicura.")
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante il cambio password: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in eseguiCambioPassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante il cambio password: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in eseguiCambioPassword: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub nuovodato()
        Try
            Dim menuscelta As New WGMenu
            menuscelta.aggiungi("Inserimento dati carta di pagamento", AddressOf cartapagamento)
            menuscelta.aggiungi("Inserimento dati accesso sito web", AddressOf datisito)
            menuscelta.aggiungi("Inserisci dati carta di identità", AddressOf cartaidentita)
            menuscelta.aggiungi("Inserimento altro dato", AddressOf altrodato)
            menuscelta.aggiungi("Inserimento Nota sicura", AddressOf notasicura)
            menuscelta.avvia("Menù cosa inserire")
        Catch ex As Exception
            _logger.ScriviLog("Exception in nuovodato: " & ex.Message)
        End Try
    End Sub

    Private Sub altrodato()
        Try
            campi = New Campi()
            campi.Aggiungi("Nome del dato", "nome", True)
            campi.AggiungiMulti("Valore", "valori", True)
            AddHandler campi.Fine, AddressOf inserimentoaltrodato
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in altrodato: " & ex.Message)
        End Try
    End Sub

    Private Sub inserimentoaltrodato()
        Try
            Dim dato As New Dato
            dato.Nome = campi.RecuperaValore("nome")
            dato.Tipo = "altrodato"
            Dim listaValori As List(Of String) = campi.RecuperaListaValori("valori")

            For Each s As String In listaValori
                dato.Valori.Add(New Valore With {.Nome = "Elemento", .Valore = s})
            Next

            If datiriservati.AggiungiDato(dato, password) Then
                MostraMessaggio("Dato inserito")
            Else
                MostraMessaggio("Errore inserimento dato.")
            End If
            campi = Nothing
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inserimento di altro dato: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in inserimentoaltrodato: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'inserimento di altro dato: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in inserimentoaltrodato: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub notasicura()
        Try
            campi = New Campi()
            campi.Aggiungi("Titolo della nota", "titolo", True)
            AddHandler campi.Fine, AddressOf apriEditorNota
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in notasicura: " & ex.Message)
        End Try
    End Sub

    Private Sub apriEditorNota()
        Try
            tempNomeNota = campi.RecuperaValore("titolo")
            campi = Nothing
            camponota = New WGCampo()
            AddHandler camponota.chiusura, AddressOf salvanota
            camponota.init(tempNomeNota, "", True, False, False, True)
            camponota.avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in apriEditorNota: " & ex.Message)
        End Try
    End Sub

    Private Sub salvanota()
        Try
            Dim testo As String = camponota.ottienitesto()
            camponota.chiudi()

            Dim dato As New Dato
            dato.Nome = tempNomeNota
            dato.Tipo = "Nota"
            dato.Valori.Add(New Valore With {.Nome = "Contenuto", .Valore = testo})

            If datiriservati.AggiungiDato(dato, password) Then
                MostraMessaggio("Nota salvata")
            Else
                MostraMessaggio("Errore salvataggio nota.")
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante il salvataggio della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in salvanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante il salvataggio della nota: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in salvanota: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub cartapagamento()
        Try
            campi = New Campi()
            campi.Aggiungi("Inserisci il nome che vuoi dare al nuovo dato", "c.nomedato", True)
            campi.AggiungiNumeroCarta("Inserisci numero carta", "c.numerocarta", True)
            campi.AggiungiDataScadenza("Inserisci data di scadenza", "c.datascadenza", True)
            campi.AggiungiCVC("Inserisci CVC", "c.cvc", True)
            AddHandler campi.Fine, AddressOf inserimentocarta
            campi.Avvia()
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto campi non impostato per carta di pagamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in cartapagamento: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la configurazione dei campi carta di pagamento: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in cartapagamento: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub inserimentocarta()
        Try
            Dim dato As New Dato
            dato.Nome = campi.RecuperaValore("c.nomedato")
            dato.Tipo = "Carta"
            dato.Valori.Add(New Valore With {.Nome = "Numero carta", .Valore = campi.RecuperaValore("c.numerocarta")})
            dato.Valori.Add(New Valore With {.Nome = "Data di scadenza", .Valore = campi.RecuperaValore("c.datascadenza")})
            dato.Valori.Add(New Valore With {.Nome = "Numero cvc", .Valore = campi.RecuperaValore("c.cvc")})

            If datiriservati.AggiungiDato(dato, password) Then
                MostraMessaggio("Dato inserito")
            Else
                MostraMessaggio("Errore inserimento dato.")
            End If
            campi = Nothing
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inserimento dei dati della carta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in inserimentocarta: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante l'inserimento dei dati della carta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in inserimentocarta: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'inserimento dei dati della carta: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in inserimentocarta: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub datisito()
        Try
            campi = New Campi()
            campi.Aggiungi("Inserisci il nome che vuoi dare al nuovo dato", "c.nomedato", True)
            campi.Aggiungi("Inserisci l'url del sito internet", "c.url", True)
            campi.Aggiungi("Inserisci il nome utente di accesso", "c.username", True)
            campi.Aggiungi("Inserisci la parola chiave di accesso", "c.password", True)
            AddHandler campi.Fine, AddressOf inserimentodatisito
            campi.Avvia()
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto campi non impostato per dati sito: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in datisito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la configurazione dei campi dati sito: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in datisito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub inserimentodatisito()
        Try
            Dim dato As New Dato
            dato.Nome = campi.RecuperaValore("c.nomedato")
            dato.Tipo = "Sito"
            dato.Valori.Add(New Valore With {.Nome = "Url del sito", .Valore = campi.RecuperaValore("c.url")})
            dato.Valori.Add(New Valore With {.Nome = "Nome utente", .Valore = campi.RecuperaValore("c.username")})
            dato.Valori.Add(New Valore With {.Nome = "Parola chiave", .Valore = campi.RecuperaValore("c.password")})

            If datiriservati.AggiungiDato(dato, password) Then
                MostraMessaggio("Il dato è stato inserito")
            Else
                MostraMessaggio("Errore inserimento dato.")
            End If
            campi = Nothing
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inserimento dei dati del sito: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in inserimentodatisito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante l'inserimento dei dati del sito: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in inserimentodatisito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'inserimento dei dati del sito: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in inserimentodatisito: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub cartaidentita()
        Try
            campi = New Campi()
            campi.Aggiungi("Inserisci il nome che vuoi dare al nuovo dato", "c.nomedato", True)
            campi.Aggiungi("Inserisci il numero di serie della carta di identità", "c.numerocartaidentita", True)
            campi.AggiungiDataScadenza("Inserisci la data di scadenza", "c.datascadenza", True)
            campi.AggiungiNumerico("Inserisci pin carta", "c.pin", True)
            campi.AggiungiNumerico("Inserisci Puc Carta", "c.puc", True)
            AddHandler campi.Fine, AddressOf inseriscicartaidentita
            campi.Avvia()
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto campi non impostato per carta identità: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in cartaidentita: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la configurazione dei campi carta identità: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in cartaidentita: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub inseriscicartaidentita()
        Try
            Dim dato As New Dato
            dato.Nome = campi.RecuperaValore("c.nomedato")
            dato.Tipo = "Documento"
            dato.Valori.Add(New Valore With {.Nome = "Numero carta", .Valore = campi.RecuperaValore("c.numerocartaidentita")})
            dato.Valori.Add(New Valore With {.Nome = "Data scadenza", .Valore = campi.RecuperaValore("c.datascadenza")})
            dato.Valori.Add(New Valore With {.Nome = "Pin carta", .Valore = campi.RecuperaValore("c.pin")})
            dato.Valori.Add(New Valore With {.Nome = "Puc Carta", .Valore = campi.RecuperaValore("c.puc")})

            If datiriservati.AggiungiDato(dato, password) Then
                MostraMessaggio("Dato inserito")
            Else
                MostraMessaggio("Errore inserimento dato.")
            End If
            campi = Nothing
        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'inserimento dei dati della carta d'identità: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in inseriscicartaidentita: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante l'inserimento dei dati della carta d'identità: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in inseriscicartaidentita: " & ex.Message & " StackTrace: " & ex.StackTrace)
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'inserimento dei dati della carta d'identità: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in inseriscicartaidentita: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub cercadati()
        Try
            campi = New Campi()
            campi.Aggiungi("Inserisci il testo da cercare", "t.cerca", True)
            AddHandler campi.Fine, AddressOf avviaricerca
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in cercadati: " & ex.Message)
        End Try
    End Sub

    Private Sub avviaricerca()
        Try
            testoRicerca = campi.RecuperaValore("t.cerca")
            campi = Nothing
            If String.IsNullOrEmpty(testoRicerca) Then Return
            AvviaCaricamento(StatoLista.Ricerca, testoRicerca)
        Catch ex As Exception
            _logger.ScriviLog("Exception in avviaricerca: " & ex.Message)
        End Try
    End Sub

    Private Sub consultadati()
        AvviaCaricamento(StatoLista.Consultazione)
    End Sub

    Private Sub EseguiSuUI(azione As Action)
        If _uiContext IsNot Nothing Then
            _uiContext.Post(Sub(state) azione.Invoke(), Nothing)
        Else
            azione.Invoke()
        End If
    End Sub

    Private Sub MostraAttesa(testo As String)
        If attesa Is Nothing Then
            attesa = New WGProcessoInCorso()
        End If
        attesa.Avvia(testo)
    End Sub

    Private Sub NascondiAttesa()
        If attesa IsNot Nothing Then
            attesa.Fine()
            attesa = Nothing
        End If
    End Sub

    Private Sub AvviaCaricamento(nuovoStato As StatoLista, Optional filtro As String = "")
        _statoCorrente = nuovoStato
        testoRicerca = filtro
        MostraAttesa("Caricamento in corso...")
        Dim t As New Thread(AddressOf ThreadCaricaDati)
        t.IsBackground = True
        t.Start()
    End Sub

    Private Sub ThreadCaricaDati()
        Try
            Dim conteggio As Integer = 0
            Select Case _statoCorrente
                Case StatoLista.Consultazione
                    conteggio = datiriservati.ContaDati(password)
                Case StatoLista.Ricerca
                    conteggio = datiriservati.ContaRisultatiRicerca(testoRicerca, password)
            End Select

            If conteggio = 0 Then
                _tempDatiElenco = New List(Of elencodato)()
            Else
                Select Case _statoCorrente
                    Case StatoLista.Consultazione
                        _tempDatiElenco = datiriservati.ElencoDatiPaginato(password, 1, Integer.MaxValue)
                    Case StatoLista.Ricerca
                        _tempDatiElenco = datiriservati.CercaDatiPaginato(testoRicerca, password, 1, Integer.MaxValue)
                End Select
            End If

            EseguiSuUI(AddressOf UI_MostraLista)
        Catch ex As Exception
            _logger.ScriviLog("Exception in ThreadCaricaDati: " & ex.Message)
            EseguiSuUI(AddressOf NascondiAttesa)
        End Try
    End Sub

    Private Sub UI_MostraLista()
        NascondiAttesa()
        Try
            If _tempDatiElenco IsNot Nothing AndAlso _tempDatiElenco.Count > 0 Then
                menulista = New WGMenuLista
                If _statoCorrente = StatoLista.Ricerca Then
                    menulista.impostaTitolo("Risultati Ricerca")
                Else
                    menulista.impostaTitolo("Menu consultazione dati personali")
                End If
                menulista.multiselezioneConsentita = True
                menulista.ciclono = True

                Dim listaDinamica As New List(Of ElementoLista)
                For Each elemento In _tempDatiElenco
                    Dim testoElemento As String = elemento.Nome
                    If String.IsNullOrWhiteSpace(testoElemento) Then testoElemento = "Dato senza nome"
                    If Not String.IsNullOrWhiteSpace(elemento.Tipo) Then testoElemento = testoElemento & ", " & elemento.Tipo
                    listaDinamica.Add(New ElementoLista(testoElemento, elemento.Id.ToString()))
                Next

                menulista.CaricaListaDinamica(listaDinamica)

                menulista.aggiungiOpzione("Consulta il dato", AddressOf OpzioneConsultaDato)
                menulista.aggiungiOpzione("Modifica i valori del dato", AddressOf OpzioneModificaValori)
                menulista.aggiungiOpzione("Modifica il nome del dato", AddressOf OpzioneModificaNome)
                menulista.aggiungiOpzione("Copia negli appunti", AddressOf OpzioneCopiaDaElenco)
                menulista.aggiungiOpzione("Elimina", AddressOf OpzioneEliminaDaElenco)
                menulista.aggiungiOpzione("Aggiorna l'elenco", AddressOf OpzioneAggiornaElenco)

                AddHandler menulista.escPremuto, AddressOf chiudilista
                AddHandler menulista.ElementoSelezionato, AddressOf gestioneclicklista
                menulista.avvia()
            Else
                If _statoCorrente = StatoLista.Ricerca Then
                    MostraMessaggio("Nessun risultato trovato.")
                Else
                    MostraMessaggio("Non sono presenti dati riservati da mostrare.")
                End If
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in UI_MostraLista: " & ex.Message)
        End Try
    End Sub

    Private Function OttieniSelezioneElenco() As List(Of String)
        Try
            If menulista Is Nothing Then Return New List(Of String)()
            Dim id As List(Of String) = menulista.ottieniElementi()
            If id Is Nothing Then Return New List(Of String)()
            Return id
        Catch ex As Exception
            _logger.ScriviLog("Exception in OttieniSelezioneElenco: " & ex.Message)
            Return New List(Of String)()
        End Try
    End Function

    Private Function OttieniIdSelezionati() As List(Of Integer)
        Dim risultato As New List(Of Integer)
        Try
            Dim id As List(Of String) = OttieniSelezioneElenco()
            For Each elemento As String In id
                Try
                    Dim valore As Integer = 0
                    If Integer.TryParse(elemento, valore) Then
                        If Not risultato.Contains(valore) Then risultato.Add(valore)
                    End If
                Catch exElemento As Exception
                    _logger.ScriviLog("Exception in OttieniIdSelezionati (elemento): " & exElemento.Message)
                End Try
            Next
        Catch ex As Exception
            _logger.ScriviLog("Exception in OttieniIdSelezionati: " & ex.Message)
        End Try
        Return risultato
    End Function

    Private Function OttieniIdSingoloElenco() As Integer
        Try
            Dim id As List(Of String) = OttieniSelezioneElenco()

            If id.Count = 0 Then
                MostraMessaggio("Non è stato selezionato nessun dato.")
                Return -1
            End If

            If id.Count > 1 Then
                MostraMessaggio("Questa operazione può essere eseguita su un solo dato per volta.")
                Return -1
            End If

            Dim elemento As String = id(0)
            Dim valore As Integer = 0
            If Not Integer.TryParse(elemento, valore) Then
                MostraMessaggio("Il dato selezionato non è valido.")
                Return -1
            End If

            Return valore
        Catch ex As Exception
            _logger.ScriviLog("Exception in OttieniIdSingoloElenco: " & ex.Message)
            Return -1
        End Try
    End Function

    Private Sub ChiudiListaDettaglio()
        Try
            If menulistavisualizza IsNot Nothing Then menulistavisualizza.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("Exception in ChiudiListaDettaglio: " & ex.Message)
        End Try
        menulistavisualizza = Nothing
    End Sub

    Private Sub ChiudiListaPrincipale()
        Try
            If menulista IsNot Nothing Then menulista.abbandona()
        Catch ex As Exception
            _logger.ScriviLog("Exception in ChiudiListaPrincipale: " & ex.Message)
        End Try
        menulista = Nothing
    End Sub

    Private Sub RicaricaElenco()
        Try
            ChiudiListaDettaglio()
            ChiudiListaPrincipale()
            AvviaCaricamento(_statoCorrente, testoRicerca)
        Catch ex As Exception
            _logger.ScriviLog("Exception in RicaricaElenco: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneAggiornaElenco()
        Try
            RicaricaElenco()
        Catch ex As Exception
            _logger.ScriviLog("Exception in OpzioneAggiornaElenco: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneConsultaDato()
        Try
            Dim id As Integer = OttieniIdSingoloElenco()
            If id < 0 Then Return
            visualizzadato(id.ToString())
        Catch ex As Exception
            _logger.ScriviLog("Exception in OpzioneConsultaDato: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneModificaValori()
        Try
            Dim id As Integer = OttieniIdSingoloElenco()
            If id < 0 Then Return
            indice = id
            modificavalori()
        Catch ex As Exception
            _logger.ScriviLog("Exception in OpzioneModificaValori: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneModificaNome()
        Try
            Dim id As Integer = OttieniIdSingoloElenco()
            If id < 0 Then Return
            indice = id
            modificanome()
        Catch ex As Exception
            _logger.ScriviLog("Exception in OpzioneModificaNome: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneCopiaDaElenco()
        Try
            Dim elenco As List(Of Integer) = OttieniIdSelezionati()

            If elenco.Count = 0 Then
                MostraMessaggio("Non è stato selezionato nessun dato.")
                Return
            End If

            Dim testo As New StringBuilder()
            Dim copiati As Integer = 0

            For Each id As Integer In elenco
                Try
                    Dim dato As Dato = datiriservati.VisualizzaDato(id, password)
                    If dato Is Nothing Then Continue For

                    If copiati > 0 Then testo.AppendLine()
                    testo.AppendLine("Nome: " & dato.Nome)
                    For Each val As Valore In dato.Valori
                        testo.AppendLine(val.Nome & ": " & val.Valore)
                    Next
                    copiati += 1
                Catch exDato As Exception
                    _logger.ScriviLog("Exception in OpzioneCopiaDaElenco (dato " & id.ToString() & "): " & exDato.Message)
                End Try
            Next

            If copiati = 0 Then
                MostraMessaggio("Non è stato possibile leggere i dati selezionati.")
                Return
            End If

            Clipboard.SetText(testo.ToString())

            If copiati = 1 Then
                MostraMessaggio("Valori copiati negli appunti.")
            Else
                MostraMessaggio("Copiati negli appunti " & copiati.ToString() & " dati.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in OpzioneCopiaDaElenco: " & ex.Message)
        End Try
    End Sub

    Private Sub OpzioneEliminaDaElenco()
        Try
            Dim elenco As List(Of Integer) = OttieniIdSelezionati()

            If elenco.Count = 0 Then
                MostraMessaggio("Non è stato selezionato nessun dato.")
                Return
            End If

            _idDaEliminare = elenco
            Dim testoDomanda As String
            If elenco.Count = 1 Then
                testoDomanda = "Sei sicuro di voler eliminare questo dato?"
            Else
                testoDomanda = "Sei sicuro di voler eliminare i " & elenco.Count.ToString() & " dati selezionati?"
            End If

            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf rispostaelimina
            domanda.avvia(testoDomanda)
        Catch ex As Exception
            _logger.ScriviLog("Exception in OpzioneEliminaDaElenco: " & ex.Message)
        End Try
    End Sub

    Private Sub gestioneclicklista()
        Try
            Dim id As List(Of String) = menulista.ottieniElementi()
            If id Is Nothing OrElse id.Count = 0 Then Return
            Dim elemento As String = id(0)
            visualizzadato(elemento)
        Catch ex As Exception
            _logger.ScriviLog("Exception in gestioneclicklista: " & ex.Message)
        End Try
    End Sub

    Private Sub chiudilista()
        Try
            If menulista IsNot Nothing Then menulista.abbandona()
            menulista = Nothing
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la chiusura della lista: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in chiudilista: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub visualizzadato(idStr As String)
        Try
            Dim id As Integer
            If Not Integer.TryParse(idStr, id) Then Return
            indice = id
            Dim datodettaglio As Dato = datiriservati.VisualizzaDato(id, password)
            If datodettaglio Is Nothing Then
                MostraMessaggio("Dato non trovato o password errata.")
                Return
            End If
            menulistavisualizza = New WGMenuLista
            menulistavisualizza.impostaTitolo("Menù consultazione " & datodettaglio.Nome)
            menulistavisualizza.multiselezioneConsentita = True
            menulistavisualizza.ciclono = True

            Dim listaDati As List(Of String) = datodettaglio.Valori.Select(Function(x) If(x.Nome = "Elemento", x.Valore, x.Nome & " " & x.Valore)).ToList()
            menulistavisualizza.CaricaElementiMassiviNoID(listaDati)

            menulistavisualizza.aggiungiOpzione("Copia i valori selezionati", AddressOf copiavaloriselezionati)
            menulistavisualizza.aggiungiOpzione("Modifica valori dato", AddressOf modificavalori)
            menulistavisualizza.aggiungiOpzione("Modifica nome dato", AddressOf modificanome)
            menulistavisualizza.aggiungiOpzione("Elimina dato", AddressOf elimina)
            menulistavisualizza.aggiungiOpzione("Copia tutti i valori", AddressOf copiavalori)
            AddHandler menulistavisualizza.escPremuto, AddressOf chiudivisualizzazione
            menulistavisualizza.avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in visualizzadato: " & ex.Message)
        End Try
    End Sub

    Private Sub chiudivisualizzazione()
        Try
            If menulistavisualizza IsNot Nothing Then menulistavisualizza.abbandona()
            menulistavisualizza = Nothing
        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante la chiusura della visualizzazione: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in chiudivisualizzazione: " & ex.Message & " StackTrace: " & ex.StackTrace)
        End Try
    End Sub

    Private Sub copiavaloriselezionati()
        Try
            If menulistavisualizza Is Nothing Then Return

            Dim id As List(Of String) = menulistavisualizza.ottieniElementi()
            If id Is Nothing OrElse id.Count = 0 Then
                MostraMessaggio("Non è stato selezionato nessun valore.")
                Return
            End If

            Dim testo As New StringBuilder()
            For Each elemento As String In id
                testo.AppendLine(elemento)
            Next

            Clipboard.SetText(testo.ToString())

            If id.Count = 1 Then
                MostraMessaggio("Valore copiato negli appunti.")
            Else
                MostraMessaggio("Copiati negli appunti " & id.Count.ToString() & " valori.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in copiavaloriselezionati: " & ex.Message)
        End Try
    End Sub

    Private Sub copiavalori()
        Try
            Dim dato As Dato = datiriservati.VisualizzaDato(indice, password)
            If dato IsNot Nothing Then
                Dim sb As New System.Text.StringBuilder()
                sb.AppendLine("Nome: " & dato.Nome)
                For Each val As Valore In dato.Valori
                    sb.AppendLine(val.Nome & ": " & val.Valore)
                Next
                Clipboard.SetText(sb.ToString())
                MostraMessaggio("Valori copiati.")
            End If
        Catch ex As Exception
            _logger.ScriviLog("Exception in copiavalori: " & ex.Message)
        End Try
    End Sub

    Private Sub elimina()
        Try
            _idDaEliminare = New List(Of Integer)
            _idDaEliminare.Add(indice)
            domanda = New WGDomanda()
            AddHandler domanda.rispostadomanda, AddressOf rispostaelimina
            domanda.avvia("Sei sicuro di voler eliminare questo dato?")
        Catch ex As Exception
            _logger.ScriviLog("Exception in elimina: " & ex.Message)
        End Try
    End Sub

    Private Sub rispostaelimina(dialog As DialogResult)
        Try
            If dialog = DialogResult.Yes Then
                Dim eliminati As Integer = 0
                Dim falliti As Integer = 0

                For Each id As Integer In _idDaEliminare
                    Try
                        If datiriservati.EliminaDato(id, password) Then
                            eliminati += 1
                        Else
                            falliti += 1
                        End If
                    Catch exSingolo As Exception
                        falliti += 1
                        _logger.ScriviLog("Exception in rispostaelimina (dato " & id.ToString() & "): " & exSingolo.Message)
                    End Try
                Next

                If eliminati > 0 AndAlso falliti = 0 Then
                    If eliminati = 1 Then
                        MostraMessaggio("Eliminazione completata.")
                    Else
                        MostraMessaggio("Eliminati " & eliminati.ToString() & " dati.")
                    End If
                ElseIf eliminati > 0 AndAlso falliti > 0 Then
                    MostraMessaggio("Eliminati " & eliminati.ToString() & " dati, mentre " & falliti.ToString() & " non sono stati eliminati.")
                Else
                    MostraMessaggio("Errore eliminazione.")
                End If

                If eliminati > 0 Then
                    RicaricaElenco()
                End If
            End If

            If domanda IsNot Nothing Then
                RemoveHandler domanda.rispostadomanda, AddressOf rispostaelimina
                domanda = Nothing
            End If

            _idDaEliminare = New List(Of Integer)
        Catch ex As Exception
            _logger.ScriviLog("Exception in rispostaelimina: " & ex.Message)
        End Try
    End Sub

    Private Sub modificanome()
        Try
            singolodato = datiriservati.VisualizzaDato(indice, password)
            If singolodato Is Nothing Then
                MostraMessaggio("Dato non trovato o password errata.")
                Return
            End If

            campi = New Campi()
            campi.Aggiungi("Scrivi il nuovo nome", "c.nomenuovo", True, , singolodato.Nome)
            AddHandler campi.Fine, AddressOf inserimentonomenuovo
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in modificanome: " & ex.Message)
        End Try
    End Sub

    Private Sub inserimentonomenuovo()
        Try
            singolodato.Nome = campi.RecuperaValore("c.nomenuovo")
            If datiriservati.ModificaDato(indice, singolodato, password) Then
                MostraMessaggio("Nome modificato.")
                RicaricaElenco()
            Else
                MostraMessaggio("Errore modifica.")
            End If
            campi = Nothing
            singolodato = Nothing
        Catch ex As Exception
            _logger.ScriviLog("Exception in inserimentonomenuovo: " & ex.Message)
        End Try
    End Sub

    Private Sub modificavalori()
        Try
            Dim dato As Dato = datiriservati.VisualizzaDato(indice, password)
            If dato Is Nothing Then
                MostraMessaggio("Dato non trovato o password errata.")
                Return
            End If

            indice2 = indice
            nome = dato.Nome
            campi = New Campi()
            nomi = New List(Of modificadato)
            Dim i As Integer = 1
            For Each el In dato.Valori
                If StringaNumerica(el.Valore) Then
                    campi.AggiungiNumerico(el.Nome, i.ToString(), True, , el.Valore)
                ElseIf DataValida(el.Valore) Then
                    campi.AggiungiData(el.Nome, i.ToString(), True, , el.Valore)
                Else
                    campi.Aggiungi(el.Nome, i.ToString(), True, , el.Valore)
                End If
                nomi.Add(New modificadato With {.nome = el.Nome, .campo = i})
                i += 1
            Next
            AddHandler campi.Fine, AddressOf modificavaloricampi
            campi.Avvia()
        Catch ex As Exception
            _logger.ScriviLog("Exception in modificavalori: " & ex.Message)
        End Try
    End Sub

    Private Sub modificavaloricampi()
        Try
            Dim dato As New Dato
            dato.Nome = nome
            For Each n In nomi
                dato.Valori.Add(New Valore With {.Nome = n.nome, .Valore = campi.RecuperaValore(n.campo.ToString())})
            Next
            If datiriservati.ModificaDato(indice2, dato, password) Then
                MostraMessaggio("Modifica avvenuta.")
                RicaricaElenco()
            Else
                MostraMessaggio("Errore modifica.")
            End If
            campi = Nothing
            nomi = Nothing
        Catch ex As Exception
            _logger.ScriviLog("Exception in modificavaloricampi: " & ex.Message)
        End Try
    End Sub

    Private Function StringaNumerica(input As String) As Boolean
        Try
            If String.IsNullOrEmpty(input) Then
                Return False
            End If
            For Each carattere As Char In input
                If Not Char.IsDigit(carattere) Then
                    Return False
                End If
            Next
            Return True
        Catch ex As Exception
            _logger.ScriviLog("Exception in StringaNumerica: " & ex.Message & " Input: " & input & " StackTrace: " & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Function DataValida(input As String) As Boolean
        Try
            Dim data As DateTime
            Return DateTime.TryParse(input, data)
        Catch ex As Exception
            _logger.ScriviLog("Exception in DataValida: " & ex.Message & " Input: " & input & " StackTrace: " & ex.StackTrace)
            Return False
        End Try
    End Function

    Private Sub impostapasswordcampi()
        Try
            Dim p1 As String = campi.RecuperaValore("p1")
            Dim p2 As String = campi.RecuperaValore("p2")
            campi = Nothing

            If String.IsNullOrEmpty(p1) Or String.IsNullOrEmpty(p2) Then
                MostraMessaggio("Password non inserita.")
                InizializzaNuovaPassword()
                Return
            End If

            If p1 <> p2 Then
                MostraMessaggio("Le password non corrispondono.")
                InizializzaNuovaPassword()
                Return
            End If

            password = p1
            If datiriservati.ImpostaPassword(password) Then
                AvviaMenuPrincipale()
            Else
                MostraMessaggio("Si è verificato un errore durante l'impostazione della password.")
            End If
        Catch ex As CryptographicException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore di crittografia durante l'impostazione della password: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("CryptographicException in impostapasswordcampi: " & ex.Message & " StackTrace: " & ex.StackTrace)

        Catch ex As NullReferenceException
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Errore: Riferimento a oggetto non impostato durante l'impostazione della password: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("NullReferenceException in impostapasswordcampi: " & ex.Message & " StackTrace: " & ex.StackTrace)

        Catch ex As Exception
            Dim msgBox As New WGMessaggi()
            msgBox.Messaggio("Si è verificato un errore inatteso durante l'impostazione della password per l'area riservata: " & ex.Message)
            msgBox.ShowDialog()
            _logger.ScriviLog("Exception in impostapasswordcampi: " & ex.Message & " StackTrace: " & ex.StackTrace)

        End Try
    End Sub

End Class