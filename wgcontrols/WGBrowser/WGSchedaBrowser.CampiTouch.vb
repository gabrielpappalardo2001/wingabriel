' ============================================================================================
' WGSchedaBrowser - campi della pagina: opzioni come nei campi di WinGabriel e scrittura dal touch
' ============================================================================================
'
' Quando si è in un campo di testo della pagina, il menu opzioni del browser offre le stesse cose
' dei campi di WinGabriel: modificare il testo in un campo di WinGabriel (con controllo ortografico,
' trova e sostituisci, riassunto e il resto) e rimandarlo alla pagina, dettatura, traduzione,
' copia, taglia, incolla, seleziona tutto, cancella tutto, leggi il contenuto.
' La tastiera a schermo (WGTastieraTouch, gestita da WGBrowser) scrive nel campo con i metodi
' *DaTouch, che aggiornano il testo conosciuto del campo come la tastiera fisica.
' ============================================================================================
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports Newtonsoft.Json.Linq
Imports wgcontrols

Partial Public Class WGSchedaBrowser

    Private _dettaturaCampoPagina As WGDettatura
    Private _tastoDettaturaPagina As WGTasto

    ''' <summary>Vero in un campo di testo modificabile (non in una casella combinata).</summary>
    Friend ReadOnly Property InCampoDiTesto As Boolean
        Get
            Return inCampoTesto
        End Get
    End Property

    Friend ReadOnly Property InCampoPassword As Boolean
        Get
            Return inCampoTesto AndAlso campoPassword
        End Get
    End Property

    Friend Function TestoPrimaDelCursore() As String
        Try
            If Not inCampoTesto OrElse campoPassword Then Return ""
            Dim posizione As Integer = Math.Max(0, Math.Min(inizioSelezione, testoCampo.Length))
            Return testoCampo.Substring(0, posizione)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TestoPrimaDelCursore: " & ex.Message)
            Return ""
        End Try
    End Function

#Region "Scrittura dalla tastiera a schermo"

    Friend Sub InserisciDaTouch(carattere As String)
        Try
            If Not inCampoTesto OrElse String.IsNullOrEmpty(carattere) Then Return
            SegnaUso()
            InserisciCarattereNelCampo(carattere, Keys.None, False, False)
            GestisciCharMessaggio(carattere(carattere.Length - 1))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InserisciDaTouch: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Testo intero (dettatura, suggerimenti, incolla): va nel campo in una volta sola.</summary>
    Friend Async Function InserisciTestoDaTouchAsync(testo As String, Optional conAnnuncio As Boolean = True) As Task
        Try
            If Not inCampoTesto OrElse String.IsNullOrEmpty(testo) Then Return
            SegnaUso()
            If Not Await _cdp.InserisciTestoAsync(testo) Then
                _cdp.InviaCarattere(testo, 0, False, False)
            End If
            RegistraTestoInserito(testo)
            If conAnnuncio Then Annuncia(If(campoPassword, "Testo inserito", testo))
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.InserisciTestoDaTouchAsync: " & ex.Message)
        End Try
    End Function

    Friend Sub CancellaDaTouch()
        Try
            If Not inCampoTesto Then Return
            SegnaUso()
            CancellaCarattereNelCampo("back")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CancellaDaTouch: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Suggerimento scelto: toglie la parte di parola già scritta e mette la parola intera con uno spazio.</summary>
    Friend Async Function SostituisciParolaDaTouchAsync(parola As String, caratteriDaSostituire As Integer) As Task
        Try
            If Not inCampoTesto OrElse String.IsNullOrEmpty(parola) Then Return
            Dim posizione As Integer = Math.Max(0, Math.Min(inizioSelezione, testoCampo.Length))
            Dim quanti As Integer = Math.Max(0, Math.Min(caratteriDaSostituire, posizione))
            For i As Integer = 1 To quanti
                _cdp.InviaTasto(WGCdpInterazione.VK_BACK, 8, ModificatoriInput.Nessuno)
            Next
            If quanti > 0 Then
                testoCampo = testoCampo.Remove(posizione - quanti, quanti)
                inizioSelezione = posizione - quanti
                fineSelezione = inizioSelezione
                versioneTestoCampo += 1
            End If
            Await InserisciTestoDaTouchAsync(parola & " ", False)
            Annuncia(parola)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SostituisciParolaDaTouchAsync: " & ex.Message)
        End Try
    End Function

#End Region

#Region "Opzioni del campo"

    Private Async Function PremiConCtrlAsync(tasto As Keys) As Task
        Dim p As New PressioneTastoBrowser()
        p.Tasto = tasto
        p.Ctrl = True
        Await GestisciTastoConCtrl(p)
    End Function

    Friend Async Sub SelezionaTuttoIlCampo()
        Try
            If Not inCampoTesto Then Return
            Await PremiConCtrlAsync(Keys.A)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SelezionaTuttoIlCampo: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub CopiaTestoDelCampo()
        Try
            If Not inCampoTesto Then Return
            If campoPassword Then
                Annuncia("Il testo di una password non si copia")
                Return
            End If
            Dim stato As JObject = Await SincronizzaCampoAsync()
            If String.IsNullOrEmpty(testoCampo) Then
                Annuncia("Campo vuoto")
                Return
            End If
            Clipboard.SetText(testoCampo)
            Annuncia("Testo del campo copiato negli appunti")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CopiaTestoDelCampo: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub TagliaTestoDelCampo()
        Try
            If Not inCampoTesto OrElse campoPassword Then Return
            Await SincronizzaCampoAsync()
            If String.IsNullOrEmpty(testoCampo) Then
                Annuncia("Campo vuoto")
                Return
            End If
            Clipboard.SetText(testoCampo)
            Await SostituisciTestoCampoAsync("")
            Annuncia("Testo del campo tagliato")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TagliaTestoDelCampo: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub IncollaNelCampo()
        Try
            If Not inCampoTesto Then Return
            If Not Clipboard.ContainsText() Then
                Annuncia("Negli appunti non c'è testo")
                Return
            End If
            Await InserisciTestoDaTouchAsync(Clipboard.GetText(), False)
            Annuncia("Incollato")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.IncollaNelCampo: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub CancellaTuttoIlCampo()
        Try
            If Not inCampoTesto Then Return
            Await SostituisciTestoCampoAsync("")
            Annuncia("Campo svuotato")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.CancellaTuttoIlCampo: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub LeggiIlCampo()
        Try
            If Not InCampo Then Return
            Await SincronizzaCampoAsync()
            LeggiContenutoCampo()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.LeggiIlCampo: " & ex.Message)
        End Try
    End Sub

    Friend Sub EsciDalCampoInSilenzio()
        Try
            If InCampo Then EsciDalCampo(True)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EsciDalCampoInSilenzio: " & ex.Message)
        End Try
    End Sub

    Friend Sub EsciDalCampoDaOpzioni()
        Try
            If InCampo Then EsciDalCampo()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.EsciDalCampoDaOpzioni: " & ex.Message)
        End Try
    End Sub

    ' Sostituisce tutto il testo del campo: seleziona tutto e scrive sopra, così la pagina riceve
    ' gli stessi eventi di una digitazione (i moduli che controllano l'input se ne accorgono).
    Private Async Function SostituisciTestoCampoAsync(testo As String) As Task
        _cdp.InviaTasto(Asc("A"c), 0, ModificatoriInput.Control)
        Await AttendiInputAsync()
        If String.IsNullOrEmpty(testo) Then
            _cdp.InviaTasto(WGCdpInterazione.VK_DELETE, 0, ModificatoriInput.Nessuno)
        ElseIf Not Await _cdp.InserisciTestoAsync(testo) Then
            _cdp.InviaCarattere(testo, 0, False, False)
        End If
        Await AttendiInputAsync()
        Await Task.Delay(80)
        Dim stato As JObject = Await SincronizzaCampoAsync()
        If stato Is Nothing Then
            testoCampo = If(campoPassword, New String("*"c, testo.Length), testo)
            inizioSelezione = testoCampo.Length
            fineSelezione = inizioSelezione
            versioneTestoCampo += 1
        End If
    End Function

    ''' <summary>
    ''' Apre il testo del campo in un campo di WinGabriel, con tutte le sue funzioni; alla chiusura,
    ''' se il testo è cambiato, lo rimette nel campo della pagina.
    ''' </summary>
    Friend Async Sub ModificaCampoConWinGabriel()
        Try
            If Not inCampoTesto Then Return
            If campoPassword Then
                Annuncia("Le password si scrivono direttamente nel campo")
                Return
            End If
            Await SincronizzaCampoAsync()
            Dim originale As String = If(testoCampo, "")
            Dim campo As New WGCampo()
            AddHandler campo.chiusura, Sub(s As Object, e As EventArgs)
                                           Try
                                               Dim nuovo As String = campo.ottienitesto()
                                               campo.chiudi()
                                               If nuovo Is Nothing OrElse nuovo = originale Then Return
                                               SuUI(Async Sub()
                                                        Try
                                                            If Not inCampoTesto Then
                                                                Annuncia("Il campo della pagina non è più attivo: il testo è rimasto negli appunti")
                                                                Clipboard.SetText(nuovo)
                                                                Return
                                                            End If
                                                            Await SostituisciTestoCampoAsync(If(campoMultiriga, nuovo, nuovo.Replace(vbCrLf, " ").Replace(vbLf, " ")))
                                                            Annuncia("Testo del campo aggiornato")
                                                        Catch exScrivi As Exception
                                                            log.ScriviLog("WGSchedaBrowser.ModificaCampoConWinGabriel scrittura: " & exScrivi.Message)
                                                        End Try
                                                    End Sub)
                                           Catch exChiudi As Exception
                                               log.ScriviLog("WGSchedaBrowser.ModificaCampoConWinGabriel chiusura: " & exChiudi.Message)
                                           End Try
                                       End Sub
            campo.init("Testo del campo della pagina, alla chiusura torna nella pagina", originale, True, False)
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.ModificaCampoConWinGabriel: " & ex.Message)
        End Try
    End Sub

    Friend Async Sub TraduciTestoDelCampo()
        Try
            If Not inCampoTesto OrElse campoPassword Then Return
            Await SincronizzaCampoAsync()
            If String.IsNullOrWhiteSpace(testoCampo) Then
                Annuncia("Il campo è vuoto, non c'è nulla da tradurre")
                Return
            End If
            WGTraduttore.TraduciEMostra(testoCampo, "Traduzione del campo")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.TraduciTestoDelCampo: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Scrive nel campo il testo tradotto nella lingua di destinazione del traduttore, al posto dell'originale.</summary>
    Friend Async Sub SostituisciConTraduzione()
        Try
            If Not inCampoTesto OrElse campoPassword Then Return
            Await SincronizzaCampoAsync()
            If String.IsNullOrWhiteSpace(testoCampo) Then
                Annuncia("Il campo è vuoto, non c'è nulla da tradurre")
                Return
            End If
            Dim testoDaTradurre As String = testoCampo
            Dim risultato As WGTraduttore.Risultato = Await WGProcessoInCorso.EseguiAsync("Traduzione del testo del campo", Function() WGTraduttore.TraduciAsync(testoDaTradurre))
            If risultato Is Nothing OrElse Not risultato.Riuscito Then
                WGDomanda.Chiedi("Traduzione non riuscita" & If(risultato Is Nothing, "", ": " & risultato.Errore) & ". Vuoi riprovare?", AddressOf SostituisciConTraduzione)
                Return
            End If
            Await SostituisciTestoCampoAsync(risultato.Testo)
            Annuncia("Testo tradotto nel campo: " & risultato.Testo)
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.SostituisciConTraduzione: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Dettatura nel campo della pagina: si parla, un tasto qualsiasi ferma, il testo va nel campo.</summary>
    Friend Sub DettaNelCampo()
        Try
            If Not inCampoTesto Then Return
            If Not WGDettatura.SistemaSupportato Then
                Annuncia("La dettatura non è disponibile su questo sistema")
                Return
            End If
            If _dettaturaCampoPagina Is Nothing Then
                _dettaturaCampoPagina = New WGDettatura()
                AddHandler _dettaturaCampoPagina.TrascrizioneCompletata, Sub(testo As String)
                                                                             SuUI(Async Sub()
                                                                                      Try
                                                                                          NascondiTastoDettaturaPagina()
                                                                                          If String.IsNullOrWhiteSpace(testo) Then
                                                                                              Annuncia("Non ho sentito niente")
                                                                                              Return
                                                                                          End If
                                                                                          Await InserisciTestoDaTouchAsync(testo.Trim())
                                                                                      Catch exTesto As Exception
                                                                                          log.ScriviLog("WGSchedaBrowser.DettaNelCampo testo: " & exTesto.Message)
                                                                                      End Try
                                                                                  End Sub)
                                                                         End Sub
                AddHandler _dettaturaCampoPagina.TrascrizioneFallita, Sub(messaggio As String)
                                                                          SuUI(Sub()
                                                                                   NascondiTastoDettaturaPagina()
                                                                                   Annuncia(messaggio)
                                                                               End Sub)
                                                                      End Sub
            End If
            If _tastoDettaturaPagina Is Nothing Then
                _tastoDettaturaPagina = New WGTasto()
                AddHandler _tastoDettaturaPagina.Interrotto, Sub(s As Object, e As EventArgs)
                                                                 SuUI(Sub()
                                                                          NascondiTastoDettaturaPagina()
                                                                          If _dettaturaCampoPagina IsNot Nothing Then _dettaturaCampoPagina.FermaRegistrazione()
                                                                      End Sub)
                                                             End Sub
            End If
            _dettaturaCampoPagina.IniziaRegistrazione()
            _tastoDettaturaPagina.Mostra("Sto ascoltando. Parla ora. Premi un tasto qualsiasi per terminare la dettatura.")
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.DettaNelCampo: " & ex.Message)
        End Try
    End Sub

    Private Sub NascondiTastoDettaturaPagina()
        Try
            If _tastoDettaturaPagina IsNot Nothing Then _tastoDettaturaPagina.Chiudi()
        Catch ex As Exception
            log.ScriviLog("WGSchedaBrowser.NascondiTastoDettaturaPagina: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
