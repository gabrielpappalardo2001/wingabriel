' ============================================================================================
' WGCentroNotifiche - raccolta unica delle notifiche di WinGabriel
' ============================================================================================
'
' A cosa serve
'   Ogni applicazione (agenda, posta, browser, notizie, sistema...) pubblica qui le sue notifiche.
'   Il centro le tiene in ordine dalla più recente, le ricorda fra un avvio e l'altro e le mostra
'   in un elenco unico, raggiungibile dalla scrivania e dal menu opzioni (Alt) del menu applicazioni.
'   L'ultima notifica compare anche in cima all'elenco delle applicazioni (F10).
'
' Come si usa
'   WGCentroNotifiche.Pubblica("Agenda", "Riunione alle 15", AddressOf ApriAgenda)
'   WGCentroNotifiche.Pubblica("", "Scaricamento completato", azione, Me, annuncia:=True)
'     con l'origine (la finestra dell'applicazione) il nome dell'applicazione si ricava da solo, e Invio
'     sulla notifica riporta a quell'applicazione se non c'è un'azione.
'   WGCentroNotifiche.Pubblica("Posta", "Nuovo messaggio da Anna", apri, azioni:={New KeyValuePair(Of String, Action)("Rispondi", rispondi)})
'     le azioni proprie compaiono con Invio sulla notifica, insieme ad Apri, Rimanda ed Elimina.
'   WGCentroNotifiche.MostraCentro()
'
' Annunci
'   Con annuncia:=True la notifica viene letta con il suono scelto per la sua applicazione; se arriva
'   da un'applicazione in sottofondo, preceduta dal nome dell'applicazione.
'   Con "Non disturbare" le notifiche arrivano e si conservano in silenzio; alla fine si dice quante sono.
' ============================================================================================
Imports System.Collections.Generic
Imports System.Data
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports wgcontrols
Imports WGDb

Public Class WGNotifica
    Public Property Id As Integer
    Public Property Applicazione As String = ""
    Public Property Testo As String = ""
    Public Property Istante As DateTime = DateTime.Now
    Public Property Letta As Boolean = False
    ' Non ricordate fra un avvio e l'altro: per questo una notifica può avere una chiave, con la quale dopo
    ' il riavvio si ritrova l'azione registrata con RegistraGestore.
    Public Property ChiaveAzione As String = ""
    Public Property Azione As Action
    Public Property Origine As Control
    ' Azioni proprie della notifica (Rispondi, Segna come fatto...), oltre ad Apri, Rimanda ed Elimina.
    Public Property Azioni As New List(Of KeyValuePair(Of String, Action))()

    Public ReadOnly Property Descrizione As String
        Get
            Dim quando As String = If(Istante.Date = DateTime.Today, Istante.ToString("HH:mm"), Istante.ToString("dd/MM HH:mm"))
            Return If(Applicazione <> "", Applicazione & ", ", "") & quando & ": " & Testo
        End Get
    End Property
End Class

Public NotInheritable Class WGCentroNotifiche

    Private Shared ReadOnly log As New Logger()
    Private Shared ReadOnly _lock As New Object()
    Private Shared ReadOnly _notifiche As New List(Of WGNotifica)()
    Private Shared _contatore As Integer = 0
    Private Shared _caricate As Boolean = False
    Private Shared _suoni As WGSuoni
    Private Const MassimoNotifiche As Integer = 200
    Private Const MassimoSalvate As Integer = 100

    Public Shared Event NotificaAggiunta(notifica As WGNotifica)

    Private Shared ReadOnly _gestori As New Dictionary(Of String, Action)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>
    ''' Registra un'azione che sopravvive al riavvio: le notifiche pubblicate con la stessa chiaveAzione, anche quelle
    ''' salvate in una sessione precedente, la eseguono quando vengono aperte.
    ''' </summary>
    Public Shared Sub RegistraGestore(chiave As String, azione As Action)
        If String.IsNullOrWhiteSpace(chiave) OrElse azione Is Nothing Then Return
        SyncLock _lock
            _gestori(chiave) = azione
        End SyncLock
    End Sub

    ' L'azione propria della notifica o, dopo un riavvio, quella registrata con la sua chiave.
    Private Shared Function AzioneDi(notifica As WGNotifica) As Action
        If notifica Is Nothing Then Return Nothing
        If notifica.Azione IsNot Nothing Then Return notifica.Azione
        If notifica.ChiaveAzione = "" Then Return Nothing
        Dim gestore As Action = Nothing
        SyncLock _lock
            _gestori.TryGetValue(notifica.ChiaveAzione, gestore)
        End SyncLock
        Return gestore
    End Function

    ' Vero se Invio sulla notifica può aprire qualcosa: un'azione, delle azioni proprie o la finestra d'origine.
    Private Shared Function PuoAprire(notifica As WGNotifica) As Boolean
        If notifica Is Nothing Then Return False
        If AzioneDi(notifica) IsNot Nothing OrElse notifica.Azioni.Count > 0 Then Return True
        Return notifica.Origine IsNot Nothing AndAlso Not notifica.Origine.IsDisposed
    End Function

    Private Sub New()
    End Sub

#Region "Archivio"

    Private Shared Function PercorsoArchivio(Optional nomeFile As String = "notifiche.txt") As String
        Try
            Dim cartella As String = WGDatabase.CartellaDati("notifiche")
            If String.IsNullOrWhiteSpace(cartella) Then Return ""
            Return Path.Combine(cartella, nomeFile)
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.PercorsoArchivio: " & ex.Message)
            Return ""
        End Try
    End Function

    Private Const TabellaNotifiche As String = "Notifiche"
    Private Const TabellaSuoni As String = "NotificheSuoni"
    Private Shared _db As WGDatabase
    Private Shared ReadOnly _bloccoDb As New Object()

    ' Le notifiche (e i suoni per applicazione) hanno un database tutto loro, nella cartella "notifiche", che non si sincronizza mai:
    ' ogni computer ha le sue. Nothing se non si riesce ad aprirlo.
    Private Shared Function Db() As WGDatabase
        SyncLock _bloccoDb
            Try
                If _db Is Nothing Then
                    Dim d As New WGDatabase("notifiche", "notifiche.sqlite")
                    If Not d.Valido Then
                        log.ScriviLog("WGCentroNotifiche.Db: impossibile aprire il database delle notifiche")
                        Return Nothing
                    End If
                    d.CreaTabellaSeNonEsiste(TabellaNotifiche, "Istante TEXT, Letta INTEGER, Applicazione TEXT, Testo TEXT, ChiaveAzione TEXT")
                    d.CreaTabellaSeNonEsiste(TabellaSuoni, "Applicazione TEXT, Codice TEXT")
                    PortaViaDalDatabaseDellInterfaccia(d)
                    _db = d
                End If
            Catch ex As Exception
                log.ScriviLog("WGCentroNotifiche.Db: " & ex.Message)
            End Try
            Return _db
        End SyncLock
    End Function

    ' Per un po' le notifiche stavano nel database dell'interfaccia (gui.sqlite), che si sincronizza: le righe di lì si portano nel
    ' database delle notifiche una volta sola e le tabelle di gui si tolgono.
    Private Shared Sub PortaViaDalDatabaseDellInterfaccia(destinazione As WGDatabase)
        Try
            Dim gui As WGDatabase = WGGui.Db()
            If gui Is Nothing Then Return
            For Each tabella As String In New String() {TabellaNotifiche, TabellaSuoni}
                Dim presente As Object = gui.EseguiScalare("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '" & tabella & "';")
                If presente Is Nothing OrElse IsDBNull(presente) OrElse Convert.ToInt64(presente) = 0 Then Continue For

                Dim dentro As Object = destinazione.EseguiScalare("SELECT COUNT(*) FROM " & tabella & ";")
                Dim vuota As Boolean = (dentro Is Nothing OrElse IsDBNull(dentro) OrElse Convert.ToInt64(dentro) = 0)
                If vuota Then
                    Dim colonne As String = If(tabella = TabellaNotifiche, "Istante, Letta, Applicazione, Testo, ChiaveAzione", "Applicazione, Codice")
                    For Each riga As DataRow In gui.EseguiQuery("SELECT " & colonne & " FROM " & tabella & ";").Rows
                        Dim valori As New Dictionary(Of String, Object)()
                        For Each nome As String In colonne.Split(New String() {", "}, StringSplitOptions.RemoveEmptyEntries)
                            valori(nome) = If(IsDBNull(riga(nome)), "", riga(nome))
                        Next
                        destinazione.InserisciRecord(tabella, valori)
                    Next
                End If
                gui.EseguiNonQuery("DROP TABLE " & tabella & ";")
            Next
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.PortaViaDalDatabaseDellInterfaccia: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Campo(riga As DataRow, colonna As String) As String
        If Not riga.Table.Columns.Contains(colonna) OrElse IsDBNull(riga(colonna)) Then Return ""
        Return Convert.ToString(riga(colonna))
    End Function

    ' Le notifiche stanno nel database. Se c'è ancora il vecchio file di testo (una riga per notifica: istante, letta,
    ' applicazione, testo e chiave, separati da tabulazioni) si importa una volta sola e poi si toglie.
    Private Shared Sub CaricaSeServe()
        If _caricate Then Return
        _caricate = True
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return
            Dim vecchio As String = PercorsoArchivio()
            If vecchio <> "" AndAlso File.Exists(vecchio) Then
                Try
                    For Each riga As String In File.ReadAllLines(vecchio, Encoding.UTF8)
                        Dim parti As String() = riga.Split(ControlChars.Tab)
                        If parti.Length < 4 Then Continue For
                        Dim chiave As String = If(parti.Length >= 5, parti(4), "")
                        Dim testoVecchio As String = If(parti.Length >= 5, parti(3), String.Join(" ", parti.Skip(3)))
                        d.InserisciRecord(TabellaNotifiche, New Dictionary(Of String, Object) From {
                            {"Istante", parti(0)}, {"Letta", If(parti(1) = "1", 1, 0)}, {"Applicazione", parti(2)}, {"Testo", testoVecchio}, {"ChiaveAzione", chiave}})
                    Next
                    File.Delete(vecchio)
                Catch exImporta As Exception
                    log.ScriviLog("WGCentroNotifiche.CaricaSeServe (importazione): " & exImporta.Message)
                End Try
            End If
            For Each riga As DataRow In d.OttieniTuttiIRecord(TabellaNotifiche, "Istante").Rows
                Dim istante As DateTime
                If Not DateTime.TryParseExact(Campo(riga, "Istante"), "o", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind, istante) Then Continue For
                _contatore += 1
                _notifiche.Add(New WGNotifica() With {.Id = _contatore, .Istante = istante, .Letta = Campo(riga, "Letta") = "1",
                                                      .Applicazione = Campo(riga, "Applicazione"), .Testo = Campo(riga, "Testo"), .ChiaveAzione = Campo(riga, "ChiaveAzione")})
            Next
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.CaricaSeServe: " & ex.Message)
        End Try
    End Sub

    ' Riscrive la tabella con le ultime notifiche (le più vecchie oltre il massimo si perdono).
    Private Shared Sub Salva()
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return
            Dim recenti As List(Of WGNotifica)
            SyncLock _lock
                recenti = _notifiche.OrderByDescending(Function(n) n.Istante).Take(MassimoSalvate).ToList()
            End SyncLock
            SyncLock _bloccoDb
                d.EseguiNonQuery("BEGIN;")
                Try
                    d.EseguiNonQuery("DELETE FROM " & TabellaNotifiche & ";")
                    For Each n As WGNotifica In recenti
                        d.InserisciRecord(TabellaNotifiche, New Dictionary(Of String, Object) From {
                            {"Istante", n.Istante.ToString("o")}, {"Letta", If(n.Letta, 1, 0)}, {"Applicazione", Pulisci(n.Applicazione)},
                            {"Testo", Pulisci(n.Testo)}, {"ChiaveAzione", Pulisci(n.ChiaveAzione)}})
                    Next
                    d.EseguiNonQuery("COMMIT;")
                Catch
                    d.EseguiNonQuery("ROLLBACK;")
                    Throw
                End Try
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.Salva: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Pulisci(testo As String) As String
        Return If(testo, "").Replace(ControlChars.Tab, " ").Replace(vbCr, " ").Replace(vbLf, " ").Trim()
    End Function

#End Region

#Region "Non disturbare"

    Private Const IdNonDisturbare As String = "notifiche_non_disturbare"
    Private Shared _nonDisturbareFino As DateTime = DateTime.MinValue
    Private Shared _nonDisturbareLetto As Boolean
    Private Shared _arrivateInSilenzio As Integer
    Private Shared WithEvents _timerNonDisturbare As Timer

    ''' <summary>Fino a quando le notifiche arrivano in silenzio; DateTime.MaxValue = finché non lo si toglie.</summary>
    Public Shared Property NonDisturbareFino As DateTime
        Get
            If Not _nonDisturbareLetto Then
                _nonDisturbareLetto = True
                Try
                    Dim valore As String = New WGConfig().LeggiValore(IdNonDisturbare)
                    Dim fino As DateTime
                    If DateTime.TryParseExact(valore, "o", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind, fino) Then _nonDisturbareFino = fino
                Catch ex As Exception
                    log.ScriviLog("WGCentroNotifiche.NonDisturbareFino: " & ex.Message)
                End Try
            End If
            Return _nonDisturbareFino
        End Get
        Set(value As DateTime)
            _nonDisturbareLetto = True
            _nonDisturbareFino = value
            Try
                Dim config As New WGConfig()
                config.AggiungiValore(IdNonDisturbare, If(value = DateTime.MinValue, "", value.ToString("o")))
            Catch ex As Exception
                log.ScriviLog("WGCentroNotifiche.NonDisturbareFino Set: " & ex.Message)
            End Try
            If value > DateTime.Now Then AvviaControlloFine()
        End Set
    End Property

    Public Shared ReadOnly Property NonDisturbareAttivo As Boolean
        Get
            Return NonDisturbareFino > DateTime.Now
        End Get
    End Property

    Public Shared Function DescriviNonDisturbare() As String
        If Not NonDisturbareAttivo Then Return "disattivato"
        If NonDisturbareFino = DateTime.MaxValue Then Return "attivo finché non lo disattivi"
        Return "attivo fino alle " & NonDisturbareFino.ToString("H:mm") & If(NonDisturbareFino.Date <> DateTime.Today, " di domani", "")
    End Function

    Private Shared Sub AvviaControlloFine()
        If _timerNonDisturbare Is Nothing Then _timerNonDisturbare = New Timer() With {.Interval = 30000}
        _timerNonDisturbare.Start()
    End Sub

    ' Finito il "non disturbare" si dice quante notifiche sono arrivate nel frattempo.
    Private Shared Sub _timerNonDisturbare_Tick(sender As Object, e As EventArgs) Handles _timerNonDisturbare.Tick
        If NonDisturbareAttivo Then Return
        _timerNonDisturbare.Stop()
        FineNonDisturbare()
    End Sub

    Private Shared Sub FineNonDisturbare()
        Dim arrivate As Integer = _arrivateInSilenzio
        _arrivateInSilenzio = 0
        If arrivate > 0 Then sintesivocale.GetInstance().LeggiTesto("Non disturbare terminato: " & If(arrivate = 1, "è arrivata una notifica", "sono arrivate " & arrivate & " notifiche") & ", sono nel centro notifiche")
    End Sub

    Public Shared Sub ScegliNonDisturbare()
        Try
            Dim menu As New WGMenu()
            menu.aggiungi("Per un'ora", Sub(s, e) ImpostaNonDisturbare(DateTime.Now.AddHours(1)))
            menu.aggiungi("Per tre ore", Sub(s, e) ImpostaNonDisturbare(DateTime.Now.AddHours(3)))
            menu.aggiungi("Fino a domani mattina alle 8", Sub(s, e) ImpostaNonDisturbare(If(DateTime.Now.Hour < 8, DateTime.Today, DateTime.Today.AddDays(1)).AddHours(8)))
            menu.aggiungi("Finché non lo disattivo", Sub(s, e) ImpostaNonDisturbare(DateTime.MaxValue))
            If NonDisturbareAttivo Then menu.aggiungi("Disattiva adesso", Sub(s, e) ImpostaNonDisturbare(DateTime.MinValue))
            menu.avvia("Non disturbare", True)
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.ScegliNonDisturbare: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ImpostaNonDisturbare(fino As DateTime)
        Dim eraAttivo As Boolean = NonDisturbareAttivo
        NonDisturbareFino = fino
        If fino = DateTime.MinValue Then
            sintesivocale.GetInstance().LeggiTesto("Non disturbare disattivato")
            If eraAttivo Then FineNonDisturbare()
        Else
            sintesivocale.GetInstance().LeggiTesto("Non disturbare " & DescriviNonDisturbare() & ": le notifiche arrivano in silenzio nel centro notifiche")
        End If
    End Sub

#End Region

#Region "Suono per applicazione"

    ' Suoni tra cui scegliere: nome mostrato e codice salvato.
    Private Shared ReadOnly SuoniDisponibili As String()() = {
        New String() {"Notifica", "notifica"},
        New String() {"Avviso", "avviso"},
        New String() {"Din", "din"},
        New String() {"Clic", "clic"},
        New String() {"Collegamento", "link"},
        New String() {"Nessun suono", "nessuno"}}

    Private Shared _suoniApplicazioni As Dictionary(Of String, String)

    Private Shared Function SuoniApplicazioni() As Dictionary(Of String, String)
        If _suoniApplicazioni IsNot Nothing Then Return _suoniApplicazioni
        _suoniApplicazioni = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return _suoniApplicazioni
            ' Il vecchio suoni.txt si importa una volta sola.
            Dim percorso As String = PercorsoArchivio("suoni.txt")
            If percorso <> "" AndAlso File.Exists(percorso) Then
                For Each riga As String In File.ReadAllLines(percorso, Encoding.UTF8)
                    Dim parti As String() = riga.Split(ControlChars.Tab)
                    If parti.Length = 2 Then _suoniApplicazioni(parti(0)) = parti(1)
                Next
                SalvaSuoni()
                WGGui.MettiDaParte(percorso)
            Else
                For Each riga As DataRow In d.EseguiQuery("SELECT Applicazione, Codice FROM " & TabellaSuoni & ";").Rows
                    If Not IsDBNull(riga(0)) AndAlso Not IsDBNull(riga(1)) Then _suoniApplicazioni(Convert.ToString(riga(0))) = Convert.ToString(riga(1))
                Next
            End If
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.SuoniApplicazioni: " & ex.Message)
        End Try
        Return _suoniApplicazioni
    End Function

    Private Shared Sub SalvaSuoni()
        Try
            Dim d As WGDatabase = Db()
            If d Is Nothing Then Return
            SyncLock _bloccoDb
                d.EseguiNonQuery("BEGIN;")
                Try
                    d.EseguiNonQuery("DELETE FROM " & TabellaSuoni & ";")
                    For Each c As KeyValuePair(Of String, String) In _suoniApplicazioni
                        d.InserisciRecord(TabellaSuoni, New Dictionary(Of String, Object) From {{"Applicazione", Pulisci(c.Key)}, {"Codice", c.Value}})
                    Next
                    d.EseguiNonQuery("COMMIT;")
                Catch
                    d.EseguiNonQuery("ROLLBACK;")
                    Throw
                End Try
            End SyncLock
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.SalvaSuoni: " & ex.Message)
        End Try
    End Sub

    Private Shared Function SuonoDi(applicazione As String) As String
        Dim codice As String = ""
        If Not String.IsNullOrEmpty(applicazione) AndAlso SuoniApplicazioni().TryGetValue(applicazione, codice) Then Return codice
        Return "notifica"
    End Function

    Private Shared Sub Suona(codice As String)
        Try
            If _suoni Is Nothing Then _suoni = New WGSuoni()
            Select Case codice
                Case "nessuno"
                Case "avviso" : _suoni.Avviso()
                Case "din" : WGSuoni.EmettiDin()
                Case "clic" : _suoni.ClickClack()
                Case "link" : _suoni.LinkAttivato()
                Case Else : _suoni.Notifica()
            End Select
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.Suona: " & ex.Message)
        End Try
    End Sub

    ''' <summary>Sceglie il suono delle notifiche di ogni applicazione; il suono si sente scorrendo le voci con Invio.</summary>
    Public Shared Sub ScegliSuoni()
        Try
            Dim applicazioni As New List(Of String)()
            SyncLock _lock
                CaricaSeServe()
                applicazioni.AddRange(_notifiche.Select(Function(n) n.Applicazione).Where(Function(a) a <> ""))
            End SyncLock
            applicazioni.AddRange(SuoniApplicazioni().Keys)
            applicazioni = applicazioni.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Function(a) a).ToList()
            If applicazioni.Count = 0 Then
                Dim m As New WGMessaggi()
                m.Messaggio("Non ci sono ancora applicazioni che hanno mandato notifiche.")
                m.ShowDialog()
                Return
            End If
            Dim menu As New WGMenu()
            For Each applicazione As String In applicazioni
                Dim scelta As String = applicazione
                Dim trovato As String() = SuoniDisponibili.FirstOrDefault(Function(s) s(1) = SuonoDi(scelta))
                Dim nomeSuono As String = If(trovato Is Nothing, SuoniDisponibili(0)(0), trovato(0))
                menu.aggiungi(scelta & ": " & nomeSuono, Sub(s, e) ScegliSuonoPer(scelta))
            Next
            menu.avvia("Suoni", True)
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.ScegliSuoni: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ScegliSuonoPer(applicazione As String)
        Dim menu As New WGMenu()
        For Each suono As String() In SuoniDisponibili
            Dim codice As String = suono(1)
            menu.aggiungi(suono(0), Sub(s, e)
                                        SuoniApplicazioni()(applicazione) = codice
                                        SalvaSuoni()
                                        Suona(codice)
                                        sintesivocale.GetInstance().LeggiTesto("Suono delle notifiche di " & applicazione & ": " & SuoniDisponibili.First(Function(x) x(1) = codice)(0))
                                    End Sub)
        Next
        menu.avvia("Suono di " & applicazione, True)
    End Sub

#End Region

    ''' <summary>
    ''' Aggiunge una notifica. applicazione vuota: si ricava dall'origine. annuncia: la legge subito,
    ''' con il nome dell'applicazione se questa non è quella in primo piano. azioni: azioni proprie.
    ''' </summary>
    Public Shared Function Pubblica(applicazione As String, testo As String, Optional azione As Action = Nothing, Optional origine As Control = Nothing, Optional annuncia As Boolean = False, Optional azioni As IEnumerable(Of KeyValuePair(Of String, Action)) = Nothing, Optional chiaveAzione As String = "") As WGNotifica
        Try
            If String.IsNullOrWhiteSpace(testo) Then Return Nothing
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Dim nome As String = If(applicazione, "").Trim()
            If nome = "" AndAlso origine IsNot Nothing AndAlso contenitore IsNot Nothing Then nome = contenitore.NomeApplicazioneDi(origine)
            Dim notifica As WGNotifica
            SyncLock _lock
                CaricaSeServe()
                _contatore += 1
                notifica = New WGNotifica() With {.Id = _contatore, .Applicazione = nome, .Testo = Pulisci(testo), .Azione = azione, .Origine = origine, .ChiaveAzione = If(chiaveAzione, "").Trim()}
                If azioni IsNot Nothing Then notifica.Azioni.AddRange(azioni.Where(Function(a) a.Value IsNot Nothing))
                _notifiche.Add(notifica)
                If _notifiche.Count > MassimoNotifiche Then _notifiche.RemoveAt(0)
            End SyncLock
            WGContenitore.VoceUltimaNotifica = "Ultima notifica, " & notifica.Descrizione
            WGContenitore.AzioneUltimaNotifica = Sub() ScegliAzione(notifica)
            Salva()
            If annuncia Then
                If NonDisturbareAttivo Then
                    _arrivateInSilenzio += 1
                    AvviaControlloFine()
                Else
                    LeggiNotifica(notifica)
                End If
            End If
            RaiseEvent NotificaAggiunta(notifica)
            Return notifica
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.Pubblica: " & ex.Message)
            Return Nothing
        End Try
    End Function

    Private Shared Sub LeggiNotifica(notifica As WGNotifica)
        Try
            Dim contenitore As WGContenitore = WGContenitore.Istanza
            Dim inSottofondo As Boolean = notifica.Origine IsNot Nothing AndAlso contenitore IsNot Nothing AndAlso Not contenitore.EApplicazioneCorrente(notifica.Origine)
            Suona(SuonoDi(notifica.Applicazione))
            If inSottofondo Then
                sintesivocale.GetInstance().LeggiTesto(If(notifica.Applicazione <> "", notifica.Applicazione & ": ", "") & notifica.Testo & ". F10 per andarci")
            Else
                sintesivocale.GetInstance().LeggiTesto(notifica.Testo)
            End If
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.LeggiNotifica: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Invio su una notifica: la sua azione (aprire l'agenda, i download...), altrimenti il ritorno
    ''' all'applicazione che l'ha mandata.
    ''' </summary>
    Public Shared Sub Apri(notifica As WGNotifica)
        Try
            If notifica Is Nothing Then Return
            notifica.Letta = True
            Salva()
            Dim azione As Action = AzioneDi(notifica)
            If azione IsNot Nothing Then
                EseguiComeApplicazione(notifica, azione)
                Return
            End If
            Differisci(Sub()
                           Try
                               Dim contenitore As WGContenitore = WGContenitore.Istanza
                               If notifica.Origine IsNot Nothing AndAlso contenitore IsNot Nothing AndAlso contenitore.VaiAllApplicazioneDi(notifica.Origine) Then Return
                               sintesivocale.GetInstance().LeggiTesto(notifica.Testo)
                           Catch ex As Exception
                               log.ScriviLog("WGCentroNotifiche.Apri (differita): " & ex.Message)
                           End Try
                       End Sub)
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.Apri: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' L'azione di una notifica parte dopo che l'elenco o il menu da cui la si è scelta si è chiuso del
    ''' tutto, e apre un'applicazione a sé con il nome dell'applicazione della notifica (Mail, Agenda...):
    ''' così la finestra aperta non finisce nell'applicazione che si sta chiudendo, che la riporterebbe
    ''' subito indietro, e chiudendola si torna dove si era.
    ''' </summary>
    Private Shared Sub EseguiComeApplicazione(notifica As WGNotifica, azione As Action)
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        Differisci(Sub()
                       Try
                           If contenitore IsNot Nothing Then contenitore.ApriComeNuovaApplicazione(If(notifica.Applicazione <> "", notifica.Applicazione, "Notifica"))
                           azione()
                       Catch ex As Exception
                           log.ScriviLog("WGCentroNotifiche.EseguiComeApplicazione: " & ex.Message)
                       End Try
                   End Sub)
    End Sub

    ''' <summary>
    ''' Esegue l'azione dopo che l'elenco o il menu di partenza si è chiuso del tutto. Il contenitore fa tornare
    ''' la scrivania solo dopo 400 ms: l'azione parte prima, così la finestra che apre prende il suo posto.
    ''' </summary>
    Private Shared Sub Differisci(azione As Action)
        Dim contenitore As WGContenitore = WGContenitore.Istanza
        If contenitore Is Nothing OrElse Not contenitore.IsHandleCreated OrElse contenitore.IsDisposed Then
            azione()
            Return
        End If
        contenitore.BeginInvoke(New MethodInvoker(Sub()
                                                      Dim attesa As New System.Windows.Forms.Timer()
                                                      attesa.Interval = 150
                                                      AddHandler attesa.Tick, Sub(s, e)
                                                                                  attesa.Stop()
                                                                                  attesa.Dispose()
                                                                                  azione()
                                                                              End Sub
                                                      attesa.Start()
                                                  End Sub))
    End Sub

    ''' <summary>Con azioni proprie chiede cosa fare (Apri, le azioni, Rimanda, Elimina); senza, apre.</summary>
    Public Shared Sub ScegliAzione(notifica As WGNotifica, Optional sempre As Boolean = False)
        Try
            If notifica Is Nothing Then Return
            If notifica.Azioni.Count = 0 AndAlso Not sempre Then
                Apri(notifica)
                Return
            End If
            Dim menu As New WGMenu()
            menu.TitoloComeMenu = False
            menu.aggiungi("Apri", Sub(s, e) Apri(notifica))
            For Each coppia As KeyValuePair(Of String, Action) In notifica.Azioni
                Dim azioneScelta As Action = coppia.Value
                menu.aggiungi(coppia.Key, Sub(s, e)
                                              notifica.Letta = True
                                              Salva()
                                              EseguiComeApplicazione(notifica, azioneScelta)
                                          End Sub)
            Next
            menu.aggiungi("Rimanda di 10 minuti", Sub(s, e) Rimanda(notifica, TimeSpan.FromMinutes(10)))
            menu.aggiungi("Rimanda di un'ora", Sub(s, e) Rimanda(notifica, TimeSpan.FromHours(1)))
            menu.aggiungi("Elimina", Sub(s, e)
                                         SyncLock _lock
                                             _notifiche.Remove(notifica)
                                         End SyncLock
                                         Salva()
                                         sintesivocale.GetInstance().LeggiTesto("Notifica eliminata")
                                     End Sub)
            menu.avvia(If(notifica.Applicazione <> "", notifica.Applicazione, "Notifica"), True)
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.ScegliAzione: " & ex.Message)
        End Try
    End Sub

    ' La notifica sparisce e torna, letta ad alta voce, dopo il tempo scelto (se WinGabriel è ancora aperto).
    Private Shared Async Sub Rimanda(notifica As WGNotifica, dopo As TimeSpan)
        Try
            SyncLock _lock
                _notifiche.Remove(notifica)
            End SyncLock
            Salva()
            sintesivocale.GetInstance().LeggiTesto("Notifica rimandata di " & If(dopo.TotalMinutes >= 60, "un'ora", CInt(dopo.TotalMinutes) & " minuti"))
            Await Task.Delay(dopo)
            Pubblica(notifica.Applicazione, notifica.Testo, notifica.Azione, notifica.Origine, True, notifica.Azioni)
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.Rimanda: " & ex.Message)
        End Try
    End Sub

    Public Shared Function DaLeggere() As Integer
        SyncLock _lock
            CaricaSeServe()
            Return _notifiche.Where(Function(n) Not n.Letta).Count()
        End SyncLock
    End Function

    ''' <summary>Centro notifiche: tutte le notifiche dalla più recente. Invio la apre, dal menu opzioni si eliminano.</summary>
    Public Shared Sub MostraCentro(Optional indice As Integer = 0)
        Try
            Dim elenco As List(Of WGNotifica)
            SyncLock _lock
                CaricaSeServe()
                elenco = _notifiche.OrderByDescending(Function(n) n.Istante).ToList()
            End SyncLock
            If elenco.Count = 0 Then
                Dim messaggio As New WGMessaggi()
                messaggio.Messaggio("Nessuna notifica." & If(NonDisturbareAttivo, " Non disturbare " & DescriviNonDisturbare() & ".", ""))
                messaggio.ShowDialog()
                Return
            End If
            Dim daLeggere As Integer = elenco.Where(Function(n) Not n.Letta).Count()
            Dim lista As New WGMenuLista()
            lista.multiselezioneConsentita = False
            ' Si sente "Centro notifiche", senza "menù" davanti.
            lista.TitoloComeMenu = False
            lista.impostaTitolo("Centro notifiche")
            For Each n As WGNotifica In elenco
                lista.aggiungiElemento(n.Descrizione & If(n.Letta, "", ", da leggere") & If(n.Azioni.Count > 0, ", con azioni", ""), n.Id.ToString())
            Next
            Dim scelta As Func(Of WGNotifica) = Function()
                                                    Dim id As String = lista.ottieniElementi().FirstOrDefault()
                                                    Dim numero As Integer
                                                    If id Is Nothing OrElse Not Integer.TryParse(id, numero) Then Return Nothing
                                                    Return elenco.FirstOrDefault(Function(n) n.Id = numero)
                                                End Function
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim numero As Integer
                                                      If Not Integer.TryParse(id, numero) Then Return
                                                      Dim notifica As WGNotifica = elenco.FirstOrDefault(Function(n) n.Id = numero)
                                                      If notifica Is Nothing Then Return
                                                      ' Una notifica che non si può più aprire (salvata prima di un riavvio, senza azione) si legge
                                                      ' e basta: chiudendo l'elenco si finirebbe nella scrivania senza aver aperto niente.
                                                      If Not PuoAprire(notifica) Then
                                                          notifica.Letta = True
                                                          Salva()
                                                          sintesivocale.GetInstance().LeggiTesto(notifica.Testo & ". Questa notifica non si può più aprire")
                                                          Return
                                                      End If
                                                      lista.abbandona()
                                                      ScegliAzione(notifica)
                                                  End Sub
            lista.aggiungiOpzione("Azioni sulla notifica selezionata", Sub(s, e)
                                                                           Dim notifica As WGNotifica = scelta()
                                                                           If notifica Is Nothing Then Return
                                                                           lista.abbandona()
                                                                           ScegliAzione(notifica, True)
                                                                       End Sub)
            lista.aggiungiOpzione("Non disturbare: " & DescriviNonDisturbare(), Sub(s, e)
                                                                                   lista.abbandona()
                                                                                   ScegliNonDisturbare()
                                                                               End Sub)
            lista.aggiungiOpzione("Suono delle notifiche per applicazione", Sub(s, e)
                                                                                lista.abbandona()
                                                                                ScegliSuoni()
                                                                            End Sub)
            lista.aggiungiOpzione("Segna tutte come lette", Sub(s, e)
                                                                SyncLock _lock
                                                                    For Each n As WGNotifica In _notifiche
                                                                        n.Letta = True
                                                                    Next
                                                                End SyncLock
                                                                Salva()
                                                                lista.abbandona()
                                                                sintesivocale.GetInstance().LeggiTesto("Tutte le notifiche segnate come lette")
                                                                MostraCentro()
                                                            End Sub)
            lista.aggiungiOpzione("Elimina tutte le notifiche", Sub(s, e)
                                                                    WGDomanda.Chiedi("Eliminare tutte le " & elenco.Count & " notifiche? Non si potranno più leggere",
                                                                                     Sub()
                                                                                         SyncLock _lock
                                                                                             _notifiche.Clear()
                                                                                         End SyncLock
                                                                                         WGContenitore.VoceUltimaNotifica = ""
                                                                                         WGContenitore.AzioneUltimaNotifica = Nothing
                                                                                         Salva()
                                                                                         lista.abbandona()
                                                                                         Dim eliminate As New WGMessaggi()
                                                                                         eliminate.Messaggio("Notifiche eliminate. Non ci sono più notifiche.")
                                                                                         eliminate.ShowDialog()
                                                                                     End Sub)
                                                                End Sub)
            ' Ultima azione, come nel multitasking: un colpo verso l'alto dice "Elimina", il doppio tocco elimina.
            lista.aggiungiOpzione("Elimina", Sub(s, e)
                                                 Dim notifica As WGNotifica = scelta()
                                                 If notifica Is Nothing Then Return
                                                 Dim posizione As Integer = Math.Max(0, lista.ottieniIndice() - 1)
                                                 SyncLock _lock
                                                     _notifiche.Remove(notifica)
                                                 End SyncLock
                                                 Salva()
                                                 lista.abbandona()
                                                 sintesivocale.GetInstance().LeggiTesto("Notifica eliminata")
                                                 MostraCentro(posizione)
                                             End Sub)
            AddHandler lista.escPremuto, Sub(s As Object, e As EventArgs) lista.abbandona()
            lista.avvia()
            If indice > 0 AndAlso elenco.Count > 0 Then lista.selezionaElemento(Math.Min(indice, elenco.Count - 1))
        Catch ex As Exception
            log.ScriviLog("WGCentroNotifiche.MostraCentro: " & ex.Message)
        End Try
    End Sub

End Class