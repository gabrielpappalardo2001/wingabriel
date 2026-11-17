Imports System.Diagnostics
Imports System.IO
Imports System.Text
Imports wgcontrols
Imports WGFileManager

''' <summary>Impostazioni di Wikipedia, salvate con WGConfig.</summary>
Public NotInheritable Class ImpostazioniWikipedia
    Public Const IdVisualizzazione As String = "wikipedia.visualizzazione"
    Public Const ValoreCampoDiTesto As String = "testo"
    Public Const ValoreBrowser As String = "browser"

    Private Shared ReadOnly _log As New Logger()

    Private Sub New()
    End Sub

    ''' <summary>False = il testo si legge in un campo di testo (predefinito), True = si apre nel browser.</summary>
    Public Shared Property VisualizzaNelBrowser As Boolean
        Get
            Try
                Dim configurazione As New WGConfig()
                Dim valore As String = configurazione.LeggiValore(IdVisualizzazione)
                Return String.Equals(If(valore, "").Trim(), ValoreBrowser, StringComparison.OrdinalIgnoreCase)
            Catch ex As Exception
                _log.ScriviLog("ImpostazioniWikipedia.VisualizzaNelBrowser (lettura): " & ex.Message)
                Return False
            End Try
        End Get
        Set(value As Boolean)
            Try
                Dim configurazione As New WGConfig()
                configurazione.SalvaValore(IdVisualizzazione, If(value, ValoreBrowser, ValoreCampoDiTesto))
            Catch ex As Exception
                _log.ScriviLog("ImpostazioniWikipedia.VisualizzaNelBrowser (scrittura): " & ex.Message)
            End Try
        End Set
    End Property
End Class

Public Class MenuWikipedia
    ''' <summary>
    ''' Da impostare all'avvio con l'apertura del browser di WinGabriel, per esempio
    ''' MenuWikipedia.ApriHtmlNelBrowser = Sub(indirizzo) gestore.Apri(indirizzo, False)
    ''' Riceve l'indirizzo file:// della pagina. Se resta vuoto si usa il browser predefinito di Windows.
    ''' </summary>
    Public Shared Property ApriHtmlNelBrowser As Action(Of String)

    Private Const IdTuttaLaVoce As String = "*"
    Private Const IdIntroduzione As String = "0"

    Private ReadOnly _log As New Logger()
    Private campo1 As WGCampoTesto
    Private campo2 As WGCampo
    Private risultati As WGMenuLista
    Private indicepagina As WGMenuLista
    Private WithEvents wikipediainterop As New WikipediaAPI
    Private idpagina As String = ""
    Private titolopagina As String = ""
    Private campo1Gestito As Boolean
    Private contenutoCorrente As WikipediaContenuto
    Private ReadOnly titoliRisultati As New Dictionary(Of String, String)()
    Private ReadOnly titoliSezioni As New Dictionary(Of String, String)()

    Public Sub New()
        MostraMenuPrincipale()
    End Sub

    Private Sub Annuncia(testo As String)
        Try
            MostraMessaggio(testo)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.Annuncia: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMessaggio(testo As String)
        Try
            Dim messaggio As New WGMessaggi()
            messaggio.Messaggio(testo)
            messaggio.ShowDialog()
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraMessaggio: " & ex.Message)
        End Try
    End Sub

    Private Sub MostraMenuPrincipale()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Ricerca", Sub(s, e) AvviaRicerca())
            menu.aggiungi("Preferiti", Sub(s, e) MostraPreferiti())
            menu.aggiungi("Recenti", Sub(s, e) MostraRecenti())
            menu.aggiungi("Configurazione", Sub(s, e) MostraConfigurazione())
            menu.avvia("Wikipedia", True)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraMenuPrincipale: " & ex.ToString())
        End Try
    End Sub

#Region "Configurazione"

    Private Sub MostraConfigurazione(Optional indice As Integer = 0)
        Try
            Dim menu As New WGMenu()
            Dim modo As String = If(ImpostazioniWikipedia.VisualizzaNelBrowser, "nel browser", "in un campo di testo")
            menu.aggiungi("Visualizzazione del testo: " & modo, Sub(s, e) ScegliVisualizzazione())
            menu.posiziona(indice)
            menu.avvia("Configurazione di Wikipedia", True)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraConfigurazione: " & ex.ToString())
        End Try
    End Sub

    Private Sub ScegliVisualizzazione()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("In un campo di testo", Sub(s, e) ImpostaVisualizzazione(False))
            menu.aggiungi("Nel browser", Sub(s, e) ImpostaVisualizzazione(True))
            menu.posiziona(If(ImpostazioniWikipedia.VisualizzaNelBrowser, 1, 0))
            menu.avvia("Dove visualizzare il testo di Wikipedia", True)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.ScegliVisualizzazione: " & ex.ToString())
        End Try
    End Sub

    Private Sub ImpostaVisualizzazione(nelBrowser As Boolean)
        Try
            ImpostazioniWikipedia.VisualizzaNelBrowser = nelBrowser
            Annuncia(If(nelBrowser, "Il testo di Wikipedia verrà aperto nel browser", "Il testo di Wikipedia verrà mostrato in un campo di testo"))
            MostraConfigurazione(0)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.ImpostaVisualizzazione: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Ricerca"

    Private Sub AvviaRicerca()
        Try
            Dim verificacon As New connessione()
            If verificacon.VerificaConnessione() = True Then
                campo1Gestito = False
                campo1 = New WGCampoTesto()
                campo1.init("Termine da cercare su wikipedia")
                AddHandler campo1.chiusura, AddressOf finecampo
                campo1.avvia()
            Else
                MostraMessaggio("La connessione ad internet non risulta disponibile, non è possibile quindi accedere al servizio di wikipedia")
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.AvviaRicerca: " & ex.ToString())
        End Try
    End Sub

    Private Sub finecampo()
        Try
            If campo1Gestito Then Return
            campo1Gestito = True
            Dim testo As String = campo1.ottienitesto()
            campo1.chiudi()
            If String.IsNullOrWhiteSpace(testo) Then Return
            CercaTesto(testo.Trim())
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.finecampo: " & ex.ToString())
        End Try
    End Sub

    Private Sub CercaTesto(testo As String)
        Try
            ultimaRicerca = testo
            ArchivioWikipedia.Istanza.Aggiungi(ArchivioWikipedia.RicercheRecenti, testo)
            wikipediainterop.SearchTerm(testo)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.CercaTesto: " & ex.ToString())
        End Try
    End Sub

    Private Sub ricercafinita(sender As Object, e As List(Of Wikipedia)) Handles wikipediainterop.RicercaTerminata
        Try
            titoliRisultati.Clear()
            risultati = New WGMenuLista()
            risultati.multiselezioneconsentita = False
            risultati.ImpostaTitolo("Risultati ricerca wikipedia")
            For Each elemento In e
                titoliRisultati(elemento.Id) = elemento.Title
                risultati.AggiungiElemento(elemento.Title, elemento.Id)
            Next
            AddHandler risultati.ElementoSelezionato, AddressOf selezionato
            AddHandler risultati.escpremuto, AddressOf chiudirisultati
            AggiungiOpzioniPagina(risultati, Function()
                                                 Dim ids As List(Of String) = risultati.ottieniElementi()
                                                 Return If(ids IsNot Nothing AndAlso ids.Count > 0, ids(0), "")
                                             End Function)
            risultati.aggiungiOpzione("Aggiungi la ricerca ai preferiti", Sub(s2, e2) AggiungiRicercaAiPreferiti(ultimaRicerca))
            risultati.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.ricercafinita: " & ex.ToString())
        End Try
    End Sub

    Private Sub ricercanonfinita(sender As Object, e As EventArgs) Handles wikipediainterop.nontrovato
        MostraMessaggio("Nessun risultato trovato")
    End Sub

#End Region

#Region "Indice e contenuto"

    Private Sub selezionato(id As String)
        Try
            idpagina = id
            titolopagina = ""
            titoliRisultati.TryGetValue(id, titolopagina)
            ArchivioWikipedia.Istanza.Aggiungi(ArchivioWikipedia.PagineRecenti, If(titolopagina = "", "Pagina " & id, titolopagina), id)
            Dim listaindice As List(Of WikipediaSection) = wikipediainterop.GetPageSections(id)
            If listaindice.Count = 0 Then
                ' Voce senza sezioni: si apre direttamente per intero.
                MostraContenuto(Nothing, "")
                Return
            End If

            titoliSezioni.Clear()
            indicepagina = New WGMenuLista()
            indicepagina.ImpostaTitolo("Indice della pagina " & titolopagina)
            indicepagina.multiselezioneconsentita = False
            AddHandler indicepagina.ElementoSelezionato, AddressOf elementoselezionatopagina
            AddHandler indicepagina.escpremuto, AddressOf chiudiindicepagina
            AggiungiOpzioniPagina(indicepagina, Function() idpagina)
            indicepagina.AggiungiElemento("Tutta la voce", IdTuttaLaVoce)
            indicepagina.AggiungiElemento("Introduzione", IdIntroduzione)
            For Each indice As WikipediaSection In listaindice
                titoliSezioni(indice.Id) = indice.Title
                indicepagina.AggiungiElemento(indice.Etichetta, indice.Id)
            Next
            indicepagina.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.selezionato: " & ex.ToString())
        End Try
    End Sub

    Private Sub elementoselezionatopagina(id As String)
        Try
            If id = IdTuttaLaVoce Then
                MostraContenuto(Nothing, "")
            ElseIf id = IdIntroduzione Then
                MostraContenuto(IdIntroduzione, "Introduzione")
            Else
                Dim titolo As String = ""
                titoliSezioni.TryGetValue(id, titolo)
                MostraContenuto(id, titolo)
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.elementoselezionatopagina: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraContenuto(sezione As String, titoloSezione As String)
        Try
            Dim contenuto As WikipediaContenuto = wikipediainterop.GetContenuto(idpagina, sezione, titoloSezione)
            If contenuto Is Nothing OrElse String.IsNullOrWhiteSpace(contenuto.Testo) Then
                If contenuto Is Nothing OrElse Not contenuto.Errore Then MostraMessaggio("Non è stato recuperato alcun testo per la parte selezionata")
                Return
            End If
            contenutoCorrente = contenuto

            If ImpostazioniWikipedia.VisualizzaNelBrowser Then
                If ApriNelBrowser(contenuto) Then Return
                MostraMessaggio("Non è stato possibile aprire il browser: il testo viene mostrato in un campo di testo")
            End If
            MostraNelCampo(contenuto)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraContenuto: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraNelCampo(contenuto As WikipediaContenuto)
        Try
            campo2 = New WGCampo()
            AddHandler campo2.chiusura, AddressOf chiudicampo2
            campo2.init("Consultazione testo wikipedia", contenuto.Testo, True, True)
            campo2.aggiungiopzione("Apri nell'editor di testo", AddressOf aprieditor)
            campo2.aggiungiopzione("Apri nel browser", AddressOf apridacampo)
            campo2.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraNelCampo: " & ex.ToString())
        End Try
    End Sub

    Private Sub apridacampo()
        Try
            If contenutoCorrente Is Nothing OrElse Not ApriNelBrowser(contenutoCorrente) Then
                MostraMessaggio("Non è stato possibile aprire il testo nel browser")
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.apridacampo: " & ex.ToString())
        End Try
    End Sub

#End Region

#Region "Browser"

    ''' <summary>Salva la pagina html in una cartella temporanea e la apre nel browser.</summary>
    Private Function ApriNelBrowser(contenuto As WikipediaContenuto) As Boolean
        Try
            If contenuto Is Nothing OrElse String.IsNullOrWhiteSpace(contenuto.Html) Then Return False
            Dim cartella As String = Path.Combine(Path.GetTempPath(), "WinGabriel", "Wikipedia")
            Directory.CreateDirectory(cartella)
            PulisciVecchiFile(cartella)
            Dim nome As String = NomeFileSicuro(contenuto.Titolo) & "_" & DateTime.Now.ToString("yyyyMMddHHmmss") & ".html"
            Dim percorso As String = Path.Combine(cartella, nome)
            File.WriteAllText(percorso, contenuto.Html, New UTF8Encoding(True))

            Dim apri As Action(Of String) = ApriHtmlNelBrowser
            If apri IsNot Nothing Then
                apri(New Uri(percorso).AbsoluteUri)
            Else
                WGDb.AvvioDaUtente.Apri(percorso)
            End If
            Return True
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.ApriNelBrowser: " & ex.ToString())
            Return False
        End Try
    End Function

    Private Shared Function NomeFileSicuro(titolo As String) As String
        Dim sb As New StringBuilder()
        Dim invalidi As Char() = Path.GetInvalidFileNameChars()
        For Each c As Char In If(titolo, "")
            If Array.IndexOf(invalidi, c) >= 0 OrElse Char.IsWhiteSpace(c) Then
                sb.Append("_"c)
            Else
                sb.Append(c)
            End If
        Next
        Dim nome As String = sb.ToString().Trim("_"c)
        If nome = "" Then nome = "wikipedia"
        If nome.Length > 60 Then nome = nome.Substring(0, 60)
        Return nome
    End Function

    Private Sub PulisciVecchiFile(cartella As String)
        Try
            For Each file As String In Directory.GetFiles(cartella, "*.html")
                Try
                    If System.IO.File.GetLastWriteTime(file) < DateTime.Now.AddDays(-1) Then System.IO.File.Delete(file)
                Catch
                End Try
            Next
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.PulisciVecchiFile: " & ex.Message)
        End Try
    End Sub

#End Region


#Region "Preferiti e recenti"

    Private Const IndirizzoPagina As String = "https://it.wikipedia.org/?curid="
    Private ultimaRicerca As String = ""

    Private Sub MostraPreferiti()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Ricerche preferite", Sub(s, e) MostraElencoArchivio(ArchivioWikipedia.RicerchePreferite))
            menu.aggiungi("Pagine preferite", Sub(s, e) MostraElencoArchivio(ArchivioWikipedia.PaginePreferite))
            menu.avvia("Preferiti", True)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraPreferiti: " & ex.ToString())
        End Try
    End Sub

    Private Sub MostraRecenti()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Ricerche recenti", Sub(s, e) MostraElencoArchivio(ArchivioWikipedia.RicercheRecenti))
            menu.aggiungi("Pagine recenti", Sub(s, e) MostraElencoArchivio(ArchivioWikipedia.PagineRecenti))
            menu.avvia("Recenti", True)
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraRecenti: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Apre l'elenco di una tabella dell'archivio. Invio avvia la ricerca o apre la pagina.</summary>
    Private Sub MostraElencoArchivio(tabella As String)
        Try
            Dim voci As List(Of VoceArchivioWikipedia) = ArchivioWikipedia.Istanza.Elenco(tabella)
            Dim dinomi As String = NomeElenco(tabella)
            If voci.Count = 0 Then
                MostraMessaggio("Non ci sono " & dinomi)
                Return
            End If
            Dim paginaTabella As Boolean = tabella = ArchivioWikipedia.PagineRecenti OrElse tabella = ArchivioWikipedia.PaginePreferite
            Dim recenti As Boolean = tabella = ArchivioWikipedia.RicercheRecenti OrElse tabella = ArchivioWikipedia.PagineRecenti

            Dim lista As New WGMenuLista()
            lista.multiselezioneconsentita = False
            lista.ImpostaTitolo(Char.ToUpper(dinomi(0)) & dinomi.Substring(1))
            Dim perChiave As New Dictionary(Of String, VoceArchivioWikipedia)()
            For Each voce As VoceArchivioWikipedia In voci
                perChiave(voce.Chiave) = voce
                lista.AggiungiElemento(voce.Testo, voce.Chiave)
            Next
            Dim scelta As Func(Of VoceArchivioWikipedia) = Function()
                                                               Dim ids As List(Of String) = lista.ottieniElementi()
                                                               Dim voce As VoceArchivioWikipedia = Nothing
                                                               If ids IsNot Nothing AndAlso ids.Count > 0 Then perChiave.TryGetValue(ids(0), voce)
                                                               Return voce
                                                           End Function
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim voce As VoceArchivioWikipedia = Nothing
                                                      If Not perChiave.TryGetValue(id, voce) Then Return
                                                      If paginaTabella Then
                                                          titoliRisultati(voce.PaginaId) = voce.Testo
                                                          selezionato(voce.PaginaId)
                                                      Else
                                                          CercaTesto(voce.Testo)
                                                      End If
                                                  End Sub
            If paginaTabella Then
                lista.aggiungiOpzione("Apri la pagina nel browser", Sub(s, e)
                                                                        Dim voce As VoceArchivioWikipedia = scelta()
                                                                        If voce IsNot Nothing Then ApriPaginaNelBrowser(voce.PaginaId)
                                                                    End Sub)
            End If
            If recenti Then
                lista.aggiungiOpzione("Aggiungi ai preferiti", Sub(s, e)
                                                                   Dim voce As VoceArchivioWikipedia = scelta()
                                                                   If voce Is Nothing Then Return
                                                                   If paginaTabella Then
                                                                       AggiungiPaginaAiPreferiti(voce.PaginaId, voce.Testo)
                                                                   Else
                                                                       AggiungiRicercaAiPreferiti(voce.Testo)
                                                                   End If
                                                               End Sub)
            End If
            lista.aggiungiOpzione("Elimina", Sub(s, e)
                                                 Dim voce As VoceArchivioWikipedia = scelta()
                                                 If voce Is Nothing Then Return
                                                 ArchivioWikipedia.Istanza.Elimina(tabella, voce.Chiave)
                                                 lista.abbandona()
                                                 Annuncia("Eliminato")
                                                 If ArchivioWikipedia.Istanza.Elenco(tabella).Count > 0 Then MostraElencoArchivio(tabella)
                                             End Sub)
            If recenti Then
                lista.aggiungiOpzione("Svuota l'elenco", Sub(s, e)
                                                             ArchivioWikipedia.Istanza.Svuota(tabella)
                                                             lista.abbandona()
                                                             Annuncia("Elenco svuotato")
                                                         End Sub)
            End If
            AddHandler lista.escpremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.MostraElencoArchivio: " & ex.ToString())
        End Try
    End Sub

    Private Shared Function NomeElenco(tabella As String) As String
        Select Case tabella
            Case ArchivioWikipedia.RicerchePreferite : Return "ricerche preferite"
            Case ArchivioWikipedia.PaginePreferite : Return "pagine preferite"
            Case ArchivioWikipedia.RicercheRecenti : Return "ricerche recenti"
            Case Else : Return "pagine recenti"
        End Select
    End Function

    Private Sub AggiungiRicercaAiPreferiti(testo As String)
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return
            Annuncia(If(ArchivioWikipedia.Istanza.Aggiungi(ArchivioWikipedia.RicerchePreferite, testo), "Ricerca aggiunta ai preferiti", "La ricerca è già nei preferiti"))
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.AggiungiRicercaAiPreferiti: " & ex.ToString())
        End Try
    End Sub

    Private Sub AggiungiPaginaAiPreferiti(id As String, titolo As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            If String.IsNullOrWhiteSpace(titolo) Then titolo = "Pagina " & id
            Annuncia(If(ArchivioWikipedia.Istanza.Aggiungi(ArchivioWikipedia.PaginePreferite, titolo, id), "Pagina aggiunta ai preferiti", "La pagina è già nei preferiti"))
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.AggiungiPaginaAiPreferiti: " & ex.ToString())
        End Try
    End Sub

    ''' <summary>Apre la pagina vera di Wikipedia nel browser.</summary>
    Private Sub ApriPaginaNelBrowser(id As String)
        Try
            If String.IsNullOrWhiteSpace(id) Then Return
            Dim indirizzo As String = IndirizzoPagina & Uri.EscapeDataString(id)
            Dim apri As Action(Of String) = ApriHtmlNelBrowser
            If apri IsNot Nothing Then
                apri(indirizzo)
            Else
                WGDb.AvvioDaUtente.Apri(indirizzo)
            End If
        Catch ex As Exception
            _log.ScriviLog("MenuWikipedia.ApriPaginaNelBrowser: " & ex.ToString())
            MostraMessaggio("Non è stato possibile aprire la pagina nel browser")
        End Try
    End Sub

    ' Le opzioni di una pagina, comuni all'elenco dei risultati e all'indice della pagina.
    Private Sub AggiungiOpzioniPagina(lista As WGMenuLista, idDellaVoce As Func(Of String))
        lista.aggiungiOpzione("Apri la pagina nel browser", Sub(s, e) ApriPaginaNelBrowser(idDellaVoce()))
        lista.aggiungiOpzione("Aggiungi la pagina ai preferiti", Sub(s, e)
                                                                     Dim id As String = idDellaVoce()
                                                                     Dim titolo As String = ""
                                                                     titoliRisultati.TryGetValue(id, titolo)
                                                                     AggiungiPaginaAiPreferiti(id, titolo)
                                                                 End Sub)
    End Sub

#End Region

    Private Sub chiudicampo2()
        campo2.chiudi()
    End Sub

    Private Sub chiudirisultati()
        risultati.abbandona()
    End Sub

    Private Sub chiudiindicepagina()
        indicepagina.abbandona()
    End Sub

    Private Sub aprieditor()
        Dim testo As String = campo2.ottienitesto()
        If testo = "" Then
            MostraMessaggio("Non è stato possibile recuperare il testo dal campo")
        Else
            Dim editor As New editor()
            editor.avvia(, testo)
            campo2.chiudi()
        End If
    End Sub
End Class