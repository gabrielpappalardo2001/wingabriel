' ============================================================================================
' WGRicercaFile - ricerca di file e cartelle per nome, in sottofondo, con risultati parziali
' ============================================================================================
'
' La ricerca gira in un thread a parte e raccoglie i risultati man mano: la gestione file li mostra
' mentre arrivano, come una cartella virtuale in cui ogni file e cartella trovati si usano come
' nella gestione file (apri, copia, taglia, elimina, rinomina...).
' Nel nome si cerca:
'   - con * e ? come in Windows ("*.docx", "relazione?.pdf");
'   - altrimenti tutte le parole scritte, in qualsiasi ordine, senza badare a maiuscole e accenti.
' Ricerca smart, se scelta: anche nel contenuto dei documenti (testo, Word, OpenDocument, PDF, RTF,
' HTML, con WGTestoFile), dove devono esserci tutte le parole scritte; è più lenta.
' Le ricerche fatte restano per tutta la sessione di WinGabriel nella voce "Ricerca file" del centro
' di controllo, che non sparisce quando una ricerca finisce: da lì si riaprono i risultati, se ne
' fa una nuova o si ferma quella in corso. Uscendo da WinGabriel le ricerche finiscono con la sessione.
'
'   Dim r As WGRicercaFile = WGRicercaFile.Nuova("fattura", {"C:\Users\Anna\Documents"})
'   r.Risultati(daIndice)   ' copia dei risultati arrivati finora
' ============================================================================================
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports wgcontrols

Public Class WGRicercaFile

    Public Const ChiaveCentroDiControllo As String = "RicercaFile"
    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _ricerche As New List(Of WGRicercaFile)()
    Private Shared _contatore As Integer

    Public ReadOnly Property Id As Integer
    Public ReadOnly Property Testo As String
    Public ReadOnly Property Cartelle As List(Of String)
    Public ReadOnly Property Iniziata As DateTime = DateTime.Now
    Public Property Finita As Boolean
    Public Property Annullata As Boolean
    Public Property CartelleEsaminate As Integer
    ' Vero mentre la gestione file mostra i risultati: è lei ad annunciare la fine.
    Public Property Mostrata As Boolean
    ' Ricerca smart: anche dentro i documenti.
    Public ReadOnly Property NelContenuto As Boolean

    Private ReadOnly _trovatiNelContenuto As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _paroleContenuto As String()

    Private ReadOnly _risultati As New List(Of String)()
    Private ReadOnly _annulla As New CancellationTokenSource()
    Private ReadOnly _confronto As Func(Of String, Boolean)

    Private Sub New(testo As String, cartelle As IEnumerable(Of String), nelContenuto As Boolean)
        _contatore += 1
        Id = _contatore
        Me.Testo = testo.Trim()
        Me.Cartelle = cartelle.Where(Function(c) Not String.IsNullOrWhiteSpace(c)).ToList()
        Me.NelContenuto = nelContenuto
        _confronto = CreaConfronto(Me.Testo)
        ' Nel contenuto si cercano le parole, anche se nel nome si è usato un modello con * e ?.
        _paroleContenuto = Normalizza(Me.Testo.Replace("*", " ").Replace("?", " ")).Split({" "c, "."c}, StringSplitOptions.RemoveEmptyEntries)
    End Sub

#Region "Ricerche della sessione"

    ''' <summary>Avvia una ricerca nelle cartelle indicate (e nelle loro sottocartelle) e la ricorda per la sessione.</summary>
    Public Shared Function Nuova(testo As String, cartelle As IEnumerable(Of String), Optional nelContenuto As Boolean = False) As WGRicercaFile
        Dim ricerca As New WGRicercaFile(testo, cartelle, nelContenuto)
        SyncLock _ricerche
            _ricerche.Add(ricerca)
        End SyncLock
        RegistraNelCentroDiControllo()
        ricerca.Avvia()
        Return ricerca
    End Function

    Public Shared Function Elenco() As List(Of WGRicercaFile)
        SyncLock _ricerche
            Return _ricerche.OrderByDescending(Function(r) r.Id).ToList()
        End SyncLock
    End Function

    Public Shared Function Trova(id As Integer) As WGRicercaFile
        SyncLock _ricerche
            Return _ricerche.FirstOrDefault(Function(r) r.Id = id)
        End SyncLock
    End Function

    ' Chi riapre i risultati dal centro di controllo (una nuova gestione file): lo imposta MenuFile.
    Public Shared Property ApriRisultati As Action(Of WGRicercaFile)
    Public Shared Property NuovaRicercaDalCentro As Action

    ' La voce resta nel centro di controllo per tutta la sessione, anche a ricerche finite.
    Private Shared Sub RegistraNelCentroDiControllo()
        Try
            WGCentroDiControllo.GetInstance().AggiungiVoceMenuDinamica(ChiaveCentroDiControllo, AddressOf EtichettaCentro, AddressOf MostraRicerche)
        Catch ex As Exception
            log.ScriviLog("WGRicercaFile.RegistraNelCentroDiControllo: " & ex.Message)
        End Try
    End Sub

    Private Shared Function EtichettaCentro() As String
        Dim tutte As List(Of WGRicercaFile) = Elenco()
        If tutte.Count = 0 Then Return "Ricerca file"
        Dim ultima As WGRicercaFile = tutte(0)
        Return "Ricerca file: " & ultima.Descrizione & If(tutte.Count > 1, ", " & tutte.Count & " ricerche in questa sessione", "")
    End Function

    ''' <summary>Dal centro di controllo: le ricerche della sessione, dalla più recente.</summary>
    Public Shared Sub MostraRicerche()
        Try
            Dim menu As New WGMenu()
            For Each ricerca As WGRicercaFile In Elenco()
                Dim scelta As WGRicercaFile = ricerca
                menu.aggiungi(scelta.Descrizione, Sub(s, e)
                                                      If ApriRisultati IsNot Nothing Then ApriRisultati.Invoke(scelta)
                                                  End Sub)
            Next
            For Each inCorso As WGRicercaFile In Elenco().Where(Function(r) Not r.Finita)
                Dim daFermare As WGRicercaFile = inCorso
                menu.aggiungi("Ferma la ricerca di " & daFermare.Testo, Sub(s, e)
                                                                           daFermare.Annulla()
                                                                           sintesivocale.GetInstance().LeggiTesto("Ricerca di " & daFermare.Testo & " fermata con " & daFermare.Conteggio & " risultati")
                                                                       End Sub)
            Next
            If NuovaRicercaDalCentro IsNot Nothing Then menu.aggiungi("Nuova ricerca", Sub(s, e) NuovaRicercaDalCentro.Invoke())
            menu.avvia("Ricerche", True)
        Catch ex As Exception
            log.ScriviLog("WGRicercaFile.MostraRicerche: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Risultati"

    Public ReadOnly Property Conteggio As Integer
        Get
            SyncLock _risultati
                Return _risultati.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>I risultati arrivati finora, a partire dall'indice indicato.</summary>
    Public Function Risultati(Optional daIndice As Integer = 0) As List(Of String)
        SyncLock _risultati
            If daIndice >= _risultati.Count Then Return New List(Of String)()
            Return _risultati.Skip(daIndice).ToList()
        End SyncLock
    End Function

    ''' <summary>Vero se il file è stato trovato per il contenuto e non per il nome.</summary>
    Public Function TrovatoNelContenuto(percorso As String) As Boolean
        SyncLock _risultati
            Return _trovatiNelContenuto.Contains(percorso)
        End SyncLock
    End Function

    ''' <summary>Toglie i risultati che non esistono più (eliminati o spostati dalla gestione file).</summary>
    Public Sub RimuoviInesistenti()
        SyncLock _risultati
            _risultati.RemoveAll(Function(p) Not WGRete.EsisteElemento(p, 8000))
        End SyncLock
    End Sub

    ''' <summary>Dopo una rinomina il risultato resta, con il nome nuovo.</summary>
    Public Sub SostituisciPercorso(vecchio As String, nuovo As String)
        SyncLock _risultati
            Dim posizione As Integer = _risultati.FindIndex(Function(p) String.Equals(p, vecchio, StringComparison.OrdinalIgnoreCase))
            If posizione >= 0 Then _risultati(posizione) = nuovo
        End SyncLock
    End Sub

    Public ReadOnly Property Stato As String
        Get
            If Annullata Then Return "fermata"
            If Finita Then Return "finita"
            Return "in corso"
        End Get
    End Property

    Public ReadOnly Property Descrizione As String
        Get
            Dim dove As String = If(Cartelle.Count = 1, Cartelle(0), "tutti i dischi")
            Dim quanti As Integer = Conteggio
            Return Testo & If(NelContenuto, ", anche nel contenuto,", "") & " in " & dove & ", " & If(quanti = 0, "nessun risultato", If(quanti = 1, "un risultato", quanti & " risultati")) & ", " & Stato
        End Get
    End Property

#End Region

#Region "Ricerca"

    Public Sub Annulla()
        Annullata = True
        Try
            _annulla.Cancel()
        Catch ex As Exception
            log.ScriviLog("WGRicercaFile.Annulla: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Normalizza(testo As String) As String
        Dim scomposto As String = If(testo, "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
        Dim sb As New StringBuilder(scomposto.Length)
        For Each c As Char In scomposto
            If Globalization.CharUnicodeInfo.GetUnicodeCategory(c) <> Globalization.UnicodeCategory.NonSpacingMark Then sb.Append(c)
        Next
        Return sb.ToString()
    End Function

    ' Con * o ? il testo è un modello come in Windows; altrimenti devono esserci tutte le parole.
    Private Shared Function CreaConfronto(testo As String) As Func(Of String, Boolean)
        If testo.Contains("*") OrElse testo.Contains("?") Then
            Dim modello As String = "^" & Regex.Escape(Normalizza(testo)).Replace("\*", ".*").Replace("\?", ".") & "$"
            Dim espressione As New Regex(modello, RegexOptions.CultureInvariant)
            Return Function(nome) espressione.IsMatch(Normalizza(nome))
        End If
        Dim parole As String() = Normalizza(testo).Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
        Return Function(nome)
                   Dim n As String = Normalizza(nome)
                   Return parole.All(Function(p) n.Contains(p))
               End Function
    End Function

    Private Shared ReadOnly CartelleDaSaltare As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "$Recycle.Bin", "System Volume Information", "$WinREAgent", "Config.Msi"}

    Private Sub Avvia()
        Dim filo As New Thread(AddressOf Esegui)
        filo.IsBackground = True
        filo.Priority = ThreadPriority.BelowNormal
        filo.Start()
    End Sub

    Private Sub Esegui()
        Dim token As CancellationToken = _annulla.Token
        Try
            Dim daEsaminare As New Stack(Of String)(Cartelle.AsEnumerable().Reverse())
            While daEsaminare.Count > 0 AndAlso Not token.IsCancellationRequested
                Dim cartella As String = daEsaminare.Pop()
                CartelleEsaminate += 1
                Dim voci As IEnumerable(Of String)
                Try
                    voci = Directory.EnumerateFileSystemEntries(cartella).ToList()
                Catch
                    ' Cartelle senza permesso di lettura o sparite nel frattempo: si saltano.
                    Continue While
                End Try
                Dim sottocartelle As New List(Of String)()
                For Each voce As String In voci
                    If token.IsCancellationRequested Then Exit For
                    Dim nome As String = Path.GetFileName(voce)
                    If _confronto(nome) Then
                        SyncLock _risultati
                            _risultati.Add(voce)
                        End SyncLock
                    ElseIf NelContenuto AndAlso _paroleContenuto.Length > 0 AndAlso WGRete.EsisteFile(voce, 8000) AndAlso WGTestoFile.Supportato(voce) Then
                        Dim contenuto As String = Normalizza(WGTestoFile.Estrai(voce))
                        If _paroleContenuto.All(Function(p) contenuto.Contains(p)) Then
                            SyncLock _risultati
                                _risultati.Add(voce)
                                _trovatiNelContenuto.Add(voce)
                            End SyncLock
                        End If
                    End If
                    Try
                        Dim attributi As FileAttributes = File.GetAttributes(voce)
                        ' Le giunzioni e i collegamenti simbolici non si seguono: porterebbero in giro all'infinito.
                        If (attributi And FileAttributes.Directory) = FileAttributes.Directory AndAlso
                           (attributi And FileAttributes.ReparsePoint) <> FileAttributes.ReparsePoint AndAlso
                           Not CartelleDaSaltare.Contains(nome) Then sottocartelle.Add(voce)
                    Catch
                    End Try
                Next
                ' In ordine, così si esplorano le sottocartelle da A a Z.
                For i As Integer = sottocartelle.Count - 1 To 0 Step -1
                    daEsaminare.Push(sottocartelle(i))
                Next
            End While
        Catch ex As Exception
            log.ScriviLog("WGRicercaFile.Esegui: " & ex.Message)
        Finally
            Finita = True
        End Try
        ' Se nessuno sta guardando i risultati, si avvisa qui.
        Try
            If Not Mostrata AndAlso Not Annullata Then
                Dim quanti As Integer = Conteggio
                sintesivocale.GetInstance().LeggiTesto("Ricerca di " & Testo & " finita: " & If(quanti = 0, "nessun risultato", If(quanti = 1, "un risultato", quanti & " risultati") & ", nel centro di controllo alla voce Ricerca file"))
            End If
        Catch ex As Exception
            log.ScriviLog("WGRicercaFile.Esegui annuncio: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
