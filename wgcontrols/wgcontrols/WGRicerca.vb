' ============================================================================================
' WGRicerca - ricerca globale di WinGabriel (Windows S, modalità smart)
' ============================================================================================
'
' Si scrive cosa si cerca e si sceglie tra:
'   - le applicazioni aperte (ci si torna);
'   - le voci dei menu: la ricerca le impara da sola man mano che i menu vengono aperti, con le voci
'     da scegliere dal menu applicazioni per arrivarci, e le riapre ripetendo quelle scelte, come i
'     collegamenti della scrivania. Così aggiungere voci ai menu non richiede di cambiare niente;
'   - gli elementi della scrivania;
'   - i file e le cartelle recenti;
'   - in fondo, cercare lo stesso testo nei file e nelle cartelle del computer (CercaNeiFile, dalla
'     gestione file); se non si trova niente altro lo si propone con una domanda.
' Le voci dei menu stanno in dati\gui\gui.sqlite; quelle scelte più spesso vengono prima.
'
'   WGRicerca.Mostra()
' ============================================================================================
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports WGDb

Public NotInheritable Class WGRicerca

    Public Class Risultato
        Public Testo As String = ""
        Public Azione As Action
        Public Punteggio As Integer
    End Class

    Private Const Tabella As String = "RicercaVoci"
    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _bloccoDb As New Object()
    Private Shared _db As WGDatabase
    ' Percorsi già imparati in questa sessione, per non riscrivere il database a ogni apertura di menu.
    Private Shared ReadOnly _giaVisti As New HashSet(Of String)(StringComparer.Ordinal)

    Private Sub New()
    End Sub

    Private Shared Function Db() As WGDatabase
        SyncLock _bloccoDb
            If _db IsNot Nothing Then Return _db
            Try
                _db = WGGui.CreaTabella(Tabella, "Nome TEXT, Percorso TEXT, Usi INTEGER", "sistema", "ricerca.db", "voci")
            Catch ex As Exception
                log.ScriviLog("WGRicerca.Db: " & ex.Message)
            End Try
            Return _db
        End SyncLock
    End Function

#Region "Apprendimento delle voci dei menu"

    ''' <summary>Ricorda le voci di un menu raggiunto con le voci indicate (vuoto = menu applicazioni).</summary>
    Friend Shared Sub ImparaMenu(percorso As List(Of String), voci As List(Of String))
        Try
            If voci Is Nothing OrElse voci.Count = 0 Then Return
            Dim base As List(Of String) = If(percorso, New List(Of String)())
            Dim nuove As New List(Of Tuple(Of String, String))()
            SyncLock _giaVisti
                For Each voce As String In voci
                    Dim completo As String = String.Join(WGScrivania.SeparatoreMenu, base.Concat({voce}))
                    If _giaVisti.Add(completo) Then nuove.Add(Tuple.Create(voce, completo))
                Next
            End SyncLock
            If nuove.Count = 0 Then Return
            Task.Run(Sub()
                         Try
                             Dim d As WGDatabase = Db()
                             If d Is Nothing Then Return
                             For Each coppia As Tuple(Of String, String) In nuove
                                 Dim esiste As Object = d.EseguiScalare("SELECT 1 FROM " & Tabella & " WHERE Percorso = @p", New Dictionary(Of String, Object) From {{"@p", coppia.Item2}})
                                 If esiste IsNot Nothing AndAlso Not IsDBNull(esiste) Then Continue For
                                 d.InserisciRecord(Tabella, New Dictionary(Of String, Object) From {{"Nome", coppia.Item1}, {"Percorso", coppia.Item2}, {"Usi", 0}})
                             Next
                         Catch ex As Exception
                             log.ScriviLog("WGRicerca.ImparaMenu: " & ex.Message)
                         End Try
                     End Sub)
        Catch ex As Exception
            log.ScriviLog("WGRicerca.ImparaMenu: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub SegnaUso(percorso As String)
        Task.Run(Sub()
                     Try
                         Dim d As WGDatabase = Db()
                         If d IsNot Nothing Then d.EseguiNonQuery("UPDATE " & Tabella & " SET Usi = IFNULL(Usi,0) + 1 WHERE Percorso = @p", New Dictionary(Of String, Object) From {{"@p", percorso}})
                     Catch ex As Exception
                         log.ScriviLog("WGRicerca.SegnaUso: " & ex.Message)
                     End Try
                 End Sub)
    End Sub

#End Region

#Region "Ricerca"

    ''' <summary>Minuscole e senza accenti, per trovare "citta" scrivendo "città" e viceversa.</summary>
    Public Shared Function Normalizza(testo As String) As String
        Dim scomposto As String = If(testo, "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
        Dim sb As New StringBuilder(scomposto.Length)
        For Each c As Char In scomposto
            If CharUnicodeInfo.GetUnicodeCategory(c) <> UnicodeCategory.NonSpacingMark Then sb.Append(c)
        Next
        Return sb.ToString().Normalize(NormalizationForm.FormC).Trim()
    End Function

    ' Quanto bene il nome corrisponde: 0 = per niente; prima l'inizio del nome, poi l'inizio di una parola, poi dentro.
    Private Shared Function Corrispondenza(nome As String, cercato As String) As Integer
        Dim n As String = Normalizza(nome)
        If cercato = "" OrElse n = "" Then Return 0
        If n = cercato Then Return 1000
        If n.StartsWith(cercato, StringComparison.Ordinal) Then Return 800
        If (" " & n).Contains(" " & cercato) Then Return 600
        If n.Contains(cercato) Then Return 400
        ' Tutte le parole cercate presenti, in qualsiasi ordine.
        Dim parole As String() = cercato.Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
        If parole.Length > 1 AndAlso parole.All(Function(p) n.Contains(p)) Then Return 300
        Return 0
    End Function

    Public Shared Function Cerca(testo As String) As List(Of Risultato)
        Dim risultati As New List(Of Risultato)()
        Dim cercato As String = Normalizza(testo)
        If cercato = "" Then Return risultati
        Dim contenitore As WGContenitore = WGContenitore.Istanza

        ' Applicazioni aperte.
        Try
            If contenitore IsNot Nothing Then
                For Each nome As String In contenitore.NomiApplicazioniAperte()
                    Dim punti As Integer = Corrispondenza(nome, cercato)
                    If punti = 0 Then Continue For
                    Dim scelto As String = nome
                    risultati.Add(New Risultato() With {.Testo = nome & ", applicazione aperta", .Punteggio = punti + 200,
                                                        .Azione = Sub() contenitore.VaiAllApplicazioneConNome(scelto)})
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGRicerca.Cerca applicazioni: " & ex.Message)
        End Try

        ' Voci dei menu imparate.
        Try
            Dim d As WGDatabase = Db()
            If d IsNot Nothing Then
                Dim tabellaVoci As DataTable = d.EseguiQuery("SELECT Nome, Percorso, IFNULL(Usi,0) AS Usi FROM " & Tabella)
                For Each riga As DataRow In tabellaVoci.Rows
                    Dim nome As String = Convert.ToString(riga("Nome"))
                    Dim punti As Integer = Corrispondenza(nome, cercato)
                    If punti = 0 Then Continue For
                    Dim percorso As String = Convert.ToString(riga("Percorso"))
                    Dim voci As List(Of String) = percorso.Split(New String() {WGScrivania.SeparatoreMenu}, StringSplitOptions.None).ToList()
                    Dim dove As String = If(voci.Count > 1, ", in " & String.Join(", ", voci.Take(voci.Count - 1)), ", menu applicazioni")
                    risultati.Add(New Risultato() With {.Testo = nome & dove, .Punteggio = punti + Math.Min(150, Convert.ToInt32(riga("Usi")) * 10),
                                                        .Azione = Sub() ApriVoceDiMenu(voci)})
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGRicerca.Cerca menu: " & ex.Message)
        End Try

        ' Scrivania.
        Try
            For Each elemento As Tuple(Of String, Action) In WGScrivania.CercaElementi(cercato)
                risultati.Add(New Risultato() With {.Testo = elemento.Item1 & ", sulla scrivania", .Punteggio = Corrispondenza(elemento.Item1, cercato) + 100, .Azione = elemento.Item2})
            Next
        Catch ex As Exception
            log.ScriviLog("WGRicerca.Cerca scrivania: " & ex.Message)
        End Try

        ' File e cartelle recenti.
        Try
            For Each percorso As String In WGRecenti.Elenco()
                Dim nome As String = Path.GetFileName(percorso.TrimEnd("\"c))
                Dim punti As Integer = Corrispondenza(nome, cercato)
                If punti = 0 Then Continue For
                Dim scelto As String = percorso
                risultati.Add(New Risultato() With {.Testo = nome & If(Directory.Exists(percorso), ", cartella recente in ", ", file recente in ") & Path.GetDirectoryName(percorso.TrimEnd("\"c)),
                                                    .Punteggio = punti, .Azione = Sub() WGScrivania.ApriPercorso(scelto)})
            Next
        Catch ex As Exception
            log.ScriviLog("WGRicerca.Cerca recenti: " & ex.Message)
        End Try

        Return risultati.OrderByDescending(Function(r) r.Punteggio).ThenBy(Function(r) r.Testo).Take(60).ToList()
    End Function

    ' Riapre la voce ripetendo le scelte nei menu, in un'applicazione nuova (o torna a quella già aperta).
    Private Shared Async Sub ApriVoceDiMenu(voci As List(Of String))
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore Is Nothing OrElse voci.Count = 0 Then Return
            SegnaUso(String.Join(WGScrivania.SeparatoreMenu, voci))
            If voci.Count = 1 AndAlso contenitore.VaiAllApplicazioneConNome(voci(0)) Then Return
            contenitore.ApriComeNuovaApplicazione(voci(0), "menu:" & String.Join(WGScrivania.SeparatoreMenu, voci))
            If Not Await contenitore.EseguiPercorsoMenuAsync(voci) Then
                sintesivocale.GetInstance().LeggiTesto(voci(voci.Count - 1) & " non si trova più nei menu")
            End If
        Catch ex As Exception
            log.ScriviLog("WGRicerca.ApriVoceDiMenu: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Finestra di ricerca"

    Private Shared _ultimaRicerca As String = ""

    ''' <summary>Cerca il testo nei file e nelle cartelle del computer (lo imposta il programma principale).</summary>
    Public Shared Property CercaNeiFile As Action(Of String)

    ''' <summary>Windows S: chiede cosa cercare e mostra i risultati.</summary>
    Public Shared Sub Mostra()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New Action(AddressOf Mostra))
                Return
            End If
            Dim campi As New Campi()
            campi.Aggiungi("Cerca in WinGabriel: applicazioni, voci dei menu, scrivania e file recenti", "testo", True, False, _ultimaRicerca)
            AddHandler campi.Fine, Sub(s, e)
                                       Try
                                           Dim testo As String = campi.RecuperaValore("testo")
                                           If String.IsNullOrWhiteSpace(testo) Then Return
                                           _ultimaRicerca = testo.Trim()
                                           MostraRisultati(_ultimaRicerca)
                                       Catch exFine As Exception
                                           log.ScriviLog("WGRicerca.Mostra fine: " & exFine.Message)
                                       End Try
                                   End Sub
            campi.Avvia()
        Catch ex As Exception
            log.ScriviLog("WGRicerca.Mostra: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraRisultati(testo As String)
        Dim risultati As List(Of Risultato) = Cerca(testo)
        Dim neiFile As Action(Of String) = CercaNeiFile
        If risultati.Count = 0 Then
            If neiFile IsNot Nothing Then
                WGDomanda.Chiedi("Nessun risultato per " & testo & " tra applicazioni, menu, scrivania e recenti. Vuoi cercarlo nei file e nelle cartelle del computer?", Sub() neiFile.Invoke(testo))
            Else
                WGDomanda.Chiedi("Nessun risultato per " & testo & ". La ricerca impara le voci dei menu man mano che li apri. Vuoi cercare qualcos'altro?", Sub() Mostra())
            End If
            Return
        End If
        Dim trovati As Integer = risultati.Count
        If neiFile IsNot Nothing Then
            risultati.Add(New Risultato() With {.Testo = "Cerca " & testo & " nei file e nelle cartelle del computer", .Azione = Sub() neiFile.Invoke(testo)})
        End If
        Dim lista As New WGMenuLista()
        lista.multiselezioneConsentita = False
        lista.impostaTitolo("Risultati")
        For i As Integer = 0 To risultati.Count - 1
            lista.aggiungiElemento(risultati(i).Testo, i.ToString())
        Next
        AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                  Dim numero As Integer
                                                  If Not Integer.TryParse(id, numero) OrElse numero < 0 OrElse numero >= risultati.Count Then Return
                                                  lista.abbandona()
                                                  Dim azione As Action = risultati(numero).Azione
                                                  If azione IsNot Nothing Then azione()
                                              End Sub
        lista.aggiungiOpzione("Cerca di nuovo", Sub(s, e)
                                                    lista.abbandona()
                                                    Mostra()
                                                End Sub)
        AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
        lista.avvia()
    End Sub

#End Region

End Class
