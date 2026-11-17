Imports System
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports wgcontrols
Imports WGNavidrome

' Configurazione delle preferenze di ascolto e gestione delle opzioni avanzate per il server musicale Navidrome.
Partial Public Class MenuMediaPlayer

    Private Const CAMPO_INDIRIZZO As String = "navidrome.indirizzo"
    Private Const CAMPO_UTENTE As String = "navidrome.utente"
    Private Const CAMPO_PASSWORD As String = "navidrome.password"

    Private Sub MostraOpzioni()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Apertura dei file multimediali", AddressOf ScegliModalitaApertura)
            menu.aggiungi("Ordine dei brani preferiti", AddressOf ScegliOrdinePreferiti)
            menu.aggiungi("Ordine dei brani recenti", AddressOf ScegliOrdineRecenti)
            menu.aggiungi("Opzioni avanzate", AddressOf MostraOpzioniAvanzate)
            menu.avvia("Opzioni del Media player")
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.MostraOpzioni: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Preferenze di ascolto (le stesse di Gestione file)
    ' ------------------------------------------------------------------------------------------

    Private Sub ScegliModalitaApertura()
        Try
            Dim valori As String() = {"", "Sottofondo", "Player"}
            Dim messaggi As String() = {
                "Verrà chiesta la modalità di apertura di ogni file multimediale.",
                "I file multimediali verranno sempre ascoltati in sottofondo.",
                "I file multimediali apriranno sempre il lettore multimediale."}
            Dim voci As New List(Of KeyValuePair(Of String, Action))
            Dim nomi As String() = {"Chiedi ogni volta", "Ascolta sempre in sottofondo", "Apri sempre il lettore multimediale"}
            For i As Integer = 0 To valori.Length - 1
                Dim valore As String = valori(i)
                Dim messaggio As String = messaggi(i)
                voci.Add(MediaUtil.Voce(nomi(i), Sub()
                                                     MediaPlayerImpostazioni.Salva(MediaPlayerImpostazioni.CHIAVE_MODALITA_APERTURA, valore)
                                                     MediaUtil.Messaggio(messaggio)
                                                 End Sub))
            Next
            Dim attuale As Integer = Array.IndexOf(valori, MediaPlayerImpostazioni.ModalitaApertura())
            MediaUtil.Scegli("Apertura dei file multimediali", voci, Math.Max(attuale, 0))
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.ScegliModalitaApertura: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliOrdinePreferiti()
        ScegliOrdine("Ordine dei brani preferiti", MediaPlayerImpostazioni.CHIAVE_ORDINE_PREFERITI, "DataAggiunta",
                     {"DataAggiunta", "Nome", "Durata"},
                     {"Per data di aggiunta", "In ordine alfabetico", "Per durata"},
                     {"I brani preferiti sono ordinati per data di aggiunta.",
                      "I brani preferiti sono in ordine alfabetico.",
                      "I brani preferiti sono ordinati per durata."})
    End Sub

    Private Sub ScegliOrdineRecenti()
        ScegliOrdine("Ordine dei brani recenti", MediaPlayerImpostazioni.CHIAVE_ORDINE_RECENTI, "Data",
                     {"Data", "Nome"},
                     {"Dal più recente", "In ordine alfabetico"},
                     {"I brani recenti partono dal più recente.",
                      "I brani recenti sono in ordine alfabetico."})
    End Sub

    Private Sub ScegliOrdine(titolo As String, chiave As String, predefinito As String, valori As String(), nomi As String(), messaggi As String())
        Try
            Dim voci As New List(Of KeyValuePair(Of String, Action))
            For i As Integer = 0 To valori.Length - 1
                Dim valore As String = valori(i)
                Dim messaggio As String = messaggi(i)
                voci.Add(MediaUtil.Voce(nomi(i), Sub()
                                                     MediaPlayerImpostazioni.Salva(chiave, valore)
                                                     MediaUtil.Messaggio(messaggio)
                                                 End Sub))
            Next
            Dim attuale As Integer = Array.IndexOf(valori, MediaPlayerImpostazioni.Leggi(chiave, predefinito))
            MediaUtil.Scegli(titolo, voci, Math.Max(attuale, 0))
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.ScegliOrdine: " & ex.ToString())
        End Try
    End Sub

    ' ------------------------------------------------------------------------------------------
    ' Opzioni avanzate: il server Navidrome
    ' ------------------------------------------------------------------------------------------

    Private Sub MostraOpzioniAvanzate()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Server Navidrome", AddressOf MostraOpzioniNavidrome)
            menu.avvia("Opzioni avanzate")
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.MostraOpzioniAvanzate: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraOpzioniNavidrome()
        Try
            Dim configurazione As NavidromeConfigurazione = NavidromeConfigurazione.Carica()
            Dim menu As New WGMenu()
            If configurazione.Configurato Then
                menu.aggiungi("Verifica il collegamento", AddressOf VerificaCollegamentoNavidrome)
                menu.aggiungi("Modifica il collegamento", AddressOf ModificaCollegamentoNavidrome)
                menu.aggiungi("Scollega il server", AddressOf ScollegaNavidrome)
                menu.avvia("Server Navidrome, collegato a " & configurazione.Indirizzo & " come " & configurazione.Utente)
            Else
                menu.aggiungi("Collega il server", AddressOf ModificaCollegamentoNavidrome)
                menu.avvia("Server Navidrome, non collegato")
            End If
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.MostraOpzioniNavidrome: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub VerificaCollegamentoNavidrome()
        Try
            Dim configurazione As NavidromeConfigurazione = NavidromeConfigurazione.Carica()
            Dim client As NavidromeClient = configurazione.CreaClient()
            Dim info As NavidromeInfoServer = Nothing
            Try
                info = Await WGProcessoInCorso.EseguiAsync("Verifico il collegamento a Navidrome", Function() client.VerificaAsync())
            Catch ex As NavidromeEccezione
                MediaUtil.Messaggio(ex.Message)
                Return
            End Try
            MediaUtil.Messaggio(DescriviServer(info, configurazione))
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.VerificaCollegamentoNavidrome: " & ex.ToString())
            MediaUtil.Messaggio("Si è verificato un errore durante la verifica del collegamento.")
        End Try
    End Sub

    Private Shared Function DescriviServer(info As NavidromeInfoServer, configurazione As NavidromeConfigurazione) As String
        Dim testo As String = "Collegamento riuscito a " & configurazione.Indirizzo & " come " & configurazione.Utente & "."
        If info IsNot Nothing AndAlso info.VersioneServer <> "" Then
            testo &= " " & If(info.Tipo <> "", info.Tipo, "Server") & " versione " & info.VersioneServer & "."
        End If
        Return testo
    End Function

    Private Sub ModificaCollegamentoNavidrome()
        Try
            Dim attuale As NavidromeConfigurazione = NavidromeConfigurazione.Carica()
            Dim campi As New Campi()
            campi.Aggiungi("Indirizzo del server, per esempio 192.168.1.10:4533", CAMPO_INDIRIZZO, True, False, attuale.Indirizzo)
            campi.Aggiungi("Nome utente", CAMPO_UTENTE, True, False, attuale.Utente)
            campi.AggiungiPassword(If(attuale.Completa, "Password, lascia vuoto per non cambiarla", "Password"), CAMPO_PASSWORD, Not attuale.Completa)
            AddHandler campi.Fine, Sub() ConfermaCollegamentoNavidrome(campi, attuale)
            campi.Avvia()
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.ModificaCollegamentoNavidrome: " & ex.ToString())
        End Try
    End Sub

    Private Async Sub ConfermaCollegamentoNavidrome(campi As Campi, attuale As NavidromeConfigurazione)
        Try
            Dim nuova As New NavidromeConfigurazione()
            nuova.Indirizzo = NavidromeClient.NormalizzaIndirizzo(campi.RecuperaValore(CAMPO_INDIRIZZO))
            nuova.Utente = If(campi.RecuperaValore(CAMPO_UTENTE), "").Trim()
            Dim password As String = campi.RecuperaValore(CAMPO_PASSWORD)
            nuova.Password = If(String.IsNullOrEmpty(password), attuale.Password, password)
            nuova.Attivo = True

            If Not nuova.Completa Then
                MediaUtil.Messaggio("Servono l'indirizzo del server, il nome utente e la password.")
                Return
            End If

            Dim client As NavidromeClient = nuova.CreaClient()
            Dim info As NavidromeInfoServer = Nothing
            Try
                info = Await WGProcessoInCorso.EseguiAsync("Mi collego a Navidrome", Function() client.VerificaAsync())
            Catch ex As NavidromeEccezione
                MediaUtil.Messaggio(ex.Message)
                Return
            End Try

            nuova.Salva()
            AggiornaVoceNavidrome(True)
            MediaUtil.Messaggio(DescriviServer(info, nuova) & " Ora trovi la voce Navidrome nel menu del Media player.")
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.ConfermaCollegamentoNavidrome: " & ex.ToString())
            MediaUtil.Messaggio("Non è stato possibile salvare il collegamento a Navidrome.")
        End Try
    End Sub

    Private Sub ScollegaNavidrome()
        Try
            WGDomanda.Chiedi("Scollegare il server Navidrome? Nome utente e password verranno dimenticati.",
                             Sub()
                                 Try
                                     Dim configurazione As NavidromeConfigurazione = NavidromeConfigurazione.Carica()
                                     configurazione.Scollega()
                                     AggiornaVoceNavidrome(False)
                                     MediaUtil.Messaggio("Server Navidrome scollegato.")
                                 Catch ex As Exception
                                     MediaUtil.Log("MenuMediaPlayer.ScollegaNavidrome: " & ex.ToString())
                                 End Try
                             End Sub)
        Catch ex As Exception
            MediaUtil.Log("MenuMediaPlayer.ScollegaNavidrome: " & ex.ToString())
        End Try
    End Sub

End Class
