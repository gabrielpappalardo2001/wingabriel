' ============================================================================================
' WGCestino - il cestino di Windows: esplorare, ripristinare, eliminare definitivamente, svuotare
' ============================================================================================
'
' Il cestino si legge direttamente dalla cartella $Recycle.Bin dell'utente su ogni disco: per ogni
' elemento eliminato ci sono un file $I, con il percorso originale, la dimensione e la data di
' eliminazione, e un file o una cartella $R con il contenuto. Così non dipende dalla lingua di
' Windows (i comandi della shell come "Ripristina" hanno nomi tradotti).
'   - ripristinare: il contenuto torna al percorso originale (con " (2)" se il nome è occupato);
'   - eliminare definitivamente: si tolgono $R e $I;
'   - svuotare: SHEmptyRecycleBin, come il comando di Windows, senza le sue finestre.
' Sulla scrivania il cestino è un elemento fisso; Cambiato avvisa quando il contenuto cambia e
' Ripristinato dice dove è tornato ogni elemento (la scrivania si riprende i suoi file).
'
'   WGCestino.Mostra()                      ' elenco del cestino, con le opzioni
'   Dim quanti As Integer = WGCestino.Conta()
' ============================================================================================
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports FileVb = Microsoft.VisualBasic.FileIO.FileSystem

Public NotInheritable Class WGCestino

    Public Class ElementoCestino
        ' Il file $I: identifica l'elemento.
        Public FileInfo As String = ""
        ' Il file o la cartella $R con il contenuto.
        Public Contenuto As String = ""
        Public PercorsoOriginale As String = ""
        Public Dimensione As Long
        Public Eliminato As DateTime

        Public ReadOnly Property Nome As String
            Get
                Dim n As String = Path.GetFileName(PercorsoOriginale.TrimEnd("\"c))
                Return If(String.IsNullOrEmpty(n), PercorsoOriginale, n)
            End Get
        End Property

        Public ReadOnly Property ECartella As Boolean
            Get
                Return Directory.Exists(Contenuto)
            End Get
        End Property
    End Class

    Public Const NomeApplicazione As String = "Cestino"

    Private Const SHERB_NOCONFIRMATION As Integer = &H1
    Private Const SHERB_NOPROGRESSUI As Integer = &H2
    Private Const SHERB_NOSOUND As Integer = &H4

    <DllImport("shell32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function SHEmptyRecycleBin(hwnd As IntPtr, pszRootPath As String, dwFlags As Integer) As Integer
    End Function

    Private Shared ReadOnly log As New Logger()
    Private Shared _lista As WGMenuLista

    ''' <summary>Il contenuto del cestino è cambiato (ripristino, eliminazione, svuotamento).</summary>
    Public Shared Event Cambiato()
    ''' <summary>Un elemento è tornato a questo percorso.</summary>
    Public Shared Event Ripristinato(percorso As String)

    Private Sub New()
    End Sub

#Region "Lettura"

    Private Shared Function CartelleCestino() As List(Of String)
        Dim risultato As New List(Of String)()
        Try
            Dim sid As String = WindowsIdentity.GetCurrent().User.Value
            For Each disco As DriveInfo In DriveInfo.GetDrives()
                Try
                    If Not disco.IsReady Then Continue For
                    If disco.DriveType <> DriveType.Fixed AndAlso disco.DriveType <> DriveType.Removable Then Continue For
                    Dim cartella As String = Path.Combine(disco.RootDirectory.FullName, "$Recycle.Bin", sid)
                    If Directory.Exists(cartella) Then risultato.Add(cartella)
                Catch exDisco As Exception
                    log.ScriviLog("WGCestino.CartelleCestino " & disco.Name & ": " & exDisco.Message)
                End Try
            Next
        Catch ex As Exception
            log.ScriviLog("WGCestino.CartelleCestino: " & ex.Message)
        End Try
        Return risultato
    End Function

    ''' <summary>Gli elementi nel cestino, dal più recente.</summary>
    Public Shared Function Elementi() As List(Of ElementoCestino)
        Dim risultato As New List(Of ElementoCestino)()
        For Each cartella As String In CartelleCestino()
            Try
                For Each info As String In Directory.GetFiles(cartella, "$I*")
                    Dim e As ElementoCestino = LeggiInfo(info)
                    If e IsNot Nothing Then risultato.Add(e)
                Next
            Catch ex As Exception
                log.ScriviLog("WGCestino.Elementi " & cartella & ": " & ex.Message)
            End Try
        Next
        Return risultato.OrderByDescending(Function(x) x.Eliminato).ToList()
    End Function

    Public Shared Function Conta() As Integer
        Try
            Return Elementi().Count
        Catch ex As Exception
            log.ScriviLog("WGCestino.Conta: " & ex.Message)
            Return 0
        End Try
    End Function

    ' Formato del file $I: versione (8 byte), dimensione (8), data di eliminazione FILETIME (8), poi il
    ' percorso originale: nella versione 1 sempre 260 caratteri UTF-16, nella 2 (Windows 10 e successivi)
    ' preceduto dalla lunghezza in caratteri (4 byte).
    Private Shared Function LeggiInfo(info As String) As ElementoCestino
        Try
            Dim contenuto As String = Path.Combine(Path.GetDirectoryName(info), "$R" & Path.GetFileName(info).Substring(2))
            If Not File.Exists(contenuto) AndAlso Not Directory.Exists(contenuto) Then Return Nothing
            Dim dati As Byte() = File.ReadAllBytes(info)
            If dati.Length < 24 Then Return Nothing
            Dim versione As Long = BitConverter.ToInt64(dati, 0)
            Dim e As New ElementoCestino()
            e.FileInfo = info
            e.Contenuto = contenuto
            e.Dimensione = BitConverter.ToInt64(dati, 8)
            Try
                e.Eliminato = DateTime.FromFileTime(BitConverter.ToInt64(dati, 16))
            Catch
                e.Eliminato = File.GetCreationTime(info)
            End Try
            Dim percorso As String
            If versione >= 2 AndAlso dati.Length >= 28 Then
                Dim caratteri As Integer = BitConverter.ToInt32(dati, 24)
                Dim byteDisponibili As Integer = Math.Min(caratteri * 2, dati.Length - 28)
                percorso = Encoding.Unicode.GetString(dati, 28, Math.Max(0, byteDisponibili))
            Else
                percorso = Encoding.Unicode.GetString(dati, 24, Math.Min(520, dati.Length - 24))
            End If
            e.PercorsoOriginale = percorso.TrimEnd(ChrW(0))
            If e.PercorsoOriginale = "" Then Return Nothing
            Return e
        Catch ex As Exception
            log.ScriviLog("WGCestino.LeggiInfo " & info & ": " & ex.Message)
            Return Nothing
        End Try
    End Function

#End Region

#Region "Operazioni"

    ''' <summary>Rimette l'elemento dov'era; restituisce il percorso in cui è tornato, o "" se non riesce.</summary>
    Public Shared Function Ripristina(e As ElementoCestino) As String
        Try
            Dim cartella As String = Path.GetDirectoryName(e.PercorsoOriginale)
            If Not String.IsNullOrEmpty(cartella) Then Directory.CreateDirectory(cartella)
            Dim arrivo As String = NomeLibero(e.PercorsoOriginale)
            If Directory.Exists(e.Contenuto) Then
                Directory.Move(e.Contenuto, arrivo)
            Else
                File.Move(e.Contenuto, arrivo)
            End If
            Try
                File.Delete(e.FileInfo)
            Catch exInfo As Exception
                log.ScriviLog("WGCestino.Ripristina $I: " & exInfo.Message)
            End Try
            RaiseEvent Ripristinato(arrivo)
            Return arrivo
        Catch ex As Exception
            log.ScriviLog("WGCestino.Ripristina " & e.PercorsoOriginale & ": " & ex.Message)
            Return ""
        End Try
    End Function

    Public Shared Function EliminaDefinitivamente(e As ElementoCestino) As Boolean
        Try
            If Directory.Exists(e.Contenuto) Then
                For Each f As String In Directory.GetFiles(e.Contenuto, "*", SearchOption.AllDirectories)
                    File.SetAttributes(f, FileAttributes.Normal)
                Next
                FileVb.DeleteDirectory(e.Contenuto, Microsoft.VisualBasic.FileIO.DeleteDirectoryOption.DeleteAllContents)
            ElseIf File.Exists(e.Contenuto) Then
                File.SetAttributes(e.Contenuto, FileAttributes.Normal)
                File.Delete(e.Contenuto)
            End If
            If File.Exists(e.FileInfo) Then File.Delete(e.FileInfo)
            Return True
        Catch ex As Exception
            log.ScriviLog("WGCestino.EliminaDefinitivamente " & e.PercorsoOriginale & ": " & ex.Message)
            Return False
        End Try
    End Function

    Public Shared Function Svuota() As Boolean
        Try
            Dim esito As Integer = SHEmptyRecycleBin(IntPtr.Zero, Nothing, SHERB_NOCONFIRMATION Or SHERB_NOPROGRESSUI Or SHERB_NOSOUND)
            ' Il cestino già vuoto risponde con un errore: conta solo che ora sia vuoto.
            Return esito = 0 OrElse Conta() = 0
        Catch ex As Exception
            log.ScriviLog("WGCestino.Svuota: " & ex.Message)
            Return False
        End Try
    End Function

    Private Shared Function NomeLibero(percorso As String) As String
        If Not File.Exists(percorso) AndAlso Not Directory.Exists(percorso) Then Return percorso
        Dim cartella As String = Path.GetDirectoryName(percorso)
        Dim nome As String = Path.GetFileName(percorso)
        Dim base As String = Path.GetFileNameWithoutExtension(nome)
        Dim estensione As String = Path.GetExtension(nome)
        Dim numero As Integer = 2
        Dim candidato As String
        Do
            candidato = Path.Combine(cartella, base & " (" & numero & ")" & estensione)
            numero += 1
        Loop While File.Exists(candidato) OrElse Directory.Exists(candidato)
        Return candidato
    End Function

    Public Shared Function DescriviDimensione(byteTotali As Long) As String
        If byteTotali < 1024 Then Return byteTotali & " byte"
        If byteTotali < 1024L * 1024 Then Return Math.Round(byteTotali / 1024.0, 1) & " kilobyte"
        If byteTotali < 1024L * 1024 * 1024 Then Return Math.Round(byteTotali / 1024.0 / 1024.0, 1) & " megabyte"
        Return Math.Round(byteTotali / 1024.0 / 1024.0 / 1024.0, 1) & " gigabyte"
    End Function

#End Region

#Region "Elenco"

    Private Shared Function Descrizione(e As ElementoCestino) As String
        Dim cartella As String = Path.GetDirectoryName(e.PercorsoOriginale)
        Return e.Nome & If(e.ECartella, ", cartella", "") & ", da " & If(String.IsNullOrEmpty(cartella), e.PercorsoOriginale, cartella) &
               ", eliminato il " & e.Eliminato.ToString("d MMMM yyyy 'alle' HH:mm")
    End Function

    Private Shared Function Titolo(quanti As Integer) As String
        Return "Cestino"
    End Function

    ''' <summary>Apre l'elenco del cestino. Invio sull'elemento chiede cosa farne; Alt ha tutte le opzioni.</summary>
    Public Shared Async Sub Mostra()
        Try
            Dim elenco As List(Of ElementoCestino) = Await WGProcessoInCorso.EseguiAsync("Lettura del cestino", Function() Task.Run(Function() Elementi()))
            If elenco.Count = 0 Then
                Dim m As New WGMessaggi()
                m.Messaggio("Il cestino è vuoto.")
                m.ShowDialog()
                Return
            End If
            Dim lista As New WGMenuLista()
            _lista = lista
            lista.multiselezioneConsentita = False
            Riempi(lista, elenco, 0)
            AddHandler lista.ElementoSelezionato, Sub(id As String)
                                                      Dim e As ElementoCestino = Trova(id)
                                                      If e IsNot Nothing Then ChiediCosaFare(e)
                                                  End Sub
            lista.aggiungiOpzione("Ripristina", Sub(s, ev) SulSelezionato(AddressOf RipristinaConAnnuncio))
            lista.aggiungiOpzione("Elimina definitivamente", Sub(s, ev) SulSelezionato(AddressOf ChiediEliminaDefinitivamente))
            lista.aggiungiOpzione("Informazioni", Sub(s, ev) SulSelezionato(AddressOf Informazioni))
            lista.aggiungiOpzione("Ripristina tutti gli elementi", Sub(s, ev) ChiediRipristinaTutti())
            lista.aggiungiOpzione("Svuota il cestino", Sub(s, ev) ChiediSvuota())
            lista.aggiungiOpzione("Aggiorna", Sub(s, ev) Aggiorna())
            AddHandler lista.escPremuto, Sub(s As Object, ev As EventArgs)
                                             _lista = Nothing
                                             lista.abbandona()
                                         End Sub
            lista.avvia()
        Catch ex As Exception
            log.ScriviLog("WGCestino.Mostra: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Riempi(lista As WGMenuLista, elenco As List(Of ElementoCestino), indice As Integer)
        lista.impostaTitolo(Titolo(elenco.Count))
        For Each e As ElementoCestino In elenco
            lista.aggiungiElemento(Descrizione(e), e.FileInfo)
        Next
        If indice > 0 Then lista.selezionaElemento(Math.Min(indice, elenco.Count - 1))
    End Sub

    ' Dopo un cambiamento l'elenco si ricarica al suo posto; se il cestino è rimasto vuoto l'elenco si chiude.
    ''' <param name="detto">Cosa è appena successo ("Cestino svuotato"): letto se l'elenco resta, nel messaggio se il cestino è rimasto vuoto.</param>
    ''' <param name="svuotato">Il cestino è stato svuotato apposta: il messaggio dice solo quello.</param>
    Private Shared Async Sub Aggiorna(Optional detto As String = "", Optional svuotato As Boolean = False)
        Try
            RaiseEvent Cambiato()
            Dim lista As WGMenuLista = _lista
            If lista Is Nothing OrElse lista.Interno Is Nothing OrElse lista.Interno.IsDisposed Then
                If detto <> "" Then Messaggio(detto & ".")
                Return
            End If
            ' Se resta qualcosa l'elenco si ricarica e basta la voce; se il cestino è rimasto vuoto l'elenco si
            ' chiude e lo si dice con un messaggio, che il ritorno alla scrivania non può troncare.
            If detto <> "" AndAlso Not svuotato AndAlso Conta() > 0 Then sintesivocale.GetInstance().LeggiTesto(detto)
            Dim elenco As List(Of ElementoCestino) = Await WGProcessoInCorso.EseguiAsync("Aggiornamento del cestino", Function() Task.Run(Function() Elementi()))
            If elenco.Count = 0 Then
                _lista = Nothing
                lista.abbandona()
                Messaggio(If(svuotato, detto & ".", If(detto <> "", detto & ". ", "") & "Il cestino ora è vuoto."))
                Return
            End If
            Dim indice As Integer = Math.Max(0, lista.ottieniIndice())
            lista.svuotaLista()
            Riempi(lista, elenco, indice)
        Catch ex As Exception
            log.ScriviLog("WGCestino.Aggiorna: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Messaggio(testo As String)
        Try
            Dim m As New WGMessaggi()
            m.Messaggio(testo)
            m.ShowDialog()
        Catch ex As Exception
            log.ScriviLog("WGCestino.Messaggio: " & ex.Message)
        End Try
    End Sub

    Private Shared Function Trova(id As String) As ElementoCestino
        If String.IsNullOrEmpty(id) OrElse Not File.Exists(id) Then Return Nothing
        Return LeggiInfo(id)
    End Function

    Private Shared Sub SulSelezionato(azione As Action(Of ElementoCestino))
        Try
            Dim lista As WGMenuLista = _lista
            If lista Is Nothing Then Return
            Dim e As ElementoCestino = Trova(lista.ottieniElemento())
            If e Is Nothing Then
                sintesivocale.GetInstance().LeggiTesto("Nessun elemento scelto")
                Return
            End If
            azione(e)
        Catch ex As Exception
            log.ScriviLog("WGCestino.SulSelezionato: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub ChiediCosaFare(e As ElementoCestino)
        Dim menu As New WGMenu()
        menu.aggiungi("Ripristina in " & Path.GetDirectoryName(e.PercorsoOriginale), Sub(s, ev) RipristinaConAnnuncio(e))
        menu.aggiungi("Elimina definitivamente", Sub(s, ev) ChiediEliminaDefinitivamente(e))
        menu.aggiungi("Informazioni", Sub(s, ev) Informazioni(e))
        menu.avvia(e.Nome, True)
    End Sub

    Private Shared Async Sub RipristinaConAnnuncio(e As ElementoCestino)
        Try
            Dim arrivo As String = Await WGProcessoInCorso.EseguiAsync("Ripristino di " & e.Nome, Function() Task.Run(Function() Ripristina(e)))
            If arrivo = "" Then
                WGDomanda.Chiedi("Non riesco a ripristinare " & e.Nome & ": forse la cartella di origine non è più scrivibile. Vuoi riprovare?", Sub() RipristinaConAnnuncio(e))
                Return
            End If
            Aggiorna(Path.GetFileName(arrivo) & " ripristinato in " & Path.GetDirectoryName(arrivo))
        Catch ex As Exception
            log.ScriviLog("WGCestino.RipristinaConAnnuncio: " & ex.Message)
        End Try
    End Sub

    Private Shared Sub Informazioni(e As ElementoCestino)
        Dim testo As String = e.Nome & vbCrLf &
                              If(e.ECartella, "Cartella", "File") & vbCrLf &
                              "Percorso originale: " & e.PercorsoOriginale & vbCrLf &
                              "Dimensione: " & DescriviDimensione(e.Dimensione) & vbCrLf &
                              "Eliminato il " & e.Eliminato.ToString("d MMMM yyyy 'alle' HH:mm")
        Dim m As New WGMessaggi()
        m.Messaggio(testo)
        m.ShowDialog()
    End Sub

    Private Shared Sub ChiediEliminaDefinitivamente(e As ElementoCestino)
        WGDomanda.Chiedi("Eliminare definitivamente " & e.Nome & "? Non si potrà più recuperare",
                         Async Sub()
                             Dim riuscito As Boolean = Await WGProcessoInCorso.EseguiAsync("Eliminazione di " & e.Nome, Function() Task.Run(Function() EliminaDefinitivamente(e)))
                             If Not riuscito Then
                                 WGDomanda.Chiedi("Non riesco a eliminare " & e.Nome & ". Vuoi riprovare?", Sub() ChiediEliminaDefinitivamente(e))
                                 Return
                             End If
                             Aggiorna(e.Nome & " eliminato definitivamente")
                         End Sub)
    End Sub

    Private Shared Sub ChiediRipristinaTutti()
        Dim quanti As Integer = Conta()
        If quanti = 0 Then
            Messaggio("Il cestino è vuoto.")
            Return
        End If
        WGDomanda.Chiedi("Ripristinare tutti i " & quanti & " elementi del cestino?",
                         Async Sub()
                             Dim riusciti As Integer = Await WGProcessoInCorso.EseguiAsync("Ripristino di tutti gli elementi", Function() Task.Run(Function() Elementi().Where(Function(x) Ripristina(x) <> "").Count()))
                             Aggiorna(riusciti & " elementi ripristinati" & If(riusciti < quanti, ", " & (quanti - riusciti) & " non riusciti", ""))
                         End Sub)
    End Sub

    Private Shared Sub ChiediSvuota()
        Dim quanti As Integer = Conta()
        If quanti = 0 Then
            Messaggio("Il cestino è già vuoto.")
            Return
        End If
        WGDomanda.Chiedi("Svuotare il cestino? " & If(quanti = 1, "L'elemento", "I " & quanti & " elementi") & " non si potranno più recuperare",
                         Async Sub()
                             Dim riuscito As Boolean = Await WGProcessoInCorso.EseguiAsync("Svuotamento del cestino", Function() Task.Run(Function() Svuota()))
                             If Not riuscito Then
                                 WGDomanda.Chiedi("Non riesco a svuotare il cestino. Vuoi riprovare?", Sub() ChiediSvuota())
                                 Return
                             End If
                             Aggiorna("Cestino svuotato", True)
                         End Sub)
    End Sub

#End Region

End Class
