' ============================================================================================
' WGManuale - il manuale di WinGabriel, da leggere dentro il programma
' ============================================================================================
'
' Il manuale è diviso in capitoli, ognuno in un file di testo della cartella Manuale incluso nella
' libreria: il nome del file dà l'ordine e la chiave (per esempio "08-scrivania.txt", chiave
' "scrivania"), la prima riga è il titolo, il resto il testo, con una riga vuota fra i paragrafi.
' La guida del lettore di schermo non sta più fra questi capitoli: è nella soluzione WGLettore,
' nel file Guida\lettore-schermo.txt, e si apre dal lettore stesso.
' Si apre da ogni punto di WinGabriel con Control F1, dal menu applicazioni e dalle opzioni di home
' e scrivania, come applicazione a sé: l'indice dei capitoli, Invio per leggerne uno in un campo di
' sola lettura, Esc per tornare all'indice; dal menu opzioni la ricerca in tutto il manuale.
'
' Il manuale segue la modalità: chi usa la classica legge solo ciò che vale per la classica.
'   - seconda riga "@smart" o "@classica": il capitolo intero vale solo per quella modalità;
'   - righe "[smart]" ... "[/smart]" e "[classica]" ... "[/classica]": paragrafi di una modalità sola;
'   - {menu} e {Menu}: "menu applicazioni" nella smart, "menu iniziale" nella classica.
' I capitoli si numerano dopo il filtro, così l'indice non ha buchi.
'
'   WGManuale.Mostra()                    ' l'indice
'   WGManuale.MostraCapitolo("scrivania") ' un capitolo, per esempio dall'F1 della scrivania
' ============================================================================================
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports System.Text
Imports wgcontrols

Public Class CapitoloManuale
    Public Property Numero As Integer
    Public Property Chiave As String = ""
    Public Property Titolo As String = ""
    Public Property Testo As String = ""
End Class

Public NotInheritable Class WGManuale

    Public Const NomeApplicazione As String = "Manuale"
    Private Shared ReadOnly log As New Logger()
    Private Shared _originali As List(Of Tuple(Of String, String, String))
    Private Shared _capitoli As List(Of CapitoloManuale)
    Private Shared _capitoliSmart As Boolean

    Private Sub New()
    End Sub

#Region "Capitoli"

    ''' <summary>I capitoli della modalità in uso, in ordine e numerati.</summary>
    Public Shared Function Capitoli() As List(Of CapitoloManuale)
        Dim smart As Boolean = WGModalita.Smart
        If _capitoli IsNot Nothing AndAlso _capitoliSmart = smart Then Return _capitoli
        Dim elenco As New List(Of CapitoloManuale)()
        Try
            For Each o As Tuple(Of String, String, String) In Originali()
                Dim righe As List(Of String) = o.Item3.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ChrW(10)).ToList()
                Dim modo As String = If(righe.Count > 0, righe(0).Trim().ToLowerInvariant(), "")
                If modo = "@smart" OrElse modo = "@classica" Then
                    If (modo = "@smart") <> smart Then Continue For
                    righe.RemoveAt(0)
                End If
                Dim tenute As New List(Of String)()
                Dim escluso As Boolean = False
                For Each riga As String In righe
                    Select Case riga.Trim().ToLowerInvariant()
                        Case "[smart]" : escluso = Not smart : Continue For
                        Case "[classica]" : escluso = smart : Continue For
                        Case "[/smart]", "[/classica]" : escluso = False : Continue For
                    End Select
                    If Not escluso Then tenute.Add(riga)
                Next
                ' Le righe vuote doppie lasciate dai blocchi tolti diventano una sola.
                Dim testo As String = String.Join(Environment.NewLine, tenute).Trim()
                Dim doppia As String = Environment.NewLine & Environment.NewLine & Environment.NewLine
                While testo.Contains(doppia)
                    testo = testo.Replace(doppia, Environment.NewLine & Environment.NewLine)
                End While
                elenco.Add(New CapitoloManuale With {.Numero = elenco.Count + 1, .Chiave = o.Item1, .Titolo = Sostituisci(o.Item2, smart), .Testo = Sostituisci(testo, smart)})
            Next
        Catch ex As Exception
            log.ScriviLog("WGManuale.Capitoli: " & ex.Message)
        End Try
        _capitoli = elenco
        _capitoliSmart = smart
        Return elenco
    End Function

    Private Shared Function Sostituisci(testo As String, smart As Boolean) As String
        Return testo.Replace("{menu}", If(smart, "menu applicazioni", "menu iniziale")).Replace("{Menu}", If(smart, "Menu applicazioni", "Menu iniziale"))
    End Function

    ' I file così come sono: chiave, titolo e resto del testo, letti una volta sola.
    Private Shared Function Originali() As List(Of Tuple(Of String, String, String))
        If _originali IsNot Nothing Then Return _originali
        Dim elenco As New List(Of Tuple(Of String, String, String))()
        Try
            Dim libreria As Assembly = GetType(WGManuale).Assembly
            Dim nomi As List(Of String) = libreria.GetManifestResourceNames().Where(Function(n) n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).OrderBy(Function(n) n, StringComparer.Ordinal).ToList()
            For Each nome As String In nomi
                Try
                    Dim testo As String
                    Using flusso As Stream = libreria.GetManifestResourceStream(nome)
                        Using lettore As New StreamReader(flusso, Encoding.UTF8, True)
                            testo = lettore.ReadToEnd()
                        End Using
                    End Using
                    Dim righe As List(Of String) = testo.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ChrW(10)).ToList()
                    While righe.Count > 0 AndAlso righe(0).Trim() = ""
                        righe.RemoveAt(0)
                    End While
                    If righe.Count = 0 Then Continue For
                    Dim titolo As String = righe(0).Trim()
                    righe.RemoveAt(0)
                    ' Il nome della risorsa è "WGGuida.08-scrivania.txt" (o con la cartella): la chiave è quel che segue il numero.
                    Dim file As String = Path.GetFileNameWithoutExtension(nome)
                    If file.Contains(".") Then file = file.Substring(file.LastIndexOf("."c) + 1)
                    Dim chiave As String = If(file.Contains("-"), file.Substring(file.IndexOf("-"c) + 1), file)
                    elenco.Add(Tuple.Create(chiave.ToLowerInvariant(), titolo, String.Join(Environment.NewLine, righe).Trim()))
                Catch exUno As Exception
                    log.ScriviLog("WGManuale.Capitoli " & nome & ": " & exUno.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGManuale.Originali: " & ex.Message)
        End Try
        _originali = elenco
        Return elenco
    End Function

    Private Shared Function Descrizione(c As CapitoloManuale) As String
        Return "Capitolo " & c.Numero & ", " & c.Titolo
    End Function

#End Region

#Region "Apertura"

    ''' <summary>Apre il manuale sull'indice dei capitoli, come applicazione a sé (o torna a quella già aperta).</summary>
    Public Shared Sub Mostra()
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New MethodInvoker(AddressOf Mostra))
                Return
            End If
            If contenitore IsNot Nothing Then
                If contenitore.VaiAllApplicazioneConNome(NomeApplicazione) Then Return
                contenitore.ApriComeNuovaApplicazione(NomeApplicazione)
            End If
            MostraIndice(0)
        Catch ex As Exception
            log.ScriviLog("WGManuale.Mostra: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Apre direttamente un capitolo (per chiave, per esempio "scrivania"); se non c'è, l'indice.</summary>
    Public Shared Sub MostraCapitolo(chiave As String)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            If contenitore IsNot Nothing AndAlso contenitore.InvokeRequired Then
                contenitore.BeginInvoke(New Action(Of String)(AddressOf MostraCapitolo), chiave)
                Return
            End If
            Dim capitolo As CapitoloManuale = Capitoli().FirstOrDefault(Function(c) String.Equals(c.Chiave, If(chiave, "").Trim(), StringComparison.OrdinalIgnoreCase))
            If capitolo Is Nothing Then
                Mostra()
                Return
            End If
            Leggi(capitolo)
        Catch ex As Exception
            log.ScriviLog("WGManuale.MostraCapitolo: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub MostraIndice(indice As Integer)
        Try
            Dim elenco As List(Of CapitoloManuale) = Capitoli()
            If elenco.Count = 0 Then
                Dim m As New WGMessaggi()
                m.Messaggio("Il manuale non è stato trovato in questa installazione di WinGabriel.")
                m.ShowDialog()
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.TitoloComeMenu = False
            lista.impostaTitolo("Manuale")
            For Each c As CapitoloManuale In elenco
                lista.aggiungiElemento(Descrizione(c), c.Numero.ToString())
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim numero As Integer
                                                      If Not Integer.TryParse(id, numero) Then Return
                                                      Dim c As CapitoloManuale = elenco.FirstOrDefault(Function(x) x.Numero = numero)
                                                      If c IsNot Nothing Then Leggi(c)
                                                  End Sub
            lista.aggiungiOpzione("Cerca nel manuale", Sub(s, e) Cerca())
            lista.aggiungiOpzione("Chiudi il manuale", Sub(s, e) lista.abbandona())
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
            If indice > 0 Then lista.selezionaElemento(Math.Min(indice, elenco.Count - 1))
        Catch ex As Exception
            log.ScriviLog("WGManuale.MostraIndice: " & ex.Message)
        End Try
    End Sub

    ' Il capitolo in un campo di sola lettura: frecce, Control freccia per i paragrafi, Esc per tornare.
    Private Shared Sub Leggi(c As CapitoloManuale)
        Try
            Dim campo As New WGCampo()
            campo.init(Descrizione(c), c.Testo, True, True)
            campo.avvia()
        Catch ex As Exception
            log.ScriviLog("WGManuale.Leggi: " & ex.Message)
        End Try
    End Sub

#End Region

#Region "Ricerca"

    Private Shared Function Normalizza(testo As String) As String
        Dim scomposto As String = If(testo, "").ToLowerInvariant().Normalize(NormalizationForm.FormD)
        Dim sb As New StringBuilder(scomposto.Length)
        For Each ch As Char In scomposto
            If CharUnicodeInfo.GetUnicodeCategory(ch) <> UnicodeCategory.NonSpacingMark Then sb.Append(ch)
        Next
        Return sb.ToString()
    End Function

    ' Le parole scritte, in qualsiasi ordine, senza badare a maiuscole e accenti; per ogni capitolo
    ' trovato si sente il primo passo che le contiene.
    Private Shared Sub Cerca()
        Dim campi As New Campi()
        campi.Aggiungi("Cerca nel manuale: una o più parole", "testo", True)
        AddHandler campi.Fine, Sub(s, e)
                                   Dim cercato As String = If(campi.RecuperaValore("testo"), "").Trim()
                                   If cercato = "" Then Return
                                   MostraRisultati(cercato)
                               End Sub
        campi.Avvia()
    End Sub

    Private Shared Sub MostraRisultati(cercato As String)
        Try
            Dim parole As String() = Normalizza(cercato).Split({" "c}, StringSplitOptions.RemoveEmptyEntries)
            Dim trovati As New List(Of Tuple(Of CapitoloManuale, String))()
            For Each c As CapitoloManuale In Capitoli()
                Dim paragrafi As String() = c.Testo.Split(New String() {Environment.NewLine & Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                Dim tutto As String = Normalizza(c.Titolo & " " & c.Testo)
                If Not parole.All(Function(p) tutto.Contains(p)) Then Continue For
                Dim passo As String = paragrafi.FirstOrDefault(Function(p) parole.Any(Function(x) Normalizza(p).Contains(x)))
                If passo Is Nothing Then passo = ""
                If passo.Length > 160 Then passo = passo.Substring(0, 160).TrimEnd() & "…"
                trovati.Add(Tuple.Create(c, passo.Replace(Environment.NewLine, " ")))
            Next
            If trovati.Count = 0 Then
                WGDomanda.Chiedi("Nessun capitolo del manuale parla di " & cercato & ". Vuoi cercare altre parole?", Sub() Cerca())
                Return
            End If
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            lista.TitoloComeMenu = False
            lista.impostaTitolo("Risultati")
            For Each t As Tuple(Of CapitoloManuale, String) In trovati
                lista.aggiungiElemento(Descrizione(t.Item1) & If(t.Item2 <> "", ": " & t.Item2, ""), t.Item1.Numero.ToString())
            Next
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim numero As Integer
                                                      If Not Integer.TryParse(id, numero) Then Return
                                                      Dim c As CapitoloManuale = Capitoli().FirstOrDefault(Function(x) x.Numero = numero)
                                                      If c IsNot Nothing Then Leggi(c)
                                                  End Sub
            lista.aggiungiOpzione("Cerca altre parole", Sub(s, e)
                                                           lista.abbandona()
                                                           Cerca()
                                                       End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGManuale.MostraRisultati: " & ex.Message)
        End Try
    End Sub

#End Region

End Class
